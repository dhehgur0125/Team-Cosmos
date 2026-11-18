/* [ 코드 설명 ] Fps Weapons Pack 권총 클립(장착·해제·재장전·발사)을 총 본체 회전만 추려 미리 저장해 둔 데이터입니다. FpsWeaponView가 시간에 맞춰 읽어 WeaponRoot(총+장갑)에 적용하며, 에디터의 'VOID LAST > Player > Bake Pistol Motions'로 다시 만들 수 있습니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    [CreateAssetMenu(menuName = "VOID LAST/Pistol Motions", fileName = "FpsPistolMotions")]
    public sealed class FpsPistolMotions : ScriptableObject
    {
        [Serializable]
        public sealed class Motion
        {
            public float length;
            public AnimationCurve x = new AnimationCurve(), y = new AnimationCurve(), z = new AnimationCurve(), w = new AnimationCurve();
            public bool IsValid => length > 0 && x != null && x.length > 0 && y != null && z != null && w != null;
            /// <summary>대기 자세 기준 회전 변화량. 시간은 0~length로 제한한다.</summary>
            public Quaternion Evaluate(float time)
            {
                time = Mathf.Clamp(time, 0, length);
                var q = new Quaternion(x.Evaluate(time), y.Evaluate(time), z.Evaluate(time), w.Evaluate(time));
                float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                if (magnitude < 0.0001f) return Quaternion.identity;
                float inverse = 1 / magnitude;
                return new Quaternion(q.x * inverse, q.y * inverse, q.z * inverse, q.w * inverse);
            }
        }

        public Motion draw = new Motion();
        public Motion holster = new Motion();
        public Motion reload = new Motion();
        public Motion fireHip = new Motion();
        public Motion fireAds = new Motion();
        public bool IsValid => draw.IsValid && holster.IsValid && reload.IsValid && fireHip.IsValid && fireAds.IsValid;
    }
}
