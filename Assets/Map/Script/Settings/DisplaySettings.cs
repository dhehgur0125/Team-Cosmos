using UnityEngine;

/// <summary>
/// 화면 모드(전체 화면 / 창 모드)와 화면 비율. PlayerPrefs(VL_Display_Mode, VL_Display_Aspect)에 저장되고
/// 게임을 켤 때마다 다시 적용된다 (예전에 남은 작은 창 크기로 시작하지 않게).
///  - 비율 "자동" = 모니터 비율 그대로. 다른 비율을 고르면 그 비율 중 모니터에 들어가는 가장 큰 크기.
///  - 전체 화면에서 모니터와 다른 비율이면 양옆(또는 위아래)에 검은 띠가 생긴다 (늘어나지 않음).
///  - 창 모드는 그 크기의 85% 창.
/// 에디터에서는 화면 크기를 바꿀 수 없어서 저장만 하고 적용은 빌드에서만 된다.
/// </summary>
public static class DisplaySettings
{
    public enum Mode { Fullscreen, Windowed }
    public enum Aspect { Auto, R16x9, R16x10, R21x9, R4x3 }

    const string ModePref = "VL_Display_Mode", AspectPref = "VL_Display_Aspect";
    const float WindowScale = 0.85f;

    public static readonly string[] ModeLabels = { "전체 화면", "창 모드" };
    public static readonly string[] AspectLabels = { "자동", "16:9", "16:10", "21:9", "4:3" };

    public static Mode CurrentMode => (Mode)Mathf.Clamp(PlayerPrefs.GetInt(ModePref, (int)Mode.Fullscreen), 0, 1);
    public static Aspect CurrentAspect => (Aspect)Mathf.Clamp(PlayerPrefs.GetInt(AspectPref, (int)Aspect.Auto), 0, 4);

    /// <summary>에디터가 아니라 실제로 화면에 적용되는지</summary>
    public static bool CanApply => !Application.isEditor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyOnStart() => Apply();

    public static void SetMode(Mode mode) { PlayerPrefs.SetInt(ModePref, (int)mode); PlayerPrefs.Save(); Apply(); }
    public static void SetAspect(Aspect aspect) { PlayerPrefs.SetInt(AspectPref, (int)aspect); PlayerPrefs.Save(); Apply(); }

    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(ModePref);
        PlayerPrefs.DeleteKey(AspectPref);
        PlayerPrefs.Save();
        Apply();
    }

    /// <summary>저장된 값으로 화면을 바꾼다 (빌드에서만)</summary>
    public static void Apply()
    {
        if (!CanApply) return;
        var size = TargetSize();
        var mode = CurrentMode == Mode.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(size.x, size.y, mode);
    }

    /// <summary>지금 설정으로 그릴 화면 크기 (가로, 세로)</summary>
    public static Vector2Int TargetSize()
    {
        var monitor = MonitorSize();
        float ratio = Ratio(CurrentAspect, monitor);
        int w = monitor.x, h = monitor.y;
        if ((float)w / h > ratio) w = Mathf.RoundToInt(h * ratio);
        else h = Mathf.RoundToInt(w / ratio);
        if (CurrentMode == Mode.Windowed) { w = Mathf.RoundToInt(w * WindowScale); h = Mathf.RoundToInt(h * WindowScale); }
        return new Vector2Int(Mathf.Max(640, w & ~1), Mathf.Max(360, h & ~1));
    }

    /// <summary>"전체 화면 · 16:9 (1920×1080)"</summary>
    public static string Describe()
    {
        var s = TargetSize();
        return ModeLabels[(int)CurrentMode] + " · " + (CurrentAspect == Aspect.Auto ? "모니터 비율" : AspectLabels[(int)CurrentAspect])
            + " (" + s.x + "×" + s.y + ")";
    }

    static Vector2Int MonitorSize()
    {
        var info = Screen.mainWindowDisplayInfo;
        if (info.width > 0 && info.height > 0) return new Vector2Int(info.width, info.height);
        var r = Screen.currentResolution;
        if (r.width > 0 && r.height > 0) return new Vector2Int(r.width, r.height);
        return new Vector2Int(1920, 1080);
    }

    static float Ratio(Aspect a, Vector2Int monitor)
    {
        switch (a)
        {
            case Aspect.R16x9: return 16f / 9f;
            case Aspect.R16x10: return 16f / 10f;
            case Aspect.R21x9: return 21f / 9f;
            case Aspect.R4x3: return 4f / 3f;
            default: return (float)monitor.x / monitor.y;
        }
    }
}
