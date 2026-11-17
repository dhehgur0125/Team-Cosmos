/* [ 코드 설명 ] 눈이 없는 몬스터의 AI입니다. 시야 정보는 전혀 쓰지 않습니다. 평소에는 맵을 천천히 배회하다가 탐지기 스캔 소리가 들리면 스캔한 위치로 달려와 촉수로 주변을 더듬어 플레이어를 찾습니다. 소리가 없어도 플레이어가 가까이 있으면 더듬기 시작하며, 촉수에 닿으면 멈춰서 휘둘러 공격합니다. 끝내 못 찾으면 다시 맵을 배회합니다. */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VoidLast.Player;

namespace VoidLast.Monster
{
    [RequireComponent(typeof(NavMeshAgent), typeof(MonsterHealth))]
    public sealed class BlindMonsterAI : MonoBehaviour
    {
        public enum Mode { Wander, Approach, Grope, Strike, Dead, Stunned }

        struct Segment { public Transform transform; public Quaternion baseRotation; }
        struct TentaclePose { public float yawAmp, pitch, pitchAmp, speed, curl; }

        [Header("배회 (평소에 맵을 돌아다님)")]
        [Tooltip("비워두면 처음 위치 주변의 무작위 지점을 돌아다닙니다.")]
        [SerializeField] Transform[] wanderPoints;
        [SerializeField, Min(1)] float roamRadius = 30;
        [SerializeField, Min(0.1f)] float wanderSpeed = 1.6f;
        [SerializeField, Min(0)] float wanderWait = 1.5f;

        [Header("스캔 소리에 반응")]
        [SerializeField, Min(0.1f)] float approachSpeed = 3.6f;
        [Tooltip("스캔 위치까지 이 시간 안에 못 가면 그 자리에서 더듬기 시작합니다.")]
        [SerializeField, Min(1)] float approachTimeout = 25;

        [Header("더듬기 (촉수로 주변 탐색)")]
        [SerializeField, Min(1)] float gropeDuration = 9;
        [Tooltip("더듬는 동안 중심 주변을 돌아다니는 반경(m)")]
        [SerializeField, Min(0.5f)] float gropeRadius = 3.5f;
        [SerializeField, Min(0.1f)] float gropeSpeed = 0.9f;
        [Tooltip("촉수에 닿는 거리(m). 벽 너머에는 닿지 않습니다.")]
        [SerializeField, Min(0.5f)] float tentacleReach = 2.6f;
        [Tooltip("촉수가 뻗어 나오는 높이(m)")]
        [SerializeField, Min(0)] float tentacleHeight = 1;
        [Tooltip("소리가 없어도 플레이어가 이 거리 안에 있으면 진동을 느끼고 더듬기 시작합니다.")]
        [SerializeField, Min(0.5f)] float proximitySenseRadius = 4.5f;
        [Tooltip("더듬기가 끝난 뒤 근접 감지가 다시 켜지기까지의 시간(초)")]
        [SerializeField, Min(0)] float proximityCooldown = 4;

        [Header("촉수 공격")]
        [SerializeField, Min(0)] float damage = 18;
        [SerializeField, Min(0.05f)] float strikeWindup = 0.45f;
        [SerializeField, Min(0.5f)] float strikeReach = 3;
        [SerializeField, Min(0.1f)] float strikeRecovery = 1;

        [Header("연결 (선택)")]
        [SerializeField] Renderer bodyRenderer;
        [Tooltip("각 촉수의 시작 Transform. 첫 번째 자식을 따라 이어지는 마디까지 함께 움직입니다. (+Z 방향으로 뻗은 촉수 기준)")]
        [SerializeField] Transform[] tentacles;

        static readonly Color WanderColor = new Color(0.4f, 0.25f, 0.5f);
        static readonly Color ApproachColor = new Color(0.9f, 0.5f, 0.1f);
        static readonly Color GropeColor = new Color(0.9f, 0.8f, 0.15f);
        static readonly Color StrikeColor = new Color(0.9f, 0.1f, 0.1f);
        static readonly Color DeadColor = new Color(0.09f, 0.09f, 0.1f);

        NavMeshAgent agent;
        MonsterHealth health;
        MonsterStun stun;
        MaterialPropertyBlock colors;
        Segment[][] chains;
        FpsHealth strikeVictim;
        Vector3 home, noisePoint, gropeCenter, pendingScanPoint;
        bool hasGoal, pendingScan, struck;
        float nextSense, nextProximity, waitUntil, stateStart, gropeEnds, nextGropeStep, proximityBlockedUntil, strikeStart, pendingScanTime;
        int previousWander = -1;

