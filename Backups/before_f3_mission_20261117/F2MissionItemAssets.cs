// 2층 임무 물건과 배터리의 바닥 프리팹·핫바 아이콘을 만든다 (Resources/FarmingItems).
// F2MissionSetup(메뉴 6)에서 함께 실행된다.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class F2MissionItemAssets
    {
        const string Dir = "Assets/Resources/FarmingItems";
        const string MatDir = Dir + "/Materials";
        const string IconDir = Dir + "/Icons";

        public static void BuildAll()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "FarmingItems");
            EnsureFolder(Dir, "Materials");
            EnsureFolder(Dir, "Icons");

            var dark = Mat("M_Item_Dark", new Color(0.09f, 0.1f, 0.11f), 0.6f, 0.45f);
            var metal = Mat("M_Item_Metal", new Color(0.62f, 0.64f, 0.67f), 0.95f, 0.6f);
            var brass = Mat("M_Item_Brass", new Color(0.72f, 0.5f, 0.2f), 0.95f, 0.55f);
            var orange = Mat("M_Item_Orange", new Color(0.92f, 0.38f, 0.06f), 0.1f, 0.5f);
            var yellow = Mat("M_Item_Yellow", new Color(0.95f, 0.75f, 0.1f), 0.05f, 0.55f);
            var white = Mat("M_Item_White", new Color(0.86f, 0.87f, 0.84f), 0.05f, 0.5f);
            var red = Mat("M_Item_RedGlow", new Color(0.6f, 0.05f, 0.03f), 0f, 0.5f, new Color(2.4f, 0.2f, 0.1f));
            var green = Mat("M_Item_GreenGlow", new Color(0.05f, 0.5f, 0.2f), 0f, 0.5f, new Color(0.2f, 2.6f, 0.8f));
            var cyan = Mat("M_Item_CyanGlow", new Color(0.05f, 0.4f, 0.5f), 0f, 0.5f, new Color(0.2f, 1.8f, 2.6f));

            Item(FarmingItemKind.QuarantineCard, new Vector3(0.2f, 0.03f, 0.13f), 0.3f, m =>
            {
                Part(m, "Card", PrimitiveType.Cube, new Vector3(0, 0.006f, 0), new Vector3(0.18f, 0.012f, 0.115f), white);
                Part(m, "Stripe", PrimitiveType.Cube, new Vector3(0, 0.0125f, 0.038f), new Vector3(0.18f, 0.002f, 0.03f), yellow);
                Part(m, "Chip", PrimitiveType.Cube, new Vector3(-0.05f, 0.0125f, -0.018f), new Vector3(0.04f, 0.002f, 0.03f), dark);
                Part(m, "LED", PrimitiveType.Cube, new Vector3(0.062f, 0.0135f, -0.03f), new Vector3(0.03f, 0.003f, 0.018f), red);
            });
            Item(FarmingItemKind.Cutter, new Vector3(0.5f, 0.2f, 0.24f), 2.5f, m =>
            {
                Part(m, "Body", PrimitiveType.Cube, new Vector3(0, 0.07f, 0), new Vector3(0.24f, 0.09f, 0.1f), orange);
                Part(m, "Grip", PrimitiveType.Cube, new Vector3(-0.17f, 0.06f, 0), new Vector3(0.14f, 0.05f, 0.05f), dark);
                Part(m, "Blade", PrimitiveType.Cylinder, new Vector3(0.17f, 0.1f, 0), new Vector3(0.2f, 0.004f, 0.2f), metal, new Vector3(90, 0, 0));
                Part(m, "Guard", PrimitiveType.Cube, new Vector3(0.12f, 0.15f, 0), new Vector3(0.14f, 0.05f, 0.04f), dark);
                Part(m, "Hub", PrimitiveType.Cylinder, new Vector3(0.17f, 0.1f, 0), new Vector3(0.05f, 0.02f, 0.05f), dark, new Vector3(90, 0, 0));
            });
            Item(FarmingItemKind.PowerModule, new Vector3(0.44f, 0.34f, 0.32f), 8f, m =>
            {
                Part(m, "Case", PrimitiveType.Cube, new Vector3(0, 0.15f, 0), new Vector3(0.42f, 0.28f, 0.3f), dark);
                Part(m, "Panel", PrimitiveType.Cube, new Vector3(0, 0.15f, 0.152f), new Vector3(0.34f, 0.2f, 0.006f), metal);
                Part(m, "Strip A", PrimitiveType.Cube, new Vector3(0, 0.2f, 0.157f), new Vector3(0.28f, 0.022f, 0.004f), green);
                Part(m, "Strip B", PrimitiveType.Cube, new Vector3(0, 0.15f, 0.157f), new Vector3(0.28f, 0.022f, 0.004f), green);
                Part(m, "Handle L", PrimitiveType.Cube, new Vector3(-0.11f, 0.31f, 0), new Vector3(0.03f, 0.03f, 0.2f), metal);
                Part(m, "Handle R", PrimitiveType.Cube, new Vector3(0.11f, 0.31f, 0), new Vector3(0.03f, 0.03f, 0.2f), metal);
            });
            Item(FarmingItemKind.FuelCoupler, new Vector3(0.42f, 0.24f, 0.24f), 3f, m =>
            {
                Part(m, "Pipe", PrimitiveType.Cylinder, new Vector3(0, 0.1f, 0), new Vector3(0.14f, 0.17f, 0.14f), brass, new Vector3(0, 0, 90));
                Part(m, "Flange L", PrimitiveType.Cylinder, new Vector3(-0.17f, 0.1f, 0), new Vector3(0.2f, 0.018f, 0.2f), metal, new Vector3(0, 0, 90));
                Part(m, "Flange R", PrimitiveType.Cylinder, new Vector3(0.17f, 0.1f, 0), new Vector3(0.2f, 0.018f, 0.2f), metal, new Vector3(0, 0, 90));
                Part(m, "Collar", PrimitiveType.Cylinder, new Vector3(0, 0.1f, 0), new Vector3(0.17f, 0.03f, 0.17f), dark, new Vector3(0, 0, 90));
                Part(m, "Lever", PrimitiveType.Cube, new Vector3(0, 0.2f, 0), new Vector3(0.03f, 0.05f, 0.14f), orange);
            });
            Item(FarmingItemKind.Battery, new Vector3(0.13f, 0.24f, 0.13f), 0.8f, m =>
            {
                Part(m, "Cell", PrimitiveType.Cylinder, new Vector3(0, 0.1f, 0), new Vector3(0.11f, 0.1f, 0.11f), dark);
                Part(m, "Band", PrimitiveType.Cylinder, new Vector3(0, 0.12f, 0), new Vector3(0.114f, 0.012f, 0.114f), cyan);
                Part(m, "Cap", PrimitiveType.Cylinder, new Vector3(0, 0.205f, 0), new Vector3(0.05f, 0.012f, 0.05f), metal);
                Part(m, "Base", PrimitiveType.Cylinder, new Vector3(0, 0.005f, 0), new Vector3(0.115f, 0.005f, 0.115f), metal);
            });
            AssetDatabase.SaveAssets();
        }

        public static FpsWorldPickup Load(FarmingItemKind kind) => AssetDatabase.LoadAssetAtPath<FpsWorldPickup>(Dir + "/" + kind + ".prefab");

        static void Item(FarmingItemKind kind, Vector3 colliderSize, float mass, Action<Transform> build)
        {
            var root = new GameObject(kind.ToString());
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            build(model.transform);

            var box = root.AddComponent<BoxCollider>();
            box.size = colliderSize;
            box.center = new Vector3(0, colliderSize.y * 0.5f, 0);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true; body.useGravity = true; body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var range = new GameObject("Automatic Pickup Range");
            range.transform.SetParent(root.transform, false);
            var trigger = range.AddComponent<SphereCollider>();
            trigger.isTrigger = true; trigger.radius = 0.6f;
            var pickup = root.AddComponent<FpsWorldPickup>();
            var so = new SerializedObject(pickup);
            so.FindProperty("item").enumValueIndex = (int)kind;
            so.FindProperty("pickupTrigger").objectReferenceValue = trigger;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (FarmingItems.IsScannable(kind)) root.AddComponent<DetectableItem>();

            RenderIcon(kind, model.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, Dir + "/" + kind + ".prefab");
            Object.DestroyImmediate(root);
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        public static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 size, Material mat, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>모델을 투명 배경으로 렌더링해 핫바 아이콘을 만든다. 실패하면 단순한 사각형으로 대체.</summary>
        static void RenderIcon(FarmingItemKind kind, GameObject model)
        {
            const int size = 256;
            string path = IconDir + "/" + kind + ".png";
            var origin = new Vector3(0, 5000, 0);
            var temp = new List<Object>();
            Texture2D tex = null;
            try
            {
                var m = Object.Instantiate(model, origin, Quaternion.Euler(-25, 200, 15));
                temp.Add(m);
                var renderers = m.GetComponentsInChildren<Renderer>();
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                var camGo = new GameObject("ItemIconCamera"); temp.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y) * 1.2f;
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 12f;
                cam.transform.position = b.center + new Vector3(0, 0, -4f);
                cam.transform.rotation = Quaternion.identity;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None; data.renderShadows = false;
                var key = new GameObject("ItemIconKey"); temp.Add(key);
                var kl = key.AddComponent<Light>(); kl.type = LightType.Point; kl.range = 8; kl.intensity = 7f; kl.color = new Color(1f, 0.97f, 0.92f);
                key.transform.position = b.center + new Vector3(-1.2f, 1.4f, -2f);
                var fill = new GameObject("ItemIconFill"); temp.Add(fill);
                var fl = fill.AddComponent<Light>(); fl.type = LightType.Point; fl.range = 8; fl.intensity = 3.5f; fl.color = new Color(0.75f, 0.85f, 1f);
                fill.transform.position = b.center + new Vector3(1.6f, -0.4f, -1.6f);

                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                temp.Add(rt);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                int opaque = 0;
                foreach (var c in tex.GetPixels32()) if (c.a > 10) opaque++;
                if (opaque < size * size / 200) { Object.DestroyImmediate(tex); tex = null; }
            }
            catch (Exception e) { Debug.LogWarning("[F2MissionItemAssets] 아이콘 렌더링 실패, 사각형으로 대체: " + e.Message); tex = null; }
            finally { foreach (var o in temp) if (o != null) Object.DestroyImmediate(o); }

            if (tex == null)
            {
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                for (int y = 48; y < size - 48; y++) for (int x = 48; x < size - 48; x++) px[y * size + x] = new Color32(220, 180, 60, 255);
                tex.SetPixels32(px); tex.Apply();
            }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
#endif
