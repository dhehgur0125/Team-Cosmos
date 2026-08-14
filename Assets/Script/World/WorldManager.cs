using System.Collections.Generic;
using UnityEngine;

public class WorldManager : MonoBehaviour
{
    // ============================================================
    // 플레이어
    // ============================================================

    [Header("플레이어")]
    public Transform player;


    // ============================================================
    // 자동화 로봇
    // ============================================================

    [Header("자동화 로봇")]
    public Transform[] bots;


    // ============================================================
    // 청크 프리팹 데이터
    // ============================================================

    [System.Serializable]
    public class ChunkPrefabData
    {
        // 청크 프리팹
        public GameObject prefab;

        // 등급 안에서 이 청크가 선택될 확률
        [Range(0f, 100f)]
        public float probability;
    }


    // ============================================================
    // 청크 등급
    // ============================================================

    [System.Serializable]
    public class ChunkGrade
    {
        // 등급 이름
        public string gradeName;

        // ----------------------------------------
        // 기본 등급 확률
        // ----------------------------------------

        [Range(0f, 100f)]
        public float probability;


        // ----------------------------------------
        // 등급 Pity 설정
        // ----------------------------------------

        // 몇 번 선택되지 않으면 확률 증가
        public int pityCount = 10;

        // Pity가 발동할 때마다 증가하는 확률
        public float probabilityIncrease = 0.1f;

        // 등급 확률의 최대값
        public float maxProbability = 5f;


        // ----------------------------------------
        // 등급에 포함된 청크
        // ----------------------------------------

        public ChunkPrefabData[] chunks;
    }


    // ============================================================
    // 청크 등급 목록
    // ============================================================

    [Header("청크 등급 설정")]
    public ChunkGrade[] chunkGrades;


    // ============================================================
    // 등급별 Pity 실패 횟수
    // ============================================================

    // Key = 등급 번호
    // Value = 해당 등급이 선택되지 않은 횟수

    private Dictionary<int, int> gradeFailureCounts =
        new Dictionary<int, int>();


    // ============================================================
    // 청크 설정
    // ============================================================

    [Header("청크 설정")]

    // 청크 한 변의 크기
    public int chunkSize = 32;

    // 플레이어 주변 청크 로딩 거리
    public int playerRenderDistance = 2;

    // 로봇 주변 청크 로딩 거리
    //
    // 0 = 로봇이 현재 있는 청크만
    //
    public int botRenderDistance = 0;


    // ============================================================
    // 월드 설정
    // ============================================================

    [Header("월드 설정")]

    public int worldSeed = 12345;


    // ============================================================
    // 현재 로딩되어 있는 청크
    // ============================================================

    private Dictionary<Vector2Int, Chunk> loadedChunks =
        new Dictionary<Vector2Int, Chunk>();


    // ============================================================
    // 청크 데이터
    // ============================================================

    private Dictionary<Vector2Int, ChunkData> chunkData =
        new Dictionary<Vector2Int, ChunkData>();


    // ============================================================
    // 시작
    // ============================================================

    void Start()
    {
        UpdateChunks();
    }


    // ============================================================
    // 매 프레임 청크 확인
    // ============================================================

    void Update()
    {
        UpdateChunks();
    }


    // ============================================================
    // 플레이어 + 로봇이 필요한 청크 확인
    // ============================================================

    void UpdateChunks()
    {
        HashSet<Vector2Int> requiredChunks =
            new HashSet<Vector2Int>();


        // --------------------------------------------------------
        // 플레이어
        // --------------------------------------------------------

        if (player != null)
        {
            Vector2Int playerChunk =
                GetChunkCoord(player.position);

            AddRequiredChunks(
                playerChunk,
                playerRenderDistance,
                requiredChunks
            );
        }


        // --------------------------------------------------------
        // 로봇
        // --------------------------------------------------------

        if (bots != null)
        {
            foreach (Transform bot in bots)
            {
                if (bot == null)
                    continue;


                Vector2Int botChunk =
                    GetChunkCoord(bot.position);


                AddRequiredChunks(
                    botChunk,
                    botRenderDistance,
                    requiredChunks
                );
            }
        }


        // --------------------------------------------------------
        // 필요한 청크가 없으면 종료
        // --------------------------------------------------------

        if (requiredChunks.Count == 0)
            return;


        // --------------------------------------------------------
        // 필요한 청크 생성
        // --------------------------------------------------------

        foreach (Vector2Int coord in requiredChunks)
        {
            if (!loadedChunks.ContainsKey(coord))
            {
                CreateChunk(coord);
            }
        }


        // --------------------------------------------------------
        // 필요 없는 청크 삭제
        // --------------------------------------------------------

        RemoveUnnecessaryChunks(
            requiredChunks
        );
    }


