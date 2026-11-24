/* [ 코드 설명 ] 파밍 아이템(주사기)을 권총과 같은 팩 장갑(FirstPersonArms)으로 잡아 보이게 합니다. 팔은 미리 잡는 자세로 고정된 본을 쓰고, FpsItemView가 움직이는 ItemRoot(주사기)를 따라 함께 이동·회전합니다. 사용 중에는 펌프 진행률에 맞춰 엄지를 캡 위로 올립니다. 아이템 상태·사용 판정은 기존 FpsItemView/FpsInventory가 그대로 담당합니다. */
using UnityEngine;

namespace VoidLast.Player
{
    [DefaultExecutionOrder(210)] // After FpsItemView places ItemRoot this frame.
    public sealed class FpsItemHands : MonoBehaviour
    {
        [Tooltip("FpsItemView의 ItemRoot (주사기)")]
        [SerializeField] Transform anchor;
        [Tooltip("anchor 대신: 실행 중에 FpsCarriedItemView가 만드는 이 아이템의 1인칭 모델을 따라간다 (None이면 anchor 사용)")]
        [SerializeField] FarmingItemKind carriedItem = FarmingItemKind.None;
        [Tooltip("카메라 아래의 팩 팔 인스턴스 (Animator 없이 잡는 자세로 고정)")]
        [SerializeField] Transform arms;
        [Tooltip("ItemRoot 기준 팔 위치/회전 (스케일 제외)")]
        [SerializeField] Vector3 offsetPosition;
        [SerializeField] Vector3 offsetEuler;
        [Header("엄지로 펌프 누르기")]
        [SerializeField] Transform[] thumbBones = new Transform[0];
        [SerializeField] Vector3[] thumbPressEuler = new Vector3[0];
        [Tooltip("펌프 진행률의 몇 배 빠르게 엄지가 캡 위로 올라가는지")]
        [SerializeField, Min(1)] float thumbLead = 3;
        FpsItemView view;
        FpsCarriedItemView carried;
        FpsInventory inventory;
        Quaternion[] thumbRest;

        void Awake()
        {
            view = GetComponent<FpsItemView>(); carried = GetComponent<FpsCarriedItemView>(); inventory = GetComponent<FpsInventory>();
            thumbRest = new Quaternion[thumbBones.Length];
            for (int i = 0; i < thumbBones.Length; i++) if (thumbBones[i] != null) thumbRest[i] = thumbBones[i].localRotation;
            if (arms != null) arms.gameObject.SetActive(false);
        }
        void OnDisable() { if (arms != null) arms.gameObject.SetActive(false); }
        void LateUpdate()
        {
            if (arms == null) return;
            if (carriedItem != FarmingItemKind.None)
            {
                // The carried model is created on first selection; look it up only while that item is in hand.
                bool selected = inventory != null && inventory.IsAlive && inventory.ItemAt(inventory.SelectedIndex) == carriedItem;
                anchor = selected && carried != null ? carried.FirstPersonModel(carriedItem) : null;
            }
            bool show = anchor != null && anchor.gameObject.activeInHierarchy;
            if (anchor == null) { if (arms.gameObject.activeSelf) arms.gameObject.SetActive(false); return; }
            if (arms.gameObject.activeSelf != show) arms.gameObject.SetActive(show);
            if (!show) return;
            arms.SetPositionAndRotation(anchor.position + anchor.rotation * offsetPosition, anchor.rotation * Quaternion.Euler(offsetEuler));
            float press = view != null ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(view.PumpProgress * thumbLead)) : 0;
            for (int i = 0; i < thumbBones.Length; i++)
                if (thumbBones[i] != null) thumbBones[i].localRotation = thumbRest[i] * Quaternion.Euler((i < thumbPressEuler.Length ? thumbPressEuler[i] : Vector3.zero) * press);
        }
    }
}
