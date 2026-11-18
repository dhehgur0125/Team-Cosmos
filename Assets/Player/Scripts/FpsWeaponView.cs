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
        [SerializeField] Vector3 reloadEulerAngles;
        [SerializeField] Vector3 recoilPosition = new Vector3(0, 0.01f, -0.032f);
        [SerializeField] Vector3 recoilEuler = new Vector3(-6, 0, 0.35f);
        [Tooltip("WeaponRoot 로컬 좌표의 손목 회전 중심. 총과 장갑 전체에 함께 적용됩니다.")]
        [SerializeField] Vector3 recoilPivot = new Vector3(0, -0.045f, -0.065f);
        [SerializeField, Min(1)] float recoilReturnSpeed = 16;
        [SerializeField, Range(0, 1)] float aimedRecoilMultiplier = 0.6f;
        [Header("권총 애니메이션 (Fps Weapons Pack 클립 베이크 데이터. 비어 있으면 기존 코드 반동만 사용)")]
        [SerializeField] FpsPistolMotions motions;
        [SerializeField, Range(0.1f, 0.4f)] float holsterDuration = 0.2f;
        [Tooltip("장착·해제·재장전·발사 회전 크기 배율 (1 = 원본 클립)")]
        [SerializeField, Range(0, 1.5f)] float motionScale = 1;
        FpsEquipment equipment;
        FpsMotor motor;
        float aimBlend;
        float recoil;
        int shotFrame = -1;
        FpsPistol pistol;
        float drawTime = -1, holsterTime = -1, fireTime = -1;
        bool fireAds, wasSelected;
        readonly FpsEquipMotion equip = new FpsEquipMotion();
        public float EquipDuration => equipDuration;
        public float AimBlend => aimBlend;
        public float RecoilAmount => recoil;
        public bool IsVisible => weaponRoot != null && weaponRoot.gameObject.activeSelf && holsterTime < 0; // The holster animation keeps the root alive briefly, but the pistol no longer counts as visible.
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
            equip.Reset(); recoil = 0; drawTime = holsterTime = fireTime = -1; wasSelected = false;
        }
        void Kick() { recoil = 1; shotFrame = Time.frameCount; StartFire(); } // Restart a bounded kick; preserve its peak for the firing frame.
        void PistolChanged() { if (pistol.IsReloading) { recoil = 0; fireTime = -1; Refresh(0); } }
        void SelectionChanged() { equip.Reset(); recoil = 0; fireTime = -1; Refresh(0); }
        FpsPistolMotions.Motion CurrentFire => fireAds ? motions.fireAds : motions.fireHip;
        void StartFire()
        {
            if (motions == null || !motions.IsValid || !wasSelected || pistol != null && pistol.IsReloading) return;
            fireTime = 0; fireAds = aimBlend > 0.5f;
        }
        // 클립은 총 본체 회전만 담고 있어 총과 장갑이 같은 WeaponRoot에서 손목 기준으로 함께 움직인다.
        Quaternion AnimationRotation()
        {
            Quaternion q = Quaternion.identity;
            if (drawTime >= 0) q *= motions.draw.Evaluate(drawTime);
            if (holsterTime >= 0) q *= motions.holster.Evaluate(holsterTime / holsterDuration * motions.holster.length);
            if (pistol != null && pistol.IsReloading) q *= motions.reload.Evaluate(pistol.ReloadProgress * motions.reload.length);
            if (fireTime >= 0) q *= CurrentFire.Evaluate(fireTime);
            return Mathf.Approximately(motionScale, 1) ? q : Quaternion.Slerp(Quaternion.identity, q, motionScale);
        }
        public void Refresh(float deltaTime)
        {
            if (weaponRoot == null || equipment == null) return;
            bool visible = equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !motor.IsDead;
            bool animated = motions != null && motions.IsValid;
            bool holstering = false;
            if (animated)
            {
                if (visible && !wasSelected) { drawTime = 0; holsterTime = fireTime = -1; }
                else if (!visible && wasSelected && !motor.IsDead && weaponRoot.gameObject.activeSelf) { holsterTime = 0; drawTime = fireTime = -1; }
                if (visible || motor.IsDead) holsterTime = -1;
                if (!visible) drawTime = -1;
                float step = Mathf.Max(0, deltaTime);
                if (drawTime >= 0) { drawTime += step * motions.draw.length / equipDuration; if (drawTime >= motions.draw.length) drawTime = -1; }
                if (holsterTime >= 0) { holsterTime += step; if (holsterTime >= holsterDuration) holsterTime = -1; }
                if (fireTime >= 0) { fireTime += step; if (fireTime >= CurrentFire.length) fireTime = -1; }
                holstering = holsterTime >= 0;
            }
            wasSelected = visible;
            if (!visible || pistol != null && pistol.IsReloading) recoil = 0;
            else if (shotFrame != Time.frameCount) { recoil *= Mathf.Exp(-recoilReturnSpeed * Mathf.Max(0, deltaTime)); if (recoil < 0.001f) recoil = 0; }
            equip.Tick(visible, 1, deltaTime, equipDuration);
            weaponRoot.gameObject.SetActive(visible || holstering);
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
            if (!animated) pose *= Quaternion.Euler(reloadEulerAngles * reload);
            Quaternion rotation = animated ? AnimationRotation() : Quaternion.Euler(recoilEuler * kick);
            float equipBlend = holstering ? 1 - Mathf.SmoothStep(0, 1, holsterTime / holsterDuration) : equip.Blend;
            Vector3 pivotOffset = (recoilPivot - rotation * recoilPivot) * modelScale;
            weaponRoot.localPosition = Vector3.Lerp(hipPosition, aimPosition, aimBlend) + equipOffset * (1 - equipBlend) + reloadOffset * reload
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
