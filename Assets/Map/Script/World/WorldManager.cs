using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(OreGenerator))]
public class WorldManager : NetworkBehaviour
{
    public static WorldManager Instance { get; private set; }

    [Header("자동화 로봇")]
    public Transform[] bots;

    [Header("월드맵 카메라 연동")]
    public Transform worldMapCameraTransform;
    public int mapCameraRenderDistance = 4;
    private bool isMapOpen = false;

    [Header("시작 구역 3x3 평지 청크 설정")]
    public GameObject starterFlatChunkPrefab;
    public bool spawnOresInStarterArea = true;

    [System.Serializable]
    public class ChunkPrefabData
    {
        public GameObject prefab;
        [Range(0f, 100f)] public float probability;
    }

    [System.Serializable]
    public class ChunkGrade
    {
        public string gradeName;
        [Range(0f, 100f)] public float probability;
        public int pityCount = 10;
        public float probabilityIncrease = 0.1f;
        public float maxProbability = 5f;
        public ChunkPrefabData[] chunks;
    }

    [Header("청크 등급 설정")]
    public ChunkGrade[] chunkGrades;

    private Dictionary<int, int> gradeFailureCounts = new Dictionary<int, int>();

    [System.Serializable]
    public class BuildingPrefabData
    {
        public string id;
        public GameObject prefab;
    }

    [Header("건물 프리팹")]
    public BuildingPrefabData[] buildingPrefabs;

    [Header("청크 및 월드 설정")]
    public int chunkSize = 32;
    public int playerRenderDistance = 2;
    public int botRenderDistance = 0;
    public int worldSeed = 12345;

    [Header("고정 맵 설정")]
    [Tooltip("맵의 반경 (예: 50이면 -50~50까지 총 100x100 맵 생성)")]
    public int mapRadius = 50;

    private Dictionary<Vector2Int, Chunk> loadedChunks = new Dictionary<Vector2Int, Chunk>();
    private Dictionary<Vector2Int, ChunkData> chunkData = new Dictionary<Vector2Int, ChunkData>();
    private HashSet<Vector2Int> discoveredChunks = new HashSet<Vector2Int>();

    private Dictionary<Vector2Int, string> precalculatedMap = new Dictionary<Vector2Int, string>();

    private OreGenerator oreGenerator;
    private float chunkUpdateTimer = 0f;
    public bool IsInitialChunksReady { get; private set; } = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        oreGenerator = GetComponent<OreGenerator>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        PrecalculateMapData();

        // 시작 구역 청크를 먼저 생성
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector2Int coord = new Vector2Int(x, z);

