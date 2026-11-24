/* [ 코드 설명 ] 1~3번 기존 장비와 4~6번 파밍 슬롯 선택·권총 조준 상태를 관리합니다. 파밍 아이템 내용과 효과는 별도 인벤토리에 맡깁니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    /// <summary>1인칭 장비 표시 컴포넌트. 표시 중이면 슬롯 변경 전에 내리는 시간을 준다.</summary>
    public interface IFpsEquipLowering { bool IsShowingEquipment { get; } }

    [DefaultExecutionOrder(-150)]
    public sealed class FpsEquipment : MonoBehaviour
    {
        public enum EquipmentSlot { Pistol = 1, Melee = 2, Item = 3, FarmingItem = 4, FarmingItem2 = 5, FarmingItem3 = 6 }
        [SerializeField] EquipmentSlot selectedSlot = EquipmentSlot.Pistol;
        [Tooltip("3번 고유 아이템·4~6번 파밍 아이템을 내린 뒤 다음 장비로 바꾸기까지의 시간(초)")]
        [SerializeField, Range(0.05f, 0.5f)] float itemLowerDuration = 0.2f;
        FpsInput input;
        FpsMotor motor;
        FpsPistol pistol;
        FpsPackPistolRig pistolRig;
        IFpsEquipLowering[] lowerings;
        int pendingSlot;
        float pendingUntil, pendingDuration;
        public EquipmentSlot SelectedSlot => selectedSlot;
        /// <summary>권총 해제 애니메이션이 끝나기를 기다리는 중. 이 동안 권총은 쏘거나 조준할 수 없다.</summary>
        public bool IsSwitching => pendingSlot != 0;
        /// <summary>현재 장비를 내리는 정도. 1 = 들고 있음, 0 = 완전히 내림.</summary>
        public float LowerBlend => pendingSlot == 0 ? 1 : 1 - Mathf.SmoothStep(0, 1, 1 - Mathf.Clamp01((pendingUntil - Time.time) / Mathf.Max(0.01f, pendingDuration)));
        public bool IsAiming { get; private set; }
        /// <summary>멀티플레이 복제본이면 입력 대신 네트워크 상태를 받는다.</summary>
        public bool IsNetworkProxy { get; set; }
        public event Action MeleeRequested;
        public event Action SlotChanged;
        public event Action<int> SlotSelected;
        void Awake() { input = GetComponent<FpsInput>(); motor = GetComponent<FpsMotor>(); pistol = GetComponent<FpsPistol>(); pistolRig = GetComponent<FpsPackPistolRig>(); lowerings = GetComponents<IFpsEquipLowering>(); }
        void Update()
        {
            if (pendingSlot != 0 && (Time.time >= pendingUntil || motor != null && motor.IsDead)) Select(pendingSlot);
            if (!IsNetworkProxy) Process(input.Slot, input.Aim, input.Attack);
        }
        bool ShowingItem()
        {
            if (lowerings != null) foreach (var lowering in lowerings) if (lowering != null && ((Behaviour)lowering).isActiveAndEnabled && lowering.IsShowingEquipment) return true;
            return false;
        }
        void Select(int slot)
        {
            pendingSlot = 0;
            if (selectedSlot == (EquipmentSlot)slot) return;
            selectedSlot = (EquipmentSlot)slot;
            SlotChanged?.Invoke();
        }
        public void ApplyNetworkState(int slot, bool aiming)
        {
            if (!IsNetworkProxy) return;
            if (slot >= 1 && slot <= 6 && selectedSlot != (EquipmentSlot)slot) { selectedSlot = (EquipmentSlot)slot; SlotChanged?.Invoke(); }
            IsAiming = aiming && (motor == null || !motor.IsDead);
        }
        /// <summary>복제본 전용: 주인이 근접 공격을 했다는 신호 (애니메이션만 재생).</summary>
        public void RaiseNetworkMelee() { if (IsNetworkProxy) MeleeRequested?.Invoke(); }
        public void Process(int slot, bool aim, bool attack)
        {
            if (motor != null && motor.IsDead) { IsAiming = false; return; }
            if (slot >= 1 && slot <= 6)
            {
                if (pendingSlot != 0 && slot == (int)selectedSlot)
                {
                    pendingSlot = 0; // Pistol pressed again mid-holster: keep it and draw again.
                    if (pistolRig != null) pistolRig.CancelHolster();
                }
                else if (pendingSlot != 0) pendingSlot = slot; // Another slot mid-holster: just retarget.
                else if (selectedSlot != (EquipmentSlot)slot)
                {
                    // The held pistol/item goes down first; the next one rises only after it is gone.
                    float holster = selectedSlot == EquipmentSlot.Pistol ? (pistolRig != null ? pistolRig.BeginHolster() : 0) : ShowingItem() ? itemLowerDuration : 0;
                    if (holster > 0) { pendingSlot = slot; pendingDuration = holster; pendingUntil = Time.time + holster; }
                    else Select(slot);
                }
            }
            if (slot >= 1 && slot <= 6) SlotSelected?.Invoke(slot);
            IsAiming = selectedSlot == EquipmentSlot.Pistol && pendingSlot == 0 && aim && (pistol == null || !pistol.IsReloading);
            if (selectedSlot == EquipmentSlot.Melee && attack) MeleeRequested?.Invoke();
        }
        void OnDisable() { IsAiming = false; if (pendingSlot != 0) Select(pendingSlot); }
    }
}
