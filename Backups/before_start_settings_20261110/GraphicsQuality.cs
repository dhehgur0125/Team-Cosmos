using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 그래픽 품질 고르기 (품질 단계는 메뉴 Tools/VOID LAST/Optimize/Setup Graphics Quality가 만든다).
///  - 처음 실행하면 컴퓨터를 보고 자동으로 고른다: 내장 그래픽이거나, 그래픽 메모리가 6GB 미만이거나, 배터리가 있고(노트북) 그래픽 메모리가 8GB 미만이면 → 노트북.
///  - 대기실 오른쪽 위 버튼으로 바꾸면 PlayerPrefs에 저장되어 다음 실행에도 유지된다.
///  - 노트북 단계에서는 씬을 불러올 때 빛 번짐(Bloom)의 고품질 필터를 꺼서 화면 효과 비용을 줄인다.
/// </summary>
public static class GraphicsQuality
{
    const string Pref = "VL_GraphicsQuality";   // 없음 = 자동, 0 = 높음(PC), 1 = 노트북
    public const string HighName = "PC", LaptopName = "Laptop";

    public static bool IsLaptop => QualitySettings.GetQualityLevel() == LaptopIndex;
    public static bool IsAuto => !PlayerPrefs.HasKey(Pref);
    public static string DisplayName => IsLaptop ? "노트북" : "높음";

    static int LaptopIndex => System.Array.IndexOf(QualitySettings.names, LaptopName);
    static int HighIndex => Mathf.Max(0, System.Array.IndexOf(QualitySettings.names, HighName));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        int saved = PlayerPrefs.GetInt(Pref, -1);
        bool laptop = saved >= 0 ? saved == 1 : LooksLikeLaptop();
        SetLevel(laptop);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Debug.Log($"[그래픽] {DisplayName} ({(saved >= 0 ? "저장된 선택" : "자동")}) · GPU {SystemInfo.graphicsDeviceName} {SystemInfo.graphicsMemorySize}MB · RAM {SystemInfo.systemMemorySize}MB · 배터리 {SystemInfo.batteryStatus}");
    }

    /// <summary>내장 그래픽·그래픽 메모리 부족·배터리(노트북)이면 노트북 단계</summary>
    public static bool LooksLikeLaptop()
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

    /// <summary>대기실 버튼: 높음 ↔ 노트북 (저장)</summary>
    public static void Toggle()
    {
        bool laptop = !IsLaptop;
        PlayerPrefs.SetInt(Pref, laptop ? 1 : 0);
        PlayerPrefs.Save();
        SetLevel(laptop);
        TuneLoadedScenes();
    }

    static void SetLevel(bool laptop)
    {
        int index = laptop && LaptopIndex >= 0 ? LaptopIndex : HighIndex;
        if (QualitySettings.GetQualityLevel() != index) QualitySettings.SetQualityLevel(index, true);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TuneLoadedScenes();

    static void TuneLoadedScenes()
    {
        if (!IsLaptop) return;
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
