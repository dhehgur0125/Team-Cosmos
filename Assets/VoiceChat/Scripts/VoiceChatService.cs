using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// 게임 전체에서 하나만 쓰는 Vivox 음성 채팅 관리자 (씬이 바뀌어도 유지).
///  - 로비 대기방: 방(세션)마다 하나의 전체 음성 채널 (vl_lobby_세션ID)
///  - 게임(Stage2): 근거리 3D 음성 채널 (vl_near_세션ID) + 무전기 채널 (vl_radio_세션ID)
/// Unity Services 초기화·익명 로그인은 로비(LobbyManager)가 먼저 해 두고, 여기서는 Vivox 초기화/로그인과 채널 입장·퇴장만 맡는다.
/// </summary>
[DefaultExecutionOrder(-900)]
public sealed class VoiceChatService : MonoBehaviour
{
    public enum Status { Offline, Connecting, Ready, Error }

    static VoiceChatService s_instance;
    static bool s_quitting;

    /// <summary>필요할 때 자동으로 만들어진다 (DontDestroyOnLoad).</summary>
    public static VoiceChatService Instance
    {
        get
        {
            if (s_instance == null && Application.isPlaying && !s_quitting)
            {
                var go = new GameObject("VoiceChatService");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<VoiceChatService>();
            }
            return s_instance;
        }
    }
    public static bool Exists => s_instance != null;

    public Status State { get; private set; } = Status.Offline;
    public string LastError { get; private set; } = "";
    /// <summary>지금 들어가 있는 방(세션) ID. 채널 이름을 만들 때 쓴다.</summary>
    public string SessionKey { get; private set; } = "";
    public bool MicMuted { get; private set; }
    public event Action StatusChanged;

    public string LobbyChannel => "vl_lobby_" + Sanitize(SessionKey);
    public string NearChannel => "vl_near_" + Sanitize(SessionKey);
    public string RadioChannel => "vl_radio_" + Sanitize(SessionKey);

