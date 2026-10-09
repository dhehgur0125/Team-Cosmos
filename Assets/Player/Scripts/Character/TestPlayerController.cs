/*
    [ 코드 설명 ]

    플레이어의 이동, 회전, 점프를 담당하는 스크립트입니다.

    WASD
    → 캐릭터 이동

    Shift + 이동
    → 달리기

    Space
    → 점프

    카메라 방향을 기준으로 캐릭터가 이동하고 회전합니다.

    견착 또는 정밀 조준 중
    → Shift를 눌러도 달릴 수 없음
    → 자동으로 걷기 속도로 이동

    멀티플레이에서는
    자신의 캐릭터만 입력을 받을 수 있습니다.
*/

using UnityEngine;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
public class TestPlayerController : NetworkBehaviour
{
    private Animator animator;
    private CharacterController controller;
    private GunAim gunAim;

    [Header("Movement Settings")]
    public float walkSpeed = 3.5f;
    public float runSpeed = 7.5f;
    public float rotationSpeed = 14f;

    [Header("Jump & Gravity Settings")]
    public float jumpHeight = 1.3f;
    public float gravity = -25f;

    private float verticalVelocity = 0f;
    private bool isGrounded = true;

    [Header("Camera Reference")]
    public ShoulderCamera shoulderCam;

    [Header("Ground Check")]
    private RaycastHit[] groundHitsBuffer = new RaycastHit[8];

    private Quaternion lastStableRotation;
    private bool canMove = false;
    private bool spawnInitialized = false;
    private NetworkVariable<bool> networkSpawnInitialized =
    new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Spawn Settings")]
    [SerializeField]
    private Vector3[] spawnPositions =
    {
        new Vector3(250f, 20f, 250f), // 호스트
        new Vector3(254f, 20f, 250f), // 참가자 1
        new Vector3(250f, 20f, 254f), // 참가자 2
        new Vector3(246f, 20f, 250f)  // 참가자 3
    };

    private void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        gunAim = GetComponent<GunAim>();

        lastStableRotation = transform.rotation;

