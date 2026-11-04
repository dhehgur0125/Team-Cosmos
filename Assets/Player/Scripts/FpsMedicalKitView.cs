/* [ 코드 설명 ] 의료 키트의 기존 모델·장갑을 같은 기준점으로 장착하고 치료 방향으로 살짝 움직입니다. 표시와 횟수 UI는 의료 키트 컴포넌트가 있는 3번 슬롯에서만 활성화하며 회복·네트워크 판정은 하지 않습니다. */
using UnityEngine;
using UnityEngine.UI;

namespace VoidLast.Player
{
    public sealed class FpsMedicalKitView : MonoBehaviour
    {
        [SerializeField] FpsMedicalKit medicalKit;
        [SerializeField] Transform localRoot;
        [SerializeField] Transform worldRoot;
        [SerializeField] Text usesLabel;
        [SerializeField] Vector3 heldPosition = new Vector3(0.34f, -0.16f, 0.45f);
        [SerializeField] Vector3 heldEuler = new Vector3(-10, -20, 0);
        [SerializeField] Vector3 selfOffset = new Vector3(-0.02f, 0.02f, -0.06f);
        [SerializeField] Vector3 otherOffset = new Vector3(0.045f, 0.02f, 0.07f);
        [SerializeField] Vector3 equipOffset = new Vector3(0, -0.4f, -0.05f);
        [SerializeField, Min(0.1f)] float equipDuration = 0.4f;
        [SerializeField, Min(1)] float transitionSpeed = 9;
        readonly FpsEquipMotion equip = new FpsEquipMotion();
        FpsEquipment equipment;
        float pose;
        bool lastUseOnOther;
        public bool IsEquipped => localRoot != null && medicalKit != null && medicalKit.IsSelected && equip.IsReady;
        public Transform LocalRoot => localRoot;
        public Transform WorldRoot => worldRoot;
        void Awake() => equipment = GetComponent<FpsEquipment>();
        void OnEnable() { equipment.SlotChanged += SelectionChanged; Refresh(0); }
        void OnDisable() { equipment.SlotChanged -= SelectionChanged; equip.Reset(); Hide(); }
        void SelectionChanged() { equip.Reset(); Refresh(0); }
        void LateUpdate() => Refresh(Time.deltaTime);
        void Hide()
        {
            if (localRoot != null) localRoot.gameObject.SetActive(false);
            if (worldRoot != null) worldRoot.gameObject.SetActive(false);
            if (usesLabel != null) usesLabel.enabled = false;
        }
        public void Refresh(float deltaTime)
        {
            bool visible = medicalKit != null && medicalKit.IsSelected && localRoot != null;
            equip.Tick(visible, 3, deltaTime, equipDuration);
            if (!visible) { pose = 0; Hide(); return; }
            localRoot.gameObject.SetActive(true);
            if (worldRoot != null) worldRoot.gameObject.SetActive(true);
            if (medicalKit.IsUsing) lastUseOnOther = medicalKit.UsingOnOther;
            pose = Mathf.Lerp(pose, medicalKit.IsUsing ? 1 : 0, 1 - Mathf.Exp(-transitionSpeed * Mathf.Max(0, deltaTime)));
            localRoot.localPosition = heldPosition + equipOffset * (1 - equip.Blend) + (lastUseOnOther ? otherOffset : selfOffset) * pose;
            localRoot.localRotation = Quaternion.Euler(heldEuler + new Vector3(-8 * pose, 0, 0));
            if (usesLabel != null)
            {
                usesLabel.enabled = medicalKit.IsLocal;
                usesLabel.text = "의료 키트 " + medicalKit.RemainingUses + " / " + medicalKit.MaximumUses
                    + (medicalKit.IsUsing ? "\n치료 중 " + (medicalKit.UseProgress * medicalKit.UseDuration).ToString("F1") + " / " + medicalKit.UseDuration.ToString("F1") + " s" : "");
            }
        }
    }
}
