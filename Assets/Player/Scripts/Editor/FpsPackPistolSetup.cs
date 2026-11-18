/* [ 코드 설명 ] Fps Weapons Pack의 팔·권총 모델과 Hand/Gun Animator로 1인칭 권총 리그 프리팹을 만들고 Player 프리팹에 연결합니다. 원본 팩 에셋은 수정하지 않으며(머티리얼은 URP 복사본 사용), 기존 SciFi 권총과 장갑은 삭제하지 않고 비활성화만 합니다. 여러 번 실행해도 같은 결과가 되도록 만들었습니다. */
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VoidLast.Player.Editor
{
    public static class FpsPackPistolSetup
    {
        const string Pack = "Assets/fps weapons pack/";
        const string ArmsFbx = Pack + "GunAndArmMeshes/FirstPersonArms.fbx";
        const string PistolFbx = Pack + "GunAndArmMeshes/Pistol.fbx";
        const string HandController = Pack + "GunAndArmMeshes/HandAnimator.controller";
        const string GunController = Pack + "GunAndArmMeshes/GunAnimator.controller";
        const string MaterialFolder = "Assets/Player/Materials/PackPistol";
        const string RigPrefab = "Assets/Player/Prefabs/Weapons/VOID_LAST_PackPistolRig.prefab";
        const string PlayerPrefab = "Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab";

        [MenuItem("VOID LAST/Player/Build Pack Pistol Rig (assets only)")]
        public static void BuildAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var arms = UrpCopy("gloved arms mat");
            var pistol = UrpCopy("pistol mat");
            var root = new GameObject("PackPistolRig");
            try
            {
                var hand = Place(ArmsFbx, root.transform, HandController, arms);
                var gun = Place(PistolFbx, root.transform, GunController, pistol);
                hand.name = "FirstPersonArms"; gun.name = "Pistol";
                PrefabUtility.SaveAsPrefabAsset(root, RigPrefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("Pack pistol rig prefab: " + RigPrefab);
        }

        [MenuItem("VOID LAST/Player/Apply Pack Pistol Rig To Player Prefab")]
        public static void ApplyToPlayer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (rigAsset == null) throw new InvalidOperationException("Run 'Build Pack Pistol Rig' first.");
            AssetDatabase.ImportAsset(PlayerPrefab, ImportAssetOptions.ForceUpdate); // Make sure no stale cached copy is saved back.
            var player = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var camera = player.GetComponentInChildren<Camera>(true).transform;
                var weaponRoot = camera.Find("WeaponRoot");
                var rig = camera.Find("PackPistolRig");
                if (rig == null)
                {
                    rig = ((GameObject)PrefabUtility.InstantiatePrefab(rigAsset, camera)).transform;
                    rig.name = "PackPistolRig"; rig.localPosition = Vector3.zero; rig.localRotation = Quaternion.identity; rig.localScale = Vector3.one;
                }
                var hand = rig.Find("FirstPersonArms").GetComponent<Animator>();
                var gun = rig.Find("Pistol").GetComponent<Animator>();

                // Old first-person pistol model and glove stay in the prefab but no longer render.
                foreach (string name in new[] { "SciFiPistol02_2", "FPS Right Glove" })
                { var old = weaponRoot.Find(name); if (old != null) old.gameObject.SetActive(false); }

                if (!player.TryGetComponent<FpsPackPistolRig>(out var driver)) driver = player.AddComponent<FpsPackPistolRig>();
                var so = new SerializedObject(driver);
                so.FindProperty("rigRoot").objectReferenceValue = rig.gameObject;
                so.FindProperty("handAnimator").objectReferenceValue = hand;
                so.FindProperty("gunAnimator").objectReferenceValue = gun;
                // Muzzle flash and flashlight ride the animated gun body (offsets measured from the idle pose, master-bone local units).
                var master = rig.Find("Pistol/PistolRig/master");
                var followers = so.FindProperty("followers");
                followers.arraySize = 2;
                SetFollower(followers.GetArrayElementAtIndex(0), weaponRoot.Find("MuzzleFlash"), master, new Vector3(0.000048f, 0.001574f, 0.000082f));
                SetFollower(followers.GetArrayElementAtIndex(1), weaponRoot.Find("Flashlight (1P)"), master, new Vector3(0.000048f, 0.00078f, -0.000248f));
                so.ApplyModifiedPropertiesWithoutUndo();

                // Rig renderers follow the existing "first-person camera only" rule.
                var visibility = new SerializedObject(player.GetComponent<FpsLocalVisibility>());
                var list = visibility.FindProperty("firstPersonRenderers");
                var existing = new HashSet<UnityEngine.Object>();
                for (int i = 0; i < list.arraySize; i++) existing.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (existing.Add(skin)) { list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = skin; }
                visibility.ApplyModifiedPropertiesWithoutUndo();

                // The pack reload animates its own magazine, so the old SciFi clip drop is disconnected.
                var drop = player.GetComponent<FpsMagazineDrop>();
                if (drop != null) { var d = new SerializedObject(drop); d.FindProperty("localClip").objectReferenceValue = null; d.ApplyModifiedPropertiesWithoutUndo(); }

                // The pack animation owns the 1P pose, so the earlier rotation layer is off; draw (0.667s) needs the longest allowed equip time.
                var weapon = new SerializedObject(player.GetComponent<FpsWeaponView>());
                weapon.FindProperty("motions").objectReferenceValue = null;
                weapon.FindProperty("equipDuration").floatValue = 0.4f;
                weapon.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            Debug.Log("Pack pistol rig applied to " + PlayerPrefab);
        }

        const string ArmsFbx2 = ArmsFbx;
        // Pistol grip in the pack's hip idle, measured in FPS-camera space: centroid and the grip's downward axis.
        static readonly Vector3 GripCentre = new Vector3(0.154f, -0.186f, 0.314f);
        static readonly Vector3 GripDown = new Vector3(0, -0.891f, -0.454f);
        static readonly Vector3 ArmsPlacement = new Vector3(0, -1.761f, 0.188f);

        /// <summary>4~6번 파밍 아이템(주사기)을 권총과 같은 팩 장갑으로 잡게 한다. 팩 권총 대기 자세의 오른손 그립에 주사기를 끼운다.</summary>
        [MenuItem("VOID LAST/Player/Apply Pack Hands To Syringe")]
        public static void ApplySyringeHands()
        {
            EditPlayer(player =>
            {
                var camera = player.GetComponentInChildren<Camera>(true).transform;
                var view = player.GetComponent<FpsItemView>();
                var itemRoot = camera.Find("ItemRoot");
                var arms = BuildArms(player, camera, "ItemArms (Syringe)");
                HideOldGlove(itemRoot.Find("FPS Right Glove"));

                // Syringe held in the grip: needle down through the fist, pump cap just above the thumb.
                const float scale = 0.9f;
                Quaternion held = Quaternion.LookRotation(GripDown.normalized, Vector3.ProjectOnPlane(Vector3.forward, GripDown).normalized);
                Vector3 heldPosition = GripCentre - held * (new Vector3(0, 0, -0.035f) * scale);
                var itemFields = new SerializedObject(view);
                itemFields.FindProperty("modelScale").floatValue = scale;
                itemFields.FindProperty("heldPosition").vector3Value = heldPosition;
                itemFields.FindProperty("heldEuler").vector3Value = held.eulerAngles;
                // Self: wrist turns the needle back toward the body. Other: needle forward, arm reaching out.
                itemFields.FindProperty("usePosition").vector3Value = heldPosition + SelfUseOffset;
                itemFields.FindProperty("useEuler").vector3Value = (Quaternion.Euler(SelfUseTilt) * held).eulerAngles;
                itemFields.FindProperty("otherUsePosition").vector3Value = heldPosition + OtherUseOffset;
                itemFields.FindProperty("otherUseEuler").vector3Value = (Quaternion.Euler(OtherUseTilt) * held).eulerAngles;
                itemFields.ApplyModifiedPropertiesWithoutUndo();

                var handFields = HandsFor(player, itemRoot, arms, held, heldPosition);
                var bones = handFields.FindProperty("thumbBones"); var press = handFields.FindProperty("thumbPressEuler");
                string[] thumb = { "Thumb1.R", "Thumb2.R", "Thumb3.R" }; Vector3[] bend = { new Vector3(0, 35, 0), new Vector3(0, 30, 0), new Vector3(0, 30, 0) };
                bones.arraySize = press.arraySize = thumb.Length;
                for (int i = 0; i < thumb.Length; i++) { bones.GetArrayElementAtIndex(i).objectReferenceValue = FindChild(arms, thumb[i]); press.GetArrayElementAtIndex(i).vector3Value = bend[i]; }
                handFields.ApplyModifiedPropertiesWithoutUndo();
            });
            Debug.Log("Syringe now held by the pack gloves");
        }

        /// <summary>3번 고유 아이템(구급상자·빛 방출기)도 팩 장갑으로 잡게 한다. 아이템의 화면 위치는 그대로 두고, 손잡이가 권총 그립 자리에 오도록 팔을 맞춘다.</summary>
        [MenuItem("VOID LAST/Player/Apply Pack Hands To Slot 3 Items")]
        public static void ApplySlot3Hands()
        {
            EditPlayer(player =>
            {
                var camera = player.GetComponentInChildren<Camera>(true).transform;
                Vector3 down = GripDown.normalized;

                // Medical kit: the top carry handle runs through the fist (kit +X along the grip); the hand comes in from the right side.
                var kitRoot = camera.Find("MedicalKitRoot");
                Vector3 kitForward = Vector3.ProjectOnPlane(Vector3.forward, down).normalized;
                Quaternion kitGrip = Quaternion.LookRotation(kitForward, Vector3.Cross(kitForward, down));
                var kitArms = BuildArms(player, camera, "ItemArms (MedicalKit)");
                HideOldGlove(kitRoot.Find("FPS Right Glove"));
                HandsFor(player, kitRoot, kitArms, kitGrip, GripCentre - kitGrip * new Vector3(0, -0.015f, 0)).ApplyModifiedPropertiesWithoutUndo();

                // Flash emitter: held like a torch, the lower body in the fist and the lit head standing above it.
                var emitterRoot = camera.Find("FlashEmitterRoot");
                Vector3 up = -down;
                Quaternion emitterGrip = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up).normalized, up);
                var emitterArms = BuildArms(player, camera, "ItemArms (FlashEmitter)");
                HideOldGlove(emitterRoot.Find("FPS Right Glove"));
                HandsFor(player, emitterRoot, emitterArms, emitterGrip, GripCentre - emitterGrip * new Vector3(0, -0.035f, 0)).ApplyModifiedPropertiesWithoutUndo();
            });
            Debug.Log("Slot 3 items now held by the pack gloves");
        }
        // Chosen from Play renders: self = pressed down toward the body (hand stays on screen); other = needle forward, arm reaching out.
        static readonly Vector3 SelfUseOffset = new Vector3(-0.04f, -0.05f, -0.03f);
        static readonly Vector3 SelfUseTilt = new Vector3(20, 0, 0);
        static readonly Vector3 OtherUseOffset = new Vector3(-0.02f, 0.02f, 0.1f);
        static readonly Vector3 OtherUseTilt = new Vector3(-60, 0, 0);

        static void EditPlayer(Action<GameObject> edit)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            AssetDatabase.ImportAsset(PlayerPrefab, ImportAssetOptions.ForceUpdate); // No stale cached copy is saved back.
            var player = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try { edit(player); PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefab); }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        // A pack-arms copy frozen in the pistol hip-idle grip, right hand only, listed as first-person-only.
        static Transform BuildArms(GameObject player, Transform camera, string name)
        {
            var arms = camera.Find(name);
            if (arms == null)
            {
                arms = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArmsFbx2), camera)).transform;
                arms.name = name;
            }
            if (arms.TryGetComponent<Animator>(out var animator)) animator.enabled = false; // The baked grip pose must not be reset.
            foreach (var item in AssetDatabase.LoadAllAssetsAtPath(ArmsFbx2))
                if (item is AnimationClip clip && clip.name == "PistolHipFire") clip.SampleAnimation(arms.gameObject, 0);
            arms.localPosition = ArmsPlacement; arms.localRotation = Quaternion.identity; arms.localScale = new Vector3(1, 1, 0.6f);
            var leftShoulder = FindChild(arms, "Sholder.L"); if (leftShoulder != null) leftShoulder.localScale = Vector3.one * 0.001f; // One-handed item: hide the support hand.
            var glove = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/gloved arms mat URP.mat");
            var visibility = new SerializedObject(player.GetComponent<FpsLocalVisibility>());
            var list = visibility.FindProperty("firstPersonRenderers");
            foreach (var skin in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.sharedMaterial = glove; skin.updateWhenOffscreen = true;
                bool present = false;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == skin) present = true;
                if (!present) { list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = skin; }
            }
            visibility.ApplyModifiedPropertiesWithoutUndo();
            arms.gameObject.SetActive(false);
            return arms;
        }

        static void HideOldGlove(Transform glove) { if (glove != null) glove.gameObject.SetActive(false); }

        // gripRotation/gripPosition: where the item sits inside the frozen pistol grip while the arms are at their camera placement.
        // The arms keep that offset from the item, so wherever the item's view holds it, the hand stays on its handle.
        static SerializedObject HandsFor(GameObject player, Transform anchor, Transform arms, Quaternion gripRotation, Vector3 gripPosition)
        {
            FpsItemHands hands = null;
            foreach (var existing in player.GetComponents<FpsItemHands>())
                if (new SerializedObject(existing).FindProperty("anchor").objectReferenceValue == anchor) hands = existing;
            if (hands == null) hands = player.AddComponent<FpsItemHands>();
            var fields = new SerializedObject(hands);
            fields.FindProperty("anchor").objectReferenceValue = anchor;
            fields.FindProperty("arms").objectReferenceValue = arms;
            Quaternion inverse = Quaternion.Inverse(gripRotation);
            fields.FindProperty("offsetPosition").vector3Value = inverse * (ArmsPlacement - gripPosition);
            fields.FindProperty("offsetEuler").vector3Value = inverse.eulerAngles;
            return fields;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        const string WorldMesh = "Assets/Player/Prefabs/Weapons/PackPistol_World.asset";

        /// <summary>3인칭(다른 플레이어가 보는) 손 소켓의 권총도 팩 권총으로 바꾼다. 기존 SciFi 총과 같은 총구 위치에 맞춘다.</summary>
        [MenuItem("VOID LAST/Player/Apply Pack Pistol To World (3P) Socket")]
        public static void ApplyWorldPistol()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var mesh = BakeWorldMesh();
            var material = UrpCopy("pistol mat");
            AssetDatabase.ImportAsset(PlayerPrefab, ImportAssetOptions.ForceUpdate);
            var player = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var animator = player.GetComponentInChildren<Animator>(true);
                var socket = animator.GetBoneTransform(HumanBodyBones.RightHand).Find("WorldWeaponSocket");
                var model = socket.Find("PackPistol (3P)");
                if (model == null) { model = new GameObject("PackPistol (3P)").transform; model.SetParent(socket, false); }
                var muzzle = socket.Find("MuzzleFlash");
                model.localPosition = muzzle != null ? muzzle.localPosition : new Vector3(0, 0.032f, 0.128f); // Mesh origin is its muzzle.
                model.localRotation = Quaternion.identity; model.localScale = Vector3.one;
                if (!model.TryGetComponent<MeshFilter>(out var filter)) filter = model.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                if (!model.TryGetComponent<MeshRenderer>(out var renderer)) renderer = model.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var old = socket.Find("SciFiPistol02_2"); if (old != null) old.gameObject.SetActive(false);
                var light = socket.Find("Flashlight (3P)"); if (light != null) light.localPosition = new Vector3(0, -0.005f, 0.085f); // Under the shorter pack barrel.

                var visibility = new SerializedObject(player.GetComponent<FpsLocalVisibility>());
                var list = visibility.FindProperty("worldRenderers");
                bool present = false;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) present = true;
                if (!present) { list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = renderer; }
                visibility.ApplyModifiedPropertiesWithoutUndo();

                var drop = player.GetComponent<FpsMagazineDrop>();
                if (drop != null) { var d = new SerializedObject(drop); d.FindProperty("worldClip").objectReferenceValue = null; d.ApplyModifiedPropertiesWithoutUndo(); }
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            Debug.Log("World pistol replaced with pack pistol");
        }

        // Static copy of the pack pistol in its idle pose: barrel +Z, up +Y, origin at the muzzle face centre.
        static Mesh BakeWorldMesh()
        {
            var go = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PistolFbx));
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
                foreach (var item in AssetDatabase.LoadAllAssetsAtPath(PistolFbx))
                    if (item is AnimationClip clip && clip.name == "PistolHipFire") clip.SampleAnimation(go, 0);
                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                var baked = new Mesh(); skin.BakeMesh(baked, true);
                var toRoot = go.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                var normalMatrix = toRoot.inverse.transpose;
                var vertices = baked.vertices; var normals = baked.normals; var tangents = baked.tangents;
                float front = float.MinValue;
                for (int i = 0; i < vertices.Length; i++) { vertices[i] = toRoot.MultiplyPoint3x4(vertices[i]); front = Mathf.Max(front, vertices[i].z); }
                Vector3 muzzle = Vector3.zero; int count = 0;
                foreach (var v in vertices) if (v.z > front - 0.006f) { muzzle += v; count++; }
                muzzle /= Mathf.Max(1, count);
                for (int i = 0; i < vertices.Length; i++) vertices[i] -= muzzle;
                for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                for (int i = 0; i < tangents.Length; i++) { var t = toRoot.MultiplyVector(tangents[i]).normalized; tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w); }
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WorldMesh);
                bool create = mesh == null;
                if (create) mesh = new Mesh { name = "PackPistol_World" };
                mesh.Clear();
                mesh.vertices = vertices; mesh.normals = normals; mesh.tangents = tangents; mesh.uv = baked.uv;
                mesh.subMeshCount = baked.subMeshCount;
                for (int i = 0; i < baked.subMeshCount; i++) mesh.SetTriangles(baked.GetTriangles(i), i);
                mesh.RecalculateBounds();
                if (create) AssetDatabase.CreateAsset(mesh, WorldMesh); else EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssets();
                return mesh;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void SetFollower(SerializedProperty item, Transform target, Transform bone, Vector3 position)
        {
            item.FindPropertyRelative("target").objectReferenceValue = target;
            item.FindPropertyRelative("bone").objectReferenceValue = bone;
            item.FindPropertyRelative("localPosition").vector3Value = position;
            item.FindPropertyRelative("localEuler").vector3Value = new Vector3(270.02f, 180, 0); // Camera-aligned at idle.
        }

        static GameObject Place(string fbx, Transform parent, string controllerPath, Material material)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), parent);
            go.transform.localPosition = new Vector3(0, -1.761f, 0.188f); go.transform.localRotation = Quaternion.identity; go.transform.localScale = new Vector3(1, 1, 0.6f); // Same offset as the pack's own FpsController.
            if (!go.TryGetComponent<Animator>(out var animator)) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            animator.applyRootMotion = false;
            foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.sharedMaterials = new[] { material };
            return go;
        }

        // The pack's materials use the built-in Standard shader, which this URP project cannot render.
        static Material UrpCopy(string sourceName)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Player/Materials", "PackPistol");
            string path = MaterialFolder + "/" + sourceName + " URP.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Material>(Pack + "materials/" + sourceName + ".mat");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = sourceName + " URP" };
            material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            if (source.GetTexture("_BumpMap") != null) { material.SetTexture("_BumpMap", source.GetTexture("_BumpMap")); material.EnableKeyword("_NORMALMAP"); }
            if (source.GetTexture("_MetallicGlossMap") != null) { material.SetTexture("_MetallicGlossMap", source.GetTexture("_MetallicGlossMap")); material.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            if (source.GetTexture("_OcclusionMap") != null) material.SetTexture("_OcclusionMap", source.GetTexture("_OcclusionMap"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
