using UnityEngine;

/// <summary>
/// 정전 상태 비상등 연출. 상태는 SyncedClock 시간 + 시드로만 계산되므로
/// 멀티플레이(최대 4명) 모든 클라이언트에서 같은 타이밍으로 깜빡인다.
/// 순수 연출용이라 네트워크 트래픽이 없다.
/// </summary>
[RequireComponent(typeof(Light))]
public class EmergencyLightFlicker : MonoBehaviour
{
    public enum FlickerMode
    {
        Flicker, // 대부분 켜져 있다가 가끔 몇 초간 지직거림
        Broken,  // 대부분 꺼져 있고 가끔 번쩍
        Pulse    // 경보등처럼 천천히 숨쉬듯 밝아졌다 어두워짐
    }

    public FlickerMode mode = FlickerMode.Flicker;
    [Min(0f)] public float baseIntensity = 1.2f;
    [Range(0f, 1f)] public float minFactor = 0.03f;
    [Tooltip("초당 상태가 바뀌는 횟수 (지직거림 속도)")]
    [Min(1f)] public float tickRate = 14f;
    [Tooltip("Flicker: 지직거림 구간이 생길 확률 / Broken: 켜질 확률")]
    [Range(0f, 1f)] public float eventChance = 0.35f;
    [Tooltip("이벤트 판정 주기(초)")]
    [Min(0.1f)] public float eventWindow = 2.5f;
    [Tooltip("Pulse 한 주기(초)")]
    [Min(0.1f)] public float pulsePeriod = 2f;
    [Tooltip("0이면 위치 기반 시드를 사용 (모든 클라이언트 동일)")]
    public int seed;

    [Header("램프 발광 (선택)")]
    public Renderer lampRenderer;
    [ColorUsage(false, true)] public Color lampEmission = new Color(4f, 0.2f, 0.08f);


    [Header("최적화")]
    [Tooltip("현재 카메라에서 먼 비상등의 실시간 조명을 점진적으로 줄입니다.")]
    public bool distanceOptimization;
    [Min(10f)] public float fullBrightnessDistance = 30f;
    [Min(1f)] public float fadeDistance = 10f;
    [Range(5f, 30f)] public float pulseUpdateRate = 20f;
    static Camera cachedCamera;
    static int cameraFrame = -1;
    double nextSampleTime = double.NegativeInfinity;
    float originalIntensity, lastIntensity = float.NaN, lastEmissionFactor = float.NaN;
    MaterialPropertyBlock originalBlock;

    static Camera ViewCamera()
    {
        if (cameraFrame != Time.frameCount)
        {
            cameraFrame = Time.frameCount;
            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled || !cachedCamera.CompareTag("MainCamera"))
                cachedCamera = Camera.main;
        }
        return cachedCamera;
    }

    void OnEnable() { nextSampleTime = double.NegativeInfinity; lastIntensity = lastEmissionFactor = float.NaN; }

    void OnDisable()
    {
        if (_light != null) _light.intensity = originalIntensity;
        if (lampRenderer != null && originalBlock != null) lampRenderer.SetPropertyBlock(originalBlock);
    }

    Light _light;
    MaterialPropertyBlock _mpb;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        _light = GetComponent<Light>();
        originalIntensity = _light.intensity;
        if (lampRenderer != null) { originalBlock = new MaterialPropertyBlock(); lampRenderer.GetPropertyBlock(originalBlock); }
        if (seed == 0) seed = SyncedClock.SeedFromPosition(transform.position);
        _mpb = new MaterialPropertyBlock();
    }


    void Update()
    {
        double now = SyncedClock.Time;
        // Sample on shared clock boundaries: multiplayer flicker still uses identical seeds/ticks.
        if (now >= nextSampleTime - 1.0 && now < nextSampleTime) return;
        float visibility = 1f;
        if (distanceOptimization)
        {
            Camera camera = ViewCamera();
            if (camera != null)
            {
                float distance = Vector3.Distance(camera.transform.position, transform.position);
                visibility = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(fullBrightnessDistance, fullBrightnessDistance + Mathf.Max(1f, fadeDistance), distance));
            }
        }
        float rate = visibility <= 0f ? 5f : (mode == FlickerMode.Pulse ? pulseUpdateRate : tickRate);
        rate = Mathf.Max(1f, rate);
        nextSampleTime = (System.Math.Floor(now * rate) + 1.0) / rate;
        // Distant lamps retain their authored emissive surface without a realtime light.
        float factor = visibility > 0f ? Evaluate(now) : 0f;
        float intensity = baseIntensity * factor * visibility;
        if (!Mathf.Approximately(lastIntensity, intensity))
        {
            _light.intensity = intensity;
            lastIntensity = intensity;
        }
        if (lampRenderer != null && visibility > 0f && !Mathf.Approximately(lastEmissionFactor, factor))
        {
            lampRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionId, lampEmission * factor);
            lampRenderer.SetPropertyBlock(_mpb);
            lastEmissionFactor = factor;
        }
    }

    float Evaluate(double t)
    {
        long tick = (long)System.Math.Floor(t * tickRate);
        long window = (long)System.Math.Floor(t / eventWindow);
        float r = SyncedClock.Hash01(tick, seed);
        bool inEvent = SyncedClock.Hash01(window, seed + 7919) < eventChance;

        switch (mode)
        {
            case FlickerMode.Pulse:
            {
                double phase = (t / pulsePeriod + (seed & 1023) / 1024.0) % 1.0;
                float s = 0.5f + 0.5f * Mathf.Sin((float)(phase * 2.0 * System.Math.PI));
                return Mathf.Lerp(minFactor, 1f, s * s);
            }
            case FlickerMode.Broken:
                if (!inEvent) return 0f;
                return r < 0.55f ? minFactor : Mathf.Lerp(0.5f, 1f, r);
            default: // Flicker
                if (!inEvent) return 0.9f + 0.1f * SyncedClock.Hash01(tick, seed + 13);
                return r < 0.45f ? minFactor : Mathf.Lerp(0.35f, 1f, r);
        }
    }
}