        public Mode State { get; private set; }
        /// <summary>멀티플레이: 방장(서버)이 아닌 화면의 복제본이면 AI를 돌리지 않고 서버가 보낸 상태로 연출만 한다.</summary>
        public bool NetworkProxy { get; set; }
        /// <summary>복제본 전용: 서버 AI의 상태를 받아 색·자세 연출에 쓴다.</summary>
        public void ApplyNetworkState(int mode)
        {
            if (!NetworkProxy || (int)State == mode) return;
            State = (Mode)mode;
            stateStart = Time.time;
            if (State == Mode.Strike) strikeStart = Time.time;   // 치켜들기 → 내려치기 연출 시간을 맞춤
            StateChanged?.Invoke(State);
        }
        public Vector3 Destination { get; private set; }
        public Vector3 GropeCenter => gropeCenter;
        public int ScansHeard { get; private set; }
        public int GropeCount { get; private set; }
        public int StrikesLanded { get; private set; }
        public event System.Action<BlindMonsterAI> GropeStarted;
        public event System.Action<BlindMonsterAI> Struck;
        public event System.Action<Mode> StateChanged; // 상태가 바뀔 때마다 (효과음·애니메이션 연결용)
        public bool NavigationReady => agent != null && agent.enabled && agent.isOnNavMesh;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<MonsterHealth>();
            colors = new MaterialPropertyBlock();
            CacheTentacles();
        }

        void OnEnable()
        {
            health.Damaged += OnHit;
            health.Died += Die;
            ScanNoise.Emitted += OnScanHeard; // 탐지기 스캔 소리는 눈 없는 몬스터만 듣는다
            GunNoise.Emitted += OnGunHeard;
        }

        void OnDisable()
        {
            health.Damaged -= OnHit;
            health.Died -= Die;
            ScanNoise.Emitted -= OnScanHeard;
            GunNoise.Emitted -= OnGunHeard;
            if (NavigationReady) agent.isStopped = true;
        }

        void Start()
        {
            home = transform.position;
            if (!NetworkProxy && !NavigationReady) Debug.LogError("BlindMonsterAI는 생성 위치에 구워진 NavMesh가 필요합니다.", this);
        }

        void Update()
        {
            if (NetworkProxy || health.IsDead || !NavigationReady) return;
            if (State == Mode.Stunned)
            {
                if (stun != null && stun.IsStunned) return;
                Change(Mode.Wander); nextProximity = nextSense = 0;
            }

            // 파동이 이 몬스터에 닿는 시간이 되면 스캔 소리를 '들은' 것으로 처리
            if (pendingScan && Time.time >= pendingScanTime)
            {
                pendingScan = false;
                HearAt(pendingScanPoint);
            }

            agent.updateRotation = State != Mode.Strike;

            switch (State)
            {
                case Mode.Wander:
                    SenseProximity();
                    if (State != Mode.Wander) break;
                    if (hasGoal && Arrived()) { Stop(); waitUntil = Time.time + wanderWait * UnityEngine.Random.Range(0.6f, 1.4f); }
                    if (!hasGoal && Time.time >= waitUntil) PickWanderPoint();
                    break;

                case Mode.Approach:
                    SenseProximity();
                    if (State != Mode.Approach) break;
                    if (Time.time - stateStart > approachTimeout) BeginGrope(transform.position);
                    else if (!hasGoal || Arrived()) BeginGrope(noisePoint);
                    break;

                case Mode.Grope:
                    TickGrope();
                    break;

                case Mode.Strike:
                    TickStrike();
                    break;
            }
        }

        // ------------------------------------------------------------------ 소리 듣기

        void OnScanHeard(Vector3 origin, float radius, float waveSpeed)
        {
            if (health.IsDead || State == Mode.Stunned) return;
            float distance = Vector3.Distance(transform.position, origin);
            if (distance > radius) return;

            ScansHeard++;
            pendingScan = true;
            pendingScanPoint = origin;
            pendingScanTime = Time.time + (waveSpeed > 0f ? distance / waveSpeed : 0f);
        }

        // 사격 소리: 같은 층에서 들리는 거리 안이면 쏜 위치로 간다 (실제 이동은 Update에서 처리하므로 복제본은 움직이지 않는다)
        void OnGunHeard(Vector3 origin, float radius)
        {
            if (Mathf.Abs(transform.position.y - origin.y) > 3.5f) return;
            OnScanHeard(origin, radius, 0f);
        }

        // 소리가 난 위치(또는 맞은 방향)로 달려간다
        void HearAt(Vector3 point)
        {
            if (health.IsDead || State == Mode.Strike) return;

            noisePoint = point;
            Change(Mode.Approach);
            stateStart = Time.time;
            if (!MoveTo(point, approachSpeed, 0.8f, 3f)) BeginGrope(transform.position);
        }

