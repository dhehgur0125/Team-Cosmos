/* [ 코드 설명 ] F 상호작용은 작은 SphereCast와 가림 검사로 주사기를 선택합니다. 우클릭 대상 플레이어의 거리·시야를 확인하며 Q 드랍 경로와 생성 공간을 검사합니다. */
using System.Linq;
using UnityEngine;

namespace VoidLast.Player
{
    [DefaultExecutionOrder(-40)]
    public sealed class FpsItemInteractor : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] FpsWorldPickup syringePickupPrefab;
        [SerializeField, Min(0.1f)] float pickupDistance = 2.5f;
        [SerializeField, Range(0.02f, 0.2f)] float pickupAssistRadius = 0.1f;
        [SerializeField, Min(0.5f)] float playerUseDistance = 2.2f;
        [SerializeField, Min(0.6f)] float dropDistance = 1f;
        [SerializeField, Min(0.05f)] float dropClearance = 0.25f;
        FpsInput input;
        FpsInventory inventory;
        CharacterController capsule;
        public FpsWorldPickup Target { get; private set; }
        public FpsWorldPickup LastDropped { get; private set; }
        public float PickupDistance => pickupDistance;
        void Awake() { input = GetComponent<FpsInput>(); inventory = GetComponent<FpsInventory>(); capsule = GetComponent<CharacterController>(); }
        void Update()
        {
            UpdateTarget();
            if (input.Interact) TryPickup();
            if (input.Drop) TryDrop();
        }
        public void UpdateTarget()
        {
            Target = null;
            if (!inventory.IsAlive || viewCamera == null) return;
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            foreach (var hit in Physics.SphereCastAll(ray, pickupAssistRadius, pickupDistance, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                var pickup = hit.collider.GetComponentInParent<FpsWorldPickup>();
                if (pickup != null && VisibleCollider(hit.collider, pickup.transform)) { Target = pickup; return; }
            }
        }
        bool VisibleCollider(Collider target, Transform owner)
        {
            Vector3 origin = viewCamera.transform.position;
            Vector3 delta = target.bounds.center - origin;
            foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude + 0.02f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                return hit.collider.transform.IsChildOf(owner);
            }
            return false;
        }
        public FpsHealth FindUseTarget()
        {
            if (!inventory.IsAlive) return null;
            var ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            foreach (var hit in Physics.SphereCastAll(ray, 0.16f, playerUseDistance, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                var target = hit.collider.GetComponentInParent<FpsHealth>();
                if (target != null && target.gameObject != gameObject && CanReachPlayer(target)) return target;
            }
            return null;
        }
        public bool CanReachPlayer(FpsHealth target)
        {
            if (target == null || !target.isActiveAndEnabled || target.IsDead || target.gameObject == gameObject || Vector3.Distance(transform.position, target.transform.position) > playerUseDistance) return false;
            var collider = target.GetComponent<Collider>();
            var targetMotor = target.GetComponent<FpsMotor>();
            return collider != null && collider.enabled && (targetMotor == null || !targetMotor.IsDead) && VisibleCollider(collider, target.transform);
        }
        public bool TryPickup()
        {
            UpdateTarget();
            return Target != null && Target.TryCollect(inventory);
        }
        public bool TryDrop()
        {
            if (!inventory.IsAlive || inventory.IsUsing || inventory.ItemAt(inventory.SelectedIndex) == FarmingItemKind.None || syringePickupPrefab == null) return false;
            Vector3 direction = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.1f) direction = transform.forward;
            Vector3 start = transform.position + Vector3.up * (capsule.height * 0.65f);
            Vector3 position = start + direction * Mathf.Max(dropDistance, capsule.radius + dropClearance + 0.1f);
            foreach (var hit in Physics.SphereCastAll(start, dropClearance, direction, Vector3.Distance(start, position), ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform)) return false;
            if (Physics.CheckSphere(position, dropClearance, ~0, QueryTriggerInteraction.Ignore)) return false;
            var pickup = Instantiate(syringePickupPrefab, position, Quaternion.LookRotation(direction) * Quaternion.Euler(0, 0, 25));
            if (!inventory.TryRemoveSelected(out _)) { Destroy(pickup.gameObject); return false; }
            pickup.ConfigureDrop(inventory);
            pickup.GetComponent<Rigidbody>().linearVelocity = direction * 0.6f + Vector3.up * 0.2f;
            LastDropped = pickup;
            return true;
        }
    }
}
