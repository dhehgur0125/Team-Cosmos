using UnityEngine;
using UnityEngine.AI;

namespace VoidLast.Monster
{
    /// <summary>Presentation only; consumes the existing authoritative/proxy AI state.</summary>
    [DisallowMultipleComponent]
    public sealed class MonsterAssetAnimation : MonoBehaviour
    {
        [SerializeField] Animator animator;
        SmallMonsterAI small;
        BlindMonsterAI blind;
        NavMeshAgent agent;
        Vector3 previousPosition;
        int current = -1;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int State = Animator.StringToHash("State");
        static readonly int[] States = {
            Animator.StringToHash("Base Layer.Locomotion"),
            Animator.StringToHash("Base Layer.Scream"),
            Animator.StringToHash("Base Layer.Attack"),
            Animator.StringToHash("Base Layer.Grope"),
            Animator.StringToHash("Base Layer.Dead"),
            Animator.StringToHash("Base Layer.Stunned")
        };
        void Awake()
        {
            small = GetComponent<SmallMonsterAI>();
            blind = GetComponent<BlindMonsterAI>();
            agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.applyRootMotion = false;
        }
        void OnEnable()
        {
            previousPosition = transform.position;
            current = -1;
            if (small != null) small.StateChanged += OnSmallState;
            if (blind != null) blind.StateChanged += OnBlindState;
        }
        void OnDisable()
        {
            if (small != null) small.StateChanged -= OnSmallState;
            if (blind != null) blind.StateChanged -= OnBlindState;
        }
        void OnSmallState(SmallMonsterAI.Mode mode) { Apply(SmallState(mode)); }
        void OnBlindState(BlindMonsterAI.Mode mode) { Apply(BlindState(mode)); }
        static int SmallState(SmallMonsterAI.Mode mode)
        {
            switch (mode) {
                case SmallMonsterAI.Mode.Scream: return 1;
                case SmallMonsterAI.Mode.Attack: return 2;
                case SmallMonsterAI.Mode.Dead: return 4;
                case SmallMonsterAI.Mode.Stunned: return 5;
                default: return 0;
            }
        }
        static int BlindState(BlindMonsterAI.Mode mode)
        {
            switch (mode) {
                case BlindMonsterAI.Mode.Strike: return 2;
                case BlindMonsterAI.Mode.Grope: return 3;
                case BlindMonsterAI.Mode.Dead: return 4;
                case BlindMonsterAI.Mode.Stunned: return 5;
                default: return 0;
            }
        }
        void Apply(int value)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null || current == value) return;
            current = value;
            animator.SetInteger(State, value);
            animator.CrossFadeInFixedTime(States[value], 0.08f, 0, 0);
        }
        void LateUpdate()
        {
            var delta = transform.position - previousPosition;
            previousPosition = transform.position;
            if (animator == null || animator.runtimeAnimatorController == null) return;
            bool proxy = (small != null && small.NetworkProxy) || (blind != null && blind.NetworkProxy);
            float speed = proxy ? Vector3.ProjectOnPlane(delta, Vector3.up).magnitude / Mathf.Max(Time.deltaTime, 0.0001f)
                : agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0;
            animator.SetFloat(Speed, Mathf.Min(speed, 8), 0.1f, Time.deltaTime);
            Apply(small != null ? SmallState(small.State) : blind != null ? BlindState(blind.State) : 0);
        }
    }
}