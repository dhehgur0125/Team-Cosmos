/* [ 코드 설명 ] 눈 있는 대형 몬스터 전용 애니메이션 연출입니다. EyedMonsterAI의 상태·속도·HP를 읽어 Animator만 갱신하며 AI, 이동, 공격 판정에는 관여하지 않습니다. */
using UnityEngine;
using UnityEngine.AI;

namespace VoidLast.Monster
{
    [DisallowMultipleComponent, RequireComponent(typeof(EyedMonsterAI))]
    public sealed class EyedMonsterAnimation : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] AnimationClip attackClip;
        EyedMonsterAI ai;
        MonsterHealth health;
        MonsterMelee melee;
        NavMeshAgent agent;
        Vector3 previousPosition;
        float previousHealth, attackSpeed = 1;
        int current = -1, swings;
        bool hit;
        const int Locomotion = 0, Attack = 1, Dead = 2;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int AttackSpeed = Animator.StringToHash("AttackSpeed");
        static readonly int HitState = Animator.StringToHash("Base Layer.Hit");
        static readonly int[] States = {
            Animator.StringToHash("Base Layer.Locomotion"),
            Animator.StringToHash("Base Layer.Attack"),
            Animator.StringToHash("Base Layer.Dead")
        };
        void Awake()
        {
            ai = GetComponent<EyedMonsterAI>(); health = GetComponent<MonsterHealth>();
            melee = GetComponent<MonsterMelee>(); agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.applyRootMotion = false; // 이동은 NavMeshAgent와 MonsterMelee가 담당한다.
            // 공격 클립 한 번이 기존 공격 시간(준비+돌진+회복)에 맞게 재생되도록 속도만 맞춘다.
            if (attackClip != null && melee != null) attackSpeed = attackClip.length / Mathf.Max(0.01f, melee.Windup + melee.LungeDuration + melee.RecoveryDuration);
        }
        void OnEnable() { previousPosition = transform.position; current = -1; hit = false; }
        void Start() { previousHealth = health.Current; }
        bool Finished(int state, float time)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            return !animator.IsInTransition(0) && info.fullPathHash == state && info.normalizedTime >= time;
        }
        void LateUpdate()
        {
            var delta = transform.position - previousPosition;
            previousPosition = transform.position;
            bool damaged = health.Current < previousHealth;
            previousHealth = health.Current;
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
            float speed = ai.NetworkProxy ? Vector3.ProjectOnPlane(delta, Vector3.up).magnitude / Mathf.Max(Time.deltaTime, 0.0001f)
                : agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0;
            animator.SetFloat(Speed, Mathf.Min(speed, 8), 0.1f, Time.deltaTime);
            int target = health.IsDead || ai.State == EyedMonsterAI.Mode.Dead ? Dead : ai.State == EyedMonsterAI.Mode.Attack ? Attack : Locomotion;
            if (target == Attack && melee != null && melee.Swings != swings) { swings = melee.Swings; current = -1; } // 연속 공격마다 처음부터 재생
            if (target != current)
            {
                current = target; hit = false; animator.speed = 1;
                if (target == Attack) animator.SetFloat(AttackSpeed, attackSpeed);
                animator.CrossFadeInFixedTime(States[target], target == Dead ? 0.15f : 0.1f, 0, 0);
            }
            else if (target == Dead) { if (Finished(States[Dead], 0.96f)) animator.speed = 0; } // 원본 클립이 반복 설정이라 마지막 자세에서 멈춘다.
            else if (target == Locomotion)
            {
                if (damaged && !hit) { hit = true; animator.CrossFadeInFixedTime(HitState, 0.05f, 0, 0); }
                else if (hit && Finished(HitState, 0.9f)) { hit = false; animator.CrossFadeInFixedTime(States[Locomotion], 0.15f, 0, 0); }
            }
        }
    }
}
