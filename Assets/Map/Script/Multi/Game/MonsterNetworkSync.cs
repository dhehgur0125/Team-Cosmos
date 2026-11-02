using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using VoidLast.Monster;

/// <summary>
/// 몬스터 멀티플레이 동기화 (씬에 배치된 몬스터 루트에 NetworkObject + NetworkTransform(서버 권한)과 함께 붙임).
///  - 방장(서버)만 AI·NavMesh·근접 공격을 돌린다. 몬스터는 서버 화면의 모든 대원(복제본 포함)을 보고 쫓는다.
///  - 다른 대원 화면: AI를 멈추고 위치는 NetworkTransform, 상태(색·자세)·돌진 기울기·HP·비명은 여기서 받는다.
///  - 다른 대원이 쏜 총알 피해는 서버로 보내 서버의 몬스터 HP를 깎는다.
/// 네트워크 없이 씬을 바로 실행하면(오프라인 테스트) 아무것도 하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterNetworkSync : NetworkBehaviour
{
    readonly NetworkVariable<byte> _mode = new NetworkVariable<byte>();
    readonly NetworkVariable<float> _lean = new NetworkVariable<float>();
    readonly NetworkVariable<float> _health = new NetworkVariable<float>(-1f);

    MonsterHealth _hp;
    MonsterMelee _melee;
    NavMeshAgent _agent;
    EyedMonsterAI _eyed;
    SmallMonsterAI _small;
    BlindMonsterAI _blind;

    void Awake()
    {
        _hp = GetComponent<MonsterHealth>();
        _melee = GetComponent<MonsterMelee>();
        _agent = GetComponent<NavMeshAgent>();
        _eyed = GetComponent<EyedMonsterAI>();
        _small = GetComponent<SmallMonsterAI>();
        _blind = GetComponent<BlindMonsterAI>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _health.Value = _hp != null ? _hp.Current : 0;
            if (_small != null) _small.Screamed += OnServerScream;
            return;
        }

        // 클라이언트: AI를 멈추고 연출만
        SetProxy(true);
        if (_agent != null) _agent.enabled = false;
        if (_hp != null) _hp.NetworkHitRedirect = SendHit;
        _health.OnValueChanged += OnHealth;
        _mode.OnValueChanged += OnMode;
        OnHealth(-1, _health.Value);
        OnMode(0, _mode.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (_small != null) _small.Screamed -= OnServerScream;
        _health.OnValueChanged -= OnHealth;
        _mode.OnValueChanged -= OnMode;
        if (_hp != null) _hp.NetworkHitRedirect = null;
    }

    void SetProxy(bool proxy)
    {
        if (_eyed != null) _eyed.NetworkProxy = proxy;
        if (_small != null) _small.NetworkProxy = proxy;
        if (_blind != null) _blind.NetworkProxy = proxy;
        if (_melee != null && proxy) _melee.enabled = false;
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer)
        {
            byte mode = (byte)(_eyed != null ? (int)_eyed.State : _small != null ? (int)_small.State : _blind != null ? (int)_blind.State : 0);
            if (_mode.Value != mode) _mode.Value = mode;
            float lean = _melee != null ? Mathf.Round(_melee.Lean * 2f) / 2f : 0;
            if (!Mathf.Approximately(_lean.Value, lean)) _lean.Value = lean;
            float hp = _hp != null ? _hp.Current : 0;
            if (!Mathf.Approximately(_health.Value, hp)) _health.Value = hp;
        }
        else if (_melee != null)
        {
            _melee.ApplyNetworkLean(_lean.Value);
        }
    }

    void OnMode(byte previous, byte current)
    {
        if (_eyed != null) _eyed.ApplyNetworkState(current);
        if (_small != null) _small.ApplyNetworkState(current);
        if (_blind != null) _blind.ApplyNetworkState(current);
    }

    void OnHealth(float previous, float current)
    {
        if (current >= 0 && _hp != null) _hp.ApplyNetworkHealth(current);
    }

    // ------------------------------------------------------------------ 피격: 클라이언트 → 서버
    void SendHit(float damage, GameObject attacker, Vector3 hitPoint)
    {
        ulong attackerId = ulong.MaxValue;
        var no = attacker != null ? attacker.GetComponentInParent<NetworkObject>() : null;
        if (no != null && no.IsSpawned) attackerId = no.NetworkObjectId;
        HitRpc(damage, attackerId, hitPoint);
    }

    [Rpc(SendTo.Server)]
    void HitRpc(float damage, ulong attackerObjectId, Vector3 hitPoint)
    {
        if (_hp == null || damage <= 0 || damage > 1000) return;
        GameObject attacker = null;
        if (attackerObjectId != ulong.MaxValue && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerObjectId, out var no))
            attacker = no.gameObject;
        _hp.TakeHit(damage, attacker, hitPoint);
    }

    // ------------------------------------------------------------------ 비명 소리
    void OnServerScream(SmallMonsterAI _) => ScreamRpc();

    [Rpc(SendTo.NotServer)]
    void ScreamRpc()
    {
        if (_small != null) _small.PlayNetworkScream();
    }
}
