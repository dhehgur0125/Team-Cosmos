/* [ 코드 설명 ] 탄약 팩 드랍 아이템(4~6번 칸)의 바닥 프리팹과 핫바 아이콘을 만듭니다. 모델은 프로젝트에 있는 SciFiAmmoBox01을 손에 드는 크기로 줄여 쓰고, 나머지 구조(충돌·자동 줍기 범위·FpsWorldPickup)는 배터리 프리팹을 그대로 따릅니다. 원본 에셋은 수정하지 않습니다. */
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsAmmoPackSetup
    {
        const string Template = "Assets/Resources/FarmingItems/Battery.prefab";
        const string Output = "Assets/Resources/FarmingItems/AmmoPack.prefab";
        const string IconPath = "Assets/Resources/FarmingItems/Icons/AmmoPack.png";
        const string BoxPrefab = "Assets/TirgamesAssets/SciFiWorld/Shared/Weapons/Prefabs/SciFiAmmoBox01.prefab";
        const float Length = 0.28f; // Longest side once held: a hand-sized pack, not the 0.63 m crate.

        [MenuItem("VOID LAST/Player/Build Ammo Pack Item")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Template));
            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = "AmmoPack";
                var model = root.transform.Find("Model");
                for (int i = model.childCount - 1; i >= 0; i--) Object.DestroyImmediate(model.GetChild(i).gameObject);

                var box = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefab), model);
                PrefabUtility.UnpackPrefabInstance(box, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); // Its UCX collision child is removed below.
                foreach (var collider in box.GetComponentsInChildren<Collider>(true))
                    if (collider.gameObject == box) Object.DestroyImmediate(collider); else Object.DestroyImmediate(collider.gameObject);
                var renderer = box.GetComponent<Renderer>();
                float scale = Length / Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.y, renderer.bounds.size.z);
                box.transform.localScale = Vector3.one * scale;
                box.transform.localPosition = Vector3.zero;
                var bounds = renderer.bounds; // In root space: root sits at the origin.
                box.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z); // Rest on the floor, centred.
                bounds = renderer.bounds;

                var body = root.GetComponent<BoxCollider>();
                body.center = bounds.center; body.size = bounds.size;
                var pickup = root.GetComponent<FpsWorldPickup>();
                var fields = new SerializedObject(pickup);
                fields.FindProperty("item").intValue = (int)FarmingItemKind.AmmoPack;
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Output);
            }
            finally { Object.DestroyImmediate(root); }
            RenderIcon();
            Debug.Log("Ammo pack item built: " + Output);
        }

        static Texture2D Capture(Camera camera, Color background, int size)
        {
            camera.backgroundColor = background;
            var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = rt; camera.Render(); camera.targetTexture = null;
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, size, size), 0, 0); texture.Apply();
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
            return texture;
        }

        // Transparent 256px hotbar icon rendered from the pickup model, in the style of the other item icons.
        static void RenderIcon()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Output);
            var stage = new GameObject("AmmoPackIconStage");
            try
            {
                stage.transform.position = new Vector3(1000, 1000, 1000);
                var model = Object.Instantiate(prefab.transform.Find("Model").gameObject, stage.transform, false);
                var renderers = model.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(stage.transform, false);
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 0);
                camera.orthographic = true; camera.orthographicSize = bounds.extents.magnitude * 0.85f; camera.nearClipPlane = 0.01f; camera.farClipPlane = 10;
                cameraObject.transform.position = bounds.center + new Vector3(0.6f, 0.55f, -0.8f).normalized * 2;
                cameraObject.transform.LookAt(bounds.center);
                var lightObject = new GameObject("Light"); lightObject.transform.SetParent(stage.transform, false);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f; lightObject.transform.rotation = Quaternion.Euler(40, -30, 0);
                const int size = 256;
                // URP leaves alpha unreliable for a transparent clear, so render on black and on white and recover alpha from the difference.
                var onBlack = Capture(camera, Color.black, size);
                var onWhite = Capture(camera, Color.white, size);
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var black = onBlack.GetPixels(); var white = onWhite.GetPixels(); var pixels = new Color[black.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    float alpha = Mathf.Clamp01(1 - ((white[i].r - black[i].r) + (white[i].g - black[i].g) + (white[i].b - black[i].b)) / 3f);
                    pixels[i] = alpha > 0.001f ? new Color(black[i].r / alpha, black[i].g / alpha, black[i].b / alpha, alpha) : new Color(0, 0, 0, 0);
                }
                texture.SetPixels(pixels); texture.Apply();
                File.WriteAllBytes(IconPath, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(stage); }
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