                if (!loadedChunks.ContainsKey(coord))
                {
                    CreateChunk(coord);
                }
            }
        }

        // 청크 생성 여부 확인
        IsInitialChunksReady = true;

        Debug.Log("[WorldManager] 시작 구역 3x3 청크 생성 완료");
    }

    void Update()
    {
        if (!IsServer) return; 

        chunkUpdateTimer += Time.deltaTime;
        if (chunkUpdateTimer >= 0.5f)
        {
            chunkUpdateTimer = 0f;
            UpdateChunks();
        }
    }

    public void SetMapOpenState(bool open)
    {
        isMapOpen = open;
        if (IsServer) UpdateChunks();
    }

    private void PrecalculateMapData()
    {
        Debug.Log("[WorldManager] 고정 맵 데이터 생성 시작...");
        
        System.Random globalRandom = new System.Random(worldSeed);
        
        for (int x = -mapRadius; x <= mapRadius; x++)
        {
            for (int z = -mapRadius; z <= mapRadius; z++)
            {
                Vector2Int coord = new Vector2Int(x, z);

                if (IsInStarterArea(coord) && starterFlatChunkPrefab != null)
                {
                    precalculatedMap.Add(coord, starterFlatChunkPrefab.name);
                    continue;
                }

                int gradeIndex = SelectGrade(globalRandom);
                GameObject selectedPrefab = SelectChunkFromGrade(gradeIndex, globalRandom);

                if (selectedPrefab != null)
                {
                    precalculatedMap.Add(coord, selectedPrefab.name);
                }
            }
        }
        
        Debug.Log($"[WorldManager] 총 {precalculatedMap.Count}개의 청크 데이터가 표에 저장되었습니다.");
    }

    void UpdateChunks()
    {
        HashSet<Vector2Int> requiredChunks = new HashSet<Vector2Int>();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject != null)
                {
                    Vector2Int playerChunk = GetChunkCoord(client.PlayerObject.transform.position);
                    AddRequiredChunks(playerChunk, playerRenderDistance, requiredChunks);
                    MarkDiscovered(playerChunk, playerRenderDistance);
                }
            }
        }

        if (bots != null)
        {
            foreach (Transform bot in bots)
            {
                if (bot == null) continue;
                Vector2Int botChunk = GetChunkCoord(bot.position);
                AddRequiredChunks(botChunk, botRenderDistance, requiredChunks);
                MarkDiscovered(botChunk, botRenderDistance);
            }
        }

        if (isMapOpen && worldMapCameraTransform != null)
        {
            Vector2Int mapCamChunk = GetChunkCoord(worldMapCameraTransform.position);
            
            for (int x = -mapCameraRenderDistance; x <= mapCameraRenderDistance; x++)
            {
                for (int z = -mapCameraRenderDistance; z <= mapCameraRenderDistance; z++)
                {
                    Vector2Int targetCoord = new Vector2Int(mapCamChunk.x + x, mapCamChunk.y + z);
                    if (discoveredChunks.Contains(targetCoord))
                    {
                        requiredChunks.Add(targetCoord);
                    }
                }
            }
        }

        if (requiredChunks.Count == 0) return;

        foreach (Vector2Int coord in requiredChunks)
        {
            if (!loadedChunks.ContainsKey(coord))
            {
                CreateChunk(coord);
            }
        }

        RemoveUnnecessaryChunks(requiredChunks);
    }

    void MarkDiscovered(Vector2Int center, int dist)
    {
        for (int x = -dist; x <= dist; x++)
        {
            for (int z = -dist; z <= dist; z++)
            {
                discoveredChunks.Add(new Vector2Int(center.x + x, center.y + z));
            }
        }
    }

    public bool IsInStarterArea(Vector2Int coord)
    {
        return Mathf.Abs(coord.x) <= 1 && Mathf.Abs(coord.y) <= 1;
    }

    public Vector2Int GetChunkCoord(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x / chunkSize);
        int z = Mathf.FloorToInt(position.z / chunkSize);
        return new Vector2Int(x, z);
    }

    void AddRequiredChunks(Vector2Int center, int renderDistance, HashSet<Vector2Int> requiredChunks)
    {
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int z = -renderDistance; z <= renderDistance; z++)
            {
                requiredChunks.Add(new Vector2Int(center.x + x, center.y + z));
            }
        }
    }

    int SelectGrade(System.Random random)
    {
        if (chunkGrades == null || chunkGrades.Length == 0) return -1;
        float totalProbability = 0f;
        float[] adjustedProbabilities = new float[chunkGrades.Length];
        for (int i = 0; i < chunkGrades.Length; i++)
        {
            ChunkGrade grade = chunkGrades[i];
            if (grade == null) continue;
            float probability = grade.probability;
            int failureCount = gradeFailureCounts.ContainsKey(i) ? gradeFailureCounts[i] : 0;
            if (grade.pityCount > 0)
            {
                int increaseCount = failureCount / grade.pityCount;
                probability += increaseCount * grade.probabilityIncrease;
            }
            probability = Mathf.Min(probability, grade.maxProbability);
            adjustedProbabilities[i] = probability;
            totalProbability += probability;
        }

        if (totalProbability <= 0f) return -1;
        double randomValue = random.NextDouble() * totalProbability;
        float currentProbability = 0f;
        for (int i = 0; i < chunkGrades.Length; i++)
        {
            if (chunkGrades[i] == null) continue;
            currentProbability += adjustedProbabilities[i];
            if (randomValue < currentProbability)
            {
                gradeFailureCounts[i] = 0;
                for (int j = 0; j < chunkGrades.Length; j++)
                {
                    if (j == i || chunkGrades[j] == null) continue;
                    if (!gradeFailureCounts.ContainsKey(j)) gradeFailureCounts[j] = 0;
                    gradeFailureCounts[j]++;
                }
                return i;
            }
        }
        return -1;
    }

    GameObject SelectChunkFromGrade(int gradeIndex, System.Random random)
    {
        if (gradeIndex < 0 || gradeIndex >= chunkGrades.Length) return null;
        ChunkGrade grade = chunkGrades[gradeIndex];
        if (grade == null || grade.chunks == null || grade.chunks.Length == 0) return null;
        float totalProbability = 0f;
        foreach (ChunkPrefabData data in grade.chunks)
        {
            if (data == null || data.prefab == null) continue;
            totalProbability += data.probability;
        }
        if (totalProbability <= 0f) return null;
        double randomValue = random.NextDouble() * totalProbability;
        float currentProbability = 0f;
        foreach (ChunkPrefabData data in grade.chunks)
        {
            if (data == null || data.prefab == null) continue;
            currentProbability += data.probability;
            if (randomValue < currentProbability) return data.prefab;
        }
        return null;
    }

    void CreateChunk(Vector2Int coord)
    {
        if (Mathf.Abs(coord.x) > mapRadius || Mathf.Abs(coord.y) > mapRadius)
            return;

        bool hasData = chunkData.ContainsKey(coord);

        if (!hasData)
        {
            if (!precalculatedMap.ContainsKey(coord)) return;
            
            string prefabNameToSpawn = precalculatedMap[coord];
            GameObject selectedPrefab = GetChunkPrefabByID(prefabNameToSpawn);

            if (selectedPrefab == null) return;

            ChunkData newData = new ChunkData(coord);
            chunkData.Add(coord, newData);

            int coordSeed = worldSeed + coord.x * 73856093 + coord.y * 19349663;
            System.Random rotRandom = new System.Random(coordSeed);
            newData.rotationY = rotRandom.Next(0, 4) * 90;

            if (IsInStarterArea(coord)) newData.rotationY = 0;

            newData.chunkPrefabId = selectedPrefab.name;
            Vector3 targetPosition = new Vector3((coord.x + 0.5f) * chunkSize, 0f, (coord.y + 0.5f) * chunkSize);

            GameObject obj = Instantiate(selectedPrefab, targetPosition, Quaternion.Euler(0, newData.rotationY, 0));
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null) 
                netObj.Spawn();
            else 
                Debug.LogWarning($"[경고] {selectedPrefab.name} 프리팹에 NetworkObject 컴포넌트가 없습니다!");

            Chunk chunk = SetupChunk(obj, coord);

            bool canSpawnOres = !IsInStarterArea(coord) || spawnOresInStarterArea;
            if (canSpawnOres && chunk != null && oreGenerator != null)
            {
                System.Random oreRandom = new System.Random(coordSeed);
                oreGenerator.GenerateOres(coord, chunk, oreRandom, SaveObjectToChunk);
            }

            PlayChunkAppearance(obj, targetPosition);
            return;
        }

        ChunkData data = chunkData[coord];
        GameObject savedPrefab = GetChunkPrefabByID(data.chunkPrefabId);
        if (savedPrefab == null) return;

        Vector3 restorePosition = new Vector3((coord.x + 0.5f) * chunkSize, 0f, (coord.y + 0.5f) * chunkSize);
        GameObject savedObj = Instantiate(savedPrefab, restorePosition, Quaternion.Euler(0, data.rotationY, 0));
        
        NetworkObject savedNetObj = savedObj.GetComponent<NetworkObject>();
        if (savedNetObj != null) 
            savedNetObj.Spawn();

        SetupChunk(savedObj, coord);
        PlayChunkAppearance(savedObj, restorePosition);
    }
    
    private void PlayChunkAppearance(GameObject obj, Vector3 targetPosition)
    {
        if (isMapOpen)
        {
            obj.transform.position = targetPosition;
        }
        else
        {
            ChunkAppearance appearance = obj.GetComponent<ChunkAppearance>();
            if (appearance == null) appearance = obj.AddComponent<ChunkAppearance>();
            appearance.PlaySpawnAnimation(targetPosition);
        }
    }

    Chunk SetupChunk(GameObject obj, Vector2Int coord)
    {
        if (obj == null) return null;
        Chunk chunk = obj.GetComponent<Chunk>();
        if (chunk == null)
        {
            Destroy(obj);
            return null;
        }
        chunk.Initialize(coord);
        loadedChunks.Add(coord, chunk);
        RestoreChunkData(coord, chunk);
        return chunk;
    }

    void RestoreChunkData(Vector2Int coord, Chunk chunk)
    {
        if (!chunkData.ContainsKey(coord)) return;
        ChunkData data = chunkData[coord];

        foreach (PlacedObjectData objectData in data.placedObjects)
        {
            GameObject prefab = GetPrefabByID(objectData.prefabId);
            if (prefab == null && oreGenerator != null) prefab = oreGenerator.GetOrePrefabByID(objectData.prefabId);
            if (prefab == null) continue;

            GameObject obj = Instantiate(prefab, objectData.GetPosition(), Quaternion.Euler(objectData.rotX, objectData.rotY, objectData.rotZ));
            
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null) netObj.Spawn();

            if (netObj != null && chunk.GetComponent<NetworkObject>() != null)
                netObj.TrySetParent(chunk.transform);
            else
                obj.transform.SetParent(chunk.transform);
        }
    }

    GameObject GetChunkPrefabByID(string id)
    {
        if (starterFlatChunkPrefab != null && starterFlatChunkPrefab.name == id) return starterFlatChunkPrefab;
        if (string.IsNullOrEmpty(id) || chunkGrades == null) return null;
        foreach (ChunkGrade grade in chunkGrades)
        {
            if (grade == null || grade.chunks == null) continue;
            foreach (ChunkPrefabData data in grade.chunks)
            {
                if (data != null && data.prefab != null && data.prefab.name == id) return data.prefab;
            }
        }
        return null;
    }

    GameObject GetPrefabByID(string id)
    {
        if (buildingPrefabs == null) return null;
        foreach (BuildingPrefabData data in buildingPrefabs)
        {
            if (data != null && data.id == id) return data.prefab;
        }
        return null;
    }

    void RemoveUnnecessaryChunks(HashSet<Vector2Int> requiredChunks)
    {
        List<Vector2Int> removeList = new List<Vector2Int>();
        foreach (var pair in loadedChunks)
        {
            if (!requiredChunks.Contains(pair.Key))
            {
                removeList.Add(pair.Key);
            }
        }

        foreach (Vector2Int coord in removeList)
        {
            GameObject chunkObj = loadedChunks[coord].gameObject;
            
            NetworkObject netObj = chunkObj.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(); 
            }
            else
            {
                Destroy(chunkObj);
            }
            
            loadedChunks.Remove(coord);
        }
    }

    private HashSet<ulong> reservedOres = new HashSet<ulong>();

    public bool ReserveOre(OreNode ore)
    {
        if (!IsServer || ore == null || ore.isDestroyed) return false;
        
        ulong netId = ore.GetComponent<NetworkObject>().NetworkObjectId;
        if (reservedOres.Contains(netId))
        {
            return false;
        }

        reservedOres.Add(netId);
        return true;
    }

    public void ReleaseOreReservation(OreNode ore)
    {
        if (!IsServer || ore == null) return;
        
        ulong netId = ore.GetComponent<NetworkObject>().NetworkObjectId;
        if (reservedOres.Contains(netId))
        {
            reservedOres.Remove(netId);
        }
    }

    public void OnOreDestroyed(OreNode destroyedOre)
    {
        if (!IsServer) return;

        ReleaseOreReservation(destroyedOre);

        Vector2Int coord = destroyedOre.parentChunkCoord;
        Vector3 destroyedPos = destroyedOre.transform.position; // 🌟 파괴된 광물의 정확한 좌표 저장
        
        if (chunkData.ContainsKey(coord))
        {
            ChunkData data = chunkData[coord];
            int removeIndex = data.placedObjects.FindIndex(o => 
                o.prefabId == destroyedOre.oreId && 
                Vector3.Distance(o.GetPosition(), destroyedPos) < 0.1f);

            if (removeIndex >= 0)
            {
                data.placedObjects.RemoveAt(removeIndex);
            }
        }

        // 🌟 [추가됨] 모든 클라이언트의 스캐너에게 해당 좌표의 빨간 구슬 마커를 지우라고 신호(Event) 발생!
        OreScanner.ClearMarkerOnAllClients(destroyedPos);

        // 리젠 코루틴 가동
        StartCoroutine(RespawnOreRoutine(coord, destroyedOre.oreId, destroyedPos, destroyedOre.transform.eulerAngles));
    }

    // 🌟 [신규 추가] 30초 뒤에 광물을 그 자리에 다시 생성하는 코루틴
    private System.Collections.IEnumerator RespawnOreRoutine(Vector2Int coord, string oreId, Vector3 pos, Vector3 rot)
    {
        Debug.Log($"[WorldManager] {oreId} 광물이 파괴되었습니다. 30초 후 리젠됩니다...");
        
        yield return new WaitForSeconds(10f);

        if (!IsChunkLoaded(coord))
        {
            if (!chunkData.ContainsKey(coord)) chunkData.Add(coord, new ChunkData(coord));
            chunkData[coord].placedObjects.Add(new PlacedObjectData(oreId, pos, rot.x, rot.y, rot.z));
            yield break;
        }

        Chunk chunk = GetLoadedChunk(coord);
        if (chunk == null) yield break;

        GameObject prefab = GetPrefabByID(oreId);
        if (prefab == null && oreGenerator != null) prefab = oreGenerator.GetOrePrefabByID(oreId);
        if (prefab == null) yield break;

        GameObject obj = Instantiate(prefab, pos, Quaternion.Euler(rot));
        
        // 🌟 1. 크기 문제 해결: 처음 생성될 때처럼 0.85 ~ 1.2배 사이의 무작위 원래 크기로 뻥튀기!
        float randomScale = UnityEngine.Random.Range(5f, 6f);
        obj.transform.localScale = Vector3.one * randomScale;

        NetworkObject netObj = obj.GetComponent<NetworkObject>();
        if (netObj != null) 
        {
            netObj.Spawn();
            netObj.TrySetParent(chunk.transform);
        }
        else
        {
            obj.transform.SetParent(chunk.transform);
        }

        // 🌟 2. 무한 리젠 해결: 새로 태어난 광물에게 좌표와 ID를 다시 주입 (Initialize)
        OreNode newOreNode = obj.GetComponent<OreNode>();
        if (newOreNode != null)
        {
            newOreNode.Initialize(coord, oreId);
        }

        SaveObjectToChunk(coord, oreId, obj);
        Debug.Log($"[WorldManager] {oreId} 광물 리젠 성공! (무한 리젠 활성화)");
    }

    public void SaveObjectToChunk(Vector2Int coord, string prefabId, GameObject obj)
    {
        if (obj == null) return;
        if (!chunkData.ContainsKey(coord)) chunkData.Add(coord, new ChunkData(coord));
        Vector3 rot = obj.transform.eulerAngles;
        chunkData[coord].placedObjects.Add(new PlacedObjectData(prefabId, obj.transform.position, rot.x, rot.y, rot.z));
    }

    public ChunkData GetChunkData(Vector2Int coord) => chunkData.ContainsKey(coord) ? chunkData[coord] : null;
    public HashSet<Vector2Int> GetDiscoveredChunks() => discoveredChunks;

    public bool IsChunkLoaded(Vector2Int coord) => loadedChunks.ContainsKey(coord);
    public Chunk GetLoadedChunk(Vector2Int coord) => loadedChunks.ContainsKey(coord) ? loadedChunks[coord] : null;
}