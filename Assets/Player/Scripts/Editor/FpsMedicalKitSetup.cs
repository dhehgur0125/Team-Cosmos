/* [ 코드 설명 ] 원본 구급상자와 FPS 장갑을 재사용해 의료 키트 표시·횟수 UI를 기존 Player 프리팹에 추가합니다. 네트워크 변형에는 기존 Netcode 연결 컴포넌트만 추가하며 씬과 원본 에셋은 저장하지 않습니다. */
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsMedicalKitSetup
    {
        const string ModelPath = "Assets/Player/Prefabs/Item/SciFiMedKit01.prefab";
        public const string NetworkPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS_Network.prefab";
        [MenuItem("VOID LAST/Player/Configure Medical Kit")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null) throw new InvalidOperationException("Medical kit source prefab missing: " + ModelPath);
            FpsAssetSaveGuard.Active = true;
            try
            {
                var root = PrefabUtility.LoadPrefabContents(FpsAssetSetup.PrefabPath);
                try
                {
                    var kit = root.GetComponent<FpsMedicalKit>() ?? root.AddComponent<FpsMedicalKit>();
                    var view = root.GetComponent<FpsMedicalKitView>() ?? root.AddComponent<FpsMedicalKitView>();
                    var camera = root.GetComponentInChildren<Camera>(true);
                    var local = Child(camera.transform, "MedicalKitRoot");
                    Model(local, source);
                    if (local.Find("FPS Right Glove") == null)
                    {
                        var glove = Object.Instantiate(camera.transform.Find("ItemRoot/FPS Right Glove").gameObject, local).transform;
                        glove.name = "FPS Right Glove";
                    }
                    var grip = local.Find("FPS Right Glove");
                    grip.localPosition = new Vector3(0, -0.025f, 0.025f); grip.localEulerAngles = new Vector3(90, -65, 0); grip.localScale = Vector3.one * 0.9f;
                    grip.GetComponent<MeshFilter>().sharedMesh = MedicalGlove();
                    local.localPosition = new Vector3(0.34f, -0.16f, 0.45f);
                    local.localEulerAngles = new Vector3(-10, -20, 0);
                    var animator = root.GetComponentInChildren<Animator>(true);
                    var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var world = Child(hand, "WorldMedicalKitSocket");
                    var existingSocket = hand.Find("WorldItemSocket");
                    if (existingSocket != null) { world.localPosition = existingSocket.localPosition; world.localRotation = existingSocket.localRotation; }
                    Model(world, source);
                    var hud = root.transform.Find("FPS HUD");
                    var textRoot = hud.Find("Medical Kit Uses");
                    if (textRoot == null) textRoot = new GameObject("Medical Kit Uses", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).transform;
                    textRoot.SetParent(hud, false);
                    var label = textRoot.GetComponent<Text>();
                    label.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Map/Fonts/NotoSansKR-Regular.otf");
                    label.fontSize = 24; label.color = new Color(0.7f, 1, 0.85f); label.alignment = TextAnchor.LowerRight; label.raycastTarget = false;
                    label.text = "의료 키트 3 / 3"; label.enabled = false;
                    var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
                    rect.anchoredPosition = new Vector2(-30, 35); rect.sizeDelta = new Vector2(360, 85);
                    Reference(kit, "view", view); Reference(view, "medicalKit", kit); Reference(view, "localRoot", local); Reference(view, "worldRoot", world); Reference(view, "usesLabel", label);
                    var fields = new SerializedObject(view); fields.FindProperty("heldPosition").vector3Value = local.localPosition;
                    fields.FindProperty("selfOffset").vector3Value = new Vector3(-0.02f, 0.02f, -0.06f);
                    fields.FindProperty("otherOffset").vector3Value = new Vector3(0.045f, 0.02f, 0.07f); fields.ApplyModifiedPropertiesWithoutUndo();
                    var visibility = new SerializedObject(root.GetComponent<FpsLocalVisibility>());
                    Append(visibility.FindProperty("firstPersonRenderers"), local.GetComponentsInChildren<Renderer>(true));
                    Append(visibility.FindProperty("worldRenderers"), world.GetComponentsInChildren<Renderer>(true));
                    visibility.ApplyModifiedPropertiesWithoutUndo();
                    local.gameObject.SetActive(false); world.gameObject.SetActive(false);
                    PrefabUtility.SaveAsPrefabAsset(root, FpsAssetSetup.PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                var network = PrefabUtility.LoadPrefabContents(NetworkPath);
                try
                {
                    if (network.GetComponent<FpsMedicalKitNetwork>() == null) network.AddComponent<FpsMedicalKitNetwork>();
                    PrefabUtility.SaveAsPrefabAsset(network, NetworkPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(network); }
            }
            finally { FpsAssetSaveGuard.Active = false; }
        }
        static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name); if (child != null) return child;
            child = new GameObject(name).transform; child.SetParent(parent, false); return child;
        }
        static Mesh MedicalGlove()
        {
            const string path = "Assets/Player/Prefabs/Player/Visuals/FPS_MedicalGlove.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (existing != null) return existing;
            // The existing baked glove includes detached sleeve fragments below y=-0.24, separated by an empty gap to -0.16.
            // Reuse all hand geometry; exclude only those floating fragments in this medical display copy.
            var mesh = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Mesh>(FpsPresentationSetup.GlovePath)); mesh.name = "FPS Medical Glove";
            var vertices = mesh.vertices;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var indices = mesh.GetTriangles(submesh); var kept = new List<int>();
                for (int i = 0; i < indices.Length; i += 3)
                    if (vertices[indices[i]].y > -0.18f && vertices[indices[i + 1]].y > -0.18f && vertices[indices[i + 2]].y > -0.18f)
                    { kept.Add(indices[i]); kept.Add(indices[i + 1]); kept.Add(indices[i + 2]); }
                mesh.SetTriangles(kept, submesh);
            }
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }
        static void Model(Transform parent, GameObject source)
        {
            var model = parent.Find(source.name);
            if (model == null) { model = ((GameObject)PrefabUtility.InstantiatePrefab(source, parent.gameObject.scene)).transform; model.SetParent(parent, false); }
            model.localPosition = new Vector3(0, -0.24f, 0); model.localRotation = Quaternion.identity; model.localScale = Vector3.one * 0.65f;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }
        static void Reference(Object target, string name, Object value)
        {
            var fields = new SerializedObject(target); fields.FindProperty(name).objectReferenceValue = value; fields.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Append(SerializedProperty list, Renderer[] renderers)
        {
            foreach (var renderer in renderers)
            {
                bool found = false; for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) found = true;
                if (!found) list.GetArrayElementAtIndex(list.arraySize++).objectReferenceValue = renderer;
            }
        }
    }
}
