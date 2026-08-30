using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class TestPlayerController : MonoBehaviour
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
    // Physics.RaycastAll은 호출할 때마다 배열을 새로 할당해서 GC 스파이크를 유발합니다.
    // 미리 할당해둔 버퍼에 결과를 채우는 RaycastNonAlloc으로 대체했습니다.
    private RaycastHit[] groundHitsBuffer = new RaycastHit[8];

    private Quaternion lastStableRotation;

    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        lastStableRotation = transform.rotation;

        // 코드에서도 강제로 꺼서, 실수로 Inspector에서 다시 켜지는 것을 방지합니다.
        // CharacterController.Move()로 이동/점프를 전부 담당하고 있으므로
        // 애니메이션은 위치를 절대 건드리지 않아야 카메라가 여러 번 점프하는 것처럼 튀지 않습니다.
        if (animator != null)
        {
            animator.applyRootMotion = false;
        }

        if (shoulderCam == null && Camera.main != null)
        {
            shoulderCam = Camera.main.GetComponent<ShoulderCamera>();
        }
    }

    void Update()
    {
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

        // NonAlloc 버전: 매 프레임 배열을 새로 할당하지 않아 GC 압박이 없습니다.
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
                // -4f는 다소 과했던 값이라 -2f로 완화했습니다. (지면 밀착 목적이면 이 정도로 충분)
                verticalVelocity = -2f;
            }
        }
        else
        {
            isGrounded = false;
        }
    }
}