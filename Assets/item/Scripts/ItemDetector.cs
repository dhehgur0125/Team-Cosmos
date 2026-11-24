using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VoidLast.Monster;
using VoidLast.Player;

/// <summary>
/// 플레이어에 붙이는 아이템 탐지기 (에코로케이션 방식).
/// 대원(FPS)은 스캐너 아이템을 손에 들고 좌클릭할 때 스캔한다 (FpsScannerItem, 쿨타임 scannerCooldown).
/// 인벤토리가 없는 테스트 씬(itemtest)에서는 예전처럼 activateKey(Z)로 스캔한다.
/// 스캔하면 플레이어에게서 음파가 퍼져 나가며 바닥·벽 표면을 훑고,
/// 파동이 닿은 DetectableItem은 메아리 링과 함께 발광하며 벽·지형 너머에서도 실루엣으로 비쳐 보인다.
/// </summary>
public class ItemDetector : MonoBehaviour
{
    [Header("탐지기 설정")]
    public float detectRadius = 30f; // 감지 반경 (30m)
    public float glowDuration = 3f;  // 발광 유지 시간

    [Tooltip("비워두면(Nothing) 모든 DetectableItem을 감지합니다. 지정하면 해당 레이어의 아이템만 감지합니다.")]
    public LayerMask itemLayer;

    [Tooltip("탐지기 작동 키. Q는 FpsInput의 '아이템 버리기'와 겹쳐서 Z로 변경했습니다.")]
    public KeyCode activateKey = KeyCode.Z;

    [Min(0f)] public float cooldown = 1.5f; // 재사용 대기 시간(초). Z 키·검사 도구(ActivateDetector)용

    [Header("스캐너 아이템")]
    [Tooltip("켜면 같은 오브젝트에 FpsInventory가 있을 때(대원) Z 키를 쓰지 않고, 스캐너 아이템을 손에 들고 좌클릭할 때만 스캔합니다.")]
    public bool requireScannerItem = true;
    [Tooltip("스캐너 아이템으로 스캔한 뒤 다시 쓸 수 있을 때까지의 시간(초)")]
    [Min(0f)] public float scannerCooldown = 15f;
    [Tooltip("스캐너 아이템으로 스캔할 때의 탐지 반경(m). 파동도 이 반경까지 퍼집니다. detectRadius는 Z 키·검사 도구용입니다.")]
    [Min(1f)] public float scannerRadius = 30f;

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


    [Header("임무 아이템 탐색")]
    [Min(1f)] public float missionScanRadius = 30f;
    [Tooltip("다른 층의 목표가 섞이지 않도록 플레이어와의 높이 차이를 제한합니다.")]
    [Min(0f)] public float missionHeightTolerance = 4f;
    [Min(1f)] public float missionGlowDuration = 8f;

    private sealed class MissionMarker
    {
        public FpsWorldPickup pickup;
        public float startsAt, expiresAt;
    }
    private readonly List<MissionMarker> missionMarkers = new List<MissionMarker>();
    private Camera markerCamera;
    private GUIStyle markerStyle;

    // Only scan live objectives. Never activate pickups gated by mission progression.
    private void RegisterMissionItems()
    {
        foreach (var pickup in FindObjectsByType<FpsWorldPickup>(FindObjectsSortMode.None))
        {
            if (!pickup.isActiveAndEnabled || pickup.IsClaimed || !FarmingItems.IsMissionItem(pickup.Item)) continue;
            if (pickup.gameObject.scene != gameObject.scene) continue;
            if (pickup.GetComponentInChildren<DetectableItem>() == null)
                pickup.gameObject.AddComponent<DetectableItem>();
        }
    }

