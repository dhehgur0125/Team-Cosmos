using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class MinimapCameraFollow : MonoBehaviour
{
    public Transform player;

    // 플레이어와 미니맵 카메라의 높이 차이
    public float height = 5000f;

    private Camera minimapCam;
    private bool previousFogState;

    void Awake()
    {
        minimapCam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (player == null)
            return;

        transform.position = new Vector3(
            player.position.x,
            height,
            player.position.z
        );

        // 탑다운 수직 90도 고정
        transform.rotation = Quaternion.Euler(
            90f,
            0f,
            0f
        );
    }

    // ============================================================
    // 미니맵 카메라 렌더링 시 안개 끄기 (Built-in 파이프라인)
    // ============================================================

    void OnPreRender()
    {
        previousFogState = RenderSettings.fog;
        RenderSettings.fog = false; // 미니맵 촬영 전 안개 끄기
    }

    void OnPostRender()
    {
        RenderSettings.fog = previousFogState; // 메인 카메라를 위해 안개 복구
    }

    // ============================================================
    // URP (Universal Render Pipeline) 호환 이벤트
    // ============================================================

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam == minimapCam)
        {
            previousFogState = RenderSettings.fog;
            RenderSettings.fog = false;
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam == minimapCam)
        {
            RenderSettings.fog = previousFogState;
        }
    }
}