using UnityEngine;

/// <summary>
/// 임무용 상호작용 장치 (F를 길게 눌러 작동). 실제 판정·진행은 F2MissionDirector가 맡고,
/// 여기에는 어떤 장치인지, 안내 문구, 누르는 시간, 상태 표시등만 둔다.
/// 레이캐스트용 콜라이더는 Trigger로 두어 이동을 막지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionInteractable : MonoBehaviour
{
    // 씬에 번호로 저장되므로 새 종류는 맨 뒤에만 추가한다
    public enum Kind { PowerModule = 1, FuelPump = 2, IntrusionLog = 3, QuarantineConsole = 4, CargoLift = 5, FuelValve = 6, LockdownDoor = 7 }

    public Kind kind = Kind.PowerModule;
    [Tooltip("화면 아래 안내 (예: 전력 모듈 꺼내기)")]
    public string actionLabel = "작동";
    [Min(0.1f)] public float holdSeconds = 2f;
    [Min(0.5f)] public float maxDistance = 2.6f;

    [Header("상태 표시등 (선택)")]
    public Light statusLight;
    public Renderer statusRenderer;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    MaterialPropertyBlock _block;
    Color _color = Color.clear;
    float _intensity = -1f, _pulse;

    /// <summary>표시등 색과 깜빡임 (pulse 0 = 고정)</summary>
    public void SetStatus(Color color, float intensity, float pulse = 0f)
    {
        _color = color; _intensity = intensity; _pulse = pulse;
        Apply(1f);
    }

    void Update()
    {
        if (_pulse > 0f && _intensity >= 0f) Apply(0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.time * _pulse)));
    }

    void Apply(float k)
    {
        if (statusLight != null)
        {
            statusLight.color = _color;
            statusLight.intensity = Mathf.Max(0, _intensity) * k;
            statusLight.enabled = _intensity > 0.001f;
        }
        if (statusRenderer != null)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            statusRenderer.GetPropertyBlock(_block);
            _block.SetColor(EmissionId, _color * Mathf.Max(0, _intensity) * k * 1.5f);
            _block.SetColor(BaseColorId, Color.Lerp(new Color(0.08f, 0.08f, 0.09f), _color, _intensity > 0 ? 0.6f : 0f));
            statusRenderer.SetPropertyBlock(_block);
        }
    }
}
