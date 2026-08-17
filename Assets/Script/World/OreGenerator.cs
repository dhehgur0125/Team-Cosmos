using System.Collections.Generic;
using UnityEngine;

public class OreGenerator : MonoBehaviour
{
    [Header("광물 프리팹 목록")]
    public GameObject[] orePrefabs;

    [Header("청크당 스폰 개수")]
    public int minOresPerChunk = 2;
    public int maxOresPerChunk = 5;

    public void GenerateOres(Vector2Int coord, Chunk chunk, System.Random random, System.Action<Vector2Int, string, GameObject> onSaveOre)
    {
        if (orePrefabs == null || orePrefabs.Length == 0) return;
        if (chunk.oreSpawnPoints == null || chunk.oreSpawnPoints.Length == 0) return;

        // 중복 생성을 방지하기 위해 사용 가능한 포인트 목록 복사
        List<Transform> availablePoints = new List<Transform>(chunk.oreSpawnPoints);

        int maxPossible = Mathf.Min(maxOresPerChunk, availablePoints.Count);
        int spawnCount = random.Next(minOresPerChunk, maxPossible + 1);

        for (int i = 0; i < spawnCount; i++)
        {
            if (availablePoints.Count == 0) break;

            // 스폰 포인트 무작위 선택
            int pointIdx = random.Next(0, availablePoints.Count);
            Transform spawnPoint = availablePoints[pointIdx];
            availablePoints.RemoveAt(pointIdx);

            // 광물 프리팹 무작위 선택
            int oreIdx = random.Next(0, orePrefabs.Length);
            GameObject selectedOre = orePrefabs[oreIdx];
            if (selectedOre == null) continue;

            // 스폰 포인트의 위치와 회전에 맞춰 생성 (청크 회전이 그대로 반영됨)
            GameObject oreObj = Instantiate(selectedOre, spawnPoint.position, spawnPoint.rotation);
            oreObj.transform.SetParent(chunk.transform);

            // 세이브 데이터에 등록
            onSaveOre?.Invoke(coord, selectedOre.name, oreObj);
        }
    }

    public GameObject GetOrePrefabByID(string id)
    {
        if (orePrefabs == null) return null;
        foreach (GameObject ore in orePrefabs)
        {
            if (ore != null && ore.name == id) return ore;
        }
        return null;
    }
}