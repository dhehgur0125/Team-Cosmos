using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Monster;
using VoidLast.Player;

/// <summary>
/// 1층(보조 우주선 격납고) 임무 진행과 탈출 판정. 게임의 마지막 층.
///
/// 클리어 조건
///  준비 (세 가지를 순서 없이 나눠서 할 수 있다)
///   · 연료 접속실(SE)의 전력 모듈과 정비 작업실(NW)의 공구를 챙겨, 보조 우주선(E)에 전력 모듈을 설치한다 (둘 다 가진 대원이 설치).
///   · 연료 접속실에서 연료 라인을 연결한다.
///   · 화물 적재실(SW)과 지상 정비 구역(S)의 고정 장치 2개를 푼다. 한쪽을 풀고 10초 안에 다른 쪽도 풀어야 한다 (늦으면 다시 잠김).
///     살아 있는 대원이 한 명뿐이면 시간 제한이 없다.
///  1. 준비가 끝나면 발사 관제실(NE)에서 격납고 문을 연다. 경보가 울려 소리를 듣는 몬스터가 모두 몰려온다
///     (격납 베이 통로에 갇혀 있던 촉수형·눈 없는 대형도 풀려난다).
///  2. 보조 우주선에서 점화한다. 3분 동안 한 명이라도 살아 있으면 시간이 흐르고, 30초마다 엔진 소음이 몬스터를 부른다.
///  3. 3분이 지나면 발사 관제실 북쪽 통로로 격납 베이에 내려가 탈출선에 탄다. 살아 있는 대원이 모두 타면 탈출 (고치에 남은 대원은 두고 떠난다).
///  살아 있는 대원이 한 명도 없으면 임무 실패.
///
/// 멀티플레이: 판정은 방장(서버)만 하고 상태는 모든 대원에게 동기화된다. 오프라인(씬을 바로 실행)에서도 같은 규칙으로 진행한다.
/// 장치·문·물건·몬스터 배치는 메뉴 Tools/VOID LAST/Multiplayer/6d Build F1 Mission (F1MissionSetup).
/// </summary>
[DisallowMultipleComponent]
public sealed class F1MissionDirector : NetworkBehaviour, IFloorMission
{
    public enum Phase : byte { Prepare = 0, Countdown = 1, Board = 2, Escaped = 3, Failed = 4 }

