using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using VoidLast.Player;

/// <summary>
/// 진행 중인 게임에 다시 참가 (재접속).
///  - 게임이 시작되면 방은 잠그지 않고 "비공개"로 바뀐다 → 방 목록에는 안 보이고, 참가 코드로만 들어올 수 있다.
///  - 참가자 PC: 게임을 시작할 때 "다시 참가 표"(방 코드, 이번 게임 표식, 고른 고유 아이템, 시작 인원, 우주복 색)를 PlayerPrefs에 저장한다.
///    게임 중에는 탄약·손전등도 몇 초마다 표에 적어 둔다 (강제 종료돼도 다시 들어오면 이어받음).
///    게임이 꺼지거나 연결이 끊겨 로비로 오면 "진행 중인 게임" 대기실 창이 뜬다 (나온 뒤 3시간 지나면 버림).
///  - 이 PC의 플레이어 ID는 게임을 껐다 켜도 같다 (LocalProfile). 강제 종료로 방에 예전 자리가 남아 있으면 다시 참가할 때 먼저 그 자리를 비운다.
///  - 방장: 이번 게임의 대원 명단(표식)을 기억한다. 명단에 없는 사람이 게임 중에 들어오면 내보낸다.
///    대원이 나가면 그 대원의 HP·사망 여부를 기억하고, 잠시 뒤 세션에서도 빼서 자리를 비워 둔다.
///    다시 들어오면 지금 층의 A 계단 도착 지점에 대원을 만들고, 기억해 둔 HP·사망 여부를 되돌린다.
///    예전 연결이 아직 남아 있으면(강제 종료 직후) 그 연결은 바로 끊는다 → 유령 대원이 남지 않는다.
///    가지고 있던 물건은 나갈 때 그 자리에 떨어졌으므로(NetworkPickupRegistry) 다시 주우면 된다.
///  - 인원별 밸런스는 시작 인원 그대로 (나가도·다시 들어와도 바꾸지 않음).
/// </summary>
public static class Rejoin
{
    const string PrefKeyBase = "VL_Rejoin";
    const double ExpireHours = 3;
    public const string SessionKeyPlayers = "players";
    /// <summary>방장이 방을 닫을 때 참가자에게 보내는 끊김 이유. 이걸 받으면 다시 참가 표를 지운다</summary>
    public const string RoomClosedReason = "방장이 게임을 끝내고 방을 닫았어요.";
    /// <summary>방장이 명단에 없는 사람을 내보낼 때의 이유 (예: 나가 있는 동안 게임이 끝나고 새 게임이 시작됨). 이걸 받으면 표를 지운다</summary>
    public const string GameInProgressReason = "이미 진행 중인 게임이에요. 게임이 끝나면 들어올 수 있어요.";

