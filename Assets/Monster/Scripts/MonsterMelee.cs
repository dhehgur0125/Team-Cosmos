/* [ 코드 설명 ] 짧은 준비 뒤 방향을 고정하고 NavMesh·Capsule 충돌로 제한한 전방 돌진 공격을 수행합니다. 돌진 중 전방 판정만 검사하며 중복 피해·벽 너머 피해를 막고 회복 또는 사망 시 이동 제어를 반환합니다. */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VoidLast.Player;

namespace VoidLast.Monster
{
    public sealed class MonsterMelee : MonoBehaviour
    {
        public enum AttackPhase { Ready, Windup, Strike, Recovery }
        [SerializeField, Min(0)] float damage = 30;
        [SerializeField, Min(0.1f)] float attackDistance = 2.5f;
        [SerializeField, Min(0.01f)] float windup = 0.3f;
        [SerializeField, Min(0)] float directionLockLead = 0.12f;
        [SerializeField, Min(0)] float lungeDistance = 1.5f;
        [SerializeField, Min(0.01f)] float lungeDuration = 0.2f;
        [SerializeField, Min(0)] float recoveryDuration = 0.8f;
        [SerializeField, Min(0), Tooltip("Delay from lunge start until the forward hit volume becomes active.")] float hitStartDelay = 0.08f;
        [SerializeField, Min(0.1f)] float hitReach = 2;
        [SerializeField, Min(0.1f)] float hitWidth = 1;
        [SerializeField, Min(0.1f)] float hitHeight = 1.9f;
        [SerializeField, Min(0)] float hitCenterHeight = 1.1f;
        [SerializeField, Min(1)] float turnSpeed = 360;
        [SerializeField, Min(0.005f)] float collisionPadding = 0.03f;
        [SerializeField] LayerMask obstructionMask = ~0;
        MonsterHealth health;
        MonsterVision vision;
        NavMeshAgent agent;
        CapsuleCollider body;
        FpsHealth victim;
        float elapsed;
        bool ownsMovement, previousUpdatePosition, previousUpdateRotation, lungeBlocked;
        Quaternion lockedRotation;
        readonly HashSet<FpsHealth> struck = new HashSet<FpsHealth>();
        public bool IsBusy { get; private set; }
        public bool DirectionLocked { get; private set; }
        public AttackPhase Phase { get; private set; }
        public float AttackDistance => attackDistance;
        public float Windup => windup;
        public float LungeDuration => lungeDuration;
        public float LungeDistance => lungeDistance;
        public float RecoveryDuration => recoveryDuration;
        public float Damage => damage;
        public int Swings { get; private set; }
        public int PlayersHit { get; private set; }
        public float Lean => Phase == AttackPhase.Windup ? -18 * Mathf.Clamp01(elapsed / windup) : Phase == AttackPhase.Strike ? Mathf.Lerp(-18, 25, (elapsed - windup) / lungeDuration) : Phase == AttackPhase.Recovery ? 25 * (1 - Mathf.InverseLerp(windup + lungeDuration, windup + lungeDuration + recoveryDuration, elapsed)) : 0;
        bool NavigationReady => agent != null && agent.enabled && agent.isOnNavMesh;
        void Awake() { health = GetComponent<MonsterHealth>(); vision = GetComponent<MonsterVision>(); agent = GetComponent<NavMeshAgent>(); body = GetComponent<CapsuleCollider>(); }
        void OnEnable() => health.Died += Cancel;
        void OnDisable() { health.Died -= Cancel; Cancel(); }
        public bool TryStart(FpsHealth target)
        {
            if (IsBusy || health.IsDead || !NavigationReady || body == null || target == null || target.IsDead || !vision.CanSee(target)) return false;
            if (Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up).magnitude > attackDistance) return false;
            victim = target; elapsed = 0; lungeBlocked = false; struck.Clear(); DirectionLocked = false; IsBusy = true; Phase = AttackPhase.Windup; Swings++;
            previousUpdatePosition = agent.updatePosition; previousUpdateRotation = agent.updateRotation; ownsMovement = true;
            agent.isStopped = true; agent.ResetPath(); agent.updatePosition = false; agent.updateRotation = false;
            return true;
        }
        void Update()
        {
            if (!IsBusy) return;
            if (health.IsDead || !NavigationReady) { Cancel(); return; }
            float previous = elapsed;
            elapsed += Time.deltaTime;
            if (!DirectionLocked)
            {
                if (victim != null && !victim.IsDead)
                {
                    Vector3 direction = Vector3.ProjectOnPlane(victim.transform.position - transform.position, Vector3.up);
                    float trackingTime = Mathf.Max(0, Mathf.Min(elapsed, windup - directionLockLead) - previous);
                    if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * trackingTime);
                }
                if (elapsed >= Mathf.Max(0, windup - directionLockLead)) { DirectionLocked = true; lockedRotation = transform.rotation; }
            }
            if (DirectionLocked) transform.rotation = lockedRotation;
            float from = Mathf.Clamp(previous - windup, 0, lungeDuration);
            float to = Mathf.Clamp(elapsed - windup, 0, lungeDuration);
            if (to > from)
            {
                // Short spatial steps keep both solid collision and hit checks from tunnelling on slow frames.
                int steps = Mathf.Max(1, Mathf.CeilToInt(lungeDistance * (to - from) / lungeDuration / 0.1f));
                for (int i = 1; i <= steps; i++)
                {
                    if (!lungeBlocked) Advance(lungeDistance * (to - from) / lungeDuration / steps);
                    if (Mathf.Lerp(from, to, (float)i / steps) >= hitStartDelay) Strike();
                }
            }
            Phase = elapsed < windup ? AttackPhase.Windup : elapsed < windup + lungeDuration ? AttackPhase.Strike : AttackPhase.Recovery;
            if (elapsed >= windup + lungeDuration + recoveryDuration) Cancel();
        }
        void Advance(float distance)
        {
            Vector3 direction = lockedRotation * Vector3.forward;
            Vector3 start = transform.position;
            float allowed = distance;
            if (agent.Raycast(start + direction * distance, out var edge)) allowed = Mathf.Min(allowed, Mathf.Max(0, Vector3.ProjectOnPlane(edge.position - start, Vector3.up).magnitude - collisionPadding));
            Vector3 scale = transform.lossyScale, center = transform.TransformPoint(body.center);
            float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float half = Mathf.Max(0, body.height * Mathf.Abs(scale.y) * 0.5f - radius);
            Vector3 bottom = center - Vector3.up * half, top = center + Vector3.up * half;
            float castRadius = Mathf.Max(0.01f, radius - 0.01f); // Keep the floor contact out of a horizontal cast.
            foreach (var collider in Physics.OverlapCapsule(bottom, top, castRadius, obstructionMask, QueryTriggerInteraction.Ignore))
                if (!collider.transform.IsChildOf(transform)) { allowed = 0; break; }
            foreach (var hit in Physics.CapsuleCastAll(bottom, top, castRadius, direction, distance + collisionPadding, obstructionMask, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform)) allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - collisionPadding));
            if (allowed > 0)
            {
                agent.Move(direction * allowed);
                transform.position = agent.nextPosition;
                Physics.SyncTransforms();
            }
            if (allowed < distance - 0.001f) lungeBlocked = true;
        }
        void Strike()
        {
            Vector3 center = transform.position + Vector3.up * hitCenterHeight + transform.forward * hitReach * 0.5f;
            foreach (var collider in Physics.OverlapBox(center, new Vector3(hitWidth * 0.5f, hitHeight * 0.5f, hitReach * 0.5f), lockedRotation, ~0, QueryTriggerInteraction.Ignore))
            {
                var player = collider.GetComponentInParent<FpsHealth>();
                if (player == null || player.IsDead || !player.isActiveAndEnabled || struck.Contains(player)) continue;
                if (!vision.ClearLine(transform.position + Vector3.up * 1.3f, collider.bounds.center, player.transform)) continue;
                struck.Add(player); player.TakeDamage(damage); PlayersHit++;
            }
        }
        public void Cancel()
        {
            if (ownsMovement && agent != null)
            {
                if (NavigationReady) agent.nextPosition = transform.position;
                agent.updatePosition = previousUpdatePosition; agent.updateRotation = previousUpdateRotation;
            }
            ownsMovement = false; IsBusy = false; Phase = AttackPhase.Ready; DirectionLocked = false; victim = null;
        }
        void OnValidate()
        {
            windup = Mathf.Max(0.01f, windup); lungeDuration = Mathf.Max(0.01f, lungeDuration);
            directionLockLead = Mathf.Clamp(directionLockLead, 0, windup);
            hitStartDelay = Mathf.Clamp(hitStartDelay, 0, lungeDuration);
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1, 0.25f, 0.1f); var old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0, hitCenterHeight, hitReach * 0.5f), new Vector3(hitWidth, hitHeight, hitReach));
            Gizmos.color = Color.magenta; Gizmos.DrawLine(Vector3.zero, Vector3.forward * lungeDistance);
            Gizmos.DrawWireSphere(Vector3.forward * lungeDistance, 0.1f); Gizmos.matrix = old;
        }
    }
}
