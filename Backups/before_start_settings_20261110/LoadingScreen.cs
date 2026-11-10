using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비에서 게임을 시작해 Stage2_Ship으로 넘어가는 동안 보여주는 로딩 화면.
///  - 방장이 시작을 누르거나(방장) 게임 씬 불러오기가 시작되면(참가자) 화면을 덮고, 게이지가 왼쪽부터 차오른다.
///  - 단계: 준비 → 우주선 구역 불러오기(실제 진행률) → 다른 대원 기다리기 → 내 캐릭터 배치 → 100% 후 서서히 사라짐.
///  - 씬이 바뀌어도 사라지지 않도록 DontDestroyOnLoad로 옮겼다가, 다 끝나면 스스로 없어진다.
///  - 로비 테마(어두운 남색 배경, 빨간 강조색, VOID LAST 머리글)를 따른다. UI는 코드로 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LoadingScreen : MonoBehaviour
{
    public TMP_FontAsset font;
    public string gameSceneName = "Stage2_Ship";
    [Tooltip("100%가 되고 사라지기까지 걸리는 시간")] public float fadeSeconds = 0.5f;
    [TextArea]
    public string[] tips =
    {
        "<b>E</b> 키로 권총에 달린 손전등을 켜고 끌 수 있어요.",
        "<b>F</b> 키로 장치를 조작하고, 계단 문을 열고 닫을 수 있어요.",
        "가까이 있는 대원끼리는 목소리가 들려요. <b>M</b> 키로 마이크를 끄고 켤 수 있어요.",
        "무전기를 들고 <b>V</b> 키를 누르고 있으면 멀리 있는 대원과도 대화할 수 있어요.",
        "괴물은 소리에 반응해요. 시끄러운 장치를 돌릴 때는 주변을 살피세요.",
        "흩어지면 위험해요. 서로의 위치를 자주 알려 주세요.",
    };

    public static LoadingScreen Instance { get; private set; }
    public static bool IsShowing => Instance != null && Instance._showing;
    /// <summary>다음에 보여줄 부제 (예: "STAGE 2 · 1층 격납고"). 비우면 기본 문구</summary>
    public static string Subtitle;

    enum Stage { Hidden, Starting, Loading, Waiting, Spawning, Done }

    Stage _stage;
    bool _showing, _closing, _offline;
    bool _guest;   // 참가자: 방장이 씬을 다 불러올 때까지 '시작 준비' 단계에서 기다린다
    string _targetScene;
    float _shown, _creep, _stageStart, _doneAt, _tipAt;
    int _tip;
    AsyncOperation _op;
    NetworkSceneManager _hooked;

    GameObject _canvasGo;
    CanvasGroup _group;
    RectTransform _fill;
    RawImage _stripes;
    TMP_Text _loading, _percent, _status, _tipText;

    static readonly Color Background = new Color(0.024f, 0.03f, 0.04f);
    static readonly Color Outline = new Color(0.93f, 0.95f, 0.97f, 0.9f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    // ------------------------------------------------------------------ 외부에서 부르는 곳
    /// <summary>방장이 게임 시작을 눌렀을 때 바로 보여준다</summary>
    public static void Show() { if (Instance != null) Instance.Begin(Stage.Starting); }
    /// <summary>시작이 실패했을 때 닫는다</summary>
    public static void Cancel() { if (Instance != null) Instance.Abort(); }

    /// <summary>로딩 화면이 없으면 만든다 (층 이동 때 사용. 로비 씬에는 미리 놓여 있음)</summary>
    public static LoadingScreen Ensure(TMP_FontAsset font)
    {
        if (Instance == null)
        {
            var go = new GameObject("LoadingScreen");
            DontDestroyOnLoad(go);
            var ls = go.AddComponent<LoadingScreen>();
            ls.font = font;
        }
        else if (Instance.font == null) Instance.font = font;
        return Instance;
    }

    /// <summary>오프라인(네트워크 없이)으로 씬을 불러올 때: 직접 진행률을 넘겨준다</summary>
    public static void BeginOffline(AsyncOperation op, string sceneName)
    {
        if (Instance == null) return;
        Instance._offline = true;
        Instance._targetScene = sceneName;
        Instance.Begin(Stage.Loading);
        Instance._op = op;
    }

    void Awake()
    {
        if (Instance != null && Instance != this && Instance._showing) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (_showing) Application.backgroundLoadingPriority = ThreadPriority.Normal;
        if (_hooked != null) _hooked.OnSceneEvent -= OnSceneEvent;
        _hooked = null;
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ 씬 이벤트
    void Hook()
    {
        var nm = NetworkManager.Singleton;
        var sm = nm != null && nm.IsListening ? nm.SceneManager : null;
        if (sm == _hooked) return;
        if (_hooked != null) _hooked.OnSceneEvent -= OnSceneEvent;
        _hooked = sm;
        if (_hooked != null) _hooked.OnSceneEvent += OnSceneEvent;
    }

    void OnSceneEvent(SceneEvent e)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || string.IsNullOrEmpty(e.SceneName) || !e.SceneName.StartsWith(gameSceneName)) return;   // Stage2_Ship_5f 등 모든 층
        switch (e.SceneEventType)
        {
            case SceneEventType.Load:
                if (e.ClientId != nm.LocalClientId) return;
                _offline = false;
                _targetScene = e.SceneName;
                Begin(Stage.Loading);
                _op = e.AsyncOperation;
                if (_stage < Stage.Loading) SetStage(Stage.Loading);
                break;
            case SceneEventType.LoadComplete:
                if (e.ClientId != nm.LocalClientId) return;
                if (_showing && _stage < Stage.Waiting) SetStage(Stage.Waiting);
                break;
            case SceneEventType.LoadEventCompleted:
                if (_showing && _stage < Stage.Spawning) SetStage(Stage.Spawning);
                break;
        }
    }

    // ------------------------------------------------------------------ 표시/숨김
    void Begin(Stage stage)
    {
        if (!_showing)
        {
            _showing = true;
            _closing = false;
            _shown = 0f; _creep = 0f; _op = null; _stage = Stage.Hidden;
            var nm = NetworkManager.Singleton;
            _guest = nm != null && nm.IsListening && !nm.IsServer;
            Application.backgroundLoadingPriority = ThreadPriority.High;   // 로딩 화면이 덮는 동안은 불러오기를 최대한 빠르게
            if (transform.parent != null) transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);   // 로비 씬이 내려가도 화면 유지
            if (_canvasGo == null) Build();
            _canvasGo.SetActive(true);
            _group.alpha = 0f;
            _group.blocksRaycasts = true;
            _tip = tips.Length > 0 ? Random.Range(0, tips.Length) : 0;
            ShowTip();
            ApplyBar();
        }
        if (_stage < stage) SetStage(stage);
    }

    void Abort()
    {
        if (!_showing) return;
        _showing = false;
        _stage = Stage.Hidden;
        Application.backgroundLoadingPriority = ThreadPriority.Normal;
        if (_canvasGo != null) { _canvasGo.SetActive(false); _group.blocksRaycasts = false; }
        // 아직 로비라면 로비 씬으로 되돌려 둔다 (다음 시작 때 다시 사용)
        if (SceneManager.GetActiveScene().IsValid()) SceneManager.MoveGameObjectToScene(gameObject, SceneManager.GetActiveScene());
    }

    void SetStage(Stage s)
    {
        _stage = s;
        _stageStart = Time.unscaledTime;
        _creep = Mathf.Max(_creep, StageFloor(s));
        if (s == Stage.Done) _doneAt = Time.unscaledTime;
        if (_status != null) _status.text = s == Stage.Starting && _guest ? "방장이 우주선 구역을 불러오는 중" : StageText(s);
    }

    static float StageFloor(Stage s)
    {
        switch (s)
        {
            case Stage.Loading: return 0.06f;
            case Stage.Waiting: return 0.72f;
            case Stage.Spawning: return 0.86f;
            case Stage.Done: return 1f;
            default: return 0f;
        }
    }

    static float StageCap(Stage s)
    {
        switch (s)
        {
            case Stage.Starting: return 0.06f;
            case Stage.Loading: return 0.70f;
            case Stage.Waiting: return 0.84f;
            case Stage.Spawning: return 0.96f;
            default: return 1f;
        }
    }

    static string StageText(Stage s)
    {
        switch (s)
        {
            case Stage.Starting: return "게임을 준비하는 중";
            case Stage.Loading: return "우주선 구역을 불러오는 중";
            case Stage.Waiting: return "다른 대원들을 기다리는 중";
            case Stage.Spawning: return "대원을 배치하는 중";
            case Stage.Done: return "준비 완료";
            default: return "";
        }
    }

    // ------------------------------------------------------------------ 매 프레임
    void Update()
    {
        Hook();
        if (!_showing) return;

        float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        var nm = NetworkManager.Singleton;
        float stageAge = now - _stageStart;

        // 진행 판단
        switch (_stage)
        {
            case Stage.Starting:
                // 방장: 시작 요청이 끝내 진행되지 않음. 참가자: 방장 컴퓨터가 느리면 오래 걸릴 수 있으므로 더 기다리고, 연결이 끊기면 닫는다
                if (stageAge > (_guest ? 120f : 20f) || _guest && (nm == null || !nm.IsListening)) { Abort(); return; }
                break;
            case Stage.Loading:
                if (!_offline && (nm == null || !nm.IsListening)) { SetStage(Stage.Done); break; }
                if (_op != null) _creep = Mathf.Max(_creep, Mathf.Lerp(0.06f, _offline ? 0.92f : 0.70f, Mathf.Clamp01(_op.progress / 0.9f)));
                if (_op != null && _op.isDone && SceneManager.GetActiveScene().name == _targetScene && stageAge > 0.2f) SetStage(_offline ? Stage.Done : Stage.Waiting);
                break;
            case Stage.Waiting:
            case Stage.Spawning:
                if (nm == null || !nm.IsListening || FpsNetworkPlayer.Local != null || stageAge > 45f) SetStage(Stage.Done);
                break;
        }

        // 게이지: 단계 안에서는 천천히 차오르며(멈춘 것처럼 보이지 않게) 단계의 상한을 넘지 않는다
        // 참가자가 방장을 기다리는 동안에는 30%까지 천천히 차오른다 (방장 로딩 시간을 알 수 없으므로)
        float cap = _stage == Stage.Starting && _guest ? 0.3f : StageCap(_stage);
        _creep = Mathf.Min(cap, _creep + dt * (_stage == Stage.Starting && _guest ? 0.012f : 0.035f));
        float target = _stage == Stage.Done ? 1f : _creep;
        float speed = _stage == Stage.Done ? 1.6f : 0.55f;
        _shown = Mathf.MoveTowards(_shown, target, dt * speed);
        ApplyBar();

        // 글자 애니메이션
        int dots = (int)(now * 2.5f) % 4;
        _loading.text = "Loading" + new string('.', dots) + "<color=#00000000>" + new string('.', 3 - dots) + "</color>";
        if (_stripes != null) { var uv = _stripes.uvRect; uv.x = -now * 0.35f; _stripes.uvRect = uv; }
        if (now - _tipAt > 5f) { _tip = tips.Length > 0 ? (_tip + 1) % tips.Length : 0; ShowTip(); }

        // 나타나기/사라지기
        if (_stage == Stage.Done && _shown >= 0.999f)
        {
            if (!_closing && now - _doneAt > 0.15f) { _closing = true; _doneAt = now; }
        }
        if (_closing)
        {
            _group.alpha = Mathf.Clamp01(1f - (now - _doneAt) / Mathf.Max(0.05f, fadeSeconds));
            _group.blocksRaycasts = false;
            if (_group.alpha <= 0f) { _showing = false; Application.backgroundLoadingPriority = ThreadPriority.Normal; Destroy(gameObject); }
        }
        else _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, dt * 8f);
    }

    void ApplyBar()
    {
        if (_fill == null) return;
        _fill.anchorMax = new Vector2(Mathf.Clamp01(_shown), 1f);
        _fill.gameObject.SetActive(_shown > 0.004f);
        _percent.text = Mathf.FloorToInt(_shown * 100f + 0.001f) + "%";
    }

    void ShowTip()
    {
        _tipAt = Time.unscaledTime;
        if (_tipText != null) _tipText.text = tips.Length > 0 ? "<color=#E0443A>TIP</color>   " + tips[_tip] : "";
    }

    // ------------------------------------------------------------------ UI 만들기
    void Build()
    {
        _canvasGo = new GameObject("Loading Canvas", typeof(RectTransform));
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;   // 모든 UI 위
        var scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasGo.AddComponent<GraphicRaycaster>();
        _group = _canvasGo.AddComponent<CanvasGroup>();
        var root = _canvasGo.transform;

        // 배경
        var bg = Img(root, "Background", Background, null);
        Stretch(bg.rectTransform);
        bg.raycastTarget = true;

        // 머리글 (로비와 같은 자리)
        var stripe = Img(root, "AccentStripe", LobbyStyle.Accent, null);
        Place(stripe.rectTransform, new Vector2(0, 1), new Vector2(84, -66), new Vector2(8, 72));
        var title = Txt(root, "Title", "VOID LAST", 64, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
        title.characterSpacing = 12;
        Place(title.rectTransform, new Vector2(0, 1), new Vector2(510, -50), new Vector2(800, 80));
        var sub = Txt(root, "Subtitle", string.IsNullOrEmpty(Subtitle) ? "STAGE 2 · 탈출 우주선" : Subtitle, 24, LobbyStyle.Accent, TextAlignmentOptions.Left, FontStyles.Normal);
        Place(sub.rectTransform, new Vector2(0, 1), new Vector2(510, -102), new Vector2(800, 34));
        var line = Img(root, "HeaderLine", new Color(1, 1, 1, 0.08f), null);
        Place(line.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(1760, 2));

        // 가운데: Loading... + 게이지
        _loading = Txt(root, "Loading", "Loading", 64, Outline, TextAlignmentOptions.Center, FontStyles.Normal);
        _loading.characterSpacing = 2;
        Place(_loading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 92), new Vector2(900, 90));

        var rounded = RoundedSprite(false);
        var ring = RoundedSprite(true);
        var frame = Img(root, "BarFrame", Outline, ring);
        frame.type = Image.Type.Sliced;
        Place(frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(980, 84));

        var track = Rt("Track", frame.transform);
        track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(13, 13); track.offsetMax = new Vector2(-13, -13);

        var fill = Img(track, "Fill", LobbyStyle.Accent, rounded);
        fill.type = Image.Type.Sliced;
        fill.pixelsPerUnitMultiplier = 2f;   // 안쪽 게이지는 모서리를 조금 더 작게
        _fill = fill.rectTransform;
        _fill.anchorMin = Vector2.zero; _fill.anchorMax = new Vector2(0, 1);
        _fill.pivot = new Vector2(0, 0.5f);
        _fill.offsetMin = Vector2.zero; _fill.offsetMax = Vector2.zero;
        fill.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        // 게이지 위로 흐르는 사선 줄무늬
        var stripesGo = new GameObject("Stripes", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        stripesGo.transform.SetParent(_fill, false);
        _stripes = stripesGo.GetComponent<RawImage>();
        _stripes.texture = StripeTexture();
        _stripes.raycastTarget = false;
        var sr = _stripes.rectTransform;
        sr.anchorMin = Vector2.zero; sr.anchorMax = new Vector2(0, 1); sr.pivot = new Vector2(0, 0.5f);
        sr.sizeDelta = new Vector2(980, 0); sr.anchoredPosition = Vector2.zero;
        _stripes.uvRect = new Rect(0, 0, 980f / 58f / 1f, 1f);

        // 위쪽 밝은 띠 (살짝 입체감)
        var gloss = Img(_fill, "Gloss", new Color(1, 1, 1, 0.12f), null);
        var gr = gloss.rectTransform;
        gr.anchorMin = new Vector2(0, 0.55f); gr.anchorMax = new Vector2(1, 1); gr.offsetMin = gr.offsetMax = Vector2.zero;

        _status = Txt(root, "Status", "", 26, LobbyStyle.TextDim, TextAlignmentOptions.Left, FontStyles.Normal);
        Place(_status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-170, -74), new Vector2(640, 40));
        _percent = Txt(root, "Percent", "0%", 30, Color.white, TextAlignmentOptions.Right, FontStyles.Bold);
        Place(_percent.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(390, -74), new Vector2(200, 40));

        // 아래: 팁
        var tipLine = Img(root, "TipLine", new Color(1, 1, 1, 0.08f), null);
        Place(tipLine.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 130), new Vector2(1760, 2));
        _tipText = Txt(root, "Tip", "", 24, LobbyStyle.TextDim, TextAlignmentOptions.Left, FontStyles.Normal);
        Place(_tipText.rectTransform, new Vector2(0, 0), new Vector2(80 + 800, 80), new Vector2(1600, 40));
    }

    static RectTransform Rt(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
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

    static Image Img(Transform parent, string name, Color c, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c;
        img.sprite = sprite;
        img.raycastTarget = false;
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
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    /// <summary>둥근 사각형 9-slice 스프라이트 (ring=true면 테두리만)</summary>
    static Sprite RoundedSprite(bool ring)
    {
        const int size = 64; const float radius = 26f; const float stroke = 5f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = ring ? "LoadingRing" : "LoadingRounded" };
        var px = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 둥근 사각형까지의 부호 거리 (안쪽이 음수)
                float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
                float a = Mathf.Clamp01(0.5f - outside);
                if (ring) a *= Mathf.Clamp01(outside + stroke + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px); tex.Apply();
        float b = radius + 2f;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    /// <summary>게이지 위를 흐르는 옅은 사선 줄무늬</summary>
    static Texture2D StripeTexture()
    {
        const int w = 58, h = 58;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "LoadingStripes" };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = Mathf.Repeat((x + y) / (float)w, 1f);
                float a = Mathf.SmoothStep(0, 1, Mathf.Clamp01((0.5f - Mathf.Abs(u - 0.5f)) * 6f - 0.6f)) * 0.07f;
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }
}
