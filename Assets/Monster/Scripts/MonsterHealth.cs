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
        void Awake() => Current = maximumHealth;
        public void TakeHit(float damage, GameObject attacker, Vector3 hitPoint)
        {
            if (IsDead || damage <= 0 || float.IsNaN(damage)) return;
            Current = Mathf.Max(0, Current - damage);
            if (IsDead) Died?.Invoke();
            else Damaged?.Invoke(attacker, attacker != null ? attacker.transform.position : hitPoint);
        }
    }
}
