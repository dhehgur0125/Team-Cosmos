using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 처음 화면 (Start 씬): Play → 로비, Setting → 설정 창, Exit → 게임 종료.
/// 화면은 실행할 때 코드로 만든다 (Start 씬에는 카메라·EventSystem·이 컴포넌트만 있다. 메뉴 Tools/VOID LAST/Settings/Build Start Scene).
/// </summary>
public sealed class StartMenu : MonoBehaviour
{
    public const string SceneName = "Start";

    [Tooltip("Play를 누르면 불러올 씬")]
    public string lobbySceneName = "Lobby";

    RectTransform _canvas;
    GameObject _buttons;
    TextMeshProUGUI _loading;
    RawImage _stars;
    bool _starting;

    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UiKit.EnsureEventSystem();
        Build();
    }

    void Build()
    {
        _canvas = UiKit.NewCanvas("Start Menu", 10, transform);

        // 배경: 어두운 우주 + 별 + 아래쪽 붉은 빛
        UiKit.NewImage(_canvas, "Background", new Color(0.02f, 0.025f, 0.035f)).rectTransform.Fill();
        _stars = UiKit.NewRect(_canvas, "Stars").gameObject.AddComponent<RawImage>();
        _stars.rectTransform.Fill();
        _stars.texture = StarTexture();
        _stars.uvRect = new Rect(0, 0, 2.4f, 1.35f);
        _stars.raycastTarget = false;
        var glow = UiKit.NewRect(_canvas, "Glow").gameObject.AddComponent<RawImage>();
        glow.rectTransform.Fill();
        glow.texture = GlowTexture();
        glow.raycastTarget = false;

        // 제목
        var title = UiKit.NewText(_canvas, "VOID LAST", 150, LobbyStyle.Text, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 18;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1400, 190));
        title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        var line = UiKit.NewImage(_canvas, "Line", LobbyStyle.Accent);
        line.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(160, 4));
        line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        var subtitle = UiKit.NewText(_canvas, "버려진 우주선에서 함께 살아 나가세요", 26, LobbyStyle.TextDim, TextAlignmentOptions.Center);
        subtitle.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, 108), new Vector2(900, 44));
        subtitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);

        // 버튼 3개
        var column = UiKit.NewRect(_canvas, "Buttons");
        column.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(440, 300));
        column.pivot = new Vector2(0.5f, 0.5f);
        var v = column.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 20; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = v.childForceExpandHeight = true;
        var play = UiKit.NewButton(column, "Play", 34, Play);
        UiKit.SetColor(play, LobbyStyle.Accent);
        UiKit.NewButton(column, "Setting", 34, OpenSettings);
        UiKit.NewButton(column, "Exit", 34, UiKit.QuitGame);
        _buttons = column.gameObject;

        _loading = UiKit.NewText(_canvas, "", 26, LobbyStyle.TextDim, TextAlignmentOptions.Center);
        _loading.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(600, 60));
        _loading.rectTransform.pivot = new Vector2(0.5f, 0.5f);

        var version = UiKit.NewText(_canvas, "Team Cosmos  ·  v" + Application.version, 18, new Color(1, 1, 1, 0.3f), TextAlignmentOptions.BottomRight);
        version.rectTransform.Place(new Vector2(1, 0), new Vector2(-32, 24), new Vector2(600, 40));
    }

    void Update()
    {
        if (_stars != null)
        {
            var uv = _stars.uvRect;
            uv.x = Mathf.Repeat(uv.x + Time.unscaledDeltaTime * 0.004f, 1f);
            _stars.uvRect = uv;
        }
    }

    void OpenSettings()
    {
        if (_starting) return;
        _buttons.SetActive(false);
        SettingsWindow.Open(_canvas, () => { if (_buttons != null) _buttons.SetActive(true); });
    }

    void Play()
    {
        if (_starting) return;
        _starting = true;
        _buttons.SetActive(false);
        _loading.text = "불러오는 중…";
        GameSettings.Save();
        var op = SceneManager.LoadSceneAsync(lobbySceneName);
        if (op == null) { _starting = false; _buttons.SetActive(true); _loading.text = "로비 씬을 찾지 못했어요 (빌드 설정 확인)"; }
    }

    // ------------------------------------------------------------------ 배경 그림 (코드로 생성)
    static Texture2D StarTexture()
    {
        const int w = 512, h = 288;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "StartStars" };
        var px = new Color32[w * h];
        var rng = new System.Random(7);
        for (int i = 0; i < 260; i++)
        {
            int x = rng.Next(w), y = rng.Next(h);
            float b = (float)rng.NextDouble();
            byte a = (byte)(40 + b * b * 215);
            px[y * w + x] = new Color32(220, 230, 255, a);
        }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    static Texture2D GlowTexture()
    {
        const int w = 4, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "StartGlow" };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float t = 1f - y / (float)(h - 1);          // 아래 = 1
            float a = Mathf.Pow(t, 3f) * 0.55f;
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32(150, 30, 24, (byte)(a * 255));
        }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }
}
