/* [ 코드 설명 ] 기존 FPS 에셋을 유지하며 방향 Blend Tree, 하체 Jump 레이어, SF 권총 인스턴스와 카메라별 표시 목록을 갱신합니다. 원본 캐릭터/권총과 Player 밖 에셋은 저장하지 않습니다. */
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VoidLast.Player.Editor
{
    internal sealed class FpsAssetSaveGuard : AssetModificationProcessor
    {
        internal static bool Active;
        static string[] OnWillSaveAssets(string[] paths) => Active
            ? paths.Where(p => p.StartsWith(FpsAssetSetup.Root, StringComparison.Ordinal) && p != FpsAssetSetup.ModelPath && p != FpsAssetSetup.PistolPath).ToArray()
            : paths;
    }

    public static partial class FpsAssetSetup
    {
        public const string PistolPath = Root + "Prefabs/Weapon/SciFiPistol01_2.prefab";
        [MenuItem("VOID LAST/Player/4 - Apply Jump and Pistol Update")]
        public static void ApplyFpsUpdate()
        {
            FpsAssetSaveGuard.Active = true;
            try { ApplyFpsUpdateCore(); }
            finally { FpsAssetSaveGuard.Active = false; }
        }
        static void ApplyFpsUpdateCore()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PistolPath) == null) throw new InvalidOperationException("Missing SciFiPistol01_2 prefab.");
            foreach (string name in new[] { "Left Strafe Walk", "Right Strafe Walk", "Crouched Walking", "Walk Crouching Backward", "Jump" })
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(ClipPath(name));
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                var clips = importer.clipAnimations.Length == 0 ? importer.defaultClipAnimations : importer.clipAnimations;
                if (clips.Length != 1) throw new InvalidOperationException("Review clip count: " + name);
                clips[0].loopTime = clips[0].loopPose = name != "Jump";
                clips[0].lockRootRotation = clips[0].lockRootPositionXZ = clips[0].lockRootHeightY = true;
                clips[0].keepOriginalOrientation = false; // Body orientation removes the authored 30–60 degree heading difference.
                clips[0].rotationOffset = 0;
                clips[0].keepOriginalPositionY = clips[0].keepOriginalPositionXZ = true;
                importer.clipAnimations = clips; importer.SaveAndReimport();
                if (!Clip(name).humanMotion) throw new InvalidOperationException("Invalid Humanoid clip: " + name);
            }
            UpdateController();
            AttachPistols();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            // Preserve the user's current scene objects/overrides; save only this already-authorized scene.
            SceneManager.SetActiveScene(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("VOID LAST: existing FPS updated; only Player assets saved.");
        }
        static BlendTree GetTree(AnimatorController controller, string name)
        {
            var tree = AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().FirstOrDefault(t => t.name == name);
            if (tree == null) { tree = new BlendTree { name = name }; AssetDatabase.AddObjectToAsset(tree, controller); }
            tree.children = Array.Empty<ChildMotion>(); tree.useAutomaticThresholds = false;
            return tree;
        }
        static BlendTree DirectionTree(AnimatorController controller, string name, bool running)
        {
            var tree = GetTree(controller, name); tree.blendType = BlendTreeType.SimpleDirectional2D; tree.blendParameter = "MoveX"; tree.blendParameterY = "MoveY";
            tree.children = new[] {
                Child(running ? "Standard Run" : "Walking", Vector2.up, running ? 1 : Clip("Walking").length / 0.85f, 0),
                Child("Walking Backwards", Vector2.down, Clip("Walking Backwards").length / (running ? 0.55f : 0.85f), -0.08f),
                Child("Left Strafe Walk", Vector2.left, Clip("Left Strafe Walk").length / (running ? 0.55f : 0.85f), 0.08f),
                Child("Right Strafe Walk", Vector2.right, Clip("Right Strafe Walk").length / (running ? 0.55f : 0.85f), 0)
            };
            return tree;
        }
        static ChildMotion Child(string clip, Vector2 direction, float rate, float phase) => new ChildMotion { motion = Clip(clip), position = direction, timeScale = rate, cycleOffset = phase };
        static void UpdateController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            foreach (string parameter in new[] { "Gait", "CrouchAmount", "CrouchDirection", "JumpPhase" })
                if (!controller.parameters.Any(p => p.name == parameter)) controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            if (!controller.parameters.Any(p => p.name == "Airborne")) controller.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
            var gait = GetTree(controller, "Standing Locomotion"); gait.blendType = BlendTreeType.Simple1D; gait.blendParameter = "Gait";
            gait.AddChild(Clip("Idle"), 0); gait.AddChild(DirectionTree(controller, "Walk Directions", false), 1); gait.AddChild(DirectionTree(controller, "Run Directions", true), 2);
            var baseMachine = controller.layers[0].stateMachine;
            baseMachine.states.Single(s => s.state.name == "Standing").state.motion = gait;
            var travel = GetTree(controller, "Crouch Travel"); travel.blendType = BlendTreeType.Simple1D; travel.blendParameter = "CrouchDirection";
            travel.AddChild(Clip("Walk Crouching Backward"), -1); travel.AddChild(Clip("Crouched Walking"), 1);
            var crouch = GetTree(controller, "Crouched Locomotion"); crouch.blendType = BlendTreeType.Simple1D; crouch.blendParameter = "CrouchAmount";
            crouch.AddChild(Clip("Crouching Idle"), 0); crouch.AddChild(travel, 1);
            baseMachine.states.Single(s => s.state.name == "Crouched").state.motion = crouch;
            var maskPath = Root + "Animator/VOID_LAST_LowerBody.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null) { mask = new AvatarMask { name = "VOID_LAST_LowerBody" }; AssetDatabase.CreateAsset(mask, maskPath); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, true); mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, true);
            int index = Array.FindIndex(controller.layers, l => l.name == "Lower Body Jump");
            if (index < 0) { controller.AddLayer("Lower Body Jump"); index = controller.layers.Length - 1; }
            var layers = controller.layers; layers[index].avatarMask = mask; layers[index].defaultWeight = 0; layers[index].blendingMode = AnimatorLayerBlendingMode.Override; controller.layers = layers;
            var machine = layers[index].stateMachine;
            if (machine.states.Length == 0)
            {
                var empty = State(machine, "Grounded", null, Vector3.zero);
                var jump = State(machine, "Jump", Clip("Jump"), new Vector3(250, 0));
                jump.timeParameter = "JumpPhase"; jump.timeParameterActive = true;
                machine.defaultState = empty;
                var takeoff = Transition(empty, jump); takeoff.duration = 0.06f; Condition(takeoff, "Airborne", AnimatorConditionMode.If);
                var land = Transition(jump, empty); land.duration = 0.1f; Condition(land, "Airborne", AnimatorConditionMode.IfNot);
            }
            EditorUtility.SetDirty(controller); EditorUtility.SetDirty(mask);
            foreach (var tree in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>()) EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssetIfDirty(controller); AssetDatabase.SaveAssetIfDirty(mask);
        }
        static void AttachPistols()
        {
            var player = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var animator = player.GetComponentInChildren<Animator>();
                var camera = player.GetComponentInChildren<Camera>();
                var weaponRoot = camera.transform.Find("WeaponRoot");
                if (weaponRoot == null)
                {
                    weaponRoot = new GameObject("WeaponRoot").transform; weaponRoot.SetParent(camera.transform, false);
                    var pistol = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PistolPath), player.scene);
                    pistol.transform.SetParent(weaponRoot, false);
                    PrepareGun(pistol, true);
                }
                // Sample a real pistol pose to derive the grip basis from this rig, rather than guessing bone axes.
                Transform hand;
                Vector3 socketPosition;
                Quaternion socketRotation;
                try
                {
                    AnimationMode.StartAnimationMode(); AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, Clip("Pistol Idle"), 0.6f); AnimationMode.EndSampling();
                    hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    if (hand == null) throw new InvalidOperationException("Missing humanoid right hand.");
                    Quaternion forward = animator.transform.rotation;
                    socketRotation = Quaternion.Inverse(hand.rotation) * forward;
                    socketPosition = hand.InverseTransformVector(-(forward * new Vector3(0, -0.025f, -0.065f)));
                }
                finally { AnimationMode.StopAnimationMode(); }
                var socket = hand.Find("WorldWeaponSocket");
                if (socket == null) { socket = new GameObject("WorldWeaponSocket").transform; socket.SetParent(hand, false); }
                socket.localPosition = socketPosition; socket.localRotation = socketRotation;
                if (socket.childCount == 0)
                {
                    var pistol = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PistolPath), player.scene);
                    pistol.transform.SetParent(socket, false); PrepareGun(pistol, false);
                }
                var view = player.GetComponent<FpsWeaponView>(); if (view == null) view = player.AddComponent<FpsWeaponView>();
                SetReference(view, "weaponRoot", weaponRoot); SetReference(view, "worldWeapon", socket.gameObject);
                var settings = new SerializedObject(view);
                if (settings.FindProperty("hipEulerAngles").vector3Value == new Vector3(0, 180, 0) &&
                    settings.FindProperty("aimEulerAngles").vector3Value == new Vector3(0, 180, 0))
                {
                    settings.FindProperty("hipEulerAngles").vector3Value = Vector3.zero;
                    settings.FindProperty("aimEulerAngles").vector3Value = Vector3.zero;
                    settings.FindProperty("aimPosition").vector3Value = new Vector3(0, -0.075f, 0.42f);
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                weaponRoot.localPosition = settings.FindProperty("hipPosition").vector3Value;
                weaponRoot.localRotation = Quaternion.Euler(settings.FindProperty("hipEulerAngles").vector3Value);
                weaponRoot.localScale = Vector3.one * settings.FindProperty("modelScale").floatValue;
                var visibility = new SerializedObject(player.GetComponent<FpsLocalVisibility>());
                SetRenderers(visibility.FindProperty("worldRenderers"), animator.GetComponentsInChildren<Renderer>(true));
                SetRenderers(visibility.FindProperty("firstPersonRenderers"), weaponRoot.GetComponentsInChildren<Renderer>(true));
                visibility.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }
        static void PrepareGun(GameObject pistol, bool firstPerson)
        {
            foreach (var collider in pistol.GetComponentsInChildren<Collider>(true)) { collider.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(collider); }
            foreach (var body in pistol.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; PrefabUtility.RecordPrefabInstancePropertyModifications(body); }
            if (firstPerson) foreach (var renderer in pistol.GetComponentsInChildren<Renderer>(true)) { renderer.shadowCastingMode = ShadowCastingMode.Off; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
        }
        static void SetRenderers(SerializedProperty property, Renderer[] renderers)
        {
            property.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        }
    }
}