    Task _loginTask;
    readonly HashSet<string> _wanted = new HashSet<string>();   // 들어가려는(들어간) 채널
    TransmissionMode _txMode = TransmissionMode.None;
    string _txChannel;
    bool _txApplying, _txDirty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_instance = null; s_quitting = false; }

    void Awake()
    {
        if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
        s_instance = this;
    }

    void OnApplicationQuit() => s_quitting = true;

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    public static string Sanitize(string key)
    {
        if (string.IsNullOrEmpty(key)) return "solo";
        var sb = new StringBuilder(key.Length);
        foreach (char c in key)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
            if (sb.Length >= 48) break;
        }
        return sb.Length == 0 ? "solo" : sb.ToString();
    }

    public bool IsLoggedIn
    {
        get
        {
            try { return VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn; }
            catch (Exception) { return false; }
        }
    }

    void SetState(Status s, string error = null)
    {
        State = s;
        if (error != null) LastError = error;
        StatusChanged?.Invoke();
    }

    // ------------------------------------------------------------------ login
    public async Task<bool> EnsureLoggedInAsync()
    {
        if (IsLoggedIn) { if (State != Status.Ready) SetState(Status.Ready); return true; }
        if (_loginTask == null || _loginTask.IsFaulted || _loginTask.IsCanceled || (_loginTask.IsCompleted && !IsLoggedIn)) _loginTask = LoginAsync();
        try
        {
            await _loginTask;
            return IsLoggedIn;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Voice] Vivox 로그인 실패: {e}");
            SetState(Status.Error, Short(e.Message));
            return false;
        }
    }

    async Task LoginAsync()
    {
        SetState(Status.Connecting);
        if (UnityServices.State == ServicesInitializationState.Uninitialized) await UnityServices.InitializeAsync();
        while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();

        if (VivoxService.Instance.InitializationState == VivoxInitializationState.Uninitialized)
            await VivoxService.Instance.InitializeAsync();
        while (VivoxService.Instance.InitializationState == VivoxInitializationState.Initializing) await Task.Yield();

        if (!VivoxService.Instance.IsLoggedIn)
        {
            await VivoxService.Instance.LoginAsync(new LoginOptions
            {
                // 말하는 중 표시를 위해 참가자 상태를 초당 5번 받는다
                ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.FivePerSecond,
            });
        }
        if (MicMuted) VivoxService.Instance.MuteInputDevice(); else VivoxService.Instance.UnmuteInputDevice();
        Debug.Log("[Voice] Vivox 로그인 완료");
        SetState(Status.Ready);
    }

    // ------------------------------------------------------------------ channels
    public bool IsInChannel(string channel)
    {
        try
        {
            var active = IsLoggedIn ? VivoxService.Instance.ActiveChannels : null;
            return active != null && active.ContainsKey(channel);
        }
        catch (Exception) { return false; }
    }

    public async Task JoinGroupAsync(string channel)
    {
        if (_wanted.Contains(channel)) return;
        _wanted.Add(channel);
        if (!await EnsureLoggedInAsync()) { _wanted.Remove(channel); return; }
        if (!_wanted.Contains(channel) || IsInChannel(channel)) return;   // 그 사이에 나가기로 했으면 들어가지 않음
        try
        {
            await VivoxService.Instance.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
            Debug.Log($"[Voice] 채널 입장: {channel}");
            if (!_wanted.Contains(channel)) await LeaveNowAsync(channel);
            else ReapplyTransmission();
        }
        catch (Exception e)
        {
            _wanted.Remove(channel);
            Debug.LogWarning($"[Voice] 채널 입장 실패 ({channel}): {e.Message}");
            SetState(Status.Error, Short(e.Message));
        }
    }

    public async Task JoinPositionalAsync(string channel, Channel3DProperties props)
    {
        if (_wanted.Contains(channel)) return;
        _wanted.Add(channel);
        if (!await EnsureLoggedInAsync()) { _wanted.Remove(channel); return; }
        if (!_wanted.Contains(channel) || IsInChannel(channel)) return;
        try
        {
            await VivoxService.Instance.JoinPositionalChannelAsync(channel, ChatCapability.AudioOnly, props);
            Debug.Log($"[Voice] 3D 채널 입장: {channel}");
            if (!_wanted.Contains(channel)) await LeaveNowAsync(channel);
            else ReapplyTransmission();
        }
        catch (Exception e)
        {
            _wanted.Remove(channel);
            Debug.LogWarning($"[Voice] 3D 채널 입장 실패 ({channel}): {e.Message}");
            SetState(Status.Error, Short(e.Message));
        }
    }

    public async Task LeaveAsync(string channel)
    {
        if (!_wanted.Remove(channel)) return;
        await LeaveNowAsync(channel);
    }

    async Task LeaveNowAsync(string channel)
    {
        try
        {
            if (IsInChannel(channel)) await VivoxService.Instance.LeaveChannelAsync(channel);
        }
        catch (Exception e) { Debug.LogWarning($"[Voice] 채널 나가기 경고 ({channel}): {e.Message}"); }
    }

    public async Task LeaveAllAsync()
    {
        var list = new List<string>(_wanted);
        _wanted.Clear();
        _txMode = TransmissionMode.None; _txChannel = null;
        foreach (var ch in list) await LeaveNowAsync(ch);
    }

    // ------------------------------------------------------------------ lobby / game
    /// <summary>로비 대기방에 들어갔을 때: 방 전체 음성 채널에 입장</summary>
    public async void EnterLobby(string sessionId)
    {
        if (SessionKey != sessionId) await LeaveAllAsync();
        SessionKey = sessionId ?? "";
        SetTransmission(TransmissionMode.Single, LobbyChannel);
        await JoinGroupAsync(LobbyChannel);
    }

    /// <summary>로비 대기방에서 나갔을 때</summary>
    public async void LeaveLobby()
    {
        await LeaveAllAsync();
        SessionKey = "";
    }

    /// <summary>게임 씬에 들어왔을 때: 로비 채널을 나가고 근거리 3D 채널에 입장 (무전 채널은 무전기를 가졌을 때만)</summary>
    public async Task EnterGameAsync(string sessionKeyOverride, Channel3DProperties props)
    {
        if (!string.IsNullOrEmpty(sessionKeyOverride) && sessionKeyOverride != SessionKey)
        {
            await LeaveAllAsync();
            SessionKey = sessionKeyOverride;
        }
        await LeaveAsync(LobbyChannel);
        SetTransmission(TransmissionMode.Single, NearChannel);
        await JoinPositionalAsync(NearChannel, props);
    }

    /// <summary>
    /// 송신 대상: Single(해당 채널만), All(들어간 모든 채널: 근거리 + 무전), None(말하지 않음).
    /// 채널에 아직 못 들어갔으면 입장 직후 다시 적용한다.
    /// </summary>
    public void SetTransmission(TransmissionMode mode, string channel = null)
    {
        if (_txMode == mode && _txChannel == channel) return;
        _txMode = mode; _txChannel = channel;
        ReapplyTransmission();
    }

    async void ReapplyTransmission()
    {
        if (_txApplying) { _txDirty = true; return; }
        _txApplying = true;
        try
        {
            do
            {
                _txDirty = false;
                if (!IsLoggedIn) break;
                var mode = _txMode; var ch = _txChannel;
                if (mode == TransmissionMode.Single && (string.IsNullOrEmpty(ch) || !IsInChannel(ch))) break;
                try { await VivoxService.Instance.SetChannelTransmissionModeAsync(mode, mode == TransmissionMode.Single ? ch : null); }
                catch (Exception e) { Debug.LogWarning($"[Voice] 송신 모드 변경 실패: {e.Message}"); }
            } while (_txDirty);
        }
        finally { _txApplying = false; }
    }

    // ------------------------------------------------------------------ mic
    public void SetMicMuted(bool muted)
    {
        MicMuted = muted;
        try
        {
            if (IsLoggedIn)
            {
                if (muted) VivoxService.Instance.MuteInputDevice();
                else VivoxService.Instance.UnmuteInputDevice();
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Voice] 마이크 설정 실패: {e.Message}"); }
        StatusChanged?.Invoke();
    }

    // ------------------------------------------------------------------ queries (UI용)
    /// <summary>해당 채널에서 그 플레이어(Authentication PlayerId)가 지금 말하고 있는지</summary>
    public bool IsSpeaking(string channel, string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return false;
        try
        {
            if (!IsLoggedIn || !VivoxService.Instance.ActiveChannels.TryGetValue(channel, out var list)) return false;
            foreach (var p in list) if (p.PlayerId == playerId) return p.SpeechDetected && !p.IsMuted;
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>해당 채널에 그 플레이어가 음성으로 연결되어 있는지</summary>
    public bool IsConnected(string channel, string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return false;
        try
        {
            if (!IsLoggedIn || !VivoxService.Instance.ActiveChannels.TryGetValue(channel, out var list)) return false;
            foreach (var p in list) if (p.PlayerId == playerId) return true;
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>해당 채널에서 말하고 있는 다른 사람들의 PlayerId</summary>
    public void GetSpeakers(string channel, List<string> result)
    {
        result.Clear();
        try
        {
            if (!IsLoggedIn || !VivoxService.Instance.ActiveChannels.TryGetValue(channel, out var list)) return;
            foreach (var p in list) if (!p.IsSelf && p.SpeechDetected && !p.IsMuted) result.Add(p.PlayerId);
        }
        catch (Exception) { }
    }

    /// <summary>내 마이크 입력 크기 (0~1)</summary>
    public float SelfEnergy(string channel)
    {
        try
        {
            if (!IsLoggedIn || !VivoxService.Instance.ActiveChannels.TryGetValue(channel, out var list)) return 0;
            foreach (var p in list) if (p.IsSelf) return Mathf.Clamp01(Mathf.Sqrt((float)p.AudioEnergy) * 1.2f);
        }
        catch (Exception) { }
        return 0;
    }

    public void Set3DPosition(GameObject listener, string channel)
    {
        if (listener == null) return;
        try
        {
            if (IsInChannel(channel)) VivoxService.Instance.Set3DPosition(listener, channel);
        }
        catch (Exception) { }
    }

    public string MyPlayerId
    {
        get
        {
            try { return AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : ""; }
            catch (Exception) { return ""; }
        }
    }

    static string Short(string m) => string.IsNullOrEmpty(m) ? "" : m.Length > 90 ? m.Substring(0, 90) + "..." : m;
}
