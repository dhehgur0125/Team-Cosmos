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
        }
    }

    void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientDisconnectCallback -= OnClientDisconnect;
            nm.OnServerStopped -= OnServerStopped;
        }
    }

    void OnClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsServer) return;
        if (clientId == nm.LocalClientId || clientId == NetworkManager.ServerClientId) _ = ReturnToLobbyAsync("방장과의 연결이 끊어졌어요.");
    }

    void OnServerStopped(bool wasHost) { }

    /// <summary>세션·네트워크·음성을 정리하고 로비 씬으로 돌아간다.</summary>
    public async Task ReturnToLobbyAsync(string reason)
    {
        if (_leaving) return;
        _leaving = true;
        Debug.Log("[Stage2] 로비로 돌아갑니다: " + reason);
        LobbyReturnMessage = reason;

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