        // ------------------------------------------------------------------ 더듬기

        void BeginGrope(Vector3 center)
        {
            gropeCenter = center;
            gropeEnds = Time.time + gropeDuration;
            nextGropeStep = Time.time + 0.4f;
            GropeCount++;
            Change(Mode.Grope);
            GropeStarted?.Invoke(this);
        }

        void TickGrope()
        {
            if (Time.time >= nextSense)
            {
                nextSense = Time.time + 0.1f;

                // 가까이 있으면 소리가 없어도 진동으로 위치를 어렴풋이 느끼고 그쪽을 더듬는다
                var nearby = Nearest(proximitySenseRadius);
                if (nearby != null)
                {
                    gropeCenter = nearby.transform.position;
                    gropeEnds = Mathf.Max(gropeEnds, Time.time + 2f);
                }

                // 촉수에 실제로 닿았는가
                var touched = Feel();
                if (touched != null)
                {
                    gropeCenter = touched.transform.position;
                    gropeEnds = Mathf.Max(gropeEnds, Time.time + 3f);
                    BeginStrike(touched);
                    return;
                }
            }

            if (Time.time >= gropeEnds) { EndGrope(); return; }

            // 더듬더듬: 조금 움직이고 멈추고를 반복
            if (hasGoal && Arrived()) { Stop(); nextGropeStep = Time.time + UnityEngine.Random.Range(0.5f, 1.3f); }
            if (!hasGoal && Time.time >= nextGropeStep) PickGropePoint();
        }

        void PickGropePoint()
        {
            nextGropeStep = Time.time + 0.5f;
            for (int i = 0; i < 6; i++)
            {
                if (RandomPoint(gropeCenter, gropeRadius, out var point) && MoveTo(point, gropeSpeed, 0.15f)) return;
            }
        }

        // 못 찾고 끝나면 다시 맵을 돌아다닌다
        void EndGrope()
        {
            proximityBlockedUntil = Time.time + proximityCooldown;
            Change(Mode.Wander);
            waitUntil = Time.time + 1f;
        }

        // 소리가 없어도 플레이어가 근처에 있으면 더듬기 시작
        void SenseProximity()
        {
            if (Time.time < nextProximity || Time.time < proximityBlockedUntil) return;
            nextProximity = Time.time + 0.25f;

            var nearby = Nearest(proximitySenseRadius);
            if (nearby != null) BeginGrope(nearby.transform.position);
        }

        // ------------------------------------------------------------------ 촉수 공격

        void BeginStrike(FpsHealth victim)
        {
            Change(Mode.Strike);
            strikeVictim = victim;
            strikeStart = Time.time;
            struck = false;
        }

        void TickStrike()
        {
            float t = Time.time - strikeStart;
            if (strikeVictim != null && !strikeVictim.IsDead && t < strikeWindup) FaceTowards(strikeVictim.transform.position, 540);

            if (!struck && t >= strikeWindup) { struck = true; DoStrike(); }

            if (t >= strikeWindup + strikeRecovery)
            {
                strikeVictim = null;
                Change(Mode.Grope); // 공격 후에는 계속 그 주변을 더듬는다
                nextGropeStep = Time.time + 0.4f;
            }
        }

        void DoStrike()
        {
            if (strikeVictim == null || strikeVictim.IsDead) return;

            Vector3 chest = transform.position + Vector3.up * tentacleHeight;
            Vector3 closest = BoundsOf(strikeVictim).ClosestPoint(chest);
            if (Vector3.Distance(chest, closest) <= strikeReach && ClearTouch(chest, closest, strikeVictim.transform))
            {
                strikeVictim.TakeDamage(damage);
                StrikesLanded++;
            }
            Struck?.Invoke(this);
        }

        // ------------------------------------------------------------------ 촉수 감각

