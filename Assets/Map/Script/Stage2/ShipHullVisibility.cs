using UnityEngine;

/// <summary>
/// 우주선 외부 선체 / 발사 격납고 표시 최적화 + 안개 전환.
/// - 카메라가 선체 내부(격납고 제외)에 있으면 외부 선체(Exterior)를 꺼서 보이지 않는 정점을 그리지 않는다.
/// - 격납고(Hangar)는 1~2층 높이에서만 그린다(연결 통로에서 들여다볼 수 있도록).
/// - 격납고 발사구 쪽으로 갈수록, 그리고 선체 밖에서는 실내용 짙은 안개를 걷어 우주와 행성이 보이게 한다.
/// 판정은 interval 초마다 한 번만 하고 상태가 바뀔 때만 SetActive 하므로 매 프레임 비용과 GC 할당이 없다.
/// </summary>
[DisallowMultipleComponent]
public class ShipHullVisibility : MonoBehaviour
{
    [Tooltip("외부 선체 루트 (병합 메시 + 우주 배경)")]
    public GameObject exterior;
    [Tooltip("발사 격납고 시각 요소 루트 (Visual / Lights) - 콜라이더는 항상 유지")]
    public GameObject[] hangarToggles;
    [Tooltip("발사 격납고 소품 렌더러 (콜라이더는 유지하고 그리기만 끔)")]
    public Renderer[] hangarPropRenderers;

    [Tooltip("선체 내부 공간 (월드 좌표)")]
    public Bounds interiorBounds = new Bounds(new Vector3(0.25f, 18f, -3.5f), new Vector3(146f, 37.4f, 125.6f));
    [Tooltip("발사 격납고 내부 공간 (월드 좌표)")]
    public Bounds hangarBounds = new Bounds(new Vector3(41f, 8f, 80f), new Vector3(48f, 16f, 40f));
    [Tooltip("격납고 발사구의 z 좌표 (여기에 가까울수록 안개가 옅어짐)")]
    public float launchMouthZ = 100f;
    [Tooltip("격납고를 그리는 최대 카메라 높이")]
    public float hangarVisibleMaxY = 12f;

    [Header("Cockpit (선수 조종실)")]
    [Tooltip("조종실 시각 요소 루트 (Visual / Interior / Lights) - 콜라이더는 항상 유지")]
    public GameObject[] cockpitToggles;
    public Renderer[] cockpitPropRenderers;
    [Tooltip("조종실 + 선체를 지나는 연결 통로 공간 (월드 좌표)")]
    public Bounds cockpitBounds;
    [Tooltip("조종실 뒤쪽 출입벽 x (여기서부터 캐노피 쪽으로 갈수록 안개가 옅어짐)")]
    public float cockpitBackX = -80.5f;
    public float cockpitFrontX = -118f;
    [Tooltip("이 높이 범위의 카메라에서만 조종실을 그림 (2~4층)")]
    public Vector2 cockpitVisibleY = new Vector2(8f, 30f);

    [Header("Fog")]
    public bool controlFog = true;
    [Tooltip("선체 밖/발사구 앞 안개 농도")]
    public float exteriorFogDensity = 0.0015f;
    [Tooltip("발사구로부터 이 거리 안에서 안개를 서서히 바꾼다")]
    public float fogBlendDistance = 30f;

    [Min(0.05f)] public float interval = 0.2f;
    [Tooltip("비워 두면 Camera.main 사용")]
    public Camera targetCamera;

    float _timer;
    float _interiorFog;
    int _state = -1; // bit0: exterior on, bit1: hangar on

    void OnEnable()
    {
        _interiorFog = RenderSettings.fogDensity;
        _state = -1;
        _timer = 0f;
    }

    void OnDisable()
    {
        if (controlFog) RenderSettings.fogDensity = _interiorFog;
        SetActive(exterior, true);
        if (hangarToggles != null) foreach (var g in hangarToggles) SetActive(g, true);
        SetRenderers(hangarPropRenderers, true);
        if (cockpitToggles != null) foreach (var g in cockpitToggles) SetActive(g, true);
        SetRenderers(cockpitPropRenderers, true);
    }

    void Update()
    {
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = interval;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;
        Vector3 p = cam.transform.position;

        bool inHangar = hangarBounds.Contains(p);
        bool inCockpit = !inHangar && cockpitBounds.size.sqrMagnitude > 0f && cockpitBounds.Contains(p);
        bool inInterior = !inHangar && !inCockpit && interiorBounds.Contains(p);
        bool exteriorOn = !inInterior;
        bool hangarOn = !inCockpit && (!inInterior || p.y < hangarVisibleMaxY);   // 조종실에서는 격납고가 보이지 않음
        bool cockpitOn = !inInterior || (p.y > cockpitVisibleY.x && p.y < cockpitVisibleY.y);

        int state = (exteriorOn ? 1 : 0) | (hangarOn ? 2 : 0) | (cockpitOn ? 4 : 0);
        if (state != _state)
        {
            _state = state;
            SetActive(exterior, exteriorOn);
            if (hangarToggles != null) foreach (var g in hangarToggles) SetActive(g, hangarOn);
            SetRenderers(hangarPropRenderers, hangarOn);
            if (cockpitToggles != null) foreach (var g in cockpitToggles) SetActive(g, cockpitOn);
            SetRenderers(cockpitPropRenderers, cockpitOn);
        }

        if (controlFog)
        {
            float t; // 0 = 실내 안개, 1 = 우주
            if (inInterior) t = 0f;
            else if (inHangar) t = Mathf.Clamp01(1f - (launchMouthZ - p.z) / Mathf.Max(1f, fogBlendDistance)) * 0.85f;
            else if (inCockpit) t = Mathf.Clamp01((cockpitBackX - p.x) / Mathf.Max(1f, cockpitBackX - cockpitFrontX)) * 0.85f;
            else t = 1f;
            float target = Mathf.Lerp(_interiorFog, exteriorFogDensity, t);
            if (!Mathf.Approximately(RenderSettings.fogDensity, target)) RenderSettings.fogDensity = target;
        }
    }

    static void SetRenderers(Renderer[] list, bool on)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
        {
            Renderer r = list[i];
            if (r != null && r.enabled != on) r.enabled = on;
        }
    }

    static void SetActive(GameObject g, bool on)
    {
        if (g != null && g.activeSelf != on) g.SetActive(on);
    }
}
