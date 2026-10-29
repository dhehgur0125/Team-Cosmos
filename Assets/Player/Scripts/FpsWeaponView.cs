/* [ 코드 설명 ] 기존 SF 권총 인스턴스의 기본/조준 위치를 부드럽게 전환하고 1번 슬롯에서만 표시합니다. 카메라 자식 WeaponRoot와 오른손의 외부 표시용 권총을 관리하며 사격 기능은 없습니다. */
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
        FpsEquipment equipment;
        FpsMotor motor;
        float aimBlend;
        float equipElapsed;
        bool wasVisible;
        public float AimBlend => aimBlend;
        public bool IsVisible => weaponRoot != null && weaponRoot.gameObject.activeSelf;

        void Awake()
        {
            equipment = GetComponent<FpsEquipment>(); motor = GetComponent<FpsMotor>();
            Refresh(0);
        }
        void LateUpdate() => Refresh(Time.deltaTime);
        public void Refresh(float deltaTime)
        {
            if (weaponRoot == null || equipment == null) return;
            bool visible = equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !motor.IsDead;
            if (visible && !wasVisible) equipElapsed = 0;
            wasVisible = visible;
            equipElapsed = visible ? Mathf.Min(equipDuration, equipElapsed + Mathf.Max(0, deltaTime)) : 0;
            weaponRoot.gameObject.SetActive(visible);
            if (worldWeapon != null) worldWeapon.SetActive(visible);
            float target = visible && equipment.IsAiming ? 1 : 0;
            aimBlend = Mathf.Lerp(aimBlend, target, 1 - Mathf.Exp(-aimTransitionSpeed * deltaTime));
            if (!visible) aimBlend = 0;
            float equip = Mathf.SmoothStep(0, 1, equipElapsed / Mathf.Max(0.01f, equipDuration));
            weaponRoot.localPosition = Vector3.Lerp(hipPosition, aimPosition, aimBlend) + equipOffset * (1 - equip);
            weaponRoot.localRotation = Quaternion.Slerp(Quaternion.Euler(hipEulerAngles), Quaternion.Euler(aimEulerAngles), aimBlend);
            weaponRoot.localScale = Vector3.one * modelScale;
        }
    }
}
