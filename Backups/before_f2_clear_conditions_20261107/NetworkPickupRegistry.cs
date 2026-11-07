using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Player;

/// <summary>
/// 바닥에 놓인 아이템(주사기·무전기)을 모든 대원 화면에서 똑같이 유지한다.
///  - 씬에 배치된 아이템: 모든 화면에서 같은 순서(위치 기준)로 번호를 매긴다.
///  - 누가 주우면: 서버에 알려서 다른 대원 화면에서도 사라지게 한다. (같은 순간 두 명이 주우면 서버가 먼저 받은 쪽만 인정)
///  - Q로 내려놓으면: 다른 대원 화면에도 같은 자리에 같은 아이템을 만든다.
/// 아이템 자체는 NetworkObject가 아니어서 기존 FpsWorldPickup/FpsItemInteractor 동작을 그대로 쓴다.
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
    }

    public override void OnNetworkDespawn()
    {
        FpsWorldPickup.Collected -= OnCollected;
        FpsItemInteractor.Dropped -= OnDropped;
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
        for (int i = 0; i < list.Count; i++) Register(i + 1, list[i]);
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
        var prefab = (FarmingItemKind)kind == FarmingItemKind.Radio ? radioPickupPrefab : syringePickupPrefab;
        if (prefab == null || _byId.ContainsKey(id)) return;
        var pickup = Instantiate(prefab, position, rotation);
        pickup.ConfigureDrop(null);
        var body = pickup.GetComponent<Rigidbody>();
        if (body != null) body.linearVelocity = velocity;
        if (pickup.GetComponent<DetectableItem>() == null) pickup.gameObject.AddComponent<DetectableItem>();
        Register(id, pickup);
    }
}
