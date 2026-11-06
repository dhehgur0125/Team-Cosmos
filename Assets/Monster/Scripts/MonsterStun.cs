/*
[ 코드 설명 ]
1. 기존 세 몬스터 AI가 공유하는 스턴 종료 시간만 관리합니다.
2. 피해 없이 현재 공격을 취소하고 AI의 스턴 상태로 진입합니다. 중복 적용은 종료 시간만 연장하며 사망한 몬스터는 회복시키지 않습니다.
*/
using UnityEngine;

namespace VoidLast.Monster
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonsterHealth))]
    public sealed class MonsterStun : MonoBehaviour
    {
        MonsterHealth health;
        float endsAt;
        public bool IsStunned => isActiveAndEnabled && health != null && !health.IsDead && Time.time < endsAt;
        public float Remaining => IsStunned ? endsAt - Time.time : 0;
        void Awake() => health = GetComponent<MonsterHealth>();
        void OnDisable() => endsAt = 0;
        public bool Apply(float duration)
        {
            if (!isActiveAndEnabled || health.IsDead || health.NetworkHitRedirect != null || duration <= 0 || !float.IsFinite(duration)) return false;
            bool wasStunned = IsStunned;
            endsAt = Mathf.Max(endsAt, Time.time + duration);
            if (wasStunned) return true;
            GetComponent<MonsterMelee>()?.Cancel();
            GetComponent<EyedMonsterAI>()?.BeginStun(this);
            GetComponent<SmallMonsterAI>()?.BeginStun(this);
            GetComponent<BlindMonsterAI>()?.BeginStun(this);
            return true;
        }
    }
}
