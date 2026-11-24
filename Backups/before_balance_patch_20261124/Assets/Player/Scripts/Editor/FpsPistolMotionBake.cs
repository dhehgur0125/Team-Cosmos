/* [ 코드 설명 ] Fps Weapons Pack의 Pistol.fbx 클립(PistolDraw/Holster/Reload/HipFire/AdsFire)에서 총 본체(master)의 회전 변화만 추출해 FpsPistolMotions 에셋으로 저장하고 Player 프리팹의 FpsWeaponView에 연결합니다. 원본 FBX와 총 모델은 수정하지 않습니다. */
using System;
using UnityEditor;
using UnityEngine;

namespace VoidLast.Player.Editor
{
    public static class FpsPistolMotionBake
    {
        const string Fbx = "Assets/fps weapons pack/GunAndArmMeshes/Pistol.fbx";
        const string Output = "Assets/Player/Prefabs/Weapons/FpsPistolMotions.asset";
        const string PlayerPrefab = "Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab";
        const string Master = "PistolRig/master";

        [MenuItem("VOID LAST/Player/Bake Pistol Motions")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null) throw new InvalidOperationException("Missing " + Fbx);
            var rigTransform = model.transform.Find("PistolRig");
            Quaternion rig = rigTransform.localRotation;
            var hip = Clip("PistolHipFire");
            Quaternion rest = Rotation(hip, 0); // The first idle frame is the pose FpsWeaponView already holds.

            var asset = AssetDatabase.LoadAssetAtPath<FpsPistolMotions>(Output);
            if (asset == null) { asset = ScriptableObject.CreateInstance<FpsPistolMotions>(); AssetDatabase.CreateAsset(asset, Output); }
            asset.draw = Build(Clip("PistolDraw"), rig, rest);
            asset.holster = Build(Clip("PistolHolster"), rig, rest);
            asset.reload = Build(Clip("PistolReload"), rig, rest);
            asset.fireHip = Build(hip, rig, rest);
            asset.fireAds = Build(Clip("PistolAdsFire"), rig, rest);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log("Pistol motions baked: " + Output + " (valid=" + asset.IsValid + ")");
        }

        /// <summary>Player 프리팹의 FpsWeaponView에 베이크된 에셋만 연결한다. 프리팹을 수정하므로 따로 실행한다.</summary>
        [MenuItem("VOID LAST/Player/Assign Pistol Motions To Player Prefab")]
        public static void Assign()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var asset = AssetDatabase.LoadAssetAtPath<FpsPistolMotions>(Output);
            if (asset == null) throw new InvalidOperationException("Run 'Bake Pistol Motions' first.");
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var view = root.GetComponent<FpsWeaponView>();
                var fields = new SerializedObject(view);
                fields.FindProperty("motions").objectReferenceValue = asset;
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("Pistol motions assigned to " + PlayerPrefab);
        }

        static AnimationClip Clip(string name)
        {
            foreach (var item in AssetDatabase.LoadAllAssetsAtPath(Fbx))
                if (item is AnimationClip clip && clip.name == name) return clip;
            throw new InvalidOperationException("Missing clip " + name);
        }

        static AnimationCurve Curve(AnimationClip clip, string axis) =>
            AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(Master, typeof(Transform), "m_LocalRotation." + axis));

        static Quaternion Rotation(AnimationClip clip, float time) =>
            new Quaternion(Curve(clip, "x").Evaluate(time), Curve(clip, "y").Evaluate(time), Curve(clip, "z").Evaluate(time), Curve(clip, "w").Evaluate(time)).normalized;

        // delta(t) = rig * (master(t) * master_rest^-1) * rig^-1 : rotation of the gun body in model space relative to the idle pose.
        static FpsPistolMotions.Motion Build(AnimationClip clip, Quaternion rig, Quaternion rest)
        {
            var motion = new FpsPistolMotions.Motion { length = clip.length };
            var inverseRig = Quaternion.Inverse(rig); var inverseRest = Quaternion.Inverse(rest);
            int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length * 60));
            Quaternion previous = Quaternion.identity;
            for (int i = 0; i <= frames; i++)
            {
                float time = Mathf.Min(clip.length, i * clip.length / frames);
                Quaternion delta = rig * (Rotation(clip, time) * inverseRest) * inverseRig;
                if (i > 0 && Quaternion.Dot(previous, delta) < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w); // Keep the quaternion on one side so curves interpolate the short way.
                previous = delta;
                motion.x.AddKey(time, delta.x); motion.y.AddKey(time, delta.y); motion.z.AddKey(time, delta.z); motion.w.AddKey(time, delta.w);
            }
            foreach (var curve in new[] { motion.x, motion.y, motion.z, motion.w })
                for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0);
            return motion;
        }
    }
}
