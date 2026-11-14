using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Monster;
using VoidLast.Player;

/// <summary>
/// 그 층의 반응하는 바이오매스(BioGrowth)를 관리한다.
///  - 막는 촉수: 쏜 대원 화면에서 맞힌 것을 방장에게 알리고, 방장이 피해를 세서 다 닳으면 모두에게 없애라고 알린다.
///  - 터지는 덩어리: 각자 자기 대원이 밑을 지나갔는지 보고 방장에게 알린다. 터지면 소리로 몬스터를 부르고 가까운 대원이 다친다.
///  - 둥지 중심부: 각자 자기 대원이 가까이 있으면 피해를 준다 (체력은 기존 FpsHealth 동기화를 탄다).
/// 네트워크 없이 씬을 바로 실행하면(1인 테스트) 같은 일을 이 화면에서만 처리한다.
/// </summary>
public sealed class BioGrowthSystem : NetworkBehaviour
{
    [Header("터지는 덩어리")]
    public float burstDamage = 10f;
    public float burstDamageRadius = 2.5f;
    [Tooltip("터질 때 몬스터가 듣는 반경 (권총 소리와 같은 통로)")]
    public float burstNoiseRadius = 18f;
    [Header("둥지 중심부")]
    public float hazardDamagePerSecond = 8f;
    [Header("소리 (비워 두면 소리 없음)")]
    public AudioClip burstClip;
    public AudioClip breakClip;

    const float CheckInterval = 0.2f;
    const float FloorBand = 8f;   // 이 높이 차이 안에 있는 것만 같은 층으로 본다

    readonly Dictionary<int, BioGrowth> _all = new Dictionary<int, BioGrowth>();
    float _timer;
    FpsMotor _localMotor;
    FpsHealth _localHealth;

    void Awake()
    {
        foreach (var g in FindObjectsByType<BioGrowth>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.gameObject.scene != gameObject.scene) continue;
            _all[g.id] = g;
            g.System = this;
        }
    }

    // ───────── 막는 촉수 ─────────

    public void ReportDamage(BioGrowth g, float damage)
    {
        if (IsSpawned) DamageRpc(g.id, damage);
        else if (Reduce(g.id, damage)) RemoveLocal(g.id, false);
    }

    [Rpc(SendTo.Server)]
    void DamageRpc(int id, float damage)
    {
        if (Reduce(id, damage)) RemoveRpc(id, false);
    }

    /// <summary>피해를 깎고, 이번에 다 닳았으면 true</summary>
    bool Reduce(int id, float damage)
    {
        BioGrowth g;
        if (damage <= 0f || !_all.TryGetValue(id, out g) || g.Gone || g.health <= 0f) return false;
        g.health -= damage;
        return g.health <= 0f;
    }

    // ───────── 터짐 · 근접 피해 ─────────

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = CheckInterval;

        if (!FindLocalPlayer() || _localHealth.IsDead) return;
        Vector3 p = _localMotor.transform.position;
        foreach (var g in _all.Values)
        {
            if (g.Gone || g.kind == BioGrowthKind.Blocker) continue;
            Vector3 d = g.transform.position - p;
            if (Mathf.Abs(d.y) > FloorBand || d.x * d.x + d.z * d.z > g.radius * g.radius) continue;
            if (g.kind == BioGrowthKind.Hazard) _localHealth.TakeDamage(hazardDamagePerSecond * CheckInterval);
            else if (!_localMotor.IsCrouching)
            {
                if (IsSpawned) BurstRpc(g.id);
                else RemoveLocal(g.id, true);
                return;   // 컬렉션을 도는 중이므로 한 번에 하나만
            }
        }
    }

    [Rpc(SendTo.Server)]
    void BurstRpc(int id)
    {
        BioGrowth g;
        if (_all.TryGetValue(id, out g) && !g.Gone) RemoveRpc(id, true);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void RemoveRpc(int id, bool burst) => RemoveLocal(id, burst);

    void RemoveLocal(int id, bool burst)
    {
        BioGrowth g;
        if (!_all.TryGetValue(id, out g) || g.Gone) return;
        Vector3 pos = g.transform.position;
        g.Remove();
        var clip = burst ? burstClip : breakClip;
        if (clip != null) GameAudio.PlayAt(clip, pos);
        if (!burst) return;

        GunNoise.Emit(pos, burstNoiseRadius);
        if (FindLocalPlayer() && !_localHealth.IsDead)
        {
            Vector3 d = pos - _localMotor.transform.position;
            if (Mathf.Abs(d.y) <= FloorBand && d.x * d.x + d.z * d.z <= burstDamageRadius * burstDamageRadius) _localHealth.TakeDamage(burstDamage);
        }
    }

    /// <summary>이 화면에서 조종하는 대원 (멀티: 내 FpsNetworkPlayer / 1인 테스트: 네트워크 없는 대원)</summary>
    bool FindLocalPlayer()
    {
        if (_localMotor != null && _localMotor.isActiveAndEnabled) return true;
        _localMotor = null;
        foreach (var m in FindObjectsByType<FpsMotor>(FindObjectsSortMode.None))
        {
            var net = m.GetComponent<FpsNetworkPlayer>();
            if (net != null && !(net.IsSpawned && net.IsOwner)) continue;
            var h = m.GetComponent<FpsHealth>();
            if (h == null) continue;
            _localMotor = m; _localHealth = h;
            return true;
        }
        return false;
    }
}