    /// <summary>이번 게임에서 이 PC의 대원 표식 (게임 시작·다시 참가 때 정해짐)</summary>
    public static string Token = "";
    /// <summary>다시 참가하는 중 (표를 적용한 뒤 내 대원이 나타날 때까지). 무전기 추가 지급·5층 시작 방송을 건너뛴다</summary>
    public static bool IsRejoining;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Token = ""; IsRejoining = false; _pendingCarry = null; _roster.Clear(); }

    // ------------------------------------------------------------------ 이 PC의 로그인 프로필
    static Mutex _profileMutex;
    static int _profileSlot = -1;

    /// <summary>
    /// Unity 로그인 프로필 이름. 같은 이름이면 게임을 껐다 켜도 같은 플레이어 ID로 로그인된다.
    ///  - 에디터: 프로젝트 폴더마다 다름 (Multiplayer Play Mode 가상 접속자도 서로 다름)
    ///  - 빌드: 설치마다 한 번 만든 값 + 실행 순번(같은 PC에서 여러 개 켜면 0, 1, 2…). 순번은 시스템 잠금(Mutex)으로 정하므로
    ///    게임이 강제 종료되면 잠금이 풀려 다음 실행이 같은 순번 = 같은 플레이어 ID를 받는다.
    /// </summary>
    public static string LocalProfile()
    {
        string seed;
        if (Application.isEditor) { seed = Application.dataPath; _profileSlot = 0; }
        else
        {
            string baseId = PlayerPrefs.GetString("VL_ProfileBase", "");
            if (string.IsNullOrEmpty(baseId))
            {
                baseId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString("VL_ProfileBase", baseId);
                PlayerPrefs.Save();
            }
            if (_profileSlot < 0) _profileSlot = AcquireSlot();
            seed = baseId + "/" + _profileSlot;
        }
        unchecked
        {
            int h = 23;
            foreach (char ch in seed) h = h * 31 + ch;
            return "vl" + (h & 0x7fffffff).ToString("x");
        }
    }

    static int AcquireSlot()
    {
        for (int i = 0; i < 8; i++)
        {
            Mutex m = null;
            try
            {
                m = new Mutex(false, "VoidLast_Profile_" + i);
                bool got;
                try { got = m.WaitOne(0); }
                catch (AbandonedMutexException) { got = true; }   // 예전 실행이 강제 종료됨 → 이 순번을 이어받음
                if (got) { _profileMutex = m; return i; }
                m.Dispose();
            }
            catch (Exception) { m?.Dispose(); }
        }
        return UnityEngine.Random.Range(100, 100000);   // 잠금을 못 쓰는 환경: 예전처럼 실행마다 다른 ID
    }

    /// <summary>표를 저장하는 키. 같은 PC에서 여러 개 켠 경우 실행 순번마다 따로</summary>
    static string PrefKey => _profileSlot > 0 ? PrefKeyBase + "_" + _profileSlot : PrefKeyBase;

    // ------------------------------------------------------------------ 참가자 PC: 다시 참가 표
    [Serializable]
    public class Ticket
    {
        public string sessionId, code, token;
        public int unique, players;
        public int suit = -1;
        public long savedUtcTicks;
        // 마지막으로 기억한 내 상태 (물건은 나간 자리에 떨어지므로 저장하지 않음)
        public bool hasCarry;
        public int magazine, reserve;
        public bool flashlight;
    }

    static Ticket _pendingCarry;

    public static string NewToken() => Token = Guid.NewGuid().ToString("N");

    /// <summary>참가자: 게임이 시작될 때 표를 저장한다 (방장은 방장이 나가면 게임이 끝나므로 저장하지 않음)</summary>
    public static void SaveTicket(ISession session, int players)
    {
        if (session == null || session.IsHost) return;
        if (string.IsNullOrEmpty(Token)) NewToken();
        var t = new Ticket
        {
            sessionId = session.Id, code = session.Code, token = Token,
            unique = (int)UniqueItems.LocalChoice, players = players,
            suit = PlayerSuitColor.LocalSlot,
            savedUtcTicks = DateTime.UtcNow.Ticks,
        };
        Write(t);
    }

    static void Write(Ticket t)
    {
        try { PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(t)); PlayerPrefs.Save(); } catch { }
    }

    public static bool TryLoadTicket(out Ticket ticket)
    {
        ticket = null;
        try
        {
            string json = PlayerPrefs.GetString(PrefKey, "");
            if (string.IsNullOrEmpty(json)) return false;
            ticket = JsonUtility.FromJson<Ticket>(json);
        }
        catch { ticket = null; }
        if (ticket == null || string.IsNullOrEmpty(ticket.code) || string.IsNullOrEmpty(ticket.token)) { ClearTicket(); return false; }
        if ((DateTime.UtcNow - new DateTime(ticket.savedUtcTicks, DateTimeKind.Utc)).TotalHours > ExpireHours) { ClearTicket(); return false; }
        return true;
    }

    /// <summary>게임에서 나올 때·게임 중 몇 초마다: 표의 저장 시각을 지금으로 (3시간은 나온 때부터 셈)</summary>
    public static void RefreshTicketTime()
    {
        if (!TryLoadTicket(out var t)) return;
        t.savedUtcTicks = DateTime.UtcNow.Ticks;
        Write(t);
    }

    /// <summary>참가자: 내 대원의 탄약·손전등을 표에 적어 둔다 (게임 중 몇 초마다, 나갈 때)</summary>
    public static void SaveCarry(GameObject player)
    {
        if (player == null || !TryLoadTicket(out var t) || t.token != Token) return;
        var pistol = player.GetComponent<FpsPistol>();
        var light = player.GetComponent<FpsWeaponFlashlight>();
        bool changed = !t.hasCarry;
        if (pistol != null && (pistol.Magazine != t.magazine || pistol.Reserve != t.reserve)) changed = true;
        if (light != null && light.On != t.flashlight) changed = true;
        if (!changed && (DateTime.UtcNow - new DateTime(t.savedUtcTicks, DateTimeKind.Utc)).TotalMinutes < 10) return;
        t.hasCarry = true;
        if (pistol != null) { t.magazine = pistol.Magazine; t.reserve = pistol.Reserve; }
        t.flashlight = light != null && light.On;
        t.savedUtcTicks = DateTime.UtcNow.Ticks;
        Write(t);
    }

    public static void ClearTicket()
    {
        try { PlayerPrefs.DeleteKey(PrefKey); PlayerPrefs.Save(); } catch { }
    }

    /// <summary>다시 참가하기 직전: 표의 값으로 이 PC 상태를 맞춘다</summary>
    public static void ApplyTicket(Ticket t)
    {
        Token = t.token;
        GameBalance.StartPlayers = Mathf.Clamp(t.players, 1, 4);
        UniqueItems.LocalChoice = (UniqueItemKind)t.unique;
        if (t.suit >= 0) PlayerSuitColor.LocalSlot = t.suit;
        FloorCarryOver.ResetAll();   // 예전 판의 층 이동 상태가 남아 있지 않게
        PlayLog.NewGame();
        IsRejoining = true;
        _pendingCarry = t.hasCarry ? t : null;
    }

    /// <summary>다시 참가해 내 대원이 나타났을 때: 표에 적어 둔 탄약·손전등을 되돌린다</summary>
    public static void ApplyPendingCarry(GameObject player)
    {
        var t = _pendingCarry;
        _pendingCarry = null;
        IsRejoining = false;
        if (t == null || player == null) return;
        var pistol = player.GetComponent<FpsPistol>();
        if (pistol != null) pistol.RestoreAmmo(t.magazine, t.reserve);
        var light = player.GetComponent<FpsWeaponFlashlight>();
        if (light != null && light.On != t.flashlight) light.Toggle();
    }

    /// <summary>로비로 돌아왔을 때 (게임 밖)</summary>
    public static void ResetLocalFlags() { IsRejoining = false; _pendingCarry = null; }

    // ------------------------------------------------------------------ 방장: 대원 명단
    sealed class Entry
    {
        public ulong clientId;
        public string authId = "";
        public float health = -1;
        public bool left;
        public DateTime leftUtc;
    }

    static readonly Dictionary<string, Entry> _roster = new Dictionary<string, Entry>();

    /// <summary>방장: 게임을 시작할 때 명단을 비운다</summary>
    public static void ServerBeginGame() { _roster.Clear(); }

    /// <summary>방장: 대원이 표식을 알려 왔을 때 (층마다 대원이 새로 만들어질 때도 불림). 다시 들어온 대원이면 true</summary>
    public static bool ServerRegister(FpsNetworkPlayer player, string token, string authId)
    {
        var nm = NetworkManager.Singleton;
        if (player == null || nm == null || !nm.IsServer || string.IsNullOrEmpty(token)) return false;
        ulong clientId = player.OwnerClientId;
        if (_roster.TryGetValue(token, out var e))
        {
            bool returning = e.left || e.clientId != clientId;
            ulong oldClient = e.clientId;
            e.clientId = clientId; e.left = false;
            if (!string.IsNullOrEmpty(authId)) e.authId = authId;
            if (returning)
            {
                Debug.Log($"[Rejoin] 대원이 다시 들어옴 (client {oldClient} → {clientId}, HP {e.health})");
                if (e.health >= 0) player.RestoreAfterRejoin(e.health);
                // 강제 종료 직후라 예전 연결이 아직 남아 있으면 바로 끊는다 (유령 대원 · 계단 준비 인원에 섞이지 않게)
                if (oldClient != clientId && oldClient != NetworkManager.ServerClientId && nm.ConnectedClients.ContainsKey(oldClient))
                {
                    Debug.Log($"[Rejoin] 남아 있던 예전 연결 client {oldClient}을 끊음");
                    nm.DisconnectClient(oldClient, "같은 대원이 다시 접속했어요.");
                }
            }
            return returning;
        }
        if (_roster.Count >= GameBalance.Players && clientId != NetworkManager.ServerClientId)
        {
            // 시작 인원이 다 찬 뒤 처음 보는 사람 → 진행 중인 게임에는 새로 들어올 수 없다
            Debug.Log($"[Rejoin] 명단에 없는 대원 (client {clientId}) → 내보냄");
            nm.DisconnectClient(clientId, GameInProgressReason);
            return false;
        }
        _roster[token] = new Entry { clientId = clientId, authId = authId ?? "", health = player.Health != null ? player.Health.Current : -1 };
        return false;
    }

    /// <summary>방장: 대원 HP가 바뀔 때마다 기억 (나갈 때 되돌리려고)</summary>
    public static void ServerTrackHealth(ulong clientId, float health)
    {
        foreach (var e in _roster.Values)
            if (e.clientId == clientId && !e.left) { e.health = health; return; }
    }

    /// <summary>방장: 대원이 게임을 나갔을 때. 다시 들어올 수 있게 잠시 뒤 세션 자리도 비운다</summary>
    public static void ServerClientLeft(ulong clientId)
    {
        foreach (var e in _roster.Values)
        {
            if (e.clientId != clientId || e.left) continue;
            e.left = true;
            e.leftUtc = DateTime.UtcNow;
            Debug.Log($"[Rejoin] 대원이 나감 (client {clientId}, HP {e.health}) → 다시 들어올 수 있음");
            if (!string.IsNullOrEmpty(e.authId)) _ = RemoveFromSessionAsync(e, e.authId, e.leftUtc);
            return;
        }
    }

    /// <summary>
    /// 나간 대원의 예전 세션 자리를 비운다. 세션 정보가 늦게 바뀔 수 있어 잠시 기다린 뒤 확인하고,
    /// 그새 같은 대원이 다시 들어왔으면(새로 들어온 시각이 나간 시각과 가까움) 빼지 않는다.
    /// </summary>
    static async Task RemoveFromSessionAsync(Entry entry, string authId, DateTime leftUtc)
    {
        try
        {
            await Task.Delay(4000);
            if (!entry.left || entry.leftUtc != leftUtc) return;   // 그새 다시 들어옴
            foreach (var s in MultiplayerService.Instance.Sessions.Values)
            {
                if (!s.IsHost) continue;
                foreach (var p in s.Players)
                {
                    if (p.Id != authId) continue;
                    // 컴퓨터 시계가 조금 달라도 되도록 1분 여유: 나가기 1분 전보다 나중에 들어온 자리는 다시 들어온 것으로 본다
                    var joined = p.Joined.Kind == DateTimeKind.Local ? p.Joined.ToUniversalTime() : DateTime.SpecifyKind(p.Joined, DateTimeKind.Utc);
                    if (joined > leftUtc.AddMinutes(-1)) { Debug.Log("[Rejoin] 세션 자리를 그대로 둠 (방금 다시 들어온 자리)"); break; }
                    await s.AsHost().RemovePlayerAsync(authId);
                    Debug.Log("[Rejoin] 나간 대원의 세션 자리를 비움");
                    break;
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Rejoin] 세션에서 나간 대원을 빼지 못함: " + e.Message); }
    }
}
