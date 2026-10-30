/* [ 코드 설명 ] 기존 Humanoid 상체 애니메이션에 손 IK를 더해 권총을 양손으로 잡고 주사기를 오른손으로 듭니다. 장비별 그립 회전과 가슴 기준 손 위치를 분리하며 하체·이동·원본 모델은 변경하지 않습니다. */
using UnityEngine;

namespace VoidLast.Player
{
    [RequireComponent(typeof(Animator))]
    public sealed class FpsHeldItemPose : MonoBehaviour
    {
        [SerializeField] Quaternion pistolRightRotation = Quaternion.identity;
        [SerializeField] Quaternion pistolLeftRotation = Quaternion.identity;
        [SerializeField] Quaternion syringeRightRotation = Quaternion.identity;
        [SerializeField] Vector3 pistolHandPosition = new Vector3(0.07f, -0.03f, 0.32f);
        [SerializeField] Vector3 pistolAimPosition = new Vector3(0.045f, 0.13f, 0.38f);
        [SerializeField] Vector3 supportHandOffset = new Vector3(-0.075f, -0.015f, 0.035f);
        [SerializeField] Vector3 syringeHandPosition = new Vector3(0.2f, -0.12f, 0.25f);
        [SerializeField] HumanBodyBones[] fingerBones;
        [SerializeField] Quaternion[] closedFingerRotations;
        Animator animator;
        FpsEquipment equipment;
        FpsInventory inventory;
        FpsMotor motor;
        Transform chest, player, cameraPivot;
        float rightWeight, leftWeight, aim;
        Quaternion rightGoal, leftGoal, rightIkOffset = Quaternion.identity, leftIkOffset = Quaternion.identity;
        bool rightCalibrated, leftCalibrated;
        public Vector3 RightTarget { get; private set; }
        public Vector3 LeftTarget { get; private set; }
        public float RightWeight => rightWeight;
        public float LeftWeight => leftWeight;

        void Awake()
        {
            animator = GetComponent<Animator>(); equipment = GetComponentInParent<FpsEquipment>();
            inventory = GetComponentInParent<FpsInventory>(); motor = GetComponentInParent<FpsMotor>();
            player = equipment.transform; cameraPivot = player.Find("CameraPivot");
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        }
        void OnDisable() { rightWeight = leftWeight = aim = 0; }
        void LateUpdate()
        {
            // Humanoid IK goals use avatar axes, which differ from this model's wrist axes.
            // Measure the fixed difference only after a full-weight solve, then reuse it.
            if (!rightCalibrated && rightWeight >= 0.999f)
            {
                rightIkOffset = Quaternion.Inverse(animator.GetBoneTransform(HumanBodyBones.RightHand).rotation) * rightGoal;
                rightCalibrated = true;
            }
            if (!leftCalibrated && leftWeight >= 0.999f)
            {
                leftIkOffset = Quaternion.Inverse(animator.GetBoneTransform(HumanBodyBones.LeftHand).rotation) * leftGoal;
                leftCalibrated = true;
            }
            if (fingerBones == null || closedFingerRotations == null) return;
            for (int i = 0; i < fingerBones.Length && i < closedFingerRotations.Length; i++)
            {
                var bone = animator.GetBoneTransform(fingerBones[i]); if (bone == null) continue;
                bool left = fingerBones[i] >= HumanBodyBones.LeftThumbProximal && fingerBones[i] <= HumanBodyBones.LeftLittleDistal;
                bone.localRotation = Quaternion.Slerp(bone.localRotation, closedFingerRotations[i], left ? leftWeight : rightWeight);
            }
        }
        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 1 || chest == null) return;
            bool pistol = !motor.IsDead && equipment.SelectedSlot == FpsEquipment.EquipmentSlot.Pistol;
            bool syringe = !motor.IsDead && inventory.ItemAt(inventory.SelectedIndex) == FarmingItemKind.Syringe;
            rightWeight = Mathf.MoveTowards(rightWeight, pistol || syringe ? 1 : 0, Time.deltaTime * 8);
            leftWeight = Mathf.MoveTowards(leftWeight, pistol ? 1 : 0, Time.deltaTime * 8);
            aim = Mathf.MoveTowards(aim, pistol && equipment.IsAiming ? 1 : 0, Time.deltaTime * 8);
            Quaternion facing = player.rotation;
            float pitch = cameraPivot == null ? 0 : Mathf.Clamp(Mathf.DeltaAngle(0, cameraPivot.localEulerAngles.x), -35, 35);
            Quaternion aimRotation = facing * Quaternion.Euler(pitch * aim, 0, 0);
            Vector3 position = pistol ? Vector3.Lerp(pistolHandPosition, pistolAimPosition, aim) : syringeHandPosition;
            if (syringe && inventory.IsUsing) position += inventory.IsUsingOnOther ? new Vector3(-0.08f, 0.02f, 0.12f) : new Vector3(-0.1f, -0.02f, -0.04f);
            RightTarget = chest.position + aimRotation * position;
            LeftTarget = RightTarget + aimRotation * supportHandOffset;
            rightGoal = aimRotation * (pistol ? pistolRightRotation : syringeRightRotation);
            leftGoal = aimRotation * pistolLeftRotation;
            Hand(AvatarIKGoal.RightHand, RightTarget, rightGoal * rightIkOffset, rightWeight);
            Hand(AvatarIKGoal.LeftHand, LeftTarget, leftGoal * leftIkOffset, leftWeight);
            Hint(AvatarIKHint.RightElbow, chest.position + facing * new Vector3(0.4f, -0.25f, 0.05f), rightWeight);
            Hint(AvatarIKHint.LeftElbow, chest.position + facing * new Vector3(-0.4f, -0.25f, 0.05f), leftWeight);
        }
        void Hand(AvatarIKGoal hand, Vector3 position, Quaternion rotation, float weight)
        {
            animator.SetIKPositionWeight(hand, weight); animator.SetIKRotationWeight(hand, weight);
            animator.SetIKPosition(hand, position); animator.SetIKRotation(hand, rotation);
        }
        void Hint(AvatarIKHint hint, Vector3 position, float weight)
        {
            animator.SetIKHintPositionWeight(hint, weight); animator.SetIKHintPosition(hint, position);
        }
    }
}
