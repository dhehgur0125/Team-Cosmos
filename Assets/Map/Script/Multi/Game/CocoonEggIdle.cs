using UnityEngine;

/// <summary>
/// 고치 모습(SpiderEgg 에셋)의 애니메이션을 관리한다.
///  - 평소: 대기 동작만 반복한다. 에셋의 Egg Controller는 대기를 4번 하고 알이 터져 열리는(Egg_Open, 납작해짐) 순서라서,
///    마지막 대기 상태에 들어서면 처음 대기로 되돌려 구출될 때까지 닫힌 알로 남게 한다. 에셋은 고치지 않는다.
///  - Burst(): 구출될 때 알이 터져 열리고 점액이 튄 뒤 잠시 후 사라진다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CocoonEggIdle : MonoBehaviour
{
    static readonly int LastIdle = Animator.StringToHash("Egg_Idle 0 0 0");
    static readonly int Open = Animator.StringToHash("Egg_Open");
    static readonly int FirstIdle = Animator.StringToHash("Egg_Idle");

    Animator _animator;
    bool _bursting;

    public bool IsBursting => _bursting;

    void Awake() { _animator = GetComponentInChildren<Animator>(); }

    void Update()
    {
        if (_bursting || _animator == null || !_animator.isActiveAndEnabled || !_animator.isInitialized) return;
        int state = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        if (state == LastIdle || state == Open || (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == Open))
            _animator.Play(FirstIdle, 0, 0f);
    }

    /// <summary>알이 터져 열리는 연출. destroyRoot를 연출이 끝난 뒤 없앤다 (Egg_Open 1.3초 + 여유)</summary>
    public void Burst(GameObject destroyRoot, float lifetime = 2.2f)
    {
        if (_bursting) return;
        _bursting = true;
        if (_animator != null) _animator.CrossFadeInFixedTime(Open, 0.05f, 0);
        foreach (var particles in GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Clear(true);
            particles.Play(true);
        }
        Destroy(destroyRoot != null ? destroyRoot : gameObject, lifetime);
    }
}
