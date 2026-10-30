/* [ 코드 설명 ] Player 경로의 Mixamo 클립을 검사하고 FPS 테스트 에셋을 구성하는 전용 에디터 도구입니다. 원본 모델과 기존 프리팹, 다른 씬은 저장하거나 변경하지 않습니다. */
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static partial class FpsAssetSetup
    {
        public const string Root = "Assets/Player/";
        public const string ModelPath = Root + "Prefabs/Player/Astronaut_Pilot_Full.prefab";
        public const string PrefabPath = Root + "Prefabs/Player/VOID_LAST_FPS.prefab";
        public const string ControllerPath = Root + "Animator/VOID_LAST_FPS.controller";
        public const string ScenePath = Root + "Character_test.unity";
        public static readonly string[] Files = { "Idle", "Walking", "Walking Backwards", "Left Strafe Walk", "Right Strafe Walk", "Standard Run", "Crouching Idle", "Crouched Walking", "Walk Crouching Backward", "Standing To Crouched", "Crouched To Standing", "Pistol Idle", "Unarmed Equip Underarm", "Standing Melee Punch", "Dying", "Jump" };
        static string ClipPath(string name) => Root + "Animator/" + name + ".fbx";
        public static AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(ClipPath(name)).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__"));
        static bool Loops(string name) => Array.IndexOf(Files, name) < 9 || name == "Pistol Idle";

        [MenuItem("VOID LAST/Player/1 - Inspect Humanoid Clips")]
        public static void InspectClips()
        {
            Directory.CreateDirectory(Root + "Validation");
            var report = new StringBuilder("# Actual Unity clip inspection\n\n");
            foreach (string name in Files)
            {
                var importer = AssetImporter.GetAtPath(ClipPath(name)) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("Missing FBX: " + name);
                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.SaveAndReimport();
                }
                var avatar = AssetDatabase.LoadAllAssetsAtPath(ClipPath(name)).OfType<Avatar>().FirstOrDefault();
                var clip = Clip(name);
                if (avatar == null || !avatar.isValid || !avatar.isHuman || !clip.humanMotion)
                    throw new InvalidOperationException("Invalid Humanoid: " + name);
                report.AppendLine($"- {name}: internal=`{clip.name}`; seconds={clip.length:F3}; human={clip.humanMotion}; avatarValid={avatar.isValid}; averageSpeed={clip.averageSpeed:F3}; averageAngularSpeed={clip.averageAngularSpeed:F3}; rootCurves={clip.hasRootCurves}; motionCurves={clip.hasMotionCurves}");
            }
            File.WriteAllText(Root + "Validation/ClipInspection.md", report.ToString());
            CapturePoseSheet();
            AssetDatabase.Refresh();
            Debug.Log("VOID LAST: Humanoid clips inspected; see Player/Validation.");
        }

        static readonly HumanBodyBones[,] Edges = {
            { HumanBodyBones.Hips, HumanBodyBones.Spine }, { HumanBodyBones.Spine, HumanBodyBones.Chest },
            { HumanBodyBones.Chest, HumanBodyBones.Neck }, { HumanBodyBones.Neck, HumanBodyBones.Head },
            { HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm }, { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm },
            { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand }, { HumanBodyBones.Chest, HumanBodyBones.RightUpperArm },
            { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm }, { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
            { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg }, { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg },
            { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot }, { HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg },
            { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg }, { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }
        };
        static void CapturePoseSheet()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), scene);
            var animator = model.GetComponent<Animator>();
            var texture = new Texture2D(1200, Files.Length * 200, TextureFormat.RGB24, false);
            var pixels = Enumerable.Repeat(new Color32(20, 25, 33, 255), texture.width * texture.height).ToArray();
            var positions = new StringBuilder("file,phase,hipX,hipY,hipZ,leftHandX,leftHandY,leftHandZ,rightHandX,rightHandY,rightHandZ\n");
            try
            {
                AnimationMode.StartAnimationMode();
                for (int row = 0; row < Files.Length; row++)
                {
                    var clip = Clip(Files[row]);
                    for (int phase = 0; phase < 3; phase++)
                    {
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(model, clip, clip.length * (phase == 0 ? 0.05f : phase == 1 ? 0.5f : 0.95f));
                        AnimationMode.EndSampling();
                        Vector3 hip = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                        Vector3 left = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                        Vector3 right = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                        positions.AppendLine(FormattableString.Invariant($"{Files[row]},{phase},{hip.x},{hip.y},{hip.z},{left.x},{left.y},{left.z},{right.x},{right.y},{right.z}"));
                        for (int view = 0; view < 2; view++)
                        {
                            int x0 = (phase * 2 + view) * 200;
                            int y0 = (Files.Length - row - 1) * 200;
                            DrawLine(pixels, texture.width, x0, y0 + 15, x0 + 198, y0 + 15, new Color32(65, 72, 82, 255));
                            for (int e = 0; e < Edges.GetLength(0); e++)
                            {
                                var a = animator.GetBoneTransform(Edges[e, 0]); var b = animator.GetBoneTransform(Edges[e, 1]);
                                if (a == null || b == null) continue;
                                Vector3 p = a.position, q = b.position;
                                float px = view == 0 ? p.x - hip.x : p.z - hip.z;
                                float qx = view == 0 ? q.x - hip.x : q.z - hip.z;
                                var color = e < 4 ? new Color32(240, 240, 240, 255) : e < 7 || e >= 10 && e < 13 ? new Color32(90, 200, 255, 255) : new Color32(255, 160, 80, 255);
                                DrawLine(pixels, texture.width, x0 + 100 + (int)(px * 80), y0 + 15 + (int)(p.y * 80), x0 + 100 + (int)(qx * 80), y0 + 15 + (int)(q.y * 80), color);
                            }
                        }
                    }
                }
                texture.SetPixels32(pixels); texture.Apply();
                File.WriteAllBytes(Root + "Validation/ClipPoses.png", texture.EncodeToPNG());
                File.WriteAllText(Root + "Validation/ClipPoseSamples.csv", positions.ToString());
            }
            finally { AnimationMode.StopAnimationMode(); Object.DestroyImmediate(texture); EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void DrawLine(Color32[] pixels, int width, int x, int y, int endX, int endY, Color32 color)
        {
            int steps = Mathf.Max(Mathf.Abs(endX - x), Mathf.Abs(endY - y));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0 : (float)i / steps;
                int px = Mathf.RoundToInt(Mathf.Lerp(x, endX, t)), py = Mathf.RoundToInt(Mathf.Lerp(y, endY, t));
                if (px >= 0 && px < width && py >= 0 && py < pixels.Length / width) pixels[py * width + px] = color;
            }
        }

        [MenuItem("VOID LAST/Player/2 - Build FPS Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building.");
            if (File.Exists(PrefabPath) || File.Exists(ControllerPath)) throw new InvalidOperationException("FPS assets already exist. Edit them in place; this builder will not overwrite them.");
            foreach (string name in Files)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(ClipPath(name));
                if (!Clip(name).humanMotion) throw new InvalidOperationException("Run Inspect Humanoid Clips first.");
                var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                if (clips.Length != 1) throw new InvalidOperationException("Review unexpected clip count: " + name);
                var clip = clips[0];
                clip.loopTime = Loops(name);
                clip.loopPose = Loops(name);
                // Inspection found in-place locomotion, but large Y/rotation deltas on transitions.
                // Original orientation prevents strafe/body orientation from turning the character.
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.rotationOffset = 0;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.heightFromFeet = false;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
            var mask = new AvatarMask { name = "VOID_LAST_UpperBody" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers }) mask.SetHumanoidBodyPartActive(part, true);
            AssetDatabase.CreateAsset(mask, Root + "Animator/VOID_LAST_UpperBody.mask");
            var controller = CreateController(mask);
            CreatePrefab(controller);
            PopulateTestScene();
            ApplyFpsUpdate();
            Debug.Log("VOID LAST: FPS prefab, controller, mask and Character_test scene saved inside Assets/Player.");
        }

        static AnimatorController CreateController(AvatarMask mask)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (string parameter in new[] { "MoveX", "MoveY" }) controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            foreach (string parameter in new[] { "Crouch", "CrouchBackward", "Aim", "Dead" }) controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            controller.AddParameter("Slot", AnimatorControllerParameterType.Int);
            controller.AddParameter("Melee", AnimatorControllerParameterType.Trigger);
            var machine = controller.layers[0].stateMachine;
            var walk = Tree(controller, "Standing Locomotion");
            walk.AddChild(Clip("Idle"), Vector2.zero);
            walk.AddChild(Clip("Walking"), Vector2.up);
            walk.AddChild(Clip("Walking Backwards"), Vector2.down);
            walk.AddChild(Clip("Left Strafe Walk"), Vector2.left);
            walk.AddChild(Clip("Right Strafe Walk"), Vector2.right);
            walk.AddChild(Clip("Standard Run"), Vector2.up * (4.5f / 2.2f));
            var standing = State(machine, "Standing", walk, new Vector3(200, 0));
            var crouchTree = new BlendTree { name = "Crouched Locomotion", blendType = BlendTreeType.Simple1D, blendParameter = "MoveY", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(crouchTree, controller);
            crouchTree.AddChild(Clip("Walk Crouching Backward"), -0.5f);
            crouchTree.AddChild(Clip("Crouching Idle"), 0);
            crouchTree.AddChild(Clip("Crouched Walking"), 0.5f);
            var crouched = State(machine, "Crouched", crouchTree, new Vector3(500, 150));
            var down = State(machine, "Crouch Down", Clip("Standing To Crouched"), new Vector3(200, 150));
            var up = State(machine, "Stand Up", Clip("Crouched To Standing"), new Vector3(500, 0));
            var death = State(machine, "Death", Clip("Dying"), new Vector3(200, 320));
            machine.defaultState = standing;
            Condition(Transition(standing, down), "Crouch", AnimatorConditionMode.If);
            Condition(Transition(down, up), "Crouch", AnimatorConditionMode.IfNot);
            Transition(down, crouched, true);
            Condition(Transition(crouched, up), "Crouch", AnimatorConditionMode.IfNot);
            Condition(Transition(up, down), "Crouch", AnimatorConditionMode.If);
            Transition(up, standing, true);
            var die = machine.AddAnyStateTransition(death); die.duration = 0.12f; die.hasExitTime = false; die.canTransitionToSelf = false;
            Condition(die, "Dead", AnimatorConditionMode.If);
            controller.AddLayer("Upper Body");
            var layers = controller.layers; layers[1].avatarMask = mask; layers[1].defaultWeight = 1; layers[1].blendingMode = AnimatorLayerBlendingMode.Override; controller.layers = layers;
            var upper = layers[1].stateMachine;
            var empty = State(upper, "Unarmed", null, new Vector3(0, 0));
            var equip = State(upper, "Equip Pistol", Clip("Unarmed Equip Underarm"), new Vector3(240, 0));
            var pistol = State(upper, "Pistol Idle", Clip("Pistol Idle"), new Vector3(490, 0));
            var attack = State(upper, "Melee Attack", Clip("Standing Melee Punch"), new Vector3(0, 180)); attack.tag = "Melee";
            var posture = State(upper, "Crouch Backward Posture", Clip("Crouching Idle"), new Vector3(490, 180));
            upper.defaultState = empty;
            Condition(Transition(empty, equip), "Slot", AnimatorConditionMode.Equals, 1);
            Condition(Transition(posture, equip), "Slot", AnimatorConditionMode.Equals, 1);
            Condition(Transition(attack, equip), "Slot", AnimatorConditionMode.Equals, 1);
            Transition(equip, pistol, true);
            foreach (var state in new[] { equip, pistol }) Condition(Transition(state, empty), "Slot", AnimatorConditionMode.NotEqual, 1);
            var punch = upper.AddAnyStateTransition(attack); punch.duration = 0.08f; punch.hasExitTime = false; punch.canTransitionToSelf = false;
            Condition(punch, "Melee", AnimatorConditionMode.If); Condition(punch, "Slot", AnimatorConditionMode.Equals, 2); Condition(punch, "Dead", AnimatorConditionMode.IfNot);
            Transition(attack, empty, true);
            Condition(Transition(attack, empty), "Slot", AnimatorConditionMode.NotEqual, 2);
            var back = Transition(empty, posture); Condition(back, "CrouchBackward", AnimatorConditionMode.If); Condition(back, "Slot", AnimatorConditionMode.NotEqual, 1);
            Condition(Transition(posture, empty), "CrouchBackward", AnimatorConditionMode.IfNot);
            // No dedicated aim clip exists. Aim is exposed as a parameter; no substitute animation is assigned.
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }
        static BlendTree Tree(AnimatorController controller, string name)
        {
            var tree = new BlendTree { name = name, blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveY", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller); return tree;
        }
        static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion, Vector3 position)
        {
            var state = machine.AddState(name, position); state.motion = motion; state.writeDefaultValues = false; return state;
        }
        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, bool exit = false)
        {
            var t = from.AddTransition(to); t.duration = 0.16f; t.hasFixedDuration = true; t.hasExitTime = exit; t.exitTime = 0.9f; t.interruptionSource = TransitionInterruptionSource.SourceThenDestination; return t;
        }
        static void Condition(AnimatorStateTransition transition, string parameter, AnimatorConditionMode mode, float value = 0) => transition.AddCondition(mode, value, parameter);

        static void CreatePrefab(AnimatorController controller)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("VOID_LAST_FPS"); SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var capsule = root.AddComponent<CharacterController>(); capsule.height = 1.85f; capsule.radius = 0.3f; capsule.center = Vector3.up * 0.925f; capsule.stepOffset = 0.25f; capsule.slopeLimit = 45; capsule.skinWidth = 0.025f; capsule.minMoveDistance = 0;
                root.AddComponent<FpsInput>(); root.AddComponent<FpsMotor>(); root.AddComponent<FpsEquipment>();
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), preview);
                model.transform.SetParent(root.transform, false);
                var animator = model.GetComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman) throw new InvalidOperationException("Astronaut avatar invalid.");
                // Full prefab contains mutually exclusive face/backpack/glass variants. Keep a single variant on this instance.
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    string n = renderer.name;
                    if ((n.StartsWith("Astronaut_Person_Face_") && n != "Astronaut_Person_Face_01") || n == "Astronaut_Suit_Backpack_02_Mesh" || n == "Astronaut_Suit_Backpack_Belts_02_Mesh" || n == "Astronaut_Suit_Helmet_Glass_02_Mesh" || n == "Astronaut_Suit_Helmet_Glass_03_Mesh" || n.EndsWith("Mesh 1")) renderer.gameObject.SetActive(false);
                }
                var pivot = new GameObject("CameraPivot"); pivot.transform.SetParent(root.transform, false); pivot.transform.localPosition = new Vector3(0, 1.67f, 0.08f);
                var cameraObject = new GameObject("FPS Camera"); cameraObject.transform.SetParent(pivot.transform, false); cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>(); camera.nearClipPlane = 0.03f; camera.farClipPlane = 300; camera.fieldOfView = 75; cameraObject.AddComponent<AudioListener>();
                SetReference(root.AddComponent<FpsLook>(), "cameraPivot", pivot.transform);
                SetReference(root.AddComponent<FpsAnimation>(), "animator", animator);
                var visibility = root.AddComponent<FpsLocalVisibility>();
                var serialized = new SerializedObject(visibility); serialized.FindProperty("localCamera").objectReferenceValue = camera;
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                var property = serialized.FindProperty("worldRenderers"); property.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static void SetReference(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target); serialized.FindProperty(property).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static void PopulateTestScene()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (scene.GetRootGameObjects().Any(g => g.name == "VOID_LAST_FPS")) throw new InvalidOperationException("Test scene already contains FPS player.");
            foreach (var camera in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()))
            { Undo.RecordObject(camera.gameObject, "Disable old test camera"); camera.gameObject.SetActive(false); }
            var environment = new GameObject("FPS Test Geometry"); SceneManager.MoveGameObjectToScene(environment, scene); Undo.RegisterCreatedObjectUndo(environment, "Create FPS test geometry");
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FPS Test Floor", color = new Color(0.23f, 0.27f, 0.3f) };
            var obstacleMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FPS Test Obstacle", color = new Color(0.7f, 0.34f, 0.1f) };
            AssetDatabase.CreateAsset(groundMaterial, Root + "Materials/FPS_TestFloor.mat"); AssetDatabase.CreateAsset(obstacleMaterial, Root + "Materials/FPS_TestObstacle.mat");
            Cube("Floor", new Vector3(0, -0.25f, 0), new Vector3(24, 0.5f, 24), groundMaterial, environment.transform);
            Cube("Low Ceiling - clearance 1.25m", new Vector3(0, 1.4f, 5), new Vector3(3, 0.3f, 4), obstacleMaterial, environment.transform);
            Cube("Collision Wall", new Vector3(5, 1.5f, 3), new Vector3(0.5f, 3, 6), obstacleMaterial, environment.transform);
            Cube("Step - 0.15m", new Vector3(-4, 0.075f, 3), new Vector3(2, 0.15f, 2), obstacleMaterial, environment.transform);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            player.transform.position = new Vector3(0, 0.05f, -3); Undo.RegisterCreatedObjectUndo(player, "Create FPS player");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            SceneManager.SetActiveScene(previous);
        }
        static void Cube(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = name; cube.transform.SetParent(parent, false); cube.transform.position = position; cube.transform.localScale = scale; cube.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
