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
///            무전기를 손에 들고 V(또는 좌클릭)를 누르고 있는 동안 거리와 상관없이 무전 채널 전체에 말한다.
///  - M: 내 마이크 끄기/켜기
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
    public AudioFadeModel fadeModel = AudioFadeModel.InverseByDistance;

    [Header("UI")]
    public TMP_FontAsset font;
    [Tooltip("무전 송신 시작/종료 효과음 볼륨")]
    [Range(0, 1)] public float radioClickVolume = 0.35f;

    VoiceChatService _voice;
    FpsNetworkPlayer _player;
    FpsRadioView _radioView;
    bool _entered, _radioJoined, _transmitting, _wantRadio;
    float _radioToggleAt, _nextPosition, _nextHud, _meter;
    readonly List<string> _speakers = new List<string>();
    readonly HashSet<string> _radioSpeakersBefore = new HashSet<string>();
    AudioSource _sfx;
    AudioClip _clickOn, _clickOff, _squelch;

    // HUD
    Image _dot;
    TMP_Text _mode, _sub, _mic, _speakingText, _hint;
    Image[] _meterSegs;

    async void Start()
    {
        _voice = VoiceChatService.Instance;
        BuildHud();
        BuildSounds();
        SetHud("음성 연결 중...", "Vivox에 로그인하는 중", new Color(0.99f, 0.88f, 0.28f));

        string key = _voice.SessionKey;
        if (string.IsNullOrEmpty(key)) key = FindSessionId();
        var props = new Channel3DProperties(audibleDistance, Mathf.Min(conversationalDistance, audibleDistance - 1), fadeIntensity, fadeModel);
        await _voice.EnterGameAsync(key, props);
        _entered = true;
    }

    static string FindSessionId()
    {
        try
        {
            foreach (var kv in MultiplayerService.Instance.Sessions) return kv.Key;
        }
        catch (System.Exception) { }
        return "";
    }

    void OnDestroy()
    {
        if (_voice != null && VoiceChatService.Exists)
        {
            _ = _voice.LeaveAsync(_voice.NearChannel);
            _ = _voice.LeaveAsync(_voice.RadioChannel);
        }
        if (_player != null) _player.SetRadioTalking(false);
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

        if (_player != null && _entered)
        {
            var inv = _player.Inventory;
            bool alive = inv != null && inv.IsAlive;
            bool hasRadio = inv != null && inv.HasItem(FarmingItemKind.Radio);
            bool radioInHand = alive && inv.ItemAt(inv.SelectedIndex) == FarmingItemKind.Radio;

            UpdateRadioChannel(hasRadio);

            bool pttHeld = CursorLocked() && (KeyHeld(KeyV) || (_player.PlayerInput != null && _player.PlayerInput.AttackHeld));
            bool wantTx = radioInHand && _radioJoined && pttHeld;
            if (wantTx != _transmitting) SetTransmitting(wantTx);

            // 3D 위치: 내 머리(카메라)를 말하는 위치이자 듣는 위치로
            if (Time.unscaledTime >= _nextPosition)
            {
                _nextPosition = Time.unscaledTime + 0.1f;
                var cam = _player.ViewCamera;
                _voice.Set3DPosition(cam != null ? cam.gameObject : _player.gameObject, _voice.NearChannel);
            }

            RadioSquelch(hasRadio);
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
        // 무전 중에는 근거리 + 무전 채널 모두로 말한다 (옆에 있는 대원에게도 직접 들림)
        if (on) _voice.SetTransmission(TransmissionMode.All);
        else _voice.SetTransmission(TransmissionMode.Single, _voice.NearChannel);
        if (_player != null) _player.SetRadioTalking(on);
        if (_radioView != null) _radioView.Talking = on;
        PlayClip(on ? _clickOn : _clickOff);
    }

    /// <summary>다른 대원이 무전을 시작하면 짧은 잡음 소리</summary>
    void RadioSquelch(bool hasRadio)
    {
        if (!hasRadio || !_voice.IsInChannel(_voice.RadioChannel)) { _radioSpeakersBefore.Clear(); return; }
        _voice.GetSpeakers(_voice.RadioChannel, _speakers);
        foreach (var id in _speakers) if (!_radioSpeakersBefore.Contains(id)) { PlayClip(_squelch); break; }
        _radioSpeakersBefore.Clear();
        foreach (var id in _speakers) _radioSpeakersBefore.Add(id);
    }

    // ------------------------------------------------------------------ input helpers
    const int KeyV = 0, KeyM = 1;
    static bool KeyHeld(int key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        return key == KeyV ? kb.vKey.isPressed : kb.mKey.isPressed;
#else
        return Input.GetKey(key == KeyV ? KeyCode.V : KeyCode.M);
#endif
    }
    static bool KeyDown(int key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        return key == KeyV ? kb.vKey.wasPressedThisFrame : kb.mKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(key == KeyV ? KeyCode.V : KeyCode.M);
#endif
    }
    static bool CursorLocked() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused;

    // ------------------------------------------------------------------ sounds
    void BuildSounds()
    {
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false; _sfx.spatialBlend = 0; _sfx.volume = radioClickVolume;
        _clickOn = Tone("RadioOn", new[] { 1250f, 1650f }, 0.07f);
        _clickOff = Tone("RadioOff", new[] { 1650f, 1100f }, 0.08f);
        _squelch = Noise("RadioSquelch", 0.16f);
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

    static AudioClip Noise(string name, float seconds)
    {
        int rate = 44100, n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(7);
        float prev = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float env = Mathf.Clamp01(t * 30f) * (1 - t);
            float white = (float)(rng.NextDouble() * 2 - 1);
            prev = Mathf.Lerp(prev, white, 0.35f);   // 살짝 둥글게 (치익)
            data[i] = prev * 0.35f * env;
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
        float target = _voice.MicMuted ? 0 : _voice.SelfEnergy(_voice.NearChannel);
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
        bool hasRadio = inv != null && inv.HasItem(FarmingItemKind.Radio);
        bool inHand = inv != null && inv.ItemAt(inv.SelectedIndex) == FarmingItemKind.Radio;

        if (_voice.State == VoiceChatService.Status.Error && !_voice.IsInChannel(_voice.NearChannel))
            SetHud("음성 연결 실패", string.IsNullOrEmpty(_voice.LastError) ? "콘솔 로그를 확인해 주세요" : _voice.LastError, new Color(0.94f, 0.27f, 0.27f));
        else if (!_voice.IsInChannel(_voice.NearChannel))
            SetHud("음성 연결 중...", "Vivox 채널에 들어가는 중", new Color(0.99f, 0.88f, 0.28f));
        else if (_transmitting)
            SetHud("무전 송신 중", "무전기를 가진 모든 대원에게 들려요", new Color(0.99f, 0.65f, 0.65f));
        else if (inHand)
            SetHud("근거리 대화", _radioJoined ? "무전기를 들고 있어요 · V(또는 좌클릭)를 누르는 동안 무전" : "무전 채널에 연결하는 중...", new Color(0.43f, 0.91f, 0.72f));
        else if (hasRadio)
            SetHud("근거리 대화", "무전기 보유 · 무전이 들려요 (손에 들면 송신 가능)", new Color(0.43f, 0.91f, 0.72f));
        else
            SetHud("근거리 대화", "가까이 있는 대원에게만 들려요 · 무전기를 주우면 멀리서도 대화", new Color(0.43f, 0.91f, 0.72f));

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
        _hint.text = inHand ? "[V] 무전   [M] 마이크 " + (_voice.MicMuted ? "켜기" : "끄기") : "[M] 마이크 " + (_voice.MicMuted ? "켜기" : "끄기");
    }

    static string NameOf(string playerId)
    {
        foreach (var p in FpsNetworkPlayer.All)
            if (p != null && p.AuthPlayerId == playerId) return string.IsNullOrEmpty(p.Nickname) ? "대원" : p.Nickname;
        return "대원";
    }
}
