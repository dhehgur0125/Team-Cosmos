/* [ 코드 설명 ] 무전으로 들어온 목소리를 무전기 음질로 바꾸는 오디오 필터입니다. 좁은 대역(저음·고음 깎기), 약한 찌그러짐과 비트 줄이기, 말하는 동안에만 깔리는 치지직 잡음과 툭툭 튀는 소리를 더합니다. RadioVoiceEffect가 Vivox 참가자 탭 오브젝트에 붙이며, 오디오 스레드에서 메모리 할당 없이 동작합니다. */
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RadioVoiceFilter : MonoBehaviour
{
    [Header("대역 (무전기처럼 좁게)")]
    [Range(100f, 1000f)] public float lowCut = 400f;
    [Range(1500f, 6000f)] public float highCut = 2800f;

    [Header("찌그러짐")]
    [Range(1f, 10f)] public float drive = 3.5f;
    [Tooltip("낮을수록 거칠어짐")]
    [Range(3, 16)] public int bitDepth = 7;

    [Header("치지직 잡음 (말하는 동안 + 짧은 꼬리)")]
    [Range(0f, 0.3f)] public float hiss = 0.05f;
    [Range(0f, 40f)] public float cracklesPerSecond = 14f;
    [Range(0f, 1f)] public float crackleLevel = 0.3f;

    [Header("출력")]
    [Range(0f, 2f)] public float outputGain = 1.2f;

    // 오디오 스레드에서 읽는 값 (메인 스레드에서 미리 계산)
    float hb0, hb1, hb2, ha1, ha2;
    float lb0, lb1, lb2, la1, la2;
    float quant, popChance, aDrive, aHiss, aCrackle, aGain;
    volatile bool ready;

    // 필터 상태 (오디오 스레드 전용)
    float hx1, hx2, hy1, hy2, lx1, lx2, ly1, ly2;
    float env, gate, pop;
    uint rng = 0x9E3779B9u;

    void OnEnable() => Recalculate();
    void OnValidate() { if (Application.isPlaying && isActiveAndEnabled) Recalculate(); }

    public void Recalculate()
    {
        int rate = AudioSettings.outputSampleRate;
        if (rate <= 0) rate = 48000;
        float nyquist = rate * 0.45f;
        float high = Mathf.Min(highCut, nyquist);
        float low = Mathf.Min(lowCut, high * 0.5f);

        Biquad(low, rate, true, out hb0, out hb1, out hb2, out ha1, out ha2);
        Biquad(high, rate, false, out lb0, out lb1, out lb2, out la1, out la2);
        quant = Mathf.Pow(2f, Mathf.Clamp(bitDepth, 3, 16) - 1);
        popChance = cracklesPerSecond / rate;
        aDrive = drive; aHiss = hiss; aCrackle = crackleLevel; aGain = outputGain;
        ready = true;
    }

    // RBJ 2차 필터 계수 (Q = 0.707)
    static void Biquad(float frequency, int rate, bool highPass, out float b0, out float b1, out float b2, out float a1, out float a2)
    {
        float w0 = 2f * Mathf.PI * frequency / rate;
        float cos = Mathf.Cos(w0), alpha = Mathf.Sin(w0) / (2f * 0.7071f);
        float a0 = 1f + alpha;
        if (highPass) { b0 = (1f + cos) * 0.5f; b1 = -(1f + cos); b2 = (1f + cos) * 0.5f; }
        else { b0 = (1f - cos) * 0.5f; b1 = 1f - cos; b2 = (1f - cos) * 0.5f; }
        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 = -2f * cos / a0;
        a2 = (1f - alpha) / a0;
    }

    float Random01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 16777216f;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!ready || channels <= 0) return;

        for (int i = 0; i < data.Length; i += channels)
        {
            float x = 0f;
            for (int c = 0; c < channels; c++) x += data[i + c];
            x /= channels;

            // 1) 대역 제한: 저음 깎기 → 고음 깎기
            float h = hb0 * x + hb1 * hx1 + hb2 * hx2 - ha1 * hy1 - ha2 * hy2;
            hx2 = hx1; hx1 = x; hy2 = hy1; hy1 = h;
            float l = lb0 * h + lb1 * lx1 + lb2 * lx2 - la1 * ly1 - la2 * ly2;
            lx2 = lx1; lx1 = h; ly2 = ly1; ly1 = l;

            // 2) 말하는지 판단 (잡음은 말하는 동안 + 짧은 꼬리에만)
            float a = l < 0f ? -l : l;
            env += (a - env) * (a > env ? 0.01f : 0.0005f);
            float target = env > 0.004f ? 1f : 0f;
            gate += (target - gate) * (target > gate ? 0.002f : 0.00012f);

            // 3) 찌그러짐 + 비트 줄이기
            float t = l * aDrive;
            float y = t / (1f + (t < 0f ? -t : t));
            y = Mathf.Round(y * quant) / quant;

            // 4) 치지직 잡음과 툭툭 튀는 소리
            float noise = (Random01() * 2f - 1f) * aHiss * gate;
            if (Random01() < popChance * gate) pop = (Random01() * 2f - 1f) * aCrackle;
            noise += pop;
            pop *= 0.9f;

            float o = (y + noise) * aGain;
            if (o > 1f) o = 1f; else if (o < -1f) o = -1f;
            for (int c = 0; c < channels; c++) data[i + c] = o;
        }
    }
}
