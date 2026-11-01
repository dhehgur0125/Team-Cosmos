using UnityEngine;

namespace VoidLast.Player
{
    // Match the visible astronaut to the existing crouched controller without scaling its bones.
    // Animator still supplies the gait; this final pose correction lowers the pelvis and folds the legs.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FpsMotor), typeof(CharacterController))]
    [DefaultExecutionOrder(100)]
    public sealed class FpsCrouchBodyFit : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField, Min(0)] float helmetAboveHead = 0.27f;
        [SerializeField, Min(0)] float ceilingMargin = 0.04f;
        [SerializeField, Min(0)] float ankleHeight = 0.145f;

        FpsMotor motor;
        CharacterController controller;
        Transform hips, head;
        Leg left, right;
        Vector3 hipBefore;
        bool initialized, applied;

        sealed class Leg
        {
            public Transform upper, lower, foot;
            public Quaternion upperBefore, lowerBefore, footBefore, flatRotation;
            public Vector3 target;
        }

        void Awake() => Initialize();

        public bool Initialize()
        {
            if (initialized) return true;
            motor = GetComponent<FpsMotor>();
            controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
                return false;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            left = CreateLeg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            right = CreateLeg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
            initialized = hips != null && head != null && left != null && right != null;
            return initialized;
        }

        Leg CreateLeg(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot)
        {
            var u = animator.GetBoneTransform(upper);
            var l = animator.GetBoneTransform(lower);
            var f = animator.GetBoneTransform(foot);
            if (u == null || l == null || f == null) return null;
            return new Leg { upper = u, lower = l, foot = f,
                flatRotation = Quaternion.Inverse(transform.rotation) * f.rotation };
        }

        // Clear last frame's correction before Animator evaluates, including when culled.
        void Update() => RestorePose();
        void OnDisable() => RestorePose();
        void LateUpdate()
        {
            if (!Initialize() || motor.IsDead) return;
            ApplyPose(motor.CrouchFraction);
        }

        public void ApplyPose(float crouchFraction)
        {
            if (!Initialize() || crouchFraction <= 0.001f || motor.IsDead) return;
            float weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(crouchFraction * 4));
            if (weight <= 0) return;
            hipBefore = hips.localPosition;
            Capture(left);
            Capture(right);
            float worldScale = Mathf.Abs(transform.lossyScale.y);
            float modelScale = Mathf.Abs(animator.transform.lossyScale.y);
            float top = transform.position.y + (controller.center.y + controller.height * 0.5f - ceilingMargin) * worldScale;
            float excess = Mathf.Max(0, head.position.y + helmetAboveHead * modelScale - top);
            // Controller height already eases through the transition; keep the helmet inside it.
            hips.position -= Vector3.up * excess;
            Solve(left, weight);
            Solve(right, weight);
            applied = true;
        }

        void Capture(Leg leg)
        {
            leg.upperBefore = leg.upper.localRotation;
            leg.lowerBefore = leg.lower.localRotation;
            leg.footBefore = leg.foot.localRotation;
            leg.target = leg.foot.position;
            leg.target.y = Mathf.Max(leg.target.y, transform.position.y + ankleHeight * Mathf.Abs(transform.lossyScale.y));
        }

        void Solve(Leg leg, float weight)
        {
            Vector3 target = Vector3.Lerp(leg.foot.position, leg.target, weight);
            Quaternion footRotation = Quaternion.Slerp(leg.foot.rotation, transform.rotation * leg.flatRotation, weight);
            float a = Vector3.Distance(leg.upper.position, leg.lower.position);
            float b = Vector3.Distance(leg.lower.position, leg.foot.position);
            Vector3 delta = target - leg.upper.position;
            if (a < 0.001f || b < 0.001f || delta.sqrMagnitude < 0.000001f) return;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
            Vector3 direction = delta.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(transform.forward + Vector3.up * 2, direction).normalized;
            if (bend.sqrMagnitude < 0.01f) bend = Vector3.ProjectOnPlane(transform.right, direction).normalized;
            float along = (a * a + distance * distance - b * b) / (2 * distance);
            float side = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            Vector3 knee = leg.upper.position + direction * along + bend * side;
            leg.upper.rotation = Quaternion.FromToRotation(leg.lower.position - leg.upper.position, knee - leg.upper.position) * leg.upper.rotation;
            leg.lower.rotation = Quaternion.FromToRotation(leg.foot.position - leg.lower.position, target - leg.lower.position) * leg.lower.rotation;
            leg.foot.rotation = footRotation;
        }

        public void RestorePose()
        {
            if (!applied || !initialized) return;
            hips.localPosition = hipBefore;
            Restore(left);
            Restore(right);
            applied = false;
        }

        static void Restore(Leg leg)
        {
            leg.upper.localRotation = leg.upperBefore;
            leg.lower.localRotation = leg.lowerBefore;
            leg.foot.localRotation = leg.footBefore;
        }
    }
}
