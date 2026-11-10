using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 설정 창 (Start 화면의 Setting, 게임 중 ESC 메뉴의 설정).
/// 위쪽 탭 Sound | Key | Graphic 중 누른 탭의 설정만 보인다. 바꾸면 바로 적용·저장된다.
///  - Sound: 전체·배경음·효과음·음성 채팅·마이크 (GameSettings → GameAudio)
///  - Key: 마우스 감도 + 기능별 키 (GameKeys). 키 버튼을 누르고 새 키를 누르면 바뀐다(ESC 취소). 겹치면 서로 바뀐다.
///  - Graphic: 자동 / 낮음 / 높음 (GraphicsQuality)
/// ESC: 키 입력 대기 중이면 취소, 아니면 창 닫기.
/// </summary>
public sealed class SettingsWindow : MonoBehaviour
{
    public enum Tab { Sound, Key, Graphic }

    public static bool IsOpen => _open != null;
    /// <summary>이 프레임에 설정 창이 ESC를 썼다 (다른 메뉴가 같은 ESC로 또 반응하지 않게)</summary>
    public static int EscUsedFrame = -1;
    static SettingsWindow _open;

    Action _onClosed;
    Tab _tab;
    RectTransform _content;
    readonly Button[] _tabButtons = new Button[3];
    TextMeshProUGUI _message;
    float _messageUntil;

    // 키 탭
    GameAction? _listening;
    readonly Dictionary<GameAction, TextMeshProUGUI> _keyLabels = new Dictionary<GameAction, TextMeshProUGUI>();
    readonly Dictionary<GameAction, Button> _keyButtons = new Dictionary<GameAction, Button>();

    /// <summary>parent(캔버스) 위에 설정 창을 연다. 닫히면 onClosed.</summary>
    public static SettingsWindow Open(RectTransform parent, Action onClosed, Tab tab = Tab.Sound)
    {
        if (_open != null) return _open;
        UiKit.EnsureEventSystem();
        var backdrop = UiKit.NewImage(parent, "Settings Window", UiKit.Backdrop);
        backdrop.rectTransform.Fill();
        var w = backdrop.gameObject.AddComponent<SettingsWindow>();
        w._onClosed = onClosed;
        w.Build(tab);
        _open = w;
        return w;
    }

    public void Close()
    {
        if (_listening != null) StopListening();
        GameSettings.Save();
        if (_open == this) _open = null;
        Destroy(gameObject);
        _onClosed?.Invoke();
    }

    void OnDestroy()
    {
        if (_open == this) _open = null;
        if (_listening != null) GameKeys.Listening = false;
    }

