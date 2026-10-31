/* [ 코드 설명 ] FPS 히트스캔이 명중 피해와 공격자 정보를 전달할 수 있는 최소 인터페이스입니다. 구체적인 적 종류와 AI에는 의존하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public interface IFpsDamageTarget
    {
        void TakeHit(float damage, GameObject attacker, Vector3 hitPoint);
    }
}
