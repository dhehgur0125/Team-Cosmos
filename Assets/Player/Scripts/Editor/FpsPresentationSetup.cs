/* [ 코드 설명 ] 기존 주사기를 아이콘으로 렌더하고 원본 우주복의 오른손·짧은 소매만 복사해 FPS 장갑 메시를 만듭니다. 원본 메시·본은 수정하지 않으며 HUD와 장착 인스턴스만 갱신합니다. */
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsPresentationSetup
    {
        public const string IconPath = "Assets/Player/Materials/UI/SyringeIcon.png";
        public const string GlovePath = "Assets/Player/Prefabs/Player/Visuals/FPS_RightGlove.asset";

        [MenuItem("VOID LAST/Player/6 - Update HUD and Held Item Visuals")]
        public static void Apply() => FpsFarmingSetup.Apply();

        public static void Configure(GameObject player)
        {
            Folder("Assets/Player/Materials/UI"); Folder("Assets/Player/Prefabs/Player/Visuals");
            if (AssetDatabase.LoadAssetAtPath<Sprite>(IconPath) == null || AssetImporter.GetAtPath(IconPath).userData != "transparent-srgb-v3") CreateIcon();
            ConfigureHud(player);
            var animator = player.GetComponentInChildren<Animator>();
            var body = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Astronaut_Suit_Body_Mesh");
            var existingGlove = AssetDatabase.LoadAssetAtPath<Mesh>(GlovePath);
            if (existingGlove == null || AssetImporter.GetAtPath(GlovePath).userData != "source-joints-grip-v1") CopyGlove(animator, body);
            var camera = player.GetComponentInChildren<Camera>(true);
            MountGlove(camera.transform.Find("WeaponRoot"), body.sharedMaterials, new Vector3(0, -0.045f, -0.065f), Quaternion.Euler(90, 0, 0));
            MountGlove(camera.transform.Find("ItemRoot"), body.sharedMaterials, new Vector3(0, 0, 0), Quaternion.identity);
            var view = new SerializedObject(player.GetComponent<FpsItemView>()); view.FindProperty("modelScale").floatValue = 1.1f; view.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); Folder(path.Substring(0, slash)); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
        static void CreateIcon()
        {
            var preview = new PreviewRenderUtility();
            Texture2D texture = null;
            try
            {
                var item = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FpsFarmingSetup.SyringePath));
                preview.AddSingleGO(item); item.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                preview.camera.orthographic = true; preview.camera.orthographicSize = 0.17f;
                preview.camera.nearClipPlane = 0.01f; preview.camera.farClipPlane = 5;
                preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = Color.clear;
                preview.camera.transform.position = new Vector3(0.35f, 0.1f, -0.12f);
                preview.camera.transform.LookAt(new Vector3(0, 0, 0.01f)); preview.camera.transform.Rotate(0, 0, -35, Space.Self);
                preview.lights[0].intensity = 1.4f; preview.lights[0].transform.rotation = Quaternion.Euler(40, 40, 0);
                preview.lights[1].intensity = 0.8f; preview.lights[1].transform.rotation = Quaternion.Euler(330, 220, 0);
                preview.ambientColor = new Color(0.4f, 0.4f, 0.4f);
                preview.BeginPreview(new Rect(0, 0, 512, 512), GUIStyle.none); preview.Render(true);
                var rendered = (RenderTexture)preview.EndPreview(); var previous = RenderTexture.active;
                var display = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); display.Create();
                bool previousSrgb = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = true; Graphics.Blit(rendered, display); GL.sRGBWrite = previousSrgb;
                    RenderTexture.active = display; texture = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                    texture.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); texture.Apply(); File.WriteAllBytes(IconPath, texture.EncodeToPNG());
                }
                finally { GL.sRGBWrite = previousSrgb; RenderTexture.active = previous; display.Release(); Object.DestroyImmediate(display); }
            }
            finally { if (texture != null) Object.DestroyImmediate(texture); preview.Cleanup(); }
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 512; importer.userData = "transparent-srgb-v3"; importer.SaveAndReimport();
        }
        static void CopyGlove(Animator animator, SkinnedMeshRenderer body)
        {
            var baked = new Mesh();
            var transforms = animator.GetComponentsInChildren<Transform>(true);
            var positions = transforms.Select(t => t.localPosition).ToArray(); var rotations = transforms.Select(t => t.localRotation).ToArray(); var scales = transforms.Select(t => t.localScale).ToArray();
            try
            {
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var forearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                Vector3 towardElbow = (forearm.position - hand.position).normalized;
                var middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                Vector3 alongGrip = (animator.GetBoneTransform(HumanBodyBones.RightIndexProximal).position - animator.GetBoneTransform(HumanBodyBones.RightLittleProximal).position).normalized;
                Vector3 towardFingers = Vector3.ProjectOnPlane(middle.position - hand.position, alongGrip).normalized;
                Vector3 curl = animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal).position - middle.position;
                Vector3 palmNormal = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(curl, alongGrip), towardFingers).normalized;
                // Bend the source finger joints around their existing palm axis, then bake once.
                var fingers = new[] {
                    new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
                    new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
                    new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
                    new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal }
                };
                var angles = new[] { 40f, 55f, 25f };
                foreach (var finger in fingers)
                {
                    Vector3 direction = (animator.GetBoneTransform(finger[1]).position - animator.GetBoneTransform(finger[0]).position).normalized;
                    Vector3 axis = Vector3.Cross(direction, palmNormal).normalized;
                    for (int joint = 0; joint < finger.Length; joint++) { var bone = animator.GetBoneTransform(finger[joint]); bone.rotation = Quaternion.AngleAxis(angles[joint], axis) * bone.rotation; }
                }
                body.BakeMesh(baked);
                if (Vector3.Dot(Vector3.Cross(towardFingers, alongGrip), palmNormal) > 0) alongGrip = -alongGrip;
                Quaternion gripRotation = Quaternion.LookRotation(alongGrip, towardFingers);
                Vector3 gripCenter = middle.position + towardFingers * 0.02f + palmNormal * 0.015f;
                var weights = body.sharedMesh.boneWeights; var bones = body.bones; var vertices = baked.vertices;
                var keep = new bool[vertices.Length];
                for (int i = 0; i < keep.Length; i++)
                {
                    var weight = weights[i]; float handWeight = 0, armWeight = 0;
                    foreach (var pair in new[] { (weight.boneIndex0, weight.weight0), (weight.boneIndex1, weight.weight1), (weight.boneIndex2, weight.weight2), (weight.boneIndex3, weight.weight3) })
                    {
                        if (bones[pair.Item1].IsChildOf(hand)) handWeight += pair.Item2;
                        if (bones[pair.Item1] == forearm) armWeight += pair.Item2;
                    }
                    Vector3 world = body.transform.TransformPoint(vertices[i]);
                    keep[i] = handWeight > 0.25f || armWeight > 0.3f && Vector3.Dot(world - hand.position, towardElbow) < 0.16f;
                }
                var map = new Dictionary<int, int>(); var outputVertices = new List<Vector3>(); var outputUvs = new List<Vector2>(); var outputNormals = new List<Vector3>();
                var uvs = baked.uv; var normals = baked.normals;
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(GlovePath);
                var result = existing != null ? existing : new Mesh(); result.Clear(); result.name = "FPS_RightGlove"; result.subMeshCount = baked.subMeshCount;
                var submeshes = new List<int[]>();
                for (int sub = 0; sub < baked.subMeshCount; sub++)
                {
                    var triangles = baked.GetTriangles(sub); var retained = new List<int>();
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        if (!keep[triangles[i]] || !keep[triangles[i + 1]] || !keep[triangles[i + 2]]) continue;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int source = triangles[i + corner];
                            if (!map.TryGetValue(source, out int destination))
                            {
                                destination = outputVertices.Count; map.Add(source, destination);
                                outputVertices.Add(Quaternion.Inverse(gripRotation) * (body.transform.TransformPoint(vertices[source]) - gripCenter));
                                outputNormals.Add((Quaternion.Inverse(gripRotation) * body.transform.TransformDirection(normals[source])).normalized); outputUvs.Add(uvs[source]);
                            }
                            retained.Add(destination);
                        }
                    }
                    submeshes.Add(retained.ToArray());
                }
                result.SetVertices(outputVertices); result.SetNormals(outputNormals); result.SetUVs(0, outputUvs);
                for (int sub = 0; sub < submeshes.Count; sub++) result.SetTriangles(submeshes[sub], sub);
                result.RecalculateBounds(); result.RecalculateTangents();
                Debug.Log("Copied existing right glove/sleeve: " + result.vertexCount + " vertices; original body unchanged.");
                if (existing == null) AssetDatabase.CreateAsset(result, GlovePath);
                else { EditorUtility.SetDirty(result); AssetDatabase.SaveAssetIfDirty(result); }
                var importer = AssetImporter.GetAtPath(GlovePath); importer.userData = "source-joints-grip-v1"; importer.SaveAndReimport();
            }
            finally
            {
                Object.DestroyImmediate(baked);
                for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; }
            }
        }
        static void MountGlove(Transform parent, Material[] materials, Vector3 position, Quaternion rotation)
        {
            var glove = parent.Find("FPS Right Glove");
            if (glove == null) { glove = new GameObject("FPS Right Glove", typeof(MeshFilter), typeof(MeshRenderer)).transform; glove.SetParent(parent, false); }
            glove.localPosition = position; glove.localRotation = rotation; glove.localScale = Vector3.one;
            glove.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(GlovePath);
            var renderer = glove.GetComponent<MeshRenderer>(); renderer.sharedMaterials = materials; renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        static void ConfigureHud(GameObject player)
        {
            var root = player.transform.Find("FPS HUD"); var hud = player.GetComponent<FpsHud>();
            var fill = root.Find("Health Fill").GetComponent<Image>(); fill.sprite = null; fill.type = Image.Type.Simple;
            var fillRect = fill.rectTransform; fillRect.pivot = new Vector2(0, 0.5f); fillRect.anchoredPosition = new Vector2(25, -82); fillRect.sizeDelta = new Vector2(400, 16);
            root.Find("Health Background").GetComponent<Image>().enabled = false;
            var label = root.Find("Health").GetComponent<Text>(); label.fontSize = 28; label.rectTransform.sizeDelta = new Vector2(400, 44); label.rectTransform.anchoredPosition = new Vector2(225, -44);
            var bar = root.Find("Farming Hotbar").GetComponent<RectTransform>(); bar.anchoredPosition = new Vector2(0, 84); bar.sizeDelta = new Vector2(360, 112);
            var icons = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var slot = bar.Find("Slot " + (i + 4)).GetComponent<RectTransform>(); slot.sizeDelta = new Vector2(108, 108); slot.anchoredPosition = new Vector2((i - 1) * 122, 0);
                slot.Find("Background").GetComponent<RectTransform>().sizeDelta = new Vector2(104, 104);
                var number = slot.Find("Label").GetComponent<Text>(); number.text = (i + 4).ToString(); number.fontSize = 18; number.alignment = TextAnchor.UpperLeft;
                number.rectTransform.sizeDelta = new Vector2(30, 24); number.rectTransform.anchoredPosition = new Vector2(-32, 36);
                var icon = slot.Find("Icon");
                if (icon == null) { icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).transform; icon.SetParent(slot, false); }
                var rect = (RectTransform)icon; rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f; rect.sizeDelta = new Vector2(88, 88); rect.anchoredPosition = new Vector2(0, -5);
                icons[i] = icon.GetComponent<Image>(); icons[i].sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath); icons[i].preserveAspect = true; icons[i].raycastTarget = false;
                number.transform.SetAsLastSibling();
            }
            var serialized = new SerializedObject(hud); serialized.FindProperty("syringeIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            var array = serialized.FindProperty("slotIcons"); array.arraySize = 3;
            for (int i = 0; i < 3; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = icons[i];
            serialized.FindProperty("healthBarWidth").floatValue = 400; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
