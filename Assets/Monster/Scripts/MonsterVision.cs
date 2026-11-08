/* [ 코드 설명 ] 거리·전방 시야각·실제 Collider 가림으로 살아 있는 플레이어를 감지합니다. 소리 입력은 없으며 가려진 대상 위치를 추적하지 않습니다. */
using UnityEngine;
using VoidLast.Player;

namespace VoidLast.Monster
{
    public sealed class MonsterVision : MonoBehaviour
    {
        [SerializeField] Vector3 eyeOffset = new Vector3(0, 2.3f, 0.35f);
        [SerializeField, Min(1)] float sightDistance = 15;
        [SerializeField, Range(1, 360)] float fieldOfView = 120;
        [SerializeField] LayerMask sightMask = ~0;
        public Vector3 Eye => transform.TransformPoint(eyeOffset);
        public float SightDistance => sightDistance;
        public bool CanSee(FpsHealth target, bool turnTowardHit = false)
        {
            if (target == null || target.IsDead || !target.isActiveAndEnabled) return false;
            var capsule = target.GetComponent<CharacterController>();
            if (capsule == null || !capsule.enabled) return false;
            Vector3 delta = capsule.bounds.center - Eye;
            if (delta.sqrMagnitude > sightDistance * sightDistance) return false;
            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (!turnTowardHit && flat.sqrMagnitude > 0.001f && Vector3.Angle(transform.forward, flat) > fieldOfView * 0.5f) return false;
            return ClearLine(Eye, capsule.bounds.center, target.transform) || ClearLine(Eye, capsule.bounds.center + Vector3.up * capsule.bounds.extents.y * 0.7f, target.transform);
        }
        // 최적화: 레이캐스트 결과를 담을 배열을 재사용해 매번 새 메모리를 만들지 않는다 (GC 끊김 방지)
        static readonly RaycastHit[] hitBuffer = new RaycastHit[64];
        public bool ClearLine(Vector3 origin, Vector3 point, Transform target)
        {
            Vector3 delta = point - origin;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, hitBuffer, delta.magnitude, sightMask, QueryTriggerInteraction.Ignore);
            if (count >= hitBuffer.Length)   // 너무 많이 맞으면 예전 방식으로 전부 확인
            {
                foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, sightMask, QueryTriggerInteraction.Ignore))
                    if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(target)) return false;
                return true;
            }
            for (int i = 0; i < count; i++)
                if (!hitBuffer[i].transform.IsChildOf(transform) && !hitBuffer[i].transform.IsChildOf(target)) return false;
            return true;
        }

        // 최적화: 씬 전체에서 대원을 찾는 일을 0.5초에 한 번만 하고, 모든 몬스터가 같은 목록을 나눠 쓴다
        static FpsHealth[] playerCache = new FpsHealth[0];
        static float playerCacheTime = float.NegativeInfinity;
        static FpsHealth[] Players()
        {
            if (Time.time - playerCacheTime > 0.5f || Time.time < playerCacheTime)
            {
                playerCache = FindObjectsByType<FpsHealth>(FindObjectsInactive.Exclude);
                playerCacheTime = Time.time;
            }
            return playerCache;
        }
        public FpsHealth FindVisible(FpsHealth preferred = null)
        {
            if (CanSee(preferred)) return preferred;
            FpsHealth nearest = null; float distance = float.PositiveInfinity;
            foreach (var candidate in Players())
            {
                if (candidate == null) continue;   // 목록을 만든 뒤 사라진 대원
                float d = (candidate.transform.position - transform.position).sqrMagnitude;
                if (d < distance && CanSee(candidate)) { nearest = candidate; distance = d; }
            }
            return nearest;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan; Vector3 eye = transform.TransformPoint(eyeOffset);
            Gizmos.DrawWireSphere(eye, sightDistance);
            Gizmos.DrawRay(eye, Quaternion.AngleAxis(-fieldOfView * 0.5f, Vector3.up) * transform.forward * sightDistance);
            Gizmos.DrawRay(eye, Quaternion.AngleAxis(fieldOfView * 0.5f, Vector3.up) * transform.forward * sightDistance);
        }
    }
}
