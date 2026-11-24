using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Monster;
using VoidLast.Player;

/// <summary>
/// 4층(동력 및 정비 구역) 임무 진행과 클리어 판정.
///
/// 클리어 조건
///  준비 (순서 없이 나눠서 할 수 있다)
///   · 정비 작업장(SE)에서 수리 공구를 챙긴다 (발전기가 고장 났을 때 필요).
///   · 전력 셀 보관실(NE)의 전력 셀 3개를 보조 발전기실(C)의 발전기에 끼운다.
///     전력 셀은 무거워서 한 대원이 하나만 들 수 있고, 드는 동안 달리기·사격을 할 수 없다 (FpsInventory, FpsInput).
///   · 냉각 설비실(SW)과 배관 구역(S)의 냉각 밸브 2개를 연다.
///  1. 발전기실 콘솔에서 발전기를 가동해 90초 지킨다. 살아 있는 대원이 발전기실 안에 있어야 진행되고, 비우면 멈춘다.
///     중간에 한 번 고장 나면 공구를 가진 대원이 발전기를 고쳐야 이어진다. 15초마다 소음이 쪼꼬미를 부른다.
///  2. 배전실(NW)과 전력 제어실(N)의 스위치를 10초 안에 둘 다 올린다 (늦으면 다시 내려감). 살아 있는 대원이 한 명뿐이면 시간 제한이 없다.
///  3. A 계단으로 내려간다 (StairDoorController, FloorTransition).
///  살아 있는 대원이 한 명도 없으면 임무 실패.
///
/// 멀티플레이: 판정은 방장(서버)만 하고 상태는 모든 대원에게 동기화된다. 오프라인(씬을 바로 실행)에서도 같은 규칙으로 진행한다.
/// 장치·물건·몬스터 배치는 메뉴 Tools/VOID LAST/Multiplayer/6e Build F4 Mission (F4MissionSetup).
/// </summary>
[DisallowMultipleComponent]
public sealed class F4MissionDirector : NetworkBehaviour, IFloorMission
{
    public enum Phase : byte { Objectives = 0, Exit = 1, Failed = 2 }

