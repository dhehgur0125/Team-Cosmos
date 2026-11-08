/* [ 코드 설명 ] 3번 슬롯의 의료 키트만 담당합니다. 장착 후 1.5초 치료가 끝나면 HP 50을 회복하고 공유 횟수를 차감합니다. 슬롯·대상·거리·가림 상태를 계속 확인하며 기존 주사기와 장비 선택 코드는 변경하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    [DisallowMultipleComponent]
    public sealed class FpsMedicalKit : MonoBehaviour
    {
        [SerializeField] FpsMedicalKitView view;
        [SerializeField, Min(1)] int maximumUses = 3;
        [SerializeField, Min(0)] int startingUses = 3;
        [SerializeField, Min(1)] float healAmount = 50;
        [SerializeField, Min(0.1f)] float useDuration = 1.5f;
        [SerializeField, Range(0.5f, 2.2f)] float treatmentDistance = 2;
        FpsEquipment equipment;
        FpsInput input;
        FpsHealth health, recipient;
        FpsItemInteractor interactor;
        FpsMedicalKitNetwork network;
        int remaining;
        float elapsed;
        bool Online => network != null && network.IsSpawned;
        public int MaximumUses => maximumUses;
        public int StartingUses => Mathf.Clamp(startingUses, 0, maximumUses);
        public int RemainingUses => Online ? network.RemainingUses : remaining;
        public float HealAmount => healAmount;
        public float UseDuration => useDuration;
        FpsUniqueItem unique;
        FpsUniqueItem Unique => unique != null ? unique : unique = FpsUniqueItem.For(gameObject);
        // 고유 아이템으로 힐러를 고른 대원만 3번 칸에서 꺼낸다
        public bool IsSelected => isActiveAndEnabled && health != null && !health.IsDead && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Item && Unique.Has(UniqueItemKind.Healer);
        public bool IsUsing => Online ? network.IsUsing : recipient != null;
        public bool UsingOnOther => Online ? network.UsingOnOther : recipient != null && recipient != health;
        public float UseProgress => Online ? network.UseProgress : IsUsing ? Mathf.Clamp01(elapsed / useDuration) : 0;
        public bool IsLocal => Online ? network.IsOwner : equipment != null && !equipment.IsNetworkProxy;

        void Awake()
        {
            equipment = GetComponent<FpsEquipment>(); input = GetComponent<FpsInput>(); health = GetComponent<FpsHealth>();
            interactor = GetComponent<FpsItemInteractor>(); network = GetComponent<FpsMedicalKitNetwork>(); remaining = StartingUses;
        }
        void OnEnable() { equipment.SlotChanged += CancelUse; health.Died += CancelUse; }
        void OnDisable() { equipment.SlotChanged -= CancelUse; health.Died -= CancelUse; CancelUse(); }
        void Update()
        {
            if (!IsLocal) return;
            if (input != null && input.isActiveAndEnabled && !FpsInput.Blocked)
            {
                if (input.Attack) TryStartUse();
                else if (input.AimPressed)
                {
                    var target = interactor != null ? interactor.FindUseTarget() : null;
                    if (target != null) TryStartUse(target);
                }
            }
            if (!Online) TickUse(Time.deltaTime);
        }
        public bool CanTreat(FpsHealth target)
        {
            if (!IsSelected || FpsInput.Blocked || view == null || !view.isActiveAndEnabled || !view.IsEquipped || target == null || !target.isActiveAndEnabled || !target.CanHeal) return false;
            return target == health || interactor != null && Vector3.Distance(transform.position, target.transform.position) <= treatmentDistance && interactor.CanReachPlayer(target);
        }
        public bool TryStartUse(FpsHealth target = null)
        {
            target = target == null ? health : target;
            if (!IsLocal || IsUsing || RemainingUses <= 0 || !CanTreat(target)) return false;
            if (Online) return network.RequestUse(target);
            // Never treat an online proxy through an offline path.
            if (target.NetworkHealRedirect != null) return false;
            recipient = target; elapsed = 0; return true;
        }
        public void TickUse(float deltaTime)
        {
            if (Online || !IsUsing) return;
            if (!CanTreat(recipient)) { CancelUse(); return; }
            elapsed += Mathf.Max(0, deltaTime);
            if (elapsed < useDuration) return;
            var target = recipient; recipient = null; elapsed = 0;
            if (target.Heal(healAmount)) remaining--;
        }
        public void CancelUse()
        {
            recipient = null; elapsed = 0;
            if (Online && network.IsOwner) network.CancelUse();
        }
    }
}