    public struct MissionState : INetworkSerializable, IEquatable<MissionState>
    {
        public byte phase;
        public bool moduleInstalled;
        public bool fuelLinked;
        public byte clamps;          // 풀린 고정 장치 (비트)
        public byte clampSeconds;    // 한쪽만 풀렸을 때 남은 시간(초). 0 = 시간 제한 없음
        public bool hangarOpen;
        public ushort countdown;     // 발사 준비 남은 시간(초)
        public byte boardIn, boardNeed;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref phase);
            s.SerializeValue(ref moduleInstalled);
            s.SerializeValue(ref fuelLinked);
            s.SerializeValue(ref clamps);
            s.SerializeValue(ref clampSeconds);
            s.SerializeValue(ref hangarOpen);
            s.SerializeValue(ref countdown);
            s.SerializeValue(ref boardIn);
            s.SerializeValue(ref boardNeed);
        }

        public bool Equals(MissionState o) =>
            phase == o.phase && moduleInstalled == o.moduleInstalled && fuelLinked == o.fuelLinked && clamps == o.clamps && clampSeconds == o.clampSeconds &&
            hangarOpen == o.hangarOpen && countdown == o.countdown && boardIn == o.boardIn && boardNeed == o.boardNeed;
    }

    [Header("장치")]
    public MissionInteractable moduleSocket;
    public MissionInteractable fuelLine;
    [Tooltip("고정 장치 (MissionInteractable.index = 배열 순서)")]
    public MissionInteractable[] clamps;
    public MissionInteractable hangarConsole;
    public MissionInteractable ignitionConsole;

    [Header("격납고 문 (열리면 위로 올라간다)")]
    public F2MissionDirector.LockdownDoor[] hangarDoors;
    [Min(0.2f)] public float doorSeconds = 2.2f;

    [Header("구역 (월드 좌표)")]
    [Tooltip("탈출선 탑승 구역 (탑승 발판 끝 ~ 선내). 살아 있는 대원이 모두 들어오면 탈출")]
    public Bounds boardZone = new Bounds(new Vector3(3.5f, -21.5f, -141.5f), new Vector3(12f, 6f, 9f));

    [Header("규칙")]
    [Tooltip("고정 장치 한쪽을 푼 뒤 다른 쪽을 풀어야 하는 시간(초). 대원이 한 명뿐이면 적용하지 않는다")]
    [Min(1f)] public float clampWindow = 10f;
    [Min(5f)] public float countdownSeconds = 180f;
    [Min(3f)] public float engineNoiseInterval = 30f;
    [Tooltip("경보·엔진 소음이 들리는 반경(m). 쪼꼬미·촉수형·눈 없는 대형이 듣는다")]
    [Min(5f)] public float noiseRadius = 250f;
    [Tooltip("모두 탑승한 뒤 출발까지 걸리는 시간(초)")]
    [Min(0f)] public float boardHoldSeconds = 3f;

    [Header("UI")]
    public TMP_FontAsset font;

    public static F1MissionDirector Instance { get; private set; }

    readonly NetworkVariable<MissionState> _net = new NetworkVariable<MissionState>();
    readonly List<FpsHealth> _players = new List<FpsHealth>();
    MissionState _offline, _shown;
    bool _shownInit, _endingShown;
    float _clampTimer, _countdownLeft, _nextNoise, _boardHold, _cooldown, _hold;
    string _lastVisualKey;
    MissionHud _hud;
    AudioClip _clankClip, _alarmClip;

    public MissionState State => IsSpawned ? _net.Value : _offline;
    /// <summary>판정을 맡는 쪽인지 (방장 또는 오프라인)</summary>
    bool Judges => !IsSpawned || IsServer;
    int ClampCount => clamps != null ? clamps.Length : 0;
    int AllClampsMask => (1 << ClampCount) - 1;
    static bool AllClamps(MissionState s, int mask) => mask != 0 && (s.clamps & mask) == mask;
    static bool Prepared(MissionState s, int mask) => s.moduleInstalled && s.fuelLinked && AllClamps(s, mask);

    /// <summary>1층에는 내려갈 층이 없다</summary>
    public bool ExitUnlocked => false;
    public bool IsInLockedArea(Vector3 point) => false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    void Awake()
    {
        Instance = this;
        // 밸런스 설정: 점화 시간 × 인원 배율, 고정 장치 시간 제한 (GameBalance). 점화 소음(층 전체)은 일부러 그대로
        countdownSeconds *= GameBalance.HoldoutScale;
        clampWindow = GameBalance.SyncWindow;
        FloorMission.Register(this);
        if (hangarDoors != null) foreach (var d in hangarDoors) if (d != null && d.leaf != null) d.closedPos = d.leaf.position;
        _clankClip = F2MissionDirector.MakeClank();
        _alarmClip = F2MissionDirector.MakeAlarm();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        FloorMission.Unregister(this);
        if (_endingShown) FpsHealth.DamageBlocked = false;
        base.OnDestroy();
    }

    void Start()
    {
        _hud = new MissionHud(transform, font, "1층 · 보조 우주선 격납고");
        ApplyVisuals(State, true);
    }

    public override void OnNetworkSpawn()
    {
    }

    public override void OnNetworkDespawn()
    {
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
        AnimateDoors(s.hangarOpen);
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
        if (s.phase == (byte)Phase.Escaped || s.phase == (byte)Phase.Failed) return;
        CollectPlayers();
        int alive = 0;
        foreach (var p in _players) if (Alive(p)) alive++;

        // 실패: 대원이 있었는데 모두 사망
        if (_players.Count > 0 && alive == 0) { s.phase = (byte)Phase.Failed; SetState(s); return; }


        // 고정 장치: 한쪽만 풀린 상태로 시간이 지나면 다시 잠긴다 (대원이 한 명뿐이면 시간 제한 없음)
        int mask = AllClampsMask;
        if (s.clamps != 0 && !AllClamps(s, mask))
        {
            if (alive >= 2)
            {
                _clampTimer -= dt;
                if (_clampTimer <= 0f) { s.clamps = 0; s.clampSeconds = 0; }
                else s.clampSeconds = (byte)Mathf.Clamp(Mathf.CeilToInt(_clampTimer), 1, 255);
            }
            else { _clampTimer = clampWindow; s.clampSeconds = 0; }
        }
        else s.clampSeconds = 0;

        if (s.phase == (byte)Phase.Countdown)
        {
            _countdownLeft -= dt;
            s.countdown = (ushort)Mathf.Max(0, Mathf.CeilToInt(_countdownLeft));
            if (Time.time >= _nextNoise) { _nextNoise = Time.time + engineNoiseInterval; Noise(ignitionConsole); }
            if (_countdownLeft <= 0f) { s.phase = (byte)Phase.Board; s.countdown = 0; _boardHold = 0f; }
        }

        if (s.phase == (byte)Phase.Board)
        {
            int inside = 0;
            foreach (var p in _players) if (Alive(p) && boardZone.Contains(p.transform.position + Vector3.up)) inside++;
            s.boardIn = (byte)Mathf.Min(255, inside);
            s.boardNeed = (byte)Mathf.Min(255, alive);
            if (alive > 0 && inside >= alive)
            {
                _boardHold += dt;
                if (_boardHold >= boardHoldSeconds) s.phase = (byte)Phase.Escaped;
            }
            else _boardHold = 0f;
        }
        SetState(s);
    }

    /// <summary>큰 소음: 소리를 듣는 몬스터(쪼꼬미·촉수형·눈 없는 대형)가 소리 난 곳으로 온다</summary>
    void Noise(MissionInteractable source)
    {
        if (source != null) GunNoise.Emit(source.transform.position, noiseRadius);
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
            case MissionInteractable.Kind.ModuleSocket: return moduleSocket;
            case MissionInteractable.Kind.FuelLine: return fuelLine;
            case MissionInteractable.Kind.HoldClamp: return clamps != null && index >= 0 && index < clamps.Length ? clamps[index] : null;
            case MissionInteractable.Kind.HangarConsole: return hangarConsole;
            case MissionInteractable.Kind.IgnitionConsole: return ignitionConsole;
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
        if (s.phase != (byte)Phase.Prepare) return;
        int mask = AllClampsMask;
        bool usedModule = false;
        switch (kind)
        {
            case MissionInteractable.Kind.ModuleSocket:
                if (s.moduleInstalled || inventory == null || !inventory.HasItem(FarmingItemKind.PowerModule) || !inventory.HasItem(FarmingItemKind.ToolKit)) return;
                s.moduleInstalled = true;
                usedModule = true;
                break;
            case MissionInteractable.Kind.FuelLine:
                if (s.fuelLinked) return;
                s.fuelLinked = true;
                break;
            case MissionInteractable.Kind.HoldClamp:
                int bit = 1 << index;
                if ((s.clamps & bit) != 0) return;
                if (s.clamps == 0) _clampTimer = clampWindow;   // 첫 번째를 푼 순간부터 시간을 잰다
                s.clamps |= (byte)bit;
                break;
            case MissionInteractable.Kind.HangarConsole:
                if (s.hangarOpen || !Prepared(s, mask)) return;
                s.hangarOpen = true;
                Noise(hangarConsole);   // 경보 → 몬스터가 몰려온다
                break;
            case MissionInteractable.Kind.IgnitionConsole:
                if (!s.hangarOpen) return;
                s.phase = (byte)Phase.Countdown;
                _countdownLeft = countdownSeconds;
                s.countdown = (ushort)Mathf.CeilToInt(countdownSeconds);
                _nextNoise = Time.time + engineNoiseInterval;
                Noise(ignitionConsole);
                break;
            default:
                return;
        }
        SetState(s);
        if (usedModule)
        {
            ConsumeItem(inventory, FarmingItemKind.PowerModule, clientId);
            ConsumeItem(inventory, FarmingItemKind.ToolKit, clientId);
        }
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

    /// <summary>지금 이 장치를 쓸 수 있는지. 못 쓰면 reason에 이유(화면 안내)</summary>
    bool Available(MissionInteractable d, MissionState s, FpsHealth me, out string reason)
    {
        reason = null;
        var inventory = me != null ? me.GetComponent<FpsInventory>() : null;
        bool Has(FarmingItemKind k) => inventory != null && inventory.HasItem(k);
        int mask = AllClampsMask;
        bool prepare = s.phase == (byte)Phase.Prepare;
        switch (d.kind)
        {
            case MissionInteractable.Kind.ModuleSocket:
                if (s.moduleInstalled) { reason = "전력 모듈 설치 완료"; return false; }
                if (!Has(FarmingItemKind.PowerModule)) { reason = Need + "전력 모듈이 필요해요</color> · 연료 접속실에 있어요"; return false; }
                if (!Has(FarmingItemKind.ToolKit)) { reason = Need + "설치할 공구가 필요해요</color> · 정비 작업실에 있어요 (모듈을 든 대원이 함께 들어야 해요)"; return false; }
                return prepare;
            case MissionInteractable.Kind.FuelLine:
                if (s.fuelLinked) { reason = "연료 라인 연결 완료"; return false; }
                return prepare;
            case MissionInteractable.Kind.HoldClamp:
                if ((s.clamps & (1 << d.index)) != 0) { reason = AllClamps(s, mask) ? "고정 장치 해제 완료" : "고정 장치 풀림 · " + (s.clampSeconds > 0 ? "<b>" + s.clampSeconds + "초</b> 안에 다른 쪽도 풀어야 해요" : "다른 쪽도 풀어야 해요"); return false; }
                return prepare;
            case MissionInteractable.Kind.HangarConsole:
                if (s.hangarOpen) { reason = "격납고 문 열림"; return false; }
                if (!Prepared(s, mask)) { reason = Need + "발사 준비가 덜 됐어요</color> · 전력 모듈, 연료 라인, 고정 장치를 먼저 끝내세요"; return false; }
                return prepare;
            case MissionInteractable.Kind.HangarDoor:
                reason = s.hangarOpen ? "" : Need + "잠김</color> · 발사 관제실에서 격납고 문을 열어야 해요";
                return false;
            case MissionInteractable.Kind.IgnitionConsole:
                if (s.phase == (byte)Phase.Countdown) { reason = "발사 준비 중  " + Clock(s.countdown); return false; }
                if (s.phase != (byte)Phase.Prepare) { reason = "발사 준비 완료 · 격납 베이의 탈출선으로 가세요"; return false; }
                if (!s.hangarOpen) { reason = Need + "먼저 발사 관제실에서 격납고 문을 여세요</color>"; return false; }
                return true;
        }
        reason = "";
        return false;
    }

    static string Clock(int seconds) => (seconds / 60) + ":" + (seconds % 60).ToString("00");

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
        else if (!Available(target, s, me, out var reason)) { _hold = 0f; prompt = reason ?? ""; }
        else
        {
            if (GameKeys.Held(GameAction.Interact) && _cooldown <= 0f)
            {
                _hold += Time.deltaTime;
                if (_hold >= target.holdSeconds) { _hold = 0f; _cooldown = 0.6f; Request(target, me); }
            }
            else _hold = 0f;
            progress = _hold / target.holdSeconds;
            prompt = "[" + GameKeys.Label(GameAction.Interact) + "] 길게 눌러 " + target.actionLabel;
        }
        if (_hud != null) _hud.SetPrompt(prompt, progress);
    }

    // ================================================================== 장치 표시등 / 문 / 알림
    void ApplyVisuals(MissionState s, bool force)
    {
        string key = s.moduleInstalled + "|" + s.fuelLinked + "|" + s.clamps + "|" + s.hangarOpen + "|" + s.phase;
        if (!force && key == _lastVisualKey) return;
        _lastVisualKey = key;
        int mask = AllClampsMask;
        bool prepared = Prepared(s, mask);
        if (moduleSocket != null) { if (s.moduleInstalled) moduleSocket.SetStatus(MissionHud.Green, 1.4f); else moduleSocket.SetStatus(MissionHud.Amber, 1.4f, 2f); }
        if (fuelLine != null) { if (s.fuelLinked) fuelLine.SetStatus(MissionHud.Green, 1.2f); else fuelLine.SetStatus(MissionHud.Amber, 1.4f, 2f); }
        if (clamps != null)
            for (int i = 0; i < clamps.Length; i++)
            {
                if (clamps[i] == null) continue;
                bool released = (s.clamps & (1 << i)) != 0;
                if (AllClamps(s, mask)) clamps[i].SetStatus(MissionHud.Green, 1.2f);
                else if (released) clamps[i].SetStatus(MissionHud.Amber, 2.2f, 6f);
                else clamps[i].SetStatus(MissionHud.Red, 1.6f, 2f);
            }
        if (hangarConsole != null)
        {
            if (s.hangarOpen) hangarConsole.SetStatus(MissionHud.Green, 1.4f);
            else if (prepared) hangarConsole.SetStatus(MissionHud.Amber, 1.8f, 2.5f);
            else hangarConsole.SetStatus(MissionHud.Red, 0.5f);
        }
        if (ignitionConsole != null)
        {
            if (s.phase == (byte)Phase.Countdown) ignitionConsole.SetStatus(MissionHud.Amber, 2.2f, 4f);
            else if (s.phase != (byte)Phase.Prepare) ignitionConsole.SetStatus(MissionHud.Green, 1.6f);
            else if (s.hangarOpen) ignitionConsole.SetStatus(MissionHud.Amber, 1.8f, 2.5f);
            else ignitionConsole.SetStatus(MissionHud.Red, 0.5f);
        }
    }

    /// <summary>격납고 문 움직임 (상태만 보고 모든 화면에서 똑같이 움직인다)</summary>
    void AnimateDoors(bool open)
    {
        if (hangarDoors == null) return;
        foreach (var d in hangarDoors)
        {
            if (d == null || d.leaf == null) continue;
            float target = open ? 1f : 0f;
            if (d.obstacle != null && d.obstacle.enabled == open) d.obstacle.enabled = !open;
            if (d.interactTrigger != null && d.interactTrigger.enabled == open) d.interactTrigger.enabled = !open;
            if (Mathf.Approximately(d.t, target)) continue;
            d.t = Mathf.MoveTowards(d.t, target, Time.deltaTime / doorSeconds);
            d.leaf.position = d.closedPos + d.openOffset * Mathf.SmoothStep(0, 1, d.t);
            if (d.blocker != null) d.blocker.enabled = d.t < 0.6f;
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
        int mask = AllClampsMask;
        if (!_shown.moduleInstalled && s.moduleInstalled) { _hud.Toast("전력 모듈 설치 완료"); PlayAt(moduleSocket, _clankClip); }
        if (!_shown.fuelLinked && s.fuelLinked) { _hud.Toast("연료 라인 연결 완료"); PlayAt(fuelLine, _clankClip); }
        if (s.clamps != _shown.clamps)
        {
            if (AllClamps(s, mask)) _hud.Toast("고정 장치 2개 해제 완료");
            else if (s.clamps == 0) _hud.Toast("<color=#FCA5A5>시간 초과 · 고정 장치가 다시 잠겼습니다</color> 두 곳을 거의 동시에 풀어야 해요");
            else _hud.Toast("고정 장치 한쪽 해제 · <color=#FCA5A5>" + (s.clampSeconds > 0 ? Mathf.RoundToInt(clampWindow) + "초 안에" : "이어서") + " 다른 쪽도 푸세요!</color>");
            if (clamps != null)
                for (int i = 0; i < clamps.Length; i++)
                    if (((s.clamps ^ _shown.clamps) & (1 << i)) != 0) PlayAt(clamps[i], _clankClip);
        }
        if (!Prepared(_shown, mask) && Prepared(s, mask)) _hud.Toast("발사 준비 끝 · <b>발사 관제실</b>에서 격납고 문을 여세요");
        if (!_shown.hangarOpen && s.hangarOpen)
        {
            _hud.Toast("격납고 문 열림 · <color=#FCA5A5>경보! 몬스터가 몰려옵니다</color> · 보조 우주선에서 점화하세요");
            PlayAt(hangarConsole, _alarmClip);
            if (hangarDoors != null) foreach (var d in hangarDoors) if (d != null && d.leaf != null) GameAudio.PlayAt(_alarmClip, d.leaf.position, 1f);
        }
        if (_shown.phase != s.phase)
        {
            if (s.phase == (byte)Phase.Countdown) { _hud.Toast("점화 · <color=#FCA5A5>엔진 소음이 퍼집니다!</color> 발사 준비가 끝날 때까지 버티세요"); PlayAt(ignitionConsole, _alarmClip); }
            if (s.phase == (byte)Phase.Board) _hud.Banner("발사 준비 완료", "발사 관제실 북쪽 통로로 내려가 격납 베이의 탈출선에 타세요", MissionHud.Green);
            if (s.phase == (byte)Phase.Escaped) ShowEnding();
            if (s.phase == (byte)Phase.Failed) _hud.Banner("임무 실패", "살아남은 대원이 없습니다", MissionHud.Red);
        }
        _shown = s;
    }

    /// <summary>탈출 성공 화면 (살아서 탄 대원 / 남겨진 대원) → 대기실로 돌아간다</summary>
    void ShowEnding()
    {
        if (_endingShown) return;
        _endingShown = true;
        FpsHealth.DamageBlocked = true;
        var escaped = new List<string>();
        var left = new List<string>();
        if (Stage2NetworkBootstrap.IsOnline)
        {
            foreach (var p in FpsNetworkPlayer.All)
            {
                if (p == null || p.Health == null) continue;
                (p.Health.IsDead ? left : escaped).Add(string.IsNullOrEmpty(p.Nickname) ? "대원" : p.Nickname);
            }
        }
        else escaped.Add(PlayerPrefs.GetString("VL_Nickname", "대원"));
        var screen = FindFirstObjectByType<GameOverScreen>();
        if (screen != null) screen.ShowEscape(escaped, left);
        else _hud.Banner("탈출 성공", "탈출선이 출발했습니다", MissionHud.Green);
    }

    // ================================================================== 목표 목록
    void UpdateHud(MissionState s)
    {
        if (_hud == null) return;
        int mask = AllClampsMask;
        _hud.BeginObjectives();
        if (s.moduleInstalled) _hud.Line(true, "보조 우주선에 전력 모듈 설치");
        else
        {
            bool module = TeamHas(FarmingItemKind.PowerModule), tool = TeamHas(FarmingItemKind.ToolKit);
            _hud.Line(module, "연료 접속실에서 전력 모듈 챙기기");
            _hud.Line(tool, "정비 작업실에서 공구 챙기기");
            if (module && tool) _hud.Line(false, "보조 우주선에 전력 모듈 설치  <color=#9FB3C2>(모듈과 공구를 한 대원이)</color>");
        }
        _hud.Line(s.fuelLinked, "연료 접속실에서 연료 라인 연결");
        string clampNote = AllClamps(s, mask) ? "" : s.clampSeconds > 0 ? "  <color=#FCA5A5>남은 시간 " + s.clampSeconds + "초</color>" : "  <color=#9FB3C2>(화물 적재실 · 지상 정비 구역, 10초 안에 둘 다)</color>";
        _hud.Line(AllClamps(s, mask), "고정 장치 풀기  <b>" + Count(s.clamps) + " / " + ClampCount + "</b>" + clampNote);
        if (Prepared(s, mask)) _hud.Line(s.hangarOpen, "발사 관제실에서 격납고 문 열기");
        if (s.hangarOpen)
        {
            if (s.phase == (byte)Phase.Prepare) _hud.Line(false, "보조 우주선에서 점화");
            else if (s.phase == (byte)Phase.Countdown) _hud.Line(false, "발사 준비 중  <b>" + Clock(s.countdown) + "</b>  <color=#FCA5A5>버티세요</color>");
            else _hud.Line(true, "발사 준비 완료");
        }
        if (s.phase == (byte)Phase.Board) _hud.Raw("<color=#FCD34D>▶</color> <b>격납 베이의 탈출선에 탑승</b>  " + s.boardIn + " / " + s.boardNeed + "명  <color=#9FB3C2>(발사 관제실 북쪽 통로)</color>");
        else if (s.phase == (byte)Phase.Escaped) _hud.Raw("<color=#6EE7B7>■ 탈출 성공</color>");
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
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);
        Gizmos.DrawWireCube(boardZone.center, boardZone.size);
    }
}
