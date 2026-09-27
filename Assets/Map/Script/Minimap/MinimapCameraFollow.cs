using UnityEngine;
using UnityEngine.Rendering;
using Unity.Netcode; // 🌟 [멀티플레이 변경] Netcode 네임스페이스 추가

[RequireComponent(typeof(Camera))]
public class MinimapCameraFollow : MonoBehaviour // MonoBehaviour 유지
{
    [Tooltip("멀티플레이 환경에서는 비워두면 자동으로 로컬 플레이어를 찾습니다.")]
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
        // 🌟 [멀티플레이 변경] 플레이어가 할당되지 않았다면 로컬 플레이어를 자동 검색
        if (player == null)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localPlayerObj != null)
                {
                    player = localPlayerObj.transform;
                }
            }
            
            // 아직 로컬 플레이어가 스폰되지 않았다면 리턴
            if (player == null) return; 
        }

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