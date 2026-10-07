using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class ResourceManager : NetworkBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("참조")]
    public WorldManager worldManager;
    public OreGenerator oreGenerator;

    [Header("광물 리젠 설정")]
    [Tooltip("파괴된 광물이 다시 생성되기까지 걸리는 시간 (초)")]
    public float regenTime = 60f;

    // 로봇들이 예약한 광물의 NetworkObjectId 목록 (서버에서만 관리하여 중복 채굴 방지)
    private HashSet<ulong> reservedOres = new HashSet<ulong>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ============================================================
    // 1. 광물 예약 시스템
    // ============================================================
    public bool ReserveOre(OreNode ore)
    {
        if (!IsServer || ore == null || ore.isDestroyed) return false;
        
        ulong netId = ore.GetComponent<NetworkObject>().NetworkObjectId;
        
        // 이미 다른 로봇이 예약했다면 거절
        if (reservedOres.Contains(netId)) return false;

        reservedOres.Add(netId);
        return true;
    }

    public void CancelReservation(OreNode ore)
    {
        if (!IsServer || ore == null) return;

        ulong netId = ore.GetComponent<NetworkObject>().NetworkObjectId;
        if (reservedOres.Contains(netId))
        {
            reservedOres.Remove(netId);
        }
    }

    // ============================================================
    // 2. 광물 파괴 및 세이브 데이터 처리
    // ============================================================
    public void OnOreDestroyed(OreNode ore)
    {
        if (!IsServer) return;

        // 1. 예약 해제
        CancelReservation(ore);

        Vector2Int coord = ore.parentChunkCoord;
        string oreId = ore.oreId;
        Vector3 pos = ore.transform.position;
        Vector3 rot = ore.transform.eulerAngles;

        // 2. 월드 매니저의 세이브 데이터에서 해당 광물 삭제 
        // (플레이어가 멀리 가서 청크가 언로드되었다가 다시 로드될 때 부활하는 것 방지)
        if (worldManager != null)
        {
            ChunkData data = worldManager.GetChunkData(coord);
            if (data != null)
            {
                int removeIdx = data.placedObjects.FindIndex(o => 
                    o.prefabId == oreId && 
                    Vector3.Distance(o.GetPosition(), pos) < 0.1f);

                if (removeIdx >= 0)
                {
                    data.placedObjects.RemoveAt(removeIdx);
                }
            }
        }

        // 3. 리젠 타이머 시작
        StartCoroutine(RegenerateOreRoutine(coord, oreId, pos, rot));
    }

    // ============================================================
    // 3. 광물 리젠 시스템
    // ============================================================
    private IEnumerator RegenerateOreRoutine(Vector2Int coord, string oreId, Vector3 pos, Vector3 rot)
    {
        yield return new WaitForSeconds(regenTime);

        if (worldManager == null || oreGenerator == null) yield break;

        ChunkData data = worldManager.GetChunkData(coord);
        if (data == null) yield break;

        // 1. 세이브 데이터에 다시 기록 (나중에 해당 청크에 방문하면 자연스럽게 로드됨)
        data.placedObjects.Add(new PlacedObjectData(oreId, pos, rot.x, rot.y, rot.z));

        // 2. 만약 플레이어가 현재 그 청크에 머물러 있어서 지형이 로드된 상태라면 눈앞에서 즉시 스폰
        if (worldManager.IsChunkLoaded(coord))
        {
            GameObject prefab = oreGenerator.GetOrePrefabByID(oreId);
            if (prefab != null)
            {
                GameObject newOre = Instantiate(prefab, pos, Quaternion.Euler(rot));
                
                NetworkObject netObj = newOre.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn();
                    
                    // 해당 청크의 자식 오브젝트로 다시 편입
                    Chunk loadedChunk = worldManager.GetLoadedChunk(coord);
                    if (loadedChunk != null && loadedChunk.GetComponent<NetworkObject>() != null)
                    {
                        netObj.TrySetParent(loadedChunk.transform);
                    }
                }
            }
        }
    }
}