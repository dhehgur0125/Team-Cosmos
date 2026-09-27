using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode; // 🌟 [멀티플레이 변경] Netcode 네임스페이스 추가

// 🌟 [멀티플레이 변경] MonoBehaviour -> NetworkBehaviour로 변경
public class OreGenerator : NetworkBehaviour 
{
    // ============================================================
    // 광물 데이터 정의
    // ============================================================

    [Serializable]
    public class OreData
    {
        public string oreName = "Stone";
        public GameObject prefab;

        [Header("등장 거리 조건 (청크 거리 단위)")]
        public float minChunkDistance = 0f;
        public float maxChunkDistance = 999f;

        [Header("가중치 (높을수록 잘 나옴)")]
        [Range(1f, 100f)]
        public float spawnWeight = 50f;

        [Header("광맥(Vein) 군집 크기")]
        [Range(1, 5)] public int veinSizeMin = 1;
        [Range(1, 10)] public int veinSizeMax = 3;
    }

    [Header("광물 목록 및 티어 설정")]
    public OreData[] oreList;

    [Header("청크당 스폰 설정")]
    public int minOresPerChunk = 3;
    public int maxOresPerChunk = 6;

    [Header("스케일 랜덤 편차")]
    public float minScale = 0.85f;
    public float maxScale = 1.2f;


    // ============================================================
    // 메인 광물 생성 함수 (WorldManager의 서버 로직에서만 호출됨)
    // ============================================================

    public void GenerateOres(Vector2Int coord, Chunk chunk, System.Random random, Action<Vector2Int, string, GameObject> onSaveOre)
    {
        if (oreList == null || oreList.Length == 0) return;
        if (chunk.oreSpawnPoints == null || chunk.oreSpawnPoints.Length == 0) return;

        // 원점(0,0)으로부터의 청크 거리 계산
        float chunkDistance = Mathf.Sqrt(coord.x * coord.x + coord.y * coord.y);

        // 1. 현재 거리(티어)에 맞는 광물 후보 필터링
        List<OreData> eligibleOres = GetEligibleOres(chunkDistance);
        if (eligibleOres.Count == 0) return;

        // 2. 사용 가능한 스폰 포인트 리스트 준비
        List<Transform> availablePoints = new List<Transform>(chunk.oreSpawnPoints);

        int targetSpawnCount = random.Next(minOresPerChunk, Mathf.Min(maxOresPerChunk, availablePoints.Count) + 1);
        int currentSpawned = 0;

        // 3. 광맥(Cluster) 단위로 순회 스폰
        while (currentSpawned < targetSpawnCount && availablePoints.Count > 0)
        {
            OreData chosenOre = PickWeightedOre(eligibleOres, random);
            if (chosenOre == null || chosenOre.prefab == null) break;

            int veinSize = random.Next(chosenOre.veinSizeMin, chosenOre.veinSizeMax + 1);

            int startIdx = random.Next(0, availablePoints.Count);
            Transform currentPoint = availablePoints[startIdx];
            availablePoints.RemoveAt(startIdx);

            SpawnSingleOre(coord, chunk, chosenOre, currentPoint, random, onSaveOre);
            currentSpawned++;

            for (int v = 1; v < veinSize; v++)
            {
                if (availablePoints.Count == 0 || currentSpawned >= targetSpawnCount) break;

                int nearestIdx = FindNearestPointIndex(currentPoint.position, availablePoints);
                Transform nearestPoint = availablePoints[nearestIdx];
                availablePoints.RemoveAt(nearestIdx);

                SpawnSingleOre(coord, chunk, chosenOre, nearestPoint, random, onSaveOre);
                currentSpawned++;
                currentPoint = nearestPoint; 
            }
        }
    }


    // ============================================================
    // 개별 광물 인스턴스화 (랜덤 스케일 및 회전 편차 적용 + 네트워크 스폰)
    // ============================================================

    private void SpawnSingleOre(Vector2Int coord, Chunk chunk, OreData oreData, Transform point, System.Random random, Action<Vector2Int, string, GameObject> onSaveOre)
    {
        // Y축 랜덤 회전 편차 및 스케일 계산
        float randomYAngle = (float)(random.NextDouble() * 360.0);
        Quaternion finalRotation = point.rotation * Quaternion.Euler(0f, randomYAngle, 0f);
        float randomScale = (float)(minScale + random.NextDouble() * (maxScale - minScale));

        // 1. 서버 메모리상에 오브젝트 생성
        GameObject oreObj = Instantiate(oreData.prefab, point.position, finalRotation);
        oreObj.transform.localScale = Vector3.one * randomScale;

        // 🌟 [멀티플레이 변경] 생성된 광물을 클라이언트들에게 동기화(Spawn) 및 부모 설정
        NetworkObject netObj = oreObj.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            // 클라이언트 화면에 나타나도록 네트워크 스폰 명령
            netObj.Spawn();

            // NGO에서는 NetworkObject가 붙은 오브젝트끼리 부모/자식 관계를 맺을 때 TrySetParent를 사용해야 합니다.
            NetworkObject chunkNetObj = chunk.GetComponent<NetworkObject>();
            if (chunkNetObj != null && chunkNetObj.IsSpawned)
            {
                netObj.TrySetParent(chunk.transform);
            }
            else
            {
                // Fallback (예외 상황 처리)
                oreObj.transform.SetParent(chunk.transform);
            }
        }
        else
        {
            Debug.LogWarning($"[경고] {oreData.prefab.name} 프리팹에 NetworkObject 컴포넌트가 없습니다! 동기화되지 않습니다.");
            oreObj.transform.SetParent(chunk.transform);
        }

        // 세이브 데이터에 등록
        onSaveOre?.Invoke(coord, oreData.prefab.name, oreObj);
    }


    // ============================================================
    // 거리(티어) 필터링 & 가중치 추첨 (이하 로직은 기존과 동일)
    // ============================================================

    private List<OreData> GetEligibleOres(float distance)
    {
        List<OreData> list = new List<OreData>();
        foreach (var ore in oreList)
        {
            if (ore == null || ore.prefab == null) continue;
            if (distance >= ore.minChunkDistance && distance <= ore.maxChunkDistance)
            {
                list.Add(ore);
            }
        }
        return list;
    }

    private OreData PickWeightedOre(List<OreData> ores, System.Random random)
    {
        float totalWeight = 0f;
        foreach (var ore in ores)
        {
            totalWeight += ore.spawnWeight;
        }

        if (totalWeight <= 0f) return ores[0];

        double roll = random.NextDouble() * totalWeight;
        float current = 0f;

        foreach (var ore in ores)
        {
            current += ore.spawnWeight;
            if (roll < current)
            {
                return ore;
            }
        }

        return ores[0];
    }

    private int FindNearestPointIndex(Vector3 targetPos, List<Transform> points)
    {
        int bestIdx = 0;
        float minDstSqr = float.MaxValue;

        for (int i = 0; i < points.Count; i++)
        {
            float dstSqr = (targetPos - points[i].position).sqrMagnitude;
            if (dstSqr < minDstSqr)
            {
                minDstSqr = dstSqr;
                bestIdx = i;
            }
        }

        return bestIdx;
    }


    // ============================================================
    // 복구용 프리팹 검색
    // ============================================================

    public GameObject GetOrePrefabByID(string id)
    {
        if (oreList == null) return null;
        foreach (var ore in oreList)
        {
            if (ore?.prefab != null && ore.prefab.name == id)
            {
                return ore.prefab;
            }
        }
        return null;
    }
}