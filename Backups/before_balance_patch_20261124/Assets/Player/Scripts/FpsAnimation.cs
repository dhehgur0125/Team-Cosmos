/* [ 코드 설명 ] 이동 상태와 장비 선택을 Humanoid Animator에 전달합니다. 사망 및 근접 일회성 동작을 관리하며 실제 이동은 CharacterController에 맡깁니다. */
using UnityEngine;

namespace VoidLast.Player
{
    public sealed class FpsAnimation : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField, Min(0)] float directionDamping = 0.12f;
        FpsMotor motor;
        FpsEquipment equipment;
        FpsInventory inventory;
        FpsPistol pistol;
        FpsHealth health;
        FpsMagazineDrop magazine;
        float upperWeight, jumpWeight, hitWeight, idleTime, nextIdleBreak = 8;
        bool wasDead;
        Vector2 moveDirection = Vector2.up;
        [Tooltip("이동 방향이 바뀔 때 다리 방향이 따라 도는 속도(도/초)")]
        [SerializeField, Min(90)] float directionTurnSpeed = 720;
        readonly System.Collections.Generic.HashSet<int> parameters = new System.Collections.Generic.HashSet<int>();
        [Header("추가 동작 (Human Animations 컨트롤러에 있을 때만 사용)")]
        [Tooltip("이 체력 비율 이하에서 부상 대기 자세로 바뀝니다")]
        [SerializeField, Range(0, 1)] float woundedHealth = 0.35f;
        [SerializeField, Min(2)] float idleBreakMin = 8, idleBreakMax = 16;
        static readonly int MoveX = Animator.StringToHash("MoveX"), MoveY = Animator.StringToHash("MoveY"),
            Crouch = Animator.StringToHash("Crouch"), Slot = Animator.StringToHash("Slot"), Aim = Animator.StringToHash("Aim"),
            Dead = Animator.StringToHash("Dead"), Melee = Animator.StringToHash("Melee");

        void Awake()
        {
            motor = GetComponent<FpsMotor>(); equipment = GetComponent<FpsEquipment>(); inventory = GetComponent<FpsInventory>();
            pistol = GetComponent<FpsPistol>(); health = GetComponent<FpsHealth>(); magazine = GetComponent<FpsMagazineDrop>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                foreach (var parameter in animator.parameters) parameters.Add(parameter.nameHash);
            }
        }
        void OnEnable()
        {
            if (equipment == null) equipment = GetComponent<FpsEquipment>(); equipment.MeleeRequested += Attack;
            if (pistol != null) pistol.Shot += Fired;
            if (health != null) health.DamageTaken += Damaged;
        }
        void OnDisable()
        {
            if (equipment != null) equipment.MeleeRequested -= Attack;
            if (pistol != null) pistol.Shot -= Fired;
            if (health != null) health.DamageTaken -= Damaged;
        }
        bool Has(string name) => parameters.Contains(Animator.StringToHash(name));
        void Fired() { if (animator != null && Has("Shoot") && !motor.IsDead) animator.SetTrigger("Shoot"); }
        void Damaged(float amount)
        {
            if (animator == null || !Has("Hit") || motor.IsDead || amount <= 0) return;
            animator.SetInteger("HitIndex", Random.Range(0, 4)); animator.SetTrigger("Hit"); hitWeight = 1;
        }
        void Update()
        {
            if (animator == null || !animator.isInitialized) return;
            Vector2 velocity = motor.LocalVelocity;
            float speed = velocity.magnitude;
            if (speed > 0.03f)
            {
                // Rotate the unit direction instead of damping X/Y separately: a left-right flip must not pass through
                // a near-zero vector, where the 8-way blend mixes every leg cycle at once.
                Vector2 target = velocity / speed;
                // An exact reversal has no defined turning side; go through the perpendicular so the turn stays in plane.
                if (Vector2.Dot(moveDirection, target) < -0.98f) target = new Vector2(-moveDirection.y, moveDirection.x);
                float maxRadians = directionTurnSpeed * Mathf.Deg2Rad * Time.deltaTime;
                Vector3 turned = Vector3.RotateTowards(new Vector3(moveDirection.x, moveDirection.y, 0), new Vector3(target.x, target.y, 0), maxRadians, 0);
                moveDirection = new Vector2(turned.x, turned.y).normalized;
                if (moveDirection.sqrMagnitude < 0.5f) moveDirection = target;
                animator.SetFloat(MoveX, moveDirection.x);
                animator.SetFloat(MoveY, moveDirection.y);
            }
            // Separate speed from direction: diagonal input must not mix in Idle or Run.
            float gait = speed <= motor.WalkSpeed ? speed / motor.WalkSpeed : 1 + Mathf.InverseLerp(motor.WalkSpeed, motor.RunSpeed, speed);
            animator.SetFloat("Gait", gait, 0.1f, Time.deltaTime);
            animator.SetFloat("CrouchAmount", Mathf.Clamp01(speed / motor.CrouchSpeed), 0.1f, Time.deltaTime);
            animator.SetFloat("CrouchDirection", velocity.y < -0.05f ? -1 : 1, directionDamping, Time.deltaTime);
            animator.SetBool("Airborne", motor.IsAirborne);
            animator.SetFloat("JumpPhase", motor.JumpPhase);
            animator.SetBool(Crouch, motor.IsCrouching);
            animator.SetInteger(Slot, (int)equipment.SelectedSlot);
            animator.SetBool(Aim, equipment.IsAiming);
            if (motor.IsDead && !wasDead && Has("DeathIndex")) animator.SetInteger("DeathIndex", Random.Range(0, 5)); // Pick the fall before Dead flips.
            wasDead = motor.IsDead;
            animator.SetBool(Dead, motor.IsDead);
            if (Has("Reload")) animator.SetBool("Reload", magazine != null ? magazine.IsReloading : pistol != null && pistol.IsReloading);
            if (Has("Holster")) animator.SetBool("Holster", equipment.IsSwitching && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol);
            if (Has("Wounded") && health != null) animator.SetFloat("Wounded", health.Current <= health.Maximum * woundedHealth ? 1 : 0, 0.5f, Time.deltaTime);
            if (Has("IdleBreak"))
            {
                bool still = speed < 0.03f && !motor.IsCrouching && !motor.IsAirborne && !motor.IsDead && upperWeight < 0.01f;
                idleTime = still ? idleTime + Time.deltaTime : 0;
                if (idleTime > nextIdleBreak) { animator.SetTrigger("IdleBreak"); idleTime = 0; nextIdleBreak = Random.Range(idleBreakMin, idleBreakMax); }
            }
            bool backward = motor.IsCrouching && velocity.y < -0.05f;
            animator.SetBool("CrouchBackward", backward);
            bool attacking = animator.GetCurrentAnimatorStateInfo(1).IsTag("Melee") ||
                (animator.IsInTransition(1) && animator.GetNextAnimatorStateInfo(1).IsTag("Melee"));
            bool holdingItem = inventory != null && inventory.ItemAt(inventory.SelectedIndex) != FarmingItemKind.None || FpsUniqueItem.HoldsHandItem(inventory);
            // The pack has real crouch-backward legs, so walking back no longer needs an upper-body override.
            float targetWeight = !motor.IsDead && (equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol || holdingItem || attacking || backward && !Has("Wounded")) ? 1 : 0;
            upperWeight = Mathf.MoveTowards(upperWeight, targetWeight, Time.deltaTime * 8);
            animator.SetLayerWeight(1, upperWeight);
            jumpWeight = Mathf.MoveTowards(jumpWeight, motor.IsAirborne ? 1 : 0, Time.deltaTime * 12);
            animator.SetLayerWeight(2, jumpWeight);
            if (animator.layerCount > 3)
            {
                // Hit layer only while a flinch is playing; its empty rest state would otherwise pin the arms.
                bool flinching = animator.IsInTransition(3) || !animator.GetCurrentAnimatorStateInfo(3).IsName("None");
                hitWeight = Mathf.MoveTowards(hitWeight, flinching && !motor.IsDead ? 1 : 0, Time.deltaTime * 6);
                animator.SetLayerWeight(3, hitWeight);
            }
        }
        void Attack()
        {
            if (animator == null || motor.IsDead) return;
            if (animator.GetCurrentAnimatorStateInfo(1).IsTag("Melee") ||
                (animator.IsInTransition(1) && animator.GetNextAnimatorStateInfo(1).IsTag("Melee"))) return;
            animator.SetTrigger(Melee);
        }
        public void Die()
        {
            upperWeight = jumpWeight = 0;
            motor.StopForDeath();
            equipment.Process(0, false, false);
            if (animator != null && Has("DeathIndex") && !wasDead) animator.SetInteger("DeathIndex", Random.Range(0, 5));
            wasDead = true;
            if (animator != null) { animator.ResetTrigger(Melee); animator.SetBool(Dead, true); animator.SetLayerWeight(1, 0); animator.SetLayerWeight(2, 0); }
        }
        /// <summary>고치에서 구출: 쓰러진 자세를 풀고 처음 상태로 되돌린다</summary>
        public void Revive()
        {
            motor.ResumeAfterRevive();
            if (animator != null) { animator.SetBool(Dead, false); animator.Rebind(); animator.Update(0f); }
        }
    }
}
