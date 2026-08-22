using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(OreGenerator))]
public class WorldManager : MonoBehaviour
{
    // ============================================================
    // 플레이어 & 로봇
    // ============================================================

    [Header("플레이어")]
    public Transform player;

    [Header("자동화 로봇")]
    public Transform[] bots;


    // ============================================================
    // 청크 등급 및 프리팹 (ChunkSelector 통합)
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
        UpdateChunks();
    }

    void Update()
    {
        UpdateChunks();
    }


    // ============================================================
    // 청크 로드/언로드 관리
    // ============================================================

    void UpdateChunks()
    {
        HashSet<Vector2Int> requiredChunks = new HashSet<Vector2Int>();

        if (player != null)
        {
            Vector2Int playerChunk = GetChunkCoord(player.position);
            AddRequiredChunks(playerChunk, playerRenderDistance, requiredChunks);
        }

        if (bots != null)
        {
            foreach (Transform bot in bots)
            {
                if (bot == null) continue;
                Vector2Int botChunk = GetChunkCoord(bot.position);
                AddRequiredChunks(botChunk, botRenderDistance, requiredChunks);
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

    Vector2Int GetChunkCoord(Vector3 position)
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
    // 청크 선택 로직 (Pity 및 등급 가중치)
    // ============================================================

    int SelectGrade(System.Random random)
    {
        if (chunkGrades == null || chunkGrades.Length == 0)
        {
            Debug.LogError("Chunk Grades가 설정되지 않았습니다.");
            return -1;
        }

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

        if (totalProbability <= 0f)
        {
            Debug.LogError("등급 확률의 합이 0입니다.");
            return -1;
        }

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

        if (totalProbability <= 0f)
        {
            Debug.LogError("선택된 등급의 청크 확률 합이 0입니다.\n등급: " + grade.gradeName);
            return null;
        }

        double randomValue = random.NextDouble() * totalProbability;
        float currentProbability = 0f;

        foreach (ChunkPrefabData data in grade.chunks)
        {
            if (data == null || data.prefab == null) continue;

            currentProbability += data.probability;
            if (randomValue < currentProbability)
            {
                return data.prefab;
            }
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

        // ========================================================
        // 1. 신규 청크 생성
        // ========================================================
        if (!hasData)
        {
            ChunkData newData = new ChunkData(coord);
            chunkData.Add(coord, newData);

            int seed = worldSeed + coord.x * 73856093 + coord.y * 19349663;
            System.Random random = new System.Random(seed);

            GameObject selectedPrefab = GetRandomChunkPrefab(random);
            if (selectedPrefab == null)
            {
                Debug.LogError("생성할 청크 프리팹을 선택하지 못했습니다.");
                chunkData.Remove(coord);
                return;
            }

            newData.chunkPrefabId = selectedPrefab.name;
            newData.rotationY = random.Next(0, 4) * 90;

            // 최종 배치되어야 할 실제 월드 좌표
            Vector3 targetPosition = new Vector3(
                (coord.x + 0.5f) * chunkSize,
                0f,
                (coord.y + 0.5f) * chunkSize
            );

            // 인스턴스화
            GameObject obj = Instantiate(
                selectedPrefab,
                targetPosition,
                Quaternion.Euler(0, newData.rotationY, 0)
            );

            Chunk chunk = SetupChunk(obj, coord);

            // 신규 청크일 때 광물 스폰
            if (chunk != null && oreGenerator != null)
            {
                oreGenerator.GenerateOres(coord, chunk, random, SaveObjectToChunk);
            }

            // ----------------------------------------------------
            // 🌟 [추가] 청크 등장 애니메이션 실행
            // ----------------------------------------------------
            ChunkAppearance appearance = obj.GetComponent<ChunkAppearance>();
            if (appearance == null)
            {
                appearance = obj.AddComponent<ChunkAppearance>();
            }
            appearance.PlaySpawnAnimation(targetPosition);

            return;
        }

        // ========================================================
        // 2. 기존 청크 복구 (재방문)
        // ========================================================
        ChunkData data = chunkData[coord];
        GameObject savedPrefab = GetChunkPrefabByID(data.chunkPrefabId);

        if (savedPrefab == null)
        {
            Debug.LogError("저장된 청크 프리팹을 찾을 수 없습니다: " + data.chunkPrefabId);
            return;
        }

        Vector3 restorePosition = new Vector3(
            (coord.x + 0.5f) * chunkSize,
            0f,
            (coord.y + 0.5f) * chunkSize
        );

        GameObject savedObj = Instantiate(
            savedPrefab,
            restorePosition,
            Quaternion.Euler(0, data.rotationY, 0)
        );

        SetupChunk(savedObj, coord);

        // 재방문 청크도 솟아오르는 연출을 원하시면 아래 주석을 해제하세요.
       
        ChunkAppearance restoreAppearance = savedObj.GetComponent<ChunkAppearance>();
        if (restoreAppearance == null) restoreAppearance = savedObj.AddComponent<ChunkAppearance>();
        restoreAppearance.PlaySpawnAnimation(restorePosition);
    
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
            Debug.LogError("청크 프리팹에 Chunk.cs가 없습니다: " + obj.name);
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
            // 건물 프리팹 검색 -> 없으면 OreGenerator에서 광물 프리팹 검색
            GameObject prefab = GetPrefabByID(objectData.prefabId);
            if (prefab == null && oreGenerator != null)
            {
                prefab = oreGenerator.GetOrePrefabByID(objectData.prefabId);
            }

            if (prefab == null)
            {
                Debug.LogWarning("프리팹을 찾을 수 없습니다: " + objectData.prefabId);
                continue;
            }

            GameObject obj = Instantiate(
                prefab,
                objectData.GetPosition(),
                Quaternion.Euler(objectData.rotX, objectData.rotY, objectData.rotZ)
            );

            obj.transform.SetParent(chunk.transform);
        }
    }


    // ============================================================
    // 프리팹 탐색
    // ============================================================

    GameObject GetChunkPrefabByID(string id)
    {
        if (string.IsNullOrEmpty(id) || chunkGrades == null) return null;

        foreach (ChunkGrade grade in chunkGrades)
        {
            if (grade == null || grade.chunks == null) continue;

            foreach (ChunkPrefabData data in grade.chunks)
            {
                if (data != null && data.prefab != null && data.prefab.name == id)
                {
                    return data.prefab;
                }
            }
        }

        return null;
    }

    GameObject GetPrefabByID(string id)
    {
        if (buildingPrefabs == null) return null;

        foreach (BuildingPrefabData data in buildingPrefabs)
        {
            if (data != null && data.id == id)
            {
                return data.prefab;
            }
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
            Destroy(loadedChunks[coord].gameObject);
            loadedChunks.Remove(coord);
        }
    }

    public void SaveObjectToChunk(Vector2Int coord, string prefabId, GameObject obj)
    {
        if (obj == null) return;

        if (!chunkData.ContainsKey(coord))
        {
            chunkData.Add(coord, new ChunkData(coord));
        }

        Vector3 rot = obj.transform.eulerAngles;
        chunkData[coord].placedObjects.Add(
            new PlacedObjectData(prefabId, obj.transform.position, rot.x, rot.y, rot.z)
        );
    }

    public ChunkData GetChunkData(Vector2Int coord)
    {
        return chunkData.ContainsKey(coord) ? chunkData[coord] : null;
    }
}