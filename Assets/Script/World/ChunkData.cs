using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ChunkData
{
    public int x;
    public int z;

    // 청크 자체의 Y축 회전값
    public float rotationY;
    public string chunkPrefabId;
    public List<PlacedObjectData> placedObjects
        = new List<PlacedObjectData>();

    public ChunkData(Vector2Int coord)
    {
        x = coord.x;
        z = coord.y;

        // 기본 회전값
        rotationY = 0;
        chunkPrefabId = "";
    }
}

[Serializable]
public class PlacedObjectData
{
    public string prefabId;

    public float x;
    public float y;
    public float z;

    // 청크 안에 배치된 오브젝트의 Y축 회전값
    public float rotY;

    public PlacedObjectData(
        string prefabId,
        Vector3 position,
        float rotationY)
    {
        this.prefabId = prefabId;

        x = position.x;
        y = position.y;
        z = position.z;

        rotY = rotationY;
    }

    public Vector3 GetPosition()
    {
        return new Vector3(x, y, z);
    }
}

