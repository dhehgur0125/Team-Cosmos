/*
[ 코드 설명 ]
1. 기본 도형으로 빛 방출기 모델과 장비 테스트용 FPS 변형 프리팹을 만듭니다.
2. 기존 의료 키트·원본 플레이어·맵은 보존하고 Monster_test 복사본에만 테스트용 플레이어를 배치합니다.
*/
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsFlashEmitterSetup
    {
        public const string ModelPath = "Assets/Player/Prefabs/Item/VOID_LAST_FlashEmitter.prefab";
        public const string PlayerPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS_FlashEmitter_Test.prefab";
        public const string NetworkPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS_FlashEmitter_Network_Test.prefab";
        public const string ScenePath = "Assets/Player/FlashEmitter_test.unity";
        public const string TestMonsterPath = "Assets/Player/Validation/FlashEmitter_NetworkMonster.prefab";
        [MenuItem("VOID LAST/Player/Configure Flash Emitter Test")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            FpsAssetSaveGuard.Active = true;
            try
            {
                BuildModel(); ConfigurePlayer(PlayerPath, FpsAssetSetup.PrefabPath, false);
                ConfigurePlayer(NetworkPath, FpsMedicalKitSetup.NetworkPath, true);
                BuildTestMonster();
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) CreateScene();
            }
            finally { FpsAssetSaveGuard.Active = false; }
        }
        static void BuildTestMonster()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TestMonsterPath) != null) return;
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Monster/Prefabs/VOID_LAST_EyedLargeMonster.prefab"), preview);
                root.name = "FlashEmitter_NetworkMonster";
                root.AddComponent<NetworkObject>(); root.AddComponent<NetworkTransform>(); root.AddComponent<MonsterNetworkSync>();
                PrefabUtility.SaveAsPrefabAsset(root, TestMonsterPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static Material Material(string name, Color color, bool glowing = false)
        {
            string path = "Assets/Player/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path); if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            material.SetFloat("_Metallic", glowing ? 0 : 0.75f); material.SetFloat("_Smoothness", 0.5f);
            if (glowing) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 0.08f); }
            AssetDatabase.CreateAsset(material, path); return material;
        }
        static void BuildModel()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) != null)
            {
                var existing = PrefabUtility.LoadPrefabContents(ModelPath);
                try
                {
                    var lensTransform = existing.transform.Find("Emissive lens");
                    lensTransform.localPosition = new Vector3(0, 0, 0.145f); lensTransform.localScale = new Vector3(0.1f, 0.1f, 0.07f);
                    existing.transform.Find("EmissionPoint").localPosition = new Vector3(0, 0, 0.185f);
                    PrefabUtility.SaveAsPrefabAsset(existing, ModelPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(existing); }
                return;
            }
            var body = Material("FlashEmitter_Body", new Color(0.09f, 0.11f, 0.13f));
            var lens = Material("FlashEmitter_Lens", new Color(0.65f, 0.9f, 1), true);
            var root = new GameObject("VOID_LAST_FlashEmitter");
            try
            {
                Primitive(root.transform, "Capsule body", PrimitiveType.Capsule, Vector3.zero, new Vector3(0.08f, 0.12f, 0.08f), new Vector3(90, 0, 0), body);
                Primitive(root.transform, "Emitter rim", PrimitiveType.Cylinder, new Vector3(0, 0, 0.115f), new Vector3(0.086f, 0.012f, 0.086f), new Vector3(90, 0, 0), body);
                Primitive(root.transform, "Emissive lens", PrimitiveType.Sphere, new Vector3(0, 0, 0.145f), new Vector3(0.1f, 0.1f, 0.07f), Vector3.zero, lens);
                var muzzle = new GameObject("EmissionPoint").transform; muzzle.SetParent(root.transform, false); muzzle.localPosition = new Vector3(0, 0, 0.185f);
                PrefabUtility.SaveAsPrefabAsset(root, ModelPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        static void Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = material;
        }
        static void ConfigurePlayer(string path, string sourcePath, bool online)
        {
            var preview = EditorSceneManager.NewPreviewScene(); GameObject root = null; bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            try
            {
                root = existing ? PrefabUtility.LoadPrefabContents(path) : (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), preview);
                root.name = online ? "VOID_LAST_FPS_FlashEmitter_Network_Test" : "VOID_LAST_FPS_FlashEmitter_Test";
                root.GetComponent<FpsMedicalKit>().enabled = false; // Only this test variant; original healer prefab stays intact.
                var emitter = root.GetComponent<FpsFlashEmitter>(); if (emitter == null) emitter = root.AddComponent<FpsFlashEmitter>();
                var view = root.GetComponent<FpsFlashEmitterView>(); if (view == null) view = root.AddComponent<FpsFlashEmitterView>();
                if (online && root.GetComponent<FpsFlashEmitterNetwork>() == null) root.AddComponent<FpsFlashEmitterNetwork>();
                var equipment = new SerializedObject(root.GetComponent<FpsEquipment>()); equipment.FindProperty("selectedSlot").intValue = 3; equipment.ApplyModifiedPropertiesWithoutUndo();
                var camera = root.GetComponentInChildren<Camera>(true); var local = Child(camera.transform, "FlashEmitterRoot");
                var localModel = Model(local);
                if (local.Find("FPS Right Glove") == null)
                {
                    var glove = Object.Instantiate(camera.transform.Find("WeaponRoot/FPS Right Glove").gameObject, local).transform;
                    glove.name = "FPS Right Glove"; glove.localPosition = new Vector3(0, -0.025f, -0.055f); glove.localEulerAngles = new Vector3(90, 0, 0); glove.localScale = Vector3.one * 0.8f;
                    var clean = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Player/Prefabs/Player/Visuals/FPS_MedicalGlove.asset");
                    if (clean != null) glove.GetComponent<MeshFilter>().sharedMesh = clean;
                }
                var hand = root.GetComponentInChildren<Animator>(true).GetBoneTransform(HumanBodyBones.RightHand);
                var world = Child(hand, "WorldFlashEmitterSocket"); var existingSocket = hand.Find("WorldWeaponSocket");
                world.localPosition = existingSocket.localPosition; world.localRotation = existingSocket.localRotation;
                var worldModel = Model(world);
                var localMuzzle = localModel.Find("EmissionPoint"); var worldMuzzle = worldModel.Find("EmissionPoint");
                var localParticles = Particles(localMuzzle); var worldParticles = Particles(worldMuzzle);
                var lightRoot = Child(worldMuzzle, "Flash Light"); var light = lightRoot.GetComponent<Light>(); if (light == null) light = lightRoot.gameObject.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(0.8f, 0.92f, 1); light.range = 4; light.intensity = 0; light.shadows = LightShadows.None; light.enabled = false;
                var hud = root.transform.Find("FPS HUD"); var gaugeRoot = hud.Find("Flash Emitter Charge");
                if (gaugeRoot == null) { gaugeRoot = new GameObject("Flash Emitter Charge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform; gaugeRoot.SetParent(hud, false); }
                var gauge = gaugeRoot.GetComponent<Image>(); gauge.raycastTarget = false; gauge.enabled = false;
                var rect = gauge.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0, 0.5f); rect.anchoredPosition = new Vector2(-70, -25); rect.sizeDelta = new Vector2(140, 4);
                Reference(emitter, "view", view); Reference(emitter, "viewCamera", camera);
                Reference(view, "emitter", emitter); Reference(view, "localCamera", camera); Reference(view, "localRoot", local); Reference(view, "worldRoot", world);
                Reference(view, "localMuzzle", localMuzzle); Reference(view, "worldMuzzle", worldMuzzle);
                Reference(view, "localLens", localModel.Find("Emissive lens").GetComponent<Renderer>()); Reference(view, "worldLens", worldModel.Find("Emissive lens").GetComponent<Renderer>());
                Reference(view, "localFlash", localParticles); Reference(view, "worldFlash", worldParticles); Reference(view, "flashLight", light); Reference(view, "chargeGauge", gauge);
                var visibility = new SerializedObject(root.GetComponent<FpsLocalVisibility>());
                Append(visibility.FindProperty("firstPersonRenderers"), local.GetComponentsInChildren<Renderer>(true)); Append(visibility.FindProperty("worldRenderers"), world.GetComponentsInChildren<Renderer>(true)); visibility.ApplyModifiedPropertiesWithoutUndo();
                local.gameObject.SetActive(false); world.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { if (existing && root != null) PrefabUtility.UnloadPrefabContents(root); EditorSceneManager.ClosePreviewScene(preview); }
        }
        static Transform Model(Transform parent)
        {
            var model = parent.Find("VOID_LAST_FlashEmitter"); if (model != null) return model;
            model = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), parent.gameObject.scene)).transform; model.SetParent(parent, false); return model;
        }
        static ParticleSystem Particles(Transform parent)
        {
            var child = Child(parent, "Flash particles"); var particles = child.GetComponent<ParticleSystem>();
            if (particles == null) particles = child.gameObject.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.loop = false; main.playOnAwake = false; main.duration = 0.2f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f); main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.1f); main.startColor = new Color(0.8f, 0.95f, 1); main.maxParticles = 12; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission; emission.enabled = false; var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.045f;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); color.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Player/Materials/Effects/PistolImpact.mat"); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return particles;
        }
        static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name); if (child != null) return child;
            child = new GameObject(name).transform; child.SetParent(parent, false); return child;
        }
        static void Reference(Object target, string name, Object value) { var fields = new SerializedObject(target); fields.FindProperty(name).objectReferenceValue = value; fields.ApplyModifiedPropertiesWithoutUndo(); }
        static void Append(SerializedProperty list, Renderer[] renderers)
        {
            foreach (var renderer in renderers)
            {
                bool found = false; for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) found = true;
                if (!found) list.GetArrayElementAtIndex(list.arraySize++).objectReferenceValue = renderer;
            }
        }
        static void CreateScene()
        {
            if (!AssetDatabase.CopyAsset("Assets/Monster/Scenes/Monster_test.unity", ScenePath)) throw new InvalidOperationException("Could not copy existing Monster_test.");
            var previous = SceneManager.GetActiveScene(); var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var oldPlayer = root.GetComponentInChildren<FpsEquipment>(true); if (oldPlayer == null) continue;
                    var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), scene);
                    player.transform.SetPositionAndRotation(oldPlayer.transform.position, oldPlayer.transform.rotation);
                    Object.DestroyImmediate(oldPlayer.gameObject);
                }
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
        }
    }
}
