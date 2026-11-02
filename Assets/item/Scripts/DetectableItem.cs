using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 탐지기(ItemDetector)에 감지되는 아이템에 붙이는 컴포넌트.
/// 탐지 파동이 닿으면 일정 시간 동안
///  0) 닿는 순간 아이템 위치에서 메아리 링(Echo Ping)이 번지고
///  1) 재질의 Emission을 켜서 발광시키고
///  2) 벽/지형에 가려진 부분은 X-Ray 실루엣(프레넬 림)으로 비쳐 보이게 한다.
/// </summary>
public class DetectableItem : MonoBehaviour
{
    // 활성화된 모든 탐지 대상. 레이어/콜라이더 없이 거리만으로 찾기 위한 목록.
    public static readonly List<DetectableItem> All = new List<DetectableItem>();

    // ItemDetector가 등록하는 X-Ray 셰이더. StartGlow(duration)만 호출돼도 벽 투과가 적용되도록 한다.
    public static Shader DefaultXRayShader;
    public static Shader DefaultPingShader;

    [Header("발광 색상")]
    [ColorUsage(true, true)] // HDR 색상 활성화 (URP에서 강하게 빛나게 함)
    public Color glowColor = Color.yellow * 2f;

    [Header("에코 핑 (파동이 닿는 순간 번지는 메아리 링)")]
    public bool echoPing = true;
    [Min(0.1f)] public float pingRadius = 1.4f;   // 링이 퍼지는 최대 반경(m)
    [Min(0.1f)] public float pingDuration = 0.9f; // 링이 퍼지는 시간(초)

    [Header("벽 투과 효과")]
    public bool throughWalls = true;
    [Range(0f, 1f)] public float seeThroughAlpha = 0.45f; // 벽 너머 실루엣의 채움 농도
    [Min(0.5f)] public float rimPower = 2.5f;              // 클수록 외곽선이 얇아짐
    [Min(0f)] public float fadeOutTime = 0.6f;             // 끝나기 직전 서서히 사라지는 시간

    private struct MeshPart
    {
        public Renderer renderer;
        public Mesh mesh;
    }

    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private readonly List<MeshPart> parts = new List<MeshPart>();
    private readonly List<Material> emissionMats = new List<Material>();
    private readonly List<Color> originalEmission = new List<Color>();
    private readonly List<bool> originalEmissionOn = new List<bool>();
    private bool partsCached;
    private bool emissionCached;

    private Material occludedMat; // 가려진 부분: ZTest Greater
    private Material pingMat;     // 파동이 닿는 순간의 메아리 링
    private Vector3 pingOrigin;
    private Material visibleMat;  // 보이는 부분: 은은한 림라이트(가산)

