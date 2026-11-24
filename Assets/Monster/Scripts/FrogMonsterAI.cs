/* [ 코드 설명 ] 개구리(눈 있는 점프형) 몬스터의 AI입니다. 소리에는 반응하지 않고 시야만 씁니다.
   매복 지점(천장·벽·바닥)에 붙어 가만히 있다가, 감지 범위 안에 "서 있는" 대원이 보이면 짧게 경고음을 낸 뒤 그 자리로 뛰어내려 덮칩니다.
   앉아 있는 대원은 알아보지 못합니다(앉아서 지나가면 안전). 착지한 뒤에는 잠깐 쫓아가며 물고, 놓치면 가까운 빈 매복 지점으로 돌아가 다시 붙습니다.
   매복 중에 총에 맞으면 앉아 있어도 쏜 대원에게 바로 뛰어내립니다.
   - 매복 지점(ambushPoints): 위치 = 붙는 자리, 위쪽(초록 화살표) = 표면에서 방 안쪽을 향하는 방향(천장이면 아래). 비워 두면 처음 서 있던 바닥에서 매복합니다.
   - 몬스터 본체(루트)는 항상 똑바로 서 있고, 천장에 거꾸로 붙은 모습은 visualPivot만 돌려서 만듭니다 (멀티플레이에서 위치·좌우 회전만 동기화되기 때문). */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VoidLast.Player;

