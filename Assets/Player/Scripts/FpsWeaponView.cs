/* [ 코드 설명 ] 기존 권총의 장착·조준·임시 재장전 위치를 한 곳에서 보간합니다. 탄약과 발사는 FpsPistol에 맡기며 카메라 자식과 외부 권총 표시를 유지합니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsWeaponView : MonoBehaviour
    {
        [SerializeField] Transform weaponRoot;
        [SerializeField] GameObject worldWeapon;
        [SerializeField] Vector3 hipPosition = new Vector3(0.2f, -0.18f, 0.42f);
        [SerializeField] Vector3 hipEulerAngles = Vector3.zero;
        [SerializeField] Vector3 aimPosition = new Vector3(0, -0.075f, 0.42f);
        [SerializeField] Vector3 aimEulerAngles = Vector3.zero;
        [SerializeField, Min(0.01f)] float modelScale = 1.25f;
        [SerializeField, Min(0.01f)] float aimTransitionSpeed = 12;
        [SerializeField, Range(0.2f, 0.4f)] float equipDuration = 0.3f;
        [SerializeField] Vector3 equipOffset = new Vector3(0, -0.35f, -0.08f);
        [SerializeField] Vector3 reloadOffset = new Vector3(0, -0.4f, -0.08f);
        FpsEquipment equipment;
        FpsMotor motor;
        float aimBlend;
        FpsPistol pistol;
        readonly FpsEquipMotion equip = new FpsEquipMotion();
        public float AimBlend => aimBlend;
        public bool IsVisible => weaponRoot != null && weaponRoot.gameObject.activeSelf;
        public bool IsEquipped => IsVisible && equip.IsReady && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !motor.IsDead;

        void Awake()
        {
            equipment = GetComponent<FpsEquipment>(); motor = GetComponent<FpsMotor>(); pistol = GetComponent<FpsPistol>();
            Refresh(0);
        }
        void LateUpdate() => Refresh(Time.deltaTime);
        void OnEnable() => equipment.SlotChanged += SelectionChanged;
        void OnDisable() { equipment.SlotChanged -= SelectionChanged; equip.Reset(); }
        void SelectionChanged() { equip.Reset(); Refresh(0); }
        public void Refresh(float deltaTime)
        {
            if (weaponRoot == null || equipment == null) return;
            bool visible = equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !motor.IsDead;
            equip.Tick(visible, 1, deltaTime, equipDuration);
            weaponRoot.gameObject.SetActive(visible);
            if (worldWeapon != null) worldWeapon.SetActive(visible);
            float target = visible && equipment.IsAiming && (pistol == null || !pistol.IsReloading) ? 1 : 0;
            aimBlend = Mathf.Lerp(aimBlend, target, 1 - Mathf.Exp(-aimTransitionSpeed * deltaTime));
            if (!visible) aimBlend = 0;
            float reload = 0;
            if (pistol != null && pistol.IsReloading)
            {
                float progress = pistol.ReloadProgress;
                reload = progress < 0.2f ? Mathf.SmoothStep(0, 1, progress / 0.2f) : progress < 0.8f ? 1 : Mathf.SmoothStep(1, 0, (progress - 0.8f) / 0.2f);
            }
            weaponRoot.localPosition = Vector3.Lerp(hipPosition, aimPosition, aimBlend) + equipOffset * (1 - equip.Blend) + reloadOffset * reload;
            weaponRoot.localRotation = Quaternion.Slerp(Quaternion.Euler(hipEulerAngles), Quaternion.Euler(aimEulerAngles), aimBlend);
            weaponRoot.localScale = Vector3.one * modelScale;
        }
    }
}
