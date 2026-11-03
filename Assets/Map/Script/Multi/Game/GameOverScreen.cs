using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VoidLast.Player;

/// <summary>
/// 모든 대원이 죽으면 게임 오버 화면을 띄우고 대기실로 돌아간다.
///  - 화면이 서서히 나타남 → 잠시 보여줌 → 방장이 모든 대원을 로비 씬으로 이동 (같은 방 그대로)
///  - 로비 대기방이 준비되면 게임 오버 화면이 서서히 사라진다 (씬이 바뀌어도 화면이 유지되도록 DontDestroyOnLoad)
///  - 대원마다 같은 HP 정보를 받고 있으므로 각자 화면에서 판단해 띄우고, 씬 이동은 방장만 한다.
///  - 오프라인(씬 직접 실행)에서는 혼자 죽으면 같은 화면을 보여주고 로비 첫 화면으로 간다.
/// 로비 테마(어두운 남색, 빨간 강조색, VOID LAST 머리글)를 따른다.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameOverScreen : MonoBehaviour
{
    public TMP_FontAsset font;
    public string lobbySceneName = "Lobby";
    [Tooltip("마지막 대원이 쓰러지고 화면이 뜨기 시작할 때까지")] [Min(0)] public float triggerDelay = 1.5f;
    [Min(0.1f)] public float fadeInSeconds = 1.8f;
    [Tooltip("다 나타난 뒤 대기실로 이동하기 전까지 보여주는 시간")] [Min(0)] public float holdSeconds = 3.5f;
    [Min(0.1f)] public float fadeOutSeconds = 1.6f;

    public static bool IsShowing { get; private set; }

    enum Step { Watching, Waiting, FadingIn, Holding, Moving, FadingOut }
    Step _step;
    float _stepStart, _allDeadSince = -1f, _lobbySince = -1f;
    FpsHealth _offlineHealth;
    readonly List<string> _crew = new List<string>();

    CanvasGroup _group;
    TMP_Text _title, _status;
    RectTransform _titleLine;

    static readonly Color Background = new Color(0.024f, 0.03f, 0.04f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { IsShowing = false; }

    void OnDestroy() { if (_step != Step.Watching) IsShowing = false; }

    void SetStep(Step s) { _step = s; _stepStart = Time.unscaledTime; }
    float StepAge => Time.unscaledTime - _stepStart;

    void Update()
    {
        switch (_step)
        {
            case Step.Watching:
                if (AllDead())
                {
                    if (_allDeadSince < 0f) _allDeadSince = Time.unscaledTime;
                    if (Time.unscaledTime - _allDeadSince >= triggerDelay) Show();
                }
                else _allDeadSince = -1f;
                break;

            case Step.FadingIn:
                {
                    float k = Mathf.Clamp01(StepAge / fadeInSeconds);
                    _group.alpha = Mathf.SmoothStep(0, 1, k);
                    _title.characterSpacing = Mathf.Lerp(60f, 14f, 1f - Mathf.Pow(1f - k, 3f));
                    _titleLine.sizeDelta = new Vector2(Mathf.Lerp(0f, 560f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(k * 1.4f - 0.3f))), 3f);
                    if (k >= 1f) SetStep(Step.Holding);
                }
                break;

            case Step.Holding:
                {
                    int left = Mathf.Max(1, Mathf.CeilToInt(holdSeconds - StepAge));
                    _status.text = "잠시 후 대기실로 돌아갑니다  <color=#E0443A>" + left + "</color>";
                    if (StepAge >= holdSeconds) MoveToLobby();
                }
                break;

            case Step.Moving:
                {
                    int dots = (int)(Time.unscaledTime * 2.5f) % 4;
                    _status.text = "대기실로 이동하는 중" + new string('.', dots) + "<color=#00000000>" + new string('.', 3 - dots) + "</color>";
                    if (SceneManager.GetActiveScene().name == lobbySceneName)
                    {
                        if (_lobbySince < 0f) _lobbySince = Time.unscaledTime;
                        if (Time.unscaledTime - _lobbySince > 0.8f) SetStep(Step.FadingOut);   // 대기방 화면이 그려질 시간
                    }
                    else if (StepAge > 25f) Fallback();   // 방장이 끝내 이동시키지 못함
                }
                break;

            case Step.FadingOut:
                _group.alpha = 1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01(StepAge / fadeOutSeconds));
                _group.blocksRaycasts = _group.alpha > 0.5f;
                if (StepAge >= fadeOutSeconds) { IsShowing = false; Destroy(gameObject); }
                break;
        }
    }

    // ------------------------------------------------------------------ 판단
    bool AllDead()
    {
        if (Stage2NetworkBootstrap.IsOnline)
        {
            if (FpsNetworkPlayer.All.Count == 0) return false;
            foreach (var p in FpsNetworkPlayer.All)
                if (p != null && p.Health != null && !p.Health.IsDead) return false;
            return true;
        }
        if (_offlineHealth == null)
        {
            var cam = Camera.main;
            if (cam != null) _offlineHealth = cam.GetComponentInParent<FpsHealth>();
        }
        return _offlineHealth != null && _offlineHealth.IsDead;
    }

    // ------------------------------------------------------------------ 진행
    void Show()
    {
        _crew.Clear();
        foreach (var p in FpsNetworkPlayer.All) if (p != null) _crew.Add(string.IsNullOrEmpty(p.Nickname) ? "대원" : p.Nickname);

        // 씬이 바뀌어도 화면 유지
        transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
        Build();
        IsShowing = true;
        _group.alpha = 0f;
        _status.text = "";
        SetStep(Step.FadingIn);
        Debug.Log("[GameOver] 모든 대원이 사망 → 게임 오버");
    }

    void MoveToLobby()
    {
        SetStep(Step.Moving);
        var nm = NetworkManager.Singleton;
        if (Stage2NetworkBootstrap.IsOnline)
        {
            if (!nm.IsServer) return;   // 참가자는 방장이 옮겨 줄 때까지 기다린다
            var status = nm.SceneManager != null ? nm.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single) : SceneEventProgressStatus.SceneManagementNotEnabled;
            Debug.Log("[GameOver] 모든 대원을 대기실로 이동: " + status);
            if (status != SceneEventProgressStatus.Started) Fallback();
        }
        else
        {
            SceneManager.LoadScene(lobbySceneName);
        }
    }

    /// <summary>방장이 대원을 옮기지 못했을 때: 각자 방에서 나와 로비 첫 화면으로</summary>
    void Fallback()
    {
        _stepStart = Time.unscaledTime;   // 한 번만
        var boot = FindFirstObjectByType<Stage2NetworkBootstrap>();
        if (boot != null) _ = boot.ReturnToLobbyAsync("게임 오버 · 대기실로 돌아가지 못해 방에서 나왔어요.");
        else SceneManager.LoadScene(lobbySceneName);
    }

    // ------------------------------------------------------------------ UI
    void Build()
    {
        var canvasGo = new GameObject("Game Over Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1100;   // 로딩 화면보다도 위
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = true;
        var root = canvasGo.transform;

        var bg = Img(root, "Background", Background);
        Stretch(bg.rectTransform);
        bg.raycastTarget = true;
        // 가운데가 살짝 붉은 빛
        var glow = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        glow.transform.SetParent(root, false);
        var gi = glow.GetComponent<RawImage>();
        gi.texture = GlowTexture(); gi.color = new Color(LobbyStyle.Accent.r, LobbyStyle.Accent.g, LobbyStyle.Accent.b, 0.16f); gi.raycastTarget = false;
        Place(gi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1500, 900));

        // 머리글 (로비와 같은 자리)
        var stripe = Img(root, "AccentStripe", LobbyStyle.Accent);
        Place(stripe.rectTransform, new Vector2(0, 1), new Vector2(84, -66), new Vector2(8, 72));
        var head = Txt(root, "Title", "VOID LAST", 64, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
        head.characterSpacing = 12;
        Place(head.rectTransform, new Vector2(0, 1), new Vector2(510, -50), new Vector2(800, 80));
        var sub = Txt(root, "Subtitle", "STAGE 2 · 탈출 우주선", 24, LobbyStyle.Accent, TextAlignmentOptions.Left, FontStyles.Normal);
        Place(sub.rectTransform, new Vector2(0, 1), new Vector2(510, -102), new Vector2(800, 34));
        var line = Img(root, "HeaderLine", new Color(1, 1, 1, 0.08f));
        Place(line.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(1760, 2));

        // 가운데
        _title = Txt(root, "GameOver", "<mspace=0.78em>GAME OVER</mspace>", 132, LobbyStyle.Accent, TextAlignmentOptions.Center, FontStyles.Bold);
        _title.characterSpacing = 60f;
        Place(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 110), new Vector2(1600, 170));
        var tl = Img(root, "TitleLine", LobbyStyle.Accent);
        _titleLine = tl.rectTransform;
        Place(_titleLine, new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(0, 3));
        var msg = Txt(root, "Message", "모든 대원이 사망했어요", 38, LobbyStyle.Text, TextAlignmentOptions.Center, FontStyles.Normal);
        Place(msg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(1200, 56));
        var crew = Txt(root, "Crew", _crew.Count > 0 ? "대원  " + string.Join("  ·  ", _crew) : "", 24, LobbyStyle.TextDim, TextAlignmentOptions.Center, FontStyles.Normal);
        Place(crew.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -92), new Vector2(1400, 40));

        // 아래
        var bottomLine = Img(root, "BottomLine", new Color(1, 1, 1, 0.08f));
        Place(bottomLine.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 130), new Vector2(1760, 2));
        _status = Txt(root, "Status", "", 26, LobbyStyle.TextDim, TextAlignmentOptions.Center, FontStyles.Normal);
        Place(_status.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 80), new Vector2(1400, 40));
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    static Image Img(Transform parent, string name, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    TMP_Text Txt(Transform parent, string name, string text, float size, Color c, TextAlignmentOptions align, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = style;
        t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    static Texture2D GlowTexture()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "GameOverGlow" };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3 - 2 * a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }
}