        // 촉수가 닿는 거리 안(벽에 막히지 않은)의 가장 가까운 플레이어
        FpsHealth Feel()
        {
            Vector3 chest = transform.position + Vector3.up * tentacleHeight;
            FpsHealth best = null;
            float bestDistance = tentacleReach;
            foreach (var candidate in FindObjectsByType<FpsHealth>())
            {
                if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;
                Vector3 closest = BoundsOf(candidate).ClosestPoint(chest);
                float distance = Vector3.Distance(chest, closest);
                if (distance < bestDistance && ClearTouch(chest, closest, candidate.transform)) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        FpsHealth Nearest(float radius)
        {
            FpsHealth best = null;
            float bestDistance = radius;
            foreach (var candidate in FindObjectsByType<FpsHealth>())
            {
                if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled) continue;
                Vector3 delta = candidate.transform.position - transform.position;
                if (Mathf.Abs(delta.y) > 3f) continue;
                float distance = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude;
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
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

        // ------------------------------------------------------------------ 배회

        void PickWanderPoint()
        {
            waitUntil = Time.time + 0.4f;

            if (wanderPoints != null && wanderPoints.Length > 0)
            {
                int start = UnityEngine.Random.Range(0, wanderPoints.Length);
                for (int i = 0; i < wanderPoints.Length; i++)
                {
                    int index = (start + i) % wanderPoints.Length;
                    var point = wanderPoints[index];
                    if (point == null || (wanderPoints.Length > 1 && index == previousWander)) continue;
                    if (MoveTo(point.position, wanderSpeed, 0.2f)) { previousWander = index; return; }
                }
                return;
            }

            if (RandomPoint(home, roamRadius, out var destination)) MoveTo(destination, wanderSpeed, 0.2f);
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

        // ------------------------------------------------------------------ 피격 / 사망

        // 눈이 없어도 맞은 방향은 느낀다 -> 그쪽으로 가서 더듬는다
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
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        }

        // ------------------------------------------------------------------ 연출 (상태 색상 / 촉수 움직임)

        void CacheTentacles()
        {
            if (tentacles == null) { chains = new Segment[0][]; return; }

            chains = new Segment[tentacles.Length][];
            for (int i = 0; i < tentacles.Length; i++)
            {
                var list = new List<Segment>();
                Transform segment = tentacles[i];
                while (segment != null)
                {
                    list.Add(new Segment { transform = segment, baseRotation = segment.localRotation });
                    segment = segment.childCount > 0 ? segment.GetChild(0) : null;
                }
                chains[i] = list.ToArray();
            }
        }

        TentaclePose CurrentPose()
        {
            switch (State)
            {
                case Mode.Approach: return new TentaclePose { yawAmp = 8, pitch = 0, pitchAmp = 4, speed = 3.2f, curl = 6 };
                case Mode.Grope: return new TentaclePose { yawAmp = 50, pitch = 25, pitchAmp = 15, speed = 2.4f, curl = 14 }; // 넓게 휘저으며 바닥을 짚음
                case Mode.Strike:
                    return Time.time - strikeStart < strikeWindup
                        ? new TentaclePose { yawAmp = 0, pitch = -40, pitchAmp = 0, speed = 1, curl = 10 }  // 치켜들기
                        : new TentaclePose { yawAmp = 0, pitch = 40, pitchAmp = 0, speed = 1, curl = 0 };   // 내려치기
                case Mode.Dead: return new TentaclePose { yawAmp = 0, pitch = 85, pitchAmp = 0, speed = 0, curl = 20 };
                default: return new TentaclePose { yawAmp = 10, pitch = 20, pitchAmp = 5, speed = 1, curl = 8 };
            }
        }

        void LateUpdate()
        {
            if (bodyRenderer != null)
            {
                Color color = State == Mode.Dead ? DeadColor : State == Mode.Strike ? StrikeColor : State == Mode.Grope ? GropeColor : State == Mode.Approach ? ApproachColor : WanderColor;
                if (State == Mode.Stunned) color = new Color(0.45f, 0.8f, 1);
                colors.SetColor("_BaseColor", color);
                bodyRenderer.SetPropertyBlock(colors);
            }

            if (State == Mode.Stunned || chains == null || chains.Length == 0) return;

            TentaclePose pose = CurrentPose();
            float time = Time.time;
            float blend = 1f - Mathf.Exp(-10f * Time.deltaTime);
            for (int i = 0; i < chains.Length; i++)
            {
                var chain = chains[i];
                if (chain == null || chain.Length == 0) continue;

                float phase = i * 1.7f;
                float yaw = Mathf.Sin(time * pose.speed + phase) * pose.yawAmp;
                float pitch = pose.pitch + Mathf.Sin(time * pose.speed * 1.9f + phase) * pose.pitchAmp;
                for (int s = 0; s < chain.Length; s++)
                {
                    Quaternion offset = s == 0
                        ? Quaternion.Euler(pitch, yaw, 0)
                        : Quaternion.Euler(pose.curl * 0.5f + Mathf.Sin(time * pose.speed * 1.4f - s * 0.9f + phase) * pose.curl * 0.5f, yaw * 0.25f, 0);
                    chain[s].transform.localRotation = Quaternion.Slerp(chain[s].transform.localRotation, chain[s].baseRotation * offset, blend);
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            Vector3 chest = transform.position + Vector3.up * tentacleHeight;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, proximitySenseRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(chest, tentacleReach);
            if (Application.isPlaying && (State == Mode.Grope || State == Mode.Strike))
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(gropeCenter, gropeRadius);
            }
        }
    }
}
