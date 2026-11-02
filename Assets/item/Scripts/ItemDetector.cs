using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VoidLast.Monster;

/// <summary>
/// 플레이어에 붙이는 아이템 탐지기 (에코로케이션 방식).
/// 키를 누르면 플레이어에게서 음파가 퍼져 나가며 바닥·벽 표면을 훑고,
/// 파동이 닿은 DetectableItem은 메아리 링과 함께 발광하며 벽·지형 너머에서도 실루엣으로 비쳐 보인다.
/// </summary>
public class ItemDetector : MonoBehaviour
{
    [Header("탐지기 설정")]
    public float detectRadius = 15f; // 감지 반경 (15m)
    public float glowDuration = 3f;  // 발광 유지 시간

    [Tooltip("비워두면(Nothing) 모든 DetectableItem을 감지합니다. 지정하면 해당 레이어의 아이템만 감지합니다.")]
    public LayerMask itemLayer;

    [Tooltip("탐지기 작동 키. Q는 FpsInput의 '아이템 버리기'와 겹쳐서 Z로 변경했습니다.")]
    public KeyCode activateKey = KeyCode.Z;

    [Min(0f)] public float cooldown = 1.5f; // 재사용 대기 시간(초)

    [Header("스캔 파동")]
    [Tooltip("파동이 퍼지는 속도(m/s). 아이템은 파동이 닿는 순간 발광합니다. 0이면 반경 안의 아이템이 동시에 발광합니다.")]
    public float waveSpeed = 12f;

    [Header("몬스터 반응")]
    [Tooltip("스캔 소리가 들리는 반경(m). 이 안에 있는 '눈 없는 몬스터'가 스캔한 위치로 몰려옵니다. 눈 있는 몬스터는 반응하지 않습니다.")]
    public float noiseRadius = 20f;

    [Header("음파(소나) 효과")]
    public bool showWave = true;

    [Tooltip("Custom/SonarWave 셰이더 (표면을 훑는 파동). 카메라의 Depth Texture가 필요합니다.")]
    public Shader sonarShader;

    [Tooltip("Custom/EchoPing 셰이더 (파동이 아이템에 닿을 때 번지는 메아리 링)")]
    public Shader pingShader;

    [ColorUsage(false, true)] public Color waveColor = new Color(0.15f, 0.85f, 1.2f, 1f);
    [Min(0.01f)] public float frontWidth = 0.18f; // 파면(가장 밝은 선)의 두께(m)
    [Min(0.1f)] public float trailLength = 5f;    // 파면 뒤로 잔물결이 이어지는 길이(m)
    [Min(0.1f)] public float ringSpacing = 1.2f;  // 잔물결 간격(m)

    [Header("벽 투과 발광")]
    [Tooltip("Custom/ItemXRayGlow 셰이더. 빌드에 포함되도록 반드시 연결되어 있어야 합니다.")]
    public Shader xrayShader;

    private sealed class Wave
    {
        public Vector3 origin;
        public float startTime;
        public Material material;
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int OriginId = Shader.PropertyToID("_Origin");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int MaxRadiusId = Shader.PropertyToID("_MaxRadius");
    private static readonly int BandWidthId = Shader.PropertyToID("_BandWidth");
    private static readonly int TrailLengthId = Shader.PropertyToID("_TrailLength");
    private static readonly int RingSpacingId = Shader.PropertyToID("_RingSpacing");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private readonly List<Wave> waves = new List<Wave>();
    private float nextReadyTime;

    public bool IsReady => Time.time >= nextReadyTime;
    public float CooldownRemaining => Mathf.Max(0f, nextReadyTime - Time.time);

    private void Reset()
    {
        // 에디터에서 컴포넌트를 처음 붙일 때 셰이더들을 자동으로 연결
        xrayShader = Shader.Find("Custom/ItemXRayGlow");
        sonarShader = Shader.Find("Custom/SonarWave");
        pingShader = Shader.Find("Custom/EchoPing");
    }

    private void Awake()
    {
        if (xrayShader != null) DetectableItem.DefaultXRayShader = xrayShader;
        if (pingShader != null) DetectableItem.DefaultPingShader = pingShader;
    }

    private void OnDisable()
    {
        foreach (Wave wave in waves) DestroyWaveMaterial(wave);
        waves.Clear();
    }

