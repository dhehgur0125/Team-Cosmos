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
        // 자신의 캐릭터만 카메라를 연결
        if (IsOwner)
        {
            if (Camera.main != null)
                shoulderCam = Camera.main.GetComponent<ShoulderCamera>();
        }

        // 서버에서 스폰 위치를 위로 이동
        if (IsServer)
        {
            Vector3 spawnPos = transform.position;
            spawnPos.y += 5f;

            transform.position = spawnPos;
        }
    }

    private void Update()
    {
        // 다른 플레이어 캐릭터의 입력은 받지 않음
        if (!IsOwner)
            return;

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