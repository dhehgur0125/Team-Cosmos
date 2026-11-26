using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Player;

/// <summary>
/// 바닥에 놓인 아이템(주사기·무전기·배터리·임무 물건)을 모든 대원 화면에서 똑같이 유지한다.
///  - 씬에 배치된 아이템: 모든 화면에서 같은 순서(위치 기준)로 번호를 매긴다. 아직 꺼져 있는 물건(임무가 진행되면 나타나는 물건)도 포함한다.
///  - 누가 주우면: 서버에 알려서 다른 대원 화면에서도 사라지게 한다. (같은 순간 두 명이 주우면 서버가 먼저 받은 쪽만 인정)
///  - Q로 내려놓거나 죽어서 떨어뜨리면: 다른 대원 화면에도 같은 자리에 같은 아이템을 만든다.
///  - 게임 도중 들어온 대원(재접속): 방장이 지금 남아 있는 아이템 목록을 보내 줘서, 그새 주운 것은 지우고 떨어진 것은 만든다.
/// 아이템 자체는 NetworkObject가 아니어서 기존 FpsWorldPickup/FpsItemInteractor 동작을 그대로 쓴다.
/// 씬 아이템 번호는 위치·이름으로 정한다 (예전에 주워서 없어진 아이템이 화면마다 달라도 번호가 밀리지 않게). 떨어뜨린 아이템 번호는 10억 미만.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkPickupRegistry : NetworkBehaviour
{
    [Tooltip("다른 대원이 내려놓은 아이템을 내 화면에 만들 때 쓰는 프리팹")]
    public FpsWorldPickup syringePickupPrefab;
    public FpsWorldPickup radioPickupPrefab;

    readonly Dictionary<int, FpsWorldPickup> _byId = new Dictionary<int, FpsWorldPickup>();
    readonly Dictionary<FpsWorldPickup, int> _ids = new Dictionary<FpsWorldPickup, int>();
    readonly HashSet<int> _claimed = new HashSet<int>();   // 서버 전용
    int _dropCounter;

    public override void OnNetworkSpawn()
    {
        RegisterScenePickups();
        FpsWorldPickup.Collected += OnCollected;
        FpsItemInteractor.Dropped += OnDropped;
        if (IsServer && NetworkManager.SceneManager != null) NetworkManager.SceneManager.OnSynchronizeComplete += OnLateJoin;
    }

    public override void OnNetworkDespawn()
    {
        FpsWorldPickup.Collected -= OnCollected;
        FpsItemInteractor.Dropped -= OnDropped;
        if (NetworkManager != null && NetworkManager.SceneManager != null) NetworkManager.SceneManager.OnSynchronizeComplete -= OnLateJoin;
    }

    const int SceneIdBase = 1000000000;

    // ------------------------------------------------------------------ 게임 도중 들어온 대원
    void OnLateJoin(ulong clientId)
    {
        if (!IsServer || clientId == NetworkManager.ServerClientId) return;
        var scene = new List<int>();
        var dropIds = new List<int>(); var dropKinds = new List<byte>(); var dropPos = new List<Vector3>();
        foreach (var kv in _byId)
        {
            if (kv.Value == null) continue;
            if (kv.Key >= SceneIdBase) scene.Add(kv.Key);
            else if (kv.Value.gameObject.activeInHierarchy && !kv.Value.IsClaimed)
            {
                dropIds.Add(kv.Key); dropKinds.Add((byte)kv.Value.Item); dropPos.Add(kv.Value.transform.position);
            }
        }
        LateJoinRpc(scene.ToArray(), dropIds.ToArray(), dropKinds.ToArray(), dropPos.ToArray(), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        Debug.Log($"[Pickup] 게임 도중 들어온 Client {clientId}에게 아이템 상태 전송 (씬 {scene.Count}, 떨어진 것 {dropIds.Count})");
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void LateJoinRpc(int[] sceneAlive, int[] dropIds, byte[] dropKinds, Vector3[] dropPositions, RpcParams rpcParams = default)
    {
        var alive = new HashSet<int>(sceneAlive);
        var remove = new List<int>();
        foreach (var kv in _byId) if (kv.Key >= SceneIdBase && !alive.Contains(kv.Key)) remove.Add(kv.Key);
        foreach (int id in remove)
        {
            var pickup = _byId[id];
            _byId.Remove(id);
            if (pickup != null) { _ids.Remove(pickup); pickup.RemoveByNetwork(); }
        }
        for (int i = 0; i < dropIds.Length && i < dropKinds.Length && i < dropPositions.Length; i++)
            SpawnDrop(dropIds[i], (FarmingItemKind)dropKinds[i], dropPositions[i], Quaternion.identity, Vector3.zero);
    }

    void RegisterScenePickups()
    {
        var list = new List<FpsWorldPickup>();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var p in root.GetComponentsInChildren<FpsWorldPickup>(true))
                if (!p.IsDropped && !p.IsClaimed) list.Add(p);
        list.Sort((a, b) =>
        {
            int c = Key(a.transform.position).CompareTo(Key(b.transform.position));
            return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
        });
        // 번호 = 위치·이름으로 만든 값 (모든 화면에서 같음). 겹치면 정렬 순서대로 다음 빈 번호
        foreach (var p in list)
        {
            int id = SceneIdBase + (int)(Hash(Key(p.transform.position), p.name) % 1000000000u);
            while (_byId.ContainsKey(id)) id = id >= 2000000000 ? SceneIdBase : id + 1;
            Register(id, p);
        }
    }

    static uint Hash(long key, string name)
    {
        unchecked
        {
            uint h = 2166136261;
            for (int i = 0; i < 8; i++) { h ^= (byte)(key >> (i * 8)); h *= 16777619; }
            foreach (char c in name ?? "") { h ^= c; h *= 16777619; }
            return h;
        }
    }

    static long Key(Vector3 p)
    {
        unchecked
        {
            long x = Mathf.RoundToInt(p.x * 100) + 1000000, y = Mathf.RoundToInt(p.y * 100) + 1000000, z = Mathf.RoundToInt(p.z * 100) + 1000000;
            return (x * 2000003L + y) * 2000003L + z;
        }
    }

    void Register(int id, FpsWorldPickup pickup)
    {
        if (pickup == null) return;
        _byId[id] = pickup;
        _ids[pickup] = id;
    }

    static bool IsLocalOwner(Component c)
    {
        var player = c != null ? c.GetComponent<FpsNetworkPlayer>() : null;
        return player != null && player.IsSpawned && player.IsOwner;
    }

    // ------------------------------------------------------------------ 줍기
    void OnCollected(FpsWorldPickup pickup, FpsInventory inventory)
    {
        if (!IsSpawned || !IsLocalOwner(inventory)) return;
        if (!_ids.TryGetValue(pickup, out int id)) return;
        _ids.Remove(pickup); _byId.Remove(id);
        ClaimRpc(id, (byte)pickup.Item);
    }

    [Rpc(SendTo.Server)]
    void ClaimRpc(int id, byte kind, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!_claimed.Add(id))
        {
            // 이미 다른 대원이 먼저 주웠다 → 늦게 주운 대원의 핫바에서 다시 뺀다
            RevokeRpc(kind, RpcTarget.Single(sender, RpcTargetUse.Temp));
            return;
        }
        RemoveRpc(id);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void RemoveRpc(int id)
    {
        if (!_byId.TryGetValue(id, out var pickup)) return;
        _byId.Remove(id);
        if (pickup != null) { _ids.Remove(pickup); pickup.RemoveByNetwork(); }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void RevokeRpc(byte kind, RpcParams rpcParams = default)
    {
        var local = FpsNetworkPlayer.Local;
        if (local == null || local.Inventory == null) return;
        local.Inventory.RemoveOneByNetwork((FarmingItemKind)kind);
    }

    // ------------------------------------------------------------------ 내려놓기
    void OnDropped(FpsItemInteractor interactor, FpsWorldPickup pickup)
    {
        if (!IsSpawned || pickup == null || !IsLocalOwner(interactor)) return;
        int id = (int)(NetworkManager.LocalClientId + 1) * 100000 + (++_dropCounter);
        Register(id, pickup);
        var body = pickup.GetComponent<Rigidbody>();
        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        DropRpc(id, (byte)pickup.Item, pickup.transform.position, pickup.transform.rotation, velocity);
    }

    [Rpc(SendTo.NotMe)]
    void DropRpc(int id, byte kind, Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        SpawnDrop(id, (FarmingItemKind)kind, position, rotation, velocity);
    }

    /// <summary>방장 전용: 게임을 나간 대원의 물건을 마지막 위치에 떨어뜨린다</summary>
    public void ServerSpawnDrop(FarmingItemKind kind, Vector3 position)
    {
        if (!IsSpawned || !IsServer) return;
        int id = 900000000 + (++_dropCounter);
        SpawnDrop(id, kind, position, Quaternion.identity, Vector3.zero);
        DropRpc(id, (byte)kind, position, Quaternion.identity, Vector3.zero);
    }

    void SpawnDrop(int id, FarmingItemKind kind, Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        var prefab = kind == FarmingItemKind.Radio ? radioPickupPrefab : kind == FarmingItemKind.Syringe ? syringePickupPrefab : FarmingItems.PickupPrefab(kind);
        if (prefab == null || _byId.ContainsKey(id)) return;
        var pickup = Instantiate(prefab, position, rotation);
        pickup.ConfigureDrop(null);
        var body = pickup.GetComponent<Rigidbody>();
        if (body != null) body.linearVelocity = velocity;
        if (FarmingItems.IsScannable(kind) && pickup.GetComponent<DetectableItem>() == null) pickup.gameObject.AddComponent<DetectableItem>();
        Register(id, pickup);
    }
}
