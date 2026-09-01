using UnityEngine;

[RequireComponent(typeof(WorldManager))]
public class WorldFogController : MonoBehaviour
{
    private WorldManager worldManager;

    [Header("안개 시각 설정")]
    public Color fogColor = new Color(0.75f, 0.85f, 0.95f, 1f);

    [Header("안개 시작 지점 비율 (0.0 ~ 1.0)")]
    [Tooltip("최대 시야 거리의 몇 % 지점부터 안개가 옅게 끼기 시작할지")]
    [Range(0.3f, 0.9f)]
    public float fogStartRatio = 0.6f;

    void Awake()
    {
        worldManager = GetComponent<WorldManager>();
    }

    void Start()
    {
        ApplyFogSettings();
    }

    // 인스펙터에서 수치를 바꿀 때 에디터에서도 실시간 반영
    void OnValidate()
    {
        if (worldManager == null) worldManager = GetComponent<WorldManager>();
        ApplyFogSettings();
    }

    public void ApplyFogSettings()
    {
        if (worldManager == null) return;

        // 1. 유니티 안개 활성화 및 모드 고정
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;

        // 메인 카메라 배경색도 안개색과 일치시킴 (스카이박스를 안 쓰는 경우)
        if (Camera.main != null && Camera.main.clearFlags == CameraClearFlags.SolidColor)
        {
            Camera.main.backgroundColor = fogColor;
        }

        // 2. 최대 시야 거리 계산 (플레이어 렌더 청크 끝단 거리)
        float maxViewDistance = worldManager.playerRenderDistance * worldManager.chunkSize;

        // 3. Linear 안개의 Start/End 거리 설정
        // Start: 안개가 끼기 시작하는 거리 (예: 전체 거리의 60% 지점)
        // End: 완전히 안개에 덮여 지형 끝부분이 가려지는 거리
        RenderSettings.fogStartDistance = maxViewDistance * fogStartRatio;
        RenderSettings.fogEndDistance = maxViewDistance;

        // 4. 카메라의 Far Clip Plane을 안개 끝거리보다 살짝 여유 있게 설정
        if (Camera.main != null)
        {
            Camera.main.farClipPlane = maxViewDistance * 1.2f;
        }
    }
}