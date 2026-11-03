using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using VoidLast.Monster;
using VoidLast.Player;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 2층(정비 및 화물 구역) 임무 진행과 클리어 판정.
///
/// 클리어 조건
///  1. 정비 보관실(NW)에서 보조 우주선용 전력 모듈을 꺼낸다 (F 길게 3초). 꺼낼 때 금속음이 나서 근처의 촉수형(눈 없는) 몬스터가 다가온다.
///  2. 연료 공급실(SW)에서 연료 공급 장치를 가동한다 (F 길게 2초). 연료가 다 찰 때까지(기본 45초) 살아 있는 대원이 연료 공급실 안에 있어야 진행되고,
///     비우면 멈춘다. 펌프 소음이 주기적으로 퍼져서 소형 정찰형 몬스터가 연료 공급실로 조사하러 온다.
///  3. 1·2를 모두 마치면 1층 격납고 입구로 이동한다. 살아 있는 대원 전원이 격납고 입구 구역에 모이면 2층 클리어.
///  (선택) 검역 보관실(SE)에서 침입 흔적(승무원 기록)을 조사할 수 있다. 클리어에는 필요 없다.
///  살아 있는 대원이 한 명도 없으면 임무 실패.
///
/// 멀티플레이: 판정은 방장(서버)만 하고 상태는 모든 대원에게 동기화된다. 1·2는 순서와 상관없이 대원이 나눠서 동시에 진행할 수 있다.
/// 오프라인(씬을 바로 실행): 같은 규칙을 이 컴퓨터에서 그대로 진행한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class F2MissionDirector : NetworkBehaviour
{
    public enum Phase : byte { Objectives = 0, Rally = 1, Cleared = 2, Failed = 3 }

    public struct MissionState : INetworkSerializable, IEquatable<MissionState>
    {
        public byte phase;
        public bool module;
        public FixedString64Bytes carrier;
        public byte fuel;            // 0 대기, 1 공급 중, 2 완료
        public float fuelProgress;   // 0~1
        public bool fuelPaused;
        public bool trace;
        public byte rallyIn, rallyNeed;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref phase);
            s.SerializeValue(ref module);
            s.SerializeValue(ref carrier);
            s.SerializeValue(ref fuel);
            s.SerializeValue(ref fuelProgress);
            s.SerializeValue(ref fuelPaused);
            s.SerializeValue(ref trace);
            s.SerializeValue(ref rallyIn);
            s.SerializeValue(ref rallyNeed);
        }

        public bool Equals(MissionState o) =>
            phase == o.phase && module == o.module && carrier.Equals(o.carrier) && fuel == o.fuel &&
            Mathf.Approximately(fuelProgress, o.fuelProgress) && fuelPaused == o.fuelPaused && trace == o.trace &&
            rallyIn == o.rallyIn && rallyNeed == o.rallyNeed;
    }

    [Header("장치")]
    public MissionInteractable powerModule;
    public MissionInteractable fuelPump;
    public MissionInteractable intrusionLog;

    [Header("구역 (월드 좌표)")]
    [Tooltip("연료 공급 중 대원이 머물러야 하는 연료 공급실")]
    public Bounds fuelRoom = new Bounds(new Vector3(-62f, 10f, 26f), new Vector3(22f, 6f, 18f));
    [Tooltip("1층 격납고 입구 합류 구역")]
    public Bounds rallyZone = new Bounds(new Vector3(38.8f, 1.5f, -84.5f), new Vector3(6f, 4f, 9f));
    public Light rallyLight;

    [Header("규칙")]
    [Min(5f)] public float fuelSeconds = 45f;
    [Min(3f)] public float pumpNoiseInterval = 15f;
    [Tooltip("펌프 소음이 소형 정찰형에게 들리는 반경(m, 같은 층만)")]
    [Min(5f)] public float pumpNoiseRadius = 100f;
    [Tooltip("전력 모듈을 꺼낼 때 금속음이 촉수형 몬스터에게 들리는 반경(m)")]
    [Min(1f)] public float moduleNoiseRadius = 16f;

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
    MissionInteractable _target;
    string _lastStatusKey;

    public MissionState State => IsSpawned ? _net.Value : _offline;
    bool HasAuthority => !IsSpawned || IsServer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; FloorCleared = null; }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        BuildHud();
        ApplyVisuals(State, true);
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

        if (s.phase == (byte)Phase.Objectives)
        {
            if (s.fuel == 1)
            {
                bool guarded = false;
                foreach (var p in _players) if (Alive(p) && fuelRoom.Contains(p.transform.position + Vector3.up)) { guarded = true; break; }
                s.fuelPaused = !guarded;
                if (_fuelAccum < s.fuelProgress) _fuelAccum = s.fuelProgress;   // 방장이 바뀌는 등으로 값이 어긋났을 때 보정
                if (guarded) _fuelAccum = Mathf.Min(1f, _fuelAccum + dt / fuelSeconds);
                // 화면/네트워크용 값은 0.5% 단위로 끊어서 보낸다 (매 프레임 아주 작게 늘어나는 값을 반올림하면 0에 머무르므로 내림)
                s.fuelProgress = _fuelAccum >= 1f ? 1f : Mathf.Floor(_fuelAccum * 200f) / 200f;
                if (Time.time >= _nextNoise) { _nextNoise = Time.time + pumpNoiseInterval; PumpNoise(); }
                if (_fuelAccum >= 1f) { s.fuel = 2; s.fuelPaused = false; }
            }
            if (s.module && s.fuel == 2) s.phase = (byte)Phase.Rally;
        }

        if (s.phase == (byte)Phase.Rally)
        {
            int inside = 0;
            foreach (var p in _players) if (Alive(p) && rallyZone.Contains(p.transform.position + Vector3.up * 0.5f)) inside++;
            s.rallyIn = (byte)Mathf.Min(255, inside);
            s.rallyNeed = (byte)Mathf.Min(255, alive);
            if (alive > 0 && inside >= alive) s.phase = (byte)Phase.Cleared;
        }
        SetState(s);
    }

    /// <summary>펌프 소음: 같은 층의 소형 정찰형 몬스터가 연료 공급실로 조사하러 온다</summary>
    void PumpNoise()
    {
        if (fuelPump == null) return;
        Vector3 at = fuelPump.transform.position;
        foreach (var m in FindObjectsByType<SmallMonsterAI>(FindObjectsSortMode.None))
        {
            Vector3 d = m.transform.position - at;
            if (Mathf.Abs(d.y) > 3.5f || d.sqrMagnitude > pumpNoiseRadius * pumpNoiseRadius) continue;
            m.HearNoise(at);
        }
    }

    // ================================================================== 상호작용 요청
    void Request(MissionInteractable.Kind kind, FpsHealth who)
    {
        if (HasAuthority) TryApply(kind, who != null ? who.transform.position : Vector3.zero, LocalNickname(), who != null);
        else InteractRpc((byte)kind);
    }

    [Rpc(SendTo.Server)]
    void InteractRpc(byte kind, RpcParams rpcParams = default)
    {
        var player = FpsNetworkPlayer.FindByClient(rpcParams.Receive.SenderClientId);
        if (player == null || player.Health == null || player.Health.IsDead) return;
        TryApply((MissionInteractable.Kind)kind, player.transform.position, string.IsNullOrEmpty(player.Nickname) ? "대원" : player.Nickname, true);
    }

    void TryApply(MissionInteractable.Kind kind, Vector3 from, string nick, bool checkDistance)
    {
        var device = Device(kind);
        if (device == null) return;
        if (checkDistance && Vector3.Distance(from, device.transform.position) > device.maxDistance + 2.5f) return;
        var s = State;
        switch (kind)
        {
            case MissionInteractable.Kind.PowerModule:
                if (s.phase != (byte)Phase.Objectives || s.module) return;
                s.module = true;
                s.carrier = new FixedString64Bytes(nick.Length > 20 ? nick.Substring(0, 20) : nick);
                ScanNoise.Emit(device.transform.position, moduleNoiseRadius, 14f);   // 금속음 → 촉수형 몬스터
                break;
            case MissionInteractable.Kind.FuelPump:
                if (s.phase != (byte)Phase.Objectives || s.fuel != 0) return;
                s.fuel = 1; s.fuelProgress = 0f; s.fuelPaused = false; _fuelAccum = 0f;
                _nextNoise = Time.time + pumpNoiseInterval;
                PumpNoise();
                break;
            case MissionInteractable.Kind.IntrusionLog:
                if (s.trace) return;
                s.trace = true;
                break;
        }
        SetState(s);
    }

    MissionInteractable Device(MissionInteractable.Kind kind) =>
        kind == MissionInteractable.Kind.PowerModule ? powerModule : kind == MissionInteractable.Kind.FuelPump ? fuelPump : intrusionLog;

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
        return Input.GetKey(KeyCode.F);
