/* [ 코드 설명 ] NavMesh 경로로 지정 지점 순찰(서로 가까운 지점끼리만 이어 걸으며 가까운 미방문 지점 우선, 모두 방문하면 새로 한 바퀴)·시각 추격·마지막 목격 위치 조사·시간 제한 수색·복귀를 수행합니다. 보이는 기존 대상을 우선하며 숨은 플레이어의 현재 위치나 소리는 읽지 않습니다. */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VoidLast.Player;

namespace VoidLast.Monster
{
    [RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth), typeof(MonsterVision))]
    public sealed class EyedMonsterAI : MonoBehaviour
    {
        public enum Mode { Patrol, Chase, Attack, Investigate, Search, Return, Dead, Stunned }
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
        [SerializeField, Min(1), Tooltip("순찰 중 한 번에 이동하는 최대 경로 길이. 이 안의 지점끼리만 이어 걸어 방을 가로지르는 먼 이동을 막는다.")] float patrolHopDistance = 20;
        [SerializeField, Range(0, 1), Tooltip("가장 가까운 미방문 지점 대신 두 번째로 가까운 미방문 지점을 목표로 삼을 확률.")] float patrolRandomness = 0.25f;
        NavMeshAgent agent;
        MonsterHealth health;
        MonsterVision vision;
        MonsterMelee melee;
        MonsterStun stun;
        MaterialPropertyBlock colors;
        bool targetVisible, knowsLocation, hasGoal;
        float nextSense, nextPath, waitUntil, searchEnds, deathBlend;
        int previousPatrol = -1;
        bool[] patrolVisited; // 이번 바퀴에 들른 지점
        int[][] patrolLinks; float[][] patrolLinkLengths; // patrolHopDistance 이내로 이어진 이웃 지점과 경로 길이
        public Mode State { get; private set; }
        /// <summary>멀티플레이: 방장(서버)이 아닌 화면의 복제본이면 AI를 돌리지 않고 서버가 보낸 상태로 연출만 한다.</summary>
        public bool NetworkProxy { get; set; }
        /// <summary>밸런스 설정(GameBalance) 값 적용</summary>
        public void ApplyBalance(float chase) { chaseSpeed = chase; }
        /// <summary>복제본 전용: 서버 AI의 상태를 받아 색·자세 연출에 쓴다.</summary>
        public void ApplyNetworkState(int mode)
        {
            if (!NetworkProxy || (int)State == mode) return;
            State = (Mode)mode;
        }
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
        void OnEnable() { health.Damaged += OnHit; health.Died += Die; health.Revived += GetUp; }
        void OnDisable() { health.Damaged -= OnHit; health.Died -= Die; health.Revived -= GetUp; if (NavigationReady) agent.isStopped = true; }
        void Start() { if (!NetworkProxy && !NavigationReady) Debug.LogError("Monster requires a baked NavMesh at its spawn position.", this); }
        void Update()
        {
            if (NetworkProxy || health.IsDead || !NavigationReady) return;
            if (State == Mode.Stunned)
            {
                if (stun != null && stun.IsStunned) return;
                targetVisible = false; nextSense = 0; Change(knowsLocation ? Mode.Investigate : Mode.Return);
            }
            if (Time.time >= nextSense)
            {
                nextSense = Time.time + senseInterval;
                if (Target != null && (Target.IsDead || !Target.isActiveAndEnabled || StairSafeZone.Contains(Target))) { Target = null; knowsLocation = false; }
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
            if (patrolVisited == null || patrolVisited.Length != patrolPoints.Length) { patrolVisited = new bool[patrolPoints.Length]; BuildPatrolLinks(); }
            int chosen = nearest || previousPatrol < 0 ? NearestPatrol() : NextPatrol();
            if (chosen >= 0 && MoveTo(patrolPoints[chosen].position, patrolSpeed, 0.2f)) { previousPatrol = chosen; patrolVisited[chosen] = true; PatrolDestinations++; }
            else waitUntil = Time.time + 1;
        }
        int NearestPatrol()
        {
            int chosen = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                var point = patrolPoints[i];
                if (point == null || !Reachable(point.position, out _)) continue;
                float distance = (point.position - transform.position).sqrMagnitude;
                if (distance < best) { chosen = i; best = distance; }
            }
            return chosen;
        }
        /// <summary>경로 길이가 patrolHopDistance 이내인 지점끼리 이웃으로 묶는다. 순찰은 이웃 사이로만 이동하므로 먼 지점으로 방을 가로질러 가지 않는다.</summary>
        void BuildPatrolLinks()
        {
            int n = patrolPoints.Length; var path = new NavMeshPath();
            var links = new List<int>[n]; var lengths = new List<float>[n];
            for (int i = 0; i < n; i++) { links[i] = new List<int>(); lengths[i] = new List<float>(); }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (patrolPoints[i] == null || patrolPoints[j] == null || Vector3.Distance(patrolPoints[i].position, patrolPoints[j].position) > patrolHopDistance) continue;
                    if (!NavMesh.SamplePosition(patrolPoints[i].position, out var a, 0.8f, agent.areaMask) || !NavMesh.SamplePosition(patrolPoints[j].position, out var b, 0.8f, agent.areaMask)) continue;
                    if (!NavMesh.CalculatePath(a.position, b.position, agent.areaMask, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                    float length = 0;
                    for (int c = 1; c < path.corners.Length; c++) length += Vector3.Distance(path.corners[c - 1], path.corners[c]);
                    if (length > patrolHopDistance) continue;
                    links[i].Add(j); lengths[i].Add(length); links[j].Add(i); lengths[j].Add(length);
                }
            patrolLinks = new int[n][]; patrolLinkLengths = new float[n][];
            for (int i = 0; i < n; i++) { patrolLinks[i] = links[i].ToArray(); patrolLinkLengths[i] = lengths[i].ToArray(); }
        }
        /// <summary>이웃 지점을 따라 가장 가까운 미방문 지점 쪽으로 한 칸 이동할 지점을 고른다. 모두 들렀으면 새 바퀴를 시작한다.</summary>
        int NextPatrol()
        {
            int n = patrolPoints.Length, current = previousPatrol;
            if (n == 1) return 0;
            if (patrolPoints[current] == null || Vector3.ProjectOnPlane(patrolPoints[current].position - transform.position, Vector3.up).sqrMagnitude > 4) return NearestPatrol();
            var distance = new float[n]; var parent = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { distance[i] = float.PositiveInfinity; parent[i] = -1; }
            distance[current] = 0;
            for (int step = 0; step < n; step++)
            {
                int u = -1;
                for (int i = 0; i < n; i++) if (!done[i] && (u < 0 || distance[i] < distance[u])) u = i;
                if (u < 0 || float.IsPositiveInfinity(distance[u])) break;
                done[u] = true;
                for (int k = 0; k < patrolLinks[u].Length; k++)
                {
                    int v = patrolLinks[u][k]; float through = distance[u] + patrolLinkLengths[u][k];
                    if (through < distance[v]) { distance[v] = through; parent[v] = u; }
                }
            }
            for (int lap = 0; lap < 2; lap++)
            {
                int best = -1, second = -1; float bestScore = float.PositiveInfinity, secondScore = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (i == current || patrolVisited[i] || patrolPoints[i] == null) continue;
                    float score = distance[i];
                    if (float.IsPositiveInfinity(score))
                    {
                        // 이웃으로 이어지지 않은 지점(지점이 드문 맵)은 기존처럼 직접 이동한다.
                        if (!Reachable(patrolPoints[i].position, out _)) continue;
                        score = 100000 + Vector3.Distance(patrolPoints[i].position, transform.position);
                    }
                    if (score < bestScore) { second = best; secondScore = bestScore; best = i; bestScore = score; }
                    else if (score < secondScore) { second = i; secondScore = score; }
                }
                if (best >= 0)
                {
                    int goal = second >= 0 && Random.value < patrolRandomness ? second : best;
                    while (parent[goal] >= 0 && parent[goal] != current) goal = parent[goal];
                    return goal;
                }
                System.Array.Clear(patrolVisited, 0, n); patrolVisited[current] = true; // 한 바퀴를 다 돌았다.
            }
            return -1;
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
            if (State == Mode.Stunned) return;
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
        public void BeginStun(MonsterStun effect)
        {
            if (health.IsDead || NetworkProxy) return;
            stun = effect; melee.Cancel(); Change(Mode.Stunned); Stop();
            if (NavigationReady) { agent.velocity = Vector3.zero; agent.updateRotation = false; }
        }
        void Die()
        {
            Change(Mode.Dead); melee.Cancel(); Target = null; targetVisible = false;
            if (agent.enabled) agent.enabled = false;
            downColliders.Clear();
            foreach (var collider in GetComponentsInChildren<Collider>()) if (collider.enabled) { collider.enabled = false; downColliders.Add(collider); }
        }
        readonly List<Collider> downColliders = new List<Collider>(); // 쓰러질 때 끈 충돌체 (다시 일어날 때 켠다)
        /// <summary>그로기가 끝나 다시 일어난다: 충돌체와 길 찾기를 되살리고 순찰로 돌아간다.</summary>
        void GetUp()
        {
            foreach (var collider in downColliders) if (collider != null) collider.enabled = true;
            downColliders.Clear();
            if (NetworkProxy) return;   // 복제본의 상태는 서버가 보내 준다
            agent.enabled = true;
            Target = null; targetVisible = knowsLocation = false; nextSense = 0;
            Change(Mode.Return);
        }
        void LateUpdate()
        {
            if (bodyRenderer != null)
            {
                Color color = State == Mode.Dead ? new Color(0.09f, 0.09f, 0.1f) : State == Mode.Attack ? new Color(1, 0.35f, 0.05f) : State == Mode.Chase ? new Color(0.8f, 0.08f, 0.06f) : State == Mode.Search || State == Mode.Investigate ? new Color(0.85f, 0.65f, 0.04f) : new Color(0.22f, 0.45f, 0.52f);
                if (State == Mode.Stunned) color = new Color(0.45f, 0.8f, 1);
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