    public struct MissionState : INetworkSerializable, IEquatable<MissionState>
    {
        public byte phase;
        public byte cells;             // 발전기에 끼운 전력 셀 수
        public byte valves;            // 연 냉각 밸브 (비트)
        public byte generator;         // 0 대기, 1 가동 중, 2 완료
        public float genProgress;      // 0~1
        public bool genPaused;         // 발전기실에 아무도 없어 멈춤
        public bool genBroken;         // 고장 (공구로 수리할 때까지 멈춤)
        public byte switches;          // 올린 스위치 (비트)
        public byte switchSeconds;     // 한쪽만 올렸을 때 남은 시간(초). 0 = 시간 제한 없음

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref phase);
            s.SerializeValue(ref cells);
            s.SerializeValue(ref valves);
            s.SerializeValue(ref generator);
            s.SerializeValue(ref genProgress);
            s.SerializeValue(ref genPaused);
            s.SerializeValue(ref genBroken);
            s.SerializeValue(ref switches);
            s.SerializeValue(ref switchSeconds);
        }

        public bool Equals(MissionState o) =>
            phase == o.phase && cells == o.cells && valves == o.valves && generator == o.generator && Mathf.Approximately(genProgress, o.genProgress) &&
            genPaused == o.genPaused && genBroken == o.genBroken && switches == o.switches && switchSeconds == o.switchSeconds;
    }

    [Header("장치")]
    [Tooltip("보조 발전기: 전력 셀을 끼우는 곳이자, 고장 나면 공구로 고치는 곳")]
    public MissionInteractable generator;
    public MissionInteractable generatorStart;
    [Tooltip("냉각 밸브 (MissionInteractable.index = 배열 순서)")]
    public MissionInteractable[] valves;
    [Tooltip("전력 스위치 (MissionInteractable.index = 배열 순서)")]
    public MissionInteractable[] switches;

    [Header("구역 (월드 좌표)")]
    [Tooltip("발전기를 돌리는 동안 대원이 머물러야 하는 보조 발전기실")]
    public Bounds generatorRoom = new Bounds(new Vector3(7f, 26.2f, 5f), new Vector3(27f, 5f, 19f));
    [Tooltip("임무를 끝내면 켜지는 A 계단 입구 안내등")]
    public Light[] exitLights;

    [Header("규칙")]
    [Min(1)] public int cellsNeeded = 3;
    [Min(5f)] public float generatorSeconds = 90f;
    [Tooltip("가동이 이 비율 사이 어딘가에 이르면 한 번 고장 난다")]
    public Vector2 breakRange = new Vector2(0.35f, 0.65f);
    [Min(3f)] public float noiseInterval = 15f;
    [Tooltip("발전기 소음이 쪼꼬미에게 들리는 반경(m)")]
    [Min(5f)] public float noiseRadius = 100f;
    [Tooltip("스위치 한쪽을 올린 뒤 다른 쪽을 올려야 하는 시간(초). 대원이 한 명뿐이면 적용하지 않는다")]
    [Min(1f)] public float switchWindow = 10f;

    [Header("UI")]
    public TMP_FontAsset font;

    public static F4MissionDirector Instance { get; private set; }

    readonly NetworkVariable<MissionState> _net = new NetworkVariable<MissionState>();
    readonly MissionLeaverDrops _leaverDrops = new MissionLeaverDrops();
    readonly List<FpsHealth> _players = new List<FpsHealth>();
    MissionState _offline, _shown;
    bool _shownInit, _brokeOnce;
    float _genAccum, _breakAt, _nextNoise, _switchTimer, _cooldown, _hold;
    string _lastVisualKey;
    MissionHud _hud;
    AudioClip _clankClip, _alarmClip;

    public MissionState State => IsSpawned ? _net.Value : _offline;
    /// <summary>판정을 맡는 쪽인지 (방장 또는 오프라인)</summary>
    bool Judges => !IsSpawned || IsServer;
    int ValveMask => (1 << (valves != null ? valves.Length : 0)) - 1;
    int SwitchMask => (1 << (switches != null ? switches.Length : 0)) - 1;
    static bool All(byte value, int mask) => mask != 0 && (value & mask) == mask;
    bool Fueled(MissionState s) => s.cells >= cellsNeeded && All(s.valves, ValveMask);

    public bool ExitUnlocked => State.phase == (byte)Phase.Exit;
    public bool IsInLockedArea(Vector3 point) => false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    void Awake()
    {
        Instance = this;
        // 밸런스 설정: 발전기 시간 × 인원 배율, 소음 반경·간격, 스위치 시간 제한 (GameBalance)
        generatorSeconds *= GameBalance.HoldoutScale;
        noiseRadius = GameBalance.Current.holdoutNoiseRadius;
        switchWindow = GameBalance.SyncWindow;
        FloorMission.Register(this);
        _clankClip = F2MissionDirector.MakeClank();
        _alarmClip = F2MissionDirector.MakeAlarm();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        FloorMission.Unregister(this);
        base.OnDestroy();
    }

    void Start()
    {
        _hud = new MissionHud(transform, font, "4층 · 동력 및 정비 구역");
        ApplyVisuals(State, true);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback += _leaverDrops.OnClientLeft;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= _leaverDrops.OnClientLeft;
    }

    void SetState(MissionState s)
    {
        if (IsSpawned) { if (IsServer && !_net.Value.Equals(s)) _net.Value = s; }
        else _offline = s;
    }

    void Update()
    {
        if (Judges) Simulate(Time.deltaTime);
        var s = State;
        ApplyVisuals(s, false);
        AnnounceChanges(s);
        LocalInteraction(s);
        UpdateHud(s);
    }

    // ================================================================== 방장(또는 오프라인) 판정
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
        if (s.phase == (byte)Phase.Failed) return;
        CollectPlayers();
        int alive = 0;
        foreach (var p in _players) if (Alive(p)) alive++;

        // 실패: 대원이 있었는데 모두 사망
        if (_players.Count > 0 && alive == 0) { s.phase = (byte)Phase.Failed; SetState(s); return; }

        if (IsSpawned) _leaverDrops.Tick();

        if (s.phase == (byte)Phase.Objectives)
        {
            if (s.generator == 1)
            {
                bool guarded = false;
                foreach (var p in _players) if (Alive(p) && generatorRoom.Contains(p.transform.position + Vector3.up)) { guarded = true; break; }
                s.genPaused = !guarded;
                if (_genAccum < s.genProgress) _genAccum = s.genProgress;   // 방장이 바뀌는 등으로 값이 어긋났을 때 보정
                // 가동 중간에 한 번 고장 난다: 공구로 고칠 때까지 멈춘다
                if (!_brokeOnce && _genAccum >= _breakAt) { _brokeOnce = true; s.genBroken = true; }
                if (guarded && !s.genBroken) _genAccum = Mathf.Min(1f, _genAccum + dt / generatorSeconds);
                s.genProgress = _genAccum >= 1f ? 1f : Mathf.Floor(_genAccum * 200f) / 200f;   // 화면/네트워크용 값은 0.5% 단위
                if (Time.time >= _nextNoise) { _nextNoise = Time.time + GameBalance.NextHoldoutNoise((1f - _genAccum) * generatorSeconds); MissionNoise(generator); }
                if (_genAccum >= 1f) { s.generator = 2; s.genPaused = false; }
            }

            // 스위치: 한쪽만 올린 상태로 시간이 지나면 다시 내려간다 (대원이 한 명뿐이면 시간 제한 없음)
            int mask = SwitchMask;
            if (s.switches != 0 && !All(s.switches, mask))
            {
                if (alive >= 2)
                {
                    _switchTimer -= dt;
                    if (_switchTimer <= 0f) { s.switches = 0; s.switchSeconds = 0; }
                    else s.switchSeconds = (byte)Mathf.Clamp(Mathf.CeilToInt(_switchTimer), 1, 255);
                }
                else { _switchTimer = switchWindow; s.switchSeconds = 0; }
            }
            else s.switchSeconds = 0;

            if (s.generator == 2 && All(s.switches, mask)) s.phase = (byte)Phase.Exit;
        }
        SetState(s);
    }

    /// <summary>임무 소음: 쪼꼬미가 듣고 소리 난 곳으로 조사하러 온다</summary>
    void MissionNoise(MissionInteractable source)
    {
        if (source == null) return;
        Vector3 at = source.transform.position;
        foreach (var m in FindObjectsByType<SmallMonsterAI>(FindObjectsSortMode.None))
        {
            Vector3 d = m.transform.position - at;
            if (Mathf.Abs(d.y) > 3.5f || d.sqrMagnitude > noiseRadius * noiseRadius) continue;
            m.HearNoise(at);
        }
    }

    // ================================================================== 상호작용 요청
    void Request(MissionInteractable device, FpsHealth who)
    {
        if (Judges) TryApply(device.kind, device.index, who != null ? who.transform.position : Vector3.zero, who != null, who != null ? who.GetComponent<FpsInventory>() : null, IsSpawned ? NetworkManager.LocalClientId : 0);
        else InteractRpc((byte)device.kind, (byte)device.index);
    }

    [Rpc(SendTo.Server)]
    void InteractRpc(byte kind, byte index, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        var player = FpsNetworkPlayer.FindByClient(sender);
        if (player == null || player.Health == null || player.Health.IsDead) return;
        TryApply((MissionInteractable.Kind)kind, index, player.transform.position, true, player.Inventory, sender);
    }

    MissionInteractable Device(MissionInteractable.Kind kind, int index)
    {
        switch (kind)
        {
            case MissionInteractable.Kind.Generator: return generator;
            case MissionInteractable.Kind.GeneratorStart: return generatorStart;
            case MissionInteractable.Kind.CoolantValve: return valves != null && index >= 0 && index < valves.Length ? valves[index] : null;
            case MissionInteractable.Kind.PowerSwitch: return switches != null && index >= 0 && index < switches.Length ? switches[index] : null;
            default: return null;
        }
    }

    void TryApply(MissionInteractable.Kind kind, int index, Vector3 from, bool checkDistance, FpsInventory inventory, ulong clientId)
    {
        var device = Device(kind, index);
        if (device == null) return;
        if (checkDistance)
        {
            var box = device.GetComponent<Collider>();
            Vector3 point = box != null ? box.ClosestPoint(from) : device.transform.position;
            if (Vector3.Distance(from, point) > device.maxDistance + 2.5f) return;
        }
        var s = State;
        if (s.phase != (byte)Phase.Objectives) return;
        bool usedCell = false;
        switch (kind)
        {
            case MissionInteractable.Kind.Generator:
                if (s.genBroken)
                {
                    // 고장 수리: 공구가 있어야 한다 (공구는 사라지지 않는다)
                    if (inventory == null || !inventory.HasItem(FarmingItemKind.ToolKit)) return;
                    s.genBroken = false;
                }
                else
                {
                    if (s.cells >= cellsNeeded || inventory == null || !inventory.HasItem(FarmingItemKind.PowerCell)) return;
                    s.cells++;
                    usedCell = true;
                }
                break;
            case MissionInteractable.Kind.CoolantValve:
                if ((s.valves & (1 << index)) != 0) return;
                s.valves |= (byte)(1 << index);
                break;
            case MissionInteractable.Kind.GeneratorStart:
                if (s.generator != 0 || !Fueled(s)) return;
                s.generator = 1; s.genProgress = 0f; s.genPaused = false; s.genBroken = false; _genAccum = 0f; _brokeOnce = false;
                _breakAt = UnityEngine.Random.Range(breakRange.x, breakRange.y);
                _nextNoise = Time.time + GameBalance.NextHoldoutNoise(generatorSeconds);
                MissionNoise(generator);
                break;
            case MissionInteractable.Kind.PowerSwitch:
                if (s.generator != 2 || (s.switches & (1 << index)) != 0) return;
                if (s.switches == 0) _switchTimer = switchWindow;   // 첫 번째를 올린 순간부터 시간을 잰다
                s.switches |= (byte)(1 << index);
                break;
            default:
                return;
        }
        SetState(s);
        if (usedCell) ConsumeItem(inventory, FarmingItemKind.PowerCell, clientId);
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

    // ================================================================== 내 화면: F 길게 누르기
    static FpsHealth LocalPlayer()
    {
        if (Stage2NetworkBootstrap.IsOnline) return FpsNetworkPlayer.Local != null ? FpsNetworkPlayer.Local.Health : null;
        var cam = Camera.main;
        return cam != null ? cam.GetComponentInParent<FpsHealth>() : null;
    }

    const string Need = "<color=#FCA5A5>";

    /// <summary>지금 이 장치를 쓸 수 있는지. 쓸 수 있으면 label에 할 일, 못 쓰면 reason에 이유(화면 안내)</summary>
    bool Available(MissionInteractable d, MissionState s, FpsHealth me, out string label, out string reason)
    {
        reason = null; label = d.actionLabel;
        var inventory = me != null ? me.GetComponent<FpsInventory>() : null;
        bool Has(FarmingItemKind k) => inventory != null && inventory.HasItem(k);
        bool objectives = s.phase == (byte)Phase.Objectives;
        switch (d.kind)
        {
            case MissionInteractable.Kind.Generator:
                if (s.genBroken)
                {
                    if (!Has(FarmingItemKind.ToolKit)) { reason = Need + "발전기 고장!</color> 수리 공구가 필요해요 · 정비 작업장에 있어요"; return false; }
                    label = "공구로 발전기 수리";
                    return objectives;
                }
                if (s.generator == 1) { reason = "발전기 가동 중  " + Mathf.RoundToInt(s.genProgress * 100) + "%"; return false; }
                if (s.generator == 2) { reason = "발전기 가동 완료"; return false; }
                if (s.cells >= cellsNeeded) { reason = "전력 셀 " + cellsNeeded + "개 장착 완료 · 콘솔에서 가동하세요"; return false; }
                if (!Has(FarmingItemKind.PowerCell)) { reason = Need + "전력 셀이 필요해요</color>  (" + s.cells + " / " + cellsNeeded + ") · 전력 셀 보관실에 있어요"; return false; }
                label = "전력 셀 끼우기";
                return objectives;
            case MissionInteractable.Kind.CoolantValve:
                if ((s.valves & (1 << d.index)) != 0) { reason = "냉각 밸브 열림"; return false; }
                return objectives;
            case MissionInteractable.Kind.GeneratorStart:
                if (s.generator == 1) { reason = "발전기 가동 중  " + Mathf.RoundToInt(s.genProgress * 100) + "%"; return false; }
                if (s.generator == 2) { reason = "발전기 가동 완료 · 배전실과 전력 제어실의 스위치를 올리세요"; return false; }
                if (s.cells < cellsNeeded) { reason = Need + "전력 셀이 모자라요</color>  (" + s.cells + " / " + cellsNeeded + ")"; return false; }
                if (!All(s.valves, ValveMask)) { reason = Need + "냉각 밸브를 먼저 여세요</color> · 냉각 설비실과 배관 구역"; return false; }
                return objectives;
            case MissionInteractable.Kind.PowerSwitch:
                if ((s.switches & (1 << d.index)) != 0) { reason = All(s.switches, SwitchMask) ? "전력 연결 완료" : "스위치 올림 · " + (s.switchSeconds > 0 ? "<b>" + s.switchSeconds + "초</b> 안에 다른 쪽도 올려야 해요" : "다른 쪽도 올려야 해요"); return false; }
                if (s.generator != 2) { reason = Need + "전력이 없어요</color> · 먼저 보조 발전기를 가동하세요"; return false; }
                return objectives;
        }
        reason = "";
        return false;
    }

    void LocalInteraction(MissionState s)
    {
        MissionInteractable target = null;
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
                if (d != null) { if (h.distance <= d.maxDistance) target = d; break; }
                if (!h.collider.isTrigger) break;   // 벽 등에 가려짐
            }
        }

        if (target == null) _hold = 0f;
        else if (!Available(target, s, me, out var label, out var reason)) { _hold = 0f; prompt = reason ?? ""; }
        else
        {
            if (GameKeys.Held(GameAction.Interact) && _cooldown <= 0f)
            {
                _hold += Time.deltaTime;
                if (_hold >= target.holdSeconds) { _hold = 0f; _cooldown = 0.6f; Request(target, me); }
            }
            else _hold = 0f;
            progress = _hold / target.holdSeconds;
            prompt = "[" + GameKeys.Label(GameAction.Interact) + "] 길게 눌러 " + label;
        }
        if (_hud != null) _hud.SetPrompt(prompt, progress);
    }

    // ================================================================== 장치 표시등 / 알림
    void ApplyVisuals(MissionState s, bool force)
    {
        string key = s.cells + "|" + s.valves + "|" + s.generator + "|" + s.genPaused + "|" + s.genBroken + "|" + s.switches + "|" + s.phase;
        if (!force && key == _lastVisualKey) return;
        _lastVisualKey = key;
        bool halted = s.genPaused || s.genBroken;
        if (generator != null)
        {
            if (s.generator == 2) generator.SetStatus(MissionHud.Green, 1.6f);
            else if (s.generator == 1) generator.SetStatus(halted ? MissionHud.Red : MissionHud.Amber, 2.4f, halted ? 6f : 3.5f);
            else if (s.cells >= cellsNeeded) generator.SetStatus(MissionHud.Green, 1f);
            else generator.SetStatus(MissionHud.Amber, 1.4f, 2f);
        }
        if (generatorStart != null)
        {
            if (s.generator == 2) generatorStart.SetStatus(MissionHud.Green, 1.4f);
            else if (s.generator == 1) generatorStart.SetStatus(halted ? MissionHud.Red : MissionHud.Amber, 2f, halted ? 6f : 3.5f);
            else if (Fueled(s)) generatorStart.SetStatus(MissionHud.Amber, 1.8f, 2.5f);
            else generatorStart.SetStatus(MissionHud.Red, 0.5f);
        }
        if (valves != null)
            for (int i = 0; i < valves.Length; i++)
            {
                if (valves[i] == null) continue;
                if ((s.valves & (1 << i)) != 0) valves[i].SetStatus(MissionHud.Green, 1.2f); else valves[i].SetStatus(MissionHud.Amber, 1.6f, 2f);
            }
        if (switches != null)
            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i] == null) continue;
                if (All(s.switches, SwitchMask)) switches[i].SetStatus(MissionHud.Green, 1.4f);
                else if ((s.switches & (1 << i)) != 0) switches[i].SetStatus(MissionHud.Amber, 2.2f, 6f);
                else if (s.generator == 2) switches[i].SetStatus(MissionHud.Amber, 1.8f, 2.5f);
                else switches[i].SetStatus(MissionHud.Red, 0.5f);
            }
        if (exitLights != null)
            foreach (var l in exitLights)
            {
                if (l == null) continue;
                l.enabled = s.phase == (byte)Phase.Exit;
                l.color = MissionHud.Amber;
            }
    }

    void PlayAt(MissionInteractable device, AudioClip clip)
    {
        if (device != null && clip != null) GameAudio.PlayAt(clip, device.transform.position, 1f);
    }

    /// <summary>우리 팀 누군가 이 물건을 들고 있는지 (목표 목록 표시용)</summary>
    static bool TeamHas(FarmingItemKind kind)
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
        if (_hud == null) return;
        if (!_shownInit) { _shownInit = true; _shown = s; return; }
        if (s.cells > _shown.cells) { _hud.Toast("전력 셀 장착  " + s.cells + " / " + cellsNeeded); PlayAt(generator, _clankClip); }
        if (s.valves != _shown.valves)
        {
            _hud.Toast("냉각 밸브 열림  " + Count(s.valves) + " / " + (valves != null ? valves.Length : 0));
            if (valves != null) for (int i = 0; i < valves.Length; i++) if (((s.valves ^ _shown.valves) & (1 << i)) != 0) PlayAt(valves[i], _clankClip);
        }
        if (_shown.generator == 0 && s.generator == 1) _hud.Toast("발전기 가동 · <color=#FCA5A5>소음이 퍼집니다!</color> 끝날 때까지 발전기실을 지키세요");
        if (!_shown.genPaused && s.genPaused && s.generator == 1) _hud.Toast("<color=#FCA5A5>발전기실에 아무도 없어 발전기가 멈췄습니다</color>");
        if (!_shown.genBroken && s.genBroken) { _hud.Toast("<color=#FCA5A5>발전기 고장!</color> 공구를 가진 대원이 발전기를 고칠 때까지 멈춥니다"); PlayAt(generator, _alarmClip); }
        if (_shown.genBroken && !s.genBroken && s.generator == 1) { _hud.Toast("발전기 수리 완료 · 가동을 이어 갑니다"); PlayAt(generator, _clankClip); }
        if (_shown.generator == 1 && s.generator == 2) _hud.Toast("발전기 가동 완료 · <b>배전실</b>과 <b>전력 제어실</b>의 스위치를 올리세요");
        if (s.switches != _shown.switches)
        {
            if (All(s.switches, SwitchMask)) { }   // 아래 Exit 알림으로 대신한다
            else if (s.switches == 0) _hud.Toast("<color=#FCA5A5>시간 초과 · 스위치가 다시 내려갔습니다</color> 두 곳을 거의 동시에 올려야 해요");
            else _hud.Toast("스위치 한쪽 올림 · <color=#FCA5A5>" + (s.switchSeconds > 0 ? Mathf.RoundToInt(switchWindow) + "초 안에" : "이어서") + " 다른 쪽도 올리세요!</color>");
            if (switches != null) for (int i = 0; i < switches.Length; i++) if (((s.switches ^ _shown.switches) & (1 << i)) != 0) PlayAt(switches[i], _clankClip);
        }
        if (_shown.phase != s.phase)
        {
            if (s.phase == (byte)Phase.Exit) _hud.Toast("전력 연결 완료 · <b>A 계단 안으로 들어가 3층으로 내려가세요</b> (계단 반쯤에서 " + GameKeys.Label(GameAction.Interact) + ")");
            if (s.phase == (byte)Phase.Failed) _hud.Banner("임무 실패", "살아남은 대원이 없습니다", MissionHud.Red);
        }
        _shown = s;
    }

    // ================================================================== 목표 목록
    void UpdateHud(MissionState s)
    {
        if (_hud == null) return;
        int valveCount = valves != null ? valves.Length : 0, switchCount = switches != null ? switches.Length : 0;
        _hud.BeginObjectives();
        _hud.Line(s.cells >= cellsNeeded, "전력 셀을 보조 발전기에 끼우기  <b>" + s.cells + " / " + cellsNeeded + "</b>" + (s.cells >= cellsNeeded ? "" : "  <color=#9FB3C2>(한 번에 1개, 드는 동안 달리기·사격 불가)</color>"));
        _hud.Line(All(s.valves, ValveMask), "냉각 밸브 열기  <b>" + Count(s.valves) + " / " + valveCount + "</b>" + (All(s.valves, ValveMask) ? "" : "  <color=#9FB3C2>(냉각 설비실 · 배관 구역)</color>"));
        if (s.generator != 2) _hud.Line(TeamHas(FarmingItemKind.ToolKit), "정비 작업장에서 수리 공구 챙기기");
        if (Fueled(s))
        {
            if (s.generator == 0) _hud.Line(false, "발전기실 콘솔에서 발전기 가동");
            else if (s.generator == 1)
            {
                string note = s.genBroken ? "  <color=#FCA5A5>고장 · 공구로 수리 필요</color>"
                    : s.genPaused ? "  <color=#FCA5A5>발전기실에 대원 필요</color>"
                    : "  <color=#FCA5A5>소음 주의</color>";
                _hud.Line(false, "발전기 가동 중  <b>" + Mathf.RoundToInt(s.genProgress * 100) + "%</b>" + note);
            }
            else _hud.Line(true, "발전기 가동 완료");
        }
        if (s.generator == 2)
        {
            string note = All(s.switches, SwitchMask) ? "" : s.switchSeconds > 0 ? "  <color=#FCA5A5>남은 시간 " + s.switchSeconds + "초</color>" : "  <color=#9FB3C2>(배전실 · 전력 제어실, 10초 안에 둘 다)</color>";
            _hud.Line(All(s.switches, SwitchMask), "전력 스위치 올리기  <b>" + Count(s.switches) + " / " + switchCount + "</b>" + note);
        }
        if (s.phase == (byte)Phase.Exit) _hud.Raw("<color=#FCD34D>▶</color> <b>A 계단 안에서 3층으로 내려가기</b>");
        else if (s.phase == (byte)Phase.Failed) _hud.Raw("<color=#FCA5A5>■ 임무 실패</color>");
        _hud.EndObjectives();
        _hud.Tick();
    }

    static int Count(byte mask)
    {
        int n = 0;
        for (int m = mask; m != 0; m >>= 1) n += m & 1;
        return n;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);
        Gizmos.DrawWireCube(generatorRoom.center, generatorRoom.size);
    }
}