    // ============================================================
    // 월드 좌표 → 청크 좌표
    // ============================================================

    Vector2Int GetChunkCoord(Vector3 position)
    {
        int x =
            Mathf.FloorToInt(
                position.x / chunkSize
            );


        int z =
            Mathf.FloorToInt(
                position.z / chunkSize
            );


        return new Vector2Int(
            x,
            z
        );
    }


    // ============================================================
    // 필요한 청크 추가
    // ============================================================

    void AddRequiredChunks(
        Vector2Int center,
        int renderDistance,
        HashSet<Vector2Int> requiredChunks)
    {
        for (
            int x = -renderDistance;
            x <= renderDistance;
            x++
        )
        {
            for (
                int z = -renderDistance;
                z <= renderDistance;
                z++
            )
            {
                Vector2Int coord =
                    new Vector2Int(
                        center.x + x,
                        center.y + z
                    );


                requiredChunks.Add(coord);
            }
        }
    }


    // ============================================================
    // 1차 : 등급 선택
    //
    // 등급 확률에만 Pity 적용
    // ============================================================

    int SelectGrade(
        System.Random random)
    {
        if (
            chunkGrades == null ||
            chunkGrades.Length == 0
        )
        {
            Debug.LogError(
                "Chunk Grades가 설정되지 않았습니다."
            );

            return -1;
        }


        float totalProbability = 0f;


        // 각 등급의 최종 확률
        float[] adjustedProbabilities =
            new float[chunkGrades.Length];


        // ========================================================
        // 각 등급의 Pity 적용 확률 계산
        // ========================================================

        for (
            int i = 0;
            i < chunkGrades.Length;
            i++
        )
        {
            ChunkGrade grade =
                chunkGrades[i];


            if (grade == null)
                continue;


            // --------------------------------------------
            // 기본 확률
            // --------------------------------------------

            float probability =
                grade.probability;


            // --------------------------------------------
            // 실패 횟수 가져오기
            // --------------------------------------------

            int failureCount = 0;


            if (
                gradeFailureCounts.ContainsKey(i)
            )
            {
                failureCount =
                    gradeFailureCounts[i];
            }


            // --------------------------------------------
            // Pity 적용
            // --------------------------------------------

            if (
                grade.pityCount > 0
            )
            {
                int increaseCount =
                    failureCount /
                    grade.pityCount;


                probability +=
                    increaseCount *
                    grade.probabilityIncrease;
            }


            // --------------------------------------------
            // 최대 확률 제한
            // --------------------------------------------

            probability =
                Mathf.Min(
                    probability,
                    grade.maxProbability
                );


            adjustedProbabilities[i] =
                probability;


            totalProbability +=
                probability;
        }


        // ========================================================
        // 확률이 전부 0인 경우
        // ========================================================

        if (
            totalProbability <= 0f
        )
        {
            Debug.LogError(
                "등급 확률의 합이 0입니다."
            );

            return -1;
        }


        // ========================================================
        // 랜덤값 생성
        // ========================================================

        double randomValue =
            random.NextDouble() *
            totalProbability;


        float currentProbability = 0f;


        // ========================================================
        // 등급 선택
        // ========================================================

        for (
            int i = 0;
            i < chunkGrades.Length;
            i++
        )
        {
            if (
                chunkGrades[i] == null
            )
            {
                continue;
            }


            currentProbability +=
                adjustedProbabilities[i];


            if (
                randomValue <
                currentProbability
            )
            {
                // --------------------------------------------
                // 선택된 등급 Pity 초기화
                // --------------------------------------------

                gradeFailureCounts[i] = 0;


                // --------------------------------------------
                // 선택되지 않은 등급 Pity 증가
                // --------------------------------------------

                for (
                    int j = 0;
                    j < chunkGrades.Length;
                    j++
                )
                {
                    if (j == i)
                        continue;


                    if (
                        chunkGrades[j] == null
                    )
                    {
                        continue;
                    }


                    if (
                        !gradeFailureCounts.ContainsKey(j)
                    )
                    {
                        gradeFailureCounts[j] = 0;
                    }


                    gradeFailureCounts[j]++;
                }


                return i;
            }
        }


        return -1;
    }


