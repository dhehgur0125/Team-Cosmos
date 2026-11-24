/* [ 코드 설명 ]
1. 기존 권총 재장전 진행률에 맞춰 원본 Clip을 숨기고 한 번만 시각용 탄창을 떨어뜨립니다.
2. 완료·취소·무기 전환 시 원본을 복원하며 로컬 FPS와 원격 전신 탄창을 함께 관리합니다.
*/
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsMagazineDrop : MonoBehaviour
    {
        [SerializeField] Transform localClip, worldClip;
        [SerializeField, Range(0.05f, 0.8f)] float dropAtProgress = 0.22f;
        [SerializeField, Min(0.1f)] float lifetime = 5;
        FpsPistol pistol;
        FpsEquipment equipment;
        FpsHealth health;
        bool previousReload, dropped, networkReload;
        float networkProgress;
        double networkCycle = -1;
        public int DropsCreated { get; private set; }
        public FpsDroppedMagazine LastDrop { get; private set; }
        public bool ClipVisible => localClip != null && localClip.gameObject.activeSelf;
        public bool IsReloading => equipment.IsNetworkProxy ? networkReload : pistol.IsReloading;
        public float ReloadProgress => equipment.IsNetworkProxy ? networkProgress : pistol.ReloadProgress;
        void Awake() { pistol = GetComponent<FpsPistol>(); equipment = GetComponent<FpsEquipment>(); health = GetComponent<FpsHealth>(); }
        void OnEnable() { pistol.Changed += Refresh; equipment.SlotChanged += Refresh; }
        void OnDisable() { pistol.Changed -= Refresh; equipment.SlotChanged -= Refresh; SetVisible(true); previousReload = dropped = networkReload = false; }
        void LateUpdate() => Refresh();
        public void ApplyNetworkReload(bool reloading, float progress, double cycle = -1)
        {
            if (reloading && cycle != networkCycle) { networkCycle = cycle; dropped = false; }
            networkReload = reloading; networkProgress = progress; Refresh();
        }
        public void Refresh()
        {
            bool reloading = IsReloading && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !health.IsDead;
            if (!reloading) { previousReload = false; if (!equipment.IsNetworkProxy) dropped = false; SetVisible(true); return; }
            if (!previousReload && !equipment.IsNetworkProxy) dropped = false;
            previousReload = true;
            if (!dropped && ReloadProgress >= dropAtProgress)
            {
                dropped = true;
                LastDrop = FpsDroppedMagazine.Spawn(equipment.IsNetworkProxy ? worldClip : localClip, lifetime);
                if (LastDrop != null) DropsCreated++;
            }
            SetVisible(!dropped);
        }
        void SetVisible(bool visible)
        {
            if (localClip != null) localClip.gameObject.SetActive(visible);
            if (worldClip != null) worldClip.gameObject.SetActive(visible);
        }
    }
}
