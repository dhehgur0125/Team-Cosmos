using UnityEngine;

/// <summary>
/// 1인칭 마우스 시점 스크립트.
///  - 마우스 좌우: playerBody(플레이어 몸통) 전체를 회전
///  - 마우스 상하: 카메라만 고개를 숙이고 젖힘
/// 카메라가 playerBody의 자식이면 기존처럼 로컬 회전만 적용하고,
/// 자식이 아니면(씬에 따로 놓여 있으면) 매 프레임 플레이어의 눈 위치로 따라붙는다.
/// </summary>
public class MouseLook : MonoBehaviour
{
    [Header("마우스 감도")]
    [Tooltip("마우스를 한 단위 움직일 때 회전하는 각도(도). 프레임 속도와 관계없이 일정합니다.")]
    public float mouseSensitivity = 3f;

    [Header("플레이어 몸통 (드래그해서 연결)")]
    public Transform playerBody;

    [Header("카메라 위치 (플레이어의 자식이 아닐 때만 사용)")]
    [Tooltip("플레이어 몸통 기준 눈 위치입니다. 몸통이 큐브처럼 큰 모양이라면 안쪽에 두어 몸통이 시야를 가리지 않게 합니다.")]
    public Vector3 eyeOffset = new Vector3(0f, 0.4f, 0f);

    [Header("상하 시점 제한(도)")]
    public float minPitch = -90f;
    public float maxPitch = 90f;

    private float xRotation = 0f;
    private bool isChildOfBody;

    void Start()
    {
        // 연결을 깜빡했을 때는 부모를 몸통으로 사용
        if (playerBody == null) playerBody = transform.parent;
        isChildOfBody = playerBody != null && transform.IsChildOf(playerBody);

        if (playerBody == null)
            Debug.LogWarning("[MouseLook] playerBody가 연결되지 않았습니다. 인스펙터에서 플레이어를 연결해 주세요.", this);

        // 게임 시작 시 마우스 커서를 숨기고 화면 중앙에 고정
        LockCursor();
    }

    void Update()
    {
        // Esc: 커서 해제 / 게임 화면 클릭: 다시 고정
        if (Input.GetKeyDown(KeyCode.Escape)) UnlockCursor();
        else if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)) LockCursor();

        if (playerBody == null || Cursor.lockState != CursorLockMode.Locked) return;

        // 마우스 이동량은 이미 프레임당 변화량이라 Time.deltaTime을 곱하지 않는다.
        // (곱하면 프레임 속도에 따라 감도가 달라져 시점이 불규칙하게 느낀다)
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // 상하 시점 회전 (카메라만 고개를 숙이고 젖힘)
        xRotation = Mathf.Clamp(xRotation - mouseY, minPitch, maxPitch);

        // 좌우 시점 회전 (플레이어 몸통 전체를 좌우로 회전시킴)
        playerBody.Rotate(Vector3.up * mouseX);
    }

    // PlayerMovement가 Update에서 이동을 끝낸 뒤에 카메라를 맞춰야 떨림이 없다.
    void LateUpdate()
    {
        if (playerBody == null) return;

        Quaternion pitch = Quaternion.Euler(xRotation, 0f, 0f);

        if (isChildOfBody)
        {
            transform.localRotation = pitch;
        }
        else
        {
            transform.SetPositionAndRotation(
                playerBody.position + playerBody.rotation * eyeOffset,
                playerBody.rotation * pitch);
        }
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}