using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using TMPro;
using Unity.Networking.Transport.Relay;

public class RelayManager : MonoBehaviour
{
    [Header("UI 연결")]
    public TMP_Text joinCodeText;
    public TMP_InputField joinCodeInput;

    [Header("방 생성 및 참가 후 비활성화할 UI")]
    public GameObject[] uiToHide;

    [Header("방 생성 및 참가 후 활성화할 UI")]
    public GameObject[] uiToActive;

    private NetworkManager networkManager;
    private float nextStatusLogTime;
    private bool connectionUIHandled = false;
    private bool callbacksRegistered = false;

    private async void Start()
    {
        networkManager = NetworkManager.Singleton;

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Debug.Log(
                    $"유니티 클라우드 로그인 성공! ID: " +
                    $"{AuthenticationService.Instance.PlayerId}"
                );
            }

            // 초기화가 끝난 뒤 NetworkManager를 다시 확인
            networkManager = NetworkManager.Singleton;

            if (networkManager == null)
            {
                Debug.LogError(
                    "[RelayManager] NetworkManager를 찾을 수 없습니다."
                );
                return;
            }

            RegisterNetworkCallbacks();

            Debug.Log("[RelayManager] 초기화 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError(
                $"Unity Services 초기화 또는 로그인 실패:\n{e}"
            );
        }
    }

    private void OnDestroy()
    {
        UnregisterNetworkCallbacks();
    }

    private void RegisterNetworkCallbacks()
    {
        NetworkManager nm = NetworkManager.Singleton;

        if (nm == null)
        {
            Debug.LogError("[RelayManager] NetworkManager를 찾을 수 없습니다.");
            return;
        }

        if (callbacksRegistered)
            return;

        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        callbacksRegistered = true;

        Debug.Log("[RelayManager] 네트워크 이벤트 등록 완료");
    }

    private void UnregisterNetworkCallbacks()
    {
        NetworkManager nm = NetworkManager.Singleton;

        if (nm != null && callbacksRegistered)
        {
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        callbacksRegistered = false;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.LogError(
            $"[연결 종료] Client ID: {clientId}"
        );
    }

    // 실제 클라이언트 연결이 완료되었을 때 호출
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[연결 이벤트] Client ID: {clientId}");

        NetworkManager nm = NetworkManager.Singleton;

        if (nm == null)
            return;

        Debug.Log($"[연결 이벤트] LocalClientId: {nm.LocalClientId}");

        if (clientId == nm.LocalClientId)
        {
            Debug.Log("[UI 처리] 연결 성공!");

            HideUIAfterConnection();
            LockGameCursor();
        }
    }

    // ==========================================
    // 방장 전용: 방 생성
    // ==========================================
    public async void CreateRelayRoom()
    {
        Debug.Log("방 생성 버튼 클릭됨!");

        try
        {
            Debug.Log("방 생성 중...");

            // 1. NetworkManager 확인
            NetworkManager nm = NetworkManager.Singleton;

            if (nm == null)
            {
                Debug.LogError(
                    "[방 생성 실패] NetworkManager.Singleton이 null입니다."
                );
                return;
            }

            // 2. UnityTransport 확인
            UnityTransport transport = nm.GetComponent<UnityTransport>();

            if (transport == null)
            {
                Debug.LogError(
                    "[방 생성 실패] NetworkManager에 UnityTransport가 없습니다."
                );
                return;
            }

            // 3. Unity Services 초기화 및 로그인 확인
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                Debug.LogError(
                    "[방 생성 실패] Unity Services가 초기화되지 않았습니다."
                );
                return;
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.LogError(
                    "[방 생성 실패] Unity Authentication 로그인이 필요합니다."
                );
                return;
            }

            // 4. Relay 방 생성
            Allocation allocation =
                await RelayService.Instance.CreateAllocationAsync(3);

            Debug.Log("[방 생성] Relay Allocation 생성 성공");

            // 5. 참가 코드 생성
            string joinCode =
                await RelayService.Instance.GetJoinCodeAsync(
                    allocation.AllocationId
                );

            Debug.Log($"[방 생성] 참가 코드 생성 성공: {joinCode}");

            // 6. 참가 코드 표시
            if (joinCodeText != null)
            {
                joinCodeText.text = joinCode;
                joinCodeText.gameObject.SetActive(true);
            }

            // 7. Relay 연결 설정
            RelayServerData relayData =
                allocation.ToRelayServerData("dtls");

            transport.SetRelayServerData(relayData);

            Debug.Log("[방 생성] Relay 연결 설정 완료");

            // 8. 호스트 시작
            bool success = nm.StartHost();

            if (!success)
            {
                Debug.LogError("[방 생성 실패] StartHost()가 false를 반환했습니다.");
                return;
            }

            Debug.Log("[방 생성] 호스트 시작 성공!");

            // 9. UI 변경
            HideUIAfterConnection();

            // 10. 게임 커서 잠금
            LockGameCursor();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"방 생성 실패:\n{e}");
        }
    }

    // ==========================================
    // 참가자 전용: 방 참가
    // ==========================================
    public async void JoinRelayRoom()
    {
        Debug.Log("[1] 방 참가 버튼 클릭");

        try
        {
            if (joinCodeInput == null)
            {
                Debug.LogError("[오류] JoinCodeInput이 연결되지 않았습니다.");
                return;
            }

            string inputCode = joinCodeInput.text.Trim();

            if (string.IsNullOrWhiteSpace(inputCode))
            {
                Debug.LogError("[오류] 참가 코드가 비어 있습니다.");
                return;
            }

            Debug.Log($"[2] 참가 코드: {inputCode}");

            // Unity Services 초기화 확인
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                Debug.LogError("[오류] Unity Services가 초기화되지 않았습니다.");
                return;
            }

            // 익명 로그인 확인
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log("[3] 익명 로그인 진행 중...");

                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            Debug.Log("[4] Relay 참가 요청 시작");

            JoinAllocation allocation =
                await RelayService.Instance.JoinAllocationAsync(inputCode);

            Debug.Log("[5] Relay 참가 요청 성공");

            NetworkManager nm = NetworkManager.Singleton;

            if (nm == null)
            {
                Debug.LogError("[오류] NetworkManager.Singleton이 없습니다.");
                return;
            }

            UnityTransport transport =
                nm.GetComponent<UnityTransport>();

            if (transport == null)
            {
                Debug.LogError("[오류] UnityTransport가 없습니다.");
                return;
            }

            RelayServerData relayData =
                allocation.ToRelayServerData("dtls");

            transport.SetRelayServerData(relayData);

            Debug.Log("[6] Relay 연결 정보 설정 완료");

            bool started = nm.StartClient();

            Debug.Log($"[7] StartClient 결과: {started}");

            if (!started)
            {
                Debug.LogError("[오류] StartClient 실행 실패");
                return;
            }

            Debug.Log("[8] 서버 연결 대기 중");
        }
        catch (System.Exception e)
        {
            Debug.LogError(
                $"[Relay 참가 오류]\n{e}"
            );
        }
    }

    // ==========================================
    // 게임 커서 제어
    // ==========================================
    private void LockGameCursor()
    {
        ShoulderCamera shoulderCamera =
            Camera.main != null
                ? Camera.main.GetComponent<ShoulderCamera>()
                : null;

        if (shoulderCamera != null)
        {
            shoulderCamera.EnableGameCursorLock();
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void HideUIAfterConnection()
    {
        // 등록된 UI 비활성화
        foreach (GameObject ui in uiToHide)
        {
            if (ui != null)
            {
                ui.SetActive(false);
            }
        }

        foreach (GameObject ui in uiToActive)
        {
            if (ui != null)
            {
                ui.SetActive(true);
            }
        }

        // 참가 코드 표시 UI는 활성화 상태 유지
        if (joinCodeText != null)
        {
            joinCodeText.gameObject.SetActive(true);
        }
    }
    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;

        if (nm == null || !nm.IsListening)
            return;

        if (nm.IsClient && nm.IsConnectedClient && !connectionUIHandled)
        {
            connectionUIHandled = true;

            Debug.Log("[UI 처리] 연결 상태 확인 완료!");

            HideUIAfterConnection();
            LockGameCursor();
        }

        if (Time.time >= nextStatusLogTime)
        {


            nextStatusLogTime = Time.time + 3f;
        }
    }

    
}