    // ============================================================
    // 2차 : 선택된 등급 안에서 청크 선택
    //
    // 여기에는 Pity가 없음
    // ============================================================

    GameObject SelectChunkFromGrade(
        int gradeIndex,
        System.Random random)
    {
        if (
            gradeIndex < 0 ||
            gradeIndex >= chunkGrades.Length
        )
        {
            return null;
        }


        ChunkGrade grade =
            chunkGrades[gradeIndex];


        if (
            grade == null ||
            grade.chunks == null ||
            grade.chunks.Length == 0
        )
        {
            return null;
        }


        float totalProbability = 0f;


        // ========================================================
        // 등급 내부 청크 확률 계산
        // ========================================================

        foreach (
            ChunkPrefabData data
            in grade.chunks
        )
        {
            if (
                data == null ||
                data.prefab == null
            )
            {
                continue;
            }


            totalProbability +=
                data.probability;
        }


        // ========================================================
        // 청크 확률이 전부 0인 경우
        // ========================================================

        if (
            totalProbability <= 0f
        )
        {
            Debug.LogError(
                "선택된 등급의 청크 확률 합이 0입니다.\n" +
                "등급: " +
                grade.gradeName
            );

            return null;
        }


        // ========================================================
        // 랜덤값
        // ========================================================

        double randomValue =
            random.NextDouble() *
            totalProbability;


        float currentProbability = 0f;


        // ========================================================
        // 청크 선택
        // ========================================================

        foreach (
            ChunkPrefabData data
            in grade.chunks
        )
        {
            if (
                data == null ||
                data.prefab == null
            )
            {
                continue;
            }


            currentProbability +=
                data.probability;


            if (
                randomValue <
                currentProbability
            )
            {
                return data.prefab;
            }
        }


        return null;
    }


    // ============================================================
    // 청크 랜덤 선택
    //
    // 1차 : 등급 선택
    // 2차 : 등급 내부 청크 선택
    // ============================================================

    GameObject GetRandomChunkPrefab(
        System.Random random)
    {
        // --------------------------------------------------------
        // 1차 : 등급 선택
        // --------------------------------------------------------

        int gradeIndex =
            SelectGrade(random);


        if (
            gradeIndex < 0
        )
        {
            return null;
        }


        // --------------------------------------------------------
        // 2차 : 등급 내부 청크 선택
        // --------------------------------------------------------

        return SelectChunkFromGrade(
            gradeIndex,
            random
        );
    }


    // ============================================================
    // 청크 생성
    // ============================================================

