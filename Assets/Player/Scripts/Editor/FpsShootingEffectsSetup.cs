/* [ 코드 설명 ] 기존 총기 효과와 프로젝트의 화염·광점·연기 텍스처를 재사용해 총구 화염, 금속 스파크, 일반 벽 먼지를 조정합니다. Player 효과 에셋과 FPS 프리팹만 저장하고 원본 권총·손·씬·네트워크 프리팹은 보존합니다. */
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
        public const string DustPath = "Assets/Player/Prefabs/Effects/VOID_LAST_PistolDustImpact.prefab";
        [MenuItem("VOID LAST/Player/9 - Add Pistol Shot Effects")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            FpsAssetSaveGuard.Active = true;
            try
            {
                var flashMaterial = Material("PistolMuzzleFlash", "Muzzle_Flash_A.png");
                flashMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "Muzzle_Flash_A.png"));
                flashMaterial.SetColor("_BaseColor", new Color(1.8f, 1.8f, 1.8f, 1));
                EditorUtility.SetDirty(flashMaterial); AssetDatabase.SaveAssetIfDirty(flashMaterial);
                var sparkMaterial = Material("PistolImpact", "White_DotGlow.png");
                var dustMaterial = Material("PistolDustImpact", "CloudBlur.png");
                dustMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "CloudBlur.png"));
                dustMaterial.SetFloat("_Blend", 0); dustMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                dustMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                EditorUtility.SetDirty(dustMaterial); AssetDatabase.SaveAssetIfDirty(dustMaterial);
                BuildImpact(sparkMaterial);
                BuildDust(dustMaterial);
                var root = PrefabUtility.LoadPrefabContents(FpsAssetSetup.PrefabPath);
                try
                {
                    var camera = root.GetComponentInChildren<Camera>(true);
                    var local = Flash(camera.transform.Find("WeaponRoot"), flashMaterial);
                    var animator = root.GetComponentInChildren<Animator>();
                    var world = Flash(animator.GetBoneTransform(HumanBodyBones.RightHand).Find("WorldWeaponSocket"), flashMaterial);
                    var lightRoot = world.transform.Find("Muzzle Light");
                    if (lightRoot == null) { lightRoot = new GameObject("Muzzle Light").transform; lightRoot.SetParent(world.transform, false); }
                    var light = lightRoot.GetComponent<Light>(); if (light == null) light = lightRoot.gameObject.AddComponent<Light>();
                    var legacyLight = world.GetComponent<Light>();
                    if (legacyLight != null)
                    {
                        var legacyData = world.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                        if (legacyData != null) Object.DestroyImmediate(legacyData);
                        Object.DestroyImmediate(legacyLight);
                    }
                    light.type = LightType.Point; light.color = new Color(1, 0.7f, 0.35f); light.range = 1.25f;
                    light.intensity = 0.35f; light.shadows = LightShadows.None; light.enabled = false;
                    var effects = root.GetComponent<FpsPistolEffects>(); if (effects == null) effects = root.AddComponent<FpsPistolEffects>();
                    var fields = new SerializedObject(effects);
                    fields.FindProperty("localFlash").objectReferenceValue = local;
                    fields.FindProperty("worldFlash").objectReferenceValue = world;
                    fields.FindProperty("muzzleLight").objectReferenceValue = light;
                    fields.FindProperty("impactPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPath).GetComponent<ParticleSystem>();
                    fields.FindProperty("dustImpactPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(DustPath).GetComponent<ParticleSystem>();
                    fields.FindProperty("flashSize").floatValue = 0.2f;
                    fields.FindProperty("flashDuration").floatValue = 0.065f;
                    fields.FindProperty("lightDuration").floatValue = 0.025f;
                    fields.FindProperty("lightIntensity").floatValue = 0.35f;
                    fields.FindProperty("lightRange").floatValue = 1.25f;
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
            var go = existing != null ? existing.gameObject : new GameObject("MuzzleFlash");
            if (existing == null) go.transform.SetParent(parent, false);
            // Measured front-face center: (-0.000012, 0.021393, 0.161136); move just outside barrel.
            go.transform.localPosition = new Vector3(0, 0.0214f, 0.168f);
            var ps = go.GetComponent<ParticleSystem>(); if (ps == null) ps = Particle(go, material);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0; main.startLifetime = 0.065f; main.startSize = 0.2f; main.startColor = Color.white;
            main.maxParticles = 1;
            var emission = ps.emission; emission.enabled = false; var shape = ps.shape; shape.enabled = false;
            var sheet = ps.textureSheetAnimation; sheet.enabled = true; sheet.numTilesX = sheet.numTilesY = 2; sheet.cycleCount = 1;
            // Existing atlas A contains three flame frames; the fourth tile is empty.
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, 0.74f));
            return ps;
        }
        static void BuildImpact(Material material)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPath) != null;
            var go = exists ? PrefabUtility.LoadPrefabContents(ImpactPath) : new GameObject("VOID_LAST_PistolImpact");
            try
            {
                var ps = go.GetComponent<ParticleSystem>(); if (ps == null) ps = Particle(go, material);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.24f); main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.025f); main.startColor = new Color(1, 0.75f, 0.35f);
                main.gravityModifier = 0.65f; main.stopAction = ParticleSystemStopAction.Destroy; main.maxParticles = 12;
                var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 9) });
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 65; shape.radius = 0.008f;
                var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.25f));
                var collision = ps.collision; collision.enabled = false;
                var trails = ps.trails; trails.enabled = false;
                var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.lengthScale = 1.2f; renderer.velocityScale = 0.035f; renderer.cameraVelocityScale = 0;
                renderer.minParticleSize = 0.0015f; // A roughly one-pixel spark floor, without scaling distant bursts into explosions.
                PrefabUtility.SaveAsPrefabAsset(go, ImpactPath);
            }
            finally { if (exists) PrefabUtility.UnloadPrefabContents(go); else Object.DestroyImmediate(go); }
        }
        static void BuildDust(Material material)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DustPath) != null) return;
            var go = new GameObject("VOID_LAST_PistolDustImpact");
            try
            {
                var ps = Particle(go, material); var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.65f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.085f); main.startColor = new Color(0.65f, 0.6f, 0.52f, 0.7f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); main.gravityModifier = 0.08f;
                main.stopAction = ParticleSystemStopAction.Destroy; main.maxParticles = 6;
                var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 6) });
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 60; shape.radius = 0.015f;
                var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 2.3f));
                PrefabUtility.SaveAsPrefabAsset(go, DustPath);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
