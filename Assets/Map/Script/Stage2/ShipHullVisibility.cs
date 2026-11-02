using UnityEngine;

/// <summary>
/// 우주선 외부 선체 / 발사 격납고 / 조종실 표시 최적화 + 안개 전환.
/// - 카메라가 선체 내부(1~5층)나 5층→조종실 연결 통로 안에 있으면 외부 선체(Exterior, 외부 햇빛 포함)를 꺼서 보이지 않는 정점을 그리지 않는다.
/// - 격납고(Hangar)는 1~2층 높이에서만 그린다(연결 통로에서 들여다볼 수 있도록).
/// - 조종실은 5층 높이에서만 그린다(연결 통로 끝에서 보이도록).
/// - 격납고 발사구 쪽, 조종실 안, 선체 밖에서는 실내용 짙은 안개를 걷어 우주와 선체가 보이게 한다.
/// 판정은 interval 초마다 한 번만 하고 상태가 바뀔 때만 SetActive 하므로 매 프레임 비용과 GC 할당이 없다.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class ShipHullVisibility : MonoBehaviour
{
    [Tooltip("외부 선체 루트 (합친 스테이션 메시 + 우주 배경 + 외부 햇빛)")]
    public GameObject exterior;
    [Tooltip("외부 선체 햇빛 (그림자 없음). 게임 중에는 exterior 와 함께 꺼지고, 에디터에서는 씬 카메라가 선체 안에 있을 때만 끈다")]
    public Light[] exteriorLights;
    [Tooltip("발사 격납고 시각 요소 루트 (Visual / Lights) - 콜라이더는 항상 유지")]
    public GameObject[] hangarToggles;
    [Tooltip("발사 격납고 소품 렌더러 (콜라이더는 유지하고 그리기만 끔)")]
    public Renderer[] hangarPropRenderers;

    [Tooltip("선체 내부 공간 (월드 좌표)")]
    public Bounds interiorBounds = new Bounds(new Vector3(0.25f, 18f, -3.5f), new Vector3(146f, 37.4f, 125.6f));
    [Tooltip("발사 격납고 내부 공간 (월드 좌표)")]
    public Bounds hangarBounds = new Bounds(new Vector3(41f, 8f, 80f), new Vector3(48f, 16f, 40f));
    [Tooltip("격납 베이 입구(우주 쪽)의 z 좌표 (여기에 가까울수록 안개가 옅어짐)")]
    public float launchMouthZ = -159f;
    [Tooltip("격납고를 그리는 최대 카메라 높이")]
    public float hangarVisibleMaxY = 12f;

    [Header("Cockpit (조종실, 5층과 연결)")]
    [Tooltip("조종실 시각 요소 루트 (Visual / Lights) - 콜라이더는 항상 유지")]
    public GameObject[] cockpitToggles;
    public Renderer[] cockpitPropRenderers;
    [Tooltip("조종실 방 공간 (월드 좌표)")]
    public Bounds cockpitBounds;
    [Tooltip("5층 → 조종실 연결 통로 공간 (월드 좌표). 이 안에서는 외부 선체를 그리지 않는다")]
    public Bounds tunnelBounds;
    [Tooltip("이 높이 범위의 카메라에서만 조종실을 그림 (5층)")]
    public Vector2 cockpitVisibleY = new Vector2(28f, 42f);
    [Tooltip("조종실 안 안개 (0 = 실내 안개, 1 = 우주)")]
    [Range(0f, 1f)] public float cockpitFogBlend = 0.85f;

    [Header("Fog")]
    public bool controlFog = true;
    [Tooltip("선체 밖/발사구 앞 안개 농도")]
    public float exteriorFogDensity = 0.0015f;
    [Tooltip("발사구로부터 이 거리 안에서 안개를 서서히 바꾼다")]
    public float fogBlendDistance = 60f;

    [Min(0.05f)] public float interval = 0.2f;
    [Tooltip("비워 두면 Camera.main 사용")]
    public Camera targetCamera;

    float _timer;
    float _interiorFog;
    int _state = -1; // bit0: exterior on, bit1: hangar on, bit2: cockpit on

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        _interiorFog = RenderSettings.fogDensity;
        _state = -1;
        _timer = 0f;
    }

    void OnDisable()
    {
        if (!Application.isPlaying) return;
        if (controlFog) RenderSettings.fogDensity = _interiorFog;
        SetActive(exterior, true);
        if (hangarToggles != null) foreach (var g in hangarToggles) SetActive(g, true);
        SetRenderers(hangarPropRenderers, true);
        if (cockpitToggles != null) foreach (var g in cockpitToggles) SetActive(g, true);
        SetRenderers(cockpitPropRenderers, true);
    }

    void Update()
    {
        if (!Application.isPlaying) { EditorUpdate(); return; }
        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = interval;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;
        Vector3 p = cam.transform.position;

        bool inHangar = hangarBounds.Contains(p);
        bool inCockpit = !inHangar && cockpitBounds.size.sqrMagnitude > 0f && cockpitBounds.Contains(p);
        bool inTunnel = !inHangar && !inCockpit && tunnelBounds.size.sqrMagnitude > 0f && tunnelBounds.Contains(p);
        bool inInterior = !inHangar && !inCockpit && !inTunnel && interiorBounds.Contains(p);
        bool exteriorOn = !inInterior && !inTunnel;
        bool hangarOn = !inCockpit && !inTunnel && (!inInterior || p.y < hangarVisibleMaxY);   // 조종실/통로에서는 격납고가 보이지 않음
        bool cockpitOn = inCockpit || inTunnel || (!inInterior && !inHangar) || (p.y > cockpitVisibleY.x && p.y < cockpitVisibleY.y);

        int state = (exteriorOn ? 1 : 0) | (hangarOn ? 2 : 0) | (cockpitOn ? 4 : 0);
        if (state != _state)
        {
            _state = state;
            SetActive(exterior, exteriorOn);
            if (exteriorLights != null) foreach (var l in exteriorLights) if (l != null && l.enabled != exteriorOn) l.enabled = exteriorOn;
            if (hangarToggles != null) foreach (var g in hangarToggles) SetActive(g, hangarOn);
            SetRenderers(hangarPropRenderers, hangarOn);
            if (cockpitToggles != null) foreach (var g in cockpitToggles) SetActive(g, cockpitOn);
            SetRenderers(cockpitPropRenderers, cockpitOn);
        }

        if (controlFog)
        {
            float t; // 0 = 실내 안개, 1 = 우주
            if (inInterior || inTunnel) t = 0f;
            else if (inHangar) t = Mathf.Clamp01(1f - Mathf.Abs(launchMouthZ - p.z) / Mathf.Max(1f, fogBlendDistance)) * 0.85f;
            else if (inCockpit) t = cockpitFogBlend;
            else t = 1f;
            float target = Mathf.Lerp(_interiorFog, exteriorFogDensity, t);
            if (!Mathf.Approximately(RenderSettings.fogDensity, target)) RenderSettings.fogDensity = target;
        }
    }

    /// <summary>에디터(플레이 전): 씬 뷰 카메라가 선체 안이면 외부 햇빛만 꺼서 실내 조명이 바뀌어 보이지 않게 한다.</summary>
    void EditorUpdate()
    {
#if UNITY_EDITOR
        var sv = UnityEditor.SceneView.lastActiveSceneView;
        if (sv == null || sv.camera == null || exteriorLights == null) return;
        Vector3 p = sv.camera.transform.position;
        bool inside = interiorBounds.Contains(p) || (tunnelBounds.size.sqrMagnitude > 0f && tunnelBounds.Contains(p));
        foreach (var l in exteriorLights) if (l != null && l.enabled == inside) l.enabled = !inside;
#endif
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
