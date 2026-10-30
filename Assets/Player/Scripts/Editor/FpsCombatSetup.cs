/* [ 코드 설명 ] 기존 FPS 프리팹에 사격 컴포넌트와 우측 하단 탄약 UI를 연결합니다. 간단한 탄흔 메시·머티리얼·프리팹만 Player 내부에 생성하며 원본 장비와 맵은 변경하지 않습니다. */
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsCombatSetup
    {
        public const string MarkPath = "Assets/Player/Prefabs/Effects/VOID_LAST_BulletMark.prefab";
        [MenuItem("VOID LAST/Player/8 - Add Pistol Shooting and Reload")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            FpsAssetSaveGuard.Active = true;
            try
            {
                BuildMark();
                var root = PrefabUtility.LoadPrefabContents(FpsAssetSetup.PrefabPath);
                try
                {
                    var pistol = root.GetComponent<FpsPistol>() ?? root.AddComponent<FpsPistol>();
                    var fields = new SerializedObject(pistol);
                    fields.FindProperty("viewCamera").objectReferenceValue = root.GetComponentInChildren<Camera>(true);
                    fields.FindProperty("bulletMarkPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(MarkPath).GetComponent<FpsBulletMark>();
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    var hud = root.transform.Find("FPS HUD"); var existing = hud.Find("Ammo");
                    var label = existing != null ? existing.GetComponent<Text>() : new GameObject("Ammo", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                    if (existing == null)
                    {
                        label.transform.SetParent(hud, false);
                        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 28; label.color = Color.white;
                        label.alignment = TextAnchor.MiddleRight; label.raycastTarget = false; label.text = "10 / 30";
                        var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(1, 0);
                        rect.anchoredPosition = new Vector2(-30, 35); rect.sizeDelta = new Vector2(220, 44);
                    }
                    var ui = new SerializedObject(root.GetComponent<FpsHud>()); ui.FindProperty("ammoLabel").objectReferenceValue = label; ui.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, FpsAssetSetup.PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            finally { FpsAssetSaveGuard.Active = false; }
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); Folder(path.Substring(0, slash)); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
        static void BuildMark()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MarkPath) != null) return;
            Folder("Assets/Player/Materials/Effects"); Folder("Assets/Player/Prefabs/Effects");
            const string materialPath = "Assets/Player/Materials/Effects/BulletMark.mat";
            const string meshPath = "Assets/Player/Materials/Effects/BulletMarkMesh.asset";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("Existing URP Unlit shader is unavailable.");
                material = new Material(shader) { name = "BulletMark" }; material.SetColor("_BaseColor", new Color(0.025f, 0.025f, 0.025f)); material.SetFloat("_Cull", 0);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                const int sides = 16; var vertices = new Vector3[sides + 1]; var normals = new Vector3[sides + 1]; var triangles = new int[sides * 3];
                for (int i = 0; i <= sides; i++) normals[i] = Vector3.forward;
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2 / sides; float radius = 0.022f * (i % 3 == 0 ? 0.88f : 1);
                    vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
                    triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % sides + 1;
                }
                mesh = new Mesh { name = "BulletMarkMesh", vertices = vertices, normals = normals, triangles = triangles }; mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            var mark = new GameObject("VOID_LAST_BulletMark", typeof(MeshFilter), typeof(MeshRenderer), typeof(FpsBulletMark));
            try
            {
                mark.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = mark.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                PrefabUtility.SaveAsPrefabAsset(mark, MarkPath);
            }
            finally { Object.DestroyImmediate(mark); }
        }
    }
}
