using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 중 ESC 메뉴: 계속하기 / 설정 / 게임 종료.
///  - 층 씬(대원이 있는 씬)에서만 열린다. Start·로비 씬, 로딩 화면 중에는 열리지 않는다.
///  - 열려 있는 동안 대원 조작(FpsInput)은 막히고 마우스 커서가 풀린다. 멀티플레이라 게임은 멈추지 않는다.
///  - 닫으면 열기 전에 커서가 잠겨 있었을 때만 다시 잠근다 (게임 오버 화면 등은 그대로).
/// 씬에 놓지 않고 게임 시작 때 자동으로 만들어진다 (씬이 바뀌어도 남음).
/// </summary>
[DefaultExecutionOrder(-400)]   // FpsInput(-300)보다 먼저: 여는 프레임의 ESC가 '커서 풀기'로 새지 않게
public sealed class PauseMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    /// <summary>이 프레임의 ESC를 메뉴가 썼다 (FpsInput이 같은 ESC로 커서를 풀지 않게)</summary>
    public static int EscUsedFrame = -1;
    static PauseMenu _instance;

    RectTransform _canvas;
    GameObject _panel, _confirm;
    bool _wasLocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (_instance != null) return;
        var go = new GameObject("PauseMenu");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<PauseMenu>();
        SceneManager.sceneLoaded += (s, m) => { if (_instance != null && IsOpen) _instance.Close(false); };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { IsOpen = false; _instance = null; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        // 설정 창이 열려 있거나 키 입력을 기다리는 중이면 ESC는 그쪽에서 쓴다
        if (GameKeys.Listening || SettingsWindow.IsOpen || SettingsWindow.EscUsedFrame == Time.frameCount) return;
        if (IsOpen)
        {
            EscUsedFrame = Time.frameCount;
            if (_confirm != null && _confirm.activeSelf) { _confirm.SetActive(false); _panel.SetActive(true); }
            else Close(true);
        }
        else if (CanOpen()) { EscUsedFrame = Time.frameCount; Open(); }
    }

    static bool CanOpen()
    {
        if (LoadingScreen.IsShowing) return false;
        string scene = SceneManager.GetActiveScene().name;
        if (scene == StartMenu.SceneName || scene == "Lobby") return false;
        return FindAnyObjectByType<VoidLast.Player.FpsInput>() != null;
    }

    void Open()
    {
        _wasLocked = Cursor.lockState == CursorLockMode.Locked;
        IsOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UiKit.EnsureEventSystem();

        _canvas = UiKit.NewCanvas("Pause Menu", 985, transform);
        var dim = UiKit.NewImage(_canvas, "Dim", UiKit.Backdrop);
        dim.rectTransform.Fill();

        _panel = BuildPanel("Menu", 560, out var list);
        var title = UiKit.NewText(list, "메뉴", 40, LobbyStyle.Text, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        Size(title.rectTransform, 64);
        Size(UiKit.NewButton(list, "계속하기", 28, () => Close(true)).transform, 76);
        Size(UiKit.NewButton(list, "설정", 28, OpenSettings).transform, 76);
        var exit = UiKit.NewButton(list, "게임 종료", 28, () => { _panel.SetActive(false); _confirm.SetActive(true); });
        Size(exit.transform, 76);
        var note = UiKit.NewText(list, "멀티플레이 중에는 메뉴를 열어도 게임이 멈추지 않아요.", 19, LobbyStyle.TextDim, TextAlignmentOptions.Center);
        Size(note.rectTransform, 40);

        _confirm = BuildPanel("Confirm Exit", 600, out var c);
        var q = UiKit.NewText(c, "게임을 끝낼까요?", 34, LobbyStyle.Text, TextAlignmentOptions.Center);
        q.fontStyle = FontStyles.Bold;
        Size(q.rectTransform, 60);
        var warn = UiKit.NewText(c, "방장이 게임을 끝내면 함께하던 대원들도\n게임에서 나가게 돼요.", 22, LobbyStyle.TextDim, TextAlignmentOptions.Center);
        Size(warn.rectTransform, 76);
        var quit = UiKit.NewButton(c, "끝내기", 28, UiKit.QuitGame);
        UiKit.SetColor(quit, LobbyStyle.Accent);
        Size(quit.transform, 76);
        Size(UiKit.NewButton(c, "취소", 28, () => { _confirm.SetActive(false); _panel.SetActive(true); }).transform, 76);
        _confirm.SetActive(false);
    }

    GameObject BuildPanel(string name, float width, out RectTransform list)
    {
        var panel = UiKit.NewImage(_canvas, name, UiKit.Window);
        var r = panel.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(width, 0);
        var accent = UiKit.NewImage(panel.transform, "Accent", LobbyStyle.Accent);
        accent.rectTransform.anchorMin = new Vector2(0, 1); accent.rectTransform.anchorMax = new Vector2(1, 1);
        accent.rectTransform.pivot = new Vector2(0.5f, 1); accent.rectTransform.sizeDelta = new Vector2(0, 4);
        var v = panel.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        v.padding = new RectOffset(48, 48, 40, 40); v.spacing = 16;
        v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        var fit = panel.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        accent.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        list = r;
        return panel.gameObject;
    }

    static void Size(Transform t, float height) => t.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = height;

    void OpenSettings()
    {
        _panel.SetActive(false);
        SettingsWindow.Open(_canvas, () => { if (_panel != null) _panel.SetActive(true); });
    }

    void Close(bool relock)
    {
        IsOpen = false;
        if (_canvas != null) Destroy(_canvas.gameObject);
        _canvas = null; _panel = _confirm = null;
        if (relock && _wasLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
