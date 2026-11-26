using System.Collections.Generic;
using UnityEngine;
using VoidLast.Player;

/// <summary>
/// 게임을 나간 대원이 들고 있던 물건을 마지막 위치에 떨어뜨린다 (방장 전용).
/// 임무 물건을 든 대원이 나가도 임무가 막히지 않게 하고, 다시 참가하면 주울 수 있게 한다.
/// 모든 층에 있는 NetworkPickupRegistry가 Tick과 OnClientLeft를 불러 준다 (5층처럼 임무가 없는 층 포함).
/// </summary>
public sealed class MissionLeaverDrops
{
    struct Snapshot { public Vector3 position; public FarmingItemKind a, b, c; }

    readonly Dictionary<ulong, Snapshot> _snapshots = new Dictionary<ulong, Snapshot>();
    float _next;

    /// <summary>대원들의 위치와 인벤토리를 0.25초마다 기억해 둔다</summary>
    public void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        foreach (var p in FpsNetworkPlayer.All)
        {
            if (p == null || p.Inventory == null) continue;
            _snapshots[p.OwnerClientId] = new Snapshot { position = p.transform.position, a = p.Inventory.ItemAt(0), b = p.Inventory.ItemAt(1), c = p.Inventory.ItemAt(2) };
        }
    }

    public void OnClientLeft(ulong clientId)
    {
        if (!_snapshots.TryGetValue(clientId, out var last)) return;
        _snapshots.Remove(clientId);
        var registry = Object.FindFirstObjectByType<NetworkPickupRegistry>();
        if (registry == null) return;
        Drop(registry, last.a, last.position + new Vector3(0.4f, 0.6f, 0f));
        Drop(registry, last.b, last.position + new Vector3(-0.2f, 0.6f, 0.35f));
        Drop(registry, last.c, last.position + new Vector3(-0.2f, 0.6f, -0.35f));
    }

    static void Drop(NetworkPickupRegistry registry, FarmingItemKind kind, Vector3 at)
    {
        if (kind != FarmingItemKind.None) registry.ServerSpawnDrop(kind, at);
    }
}