    private bool glowing;
    private float glowStart;
    private float glowEnd;
    private float phase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Enter Play Mode 옵션으로 도메인 리로드를 꺼도 이전 플레이의 잔여 값이 남지 않도록 초기화
        All.Clear();
        DefaultXRayShader = null;
        DefaultPingShader = null;
        quadMesh = null;
        cubeMesh = null;
    }

    public bool IsGlowing => glowing;

    // 쿼드/큐브 기본 메시 (탐지기 효과들이 공유해서 사용)
    private static Mesh quadMesh;
    private static Mesh cubeMesh;

    public static Mesh QuadMesh
    {
        get
        {
            if (quadMesh == null) quadMesh = CreatePrimitiveMesh(PrimitiveType.Quad);
            return quadMesh;
        }
    }

    public static Mesh CubeMesh
    {
        get
        {
            if (cubeMesh == null) cubeMesh = CreatePrimitiveMesh(PrimitiveType.Cube);
            return cubeMesh;
        }
    }

    // 아이템 전체(자식 포함)의 월드 중심. 탐지 거리 계산에 사용한다.
    public Vector3 WorldCenter
    {
        get
        {
            CacheParts();
            if (parts.Count == 0) return transform.position;

            Bounds bounds = parts[0].renderer.bounds;
            for (int i = 1; i < parts.Count; i++)
                bounds.Encapsulate(parts[i].renderer.bounds);
            return bounds.center;
        }
    }

    private void Awake()
    {
        phase = Random.value * Mathf.PI * 2f; // 아이템마다 깜빡이는 타이밍을 다르게
    }

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        StopGlow();
    }

    private void OnDestroy()
    {
        DestroyMaterial(ref occludedMat);
        DestroyMaterial(ref visibleMat);
        DestroyMaterial(ref pingMat);
        foreach (Material m in emissionMats)
        {
            if (m != null) Destroy(m);
        }
        emissionMats.Clear();
    }

    // 기존 호환: 지금 바로 발광
    public void StartGlow(float duration)
    {
        StartGlow(duration, 0f);
    }

    // delay초 뒤에 발광을 시작한다 (스캔 파동이 도달하는 타이밍용)
    public void StartGlow(float duration, float delay)
    {
        CacheParts();
        CacheEmission();
        EnsureMaterials();

        float now = Time.time;
        delay = Mathf.Max(0f, delay);

        // 이미 빛나는 중이면 처음부터 다시 켜지 않고 시간만 연장
        if (glowing && now >= glowStart)
        {
            glowEnd = Mathf.Max(glowEnd, now + delay + duration);
            return;
        }

        glowStart = now + delay;
        glowEnd = glowStart + duration;
        pingOrigin = WorldCenter;
        glowing = true;
    }

    public void StopGlow()
    {
        if (emissionCached) RestoreEmission();
        glowing = false;
    }

    private void Update()
    {
        if (!glowing) return;

        float now = Time.time;
        if (now < glowStart) return; // 스캔 파동이 아직 도달하지 않음
        if (now >= glowEnd)
        {
            StopGlow();
            return;
        }

        float t = now - glowStart;
        float appear = Mathf.Clamp01(t / 0.12f);                                              // 등장
        float fade = fadeOutTime > 0f ? Mathf.Clamp01((glowEnd - now) / fadeOutTime) : 1f;     // 소멸
        float pulse = 0.85f + 0.15f * Mathf.Sin(now * 7f + phase);                             // 숨쉬는 듯한 깜빡임
        float flash = 1f + 1.5f * Mathf.Exp(-t * 6f);                                          // 감지되는 순간 번쩍

        float level = appear * fade * pulse;
        ApplyEmission(level * flash);
        DrawXRay(level * flash);
        DrawPing(t);
    }

    // ------------------------------------------------------------------ Emission (보이는 상태의 발광)

    private void ApplyEmission(float k)
    {
        for (int i = 0; i < emissionMats.Count; i++)
        {
            Material m = emissionMats[i];
            if (m == null) continue;
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColorId, originalEmission[i] + glowColor * k);
        }
    }

    private void RestoreEmission()
    {
        for (int i = 0; i < emissionMats.Count; i++)
        {
            Material m = emissionMats[i];
            if (m == null) continue;
            m.SetColor(EmissionColorId, originalEmission[i]);
            if (!originalEmissionOn[i]) m.DisableKeyword("_EMISSION");
        }
    }

    // ------------------------------------------------------------------ X-Ray (벽 투과)

    private void DrawXRay(float intensity)
    {
        if (occludedMat == null || visibleMat == null) return;

        occludedMat.SetColor(GlowColorId, glowColor);
        occludedMat.SetFloat(IntensityId, intensity);
        occludedMat.SetFloat(FillAlphaId, seeThroughAlpha);

        visibleMat.SetColor(GlowColorId, glowColor);
        visibleMat.SetFloat(IntensityId, intensity * 0.6f);

        for (int i = 0; i < parts.Count; i++)
        {
            Renderer r = parts[i].renderer;
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

            Mesh mesh = parts[i].mesh;
            Matrix4x4 matrix = r.localToWorldMatrix;
            Bounds bounds = r.bounds;
            int layer = r.gameObject.layer;

            RenderMesh(occludedMat, mesh, matrix, bounds, layer);
            RenderMesh(visibleMat, mesh, matrix, bounds, layer);
        }
    }

    private static void RenderMesh(Material material, Mesh mesh, Matrix4x4 matrix, Bounds bounds, int layer)
    {
        RenderParams rp = new RenderParams(material)
        {
            layer = layer,
            worldBounds = bounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false
        };

        for (int s = 0; s < mesh.subMeshCount; s++)
            Graphics.RenderMesh(rp, mesh, s, matrix);
    }

