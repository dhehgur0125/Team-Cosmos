using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using VoidLast.Monster;
using VoidLast.Player;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 2층(화물 및 검역 구역) 임무 진행과 클리어 판정. 앞 단계를 해야 다음 단계가 열린다.
///
/// 클리어 조건
///  1. 화물 분류실(E)에서 검역 카드를 찾아 검역 관제실(NE)의 봉쇄를 푼다. 풀리면 장비 작업실(N)의 봉쇄 문이 열린다.
///  2. 장비 작업실에서 절단기를 얻는다.
///  3. 정비 보관실(NW)에서 절단기로 전력 모듈을 꺼낸다. 금속음이 소형 정찰형(쪼꼬미)을 부른다.
///     모듈은 중앙 화물창(C) 리프트에 실어 1층으로 내려보낸다.
///  4. 연료 커플러를 찾는다. 후보 위치 중 한 곳에 판마다 다르게 나타나고, 스캔으로 찾을 수 있다 (스캔 소리는 눈 없는 몬스터를 부른다).
///  5. 연료 공급실(SW)에 커플러를 끼우고 90초 충전한다. 살아 있는 대원이 연료 공급실 안에 있어야 진행되고, 비우면 멈춘다.
///     충전 중간에 컨테이너 보관실(S)의 밸브가 고장 나면 고칠 때까지 멈춘다. 펌프 소음이 쪼꼬미를 부른다.
///  6. (선택) 검역 보관실(SE)의 기록을 조사하면 그 자리에 배터리가 나타난다.
///  7. A 계단으로 내려간다 (StairDoorController, FloorTransition).
///  살아 있는 대원이 한 명도 없으면 임무 실패.
///
/// 임무 물건은 일반 아이템처럼 인벤토리에 들어가고, 장치에 쓰면 사라진다. 죽으면 그 자리에 떨어지고, 게임을 나가면 마지막 위치에 떨어진다.
/// 멀티플레이: 판정은 방장(서버)만 하고 상태는 모든 대원에게 동기화된다.
/// 오프라인(씬을 바로 실행): 같은 규칙을 이 컴퓨터에서 그대로 진행한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class F2MissionDirector : NetworkBehaviour
{
    public enum Phase : byte { Objectives = 0, Rally = 1, Cleared = 2, Failed = 3 }

    public struct MissionState : INetworkSerializable, IEquatable<MissionState>
    {
        public byte phase;
        public bool lockdownLifted;
        public byte module;          // 0 보관함 안, 1 꺼냄, 2 리프트로 보냄
        public FixedString64Bytes carrier;
        public byte couplerSpot;     // 0 아직 안 정함, 1~ 커플러가 나타날 후보 번호
        public byte fuel;            // 0 대기, 1 공급 중, 2 완료
        public float fuelProgress;   // 0~1
        public bool fuelPaused;
        public bool valveBroken;
        public bool trace;
        public byte rallyIn, rallyNeed;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref phase);
            s.SerializeValue(ref lockdownLifted);
            s.SerializeValue(ref module);
            s.SerializeValue(ref carrier);
            s.SerializeValue(ref couplerSpot);
            s.SerializeValue(ref fuel);
            s.SerializeValue(ref fuelProgress);
            s.SerializeValue(ref fuelPaused);
            s.SerializeValue(ref valveBroken);
            s.SerializeValue(ref trace);
            s.SerializeValue(ref rallyIn);
            s.SerializeValue(ref rallyNeed);
        }

        public bool Equals(MissionState o) =>
            phase == o.phase && lockdownLifted == o.lockdownLifted && module == o.module && carrier.Equals(o.carrier) &&
            couplerSpot == o.couplerSpot && fuel == o.fuel && Mathf.Approximately(fuelProgress, o.fuelProgress) &&
            fuelPaused == o.fuelPaused && valveBroken == o.valveBroken && trace == o.trace &&
            rallyIn == o.rallyIn && rallyNeed == o.rallyNeed;
    }

    /// <summary>검역 봉쇄 문: 봉쇄를 풀면 위로 올라가 천장 속으로 들어간다</summary>
    [Serializable]
    public class LockdownDoor
    {
        public Transform leaf;
        public Collider blocker;
        public Collider interactTrigger;
        public NavMeshObstacle obstacle;
        public Vector3 openOffset = new Vector3(0, 4f, 0);
        [NonSerialized] public Vector3 closedPos;
        [NonSerialized] public float t;
    }

    [Header("장치")]
    public MissionInteractable quarantineConsole;
    public MissionInteractable powerModule;
    public MissionInteractable cargoLift;
    public MissionInteractable fuelPump;
    public MissionInteractable fuelValve;
    public MissionInteractable intrusionLog;

    [Header("임무 진행에 따라 나타나는 물건 (씬에 꺼진 채로 놓아 둠)")]
    public FpsWorldPickup modulePickup;
    [Tooltip("연료 커플러 후보 위치. 판마다 이 중 한 곳에만 나타난다")]
    public FpsWorldPickup[] couplerSpots;
    public FpsWorldPickup batteryReward;

    [Header("검역 봉쇄")]
    public LockdownDoor[] lockdownDoors;
    [Min(0.2f)] public float lockdownDoorSeconds = 1.6f;
    [Tooltip("봉쇄를 풀기 전에는 들어갈 수 없는 구역 (장비 작업실). 여기에는 고치가 생기지 않는다")]
    public Bounds lockedArea = new Bounds(new Vector3(11f, 10f, -11f), new Vector3(20f, 6f, 16f));

    [Header("화물 리프트")]
    public Transform liftPlatform;
    public GameObject liftCargo;
    [Min(0.5f)] public float liftTravel = 3.2f;
    [Min(0.5f)] public float liftSeconds = 6f;

    [Header("구역 (월드 좌표)")]
    [Tooltip("연료 공급 중 대원이 머물러야 하는 연료 공급실")]
    public Bounds fuelRoom = new Bounds(new Vector3(-62f, 10f, 26f), new Vector3(22f, 6f, 18f));
    [Tooltip("2층 → 1층 계단 안 (A·B). 여기에 살아 있는 대원 전원이 들어오면 클리어")]
    public Bounds[] rallyZones =
    {
        new Bounds(new Vector3(-50f, 5f, 0f), new Vector3(15.4f, 8.5f, 15.4f)),   // A 계단 (서쪽)
        new Bounds(new Vector3(50f, 5f, 0f), new Vector3(15.4f, 8.5f, 15.4f)),    // B 계단 (동쪽)
    };
    [Tooltip("탈출 준비가 끝나면 켜지는 계단 입구 안내등")]
    public Light[] rallyLights;

    [Header("규칙")]
    [Min(5f)] public float fuelSeconds = 90f;
    [Tooltip("충전이 이 비율 사이 어딘가에 이르면 밸브가 고장 난다")]
    public Vector2 valveBreakRange = new Vector2(0.35f, 0.65f);
    [Min(3f)] public float pumpNoiseInterval = 15f;
    [Tooltip("펌프 소음이 쪼꼬미에게 들리는 반경(m, 같은 층만)")]
    [Min(5f)] public float pumpNoiseRadius = 100f;
    [Tooltip("전력 모듈을 꺼낼 때 금속음이 쪼꼬미에게 들리는 반경(m, 같은 층만)")]
    [Min(1f)] public float moduleNoiseRadius = 30f;

    [Header("UI / 이야기")]
    public TMP_FontAsset font;
    [TextArea(4, 12)]
    public string intrusionLogText =
        "<b>검역 보관실 · 정비 담당 음성 기록</b>\n\n" +
        "\"3번 표본 컨테이너 봉인이… 안쪽에서 뜯겨 있어. 행성에서 실어 온 그 상자야.\n" +
        "바닥에 끈적한 막이 깔려 있고, 껍질 같은 게 여러 개 남아 있어. 뭔가 여기서 부화했어.\n" +
        "흔적이 환기구 쪽으로 이어져. 통제실에 보고하고 이 구역을 봉쇄해야—\n" +
        "…잠깐, 천장에서 무슨 소리가—\"\n\n<color=#8A96A3>(기록이 끊겼다)</color>";

    public static F2MissionDirector Instance { get; private set; }
    /// <summary>2층 클리어 순간 (모든 화면에서 한 번)</summary>
    public static event Action FloorCleared;

    readonly NetworkVariable<MissionState> _net = new NetworkVariable<MissionState>();
    MissionState _offline;
    MissionState _shown;
    bool _shownInit;
    float _nextNoise, _cooldown, _hold;
    float _fuelAccum;   // 판정 쪽에서만 쓰는 정밀 진행률 (네트워크로는 반올림 값만 보냄)
    float _valveBreakAt;
    bool _valveBrokeOnce;
    float _liftT, _nextSnapshot;
    Vector3 _liftTop;
    MissionInteractable _target;
    string _lastStatusKey;
    AudioClip _clankClip, _alarmClip, _liftClip;

    public MissionState State => IsSpawned ? _net.Value : _offline;
    bool HasAuthority => !IsSpawned || IsServer;
    /// <summary>아직 봉쇄되어 들어갈 수 없는 곳인지</summary>
    public bool IsInLockedArea(Vector3 point) => !State.lockdownLifted && lockedArea.Contains(point);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; FloorCleared = null; }

    void Awake()
    {
        Instance = this;
        if (lockdownDoors != null)
            foreach (var d in lockdownDoors) if (d != null && d.leaf != null) d.closedPos = d.leaf.position;
        if (liftPlatform != null) _liftTop = liftPlatform.position;
        _clankClip = MakeClank();
        _alarmClip = MakeAlarm();
        _liftClip = MakeLiftHum();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        BuildHud();
        ApplyVisuals(State, true);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback += OnClientLeft;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
    }

    void SetState(MissionState s)
    {
        if (IsSpawned) { if (IsServer && !_net.Value.Equals(s)) _net.Value = s; }
        else _offline = s;
    }

    // ================================================================== 서버(또는 오프라인) 판정
    void Update()
    {
        if (HasAuthority) Simulate(Time.deltaTime);
        var s = State;
        ApplyVisuals(s, false);
        AnimateWorld(s);
        AnnounceChanges(s);
        LocalInteraction();
        UpdateHud(s);
    }

    readonly List<FpsHealth> _players = new List<FpsHealth>();

    void CollectPlayers()
    {
        _players.Clear();
        if (IsSpawned)
        {
            foreach (var p in FpsNetworkPlayer.All) if (p != null && p.Health != null) _players.Add(p.Health);
        }
        else
        {
            foreach (var h in FindObjectsByType<FpsHealth>(FindObjectsSortMode.None)) if (h.isActiveAndEnabled) _players.Add(h);
        }
    }

    static bool Alive(FpsHealth h) => h != null && h.isActiveAndEnabled && !h.IsDead;

    void Simulate(float dt)
    {
        var s = State;
        if (s.phase == (byte)Phase.Cleared || s.phase == (byte)Phase.Failed) return;
        CollectPlayers();
        int alive = 0;
        foreach (var p in _players) if (Alive(p)) alive++;

        // 실패: 대원이 있었는데 모두 사망
        if (_players.Count > 0 && alive == 0) { s.phase = (byte)Phase.Failed; SetState(s); return; }

        SnapshotPlayers();

        // 연료 커플러가 나타날 곳을 판마다 하나 고른다
        if (s.couplerSpot == 0 && couplerSpots != null && couplerSpots.Length > 0)
            s.couplerSpot = (byte)UnityEngine.Random.Range(1, couplerSpots.Length + 1);

        if (s.phase == (byte)Phase.Objectives)
        {
            if (s.fuel == 1)
            {
                bool guarded = false;
                foreach (var p in _players) if (Alive(p) && fuelRoom.Contains(p.transform.position + Vector3.up)) { guarded = true; break; }
                s.fuelPaused = !guarded;
                if (_fuelAccum < s.fuelProgress) _fuelAccum = s.fuelProgress;   // 방장이 바뀌는 등으로 값이 어긋났을 때 보정
                // 충전 중간에 밸브가 한 번 고장 난다: 고칠 때까지 충전이 멈춘다
                if (!_valveBrokeOnce && _fuelAccum >= _valveBreakAt) { _valveBrokeOnce = true; s.valveBroken = true; }
                if (guarded && !s.valveBroken) _fuelAccum = Mathf.Min(1f, _fuelAccum + dt / fuelSeconds);
                // 화면/네트워크용 값은 0.5% 단위로 끊어서 보낸다 (매 프레임 아주 작게 늘어나는 값을 반올림하면 0에 머무르므로 내림)
                s.fuelProgress = _fuelAccum >= 1f ? 1f : Mathf.Floor(_fuelAccum * 200f) / 200f;
                if (Time.time >= _nextNoise) { _nextNoise = Time.time + pumpNoiseInterval; MissionNoise(fuelPump, pumpNoiseRadius); }
                if (_fuelAccum >= 1f) { s.fuel = 2; s.fuelPaused = false; }
            }
            if (s.module == 2 && s.fuel == 2) s.phase = (byte)Phase.Rally;
        }

        if (s.phase == (byte)Phase.Rally)
        {
            int inside = 0;
            foreach (var p in _players) if (Alive(p) && InRally(p.transform.position + Vector3.up * 0.5f)) inside++;
            s.rallyIn = (byte)Mathf.Min(255, inside);
            s.rallyNeed = (byte)Mathf.Min(255, alive);
            // 하나로 된 예전 씬에서만: 계단 안에 모두 들어가면 클리어. 층별 씬에서는 계단 문을 열면 FloorTransition이 아래층으로 이동시킨다
            if (FloorTransition.Instance == null && alive > 0 && inside >= alive) s.phase = (byte)Phase.Cleared;
        }
        SetState(s);
    }

    bool InRally(Vector3 p)
    {
        if (rallyZones == null) return false;
        foreach (var z in rallyZones) if (z.Contains(p)) return true;
        return false;
    }

    /// <summary>임무 소음: 같은 층의 쪼꼬미만 듣고 소리 난 곳으로 조사하러 온다</summary>
    void MissionNoise(MissionInteractable source, float radius)
    {
        if (source == null) return;
        Vector3 at = source.transform.position;
        foreach (var m in FindObjectsByType<SmallMonsterAI>(FindObjectsSortMode.None))
        {
            Vector3 d = m.transform.position - at;
            if (Mathf.Abs(d.y) > 3.5f || d.sqrMagnitude > radius * radius) continue;
            m.HearNoise(at);
        }
    }

    // ================================================================== 게임을 나간 대원의 물건
    struct PlayerSnapshot { public Vector3 position; public FarmingItemKind a, b, c; }
    readonly Dictionary<ulong, PlayerSnapshot> _snapshots = new Dictionary<ulong, PlayerSnapshot>();

    void SnapshotPlayers()
    {
        if (!IsSpawned || Time.unscaledTime < _nextSnapshot) return;
        _nextSnapshot = Time.unscaledTime + 0.25f;
        foreach (var p in FpsNetworkPlayer.All)
        {
            if (p == null || p.Inventory == null) continue;
            _snapshots[p.OwnerClientId] = new PlayerSnapshot { position = p.transform.position, a = p.Inventory.ItemAt(0), b = p.Inventory.ItemAt(1), c = p.Inventory.ItemAt(2) };
        }
    }

    void OnClientLeft(ulong clientId)
    {
        if (!_snapshots.TryGetValue(clientId, out var last)) return;
        _snapshots.Remove(clientId);
        var registry = FindFirstObjectByType<NetworkPickupRegistry>();
        if (registry == null) return;
        DropLeft(registry, last.a, last.position + new Vector3(0.4f, 0.6f, 0f));
        DropLeft(registry, last.b, last.position + new Vector3(-0.2f, 0.6f, 0.35f));
        DropLeft(registry, last.c, last.position + new Vector3(-0.2f, 0.6f, -0.35f));
    }

    static void DropLeft(NetworkPickupRegistry registry, FarmingItemKind kind, Vector3 at)
    {
        if (kind != FarmingItemKind.None) registry.ServerSpawnDrop(kind, at);
    }

    // ================================================================== 상호작용 요청
    void Request(MissionInteractable.Kind kind, FpsHealth who)
    {
        if (HasAuthority) TryApply(kind, who != null ? who.transform.position : Vector3.zero, LocalNickname(), who != null, who != null ? who.GetComponent<FpsInventory>() : null, IsSpawned ? NetworkManager.LocalClientId : 0);
        else InteractRpc((byte)kind);
    }

    [Rpc(SendTo.Server)]
    void InteractRpc(byte kind, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        var player = FpsNetworkPlayer.FindByClient(sender);
        if (player == null || player.Health == null || player.Health.IsDead) return;
        TryApply((MissionInteractable.Kind)kind, player.transform.position, string.IsNullOrEmpty(player.Nickname) ? "대원" : player.Nickname, true, player.Inventory, sender);
    }

    /// <summary>이 장치를 쓰는 데 필요한 임무 물건</summary>
    static FarmingItemKind Needs(MissionInteractable.Kind kind)
    {
        switch (kind)
        {
            case MissionInteractable.Kind.QuarantineConsole: return FarmingItemKind.QuarantineCard;
            case MissionInteractable.Kind.PowerModule: return FarmingItemKind.Cutter;
            case MissionInteractable.Kind.CargoLift: return FarmingItemKind.PowerModule;
            case MissionInteractable.Kind.FuelPump: return FarmingItemKind.FuelCoupler;
            default: return FarmingItemKind.None;
        }
    }

    void TryApply(MissionInteractable.Kind kind, Vector3 from, string nick, bool checkDistance, FpsInventory inventory, ulong clientId)
    {
        var device = Device(kind);
        if (device == null) return;
        if (checkDistance && Vector3.Distance(from, device.transform.position) > device.maxDistance + 2.5f) return;
        var need = Needs(kind);
        if (need != FarmingItemKind.None && (inventory == null || !inventory.HasItem(need))) return;
        var s = State;
        bool objectives = s.phase == (byte)Phase.Objectives;
        switch (kind)
        {
            case MissionInteractable.Kind.QuarantineConsole:
                if (!objectives || s.lockdownLifted) return;
                s.lockdownLifted = true;
                break;
            case MissionInteractable.Kind.PowerModule:
                if (!objectives || s.module != 0) return;
                s.module = 1;
                s.carrier = new FixedString64Bytes(nick.Length > 20 ? nick.Substring(0, 20) : nick);
                MissionNoise(device, moduleNoiseRadius);   // 금속음 → 쪼꼬미
                break;
            case MissionInteractable.Kind.CargoLift:
                if (!objectives || s.module != 1) return;
                s.module = 2;
                break;
            case MissionInteractable.Kind.FuelPump:
                if (!objectives || s.fuel != 0 || s.module != 2) return;
                s.fuel = 1; s.fuelProgress = 0f; s.fuelPaused = false; s.valveBroken = false; _fuelAccum = 0f; _valveBrokeOnce = false;
                _valveBreakAt = UnityEngine.Random.Range(valveBreakRange.x, valveBreakRange.y);
                _nextNoise = Time.time + pumpNoiseInterval;
                MissionNoise(device, pumpNoiseRadius);
                break;
            case MissionInteractable.Kind.FuelValve:
                if (!objectives || !s.valveBroken) return;
                s.valveBroken = false;
                break;
            case MissionInteractable.Kind.IntrusionLog:
                if (s.trace) return;
                s.trace = true;
                break;
            default:
                return;
        }
        SetState(s);
        if (need != FarmingItemKind.None) ConsumeItem(inventory, need, clientId);
    }

    /// <summary>장치에 쓴 임무 물건을 그 대원의 인벤토리에서 없앤다 (인벤토리는 대원 본인 화면이 관리한다)</summary>
    void ConsumeItem(FpsInventory inventory, FarmingItemKind kind, ulong clientId)
    {
        if (!IsSpawned || clientId == NetworkManager.LocalClientId) inventory.RemoveOneByNetwork(kind);
        else ConsumeItemRpc((byte)kind, RpcTarget.Single(clientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void ConsumeItemRpc(byte kind, RpcParams rpcParams = default)
    {
        var local = FpsNetworkPlayer.Local;
        if (local != null && local.Inventory != null) local.Inventory.RemoveOneByNetwork((FarmingItemKind)kind);
    }

    MissionInteractable Device(MissionInteractable.Kind kind)
    {
        switch (kind)
        {
            case MissionInteractable.Kind.QuarantineConsole: return quarantineConsole;
            case MissionInteractable.Kind.PowerModule: return powerModule;
            case MissionInteractable.Kind.CargoLift: return cargoLift;
            case MissionInteractable.Kind.FuelPump: return fuelPump;
            case MissionInteractable.Kind.FuelValve: return fuelValve;
            case MissionInteractable.Kind.IntrusionLog: return intrusionLog;
            default: return null;
        }
    }

    // ================================================================== 내 화면: F 길게 누르기
    static FpsHealth LocalPlayer()
    {
        if (Stage2NetworkBootstrap.IsOnline) return FpsNetworkPlayer.Local != null ? FpsNetworkPlayer.Local.Health : null;
        var cam = Camera.main;
        return cam != null ? cam.GetComponentInParent<FpsHealth>() : null;
    }

    static string LocalNickname()
    {
        if (FpsNetworkPlayer.Local != null && !string.IsNullOrEmpty(FpsNetworkPlayer.Local.Nickname)) return FpsNetworkPlayer.Local.Nickname;
        return PlayerPrefs.GetString("VL_Nickname", "대원");
    }

    static bool InteractHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.fKey.isPressed;
#else
        return GameKeys.Held(GameAction.Interact);
#endif
    }

    bool Available(MissionInteractable d, MissionState s, FpsHealth me, out string reason)
    {
        reason = null;
        var inventory = me != null ? me.GetComponent<FpsInventory>() : null;
        bool Has(FarmingItemKind k) => inventory != null && inventory.HasItem(k);
        const string Need = "<color=#FCA5A5>";
        switch (d.kind)
        {
            case MissionInteractable.Kind.QuarantineConsole:
                if (s.lockdownLifted) { reason = "검역 봉쇄 해제됨"; return false; }
                if (!Has(FarmingItemKind.QuarantineCard)) { reason = Need + "검역 카드가 필요해요</color> · 화물 분류실에 있어요"; return false; }
                break;
            case MissionInteractable.Kind.PowerModule:
                if (s.module != 0) { reason = "전력 모듈은 이미 꺼냈어요 (" + s.carrier + ")"; return false; }
                if (!Has(FarmingItemKind.Cutter)) { reason = Need + "보관함을 자를 절단기가 필요해요</color> · 장비 작업실에 있어요"; return false; }
                break;
            case MissionInteractable.Kind.CargoLift:
                if (s.module == 2) { reason = "전력 모듈을 1층으로 보냈어요"; return false; }
                if (!Has(FarmingItemKind.PowerModule)) { reason = Need + "실어 보낼 전력 모듈이 필요해요</color>"; return false; }
                break;
            case MissionInteractable.Kind.FuelPump:
                if (s.fuel == 1) { reason = "연료 공급 중  " + Mathf.RoundToInt(s.fuelProgress * 100) + "%"; return false; }
                if (s.fuel == 2) { reason = "연료 공급 완료"; return false; }
                if (s.module != 2) { reason = Need + "먼저 전력 모듈을 1층으로 보내세요</color>"; return false; }
                if (!Has(FarmingItemKind.FuelCoupler)) { reason = Need + "연료 커플러가 필요해요</color>"; return false; }
                break;
            case MissionInteractable.Kind.FuelValve:
                if (!s.valveBroken) { reason = s.fuel == 2 ? "연료 밸브 정상" : "연료 밸브 정상 작동 중"; return false; }
                break;
            case MissionInteractable.Kind.LockdownDoor:
                reason = s.lockdownLifted ? "" : Need + "검역 봉쇄 중</color> · 검역 관제실에서 해제하세요";
                return false;
            case MissionInteractable.Kind.IntrusionLog:
                return true;   // 몇 번이든 다시 볼 수 있음
        }
        if (s.phase != (byte)Phase.Objectives) { reason = ""; return false; }
        return true;
    }

    void LocalInteraction()
    {
        _target = null;
        string prompt = "";
        float progress = 0f;
        var me = LocalPlayer();
        var cam = Camera.main;
        _cooldown -= Time.deltaTime;
        if (me != null && !me.IsDead && cam != null && Cursor.lockState == CursorLockMode.Locked && !FloorTransition.InCutscene)
        {
            var hits = Physics.SphereCastAll(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f)), 0.12f, 3.2f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(me.transform)) continue;
                var d = h.collider.GetComponentInParent<MissionInteractable>();
                if (d != null) { if (h.distance <= d.maxDistance) _target = d; break; }
                if (!h.collider.isTrigger) break;   // 벽 등에 가려짐
            }
        }

        if (_target != null)
        {
            var s = State;
            if (Available(_target, s, me, out var reason))
            {
                if (InteractHeld() && _cooldown <= 0f)
                {
                    _hold += Time.deltaTime;
                    if (_hold >= _target.holdSeconds)
                    {
                        _hold = 0f; _cooldown = 0.6f;
                        if (_target.kind == MissionInteractable.Kind.IntrusionLog) ShowLog();
                        Request(_target.kind, me);
                    }
                }
                else _hold = 0f;
                progress = _hold / _target.holdSeconds;
                prompt = "[" + GameKeys.Label(GameAction.Interact) + "] 길게 눌러 " + _target.actionLabel;
            }
            else { _hold = 0f; prompt = reason ?? ""; }
        }
        else _hold = 0f;

        if (_prompt != null)
        {
            _prompt.text = prompt;
            _promptBarBg.gameObject.SetActive(progress > 0f);
            _promptBar.rectTransform.sizeDelta = new Vector2(260f * Mathf.Clamp01(progress), 6f);
        }
    }

    // ================================================================== 장치 표시등 / 알림
    static readonly Color Green = new Color(0.25f, 1f, 0.45f), Amber = new Color(1f, 0.62f, 0.12f), Red = new Color(1f, 0.18f, 0.12f), Cyan = new Color(0.3f, 0.85f, 1f);

    void ApplyVisuals(MissionState s, bool force)
    {
        string key = s.lockdownLifted + "|" + s.module + "|" + s.couplerSpot + "|" + s.fuel + "|" + s.fuelPaused + "|" + s.valveBroken + "|" + s.trace + "|" + s.phase;
        if (!force && key == _lastStatusKey) return;
        _lastStatusKey = key;
        if (quarantineConsole != null) { if (s.lockdownLifted) quarantineConsole.SetStatus(Green, 1.2f); else quarantineConsole.SetStatus(Red, 1.6f, 2f); }
        if (powerModule != null)
        {
            if (s.module != 0) powerModule.SetStatus(Red, 0.25f);
            else if (s.lockdownLifted) powerModule.SetStatus(Green, 1.6f, 2.5f);
            else powerModule.SetStatus(Amber, 0.8f);
        }
        if (cargoLift != null)
        {
            if (s.module == 2) cargoLift.SetStatus(Green, 1.4f);
            else if (s.module == 1) cargoLift.SetStatus(Amber, 1.8f, 2.5f);
            else cargoLift.SetStatus(Amber, 0.6f);
        }
        if (fuelPump != null)
        {
            if (s.fuel == 2) fuelPump.SetStatus(Green, 1.6f);
            else if (s.fuel == 1) fuelPump.SetStatus(s.fuelPaused || s.valveBroken ? Red : Amber, 2.2f, s.fuelPaused || s.valveBroken ? 6f : 3.5f);
            else fuelPump.SetStatus(Amber, 1.4f, 2f);
        }
        if (fuelValve != null) { if (s.valveBroken) fuelValve.SetStatus(Red, 3f, 7f); else fuelValve.SetStatus(Green, 0.5f); }
        if (intrusionLog != null) intrusionLog.SetStatus(Cyan, s.trace ? 0.5f : 1.3f, s.trace ? 0f : 2f);

        // 임무가 진행되면 나타나는 물건
        if (s.module >= 1) Reveal(modulePickup);
        if (s.module == 2 && s.couplerSpot > 0 && couplerSpots != null && s.couplerSpot <= couplerSpots.Length) Reveal(couplerSpots[s.couplerSpot - 1]);
        if (s.trace) Reveal(batteryReward);

        if (rallyLights != null)
            foreach (var l in rallyLights)
            {
                if (l == null) continue;
                l.enabled = s.phase == (byte)Phase.Rally || s.phase == (byte)Phase.Cleared;
                l.color = s.phase == (byte)Phase.Cleared ? Green : Amber;
            }
    }

    /// <summary>꺼진 채 놓여 있던 물건을 켠다 (이미 누가 주웠으면 그대로 둔다)</summary>
    static void Reveal(FpsWorldPickup pickup)
    {
        if (pickup != null && !pickup.IsClaimed && !pickup.gameObject.activeSelf) pickup.gameObject.SetActive(true);
    }

    /// <summary>봉쇄 문과 화물 리프트 움직임 (상태만 보고 모든 화면에서 똑같이 움직인다)</summary>
    void AnimateWorld(MissionState s)
    {
        if (lockdownDoors != null)
            foreach (var d in lockdownDoors)
            {
                if (d == null || d.leaf == null) continue;
                float target = s.lockdownLifted ? 1f : 0f;
                if (d.obstacle != null && d.obstacle.enabled == s.lockdownLifted) d.obstacle.enabled = !s.lockdownLifted;
                if (d.interactTrigger != null && d.interactTrigger.enabled == s.lockdownLifted) d.interactTrigger.enabled = !s.lockdownLifted;
                if (Mathf.Approximately(d.t, target)) continue;
                d.t = Mathf.MoveTowards(d.t, target, Time.deltaTime / lockdownDoorSeconds);
                d.leaf.position = d.closedPos + d.openOffset * Mathf.SmoothStep(0, 1, d.t);
                if (d.blocker != null) d.blocker.enabled = d.t < 0.6f;
            }

        if (liftCargo != null && liftCargo.activeSelf != (s.module == 2)) liftCargo.SetActive(s.module == 2);
        if (liftPlatform != null && s.module == 2 && _liftT < 1f)
        {
            _liftT = Mathf.Min(1f, _liftT + Time.deltaTime / liftSeconds);
            liftPlatform.position = _liftTop + Vector3.down * liftTravel * Mathf.SmoothStep(0, 1, _liftT);
            // 다 내려가면 숨긴다 (아래층과 겹쳐 보이지 않도록)
            if (_liftT >= 1f) liftPlatform.gameObject.SetActive(false);
        }
    }

    void PlayAt(MissionInteractable device, AudioClip clip)
    {
        if (device != null && clip != null) GameAudio.PlayAt(clip, device.transform.position, 1f);
    }

    /// <summary>우리 팀 누군가 이 물건을 들고 있는지 (진행 목록 표시용)</summary>
    bool TeamHas(FarmingItemKind kind)
    {
        if (Stage2NetworkBootstrap.IsOnline)
        {
            foreach (var p in FpsNetworkPlayer.All) if (p != null && p.Inventory != null && p.Inventory.HasItem(kind)) return true;
            return false;
        }
        var me = LocalPlayer();
        var inventory = me != null ? me.GetComponent<FpsInventory>() : null;
        return inventory != null && inventory.HasItem(kind);
    }

    void AnnounceChanges(MissionState s)
    {
        if (!_shownInit) { _shownInit = true; _shown = s; if (s.trace) _logShownOnce = true; return; }
        if (!_shown.lockdownLifted && s.lockdownLifted) { Toast("검역 봉쇄 해제 · <b>장비 작업실</b>로 가는 문이 열렸습니다"); PlayAt(quarantineConsole, _clankClip); }
        if (_shown.module == 0 && s.module == 1) { Toast("<color=#6EE7B7>" + s.carrier + "</color> 대원이 전력 모듈을 꺼냈습니다 · <color=#FCA5A5>금속음이 퍼집니다!</color> 중앙 화물창 리프트로 옮기세요"); PlayAt(powerModule, _clankClip); }
        if (_shown.module == 1 && s.module == 2) { Toast("전력 모듈을 1층으로 보냈습니다 · 이제 <b>연료 커플러</b>를 찾으세요 (위치는 판마다 달라요)"); PlayAt(cargoLift, _liftClip); }
        if (_shown.fuel == 0 && s.fuel == 1) Toast("연료 공급 시작 · <color=#FCA5A5>펌프 소음이 퍼집니다!</color> 연료가 찰 때까지 연료 공급실을 지키세요");
        if (_shown.fuel == 1 && s.fuel == 2) Toast("연료 공급 완료");
        if (!_shown.fuelPaused && s.fuelPaused && s.fuel == 1) Toast("<color=#FCA5A5>연료 공급실에 아무도 없어 공급이 멈췄습니다</color>");
        if (!_shown.valveBroken && s.valveBroken)
        {
            Toast("<color=#FCA5A5>연료 밸브 고장!</color> 컨테이너 보관실의 밸브를 고칠 때까지 공급이 멈춥니다");
            PlayAt(fuelPump, _alarmClip); PlayAt(fuelValve, _alarmClip);
        }
        if (_shown.valveBroken && !s.valveBroken && s.fuel == 1) { Toast("연료 밸브 수리 완료 · 공급을 이어 갑니다"); PlayAt(fuelValve, _clankClip); }
        if (!_shown.trace && s.trace)
        {
            if (!_logShownOnce) ShowLog();
            Toast("기록 옆에서 <b>배터리</b>를 찾았습니다");
        }
        if (_shown.phase != s.phase)
        {
            if (s.phase == (byte)Phase.Rally) Toast(FloorTransition.Instance != null ? "탈출 준비 완료 · <b>A 계단 문을 열고 1층으로 내려가세요</b> (문 앞에서 F)" : "탈출 준비 완료 · <b>1층으로 내려가는 계단</b>으로 들어가세요 (문 앞에서 F)");
            if (s.phase == (byte)Phase.Cleared) { Banner("2층 클리어", "1층 보조 우주선 격납고로 내려가세요", Green); FloorCleared?.Invoke(); }
            if (s.phase == (byte)Phase.Failed) Banner("임무 실패", "살아남은 대원이 없습니다", Red);
        }
        _shown = s;
    }

    // ================================================================== HUD
    TMP_Text _objectives, _prompt, _toast, _bannerTitle, _bannerSub, _log;
    Image _promptBar, _promptBarBg, _logPanel;
    CanvasGroup _bannerGroup, _toastGroup;
    float _toastUntil, _bannerUntil, _logUntil;
    bool _logShownOnce;

    void BuildHud()
    {
        var canvasGo = new GameObject("F2 Mission HUD", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = canvasGo.transform;

        // 목표 (왼쪽, HP 막대 아래)
        var card = Img(root, "Objectives", new Color(0.063f, 0.07f, 0.1f, 0.78f));
        Anchor(card.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -112), new Vector2(560, 190));
        var title = Txt(card.transform, "Title", "2층 · 화물 및 검역 구역", 20, new Color(1, 1, 1, 0.9f), TextAlignmentOptions.TopLeft);
        title.fontStyle = FontStyles.Bold;
        Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -12), new Vector2(520, 30));
        _objectives = Txt(card.transform, "List", "", 17, new Color(1, 1, 1, 0.85f), TextAlignmentOptions.TopLeft);
        _objectives.lineSpacing = 12f;
        Anchor(_objectives.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -48), new Vector2(530, 140));

        // 상호작용 안내 (화면 가운데 아래)
        _prompt = Txt(root, "Prompt", "", 22, Color.white, TextAlignmentOptions.Center);
        Anchor(_prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(900, 34));
        _promptBarBg = Img(root, "PromptBarBg", new Color(1, 1, 1, 0.15f));
        Anchor(_promptBarBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -118), new Vector2(260, 6));
        _promptBar = Img(_promptBarBg.transform, "Fill", Amber);
        _promptBar.rectTransform.anchorMin = _promptBar.rectTransform.anchorMax = new Vector2(0, 0.5f);
        _promptBar.rectTransform.pivot = new Vector2(0, 0.5f);
        _promptBar.rectTransform.anchoredPosition = Vector2.zero;
        _promptBarBg.gameObject.SetActive(false);

        // 알림 (위쪽 가운데)
        var toastBg = Img(root, "Toast", new Color(0.05f, 0.06f, 0.08f, 0.82f));
        Anchor(toastBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(1000, 52));
        _toastGroup = toastBg.gameObject.AddComponent<CanvasGroup>(); _toastGroup.alpha = 0;
        _toast = Txt(toastBg.transform, "Text", "", 21, Color.white, TextAlignmentOptions.Center);
        Stretch(_toast.rectTransform);

        // 클리어/실패 배너 (가운데)
        var banner = Img(root, "Banner", new Color(0, 0, 0, 0.6f));
        Anchor(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1920, 190));
        _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>(); _bannerGroup.alpha = 0;
        _bannerTitle = Txt(banner.transform, "Title", "", 64, Green, TextAlignmentOptions.Center);
        _bannerTitle.fontStyle = FontStyles.Bold; _bannerTitle.characterSpacing = 10;
        Anchor(_bannerTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 26), new Vector2(1600, 90));
        _bannerSub = Txt(banner.transform, "Sub", "", 24, new Color(1, 1, 1, 0.8f), TextAlignmentOptions.Center);
        Anchor(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(1600, 40));

        // 승무원 기록
        _logPanel = Img(root, "Log", new Color(0.03f, 0.05f, 0.07f, 0.9f));
        Anchor(_logPanel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(860, 300));
        _log = Txt(_logPanel.transform, "Text", "", 21, new Color(0.8f, 0.92f, 1f), TextAlignmentOptions.TopLeft);
        _log.textWrappingMode = TextWrappingModes.Normal;
        Stretch(_log.rectTransform); _log.rectTransform.offsetMin = new Vector2(32, 24); _log.rectTransform.offsetMax = new Vector2(-32, -24);
        _logPanel.gameObject.SetActive(false);
    }

    void UpdateHud(MissionState s)
    {
        if (_objectives == null) return;
        // 앞 단계를 끝내야 다음 단계가 목록에 나타난다
        var sb = new System.Text.StringBuilder();
        int lines = 0;
        void Line(bool done, string label)
        {
            if (lines > 0) sb.Append('\n');
            sb.Append(done ? "<color=#6EE7B7>■</color> <color=#8A96A3>" + label + "</color>" : "<color=#FCD34D>□</color> " + label);
            lines++;
        }
        bool cutterDone = s.module != 0 || TeamHas(FarmingItemKind.Cutter);
        bool couplerDone = s.fuel != 0 || TeamHas(FarmingItemKind.FuelCoupler);
        Line(s.lockdownLifted, TeamHas(FarmingItemKind.QuarantineCard) || s.lockdownLifted ? "검역 관제실에서 검역 봉쇄 해제" : "화물 분류실에서 검역 카드 찾기");
        if (s.lockdownLifted) Line(cutterDone, "장비 작업실에서 절단기 확보");
        if (cutterDone) Line(s.module != 0, "정비 보관실에서 전력 모듈 꺼내기");
        if (s.module != 0) Line(s.module == 2, "중앙 화물창 리프트로 전력 모듈 내려보내기");
        if (s.module == 2) Line(couplerDone, "연료 커플러 찾기  <color=#9FB3C2>(위치는 판마다 달라요)</color>");
        if (couplerDone)
        {
            if (s.fuel == 0) Line(false, "연료 공급실에 커플러를 끼우고 충전 시작");
            else if (s.fuel == 1)
            {
                string note = s.valveBroken ? "  <color=#FCA5A5>컨테이너 보관실 밸브 수리 필요</color>"
                    : s.fuelPaused ? "  <color=#FCA5A5>연료 공급실에 대원 필요</color>"
                    : "  <color=#FCA5A5>소음 주의</color>";
                Line(false, "연료 충전 중  <b>" + Mathf.RoundToInt(s.fuelProgress * 100) + "%</b>" + note);
            }
            else Line(true, "연료 충전 완료");
        }
        if (lines > 0) sb.Append('\n');
        sb.Append(s.trace ? "<color=#6EE7B7>■</color> <color=#8A96A3>" : "<color=#7FA7B8>□</color> <color=#9FB3C2>").Append("(선택) 검역 보관실의 기록 조사</color>");
        lines++;
        if (s.phase == (byte)Phase.Rally)
        {
            sb.Append(FloorTransition.Instance != null ? "\n<color=#FCD34D>▶</color> <b>A 계단 문을 열고 1층으로 내려가기</b>" : "\n<color=#FCD34D>▶</color> <b>1층으로 내려가는 계단으로 이동</b>  " + s.rallyIn + " / " + s.rallyNeed + "명");
            lines++;
        }
        else if (s.phase == (byte)Phase.Cleared) { sb.Append("\n<color=#6EE7B7>■ 2층 클리어</color>"); lines++; }
        else if (s.phase == (byte)Phase.Failed) { sb.Append("\n<color=#FCA5A5>■ 임무 실패</color>"); lines++; }
        _objectives.text = sb.ToString();
        var size = _objectives.rectTransform.parent as RectTransform;
        if (size != null) size.sizeDelta = new Vector2(560, 58 + lines * 32);

        float t = Time.unscaledTime;
        _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, t < _toastUntil ? 1 : 0, Time.unscaledDeltaTime * 3f);
        _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, t < _bannerUntil ? 1 : 0, Time.unscaledDeltaTime * 1.5f);
        if (_logPanel.gameObject.activeSelf && t > _logUntil) _logPanel.gameObject.SetActive(false);
    }

    void Toast(string message) { if (_toast == null) return; _toast.text = message; _toastUntil = Time.unscaledTime + 6f; }

    void Banner(string title, string sub, Color color)
    {
        if (_bannerTitle == null) return;
        _bannerTitle.text = title; _bannerTitle.color = color; _bannerSub.text = sub;
        _bannerUntil = Time.unscaledTime + 7f;
    }

    void ShowLog()
    {
        _logShownOnce = true;
        if (_log == null) return;
        _log.text = intrusionLogText;
        _logPanel.gameObject.SetActive(true);
        _logUntil = Time.unscaledTime + 12f;
    }

    Image Img(Transform parent, string name, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
        return img;
    }

    TMP_Text Txt(Transform parent, string name, string text, float size, Color c, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow; t.richText = true;
        return t;
    }

    static void Anchor(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    // ================================================================== 소리 (코드로 만든 효과음)
    /// <summary>금속음: 짧고 높은 쇳소리 몇 개가 겹친 소리</summary>
    static AudioClip MakeClank()
    {
        const int rate = 44100; const float seconds = 0.9f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(23);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float ring = Mathf.Sin(2 * Mathf.PI * 1180f * t) * 0.35f + Mathf.Sin(2 * Mathf.PI * 1730f * t) * 0.25f + Mathf.Sin(2 * Mathf.PI * 410f * t) * 0.4f;
            float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 60f) * 0.5f;
            data[i] = (ring * Mathf.Exp(-t * 7f) + noise) * 0.7f;
        }
        var clip = AudioClip.Create("F2 Clank", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>경보음: 두 음이 번갈아 울리는 소리</summary>
    static AudioClip MakeAlarm()
    {
        const int rate = 44100; const float seconds = 2.4f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float f = Mathf.Repeat(t, 0.6f) < 0.3f ? 620f : 480f;
            data[i] = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t)) * 0.28f * Mathf.Clamp01((seconds - t) * 6f);
        }
        var clip = AudioClip.Create("F2 Alarm", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>리프트 소리: 낮게 웅웅거리다 끝에 쿵</summary>
    static AudioClip MakeLiftHum()
    {
        const int rate = 44100; const float seconds = 6f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float hum = (Mathf.Sin(2 * Mathf.PI * 62f * t) + Mathf.Sin(2 * Mathf.PI * 93f * t) * 0.5f) * Mathf.Clamp01(t * 3f) * Mathf.Clamp01((5.6f - t) * 3f) * 0.4f;
            float thudT = t - 5.5f;
            float thud = thudT > 0 ? Mathf.Sin(2 * Mathf.PI * 50f * thudT) * Mathf.Exp(-thudT * 14f) * 0.8f : 0;
            data[i] = hum + thud;
        }
        var clip = AudioClip.Create("F2 Lift", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f); Gizmos.DrawWireCube(fuelRoom.center, fuelRoom.size);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);
        if (rallyZones != null) foreach (var z in rallyZones) Gizmos.DrawWireCube(z.center, z.size);
    }
}
