/* [ 코드 설명 ] 쪼꼬미의 비명을 다른 종류의 몬스터에게 알리는 통로입니다. 눈 없는 몬스터(촉수형·눈 없는 대형)가 구독해 비명이 난 위치로 옵니다.
   쪼꼬미끼리는 SmallMonsterAI가 직접 알리므로 이 통로를 쓰지 않습니다. */
using System;
using UnityEngine;

namespace VoidLast.Monster
{
    public static class ScreamNoise
    {
        /// <summary>비명이 난 위치, 들리는 반경</summary>
        public static event Action<Vector3, float> Emitted;
        public static void Emit(Vector3 origin, float radius) => Emitted?.Invoke(origin, radius);
    }
}
