using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode; // 🌟 [멀티플레이 변경] Netcode 네임스페이스 추가

[RequireComponent(typeof(OreGenerator))]
public class WorldManager : NetworkBehaviour // 🌟 [멀티플레이 변경] MonoBehaviour -> NetworkBehaviour로 변경
{
    // ============================================================
    // 플레이어 & 로봇
    // ============================================================

    // 🌟 [멀티플레이 변경] 단일 플레이어(Transform player) 변수 삭제
    // 멀티플레이에서는 NetworkManager를 통해 접속한 모든 유저를 동적으로 추적합니다.

    [Header("자동화 로봇")]
    public Transform[] bots;


    // ============================================================
    // 월드맵 카메라 연동
    // ============================================================

    [Header("월드맵 카메라 연동")]
    public Transform worldMapCameraTransform;
    public int mapCameraRenderDistance = 4;
    private bool isMapOpen = false;


    // ============================================================
    // 시작 구역 3x3 평지 청크 설정
    // ============================================================

    [Header("시작 구역 3x3 평지 청크 설정")]
    public GameObject starterFlatChunkPrefab;
    public bool spawnOresInStarterArea = true;


    // ============================================================
    // 청크 등급 및 프리팹
    // ============================================================

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


    // ============================================================
    // 건물 프리팹 데이터
    // ============================================================

    [System.Serializable]
    public class BuildingPrefabData
    {
        public string id;
        public GameObject prefab;
    }

    [Header("건물 프리팹")]
    public BuildingPrefabData[] buildingPrefabs;


    // ============================================================
    // 청크 및 월드 설정
    // ============================================================

    [Header("청크 설정")]
    public int chunkSize = 32;
    public int playerRenderDistance = 2;
    public int botRenderDistance = 0;

    [Header("월드 설정")]
    public int worldSeed = 12345;


    // ============================================================
    // 내부 데이터 & 컴포넌트 참조
    // ============================================================

    private Dictionary<Vector2Int, Chunk> loadedChunks = new Dictionary<Vector2Int, Chunk>();
    private Dictionary<Vector2Int, ChunkData> chunkData = new Dictionary<Vector2Int, ChunkData>();
    private HashSet<Vector2Int> discoveredChunks = new HashSet<Vector2Int>();

    private OreGenerator oreGenerator;


    // ============================================================
    // 유니티 생명주기
    // ============================================================

    void Awake()
    {
        oreGenerator = GetComponent<OreGenerator>();
    }

    void Start()
    {
        // 🌟 [멀티플레이 변경] Start에서는 아무것도 하지 않습니다. 
        // 네트워크 연결(OnNetworkSpawn) 이후에 업데이트가 시작되어야 합니다.
    }

    void Update()
    {
        // 🌟 [멀티플레이 변경] 호스트(서버)가 아니면 청크 계산 로직을 돌리지 않고 종료!
        // 오직 방장 컴퓨터에서만 맵을 생성하고 관리합니다.
        if (!IsServer) return; 

        UpdateChunks();
    }


    // ============================================================
    // 월드맵 모드 제어
    // ============================================================

    public void SetMapOpenState(bool open)
    {
        isMapOpen = open;
        if (IsServer) UpdateChunks(); // 서버일 때만 갱신
    }


    // ============================================================
    // 청크 로드/언로드 관리 (플레이어 다수 + 봇 + 월드맵 카메라)
    // ============================================================

