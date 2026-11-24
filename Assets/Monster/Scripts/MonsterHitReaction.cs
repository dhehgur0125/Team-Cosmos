/* [ 코드 설명 ] 총에 맞은 몬스터가 잠깐 느려지고, 짧은 시간에 여러 발 맞으면 움찔(짧은 스턴) 멈추게 합니다.
   총은 몬스터를 죽이는 도구가 아니라 도망칠 시간을 버는 도구라는 기획(몬스터 위협 강화 기획 제안 3)입니다.
   - 맞을 때마다: GameBalance의 hitSlowTo 비율 속도로 hitSlowSeconds 동안 (NavMeshAgent 속도에 곱함)
   - flinchWindow초 안에 flinchHits발 맞으면 flinchSeconds 동안 멈춤 (MonsterStun 사용, 공격도 취소). 그 뒤 flinchCooldown초는 다시 움찔 안 함
   방장·혼자 실행에서만 동작합니다 (복제본은 AI가 꺼져 있고 위치는 서버에서 받음). MonsterHealth.Awake가 자동으로 붙입니다. */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace VoidLast.Monster
{
    [DisallowMultipleComponent]
    public sealed class MonsterHitReaction : MonoBehaviour
    {
        MonsterHealth health;
        NavMeshAgent agent;
        GameBalance.MonsterStats stats;
        bool hasStats;
        float slowUntil, flinchReadyAt;
        float baseSpeed = -1f, appliedSpeed = -1f;
        readonly Queue<float> hits = new Queue<float>();

        /// <summary>움찔한 횟수 (시험용)</summary>
        public int Flinches { get; private set; }
        public bool IsSlowed => Time.time < slowUntil;

        void Awake()
        {
            health = GetComponent<MonsterHealth>();
            agent = GetComponent<NavMeshAgent>();
            hasStats = GameBalance.TryGetStatsOf(GameBalance.KindOf(gameObject), out stats);
        }

        void OnEnable() { if (health != null) health.Damaged += OnDamaged; }
        void OnDisable() { if (health != null) health.Damaged -= OnDamaged; RestoreSpeed(); }

        void OnDamaged(GameObject attacker, Vector3 from)
        {
            if (!hasStats || health == null || health.IsDead || health.NetworkHitRedirect != null) return;
            float now = Time.time;
            if (stats.hitSlowSeconds > 0) slowUntil = Mathf.Max(slowUntil, now + stats.hitSlowSeconds);

            if (stats.flinchHits <= 0 || stats.flinchSeconds <= 0) return;
            var b = GameBalance.Current;
            hits.Enqueue(now);
            while (hits.Count > 0 && now - hits.Peek() > b.flinchWindow) hits.Dequeue();
            if (hits.Count < stats.flinchHits || now < flinchReadyAt) return;

            hits.Clear();
            var stun = GetComponent<MonsterStun>();
            if (stun == null) stun = gameObject.AddComponent<MonsterStun>();
            if (stun.Apply(stats.flinchSeconds))
            {
                Flinches++;
                flinchReadyAt = now + stats.flinchSeconds + b.flinchCooldown;
            }
        }

        void LateUpdate()
        {
            if (agent == null || !agent.enabled) return;
            // AI가 속도를 새로 정했으면 그 값을 기준 속도로 삼는다
            if (!Mathf.Approximately(agent.speed, appliedSpeed)) baseSpeed = agent.speed;
            float factor = hasStats && IsSlowed && health != null && !health.IsDead ? stats.hitSlowTo : 1f;
            appliedSpeed = baseSpeed * factor;
            agent.speed = appliedSpeed;
        }

        void RestoreSpeed()
        {
            if (agent != null && agent.enabled && baseSpeed >= 0 && Mathf.Approximately(agent.speed, appliedSpeed)) agent.speed = baseSpeed;
            appliedSpeed = -1f;
        }
    }
}
