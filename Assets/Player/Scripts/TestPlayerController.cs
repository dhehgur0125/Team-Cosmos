using UnityEngine;
using Unity.Netcode; // 🌟 [멀티플레이 추가]

[RequireComponent(typeof(CharacterController))]
// 🌟 [멀티플레이 추가] MonoBehaviour -> NetworkBehaviour 변경
public class TestPlayerController : NetworkBehaviour 
{
    private Animator animator;
    private CharacterController controller;

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

    [Header("Ground Check (GC-free)")]
    private RaycastHit[] groundHitsBuffer = new RaycastHit[8];

    private Quaternion lastStableRotation;

    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        lastStableRotation = transform.rotation;

        if (animator != null)
        {
            animator.applyRootMotion = false;
        }
    }

    // 🌟 [멀티플레이 추가] 플레이어가 스폰될 때 실행되는 네트워크 전용 콜백
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            if (Camera.main != null)
            {
                shoulderCam = Camera.main.GetComponent<ShoulderCamera>();
            }
        }

        // 🌟 [추가] 오직 서버(호스트)에서만 스폰 위치의 Y축을 강제로 높여줌
        if (IsServer)
        {
            Vector3 spawnPos = transform.position;
            spawnPos.y += 5.0f; // 원하는 만큼 높이 추가 (예: 5 미터 위)
            transform.position = spawnPos;
        }
    }

    void Update()
    {
        // 🌟 [멀티플레이 핵심] 내 캐릭터가 아니면 입력을 받지 않고 그냥 빠져나감 (무시)
        if (!IsOwner) return;

        UpdateGroundedState();

        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            isGrounded = false;
            lastStableRotation = transform.rotation;

            if (animator != null)
            {
                animator.ResetTrigger("Jump");
                animator.SetTrigger("Jump");
            }
        }

        if (!isGrounded)
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(h, 0, v).normalized;
        bool isMoving = inputDir.magnitude > 0;
        bool isRunning = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && isMoving;

        float targetAnimSpeed = 0f;
        float moveSpeed = 0f;
        Vector3 moveDir = Vector3.zero;

        if (isMoving && shoulderCam != null)
        {
            targetAnimSpeed = isRunning ? 1.0f : 0.5f;
            moveSpeed = isRunning ? runSpeed : walkSpeed;

            float camYaw = shoulderCam.GetYaw();
            Quaternion camYawRotation = Quaternion.Euler(0f, camYaw, 0f);
            moveDir = camYawRotation * inputDir;

            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            lastStableRotation = transform.rotation;
        }
        else
        {
            transform.rotation = lastStableRotation;
            moveDir = Vector3.zero;
        }

        Vector3 finalVelocity = (moveDir * moveSpeed) + (Vector3.up * verticalVelocity);
        controller.Move(finalVelocity * Time.deltaTime);

        if (animator != null)
        {
            animator.SetFloat("Speed", targetAnimSpeed, 0.15f, Time.deltaTime);
        }
    }

    private void UpdateGroundedState()
    {
        if (verticalVelocity > 0.1f)
        {
            isGrounded = false;
            return;
        }

        Vector3 rayOrigin = transform.position + Vector3.up * 0.15f;
        float rayDistance = 0.25f;

        int hitCount = Physics.RaycastNonAlloc(
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
            Transform hitTransform = groundHitsBuffer[i].transform;
            if (hitTransform != transform && !hitTransform.IsChildOf(transform))
            {
                foundGround = true;
                break;
            }
        }

        if (foundGround)
        {
            isGrounded = true;
            if (verticalVelocity < 0)
            {
                verticalVelocity = -2f;
            }
        }
        else
        {
            isGrounded = false;
        }
    }
}