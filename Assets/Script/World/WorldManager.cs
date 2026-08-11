using System.Collections.Generic;
using UnityEngine;

public class WorldManager : MonoBehaviour
{
    [Header("플레이어")]
    public Transform player;

    [Header("자동화 로봇")]
    public Transform[] bots;

    [Header("청크 프리팹")]
    public GameObject[] chunkPrefabs;

    [Header("청크 설정")]
    public int chunkSize = 32;

    // 플레이어가 주변 몇 칸의 청크를 로딩할지
    public int playerRenderDistance = 2;

    // 로봇이 주변 몇 칸의 청크를 로딩할지
    // 0 = 로봇이 있는 청크만
    public int botRenderDistance = 0;

    [Header("월드 설정")]
    public int worldSeed = 12345;

    // 현재 실제로 생성되어 있는 청크
    private Dictionary<Vector2Int, Chunk> loadedChunks =
        new Dictionary<Vector2Int, Chunk>();

    // 청크의 저장 데이터
    private Dictionary<Vector2Int, ChunkData> chunkData =
        new Dictionary<Vector2Int, ChunkData>();


    void Start()
    {
        UpdateChunks();
    }


    void Update()
    {
        UpdateChunks();
    }


    // ============================================================
    // 모든 플레이어와 로봇이 필요한 청크를 확인
    // ============================================================

    void UpdateChunks()
    {
        HashSet<Vector2Int> requiredChunks =
            new HashSet<Vector2Int>();


        // --------------------------------------------------------
        // 1. 플레이어가 필요한 청크
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
        // 2. 로봇들이 필요한 청크
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
        // 3. 필요한 청크가 없다면 종료
        // --------------------------------------------------------

        if (requiredChunks.Count == 0)
            return;


        // --------------------------------------------------------
        // 4. 필요한 청크 생성
        // --------------------------------------------------------

        foreach (Vector2Int coord in requiredChunks)
        {
            if (!loadedChunks.ContainsKey(coord))
            {
                CreateChunk(coord);
            }
        }


        // --------------------------------------------------------
        // 5. 필요하지 않은 청크 삭제
        // --------------------------------------------------------

        RemoveUnnecessaryChunks(requiredChunks);
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


        return new Vector2Int(x, z);
    }


    // ============================================================
    // 특정 중심 청크 주변의 필요한 청크들을 추가
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
    // 청크 생성
    // ============================================================

    void CreateChunk(Vector2Int coord)
    {
        // --------------------------------------------------------
        // 1. ChunkData가 없으면 새로 생성
        // --------------------------------------------------------

        if (!chunkData.ContainsKey(coord))
        {
            chunkData.Add(
                coord,
                new ChunkData(coord)
            );
        }


        // --------------------------------------------------------
        // 2. Seed + 청크 좌표로 청크 종류 결정
        // --------------------------------------------------------

        int seed =
            worldSeed +
            coord.x * 73856093 +
            coord.y * 19349663;


        System.Random random =
            new System.Random(seed);


        int randomIndex =
            random.Next(
                0,
                chunkPrefabs.Length
            );


        GameObject selectedPrefab =
            chunkPrefabs[randomIndex];


        // --------------------------------------------------------
        // 3. 청크 생성
        // --------------------------------------------------------

        GameObject obj =
            Instantiate(selectedPrefab);


        // --------------------------------------------------------
        // 4. Chunk 컴포넌트 가져오기
        // --------------------------------------------------------

        Chunk chunk =
            obj.GetComponent<Chunk>();


        if (chunk == null)
        {
            Debug.LogError(
                "Chunk 프리팹에 Chunk.cs가 없습니다: "
                + selectedPrefab.name
            );

            Destroy(obj);

            return;
        }


        // --------------------------------------------------------
        // 5. 청크 초기화
        // --------------------------------------------------------

        chunk.Initialize(coord);


        // --------------------------------------------------------
        // 6. 현재 로딩된 청크에 등록
        // --------------------------------------------------------

        loadedChunks.Add(
            coord,
            chunk
        );


        // --------------------------------------------------------
        // 7. 저장된 데이터 복구
        // --------------------------------------------------------

        RestoreChunkData(
            coord,
            chunk
        );
    }


    // ============================================================
    // 저장된 청크 데이터 복구
    // ============================================================

    void RestoreChunkData(
        Vector2Int coord,
        Chunk chunk)
    {
        if (!chunkData.ContainsKey(coord))
            return;


        ChunkData data =
            chunkData[coord];


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
                    "프리팹을 찾을 수 없습니다: "
                    + objectData.prefabId
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
    // 프리팹 ID로 프리팹 찾기
    // ============================================================

    GameObject GetPrefabByID(string id)
    {
        foreach (
            BuildingPrefabData data
            in buildingPrefabs
        )
        {
            if (data.id == id)
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


            // 플레이어 또는 로봇 중
            // 아무도 필요로 하지 않는 청크
            if (!requiredChunks.Contains(coord))
            {
                removeList.Add(coord);
            }
        }


        foreach (
            Vector2Int coord
            in removeList
        )
        {
            // GameObject만 삭제
            //
            // ChunkData는 삭제하지 않는다.
            Destroy(
                loadedChunks[coord].gameObject
            );


            loadedChunks.Remove(coord);
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
        if (!chunkData.ContainsKey(coord))
        {
            chunkData.Add(
                coord,
                new ChunkData(coord)
            );
        }


        ChunkData data =
            chunkData[coord];


        PlacedObjectData objectData =
            new PlacedObjectData(
                prefabId,
                obj.transform.position,
                obj.transform.eulerAngles.y
            );


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
        if (chunkData.ContainsKey(coord))
        {
            return chunkData[coord];
        }


        return null;
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
}
