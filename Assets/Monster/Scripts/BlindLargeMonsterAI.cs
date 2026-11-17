/* [ 코드 설명 ] 눈 없는 대형 몬스터의 AI입니다. 시야가 전혀 없고 소리와 아주 가까이 있는 대원만 알아챕니다.
   평소에는 정해진 지점을 천천히 순찰하다가, 소리(스캔·총소리·쪼꼬미 비명·발소리)가 들리면 대원이 아니라 "소리가 난 위치"로 느리게 걸어갑니다.
   도착하면 약 20초 동안 그 주변을 돌아다니며 통로를 막고, 새 소리가 나면 그쪽으로 갑니다. 소리가 없으면 순찰로 돌아갑니다.
   어느 상태에서든 가까이(벽에 가려지지 않은 3.5m 안) 대원이 있으면 그쪽으로 돌아서 느리게 내려칩니다.
   촉수형(BlindMonsterAI)과의 차이: 스캔을 더 멀리서(35m) 듣고, 느리게 오지만 오래 머뭅니다. */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VoidLast.Player;

namespace VoidLast.Monster
{
    [RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
    public sealed class BlindLargeMonsterAI : MonoBehaviour, IMonsterAnimationSource
    {
        public enum Mode { Patrol, Investigate, Search, Strike, Dead, Stunned }

        [Header("순찰 (평소)")]
        [Tooltip("비워두면 처음 위치 주변의 무작위 지점을 돌아다닙니다.")]
        [SerializeField] Transform[] patrolPoints;
        [SerializeField, Min(1)] float roamRadius = 25;
        [SerializeField, Min(0.1f)] float patrolSpeed = 1.0f;
        [SerializeField, Min(0)] float patrolWait = 2f;

        [Header("소리 듣기")]
        [Tooltip("스캔 소리가 들리는 거리(m). 촉수형은 20m.")]
        [SerializeField, Min(1)] float scanHearRadius = 35;
        [Tooltip("소리가 난 층과 높이가 이만큼 넘게 차이 나면 못 듣는다 (총소리·비명·발소리).")]
        [SerializeField, Min(0.5f)] float floorTolerance = 3.5f;

        [Header("소리 조사 / 수색")]
        [SerializeField, Min(0.1f)] float investigateSpeed = 1.8f;
        [SerializeField, Min(1)] float investigateTimeout = 60;
        [SerializeField, Min(1)] float searchDuration = 20;
        [SerializeField, Min(0.5f)] float searchRadius = 6;
        [SerializeField, Min(0.1f)] float searchSpeed = 1.0f;

        [Header("가까이 있는 대원 감지")]
        [Tooltip("이 거리 안(벽에 가려지지 않은)에 대원이 있으면 서 있든 앉아 있든 알아챈다.")]
        [SerializeField, Min(0.5f)] float proximityRadius = 3.5f;
        [SerializeField, Min(0)] float senseHeight = 1.0f;

        [Header("내려치기")]
        [SerializeField, Min(0)] float damage = 45;
        [SerializeField, Min(0.05f)] float strikeWindup = 1.0f;
        [SerializeField, Min(0.5f)] float strikeReach = 3.0f;
        [SerializeField, Range(1, 360)] float strikeAngle = 150;
        [SerializeField, Min(0.1f)] float strikeRecovery = 1.5f;
        [SerializeField, Min(1)] float turnSpeed = 120;

        NavMeshAgent agent;
        MonsterHealth health;
        MonsterStun stun;
        readonly List<Collider> downColliders = new List<Collider>();
        FpsHealth strikeVictim;
        Vector3 home, noisePoint, searchCenter, pendingScanPoint;
        bool hasGoal, pendingScan, struck;
        float nextSense, waitUntil, stateStart, searchEnds, strikeStart, pendingScanTime;
        int previousPatrol = -1;

        public Mode State { get; private set; }
        /// <summary>멀티플레이: 방장(서버)이 아닌 화면의 복제본이면 AI를 돌리지 않고 서버가 보낸 상태로 연출만 한다.</summary>
        public bool NetworkProxy { get; set; }
        /// <summary>복제본 전용: 서버 AI의 상태를 받는다.</summary>
        public void ApplyNetworkState(int mode)
        {
            if (!NetworkProxy || (int)State == mode) return;
            State = (Mode)mode;
            stateStart = Time.time;
            if (State == Mode.Strike) { strikeStart = Time.time; StrikeCount++; }
            StateChanged?.Invoke(State);
        }
        public Vector3 Destination { get; private set; }
        public Vector3 NoisePoint => noisePoint;
        public int SoundsHeard { get; private set; }
        public int StrikeCount { get; private set; }
        public int StrikesLanded { get; private set; }
        public event System.Action<Mode> StateChanged;
        public bool NavigationReady => agent != null && agent.enabled && agent.isOnNavMesh;

        // 애니메이션 연출용
        public MonsterAnimState AnimState => State == Mode.Strike ? MonsterAnimState.Attack : State == Mode.Dead ? MonsterAnimState.Dead
            : State == Mode.Stunned ? MonsterAnimState.Stunned : MonsterAnimState.Locomotion;
        public int AttackCount => StrikeCount;
        public float AttackDuration => strikeWindup + strikeRecovery;
        public float AlertDuration => 1;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<MonsterHealth>();
        }

        void OnEnable()
        {
            health.Damaged += OnHit;
            health.Died += Die;
            health.Revived += GetUp;
            ScanNoise.Emitted += OnScanHeard;
            GunNoise.Emitted += OnNoiseHeard;
            ScreamNoise.Emitted += OnNoiseHeard;
            MoveNoise.Emitted += OnNoiseHeard;
        }

        void OnDisable()
        {
            health.Damaged -= OnHit;
            health.Died -= Die;
            health.Revived -= GetUp;
            ScanNoise.Emitted -= OnScanHeard;
            GunNoise.Emitted -= OnNoiseHeard;
            ScreamNoise.Emitted -= OnNoiseHeard;
            MoveNoise.Emitted -= OnNoiseHeard;
            if (NavigationReady) agent.isStopped = true;
        }

        void Start()
        {
            home = transform.position;
            if (!NetworkProxy && !NavigationReady) Debug.LogError("BlindLargeMonsterAI는 생성 위치에 구워진 NavMesh가 필요합니다.", this);
        }

        void Update()
        {
            if (NetworkProxy || health.IsDead || !NavigationReady) return;
            if (State == Mode.Stunned)
            {
                if (stun != null && stun.IsStunned) return;
                Change(Mode.Patrol); nextSense = 0;
            }

            // 스캔 파동이 이 몬스터에 닿는 시간이 되면 소리를 '들은' 것으로 처리
            if (pendingScan && Time.time >= pendingScanTime)
            {
                pendingScan = false;
                HearAt(pendingScanPoint);
            }

            agent.updateRotation = State != Mode.Strike;
            if (State == Mode.Strike) { TickStrike(); return; }

            // 어느 상태에서든 가까이 있는 대원은 알아챈다
            if (Time.time >= nextSense)
            {
                nextSense = Time.time + 0.2f;
                var nearby = NearestInReach(proximityRadius);
                if (nearby != null) { BeginStrike(nearby); return; }
            }

            switch (State)
            {
                case Mode.Patrol:
                    if (hasGoal && Arrived()) { Stop(); waitUntil = Time.time + patrolWait * Random.Range(0.6f, 1.4f); }
                    if (!hasGoal && Time.time >= waitUntil) PickPatrolPoint();
                    break;

                case Mode.Investigate:
                    if (Time.time - stateStart > investigateTimeout) BeginSearch(transform.position);
                    else if (!hasGoal || Arrived()) BeginSearch(noisePoint);
                    break;

                case Mode.Search:
                    if (Time.time >= searchEnds) { Change(Mode.Patrol); waitUntil = Time.time + 1f; break; }
                    if (hasGoal && Arrived()) { Stop(); waitUntil = Time.time + Random.Range(1f, 2.5f); }
                    if (!hasGoal && Time.time >= waitUntil) PickSearchPoint();
                    break;
            }
        }

        // ------------------------------------------------------------------ 소리 듣기

        // 스캔: 자기만의 거리(35m)로 듣고, 파동이 닿는 시간만큼 늦게 반응한다
        void OnScanHeard(Vector3 origin, float radius, float waveSpeed)
        {
            if (NetworkProxy || health.IsDead || State == Mode.Stunned) return;
            float distance = Vector3.Distance(transform.position, origin);
            if (distance > scanHearRadius) return;
            SoundsHeard++;
            pendingScan = true;
            pendingScanPoint = origin;
            pendingScanTime = Time.time + (waveSpeed > 0f ? distance / waveSpeed : 0f);
        }

        // 총소리·쪼꼬미 비명·발소리: 같은 층에서 들리는 거리 안이면 그 위치로 간다
        void OnNoiseHeard(Vector3 origin, float radius)
        {
            if (NetworkProxy || health.IsDead || State == Mode.Stunned) return;
            Vector3 delta = transform.position - origin;
            if (Mathf.Abs(delta.y) > floorTolerance || delta.sqrMagnitude > radius * radius) return;
            SoundsHeard++;
            HearAt(origin);
        }

        /// <summary>소리가 난 위치로 걸어간다 (이미 가는 중이거나 수색 중이면 새 위치로 목표를 바꾼다)</summary>
        public void HearAt(Vector3 point)
        {
            if (NetworkProxy || health.IsDead || State == Mode.Strike || State == Mode.Stunned) return;
            noisePoint = point;
            if (State != Mode.Investigate) Change(Mode.Investigate);
            stateStart = Time.time;
            if (!MoveTo(point, investigateSpeed, 1.0f, 3f)) BeginSearch(transform.position);
        }

        // ------------------------------------------------------------------ 수색 (도착한 곳을 오래 지킨다)

        void BeginSearch(Vector3 center)
        {
            searchCenter = center;
            Change(Mode.Search);
            searchEnds = Time.time + searchDuration;
            waitUntil = Time.time + 0.8f;
        }

        void PickSearchPoint()
        {
            waitUntil = Time.time + 0.5f;
            for (int i = 0; i < 6; i++)
                if (RandomPoint(searchCenter, searchRadius, out var point) && MoveTo(point, searchSpeed, 0.3f)) return;
        }

        // ------------------------------------------------------------------ 내려치기

        void BeginStrike(FpsHealth victim)
        {
            bool searching = State == Mode.Search || State == Mode.Investigate;
            Change(Mode.Strike);
            strikeVictim = victim;
            strikeStart = Time.time;
            struck = false;
            StrikeCount++;
            // 공격이 끝나면 그 대원이 있던 곳을 계속 수색한다
            searchCenter = victim.transform.position;
            searchEnds = Mathf.Max(searching ? searchEnds : 0, Time.time + strikeWindup + strikeRecovery + 6f);
        }

        void TickStrike()
        {
            float t = Time.time - strikeStart;
            // 준비 동작의 앞 70% 동안만 대상을 향해 돈다 → 뒤로 돌아가면 피할 수 있다
            if (strikeVictim != null && !strikeVictim.IsDead && t < strikeWindup * 0.7f) FaceTowards(strikeVictim.transform.position, turnSpeed);
            if (!struck && t >= strikeWindup) { struck = true; DoStrike(); }
            if (t >= strikeWindup + strikeRecovery)
            {
                strikeVictim = null;
                Change(Mode.Search);
                waitUntil = Time.time + 0.5f;
            }
        }

        void DoStrike()
        {
            Vector3 chest = transform.position + Vector3.up * senseHeight;
            foreach (var player in FindObjectsByType<FpsHealth>(FindObjectsInactive.Exclude))
            {
                if (player == null || player.IsDead) continue;
                Vector3 closest = BoundsOf(player).ClosestPoint(chest);
                Vector3 flat = Vector3.ProjectOnPlane(closest - chest, Vector3.up);
                if (Vector3.Distance(chest, closest) > strikeReach) continue;
                if (flat.sqrMagnitude > 0.04f && Vector3.Angle(transform.forward, flat) > strikeAngle * 0.5f) continue;
                if (!ClearTouch(chest, closest, player.transform)) continue;
                player.TakeDamage(damage);
                StrikesLanded++;
            }
        }

        // ------------------------------------------------------------------ 가까운 대원 감지

        FpsHealth NearestInReach(float radius)
        {
            Vector3 chest = transform.position + Vector3.up * senseHeight;
            FpsHealth best = null;
            float bestDistance = radius;
            foreach (var candidate in FindObjectsByType<FpsHealth>(FindObjectsInactive.Exclude))
            {
                if (candidate == null || candidate.IsDead) continue;
                Vector3 closest = BoundsOf(candidate).ClosestPoint(chest);
                if (Mathf.Abs(closest.y - chest.y) > 2.5f) continue;
                float distance = Vector3.ProjectOnPlane(closest - chest, Vector3.up).magnitude;
                if (distance < bestDistance && ClearTouch(chest, closest, candidate.transform)) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        static Bounds BoundsOf(FpsHealth player)
        {
            var controller = player.GetComponent<CharacterController>();
            return controller != null ? controller.bounds : new Bounds(player.transform.position + Vector3.up, Vector3.one);
        }

        bool ClearTouch(Vector3 from, Vector3 to, Transform target)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            foreach (var hit in Physics.RaycastAll(from, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(target)) return false;
            return true;
        }

        // ------------------------------------------------------------------ 순찰

        void PickPatrolPoint()
        {
            waitUntil = Time.time + 0.5f;
            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                int start = Random.Range(0, patrolPoints.Length);
                for (int i = 0; i < patrolPoints.Length; i++)
                {
                    int index = (start + i) % patrolPoints.Length;
                    var point = patrolPoints[index];
                    if (point == null || (patrolPoints.Length > 1 && index == previousPatrol)) continue;
                    if (MoveTo(point.position, patrolSpeed, 0.3f)) { previousPatrol = index; return; }
                }
                return;
            }
            if (RandomPoint(home, roamRadius, out var destination)) MoveTo(destination, patrolSpeed, 0.3f);
        }

        // ------------------------------------------------------------------ 이동 도우미

        float FlatDistance(Vector3 point) => Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;
        bool Arrived() => !agent.pathPending && FlatDistance(Destination) <= agent.stoppingDistance + 0.25f;

        bool MoveTo(Vector3 point, float speed, float stopDistance, float sampleRange = 1.5f)
        {
            if (!NavigationReady || !NavMesh.SamplePosition(point, out var hit, sampleRange, agent.areaMask)) return false;
            var path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            agent.speed = speed; agent.stoppingDistance = stopDistance; agent.isStopped = false; agent.SetPath(path);
            Destination = hit.position; hasGoal = true;
            return true;
        }

        bool RandomPoint(Vector3 center, float radius, out Vector3 result)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 circle = Random.insideUnitCircle * radius;
                var candidate = new Vector3(center.x + circle.x, transform.position.y, center.z + circle.y);
                if (NavMesh.SamplePosition(candidate, out var hit, 3f, agent.areaMask)) { result = hit.position; return true; }
            }
            result = center;
            return false;
        }