    private void OnGUI()
    {
        if (missionMarkers.Count == 0) return;
        if (markerCamera == null) markerCamera = GetComponentInChildren<Camera>();
        if (markerCamera == null || !markerCamera.isActiveAndEnabled) return;
        if (markerStyle == null)
        {
            markerStyle = new GUIStyle(GUI.skin.box);
            markerStyle.alignment = TextAnchor.MiddleCenter;
            markerStyle.fontStyle = FontStyle.Bold;
            markerStyle.normal.textColor = new Color(0.35f, 1f, 1f);
        }
        markerStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 60f), 14, 24);
        for (int i = missionMarkers.Count - 1; i >= 0; i--)
        {
            var marker = missionMarkers[i];
            var pickup = marker.pickup;
            if (pickup == null || !pickup.isActiveAndEnabled || pickup.IsClaimed || Time.time > marker.expiresAt)
            {
                missionMarkers.RemoveAt(i);
                continue;
            }
            if (Time.time < marker.startsAt) continue;
            Vector3 point = markerCamera.WorldToViewportPoint(pickup.transform.position);
            string label = FarmingItems.HudLabel(pickup.Item) + "\n" +
                Mathf.RoundToInt(Vector3.Distance(transform.position, pickup.transform.position)) + " m";
            // 화면 밖·뒤에 있으면 화면 가장자리에 그 방(목표)이 있는 쪽을 가리키는 화살표를 띄운다
            if (point.z <= 0f || point.x < 0f || point.x > 1f || point.y < 0f || point.y > 1f)
            {
                DrawEdgeArrow(point, label);
                continue;
            }
            float width = 220f, height = markerStyle.fontSize * 2f + 16f;
            float x = Mathf.Clamp(point.x * Screen.width - width * 0.5f, 8f, Mathf.Max(8f, Screen.width - width - 8f));
            float y = Mathf.Clamp((1f - point.y) * Screen.height - height - 12f, 8f, Mathf.Max(8f, Screen.height - height - 8f));
            GUI.Label(new Rect(x, y, width, height), label, markerStyle);
        }
    }

    /// <summary>화면 밖 목표: 화면 가운데에서 목표 쪽으로 향하는 가장자리 위치에 화살표 + 이름·거리</summary>
    private void DrawEdgeArrow(Vector3 viewport, string label)
    {
        Vector2 dir = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
        if (viewport.z <= 0f) dir = -dir;   // 카메라 뒤쪽이면 투영이 뒤집힌다
        dir.x *= Screen.width; dir.y *= Screen.height;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.down;   // 바로 뒤
        dir.Normalize();
        Vector2 guiDir = new Vector2(dir.x, -dir.y);   // GUI 좌표는 y가 아래로
        float margin = 70f;
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float sx = Mathf.Abs(guiDir.x) > 0.0001f ? (center.x - margin) / Mathf.Abs(guiDir.x) : float.MaxValue;
        float sy = Mathf.Abs(guiDir.y) > 0.0001f ? (center.y - margin) / Mathf.Abs(guiDir.y) : float.MaxValue;
        Vector2 pos = center + guiDir * Mathf.Min(sx, sy);

        float arrowSize = Mathf.Clamp(Screen.height / 14f, 40f, 90f);

        // 이름·거리는 화살표 안쪽(화면 가운데 쪽)에, 화살표와 겹치지 않게 축마다 따로 밀어 넣는다
        float width = 220f, height = markerStyle.fontSize * 2f + 16f;
        Vector2 labelCenter = pos - new Vector2(guiDir.x * (width * 0.5f + arrowSize * 0.6f), guiDir.y * (height * 0.5f + arrowSize * 0.6f));
        float x = Mathf.Clamp(labelCenter.x - width * 0.5f, 8f, Mathf.Max(8f, Screen.width - width - 8f));
        float y = Mathf.Clamp(labelCenter.y - height * 0.5f, 8f, Mathf.Max(8f, Screen.height - height - 8f));
        GUI.Label(new Rect(x, y, width, height), label, markerStyle);

        // 화살표: 방향(10° 단위)마다 삼각형 텍스처를 만들어 두고 그린다 (폰트·GUI 회전에 의존하지 않음)
        int bucket = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(guiDir.y, guiDir.x) * Mathf.Rad2Deg, 360f) / 10f) % 36;
        if (arrowTextures[bucket] == null) arrowTextures[bucket] = BuildArrowTexture(bucket * 10f);
        Color savedColor = GUI.color;
        GUI.color = new Color(0.35f, 1f, 1f, 0.95f);
        GUI.DrawTexture(new Rect(pos.x - arrowSize * 0.5f, pos.y - arrowSize * 0.5f, arrowSize, arrowSize), arrowTextures[bucket]);
        GUI.color = savedColor;
    }

    private readonly Texture2D[] arrowTextures = new Texture2D[36];

    /// <summary>GUI 좌표(y 아래쪽)에서 angleDeg 방향을 가리키는 흰 삼각형 (가장자리 부드럽게)</summary>
    private static Texture2D BuildArrowTexture(float angleDeg)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ScanArrow (runtime)", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 forward = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));   // 꼭짓점 방향
        Vector2 side = new Vector2(-forward.y, forward.x);
        for (int py = 0; py < size; py++)
        {
            // 텍스처의 맨 위 줄(py = size-1)이 화면 위에 그려진다 → GUI 좌표 y = 아래로
            float gy = 0.5f - (py + 0.5f) / size;
            for (int px = 0; px < size; px++)
            {
                Vector2 p = new Vector2((px + 0.5f) / size - 0.5f, gy);
                float along = Vector2.Dot(p, forward), across = Mathf.Abs(Vector2.Dot(p, side));
                float halfWidth = (0.44f - along) * 0.6f;
                float edge = Mathf.Min(halfWidth - across, along + 0.34f, 0.44f - along);
                byte alpha = (byte)(Mathf.Clamp01(edge * size * 0.75f) * 255f);
                pixels[py * size + px] = new Color32(255, 255, 255, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private sealed class Wave
    {
        public Vector3 origin;
        public float startTime;
        public float maxRadius;
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
    private FpsInventory inventory;

    /// <summary>스캐너 아이템으로만 스캔하는지 (대원). false면 Z 키로 스캔하는 테스트 씬</summary>
    public bool UsesScannerItem => requireScannerItem && inventory != null;
    /// <summary>마지막 스캔에 적용된 재사용 대기 시간(초). 쿨타임 표시용</summary>
    public float LastCooldownDuration { get; private set; } = 1f;

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

        // 대원이면 손에 드는 스캐너(모델·좌클릭·쿨타임 표시)를 함께 붙인다. 프리팹은 건드리지 않는다
        inventory = GetComponent<FpsInventory>();
        if (UsesScannerItem && GetComponent<FpsScannerItem>() == null) gameObject.AddComponent<FpsScannerItem>();
    }

    private void OnDisable()
    {
        foreach (Wave wave in waves) DestroyWaveMaterial(wave);
        waves.Clear();
        missionMarkers.Clear();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < arrowTextures.Length; i++)
            if (arrowTextures[i] != null) { Destroy(arrowTextures[i]); arrowTextures[i] = null; }
    }

private void Update()
    {
        // 대원은 Z를 쓰지 않는다 (스캐너 아이템 좌클릭). 인벤토리가 없는 테스트 씬만 Z로 스캔
        if (!UsesScannerItem && Input.GetKeyDown(activateKey))
        {
            ActivateDetector();
        }

        DrawWaves();
    }

/// <summary>Z 키·검사 도구용 스캔 (쿨타임 cooldown, 아이템 확인 없음)</summary>
    public void ActivateDetector()
    {
        if (!IsReady)
        {
            Debug.Log($"<color=grey>[탐지기] 재사용 대기 중 ({CooldownRemaining:F1}초)</color>");
            return;
        }
        Scan(cooldown, detectRadius);
    }

    /// <summary>스캐너 아이템 좌클릭: 스캐너를 가지고 있고 쿨타임이 끝났을 때만 스캔한다 (쿨타임 scannerCooldown)</summary>
    public bool TryUseScanner()
    {
        if (!isActiveAndEnabled || !IsReady) return false;
        if (inventory != null && (!inventory.IsAlive || !(inventory.HasItem(FarmingItemKind.Scanner) || FpsUniqueItem.For(gameObject).Has(UniqueItemKind.Scanner)))) return false;
        Scan(scannerCooldown, scannerRadius);
        return true;
    }

    private void Scan(float cooldownSeconds, float radius)
    {
        LastCooldownDuration = Mathf.Max(0.01f, cooldownSeconds);
        nextReadyTime = Time.time + cooldownSeconds;

        Vector3 origin = transform.position;
        RegisterMissionItems();
        missionMarkers.Clear();
        int found = 0;

        StartWave(origin, radius);

        // 눈 없는 몬스터에게 스캔 소리를 알린다 (파동이 닿는 시간에 맞춰 각자 반응)
        ScanNoise.Emit(origin, noiseRadius, waveSpeed);

        var items = DetectableItem.All;
        for (int i = 0; i < items.Count; i++)
        {
            DetectableItem item = items[i];
            if (item == null || !item.isActiveAndEnabled) continue;

            if (item.gameObject.scene != gameObject.scene) continue;
            var pickup = item.GetComponentInParent<FpsWorldPickup>();
            if (pickup != null && (pickup.IsClaimed || !pickup.isActiveAndEnabled)) continue;
            bool mission = pickup != null && FarmingItems.IsMissionItem(pickup.Item);
            // Mission objectives must remain detectable regardless of ordinary loot layer filters.
            if (!mission && itemLayer.value != 0 && (itemLayer.value & (1 << item.gameObject.layer)) == 0) continue;
            Vector3 offset = item.WorldCenter - origin;
            float sqrDist = offset.sqrMagnitude;
            float range = mission ? Mathf.Max(radius, missionScanRadius) : radius;
            if (sqrDist > range * range) continue;
            if (mission && Mathf.Abs(pickup.transform.position.y - origin.y) > missionHeightTolerance) continue;
            float delay = waveSpeed > 0f ? Mathf.Sqrt(sqrDist) / waveSpeed : 0f;
            // Long-range objective locator responds promptly; the nearby sonar wave stays unchanged.
            if (mission) delay = Mathf.Min(delay, 1.5f);
            float duration = mission ? missionGlowDuration : glowDuration;
            item.StartGlow(duration, delay);
            if (mission)
                missionMarkers.Add(new MissionMarker { pickup = pickup, startsAt = Time.time + delay, expiresAt = Time.time + delay + duration });
            found++;
        }

        Debug.Log($"<color=cyan>[탐지기 작동] 일반 {radius}m / 임무 {missionScanRadius}m: 아이템 {found}개를 감지했습니다.</color>");
    }

    // ------------------------------------------------------------------ 음파(소나) 파동

    private void StartWave(Vector3 origin, float radius)
    {
        if (!showWave || sonarShader == null) return;

        EnsureDepthTexture();

        Material material = new Material(sonarShader) { name = "SonarWave (runtime)", hideFlags = HideFlags.HideAndDontSave };
        material.SetColor(ColorId, waveColor);
        material.SetVector(OriginId, origin);
        material.SetFloat(MaxRadiusId, radius);
        material.SetFloat(BandWidthId, frontWidth);
        material.SetFloat(TrailLengthId, trailLength);
        material.SetFloat(RingSpacingId, ringSpacing);
        material.SetFloat(IntensityId, 1f);

        waves.Add(new Wave { origin = origin, startTime = Time.time, maxRadius = radius, material = material });
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

            if (radius > wave.maxRadius + 0.5f)
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