/* [ 코드 설명 ] Character_test 씬 전용 H 키 테스트입니다. Editor/Development Build에서만 현재 HP 절반만큼 피해를 주며 실제 피해 입력과 분리합니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsHealthDebugInput : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        FpsHealth health;
        FpsInput input;
        void Awake() { health = GetComponent<FpsHealth>(); input = GetComponent<FpsInput>(); }
        void Update() { if (input.HalveHealth) HalveCurrentHealth(); }
        public void HalveCurrentHealth() => health.TakeDamage(health.Current * 0.5f);
#endif
    }
}
