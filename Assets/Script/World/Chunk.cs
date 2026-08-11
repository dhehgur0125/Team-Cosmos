using UnityEngine;

public class Chunk : MonoBehaviour
{
    public Vector2Int coordinate;

    public int Large_chunkscale = 100;


    public void Initialize(Vector2Int coord)
    {
        coordinate = coord;

        transform.position = new Vector3(
            coord.x * Large_chunkscale,
            0,
            coord.y * Large_chunkscale
        );
    }
}