    private void Update()
    {
        if (Input.GetKeyDown(activateKey))
        {
            ActivateDetector();
        }

        DrawWaves();
    }

    public void ActivateDetector()
    {
        if (!IsReady)
        {
            Debug.Log($"<color=grey>[탐지기] 재사용 대기 중 ({CooldownRemaining:F1}초)</color>");
            return;
        }
        nextReadyTime = Time.time + cooldown;

        Vector3 origin = transform.position;
        float sqrRadius = detectRadius * detectRadius;
        int found = 0;

        StartWave(origin);

        // 눈 없는 몬스터에게 스캔 소리를 알린다 (파동이 닿는 시간에 맞춰 각자 반응)
        ScanNoise.Emit(origin, noiseRadius, waveSpeed);

        var items = DetectableItem.All;
        for (int i = 0; i < items.Count; i++)
        {
            DetectableItem item = items[i];
            if (item == null || !item.isActiveAndEnabled) continue;

            // 레이어 지정이 있을 때만 레이어로 거름
            if (itemLayer.value != 0 && (itemLayer.value & (1 << item.gameObject.layer)) == 0) continue;

            float sqrDist = (item.WorldCenter - origin).sqrMagnitude;
            if (sqrDist > sqrRadius) continue;

            // 눈에 보이는 파동이 아이템에 닿는 순간(거리 / 속도)에 맞춰 발광 시작
            float delay = waveSpeed > 0f ? Mathf.Sqrt(sqrDist) / waveSpeed : 0f;
            item.StartGlow(glowDuration, delay);
            found++;
        }

        Debug.Log($"<color=cyan>[탐지기 작동] 반경 {detectRadius}m 안에서 아이템 {found}개를 감지했습니다.</color>");
    }

    // ------------------------------------------------------------------ 음파(소나) 파동

    private void StartWave(Vector3 origin)
    {
        if (!showWave || sonarShader == null) return;

        EnsureDepthTexture();

        Material material = new Material(sonarShader) { name = "SonarWave (runtime)", hideFlags = HideFlags.HideAndDontSave };
        material.SetColor(ColorId, waveColor);
        material.SetVector(OriginId, origin);
        material.SetFloat(MaxRadiusId, detectRadius);
        material.SetFloat(BandWidthId, frontWidth);
        material.SetFloat(TrailLengthId, trailLength);
        material.SetFloat(RingSpacingId, ringSpacing);
        material.SetFloat(IntensityId, 1f);

        waves.Add(new Wave { origin = origin, startTime = Time.time, material = material });
    }

    private void DrawWaves()
    {
        if (waves.Count == 0) return;

        Mesh cube = DetectableItem.CubeMesh;
        float visualSpeed = waveSpeed > 0f ? waveSpeed : 40f; // 속도 0(동시 발광)이어도 파동은 빠르게 퍼지는 모습으로

        for (int i = waves.Count - 1; i >= 0; i--)
        {
            Wave wave = waves[i];
            float radius = visualSpeed * (Time.time - wave.startTime);

            if (radius > detectRadius + 0.5f)
            {
                DestroyWaveMaterial(wave);
                waves.RemoveAt(i);
                continue;
            }

            wave.material.SetFloat(RadiusId, radius);

            // 파면이 닿은 범위만 덮는 큐브 (안쪽 면을 그려서 카메라가 안에 있어도 보인다)
            float size = (radius + frontWidth + 0.1f) * 2f;
            RenderParams rp = new RenderParams(wave.material)
            {
                layer = gameObject.layer,
                worldBounds = new Bounds(wave.origin, Vector3.one * size),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderMesh(rp, cube, 0, Matrix4x4.TRS(wave.origin, Quaternion.identity, Vector3.one * size));
        }
    }

    // 파동 셰이더는 깊이 텍스처로 표면 위치를 알아내므로, 꺼져 있다면 메인 카메라에서 켠다
    private static void EnsureDepthTexture()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        if (cam.TryGetComponent(out UniversalAdditionalCameraData data) && !data.requiresDepthTexture)
            data.requiresDepthTexture = true;
    }

    private static void DestroyWaveMaterial(Wave wave)
    {
        if (wave == null || wave.material == null) return;
        if (Application.isPlaying) Destroy(wave.material);
        else DestroyImmediate(wave.material);
        wave.material = null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}