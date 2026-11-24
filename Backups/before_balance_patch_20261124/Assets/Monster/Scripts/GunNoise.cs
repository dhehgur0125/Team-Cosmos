/* [ 코드 설명 ] 권총 사격 소리를 몬스터에게 알리는 통로입니다. 쪼꼬미·촉수형처럼 소리를 듣는 몬스터가 구독하고, 눈 있는 몬스터는 듣지 않습니다. */
using System;
using UnityEngine;

namespace VoidLast.Monster
{
    public static class GunNoise
    {
        // origin: 쏜 위치 / radius: 소리가 들리는 반경(m)
        public static event Action<Vector3, float> Emitted;

        public static void Emit(Vector3 origin, float radius) => Emitted?.Invoke(origin, radius);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Emitted = null;
    }
}
