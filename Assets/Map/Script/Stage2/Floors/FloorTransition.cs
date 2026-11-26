using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoidLast.Player;

/// <summary>
/// 층(씬) 사이 이동. 계단실 안에서 F로 위층·아래층으로 옮긴다.
///  - 계단 문은 대원이 가까이 가면 저절로 열린다 (StairDoorController).
///  - 계단실 안: 위로 가는 계단(z −쪽)과 아래로 가는 계단(z +쪽)의 반쯤에 투명 벽이 있고, 그 앞(벽 앞 약 2.6m)에 서면 안내가 뜬다.
///    살아 있는 대원이 모두 같은 벽 앞에서 각자 F를 누르면(준비) 모두 함께 이동한다. 혼자면 바로 이동.
///  - 올라가기: 임무와 상관없이 가능 (5층 제외). 내려가기: 이 층 임무를 깼거나 예전에 한 번 깬 층이어야 하고, descentSides의 계단만 (1층 제외).
///  - 떠날 때: 화면이 짧게 어두워지고 방장이 다음 층 씬을 불러온다 (참가자는 로딩 화면).
///  - 도착: 같은 이름의 계단(A로 떠나면 A) 안, 문 바로 뒤 계단참에서 문을 보고 선 채로 화면이 밝아지고 문이 열리면 조종 가능.
///  - HP·핫바·탄약·손전등은 FloorCarryOver로 이어지고, 깬 층과 층마다 남은 파밍 아이템도 기억한다.
///  - 투명 벽·위로 가는 계단 바닥(충돌)·도착 지점 위치는 실행할 때 코드로 만든다 (층 씬을 다시 만들 필요 없음).
/// 계단 좌표 (u = |x|): 계단참 u 42~46, 계단 u 46~52(높이 ±4m), 중간참 u 52~58. 계단 A는 x −쪽, B는 x +쪽. 층 높이 = (층-1) × 8m.
/// </summary>
[DisallowMultipleComponent]
public sealed class FloorTransition : NetworkBehaviour
{
    [Tooltip("이 씬의 층 (1~5)")] [Range(1, 5)] public int floor = 2;
    [Tooltip("아래층 씬 이름 (1층이면 비움)")] public string nextSceneName;
    [Tooltip("내려갈 수 있는 계단 (예: A, AB)")] public string descentSides = "AB";
    public TMP_FontAsset font;
    [Tooltip("떠날 때 화면이 검게 되는 시간")] [Min(0.1f)] public float leaveFadeSeconds = 0.5f;
    [Tooltip("도착할 때 화면이 밝아지는 시간")] [Min(0.1f)] public float arriveFadeSeconds = 0.8f;

    public static FloorTransition Instance { get; private set; }
    /// <summary>층 이동 중 (입력·피해 금지)</summary>
    public static bool InCutscene { get; private set; }

    bool _moving, _arrivalPending, _arrivalRunning, _applied;
    float _arrivalWait;

    public float FloorY => (floor - 1) * 8f;
    public bool HasNextFloor => floor > 1;
    public bool HasUpperFloor => floor < 5;
    /// <summary>아래층으로 내려갈 수 있는지 (임무를 깼거나, 예전에 이 층을 깬 적이 있음. 임무가 없는 층은 바로 가능)</summary>
    public bool CanDescend
    {
        get
        {
            if (!HasNextFloor || _moving) return false;
            if (FloorCarryOver.IsCleared(floor)) return true;
            var mission = FloorMission.Current;
            return mission == null || mission.ExitUnlocked;
        }
    }
    public bool IsMoving => _moving;
    /// <summary>이 계단(A/B)으로 내려갈 수 있는지</summary>
    public bool AllowsSide(char side) => string.IsNullOrEmpty(descentSides) || descentSides.IndexOf(side) >= 0;