        if (animator != null)
            animator.applyRootMotion = false;
    }

    public override void OnNetworkSpawn()
    {
        canMove = false;
        spawnInitialized = false;

        networkSpawnInitialized.OnValueChanged += OnSpawnInitializedChanged;

        if (IsOwner)
        {
            if (Camera.main != null)
            {
                shoulderCam = Camera.main.GetComponent<ShoulderCamera>();
            }
        }

        if (IsServer)
        {
            StartCoroutine(WaitForInitialChunks());
        }

        // 이미 서버에서 초기화된 플레이어라면 즉시 반영
        if (networkSpawnInitialized.Value)
        {
            ApplySpawnInitialized();
        }
    }

    private void OnSpawnInitializedChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            ApplySpawnInitialized();
        }
    }

    private void ApplySpawnInitialized()
    {
        spawnInitialized = true;
        canMove = true;

        Debug.Log(
            $"[Player] 이동 활성화 | " +
            $"OwnerClientId={OwnerClientId}, IsOwner={IsOwner}"
        );
    }

    public override void OnNetworkDespawn()
    {
        networkSpawnInitialized.OnValueChanged -= OnSpawnInitializedChanged;
        base.OnNetworkDespawn();
    }

    private Vector3 GetSpawnPosition(ulong clientId)
    {
        int spawnIndex = (int)clientId;

        if (spawnPositions == null || spawnPositions.Length == 0)
        {
            Debug.LogError("[Player] 스폰 위치 배열이 비어 있습니다.");
            return new Vector3(250f, 20f, 250f);
        }

        if (spawnIndex < 0 || spawnIndex >= spawnPositions.Length)
        {
            Debug.LogWarning(
                $"[Player] ClientId {clientId}에 해당하는 스폰 위치가 없습니다."
            );

            return spawnPositions[0];
        }

        return spawnPositions[spawnIndex];
    }

    [ClientRpc]
    private void SetSpawnPositionClientRpc(
        Vector3 spawnPosition,
        ClientRpcParams clientRpcParams = default)
    {
        // 이 RPC는 지정된 클라이언트에만 전송한다.
        // 해당 플레이어의 소유 클라이언트에서만 위치를 적용한다.
        if (!IsOwner)
            return;

        CharacterController cc = GetComponent<CharacterController>();

        if (cc != null)
            cc.enabled = false;

        transform.position = spawnPosition;

        if (cc != null)
            cc.enabled = true;

        verticalVelocity = 0f;
        isGrounded = false;

        Debug.Log(
            $"[Player] 스폰 위치 적용 | " +
            $"ClientId={OwnerClientId}, " +
            $"Position={transform.position}"
        );
    }

    private IEnumerator WaitForInitialChunks()
    {
        // 월드가 준비될 때까지 대기
        while (WorldManager.Instance == null ||
            !WorldManager.Instance.IsInitialChunksReady)
        {
            yield return null;
        }

        // 서버에서 해당 플레이어의 스폰 위치 결정
        Vector3 spawnPosition = GetSpawnPosition(OwnerClientId);

        // 서버 자신의 플레이어는 직접 위치 변경
        if (IsOwner)
        {
            CharacterController cc = GetComponent<CharacterController>();

            if (cc != null)
                cc.enabled = false;

            transform.position = spawnPosition;

            if (cc != null)
                cc.enabled = true;

            verticalVelocity = 0f;
            isGrounded = false;
        }
        else
        {
            // 플레이어 소유 클라이언트에만 스폰 위치 전달
            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            };

            SetSpawnPositionClientRpc(spawnPosition, rpcParams);
        }

        // 서버에서 초기화 완료 상태를 네트워크로 전달
        networkSpawnInitialized.Value = true;

        ApplySpawnInitialized();

        Debug.Log(
            $"[Player] 스폰 위치 결정 | " +
            $"ClientId={OwnerClientId}, " +
            $"Position={spawnPosition}"
        );
    }

    private void Update()
    {

        if (!IsOwner)
            return;

        if (!spawnInitialized || !canMove)
        {
            Debug.Log(
                $"[Player Movement Blocked] " +
                $"spawnInitialized={spawnInitialized}, " +
                $"canMove={canMove}"
            );

            return;
        }

        UpdateGroundedState();

        HandleJump();
        HandleMovement();
    }

    private void HandleJump()
    {
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            verticalVelocity =
                Mathf.Sqrt(jumpHeight * -2f * gravity);

            isGrounded = false;
            lastStableRotation = transform.rotation;

            if (animator != null)
            {
                animator.ResetTrigger("Jump");
                animator.SetTrigger("Jump");
            }
        }

        if (!isGrounded)
            verticalVelocity += gravity * Time.deltaTime;
    }

    private void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 inputDir =
            new Vector3(h, 0f, v).normalized;

        bool isMoving = inputDir.magnitude > 0f;

        // 견착 또는 정밀 조준 중인지 확인
        bool isAiming =
            gunAim != null &&
            gunAim.CurrentMode != GunAim.AimMode.Hip;

        // 조준 중에는 Shift를 눌러도 달리지 못함
        bool isRunning =
            !isAiming &&
            isMoving &&
            (Input.GetKey(KeyCode.LeftShift) ||
             Input.GetKey(KeyCode.RightShift));

        float targetAnimSpeed = 0f;
        float moveSpeed = 0f;

        Vector3 moveDir = Vector3.zero;

        if (isMoving && shoulderCam != null)
        {
            targetAnimSpeed =
                isRunning ? 1f : 0.5f;

            moveSpeed =
                isRunning ? runSpeed : walkSpeed;

            float camYaw = shoulderCam.GetYaw();

            Quaternion camYawRotation =
                Quaternion.Euler(0f, camYaw, 0f);

            moveDir =
                camYawRotation * inputDir;

            Quaternion targetRotation =
                Quaternion.LookRotation(moveDir);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );

            lastStableRotation = transform.rotation;
        }
        else
        {
            transform.rotation = lastStableRotation;
        }

        Vector3 finalVelocity =
            (moveDir * moveSpeed) +
            (Vector3.up * verticalVelocity);

        controller.Move(
            finalVelocity * Time.deltaTime
        );

        if (animator != null)
        {
            animator.SetFloat(
                "Speed",
                targetAnimSpeed,
                0.15f,
                Time.deltaTime
            );
        }
    }

    private void UpdateGroundedState()
    {
        if (verticalVelocity > 0.1f)
        {
            isGrounded = false;
            return;
        }

        Vector3 rayOrigin =
            transform.position +
            Vector3.up * 0.15f;

        float rayDistance = 0.25f;

        int hitCount =
            Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                groundHitsBuffer,
                rayDistance,
                ~0,
                QueryTriggerInteraction.Ignore
            );

        bool foundGround = false;

        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform =
                groundHitsBuffer[i].transform;

            if (hitTransform != transform &&
                !hitTransform.IsChildOf(transform))
            {
                foundGround = true;
                break;
            }
        }

        if (foundGround)
        {
            isGrounded = true;

            if (verticalVelocity < 0f)
                verticalVelocity = -2f;
        }
        else
        {
            isGrounded = false;
        }
    }
}