        void Stop()
        {
            if (NavigationReady) { agent.isStopped = true; agent.ResetPath(); }
            hasGoal = false;
        }

        void Change(Mode mode)
        {
            if (State == mode) return;
            State = mode;
            Stop();
            waitUntil = 0;
            stateStart = Time.time;
            StateChanged?.Invoke(mode);
        }

        void FaceTowards(Vector3 point, float degreesPerSecond)
        {
            Vector3 direction = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (direction.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degreesPerSecond * Time.deltaTime);
        }

        // ------------------------------------------------------------------ 피격 / 스턴 / 그로기

        // 눈이 없어도 맞은 방향은 느낀다 → 쏜 위치로 간다
        void OnHit(GameObject attacker, Vector3 position)
        {
            if (State == Mode.Strike || State == Mode.Stunned) return;
            HearAt(position);
        }

        public void BeginStun(MonsterStun effect)
        {
            if (health.IsDead || NetworkProxy) return;
            stun = effect; pendingScan = false; strikeVictim = null; struck = true;
            Change(Mode.Stunned); Stop();
            if (NavigationReady) { agent.velocity = Vector3.zero; agent.updateRotation = false; }
        }

        void Die()
        {
            Change(Mode.Dead);
            pendingScan = false; strikeVictim = null;
            if (agent.enabled) agent.enabled = false;
            downColliders.Clear();
            foreach (var collider in GetComponentsInChildren<Collider>()) if (collider.enabled) { collider.enabled = false; downColliders.Add(collider); }
        }

        /// <summary>그로기가 끝나 다시 일어난다: 충돌체와 길 찾기를 되살리고 순찰로 돌아간다.</summary>
        void GetUp()
        {
            foreach (var collider in downColliders) if (collider != null) collider.enabled = true;
            downColliders.Clear();
            if (NetworkProxy) return;   // 복제본의 상태는 서버가 보내 준다
            agent.enabled = true;
            pendingScan = false; nextSense = 0;
            Change(Mode.Patrol);
            waitUntil = Time.time + 1f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * senseHeight, proximityRadius);
            Gizmos.color = Color.green;
            if (patrolPoints != null) foreach (var point in patrolPoints) if (point != null) Gizmos.DrawWireSphere(point.position + Vector3.up * 0.1f, 0.3f);
            if (Application.isPlaying && (State == Mode.Investigate || State == Mode.Search))
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(State == Mode.Search ? searchCenter : noisePoint, State == Mode.Search ? searchRadius : 0.5f);
            }
        }
    }
}
