using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using VoidLast.Monster;
using VoidLast.Player;

/// <summary>
/// 3층(중앙 통제 구역) 임무 진행과 클리어 판정. 앞 단계를 해야 다음 단계가 열린다.
///
/// 클리어 조건
///  1. 서버 관리실(SW)에서 꺼진 서버 3대를 다시 켠다. 켤 때마다 소음이 쪼꼬미를 부른다.
///     모두 켜면 보안실(NW) 문이 열리고 중앙 관제실(C)의 감시 화면이 켜진다.
///  2. 보안실에서 관리자 카드를 얻는다.
///  3. 관리자 카드로 함교(N) 문을 열고, 안에서 함선 상태를 확인한다.
///  4. 데이터 보관실(S)에서 항로 데이터를 2분 동안 내려받는다. 살아 있는 대원이 데이터 보관실 안에 있어야 진행되고, 비우면 멈춘다.
///     내려받는 소음이 쪼꼬미를 부른다. 중앙 관제실 감시 화면(F를 누르고 있는 동안)으로 몬스터 위치를 보고 무전으로 알려 줄 수 있다.
///  5. 격납고 관제실(NE)에서 항로 데이터를 전송한다 (1층 격납고 잠금 해제).
///  6. B 계단으로 내려간다 (StairDoorController, FloorTransition).
///  살아 있는 대원이 한 명도 없으면 임무 실패.
///
/// 멀티플레이: 판정은 방장(서버)만 하고 상태는 모든 대원에게 동기화된다.
/// 오프라인(씬을 바로 실행): 같은 규칙을 이 컴퓨터에서 그대로 진행한다.
/// 장치·문·물건 배치는 메뉴 Tools/VOID LAST/Multiplayer/6c Build F3 Mission (F3MissionSetup).
/// </summary>
[DisallowMultipleComponent]
public sealed class F3MissionDirector : NetworkBehaviour, IFloorMission
{
    public enum Phase : byte { Objectives = 0, Exit = 1, Failed = 2 }

