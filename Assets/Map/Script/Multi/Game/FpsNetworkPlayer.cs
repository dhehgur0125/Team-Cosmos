using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using VoidLast.Monster;
using VoidLast.Player;

/// <summary>
/// Stage2 멀티플레이 대원 (VOID_LAST_FPS_Network 프리팹 루트).
///  - 내 캐릭터(Owner): 카메라·입력·HUD·사격·아이템 줍기를 켜고, 이동/장비/핫바/HP 상태를 다른 대원에게 보낸다.
///    위치·회전은 NetworkTransform(Owner 권한)이 동기화한다.
///  - 다른 대원의 복제본: 입력·카메라·HUD를 끄고, 받은 상태로 기존 애니메이션/손 IK/장비 표시 스크립트를 그대로 돌린다.
///    몬스터(서버)가 복제본을 때리거나 다른 대원이 주사기로 회복하면 실제 HP를 가진 주인에게 RPC로 전달한다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public sealed class FpsNetworkPlayer : NetworkBehaviour
{
    public struct NetState : INetworkSerializable, IEquatable<NetState>
    {
        public Vector2 velocity;
        public float verticalSpeed;
        public float crouch;
        public float pitch;
        public float useProgress;
        public byte slot;
        public byte flags;
        public byte item0, item1, item2;
        public sbyte usingIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref velocity);
            s.SerializeValue(ref verticalSpeed);
            s.SerializeValue(ref crouch);
            s.SerializeValue(ref pitch);
            s.SerializeValue(ref useProgress);
            s.SerializeValue(ref slot);
            s.SerializeValue(ref flags);
            s.SerializeValue(ref item0);
            s.SerializeValue(ref item1);
            s.SerializeValue(ref item2);
            s.SerializeValue(ref usingIndex);
        }

        public bool Equals(NetState o) =>
            velocity == o.velocity && Mathf.Approximately(verticalSpeed, o.verticalSpeed) && Mathf.Approximately(crouch, o.crouch) &&
            Mathf.Approximately(pitch, o.pitch) && Mathf.Approximately(useProgress, o.useProgress) && slot == o.slot && flags == o.flags &&
            item0 == o.item0 && item1 == o.item1 && item2 == o.item2 && usingIndex == o.usingIndex;
    }

    const byte FlagCrouch = 1, FlagGrounded = 2, FlagAim = 4, FlagUsingOther = 8, FlagRadioTalk = 16, FlagFlashlight = 32;

    [Header("내 캐릭터에서만 켜는 컴포넌트 (프리팹에서는 꺼 둠)")]
    [Tooltip("FpsInput, FpsLook, FpsHud, FpsItemInteractor, FpsPistol, ItemDetector, 카메라, AudioListener 등")]
    public Behaviour[] ownerOnlyBehaviours;
    [Tooltip("FPS HUD 캔버스 등")]
    public GameObject[] ownerOnlyObjects;
    [Tooltip("시점 상하 회전 기준 (CameraPivot)")]
    public Transform cameraPivot;
    public Camera viewCamera;

    readonly NetworkVariable<NetState> _state = new NetworkVariable<NetState>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> _health = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<FixedString64Bytes> _nickname = new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<FixedString64Bytes> _authId = new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public static FpsNetworkPlayer Local { get; private set; }
    public static readonly List<FpsNetworkPlayer> All = new List<FpsNetworkPlayer>();
    public static event Action<FpsNetworkPlayer> LocalSpawned;

    public string Nickname => _nickname.Value.ToString();
    public string AuthPlayerId => _authId.Value.ToString();
    public bool IsRadioTalking => (_state.Value.flags & FlagRadioTalk) != 0;
    public FpsHealth Health => _healthComp;
    public FpsInventory Inventory => _inventory;
    public FpsInput PlayerInput => _input;
    public Camera ViewCamera => viewCamera;

    FpsMotor _motor;
    FpsEquipment _equipment;
    FpsInventory _inventory;
    FpsHealth _healthComp;
    FpsPistol _pistol;
    FpsInput _input;
    FpsRadioView _radio;
    FpsWeaponFlashlight _flashlight;
    bool _radioTalking;
    float _nextWrite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Local = null; All.Clear(); LocalSpawned = null; }

    void Awake()
    {
        _motor = GetComponent<FpsMotor>();
        _equipment = GetComponent<FpsEquipment>();
        _inventory = GetComponent<FpsInventory>();
        _healthComp = GetComponent<FpsHealth>();
        _pistol = GetComponent<FpsPistol>();
        _input = GetComponent<FpsInput>();
        _radio = GetComponent<FpsRadioView>();
        _flashlight = GetComponent<FpsWeaponFlashlight>();
        if (cameraPivot == null) cameraPivot = transform.Find("CameraPivot");
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
    }

    // ------------------------------------------------------------------ spawn / despawn
    public override void OnNetworkSpawn()
    {
        All.Add(this);
        if (IsOwner) SetupOwner();
        else SetupProxy();
        name = (IsOwner ? "LocalPlayer_" : "RemotePlayer_") + OwnerClientId;
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        if (Local == this) Local = null;
        if (_healthComp != null) { _healthComp.Changed -= PushHealth; _healthComp.NetworkDamageRedirect = null; _healthComp.NetworkHealRedirect = null; }
        if (_pistol != null) _pistol.Shot -= OnLocalShot;
        if (_equipment != null) _equipment.MeleeRequested -= OnLocalMelee;
        ScanNoise.Emitted -= OnLocalScan;
        _health.OnValueChanged -= OnHealthChanged;
    }

    void SetupOwner()
    {
        Local = this;
        foreach (var b in ownerOnlyBehaviours) if (b != null) b.enabled = true;
        foreach (var g in ownerOnlyObjects) if (g != null) g.SetActive(true);

        _nickname.Value = new FixedString64Bytes(Truncate(PlayerPrefs.GetString("VL_Nickname", "대원"), 20));
        string auth = "";
        try { if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn) auth = AuthenticationService.Instance.PlayerId; }
        catch (Exception) { }
        _authId.Value = new FixedString64Bytes(Truncate(auth, 60));
        _health.Value = _healthComp.Current;

        _healthComp.Changed += PushHealth;
        _pistol.Shot += OnLocalShot;
        _equipment.MeleeRequested += OnLocalMelee;
        if (!IsServer) ScanNoise.Emitted += OnLocalScan;   // 방장은 자기 화면의 몬스터가 바로 듣는다
        LocalSpawned?.Invoke(this);
    }

    void SetupProxy()
    {
        foreach (var b in ownerOnlyBehaviours) if (b != null) b.enabled = false;
        foreach (var g in ownerOnlyObjects) if (g != null) g.SetActive(false);
        _motor.SetNetworkProxy(true);
        _equipment.IsNetworkProxy = true;
        _inventory.IsNetworkProxy = true;
        if (_flashlight != null) _flashlight.SetNetworkProxy(true);
        _healthComp.NetworkDamageRedirect = amount => DamageRpc(amount);
        _healthComp.NetworkHealRedirect = amount => { HealRpc(amount); return true; };
        _health.OnValueChanged += OnHealthChanged;
        _healthComp.ApplyNetworkHealth(_health.Value);
        ApplyState(_state.Value);
    }

    static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length > max ? s.Substring(0, max) : s;

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (!IsSpawned) return;
        if (IsOwner) WriteState();
        else ApplyState(_state.Value);
    }

    void WriteState()
    {
        if (Time.unscaledTime < _nextWrite) return;
        _nextWrite = Time.unscaledTime + 1f / 30f;
        float pitch = cameraPivot != null ? Mathf.DeltaAngle(0, cameraPivot.localEulerAngles.x) : 0;
        var s = new NetState
        {
            velocity = Quantize(_motor.LocalVelocity, 50f),
            verticalSpeed = Mathf.Round(_motor.VerticalSpeed * 20f) / 20f,
            crouch = Mathf.Round(_motor.CrouchFraction * 50f) / 50f,
            pitch = Mathf.Round(pitch * 2f) / 2f,
            useProgress = Mathf.Round(_inventory.UseProgress * 50f) / 50f,
            slot = (byte)_equipment.SelectedSlot,
            item0 = (byte)_inventory.ItemAt(0),
            item1 = (byte)_inventory.ItemAt(1),
            item2 = (byte)_inventory.ItemAt(2),
            usingIndex = (sbyte)(_inventory.IsUsing ? _inventory.SelectedIndex : -1),
        };
        if (_motor.IsCrouching) s.flags |= FlagCrouch;
        if (_motor.IsGrounded) s.flags |= FlagGrounded;
        if (_equipment.IsAiming) s.flags |= FlagAim;
        if (_inventory.IsUsingOnOther) s.flags |= FlagUsingOther;
        if (_radioTalking) s.flags |= FlagRadioTalk;
        if (_flashlight != null && _flashlight.On) s.flags |= FlagFlashlight;
        if (!s.Equals(_state.Value)) _state.Value = s;
    }

    static Vector2 Quantize(Vector2 v, float steps) => new Vector2(Mathf.Round(v.x * steps) / steps, Mathf.Round(v.y * steps) / steps);

    void ApplyState(NetState s)
    {
        _motor.ApplyNetworkState(s.velocity, s.verticalSpeed, (s.flags & FlagCrouch) != 0, (s.flags & FlagGrounded) != 0, s.crouch);
        _equipment.ApplyNetworkState(s.slot, (s.flags & FlagAim) != 0);
        _inventory.ApplyNetworkState((FarmingItemKind)s.item0, (FarmingItemKind)s.item1, (FarmingItemKind)s.item2, s.usingIndex, s.useProgress, (s.flags & FlagUsingOther) != 0);
        if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Slerp(cameraPivot.localRotation, Quaternion.Euler(s.pitch, 0, 0), 1f - Mathf.Exp(-20f * Time.deltaTime));
        if (_radio != null) _radio.Talking = (s.flags & FlagRadioTalk) != 0;
        if (_flashlight != null) _flashlight.ApplyNetworkState((s.flags & FlagFlashlight) != 0);
    }

    /// <summary>내 캐릭터: 무전기로 말하는 중인지 (다른 대원 화면에서 무전기를 입에 대는 연출)</summary>
    public void SetRadioTalking(bool talking)
    {
        _radioTalking = talking;
        if (_radio != null) _radio.Talking = talking;
    }

    // ------------------------------------------------------------------ health
    void PushHealth()
    {
        if (IsOwner && IsSpawned) _health.Value = _healthComp.Current;
    }

    void OnHealthChanged(float previous, float current) => _healthComp.ApplyNetworkHealth(current);

    [Rpc(SendTo.Owner)]
    void DamageRpc(float amount)
    {
        if (_healthComp != null) _healthComp.TakeDamage(amount);
    }

    [Rpc(SendTo.Owner)]
    void HealRpc(float amount)
    {
        if (_healthComp != null) _healthComp.Heal(amount);
    }

    // ------------------------------------------------------------------ effects
    void OnLocalShot() => ShotRpc();
    void OnLocalMelee() => MeleeRpc();

    [Rpc(SendTo.NotMe)]
    void ShotRpc()
    {
        if (!IsOwner && _pistol != null) _pistol.PlayNetworkShot();
    }

    [Rpc(SendTo.NotMe)]
    void MeleeRpc()
    {
        if (!IsOwner && _equipment != null) _equipment.RaiseNetworkMelee();
    }

    // ------------------------------------------------------------------ 탐지기 스캔 소리 → 서버 몬스터
    void OnLocalScan(Vector3 origin, float radius, float waveSpeed) => ScanRpc(origin, radius, waveSpeed);

    [Rpc(SendTo.Server)]
    void ScanRpc(Vector3 origin, float radius, float waveSpeed)
    {
        ScanNoise.Emit(origin, radius, waveSpeed);
    }

    /// <summary>클라이언트 ID로 대원 찾기 (몬스터 피격자 확인 등)</summary>
    public static FpsNetworkPlayer FindByClient(ulong clientId)
    {
        foreach (var p in All) if (p != null && p.OwnerClientId == clientId) return p;
        return null;
    }
}
