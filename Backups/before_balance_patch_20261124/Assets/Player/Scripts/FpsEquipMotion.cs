/* [ 코드 설명 ] 권총과 아이템이 공유하는 짧은 장착 진행 상태입니다. 표시 해제나 슬롯 변경 시 초기화하고 장착 완료 여부와 부드러운 상승 비율을 제공합니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsEquipMotion
    {
        float elapsed;
        bool visible;
        public int Key { get; private set; } = -1;
        public float Blend { get; private set; }
        public bool IsReady { get; private set; }
        public void Reset() { elapsed = Blend = 0; visible = IsReady = false; Key = -1; }
        public void Tick(bool show, int key, float deltaTime, float duration)
        {
            if (!show) { Reset(); return; }
            if (!visible || Key != key) elapsed = 0;
            visible = true; Key = key;
            duration = Mathf.Max(0.01f, duration);
            elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0, deltaTime));
            IsReady = elapsed >= duration;
            Blend = Mathf.SmoothStep(0, 1, elapsed / duration);
        }
    }
}
