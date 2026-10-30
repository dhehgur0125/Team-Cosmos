/* [ 코드 설명 ] FPS 프리팹의 장비별 그립과 외부 주사기 인스턴스를 연결하고 기존 Humanoid 상체 레이어의 손 IK를 활성화합니다. 원본 모델과 다른 씬은 저장하지 않습니다. */
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsHeldItemSetup
    {
        [MenuItem("VOID LAST/Player/7 - Update Hand Grips and World Item")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            FpsAssetSaveGuard.Active = true;
            var root = PrefabUtility.LoadPrefabContents(FpsAssetSetup.PrefabPath);
            try
            {
                var animator = root.GetComponentInChildren<Animator>();
                var pose = animator.GetComponent<FpsHeldItemPose>() ?? animator.gameObject.AddComponent<FpsHeldItemPose>();
                var config = new SerializedObject(pose);
                var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var bones = new List<HumanBodyBones>(); var rotations = new List<Quaternion>();
                Calibrate(animator, false, out var rightRotation, out var wrist, bones, rotations);
                Calibrate(animator, true, out var leftRotation, out _, bones, rotations);
                Quaternion pistolGrip = Quaternion.Euler(90, 0, 0);
                Quaternion leftGrip = Quaternion.Euler(90, 0, 180);
                config.FindProperty("pistolRightRotation").quaternionValue = pistolGrip * rightRotation;
                config.FindProperty("pistolLeftRotation").quaternionValue = leftGrip * leftRotation;
                config.FindProperty("syringeRightRotation").quaternionValue = rightRotation;
                var ids = config.FindProperty("fingerBones"); var closed = config.FindProperty("closedFingerRotations");
                ids.arraySize = closed.arraySize = bones.Count;
                for (int i = 0; i < bones.Count; i++) { ids.GetArrayElementAtIndex(i).intValue = (int)bones[i]; closed.GetArrayElementAtIndex(i).quaternionValue = rotations[i]; }
                config.ApplyModifiedPropertiesWithoutUndo();
                var pistol = right.Find("WorldWeaponSocket");
                SetSocket(pistol, pistolGrip * rightRotation, new Vector3(0, -0.045f, -0.065f) + pistolGrip * wrist);
                var syringe = right.Find("WorldItemSocket");
                if (syringe == null)
                {
                    syringe = new GameObject("WorldItemSocket").transform; syringe.SetParent(right, false);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FpsFarmingSetup.SyringePath), root.scene);
                    model.transform.SetParent(syringe, false);
                    foreach (var collider in model.GetComponentsInChildren<Collider>(true)) { collider.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(collider); }
                }
                SetSocket(syringe, rightRotation, wrist);
                var camera = root.GetComponentInChildren<Camera>(true);
                var item = new SerializedObject(root.GetComponent<FpsItemView>());
                item.FindProperty("handGrip").objectReferenceValue = camera.transform.Find("ItemRoot/FPS Right Glove");
                item.FindProperty("worldItem").objectReferenceValue = syringe;
                item.FindProperty("worldPump").objectReferenceValue = syringe.GetChild(0).Find("Pump");
                item.ApplyModifiedPropertiesWithoutUndo();
                var visibility = new SerializedObject(root.GetComponent<FpsLocalVisibility>());
                Renderers(visibility.FindProperty("worldRenderers"), animator.GetComponentsInChildren<Renderer>(true));
                Renderers(visibility.FindProperty("firstPersonRenderers"), camera.GetComponentsInChildren<Renderer>(true));
                visibility.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, FpsAssetSetup.PrefabPath);
                var controller = (AnimatorController)animator.runtimeAnimatorController;
                var layers = controller.layers; layers[1].iKPass = true; controller.layers = layers;
                var mask = layers[1].avatarMask;
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
                EditorUtility.SetDirty(controller); EditorUtility.SetDirty(mask);
                AssetDatabase.SaveAssetIfDirty(controller); AssetDatabase.SaveAssetIfDirty(mask);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); FpsAssetSaveGuard.Active = false; }
        }
        static void SetSocket(Transform socket, Quaternion handRotation, Vector3 handPosition)
        {
            socket.localRotation = Quaternion.Inverse(handRotation);
            socket.localPosition = Quaternion.Inverse(handRotation) * -handPosition;
        }
        static void Renderers(SerializedProperty field, Renderer[] renderers)
        {
            field.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) field.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        }
        static void Calibrate(Animator animator, bool left, out Quaternion rotation, out Vector3 wrist, List<HumanBodyBones> boneIds, List<Quaternion> closed)
        {
            string side = left ? "Left" : "Right";
            Transform Bone(string suffix) => animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + suffix));
            var hand = Bone("Hand"); var middle = Bone("MiddleProximal");
            Vector3 along = (Bone("IndexProximal").position - Bone("LittleProximal").position).normalized;
            Vector3 fingers = Vector3.ProjectOnPlane(middle.position - hand.position, along).normalized;
            Vector3 palm = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(Bone("MiddleDistal").position - middle.position, along), fingers).normalized;
            if (Vector3.Dot(Vector3.Cross(fingers, along), palm) > 0) along = -along;
            Quaternion grip = Quaternion.LookRotation(along, fingers);
            Vector3 center = middle.position + fingers * 0.02f + palm * 0.015f;
            rotation = Quaternion.Inverse(grip) * hand.rotation;
            wrist = Quaternion.Inverse(grip) * (hand.position - center);
            var transforms = hand.GetComponentsInChildren<Transform>(true);
            var original = transforms.Select(t => t.localRotation).ToArray();
            try
            {
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    Vector3 direction = (Bone(finger + "Intermediate").position - Bone(finger + "Proximal").position).normalized;
                    Vector3 axis = Vector3.Cross(direction, palm).normalized;
                    var joints = new[] { "Proximal", "Intermediate", "Distal" };
                    var angles = finger == "Thumb" ? new[] { 15f, 20f, 10f } : new[] { 40f, 55f, 25f };
                    for (int j = 0; j < joints.Length; j++)
                    {
                        var bone = Bone(finger + joints[j]); bone.rotation = Quaternion.AngleAxis(angles[j], axis) * bone.rotation;
                        boneIds.Add((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + joints[j])); closed.Add(bone.localRotation);
                    }
                }
            }
            finally { for (int i = 0; i < transforms.Length; i++) transforms[i].localRotation = original[i]; }
        }
    }
}
