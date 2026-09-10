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
    public float defaultDistance = 2.0f;
    public float zoomSpeed = 4f;
    public float minDistance = 0.8f;
    public float maxDistance = 4.0f;
    private float currentDistance;

    [Header("Wall Collision")]
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.25f;
    public float collisionBuffer = 0.15f;
    public float minCollisionDistance = 0.2f;

    [Header("View State")]
    public bool isFirstPerson = false;
    private float pitch = 10f;
    private float yaw = 0f;
    public float GetYaw() => yaw;

    // 🌟 월드맵 등 UI 열림 시 카메라 입력 및 커서 재잠금 차단 플래그
    [HideInInspector]
    public bool inputBlocked = false;

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
        // 월드맵이 켜져 있을 땐 마우스 클릭 시 커서 재잠금 방지 & 카메라 입력 무시
        if (inputBlocked) return;

        if (Input.GetKeyDown(KeyCode.Escape)) UnlockCursor();
        if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)) LockCursor();

        if (!isFirstPerson)
        {
            if (Input.GetKeyDown(KeyCode.Q)) targetShoulderX = -Mathf.Abs(shoulderOffsetX);
            if (Input.GetKeyDown(KeyCode.E)) targetShoulderX = Mathf.Abs(shoulderOffsetX);

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

        // 입력이 차단되지 않고 커서가 잠겨 있을 때만 마우스 회전 반영
        if (!inputBlocked && Cursor.lockState == CursorLockMode.Locked)
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

        Vector3 pivotOffset = camRotation * new Vector3(currentShoulderX, shoulderOffsetY, 0f);
        Vector3 pivotPosition = target.position + pivotOffset;
        Vector3 armDirection = camRotation * Vector3.back;
        float finalDistance = currentDistance;

        if (Physics.SphereCast(pivotPosition, collisionRadius, armDirection, out RaycastHit hit, currentDistance, collisionMask, QueryTriggerInteraction.Ignore))
        {
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