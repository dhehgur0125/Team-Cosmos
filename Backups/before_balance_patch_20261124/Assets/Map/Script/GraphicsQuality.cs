using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 그래픽 품질 고르기: 자동 / 낮음 / 높음 (품질 단계는 메뉴 Tools/VOID LAST/Optimize/Setup Graphics Quality가 만든다).
///  - 자동(처음 기본값): 컴퓨터를 보고 고른다. 내장 그래픽이거나, 그래픽 메모리가 6GB 미만이거나, 배터리가 있고(노트북) 그래픽 메모리가 8GB 미만이면 → 낮음.
///  - 설정 창(Start 화면 / 게임 중 ESC)의 Graphic 탭에서 바꾸면 PlayerPrefs에 저장되어 다음 실행에도 유지된다.
///  - 낮음 단계에서는 씬을 불러올 때 빛 번짐(Bloom)의 고품질 필터를 꺼서 화면 효과 비용을 줄인다.
/// </summary>
public static class GraphicsQuality
{
    public enum Mode { Auto = -1, High = 0, Low = 1 }

    const string Pref = "VL_GraphicsQuality";   // 없음 = 자동, 0 = 높음, 1 = 낮음
    public const string HighName = "High", LowName = "Low";

    public static bool IsLow => QualitySettings.GetQualityLevel() == LowIndex;
    public static bool IsAuto => !PlayerPrefs.HasKey(Pref);
    public static Mode Current => IsAuto ? Mode.Auto : (Mode)PlayerPrefs.GetInt(Pref);
    public static string DisplayName => IsLow ? "낮음" : "높음";

    static int LowIndex => System.Array.IndexOf(QualitySettings.names, LowName);
    static int HighIndex => Mathf.Max(0, System.Array.IndexOf(QualitySettings.names, HighName));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        int saved = PlayerPrefs.GetInt(Pref, -1);
        bool low = saved >= 0 ? saved == 1 : AutoPrefersLow();
        SetLevel(low);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Debug.Log($"[그래픽] {DisplayName} ({(saved >= 0 ? "저장된 선택" : "자동")}) · GPU {SystemInfo.graphicsDeviceName} {SystemInfo.graphicsMemorySize}MB · RAM {SystemInfo.systemMemorySize}MB · 배터리 {SystemInfo.batteryStatus}");
    }

    /// <summary>자동일 때 이 컴퓨터가 낮음으로 골라지는지: 내장 그래픽·그래픽 메모리 부족·배터리(노트북)</summary>
    public static bool AutoPrefersLow()
    {
        string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
        bool integrated = gpu.Contains("intel") || gpu.Contains("iris") || gpu.Contains("uhd graphics")
            || (gpu.Contains("radeon") && gpu.Contains("graphics"));   // AMD 내장 그래픽: "AMD Radeon(TM) Graphics", "Radeon 780M Graphics"
        int vram = SystemInfo.graphicsMemorySize;
        bool lowVram = vram > 0 && vram < 6000;
        // 배터리가 있어도(노트북·UPS) 그래픽 메모리가 넉넉하면(8GB 이상) 높음으로 둔다
        bool weakLaptop = SystemInfo.batteryStatus != BatteryStatus.Unknown && vram < 8000;
        return integrated || lowVram || weakLaptop;
    }

    /// <summary>설정 창: 자동 / 낮음 / 높음 (저장)</summary>
    public static void SetMode(Mode mode)
    {
        if (mode == Mode.Auto) PlayerPrefs.DeleteKey(Pref);
        else PlayerPrefs.SetInt(Pref, (int)mode);
        PlayerPrefs.Save();
        SetLevel(mode == Mode.Auto ? AutoPrefersLow() : mode == Mode.Low);
        TuneLoadedScenes();
    }

    static void SetLevel(bool low)
    {
        int index = low && LowIndex >= 0 ? LowIndex : HighIndex;
        if (QualitySettings.GetQualityLevel() != index) QualitySettings.SetQualityLevel(index, true);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TuneLoadedScenes();

    static void TuneLoadedScenes()
    {
        if (!IsLow) return;
        foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
        {
            if (volume.sharedProfile == null || !volume.sharedProfile.Has<Bloom>()) continue;
            // profile은 이 볼륨 전용 복사본을 만든다 (원본 에셋은 바뀌지 않음)
            if (volume.profile.TryGet(out Bloom bloom) && bloom.highQualityFiltering.value)
            {
                bloom.highQualityFiltering.overrideState = true;
                bloom.highQualityFiltering.value = false;
            }
        }
    }
}
