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
        public bool ClearLine(Vector3 origin, Vector3 point, Transform target)
        {
            Vector3 delta = point - origin;
            foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, sightMask, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(target)) return false;
            return true;
        }
        public FpsHealth FindVisible(FpsHealth preferred = null)
        {
            if (CanSee(preferred)) return preferred;
            FpsHealth nearest = null; float distance = float.PositiveInfinity;
            foreach (var candidate in FindObjectsByType<FpsHealth>(FindObjectsSortMode.InstanceID))
            {
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
