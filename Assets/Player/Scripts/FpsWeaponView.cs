/* [ 코드 설명 ] 기존 WeaponRoot 아래의 권총과 FPS 장갑을 같은 그립·손목 반동 기준으로 움직입니다. 대기·조준·장착·재장전 자세에 누적 없는 반동을 합성하고 카메라·사격 판정·3인칭 손 IK는 변경하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsWeaponView : MonoBehaviour
    {
        [SerializeField] Transform weaponRoot;
        [SerializeField] GameObject worldWeapon;
        [Header("기존 FPS 장갑 (권총과 같은 WeaponRoot 자식)")]
        [SerializeField] Transform handGrip;
        [SerializeField] Vector3 handPosition = new Vector3(0, -0.035f, -0.035f);
        [SerializeField] Vector3 handEuler = new Vector3(90, 0, 0);
        [SerializeField, Range(0.5f, 1.2f)] float handScale = 0.85f;
        [Header("1인칭 장착·조준 자세")]
        [SerializeField] Vector3 hipPosition = new Vector3(0.18f, -0.18f, 0.38f);
        [SerializeField] Vector3 hipEulerAngles = Vector3.zero;
        [SerializeField] Vector3 aimPosition = new Vector3(0, -0.063f, 0.36f);
        [SerializeField] Vector3 aimEulerAngles = Vector3.zero;
        [SerializeField, Min(0.01f)] float modelScale = 1.15f;
        [SerializeField, Min(0.01f)] float aimTransitionSpeed = 12;
        [SerializeField, Range(0.2f, 0.4f)] float equipDuration = 0.3f;
        [SerializeField] Vector3 equipOffset = new Vector3(0, -0.35f, -0.08f);
        [SerializeField] Vector3 reloadOffset = new Vector3(0, -0.4f, -0.08f);
        [SerializeField] Vector3 recoilPosition = new Vector3(0, 0.01f, -0.032f);
        [SerializeField] Vector3 recoilEuler = new Vector3(-6, 0, 0.35f);
        [Tooltip("WeaponRoot 로컬 좌표의 손목 회전 중심. 총과 장갑 전체에 함께 적용됩니다.")]
        [SerializeField] Vector3 recoilPivot = new Vector3(0, -0.045f, -0.065f);
        [SerializeField, Min(1)] float recoilReturnSpeed = 16;
        [SerializeField, Range(0, 1)] float aimedRecoilMultiplier = 0.6f;
        FpsEquipment equipment;
        FpsMotor motor;
        float aimBlend;
        float recoil;
        int shotFrame = -1;
        FpsPistol pistol;
        readonly FpsEquipMotion equip = new FpsEquipMotion();
        public float AimBlend => aimBlend;
        public float RecoilAmount => recoil;
        public bool IsVisible => weaponRoot != null && weaponRoot.gameObject.activeSelf;
        public bool IsEquipped => IsVisible && equip.IsReady && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !motor.IsDead;

        void Awake()
        {
            equipment = GetComponent<FpsEquipment>(); motor = GetComponent<FpsMotor>(); pistol = GetComponent<FpsPistol>();
            Refresh(0);
        }
        void LateUpdate() => Refresh(Time.deltaTime);
        void OnEnable()
        {
            equipment.SlotChanged += SelectionChanged;
            if (pistol != null) { pistol.Shot += Kick; pistol.Changed += PistolChanged; }
        }
        void OnDisable()
        {
            equipment.SlotChanged -= SelectionChanged;
            if (pistol != null) { pistol.Shot -= Kick; pistol.Changed -= PistolChanged; }
            equip.Reset(); recoil = 0;
        }
        void Kick() { recoil = 1; shotFrame = Time.frameCount; } // Restart a bounded kick; preserve its peak for the firing frame.
        void PistolChanged() { if (pistol.IsReloading) { recoil = 0; Refresh(0); } }
        void SelectionChanged() { equip.Reset(); recoil = 0; Refresh(0); }
        public void Refresh(float deltaTime)
        {
            if (weaponRoot == null || equipment == null) return;
            bool visible = equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !motor.IsDead;
            if (!visible || pistol != null && pistol.IsReloading) recoil = 0;
            else if (shotFrame != Time.frameCount) { recoil *= Mathf.Exp(-recoilReturnSpeed * Mathf.Max(0, deltaTime)); if (recoil < 0.001f) recoil = 0; }
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
            float kick = recoil * Mathf.Lerp(1, aimedRecoilMultiplier, aimBlend);
            Quaternion pose = Quaternion.Slerp(Quaternion.Euler(hipEulerAngles), Quaternion.Euler(aimEulerAngles), aimBlend);
            Quaternion rotation = Quaternion.Euler(recoilEuler * kick);
            Vector3 pivotOffset = (recoilPivot - rotation * recoilPivot) * modelScale;
            weaponRoot.localPosition = Vector3.Lerp(hipPosition, aimPosition, aimBlend) + equipOffset * (1 - equip.Blend) + reloadOffset * reload
                + pose * (recoilPosition * kick + pivotOffset);
            weaponRoot.localRotation = pose * rotation;
            weaponRoot.localScale = Vector3.one * modelScale;
            if (handGrip != null && handGrip.parent == weaponRoot)
            {
                handGrip.localPosition = handPosition;
                handGrip.localRotation = Quaternion.Euler(handEuler);
                handGrip.localScale = Vector3.one * handScale;
            }
        }
    }
}
