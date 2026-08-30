using UnityEngine;
public class move : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 200f;
    public Transform playerCamera;

    private CharacterController controller;
    private float verticalVelocity = 0f;
    private float xRotation = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        // 마우스 커서 숨기기
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        Move();
        MouseLook();
    }

    void Move()
    {
        // WASD 입력
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        // 플레이어가 바라보는 방향 기준으로 이동
        Vector3 move = transform.right * horizontal +
                       transform.forward * vertical;

        // 대각선 이동 속도 보정
        if (move.magnitude > 1f)
        {
            move.Normalize();
        }

        


        move.y = verticalVelocity;

        controller.Move(move * moveSpeed * Time.deltaTime);
    }

    void MouseLook()
    {
        // 마우스 움직임
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // 위아래 회전
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        playerCamera.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // 좌우 회전
        transform.Rotate(Vector3.up * mouseX);
    }
}


