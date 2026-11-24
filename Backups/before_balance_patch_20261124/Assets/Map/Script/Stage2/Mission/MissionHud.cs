using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 층 임무 화면 표시 (실행 중 코드로 만든다): 왼쪽 목표 목록, 가운데 아래 상호작용 안내와 누르기 막대,
/// 위쪽 알림, 가운데 배너, 읽을거리 창. 2층(F2MissionDirector)과 같은 모양·위치.
/// </summary>
public sealed class MissionHud
{
    public static readonly Color Green = new Color(0.25f, 1f, 0.45f), Amber = new Color(1f, 0.62f, 0.12f), Red = new Color(1f, 0.18f, 0.12f), Cyan = new Color(0.3f, 0.85f, 1f);

    /// <summary>다른 화면 요소(지도 등)를 붙일 캔버스</summary>
    public Transform Root { get; }

    readonly TMP_FontAsset _font;
    readonly TMP_Text _objectives, _prompt, _toast, _bannerTitle, _bannerSub, _note;
    readonly Image _promptBar, _promptBarBg, _notePanel;
    readonly RectTransform _card;
    readonly CanvasGroup _bannerGroup, _toastGroup;
    readonly StringBuilder _lines = new StringBuilder();
    int _lineCount;
    float _toastUntil, _bannerUntil, _noteUntil;

    public MissionHud(Transform parent, TMP_FontAsset font, string title)
    {
        _font = font;
        var canvasGo = new GameObject("Mission HUD", typeof(RectTransform));
        canvasGo.transform.SetParent(parent, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        Root = canvasGo.transform;

        // 목표 (왼쪽, HP 막대 아래)
        var card = Img(Root, "Objectives", new Color(0.063f, 0.07f, 0.1f, 0.78f));
        _card = card.rectTransform;
        Anchor(_card, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -112), new Vector2(560, 190));
        var titleText = Txt(card.transform, "Title", title, 20, new Color(1, 1, 1, 0.9f), TextAlignmentOptions.TopLeft);
        titleText.fontStyle = FontStyles.Bold;
        Anchor(titleText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -12), new Vector2(520, 30));
        _objectives = Txt(card.transform, "List", "", 17, new Color(1, 1, 1, 0.85f), TextAlignmentOptions.TopLeft);
        _objectives.lineSpacing = 12f;
        Anchor(_objectives.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -48), new Vector2(530, 140));

        // 상호작용 안내 (화면 가운데 아래)
        _prompt = Txt(Root, "Prompt", "", 22, Color.white, TextAlignmentOptions.Center);
        Anchor(_prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(900, 34));
        _promptBarBg = Img(Root, "PromptBarBg", new Color(1, 1, 1, 0.15f));
        Anchor(_promptBarBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -118), new Vector2(260, 6));
        _promptBar = Img(_promptBarBg.transform, "Fill", Amber);
        _promptBar.rectTransform.anchorMin = _promptBar.rectTransform.anchorMax = new Vector2(0, 0.5f);
        _promptBar.rectTransform.pivot = new Vector2(0, 0.5f);
        _promptBar.rectTransform.anchoredPosition = Vector2.zero;
        _promptBarBg.gameObject.SetActive(false);

        // 알림 (위쪽 가운데)
        var toastBg = Img(Root, "Toast", new Color(0.05f, 0.06f, 0.08f, 0.82f));
        Anchor(toastBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(1100, 52));
        _toastGroup = toastBg.gameObject.AddComponent<CanvasGroup>(); _toastGroup.alpha = 0;
        _toast = Txt(toastBg.transform, "Text", "", 21, Color.white, TextAlignmentOptions.Center);
        Stretch(_toast.rectTransform);

        // 클리어/실패 배너 (가운데)
        var banner = Img(Root, "Banner", new Color(0, 0, 0, 0.6f));
        Anchor(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1920, 190));
        _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>(); _bannerGroup.alpha = 0;
        _bannerTitle = Txt(banner.transform, "Title", "", 64, Green, TextAlignmentOptions.Center);
        _bannerTitle.fontStyle = FontStyles.Bold; _bannerTitle.characterSpacing = 10;
        Anchor(_bannerTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 26), new Vector2(1600, 90));
        _bannerSub = Txt(banner.transform, "Sub", "", 24, new Color(1, 1, 1, 0.8f), TextAlignmentOptions.Center);
        Anchor(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(1600, 40));

        // 읽을거리 (기록, 상태 보고 등)
        _notePanel = Img(Root, "Note", new Color(0.03f, 0.05f, 0.07f, 0.9f));
        Anchor(_notePanel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(860, 320));
        _note = Txt(_notePanel.transform, "Text", "", 21, new Color(0.8f, 0.92f, 1f), TextAlignmentOptions.TopLeft);
        _note.textWrappingMode = TextWrappingModes.Normal;
        Stretch(_note.rectTransform); _note.rectTransform.offsetMin = new Vector2(32, 24); _note.rectTransform.offsetMax = new Vector2(-32, -24);
        _notePanel.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ 목표 목록 (매 프레임: Begin → Line… → End)
    public void BeginObjectives() { _lines.Clear(); _lineCount = 0; }

    /// <summary>체크 표시가 있는 목표 한 줄</summary>
    public void Line(bool done, string label)
    {
        Raw(done ? "<color=#6EE7B7>■</color> <color=#8A96A3>" + label + "</color>" : "<color=#FCD34D>□</color> " + label);
    }

    /// <summary>꾸밈 없이 그대로 넣는 한 줄 (안내, 다음 행동 등)</summary>
    public void Raw(string richText)
    {
        if (_lineCount > 0) _lines.Append('\n');
        _lines.Append(richText);
        _lineCount++;
    }

    public void EndObjectives()
    {
        _objectives.text = _lines.ToString();
        _card.sizeDelta = new Vector2(560, 58 + _lineCount * 32);
    }

    // ------------------------------------------------------------------ 안내 / 알림
    /// <summary>화면 가운데 아래 안내. progress가 0보다 크면 누르기 막대를 보여 준다</summary>
    public void SetPrompt(string text, float progress)
    {
        _prompt.text = text ?? "";
        _promptBarBg.gameObject.SetActive(progress > 0f);
        _promptBar.rectTransform.sizeDelta = new Vector2(260f * Mathf.Clamp01(progress), 6f);
    }

    public void Toast(string message) { _toast.text = message; _toastUntil = Time.unscaledTime + 6f; }

    public void Banner(string title, string sub, Color color)
    {
        _bannerTitle.text = title; _bannerTitle.color = color; _bannerSub.text = sub;
        _bannerUntil = Time.unscaledTime + 7f;
    }

    public void ShowNote(string text, float seconds = 12f)
    {
        _note.text = text;
        _notePanel.gameObject.SetActive(true);
        _noteUntil = Time.unscaledTime + seconds;
    }

    /// <summary>매 프레임: 알림·배너가 서서히 나타났다 사라지게</summary>
    public void Tick()
    {
        float t = Time.unscaledTime;
        _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, t < _toastUntil ? 1 : 0, Time.unscaledDeltaTime * 3f);
        _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, t < _bannerUntil ? 1 : 0, Time.unscaledDeltaTime * 1.5f);
        if (_notePanel.gameObject.activeSelf && t > _noteUntil) _notePanel.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ 만들기 도우미
    public Image Img(Transform parent, string name, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
        return img;
    }

    public TMP_Text Txt(Transform parent, string name, string text, float size, Color c, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow; t.richText = true;
        return t;
    }

    public static void Anchor(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
    }

    public static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
}