    // ------------------------------------------------------------------ 틀
    void Build(Tab tab)
    {
        var window = UiKit.NewImage(transform, "Window", UiKit.Window);
        window.rectTransform.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 780));
        window.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        var accent = UiKit.NewImage(window.transform, "Accent", LobbyStyle.Accent);
        accent.rectTransform.anchorMin = new Vector2(0, 1); accent.rectTransform.anchorMax = new Vector2(1, 1);
        accent.rectTransform.pivot = new Vector2(0.5f, 1); accent.rectTransform.sizeDelta = new Vector2(0, 4);

        var title = UiKit.NewText(window.transform, "설정", 40, LobbyStyle.Text);
        title.fontStyle = FontStyles.Bold;
        title.rectTransform.Place(new Vector2(0, 1), new Vector2(48, -30), new Vector2(300, 56));

        var close = UiKit.NewButton(window.transform, "X", 28, Close);
        ((RectTransform)close.transform).Place(new Vector2(1, 1), new Vector2(-28, -28), new Vector2(60, 60));

        // 탭 버튼
        var tabs = UiKit.NewRect(window.transform, "Tabs");
        tabs.anchorMin = new Vector2(0, 1); tabs.anchorMax = new Vector2(1, 1); tabs.pivot = new Vector2(0.5f, 1);
        tabs.offsetMin = new Vector2(48, -166); tabs.offsetMax = new Vector2(-48, -104);
        var layout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        string[] names = { "Sound", "Key", "Graphic" };
        for (int i = 0; i < 3; i++)
        {
            var t = (Tab)i;
            _tabButtons[i] = UiKit.NewButton(tabs, names[i], 26, () => ShowTab(t));
        }
        var line = UiKit.NewImage(window.transform, "Line", UiKit.Line);
        line.rectTransform.anchorMin = new Vector2(0, 1); line.rectTransform.anchorMax = new Vector2(1, 1);
        line.rectTransform.pivot = new Vector2(0.5f, 1);
        line.rectTransform.offsetMin = new Vector2(48, -184); line.rectTransform.offsetMax = new Vector2(-48, -182);

        _content = UiKit.NewRect(window.transform, "Content").Fill(48, 112, 48, 204);

        // 아래: 안내 문구 · 기본값으로 · 닫기
        _message = UiKit.NewText(window.transform, "", 20, LobbyStyle.TextDim);
        _message.rectTransform.anchorMin = new Vector2(0, 0); _message.rectTransform.anchorMax = new Vector2(1, 0);
        _message.rectTransform.pivot = new Vector2(0.5f, 0);
        _message.rectTransform.offsetMin = new Vector2(48, 36); _message.rectTransform.offsetMax = new Vector2(-480, 92);
        _message.alignment = TextAlignmentOptions.MidlineLeft;

        var reset = UiKit.NewButton(window.transform, "기본값으로", 24, ResetTab);
        ((RectTransform)reset.transform).Place(new Vector2(1, 0), new Vector2(-260, 36), new Vector2(200, 60));
        var done = UiKit.NewButton(window.transform, "닫기", 24, Close);
        UiKit.SetColor(done, LobbyStyle.Accent);
        ((RectTransform)done.transform).Place(new Vector2(1, 0), new Vector2(-48, 36), new Vector2(200, 60));

        ShowTab(tab);
    }

    void ShowTab(Tab tab)
    {
        if (_listening != null) StopListening();
        _tab = tab;
        for (int i = 0; i < 3; i++) UiKit.SetColor(_tabButtons[i], i == (int)tab ? LobbyStyle.Accent : LobbyStyle.ButtonDark);
        for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
        _keyLabels.Clear(); _keyButtons.Clear();
        SetMessage("");
        if (tab == Tab.Sound) BuildSound();
        else if (tab == Tab.Key) BuildKeys();
        else BuildGraphic();
    }

    void ResetTab()
    {
        if (_tab == Tab.Sound) GameSettings.ResetSound();
        else if (_tab == Tab.Key) { GameKeys.ResetAll(); GameSettings.ResetSensitivity(); }
        else GraphicsQuality.SetMode(GraphicsQuality.Mode.Auto);
        ShowTab(_tab);
        SetMessage("이 탭의 설정을 처음 값으로 되돌렸어요.", 3f);
    }

    void SetMessage(string text, float seconds = 0f)
    {
        if (_message == null) return;
        _message.text = text;
        _messageUntil = seconds > 0 ? Time.unscaledTime + seconds : float.MaxValue;
    }

    // ------------------------------------------------------------------ Sound
    void BuildSound()
    {
        var list = VerticalList(_content, 18);
        foreach (GameSettings.Volume v in Enum.GetValues(typeof(GameSettings.Volume)))
        {
            var vol = v;
            SliderRow(list, GameSettings.VolumeName(v), GameSettings.VolumeHint(v), 0, 100,
                Mathf.Round(GameSettings.GetVolume(v) * 100f), true,
                value => GameSettings.SetVolume(vol, value / 100f), value => Mathf.RoundToInt(value).ToString());
        }
    }

    // ------------------------------------------------------------------ Key
    void BuildKeys()
    {
        var top = UiKit.NewRect(_content, "Sensitivity");
        top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
        top.offsetMin = new Vector2(0, -64); top.offsetMax = Vector2.zero;
        var sens = SliderRow(top, "마우스 감도", "시점 돌리는 빠르기 (기본 1.00)", GameSettings.MinSensitivity, GameSettings.MaxSensitivity,
            GameSettings.MouseSensitivity, false,
            value => GameSettings.MouseSensitivity = Mathf.Round(value * 20f) / 20f, value => (Mathf.Round(value * 20f) / 20f).ToString("0.00"));
        ((RectTransform)sens.transform.parent).Fill();

        var grid = UiKit.NewRect(_content, "Keys").Fill(0, 0, 0, 84);
        var columns = grid.gameObject.AddComponent<HorizontalLayoutGroup>();
        columns.spacing = 40; columns.childControlWidth = columns.childControlHeight = true;
        columns.childForceExpandWidth = columns.childForceExpandHeight = true;
        var left = VerticalList(UiKit.NewRect(grid, "Left"), 6, false);
        var right = VerticalList(UiKit.NewRect(grid, "Right"), 6, false);
        int half = (GameKeys.All.Length + 1) / 2;
        for (int i = 0; i < GameKeys.All.Length; i++) KeyRow(i < half ? left : right, GameKeys.All[i]);
        SetMessage("키 버튼을 누른 뒤 바꿀 키를 누르세요. 마우스 버튼·1~6·ESC는 바꿀 수 없어요.");
    }

    void KeyRow(RectTransform parent, GameAction action)
    {
        var row = UiKit.NewRect(parent, action.ToString());
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
        var name = UiKit.NewText(row, GameKeys.ActionName(action), 22, LobbyStyle.Text);
        name.alignment = TextAlignmentOptions.MidlineLeft;
        name.rectTransform.Fill(0, 0, 200, 0);
        var button = UiKit.NewButton(row, GameKeys.Label(action), 22, () => StartListening(action), out var label);
        var br = (RectTransform)button.transform;
        br.anchorMin = new Vector2(1, 0); br.anchorMax = new Vector2(1, 1); br.pivot = new Vector2(1, 0.5f);
        br.sizeDelta = new Vector2(190, 0); br.anchoredPosition = Vector2.zero;
        _keyLabels[action] = label; _keyButtons[action] = button;
    }

    void StartListening(GameAction action)
    {
        if (_listening != null) StopListening();
        _listening = action;
        GameKeys.Listening = true;
        _keyLabels[action].text = "<color=#E0443A>키를 누르세요</color>";
        UiKit.SetColor(_keyButtons[action], LobbyStyle.AccentDark);
        SetMessage($"'{GameKeys.ActionName(action)}'에 쓸 키를 누르세요. ESC = 취소");
    }

    void StopListening()
    {
        if (_listening is GameAction a && _keyButtons.TryGetValue(a, out var b)) UiKit.SetColor(b, LobbyStyle.ButtonDark);
        _listening = null;
        GameKeys.Listening = false;
        RefreshKeyLabels();
    }

    void RefreshKeyLabels()
    {
        foreach (var pair in _keyLabels) pair.Value.text = GameKeys.Label(pair.Key);
    }

    void Update()
    {
        if (_message != null && Time.unscaledTime > _messageUntil) SetMessage("");
        var kb = Keyboard.current;
        if (kb == null) return;

        if (_listening is GameAction action)
        {
            Key pressed = GameKeys.PressedThisFrame();
            if (pressed == Key.None) return;
            if (pressed == Key.Escape) { EscUsedFrame = Time.frameCount; StopListening(); SetMessage("취소했어요.", 2f); return; }
            if (!GameKeys.IsAllowed(pressed)) { SetMessage($"'{GameKeys.KeyName(pressed)}' 키는 쓸 수 없어요. 다른 키를 누르세요.", 3f); return; }
            var swapped = GameKeys.Set(action, pressed);
            StopListening();
            SetMessage(swapped is GameAction other
                ? $"'{GameKeys.ActionName(other)}'이(가) 쓰던 키라서 두 기능의 키를 서로 바꿨어요."
                : $"'{GameKeys.ActionName(action)}' = {GameKeys.KeyName(pressed)}", 4f);
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame && EscUsedFrame != Time.frameCount)
        {
            EscUsedFrame = Time.frameCount;
            Close();
        }
    }

    // ------------------------------------------------------------------ Graphic
    void BuildGraphic()
    {
        var list = VerticalList(_content, 22);
        var heading = UiKit.NewText(list, "그래픽 품질", 26, LobbyStyle.Text);
        heading.fontStyle = FontStyles.Bold;
        heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;

        var row = UiKit.NewRect(list, "Modes");
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        var modes = new[] { GraphicsQuality.Mode.Auto, GraphicsQuality.Mode.Low, GraphicsQuality.Mode.High };
        string[] labels = { "자동", "낮음", "높음" };
        var current = GraphicsQuality.Current;
        for (int i = 0; i < modes.Length; i++)
        {
            var mode = modes[i];
            var b = UiKit.NewButton(row, labels[i], 26, () =>
            {
                GraphicsQuality.SetMode(mode);
                ShowTab(Tab.Graphic);
                SetMessage("그래픽: " + GraphicsQuality.DisplayName + (mode == GraphicsQuality.Mode.Auto ? " (자동)" : "") + " 로 바꿨어요.", 3f);
            });
            UiKit.SetColor(b, mode == current ? LobbyStyle.Accent : LobbyStyle.ButtonDark);
        }

        string now = "지금: <b>" + GraphicsQuality.DisplayName + "</b>"
            + (GraphicsQuality.IsAuto ? "  <color=#8A949E>(자동으로 골라짐)</color>" : "");
        var info = UiKit.NewText(list, now, 24, LobbyStyle.Text);
        info.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;

        var desc = UiKit.NewText(list,
            "<b>낮음</b>  텍스처 절반 크기, 화면을 80% 해상도로 그린 뒤 선명하게 키움, 그림자 거리·품질과 화면 효과를 줄여 가볍게 돌아가요. 노트북·내장 그래픽에 알맞아요.\n" +
            "<b>높음</b>  원래 화질로 그려요.\n" +
            "<b>자동</b>  컴퓨터 사양을 보고 골라요. 이 컴퓨터는 <b>" + (GraphicsQuality.AutoPrefersLow() ? "낮음" : "높음") + "</b>으로 골라져요.\n" +
            "<color=#8A949E>그래픽 카드: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsMemorySize + "MB</color>",
            21, LobbyStyle.TextDim);
        desc.lineSpacing = 18;
        desc.gameObject.AddComponent<LayoutElement>().preferredHeight = 220;
    }

    // ------------------------------------------------------------------ 공통 줄
    static RectTransform VerticalList(RectTransform parent, float spacing, bool fill = true)
    {
        if (fill) parent = UiKit.NewRect(parent, "List").Fill();
        var v = parent.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing; v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;
        return parent;
    }

    /// <summary>[이름 + 설명] [막대] [숫자] 한 줄</summary>
    static Slider SliderRow(RectTransform parent, string name, string hint, float min, float max, float value, bool whole,
        Action<float> onChanged, Func<float, string> format)
    {
        var row = UiKit.NewRect(parent, name);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
        var label = UiKit.NewText(row, name + "\n<size=17><color=#8A949E>" + hint + "</color></size>", 24, LobbyStyle.Text);
        label.alignment = TextAlignmentOptions.MidlineLeft; label.lineSpacing = -10;
        label.rectTransform.anchorMin = new Vector2(0, 0); label.rectTransform.anchorMax = new Vector2(0, 1);
        label.rectTransform.pivot = new Vector2(0, 0.5f); label.rectTransform.sizeDelta = new Vector2(300, 0);
        label.rectTransform.anchoredPosition = Vector2.zero;

        var number = UiKit.NewText(row, format(value), 24, LobbyStyle.Text, TextAlignmentOptions.MidlineRight);
        number.rectTransform.anchorMin = new Vector2(1, 0); number.rectTransform.anchorMax = new Vector2(1, 1);
        number.rectTransform.pivot = new Vector2(1, 0.5f); number.rectTransform.sizeDelta = new Vector2(90, 0);
        number.rectTransform.anchoredPosition = Vector2.zero;

        var slider = UiKit.NewSlider(row, min, max, value, whole, v => { onChanged(v); number.text = format(v); });
        ((RectTransform)slider.transform).Fill(330, 0, 120, 0);
        return slider;
    }
}
