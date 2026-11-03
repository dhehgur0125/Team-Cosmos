// 권총 손전등 (모델 + 스포트라이트 + 원형 쿠키) 설치
// 메뉴: Tools/VOID LAST/Player/Attach Pistol Flashlight
#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class FlashlightSetup
    {
        const string PlayerPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab";
        const string Dir = "Assets/Player/Prefabs/Item/Flashlight";
        const string ModelPath = Dir + "/VOID_LAST_WeaponFlashlight.prefab";
        const string CookiePath = Dir + "/FlashlightCookie.png";
        // 총 모델(SciFiPistol01_2) 기준 장착 위치: 총열 아래, 앞쪽
        static readonly Vector3 Mount = new Vector3(0f, -0.043f, 0.112f);

        [MenuItem("Tools/VOID LAST/Player/Attach Pistol Flashlight")]
        public static string Build()
        {
            Folder(Dir);
            var cookie = BuildCookie();
            var model = BuildModel();

            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                foreach (var old in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Flashlight (")).ToList())
                    if (old != null) Object.DestroyImmediate(old.gameObject);

                var view = root.GetComponent<FpsWeaponView>();
                var so = new SerializedObject(view);
                var weaponRoot = so.FindProperty("weaponRoot").objectReferenceValue as Transform;
                var worldWeapon = so.FindProperty("worldWeapon").objectReferenceValue as GameObject;
                var fpGun = weaponRoot.Cast<Transform>().First(t => t.GetComponent<MeshFilter>() != null);
                var wGun = worldWeapon.transform.Cast<Transform>().First(t => t.GetComponent<MeshFilter>() != null);
                var cam = root.GetComponentInChildren<Camera>(true);

                // ---- 1인칭: 모델 + 빛
                var fp = Attach(model, fpGun, "Flashlight (1P)");
                var beamGo = new GameObject("Flashlight Beam");
                beamGo.transform.SetParent(fp.transform, false);
                beamGo.transform.localPosition = new Vector3(0, -0.004f, 0.058f);
                // 총은 화면 오른쪽 아래에 있으므로, 빛 중심이 약 10m 앞 화면 가운데로 모이게 살짝 안쪽을 향하게 한다
                var aimPoint = cam.transform.TransformPoint(new Vector3(0, 0, 10f));
                beamGo.transform.rotation = Quaternion.LookRotation(aimPoint - beamGo.transform.position, cam.transform.up);
                var beam = beamGo.AddComponent<Light>();
                beam.type = LightType.Spot;
                beam.range = 26f;
                beam.spotAngle = 56f;
                beam.innerSpotAngle = 46f;
                beam.intensity = 75f;
                beam.color = new Color(1f, 0.95f, 0.86f);
                beam.cookie = cookie;
                beam.shadows = LightShadows.Soft;
                beam.shadowStrength = 0.92f;
                beam.shadowNearPlane = 0.25f;
                beam.bounceIntensity = 0f;
                beam.renderMode = LightRenderMode.ForcePixel;
                beam.enabled = false;

                // ---- 3인칭: 손에 든 총에 같은 모델
                var world = Attach(model, wGun, "Flashlight (3P)");

                var fl = root.GetComponent<FpsWeaponFlashlight>() ?? root.AddComponent<FpsWeaponFlashlight>();
                var fso = new SerializedObject(fl);
                fso.FindProperty("beam").objectReferenceValue = beam;
                fso.FindProperty("intensity").floatValue = 75f;          // 사진처럼 밝은 원이 보이는 밝기
                fso.FindProperty("vignetteStrength").floatValue = 0.88f;  // 켜져 있는 동안 화면 가장자리 어둡게
                fso.FindProperty("vignetteStart").floatValue = 0.3f;
                SetArray(fso.FindProperty("firstPersonLens"), fp.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Lens").ToArray());
                SetArray(fso.FindProperty("worldLens"), world.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Lens").ToArray());
                SetArray(fso.FindProperty("firstPersonModel"), fp.GetComponentsInChildren<Renderer>(true).Where(r => r.name != "Lens").ToArray());
                SetArray(fso.FindProperty("worldModel"), world.GetComponentsInChildren<Renderer>(true).Where(r => r.name != "Lens").ToArray());
                fso.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
                return "flashlight attached: 1P=" + fp.transform.parent.name + " 3P=" + world.transform.parent.name + " beamDir=" + beamGo.transform.forward.ToString("F2");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static GameObject Attach(GameObject model, Transform gun, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, gun);
            go.name = name;
            go.transform.localPosition = Mount;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gun.gameObject.layer;
            return go;
        }

        static void SetArray(SerializedProperty p, Renderer[] items)
        {
            p.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // ------------------------------------------------------------------ 모델 (원점 = 장착점, +Z 앞, 단위 m)
        static GameObject BuildModel()
        {
            var body = Mat("M_Flashlight_Body", new Color(0.09f, 0.095f, 0.1f), 0.75f, 0.55f, false);
            var grip = Mat("M_Flashlight_Grip", new Color(0.035f, 0.035f, 0.04f), 0f, 0.25f, false);
            var bezel = Mat("M_Flashlight_Bezel", new Color(0.45f, 0.46f, 0.48f), 0.9f, 0.7f, false);
            var lens = Mat("M_Flashlight_Lens", new Color(0.85f, 0.88f, 0.9f), 0f, 0.95f, true);

            var root = new GameObject("VOID_LAST_WeaponFlashlight");
            Part(root.transform, "Rail Mount", PrimitiveType.Cube, new Vector3(0, 0.011f, 0.004f), new Vector3(0.014f, 0.01f, 0.046f), Quaternion.identity, body);
            Part(root.transform, "Body", PrimitiveType.Cylinder, new Vector3(0, -0.004f, 0f), new Vector3(0.024f, 0.036f, 0.024f), Quaternion.Euler(90, 0, 0), body);
            Part(root.transform, "Grip Ring A", PrimitiveType.Cylinder, new Vector3(0, -0.004f, -0.016f), new Vector3(0.0255f, 0.004f, 0.0255f), Quaternion.Euler(90, 0, 0), grip);
            Part(root.transform, "Grip Ring B", PrimitiveType.Cylinder, new Vector3(0, -0.004f, -0.005f), new Vector3(0.0255f, 0.004f, 0.0255f), Quaternion.Euler(90, 0, 0), grip);
            Part(root.transform, "Tail Cap", PrimitiveType.Cylinder, new Vector3(0, -0.004f, -0.038f), new Vector3(0.02f, 0.003f, 0.02f), Quaternion.Euler(90, 0, 0), grip);
            Part(root.transform, "Head", PrimitiveType.Cylinder, new Vector3(0, -0.004f, 0.043f), new Vector3(0.032f, 0.009f, 0.032f), Quaternion.Euler(90, 0, 0), body);
            Part(root.transform, "Bezel", PrimitiveType.Cylinder, new Vector3(0, -0.004f, 0.0525f), new Vector3(0.034f, 0.0015f, 0.034f), Quaternion.Euler(90, 0, 0), bezel);
            Part(root.transform, "Lens", PrimitiveType.Cylinder, new Vector3(0, -0.004f, 0.0535f), new Vector3(0.027f, 0.0012f, 0.027f), Quaternion.Euler(90, 0, 0), lens);
            Part(root.transform, "Switch", PrimitiveType.Cube, new Vector3(0.0125f, -0.002f, -0.01f), new Vector3(0.004f, 0.007f, 0.01f), Quaternion.identity, grip);
            PrefabUtility.SaveAsPrefabAsset(root, ModelPath);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        }

        static void Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Material Mat(string name, Color color, float metallic, float smoothness, bool emissive)
        {
            string path = Dir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);   // 켜짐은 FpsWeaponFlashlight가 프로퍼티 블록으로
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ 쿠키: 밝은 원판 + 또렷한 테두리 + 약한 바깥 번짐
        static Texture2D BuildCookie()
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // 원판: 가운데가 가장 밝고 바깥으로 갈수록 조금씩 어두워짐
                    float disc = Mathf.Lerp(1f, 0.72f, Mathf.SmoothStep(0, 1, r / 0.8f));
                    // 반사판 링 (살짝 밝은 띠)
                    disc += 0.07f * Mathf.Exp(-Mathf.Pow((r - 0.62f) / 0.05f, 2));
                    // 또렷한 테두리 (0.78 ~ 0.86)
                    float edge = 1f - Mathf.SmoothStep(0.78f, 0.86f, r);
                    // 테두리 밖으로 아주 약하게 번지는 빛
                    float spill = 0.07f * (1f - Mathf.SmoothStep(0.84f, 0.99f, r));
                    float v = Mathf.Clamp01(disc * edge + spill);
                    if (r >= 0.995f) v = 0;
                    byte b = (byte)(v * 255);
                    px[y * size + x] = new Color32(b, b, b, b);
                }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(CookiePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(CookiePath, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(CookiePath);
            imp.textureType = TextureImporterType.Cookie;
            imp.textureShape = TextureImporterShape.Texture2D;
            imp.alphaSource = TextureImporterAlphaSource.FromGrayScale;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.sRGBTexture = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(CookiePath);
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/');
            Folder(path.Substring(0, i));
            AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
        }
    }
}
#endif
