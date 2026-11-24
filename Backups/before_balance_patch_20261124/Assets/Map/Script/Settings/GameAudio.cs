using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>소리 종류 (소리 묶음)</summary>
public enum AudioCategory { Effects, Music, Voice }

/// <summary>
/// 소리 크기 설정을 실제 소리에 적용한다.
///  - 전체 음량 = AudioListener.volume. 소리 묶음(오디오 믹서) Resources/Audio/GameMixer: Master 아래 Effects·Music·Voice. 메뉴 Tools/VOID LAST/Settings/Create Audio Mixer가 만든다.
///  - 모든 AudioSource는 처음 보일 때 묶음에 연결된다 (지정이 없으면 효과음). 배경 음악은 AudioSource에 MusicSource 컴포넌트를 붙이면 배경음으로 간다.
///  - 근거리 음성은 Unity 소리가 아니라 Vivox가 직접 내보내므로 Vivox 출력 크기(전체 × 음성 채팅)와 마이크 크기를 따로 맞춘다.
/// </summary>
public static class GameAudio
{
    static AudioMixer _mixer;
    static AudioMixerGroup _effects, _music, _voice;
    static bool _loaded;

    static readonly string[] Params = { "MasterVolume", "MusicVolume", "EffectsVolume", "VoiceVolume" };

    public static AudioMixer Mixer { get { Load(); return _mixer; } }

    static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        _mixer = Resources.Load<AudioMixer>("Audio/GameMixer");
        if (_mixer == null) { Debug.LogWarning("[소리] Resources/Audio/GameMixer가 없어 종류별 소리 크기를 쓸 수 없어요 (전체 음량만 적용)."); return; }
        _effects = Find("Effects"); _music = Find("Music"); _voice = Find("Voice");
    }

    static AudioMixerGroup Find(string name)
    {
        var groups = _mixer.FindMatchingGroups(name);
        foreach (var g in groups) if (g.name == name) return g;
        return groups.Length > 0 ? groups[0] : null;
    }

    public static AudioMixerGroup Group(AudioCategory c)
    {
        Load();
        return c == AudioCategory.Music ? _music : c == AudioCategory.Voice ? _voice : _effects;
    }

    /// <summary>새로 만든 AudioSource를 소리 묶음에 연결 (이미 연결돼 있으면 그대로)</summary>
    public static void Route(AudioSource source, AudioCategory category = AudioCategory.Effects)
    {
        if (source == null || source.outputAudioMixerGroup != null) return;
        var g = Group(category);
        if (g != null) source.outputAudioMixerGroup = g;
    }

    /// <summary>씬에 있는 AudioSource 중 아직 묶음이 없는 것을 모두 연결한다</summary>
    public static void RouteAll()
    {
        Load();
        if (_mixer == null) return;
        foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
        {
            if (s.outputAudioMixerGroup != null) continue;
            var category = s.GetComponent<MusicSource>() != null ? AudioCategory.Music
                : IsVoiceTap(s) ? AudioCategory.Voice : AudioCategory.Effects;
            Route(s, category);
        }
    }

    static bool IsVoiceTap(AudioSource s) => s.name.StartsWith("RadioVoice") || s.GetComponent("VivoxParticipantTap") != null;

    /// <summary>저장된 크기를 소리 묶음·Vivox에 적용.
    /// 전체 음량은 AudioListener.volume으로 모든 Unity 소리에 걸고(묶음에 연결 안 된 소리 포함), 믹서 Master는 0dB로 둔다.</summary>
    public static void Apply()
    {
        Load();
        AudioListener.volume = GameSettings.GetVolume(GameSettings.Volume.Master);
        if (_mixer != null)
        {
            _mixer.SetFloat(Params[0], 0f);
            _mixer.SetFloat(Params[1], ToDecibel(GameSettings.GetVolume(GameSettings.Volume.Music)));
            _mixer.SetFloat(Params[2], ToDecibel(GameSettings.GetVolume(GameSettings.Volume.Effects)));
            _mixer.SetFloat(Params[3], ToDecibel(GameSettings.GetVolume(GameSettings.Volume.Voice)));
        }
        ApplyVoice();
    }

    /// <summary>AudioSource.PlayClipAtPoint 대신: 효과음 묶음에 연결된 일회용 소리</summary>
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null) return;
        var go = new GameObject("One shot audio");
        go.transform.position = position;
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.volume = volume; s.spatialBlend = 1f;
        Route(s);
        s.Play();
        Object.Destroy(go, clip.length * (Time.timeScale < 0.01f ? 0.01f : Time.timeScale) + 0.1f);
    }

    /// <summary>Vivox 근거리 음성·마이크 크기 (Vivox 로그인 뒤에도 불린다)</summary>
    public static void ApplyVoice()
    {
        try
        {
            var vivox = Unity.Services.Vivox.VivoxService.Instance;
            if (vivox == null || !vivox.IsLoggedIn) return;
            float output = GameSettings.GetVolume(GameSettings.Volume.Master) * GameSettings.GetVolume(GameSettings.Volume.Voice);
            vivox.SetOutputDeviceVolume(VivoxLevel(output));
            vivox.SetInputDeviceVolume(VivoxLevel(GameSettings.GetVolume(GameSettings.Volume.Mic)));
        }
        catch (System.Exception e) { Debug.LogWarning("[소리] 음성 크기 적용 실패(무시): " + e.Message); }
    }

    /// <summary>0~1 → 믹서 dB (-80 = 소리 없음, 0 = 원래 크기)</summary>
    static float ToDecibel(float v) => v <= 0.0001f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(v));

    /// <summary>0~1 → Vivox 크기 (-50 ~ 0, 0 = 원래 크기)</summary>
    static int VivoxLevel(float v) => v <= 0.003f ? -50 : Mathf.Clamp(Mathf.RoundToInt(20f * Mathf.Log10(v)), -50, 0);

    // ------------------------------------------------------------------ 자동 실행
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var go = new GameObject("GameAudio");
        go.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();
        GameSettings.Changed -= Apply;
        GameSettings.Changed += Apply;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => RouteAll();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _loaded = false; _mixer = null; _effects = _music = _voice = null; }

    /// <summary>믹서 값은 Awake에서 넣으면 무시될 수 있어 Start에서 적용하고, 나중에 생긴 소리도 1초마다 연결한다</summary>
    sealed class Runner : MonoBehaviour
    {
        float _next;
        void Start() { RouteAll(); Apply(); }
        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            RouteAll();
        }
    }
}
