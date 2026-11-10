using System;
using UnityEngine;

/// <summary>
/// 게임 설정 값 (소리 크기·마우스 감도). PlayerPrefs에 저장되어 다음 실행에도 유지된다.
/// 키 설정은 GameKeys, 그래픽 품질은 GraphicsQuality가 따로 저장한다.
/// </summary>
public static class GameSettings
{
    public enum Volume { Master, Music, Effects, Voice, Mic }

    static readonly string[] VolumeKeys = { "VL_Vol_Master", "VL_Vol_Music", "VL_Vol_Effects", "VL_Vol_Voice", "VL_Vol_Mic" };
    const string SensitivityKey = "VL_MouseSensitivity";

    public const float MinSensitivity = 0.2f, MaxSensitivity = 3f;

    /// <summary>값이 바뀌면 (소리 적용 등)</summary>
    public static event Action Changed;

    /// <summary>0 ~ 1 (기본 1)</summary>
    public static float GetVolume(Volume v) => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKeys[(int)v], 1f));

    public static void SetVolume(Volume v, float value)
    {
        PlayerPrefs.SetFloat(VolumeKeys[(int)v], Mathf.Clamp01(value));
        Changed?.Invoke();
    }

    /// <summary>마우스 감도 배율 (기본 1 = 원래 감도)</summary>
    public static float MouseSensitivity
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
        set { PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, MinSensitivity, MaxSensitivity)); Changed?.Invoke(); }
    }

    public static void ResetSound()
    {
        foreach (var k in VolumeKeys) PlayerPrefs.DeleteKey(k);
        Changed?.Invoke();
    }

    public static void ResetSensitivity()
    {
        PlayerPrefs.DeleteKey(SensitivityKey);
        Changed?.Invoke();
    }

    /// <summary>설정 창을 닫을 때 디스크에 쓴다</summary>
    public static void Save() => PlayerPrefs.Save();

    public static string VolumeName(Volume v)
    {
        switch (v)
        {
            case Volume.Master: return "전체 음량";
            case Volume.Music: return "배경음";
            case Volume.Effects: return "효과음";
            case Volume.Voice: return "음성 채팅";
            default: return "마이크";
        }
    }

    public static string VolumeHint(Volume v)
    {
        switch (v)
        {
            case Volume.Master: return "모든 소리";
            case Volume.Music: return "배경 음악·분위기 소리";
            case Volume.Effects: return "총·발소리·괴물·장치";
            case Volume.Voice: return "동료 목소리 (근거리·무전)";
            default: return "내 목소리 크기";
        }
    }
}
