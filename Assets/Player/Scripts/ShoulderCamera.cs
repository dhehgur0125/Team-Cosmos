using UnityEngine;
using Unity.Netcode; // 🌟 [멀티플레이 추가]

public class ShoulderCamera : MonoBehaviour // MonoBehaviour 유지
{
    [Header("Target Settings")]
    [Tooltip("멀티플레이에서는 비워두면 자동으로 로컬 플레이어를 찾습니다.")]
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

    [HideInInspector]
    public bool inputBlocked = false;

    private bool isTargetInitialized = false; // 🌟 타겟 초기화 체크용 플래그

    void Start()
    {
        // 🌟 Start에서는 타겟이 아직 없을 수 있으므로 yaw 초기화는 LateUpdate로 미룹니다.
        currentShoulderX = shoulderOffsetX;
        targetShoulderX = shoulderOffsetX;
        currentDistance = defaultDistance;
        LockCursor();
    }

    void Update()
    {
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
        // 🌟 [멀티플레이 추가] 로컬 플레이어 자동 할당 로직
        if (target == null)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localPlayerObj != null)
                {
                    target = localPlayerObj.transform;
                }
            }
            if (target == null) return;
        }

        // 🌟 타겟을 처음 찾았을 때 카메라 방향을 플레이어 등짝에 맞춤
        if (!isTargetInitialized)
        {
            yaw = target.eulerAngles.y;
            isTargetInitialized = true;
        }

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