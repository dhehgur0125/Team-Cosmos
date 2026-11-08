/*
[ 코드 설명 ]
1. 원본 URP Torch를 빛 방출기 표시 프리팹에 연결하고 테스트용 FPS 변형의 손·발광부 참조를 구성합니다.
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
        static void BuildModel()
        {
            // Resolve by GUID so moving the original URP asset does not break this setup.
            var torch = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("c290ea15c38597b4893a844f3a1411da"));
            if (torch == null) throw new InvalidOperationException("URP Torch source is missing.");
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(ModelPath) : new GameObject("VOID_LAST_FlashEmitter");
            try
            {
                foreach (string name in new[] { "Capsule body", "Emitter rim", "Emissive lens" })
                {
                    var primitive = root.transform.Find(name);
                    if (primitive != null) Object.DestroyImmediate(primitive.gameObject);
                }
                var model = root.transform.Find("Torch");
                if (model == null)
                {
                    model = ((GameObject)PrefabUtility.InstantiatePrefab(torch, root.scene)).transform;
                    model.SetParent(root.transform, false); model.name = "Torch";
                }
                model.localPosition = new Vector3(0, -0.08f, 0); model.localRotation = Quaternion.identity; model.localScale = Vector3.one;
                // Keep the original gameplay origin; only the visible flash moves to the long upper emitter.
                Child(root.transform, "EmissionPoint").localPosition = new Vector3(0, 0, 0.185f);
                Child(root.transform, "VisualEmissionPoint").localPosition = new Vector3(0, 0.15f, 0.045f);
                PrefabUtility.SaveAsPrefabAsset(root, ModelPath);
            }
            finally { if (existing) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
        }
        /// <summary>
        /// 고유 아이템 선택용: 원래 대원 프리팹(VOID_LAST_FPS)에 빛 방출기(스턴)를 함께 넣고, 네트워크 프리팹에 동기화 컴포넌트를 붙인다.
        /// 구급상자는 그대로 두며, 3번 칸에서 무엇을 꺼낼지는 대기실에서 고른 고유 아이템(FpsUniqueItem)이 정한다.
        /// </summary>
        [MenuItem("VOID LAST/Player/Add Flash Emitter To Main Players (Unique Items)")]
        public static void ApplyToMainPlayers()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            FpsAssetSaveGuard.Active = true;
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) BuildModel();
                ConfigurePlayer(FpsAssetSetup.PrefabPath, FpsAssetSetup.PrefabPath, false, false);
                var network = PrefabUtility.LoadPrefabContents(FpsMedicalKitSetup.NetworkPath);
                try
                {
                    if (network.GetComponent<FpsFlashEmitterNetwork>() == null) network.AddComponent<FpsFlashEmitterNetwork>();
                    PrefabUtility.SaveAsPrefabAsset(network, FpsMedicalKitSetup.NetworkPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(network); }
                AssetDatabase.SaveAssets();
                Debug.Log("[고유 아이템] VOID_LAST_FPS에 빛 방출기, VOID_LAST_FPS_Network에 FpsFlashEmitterNetwork를 넣었습니다.");
            }
            finally { FpsAssetSaveGuard.Active = false; }
        }
        static void ConfigurePlayer(string path, string sourcePath, bool online, bool testVariant = true)
        {
            var preview = EditorSceneManager.NewPreviewScene(); GameObject root = null; bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            try
            {
                root = existing ? PrefabUtility.LoadPrefabContents(path) : (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), preview);
                if (testVariant)
                {
                    root.name = online ? "VOID_LAST_FPS_FlashEmitter_Network_Test" : "VOID_LAST_FPS_FlashEmitter_Test";
                    root.GetComponent<FpsMedicalKit>().enabled = false; // Only this test variant; original healer prefab stays intact.
                }
                var emitter = root.GetComponent<FpsFlashEmitter>(); if (emitter == null) emitter = root.AddComponent<FpsFlashEmitter>();
                var view = root.GetComponent<FpsFlashEmitterView>(); if (view == null) view = root.AddComponent<FpsFlashEmitterView>();
                if (online && root.GetComponent<FpsFlashEmitterNetwork>() == null) root.AddComponent<FpsFlashEmitterNetwork>();
                if (testVariant) { var equipment = new SerializedObject(root.GetComponent<FpsEquipment>()); equipment.FindProperty("selectedSlot").intValue = 3; equipment.ApplyModifiedPropertiesWithoutUndo(); }
                var camera = root.GetComponentInChildren<Camera>(true); var local = Child(camera.transform, "FlashEmitterRoot");
                var localModel = Model(local);
                if (local.Find("FPS Right Glove") == null)
                {
                    var glove = Object.Instantiate(camera.transform.Find("WeaponRoot/FPS Right Glove").gameObject, local).transform;
                    glove.name = "FPS Right Glove"; glove.localPosition = new Vector3(0, -0.025f, -0.055f); glove.localEulerAngles = new Vector3(90, 0, 0); glove.localScale = Vector3.one * 0.8f;
                    var clean = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Player/Prefabs/Player/Visuals/FPS_MedicalGlove.asset");
                    if (clean != null) glove.GetComponent<MeshFilter>().sharedMesh = clean;
                }
                var grip = local.Find("FPS Right Glove");
                grip.localPosition = new Vector3(0.008f, -0.015f, -0.012f); grip.localEulerAngles = new Vector3(90, 0, 0);
                var hand = root.GetComponentInChildren<Animator>(true).GetBoneTransform(HumanBodyBones.RightHand);
                var world = Child(hand, "WorldFlashEmitterSocket"); var existingSocket = hand.Find("WorldWeaponSocket");
                world.localPosition = existingSocket.localPosition; world.localRotation = existingSocket.localRotation;
                var worldModel = Model(world);
                var localMuzzle = localModel.Find("VisualEmissionPoint"); var worldMuzzle = worldModel.Find("VisualEmissionPoint");
                MoveEffectChildren(localModel.Find("EmissionPoint"), localMuzzle);
                MoveEffectChildren(worldModel.Find("EmissionPoint"), worldMuzzle);
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
                Reference(view, "stunOrigin", localModel.Find("EmissionPoint"));
                Reference(view, "localLens", localModel.Find("Torch").GetComponent<Renderer>()); Reference(view, "worldLens", worldModel.Find("Torch").GetComponent<Renderer>());
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
        static void MoveEffectChildren(Transform previous, Transform destination)
        {
            while (previous.childCount > 0) previous.GetChild(0).SetParent(destination, false);
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
            for (int i = list.arraySize - 1; i >= 0; i--)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    int before = list.arraySize; list.DeleteArrayElementAtIndex(i);
                    if (list.arraySize == before) list.DeleteArrayElementAtIndex(i);
                }
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
