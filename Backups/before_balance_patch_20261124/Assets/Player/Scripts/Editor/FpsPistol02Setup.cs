/* [ 코드 설명 ]
1. 원본을 보존한 SciFiPistol02_2 변형과 기존 우주복에서 추출한 권총용 팔 메시를 구성합니다.
2. 최신 Player 프리팹의 총 모델·조준 오프셋·탄창·총구 참조만 연결하며 맵과 로비는 저장하지 않습니다.
*/
using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsPistol02Setup
    {
        public const string Source = "Assets/TirgamesAssets/SciFiWorld/Shared/Weapons/Prefabs/SciFiPistol02_2.prefab";
        public const string Weapon = "Assets/Player/Prefabs/Weapons/VOID_LAST_SciFiPistol02_2.prefab";
        public const string Arm = "Assets/Player/Prefabs/Player/Visuals/FPS_PistolRightArm.asset";
        public const string Player = "Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab";
        public const string Network = "Assets/Player/Prefabs/Player/VOID_LAST_FPS_Network.prefab";
        [MenuItem("VOID LAST/Player/Configure Pistol 02")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            FpsAssetSaveGuard.Active = true;
            try
            {
                BuildWeapon();
                var root = PrefabUtility.LoadPrefabContents(Player);
                try
                {
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(Arm) == null) FpsPresentationSetup.CreatePistolArm(root, Arm);
                    var camera = root.GetComponentInChildren<Camera>(true);
                    var localRoot = camera.transform.Find("WeaponRoot");
                    var animator = root.GetComponentInChildren<Animator>(true);
                    var worldRoot = animator.GetBoneTransform(HumanBodyBones.RightHand).Find("WorldWeaponSocket");
                    var local = ReplaceModel(localRoot); var world = ReplaceModel(worldRoot);
                    var glove = localRoot.Find("FPS Right Glove"); glove.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Arm);
                    var fields = new SerializedObject(root.GetComponent<FpsWeaponView>());
                    fields.FindProperty("handPosition").vector3Value = new Vector3(0.015f, -0.025f, -0.045f);
                    fields.FindProperty("handEuler").vector3Value = new Vector3(90, 0, 0);
                    fields.FindProperty("hipPosition").vector3Value = new Vector3(0.18f, -0.10f, 0.50f);
                    float scale = fields.FindProperty("modelScale").floatValue;
                    var dot = local.Find("Scope/Dot"); Vector3 sight = localRoot.InverseTransformPoint(dot.position);
                    fields.FindProperty("aimPosition").vector3Value = new Vector3(-sight.x * scale, -sight.y * scale, 0.45f);
                    fields.FindProperty("aimEulerAngles").vector3Value = Vector3.zero;
                    fields.FindProperty("reloadOffset").vector3Value = new Vector3(0.04f, -0.045f, -0.02f);
                    fields.FindProperty("reloadEulerAngles").vector3Value = new Vector3(8, 0, 20);
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    var drop = root.GetComponent<FpsMagazineDrop>(); if (drop == null) drop = root.AddComponent<FpsMagazineDrop>();
                    Reference(drop, "localClip", local.Find("Clip")); Reference(drop, "worldClip", world.Find("Clip"));
                    var visibility = new SerializedObject(root.GetComponent<FpsLocalVisibility>());
                    Renderers(visibility.FindProperty("firstPersonRenderers"), local.GetComponentsInChildren<Renderer>(true));
                    Renderers(visibility.FindProperty("worldRenderers"), world.GetComponentsInChildren<Renderer>(true)); visibility.ApplyModifiedPropertiesWithoutUndo();
                    foreach(var mount in new[]{localRoot,worldRoot})
                    {
                        var muzzle = mount.Find("MuzzleFlash"); if (muzzle != null) muzzle.localPosition = new Vector3(0, 0.032f, 0.128f);
                        // Camera-specific forceRenderingOff must not use the GPU drawer's cached visibility.
                        var script = AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.unity.render-pipelines.core/Runtime/GPUDriven/Components/DisallowGPUDrivenRendering.cs");
                        if (script == null || script.GetClass() == null) throw new InvalidOperationException("Unity GPU rendering opt-out component missing.");
                        var optOut = mount.GetComponent(script.GetClass()) ?? mount.gameObject.AddComponent(script.GetClass());
                        var optOutFields = new SerializedObject(optOut);
                        optOutFields.FindProperty("m_applyToChildrenRecursively").boolValue = true;
                        optOutFields.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, Player);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                var network = PrefabUtility.LoadPrefabContents(Network);
                try { if (network.GetComponent<FpsReloadVisualNetwork>() == null) network.AddComponent<FpsReloadVisualNetwork>(); RepairVisibility(network); PrefabUtility.SaveAsPrefabAsset(network, Network); }
                finally { PrefabUtility.UnloadPrefabContents(network); }
                foreach(var path in new[]{FpsFlashEmitterSetup.PlayerPath,FpsFlashEmitterSetup.NetworkPath})
                {
                    if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)continue;
                    var variant=PrefabUtility.LoadPrefabContents(path);
                    try{RepairVisibility(variant);PrefabUtility.SaveAsPrefabAsset(variant,path);}
                    finally{PrefabUtility.UnloadPrefabContents(variant);}
                }
            }
            finally { FpsAssetSaveGuard.Active = false; }
        }
        static void BuildWeapon()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Player/Prefabs/Weapons")) AssetDatabase.CreateFolder("Assets/Player/Prefabs", "Weapons");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Weapon) != null) return;
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source), preview);
                root.name = "VOID_LAST_SciFiPistol02_2";
                // Source prefab's Clip is pulled out. Seat only this variant using the original FBX mount.
                var clip = root.transform.Find("Clip");
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(clip.GetComponent<MeshFilter>().sharedMesh));
                var sourceClip = fbx.transform.Find("Clip"); clip.localPosition = sourceClip.localPosition; clip.localRotation = sourceClip.localRotation;
                foreach(var c in root.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                PrefabUtility.SaveAsPrefabAsset(root, Weapon);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static Transform ReplaceModel(Transform mount)
        {
            var current = mount.Find("SciFiPistol02_2"); if (current != null) return current;
            var old = mount.Find("SciFiPistol01_2");
            var model = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Weapon), mount.gameObject.scene)).transform;
            model.SetParent(mount, false); model.name = "SciFiPistol02_2";
            if (old != null)
            {
                for (int i = old.childCount - 1; i >= 0; i--)
                    if (PrefabUtility.IsAddedGameObjectOverride(old.GetChild(i).gameObject)) old.GetChild(i).SetParent(mount, true);
                Object.DestroyImmediate(old.gameObject);
            }
            return model;
        }
        static void Reference(Object target, string field, Object value)
        { var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        static void RepairVisibility(GameObject root)
        {
            var fields=new SerializedObject(root.GetComponent<FpsLocalVisibility>());
            var local=root.GetComponentInChildren<Camera>(true).transform.Find("WeaponRoot");
            var world=root.GetComponentInChildren<Animator>(true).GetBoneTransform(HumanBodyBones.RightHand).Find("WorldWeaponSocket");
            Renderers(fields.FindProperty("firstPersonRenderers"),local.GetComponentsInChildren<Renderer>(true));
            Renderers(fields.FindProperty("worldRenderers"),world.GetComponentsInChildren<Renderer>(true));fields.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Renderers(SerializedProperty list, Renderer[] additions)
        {
            for(int i=list.arraySize-1;i>=0;i--) if(list.GetArrayElementAtIndex(i).objectReferenceValue==null)
            {int count=list.arraySize;list.DeleteArrayElementAtIndex(i);if(list.arraySize==count)list.DeleteArrayElementAtIndex(i);}
            foreach(var renderer in additions)
            {
                bool exists=false; for(int i=0;i<list.arraySize;i++) if(list.GetArrayElementAtIndex(i).objectReferenceValue==renderer)exists=true;
                if(!exists)list.GetArrayElementAtIndex(list.arraySize++).objectReferenceValue=renderer;
            }
        }
    }
}