#endif
    }

    bool Available(MissionInteractable d, MissionState s, out string reason)
    {
        reason = null;
        switch (d.kind)
        {
            case MissionInteractable.Kind.PowerModule:
                if (s.module) { reason = "전력 모듈은 이미 확보했어요 (" + s.carrier + ")"; return false; }
                break;
            case MissionInteractable.Kind.FuelPump:
                if (s.fuel == 1) { reason = "연료 공급 중  " + Mathf.RoundToInt(s.fuelProgress * 100) + "%"; return false; }
                if (s.fuel == 2) { reason = "연료 공급 완료"; return false; }
                break;
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
        if (me != null && !me.IsDead && cam != null && Cursor.lockState == CursorLockMode.Locked)
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
            if (Available(_target, s, out var reason))
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
                prompt = "[F] 길게 눌러 " + _target.actionLabel;
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
        string key = s.module + "|" + s.fuel + "|" + s.fuelPaused + "|" + s.trace + "|" + s.phase;
        if (!force && key == _lastStatusKey) return;
        _lastStatusKey = key;
        if (powerModule != null) { if (s.module) powerModule.SetStatus(Red, 0.25f); else powerModule.SetStatus(Green, 1.6f, 2.5f); }
        if (fuelPump != null)
        {
            if (s.fuel == 2) fuelPump.SetStatus(Green, 1.6f);
            else if (s.fuel == 1) fuelPump.SetStatus(s.fuelPaused ? Red : Amber, 2.2f, s.fuelPaused ? 6f : 3.5f);
            else fuelPump.SetStatus(Amber, 1.4f, 2f);
        }
        if (intrusionLog != null) intrusionLog.SetStatus(Cyan, s.trace ? 0.5f : 1.3f, s.trace ? 0f : 2f);
        if (rallyLight != null)
        {
            rallyLight.enabled = s.phase == (byte)Phase.Rally || s.phase == (byte)Phase.Cleared;
            rallyLight.color = s.phase == (byte)Phase.Cleared ? Green : Amber;
        }
    }

    void AnnounceChanges(MissionState s)
    {
        if (!_shownInit) { _shownInit = true; _shown = s; if (s.trace) _logShownOnce = true; return; }
        if (!_shown.module && s.module) Toast("<color=#6EE7B7>" + s.carrier + "</color> 대원이 보조 우주선용 전력 모듈을 확보했습니다");
        if (_shown.fuel == 0 && s.fuel == 1) Toast("연료 공급 시작 · <color=#FCA5A5>펌프 소음이 퍼집니다!</color> 연료가 찰 때까지 연료 공급실을 지키세요");
        if (_shown.fuel == 1 && s.fuel == 2) Toast("연료 공급 완료");
        if (!_shown.fuelPaused && s.fuelPaused && s.fuel == 1) Toast("<color=#FCA5A5>연료 공급실에 아무도 없어 공급이 멈췄습니다</color>");
        if (!_shown.trace && s.trace && !_logShownOnce) { ShowLog(); }
        if (_shown.phase != s.phase)
        {
            if (s.phase == (byte)Phase.Rally) Toast("탈출 준비 완료 · <b>1층 격납고 입구</b>로 이동해 대원과 합류하세요");
            if (s.phase == (byte)Phase.Cleared) { Banner("2층 클리어", "보조 우주선 격납고로 진입하세요", Green); FloorCleared?.Invoke(); }
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
        var title = Txt(card.transform, "Title", "2층 · 정비 및 화물 구역", 20, new Color(1, 1, 1, 0.9f), TextAlignmentOptions.TopLeft);
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
        string Done(bool d) => d ? "<color=#6EE7B7>■</color> " : "<color=#FCD34D>□</color> ";
        string line1 = Done(s.module) + (s.module ? "<color=#8A96A3>" : "") + "정비 보관실에서 보조 우주선용 전력 모듈 확보" + (s.module ? "  (" + s.carrier + ")</color>" : "");
        string fuelText = s.fuel == 0 ? "연료 공급실에서 연료 공급 장치 가동"
            : s.fuel == 1 ? "연료 공급 중  <b>" + Mathf.RoundToInt(s.fuelProgress * 100) + "%</b>" + (s.fuelPaused ? "  <color=#FCA5A5>(연료 공급실에 대원 필요)</color>" : "  <color=#FCA5A5>소음 주의</color>")
            : "<color=#8A96A3>연료 공급 완료</color>";
        string line2 = Done(s.fuel == 2) + fuelText;
        string line3 = (s.trace ? "<color=#6EE7B7>■</color> <color=#8A96A3>" : "<color=#7FA7B8>□</color> <color=#9FB3C2>") + "(선택) 검역 보관실의 침입 흔적 조사</color>";
        string text = line1 + "\n" + line2 + "\n" + line3;
        if (s.phase == (byte)Phase.Rally) text += "\n<color=#FCD34D>▶</color> <b>1층 격납고 입구로 이동해 합류</b>  " + s.rallyIn + " / " + s.rallyNeed + "명";
        else if (s.phase == (byte)Phase.Cleared) text += "\n<color=#6EE7B7>■ 2층 클리어</color>";
        else if (s.phase == (byte)Phase.Failed) text += "\n<color=#FCA5A5>■ 임무 실패</color>";
        _objectives.text = text;
        var size = _objectives.rectTransform.parent as RectTransform;
        if (size != null) size.sizeDelta = new Vector2(560, s.phase == (byte)Phase.Objectives ? 160 : 192);

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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f); Gizmos.DrawWireCube(fuelRoom.center, fuelRoom.size);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f); Gizmos.DrawWireCube(rallyZone.center, rallyZone.size);
    }
}
