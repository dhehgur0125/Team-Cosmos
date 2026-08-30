using UnityEngine;

public class ShoulderCamera : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target;                  // 플레이어 최상위 루트 오브젝트

    [Header("Camera Offsets")]
    public float shoulderOffsetX = 0.55f;
    public float shoulderOffsetY = 1.4f;
    public Vector3 firstPersonOffset = new Vector3(0f, 1.5f, 0.05f);

    [Header("Shoulder Switch Settings")]
    public float switchSpeed = 12f;
    private float currentShoulderX = 0.55f;
    private float targetShoulderX = 0.55f;

    [Header("Mouse & Angle Limits")]
    public float mouseSensitivity = 2.0f;
    public float minPitch = -25f;
    public float maxPitch = 40f;

    [Header("Zoom Settings")]
    public float defaultDistance = 2.0f;       // 시작 거리 (기존 shoulderOffsetZ의 절대값과 같은 개념)
    public float zoomSpeed = 4f;
    public float minDistance = 0.8f;
    public float maxDistance = 4.0f;
    private float currentDistance;

    [Header("Wall Collision")]
    // 카메라가 충돌 검사할 레이어. Player(캐릭터 자신) 레이어는 반드시 빼주세요.
    // 안 빼면 카메라가 캐릭터 자기 몸에 부딪혀서 계속 확 당겨옵니다.
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.25f;      // SphereCast 반지름 (카메라 두께라고 생각하면 됨)
    public float collisionBuffer = 0.15f;      // 벽에서 카메라를 얼마나 띄워둘지
    public float minCollisionDistance = 0.2f;  // 벽에 딱 붙었을 때 최소 거리 (피벗 뚫고 들어가는 것 방지)

    [Header("View State")]
    public bool isFirstPerson = false;
    private float pitch = 10f;
    private float yaw = 0f;
    public float GetYaw() => yaw;

    void Start()
    {
        if (target != null)
        {
            yaw = target.eulerAngles.y;
        }
        currentShoulderX = shoulderOffsetX;
        targetShoulderX = shoulderOffsetX;
        currentDistance = defaultDistance;
        LockCursor();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) UnlockCursor();
        if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)) LockCursor();

        if (!isFirstPerson)
        {
            if (Input.GetKeyDown(KeyCode.Q)) targetShoulderX = -Mathf.Abs(shoulderOffsetX);
            if (Input.GetKeyDown(KeyCode.E)) targetShoulderX = Mathf.Abs(shoulderOffsetX);

            // 마우스 휠 줌: 휠을 위로 올리면(양수) 가까워지고, 내리면 멀어짐
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                currentDistance -= scroll * zoomSpeed;
                currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
            }
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            isFirstPerson = !isFirstPerson;
            UpdateMeshVisibility();
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        Quaternion camRotation = Quaternion.Euler(pitch, yaw, 0f);
        currentShoulderX = Mathf.Lerp(currentShoulderX, targetShoulderX, switchSpeed * Time.deltaTime);

        if (isFirstPerson)
        {
            transform.position = target.position + (camRotation * firstPersonOffset);
            transform.rotation = camRotation;
            return;
        }

        // 피벗(어깨 지점): 캐릭터 기준 좌우/높이 오프셋까지만 적용한 지점.
        // 여기서부터 카메라 방향으로 팔(arm)을 뻗는다고 생각하면 됩니다.
        Vector3 pivotOffset = camRotation * new Vector3(currentShoulderX, shoulderOffsetY, 0f);
        Vector3 pivotPosition = target.position + pivotOffset;

        // 카메라가 뒤로 빠지는 방향 (로컬 -Z를 월드로 변환)
        Vector3 armDirection = camRotation * Vector3.back;

        float finalDistance = currentDistance;

        // 피벗에서 카메라 방향으로 SphereCast를 쏴서 중간에 벽이 있는지 검사
        if (Physics.SphereCast(pivotPosition, collisionRadius, armDirection, out RaycastHit hit, currentDistance, collisionMask, QueryTriggerInteraction.Ignore))
        {
            // 캐릭터 자신의 콜라이더는 무시 (collisionMask에서 Player 레이어를 뺐다면 애초에 안 걸리지만, 이중 안전장치)
            if (hit.transform != target && !hit.transform.IsChildOf(target))
            {
                finalDistance = Mathf.Clamp(hit.distance - collisionBuffer, minCollisionDistance, currentDistance);
            }
        }

        transform.position = pivotPosition + armDirection * finalDistance;
        transform.rotation = camRotation;
    }

    public void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void UpdateMeshVisibility()
    {
        if (target == null) return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            r.enabled = !isFirstPerson;
        }
    }
}