    void CreateChunk(Vector2Int coord)
    {
        // ========================================================
        // 기존 청크 데이터가 있는지 확인
        // ========================================================

        bool hasData =
            chunkData.ContainsKey(coord);


        // ========================================================
        // 새로운 청크
        // ========================================================

        if (!hasData)
        {
            ChunkData newData =
                new ChunkData(coord);


            chunkData.Add(
                coord,
                newData
            );


            // ----------------------------------------------------
            // 청크 좌표 기반 Seed
            // ----------------------------------------------------

            int seed =
                worldSeed +
                coord.x * 73856093 +
                coord.y * 19349663;


            System.Random random =
                new System.Random(seed);


            // ----------------------------------------------------
            // 등급 → 청크 선택
            // ----------------------------------------------------

            GameObject selectedPrefab =
                GetRandomChunkPrefab(random);


            if (
                selectedPrefab == null
            )
            {
                Debug.LogError(
                    "생성할 청크 프리팹을 선택하지 못했습니다."
                );


                chunkData.Remove(coord);


                return;
            }


            // ----------------------------------------------------
            // 선택된 청크 ID 저장
            // ----------------------------------------------------

            newData.chunkPrefabId =
                GetChunkPrefabID(
                    selectedPrefab
                );


            // ----------------------------------------------------
            // 0 / 90 / 180 / 270도 중 랜덤 회전
            // ----------------------------------------------------

            int randomRotation =
                random.Next(0, 4) * 90;


            newData.rotationY =
                randomRotation;


            // ----------------------------------------------------
            // 청크 생성
            // ----------------------------------------------------

            GameObject obj =
                Instantiate(
                    selectedPrefab,
                    Vector3.zero,
                    Quaternion.Euler(
                        0,
                        newData.rotationY,
                        0
                    )
                );


            SetupChunk(
                obj,
                coord
            );


            return;
        }


        // ========================================================
        // 기존 청크
        // ========================================================

        ChunkData data =
            chunkData[coord];


        // --------------------------------------------------------
        // 저장된 프리팹 찾기
        // --------------------------------------------------------

        GameObject savedPrefab =
            GetChunkPrefabByID(
                data.chunkPrefabId
            );


        if (
            savedPrefab == null
        )
        {
            Debug.LogError(
                "저장된 청크 프리팹을 찾을 수 없습니다.\n" +
                "Chunk Prefab ID: " +
                data.chunkPrefabId
            );


            return;
        }


        // --------------------------------------------------------
        // 저장된 프리팹 + 저장된 회전으로 생성
        // --------------------------------------------------------

        GameObject savedObj =
            Instantiate(
                savedPrefab,
                Vector3.zero,
                Quaternion.Euler(
                    0,
                    data.rotationY,
                    0
                )
            );


        SetupChunk(
            savedObj,
            coord
        );
    }


    // ============================================================
    // 청크 설정
    // ============================================================

    void SetupChunk(
        GameObject obj,
        Vector2Int coord)
    {
        if (obj == null)
            return;


        // --------------------------------------------------------
        // Chunk 컴포넌트
        // --------------------------------------------------------

        Chunk chunk =
            obj.GetComponent<Chunk>();


        if (chunk == null)
        {
            Debug.LogError(
                "청크 프리팹에 Chunk.cs가 없습니다: " +
                obj.name
            );


            Destroy(obj);


            return;
        }


        // --------------------------------------------------------
        // 청크 초기화
        // --------------------------------------------------------

        chunk.Initialize(
            coord
        );


        // --------------------------------------------------------
        // 로딩된 청크 등록
        // --------------------------------------------------------

        loadedChunks.Add(
            coord,
            chunk
        );


        // --------------------------------------------------------
        // 저장된 오브젝트 복구
        // --------------------------------------------------------

        RestoreChunkData(
            coord,
            chunk
        );
    }


    // ============================================================
    // 청크 프리팹 ID 가져오기
    // ============================================================

    string GetChunkPrefabID(
        GameObject prefab)
    {
        if (prefab == null)
            return null;


        return prefab.name;
    }


    // ============================================================
    // 저장된 ID로 청크 프리팹 찾기
    // ============================================================

    GameObject GetChunkPrefabByID(
        string id)
    {
        if (
            string.IsNullOrEmpty(id)
        )
        {
            return null;
        }


        // --------------------------------------------------------
        // 모든 등급 검색
        // --------------------------------------------------------

        foreach (
            ChunkGrade grade
            in chunkGrades
        )
        {
            if (
                grade == null ||
                grade.chunks == null
            )
            {
                continue;
            }


            // ----------------------------------------------------
            // 등급 안의 모든 청크 검색
            // ----------------------------------------------------

            foreach (
                ChunkPrefabData data
                in grade.chunks
            )
            {
                if (
                    data == null ||
                    data.prefab == null
                )
                {
                    continue;
                }


                if (
                    GetChunkPrefabID(data.prefab)
                    == id
                )
                {
                    return data.prefab;
                }
            }
        }


        return null;
    }


