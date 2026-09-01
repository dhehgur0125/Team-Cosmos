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

    // 3축 회전값 (경사면 배치 및 WorldManager 에러 해결)
    public float rotX;
    public float rotY;
    public float rotZ;

    // 1. 3축 회전 생성자 (WorldManager 166번 줄 오류 해결)
    public PlacedObjectData(
        string prefabId,
        Vector3 position,
        float rotX,
        float rotY,
        float rotZ)
    {
        this.prefabId = prefabId;

        x = position.x;
        y = position.y;
        z = position.z;

        this.rotX = rotX;
        this.rotY = rotY;
        this.rotZ = rotZ;
    }

    // 2. 기존 Y축 회전 전용 생성자 (하위 호환)
    public PlacedObjectData(
        string prefabId,
        Vector3 position,
        float rotationY) : this(prefabId, position, 0f, rotationY, 0f)
    {
    }

    public Vector3 GetPosition()
    {
        return new Vector3(x, y, z);
    }
}