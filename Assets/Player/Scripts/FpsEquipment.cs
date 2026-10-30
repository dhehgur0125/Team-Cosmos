/* [ 코드 설명 ] 1~3번 기존 장비와 4~6번 파밍 슬롯 선택·권총 조준 상태를 관리합니다. 파밍 아이템 내용과 효과는 별도 인벤토리에 맡깁니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    [DefaultExecutionOrder(-150)]
    public sealed class FpsEquipment : MonoBehaviour
    {
        public enum EquipmentSlot { Pistol = 1, Melee = 2, Item = 3, FarmingItem = 4, FarmingItem2 = 5, FarmingItem3 = 6 }
        [SerializeField] EquipmentSlot selectedSlot = EquipmentSlot.Pistol;
        FpsInput input;
        FpsMotor motor;
        FpsPistol pistol;
        public EquipmentSlot SelectedSlot => selectedSlot;
        public bool IsAiming { get; private set; }
        public event Action MeleeRequested;
        public event Action SlotChanged;
        public event Action<int> SlotSelected;
        void Awake() { input = GetComponent<FpsInput>(); motor = GetComponent<FpsMotor>(); pistol = GetComponent<FpsPistol>(); }
        void Update() => Process(input.Slot, input.Aim, input.Attack);
        public void Process(int slot, bool aim, bool attack)
        {
            if (motor != null && motor.IsDead) { IsAiming = false; return; }
            if (slot >= 1 && slot <= 6 && selectedSlot != (EquipmentSlot)slot)
            {
                selectedSlot = (EquipmentSlot)slot;
                SlotChanged?.Invoke();
            }
            if (slot >= 1 && slot <= 6) SlotSelected?.Invoke(slot);
            IsAiming = selectedSlot == EquipmentSlot.Pistol && aim && (pistol == null || !pistol.IsReloading);
            if (selectedSlot == EquipmentSlot.Melee && attack) MeleeRequested?.Invoke();
        }
        void OnDisable() => IsAiming = false;
    }
}
