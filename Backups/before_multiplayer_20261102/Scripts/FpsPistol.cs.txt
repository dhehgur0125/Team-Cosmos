/* [ 코드 설명 ] 권총의 반자동 히트스캔·탄약·취소 가능한 재장전을 관리합니다. 실제 명중한 피해 수신 대상에만 피해와 공격자를 전달하며 네트워크 처리는 하지 않습니다. */
using System;
using UnityEngine;

namespace VoidLast.Player
{
    [DefaultExecutionOrder(-75)]
    public sealed class FpsPistol : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] FpsBulletMark bulletMarkPrefab;
        [SerializeField, Min(1)] int magazineCapacity = 10;
        [SerializeField, Min(0)] int startingMagazine = 10;
        [SerializeField, Min(0)] int startingReserve = 30;
        [SerializeField, Min(0.01f)] float shotInterval = 0.18f;
        [SerializeField, Min(1)] float range = 100;
        [SerializeField, Min(0)] float damage = 30;
        [SerializeField, Min(0.1f)] float reloadDuration = 1.5f;
        FpsInput input;
        FpsEquipment equipment;
        FpsMotor motor;
        FpsWeaponView view;
        float nextShot, reloadElapsed;
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public bool IsReloading { get; private set; }
        public float ReloadProgress => IsReloading ? Mathf.Clamp01(reloadElapsed / reloadDuration) : 0;
        public float ReloadDuration => reloadDuration;
        public float ShotInterval => shotInterval;
        public float Damage => damage;
        public bool HasLastHit { get; private set; }
        public RaycastHit LastHit { get; private set; }
        public event Action Changed;
        public event Action Shot;
        public event Action<RaycastHit> Hit;
        bool Equipped => equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol && !motor.IsDead;
        void Awake()
        {
            input = GetComponent<FpsInput>(); equipment = GetComponent<FpsEquipment>(); motor = GetComponent<FpsMotor>(); view = GetComponent<FpsWeaponView>();
            magazineCapacity = Mathf.Max(1, magazineCapacity); reloadDuration = Mathf.Max(0.1f, reloadDuration);
            Magazine = Mathf.Clamp(startingMagazine, 0, magazineCapacity); Reserve = Mathf.Max(0, startingReserve);
        }
        void OnEnable() => equipment.SlotChanged += CancelReload;
        void OnDisable() { equipment.SlotChanged -= CancelReload; CancelReload(); }
        void Update()
        {
            TickReload(Time.deltaTime);
            if (input.Reload) TryReload();
            if (input.Attack) TryFire(); // Press edge only; AttackHeld never fires the pistol.
        }
        public bool TryFire()
        {
            if (!Equipped || IsReloading || !view.IsEquipped || Magazine <= 0 || Time.time < nextShot || viewCamera == null) return false;
            Magazine--; nextShot = Time.time + Mathf.Max(0.01f, shotInterval); HasLastHit = false;
            var ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                LastHit = hit; nearest = hit.distance; HasLastHit = true;
            }
            if (HasLastHit)
            {
                var damageTarget = LastHit.collider.GetComponentInParent<IFpsDamageTarget>();
                damageTarget?.TakeHit(damage, gameObject, LastHit.point);
                Hit?.Invoke(LastHit);
                // Surface marks belong to scenery, not damage receivers or player characters.
                if (bulletMarkPrefab != null && damageTarget == null && LastHit.collider.GetComponentInParent<FpsHealth>() == null) FpsBulletMark.Spawn(bulletMarkPrefab, LastHit);
            }
            Shot?.Invoke();
            Changed?.Invoke(); return true;
        }
        public bool TryReload()
        {
            if (!Equipped || IsReloading || !view.IsEquipped || Magazine >= magazineCapacity || Reserve <= 0) return false;
            IsReloading = true; reloadElapsed = 0; equipment.Process(0, false, false); Changed?.Invoke(); return true;
        }
        public void TickReload(float deltaTime)
        {
            if (!IsReloading) return;
            if (!Equipped) { CancelReload(); return; }
            reloadElapsed += Mathf.Max(0, deltaTime);
            if (reloadElapsed < reloadDuration) return;
            int amount = Mathf.Min(magazineCapacity - Magazine, Reserve);
            Magazine += amount; Reserve -= amount; IsReloading = false; reloadElapsed = 0; Changed?.Invoke();
        }
        public void CancelReload()
        {
            if (!IsReloading) return;
            IsReloading = false; reloadElapsed = 0; Changed?.Invoke();
        }
    }
}
