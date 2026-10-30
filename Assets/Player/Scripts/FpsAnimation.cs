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
        float upperWeight, jumpWeight;
        static readonly int MoveX = Animator.StringToHash("MoveX"), MoveY = Animator.StringToHash("MoveY"),
            Crouch = Animator.StringToHash("Crouch"), Slot = Animator.StringToHash("Slot"), Aim = Animator.StringToHash("Aim"),
            Dead = Animator.StringToHash("Dead"), Melee = Animator.StringToHash("Melee");

        void Awake()
        {
            motor = GetComponent<FpsMotor>(); equipment = GetComponent<FpsEquipment>();
            if (animator != null) animator.applyRootMotion = false;
        }
        void OnEnable() { if (equipment == null) equipment = GetComponent<FpsEquipment>(); equipment.MeleeRequested += Attack; }
        void OnDisable() { if (equipment != null) equipment.MeleeRequested -= Attack; }
        void Update()
        {
            if (animator == null || !animator.isInitialized) return;
            Vector2 velocity = motor.LocalVelocity;
            float speed = velocity.magnitude;
            if (speed > 0.03f)
            {
                Vector2 direction = velocity / speed;
                animator.SetFloat(MoveX, direction.x, directionDamping, Time.deltaTime);
                animator.SetFloat(MoveY, direction.y, directionDamping, Time.deltaTime);
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
            animator.SetBool(Dead, motor.IsDead);
            bool backward = motor.IsCrouching && velocity.y < -0.05f;
            animator.SetBool("CrouchBackward", backward);
            bool attacking = animator.GetCurrentAnimatorStateInfo(1).IsTag("Melee") ||
                (animator.IsInTransition(1) && animator.GetNextAnimatorStateInfo(1).IsTag("Melee"));
            float targetWeight = !motor.IsDead && (equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol || backward || attacking) ? 1 : 0;
            upperWeight = Mathf.MoveTowards(upperWeight, targetWeight, Time.deltaTime * 8);
            animator.SetLayerWeight(1, upperWeight);
            jumpWeight = Mathf.MoveTowards(jumpWeight, motor.IsAirborne ? 1 : 0, Time.deltaTime * 12);
            animator.SetLayerWeight(2, jumpWeight);
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
            if (animator != null) { animator.ResetTrigger(Melee); animator.SetBool(Dead, true); animator.SetLayerWeight(1, 0); animator.SetLayerWeight(2, 0); }
        }
    }
}
