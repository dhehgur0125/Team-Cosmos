using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using VoidLast.Player;

/// <summary>
/// 진행 중인 게임에 다시 참가 (재접속).
///  - 게임이 시작되면 방은 잠그지 않고 "비공개"로 바뀐다 → 방 목록에는 안 보이고, 참가 코드로만 들어올 수 있다.
///  - 참가자 PC: 게임을 시작할 때 "다시 참가 표"(방 코드, 이번 게임 표식, 고른 고유 아이템, 시작 인원)를 PlayerPrefs에 저장한다.
///    게임이 꺼지거나 연결이 끊겨 로비로 오면 "진행 중인 게임 다시 참가" 버튼이 보인다 (3시간 지나면 버림).
///  - 방장: 이번 게임의 대원 명단(표식)을 기억한다. 명단에 없는 사람이 게임 중에 들어오면 내보낸다.
///    대원이 나가면 그 대원의 HP·사망 여부를 기억하고, 세션에서도 빼서 자리를 비워 둔다.
///    다시 들어오면 지금 층의 A 계단 도착 지점에 대원을 만들고, 기억해 둔 HP·사망 여부를 되돌린다.
///    가지고 있던 물건은 나갈 때 그 자리에 떨어졌으므로(MissionLeaverDrops 등) 다시 주우면 된다. 권총·고유 아이템은 새로 받는다.
///  - 인원별 밸런스는 시작 인원 그대로 (나가도·다시 들어와도 바꾸지 않음).
/// </summary>
public static class Rejoin
{
    const string PrefKey = "VL_Rejoin";
    const double ExpireHours = 3;
    public const string SessionKeyPlayers = "players";

    /// <summary>이번 게임에서 이 PC의 대원 표식 (게임 시작·다시 참가 때 정해짐)</summary>
    public static string Token = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Token = ""; _roster.Clear(); }

    // ------------------------------------------------------------------ 참가자 PC: 다시 참가 표
    [Serializable]
    public class Ticket
    {
        public string sessionId, code, token;
        public int unique, players;
        public long savedUtcTicks;
    }

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
            savedUtcTicks = DateTime.UtcNow.Ticks,
        };
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
        FloorCarryOver.ResetAll();   // 예전 판의 층 이동 상태가 남아 있지 않게
        PlayLog.NewGame();
    }

    // ------------------------------------------------------------------ 방장: 대원 명단
    sealed class Entry
    {
        public ulong clientId;
        public string authId = "";
        public float health = -1;
        public bool left;
    }

    static readonly Dictionary<string, Entry> _roster = new Dictionary<string, Entry>();

    /// <summary>방장: 게임을 시작할 때 명단을 비운다</summary>
    public static void ServerBeginGame() { _roster.Clear(); }

    /// <summary>방장: 대원이 표식을 알려 왔을 때 (층마다 대원이 새로 만들어질 때도 불림)</summary>
    public static void ServerRegister(FpsNetworkPlayer player, string token, string authId)
    {
        var nm = NetworkManager.Singleton;
        if (player == null || nm == null || !nm.IsServer || string.IsNullOrEmpty(token)) return;
        ulong clientId = player.OwnerClientId;
        if (_roster.TryGetValue(token, out var e))
        {
            bool returning = e.left || e.clientId != clientId;
            e.clientId = clientId; e.left = false;
            if (!string.IsNullOrEmpty(authId)) e.authId = authId;
            if (returning)
            {
                Debug.Log($"[Rejoin] 대원이 다시 들어옴 (client {clientId}, HP {e.health})");
                if (e.health >= 0) player.RestoreAfterRejoin(e.health);
            }
            return;
        }
        if (_roster.Count >= GameBalance.Players && clientId != NetworkManager.ServerClientId)
        {
            // 시작 인원이 다 찬 뒤 처음 보는 사람 → 진행 중인 게임에는 새로 들어올 수 없다
            Debug.Log($"[Rejoin] 명단에 없는 대원 (client {clientId}) → 내보냄");
            nm.DisconnectClient(clientId, "이미 진행 중인 게임이에요. 게임이 끝나면 들어올 수 있어요.");
            return;
        }
        _roster[token] = new Entry { clientId = clientId, authId = authId ?? "", health = player.Health != null ? player.Health.Current : -1 };
    }

    /// <summary>방장: 대원 HP가 바뀔 때마다 기억 (나갈 때 되돌리려고)</summary>
    public static void ServerTrackHealth(ulong clientId, float health)
    {
        foreach (var e in _roster.Values)
            if (e.clientId == clientId && !e.left) { e.health = health; return; }
    }

    /// <summary>방장: 대원이 게임을 나갔을 때. 다시 들어올 수 있게 세션 자리도 비운다</summary>
    public static void ServerClientLeft(ulong clientId)
    {
        foreach (var e in _roster.Values)
        {
            if (e.clientId != clientId || e.left) continue;
            e.left = true;
            Debug.Log($"[Rejoin] 대원이 나감 (client {clientId}, HP {e.health}) → 다시 들어올 수 있음");
            if (!string.IsNullOrEmpty(e.authId)) _ = RemoveFromSessionAsync(e.authId);
            return;
        }
    }

    static async Task RemoveFromSessionAsync(string authId)
    {
        try
        {
            foreach (var s in MultiplayerService.Instance.Sessions.Values)
            {
                if (!s.IsHost) continue;
                bool present = false;
                foreach (var p in s.Players) if (p.Id == authId) { present = true; break; }
                if (present) await s.AsHost().RemovePlayerAsync(authId);
            }
        }
        catch (Exception e) { Debug.LogWarning("[Rejoin] 세션에서 나간 대원을 빼지 못함: " + e.Message); }
    }
}
