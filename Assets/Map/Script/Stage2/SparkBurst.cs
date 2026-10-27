using UnityEngine;

/// <summary>
/// 고장난 배선에서 튀는 스파크. 터지는 시점은 SyncedClock + 시드로 결정되므로
/// 모든 클라이언트에서 같은 순간에 스파크와 섬광이 보인다. (연출 전용, 네트워크 트래픽 없음)
/// ParticleSystem의 Emission 모듈은 꺼 두고 이 스크립트가 Emit()으로 직접 뿜는다.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class SparkBurst : MonoBehaviour
{
    [Tooltip("스파크 판정 간격(초)")]
    [Min(0.05f)] public float checkInterval = 0.2f;
    [Tooltip("판정마다 터질 확률")]
    [Range(0f, 1f)] public float burstChance = 0.12f;
    public Vector2Int burstCount = new Vector2Int(8, 22);

    [Header("섬광 (선택)")]
    public Light flashLight;
    [Min(0f)] public float flashIntensity = 3f;
    [Min(0.01f)] public float flashDuration = 0.12f;

    [Tooltip("0이면 위치 기반 시드를 사용 (모든 클라이언트 동일)")]
    public int seed;

    ParticleSystem _ps;
    long _lastTick = long.MinValue;
    double _flashStart = -999.0;

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        var em = _ps.emission; em.enabled = false;
        if (seed == 0) seed = SyncedClock.SeedFromPosition(transform.position);
        if (flashLight != null) flashLight.intensity = 0f;
    }

    void Update()
    {
        double t = SyncedClock.Time;
        long tick = (long)System.Math.Floor(t / checkInterval);
        if (tick != _lastTick)
        {
            // 프레임이 밀려도 최근 몇 틱만 확인 (늦게 접속한 클라이언트가 몰아서 터뜨리지 않도록)
            long from = _lastTick == long.MinValue ? tick : System.Math.Max(_lastTick + 1, tick - 3);
            for (long k = from; k <= tick; k++)
            {
                if (SyncedClock.Hash01(k, seed) < burstChance)
                {
                    float c = SyncedClock.Hash01(k, seed + 101);
                    _ps.Emit(Mathf.RoundToInt(Mathf.Lerp(burstCount.x, burstCount.y, c)));
                    _flashStart = k * checkInterval;
                }
            }
            _lastTick = tick;
        }

        if (flashLight != null)
        {
            double age = t - _flashStart;
            if (age >= 0 && age < flashDuration)
            {
                float jitter = SyncedClock.Hash01((long)(t * 60.0), seed + 7);
                flashLight.intensity = flashIntensity * (1f - (float)(age / flashDuration)) * Mathf.Lerp(0.5f, 1f, jitter);
            }
            else flashLight.intensity = 0f;
        }
    }
}