    public struct MissionState : INetworkSerializable, IEquatable<MissionState>
    {
        public byte phase;
        public byte servers;            // 다시 켠 서버 (비트: 1번 = 1, 2번 = 2, 3번 = 4 …)
        public bool bridgeOpen;         // 관리자 카드로 함교 문을 열었는지
        public bool statusChecked;      // 함교에서 함선 상태를 확인했는지
        public byte download;           // 0 대기, 1 내려받는 중, 2 완료
        public float downloadProgress;  // 0~1
        public bool downloadPaused;     // 데이터 보관실에 아무도 없어 멈춤
        public bool sent;               // 격납고 관제실에서 전송 완료

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref phase);
            s.SerializeValue(ref servers);
            s.SerializeValue(ref bridgeOpen);
            s.SerializeValue(ref statusChecked);
            s.SerializeValue(ref download);
            s.SerializeValue(ref downloadProgress);
            s.SerializeValue(ref downloadPaused);
            s.SerializeValue(ref sent);
        }

        public bool Equals(MissionState o) =>
            phase == o.phase && servers == o.servers && bridgeOpen == o.bridgeOpen && statusChecked == o.statusChecked &&
            download == o.download && Mathf.Approximately(downloadProgress, o.downloadProgress) && downloadPaused == o.downloadPaused && sent == o.sent;
    }

    /// <summary>감시 화면 지도에 그리는 방 (area = 월드 x, z 범위)</summary>
    [Serializable]
    public struct MapRoom { public string name; public Rect area; }

    [Header("장치")]
    [Tooltip("다시 켜야 하는 서버 (MissionInteractable.index = 배열 순서)")]
    public MissionInteractable[] servers;
    public MissionInteractable bridgeStatus;
    public MissionInteractable routeDownload;
    public MissionInteractable monsterMonitor;
    public MissionInteractable hangarUplink;

    [Header("잠긴 문 (열리면 위로 올라가 천장 속으로 들어간다)")]
    [Tooltip("서버를 모두 켜면 열리는 보안실 문")]
    public F2MissionDirector.LockdownDoor[] securityDoors;
    [Tooltip("관리자 카드로 여는 함교 문 (하나를 열면 모두 열린다)")]
    public F2MissionDirector.LockdownDoor[] bridgeDoors;
    [Min(0.2f)] public float doorSeconds = 1.6f;

    [Header("구역 (월드 좌표)")]
    [Tooltip("서버를 켜기 전에는 들어갈 수 없는 보안실. 여기에는 고치가 생기지 않는다")]
    public Bounds securityRoom = new Bounds(new Vector3(-35f, 18f, 28f), new Vector3(16f, 6f, 20f));
    [Tooltip("문을 열기 전에는 들어갈 수 없는 함교. 여기에는 고치가 생기지 않는다")]
    public Bounds bridgeRoom = new Bounds(new Vector3(-6f, 18f, 29f), new Vector3(24f, 6f, 18f));
    [Tooltip("내려받는 동안 대원이 머물러야 하는 데이터 보관실")]
    public Bounds dataRoom = new Bounds(new Vector3(-1f, 18f, -50f), new Vector3(20f, 6f, 16f));
    [Tooltip("임무를 끝내면 켜지는 B 계단 입구 안내등")]
    public Light[] exitLights;

    [Header("규칙")]
    [Min(5f)] public float downloadSeconds = 120f;
    [Min(3f)] public float downloadNoiseInterval = 15f;
    [Tooltip("내려받는 소음이 쪼꼬미에게 들리는 반경(m)")]
    [Min(5f)] public float downloadNoiseRadius = 100f;
    [Tooltip("서버를 켤 때 나는 소음이 쪼꼬미에게 들리는 반경(m)")]
    [Min(1f)] public float serverNoiseRadius = 30f;

    [Header("감시 화면 지도 (메뉴 6c가 맵 배치 파일에서 채운다)")]
    public MapRoom[] mapRooms;
    public Rect[] mapCorridors;

    [Header("UI / 이야기")]
    public TMP_FontAsset font;
    [TextArea(4, 12)]
    public string bridgeStatusText =
        "<b>함교 · 함선 상태</b>\n\n" +
        "주 동력 — 비상 전원으로 가동 중\n" +
        "선내 생체 반응 — <color=#FCA5A5>여러 곳에서 감지</color>\n" +
        "1층 보조 우주선 격납고 — <color=#FCA5A5>잠김</color>\n\n" +
        "보조 우주선을 띄우려면 <b>항로 데이터</b>가 필요합니다.\n" +
        "· 데이터 보관실에서 항로 데이터를 내려받으세요 (약 2분)\n" +
        "· 격납고 관제실에서 1층으로 전송하면 격납고 잠금이 풀립니다";

    public static F3MissionDirector Instance { get; private set; }

    readonly NetworkVariable<MissionState> _net = new NetworkVariable<MissionState>();
    readonly MissionLeaverDrops _leaverDrops = new MissionLeaverDrops();
    readonly List<FpsHealth> _players = new List<FpsHealth>();
    MissionState _offline, _shown;
    bool _shownInit, _statusShownOnce;
    float _downloadAccum;   // 판정 쪽에서만 쓰는 정밀 진행률 (네트워크로는 0.5% 단위 값만 보냄)
    float _nextNoise, _cooldown, _hold;
    string _lastVisualKey;
    MissionHud _hud;
    F3MonitorMap _map;
    AudioClip _clankClip, _alarmClip;

    public MissionState State => IsSpawned ? _net.Value : _offline;
    /// <summary>판정을 맡는 쪽인지 (방장 또는 오프라인)</summary>
    bool Judges => !IsSpawned || IsServer;
    int ServerCount => servers != null ? servers.Length : 0;
    int AllServersMask => (1 << ServerCount) - 1;
    static bool AllOn(MissionState s, int mask) => mask != 0 && (s.servers & mask) == mask;

    public bool ExitUnlocked => State.phase == (byte)Phase.Exit;

    public bool IsInLockedArea(Vector3 point)
    {
        var s = State;
        return (!AllOn(s, AllServersMask) && securityRoom.Contains(point)) || (!s.bridgeOpen && bridgeRoom.Contains(point));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    void Awake()
    {
        Instance = this;
        FloorMission.Register(this);
        RememberClosed(securityDoors);
        RememberClosed(bridgeDoors);
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
        _hud = new MissionHud(transform, font, "3층 · 중앙 통제 구역");
        _map = new F3MonitorMap(_hud, mapRooms, mapCorridors, FloorTransition.Instance != null ? FloorTransition.Instance.FloorY : 16f);
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
        AnimateDoors(securityDoors, AllOn(s, AllServersMask));
        AnimateDoors(bridgeDoors, s.bridgeOpen);
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
            if (s.download == 1)
            {
                bool guarded = false;
                foreach (var p in _players) if (Alive(p) && dataRoom.Contains(p.transform.position + Vector3.up)) { guarded = true; break; }
                s.downloadPaused = !guarded;
                if (_downloadAccum < s.downloadProgress) _downloadAccum = s.downloadProgress;   // 방장이 바뀌는 등으로 값이 어긋났을 때 보정
                if (guarded) _downloadAccum = Mathf.Min(1f, _downloadAccum + dt / downloadSeconds);
                // 화면/네트워크용 값은 0.5% 단위로 끊어서 보낸다
                s.downloadProgress = _downloadAccum >= 1f ? 1f : Mathf.Floor(_downloadAccum * 200f) / 200f;
                if (Time.time >= _nextNoise) { _nextNoise = Time.time + downloadNoiseInterval; MissionNoise(routeDownload, downloadNoiseRadius); }
                if (_downloadAccum >= 1f) { s.download = 2; s.downloadPaused = false; }
            }
            if (s.sent) s.phase = (byte)Phase.Exit;
        }
        SetState(s);
    }

    /// <summary>임무 소음: 쪼꼬미가 듣고 소리 난 곳으로 조사하러 온다</summary>
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

    /// <summary>그 장치에서 대원과 가장 가까운 지점 (거리 확인용). 없는 장치면 false</summary>
    bool DevicePoint(MissionInteractable.Kind kind, int index, Vector3 from, out Vector3 point, out float reach)
    {
        point = default; reach = 3f;
        if (kind == MissionInteractable.Kind.BridgeDoor)
        {
            if (bridgeDoors == null || index < 0 || index >= bridgeDoors.Length || bridgeDoors[index] == null || bridgeDoors[index].leaf == null) return false;
            var door = bridgeDoors[index];
            point = door.interactTrigger != null && door.interactTrigger.enabled ? door.interactTrigger.ClosestPoint(from) : door.leaf.position;
            return true;
        }
        MissionInteractable device = null;
        switch (kind)
        {
            case MissionInteractable.Kind.ServerRack: device = servers != null && index >= 0 && index < servers.Length ? servers[index] : null; break;
            case MissionInteractable.Kind.BridgeStatus: device = bridgeStatus; break;
            case MissionInteractable.Kind.RouteDownload: device = routeDownload; break;
            case MissionInteractable.Kind.HangarUplink: device = hangarUplink; break;
        }
        if (device == null) return false;
        var box = device.GetComponent<Collider>();
        point = box != null ? box.ClosestPoint(from) : device.transform.position;
        reach = device.maxDistance;
        return true;
    }

    void TryApply(MissionInteractable.Kind kind, int index, Vector3 from, bool checkDistance, FpsInventory inventory, ulong clientId)
    {
        if (!DevicePoint(kind, index, from, out var point, out float reach)) return;
        if (checkDistance && Vector3.Distance(from, point) > reach + 2.5f) return;
        var s = State;
        if (s.phase != (byte)Phase.Objectives) return;
        bool allOn = AllOn(s, AllServersMask);
        bool usedCard = false;
        switch (kind)
        {
            case MissionInteractable.Kind.ServerRack:
                int bit = 1 << index;
                if ((s.servers & bit) != 0) return;
                s.servers |= (byte)bit;
                MissionNoise(servers[index], serverNoiseRadius);   // 서버 팬 소리 → 쪼꼬미
                break;
            case MissionInteractable.Kind.BridgeDoor:
                if (s.bridgeOpen || inventory == null || !inventory.HasItem(FarmingItemKind.AdminCard)) return;
                s.bridgeOpen = true;
                usedCard = true;
                break;
            case MissionInteractable.Kind.BridgeStatus:
                if (!s.bridgeOpen || s.statusChecked) return;
                s.statusChecked = true;
                break;
            case MissionInteractable.Kind.RouteDownload:
                if (!allOn || !s.statusChecked || s.download != 0) return;
                s.download = 1; s.downloadProgress = 0f; s.downloadPaused = false; _downloadAccum = 0f;
                _nextNoise = Time.time + downloadNoiseInterval;
                MissionNoise(routeDownload, downloadNoiseRadius);
                break;
            case MissionInteractable.Kind.HangarUplink:
                if (s.download != 2 || s.sent) return;
                s.sent = true;
                break;
            default:
                return;
        }
        SetState(s);
        if (usedCard) ConsumeItem(inventory, FarmingItemKind.AdminCard, clientId);
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
        bool allOn = AllOn(s, AllServersMask);
        bool objectives = s.phase == (byte)Phase.Objectives;
        switch (d.kind)
        {
            case MissionInteractable.Kind.ServerRack:
                if ((s.servers & (1 << d.index)) != 0) { reason = "서버 가동 중"; return false; }
                if (!objectives) { reason = ""; return false; }
                return true;
            case MissionInteractable.Kind.SecurityDoor:
                reason = allOn ? "" : Need + "잠김</color> · 서버 관리실의 서버를 모두 다시 켜야 열려요  (" + Count(s.servers) + " / " + ServerCount + ")";
                return false;
            case MissionInteractable.Kind.BridgeDoor:
                if (s.bridgeOpen || !objectives) { reason = ""; return false; }
                if (inventory == null || !inventory.HasItem(FarmingItemKind.AdminCard)) { reason = Need + "잠김</color> · 관리자 카드가 필요해요 · 보안실에 있어요"; return false; }
                return true;
            case MissionInteractable.Kind.BridgeStatus:
                return true;   // 몇 번이든 다시 볼 수 있음
            case MissionInteractable.Kind.RouteDownload:
                if (s.download == 1) { reason = "항로 데이터 내려받는 중  " + Mathf.RoundToInt(s.downloadProgress * 100) + "%"; return false; }
                if (s.download == 2) { reason = "항로 데이터 내려받기 완료"; return false; }
                if (!allOn) { reason = Need + "단말이 꺼져 있어요</color> · 서버 관리실의 서버를 먼저 켜세요"; return false; }
                if (!s.statusChecked) { reason = Need + "먼저 함교에서 함선 상태를 확인하세요</color>"; return false; }
                if (!objectives) { reason = ""; return false; }
                return true;
            case MissionInteractable.Kind.MonsterMonitor:
                if (!allOn) { reason = Need + "화면이 꺼져 있어요</color> · 서버 관리실의 서버를 먼저 켜세요"; return false; }
                return true;   // 누르고 있는 동안 보기
            case MissionInteractable.Kind.HangarUplink:
                if (s.sent) { reason = "항로 데이터 전송 완료 · 1층 격납고 잠금 해제됨"; return false; }
                if (s.download != 2) { reason = Need + "보낼 항로 데이터가 없어요</color> · 데이터 보관실에서 내려받으세요"; return false; }
                if (!objectives) { reason = ""; return false; }
                return true;
        }
        reason = "";
        return false;
    }

    static int Count(byte mask)
    {
        int n = 0;
        for (int m = mask; m != 0; m >>= 1) n += m & 1;
        return n;
    }

    void LocalInteraction(MissionState s)
    {
        MissionInteractable target = null;
        string prompt = "";
        float progress = 0f;
        bool showMap = false;
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

        bool held = GameKeys.Held(GameAction.Interact);
        string key = "[" + GameKeys.Label(GameAction.Interact) + "] ";
        if (target == null) _hold = 0f;
        else if (!Available(target, s, me, out var reason)) { _hold = 0f; prompt = reason ?? ""; }
        else if (target.kind == MissionInteractable.Kind.MonsterMonitor)
        {
            // 감시 화면: 누르고 있는 동안만 지도가 뜬다 (판정 없음, 내 화면에서만)
            _hold = 0f;
            showMap = held;
            prompt = held ? "" : key + "누르고 있는 동안 " + target.actionLabel;
        }
        else
        {
            if (held && _cooldown <= 0f)
            {
                _hold += Time.deltaTime;
                if (_hold >= target.holdSeconds)
                {
                    _hold = 0f; _cooldown = 0.6f;
                    if (target.kind == MissionInteractable.Kind.BridgeStatus) ShowStatus();
                    Request(target, me);
                }
            }
            else _hold = 0f;
            progress = _hold / target.holdSeconds;
            prompt = key + "길게 눌러 " + target.actionLabel;
        }

        if (_hud != null) _hud.SetPrompt(prompt, progress);
        if (_map != null) { _map.SetVisible(showMap); _map.Tick(me); }
    }

    void ShowStatus()
    {
        _statusShownOnce = true;
        if (_hud != null) _hud.ShowNote(bridgeStatusText, 14f);
    }

    // ================================================================== 장치 표시등 / 문 / 알림
    void ApplyVisuals(MissionState s, bool force)
    {
        string key = s.servers + "|" + s.bridgeOpen + "|" + s.statusChecked + "|" + s.download + "|" + s.downloadPaused + "|" + s.sent + "|" + s.phase;
        if (!force && key == _lastVisualKey) return;
        _lastVisualKey = key;
        bool allOn = AllOn(s, AllServersMask);
        if (servers != null)
            for (int i = 0; i < servers.Length; i++)
            {
                if (servers[i] == null) continue;
                if ((s.servers & (1 << i)) != 0) servers[i].SetStatus(MissionHud.Green, 1.2f); else servers[i].SetStatus(MissionHud.Red, 1.8f, 2f);
            }
        if (bridgeStatus != null)
        {
            if (s.statusChecked) bridgeStatus.SetStatus(MissionHud.Cyan, 0.5f);
            else if (s.bridgeOpen) bridgeStatus.SetStatus(MissionHud.Cyan, 1.4f, 2f);
            else bridgeStatus.SetStatus(MissionHud.Amber, 0.5f);
        }
        if (routeDownload != null)
        {
            if (s.download == 2) routeDownload.SetStatus(MissionHud.Green, 1.4f);
            else if (s.download == 1) routeDownload.SetStatus(s.downloadPaused ? MissionHud.Red : MissionHud.Amber, 2.2f, s.downloadPaused ? 6f : 3.5f);
            else if (allOn && s.statusChecked) routeDownload.SetStatus(MissionHud.Amber, 1.6f, 2f);
            else routeDownload.SetStatus(MissionHud.Amber, 0.4f);
        }
        if (monsterMonitor != null) { if (allOn) monsterMonitor.SetStatus(MissionHud.Cyan, 1.2f); else monsterMonitor.SetStatus(MissionHud.Red, 0.3f); }
        if (hangarUplink != null)
        {
            if (s.sent) hangarUplink.SetStatus(MissionHud.Green, 1.6f);
            else if (s.download == 2) hangarUplink.SetStatus(MissionHud.Amber, 1.8f, 2.5f);
            else hangarUplink.SetStatus(MissionHud.Amber, 0.4f);
        }
        if (exitLights != null)
            foreach (var l in exitLights)
            {
                if (l == null) continue;
                l.enabled = s.phase == (byte)Phase.Exit;
                l.color = MissionHud.Amber;
            }
    }

    static void RememberClosed(F2MissionDirector.LockdownDoor[] doors)
    {
        if (doors == null) return;
        foreach (var d in doors) if (d != null && d.leaf != null) d.closedPos = d.leaf.position;
    }

    /// <summary>잠긴 문 움직임 (상태만 보고 모든 화면에서 똑같이 움직인다)</summary>
    void AnimateDoors(F2MissionDirector.LockdownDoor[] doors, bool open)
    {
        if (doors == null) return;
        foreach (var d in doors)
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

    void PlayAt(Vector3 position, AudioClip clip)
    {
        if (clip != null) GameAudio.PlayAt(clip, position, 1f);
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
        if (!_shownInit) { _shownInit = true; _shown = s; if (s.statusChecked) _statusShownOnce = true; return; }
        int mask = AllServersMask;
        if (s.servers != _shown.servers)
        {
            int newly = s.servers & ~_shown.servers;
            for (int i = 0; i < ServerCount; i++)
                if ((newly & (1 << i)) != 0 && servers[i] != null) PlayAt(servers[i].transform.position, _clankClip);
            if (AllOn(s, mask) && !AllOn(_shown, mask))
            {
                _hud.Toast("서버 재시작 완료 · <b>보안실</b> 문이 열렸습니다 · 중앙 관제실 감시 화면도 켜졌습니다");
                if (securityDoors != null) foreach (var d in securityDoors) if (d != null && d.leaf != null) PlayAt(d.leaf.position, _clankClip);
            }
            else _hud.Toast("서버 재시작  " + Count(s.servers) + " / " + ServerCount + " · <color=#FCA5A5>서버 소리가 퍼집니다!</color>");
        }
        if (!_shown.bridgeOpen && s.bridgeOpen)
        {
            _hud.Toast("함교 문이 열렸습니다 · 안에서 <b>함선 상태</b>를 확인하세요");
            if (bridgeDoors != null) foreach (var d in bridgeDoors) if (d != null && d.leaf != null) PlayAt(d.leaf.position, _clankClip);
        }
        if (!_shown.statusChecked && s.statusChecked)
        {
            if (!_statusShownOnce) ShowStatus();
            _hud.Toast("함선 상태 확인 · <b>데이터 보관실</b>에서 항로 데이터를 내려받으세요");
        }
        if (_shown.download == 0 && s.download == 1)
            _hud.Toast("항로 데이터 내려받기 시작 · <color=#FCA5A5>소음이 퍼집니다!</color> 끝날 때까지 데이터 보관실을 지키세요");
        if (!_shown.downloadPaused && s.downloadPaused && s.download == 1)
        {
            _hud.Toast("<color=#FCA5A5>데이터 보관실에 아무도 없어 내려받기가 멈췄습니다</color>");
            if (routeDownload != null) PlayAt(routeDownload.transform.position, _alarmClip);
        }
        if (_shown.download == 1 && s.download == 2) _hud.Toast("항로 데이터 내려받기 완료 · <b>격납고 관제실</b>에서 전송하세요");
        if (_shown.phase != s.phase)
        {
            if (s.phase == (byte)Phase.Exit)
            {
                _hud.Toast("항로 데이터 전송 완료 · 1층 격납고 잠금 해제 · <b>B 계단 안으로 들어가 2층으로 내려가세요</b> (계단 반쯤에서 " + GameKeys.Label(GameAction.Interact) + ")");
                if (hangarUplink != null) PlayAt(hangarUplink.transform.position, _clankClip);
            }
            if (s.phase == (byte)Phase.Failed) _hud.Banner("임무 실패", "살아남은 대원이 없습니다", MissionHud.Red);
        }
        _shown = s;
    }

    // ================================================================== 목표 목록
    void UpdateHud(MissionState s)
    {
        if (_hud == null) return;
        bool allOn = AllOn(s, AllServersMask);
        bool cardDone = s.bridgeOpen || TeamHas(FarmingItemKind.AdminCard);
        // 앞 단계를 끝내야 다음 단계가 목록에 나타난다
        _hud.BeginObjectives();
        _hud.Line(allOn, "서버 관리실에서 서버 다시 켜기  <b>" + Count(s.servers) + " / " + ServerCount + "</b>");
        if (allOn) _hud.Line(cardDone, "보안실에서 관리자 카드 찾기");
        if (allOn && cardDone) _hud.Line(s.bridgeOpen, "관리자 카드로 함교 문 열기");
        if (s.bridgeOpen) _hud.Line(s.statusChecked, "함교에서 함선 상태 확인");
        if (s.statusChecked)
        {
            if (s.download == 0) _hud.Line(false, "데이터 보관실에서 항로 데이터 내려받기 시작");
            else if (s.download == 1)
            {
                string note = s.downloadPaused ? "  <color=#FCA5A5>데이터 보관실에 대원 필요</color>" : "  <color=#FCA5A5>소음 주의</color>";
                _hud.Line(false, "항로 데이터 내려받는 중  <b>" + Mathf.RoundToInt(s.downloadProgress * 100) + "%</b>" + note);
            }
            else _hud.Line(true, "항로 데이터 내려받기 완료");
        }
        if (s.download == 2) _hud.Line(s.sent, "격납고 관제실에서 항로 데이터 전송");
        if (allOn && s.phase == (byte)Phase.Objectives) _hud.Raw("<color=#7FA7B8>◇</color> <color=#9FB3C2>중앙 관제실 감시 화면에서 몬스터 위치 확인 가능</color>");
        if (s.phase == (byte)Phase.Exit) _hud.Raw("<color=#FCD34D>▶</color> <b>B 계단 안에서 2층으로 내려가기</b>");
        else if (s.phase == (byte)Phase.Failed) _hud.Raw("<color=#FCA5A5>■ 임무 실패</color>");
        _hud.EndObjectives();
        _hud.Tick();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.15f, 0.5f);
        Gizmos.DrawWireCube(securityRoom.center, securityRoom.size);
        Gizmos.DrawWireCube(bridgeRoom.center, bridgeRoom.size);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);
        Gizmos.DrawWireCube(dataRoom.center, dataRoom.size);
    }
}
