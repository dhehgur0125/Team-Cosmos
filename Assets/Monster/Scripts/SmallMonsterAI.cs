/* [ 코드 설명 ] 눈 달린 소형 몬스터(쪼꼬미)의 AI입니다. NavMesh 위를 돌아다니며 플레이어를 적극적으로 수색하고, 시야에 플레이어를 먼저 발견하면 멈춰서 비명을 질러 주변 동료를 불러 모은 뒤 곧바로 추격·근접 공격합니다. 놓치면 마지막 목격 위치를 조사하고 주변을 수색하다가 다시 수색 순찰로 돌아갑니다. 탐지기 스캔 소리에는 반응하지 않으며, 보이지 않는 플레이어의 위치는 읽지 않습니다. */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VoidLast.Player;

namespace VoidLast.Monster
{
    [RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth), typeof(MonsterVision))]
    [RequireComponent(typeof(MonsterMelee))]
    public sealed class SmallMonsterAI : MonoBehaviour
    {
        public enum Mode { Search, Scream, Chase, Attack, Investigate, Hunt, Dead }

        static readonly List<SmallMonsterAI> All = new List<SmallMonsterAI>();

        [Header("수색 (플레이어를 찾아 돌아다니기)")]
        [Tooltip("비워두면 처음 위치 주변의 무작위 지점을 돌아다닙니다.")]
        [SerializeField] Transform[] searchPoints;
        [SerializeField, Min(1)] float roamRadius = 30;
        [SerializeField, Min(0.1f)] float searchSpeed = 2.4f;
        [Tooltip("수색 지점에 도착한 뒤 두리번거리는 시간(초)")]
        [SerializeField, Min(0)] float lookAroundTime = 1.6f;
        [SerializeField, Min(1)] float lookAroundAngle = 75;

        [Header("추격 / 조사")]
        [SerializeField, Min(0.1f)] float chaseSpeed = 4.2f;
        [SerializeField, Min(0.05f)] float senseInterval = 0.12f;
        [Tooltip("플레이어를 놓친 뒤 마지막 목격 위치 주변을 수색하는 시간(초)")]
        [SerializeField, Min(0.1f)] float huntDuration = 8;
        [SerializeField, Min(1)] float huntRadius = 8;

        [Header("비명 (플레이어를 먼저 발견했을 때)")]
        [SerializeField, Min(0.1f)] float screamDuration = 1.1f;
        [Tooltip("비명이 들려서 동료 쪼꼬미들이 몰려오는 반경(m)")]
        [SerializeField, Min(1)] float screamAlertRadius = 25;
        [SerializeField] AudioClip screamClip;
        [SerializeField, Range(0, 1)] float screamVolume = 1;

        [Header("연결 (선택)")]
        [SerializeField] Transform visualPivot;
        [SerializeField] Renderer bodyRenderer;
        [Tooltip("선택. 'Scream' 트리거 파라미터가 있으면 비명 순간에 호출합니다.")]
        [SerializeField] Animator animator;

        NavMeshAgent agent;
        MonsterHealth health;
        MonsterVision vision;
        MonsterMelee melee;
        MaterialPropertyBlock colors;
        bool targetVisible, knowsLocation, hasGoal, aggro;
        float nextSense, nextPath, waitUntil, huntEnds, screamEnds, lookUntil, lookStart, lookDuration, deathBlend;
        Quaternion lookBase;
        Vector3 home, pivotScale, pivotPosition;
        int previousSearch = -1;

        public Mode State { get; private set; }
        /// <summary>멀티플레이: 방장(서버)이 아닌 화면의 복제본이면 AI를 돌리지 않고 서버가 보낸 상태로 연출만 한다.</summary>
        public bool NetworkProxy { get; set; }
        /// <summary>복제본 전용: 서버 AI의 상태를 받아 색·자세 연출에 쓴다.</summary>
        public void ApplyNetworkState(int mode)
        {
            if (!NetworkProxy || (int)State == mode) return;
            State = (Mode)mode;
            StateChanged?.Invoke(State);
        }
        public FpsHealth Target { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public Vector3 Destination { get; private set; }
        public bool IsAggro => aggro;
        public int ScreamCount { get; private set; }
        public event System.Action<SmallMonsterAI> Screamed;
        public event System.Action<Mode> StateChanged; // 상태가 바뀔 때마다 (효과음·애니메이션 연결용)
        public bool NavigationReady => agent != null && agent.enabled && agent.isOnNavMesh;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<MonsterHealth>();
            vision = GetComponent<MonsterVision>();
            melee = GetComponent<MonsterMelee>();
            colors = new MaterialPropertyBlock();
            if (visualPivot != null) { pivotScale = visualPivot.localScale; pivotPosition = visualPivot.localPosition; }
        }

        void OnEnable()
        {
            All.Add(this);
            health.Damaged += OnHit;
            health.Died += Die;
        }

        void OnDisable()
        {
            All.Remove(this);
            health.Damaged -= OnHit;
            health.Died -= Die;
            if (NavigationReady) agent.isStopped = true;
        }

        void Start()
        {
            home = transform.position;
            if (!NetworkProxy && !NavigationReady) Debug.LogError("SmallMonsterAI는 생성 위치에 구워진 NavMesh가 필요합니다.", this);
        }

        void Update()
        {
            if (NetworkProxy || health.IsDead || !NavigationReady) return;
            Sense();

            bool lookingAround = Time.time < lookUntil;
            agent.updateRotation = !melee.IsBusy && State != Mode.Scream && !lookingAround;

            if (melee.IsBusy) { Change(Mode.Attack); return; } // 공격 중에는 MonsterMelee가 이동을 가진다
            if (State == Mode.Scream) { TickScream(); return; }

            if (targetVisible && Target != null && !Target.IsDead)
            {
                if (!aggro) { BeginScream(); return; } // 처음 발견했다면 먼저 비명

                Change(Mode.Chase);
                var close = vision.CanSee(Target) && FlatDistance(Target.transform.position) <= melee.AttackDistance ? Target : vision.FindVisible();
                if (melee.TryStart(close)) { Change(Mode.Attack); Stop(); agent.updateRotation = false; return; }
                if (Time.time >= nextPath)
                {
                    nextPath = Time.time + 0.25f;
                    MoveTo(LastKnownPosition, chaseSpeed, Mathf.Max(0.3f, melee.AttackDistance - 0.35f));
                }
                return;
            }

            if (State == Mode.Chase || State == Mode.Attack) Change(knowsLocation ? Mode.Investigate : Mode.Search);

            switch (State)
            {
                case Mode.Search:
                    if (lookingAround) { LookAroundStep(); break; }
                    if (hasGoal && Arrived()) { BeginLookAround(lookAroundTime); break; }
                    if (!hasGoal && Time.time >= waitUntil) PickSearchPoint();
                    break;

                case Mode.Investigate:
                    if (!hasGoal && !MoveTo(LastKnownPosition, chaseSpeed, 0.3f)) BeginHunt();
                    else if (hasGoal && Arrived()) BeginHunt();
                    break;

                case Mode.Hunt:
                    if (Time.time >= huntEnds) { GiveUp(); break; }
                    if (lookingAround) { LookAroundStep(); break; }
                    if (hasGoal && Arrived()) { BeginLookAround(lookAroundTime * 0.7f); break; }
                    if (!hasGoal && Time.time >= waitUntil) PickHuntPoint();
                    break;
            }
        }

        // ------------------------------------------------------------------ 감각

        void Sense()
        {
            if (Time.time < nextSense) return;
            nextSense = Time.time + senseInterval;

            if (Target != null && (Target.IsDead || !Target.isActiveAndEnabled)) { Target = null; knowsLocation = false; aggro = false; }
            var seen = vision.FindVisible(Target);
            targetVisible = seen != null;
            if (seen != null) { Target = seen; LastKnownPosition = seen.transform.position; knowsLocation = true; }
        }

        // ------------------------------------------------------------------ 비명

        void BeginScream()
        {
            aggro = true;
            ScreamCount++;
            Change(Mode.Scream);
            screamEnds = Time.time + screamDuration;
            if (screamClip != null) AudioSource.PlayClipAtPoint(screamClip, transform.position, screamVolume);
            TriggerAnimator("Scream");
            AlertAllies();
            Screamed?.Invoke(this);
        }

        /// <summary>복제본 전용: 서버에서 비명을 질렀을 때 소리와 애니메이션만 재생한다.</summary>
        public void PlayNetworkScream()
        {
            if (screamClip != null) AudioSource.PlayClipAtPoint(screamClip, transform.position, screamVolume);
            TriggerAnimator("Scream");
        }

        void TickScream()
        {
            if (Target != null) FaceTowards(Target.transform.position, 540);
            if (Time.time < screamEnds) return;

            if (targetVisible && Target != null && !Target.IsDead) Change(Mode.Chase);
            else Change(knowsLocation ? Mode.Investigate : Mode.Search);
        }

        // 비명이 닿는 범위의 동료들에게 플레이어의 위치를 알린다
        void AlertAllies()
        {
            float sqrRadius = screamAlertRadius * screamAlertRadius;
            foreach (var other in All)
            {
                if (other == this || other == null || other.health.IsDead) continue;
                if ((other.transform.position - transform.position).sqrMagnitude > sqrRadius) continue;
                other.HearScream(Target, LastKnownPosition);
            }
        }

        /// <summary>
        /// 큰 소음(예: 2층 연료 펌프)을 들었을 때: 지금 쫓는 대상이 없으면 소리 난 곳을 조사하러 간다.
        /// 그 자리에서 대원을 보면 평소처럼 비명 → 추격으로 이어진다.
        /// </summary>
        public void HearNoise(Vector3 position)
        {
            if (NetworkProxy || health.IsDead || State == Mode.Scream || State == Mode.Attack || State == Mode.Chase) return;
            if (targetVisible && Target != null && !Target.IsDead) return;
            LastKnownPosition = position;
            knowsLocation = true;
            if (State == Mode.Investigate) Stop();   // 이미 조사 중이면 새 위치로 다시 경로를 잡는다
            else Change(Mode.Investigate);
        }

        void HearScream(FpsHealth player, Vector3 position)
        {
            if (health.IsDead || State == Mode.Scream || State == Mode.Attack) return;
            if (targetVisible && Target != null && !Target.IsDead) { aggro = true; return; } // 이미 보고 있으면 계속 추격

            Target = player;
            LastKnownPosition = position;
            knowsLocation = true;
            aggro = true; // 이미 동료의 비명을 들었으니 직접 보게 돼도 다시 비명을 지르지 않는다
            Change(Mode.Investigate);
        }

        // ------------------------------------------------------------------ 수색

        void PickSearchPoint()
        {
            waitUntil = Time.time + 0.4f;

            if (searchPoints != null && searchPoints.Length > 0)
            {
                int start = UnityEngine.Random.Range(0, searchPoints.Length);
                for (int i = 0; i < searchPoints.Length; i++)
                {
                    int index = (start + i) % searchPoints.Length;
                    var point = searchPoints[index];
                    if (point == null || (searchPoints.Length > 1 && index == previousSearch)) continue;
                    if (MoveTo(point.position, searchSpeed, 0.2f)) { previousSearch = index; return; }
                }
                return;
            }

            if (RandomPoint(home, roamRadius, out var destination)) MoveTo(destination, searchSpeed, 0.2f);
        }

        void BeginHunt()
        {
            Change(Mode.Hunt);
            huntEnds = Time.time + huntDuration;
            BeginLookAround(lookAroundTime); // 마지막 목격 위치에 도착하면 먼저 주변을 둘러본다
        }

        void PickHuntPoint()
        {
            waitUntil = Time.time + 0.3f;
            for (int i = 0; i < 4; i++)
            {
                if (RandomPoint(LastKnownPosition, huntRadius, out var destination) && MoveTo(destination, searchSpeed + 0.8f, 0.2f)) return;
            }
        }

        void GiveUp()
        {
            Target = null;
            knowsLocation = false;
            aggro = false;
            Change(Mode.Search);
        }

        // ------------------------------------------------------------------ 두리번거리기

        void BeginLookAround(float duration)
        {
            Stop();
            lookBase = transform.rotation;
            lookStart = Time.time;
            lookDuration = Mathf.Max(0.05f, duration);
            lookUntil = Time.time + lookDuration;
        }

        void LookAroundStep()
        {
            float t = (Time.time - lookStart) / lookDuration;
            transform.rotation = lookBase * Quaternion.Euler(0, Mathf.Sin(t * Mathf.PI * 2f) * lookAroundAngle, 0);
        }

        // ------------------------------------------------------------------ 이동 도우미

        float FlatDistance(Vector3 point) => Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;
        bool Arrived() => !agent.pathPending && FlatDistance(Destination) <= agent.stoppingDistance + 0.2f;

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
                Vector2 circle = UnityEngine.Random.insideUnitCircle * radius;
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
            nextPath = 0; waitUntil = 0; lookUntil = 0;
            StateChanged?.Invoke(mode);
        }

        void FaceTowards(Vector3 point, float degreesPerSecond)
        {
            Vector3 direction = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (direction.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degreesPerSecond * Time.deltaTime);
        }

        void TriggerAnimator(string parameter)
        {
            if (animator == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.name == parameter && p.type == AnimatorControllerParameterType.Trigger) { animator.SetTrigger(parameter); return; }
            }
        }

        // ------------------------------------------------------------------ 피격 / 사망

        void OnHit(GameObject attacker, Vector3 position)
        {
            if (State == Mode.Scream || State == Mode.Attack) return;

            var shooter = attacker != null ? attacker.GetComponent<FpsHealth>() : null;
            if (vision.CanSee(shooter, true))
            {
                Target = shooter; LastKnownPosition = position; knowsLocation = targetVisible = true;
                if (!aggro) BeginScream(); else Change(Mode.Chase);
            }
            else if (Target == null)
            {
                LastKnownPosition = position; knowsLocation = true; targetVisible = false;
                Change(Mode.Investigate);
            }
        }

        void Die()
        {
            Change(Mode.Dead);
            melee.Cancel();
            Target = null; targetVisible = false; aggro = false;
            if (agent.enabled) agent.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        }

        // ------------------------------------------------------------------ 연출 (상태 색상 / 비명 떨림 / 쓰러짐)

        void LateUpdate()
        {
            if (bodyRenderer != null)
            {
                Color color = State == Mode.Dead ? new Color(0.09f, 0.09f, 0.1f)
                    : State == Mode.Scream ? new Color(1f, 0.95f, 0.15f)
                    : State == Mode.Attack ? new Color(1f, 0.35f, 0.05f)
                    : State == Mode.Chase ? new Color(0.8f, 0.08f, 0.06f)
                    : State == Mode.Investigate || State == Mode.Hunt ? new Color(0.85f, 0.65f, 0.04f)
                    : new Color(0.3f, 0.7f, 0.4f);
                colors.SetColor("_BaseColor", color);
                bodyRenderer.SetPropertyBlock(colors);
            }

            if (visualPivot == null) return;
            deathBlend = Mathf.MoveTowards(deathBlend, health.IsDead ? 1 : 0, Time.deltaTime * 2);
            float pulse = State == Mode.Scream ? 1f + 0.22f * Mathf.Abs(Mathf.Sin(Time.time * 18f)) : 1f; // 비명을 지르며 부풀었다 줄었다
            visualPivot.localScale = pivotScale * pulse;
            visualPivot.localRotation = Quaternion.Euler(melee.Lean * (1 - deathBlend), 0, deathBlend * 85);
            visualPivot.localPosition = pivotPosition + Vector3.up * deathBlend * 0.35f;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 center = Application.isPlaying ? home : transform.position;
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f);
            if (searchPoints == null || searchPoints.Length == 0) Gizmos.DrawWireSphere(center, roamRadius);
            else foreach (var point in searchPoints) if (point != null) Gizmos.DrawWireSphere(point.position + Vector3.up * 0.1f, 0.25f);

            Gizmos.color = new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, screamAlertRadius);
            if (Application.isPlaying && knowsLocation) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(LastKnownPosition, 0.4f); }
        }
    }
}
