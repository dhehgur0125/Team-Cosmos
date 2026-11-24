/* [ 코드 설명 ] 눈 없는 대형·개구리가 함께 쓰는 애니메이션 연출입니다. AI가 알려 주는 상태(이동·공격·경고·쓰러짐·스턴)에 맞는 Animator 상태로 넘기기만 하고,
   AI·이동·공격 판정에는 관여하지 않습니다. 멀티플레이 복제본에서도 서버가 보낸 상태로 같은 동작이 재생됩니다.
   Animator 컨트롤러에는 Locomotion(Speed로 섞는 이동), Attack(AttackSpeed), Alert(AlertSpeed), Hit, Dead, Stunned 상태가 있어야 합니다
   (메뉴 Tools/VOID LAST/Monster/Setup New Monsters가 만듭니다). */
using UnityEngine;
using UnityEngine.AI;

namespace VoidLast.Monster
{
    public enum MonsterAnimState { Locomotion, Attack, Alert, Dead, Stunned }

    /// <summary>MonsterStateAnimation이 읽는 AI 쪽 정보</summary>
    public interface IMonsterAnimationSource
    {
        MonsterAnimState AnimState { get; }
        bool NetworkProxy { get; }
        /// <summary>공격을 시작할 때마다 1씩 늘어난다 (연속 공격도 동작을 처음부터 재생)</summary>
        int AttackCount { get; }
        /// <summary>지금 공격 한 번에 걸리는 시간(초). 공격 동작 한 번이 이 시간에 맞게 재생된다.</summary>
        float AttackDuration { get; }
        /// <summary>경고(울음) 동작에 걸리는 시간(초)</summary>
        float AlertDuration { get; }
    }

    [DisallowMultipleComponent, RequireComponent(typeof(MonsterHealth))]
    public sealed class MonsterStateAnimation : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] AnimationClip attackClip;
        [SerializeField] AnimationClip alertClip;
        IMonsterAnimationSource source;
        MonsterHealth health;
        NavMeshAgent agent;
        Vector3 previousPosition;
        float previousHealth;
        int current = -1, attacks;
        bool hit;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int AttackSpeed = Animator.StringToHash("AttackSpeed");
        static readonly int AlertSpeed = Animator.StringToHash("AlertSpeed");
        static readonly int HitState = Animator.StringToHash("Base Layer.Hit");
        static readonly int[] States = {
            Animator.StringToHash("Base Layer.Locomotion"),
            Animator.StringToHash("Base Layer.Attack"),
            Animator.StringToHash("Base Layer.Alert"),
            Animator.StringToHash("Base Layer.Dead"),
            Animator.StringToHash("Base Layer.Stunned")
        };

        void Awake()
        {
            source = GetComponent<IMonsterAnimationSource>();
            health = GetComponent<MonsterHealth>();
            agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.applyRootMotion = false;   // 이동은 AI가 담당한다
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
            if (source == null || animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;

            float speed = source.NetworkProxy || agent == null || !agent.enabled || !agent.isOnNavMesh
                ? Vector3.ProjectOnPlane(delta, Vector3.up).magnitude / Mathf.Max(Time.deltaTime, 0.0001f)
                : agent.velocity.magnitude;
            animator.SetFloat(Speed, Mathf.Min(speed, 8), 0.1f, Time.deltaTime);

            int target = health.IsDead ? (int)MonsterAnimState.Dead : (int)source.AnimState;
            if (target == (int)MonsterAnimState.Attack && source.AttackCount != attacks) { attacks = source.AttackCount; current = -1; }
            if (target != current)
            {
                current = target; hit = false; animator.speed = 1;
                if (target == (int)MonsterAnimState.Attack && attackClip != null)
                    animator.SetFloat(AttackSpeed, attackClip.length / Mathf.Max(0.05f, source.AttackDuration));
                if (target == (int)MonsterAnimState.Alert && alertClip != null)
                    animator.SetFloat(AlertSpeed, Mathf.Clamp(alertClip.length / Mathf.Max(0.05f, source.AlertDuration), 0.5f, 3f));
                animator.CrossFadeInFixedTime(States[target], target == (int)MonsterAnimState.Dead ? 0.15f : 0.1f, 0, 0);
            }
            else if (target == (int)MonsterAnimState.Dead)
            {
                if (Finished(States[target], 0.96f)) animator.speed = 0;   // 쓰러진 마지막 자세에서 멈춘다 (다시 일어나면 위에서 1로 돌아온다)
            }
            else if (target == (int)MonsterAnimState.Locomotion)
            {
                if (damaged && !hit) { hit = true; animator.CrossFadeInFixedTime(HitState, 0.05f, 0, 0); }
                else if (hit && Finished(HitState, 0.9f)) { hit = false; animator.CrossFadeInFixedTime(States[target], 0.15f, 0, 0); }
            }
        }
    }
}
