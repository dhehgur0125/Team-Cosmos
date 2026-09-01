using UnityEngine;

public class Chunk : MonoBehaviour
{
    [Header("청크 좌표")]
    public Vector2Int coordinate;

    [Header("광물 생성 후보 위치 (빈 오브젝트 목록)")]
    public Transform[] oreSpawnPoints;

    public void Initialize(Vector2Int coord)
    {
        coordinate = coord;
    }
}