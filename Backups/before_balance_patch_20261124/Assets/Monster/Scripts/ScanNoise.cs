/* [ 코드 설명 ] 탐지기 스캔이 일어났다는 사실을 몬스터에게 알리는 가벼운 알림 통로입니다. 탐지기는 어떤 몬스터가 있는지 몰라도 소리만 내보내고, 소리를 들을 수 있는 몬스터(눈 없는 몬스터)가 스스로 구독해서 반응합니다. */
using System;
using UnityEngine;

namespace VoidLast.Monster
{
    public static class ScanNoise
    {
        // origin: 스캔한 위치 / radius: 소리가 들리는 반경(m) / waveSpeed: 파동 속도(m/s, 0 이하면 즉시 도달)
        public static event Action<Vector3, float, float> Emitted;

        public static void Emit(Vector3 origin, float radius, float waveSpeed) => Emitted?.Invoke(origin, radius, waveSpeed);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Emitted = null; // 도메인 리로드를 꺼도 이전 플레이의 구독이 남지 않게 초기화
    }
}
