using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 로비의 NetworkManager에 붙는다 (NetworkManager와 함께 DontDestroyOnLoad로 유지).
/// 로비에서는 플레이어 캐릭터를 만들지 않고, 방장이 게임 씬을 불러오면
/// 모든 대원의 씬 로드가 끝난 시점에 서버가 플레이어 프리팹을 스폰한다.
/// 게임 씬에 따로 놓인 NetworkManager가 있으면 비활성화해서 충돌을 막는다.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LobbyPlayerSpawner : MonoBehaviour
{
    [Tooltip("게임 씬에서 대원마다 스폰할 플레이어 프리팹 (NetworkPrefabs 목록에 있어야 함)")]
    public GameObject playerPrefab;
    public string lobbySceneName = "Lobby";
    [Tooltip("이 이름이 들어간 오브젝트를 스폰 지점으로 사용 (예: PlayerStart, PlayerSpawn)")]
    public string[] spawnPointKeywords = { "PlayerSpawn", "PlayerStart" };
    [Tooltip("스폰 지점이 하나뿐일 때 대원끼리 띄우는 간격(m)")]
    public float spacing = 2f;

    NetworkManager _nm;
    bool _hooked;

    void Awake()
    {
        _nm = GetComponent<NetworkManager>();
        _nm.OnServerStarted += OnServerStarted;
        _nm.OnServerStopped += OnServerStopped;
        SceneManager.sceneLoaded += OnUnitySceneLoaded;
    }

    void OnDestroy()
    {
        if (_nm != null)
        {
            _nm.OnServerStarted -= OnServerStarted;
            _nm.OnServerStopped -= OnServerStopped;
            if (_hooked && _nm.SceneManager != null) _nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;
    }

    void OnServerStarted()
    {
        if (_hooked || _nm.SceneManager == null) return;
        _nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        _hooked = true;
    }

    void OnServerStopped(bool wasHost)
    {
        if (_hooked && _nm.SceneManager != null) _nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        _hooked = false;
    }

    void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        if (!_nm.IsServer || sceneName == lobbySceneName || playerPrefab == null) return;

        var points = FindSpawnPoints(sceneName);
        int index = 0;
        foreach (ulong clientId in _nm.ConnectedClientsIds)
        {
            if (_nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null) continue;

            Vector3 pos; Quaternion rot;
            if (points.Count > 0)
            {
                var p = points[index % points.Count];
                pos = p.position + p.right * (spacing * (index / points.Count));
                rot = p.rotation;
            }
            else
            {
                pos = playerPrefab.transform.position + Vector3.right * (spacing * index);
                rot = playerPrefab.transform.rotation;
            }

            var go = Instantiate(playerPrefab, pos, rot);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
            Debug.Log($"[Lobby] {sceneName}: Client {clientId} 플레이어 스폰 @ {pos}");
            index++;
        }
        if (timedOut != null && timedOut.Count > 0)
            Debug.LogWarning($"[Lobby] 씬 로드 시간 초과 대원 {timedOut.Count}명");
    }

    List<Transform> FindSpawnPoints(string sceneName)
    {
        var list = new List<Transform>();
        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid()) return list;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                foreach (var k in spawnPointKeywords)
                    if (!string.IsNullOrEmpty(k) && t.name.Contains(k)) { list.Add(t); break; }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == lobbySceneName) return;
        // 게임 씬(예: MapScene)에 따로 놓인 NetworkManager는 로비에서 넘어왔을 때 쓰지 않는다.
        // (NetworkManager는 OnEnable에서 스스로 DontDestroyOnLoad로 옮겨지므로 전체에서 찾는다)
        foreach (var other in FindObjectsByType<NetworkManager>(FindObjectsInactive.Exclude))
        {
            if (other == _nm || other == NetworkManager.Singleton) continue;
            Debug.Log($"[Lobby] 게임 씬의 중복 NetworkManager '{other.name}' 비활성화");
            other.gameObject.SetActive(false);
        }
    }
}
