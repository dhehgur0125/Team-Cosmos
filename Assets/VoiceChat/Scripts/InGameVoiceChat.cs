using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Multiplayer;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;
using VoidLast.Player;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Stage2 멀티플레이 음성 채팅.
///  - 근거리 음성: Vivox 3D 채널. 내 카메라 위치를 계속 알려서 가까운 대원끼리만 들린다 (멀어질수록 작아짐).
///  - 무전기: 무전기 아이템을 가지고 있으면 무전 채널에 들어가 다른 대원의 무전을 듣는다.
///            무전기를 가지고 있으면 손에 무엇을 들었든(총·주사기·맨손) V를 누르고 있는 동안 무전 채널에 말한다.
///            (무전기를 손에 들고 있을 때는 좌클릭으로도 송신)
///            무전으로 말하는 동안 무전기를 가진 대원에게는 무전으로, 바로 옆(radioVoiceNearDistance) 대원에게는 육성으로 들린다.
///            그보다 멀리 있는 대원에게는 그 사람의 육성을 내 쪽에서 꺼서, 무전기가 없으면 무전 내용이 들리지 않는다.
///            새어 나감 방지: V를 누르면 먼저 '무전 중' 신호를 보내고 무전 채널로만 말하다가, 신호가 다른 대원에게 도착할 만큼
///            (네트워크 왕복 시간 기준) 기다린 뒤에 육성(근거리 채널)을 함께 켠다. 그래서 멀리 있는 대원이 음소거하기 전에 목소리가 새지 않는다.
///  - M: 내 마이크 끄기/켜기
///  - 내 대원이 죽으면 무전 채널에서 나가고, 죽은 대원끼리만 들리는 사망자 채널에만 말한다 (살아있는 대원에게는 안 들림).
///    근거리 채널에는 듣기만 하며 남아서, 관전 중인 대원의 머리 위치에서 듣는다 → 관전 대상의 목소리(와 그 주변 소리)가 들린다.
/// 화면 오른쪽 위에 음성 상태 HUD를 그린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class InGameVoiceChat : MonoBehaviour
{
    [Header("근거리 음성 (Vivox 3D)")]
    [Tooltip("이 거리(m)보다 멀면 들리지 않음")]
    [Min(5)] public int audibleDistance = 28;
    [Tooltip("이 거리(m) 안에서는 원래 크기로 들림")]
    [Min(1)] public int conversationalDistance = 3;
    [Range(0.1f, 3f)] public float fadeIntensity = 1f;
    [Tooltip("무전으로 말하는 대원의 육성이 들리는 거리(m). 이보다 멀면 무전기가 있어야 들림")]
    [Min(0.5f)] public float radioVoiceNearDistance = 4f;
    public AudioFadeModel fadeModel = AudioFadeModel.InverseByDistance;

    [Header("UI")]
    public TMP_FontAsset font;
    [Tooltip("무전 송신 시작/종료 효과음 볼륨")]
    [Range(0, 1)] public float radioClickVolume = 0.35f;

    VoiceChatService _voice;
    FpsNetworkPlayer _player;
    FpsRadioView _radioView;
    bool _entered, _radioJoined, _transmitting, _wantRadio, _dead;
    float _radioToggleAt, _nextPosition, _nextHud, _meter;
    readonly List<string> _speakers = new List<string>();
    readonly HashSet<string> _nearMuted = new HashSet<string>();   // 무전 중이라 내 쪽에서 육성을 끈 대원
    bool _nearPending;      // 무전 시작 직후: 육성(근거리)은 아직 끈 상태
    float _nearOnAt;
    GameObject _listenPoint;   // 죽은 뒤 근거리 음성을 듣는 자리 (관전 대상 머리)
    AudioSource _sfx;
    AudioClip _clickOn, _clickOff;

    // HUD
    Image _dot;
    TMP_Text _mode, _sub, _mic, _speakingText, _hint;
    Image[] _meterSegs;

    async void Start()
    {
        _voice = VoiceChatService.Instance;
        // 무전으로 들리는 다른 대원 목소리에만 무전기 음질(치지직)을 입힌다 (근거리 육성은 그대로)
        if (GetComponent<RadioVoiceEffect>() == null) gameObject.AddComponent<RadioVoiceEffect>();
        BuildHud();
        BuildSounds();
        SetHud("음성 연결 중...", "Vivox에 로그인하는 중", new Color(0.99f, 0.88f, 0.28f));

        string key = FindSessionId(_voice.SessionKey);
        // 앞 층에서 무전 채널에 들어가 있었으면 그 상태로 시작한다 (무전기를 잃었으면 UpdateRadioChannel이 나간다)
        _radioJoined = _wantRadio = _voice.IsWanted(_voice.RadioChannel);
        // 죽은 채로 다음 층에 내려왔으면 근거리 채널에 다시 들어가지 않고 사망자 채널로 바로
        if (FloorCarryOver.Pending && FloorCarryOver.HasPlayerState && FloorCarryOver.Dead && !string.IsNullOrEmpty(_voice.SessionKey))
        {
            _dead = true;
            _radioJoined = _wantRadio = false;   // 사망자는 무전 채널에서 나간다 → 구출되면 무전기가 있을 때 다시 들어감
            await _voice.EnterDeadAsync(NearProperties());
            _entered = true;
            return;
        }
        await _voice.EnterGameAsync(key, NearProperties());
        if (_radioJoined && !_voice.IsWanted(_voice.RadioChannel)) _radioJoined = false;   // 방이 바뀌어 채널을 모두 나왔으면 다시 들어간다
        _entered = true;
    }

    Channel3DProperties NearProperties() => new Channel3DProperties(audibleDistance, Mathf.Min(conversationalDistance, audibleDistance - 1), fadeIntensity, fadeModel);

    /// <summary>
    /// 음성 채널 이름에 쓸 방 ID: 지금 들어가 있는 방(세션)을 우선한다. 음성 쪽에 기억된 방이 실제로 들어가 있는 방이면 그대로.
    /// (기억된 값이 예전 방이면 방장·참가자가 서로 다른 채널에 들어가 아무 소리도 안 들린다)
    /// 주의: MultiplayerService.Sessions의 키는 방 ID가 아니라 PC마다 다른 '세션 종류' 이름이다 → 반드시 값의 Id를 쓴다.
    /// (예전엔 키를 써서, 기억된 방이 없을 때(다시 참가·게임 재시작) 혼자 다른 채널에 들어가 근거리·무전이 모두 안 들렸다)
    /// </summary>
    static string FindSessionId(string remembered)
    {
        string first = null, seen = "";
        try
        {
            foreach (var kv in MultiplayerService.Instance.Sessions)
            {
                string id = kv.Value?.Id;
                seen += kv.Key + "→" + (id ?? "null") + "(" + kv.Value?.State + ") ";
                if (string.IsNullOrEmpty(id)) continue;
                if (id == remembered) return id;
                if (first == null) first = id;
            }
        }
        catch (System.Exception e) { seen += "오류 " + e.Message; }
        if (first != null) return first;
        if (!string.IsNullOrEmpty(remembered)) return remembered;
        // 다시 참가 표에 적힌 방 ID (게임을 껐다 켜서 다시 참가했을 때)
        if (Rejoin.TryLoadTicket(out var ticket) && !string.IsNullOrEmpty(ticket.sessionId))
        {
            Debug.Log("[Voice] 들어가 있는 방 정보가 없어 다시 참가 표의 방 ID를 씀: " + ticket.sessionId + " (보인 방: " + seen + ")");
            return ticket.sessionId;
        }
        Debug.LogWarning("[Voice] 음성 채널에 쓸 방 ID를 찾지 못함: " + seen);
        return "";
    }

    void OnDestroy()
    {
        if (_voice != null && VoiceChatService.Exists)
        {
            // 무전 때문에 내 쪽에서 꺼 둔 목소리는 다시 켜 둔다 (다음 씬에서 기억하지 못하므로)
            foreach (var id in _nearMuted) _voice.SetLocalMute(_voice.NearChannel, id, false);
            _nearMuted.Clear();
            // 아래층으로 이동하는 중이면 채널을 그대로 유지한다 (다음 층 씬에서 이어서 사용)
            if (!FloorCarryOver.Transitioning)
            {
                _ = _voice.LeaveAsync(_voice.NearChannel);
                _ = _voice.LeaveAsync(_voice.RadioChannel);
                _ = _voice.LeaveAsync(_voice.DeadChannel);
            }
        }
        if (_player != null) _player.SetRadioTalking(false);
        if (_listenPoint != null) Destroy(_listenPoint);
    }

    void OnApplicationFocus(bool focus)
    {
        if (!focus && _transmitting) SetTransmitting(false);
    }

    void Update()
    {
        if (_voice == null) return;
        if (_player == null)
        {
            _player = FpsNetworkPlayer.Local;
            if (_player != null) _radioView = _player.GetComponent<FpsRadioView>();
        }

        // M: 마이크 끄기/켜기
        if (KeyDown(KeyM) && CursorLocked()) _voice.SetMicMuted(!_voice.MicMuted);

        // 죽으면 사망자 채널로 (한 번만)
        if (_player != null && _entered && !_dead && _player.Health != null && _player.Health.IsDead)
        {
            _dead = true;
            if (_transmitting) SetTransmitting(false);
            _radioJoined = false; _wantRadio = false;
            _ = _voice.EnterDeadAsync(NearProperties());
        }
        // 고치에서 구출되면 근거리 채널로 돌아간다
        else if (_player != null && _entered && _dead && _player.Health != null && !_player.Health.IsDead)
        {
            _dead = false;
            _ = _voice.LeaveDeadAsync(NearProperties());
        }

        if (_player != null && _entered && !_dead)
        {
            var inv = _player.Inventory;
            bool alive = inv != null && inv.IsAlive;
            // 무전기: 4~6번 칸에 주웠거나 받은 무전기, 또는 3번 칸의 고유 아이템 무전
            var unique = _player.UniqueItem;
            bool hasRadio = inv != null && (inv.HasItem(FarmingItemKind.Radio) || unique != null && unique.Has(UniqueItemKind.Radio));
            bool radioInHand = alive && (inv.ItemAt(inv.SelectedIndex) == FarmingItemKind.Radio || unique != null && unique.InHand(UniqueItemKind.Radio));

            // 층에 막 도착해 아이템을 되돌리는 중에는 무전기가 잠깐 없는 것처럼 보이므로 채널을 건드리지 않는다
            if (!FloorCarryOver.Pending) UpdateRadioChannel(hasRadio);

            bool pttHeld = CursorLocked() && (KeyHeld(KeyV) || (radioInHand && _player.PlayerInput != null && _player.PlayerInput.AttackHeld));
            // 무전 채널에 실제로 들어가 있을 때만 무전으로 말한다
            // (예전엔 '들어가려고 함'만 보고 송신을 바꿔서, 채널에 못 들어갔으면 V를 누르는 동안 아무 데도 말이 안 나갔다)
            bool wantTx = alive && hasRadio && _radioJoined && pttHeld && _voice.IsInChannel(_voice.RadioChannel);
            if (wantTx != _transmitting) SetTransmitting(wantTx);
            // 무전 신호가 다른 대원에게 도착했을 시간이 지나면 육성도 함께 내보낸다
            if (_transmitting && _nearPending && Time.unscaledTime >= _nearOnAt)
            {
                _nearPending = false;
                _voice.SetTransmission(TransmissionMode.All);
            }

            // 3D 위치: 내 머리(카메라)를 말하는 위치이자 듣는 위치로
            if (Time.unscaledTime >= _nextPosition)
            {
                _nextPosition = Time.unscaledTime + 0.1f;
                var cam = _player.ViewCamera;
                _voice.Set3DPosition(cam != null ? cam.gameObject : _player.gameObject, _voice.NearChannel);
            }

            UpdateRadioVoiceMutes(MyHead());
        }
        else if (_player != null && _entered && _dead)
        {
            // 죽은 뒤: 근거리 채널에서 듣기만 한다. 듣는 위치 = 관전 중인 대원의 머리 (관전 전이면 내 몸 자리)
            Vector3 ear = ListenPosition(out Quaternion facing);
            if (Time.unscaledTime >= _nextPosition)
            {
                _nextPosition = Time.unscaledTime + 0.1f;
                if (_listenPoint == null) _listenPoint = new GameObject("Voice Listen Point (Spectator)") { hideFlags = HideFlags.HideInHierarchy };
                _listenPoint.transform.SetPositionAndRotation(ear, facing);
                _voice.Set3DPosition(_listenPoint, _voice.NearChannel);
            }
            UpdateRadioVoiceMutes(ear);
        }

        UpdateHud();
    }

    // ------------------------------------------------------------------ radio
    void UpdateRadioChannel(bool hasRadio)
    {
        if (hasRadio != _wantRadio) { _wantRadio = hasRadio; _radioToggleAt = Time.unscaledTime + (hasRadio ? 0f : 0.6f); }
        if (Time.unscaledTime < _radioToggleAt || _wantRadio == _radioJoined) return;
        _radioJoined = _wantRadio;
        if (_radioJoined) _ = _voice.JoinGroupAsync(_voice.RadioChannel);
        else
        {
            if (_transmitting) SetTransmitting(false);
            _ = _voice.LeaveAsync(_voice.RadioChannel);
        }
    }

    void SetTransmitting(bool on)
    {
        _transmitting = on;
        // 무전 중에는 근거리 + 무전 채널 모두로 말한다.
        // 근거리 채널 육성은 듣는 쪽에서 바로 옆(radioVoiceNearDistance) 대원만 들리도록 거른다 (UpdateRadioVoiceMutes).
        // 시작 직후에는 듣는 쪽이 아직 '무전 중'을 모를 수 있으므로 잠깐 무전 채널로만 말한다.
        if (on)
        {
            _voice.SetTransmission(TransmissionMode.Single, _voice.RadioChannel);
            _nearPending = true;
            _nearOnAt = Time.unscaledTime + NearVoiceDelay();
        }
        else
        {
            _nearPending = false;
            _voice.SetTransmission(TransmissionMode.Single, _voice.NearChannel);
        }
        if (_player != null) _player.SetRadioTalking(on);
        if (_radioView != null) _radioView.Talking = on;
        PlayClip(on ? _clickOn : _clickOff);
    }

    /// <summary>
    /// 듣는 쪽 처리: 무전으로 말하고 있는 대원이 바로 옆(radioVoiceNearDistance)보다 멀리 있으면
    /// 근거리 채널의 그 사람 목소리를 내 쪽에서 끈다 → 무전기가 없으면 무전 내용이 들리지 않는다 (무전기가 있으면 무전 채널로 들림).
    /// </summary>
    float _nextMuteRefresh;

    void UpdateRadioVoiceMutes(Vector3 me)
    {
        // 1초마다 꺼 둔 목소리를 다시 끈다: 상대가 채널에 다시 들어오면(다시 참가 등) Vivox 참가자가 새로 생겨 꺼 둔 게 풀린다
        bool refresh = Time.unscaledTime >= _nextMuteRefresh;
        if (refresh) _nextMuteRefresh = Time.unscaledTime + 1f;
        foreach (var p in FpsNetworkPlayer.All)
        {
            if (p == null || p == _player) continue;
            string id = p.AuthPlayerId;
            if (string.IsNullOrEmpty(id)) continue;
            Vector3 head = HeadOf(p);
            bool mute = p.IsRadioTalking && Vector3.Distance(me, head) > radioVoiceNearDistance;
            if (mute && refresh && _nearMuted.Contains(id)) { _voice.SetLocalMute(_voice.NearChannel, id, true); continue; }
            if (mute == _nearMuted.Contains(id)) continue;
            if (!_voice.SetLocalMute(_voice.NearChannel, id, mute) && mute) continue;   // 아직 채널에 없으면 다음에 다시 시도
            if (mute) _nearMuted.Add(id); else _nearMuted.Remove(id);
        }
    }

    Vector3 MyHead()
    {
        var cam = _player.ViewCamera;
        return cam != null ? cam.transform.position : _player.transform.position + Vector3.up * 1.6f;
    }

    static Vector3 HeadOf(FpsNetworkPlayer p) => p.cameraPivot != null ? p.cameraPivot.position : p.transform.position + Vector3.up * 1.6f;

    /// <summary>죽은 뒤 근거리 음성을 듣는 자리: 관전 중인 대원의 머리 (관전 카메라 방향으로 들음)</summary>
    Vector3 ListenPosition(out Quaternion facing)
    {
        var target = SpectatorSystem.IsSpectating ? SpectatorSystem.Target : null;
        if (target != null)
        {
            var view = Camera.main;
            facing = view != null ? view.transform.rotation : target.transform.rotation;
            return HeadOf(target);
        }
        facing = _player.transform.rotation;
        return MyHead();
    }

    /// <summary>
    /// '무전 중' 신호가 다른 대원에게 도착하기까지 걸리는 시간(초).
    /// 내 신호는 방장을 거쳐 다른 대원에게 가므로 왕복 시간(RTT) 정도 + 여유. (0.2~0.6초)
    /// </summary>
    static float NearVoiceDelay()
    {
        float rttMs = 0f;
        try
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            var transport = nm != null ? nm.NetworkConfig.NetworkTransport : null;
            if (transport != null)
            {
                if (nm.IsServer)
                {
                    foreach (var id in nm.ConnectedClientsIds)
                        if (id != Unity.Netcode.NetworkManager.ServerClientId) rttMs = Mathf.Max(rttMs, transport.GetCurrentRtt(id));
                }
                else rttMs = transport.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId) * 2f;   // 나→방장→다른 대원
            }
        }
        catch (System.Exception) { }
        return Mathf.Clamp(0.12f + rttMs / 1000f, 0.2f, 0.6f);
    }

    // ------------------------------------------------------------------ input helpers
    // 키는 설정 창(Key 탭)에서 바꿀 수 있다: 무전 = GameAction.Radio(기본 V), 마이크 = GameAction.MicMute(기본 M)
    const int KeyV = 0, KeyM = 1;
    static bool KeyHeld(int key) => GameKeys.Held(key == KeyV ? GameAction.Radio : GameAction.MicMute);
    static bool KeyDown(int key) => GameKeys.Down(key == KeyV ? GameAction.Radio : GameAction.MicMute);
    static bool CursorLocked() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused;

    // ------------------------------------------------------------------ sounds
    void BuildSounds()
    {
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false; _sfx.spatialBlend = 0; _sfx.volume = radioClickVolume;
        GameAudio.Route(_sfx);
        _clickOn = Tone("RadioOn", new[] { 1250f, 1650f }, 0.07f);
        _clickOff = Tone("RadioOff", new[] { 1650f, 1100f }, 0.08f);
    }

    void PlayClip(AudioClip clip) { if (_sfx != null && clip != null) _sfx.PlayOneShot(clip, radioClickVolume); }

    static AudioClip Tone(string name, float[] freqs, float seconds)
    {
        int rate = 44100, n = (int)(rate * seconds * freqs.Length);
        var data = new float[n];
        int per = n / freqs.Length;
        for (int i = 0; i < n; i++)
        {
            float f = freqs[Mathf.Min(freqs.Length - 1, i / per)];
            float t = (float)i / rate, local = (float)(i % per) / per;
            float env = Mathf.Clamp01(local * 20f) * Mathf.Clamp01((1 - local) * 12f);
            data[i] = Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f * env;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ------------------------------------------------------------------ HUD
    void BuildHud()
    {
        var canvasGo = new GameObject("VoiceHUD", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var card = NewImage(canvasGo.transform, "Card", new Color(0.063f, 0.07f, 0.1f, 0.84f));
        var cr = card.rectTransform;
        cr.anchorMin = cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(1, 1);
        cr.anchoredPosition = new Vector2(-28, -28); cr.sizeDelta = new Vector2(380, 206);

        _dot = NewImage(card.transform, "Dot", new Color(0.98f, 0.8f, 0.08f));
        Place(_dot.rectTransform, 20, 20, 10, 10);
        var title = NewText(card.transform, "Title", "VOICE", 13, new Color(1, 1, 1, 0.55f), TextAlignmentOptions.Left);
        title.characterSpacing = 8; title.fontStyle = FontStyles.Bold;
        Place(title.rectTransform, 38, 13, 120, 20);
        _mic = NewText(card.transform, "Mic", "MIC ON", 12, new Color(1, 1, 1, 0.85f), TextAlignmentOptions.Right);
        Place(_mic.rectTransform, 220, 13, 140, 20);

        _mode = NewText(card.transform, "Mode", "", 26, Color.white, TextAlignmentOptions.Left);
        _mode.fontStyle = FontStyles.Bold;
        Place(_mode.rectTransform, 20, 34, 340, 42);
        _sub = NewText(card.transform, "Sub", "", 13, new Color(1, 1, 1, 0.55f), TextAlignmentOptions.TopLeft);
        _sub.textWrappingMode = TextWrappingModes.Normal;
        Place(_sub.rectTransform, 20, 76, 340, 42);

        var meterLabel = NewText(card.transform, "MeterLabel", "MIC", 11, new Color(1, 1, 1, 0.5f), TextAlignmentOptions.Left);
        meterLabel.fontStyle = FontStyles.Bold;
        Place(meterLabel.rectTransform, 20, 122, 40, 16);
        _meterSegs = new Image[16];
        for (int i = 0; i < _meterSegs.Length; i++)
        {
            _meterSegs[i] = NewImage(card.transform, "Seg" + i, new Color(1, 1, 1, 0.1f));
            Place(_meterSegs[i].rectTransform, 60 + i * 18.75f, 124, 15, 12);
        }

        _speakingText = NewText(card.transform, "Speaking", "", 14, new Color(0.43f, 0.91f, 0.72f), TextAlignmentOptions.TopLeft);
        _speakingText.textWrappingMode = TextWrappingModes.Normal;
        Place(_speakingText.rectTransform, 20, 146, 340, 24);
        _hint = NewText(card.transform, "Hint", "", 12, new Color(1, 1, 1, 0.6f), TextAlignmentOptions.Left);
        Place(_hint.rectTransform, 20, 178, 340, 20);
    }

    Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color; img.raycastTarget = false;
        return img;
    }

    TMP_Text NewText(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
        t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
    }

    void SetHud(string mode, string sub, Color color)
    {
        if (_mode == null) return;
        _mode.text = mode; _mode.color = color;
        _sub.text = sub;
        _dot.color = color;
    }

    void UpdateHud()
    {
        if (_mode == null) return;
        // 마이크 미터는 매 프레임
        float target = _voice.MicMuted ? 0 : _voice.SelfEnergy(_dead ? _voice.DeadChannel : _voice.NearChannel);
        _meter = Mathf.Lerp(_meter, target, 1 - Mathf.Exp(-(target > _meter ? 30f : 8f) * Time.unscaledDeltaTime));
        int lit = Mathf.RoundToInt(_meter * _meterSegs.Length);
        for (int i = 0; i < _meterSegs.Length; i++)
        {
            bool on = i < lit;
            _meterSegs[i].color = !on ? new Color(1, 1, 1, 0.1f) : i < 10 ? new Color(0.2f, 0.83f, 0.6f) : i < 13 ? new Color(0.98f, 0.8f, 0.08f) : new Color(0.97f, 0.44f, 0.44f);
        }

        if (Time.unscaledTime < _nextHud) return;
        _nextHud = Time.unscaledTime + 0.15f;

        _mic.text = _voice.MicMuted ? "<color=#F87171>MIC OFF</color>" : "MIC ON";
        var inv = _player != null ? _player.Inventory : null;
        var unique = _player != null ? _player.UniqueItem : null;
        bool hasRadio = inv != null && (inv.HasItem(FarmingItemKind.Radio) || unique != null && unique.Has(UniqueItemKind.Radio));

        if (_dead)
        {
            if (!_voice.IsInChannel(_voice.DeadChannel)) SetHud("사망자 대화 연결 중...", "죽은 대원끼리만 대화할 수 있어요", new Color(0.99f, 0.88f, 0.28f));
            else SetHud("사망자 대화", "내 목소리는 죽은 대원끼리만 들려요 · 관전 중인 대원의 목소리가 들려요", new Color(0.75f, 0.68f, 1f));
            var dead = new List<string>();
            _voice.GetSpeakers(_voice.DeadChannel, _speakers);
            foreach (var id in _speakers) dead.Add(NameOf(id));
            _voice.GetSpeakers(_voice.NearChannel, _speakers);   // 관전 대상 근처에서 말하는 살아있는 대원
            foreach (var id in _speakers) if (!_nearMuted.Contains(id)) dead.Add(NameOf(id) + " <color=#6EE7B7>(관전)</color>");
            _speakingText.text = dead.Count > 0 ? "말하는 중: " + string.Join(", ", dead) : "";
            _hint.text = "[" + GameKeys.Label(GameAction.MicMute) + "] 마이크 " + (_voice.MicMuted ? "켜기" : "끄기");
            return;
        }
        if (_voice.State == VoiceChatService.Status.Error && !_voice.IsInChannel(_voice.NearChannel))
            SetHud("음성 다시 연결 중...", string.IsNullOrEmpty(_voice.LastError) ? "연결이 끊겨 잠시 뒤 자동으로 다시 들어가요" : "연결 실패: " + _voice.LastError, new Color(0.94f, 0.27f, 0.27f));
        else if (!_voice.IsInChannel(_voice.NearChannel))
            SetHud("음성 연결 중...", "Vivox 채널에 들어가는 중", new Color(0.99f, 0.88f, 0.28f));
        else if (_transmitting)
            SetHud("무전 송신 중", "무전기를 가진 대원 + 바로 옆(" + radioVoiceNearDistance.ToString("0.#") + "m) 대원에게 들려요", new Color(0.99f, 0.65f, 0.65f));
        else if (hasRadio)
            SetHud("근거리 대화", _voice.IsInChannel(_voice.RadioChannel) ? "무전기 보유 · V를 누르는 동안 무전 (무전기를 가진 대원 + 바로 옆 대원에게 들림)" : "무전 채널에 연결하는 중...", new Color(0.43f, 0.91f, 0.72f));
        else
            SetHud("근거리 대화", "가까이 있는 대원에게만 들려요 · 무전기를 받으면 멀리서도 대화", new Color(0.43f, 0.91f, 0.72f));

        // 말하는 사람 표시
        var parts = new List<string>();
        _voice.GetSpeakers(_voice.NearChannel, _speakers);
        foreach (var id in _speakers) parts.Add(NameOf(id));
        if (hasRadio)
        {
            _voice.GetSpeakers(_voice.RadioChannel, _speakers);
            foreach (var id in _speakers) parts.Add(NameOf(id) + " <color=#FCA5A5>(무전)</color>");
        }
        _speakingText.text = parts.Count > 0 ? "말하는 중: " + string.Join(", ", parts) : "";
        string mic = "[" + GameKeys.Label(GameAction.MicMute) + "] 마이크 " + (_voice.MicMuted ? "켜기" : "끄기");
        _hint.text = hasRadio ? "[" + GameKeys.Label(GameAction.Radio) + "] 무전   " + mic : mic;
    }

    static string NameOf(string playerId)
    {
        foreach (var p in FpsNetworkPlayer.All)
            if (p != null && p.AuthPlayerId == playerId) return string.IsNullOrEmpty(p.Nickname) ? "대원" : p.Nickname;
        return "대원";
    }
}
