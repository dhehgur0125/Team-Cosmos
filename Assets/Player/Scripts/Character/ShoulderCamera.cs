/*
    [ 코드 설명 ]

    플레이어의 1인칭 / 3인칭 카메라를 담당하는 스크립트입니다.

    마우스 이동
    → 카메라 회전

    마우스 휠
    → 3인칭 카메라 거리 조절

    Q / E
    → 왼쪽 / 오른쪽 어깨 시점 변경

    V
    → 1인칭 / 3인칭 전환

    우클릭 유지
    → GunAim의 Shoulder 상태
    → 3인칭 카메라가 가까워지고 FOV 감소

    우클릭 빠르게 두 번
    → GunAim의 Precision 상태
    → 강제로 1인칭 정밀 조준 시점
    → FOV 크게 감소

    정밀 조준이 끝나면
    → 정밀 조준 전 사용하던 1인칭 / 3인칭 상태로 돌아갑니다.
*/

using UnityEngine;
using Unity.Netcode;

public class ShoulderCamera : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("멀티플레이에서는 비워두면 자동으로 로컬 플레이어를 찾습니다.")]
    public Transform target;

    [Header("Camera Offsets")]
    public float shoulderOffsetX = 0.55f;
    public float shoulderOffsetY = 1.4f;

    public Vector3 firstPersonOffset =
        new Vector3(0f, 1.5f, 0.05f);

    [Header("Shoulder Switch")]
    public float switchSpeed = 12f;

    private float currentShoulderX;
    private float targetShoulderX;

    [Header("Mouse & Angle Limits")]
    public float mouseSensitivity = 2f;
    public float minPitch = -25f;
    public float maxPitch = 40f;

    [Header("Zoom Settings")]
    public float defaultDistance = 2f;
    public float zoomSpeed = 4f;
    public float minDistance = 0.8f;
    public float maxDistance = 4f;

    private float currentDistance;
    private float displayDistance;

    [Header("Aim Camera")]
    [SerializeField] private float shoulderAimDistance = 1.2f;
    [SerializeField] private float shoulderAimFov = 50f;
    [SerializeField] private float precisionAimFov = 35f;
    [SerializeField] private float aimTransitionSpeed = 10f;

    [Header("Wall Collision")]
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.25f;
    public float collisionBuffer = 0.15f;
    public float minCollisionDistance = 0.2f;

    [Header("First Person Visibility")]
    public LayerMask firstPersonVisibleLayers;

    [Header("View State")]
    public bool isFirstPerson = false;

    [HideInInspector]
    public bool inputBlocked = false;

    private Camera viewCamera;
    private GunAim gunAim;

    private float normalFov;
    private float pitch = 10f;
    private float yaw;

    private bool isTargetInitialized;

    private bool visibilityInitialized;
    private bool lastFirstPersonState;

    public float GetYaw() => yaw;

    private void Start()
    {
        viewCamera = GetComponent<Camera>();

        if (viewCamera != null)
            normalFov = viewCamera.fieldOfView;

        currentShoulderX = shoulderOffsetX;
        targetShoulderX = shoulderOffsetX;

        currentDistance = defaultDistance;
        displayDistance = defaultDistance;

        LockCursor();
    }

    private void Update()
    {
        if (inputBlocked)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
            UnlockCursor();

        if (Cursor.lockState != CursorLockMode.Locked &&
            Input.GetMouseButtonDown(0))
        {
            LockCursor();
        }

        // 정밀 조준 중에는 V 시점 변경 방지
        bool precisionAim =
            gunAim != null &&
            gunAim.CurrentMode == GunAim.AimMode.Precision;

        if (Input.GetKeyDown(KeyCode.V) && !precisionAim)
            isFirstPerson = !isFirstPerson;

        if (!isFirstPerson && !precisionAim)
        {
            if (Input.GetKeyDown(KeyCode.Q))
                targetShoulderX = -Mathf.Abs(shoulderOffsetX);

            if (Input.GetKeyDown(KeyCode.E))
                targetShoulderX = Mathf.Abs(shoulderOffsetX);

            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (Mathf.Abs(scroll) > 0.0001f)
            {
                currentDistance -= scroll * zoomSpeed;

                currentDistance = Mathf.Clamp(
                    currentDistance,
                    minDistance,
                    maxDistance
                );
            }
        }
    }

    private void LateUpdate()
    {
        FindLocalPlayer();

        if (target == null)
            return;

        FindGunAim();

        if (!isTargetInitialized)
        {
            yaw = target.eulerAngles.y;
            isTargetInitialized = true;
        }

        UpdateRotation();

        GunAim.AimMode aimMode =
            gunAim != null
                ? gunAim.CurrentMode
                : GunAim.AimMode.Hip;

        bool precisionAim =
            aimMode == GunAim.AimMode.Precision;

        bool useFirstPerson =
            isFirstPerson || precisionAim;

        UpdateVisibility(useFirstPerson);
        UpdateFov(aimMode);

        Quaternion cameraRotation =
            Quaternion.Euler(pitch, yaw, 0f);

        if (useFirstPerson)
        {
            UpdateFirstPerson(cameraRotation);
            return;
        }

        UpdateThirdPerson(cameraRotation, aimMode);
    }

    private void FindLocalPlayer()
    {
        if (target != null)
            return;

        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsConnectedClient)
        {
            return;
        }

        NetworkObject player =
            NetworkManager.Singleton.LocalClient.PlayerObject;

        if (player != null)
            target = player.transform;
    }

    private void FindGunAim()
    {
        if (target == null)
            return;

        if (gunAim == null ||
            gunAim.transform != target)
        {
            gunAim = target.GetComponent<GunAim>();
        }
    }

    private void UpdateRotation()
    {
        if (inputBlocked ||
            Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;

        pitch = Mathf.Clamp(
            pitch,
            minPitch,
            maxPitch
        );
    }

    private void UpdateFov(GunAim.AimMode aimMode)
    {
        if (viewCamera == null)
            return;

        float targetFov = normalFov;

        if (aimMode == GunAim.AimMode.Shoulder)
            targetFov = shoulderAimFov;

        else if (aimMode == GunAim.AimMode.Precision)
            targetFov = precisionAimFov;

        viewCamera.fieldOfView = Mathf.Lerp(
            viewCamera.fieldOfView,
            targetFov,
            aimTransitionSpeed * Time.deltaTime
        );
    }

    private void UpdateFirstPerson(
        Quaternion cameraRotation)
    {
        transform.position =
            target.position +
            cameraRotation * firstPersonOffset;

        transform.rotation = cameraRotation;
    }

    private void UpdateThirdPerson(
        Quaternion cameraRotation,
        GunAim.AimMode aimMode)
    {
        currentShoulderX = Mathf.Lerp(
            currentShoulderX,
            targetShoulderX,
            switchSpeed * Time.deltaTime
        );

        float targetDistance =
            aimMode == GunAim.AimMode.Shoulder
                ? Mathf.Min(
                    currentDistance,
                    shoulderAimDistance
                )
                : currentDistance;

        displayDistance = Mathf.Lerp(
            displayDistance,
            targetDistance,
            aimTransitionSpeed * Time.deltaTime
        );

        Vector3 pivotOffset =
            cameraRotation *
            new Vector3(
                currentShoulderX,
                shoulderOffsetY,
                0f
            );

        Vector3 pivotPosition =
            target.position + pivotOffset;

        Vector3 armDirection =
            cameraRotation * Vector3.back;

        float finalDistance = displayDistance;

        if (Physics.SphereCast(
            pivotPosition,
            collisionRadius,
            armDirection,
            out RaycastHit hit,
            displayDistance,
            collisionMask,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.transform != target &&
                !hit.transform.IsChildOf(target))
            {
                finalDistance = Mathf.Clamp(
                    hit.distance - collisionBuffer,
                    minCollisionDistance,
                    displayDistance
                );
            }
        }

        transform.position =
            pivotPosition +
            armDirection * finalDistance;

        transform.rotation = cameraRotation;
    }

    private void UpdateVisibility(bool firstPerson)
    {
        if (visibilityInitialized &&
            firstPerson == lastFirstPersonState)
        {
            return;
        }

        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (IsLayerInMask(
                renderer.gameObject.layer,
                firstPersonVisibleLayers))
            {
                renderer.enabled = true;
                continue;
            }

            renderer.enabled = !firstPerson;
        }

        lastFirstPersonState = firstPerson;
        visibilityInitialized = true;
    }

    private bool IsLayerInMask(
        int layer,
        LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
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
}