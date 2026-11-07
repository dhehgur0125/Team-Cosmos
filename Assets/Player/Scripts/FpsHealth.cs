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
        /// <summary>연출(층 이동 등) 중에는 피해를 받지 않는다</summary>
        public static bool DamageBlocked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { DamageBlocked = false; }

        void Awake() => Current = Mathf.Clamp(startingHealth, 0, maximumHealth);
        void Start() { if (IsDead) HandleDeath(); }
        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0 || float.IsNaN(amount) || DamageBlocked) return;
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
        /// <summary>층 이동 후: 이전 층에서의 HP를 그대로 이어받는다 (0이면 사망 상태로)</summary>
        public void RestoreFromCarryOver(float value)
        {
            if (float.IsNaN(value) || NetworkDamageRedirect != null) return;
            Current = Mathf.Clamp(value, 0, maximumHealth);
            Changed?.Invoke();
            if (IsDead) HandleDeath();
        }
        /// <summary>복제본 전용: 주인이 알려준 HP를 그대로 표시한다. 0이 되면 사망 연출도 한 번 재생한다.</summary>
        public void ApplyNetworkHealth(float value)
        {
            if (float.IsNaN(value)) return;
            value = Mathf.Clamp(value, 0, maximumHealth);
            if (Mathf.Approximately(value, Current)) return;
            if (deathHandled && value > 0) { ApplyRevive(value); return; }
            Current = value;
            Changed?.Invoke();
            if (IsDead) HandleDeath();
        }
        /// <summary>고치에서 구출: 죽은 대원을 주어진 HP로 되살린다. 복제본은 주인이 알려 준 HP로 함께 되살아난다.</summary>
        public void Revive(float value)
        {
            if (!IsDead || float.IsNaN(value) || value <= 0 || NetworkDamageRedirect != null) return;
            ApplyRevive(value);
        }
        void ApplyRevive(float value)
        {
            Current = Mathf.Clamp(value, 1, maximumHealth);
            deathHandled = false;
            GetComponent<FpsAnimation>()?.Revive();
            Changed?.Invoke();
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
