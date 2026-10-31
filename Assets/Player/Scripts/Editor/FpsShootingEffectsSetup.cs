/* [ 코드 설명 ] 기존 화염·광점 텍스처를 재사용해 Player 내부 효과 리소스를 만들고 FPS 프리팹의 확인된 총구에 연결합니다. 원본 권총·손·Animator·씬은 저장하지 않습니다. */
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsShootingEffectsSetup
    {
        const string TextureRoot = "Assets/Creepy_Cat/3D Scifi Kit Vol 4/Textures/Particles/";
        public const string ImpactPath = "Assets/Player/Prefabs/Effects/VOID_LAST_PistolImpact.prefab";
        [MenuItem("VOID LAST/Player/9 - Add Pistol Shot Effects")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            FpsAssetSaveGuard.Active = true;
            try
            {
                var flashMaterial = Material("PistolMuzzleFlash", "Muzzle_Flash_B.png");
                var sparkMaterial = Material("PistolImpact", "White_DotGlow.png");
                BuildImpact(sparkMaterial);
                var root = PrefabUtility.LoadPrefabContents(FpsAssetSetup.PrefabPath);
                try
                {
                    var camera = root.GetComponentInChildren<Camera>(true);
                    var local = Flash(camera.transform.Find("WeaponRoot"), flashMaterial);
                    var animator = root.GetComponentInChildren<Animator>();
                    var world = Flash(animator.GetBoneTransform(HumanBodyBones.RightHand).Find("WorldWeaponSocket"), flashMaterial);
                    var light = world.GetComponent<Light>(); if (light == null) light = world.gameObject.AddComponent<Light>();
                    light.type = LightType.Point; light.color = new Color(1, 0.65f, 0.3f); light.range = 1;
                    light.intensity = 0.12f; light.shadows = LightShadows.None; light.enabled = false;
                    var effects = root.GetComponent<FpsPistolEffects>(); if (effects == null) effects = root.AddComponent<FpsPistolEffects>();
                    var fields = new SerializedObject(effects);
                    fields.FindProperty("localFlash").objectReferenceValue = local;
                    fields.FindProperty("worldFlash").objectReferenceValue = world;
                    fields.FindProperty("muzzleLight").objectReferenceValue = light;
                    fields.FindProperty("impactPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPath).GetComponent<ParticleSystem>();
                    if (Mathf.Approximately(fields.FindProperty("flashSize").floatValue, 0.085f)) fields.FindProperty("flashSize").floatValue = 0.16f;
                    if (Mathf.Approximately(fields.FindProperty("lightIntensity").floatValue, 0.65f)) fields.FindProperty("lightIntensity").floatValue = 0.12f;
                    if (Mathf.Approximately(fields.FindProperty("lightRange").floatValue, 1.5f)) fields.FindProperty("lightRange").floatValue = 1;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    var visibility = new SerializedObject(root.GetComponent<FpsLocalVisibility>());
                    Append(visibility.FindProperty("firstPersonRenderers"), local.GetComponent<Renderer>());
                    Append(visibility.FindProperty("worldRenderers"), world.GetComponent<Renderer>());
                    visibility.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, FpsAssetSetup.PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            finally { FpsAssetSaveGuard.Active = false; }
        }
        static void Append(SerializedProperty list, Object renderer)
        {
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) return;
            int index = list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
        }
        static Material Material(string name, string texture)
        {
            string path = "Assets/Player/Materials/Effects/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + texture);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (source == null || shader == null) throw new InvalidOperationException("Existing particle texture/shader missing: " + texture);
            var material = new Material(shader) { name = name, renderQueue = (int)RenderQueue.Transparent };
            material.SetTexture("_BaseMap", source); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 2); material.SetFloat("_Cull", 0); material.SetFloat("_ZWrite", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetOverrideTag("RenderType", "Transparent");
            material.SetShaderPassEnabled("ShadowCaster", false); material.SetShaderPassEnabled("DepthOnly", false);
            AssetDatabase.CreateAsset(material, path); return material;
        }
        static ParticleSystem Particle(GameObject go, Material material)
        {
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = 0.12f; main.maxParticles = 12;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.75f, 0.35f), new GradientAlphaKey(0, 1) }); color.color = gradient;
            return ps;
        }
        static ParticleSystem Flash(Transform parent, Material material)
        {
            var existing = parent.Find("MuzzleFlash");
            if (existing != null)
            {
                if (Vector3.Distance(existing.localPosition, new Vector3(0, 0.0214f, 0.166f)) < 0.001f) existing.localPosition = new Vector3(0, 0.0214f, 0.19f);
                return existing.GetComponent<ParticleSystem>();
            }
            var go = new GameObject("MuzzleFlash"); go.transform.SetParent(parent, false);
            // Measured front-face center: (-0.000012, 0.021393, 0.161136); move just outside barrel.
            go.transform.localPosition = new Vector3(0, 0.0214f, 0.19f);
            var ps = Particle(go, material); var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0; main.startLifetime = 0.045f; main.startSize = 0.16f; main.startColor = new Color(1, 0.65f, 0.3f);
            var emission = ps.emission; emission.enabled = false; var shape = ps.shape; shape.enabled = false;
            var sheet = ps.textureSheetAnimation; sheet.enabled = true; sheet.numTilesX = sheet.numTilesY = 4; sheet.cycleCount = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, 1));
            return ps;
        }
        static void BuildImpact(Material material)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPath) != null) return;
            var go = new GameObject("VOID_LAST_PistolImpact");
            try
            {
                var ps = Particle(go, material); var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.18f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f); main.startColor = new Color(1, 0.65f, 0.3f);
                main.gravityModifier = 0.25f; main.stopAction = ParticleSystemStopAction.Destroy;
                var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 7) });
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55; shape.radius = 0.008f;
                var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.25f));
                PrefabUtility.SaveAsPrefabAsset(go, ImpactPath);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
