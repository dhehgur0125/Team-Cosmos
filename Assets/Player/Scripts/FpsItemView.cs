/* [ 코드 설명 ] 장갑과 주사기를 카메라 앞에 표시하고 사용 방향을 보간합니다. 사용 진행률에 맞춰 실제 Pump 자식을 누르고 취소·해제 시 복원하며 회복 효과는 변경하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsItemView : MonoBehaviour
    {
        [SerializeField] Transform itemRoot;
        [SerializeField] Transform pump;
        [SerializeField] Vector3 pumpPressedOffset = new Vector3(0, 0, 0.055f);
        [SerializeField] Vector3 heldPosition = new Vector3(0.2f, -0.18f, 0.4f);
        [SerializeField] Vector3 heldEuler = new Vector3(-15, -25, -30);
        [SerializeField] Vector3 usePosition = new Vector3(0.04f, -0.16f, 0.32f);
        [SerializeField] Vector3 useEuler = new Vector3(15, 165, -25);
        [SerializeField] Vector3 otherUsePosition = new Vector3(0.08f, -0.12f, 0.62f);
        [SerializeField] Vector3 otherUseEuler = new Vector3(0, 0, -20);
        [SerializeField, Min(0.1f)] float modelScale = 1.4f;
        FpsInventory inventory;
        float pose;
        float pumpProgress;
        Vector3 pumpRestPosition;
        public bool IsVisible => itemRoot != null && itemRoot.gameObject.activeSelf;
        public Transform ItemRoot => itemRoot;
        public float PumpProgress => pumpProgress;
        void Awake() { inventory = GetComponent<FpsInventory>(); if (pump != null) pumpRestPosition = pump.localPosition; Refresh(0); }
        void OnDisable() { if (pump != null) pump.localPosition = pumpRestPosition; pumpProgress = 0; }
        void LateUpdate() => Refresh(Time.deltaTime);
        public void Refresh(float deltaTime)
        {
            if (itemRoot == null || inventory == null) return;
            bool visible = inventory.IsAlive && inventory.ItemAt(inventory.SelectedIndex) == FarmingItemKind.Syringe;
            itemRoot.gameObject.SetActive(visible);
            pose = visible ? Mathf.MoveTowards(pose, inventory.IsUsing ? 1 : 0, Mathf.Max(0, deltaTime) * 3) : 0;
            float blend = Mathf.SmoothStep(0, 1, pose);
            itemRoot.localPosition = Vector3.Lerp(heldPosition, inventory.IsUsingOnOther ? otherUsePosition : usePosition, blend);
            itemRoot.localRotation = Quaternion.Slerp(Quaternion.Euler(heldEuler), Quaternion.Euler(inventory.IsUsingOnOther ? otherUseEuler : useEuler), blend);
            itemRoot.localScale = Vector3.one * modelScale;
            if (!visible) pumpProgress = 0;
            else if (inventory.IsUsing) pumpProgress = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.12f, 1, inventory.UseProgress));
            else pumpProgress = Mathf.MoveTowards(pumpProgress, 0, Mathf.Max(0, deltaTime) * 8);
            if (pump != null) pump.localPosition = pumpRestPosition + pumpPressedOffset * pumpProgress;
        }
    }
}
