using UnityEngine;
using Unity.Netcode;

public class RobotCommandManager : NetworkBehaviour
{
    [Header("References")]
    public WorldManager worldManager;
    public Transform robotTransform;

    [Header("Robot Settings")]
    [Tooltip("로봇의 이동 속도")]
    [SerializeField] private float moveSpeed = 5f;
    [Tooltip("채굴 완료로 인정할 근접 거리 (도착 판정 거리)")]
    [SerializeField] private float arrivalThreshold = 1.5f;

    [Header("Target Data")]
    // 네트워크로 동기화될 필요가 있다면 NetworkVariable을 쓸 수 있지만, 
    // 여기서는 서버가 직접 관리하는 변수로 둡니다.
    private Vector3? designatedOrePosition = null;
    private bool isMiningInProgress = false;

    private void OnEnable()
    {
        OreScanner.OnOrePositionSelected += OnOrePositionSelectedEvent;
        OreScanner.OnOrePositionCleared += OnOrePositionClearedEvent;
    }

    private void OnDisable()
    {
        OreScanner.OnOrePositionSelected -= OnOrePositionSelectedEvent;
        OreScanner.OnOrePositionCleared -= OnOrePositionClearedEvent;
    }

    // 플레이어가 스캐너로 좌표를 찍었을 때 (클라이언트 -> 서버로 요청)
    private void OnOrePositionSelectedEvent(Vector3 targetPos)
    {
        // 오직 로컬 플레이어의 입력인 경우에만 서버로 명령 전송
        if (!IsOwner) return;

        SetTargetOrePositionServerRpc(targetPos);
    }

    // 플레이어가 스캐너로 지정을 해제했을 때 (클라이언트 -> 서버로 요청)
    private void OnOrePositionClearedEvent()
    {
        if (!IsOwner) return;

        ClearTargetOrePositionServerRpc();
    }

    // ============================================================
    // 서버 RPC (오직 서버에서만 실행되어 맵 전체와 동기화 보장)
    // ============================================================

    [Rpc(SendTo.Server)]
    private void SetTargetOrePositionServerRpc(Vector3 targetPos)
    {
        designatedOrePosition = targetPos;
        isMiningInProgress = false;
        Debug.Log($"[서버 - 로봇 명령] 광물 채굴 좌표 지정됨: {targetPos}. 이동을 시작합니다.");
    }

    [Rpc(SendTo.Server)]
    private void ClearTargetOrePositionServerRpc()
    {
        designatedOrePosition = null;
        isMiningInProgress = false;
        Debug.Log("[서버 - 로봇 명령] 광물 좌표 지정이 해제되어 임무를 중단합니다.");
    }

    void Update()
    {
        // 🌟 핵심: 로봇의 이동과 채굴 판정은 오직 '서버(Host)'에서만 계산합니다!
        if (!IsServer) return;

        if (designatedOrePosition == null || robotTransform == null) return;

        Vector3 targetPos = designatedOrePosition.Value;

        // 로봇 이동 로직 (서버가 계산하므로 모든 유저에게 위치가 자연스럽게 동기화됨)
        Vector3 direction = (targetPos - robotTransform.position);
        direction.y = 0f;

        float distanceToTarget = direction.magnitude;

        if (distanceToTarget > arrivalThreshold)
        {
            robotTransform.position = Vector3.MoveTowards(
                robotTransform.position, 
                targetPos, 
                moveSpeed * Time.deltaTime
            );

            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                robotTransform.rotation = Quaternion.Slerp(robotTransform.rotation, targetRotation, 10f * Time.deltaTime);
            }
        }
        else
        {
            if (!isMiningInProgress)
            {
                isMiningInProgress = true;
                PerformMiningOperation(targetPos);
            }
        }
    }

    private void PerformMiningOperation(Vector3 targetPos)
    {
        Debug.Log($"[서버 - 로봇 채굴] 목표 좌표 {targetPos}에 도착하여 채굴을 시작합니다!");

        // TODO: 채굴 완료 후 자원 획득 처리 등...

        designatedOrePosition = null;
        isMiningInProgress = false;
        
        Debug.Log("[서버 - 로봇 채굴] 채굴 완료!");
    }
}