/* [ 코드 설명 ] 월드 주사기의 중복 획득을 막습니다. 맵 배치품은 E로만, 드랍품은 지연 시간 후 접근 Trigger로 획득하며 드랍한 플레이어는 범위를 한 번 벗어나야 합니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsWorldPickup : MonoBehaviour
    {
        [SerializeField] FarmingItemKind item = FarmingItemKind.Syringe;
        [SerializeField] SphereCollider pickupTrigger;
        [SerializeField, Min(0)] float pickupDelay = 1.2f;
        readonly Collider[] nearby = new Collider[32];
        FpsInventory dropper;
        bool claimed, dropped, dropperLeftRange;
        float availableAt;
        public bool IsDropped => dropped;
        public bool DropperLeftRange => dropperLeftRange;
        public bool IsClaimed => claimed;
        public FarmingItemKind Item => item;
        /// <summary>누군가 이 아이템을 주웠을 때 (멀티플레이: 다른 대원 화면에서도 없애기 위해 사용)</summary>
        public static event Action<FpsWorldPickup, FpsInventory> Collected;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Collected = null;

        public void ConfigureDrop(FpsInventory owner)
        {
            dropped = true; dropper = owner; dropperLeftRange = false;
            availableAt = Time.time + pickupDelay;
            var body = GetComponent<Rigidbody>(); body.isKinematic = false; body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        void FixedUpdate()
        {
            if (!dropped || dropperLeftRange || dropper == null || pickupTrigger == null) return;
            int count = Physics.OverlapSphereNonAlloc(pickupTrigger.transform.TransformPoint(pickupTrigger.center), pickupTrigger.radius * pickupTrigger.transform.lossyScale.x, nearby, ~0, QueryTriggerInteraction.Ignore);
            if (count == nearby.Length) return;
            for (int i = 0; i < count; i++) if (nearby[i].GetComponentInParent<FpsInventory>() == dropper) return;
            dropperLeftRange = true;
        }
        void OnTriggerStay(Collider other)
        {
            if (!dropped || Time.time < availableAt) return;
            var inventory = other.GetComponentInParent<FpsInventory>();
            if (inventory != null) TryCollect(inventory);
        }
        public bool TryCollect(FpsInventory inventory)
        {
            if (claimed || inventory == null || inventory.IsNetworkProxy || !inventory.IsAlive) return false;
            if (dropped && (Time.time < availableAt || inventory == dropper && !dropperLeftRange)) return false;
            if (!inventory.TryAdd(item)) return false;
            claimed = true;
            Collected?.Invoke(this, inventory);
            gameObject.SetActive(false); Destroy(gameObject);
            return true;
        }
        /// <summary>다른 대원이 주웠다는 네트워크 알림을 받았을 때 이 화면에서도 없앤다.</summary>
        public void RemoveByNetwork()
        {
            if (claimed) return;
            claimed = true;
            gameObject.SetActive(false); Destroy(gameObject);
        }
    }
}
