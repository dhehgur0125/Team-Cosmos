/* [ 코드 설명 ] CharacterController로 이동·중력·웅크리기·지상 점프를 처리합니다. 캡슐 바닥을 유지하며 높이를 바꾸고 천장·착지를 검사합니다. Root Motion은 사용하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    [RequireComponent(typeof(CharacterController), typeof(FpsInput))]
    [DefaultExecutionOrder(-100)]
    public sealed class FpsMotor : MonoBehaviour
    {
        [SerializeField, Min(0)] float walkSpeed = 2.2f;
        [SerializeField, Min(0)] float runSpeed = 4.5f;
        [SerializeField, Min(0)] float crouchSpeed = 1.1f;
        [SerializeField, Min(0.6f)] float standingHeight = 1.85f;
        [SerializeField, Min(0.6f)] float crouchingHeight = 1.15f;
        [SerializeField, Min(0.1f)] float heightChangeSpeed = 3f;
        [SerializeField] float gravity = -22f;
        [SerializeField, Min(0.1f)] float jumpHeight = 1.1f;
        [SerializeField, Range(0.05f, 0.5f)] float itemUseSpeedMultiplier = 0.15f;
        [SerializeField] LayerMask obstructionMask = ~0;
        readonly Collider[] overlaps = new Collider[32];
        CharacterController controller;
        FpsInput input;
        FpsEquipment equipment;
        FpsInventory inventory;
        float verticalSpeed;
        public bool IsCrouching { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsAirborne => !IsGrounded && !IsDead;
        public float VerticalSpeed => verticalSpeed;
        public float JumpHeight => jumpHeight;
        public float CrouchSpeed => crouchSpeed;
        public float JumpPhase
        {
            get
            {
                float launchSpeed = Mathf.Sqrt(2 * -gravity * jumpHeight);
                return verticalSpeed >= 0 ? Mathf.Lerp(0, 0.5f, 1 - verticalSpeed / launchSpeed) : Mathf.Lerp(0.5f, 0.92f, -verticalSpeed / launchSpeed);
            }
        }
        public Vector2 LocalVelocity { get; private set; }
        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float CrouchFraction => controller == null ? 0 : Mathf.InverseLerp(standingHeight, crouchingHeight, controller.height);

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<FpsInput>();
            equipment = GetComponent<FpsEquipment>();
            inventory = GetComponent<FpsInventory>();
        }

        void Update() => Simulate(input.Move, input.Sprint && (equipment == null || !equipment.IsAiming), input.Crouch, Time.deltaTime, input.Jump);

        public void Simulate(Vector2 move, bool sprint, bool crouch, float deltaTime, bool jump = false)
        {
            if (controller == null || !controller.enabled || deltaTime <= 0) return;
            if (IsDead) { move = Vector2.zero; sprint = false; crouch = IsCrouching; }
            bool groundedBeforeMove = controller.isGrounded && verticalSpeed <= 0;
            IsCrouching = (groundedBeforeMove ? crouch : IsCrouching) || (controller.height < standingHeight - 0.001f && !CanStand());
            float targetHeight = IsCrouching ? crouchingHeight : standingHeight;
            controller.height = Mathf.MoveTowards(controller.height, targetHeight, heightChangeSpeed * deltaTime);
            controller.center = Vector3.up * (controller.height * 0.5f);
            move = Vector2.ClampMagnitude(move, 1);
            bool usingItem = inventory != null && inventory.IsUsing;
            IsRunning = sprint && !usingItem && !IsCrouching && move.y > 0.1f;
            float speed = IsCrouching ? crouchSpeed : IsRunning ? runSpeed : walkSpeed;
            if (usingItem) speed *= itemUseSpeedMultiplier;
            Vector3 displacement = transform.TransformDirection(new Vector3(move.x, 0, move.y)) * speed;
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            if (jump && groundedBeforeMove && !crouch && !IsCrouching && CrouchFraction < 0.01f && !IsDead)
                verticalSpeed = Mathf.Sqrt(-2 * gravity * jumpHeight);
            verticalSpeed = Mathf.Max(verticalSpeed + gravity * deltaTime, -50);
            displacement.y = verticalSpeed;
            Vector3 before = transform.position;
            var flags = controller.Move(displacement * deltaTime);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
            IsGrounded = (flags & CollisionFlags.Below) != 0 && verticalSpeed <= 0;
            if (IsGrounded) verticalSpeed = -2;
            var local = transform.InverseTransformDirection((transform.position - before) / deltaTime);
            LocalVelocity = new Vector2(local.x, local.z);
        }

        public bool CanStand()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            // Check only the extra volume above the crouched capsule; ignore our own colliders.
            float radius = controller.radius - 0.015f;
            Vector3 bottom = transform.position + Vector3.up * (crouchingHeight - controller.radius + 0.02f);
            Vector3 top = transform.position + Vector3.up * (standingHeight - controller.radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (!overlaps[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        public void StopForDeath() { IsDead = true; IsRunning = false; LocalVelocity = Vector2.zero; }
        void OnValidate()
        {
            var capsule = GetComponent<CharacterController>();
            float minimum = capsule == null ? 0.6f : capsule.radius * 2 + 0.05f;
            standingHeight = Mathf.Max(standingHeight, minimum + 0.1f);
            crouchingHeight = Mathf.Clamp(crouchingHeight, minimum, standingHeight - 0.1f);
            gravity = Mathf.Min(-0.1f, gravity);
        }
    }
}
