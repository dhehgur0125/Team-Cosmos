using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 설정 창·Start 화면·ESC 메뉴를 코드로 만들 때 쓰는 도구 (로비와 같은 색: LobbyStyle).
/// 씬에 UI를 미리 놓지 않고 실행할 때 만들어서, 층 씬을 다시 만들어도(메뉴 9) 사라지지 않는다.
/// </summary>
public static class UiKit
{
    static TMP_FontAsset _font;

    /// <summary>한글 글꼴 (NotoSansKR SDF, TMP 설정의 대체 글꼴에서 찾는다)</summary>
    public static TMP_FontAsset Font
    {
        get
        {
            if (_font != null) return _font;
            var fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks != null)
                foreach (var f in fallbacks)
                    if (f != null && f.name.Contains("Noto")) { _font = f; break; }
            if (_font == null) _font = TMP_Settings.defaultFontAsset;
            return _font;
        }
    }

    public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.72f);
    public static readonly Color Window = new Color(0.06f, 0.072f, 0.09f, 1f);
    public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);

    public static RectTransform NewCanvas(string name, int order, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return (RectTransform)go.transform;
    }

    public static RectTransform NewRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static RectTransform Fill(this RectTransform r, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
        return r;
    }

    /// <summary>anchor·pivot을 한 점에 두고 크기·위치 지정</summary>
    public static RectTransform Place(this RectTransform r, Vector2 anchor, Vector2 position, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = anchor;
        r.anchoredPosition = position; r.sizeDelta = size;
        return r;
    }

    public static Image NewImage(Transform parent, string name, Color color)
    {
        var r = NewRect(parent, name);
        r.gameObject.AddComponent<CanvasRenderer>();
        var img = r.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static TextMeshProUGUI NewText(Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var r = NewRect(parent, "Text");
        r.gameObject.AddComponent<CanvasRenderer>();
        var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = Font; t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
        t.richText = true; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    public static Button NewButton(Transform parent, string label, float fontSize, Action onClick, out TextMeshProUGUI text)
    {
        var img = NewImage(parent, "Button " + label, Color.white);
        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        SetColor(button, LobbyStyle.ButtonDark);
        if (onClick != null) button.onClick.AddListener(() => onClick());
        text = NewText(img.transform, label, fontSize, LobbyStyle.Text, TextAlignmentOptions.Center);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.Fill(8, 2, 8, 2);
        return button;
    }

    public static Button NewButton(Transform parent, string label, float fontSize, Action onClick)
        => NewButton(parent, label, fontSize, onClick, out _);

    /// <summary>버튼 기본 색 (마우스를 올리면 밝게, 누르면 빨강)</summary>
    public static void SetColor(Button b, Color normal)
    {
        var c = b.colors;
        c.normalColor = normal;
        c.selectedColor = normal;
        c.highlightedColor = Color.Lerp(normal, Color.white, 0.18f);
        c.pressedColor = LobbyStyle.AccentDark;
        c.disabledColor = new Color(normal.r * 0.6f, normal.g * 0.6f, normal.b * 0.6f, 0.6f);
        c.colorMultiplier = 1f; c.fadeDuration = 0.08f;
        b.colors = c;
    }

    /// <summary>가로 막대(0~1 값 등). onChanged는 사용자가 움직일 때만 불린다.</summary>
    public static Slider NewSlider(Transform parent, float min, float max, float value, bool whole, Action<float> onChanged)
    {
        var root = NewRect(parent, "Slider");
        var slider = root.gameObject.AddComponent<Slider>();

        var bg = NewImage(root, "Track", new Color(1f, 1f, 1f, 0.1f));
        bg.rectTransform.anchorMin = new Vector2(0, 0.5f); bg.rectTransform.anchorMax = new Vector2(1, 0.5f);
        bg.rectTransform.sizeDelta = new Vector2(0, 8); bg.raycastTarget = true;

        var fillArea = NewRect(root, "Fill Area");
        fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
        fillArea.sizeDelta = new Vector2(0, 8);
        var fill = NewImage(fillArea, "Fill", LobbyStyle.Accent);
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1); fill.rectTransform.sizeDelta = Vector2.zero;
        fill.raycastTarget = false;

        var handleArea = NewRect(root, "Handle Area");
        handleArea.anchorMin = new Vector2(0, 0.5f); handleArea.anchorMax = new Vector2(1, 0.5f);
        handleArea.sizeDelta = new Vector2(-22, 26);   // 손잡이 높이 26, 양 끝 11씩 안쪽
        var handle = NewImage(handleArea, "Handle", LobbyStyle.Text);
        handle.rectTransform.sizeDelta = new Vector2(18, 0);

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = whole;
        slider.SetValueWithoutNotify(value);
        var colors = slider.colors;
        colors.highlightedColor = new Color(1f, 0.85f, 0.8f); colors.pressedColor = LobbyStyle.Accent;
        slider.colors = colors;
        if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
        return slider;
    }

    /// <summary>클릭을 받을 EventSystem이 없으면 지금 씬에 만든다 (층 씬에는 없다)</summary>
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    /// <summary>게임 끄기 (에디터에서는 실행만 멈춤)</summary>
    public static void QuitGame()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
