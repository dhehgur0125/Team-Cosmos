using UnityEngine;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
public class RobotCommandManager : NetworkBehaviour
{
    [Header("References")]
    public Transform robotTransform; 

    [Header("Robot Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float arrivalThreshold = 3.5f; // 🌟 광물 크기를 고려해 넉넉하게 설정
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Obstacle Avoidance (No NavMesh)")]
    [Tooltip("장애물로 인식할 레이어")]
    [SerializeField] private LayerMask obstacleLayer;
    [Tooltip("정면 더듬이 길이")]
    [SerializeField] private float centerRayDistance = 4.0f;
    [Tooltip("측면 더듬이 길이")]
    [SerializeField] private float sideRayDistance = 2.0f;
    
    [Tooltip("장애물 발견 시 강제로 우회할 시간 (초)")]
    [SerializeField] private float evasionDuration = 1.5f;
    private float currentEvasionTimer = 0f;
    private Vector3 lockedEvasionDirection = Vector3.zero;

    [Header("Gravity Settings")]
    [SerializeField] private float gravity = -15f;
    private float verticalVelocity = 0f;

    [Header("Mining Settings")]
    [Tooltip("한 번 타격 시 입히는 데미지")]
    [SerializeField] private float miningDamage = 25f;
    [Tooltip("타격 주기 (초)")]
    [SerializeField] private float miningInterval = 1.0f;

    private CharacterController controller;

    private Vector3? designatedOrePosition = null;
    private bool isMiningInProgress = false;
    private OreNode currentTargetOre = null;

    // 관제탑이 확인하는 로봇의 작업 상태
    public bool IsIdle { get; private set; } = true;

    // 관제탑에서 목표 위치를 확인할 수 있는 프로퍼티
    public Vector3? TargetPosition => designatedOrePosition; 

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (robotTransform == null) robotTransform = transform;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            // 관제탑에 안전하게 등록되도록 코루틴 실행
            StartCoroutine(RegisterToManagerSafe());
        }
    }

    private IEnumerator RegisterToManagerSafe()
    {
        while (RobotTaskManager.Instance == null)
        {
            yield return null;
        }

        RobotTaskManager.Instance.RegisterRobot(this);
    }

    // 관제탑이 이 로봇에게 임무를 하달할 때 호출
    public void AssignTask(Vector3 targetPos)
    {
        if (!IsServer) return;

        // 1. 혹시라도 하던 일이 있으면 먼저 전부 취소
        CancelCurrentMining();

        // 2. 취소가 끝난 직후에 확실하게 작업 중 상태로 변경
        IsIdle = false; 

        designatedOrePosition = targetPos;
        isMiningInProgress = false;
        
        Debug.Log($"[서버 - 로봇] 관제탑으로부터 채굴 좌표 할당받음: {targetPos}. 이동 시작.");
    }

    // 관제탑에서 작업 취소 명령을 내릴 때 호출
    public void ForceCancelTask()
    {
        if (!IsServer) return;
        
        Debug.Log("[서버 - 로봇] 관제탑의 긴급 호출! 작업을 취소하고 대기합니다.");
        CancelCurrentMining();
        
        if (RobotTaskManager.Instance != null)
        {
            RobotTaskManager.Instance.TryAssignTasks();
        }
    }

    private void CancelCurrentMining()
    {
        if (isMiningInProgress && currentTargetOre != null)
        {
            if (WorldManager.Instance != null)
                WorldManager.Instance.ReleaseOreReservation(currentTargetOre);
        }

        StopAllCoroutines();
        designatedOrePosition = null;
        isMiningInProgress = false;
        currentTargetOre = null;
        currentEvasionTimer = 0f; // 회피 타이머도 초기화
        IsIdle = true;
    }

    void Update()
    {
        if (!IsServer) return;

        ApplyGravity();

        if (designatedOrePosition == null) return;

        Vector3 targetPos = designatedOrePosition.Value;
        float distanceToTarget = Vector3.Distance(
            new Vector3(robotTransform.position.x, 0, robotTransform.position.z), 
            new Vector3(targetPos.x, 0, targetPos.z)
        );

        if (distanceToTarget > arrivalThreshold)
        {
            MoveStraightToTarget(targetPos);
        }
        else
        {
            if (!isMiningInProgress)
            {
                isMiningInProgress = true;
                StartCoroutine(MineOreRoutine(targetPos));
            }
        }
    }

