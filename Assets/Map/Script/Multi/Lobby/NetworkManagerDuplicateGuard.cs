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
        Debug.Log("[Lobby] 이미 연결 중인 NetworkManager가 있어 씬의 NetworkManager를 끕니다 (같은 방으로 복귀).");
        gameObject.SetActive(false);
    }
}
