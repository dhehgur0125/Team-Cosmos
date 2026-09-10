using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class WorldMapCameraController : MonoBehaviour
{
    [Header("타겟 참조")]
    public Transform player;

    [Header("카메라 높이 & 줌 설정 (Perspective용)")]
    public float defaultHeight = 300f;
    public float minHeight = 60f;
    public float maxHeight = 1000f;

    [Header("직교 줌 크기 설정 (Orthographic용)")]
    public float defaultOrthoSize = 100f;
    public float minOrthoSize = 30f;
    public float maxOrthoSize = 400f;

    [Header("조작 감도")]
    public float zoomSensitivity = 30f;
    [Range(0.5f, 3.0f)]
    public float dragSensitivity = 1.0f;

    [Header("맵 핀(Waypoint Pin) 설정")]
    [Tooltip("우클릭 시 생성될 핀 프리팹")]
    public GameObject pinPrefab;
    [Tooltip("지형 충돌 판정을 위한 레이어 마스크")]
    public LayerMask terrainLayer = ~0;
    [Tooltip("동시에 유지할 최대 핀 개수")]
    public int maxPins = 8;
    [Tooltip("우클릭 시 기존 핀 삭제 판정 반경 (미터)")]
    public float pinRemoveRadius = 8f;

    private Camera mapCam;
    private Vector3 lastMousePos;
    private bool isDragging = false;
    private bool isOpen = false;
    private bool previousFogState;

    // 생성된 핀 리스트
    private List<GameObject> activePins = new List<GameObject>();

    void Awake()
    {
        mapCam = GetComponent<Camera>();
        mapCam.enabled = false;
    }

    public void OpenMap()
    {
        isOpen = true;
        mapCam.enabled = true;

        if (player != null)
        {
            float yPos = defaultHeight;
            transform.position = new Vector3(player.position.x, yPos, player.position.z);
        }

        if (mapCam.orthographic)
        {
            mapCam.orthographicSize = defaultOrthoSize;
        }

        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    public void CloseMap()
    {
        isOpen = false;
        mapCam.enabled = false;
        isDragging = false;
    }

    void Update()
    {
        if (!isOpen) return;

        HandleZoom();
        HandleDrag();    // 좌클릭: 지도 드래그
        HandlePinPlacement(); // 우클릭: 핀 설치 및 제거

        // 스페이스바: 플레이어 중심으로 재정렬
        if (Input.GetKeyDown(KeyCode.Space) && player != null)
        {
            transform.position = new Vector3(player.position.x, transform.position.y, player.position.z);
        }
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        if (mapCam.orthographic)
        {
            mapCam.orthographicSize = Mathf.Clamp(
                mapCam.orthographicSize - scroll * zoomSensitivity,
                minOrthoSize,
                maxOrthoSize
            );
        }
        else
        {
            float targetHeight = Mathf.Clamp(
                transform.position.y - scroll * (zoomSensitivity * 5f),
                minHeight,
                maxHeight
            );
            transform.position = new Vector3(transform.position.x, targetHeight, transform.position.z);
        }
    }

    void HandleDrag()
    {
        // 🌟 좌클릭(0)만 지도 이동으로 처리
        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            lastMousePos = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
        }

        if (isDragging)
        {
            Vector3 mouseDelta = Input.mousePosition - lastMousePos;

            if (mouseDelta.sqrMagnitude > 0.0001f)
            {
                float worldFactor = 0f;

                if (mapCam.orthographic)
                {
                    worldFactor = (mapCam.orthographicSize * 2f) / Screen.height;
                }
                else
                {
                    float frustumHeight = 2.0f * transform.position.y * Mathf.Tan(mapCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    worldFactor = frustumHeight / Screen.height;
                }

                Vector3 moveDelta = new Vector3(
                    -mouseDelta.x * worldFactor * dragSensitivity,
                    0f,
                    -mouseDelta.y * worldFactor * dragSensitivity
                );

                transform.position += moveDelta;
                lastMousePos = Input.mousePosition;
            }
        }
    }

    // ============================================================
    // 맵 핀 생성 & 삭제 로직 (우클릭)
    // ============================================================
    void HandlePinPlacement()
    {
        if (Input.GetMouseButtonDown(1)) // 마우스 우클릭
        {
            if (pinPrefab == null)
            {
                Debug.LogWarning("[WorldMap] Pin Prefab이 설정되지 않았습니다.");
                return;
            }

            // 카메라 기준 마우스 화면 위치에서 레이캐스트 발사
            Ray ray = mapCam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 5000f, terrainLayer, QueryTriggerInteraction.Ignore))
            {
                Vector3 clickPoint = hit.point;

                // 1. 클릭 지점 근처에 이미 핀이 있는지 검사 -> 있으면 해당 핀 제거
                GameObject nearbyPin = GetNearestPin(clickPoint, pinRemoveRadius);
                if (nearbyPin != null)
                {
                    activePins.Remove(nearbyPin);
                    Destroy(nearbyPin);
                    return;
                }

                // 2. 최대 설치 개수 도달 시 가장 오래된 핀 순차 제거
                if (activePins.Count >= maxPins)
                {
                    GameObject oldestPin = activePins[0];
                    activePins.RemoveAt(0);
                    if (oldestPin != null) Destroy(oldestPin);
                }

                // 3. 새 핀 생성
                GameObject newPin = Instantiate(pinPrefab, clickPoint, Quaternion.identity);
                activePins.Add(newPin);
            }
        }
    }

    GameObject GetNearestPin(Vector3 point, float maxDist)
    {
        GameObject nearest = null;
        float minDst = maxDist;

        // X, Z 수평 평면 기준 거리 계산
        Vector2 targetPos = new Vector2(point.x, point.z);

        for (int i = activePins.Count - 1; i >= 0; i--)
        {
            if (activePins[i] == null)
            {
                activePins.RemoveAt(i);
                continue;
            }

            Vector3 pinPos = activePins[i].transform.position;
            float dst = Vector2.Distance(targetPos, new Vector2(pinPos.x, pinPos.z));

            if (dst < minDst)
            {
                minDst = dst;
                nearest = activePins[i];
            }
        }

        return nearest;
    }

    // ============================================================
    // 안개(Fog) 일시 끄기 처리
    // ============================================================
    void OnPreRender()
    {
        previousFogState = RenderSettings.fog;
        RenderSettings.fog = false;
    }

    void OnPostRender()
    {
        RenderSettings.fog = previousFogState;
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCam;
        RenderPipelineManager.endCameraRendering += OnEndCam;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCam;
        RenderPipelineManager.endCameraRendering -= OnEndCam;
    }

    private void OnBeginCam(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == mapCam)
        {
            previousFogState = RenderSettings.fog;
            RenderSettings.fog = false;
        }
    }

    private void OnEndCam(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == mapCam)
        {
            RenderSettings.fog = previousFogState;
        }
    }
}