    private void ApplyGravity()
    {
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0) verticalVelocity = -2f; 
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        controller.Move(new Vector3(0, verticalVelocity * Time.deltaTime, 0));
    }

    // 🌟 1~2초간 목표를 잊고 강제로 우회(Evasion Mode)하는 로직
    private void MoveStraightToTarget(Vector3 targetPos)
    {
        Vector3 directionToTarget = (targetPos - robotTransform.position);
        directionToTarget.y = 0f; 

        if (directionToTarget.sqrMagnitude > 0.001f)
        {
            Vector3 desiredDirection = directionToTarget.normalized;
            Vector3 finalMoveDirection = desiredDirection;

            Vector3 rayOrigin = robotTransform.position + Vector3.up * 0.5f; // 무릎 높이에서 발사

            // 1. 회피 모드 진행 중 (타이머가 0보다 클 때)
            if (currentEvasionTimer > 0f)
            {
                currentEvasionTimer -= Time.deltaTime; 
                
                finalMoveDirection = lockedEvasionDirection;

                // 회피 중 다른 바위에 정면 충돌하는 것 방지
                if (Physics.Raycast(rayOrigin, robotTransform.forward, out RaycastHit hit, centerRayDistance, obstacleLayer))
                {
                    Vector3 slideDir = Vector3.Cross(hit.normal, Vector3.up).normalized;
                    if (Vector3.Dot(slideDir, lockedEvasionDirection) < 0) slideDir = -slideDir;
                    lockedEvasionDirection = slideDir;
                }
            }
            // 2. 평상시 상태 (장애물 탐지)
            else
            {
                Vector3 forward = robotTransform.forward;
                Vector3 rightAngle = Quaternion.Euler(0, 45f, 0) * forward;
                Vector3 leftAngle = Quaternion.Euler(0, -45f, 0) * forward;

                bool hitCenter = Physics.Raycast(rayOrigin, forward, out RaycastHit centerHit, centerRayDistance, obstacleLayer);
                bool hitRight = Physics.Raycast(rayOrigin, rightAngle, out RaycastHit rightHit, sideRayDistance, obstacleLayer);
                bool hitLeft = Physics.Raycast(rayOrigin, leftAngle, out RaycastHit leftHit, sideRayDistance, obstacleLayer);

                Debug.DrawRay(rayOrigin, forward * centerRayDistance, hitCenter ? Color.red : Color.green);
                Debug.DrawRay(rayOrigin, rightAngle * sideRayDistance, hitRight ? Color.red : Color.green);
                Debug.DrawRay(rayOrigin, leftAngle * sideRayDistance, hitLeft ? Color.red : Color.green);

                if (hitCenter || hitRight || hitLeft)
                {
                    // 장애물 감지 시 회피 모드 돌입
                    currentEvasionTimer = evasionDuration; 

                    if (hitCenter)
                    {
                        lockedEvasionDirection = Vector3.Cross(centerHit.normal, Vector3.up).normalized;
                        if (Vector3.Dot(lockedEvasionDirection, desiredDirection) < 0) 
                            lockedEvasionDirection = -lockedEvasionDirection;
                    }
                    else if (hitRight) 
                        lockedEvasionDirection = -robotTransform.right; 
                    else if (hitLeft) 
                        lockedEvasionDirection = robotTransform.right;  

                    finalMoveDirection = lockedEvasionDirection;
                }
                else
                {
                    finalMoveDirection = desiredDirection;
                }
            }

            // 물리 이동 적용
            controller.Move(finalMoveDirection * moveSpeed * Time.deltaTime);

            // 부드러운 회전 적용
            if (finalMoveDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(finalMoveDirection);
                robotTransform.rotation = Quaternion.Slerp(robotTransform.rotation, targetRotation, 10f * Time.deltaTime);
            }
        }
    }

    private IEnumerator MineOreRoutine(Vector3 targetPos)
    {
        // 넓은 탐색 범위로 좌표 오차 방지
        Collider[] colliders = Physics.OverlapSphere(targetPos, 5.0f);
        
        OreNode nearestOre = null;
        float minDistance = float.MaxValue;

        foreach (Collider col in colliders)
        {
            OreNode ore = col.GetComponentInParent<OreNode>();
            if (ore != null && !ore.isDestroyed)
            {
                float dist = Vector3.Distance(targetPos, ore.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearestOre = ore;
                }
            }
        }

        if (nearestOre != null)
        {
            currentTargetOre = nearestOre;
        }

        if (currentTargetOre == null)
        {
            OreNode[] allOres = FindObjectsOfType<OreNode>();
            foreach (OreNode ore in allOres)
            {
                if (!ore.isDestroyed)
                {
                    currentTargetOre = ore;
                    break;
                }
            }
        }

        if (currentTargetOre == null)
        {
            Debug.Log("[서버 - 로봇] 맵에 캘 수 있는 광물이 아예 존재하지 않습니다.");
            CancelCurrentMining();
            if (RobotTaskManager.Instance != null) RobotTaskManager.Instance.TryAssignTasks();
            yield break;
        }

        // WorldManager를 통해 안전하게 자원 예약
        if (WorldManager.Instance != null && !WorldManager.Instance.ReserveOre(currentTargetOre))
        {
            Debug.Log($"[서버 - 로봇] 이미 다른 로봇이 예약한 광물입니다. 임무 취소.");
            CancelCurrentMining();
            if (RobotTaskManager.Instance != null) RobotTaskManager.Instance.TryAssignTasks();
            yield break;
        }

        Debug.Log($"[서버 - 로봇] 채굴 시작! 대상 광물 체력 깎는 중... (대상: {currentTargetOre.name})");

        while (currentTargetOre != null && !currentTargetOre.isDestroyed)
        {
            Vector3 lookDir = currentTargetOre.transform.position - robotTransform.position;
            lookDir.y = 0f;
            if (lookDir != Vector3.zero)
            {
                robotTransform.rotation = Quaternion.LookRotation(lookDir);
            }

            currentTargetOre.TakeDamage(miningDamage);
            
            yield return new WaitForSeconds(miningInterval);
        }

        Debug.Log("[서버 - 로봇] 광물 채굴 완료!");

        currentTargetOre = null;
        designatedOrePosition = null;
        isMiningInProgress = false;
        IsIdle = true;

        if (RobotTaskManager.Instance != null)
        {
            RobotTaskManager.Instance.TryAssignTasks();
        }
    }
}