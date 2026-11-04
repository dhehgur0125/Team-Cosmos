/* [ 코드 설명 ] HP를 0~최대값으로 제한하고 피해·회복 API와 변경 이벤트를 제공합니다. HP가 0이면 기존 FpsAnimation.Die를 한 번 호출합니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    [DefaultExecutionOrder(-250)]
    public sealed class FpsHealth : MonoBehaviour
    {
        [SerializeField, Min(1)] float maximumHealth = 100;
        [SerializeField, Min(0)] float startingHealth = 100;
        public float Maximum => maximumHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0;
        public bool CanHeal => !IsDead && Current < maximumHealth;
        public event Action Changed;
        public event Action Died;
        /// <summary>멀티플레이: 다른 대원의 복제본이면 피해·회복을 직접 바꾸지 않고 주인 클라이언트에 전달한다.</summary>
        public Action<float> NetworkDamageRedirect;
        public Func<float, bool> NetworkHealRedirect;
        bool deathHandled;

        void Awake() => Current = Mathf.Clamp(startingHealth, 0, maximumHealth);
        void Start() { if (IsDead) HandleDeath(); }
        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0 || float.IsNaN(amount)) return;
            if (NetworkDamageRedirect != null) { NetworkDamageRedirect(amount); return; }
            Current = Mathf.Clamp(Current - amount, 0, maximumHealth);
            Changed?.Invoke();
            if (IsDead) HandleDeath();
        }
        public bool Heal(float amount)
        {
            if (!CanHeal || amount <= 0 || float.IsNaN(amount)) return false;
            if (NetworkHealRedirect != null) return NetworkHealRedirect(amount);
            Current = Mathf.Clamp(Current + amount, 0, maximumHealth);
            Changed?.Invoke();
            return true;
        }
        /// <summary>복제본 전용: 주인이 알려준 HP를 그대로 표시한다. 0이 되면 사망 연출도 한 번 재생한다.</summary>
        public void ApplyNetworkHealth(float value)
        {
            if (float.IsNaN(value)) return;
            value = Mathf.Clamp(value, 0, maximumHealth);
            if (Mathf.Approximately(value, Current)) return;
            if (deathHandled && value > 0) return; // 부활은 아직 지원하지 않음
            Current = value;
            Changed?.Invoke();
            if (IsDead) HandleDeath();
        }
        void HandleDeath()
        {
            if (deathHandled) return;
            deathHandled = true;
            GetComponent<FpsAnimation>()?.Die();
            Died?.Invoke();
        }
        void OnValidate()
        {
            maximumHealth = Mathf.Max(1, maximumHealth);
            startingHealth = Mathf.Clamp(startingHealth, 0, maximumHealth);
        }
    }
}
