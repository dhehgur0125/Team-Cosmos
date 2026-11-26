using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Stage2_Ship 씬의 온라인/오프라인 전환.
///  - 로비에서 게임 시작으로 들어온 경우(네트워크 실행 중): 씬에 미리 놓인 테스트용 1인 플레이어와 기존 Vivox 프로토타입을 끄고,
///    대원마다 서버가 스폰하는 VOID_LAST_FPS_Network와 멀티플레이 음성 채팅(InGameVoiceChat)을 쓴다.
///  - 씬을 에디터에서 바로 실행한 경우(오프라인): 기존 테스트 구성을 그대로 쓰고 멀티플레이 오브젝트는 끈다.
///  - 게임 중 방장과의 연결이 끊어지면 로비로 돌아간다.
/// 다른 스크립트보다 먼저 Awake해서, 꺼야 할 오브젝트가 Start/OnEnable을 실행하기 전에 비활성화한다.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class Stage2NetworkBootstrap : MonoBehaviour
{
    [Tooltip("오프라인 테스트용으로만 쓰는 오브젝트 (씬에 놓인 1인 플레이어, Vivox 프로토타입 HUD 등)")]
    public GameObject[] offlineOnly;
    [Tooltip("멀티플레이에서만 쓰는 오브젝트 (멀티 음성 채팅 등). NetworkObject는 여기에 넣지 말 것")]
    public GameObject[] onlineOnly;
    public string lobbySceneName = "Lobby";

    public static bool IsOnline
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening;
        }
    }

    bool _online, _leaving;

    void Awake()
    {
        _online = IsOnline;
        foreach (var g in offlineOnly)
        {
            if (g == null) continue;
            if (_online) { g.SetActive(false); Destroy(g); }
        }
        foreach (var g in onlineOnly) if (g != null) g.SetActive(_online);
        Debug.Log(_online ? "[Stage2] 멀티플레이 모드: 서버가 대원마다 플레이어를 스폰합니다." : "[Stage2] 오프라인 테스트 모드: 씬에 놓인 플레이어를 사용합니다.");

        if (_online)
        {
            var nm = NetworkManager.Singleton;
            nm.OnClientDisconnectCallback += OnClientDisconnect;
            nm.OnServerStopped += OnServerStopped;
            nm.OnClientStopped += OnClientStopped;
        }
    }

    void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientDisconnectCallback -= OnClientDisconnect;
            nm.OnServerStopped -= OnServerStopped;
            nm.OnClientStopped -= OnClientStopped;
        }
    }

    void OnClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (nm.IsServer) { Rejoin.ServerClientLeft(clientId); return; }   // 방장: 나간 대원을 기억해 다시 들어올 수 있게
        if (clientId == nm.LocalClientId || clientId == NetworkManager.ServerClientId) LostHost(nm.DisconnectReason);
    }

    /// <summary>참가자: 방장과의 연결이 끊겼다 → 끊긴 이유에 따라 다시 참가 표를 남기거나 지우고 로비로</summary>
    void LostHost(string reason)
    {
        if (_leaving || _quitting) return;
        if (reason == Rejoin.RoomClosedReason || reason == Rejoin.GameInProgressReason)
            Rejoin.ClearTicket();         // 방장이 방을 닫음 / 이미 다른 게임이 진행 중 → 다시 들어갈 곳이 없다
        else Rejoin.RefreshTicketTime();  // 연결이 끊김 → 대기실 창에서 다시 참가할 수 있게
        _ = ReturnToLobbyAsync(!string.IsNullOrEmpty(reason) ? reason : "방장과의 연결이 끊어졌어요. 대기실에서 다시 참가할 수 있어요.");
    }

    /// <summary>참가자: 끊김 알림 없이 네트워크가 멈춘 경우에도 로비로 (화면이 게임 안에 멈춰 있지 않게)</summary>
    void OnClientStopped(bool wasHost)
    {
        if (wasHost) return;   // 방장은 OnServerStopped에서
        var nm = NetworkManager.Singleton;
        LostHost(nm != null ? nm.DisconnectReason : "");
    }

    /// <summary>
    /// 방장: 서버가 멈췄다. 직접 나가는 중(_leaving)이나 게임을 끄는 중이 아니면 방장 PC의 인터넷이 끊긴 것 →
    /// 게임을 이어 갈 수 없으므로 방을 닫고 로비로 간다 (예전엔 아무것도 안 해서 방장 화면이 멈춰 있었음)
    /// </summary>
    void OnServerStopped(bool wasHost)
    {
        if (_leaving || _quitting) return;
        Debug.LogWarning("[Stage2] 방장 네트워크가 멈춤 → 로비로");
        _ = ReturnToLobbyAsync("인터넷 연결이 끊어져 게임이 끝났어요.");
    }

    /// <summary>방장: 방을 닫기 전에 참가자들에게 "방이 닫혔다"고 알리고 끊는다 (참가자 PC의 다시 참가 표가 지워지도록)</summary>
    static void NotifyRoomClosed(NetworkManager nm)
    {
        if (nm == null || !nm.IsServer || !nm.IsListening) return;
        foreach (var id in new List<ulong>(nm.ConnectedClientsIds))
            if (id != NetworkManager.ServerClientId)
                try { nm.DisconnectClient(id, Rejoin.RoomClosedReason); } catch (Exception) { }
    }

    bool _quitting;

    void OnApplicationQuit()
    {
        _quitting = true;
        // 방장이 게임 창을 닫음: 참가자들이 닫힌 방에 다시 참가하려 하지 않게 알린다
        if (_online) NotifyRoomClosed(NetworkManager.Singleton);
    }

    /// <summary>세션·네트워크·음성을 정리하고 로비 씬으로 돌아간다.</summary>
    public async Task ReturnToLobbyAsync(string reason)
    {
        if (_leaving) return;
        _leaving = true;
        Debug.Log("[Stage2] 로비로 돌아갑니다: " + reason);
        LobbyReturnMessage = reason;
        NotifyRoomClosed(NetworkManager.Singleton);   // 방장이 방을 닫는 경우

        if (VoiceChatService.Exists) await VoiceChatService.Instance.LeaveAllAsync();
        try
        {
            var sessions = new List<ISession>(MultiplayerService.Instance.Sessions.Values);
            foreach (var s in sessions)
            {
                try { if (s.IsHost) await s.AsHost().DeleteAsync(); else await s.LeaveAsync(); }
                catch (Exception e) { Debug.LogWarning("[Stage2] 세션 정리 경고: " + e.Message); }
            }
        }
        catch (Exception) { }

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening) nm.Shutdown();
            Destroy(nm.gameObject);   // 로비 씬이 새 NetworkManager를 만든다
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(lobbySceneName);
    }

    /// <summary>로비로 돌아왔을 때 상태 줄에 보여줄 안내</summary>
    public static string LobbyReturnMessage;
}
