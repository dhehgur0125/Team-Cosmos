/* [ 코드 설명 ] 몬스터 HP와 실제 명중 피해를 관리하고 피격자·피격 시점의 공격자 위치를 AI에 전달합니다. 사망 이벤트는 한 번만 발생합니다. */
using System;
using UnityEngine;
using VoidLast.Player;

namespace VoidLast.Monster
{
    public sealed class MonsterHealth : MonoBehaviour, IFpsDamageTarget
    {
        [SerializeField, Min(1)] float maximumHealth = 300;
        public float Maximum => maximumHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0;
        public event Action<GameObject, Vector3> Damaged;
        public event Action Died;
        /// <summary>멀티플레이: 복제본에 맞은 피해는 직접 깎지 않고 서버로 보낸다.</summary>
        public Action<float, GameObject, Vector3> NetworkHitRedirect;
        void Awake() => Current = maximumHealth;
        public void TakeHit(float damage, GameObject attacker, Vector3 hitPoint)
        {
            if (IsDead || damage <= 0 || float.IsNaN(damage)) return;
            if (NetworkHitRedirect != null) { NetworkHitRedirect(damage, attacker, hitPoint); return; }
            Current = Mathf.Max(0, Current - damage);
            if (IsDead) Died?.Invoke();
            else Damaged?.Invoke(attacker, attacker != null ? attacker.transform.position : hitPoint);
        }
        /// <summary>복제본 전용: 서버의 HP를 반영한다. 0이 되면 사망 이벤트를 한 번 낸다.</summary>
        public void ApplyNetworkHealth(float value)
        {
            if (float.IsNaN(value) || IsDead) return;
            Current = Mathf.Clamp(value, 0, maximumHealth);
            if (IsDead) Died?.Invoke();
        }
    }
}
