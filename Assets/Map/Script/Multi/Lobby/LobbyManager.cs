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
using VoidLast.Player;

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
    const string KeyUnique = "unique";   // 고른 고유 아이템 (UniqueItems.Code)
    const string PrefsNickname = "VL_Nickname";

    [Header("설정")]
    [Tooltip("게임 시작 시 불러올 씬 (빌드 설정에 등록되어 있어야 함)")]
    public string gameSceneName = "Stage2_Ship_2f";
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

    [Header("음성 채팅")]
    public Button micButton;
    public TMP_Text micButtonText;
    public TMP_Text voiceStatusText;

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
    readonly string[] _slotPlayerIds = new string[4];
    float _nextVoiceRefresh;

    // 고유 아이템 고르기 (대기방 오른쪽 아래 카드에 코드로 만든다)
    Button[] _uniqueButtons;
    TMP_Text[] _uniqueLabels;
    readonly Dictionary<UniqueItemKind, string> _uniqueOwners = new Dictionary<UniqueItemKind, string>();
    bool _savingUnique;

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

        if (chat != null) { chat.MessageReceived += OnChatMessage; chat.GameStartChanged += OnGameStartChanged; }
        if (micButton != null) micButton.onClick.AddListener(ToggleMic);
        BuildUniquePicker();
        BuildBackToStartButton();
    }

    async void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // 이전 게임의 층 이동 정보·연출 상태 초기화
        FloorCarryOver.ResetAll();
        LobbyPlayerSpawner.KeywordOverride = null;
        VoidLast.Player.FpsInput.Blocked = false;
        VoidLast.Player.FpsHealth.DamageBlocked = false;
        ScreenFader.Clear();
        LoadingScreen.Subtitle = null;

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
            SetStatus(string.IsNullOrEmpty(Stage2NetworkBootstrap.LobbyReturnMessage) ? "준비 완료. 방을 만들거나 목록에서 들어가세요." : Stage2NetworkBootstrap.LobbyReturnMessage);
            Stage2NetworkBootstrap.LobbyReturnMessage = null;
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

        if (TryResumeRoom()) return;   // 게임이 끝나 같은 방으로 돌아온 경우
        await RefreshRoomListAsync(false);
    }

    /// <summary>
    /// 게임 오버 등으로 방장이 모든 대원을 로비 씬으로 데려온 경우: 연결과 방(세션)이 그대로 남아 있으므로 바로 대기방을 보여준다.
    /// 방장은 방을 다시 열고(잠금 해제, 상태 lobby), 모두 준비 상태를 해제한다.
    /// </summary>
    bool TryResumeRoom()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return false;
        ISession session = null;
        try
        {
            foreach (var kv in MultiplayerService.Instance.Sessions) { session = kv.Value; break; }
        }
        catch (Exception) { }
        if (session == null) return false;

        EnterRoom(session);
        AddSystemLine("게임이 끝나 대기방으로 돌아왔어요.");
        SetStatus(session.IsHost ? "게임 오버 · 대기방으로 돌아왔어요. 대원이 모두 준비하면 다시 시작할 수 있어요." : "게임 오버 · 대기방으로 돌아왔어요. 준비를 눌러 주세요.");
        _ = ResetRoomAfterGameAsync(session);
        return true;
    }

    async Task ResetRoomAfterGameAsync(ISession session)
    {
        try
        {
            if (session.IsHost)
            {
                var host = session.AsHost();
                host.IsLocked = false;
                host.SetProperty(KeyState, new SessionProperty("lobby", VisibilityPropertyOptions.Public));
                await host.SavePropertiesAsync();
            }
            session.CurrentPlayer.SetProperty(KeyReady, new PlayerProperty("0", VisibilityPropertyOptions.Member));
            await session.SaveCurrentPlayerDataAsync();
            if (_session == session) RefreshRoom();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] 대기방 복귀 정리 중 경고: {e.Message}");
        }
    }

    void OnDestroy()
    {
        if (chat != null) { chat.MessageReceived -= OnChatMessage; chat.GameStartChanged -= OnGameStartChanged; }
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnNetworkDisconnected;
        UnbindSession();
    }

    void Update()
    {
        if (_backToStart != null && _backToStart.activeSelf != mainPanel.activeSelf) _backToStart.SetActive(mainPanel.activeSelf);
        if (_session == null && mainPanel.activeSelf && !_busy && Time.unscaledTime >= _nextListRefresh)
            _ = RefreshRoomListAsync(false);

        // 방장: 네트워크 연결 수가 바뀌면 시작 버튼 상태 갱신
        if (_session != null && _session.IsHost && Time.unscaledTime >= _nextHostCheck)
        {
            _nextHostCheck = Time.unscaledTime + 0.5f;
            UpdateRoomButtons();
        }

        if (_session != null && Time.unscaledTime >= _nextVoiceRefresh)
        {
            _nextVoiceRefresh = Time.unscaledTime + 0.12f;
            RefreshVoice();
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

    /// <summary>방을 만들거나 들어갈 때의 내 정보. 고유 아이템은 방에 들어간 뒤 고른다</summary>
    Dictionary<string, PlayerProperty> MyPlayerProperties(bool ready)
    {
        UniqueItems.LocalChoice = UniqueItemKind.None;
        return new Dictionary<string, PlayerProperty>
        {
            { KeyName, new PlayerProperty(Nickname, VisibilityPropertyOptions.Member) },
            { KeyReady, new PlayerProperty(ready ? "1" : "0", VisibilityPropertyOptions.Member) },
            { KeyUnique, new PlayerProperty("", VisibilityPropertyOptions.Member) },
        };
    }

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
        VoiceChatService.Instance.EnterLobby(session.Id);   // 대기방 음성 채팅 입장
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

    static UniqueItemKind PlayerUnique(IReadOnlyPlayer p) =>
        p.Properties != null && p.Properties.TryGetValue(KeyUnique, out var prop) ? UniqueItems.Parse(prop.Value) : UniqueItemKind.None;

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

        // 고유 아이템 주인: 같은 아이템을 둘이 동시에 골랐으면 방장 → 먼저 들어온 대원이 가진다
        _uniqueOwners.Clear();
        foreach (var p in players)
        {
            var kind = PlayerUnique(p);
            if (kind != UniqueItemKind.None && !_uniqueOwners.ContainsKey(kind)) _uniqueOwners[kind] = p.Id;
        }

        string myId = AuthenticationService.Instance.PlayerId;
        for (int i = 0; i < slots.Length; i++)
        {
            _slotPlayerIds[i] = i < players.Count ? players[i].Id : null;
            if (i < players.Count)
            {
                var p = players[i];
                bool isHost = p.Id == _session.Host;
                bool isMe = p.Id == myId;
                string pid = p.Id;
                _knownNames[p.Id] = PlayerName(p);
                slots[i].SetPlayer(PlayerName(p), isHost, isHost || PlayerReady(p), isMe,
                    _session.IsHost && !isMe, () => _ = KickAsync(pid));
                var kind = PlayerUnique(p);
                slots[i].SetUniqueItem(kind != UniqueItemKind.None ? UniqueItems.DisplayName(kind) : null);
            }
            else if (i < _session.MaxPlayers) slots[i].SetOpen();
            else slots[i].SetClosed();
        }

        // 내 선택 확인: 다른 대원이 먼저 같은 아이템을 골랐으면 내 선택을 지운다
        var me = players.Find(p => p.Id == myId);
        if (me != null && !_savingUnique)
        {
            var mine = PlayerUnique(me);
            if (mine != UniqueItemKind.None && _uniqueOwners.TryGetValue(mine, out var owner) && owner != myId)
            {
                string other = _knownNames.TryGetValue(owner, out var n) ? n : "다른 대원";
                SetStatus($"{other} 님이 먼저 '{UniqueItems.DisplayName(mine)}'을(를) 골랐어요. 다른 고유 아이템을 골라 주세요.", true);
                _ = SaveUniqueAsync(UniqueItemKind.None, true);
            }
            else UniqueItems.LocalChoice = mine;
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

        UpdateUniquePicker();
        var myPlayer = _session.GetPlayer(AuthenticationService.Instance.PlayerId);
        bool picked = myPlayer != null && PlayerUnique(myPlayer) != UniqueItemKind.None;

        if (!host)
        {
            bool ready = myPlayer != null && PlayerReady(myPlayer);
            readyButtonText.text = ready ? "준비 취소" : "준비";
            readyButton.image.color = ready ? LobbyStyle.ButtonDark : LobbyStyle.Accent;
            readyButton.interactable = !_busy && !_savingUnique && (ready || picked);   // 고유 아이템을 골라야 준비할 수 있다
            startHintText.text = ready ? "방장이 시작하기를 기다리는 중..." : !picked ? "고유 아이템을 하나 고른 뒤 준비를 눌러 주세요." : "준비를 누르면 방장이 게임을 시작할 수 있어요.";
            return;
        }

        var nm = NetworkManager.Singleton;
        int connected = nm != null && nm.IsServer ? nm.ConnectedClientsIds.Count : 0;
        bool allReady = AllGuestsReady(out int notReady);
        bool allConnected = connected >= _session.PlayerCount;
        bool allPicked = AllPickedUniqueItems(out int notPicked);
        startButton.interactable = !_busy && !_savingUnique && allReady && allConnected && allPicked;
        if (!allConnected) startHintText.text = "들어오는 대원의 연결을 기다리는 중...";
        else if (!picked) startHintText.text = "고유 아이템을 하나 골라 주세요.";
        else if (!allReady) startHintText.text = $"준비하지 않은 대원이 {notReady}명 있어요.";
        else if (!allPicked) startHintText.text = $"고유 아이템을 고르지 않은 대원이 {notPicked}명 있어요.";
        else startHintText.text = _session.PlayerCount == 1 ? "혼자서도 시작할 수 있어요." : "모두 준비됐어요. 게임을 시작하세요!";
    }

    /// <summary>모든 대원이 서로 다른 고유 아이템을 골랐는지</summary>
    bool AllPickedUniqueItems(out int notPicked)
    {
        notPicked = 0;
        var used = new HashSet<UniqueItemKind>();
        foreach (var p in _session.Players)
        {
            var kind = PlayerUnique(p);
            if (kind == UniqueItemKind.None || !used.Add(kind)) notPicked++;
        }
        return notPicked == 0;
    }

    // ------------------------------------------------------------------ unique item picker
    /// <summary>
    /// 대기방 오른쪽 아래 카드(준비·게임 시작 버튼이 있는 곳)에 고유 아이템 버튼 4개를 만든다.
    /// 로비 씬을 다시 만들지 않아도 되도록 실행할 때 코드로 만들고, 기존 버튼은 조금 아래로 줄여 자리를 만든다.
    /// </summary>
    void BuildUniquePicker()
    {
        if (readyButton == null || _uniqueButtons != null) return;
        var card = readyButton.transform.parent as RectTransform;
        if (card == null) return;

        if (startHintText != null)
        {
            var sh = startHintText.rectTransform;
            sh.anchoredPosition = new Vector2(sh.anchoredPosition.x, -10);
            sh.sizeDelta = new Vector2(sh.sizeDelta.x, 44);
            startHintText.fontSize = Mathf.Min(startHintText.fontSize, 19);
        }
        ResizeBottom(readyButton, 84, 74);
        ResizeBottom(startButton, 84, 74);
        ResizeBottom(leaveButton, 18, 56);

        var row = new GameObject("UniqueItemPicker", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(card, false);
        row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1);
        row.offsetMin = new Vector2(24, -60 - 66); row.offsetMax = new Vector2(-24, -60);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10; layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;

        var kinds = UniqueItems.Selectable;
        _uniqueButtons = new Button[kinds.Length];
        _uniqueLabels = new TMP_Text[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            var kind = kinds[i];
            var go = new GameObject("Unique_" + UniqueItems.Code(kind), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(row, false);
            var image = go.GetComponent<Image>();
            image.sprite = readyButton.image.sprite; image.type = readyButton.image.type;
            image.color = LobbyStyle.ButtonDark;
            var button = go.GetComponent<Button>();
            button.colors = readyButton.colors;
            button.targetGraphic = image;
            button.onClick.AddListener(() => _ = PickUniqueAsync(kind));

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
            labelGo.transform.SetParent(go.transform, false);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            if (startHintText != null) label.font = startHintText.font;
            label.fontSize = 22; label.alignment = TextAlignmentOptions.Center; label.richText = true;
            label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
            var lr = label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(4, 2); lr.offsetMax = new Vector2(-4, -2);
            _uniqueButtons[i] = button; _uniqueLabels[i] = label;
        }
        UpdateUniquePicker();
    }

    // ------------------------------------------------------------------ back to start
    /// <summary>
    /// 첫 화면(방 목록) 오른쪽 위에 "처음 화면" 버튼을 코드로 만든다 → Start 씬으로 돌아간다.
    /// (그래픽 품질 버튼은 Start 화면·게임 중 ESC의 설정 창으로 옮겼다)
    /// </summary>
    GameObject _backToStart;   // 첫 화면(mainPanel)이 보일 때만 보인다 (Update)

    void BuildBackToStartButton()
    {
        var root = mainPanel != null ? mainPanel.transform.parent as RectTransform : null;
        if (root == null || readyButton == null) return;
        var go = new GameObject("BackToStartButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(root, false);
        _backToStart = go;
        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(1, 1);
        r.anchoredPosition = new Vector2(-80, -44); r.sizeDelta = new Vector2(220, 56);
        var image = go.GetComponent<Image>();
        image.sprite = readyButton.image.sprite; image.type = readyButton.image.type; image.color = LobbyStyle.ButtonDark;
        var button = go.GetComponent<Button>();
        button.colors = readyButton.colors; button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            if (_busy) return;
            SceneManager.LoadScene(StartMenu.SceneName);
        });
        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
        labelGo.transform.SetParent(go.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        if (startHintText != null) label.font = startHintText.font;
        label.text = "← 처음 화면";
        label.fontSize = 22; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        var lr = label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
    }

    static void ResizeBottom(Button button, float y, float height)
    {
        if (button == null) return;
        var r = (RectTransform)button.transform;
        r.offsetMin = new Vector2(r.offsetMin.x, y); r.offsetMax = new Vector2(r.offsetMax.x, y + height);
    }

    void UpdateUniquePicker()
    {
        if (_uniqueButtons == null) return;
        string myId = _session != null && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : null;
        var me = _session != null && myId != null ? _session.GetPlayer(myId) : null;
        bool locked = _session == null || _busy || _savingUnique || (!_session.IsHost && me != null && PlayerReady(me));   // 준비한 대원은 준비를 취소해야 바꿀 수 있다
        var kinds = UniqueItems.Selectable;
        for (int i = 0; i < kinds.Length; i++)
        {
            _uniqueOwners.TryGetValue(kinds[i], out var owner);
            bool mine = owner != null && owner == myId;
            bool taken = owner != null && !mine;
            string sub = mine ? "내 선택" : taken ? (_knownNames.TryGetValue(owner, out var n) ? n : "대원") : "선택 가능";
            _uniqueLabels[i].text = $"<b>{UniqueItems.DisplayName(kinds[i])}</b>\n<size=15><color=#{(mine ? "FFFFFF" : taken ? "8A949E" : "7FD6F0")}>{sub}</color></size>";
            _uniqueButtons[i].image.color = mine ? LobbyStyle.Accent : taken ? new Color(0.11f, 0.12f, 0.14f) : LobbyStyle.ButtonDark;
            _uniqueButtons[i].interactable = !locked && !taken;
        }
    }

    async Task PickUniqueAsync(UniqueItemKind kind)
    {
        if (_session == null || _busy || _savingUnique) return;
        string myId = AuthenticationService.Instance.PlayerId;
        var me = _session.GetPlayer(myId);
        if (!_session.IsHost && me != null && PlayerReady(me))
        {
            SetStatus("준비를 취소해야 고유 아이템을 바꿀 수 있어요.", true);
            return;
        }
        var current = me != null ? PlayerUnique(me) : UniqueItemKind.None;
        var next = current == kind ? UniqueItemKind.None : kind;   // 같은 버튼을 다시 누르면 선택 취소
        if (next != UniqueItemKind.None && _uniqueOwners.TryGetValue(next, out var owner) && owner != myId)
        {
            string other = _knownNames.TryGetValue(owner, out var n) ? n : "다른 대원";
            SetStatus($"{other} 님이 이미 '{UniqueItems.DisplayName(next)}'을(를) 골랐어요.", true);
            return;
        }
        await SaveUniqueAsync(next, false);
        if (_session == null) return;
        if (next != UniqueItemKind.None) SetStatus($"고유 아이템 '{UniqueItems.DisplayName(next)}' · {UniqueItems.Description(next)} (게임에서 3번 키)");
        else SetStatus("고유 아이템 선택을 취소했어요.");
    }

    /// <summary>내 고유 아이템을 방 정보에 저장한다. clearReady면 준비도 함께 푼다 (선택이 지워졌을 때)</summary>
    async Task SaveUniqueAsync(UniqueItemKind kind, bool clearReady)
    {
        var session = _session;
        if (session == null) return;
        _savingUnique = true;
        UpdateRoomButtons();
        try
        {
            session.CurrentPlayer.SetProperty(KeyUnique, new PlayerProperty(UniqueItems.Code(kind), VisibilityPropertyOptions.Member));
            if (clearReady && !session.IsHost) session.CurrentPlayer.SetProperty(KeyReady, new PlayerProperty("0", VisibilityPropertyOptions.Member));
            await session.SaveCurrentPlayerDataAsync();
            UniqueItems.LocalChoice = kind;
        }
        catch (Exception e)
        {
            SetStatus(FriendlyError(e), true);
        }
        finally
        {
            _savingUnique = false;
            if (_session == session) RefreshRoom();
        }
    }

    async Task ToggleReadyAsync()
    {
        if (_session == null || _session.IsHost || _busy) return;
        var me = _session.GetPlayer(AuthenticationService.Instance.PlayerId);
        bool next = !(me != null && PlayerReady(me));
        if (next && (me == null || PlayerUnique(me) == UniqueItemKind.None))
        {
            SetStatus("먼저 고유 아이템을 하나 골라 주세요.", true);
            return;
        }
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
        if (_savingUnique || !AllPickedUniqueItems(out _))
        {
            SetStatus("모든 대원이 서로 다른 고유 아이템을 골라야 시작할 수 있어요.", true);
            return;
        }
        var hostPlayer = _session.GetPlayer(AuthenticationService.Instance.PlayerId);
        if (hostPlayer != null) UniqueItems.LocalChoice = PlayerUnique(hostPlayer);

        await RunBusy("게임을 시작하는 중...", async () =>
        {
            _gameStarting = true;
            LoadingScreen.Show();   // 방장은 누르자마자 로딩 화면
            if (chat != null) chat.AnnounceGameStart(true);   // 참가자도 바로 로딩 화면 (씬 불러오기 신호는 방장 로딩이 끝나야 옴)
            try
            {
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
            }
            catch
            {
                _gameStarting = false;
                LoadingScreen.Cancel();
                if (chat != null) chat.AnnounceGameStart(false);
                throw;
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
        if (VoiceChatService.Exists) VoiceChatService.Instance.LeaveLobby();
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

    // ------------------------------------------------------------------ voice
    void ToggleMic()
    {
        var voice = VoiceChatService.Instance;
        voice.SetMicMuted(!voice.MicMuted);
        RefreshVoice();
    }

    /// <summary>대기방 카드마다 말하는 중 표시 + 마이크 버튼/연결 상태 갱신</summary>
    void RefreshVoice()
    {
        if (!VoiceChatService.Exists) return;
        var voice = VoiceChatService.Instance;
        string channel = voice.LobbyChannel;
        bool inChannel = voice.IsInChannel(channel);
        string myId = voice.MyPlayerId;
        for (int i = 0; i < slots.Length && i < _slotPlayerIds.Length; i++)
        {
            string id = _slotPlayerIds[i];
            if (string.IsNullOrEmpty(id)) { slots[i].SetVoice(false, false, false); continue; }
            bool me = id == myId;
            bool connected = inChannel && voice.IsConnected(channel, id);
            bool speaking = connected && voice.IsSpeaking(channel, id) && !(me && voice.MicMuted);
            slots[i].SetVoice(connected, speaking, me && voice.MicMuted);
        }
        if (micButtonText != null) micButtonText.text = voice.MicMuted ? "마이크 켜기" : "마이크 끄기";
        if (micButton != null) micButton.image.color = voice.MicMuted ? LobbyStyle.Accent : LobbyStyle.ButtonDark;
        if (voiceStatusText != null)
        {
            if (inChannel) { voiceStatusText.text = voice.MicMuted ? "음성 연결됨 · 마이크 꺼짐" : "음성 연결됨 · 말하면 대원에게 들려요"; voiceStatusText.color = voice.MicMuted ? LobbyStyle.TextDim : LobbyStyle.Ready; }
            else if (voice.State == VoiceChatService.Status.Error) { voiceStatusText.text = "음성 연결 실패"; voiceStatusText.color = LobbyStyle.Error; }
            else { voiceStatusText.text = "음성 연결 중..."; voiceStatusText.color = LobbyStyle.TextDim; }
        }
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

    /// <summary>참가자: 방장이 게임 시작을 누르면 바로 로딩 화면을 띄운다 (실패하면 닫는다)</summary>
    void OnGameStartChanged(bool starting)
    {
        if (_session == null || _session.IsHost) return;   // 방장은 StartGameAsync에서 직접 처리
        if (starting)
        {
            _gameStarting = true;
            LoadingScreen.Show();
        }
        else
        {
            _gameStarting = false;
            LoadingScreen.Cancel();
            SetStatus("방장이 게임을 시작하지 못했어요. 잠시 후 다시 시도해요.", true);
        }
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