    // ============================================================
    // 저장된 청크 데이터 복구
    // ============================================================

    void RestoreChunkData(
        Vector2Int coord,
        Chunk chunk)
    {
        if (
            !chunkData.ContainsKey(coord)
        )
        {
            return;
        }


        ChunkData data =
            chunkData[coord];


        // --------------------------------------------------------
        // 저장된 건물/오브젝트 복구
        // --------------------------------------------------------

        foreach (
            PlacedObjectData objectData
            in data.placedObjects
        )
        {
            GameObject prefab =
                GetPrefabByID(
                    objectData.prefabId
                );


            if (prefab == null)
            {
                Debug.LogWarning(
                    "프리팹을 찾을 수 없습니다: " +
                    objectData.prefabId
                );


                continue;
            }


            GameObject obj =
                Instantiate(
                    prefab,
                    objectData.GetPosition(),
                    Quaternion.Euler(
                        0,
                        objectData.rotY,
                        0
                    )
                );


            obj.transform.SetParent(
                chunk.transform
            );
        }
    }


    // ============================================================
    // 건물 프리팹 데이터
    // ============================================================

    [System.Serializable]
    public class BuildingPrefabData
    {
        public string id;

        public GameObject prefab;
    }


    public BuildingPrefabData[] buildingPrefabs;


    // ============================================================
    // 건물 프리팹 ID로 찾기
    // ============================================================

    GameObject GetPrefabByID(
        string id)
    {
        if (
            buildingPrefabs == null
        )
        {
            return null;
        }


        foreach (
            BuildingPrefabData data
            in buildingPrefabs
        )
        {
            if (
                data == null
            )
            {
                continue;
            }


            if (
                data.id == id
            )
            {
                return data.prefab;
            }
        }


        return null;
    }


    // ============================================================
    // 필요하지 않은 청크 삭제
    // ============================================================

    void RemoveUnnecessaryChunks(
        HashSet<Vector2Int> requiredChunks)
    {
        List<Vector2Int> removeList =
            new List<Vector2Int>();


        foreach (
            var pair
            in loadedChunks
        )
        {
            Vector2Int coord =
                pair.Key;


            // 플레이어와 로봇 모두에게
            // 필요하지 않은 청크

            if (
                !requiredChunks.Contains(coord)
            )
            {
                removeList.Add(
                    coord
                );
            }
        }


        // --------------------------------------------------------
        // 실제 삭제
        // --------------------------------------------------------

        foreach (
            Vector2Int coord
            in removeList
        )
        {
            // GameObject만 삭제
            //
            // ChunkData는 삭제하지 않는다.
            //
            // 따라서 나중에 다시 접근하면
            // 기존 청크가 복구된다.

            Destroy(
                loadedChunks[coord].gameObject
            );


            loadedChunks.Remove(
                coord
            );
        }
    }


    // ============================================================
    // 건물을 청크 데이터에 저장
    // ============================================================

    public void SaveObjectToChunk(
        Vector2Int coord,
        string prefabId,
        GameObject obj)
    {
        if (obj == null)
            return;


        // --------------------------------------------------------
        // 청크 데이터가 없다면 생성
        // --------------------------------------------------------

        if (
            !chunkData.ContainsKey(coord)
        )
        {
            chunkData.Add(
                coord,
                new ChunkData(coord)
            );
        }


        ChunkData data =
            chunkData[coord];


        // --------------------------------------------------------
        // 건물 데이터 생성
        // --------------------------------------------------------

        PlacedObjectData objectData =
            new PlacedObjectData(
                prefabId,
                obj.transform.position,
                obj.transform.eulerAngles.y
            );


        // --------------------------------------------------------
        // 저장
        // --------------------------------------------------------

        data.placedObjects.Add(
            objectData
        );
    }


    // ============================================================
    // 청크 데이터 가져오기
    // ============================================================

    public ChunkData GetChunkData(
        Vector2Int coord)
    {
        if (
            chunkData.ContainsKey(coord)
        )
        {
            return chunkData[coord];
        }


        return null;
    }
}