namespace VoidLast.Monster
{
    [RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth), typeof(MonsterVision))]
    [RequireComponent(typeof(MonsterMelee))]
    public sealed class FrogMonsterAI : MonoBehaviour, IMonsterAnimationSource
    {
        public enum Mode { Ambush, Warn, Jump, Chase, Attack, Return, Climb, Dead, Stunned }
        static readonly List<FrogMonsterAI> All = new List<FrogMonsterAI>();

        [Header("매복")]
        [Tooltip("붙을 수 있는 자리들. 위쪽(초록 화살표)이 표면에서 방 안쪽을 향하게 놓습니다. 비워 두면 처음 위치의 바닥에서 매복합니다.")]
        [SerializeField] Transform[] ambushPoints;
        [Tooltip("매복 지점에서 이 거리 안에 서 있는 대원이 보이면 발견한다.")]
        [SerializeField, Min(1)] float detectRadius = 7;
        [SerializeField, Min(0.05f)] float senseInterval = 0.15f;
        [Tooltip("다시 붙은 뒤 이 시간 동안은 뛰어내리지 않는다.")]
        [SerializeField, Min(0)] float rearmDelay = 10;

        [Header("경고와 점프")]
        [Tooltip("뛰어내리기 전에 울음소리를 내는 시간. 이 소리를 듣고 달리면 피할 수 있다.")]
        [SerializeField, Min(0.05f)] float warnTime = 0.6f;
        [Tooltip("매복 중에 총에 맞았을 때의 경고 시간.")]
        [SerializeField, Min(0.05f)] float hitWarnTime = 0.2f;
        [SerializeField, Min(0.1f)] float jumpTime = 0.5f;
        [SerializeField, Min(0)] float jumpArc = 0.4f;
        [SerializeField, Min(0)] float landDamage = 35;
        [SerializeField, Min(0.1f)] float landRadius = 1.5f;
        [SerializeField] AudioClip warnClip;
        [SerializeField, Range(0, 1)] float warnVolume = 1;

        [Header("추격 (착지 후)")]
        [SerializeField, Min(0.1f)] float chaseSpeed = 3.5f;
        [SerializeField, Min(0.5f)] float chaseTime = 8;
        [SerializeField, Min(1)] float loseDistance = 12;

        [Header("복귀 (다시 붙기)")]
        [SerializeField, Min(0.1f)] float returnSpeed = 2.5f;
        [SerializeField, Min(0.1f)] float climbTime = 0.7f;
        [SerializeField, Min(1)] float returnTimeout = 20;

        [Header("연결")]
        [Tooltip("모델을 담은 자식. 천장·벽에 붙을 때 이 오브젝트만 돌린다.")]
        [SerializeField] Transform visualPivot;

        NavMeshAgent agent;
        MonsterHealth health;
        MonsterVision vision;
        MonsterMelee melee;
        MonsterStun stun;
        CapsuleCollider body;
        readonly List<Collider> downColliders = new List<Collider>();
        Vector3[] anchorPositions, anchorNormals;
        Vector3 bodyCenter, jumpFrom, jumpTo, climbFrom, fallFrom, fallTo;
        FpsHealth target, pendingTarget;
        int anchor = -1;
        bool hasGoal, stunnedAttached, falling;
        float stateStart, armedAt, warnEnds, currentWarnTime, chaseEnds, nextSense, nextPath, fallStart, attachBlend;

        public Mode State { get; private set; }
        /// <summary>멀티플레이: 방장(서버)이 아닌 화면의 복제본이면 AI를 돌리지 않고 서버가 보낸 상태로 연출만 한다.</summary>
        public bool NetworkProxy { get; set; }
        /// <summary>밸런스 설정(GameBalance) 값 적용</summary>
        public void ApplyBalance(float chase, float land) { chaseSpeed = chase; landDamage = land; }
        /// <summary>복제본 전용: 서버 AI의 상태를 받는다.</summary>
        public void ApplyNetworkState(int mode)
        {
            if (!NetworkProxy || (int)State == mode) return;
            State = (Mode)mode;
            stateStart = Time.time;
            if (State == Mode.Warn) PlayWarn();
            if (State == Mode.Jump) JumpCount++;
            StateChanged?.Invoke(State);
        }
        public FpsHealth Target => target;
        public Vector3 Destination { get; private set; }
        public int JumpCount { get; private set; }
        public int LandHits { get; private set; }
        /// <summary>지금 매복 지점에 붙어 있는가</summary>
        public bool Attached => State == Mode.Ambush || State == Mode.Warn || (State == Mode.Stunned && stunnedAttached);
        public float ArmedIn => Mathf.Max(0, armedAt - Time.time);
        public event System.Action<Mode> StateChanged;
        public bool NavigationReady => agent != null && agent.enabled && agent.isOnNavMesh;

        // 애니메이션 연출용
        public MonsterAnimState AnimState => State == Mode.Warn ? MonsterAnimState.Alert : State == Mode.Jump || State == Mode.Attack ? MonsterAnimState.Attack
            : State == Mode.Dead ? MonsterAnimState.Dead : State == Mode.Stunned ? MonsterAnimState.Stunned : MonsterAnimState.Locomotion;
        public int AttackCount => JumpCount + (melee != null ? melee.Swings : 0);
        public float AttackDuration => State == Mode.Jump || melee == null ? jumpTime + 0.4f : melee.Windup + melee.LungeDuration + melee.RecoveryDuration;
        public float AlertDuration => currentWarnTime > 0 ? currentWarnTime : warnTime;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<MonsterHealth>();
            vision = GetComponent<MonsterVision>();
            melee = GetComponent<MonsterMelee>();
            body = GetComponent<CapsuleCollider>();
            if (body != null) bodyCenter = body.center;
            BuildAnchors();
        }

        void OnEnable() { All.Add(this); health.Damaged += OnHit; health.Died += Die; health.Revived += GetUp; }
        void OnDisable() { All.Remove(this); health.Damaged -= OnHit; health.Died -= Die; health.Revived -= GetUp; }

        void Start()
        {
            if (NetworkProxy) return;
            // 시작하자마자 가장 가까운 매복 지점에 붙는다
            anchor = NearestFreeAnchor(transform.position);
            if (agent.enabled) agent.enabled = false;
            transform.position = anchorPositions[anchor];
            attachBlend = 1;
            Set(Mode.Ambush);
            armedAt = 0;
        }

        void BuildAnchors()
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>();
            if (ambushPoints != null)
                foreach (var point in ambushPoints)
                    if (point != null) { positions.Add(point.position); normals.Add(point.up); }
            if (positions.Count == 0) { positions.Add(transform.position); normals.Add(Vector3.up); }
            anchorPositions = positions.ToArray(); anchorNormals = normals.ToArray();
        }

        void Update()
        {
            if (NetworkProxy) return;
            if (health.IsDead) { TickFall(); return; }
            if (State == Mode.Stunned)
            {
                if (stun != null && stun.IsStunned) return;
                if (stunnedAttached) { Set(Mode.Ambush); armedAt = Mathf.Max(armedAt, Time.time + 0.5f); }
                else BeginReturn();
            }
            switch (State)
            {
                case Mode.Ambush: TickAmbush(); break;
                case Mode.Warn: TickWarn(); break;
                case Mode.Jump: TickJump(); break;
                case Mode.Chase:
                case Mode.Attack: TickChase(); break;
                case Mode.Return: TickReturn(); break;
                case Mode.Climb: TickClimb(); break;
            }
        }

        // ------------------------------------------------------------------ 매복 / 발견

        void TickAmbush()
        {
            if (Time.time < armedAt || Time.time < nextSense) return;
            nextSense = Time.time + senseInterval;
            var seen = FindStanding();
            if (seen != null) BeginWarn(seen, warnTime);
        }

        /// <summary>감지 범위 안에서 보이는, 앉아 있지 않은 가장 가까운 대원</summary>
        FpsHealth FindStanding()
        {
            Vector3 eye = anchorPositions[anchor] + anchorNormals[anchor] * 0.4f;
            FpsHealth best = null;
            float bestDistance = detectRadius;
            foreach (var candidate in FindObjectsByType<FpsHealth>(FindObjectsInactive.Exclude))
            {
                if (candidate == null || candidate.IsDead || StairSafeZone.Contains(candidate)) continue;
                var motor = candidate.GetComponent<FpsMotor>();
                if (motor != null && (motor.IsCrouching || motor.CrouchFraction > 0.5f)) continue;   // 앉은 대원은 알아보지 못한다
                var controller = candidate.GetComponent<CharacterController>();
                Vector3 center = controller != null ? controller.bounds.center : candidate.transform.position + Vector3.up;
                float distance = Vector3.Distance(eye, center);
                if (distance < bestDistance && ClearLine(eye, center, candidate.transform)) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        bool ClearLine(Vector3 from, Vector3 to, Transform other)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            foreach (var hit in Physics.RaycastAll(from, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(other)) return false;
            return true;
        }

        // ------------------------------------------------------------------ 경고 → 점프 → 착지

        void BeginWarn(FpsHealth victim, float time)
        {
            target = victim;
            currentWarnTime = time;
            warnEnds = Time.time + time;
            Set(Mode.Warn);
            PlayWarn();
        }

        void TickWarn()
        {
            if (target == null || target.IsDead || !target.isActiveAndEnabled || StairSafeZone.Contains(target)) { Set(Mode.Ambush); armedAt = Time.time + 1f; return; }
            FaceTowards(target.transform.position, 720);
            if (Time.time >= warnEnds) BeginJump();
        }

        void BeginJump()
        {
            // 경고가 끝난 순간 대원이 있던 자리를 노린다 (그 뒤로 움직이면 빗나간다)
            if (!NavMesh.SamplePosition(target.transform.position, out var hit, 2.5f, agent.areaMask)) { Set(Mode.Ambush); armedAt = Time.time + 2f; return; }
            jumpFrom = transform.position;
            jumpTo = hit.position;
            JumpCount++;
            Set(Mode.Jump);
        }

        void TickJump()
        {
            float t = Mathf.Clamp01((Time.time - stateStart) / jumpTime);
            Vector3 position = Vector3.Lerp(jumpFrom, jumpTo, t);
            // 위에서 떨어질 때는 점점 빨라지고, 같은 높이에서는 작은 포물선을 그린다
            position.y = Mathf.Lerp(jumpFrom.y, jumpTo.y, jumpFrom.y > jumpTo.y + 0.5f ? t * t : t) + jumpArc * Mathf.Sin(t * Mathf.PI);
            transform.position = position;
            FaceTowards(jumpTo, 720);
            if (t >= 1) Land();
        }

        void Land()
        {
            transform.position = jumpTo;
            agent.enabled = true;
            agent.Warp(jumpTo);
            foreach (var player in FindObjectsByType<FpsHealth>(FindObjectsInactive.Exclude))
            {
                if (player == null || player.IsDead || StairSafeZone.Contains(player)) continue;
                Vector3 delta = player.transform.position - jumpTo;
                if (Mathf.Abs(delta.y) > 2f || Vector3.ProjectOnPlane(delta, Vector3.up).magnitude > landRadius) continue;
                player.TakeDamage(landDamage); PlayLog.MonsterHit(gameObject, landDamage);
                LandHits++;
            }
            chaseEnds = Time.time + chaseTime;
            nextPath = 0;
            Set(Mode.Chase);
        }

        // ------------------------------------------------------------------ 추격 (착지 후 잠깐)

        void TickChase()
        {
            if (melee.IsBusy) { if (State != Mode.Attack) Set(Mode.Attack); agent.updateRotation = false; return; }   // 공격 중에는 MonsterMelee가 몸을 움직인다
            agent.updateRotation = true;
            if (State == Mode.Attack) Set(Mode.Chase);
            if (!NavigationReady || target == null || target.IsDead || !target.isActiveAndEnabled || StairSafeZone.Contains(target) || Time.time >= chaseEnds
                || FlatDistance(target.transform.position) > loseDistance) { BeginReturn(); return; }

            if (FlatDistance(target.transform.position) <= melee.AttackDistance && melee.TryStart(target)) { Stop(); Set(Mode.Attack); agent.updateRotation = false; return; }
            if (Time.time >= nextPath)
            {
                nextPath = Time.time + 0.25f;
                if (!MoveTo(target.transform.position, chaseSpeed, Mathf.Max(0.3f, melee.AttackDistance - 0.35f), 2f)) BeginReturn();
            }
        }

        // ------------------------------------------------------------------ 복귀 (가까운 빈 매복 지점으로 가서 다시 붙는다)

        void BeginReturn()
        {
            target = null;
            melee.Cancel();
            anchor = NearestFreeAnchor(transform.position);
            Set(Mode.Return);
            if (NavigationReady) agent.updateRotation = true;
            if (!NavigationReady || !MoveTo(GroundUnder(anchor), returnSpeed, 0.2f, 2f)) BeginClimb();
        }

        void TickReturn()
        {
            if (!hasGoal || Arrived() || Time.time - stateStart > returnTimeout) BeginClimb();
        }

        void BeginClimb()
        {
            Stop();
            if (agent.enabled) agent.enabled = false;
            climbFrom = transform.position;
            Set(Mode.Climb);
        }

        void TickClimb()
        {
            float t = Mathf.Clamp01((Time.time - stateStart) / climbTime);
            transform.position = Vector3.Lerp(climbFrom, anchorPositions[anchor], Mathf.SmoothStep(0, 1, t));
            if (t < 1) return;
            Set(Mode.Ambush);
            armedAt = Time.time + rearmDelay;
            if (pendingTarget != null && !pendingTarget.IsDead) BeginWarn(pendingTarget, hitWarnTime);
            pendingTarget = null;
        }

        int NearestFreeAnchor(Vector3 from)
        {
            int best = -1; float bestDistance = float.PositiveInfinity;
            for (int pass = 0; pass < 2 && best < 0; pass++)   // 빈 자리가 없으면 겹치더라도 가장 가까운 곳
                for (int i = 0; i < anchorPositions.Length; i++)
                {
                    if (pass == 0 && TakenByOther(anchorPositions[i])) continue;
                    float distance = (anchorPositions[i] - from).sqrMagnitude;
                    if (distance < bestDistance) { best = i; bestDistance = distance; }
                }
            return best;
        }

        bool TakenByOther(Vector3 position)
        {
            foreach (var other in All)
            {
                if (other == this || other == null || other.anchor < 0 || other.anchorPositions == null) continue;
                if ((other.anchorPositions[other.anchor] - position).sqrMagnitude < 0.25f) return true;
            }
            return false;
        }

        /// <summary>매복 지점 아래(옆)의 걸어갈 수 있는 바닥</summary>
        Vector3 GroundUnder(int index) => FloorBelow(anchorPositions[index] + anchorNormals[index] * 0.8f, out var floor) ? floor : transform.position;

        /// <summary>
        /// 그 지점에서 아래로 내려가 처음 만나는 바닥의 걸을 수 있는 위치.
        /// (가장 가까운 NavMesh를 바로 찾으면 천장 위쪽 면이 잡힐 수 있어서, 먼저 아래로 광선을 쏴 바닥을 찾는다)
        /// </summary>
        bool FloorBelow(Vector3 from, out Vector3 floor)
        {
            floor = from;
            float best = float.PositiveInfinity; Vector3 point = from; bool found = false;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best || hit.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<FpsHealth>() != null || hit.collider.GetComponentInParent<MonsterHealth>() != null) continue;
                best = hit.distance; point = hit.point; found = true;
            }
            if (!found || !NavMesh.SamplePosition(point, out var sample, 2f, agent.areaMask)) return false;
            floor = sample.position;
            return true;
        }

        // ------------------------------------------------------------------ 이동 도우미

        float FlatDistance(Vector3 point) => Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;
        bool Arrived() => !agent.pathPending && FlatDistance(Destination) <= agent.stoppingDistance + 0.25f;

        bool MoveTo(Vector3 point, float speed, float stopDistance, float sampleRange)
        {
            if (!NavigationReady || !NavMesh.SamplePosition(point, out var hit, sampleRange, agent.areaMask)) return false;
            var path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            agent.speed = speed; agent.stoppingDistance = stopDistance; agent.isStopped = false; agent.SetPath(path);
            Destination = hit.position; hasGoal = true;
            return true;
        }

        void Stop()
        {
            if (NavigationReady) { agent.isStopped = true; agent.ResetPath(); }
            hasGoal = false;
        }

        void Set(Mode mode)
        {
            if (State == mode) { stateStart = Time.time; return; }
            State = mode;
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

        void OnHit(GameObject attacker, Vector3 position)
        {
            var shooter = attacker != null ? attacker.GetComponentInParent<FpsHealth>() : null;
            if (shooter == null || shooter.IsDead || StairSafeZone.Contains(shooter)) return;
            switch (State)
            {
                case Mode.Ambush: BeginWarn(shooter, hitWarnTime); break;   // 앉아 있어도 쏜 대원에게 바로 뛰어내린다
                case Mode.Climb: pendingTarget = shooter; break;
                case Mode.Return: target = shooter; chaseEnds = Time.time + chaseTime; nextPath = 0; Set(Mode.Chase); break;
                case Mode.Chase: if (target != shooter) { target = shooter; nextPath = 0; } break;
            }
        }

        public void BeginStun(MonsterStun effect)
        {
            if (health.IsDead || NetworkProxy || State == Mode.Jump || State == Mode.Climb) return;   // 공중에서는 멈추지 않는다
            stunnedAttached = Attached;
            stun = effect; melee.Cancel(); Stop();
            if (NavigationReady) { agent.velocity = Vector3.zero; agent.updateRotation = false; }
            Set(Mode.Stunned);
        }

        void Die()
        {
            bool onGround = State == Mode.Chase || State == Mode.Attack || State == Mode.Return || (State == Mode.Stunned && !stunnedAttached);
            Set(Mode.Dead);
            melee.Cancel(); target = pendingTarget = null; hasGoal = false;
            if (agent.enabled) agent.enabled = false;
            downColliders.Clear();
            foreach (var collider in GetComponentsInChildren<Collider>()) if (collider.enabled) { collider.enabled = false; downColliders.Add(collider); }
            if (NetworkProxy || onGround) return;
            // 붙어 있거나 공중에서 쓰러지면 바닥으로 떨어진다
            fallFrom = transform.position;
            fallTo = FloorBelow(fallFrom + Vector3.down * 0.3f, out var floor) ? floor : fallFrom;
            fallStart = Time.time;
            falling = true;
        }

        void TickFall()
        {
            if (!falling) return;
            float t = Mathf.Clamp01((Time.time - fallStart) / 0.35f);
            transform.position = Vector3.Lerp(fallFrom, fallTo, t * t);
            if (t >= 1) falling = false;
        }

        /// <summary>그로기가 끝나 다시 일어난다: 충돌체와 길 찾기를 되살리고 매복 지점으로 돌아간다.</summary>
        void GetUp()
        {
            foreach (var collider in downColliders) if (collider != null) collider.enabled = true;
            downColliders.Clear();
            if (NetworkProxy) return;   // 복제본의 상태는 서버가 보내 준다
            if (falling) { falling = false; transform.position = fallTo; }
            agent.enabled = true;
            if (agent.isOnNavMesh) agent.Warp(transform.position);
            BeginReturn();
        }

        // ------------------------------------------------------------------ 연출 (천장·벽에 붙은 자세 / 경고음)

        void LateUpdate()
        {
            if (visualPivot == null || anchorPositions == null) return;
            // 가장 가까운 매복 지점까지의 거리로 "붙은 정도"를 정한다 → 서버와 복제본이 같은 계산을 한다
            int nearest = 0; float distance = float.PositiveInfinity;
            for (int i = 0; i < anchorPositions.Length; i++)
            {
                float d = Vector3.Distance(anchorPositions[i], transform.position);
                if (d < distance) { distance = d; nearest = i; }
            }
            bool mayAttach = State != Mode.Chase && State != Mode.Attack && State != Mode.Return;
            float wanted = mayAttach ? 1 - Mathf.Clamp01(distance / 1.2f) : 0;
            attachBlend = Mathf.MoveTowards(attachBlend, wanted, Time.deltaTime * 8);

            Vector3 normal = anchorNormals[nearest];
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(transform.up, normal);
            Quaternion attached = Quaternion.LookRotation(forward.normalized, normal);
            visualPivot.rotation = Quaternion.Slerp(transform.rotation, attached, attachBlend);
            // 맞는 범위(충돌체)도 몸을 따라 표면 쪽으로 옮긴다
            if (body != null) body.center = Vector3.Lerp(bodyCenter, transform.InverseTransformDirection(normal) * bodyCenter.magnitude, attachBlend);
        }

        void PlayWarn()
        {
            var clip = warnClip != null ? warnClip : DefaultWarnClip();
            GameAudio.PlayAt(clip, transform.position, warnVolume);
        }

        static AudioClip defaultWarn;
        /// <summary>경고음 파일이 없을 때 쓰는 짧은 울음소리 (코드로 만든 낮은 그르렁 소리)</summary>
        static AudioClip DefaultWarnClip()
        {
            if (defaultWarn != null) return defaultWarn;
            const int rate = 44100; const float seconds = 0.55f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            var rng = new System.Random(11);
            float phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float freq = Mathf.Lerp(170f, 70f, t);                      // 높게 시작해 낮게 떨어지는 울음
                phase += 2 * Mathf.PI * freq / rate;
                float croak = Mathf.Sign(Mathf.Sin(phase)) * 0.5f + Mathf.Sin(phase * 2.01f) * 0.3f;
                float rattle = 0.55f + 0.45f * Mathf.Sin(2 * Mathf.PI * 34f * t * seconds);   // 떨림
                float noise = (float)(rng.NextDouble() * 2 - 1) * 0.25f;
                float envelope = Mathf.Clamp01(t * 12f) * Mathf.Pow(1 - t, 0.6f);
                data[i] = (croak * rattle + noise) * envelope * 0.7f;
            }
            defaultWarn = AudioClip.Create("FrogWarn", n, 1, rate, false);
            defaultWarn.SetData(data, 0);
            return defaultWarn;
        }

        void OnDrawGizmosSelected()
        {
            if (ambushPoints == null) return;
            foreach (var point in ambushPoints)
            {
                if (point == null) continue;
                Gizmos.color = new Color(0.3f, 1f, 0.4f);
                Gizmos.DrawWireSphere(point.position, 0.25f);
                Gizmos.DrawRay(point.position, point.up * 0.8f);
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.35f);
                Gizmos.DrawWireSphere(point.position, detectRadius);
            }
        }
    }
}