    void UpdateChunks()
    {
        HashSet<Vector2Int> requiredChunks = new HashSet<Vector2Int>();

        // 🌟 [멀티플레이 변경] 1. 접속한 '모든' 플레이어의 주변 청크를 계산
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

        // 2. 자동화 로봇 주변 청크
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

        // 3. 월드맵 카메라 구역 (서버 기준 글로벌 탐험 기록 공유)
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

        // 필요한 청크 생성
        foreach (Vector2Int coord in requiredChunks)
        {
            if (!loadedChunks.ContainsKey(coord))
            {
                CreateChunk(coord);
            }
        }

        // 시야 밖으로 벗어난 청크 언로드
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


    // ============================================================
    // 청크 선택 로직
    // ============================================================

    int SelectGrade(System.Random random)
    {
        // (기존 확률 및 천장 로직 동일 유지)
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

    GameObject GetRandomChunkPrefab(System.Random random)
    {
        int gradeIndex = SelectGrade(random);
        if (gradeIndex < 0) return null;
        return SelectChunkFromGrade(gradeIndex, random);
    }


    // ============================================================
    // 청크 생성
    // ============================================================

    void CreateChunk(Vector2Int coord)
    {
        bool hasData = chunkData.ContainsKey(coord);

        // ------------------------------------------------------------
        // 1. 신규 청크 생성
        // ------------------------------------------------------------
        if (!hasData)
        {
            ChunkData newData = new ChunkData(coord);
            chunkData.Add(coord, newData);

            int seed = worldSeed + coord.x * 73856093 + coord.y * 19349663;
            System.Random random = new System.Random(seed);

            GameObject selectedPrefab = null;

            if (IsInStarterArea(coord) && starterFlatChunkPrefab != null)
            {
                selectedPrefab = starterFlatChunkPrefab;
                newData.rotationY = 0;
            }
            else
            {
                selectedPrefab = GetRandomChunkPrefab(random);
                newData.rotationY = random.Next(0, 4) * 90;
            }

            if (selectedPrefab == null)
            {
                chunkData.Remove(coord);
                return;
            }

            newData.chunkPrefabId = selectedPrefab.name;
            Vector3 targetPosition = new Vector3((coord.x + 0.5f) * chunkSize, 0f, (coord.y + 0.5f) * chunkSize);

            // 서버에서 오브젝트 생성
            GameObject obj = Instantiate(selectedPrefab, targetPosition, Quaternion.Euler(0, newData.rotationY, 0));
            
            // 🌟 [멀티플레이 변경] 생성된 청크를 모든 클라이언트에게 전송(동기화)
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null) 
                netObj.Spawn();
            else 
                Debug.LogWarning($"[경고] {selectedPrefab.name} 프리팹에 NetworkObject 컴포넌트가 없습니다!");

            Chunk chunk = SetupChunk(obj, coord);

            bool canSpawnOres = !IsInStarterArea(coord) || spawnOresInStarterArea;
            if (canSpawnOres && chunk != null && oreGenerator != null)
            {
                oreGenerator.GenerateOres(coord, chunk, random, SaveObjectToChunk);
            }

            PlayChunkAppearance(obj, targetPosition);
            return;
        }

        // ------------------------------------------------------------
        // 2. 기존 탐험 구역 청크 복구
        // ------------------------------------------------------------
        ChunkData data = chunkData[coord];
        GameObject savedPrefab = GetChunkPrefabByID(data.chunkPrefabId);
        if (savedPrefab == null) return;

        Vector3 restorePosition = new Vector3((coord.x + 0.5f) * chunkSize, 0f, (coord.y + 0.5f) * chunkSize);
        GameObject savedObj = Instantiate(savedPrefab, restorePosition, Quaternion.Euler(0, data.rotationY, 0));
        
        // 🌟 [멀티플레이 변경] 복구된 청크도 네트워크에 스폰
        NetworkObject savedNetObj = savedObj.GetComponent<NetworkObject>();
        if (savedNetObj != null) 
            savedNetObj.Spawn();

        SetupChunk(savedObj, coord);
        PlayChunkAppearance(savedObj, restorePosition);
    }
    
    // 연출 분리용 헬퍼 함수
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


    // ============================================================
    // 청크 셋업 & 데이터 복원
    // ============================================================

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
            
            // 🌟 [멀티플레이 변경] 복구된 건물/광물 오브젝트 네트워크 스폰
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null) netObj.Spawn();

            // 주의: 네트워크 객체는 부모(Parent)를 지정할 때 특별한 제약이 있을 수 있습니다.
            // NetworkObject의 부모 설정은 NetworkObject.TrySetParent()를 사용하는 것이 좋습니다.
            if (netObj != null && chunk.GetComponent<NetworkObject>() != null)
                netObj.TrySetParent(chunk.transform);
            else
                obj.transform.SetParent(chunk.transform);
        }
    }


    // ============================================================
    // 프리팹 탐색
    // ============================================================

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


    // ============================================================
    // 청크 언로드 및 데이터 저장
    // ============================================================

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
            
            // 🌟 [멀티플레이 변경] 단순 Destroy 대신 NetworkObject.Despawn 호출 (서버가 클라이언트들에게 파괴 명령)
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

    public void SaveObjectToChunk(Vector2Int coord, string prefabId, GameObject obj)
    {
        if (obj == null) return;
        if (!chunkData.ContainsKey(coord)) chunkData.Add(coord, new ChunkData(coord));
        Vector3 rot = obj.transform.eulerAngles;
        chunkData[coord].placedObjects.Add(new PlacedObjectData(prefabId, obj.transform.position, rot.x, rot.y, rot.z));
    }

    public ChunkData GetChunkData(Vector2Int coord) => chunkData.ContainsKey(coord) ? chunkData[coord] : null;
    public HashSet<Vector2Int> GetDiscoveredChunks() => discoveredChunks;
}