private void DrawPing(float elapsed)
    {
        if (!echoPing || pingMat == null) return;

        float progress = elapsed / pingDuration;
        if (progress <= 0f || progress >= 1.25f) return; // 두 번째 링이 0.25만큼 늦게 따라온다

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 toItem = pingOrigin - cam.transform.position;
        if (toItem.sqrMagnitude < 0.0001f) return;

        pingMat.SetColor(ColorId, glowColor);
        pingMat.SetFloat(ProgressId, progress);

        // 카메라를 향하는 쿼드에 그린다 (깊이 검사를 하지 않아 벽 너머에서도 보인다)
        float size = pingRadius * 2f;
        Matrix4x4 matrix = Matrix4x4.TRS(pingOrigin, Quaternion.LookRotation(toItem, cam.transform.up), Vector3.one * size);

        RenderParams rp = new RenderParams(pingMat)
        {
            layer = gameObject.layer,
            worldBounds = new Bounds(pingOrigin, Vector3.one * size),
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false
        };
        Graphics.RenderMesh(rp, QuadMesh, 0, matrix);
    }


private void EnsureMaterials()
    {
        // 벽 투과(X-Ray)용 재질
        if (throughWalls && occludedMat == null && DefaultXRayShader != null)
        {
            Shader shader = DefaultXRayShader;

            occludedMat = new Material(shader) { name = "XRay_Occluded (runtime)", hideFlags = HideFlags.HideAndDontSave };
            occludedMat.SetFloat("_ZTest", (float)CompareFunction.Greater);
            occludedMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            occludedMat.SetFloat("_FresnelPower", rimPower);
            occludedMat.SetFloat("_RimBoost", 1.5f);

            visibleMat = new Material(shader) { name = "XRay_Visible (runtime)", hideFlags = HideFlags.HideAndDontSave };
            visibleMat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            visibleMat.SetFloat("_DstBlend", (float)BlendMode.One);
            visibleMat.SetFloat("_FillAlpha", 0f);
            visibleMat.SetFloat("_FresnelPower", rimPower + 0.5f);
            visibleMat.SetFloat("_RimBoost", 1.2f);
        }

        // 에코 핑(메아리 링)용 재질
        if (echoPing && pingMat == null && DefaultPingShader != null)
        {
            pingMat = new Material(DefaultPingShader) { name = "EchoPing (runtime)", hideFlags = HideFlags.HideAndDontSave };
        }
    }

    private static void DestroyMaterial(ref Material material)
    {
        if (material == null) return;
        if (Application.isPlaying) Destroy(material);
        else DestroyImmediate(material);
        material = null;
    }

private static Mesh CreatePrimitiveMesh(PrimitiveType type)
    {
        GameObject temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(temp);
        return mesh;
    }


    // ------------------------------------------------------------------ 캐시

    private void CacheParts()
    {
        if (partsCached) return;
        partsCached = true;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (r is MeshRenderer)
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
            }
            else if (r is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh;
            }

            if (mesh != null) parts.Add(new MeshPart { renderer = r, mesh = mesh });
        }
    }

    private void CacheEmission()
    {
        if (emissionCached) return;
        emissionCached = true;

        foreach (MeshPart part in parts)
        {
            foreach (Material m in part.renderer.materials) // 인스턴스 재질을 가져와 원본 에셋은 건드리지 않는다
            {
                if (m == null || !m.HasProperty(EmissionColorId)) continue;
                emissionMats.Add(m);
                originalEmission.Add(m.GetColor(EmissionColorId));
                originalEmissionOn.Add(m.IsKeywordEnabled("_EMISSION"));
            }
        }
    }
}