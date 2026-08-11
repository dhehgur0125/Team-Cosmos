using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ChunkData
{
    public int x;
    public int z;

    public List<PlacedObjectData> placedObjects
        = new List<PlacedObjectData>();

    public ChunkData(Vector2Int coord)
    {
        x = coord.x;
        z = coord.y;
    }
}

[Serializable]
public class PlacedObjectData
{
    public string prefabId;

    public float x;
    public float y;
    public float z;

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