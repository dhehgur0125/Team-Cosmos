/* [ 코드 설명 ] NavMesh 경로로 지정 지점 순찰·시각 추격·마지막 목격 위치 조사·시간 제한 수색·복귀를 수행합니다. 보이는 기존 대상을 우선하며 숨은 플레이어의 현재 위치나 소리는 읽지 않습니다. */
using UnityEngine;
using UnityEngine.AI;
using VoidLast.Player;

namespace VoidLast.Monster
{
    [RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth), typeof(MonsterVision))]
    public sealed class EyedMonsterAI : MonoBehaviour
    {
        public enum Mode { Patrol, Chase, Attack, Investigate, Search, Return, Dead }
        [SerializeField] Transform[] patrolPoints;
        [SerializeField] Transform[] searchPoints;
        [SerializeField] Transform visualPivot;
        [SerializeField] Renderer bodyRenderer;
        [SerializeField, Min(0.1f)] float patrolSpeed = 2;
        [SerializeField, Min(0.1f)] float chaseSpeed = 4.5f;
        [SerializeField, Min(0)] float patrolWait = 1.2f;
        [SerializeField, Min(0.1f)] float searchDuration = 10;
        [SerializeField, Min(1)] float searchRadius = 9;
        [SerializeField, Min(0.05f)] float senseInterval = 0.15f;
        NavMeshAgent agent;
        MonsterHealth health;
        MonsterVision vision;
        MonsterMelee melee;
        MaterialPropertyBlock colors;
        bool targetVisible, knowsLocation, hasGoal;
        float nextSense, nextPath, waitUntil, searchEnds, deathBlend;
        int previousPatrol = -1;
        public Mode State { get; private set; }
        public FpsHealth Target { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public Vector3 Destination { get; private set; }
        public int PatrolDestinations { get; private set; }
        public int SearchDestinations { get; private set; }
        public bool NavigationReady => agent != null && agent.enabled && agent.isOnNavMesh;
        void Awake()
        {
            agent = GetComponent<NavMeshAgent>(); health = GetComponent<MonsterHealth>();
            vision = GetComponent<MonsterVision>(); melee = GetComponent<MonsterMelee>(); colors = new MaterialPropertyBlock();
        }
        void OnEnable() { health.Damaged += OnHit; health.Died += Die; }
        void OnDisable() { health.Damaged -= OnHit; health.Died -= Die; if (NavigationReady) agent.isStopped = true; }
        void Start() { if (!NavigationReady) Debug.LogError("Monster requires a baked NavMesh at its spawn position.", this); }
        void Update()
        {
            if (health.IsDead || !NavigationReady) return;
            if (Time.time >= nextSense)
            {
                nextSense = Time.time + senseInterval;
                if (Target != null && (Target.IsDead || !Target.isActiveAndEnabled)) { Target = null; knowsLocation = false; }
                var seen = vision.FindVisible(Target);
                targetVisible = seen != null;
                if (seen != null) { Target = seen; LastKnownPosition = seen.transform.position; knowsLocation = true; }
            }
            agent.updateRotation = !melee.IsBusy;
            if (melee.IsBusy) { Change(Mode.Attack); return; } // Melee owns the stopped agent while lunging/recovering.
            if (targetVisible && Target != null && !Target.IsDead)
            {
                Change(Mode.Chase);
                var close = vision.CanSee(Target) && FlatDistance(Target.transform.position) <= melee.AttackDistance ? Target : vision.FindVisible();
                if (melee.TryStart(close)) { Change(Mode.Attack); Stop(); agent.updateRotation = false; return; }
                if (Time.time >= nextPath) { nextPath = Time.time + 0.25f; MoveTo(LastKnownPosition, chaseSpeed, Mathf.Max(0.3f, melee.AttackDistance - 0.35f)); }
                return;
            }
            if (State == Mode.Chase || State == Mode.Attack) Change(knowsLocation ? Mode.Investigate : Mode.Return);
            switch (State)
            {
                case Mode.Patrol:
                    if (hasGoal && Arrived()) { Stop(); waitUntil = Time.time + patrolWait; }
                    if (!hasGoal && Time.time >= waitUntil) PickPatrol(false);
                    break;
                case Mode.Investigate:
                    if (!hasGoal && !MoveTo(LastKnownPosition, chaseSpeed, 0.2f)) BeginSearch();
                    else if (hasGoal && Arrived()) BeginSearch();
                    break;
                case Mode.Search:
                    if (Time.time >= searchEnds) { Target = null; knowsLocation = false; Change(Mode.Return); break; }
                    if (hasGoal && Arrived()) { Stop(); waitUntil = Time.time + 0.4f; }
                    if (!hasGoal && Time.time >= waitUntil) PickSearch();
                    break;
                case Mode.Return:
                    if (!hasGoal) PickPatrol(true);
                    if (hasGoal && Arrived()) { Change(Mode.Patrol); waitUntil = Time.time + patrolWait; }
                    break;
            }
        }
        float FlatDistance(Vector3 point) => Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;
        bool Arrived() => !agent.pathPending && FlatDistance(Destination) <= agent.stoppingDistance + 0.2f;
        public bool Reachable(Vector3 point, out Vector3 sampled)
        {
            sampled = point;
            if (!NavigationReady || !NavMesh.SamplePosition(point, out var hit, 0.8f, agent.areaMask)) return false;
            var path = new NavMeshPath(); sampled = hit.position;
            return agent.CalculatePath(sampled, path) && path.status == NavMeshPathStatus.PathComplete;
        }
        bool MoveTo(Vector3 point, float speed, float stopDistance)
        {
            if (!Reachable(point, out var sampled)) return false;
            var path = new NavMeshPath();
            if (!agent.CalculatePath(sampled, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            agent.speed = speed; agent.stoppingDistance = stopDistance; agent.isStopped = false; agent.SetPath(path);
            Destination = sampled; hasGoal = true; return true;
        }
        void Stop() { if (NavigationReady) { agent.isStopped = true; agent.ResetPath(); } hasGoal = false; }
        void Change(Mode mode)
        {
            if (State == mode) return;
            State = mode; Stop(); nextPath = 0; waitUntil = 0;
        }
        void PickPatrol(bool nearest)
        {
            if (patrolPoints == null || patrolPoints.Length == 0) { waitUntil = Time.time + 1; return; }
            int start = Random.Range(0, patrolPoints.Length), chosen = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                int index = (start + i) % patrolPoints.Length; var point = patrolPoints[index];
                if (point == null || !nearest && patrolPoints.Length > 1 && index == previousPatrol || !Reachable(point.position, out _)) continue;
                float distance = (point.position - transform.position).sqrMagnitude;
                if (!nearest || distance < best) { chosen = index; best = distance; if (!nearest) break; }
            }
            if (chosen >= 0 && MoveTo(patrolPoints[chosen].position, patrolSpeed, 0.2f)) { previousPatrol = chosen; PatrolDestinations++; }
            else waitUntil = Time.time + 1;
        }
        void BeginSearch() { Change(Mode.Search); searchEnds = Time.time + searchDuration; }
        void PickSearch()
        {
            waitUntil = Time.time + 0.5f;
            if (searchPoints == null || searchPoints.Length == 0) return;
            int first = Random.Range(0, searchPoints.Length);
            for (int i = 0; i < searchPoints.Length; i++)
            {
                var point = searchPoints[(first + i) % searchPoints.Length];
                if (point == null || FlatDistance(point.position) < 0.5f || Vector3.Distance(point.position, LastKnownPosition) > searchRadius) continue;
                if (MoveTo(point.position, patrolSpeed, 0.2f)) { SearchDestinations++; return; }
            }
        }
        void OnHit(GameObject attacker, Vector3 position)
        {
            if (Target != null && !Target.IsDead && vision.CanSee(Target)) return;
            var shooter = attacker != null ? attacker.GetComponent<FpsHealth>() : null;
            if (vision.CanSee(shooter, true))
            {
                var direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
                if (!melee.IsBusy && direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
                Target = shooter; LastKnownPosition = position; knowsLocation = targetVisible = true; Change(Mode.Chase);
            }
            else if (Target == null)
            {
                LastKnownPosition = position; knowsLocation = true; targetVisible = false; Change(Mode.Investigate);
            }
        }
        void Die()
        {
            Change(Mode.Dead); melee.Cancel(); Target = null; targetVisible = false;
            if (agent.enabled) agent.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        }
        void LateUpdate()
        {
            if (bodyRenderer != null)
            {
                Color color = State == Mode.Dead ? new Color(0.09f, 0.09f, 0.1f) : State == Mode.Attack ? new Color(1, 0.35f, 0.05f) : State == Mode.Chase ? new Color(0.8f, 0.08f, 0.06f) : State == Mode.Search || State == Mode.Investigate ? new Color(0.85f, 0.65f, 0.04f) : new Color(0.22f, 0.45f, 0.52f);
                colors.SetColor("_BaseColor", color); bodyRenderer.SetPropertyBlock(colors);
            }
            if (visualPivot == null) return;
            deathBlend = Mathf.MoveTowards(deathBlend, health.IsDead ? 1 : 0, Time.deltaTime * 2);
            visualPivot.localRotation = Quaternion.Euler(melee.Lean * (1 - deathBlend), 0, deathBlend * 85);
            visualPivot.localPosition = Vector3.up * deathBlend * 0.7f;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            if (patrolPoints != null) foreach (var point in patrolPoints) if (point != null) Gizmos.DrawWireSphere(point.position + Vector3.up * 0.1f, 0.25f);
            Gizmos.color = Color.yellow;
            if (searchPoints != null) foreach (var point in searchPoints) if (point != null) Gizmos.DrawWireCube(point.position + Vector3.up * 0.15f, Vector3.one * 0.3f);
            if (Application.isPlaying && knowsLocation) Gizmos.DrawWireSphere(LastKnownPosition, 0.4f);
        }
    }
}
