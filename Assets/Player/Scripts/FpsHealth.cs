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
        bool deathHandled;

        void Awake() => Current = Mathf.Clamp(startingHealth, 0, maximumHealth);
        void Start() { if (IsDead) HandleDeath(); }
        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0 || float.IsNaN(amount)) return;
            Current = Mathf.Clamp(Current - amount, 0, maximumHealth);
            Changed?.Invoke();
            if (IsDead) HandleDeath();
        }
        public bool Heal(float amount)
        {
            if (!CanHeal || amount <= 0 || float.IsNaN(amount)) return false;
            Current = Mathf.Clamp(Current + amount, 0, maximumHealth);
            Changed?.Invoke();
            return true;
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