    public static string SceneFor(int f) => "Stage2_Ship_" + f + "f";
    string TargetScene(bool up) => up ? SceneFor(floor + 1) : (!string.IsNullOrEmpty(nextSceneName) ? nextSceneName : SceneFor(floor - 1));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; InCutscene = false; }

    /// <summary>
    /// 로비로 돌아왔을 때: 층 이동 연출 도중 연결이 끊기거나 나간 경우 '연출 중' 상태가 남아 있지 않게 끈다.
    /// (남아 있으면 다음 게임·다시 참가에서 임무 장치·계단 F가 안 눌리고, 방장이면 고치가 안 생기고 계단 문이 안 열린다)
    /// </summary>
    public static void ResetForLobby()
    {
        InCutscene = false;
        StairDoorController.SuppressAuto = false;
    }

    public static string FloorName(int f)
    {
        switch (f)
        {
            case 1: return "1층 · 격납고";
            case 2: return "2층 · 화물 및 검역 구역";
            case 3: return "3층 · 관제 구역";
            case 4: return "4층 · 동력 구역";
            case 5: return "5층 · 승무원 구역";
            default: return f + "층";
        }
    }

    void Awake()
    {
        Instance = this;
        // 이웃 층에서 잘라 온 계단실 조각: 위로 가는 계단이 보이도록 항상 켠다
        var ship = GameObject.Find("VOID_LAST_Ship");
        if (ship != null)
            foreach (Transform c in ship.transform)
                if (c.name.Contains("_Stairwell_") && !c.gameObject.activeSelf) c.gameObject.SetActive(true);
        BuildStairs();
        PlaceArrivalPoints();
        PlayerCountGate.ApplyAll();   // 인원별 보정: 시작 인원이 모자라면 추가 아이템·몬스터를 뺀다 (아이템 번호 매기기 전)
        RestoreFarmingItems();
        BuildPrompt();
        PlayLog.BeginFloor(floor);
        // 5층 = 시작 층: 목표 안내, 게임을 막 시작했으면 경보·함내 방송(글자)·도움말
        if (floor == 5)
        {
            var intro = new GameObject("Floor5 Intro").AddComponent<Floor5Intro>();
            intro.Init(font, !FloorCarryOver.Pending && !Rejoin.IsRejoining);   // 다시 참가한 대원에게는 시작 경보·방송을 다시 틀지 않는다
        }
        if (FloorCarryOver.Pending)
        {
            // 다른 층에서 온 경우: 도착 연출이 끝날 때까지 조작 금지, 화면은 검은 상태로 시작. 문은 연출에서 연다
            _arrivalPending = true;
            StairDoorController.SuppressAuto = true;
            SetCutscene(true);
            ScreenFader.Set(1f);
        }
        else ResetForLobby();   // 다른 층에서 온 게 아니면(게임 시작·다시 참가) 예전 연출 상태를 지운다
    }

    // ------------------------------------------------------------------ 깬 층 (게임 도중 들어온 대원도 같은 기준으로 내려갈 수 있게 방장 값을 받는다)
    readonly NetworkVariable<byte> _clearedMask = new NetworkVariable<byte>();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            byte mask = 0;
            foreach (int f in FloorCarryOver.ClearedFloors) if (f >= 1 && f <= 7) mask |= (byte)(1 << f);
            _clearedMask.Value = mask;
        }
        else
        {
            ApplyClearedMask(0, _clearedMask.Value);
            _clearedMask.OnValueChanged += ApplyClearedMask;
        }
    }

    public override void OnNetworkDespawn() { _clearedMask.OnValueChanged -= ApplyClearedMask; }

    static void ApplyClearedMask(byte previous, byte mask)
    {
        for (int f = 1; f <= 7; f++) if ((mask & (1 << f)) != 0) FloorCarryOver.ClearedFloors.Add(f);
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    // ------------------------------------------------------------------ 계단실: 투명 벽 · 위로 가는 계단 바닥 · F 구역
    struct Exit { public bool valid; public char side; public bool up; public Bounds zone; }
    readonly Exit[] _exits = new Exit[4];
    static int ZoneIndex(char side, bool up) => (side == 'B' ? 2 : 0) + (up ? 0 : 1);

    void BuildStairs()
    {
        var root = new GameObject("Stair Exits (자동 생성)").transform;
        root.SetParent(transform, false);
        float Y = FloorY;
        int wallLayer = 2;   // Ignore Raycast: 대원은 막고 총알·조준 광선은 통과
        foreach (char side in new[] { 'A', 'B' })
        {
            float s = side == 'A' ? -1f : 1f;
            if (HasUpperFloor)
            {
                // 위로 가는 계단은 위층 씬의 것이라 이 층에는 모양(계단실 조각)만 있다 → 같은 자리에 바닥(충돌)을 만든다
                var ramp = Box(root, "Up Ramp " + side, new Vector3(s * 49.05f, Y + 1.9f, -4.15f), new Vector3(7.21f, 0.2f, 7.75f), 0);
                ramp.transform.rotation = Quaternion.Euler(0, 0, side == 'A' ? 326.3f : 33.7f);
                Box(root, "Up Divider " + side, new Vector3(s * 49f, Y + 4f, 0f), new Vector3(6f, 8f, 0.3f), 0);
                Box(root, "Up Wall " + side, new Vector3(s * 49.2f, Y + 4.5f, -4.15f), new Vector3(0.3f, 5f, 7.8f), wallLayer);
                _exits[ZoneIndex(side, true)] = new Exit { valid = true, side = side, up = true, zone = new Bounds(new Vector3(s * 47.9f, Y + 1.1f, -4.15f), new Vector3(2.6f, 3.4f, 7.7f)) };
            }
            else Box(root, "Top Block " + side, new Vector3(s * 46.15f, Y, -4.15f), new Vector3(0.3f, 8f, 7.8f), wallLayer);   // 5층: 위층이 없음
            if (HasNextFloor)
            {
                Box(root, "Down Wall " + side, new Vector3(s * 49.2f, Y + 0.2f, 4.15f), new Vector3(0.3f, 5f, 7.8f), wallLayer);
                _exits[ZoneIndex(side, false)] = new Exit { valid = true, side = side, up = false, zone = new Bounds(new Vector3(s * 47.9f, Y - 1.1f, 4.15f), new Vector3(2.6f, 3.4f, 7.7f)) };
            }
        }
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    /// <summary>발 위치가 들어 있는 F 구역 번호 (없으면 -1)</summary>
    int ZoneAt(Vector3 feet)
    {
        for (int i = 0; i < _exits.Length; i++) if (_exits[i].valid && _exits[i].zone.Contains(feet)) return i;
        return -1;
    }

    bool ZoneAllowed(int zone)
    {
        if (zone < 0 || !_exits[zone].valid || _moving) return false;
        return _exits[zone].up ? HasUpperFloor : CanDescend && AllowsSide(_exits[zone].side);
    }

    /// <summary>도착 지점(Arrival_A_1~4, Arrival_B_1~4)을 계단 안, 문 바로 뒤 계단참으로 옮긴다 (문을 바라봄). 스폰보다 먼저 실행됨</summary>
    void PlaceArrivalPoints()
    {
        float Y = FloorY;
        float[] u = { 43.4f, 43.4f, 44.4f, 44.4f }, z = { -1f, 1f, -1f, 1f };
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Arrival_") || t.name.Length != 11) continue;   // Arrival_A_1
                char side = t.name[8];
                int i = t.name[10] - '1';
                if ((side != 'A' && side != 'B') || i < 0 || i > 3) continue;
                float s = side == 'A' ? -1f : 1f;
                t.SetPositionAndRotation(new Vector3(s * u[i], Y + 0.03f, z[i]), Quaternion.LookRotation(new Vector3(-s, 0, 0)));
            }
    }

    // ------------------------------------------------------------------ 파밍 아이템 기억 (층을 떠날 때 남아 있던 것만 다시 놓임)
    readonly List<FpsWorldPickup> _farming = new List<FpsWorldPickup>();

    void RestoreFarmingItems()
    {
        foreach (var root in gameObject.scene.GetRootGameObjects())
            if (root.name == "Farming Items") _farming.AddRange(root.GetComponentsInChildren<FpsWorldPickup>(true));
        if (!FloorCarryOver.FarmingLeft.TryGetValue(floor, out var left)) return;
        // 네트워크 아이템 번호(NetworkPickupRegistry)를 매기기 전에 모든 대원 화면에서 똑같이 지운다
        for (int i = _farming.Count - 1; i >= 0; i--)
        {
            var p = _farming[i];
            if (p != null && left.Contains(p.name)) continue;
            if (p != null) DestroyImmediate(p.gameObject);
            _farming.RemoveAt(i);
        }
    }

    void RememberFloor(bool cleared)
    {
        if (cleared) FloorCarryOver.ClearedFloors.Add(floor);
        var left = new HashSet<string>();
        foreach (var p in _farming)
            if (p != null && p.gameObject.activeInHierarchy && !p.IsClaimed) left.Add(p.name);
        FloorCarryOver.FarmingLeft[floor] = left;
    }

    // ------------------------------------------------------------------ F (준비) — 내 화면
    TMP_Text _prompt;
    int _myZone = -1;
    bool _myReady;
    readonly NetworkVariable<int> _readyInfo = new NetworkVariable<int>();   // 구역마다 준비 인원(4비트씩) + 살아 있는 인원(16번째 비트부터)

    int ReadyCount(int zone) => IsSpawned ? (_readyInfo.Value >> (zone * 4)) & 15 : (_myReady && _myZone == zone ? 1 : 0);
    int LivingCount => IsSpawned ? (_readyInfo.Value >> 16) & 15 : 1;

    FpsHealth _me;
    float _meScan;

    FpsHealth LocalHealth()
    {
        if (Stage2NetworkBootstrap.IsOnline) return FpsNetworkPlayer.Local != null ? FpsNetworkPlayer.Local.Health : null;
        if (_me == null && Time.unscaledTime >= _meScan)   // 오프라인: 씬의 대원 (가끔만 찾음)
        {
            _meScan = Time.unscaledTime + 1f;
            var p = LocalPlayer(out _);
            _me = p != null ? p.GetComponent<FpsHealth>() : null;
        }
        return _me;
    }

    UnityEngine.UI.Image _promptBack;

    void SetPrompt(string text)
    {
        if (_prompt == null || _prompt.text == text) return;
        _prompt.text = text;
        if (_promptBack == null) return;
        _promptBack.enabled = text.Length > 0;
        if (text.Length > 0) _promptBack.rectTransform.sizeDelta = new Vector2(_prompt.GetPreferredValues(text).x + 40f, 42f);
    }

    void LocalStairInput()
    {
        int zone = -1;
        var me = LocalHealth();
        if (me != null && !me.IsDead && !InCutscene && !_moving) zone = ZoneAt(me.transform.position);
        if (zone != _myZone)
        {
            if (_myReady && IsSpawned) ReadyRpc(-1);
            _myReady = false;
            _myZone = zone;
        }
        if (_prompt == null) return;
        if (zone < 0) { SetPrompt(""); return; }

        var exit = _exits[zone];
        string key = "[" + GameKeys.Label(GameAction.Interact) + "] ";
        string target = FloorName(exit.up ? floor + 1 : floor - 1).Split(' ')[0];
        bool allowed = ZoneAllowed(zone);
        int ready = ReadyCount(zone), living = Mathf.Max(1, LivingCount);
        if (!exit.up && !AllowsSide(exit.side))
            SetPrompt("<color=#FCA5A5>막힘</color> · 이 계단으로는 내려갈 수 없어요. " + (exit.side == 'A' ? "B" : "A") + " 계단으로 가세요");
        else if (!allowed)
            SetPrompt("<color=#FCA5A5>아직 내려갈 수 없어요</color> · 이 층의 임무를 먼저 끝내세요");
        else if (_myReady)
            SetPrompt("<color=#86EFAC>준비 완료</color> · 살아 있는 동료가 모두 " + exit.side + " 계단 이곳에 모이길 기다리는 중 " + ready + "/" + living + "   <color=#94A3B8>" + key + "취소</color>");
        else
            SetPrompt(key + target + (exit.up ? "으로 올라가기" : "으로 내려가기") + (living > 1 ? "   <color=#94A3B8>준비 " + ready + "/" + living + " · 모두 " + exit.side + " 계단 이곳에 모여 F를 눌러야 이동</color>" : ""));

        if (!allowed || Cursor.lockState != CursorLockMode.Locked || !GameKeys.Down(GameAction.Interact)) return;
        if (!IsSpawned) { BeginMove(exit.side, exit.up); return; }   // 오프라인: 혼자이므로 바로 이동
        _myReady = !_myReady;
        ReadyRpc((sbyte)(_myReady ? zone : -1));
    }

    // ------------------------------------------------------------------ F (준비) — 방장 판정
    readonly Dictionary<ulong, int> _ready = new Dictionary<ulong, int>();
    float _nextReadyCheck;

    [Rpc(SendTo.Server)]
    void ReadyRpc(sbyte zone, RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        if (zone < 0) _ready.Remove(id); else _ready[id] = zone;
        _nextReadyCheck = 0f;
    }

    void ServerReadyTick()
    {
        if (!IsSpawned || !IsServer || _moving || Time.unscaledTime < _nextReadyCheck) return;
        _nextReadyCheck = Time.unscaledTime + 0.15f;
        int living = 0;
        var counts = new int[4];
        foreach (var p in FpsNetworkPlayer.All)
        {
            if (p == null || p.Health == null || p.Health.IsDead) continue;
            living++;
            if (!_ready.TryGetValue(p.OwnerClientId, out int zone)) continue;
            if (ZoneAt(p.transform.position) != zone || !ZoneAllowed(zone)) { _ready.Remove(p.OwnerClientId); continue; }   // 구역을 벗어나면 준비 취소
            counts[zone]++;
        }
        // 나갔거나 죽은 대원의 준비는 지운다
        if (_ready.Count > 0)
        {
            var stale = new List<ulong>();
            foreach (var kv in _ready)
            {
                var p = FpsNetworkPlayer.FindByClient(kv.Key);
                if (p == null || p.Health == null || p.Health.IsDead) stale.Add(kv.Key);
            }
            foreach (var id in stale) _ready.Remove(id);
        }
        int info = Mathf.Min(living, 15) << 16;
        for (int z = 0; z < 4; z++) info |= Mathf.Min(counts[z], 15) << (z * 4);
        if (_readyInfo.Value != info) _readyInfo.Value = info;

        for (int z = 0; z < 4; z++)
            if (living > 0 && counts[z] >= living && ZoneAllowed(z))
            {
                _ready.Clear();
                BeginMove(_exits[z].side, _exits[z].up);
                return;
            }
    }

    // ------------------------------------------------------------------ 이동 시작
    /// <summary>방장(또는 오프라인): 모든 대원이 준비되면 모두 같은 층으로 이동 시작</summary>
    public bool BeginMove(char side, bool up)
    {
        bool authority = !IsSpawned || IsServer;
        if (!authority || _moving) return false;
        if (up ? !HasUpperFloor : !(CanDescend && AllowsSide(side))) return false;
        _moving = true;
        if (IsSpawned) MoveRpc((byte)side, up);
        else StartCoroutine(Leave(side, up));
        return true;
    }

    [Rpc(SendTo.ClientsAndHost)]
    void MoveRpc(byte side, bool up)
    {
        _moving = true;
        StartCoroutine(Leave((char)side, up));
    }

    IEnumerator Leave(char side, bool up)
    {
        // 이 층을 기억: 내려가면 깬 층. 올라갈 때도 임무를 이미 깼으면 깬 층으로 기억
        var mission = FloorMission.Current;
        RememberFloor(!up || mission == null || mission.ExitUnlocked || FloorCarryOver.IsCleared(floor));
        PlayLog.EndFloor(up ? "올라감" : "내려감");
        SetCutscene(true);
        SetPrompt("");
        int target = up ? floor + 1 : floor - 1;
        var player = LocalPlayer(out _);
        FloorCarryOver.SavePlayer(player);
        FloorCarryOver.Pending = true;
        FloorCarryOver.Transitioning = true;
        FloorCarryOver.ArrivalSide = side;
        FloorCarryOver.FromFloor = floor;
        LoadingScreen.Subtitle = "STAGE 2 · " + FloorName(target);
        LoadingScreen.Ensure(font);
        ScreenFader.FadeTo(1f, leaveFadeSeconds);
        while (!ScreenFader.IsBlack) yield return null;

        // 다음 층 불러오기: 방장(또는 오프라인)만. 참가자는 방장이 불러올 때까지 로딩 화면으로 기다린다
        string scene = TargetScene(up);
        if (!IsSpawned)
        {
            var op = SceneManager.LoadSceneAsync(scene);
            LoadingScreen.BeginOffline(op, scene);
        }
        else if (IsServer)
        {
            yield return new WaitForSecondsRealtime(0.3f);   // 다른 대원 화면도 다 어두워지도록
            LobbyPlayerSpawner.KeywordOverride = new[] { "Arrival_" + side };
            // 다른 씬 불러오기가 진행 중이면(예: 다시 참가한 대원이 층을 받는 중) 잠깐씩 기다렸다 다시 시도한다
            var status = SceneEventProgressStatus.None;
            float giveUp = Time.unscaledTime + 20f;
            while (IsSpawned && NetworkManager != null && NetworkManager.SceneManager != null)
            {
                status = NetworkManager.SceneManager.LoadScene(scene, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.SceneEventInProgress || Time.unscaledTime > giveUp) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Debug.Log($"[Floor] {floor}층 → {scene} (계단 {side}, {(up ? "올라감" : "내려감")}) : {status}");
            // 끝내 불러오지 못하면 모두 이 층에 남는다 (예전엔 모든 대원의 화면이 검은 채로 멈춰 있었음)
            if (status != SceneEventProgressStatus.Started && IsSpawned) CancelMoveRpc();
        }
        else
        {
            // 참가자: 네트워크 씬 불러오기 신호는 방장이 다음 층을 다 불러온 뒤에 오므로, 그동안 검은 화면 대신 로딩 화면을 보여 준다
            LoadingScreen.Show();
        }
    }

    /// <summary>방장이 다음 층을 불러오지 못함: 이동을 취소하고 이 층에서 계속한다</summary>
    [Rpc(SendTo.ClientsAndHost)]
    void CancelMoveRpc()
    {
        Debug.LogWarning("[Floor] 층 이동 취소 (다음 층을 불러오지 못함)");
        _moving = false;
        _myReady = false; _myZone = -1;
        FloorCarryOver.Pending = false;
        FloorCarryOver.Transitioning = false;
        LobbyPlayerSpawner.KeywordOverride = null;
        LoadingScreen.Cancel();
        ScreenFader.FadeTo(0f, 0.3f);
        SetCutscene(false);
    }

    // ------------------------------------------------------------------ 매 프레임
    float _nextTrack;

    void Update()
    {
        if (Time.unscaledTime >= _nextTrack)
        {
            _nextTrack = Time.unscaledTime + 1f;
            PlayLog.Track(LocalPlayer(out _));   // 판 기록: 내 대원의 피해·사격·아이템 (같은 대원이면 아무것도 안 함)
        }
        if (_arrivalPending) { ArrivalUpdate(); return; }
        ServerReadyTick();
        LocalStairInput();
    }

    // ------------------------------------------------------------------ 도착
    void ArrivalUpdate()
    {
        if (_arrivalRunning) return;
        _arrivalWait += Time.unscaledDeltaTime;
        var player = LocalPlayer(out _);
        if (player != null && !_applied)
        {
            _applied = true;
            if (!Stage2NetworkBootstrap.IsOnline) PlaceOfflinePlayer(player);
            FloorCarryOver.ApplyPlayer(player);
        }
        if (_arrivalWait > 40f && player == null) { FinishArrival(); return; }   // 대원을 끝내 못 찾음
        if (player == null || LoadingScreen.IsShowing) return;
        _arrivalRunning = true;
        StartCoroutine(Arrival());
    }

    void PlaceOfflinePlayer(GameObject player)
    {
        var point = GameObject.Find("Arrival_" + FloorCarryOver.ArrivalSide + "_1");
        if (point == null) return;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.SetPositionAndRotation(point.transform.position, point.transform.rotation);
        if (cc != null) cc.enabled = true;
    }

    /// <summary>계단참에서 문을 보고 선 채로 화면이 밝아지고 → 문이 열리면 끝</summary>
    IEnumerator Arrival()
    {
        char side = FloorCarryOver.ArrivalSide == 'B' ? 'B' : 'A';
        var doors = StairDoorController.Instance;
        int door = doors != null ? doors.FindDoor(side) : -1;
        ScreenFader.FadeTo(0f, arriveFadeSeconds);
        yield return new WaitForSeconds(arriveFadeSeconds * 0.5f);
        if (door >= 0) doors.SetLocalOpen(door, true);
        yield return new WaitForSeconds(door >= 0 ? doors.openSeconds : 0.2f);
        FinishArrival();
        // 방장이 문을 자동으로 열 때까지(대원이 가까이 있으면) 내 화면에서 열어 둔다
        float until = Time.time + 6f;
        while (door >= 0 && doors != null && Time.time < until && !doors.IsAutoOpen(door)) yield return null;
        if (door >= 0 && doors != null) doors.SetLocalOpen(door, false);
    }

    void FinishArrival()
    {
        _arrivalPending = false;
        FloorCarryOver.Pending = false;
        FloorCarryOver.Transitioning = false;
        StairDoorController.SuppressAuto = false;
        ScreenFader.FadeTo(0f, 0.3f);
        SetCutscene(false);
        StartCoroutine(DamageGrace());
    }

    IEnumerator DamageGrace()
    {
        FpsHealth.DamageBlocked = true;
        yield return new WaitForSeconds(1.5f);   // 도착 직후 잠깐은 피해 없음
        if (!InCutscene) FpsHealth.DamageBlocked = false;
    }

    // ------------------------------------------------------------------ 공용
    static void SetCutscene(bool on)
    {
        InCutscene = on;
        FpsInput.Blocked = on;
        FpsHealth.DamageBlocked = on;
    }

    void BuildPrompt()
    {
        var canvasGo = new GameObject("Stair Exit Prompt", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 36;
        var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        // 손전등 불빛 위에서도 읽히도록 글자 뒤에 어두운 판
        var bg = new GameObject("Back", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        bg.transform.SetParent(canvasGo.transform, false);
        _promptBack = bg.GetComponent<UnityEngine.UI.Image>();
        _promptBack.color = new Color(0f, 0f, 0f, 0.6f); _promptBack.raycastTarget = false; _promptBack.enabled = false;
        var br = _promptBack.rectTransform;
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f); br.pivot = new Vector2(0.5f, 0.5f); br.anchoredPosition = new Vector2(0, -90);
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(canvasGo.transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = 22; t.color = Color.white; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.richText = true;
        var r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(0, -90); r.sizeDelta = new Vector2(1100, 34);
        _prompt = t;
    }

    /// <summary>내 대원 (온라인: 내 네트워크 대원 / 오프라인: 씬에 놓인 대원)</summary>
    static GameObject LocalPlayer(out Camera cam)
    {
        cam = null;
        if (Stage2NetworkBootstrap.IsOnline)
        {
            var p = FpsNetworkPlayer.Local;
            if (p == null) return null;
            cam = p.ViewCamera;
            return p.gameObject;
        }
        foreach (var h in FindObjectsByType<FpsHealth>(FindObjectsSortMode.None))
        {
            if (h.GetComponent<FpsNetworkPlayer>() != null || !h.isActiveAndEnabled) continue;
            cam = h.GetComponentInChildren<Camera>(true);
            return h.gameObject;
        }
        return null;
    }

}
