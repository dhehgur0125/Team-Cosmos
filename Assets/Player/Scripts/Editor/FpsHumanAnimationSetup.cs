/* [ 코드 설명 ] Kevin Iglesias Human Animations(남성 Humanoid 클립)로 플레이어 전신 Animator Controller를 새로 만들고 Player 프리팹에 연결합니다. 기존 FpsAnimation 파라미터·레이어 순서(0 기본, 1 상체, 2 하체 점프)를 그대로 지키고 사격·재장전·권총 넣기/빼기·피격·사망 변형·대기 변형·부상 대기를 추가합니다. 기존 컨트롤러(VOID_LAST_FPS.controller)는 지우지 않습니다. */
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsHumanAnimationSetup
    {
        const string Pack = "Assets/Kevin Iglesias/Human Animations/Animations/Male";
        const string ControllerPath = "Assets/Player/Animator/VOID_LAST_Human.controller";
        const string UpperMask = "Assets/Player/Animator/VOID_LAST_UpperBody.mask";
        const string LowerMask = "Assets/Player/Animator/VOID_LAST_LowerBody.mask";
        const string MeleeFbx = "Assets/Player/Animator/Standing Melee Punch.fbx"; // The pack has no unarmed strike; keep the existing one.
        const string PlayerPrefab = "Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab";
        static readonly Dictionary<string, AnimationClip> cache = new Dictionary<string, AnimationClip>();

        [MenuItem("VOID LAST/Player/Build Human Animation Controller (Kevin Iglesias)")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            cache.Clear();
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath); // Regenerated from scratch every run.
            var c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in new[] { "MoveX", "MoveY", "Gait", "CrouchAmount", "CrouchDirection", "JumpPhase", "Wounded" }) c.AddParameter(p, AnimatorControllerParameterType.Float);
            foreach (var p in new[] { "Crouch", "CrouchBackward", "Aim", "Dead", "Airborne", "Reload", "Holster" }) c.AddParameter(p, AnimatorControllerParameterType.Bool);
            foreach (var p in new[] { "Slot", "DeathIndex", "HitIndex" }) c.AddParameter(p, AnimatorControllerParameterType.Int);
            foreach (var p in new[] { "Melee", "Shoot", "Hit", "IdleBreak" }) c.AddParameter(p, AnimatorControllerParameterType.Trigger);

            BuildBase(c);
            BuildUpper(c);
            BuildJump(c);
            BuildHit(c);
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            Debug.Log("Human animation controller built: " + ControllerPath);
        }

        [MenuItem("VOID LAST/Player/Use Human Animation Controller On Player")]
        public static void Assign()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new InvalidOperationException("Build the controller first.");
            AssetDatabase.ImportAsset(PlayerPrefab, ImportAssetOptions.ForceUpdate);
            var player = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var body = new SerializedObject(player.GetComponent<FpsAnimation>()).FindProperty("animator").objectReferenceValue as Animator;
                if (body == null) throw new InvalidOperationException("FpsAnimation has no Animator.");
                body.runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            Debug.Log("Player body now uses " + ControllerPath);
        }

        // ---------- Base layer: locomotion, crouch, idle variants, death ----------
        static void BuildBase(AnimatorController c)
        {
            var sm = c.layers[0].stateMachine;
            var idle = Tree(c, "Idle", BlendTreeType.Simple1D, "Wounded");
            idle.AddChild(Clip("HumanM@Idle01"), 0); idle.AddChild(Clip("HumanM@IdleWounded01"), 1);
            // Playback rates make the feet travel at the motor's real speed (pack clips: walk/crouch 1.9 m/s, run 3.8 m/s).
            var walk = Directional(c, "Walk", WalkSpeed / PackWalk, "Walk01_Forward", "Walk01_ForwardRight", "Walk01_Right", "Walk01_BackwardRight", "Walk01_Backward", "Walk01_BackwardLeft", "Walk01_Left", "Walk01_ForwardLeft");
            // One run cycle for all eight directions: mixing Sprint (5.7-7.4 m/s, 0.53 s) with Run (3.8 m/s, 0.6 s) made the feet stutter between directions.
            var run = Directional(c, "Run", RunSpeed / PackRun, "Run01_Forward", "Run01_ForwardRight", "Run01_Right", "Run01_BackwardRight", "Run01_Backward", "Run01_BackwardLeft", "Run01_Left", "Run01_ForwardLeft");
            var standing = Tree(c, "Standing Locomotion", BlendTreeType.Simple1D, "Gait");
            standing.AddChild(idle, 0); standing.AddChild(walk, 1); standing.AddChild(run, 2);
            var crouchWalk = Directional(c, "Crouch Walk", CrouchSpeed / PackWalk, "Crouch01_Walk_Forward", "Crouch01_Walk_ForwardRight", "Crouch01_Walk_Right", "Crouch01_Walk_BackwardRight", "Crouch01_Walk_Backward", "Crouch01_Walk_BackwardLeft", "Crouch01_Walk_Left", "Crouch01_Walk_ForwardLeft");
            var crouched = Tree(c, "Crouched Locomotion", BlendTreeType.Simple1D, "CrouchAmount");
            crouched.AddChild(Clip("HumanM@Crouch01_Idle"), 0); crouched.AddChild(crouchWalk, 1);

            var stand = State(sm, "Standing", standing, new Vector3(300, 0));
            var crouch = State(sm, "Crouched", crouched, new Vector3(300, 120));
            var idleBreak = State(sm, "Idle Break", Clip("HumanM@Idle01_Break01"), new Vector3(560, -60));
            sm.defaultState = stand;
            Link(stand, crouch, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Crouch");
            Link(crouch, stand, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");
            var toBreak = Link(stand, idleBreak, 0.25f); toBreak.AddCondition(AnimatorConditionMode.If, 0, "IdleBreak"); toBreak.AddCondition(AnimatorConditionMode.Less, 0.1f, "Gait"); toBreak.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");
            Link(idleBreak, stand, 0.3f, exit: true, exitTime: 0.92f);
            foreach (var leave in new[] { AnimatorConditionMode.Greater }) { var t = Link(idleBreak, stand, 0.2f); t.AddCondition(leave, 0.1f, "Gait"); }
            Link(idleBreak, crouch, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Crouch");

            // Five death falls picked by DeathIndex.
            for (int i = 0; i < 5; i++)
            {
                var death = State(sm, "Death " + (i + 1), Clip("HumanM@Death0" + (i + 1)), new Vector3(0, 260 + i * 60));
                var any = sm.AddAnyStateTransition(death); any.duration = 0.12f; any.hasExitTime = false; any.canTransitionToSelf = false;
                any.AddCondition(AnimatorConditionMode.If, 0, "Dead"); any.AddCondition(AnimatorConditionMode.Equals, i, "DeathIndex");
            }
        }

        // ---------- Upper body: pistol (draw / hip / aim / shoot / reload / holster), melee, items ----------
        static void BuildUpper(AnimatorController c)
        {
            c.AddLayer("Upper Body");
            var layers = c.layers;
            layers[1].avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperMask); layers[1].defaultWeight = 1; layers[1].iKPass = true; // FpsHeldItemPose uses this IK pass.
            c.layers = layers;
            var sm = c.layers[1].stateMachine;
            var unarmed = State(sm, "Unarmed", Clip("HumanM@Idle01"), new Vector3(300, 0));
            var draw = State(sm, "Draw Pistol", Clip("HumanM@UnsheatheHips01_R"), new Vector3(300, 120));
            var hip = State(sm, "Pistol Idle", Clip("HumanM@Gun_Aim02"), new Vector3(560, 120));
            var aim = State(sm, "Pistol Aim", Clip("HumanM@Gun_AimPrecise01"), new Vector3(560, 240));
            var shootHip = State(sm, "Pistol Shoot", Clip("HumanM@Gun_Aim02_Shoot01"), new Vector3(820, 120));
            var shootAim = State(sm, "Pistol Aim Shoot", Clip("HumanM@Gun_AimPrecise01_Shoot01"), new Vector3(820, 240));
            var reload = State(sm, "Pistol Reload", Clip("HumanM@Gun_Reload01"), new Vector3(560, 360)); reload.speed = 1.7f / 1.5f; // Pack clip 1.7s, gameplay reload 1.5s.
            var holster = State(sm, "Holster Pistol", Clip("HumanM@SheatheHips01_R"), new Vector3(300, 360));
            var melee = State(sm, "Melee Attack", MeleeClip(), new Vector3(0, 120)); melee.tag = "Melee";
            sm.defaultState = unarmed;

            Link(unarmed, draw, 0.15f).AddCondition(AnimatorConditionMode.Equals, 1, "Slot");
            Link(draw, hip, 0.15f, exit: true, exitTime: 0.85f);
            Leave(draw, unarmed); Leave(hip, unarmed); Leave(aim, unarmed); Leave(shootHip, unarmed); Leave(shootAim, unarmed); Leave(reload, unarmed); Leave(holster, unarmed);
            Link(hip, aim, 0.12f).AddCondition(AnimatorConditionMode.If, 0, "Aim");
            Link(aim, hip, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "Aim");
            foreach (var from in new[] { hip, shootHip }) { var t = Link(from, shootHip, 0.05f); t.AddCondition(AnimatorConditionMode.If, 0, "Shoot"); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Aim"); t.canTransitionToSelf = true; }
            foreach (var from in new[] { aim, shootAim }) { var t = Link(from, shootAim, 0.05f); t.AddCondition(AnimatorConditionMode.If, 0, "Shoot"); t.AddCondition(AnimatorConditionMode.If, 0, "Aim"); }
            Link(shootHip, hip, 0.15f, exit: true, exitTime: 0.8f);
            Link(shootAim, aim, 0.15f, exit: true, exitTime: 0.8f);
            foreach (var from in new[] { hip, aim, shootHip, shootAim }) Link(from, reload, 0.12f).AddCondition(AnimatorConditionMode.If, 0, "Reload");
            Link(reload, hip, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Reload");
            foreach (var from in new[] { hip, aim, shootHip, shootAim, reload, draw }) Link(from, holster, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Holster");
            Link(holster, draw, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "Holster"); // Holster cancelled by re-selecting the pistol.

            var punch = sm.AddAnyStateTransition(melee); punch.duration = 0.08f; punch.hasExitTime = false; punch.canTransitionToSelf = false;
            punch.AddCondition(AnimatorConditionMode.If, 0, "Melee"); punch.AddCondition(AnimatorConditionMode.Equals, 2, "Slot"); punch.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Link(melee, unarmed, 0.16f, exit: true, exitTime: 0.9f);
            Link(melee, draw, 0.16f).AddCondition(AnimatorConditionMode.Equals, 1, "Slot");
        }

        // ---------- Lower body jump: the pack's full jump scrubbed by the real jump phase ----------
        static void BuildJump(AnimatorController c)
        {
            c.AddLayer("Lower Body Jump");
            var layers = c.layers; layers[2].avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(LowerMask); layers[2].defaultWeight = 0; c.layers = layers;
            var sm = c.layers[2].stateMachine;
            var grounded = State(sm, "Grounded", null, new Vector3(300, 0));
            var jump = State(sm, "Jump", Clip("HumanM@Jump01"), new Vector3(300, 120));
            jump.timeParameter = "JumpPhase"; jump.timeParameterActive = true;
            sm.defaultState = grounded;
            Link(grounded, jump, 0.06f).AddCondition(AnimatorConditionMode.If, 0, "Airborne");
            Link(jump, grounded, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "Airborne");
        }

        // ---------- Hit reactions: upper body flinch over whatever else is playing ----------
        static void BuildHit(AnimatorController c)
        {
            c.AddLayer("Hit Reaction");
            var layers = c.layers; layers[3].avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperMask); layers[3].defaultWeight = 0; // FpsAnimation raises it only while a flinch plays; an idle empty state at weight 1 locks the arms. c.layers = layers;
            var sm = c.layers[3].stateMachine;
            var none = State(sm, "None", null, new Vector3(300, 0));
            sm.defaultState = none;
            for (int i = 0; i < 4; i++)
            {
                var hit = State(sm, "Hit " + (i + 1), Clip("HumanM@Damage0" + (i + 1)), new Vector3(560, i * 70));
                var any = sm.AddAnyStateTransition(hit); any.duration = 0.05f; any.hasExitTime = false; any.canTransitionToSelf = true;
                any.AddCondition(AnimatorConditionMode.If, 0, "Hit"); any.AddCondition(AnimatorConditionMode.Equals, i, "HitIndex"); any.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                Link(hit, none, 0.2f, exit: true, exitTime: 0.85f);
            }
        }

        // ---------- helpers ----------
        static AnimationClip Clip(string name)
        {
            if (cache.TryGetValue(name, out var cached)) return cached;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Model", new[] { Pack }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != name) continue; // Skip "[RM]" and longer names.
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip clip && !clip.name.StartsWith("__preview")) { cache[name] = clip; return clip; }
            }
            throw new InvalidOperationException("Missing pack clip " + name);
        }

        static AnimationClip MeleeClip()
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(MeleeFbx)) if (o is AnimationClip clip && !clip.name.StartsWith("__preview")) return clip;
            return Clip("HumanM@Damage05");
        }

        static BlendTree Tree(AnimatorController c, string name, BlendTreeType type, string parameter, string parameterY = null)
        {
            var tree = new BlendTree { name = name, blendType = type, blendParameter = parameter, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            if (parameterY != null) tree.blendParameterY = parameterY;
            AssetDatabase.AddObjectToAsset(tree, c);
            return tree;
        }

        // Eight directions, clockwise from forward, on MoveX/MoveY.
        static BlendTree Directional(AnimatorController c, string name, float timeScale, params string[] clips)
        {
            var tree = Tree(c, name, BlendTreeType.FreeformDirectional2D, "MoveX", "MoveY");
            const float d = 0.7071f;
            var positions = new[] { new Vector2(0, 1), new Vector2(d, d), new Vector2(1, 0), new Vector2(d, -d), new Vector2(0, -1), new Vector2(-d, -d), new Vector2(-1, 0), new Vector2(-d, d) };
            for (int i = 0; i < clips.Length; i++) tree.AddChild(Clip("HumanM@" + clips[i]), positions[i]);
            var children = tree.children;
            for (int i = 0; i < children.Length; i++) children[i].timeScale = timeScale;
            tree.children = children;
            return tree;
        }
        // Authored travel speeds of the pack clips (from their [RM] versions) and FpsMotor's speeds.
        const float PackWalk = 1.898f, PackRun = 3.795f, WalkSpeed = 2.2f, RunSpeed = 4.5f, CrouchSpeed = 1.1f;

        static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion, Vector3 position)
        {
            var state = sm.AddState(name, position); state.motion = motion; state.writeDefaultValues = false; return state;
        }

        static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration, bool exit = false, float exitTime = 0.9f)
        {
            var t = from.AddTransition(to); t.duration = duration; t.hasFixedDuration = true; t.hasExitTime = exit; t.exitTime = exitTime;
            t.interruptionSource = TransitionInterruptionSource.Destination; return t;
        }

        static void Leave(AnimatorState from, AnimatorState unarmed) => Link(from, unarmed, 0.16f).AddCondition(AnimatorConditionMode.NotEqual, 1, "Slot");
    }
}
