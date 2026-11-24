/* [ 코드 설명 ] 충돌면에 작은 탄흔을 붙이고 30초 후 제거합니다. 모든 사격자가 공유하는 FIFO 목록으로 월드 전체를 최대 7개로 제한하며 Collider를 사용하지 않습니다. */
using System.Collections.Generic;
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsBulletMark : MonoBehaviour
    {
        public const int MaximumCount = 7;
        public const float Lifetime = 30;
        static readonly List<FpsBulletMark> marks = new List<FpsBulletMark>(MaximumCount);
        float expiresAt;
        public static int ActiveCount { get { marks.RemoveAll(m => m == null || !m.gameObject.activeSelf); return marks.Count; } }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => marks.Clear();
        public static FpsBulletMark Spawn(FpsBulletMark prefab, RaycastHit hit)
        {
            if (prefab == null || hit.collider == null) return null;
            if (ActiveCount >= MaximumCount)
            {
                var oldest = marks[0]; marks.RemoveAt(0); oldest.gameObject.SetActive(false); Destroy(oldest.gameObject);
            }
            var mark = Instantiate(prefab, hit.point + hit.normal * 0.002f, Quaternion.FromToRotation(Vector3.forward, hit.normal));
            mark.transform.SetParent(hit.collider.transform, true);
            mark.expiresAt = Time.time + Lifetime; marks.Add(mark); return mark;
        }
        void Update()
        {
            if (Time.time < expiresAt) return;
            gameObject.SetActive(false); Destroy(gameObject);
        }
        void OnDestroy() => marks.Remove(this);
    }
}
