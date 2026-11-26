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
    /// <summary>죽은 대원끼리만 쓰는 음성 채널</summary>
    public string DeadChannel => "vl_dead_" + Sanitize(SessionKey);

    Task _loginTask;
    // 들어가 있어야 하는 채널 → 3D 채널이면 그 설정 (그룹 채널은 null)
    // 실제로 들어가 있는지는 Vivox(ActiveChannels)가 기준이고, 다르면 SyncAsync가 맞춘다.
    // 예전엔 '들어가려는 채널' 목록에만 있으면 다시 들어가지 않아서, 입장이 한 번 실패하거나 Vivox 연결이 잠깐 끊기거나
    // 나가는 중에 다시 들어가려 하면 그 층이 끝날 때까지 근거리·무전이 조용히 끊겨 있었다.
    readonly Dictionary<string, Channel3DProperties> _wanted = new Dictionary<string, Channel3DProperties>();
    readonly HashSet<string> _syncing = new HashSet<string>();   // 입장·퇴장 작업 중인 채널 (한 채널에 작업은 하나씩)
    readonly HashSet<string> _resync = new HashSet<string>();    // 작업 중에 원하는 상태가 바뀜 → 끝나면 한 번 더 맞춘다
    readonly Dictionary<string, float> _retryAt = new Dictionary<string, float>();   // 실패한 채널을 다시 시도할 시각
    readonly Dictionary<string, int> _failCount = new Dictionary<string, int>();
    float _nextWatch;
    TransmissionMode _txMode = TransmissionMode.None;
    string _txChannel;
    bool _txApplying, _txDirty;
    TransmissionMode _appliedMode = TransmissionMode.None;   // Vivox에 실제로 적용된 송신 대상
    string _appliedChannel;

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
        GameAudio.ApplyVoice();   // 설정의 음성 채팅·마이크 크기
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

    bool InAnyChannel()
    {
        try { return IsLoggedIn && VivoxService.Instance.ActiveChannels.Count > 0; }
        catch (Exception) { return false; }
    }

    public bool IsWanted(string channel) => _wanted.ContainsKey(channel);

    public Task JoinGroupAsync(string channel) => WantAsync(channel, null);

    public Task JoinPositionalAsync(string channel, Channel3DProperties props) => WantAsync(channel, props ?? new Channel3DProperties());

    Task WantAsync(string channel, Channel3DProperties props)
    {
        if (string.IsNullOrEmpty(channel)) return Task.CompletedTask;
        _wanted[channel] = props;
        _retryAt.Remove(channel); _failCount.Remove(channel);   // 새로 들어가려는 것이면 기다리지 않고 바로
        return SyncAsync(channel);
    }

    public Task LeaveAsync(string channel)
    {
        if (string.IsNullOrEmpty(channel) || !_wanted.Remove(channel)) return Task.CompletedTask;
        _retryAt.Remove(channel); _failCount.Remove(channel);
        return SyncAsync(channel);
    }

    /// <summary>
    /// 그 채널의 실제 상태(들어가 있는지)를 원하는 상태(_wanted)에 맞춘다. 한 채널에 작업은 하나씩만 하고,
    /// 작업 중에 원하는 상태가 바뀌면(예: 나가는 중에 다시 들어가기) 끝난 뒤 다시 맞춘다.
    /// 실패하면 잠시 뒤 Update의 점검이 다시 시도한다.
    /// </summary>
    async Task SyncAsync(string channel)
    {
        if (_syncing.Contains(channel)) { _resync.Add(channel); return; }
        _syncing.Add(channel);
        try
        {
            do
            {
                _resync.Remove(channel);
                bool want = _wanted.TryGetValue(channel, out var props);
                if (want)
                {
                    if (!await EnsureLoggedInAsync()) { Backoff(channel); break; }
                    if (!_wanted.ContainsKey(channel) || IsInChannel(channel)) continue;
                    try
                    {
                        if (props != null) await VivoxService.Instance.JoinPositionalChannelAsync(channel, ChatCapability.AudioOnly, props);
                        else await VivoxService.Instance.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
                        Debug.Log($"[Voice] {(props != null ? "3D 채널" : "채널")} 입장: {channel}");
                        _retryAt.Remove(channel); _failCount.Remove(channel);
                        if (State != Status.Ready) SetState(Status.Ready);
                        ReapplyTransmission();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Voice] 채널 입장 실패 ({channel}): {e.Message} → 잠시 뒤 다시 시도");
                        SetState(Status.Error, Short(e.Message));
                        Backoff(channel);
                        break;
                    }
                }
                else if (IsInChannel(channel))
                {
                    await LeaveNowAsync(channel);
                }
            } while (_resync.Contains(channel));
        }
        finally
        {
            _syncing.Remove(channel);
            _resync.Remove(channel);
        }
    }

    void Backoff(string channel)
    {
        _failCount.TryGetValue(channel, out int n);
        _failCount[channel] = ++n;
        _retryAt[channel] = Time.unscaledTime + Mathf.Min(2f * n, 10f);   // 2, 4, 6 … 10초 간격
    }

    async Task LeaveNowAsync(string channel)
    {
        try
        {
            if (IsInChannel(channel))
            {
                // 지금 말하고 있는(송신 중인) 채널이면 먼저 송신을 멈춘다 (나간 채널로 송신 설정이 남아 Vivox 오류가 나는 것 방지)
                if (_appliedMode == TransmissionMode.All || (_appliedMode == TransmissionMode.Single && _appliedChannel == channel))
                {
                    try { await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None); } catch (Exception) { }
                    _appliedMode = TransmissionMode.None; _appliedChannel = null;
                }
                await VivoxService.Instance.LeaveChannelAsync(channel);
                Debug.Log($"[Voice] 채널 나감: {channel}");
                ReapplyTransmission();
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Voice] 채널 나가기 경고 ({channel}): {e.Message}"); }
    }

    public async Task LeaveAllAsync()
    {
        var list = new List<string>(_wanted.Keys);
        try { if (IsLoggedIn) foreach (var ch in VivoxService.Instance.ActiveChannels.Keys) if (!list.Contains(ch)) list.Add(ch); }
        catch (Exception) { }
        _wanted.Clear();
        _retryAt.Clear(); _failCount.Clear();
        _txMode = TransmissionMode.None; _txChannel = null;
        foreach (var ch in list) await SyncAsync(ch);
        if (list.Count > 0 || _appliedMode != TransmissionMode.None) { _appliedMode = TransmissionMode.None; _appliedChannel = null; }
    }

    /// <summary>
    /// 1.5초마다 점검: 들어가 있어야 하는 채널에 없으면 다시 들어가고(입장 실패·Vivox 연결 끊김·로그아웃 뒤 복구),
    /// 나와야 하는 채널에 남아 있으면 나오고, 실제 송신 채널이 정해 둔 것과 다르면 다시 맞춘다.
    /// </summary>
    void Update()
    {
        if (Time.unscaledTime < _nextWatch) return;
        _nextWatch = Time.unscaledTime + 1.5f;
        if (_wanted.Count == 0 && !InAnyChannel()) return;

        foreach (var ch in new List<string>(_wanted.Keys))
        {
            if (_syncing.Contains(ch) || IsInChannel(ch)) continue;
            if (_retryAt.TryGetValue(ch, out float at) && Time.unscaledTime < at) continue;
            if (_failCount.ContainsKey(ch) || !IsLoggedIn) Debug.Log($"[Voice] 채널 다시 연결 시도: {ch}");
            else Debug.LogWarning($"[Voice] 들어가 있어야 하는 채널에 없음 → 다시 입장: {ch}");
            _ = SyncAsync(ch);
        }
        try
        {
            if (IsLoggedIn)
                foreach (var ch in new List<string>(VivoxService.Instance.ActiveChannels.Keys))
                    if (!_wanted.ContainsKey(ch) && !_syncing.Contains(ch) && ch.StartsWith("vl_")) _ = SyncAsync(ch);
        }
        catch (Exception) { }

        if (!_txApplying && IsLoggedIn && !TransmissionMatches()) ReapplyTransmission();
    }

    /// <summary>Vivox가 실제로 말하고 있는 채널이 정해 둔 송신 대상과 같은지 (채널 재입장·연결 복구 뒤 어긋날 수 있음)</summary>
    bool TransmissionMatches()
    {
        try
        {
            var tx = VivoxService.Instance.TransmittingChannels;
            if (tx == null) return true;
            var mode = _txMode; var ch = _txChannel;
            if (mode == TransmissionMode.Single && (string.IsNullOrEmpty(ch) || !IsInChannel(ch))) mode = TransmissionMode.None;
            switch (mode)
            {
                case TransmissionMode.None: return tx.Count == 0;
                case TransmissionMode.Single: return tx.Count == 1 && tx[0] == ch;
                default: return tx.Count == VivoxService.Instance.ActiveChannels.Count;
            }
        }
        catch (Exception) { return true; }
    }

    // ------------------------------------------------------------------ lobby / game
    /// <summary>로비 대기방에 들어갔을 때: 방 전체 음성 채널에 입장</summary>
    public async void EnterLobby(string sessionId)
    {
        sessionId = sessionId ?? "";
        if (SessionKey != sessionId)
        {
            // 방 이름(SessionKey)은 기다리기 전에 바꾼다: 나가는 동안 게임 씬이 먼저 시작되면 바뀐 이름으로 채널에 들어가야 한다
            var leaving = LeaveAllAsync();
            SessionKey = sessionId;
            await leaving;
        }
        else
        {
            // 같은 방의 게임이 끝나 대기방으로 돌아옴: 게임 중 채널(근거리·무전·사망자)이 남아 있으면 나간다
            // (층 이동 도중 게임이 끝나면 게임 음성 쪽에서 채널을 그대로 두고 넘어가서 대기방에서도 계속 들렸다)
            _ = LeaveAsync(NearChannel);
            _ = LeaveAsync(RadioChannel);
            _ = LeaveAsync(DeadChannel);
        }
        if (SessionKey != sessionId) return;   // 그사이 다른 방으로 바뀜
        SetTransmission(TransmissionMode.Single, LobbyChannel);
        await JoinGroupAsync(LobbyChannel);
    }

    /// <summary>로비 대기방에서 나갔을 때</summary>
    public async void LeaveLobby()
    {
        // 이름을 먼저 비운다: 예전엔 나가기가 끝난 뒤에 비워서, 그사이 다시 참가해 게임에 들어가면
        // 게임 채널 이름이 'solo'로 바뀌어 위치·무전이 엉뚱한 채널을 가리켰다
        var leaving = LeaveAllAsync();
        SessionKey = "";
        await leaving;
    }

    /// <summary>게임 씬에 들어왔을 때: 로비 채널을 나가고 근거리 3D 채널에 입장 (무전 채널은 무전기를 가졌을 때만)</summary>
    public async Task EnterGameAsync(string sessionKeyOverride, Channel3DProperties props)
    {
        if (!string.IsNullOrEmpty(sessionKeyOverride) && sessionKeyOverride != SessionKey)
        {
            var leaving = LeaveAllAsync();
            SessionKey = sessionKeyOverride;
            await leaving;
        }
        _ = LeaveAsync(LobbyChannel);
        SetTransmission(TransmissionMode.Single, NearChannel);
        await JoinPositionalAsync(NearChannel, props);
    }

    /// <summary>
    /// 내 대원이 죽었을 때: 사망자 채널에만 말한다(살아있는 대원에게는 들리지 않음).
    /// 근거리 채널에는 듣기만 하려고 남는다 → 관전 중인 대원 자리에서 그 대원의 목소리를 듣는다 (InGameVoiceChat이 듣는 위치를 옮김).
    /// 무전 채널에서는 나간다.
    /// </summary>
    public async Task EnterDeadAsync(Channel3DProperties props)
    {
        SetTransmission(TransmissionMode.Single, DeadChannel);   // 사망자 채널에 들어가기 전까지는 아무 데도 말하지 않음 (ReapplyTransmission)
        await LeaveAsync(RadioChannel);
        await JoinGroupAsync(DeadChannel);
        await JoinPositionalAsync(NearChannel, props);   // 이미 들어가 있으면 그대로 (듣기 전용)
    }

    /// <summary>고치에서 구출됐을 때: 사망자 채널에서 나와 근거리 채널에 다시 말한다.</summary>
    public async Task LeaveDeadAsync(Channel3DProperties props)
    {
        SetTransmission(TransmissionMode.Single, NearChannel);
        await LeaveAsync(DeadChannel);
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
                if (mode == TransmissionMode.Single && (string.IsNullOrEmpty(ch) || !IsInChannel(ch)))
                {
                    // 말할 채널에 아직 못 들어갔으면 그동안은 아무 데도 말하지 않는다
                    // (예: 죽은 직후 사망자 채널에 들어가기 전에 근거리 채널로 목소리가 새는 것 방지)
                    // 채널에 들어가는 순간 Vivox가 송신 대상을 바꿀 수 있어서, 들어가 있는 채널이 있으면 매번 다시 끈다
                    if (!InAnyChannel()) break;
                    mode = TransmissionMode.None; ch = null;
                }
                try
                {
                    await VivoxService.Instance.SetChannelTransmissionModeAsync(mode, mode == TransmissionMode.Single ? ch : null);
                    _appliedMode = mode; _appliedChannel = mode == TransmissionMode.Single ? ch : null;
                }
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

    /// <summary>
    /// 해당 채널에서 그 플레이어 목소리를 내 쪽에서만 끄거나 다시 켠다 (상대방·다른 대원에게는 영향 없음).
    /// 무전 중인 대원의 육성을 멀리 있는 대원에게 들리지 않게 할 때 사용.
    /// </summary>
    /// <returns>채널에서 그 플레이어를 찾아 적용했으면 true</returns>
    public bool SetLocalMute(string channel, string playerId, bool mute)
    {
        if (string.IsNullOrEmpty(playerId)) return false;
        bool applied = false;
        try
        {
            if (!IsLoggedIn || !VivoxService.Instance.ActiveChannels.TryGetValue(channel, out var list)) return false;
            foreach (var p in list)
            {
                if (p.IsSelf || p.PlayerId != playerId) continue;
                if (mute) p.MutePlayerLocally(); else p.UnmutePlayerLocally();
                applied = true;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Voice] 로컬 음소거 실패: {e.Message}"); }
        return applied;
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
