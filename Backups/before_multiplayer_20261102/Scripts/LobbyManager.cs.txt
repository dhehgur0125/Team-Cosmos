using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 멀티 대기실(로비).
/// Unity Multiplayer Services 세션(내부적으로 Lobby + Relay)을 사용한다.
///  - 방 만들기 / 방 목록 / 코드로 참가 / 빠른 참가
///  - 대기방: 플레이어 슬롯, 준비, 방장 강퇴, 게임 시작, 나가기
/// 방을 만들거나 들어가면 세션이 Relay로 NetworkManager(Host/Client)를 자동으로 시작하고,
/// 방장이 시작하면 NetworkSceneManager로 모든 대원이 게임 씬으로 넘어간다.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    const string KeyName = "name";
    const string KeyReady = "ready";
    const string KeyHostName = "host";
    const string KeyState = "state";
    const string PrefsNickname = "VL_Nickname";

    [Header("설정")]
    [Tooltip("게임 시작 시 불러올 씬 (빌드 설정에 등록되어 있어야 함)")]
    public string gameSceneName = "MapScene";
    [Range(2, 4)] public int defaultMaxPlayers = 4;
    [Tooltip("방 목록 자동 새로고침 간격(초)")]
    [Min(3f)] public float listRefreshInterval = 8f;

    [Header("공통")]
    public TMP_Text statusText;
    public GameObject loadingOverlay;
    public TMP_Text loadingText;

    [Header("메인 화면")]
    public GameObject mainPanel;
    public TMP_InputField nicknameInput;
    public Button createOpenButton;
    public Button quickJoinButton;
    public TMP_InputField joinCodeInput;
    public Button joinCodeButton;
    public Button refreshButton;
    public Transform roomListContent;
    public LobbyRoomListItem roomItemPrefab;
    public GameObject emptyListLabel;
    public TMP_Text roomCountText;

    [Header("방 만들기 창")]
    public GameObject createPanel;
    public TMP_InputField roomNameInput;
    public Button[] maxPlayerButtons;   // 2, 3, 4명
    public Toggle privateToggle;
    public Button createConfirmButton;
    public Button createCancelButton;

    [Header("대기방")]
    public GameObject roomPanel;
    public TMP_Text roomNameText;
    public TMP_Text roomCodeText;
    public TMP_Text roomInfoText;
    public Button copyCodeButton;
    public LobbyPlayerSlot[] slots;
    public Button readyButton;
    public TMP_Text readyButtonText;
    public Button startButton;
    public TMP_Text startHintText;
    public Button leaveButton;

    [Header("채팅")]
    public LobbyChat chat;
    public TMP_Text chatLogText;
    public TMP_InputField chatInput;
    public Button chatSendButton;
    public ScrollRect chatScroll;

    ISession _session;
    bool _busy;
    bool _leaving;
    bool _gameStarting;
    int _selectedMaxPlayers;
    float _nextListRefresh;
    float _nextHostCheck;
    readonly List<LobbyRoomListItem> _roomItems = new List<LobbyRoomListItem>();
    readonly Dictionary<string, string> _knownNames = new Dictionary<string, string>();
    readonly Queue<string> _chatLines = new Queue<string>();

    // ------------------------------------------------------------------ lifecycle
    void Awake()
    {
        _selectedMaxPlayers = Mathf.Clamp(defaultMaxPlayers, 2, 4);

        createOpenButton.onClick.AddListener(OpenCreatePanel);
        createCancelButton.onClick.AddListener(() => createPanel.SetActive(false));
        createConfirmButton.onClick.AddListener(() => _ = CreateRoomAsync());
        quickJoinButton.onClick.AddListener(() => _ = QuickJoinAsync());
        joinCodeButton.onClick.AddListener(() => _ = JoinByCodeAsync());
        refreshButton.onClick.AddListener(() => _ = RefreshRoomListAsync(true));
        readyButton.onClick.AddListener(() => _ = ToggleReadyAsync());
        startButton.onClick.AddListener(() => _ = StartGameAsync());
        leaveButton.onClick.AddListener(() => _ = LeaveRoomAsync());
        copyCodeButton.onClick.AddListener(CopyCode);
        chatSendButton.onClick.AddListener(SendChat);
        chatInput.onSubmit.AddListener(_ => SendChat());
        joinCodeInput.onSubmit.AddListener(code => _ = JoinByCodeAsync());
        nicknameInput.onEndEdit.AddListener(SaveNickname);

        for (int i = 0; i < maxPlayerButtons.Length; i++)
        {
            int count = i + 2;
            maxPlayerButtons[i].onClick.AddListener(() => SelectMaxPlayers(count));
        }

        if (chat != null) chat.MessageReceived += OnChatMessage;
    }

    async void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        nicknameInput.text = PlayerPrefs.GetString(PrefsNickname, "대원" + UnityEngine.Random.Range(1000, 9999));
        ShowPanel(mainPanel);
        createPanel.SetActive(false);

        SetBusy(true, "서버에 연결하는 중...");
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                options.SetProfile(LocalProfile());
                await UnityServices.InitializeAsync(options);
            }
            else
            {
                while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            Debug.Log($"[Lobby] 로그인 완료 PlayerId: {AuthenticationService.Instance.PlayerId}");
            SetStatus("준비 완료. 방을 만들거나 목록에서 들어가세요.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Unity Services 초기화/로그인 실패:\n{e}");
            SetStatus("서버 연결에 실패했어요. 인터넷 연결과 Unity Cloud 프로젝트 연결을 확인해 주세요.", true);
            SetBusy(false);
            return;
        }
        SetBusy(false);

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnNetworkDisconnected;

        await RefreshRoomListAsync(false);
    }

    void OnDestroy()
    {
        if (chat != null) chat.MessageReceived -= OnChatMessage;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnNetworkDisconnected;
        UnbindSession();
    }

    void Update()
    {
        if (_session == null && mainPanel.activeSelf && !_busy && Time.unscaledTime >= _nextListRefresh)
            _ = RefreshRoomListAsync(false);

        // 방장: 네트워크 연결 수가 바뀌면 시작 버튼 상태 갱신
        if (_session != null && _session.IsHost && Time.unscaledTime >= _nextHostCheck)
        {
            _nextHostCheck = Time.unscaledTime + 0.5f;
            UpdateRoomButtons();
        }
    }

    /// <summary>같은 PC에서 여러 개를 띄워도(에디터+빌드, Multiplayer Play Mode) 다른 플레이어로 로그인되도록 프로필을 분리한다.</summary>
    static string LocalProfile()
    {
        string seed = Application.isEditor ? Application.dataPath : Guid.NewGuid().ToString("N");
        unchecked
        {
            int h = 23;
            foreach (char ch in seed) h = h * 31 + ch;
            return "vl" + (h & 0x7fffffff).ToString("x");
        }
    }

    // ------------------------------------------------------------------ main panel
    string Nickname
    {
        get
        {
            string n = nicknameInput.text.Trim();
            if (string.IsNullOrEmpty(n)) n = "대원" + UnityEngine.Random.Range(1000, 9999);
            return n.Length > 16 ? n.Substring(0, 16) : n;
        }
    }

    void SaveNickname(string _)
    {
        PlayerPrefs.SetString(PrefsNickname, Nickname);
        PlayerPrefs.Save();
    }

    Dictionary<string, PlayerProperty> MyPlayerProperties(bool ready) => new Dictionary<string, PlayerProperty>
    {
        { KeyName, new PlayerProperty(Nickname, VisibilityPropertyOptions.Member) },
        { KeyReady, new PlayerProperty(ready ? "1" : "0", VisibilityPropertyOptions.Member) },
    };

    void OpenCreatePanel()
    {
        roomNameInput.text = Nickname + "의 방";
        privateToggle.isOn = false;
        SelectMaxPlayers(_selectedMaxPlayers);
        createPanel.SetActive(true);
    }

    void SelectMaxPlayers(int count)
    {
        _selectedMaxPlayers = count;
        for (int i = 0; i < maxPlayerButtons.Length; i++)
        {
            bool on = i + 2 == count;
            var colors = maxPlayerButtons[i].colors;
            maxPlayerButtons[i].image.color = on ? LobbyStyle.Accent : LobbyStyle.ButtonDark;
            var label = maxPlayerButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = on ? Color.white : LobbyStyle.TextDim;
        }
    }

    async Task RefreshRoomListAsync(bool manual)
    {
        _nextListRefresh = Time.unscaledTime + listRefreshInterval;
        if (UnityServices.State != ServicesInitializationState.Initialized || !AuthenticationService.Instance.IsSignedIn) return;
        if (manual) SetStatus("방 목록을 불러오는 중...");
        try
        {
            var query = new QuerySessionsOptions
            {
                Count = 30,
                FilterOptions = new List<FilterOption>
                {
                    new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater),
                },
            };
            QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(query);
            if (this == null || _session != null) return;

            foreach (var item in _roomItems) if (item != null) Destroy(item.gameObject);
            _roomItems.Clear();

            int shown = 0;
            foreach (ISessionInfo info in results.Sessions)
            {
                if (info.IsLocked) continue;
                var item = Instantiate(roomItemPrefab, roomListContent);
                item.gameObject.SetActive(true);
                string host = info.Properties != null && info.Properties.TryGetValue(KeyHostName, out var hp) ? hp.Value : "-";
                item.Setup(info.Id, info.Name, host, info.MaxPlayers - info.AvailableSlots, info.MaxPlayers, id => _ = JoinByIdAsync(id));
                _roomItems.Add(item);
                shown++;
            }
            emptyListLabel.SetActive(shown == 0);
            roomCountText.text = shown > 0 ? $"{shown}개" : "";
            if (manual) SetStatus(shown > 0 ? "방 목록을 새로 불러왔어요." : "열린 방이 없어요. 새로 만들어 보세요.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] 방 목록 조회 실패: {e.Message}");
            if (manual) SetStatus(FriendlyError(e), true);
        }
    }

    async Task CreateRoomAsync()
    {
        if (_busy) return;
        SaveNickname(null);
        string roomName = roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(roomName)) roomName = Nickname + "의 방";
        if (roomName.Length > 30) roomName = roomName.Substring(0, 30);

        createPanel.SetActive(false);
        await RunBusy("방을 만드는 중...", async () =>
        {
            var options = new SessionOptions
            {
                Name = roomName,
                MaxPlayers = _selectedMaxPlayers,
                IsPrivate = privateToggle.isOn,
                PlayerProperties = MyPlayerProperties(false),
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { KeyHostName, new SessionProperty(Nickname, VisibilityPropertyOptions.Public) },
                    { KeyState, new SessionProperty("lobby", VisibilityPropertyOptions.Public) },
                },
            }.WithRelayNetwork();

            var session = await MultiplayerService.Instance.CreateSessionAsync(options);
            EnterRoom(session);
            AddSystemLine($"방을 만들었어요. 참가 코드: <b>{session.Code}</b>");
        });
    }

    async Task JoinByIdAsync(string sessionId)
    {
        if (_busy) return;
        SaveNickname(null);
        await RunBusy("방에 들어가는 중...", async () =>
        {
            var session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId,
                new JoinSessionOptions { PlayerProperties = MyPlayerProperties(false) });
            EnterRoom(session);
        });
    }

    async Task JoinByCodeAsync()
    {
        if (_busy) return;
        string code = joinCodeInput.text.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("참가 코드를 입력해 주세요.", true);
            return;
        }
        SaveNickname(null);
        await RunBusy("코드로 방을 찾는 중...", async () =>
        {
            var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code,
                new JoinSessionOptions { PlayerProperties = MyPlayerProperties(false) });
            EnterRoom(session);
        });
    }

    async Task QuickJoinAsync()
    {
        if (_busy) return;
        SaveNickname(null);
        await RunBusy("들어갈 수 있는 방을 찾는 중...", async () =>
        {
            var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions
            {
                Count = 20,
                FilterOptions = new List<FilterOption> { new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater) },
            });
            foreach (var info in results.Sessions)
            {
                if (info.IsLocked || info.HasPassword) continue;
                try
                {
                    var session = await MultiplayerService.Instance.JoinSessionByIdAsync(info.Id,
                        new JoinSessionOptions { PlayerProperties = MyPlayerProperties(false) });
                    EnterRoom(session);
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Lobby] 빠른 참가 후보 실패 ({info.Name}): {e.Message}");
                }
            }

            // 들어갈 방이 없으면 새로 만든다
            SetLoadingText("열린 방이 없어서 새 방을 만드는 중...");
            var options = new SessionOptions
            {
                Name = Nickname + "의 방",
                MaxPlayers = defaultMaxPlayers,
                PlayerProperties = MyPlayerProperties(false),
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { KeyHostName, new SessionProperty(Nickname, VisibilityPropertyOptions.Public) },
                    { KeyState, new SessionProperty("lobby", VisibilityPropertyOptions.Public) },
                },
            }.WithRelayNetwork();
            var created = await MultiplayerService.Instance.CreateSessionAsync(options);
            EnterRoom(created);
            AddSystemLine("열린 방이 없어서 새 방을 만들었어요.");
        });
    }

    // ------------------------------------------------------------------ room
    void EnterRoom(ISession session)
    {
        _session = session;
        _leaving = false;
        _gameStarting = false;
        _knownNames.Clear();
        foreach (var p in session.Players) _knownNames[p.Id] = PlayerName(p);
        BindSession();

        _chatLines.Clear();
        chatLogText.text = "";
        ShowPanel(roomPanel);
        RefreshRoom();
        SetStatus(session.IsHost ? "방장입니다. 대원이 모두 준비하면 게임을 시작하세요." : "방에 들어왔어요. 준비를 눌러 주세요.");
        Debug.Log($"[Lobby] 방 입장: {session.Name} ({session.Id}) 코드 {session.Code} 방장={session.IsHost}");
    }

    void BindSession()
    {
        if (_session == null) return;
        _session.Changed += RefreshRoom;
        _session.PlayerJoined += OnPlayerJoined;
        _session.PlayerHasLeft += OnPlayerLeft;
        _session.RemovedFromSession += OnRemovedFromSession;
        _session.Deleted += OnSessionDeleted;
    }

    void UnbindSession()
    {
        if (_session == null) return;
        _session.Changed -= RefreshRoom;
        _session.PlayerJoined -= OnPlayerJoined;
        _session.PlayerHasLeft -= OnPlayerLeft;
        _session.RemovedFromSession -= OnRemovedFromSession;
        _session.Deleted -= OnSessionDeleted;
    }

    static string PlayerName(IReadOnlyPlayer p)
    {
        if (p.Properties != null && p.Properties.TryGetValue(KeyName, out var prop) && !string.IsNullOrEmpty(prop.Value))
            return prop.Value;
        return "대원";
    }

    static bool PlayerReady(IReadOnlyPlayer p) =>
        p.Properties != null && p.Properties.TryGetValue(KeyReady, out var prop) && prop.Value == "1";

    void OnPlayerJoined(string playerId)
    {
        // 이름은 다음 Changed 이벤트에서 들어오므로 잠시 뒤 표시
        _ = AnnounceJoinAsync(playerId);
    }

    async Task AnnounceJoinAsync(string playerId)
    {
        for (int i = 0; i < 20 && _session != null; i++)
        {
            var p = _session.GetPlayer(playerId);
            if (p != null && p.Properties != null && p.Properties.ContainsKey(KeyName))
            {
                _knownNames[playerId] = PlayerName(p);
                AddSystemLine($"{_knownNames[playerId]} 님이 들어왔어요.");
                return;
            }
            await Task.Delay(150);
        }
    }

    void OnPlayerLeft(string playerId)
    {
        string name = _knownNames.TryGetValue(playerId, out var n) ? n : "대원";
        _knownNames.Remove(playerId);
        AddSystemLine($"{name} 님이 나갔어요.");
        RefreshRoom();
    }

    void OnRemovedFromSession()
    {
        if (_leaving || _gameStarting) return;
        ReturnToMain("방에서 내보내졌어요.");
    }

    void OnSessionDeleted()
    {
        if (_leaving || _gameStarting) return;
        ReturnToMain("방장이 방을 닫았어요.");
    }

    void OnNetworkDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (_session == null || _leaving || _gameStarting || nm == null) return;
        if (!nm.IsServer && clientId == nm.LocalClientId)
        {
            _ = LeaveSilentlyAsync();
            ReturnToMain("방장과의 연결이 끊어졌어요.");
        }
    }

    void RefreshRoom()
    {
        if (_session == null || roomPanel == null) return;

        roomNameText.text = _session.Name;
        roomCodeText.text = string.IsNullOrEmpty(_session.Code) ? "------" : _session.Code;
        roomInfoText.text = $"{_session.PlayerCount} / {_session.MaxPlayers}명" + (_session.IsPrivate ? "  ·  비공개" : "");

        // 방장 먼저, 그다음 들어온 순서
        var players = new List<IReadOnlyPlayer>(_session.Players);
        players.Sort((a, b) =>
        {
            bool ah = a.Id == _session.Host, bh = b.Id == _session.Host;
            if (ah != bh) return ah ? -1 : 1;
            return a.Joined.CompareTo(b.Joined);
        });

        string myId = AuthenticationService.Instance.PlayerId;
        for (int i = 0; i < slots.Length; i++)
        {
            if (i < players.Count)
            {
                var p = players[i];
                bool isHost = p.Id == _session.Host;
                bool isMe = p.Id == myId;
                string pid = p.Id;
                _knownNames[p.Id] = PlayerName(p);
                slots[i].SetPlayer(PlayerName(p), isHost, isHost || PlayerReady(p), isMe,
                    _session.IsHost && !isMe, () => _ = KickAsync(pid));
            }
            else if (i < _session.MaxPlayers) slots[i].SetOpen();
            else slots[i].SetClosed();
        }
        UpdateRoomButtons();
    }

    bool AllGuestsReady(out int notReady)
    {
        notReady = 0;
        foreach (var p in _session.Players)
            if (p.Id != _session.Host && !PlayerReady(p)) notReady++;
        return notReady == 0;
    }

    void UpdateRoomButtons()
    {
        if (_session == null) return;
        bool host = _session.IsHost;
        readyButton.gameObject.SetActive(!host);
        startButton.gameObject.SetActive(host);

        if (!host)
        {
            var me = _session.GetPlayer(AuthenticationService.Instance.PlayerId);
            bool ready = me != null && PlayerReady(me);
            readyButtonText.text = ready ? "준비 취소" : "준비";
            readyButton.image.color = ready ? LobbyStyle.ButtonDark : LobbyStyle.Accent;
            readyButton.interactable = !_busy;
            startHintText.text = ready ? "방장이 시작하기를 기다리는 중..." : "준비를 누르면 방장이 게임을 시작할 수 있어요.";
            return;
        }

        var nm = NetworkManager.Singleton;
        int connected = nm != null && nm.IsServer ? nm.ConnectedClientsIds.Count : 0;
        bool allReady = AllGuestsReady(out int notReady);
        bool allConnected = connected >= _session.PlayerCount;
        startButton.interactable = !_busy && allReady && allConnected;
        if (!allConnected) startHintText.text = "들어오는 대원의 연결을 기다리는 중...";
        else if (!allReady) startHintText.text = $"준비하지 않은 대원이 {notReady}명 있어요.";
        else startHintText.text = _session.PlayerCount == 1 ? "혼자서도 시작할 수 있어요." : "모두 준비됐어요. 게임을 시작하세요!";
    }

    async Task ToggleReadyAsync()
    {
        if (_session == null || _session.IsHost || _busy) return;
        var me = _session.GetPlayer(AuthenticationService.Instance.PlayerId);
        bool next = !(me != null && PlayerReady(me));
        readyButton.interactable = false;
        try
        {
            _session.CurrentPlayer.SetProperty(KeyReady, new PlayerProperty(next ? "1" : "0", VisibilityPropertyOptions.Member));
            await _session.SaveCurrentPlayerDataAsync();
            RefreshRoom();
        }
        catch (Exception e)
        {
            SetStatus(FriendlyError(e), true);
        }
        finally
        {
            readyButton.interactable = true;
        }
    }

    async Task KickAsync(string playerId)
    {
        if (_session == null || !_session.IsHost) return;
        string name = _knownNames.TryGetValue(playerId, out var n) ? n : "대원";
        try
        {
            await _session.AsHost().RemovePlayerAsync(playerId);
            AddSystemLine($"{name} 님을 내보냈어요.");
        }
        catch (Exception e)
        {
            SetStatus(FriendlyError(e), true);
        }
    }

    async Task StartGameAsync()
    {
        if (_session == null || !_session.IsHost || _busy) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || nm.SceneManager == null)
        {
            SetStatus("네트워크가 아직 준비되지 않았어요. 잠시 후 다시 시도해 주세요.", true);
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SetStatus($"'{gameSceneName}' 씬이 빌드 설정에 없어요.", true);
            return;
        }

        await RunBusy("게임을 시작하는 중...", async () =>
        {
            _gameStarting = true;
            var host = _session.AsHost();
            host.IsLocked = true;   // 시작 후에는 새로 들어올 수 없게
            host.SetProperty(KeyState, new SessionProperty("ingame", VisibilityPropertyOptions.Public));
            await host.SavePropertiesAsync();

            if (chat != null) chat.SendSystem("게임을 시작합니다!");
            var status = nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                _gameStarting = false;
                host.IsLocked = false;
                host.SetProperty(KeyState, new SessionProperty("lobby", VisibilityPropertyOptions.Public));
                await host.SavePropertiesAsync();
                throw new Exception($"씬 전환 실패: {status}");
            }
        });
    }

    async Task LeaveRoomAsync()
    {
        if (_session == null) return;
        await RunBusy("방에서 나가는 중...", async () =>
        {
            await LeaveSilentlyAsync();
            ReturnToMain("방에서 나왔어요.");
        });
    }

    async Task LeaveSilentlyAsync()
    {
        var s = _session;
        if (s == null) return;
        _leaving = true;
        UnbindSession();
        _session = null;
        try
        {
            if (s.IsHost) await s.AsHost().DeleteAsync();   // 방장이 나가면 방을 닫는다
            else await s.LeaveAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] 나가기 처리 중 오류(무시): {e.Message}");
        }
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) nm.Shutdown();
    }

    void ReturnToMain(string message)
    {
        UnbindSession();
        _session = null;
        _gameStarting = false;
        ShowPanel(mainPanel);
        SetStatus(message);
        _nextListRefresh = 0f;
    }

    void CopyCode()
    {
        if (_session == null || string.IsNullOrEmpty(_session.Code)) return;
        GUIUtility.systemCopyBuffer = _session.Code;
        SetStatus($"참가 코드 {_session.Code}를 복사했어요.");
    }

    // ------------------------------------------------------------------ chat
    void SendChat()
    {
        string text = chatInput.text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (text.Length > 60) text = text.Substring(0, 60);
        chatInput.text = "";
        chatInput.ActivateInputField();
        if (chat == null || !chat.IsSpawned)
        {
            AddSystemLine("아직 채팅에 연결되지 않았어요.");
            return;
        }
        chat.Send(Nickname, text);
    }

    void OnChatMessage(string sender, string text, bool system)
    {
        if (system) AddSystemLine(text);
        else AddLine($"<color=#E0443A><b>{Escape(sender)}</b></color>  {Escape(text)}");
    }

    void AddSystemLine(string text) => AddLine($"<color=#7FA7B8>{text}</color>");

    void AddLine(string line)
    {
        _chatLines.Enqueue(line);
        while (_chatLines.Count > 50) _chatLines.Dequeue();
        chatLogText.text = string.Join("\n", _chatLines);
        Canvas.ForceUpdateCanvases();
        if (chatScroll != null) chatScroll.verticalNormalizedPosition = 0f;
    }

    static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");

    // ------------------------------------------------------------------ helpers
    void ShowPanel(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        roomPanel.SetActive(panel == roomPanel);
        if (panel != mainPanel) createPanel.SetActive(false);
    }

    async Task RunBusy(string message, Func<Task> action)
    {
        if (_busy) return;
        SetBusy(true, message);
        try
        {
            await action();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] {message} 실패:\n{e}");
            SetStatus(FriendlyError(e), true);
            if (_session == null) ShowPanel(mainPanel);
        }
        finally
        {
            SetBusy(false);
            if (_session != null) UpdateRoomButtons();
        }
    }

    void SetBusy(bool busy, string message = null)
    {
        _busy = busy;
        if (loadingOverlay != null) loadingOverlay.SetActive(busy);
        if (busy && message != null) SetLoadingText(message);
    }

    void SetLoadingText(string message)
    {
        if (loadingText != null) loadingText.text = message;
    }

    void SetStatus(string message, bool error = false)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = error ? LobbyStyle.Error : LobbyStyle.TextDim;
    }

    static string FriendlyError(Exception e)
    {
        if (e is SessionException se)
        {
            switch (se.Error)
            {
                case SessionError.SessionNotFound: return "방을 찾을 수 없어요. 코드가 맞는지 확인해 주세요.";
                case SessionError.SessionDeleted: return "이미 닫힌 방이에요.";
                case SessionError.Forbidden: return "들어갈 수 없는 방이에요. (가득 찼거나 이미 시작했어요)";
                case SessionError.RateLimitExceeded: return "요청이 너무 많아요. 잠시 후 다시 시도해 주세요.";
                case SessionError.NotAuthorized: return "로그인이 만료됐어요. 게임을 다시 실행해 주세요.";
                case SessionError.NetworkManagerNotInitialized:
                case SessionError.NetworkManagerStartFailed:
                case SessionError.NetworkSetupFailed: return "네트워크를 시작하지 못했어요. 다시 시도해 주세요.";
            }
            return $"오류: {se.Error}";
        }
        string m = e.Message ?? "";
        if (m.Contains("full", StringComparison.OrdinalIgnoreCase)) return "방이 가득 찼어요.";
        if (m.Contains("not found", StringComparison.OrdinalIgnoreCase)) return "방을 찾을 수 없어요.";
        return "문제가 생겼어요: " + (m.Length > 80 ? m.Substring(0, 80) + "..." : m);
    }
}

/// <summary>로비 UI 공통 색상.</summary>
public static class LobbyStyle
{
    public static readonly Color Accent = new Color(0.878f, 0.267f, 0.227f);      // #E0443A
    public static readonly Color AccentDark = new Color(0.45f, 0.12f, 0.1f);
    public static readonly Color Panel = new Color(0.07f, 0.085f, 0.105f, 0.92f);
    public static readonly Color PanelLight = new Color(0.12f, 0.14f, 0.17f, 0.95f);
    public static readonly Color ButtonDark = new Color(0.17f, 0.19f, 0.23f);
    public static readonly Color Text = new Color(0.93f, 0.95f, 0.97f);
    public static readonly Color TextDim = new Color(0.6f, 0.66f, 0.72f);
    public static readonly Color Ready = new Color(0.31f, 0.82f, 0.52f);
    public static readonly Color Error = new Color(1f, 0.45f, 0.4f);
}
