using UnityEngine;

public class MouseLook : MonoBehaviour
{
    [Header("마우스 감도")]
    public float mouseSensitivity = 200f;
    
    [Header("플레이어 몸통 (드래그해서 연결)")]
    public Transform playerBody; 

    private float xRotation = 0f;

    void Start()
    {
        // 게임 시작 시 마우스 커서를 숨기고 화면 중앙에 고정
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        // 마우스 이동량 받아오기
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // 상하 시점 회전 (카메라 자체만 고개를 숙이고 젖힘, 위아래 90도 제한)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f); 
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // 좌우 시점 회전 (플레이어 몸통 전체를 좌우로 회전시킴)
        playerBody.Rotate(Vector3.up * mouseX);
    }
}