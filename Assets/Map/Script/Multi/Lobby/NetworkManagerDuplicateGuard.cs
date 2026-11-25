using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 씬의 NetworkManager 오브젝트에 붙는다.
/// 게임이 끝나 같은 방 그대로 로비 씬을 다시 불러오면, 이미 연결 중인 NetworkManager(DontDestroyOnLoad)가 있으므로
/// 씬에 새로 생긴 NetworkManager는 깨어나기 전에 꺼 둔다 (NetworkManager보다 먼저 Awake).
/// </summary>
[DefaultExecutionOrder(-32000)]
public sealed class NetworkManagerDuplicateGuard : MonoBehaviour
{
    void Awake()
    {
        var existing = NetworkManager.Singleton;
        if (existing == null || existing.gameObject == gameObject) return;
        if (!existing.IsListening || existing.ShutdownInProgress)
        {
            // 게임 도중 방에서 나와 로비로 온 경우: 예전 것은 연결이 끝났는데 아직 남아 있다 → 예전 것을 치우고 씬의 것을 쓴다
            // (그대로 두면 로비에 쓸 수 있는 NetworkManager가 없어 다시 참가·새 방 만들기가 실패했다)
            Debug.Log($"[Lobby] 연결이 끝난 예전 NetworkManager({existing.name}, listening {existing.IsListening}, shutdown {existing.ShutdownInProgress})를 치우고 씬의 NetworkManager를 씁니다.");
            existing.gameObject.SetActive(false);
            Destroy(existing.gameObject);
            // NetworkManager는 Singleton이 비어 있을 때만 스스로 등록한다 (예전 것은 프레임 끝에 지워짐) → 씬의 것을 직접 등록
            var mine = GetComponent<NetworkManager>();
            if (mine != null) mine.SetSingleton();
            return;
        }
        Debug.Log("[Lobby] 이미 연결 중인 NetworkManager가 있어 씬의 NetworkManager를 끕니다 (같은 방으로 복귀).");
        gameObject.SetActive(false);
    }
}
