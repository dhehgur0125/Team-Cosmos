/* [ 코드 설명 ] 장갑과 주사기를 카메라 앞에 표시하고 사용 방향을 보간합니다. 사용 진행률에 맞춰 실제 Pump 자식을 누르고 취소·해제 시 복원하며 회복 효과는 변경하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsItemView : MonoBehaviour
    {
        [SerializeField] Transform itemRoot;
        [SerializeField] Transform pump;
        [SerializeField] Transform handGrip;
        [SerializeField] Vector3 handPosition = new Vector3(0, 0.005f, -0.015f);
        [SerializeField] Vector3 handEuler;
        [SerializeField, Min(0.1f)] float handScale = 0.9f;
        [SerializeField] Transform worldItem;
        [SerializeField] Transform worldPump;
        [SerializeField] Vector3 pumpPressedOffset = new Vector3(0, 0, 0.055f);
        [SerializeField] Vector3 heldPosition = new Vector3(0.2f, -0.18f, 0.4f);
        [SerializeField] Vector3 heldEuler = new Vector3(-15, -25, -30);
        [SerializeField] Vector3 usePosition = new Vector3(0.04f, -0.16f, 0.32f);
        [SerializeField] Vector3 useEuler = new Vector3(15, 165, -25);
        [SerializeField] Vector3 otherUsePosition = new Vector3(0.12f, -0.22f, 0.46f);
        [SerializeField] Vector3 otherUseEuler = new Vector3(0, 0, -20);
        [SerializeField, Min(0.1f)] float modelScale = 1.4f;
        [SerializeField, Range(0.3f, 0.5f)] float equipDuration = 0.4f;
        [SerializeField] Vector3 equipOffset = new Vector3(0, -0.35f, -0.08f);
        [SerializeField] Transform uniqueItemRoot;
        [SerializeField] Vector3 uniqueHeldPosition = new Vector3(0.2f, -0.18f, 0.4f);
        FpsInventory inventory;
        FpsEquipment equipment;
        readonly FpsEquipMotion equip = new FpsEquipMotion();
        float pose;
        float pumpProgress;
        Vector3 pumpRestPosition;
        Vector3 worldPumpRestPosition;
        public bool IsVisible => itemRoot != null && itemRoot.gameObject.activeSelf;
        public Transform ItemRoot => itemRoot;
        public float PumpProgress => pumpProgress;
        public Transform WorldItem => worldItem;
        public bool IsEquipped => equip.IsReady && equip.Key == (int)equipment.SelectedSlot && inventory.IsAlive;
        public bool CanUse => IsEquipped && inventory.ItemAt(inventory.SelectedIndex) == FarmingItemKind.Syringe;
        void Awake() { inventory = GetComponent<FpsInventory>(); equipment = GetComponent<FpsEquipment>(); if (pump != null) pumpRestPosition = pump.localPosition; if (worldPump != null) worldPumpRestPosition = worldPump.localPosition; Refresh(0); }
        void OnEnable() { equipment.SlotChanged += SelectionChanged; inventory.Changed += ContentsChanged; }
        void OnDisable() { equipment.SlotChanged -= SelectionChanged; inventory.Changed -= ContentsChanged; equip.Reset(); if (pump != null) pump.localPosition = pumpRestPosition; if (worldPump != null) worldPump.localPosition = worldPumpRestPosition; pumpProgress = 0; }
        void SelectionChanged() { equip.Reset(); Refresh(0); }
        void ContentsChanged() { if (inventory.ItemAt(inventory.SelectedIndex) == FarmingItemKind.None) equip.Reset(); Refresh(0); }
        void LateUpdate() => Refresh(Time.deltaTime);
        public void Refresh(float deltaTime)
        {
            if (itemRoot == null || inventory == null) return;
            bool visible = inventory.IsAlive && inventory.ItemAt(inventory.SelectedIndex) == FarmingItemKind.Syringe;
            bool uniqueVisible = inventory.IsAlive && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Item && uniqueItemRoot != null;
            equip.Tick(visible || uniqueVisible, (int)equipment.SelectedSlot, deltaTime, equipDuration);
            if (uniqueItemRoot != null) { uniqueItemRoot.gameObject.SetActive(uniqueVisible); uniqueItemRoot.localPosition = uniqueHeldPosition + equipOffset * (1 - equip.Blend); }
            itemRoot.gameObject.SetActive(visible);
            if (worldItem != null) worldItem.gameObject.SetActive(visible);
            if (handGrip != null) { handGrip.localPosition = handPosition; handGrip.localRotation = Quaternion.Euler(handEuler); handGrip.localScale = Vector3.one * handScale; }
            pose = visible ? Mathf.MoveTowards(pose, inventory.IsUsing ? 1 : 0, Mathf.Max(0, deltaTime) * 3) : 0;
            float blend = Mathf.SmoothStep(0, 1, pose);
            itemRoot.localPosition = Vector3.Lerp(heldPosition, inventory.IsUsingOnOther ? otherUsePosition : usePosition, blend) + equipOffset * (1 - equip.Blend);
            itemRoot.localRotation = Quaternion.Slerp(Quaternion.Euler(heldEuler), Quaternion.Euler(inventory.IsUsingOnOther ? otherUseEuler : useEuler), blend);
            itemRoot.localScale = Vector3.one * modelScale;
            if (!visible) pumpProgress = 0;
            else if (inventory.IsUsing) pumpProgress = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.12f, 1, inventory.UseProgress));
            else pumpProgress = Mathf.MoveTowards(pumpProgress, 0, Mathf.Max(0, deltaTime) * 8);
            if (pump != null) pump.localPosition = pumpRestPosition + pumpPressedOffset * pumpProgress;
            if (worldPump != null) worldPump.localPosition = worldPumpRestPosition + pumpPressedOffset * pumpProgress;
        }
    }
}
