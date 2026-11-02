// Stage2 멀티플레이 + 음성 채팅 + 무전기 아이템 설정
// 메뉴: Tools/VOID LAST/Multiplayer/...
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VoidLast.Monster;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class MultiplayerSetup
    {
        public const string BasePlayerPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab";
        public const string NetPlayerPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS_Network.prefab";
        public const string SyringePickupPath = "Assets/Player/Prefabs/Item/VOID_LAST_SyringePickup.prefab";
        public const string RadioDir = "Assets/VoiceChat/Radio";
        public const string RadioModelPath = RadioDir + "/VOID_LAST_RadioModel.prefab";
        public const string RadioPickupPath = RadioDir + "/VOID_LAST_RadioPickup.prefab";
        public const string RadioIconPath = RadioDir + "/RadioIcon.png";
        public const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        public const string LobbyScenePath = "Assets/Map/Scenes/Lobby.unity";
        public const string Stage2ScenePath = "Assets/Map/Scenes/Stage2_Ship.unity";
        public const string FontAssetPath = "Assets/Map/Fonts/NotoSansKR SDF.asset";
        public const string MultiRootName = "VOID_LAST_Multiplayer";
        static readonly Vector3 PlayerStart = new Vector3(-18, 8.1f, -28.5f);

        // ================================================================== all
        [MenuItem("Tools/VOID LAST/Multiplayer/Run All Setup")]
        public static string RunAll()
        {
            var log = new List<string>();
            log.Add(BuildRadioAssets());
            log.Add(UpgradeBasePlayer());
            log.Add(BuildNetworkPlayer());
            log.Add(PatchLobbyScene());
            log.Add(PatchStage2Scene());
            AssetDatabase.SaveAssets();
            string s = string.Join("\n", log);
            Debug.Log("[MultiplayerSetup]\n" + s);
            return s;
        }

        // ================================================================== radio assets
        [MenuItem("Tools/VOID LAST/Multiplayer/1 Build Radio Assets")]
        public static string BuildRadioAssets()
        {
            Folder(RadioDir);
            var body = Mat("M_Radio_Body", new Color(0.2f, 0.215f, 0.19f), 0.05f, 0.38f);
            var rubber = Mat("M_Radio_Rubber", new Color(0.045f, 0.047f, 0.05f), 0f, 0.22f);
            var metal = Mat("M_Radio_Metal", new Color(0.58f, 0.6f, 0.62f), 0.85f, 0.55f);
            var grille = Mat("M_Radio_Grille", new Color(0.075f, 0.08f, 0.085f), 0.3f, 0.3f);
            var screen = Mat("M_Radio_Screen", new Color(0.05f, 0.12f, 0.1f), 0f, 0.8f, new Color(0.15f, 0.55f, 0.35f) * 0.6f);
            var led = Mat("M_Radio_LED", new Color(0.1f, 0.6f, 0.2f), 0f, 0.8f, new Color(0.1f, 0.9f, 0.3f) * 1.5f);
            var accent = Mat("M_Radio_Accent", new Color(0.8f, 0.42f, 0.08f), 0.1f, 0.4f);

            // ---- model (원점 = 몸통 중심, +Y 위, +Z 앞면 스피커)
            var root = new GameObject("VOID_LAST_RadioModel");
            Part(root.transform, "Body", PrimitiveType.Cube, Vector3.zero, new Vector3(0.062f, 0.13f, 0.034f), body);
            Part(root.transform, "Body Edge Top", PrimitiveType.Cube, new Vector3(0, 0.0635f, 0), new Vector3(0.064f, 0.006f, 0.036f), rubber);
            Part(root.transform, "Body Edge Bottom", PrimitiveType.Cube, new Vector3(0, -0.0635f, 0), new Vector3(0.064f, 0.006f, 0.036f), rubber);
            Part(root.transform, "Grille", PrimitiveType.Cube, new Vector3(0, 0.024f, 0.0172f), new Vector3(0.048f, 0.05f, 0.002f), grille);
            for (int i = 0; i < 6; i++)
                Part(root.transform, "Grille Slat " + i, PrimitiveType.Cube, new Vector3(0, 0.004f + i * 0.0078f, 0.0185f), new Vector3(0.042f, 0.0026f, 0.0012f), rubber);
            Part(root.transform, "Screen", PrimitiveType.Cube, new Vector3(0, -0.017f, 0.0173f), new Vector3(0.042f, 0.018f, 0.0015f), screen);
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                    Part(root.transform, $"Key {r}{c}", PrimitiveType.Cube, new Vector3(-0.014f + c * 0.014f, -0.038f - r * 0.011f, 0.0176f), new Vector3(0.01f, 0.007f, 0.0022f), rubber);
            Part(root.transform, "PTT Button", PrimitiveType.Cube, new Vector3(-0.0335f, 0.012f, 0), new Vector3(0.006f, 0.034f, 0.016f), accent);
            Part(root.transform, "Belt Clip", PrimitiveType.Cube, new Vector3(0, 0.012f, -0.0185f), new Vector3(0.026f, 0.07f, 0.004f), rubber);
            Part(root.transform, "Antenna", PrimitiveType.Cylinder, new Vector3(-0.018f, 0.12f, 0), new Vector3(0.009f, 0.052f, 0.009f), rubber);
            Part(root.transform, "Antenna Base", PrimitiveType.Cylinder, new Vector3(-0.018f, 0.071f, 0), new Vector3(0.014f, 0.006f, 0.014f), metal);
            Part(root.transform, "Antenna Tip", PrimitiveType.Sphere, new Vector3(-0.018f, 0.173f, 0), new Vector3(0.011f, 0.011f, 0.011f), rubber);
            Part(root.transform, "Knob", PrimitiveType.Cylinder, new Vector3(0.016f, 0.0715f, 0), new Vector3(0.014f, 0.006f, 0.014f), metal);
            Part(root.transform, "LED", PrimitiveType.Sphere, new Vector3(0.004f, 0.069f, 0.008f), new Vector3(0.0065f, 0.0065f, 0.0065f), led);
            PrefabUtility.SaveAsPrefabAsset(root, RadioModelPath);
            Object.DestroyImmediate(root);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RadioModelPath);

            // ---- pickup (바닥에 등을 대고 누운 무전기, 원점 = 바닥 접점)
            float scale = 1.35f;
            var syringe = AssetDatabase.LoadAssetAtPath<GameObject>(SyringePickupPath);
            var syringeTrigger = syringe.GetComponentsInChildren<SphereCollider>(true).FirstOrDefault(c => c.isTrigger);
            var syringeBody = syringe.GetComponent<Rigidbody>();
            var proot = new GameObject("VOID_LAST_RadioPickup");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, proot.transform);
            inst.name = "Radio Model";
            inst.transform.localPosition = new Vector3(0, 0.034f * 0.5f * scale + 0.0025f * scale, 0);
            inst.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            inst.transform.localScale = Vector3.one * scale;
            var box = proot.AddComponent<BoxCollider>();
            box.size = new Vector3(0.066f, 0.04f, 0.14f) * scale;
            box.center = new Vector3(0, box.size.y * 0.5f, 0);
            var rb = proot.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = true; rb.mass = 0.3f;
            if (syringeBody != null) { rb.interpolation = syringeBody.interpolation; rb.collisionDetectionMode = syringeBody.collisionDetectionMode; }
            var trigGo = new GameObject("Automatic Pickup Range");
            trigGo.transform.SetParent(proot.transform, false);
            var trig = trigGo.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = syringeTrigger != null ? syringeTrigger.radius * syringeTrigger.transform.lossyScale.x : 0.6f;
            trig.center = syringeTrigger != null ? syringeTrigger.center : Vector3.zero;
            var pickup = proot.AddComponent<FpsWorldPickup>();
            var so = new SerializedObject(pickup);
            so.FindProperty("item").enumValueIndex = (int)FarmingItemKind.Radio;
            so.FindProperty("pickupTrigger").objectReferenceValue = trig;
            var sp = new SerializedObject(syringe.GetComponent<FpsWorldPickup>());
            so.FindProperty("pickupDelay").floatValue = sp.FindProperty("pickupDelay").floatValue;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(proot, RadioPickupPath);
            Object.DestroyImmediate(proot);

            string icon = RenderIcon(model);
            AssetDatabase.SaveAssets();
            return "radio assets: model, pickup, " + icon;
        }

        static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            string path = RadioDir + "/" + name + ".mat";
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

        static void Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>무전기 모델을 투명 배경으로 렌더링해서 핫바 아이콘(512px)을 만든다. 실패하면 단순한 그림으로 대체.</summary>
        static string RenderIcon(GameObject model)
        {
            const int size = 512;
            var origin = new Vector3(0, 5000, 0);
            var temp = new List<Object>();
            Texture2D tex = null;
            try
            {
                var m = (GameObject)Object.Instantiate(model, origin, Quaternion.Euler(-8, 205, -38));
                m.transform.localScale = Vector3.one * 6f;
                temp.Add(m);
                var camGo = new GameObject("RadioIconCamera"); temp.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.orthographic = true; cam.orthographicSize = 0.8f;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 10f;
                cam.transform.position = origin + new Vector3(0.04f, 0.17f, -3f);
                cam.transform.rotation = Quaternion.identity;
                cam.cullingMask = ~0;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None; data.renderShadows = false;
                var key = new GameObject("RadioIconKey"); temp.Add(key);
                var kl = key.AddComponent<Light>(); kl.type = LightType.Point; kl.range = 8; kl.intensity = 6f; kl.color = new Color(1f, 0.97f, 0.92f);
                key.transform.position = origin + new Vector3(-1.4f, 1.6f, -2.2f);
                var fill = new GameObject("RadioIconFill"); temp.Add(fill);
                var fl = fill.AddComponent<Light>(); fl.type = LightType.Point; fl.range = 8; fl.intensity = 3f; fl.color = new Color(0.75f, 0.85f, 1f);
                fill.transform.position = origin + new Vector3(1.8f, -0.4f, -1.8f);

                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
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
            catch (Exception e) { Debug.LogWarning("[MultiplayerSetup] 아이콘 렌더링 실패, 그림으로 대체: " + e.Message); tex = null; }
            finally { foreach (var o in temp) if (o != null) Object.DestroyImmediate(o); }

            bool rendered = tex != null;
            if (tex == null) tex = DrawIcon(size);
            File.WriteAllBytes(RadioIconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(RadioIconPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(RadioIconPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return rendered ? "icon (rendered)" : "icon (drawn)";
        }

        static Texture2D DrawIcon(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            Color32 bodyC = new Color32(70, 74, 66, 255), dark = new Color32(20, 22, 24, 255), edge = new Color32(200, 205, 210, 255);
            void Rect(int x0, int y0, int x1, int y1, Color32 c) { for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (x >= 0 && y >= 0 && x < size && y < size) px[y * size + x] = c; }
            Rect(186, 96, 326, 384, edge); Rect(194, 104, 318, 376, bodyC);
            Rect(212, 250, 300, 350, dark);
            for (int i = 0; i < 6; i++) Rect(218, 262 + i * 14, 294, 268 + i * 14, bodyC);
            Rect(212, 196, 300, 232, new Color32(40, 120, 80, 255));
            Rect(210, 384, 228, 470, dark);
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        // ================================================================== base player prefab
        [MenuItem("Tools/VOID LAST/Multiplayer/2 Upgrade Base Player (Radio)")]
        public static string UpgradeBasePlayer()
        {
            var root = PrefabUtility.LoadPrefabContents(BasePlayerPath);
            try
            {
                var hud = root.GetComponent<FpsHud>();
                var so = new SerializedObject(hud);
                so.FindProperty("radioIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(RadioIconPath);
                so.ApplyModifiedPropertiesWithoutUndo();

                var interactor = root.GetComponent<FpsItemInteractor>();
                so = new SerializedObject(interactor);
                so.FindProperty("radioPickupPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<FpsWorldPickup>(RadioPickupPath);
                so.ApplyModifiedPropertiesWithoutUndo();

                var view = root.GetComponent<FpsRadioView>() ?? root.AddComponent<FpsRadioView>();
                view.radioModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RadioModelPath);
                var cam = root.GetComponentInChildren<Camera>(true);
                view.firstPersonParent = cam != null ? cam.transform : null;
                view.gloveSource = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FPS Right Glove");
                view.worldSocket = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "WorldItemSocket");
                PrefabUtility.SaveAsPrefabAsset(root, BasePlayerPath);
                return $"base player: radio icon/pickup/view (glove={(view.gloveSource != null)}, socket={(view.worldSocket != null)})";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ================================================================== network player variant
        [MenuItem("Tools/VOID LAST/Multiplayer/3 Build Network Player")]
        public static string BuildNetworkPlayer()
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePlayerPath);
            GameObject go;
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath) != null;
            go = existed ? PrefabUtility.LoadPrefabContents(NetPlayerPath) : (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                go.name = "VOID_LAST_FPS_Network";
                var no = go.GetComponent<NetworkObject>() ?? go.AddComponent<NetworkObject>();
                no.DontDestroyWithOwner = false;
                var nt = go.GetComponent<NetworkTransform>() ?? go.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true;
                nt.SyncRotAngleX = false; nt.SyncRotAngleY = true; nt.SyncRotAngleZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.InLocalSpace = false; nt.Interpolate = true;

                var cam = go.GetComponentInChildren<Camera>(true);
                var listener = cam != null ? cam.GetComponent<AudioListener>() : null;
                var camData = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
                if (camData != null) camData.requiresDepthTexture = true;   // 탐지기 음파 셰이더용

                var detector = go.GetComponent<ItemDetector>() ?? go.AddComponent<ItemDetector>();
                detector.activateKey = KeyCode.Z;
                detector.xrayShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/item/Shaders/ItemXRayGlow.shader");
                detector.sonarShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/item/Shaders/SonarWave.shader");
                detector.pingShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/item/Shaders/EchoPing.shader");
                var bridge = go.GetComponent<FpsPickupDetectionBridge>() ?? go.AddComponent<FpsPickupDetectionBridge>();
                var melee = go.GetComponent<MonsterMeleeTestBridge>() ?? go.AddComponent<MonsterMeleeTestBridge>();

                var net = go.GetComponent<FpsNetworkPlayer>() ?? go.AddComponent<FpsNetworkPlayer>();
                var owner = new List<Behaviour>
                {
                    go.GetComponent<FpsInput>(), go.GetComponent<FpsLook>(), go.GetComponent<FpsHud>(), go.GetComponent<FpsItemInteractor>(),
                    go.GetComponent<FpsPistol>(), detector, bridge, melee, cam, listener,
                };
                var debugInput = go.GetComponent<FpsHealthDebugInput>(); if (debugInput != null) owner.Add(debugInput);
                owner.RemoveAll(b => b == null);
                net.ownerOnlyBehaviours = owner.ToArray();
                var hudObj = go.transform.Find("FPS HUD");
                net.ownerOnlyObjects = hudObj != null ? new[] { hudObj.gameObject } : new GameObject[0];
                net.cameraPivot = go.transform.Find("CameraPivot");
                net.viewCamera = cam;

                // 프리팹 기본값: 내 캐릭터 전용 기능은 꺼 둠 (스폰 후 Owner만 켬)
                foreach (var b in owner) b.enabled = false;
                if (hudObj != null) hudObj.gameObject.SetActive(false);

                if (existed) PrefabUtility.SaveAsPrefabAsset(go, NetPlayerPath);
                else PrefabUtility.SaveAsPrefabAsset(go, NetPlayerPath);
            }
            finally
            {
                if (existed) PrefabUtility.UnloadPrefabContents(go);
                else Object.DestroyImmediate(go);
            }

            // NetworkPrefabs 목록 등록
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath);
            if (list != null && !list.PrefabList.Any(p => p.Prefab == prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
            }
            AssetDatabase.SaveAssets();
            return "network player: " + NetPlayerPath + " (prefab list " + (list != null ? list.PrefabList.Count.ToString() : "missing") + ")";
        }

        // ================================================================== lobby
        [MenuItem("Tools/VOID LAST/Multiplayer/4 Patch Lobby Scene")]
        public static string PatchLobbyScene()
        {
            var scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
            var lm = Object.FindFirstObjectByType<LobbyManager>(FindObjectsInactive.Include);
            var spawner = Object.FindFirstObjectByType<LobbyPlayerSpawner>(FindObjectsInactive.Include);
            if (lm == null || spawner == null) throw new InvalidOperationException("Lobby scene is missing LobbyManager/LobbyPlayerSpawner");
            ApplySpawnerMapping(spawner);
            if (string.IsNullOrEmpty(lm.gameSceneName) || lm.gameSceneName == "MapScene") lm.gameSceneName = "Stage2_Ship";
            ApplyLobbyVoiceUi(lm, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath));
            EditorUtility.SetDirty(lm); EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "lobby: spawner Stage2 → network player, voice UI";
        }

        public static void ApplySpawnerMapping(LobbyPlayerSpawner spawner)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetPlayerPath);
            var list = new List<LobbyPlayerSpawner.ScenePlayerPrefab>(spawner.scenePlayerPrefabs ?? new LobbyPlayerSpawner.ScenePlayerPrefab[0]);
            list.RemoveAll(e => e == null || e.sceneName == "Stage2_Ship");
            list.Add(new LobbyPlayerSpawner.ScenePlayerPrefab { sceneName = "Stage2_Ship", prefab = prefab });
            spawner.scenePlayerPrefabs = list.ToArray();
            spawner.spawnPointKeywords = new[] { "PlayerSpawn", "PlayerStart" };
        }

        /// <summary>대기방에 음성 채팅 UI 추가: 채팅 카드 오른쪽 위 마이크 버튼/상태 + 카드마다 말하는 중 링/문구</summary>
        public static void ApplyLobbyVoiceUi(LobbyManager lm, TMP_FontAsset font)
        {
            var rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var chatCard = lm.chatLogText != null ? lm.chatLogText.GetComponentInParent<ScrollRect>(true).transform.parent : null;
            if (chatCard != null)
            {
                var old = chatCard.Find("MicButton"); if (old != null) Object.DestroyImmediate(old.gameObject);
                old = chatCard.Find("VoiceStatus"); if (old != null) Object.DestroyImmediate(old.gameObject);

                var btnImg = UiImage(chatCard, "MicButton", LobbyStyle.ButtonDark, rounded);
                btnImg.raycastTarget = true;
                var btn = btnImg.gameObject.AddComponent<Button>();
                var colors = btn.colors;
                colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(0.8f, 0.8f, 0.8f); colors.colorMultiplier = 1.2f;
                btn.colors = colors;
                var br = btnImg.rectTransform; br.anchorMin = br.anchorMax = new Vector2(1, 1); br.pivot = new Vector2(1, 1);
                br.anchoredPosition = new Vector2(-24, -9); br.sizeDelta = new Vector2(170, 38);
                var label = UiText(btnImg.transform, "Label", "마이크 끄기", 19, Color.white, TextAlignmentOptions.Center, font);
                var lr = label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
                lm.micButton = btn; lm.micButtonText = label;

                var status = UiText(chatCard, "VoiceStatus", "음성 연결 중...", 18, LobbyStyle.TextDim, TextAlignmentOptions.Right, font);
                var sr = status.rectTransform; sr.anchorMin = sr.anchorMax = new Vector2(1, 1); sr.pivot = new Vector2(1, 1);
                sr.anchoredPosition = new Vector2(-208, -12); sr.sizeDelta = new Vector2(520, 32);
                status.textWrappingMode = TextWrappingModes.NoWrap; status.overflowMode = TextOverflowModes.Ellipsis;
                lm.voiceStatusText = status;

                var title = chatCard.Find("ChatTitle")?.GetComponent<TMP_Text>();
                if (title != null) title.text = "채팅 · 음성";
            }

            foreach (var slot in lm.slots)
            {
                if (slot == null) continue;
                var old = slot.transform.Find("SpeakingRing"); if (old != null) Object.DestroyImmediate(old.gameObject);
                old = slot.transform.Find("VoiceText"); if (old != null) Object.DestroyImmediate(old.gameObject);

                var ring = UiImage(slot.transform, "SpeakingRing", LobbyStyle.Ready, knob);
                ring.type = Image.Type.Simple;
                var rr = ring.rectTransform; rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 1); rr.pivot = new Vector2(0.5f, 1);
                rr.anchoredPosition = new Vector2(0, -21); rr.sizeDelta = new Vector2(146, 146);
                if (slot.avatar != null) ring.transform.SetSiblingIndex(slot.avatar.transform.GetSiblingIndex());
                ring.gameObject.SetActive(false);
                slot.speakingRing = ring;

                // 카드 왼쪽 위 (오른쪽 위는 내보내기 버튼)
                var vt = UiText(slot.transform, "VoiceText", "", 18, LobbyStyle.TextDim, TextAlignmentOptions.Left, font);
                var vr = vt.rectTransform; vr.anchorMin = vr.anchorMax = new Vector2(0, 1); vr.pivot = new Vector2(0, 1);
                vr.anchoredPosition = new Vector2(14, -10); vr.sizeDelta = new Vector2(150, 30);
                vt.textWrappingMode = TextWrappingModes.NoWrap;
                slot.voiceText = vt;
                EditorUtility.SetDirty(slot);
            }
        }

        static Image UiImage(Transform parent, string name, Color c, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c; img.raycastTarget = false;
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            return img;
        }

        static TextMeshProUGUI UiText(Transform parent, string name, string text, float size, Color c, TextAlignmentOptions align, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.raycastTarget = false;
            return t;
        }

        // ================================================================== Stage2
        [MenuItem("Tools/VOID LAST/Multiplayer/5 Patch Stage2 Scene")]
        public static string PatchStage2Scene()
        {
            var scene = EditorSceneManager.OpenScene(Stage2ScenePath, OpenSceneMode.Single);
            var log = new List<string>();
            var integration = GameObject.Find("VOID_LAST_Integration_Test");
            if (integration == null) throw new InvalidOperationException("VOID_LAST_Integration_Test not found");
            var offlinePlayer = integration.GetComponentsInChildren<FpsHealth>(true).Select(h => h.gameObject).FirstOrDefault(g => g.GetComponent<FpsNetworkPlayer>() == null);
            var prototypeVoice = integration.GetComponentInChildren<StandaloneRadioTest>(true)?.gameObject;

            var old = GameObject.Find(MultiRootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(MultiRootName);

            // ---- spawn points
            var spawns = new GameObject("Player Spawns").transform; spawns.SetParent(root.transform, false);
            var exclude = integration.GetComponentsInChildren<FpsWorldPickup>(true).Select(p => p.transform.position).ToList();
            var chosen = new List<Vector3>();
            foreach (var c in Ring(PlayerStart, 0.0f, 3.2f, 0.8f))
            {
                if (chosen.Count >= 4) break;
                if (chosen.Any(p => (p - c).sqrMagnitude < 1.3f * 1.3f) || exclude.Any(p => Flat(p - c) < 0.8f)) continue;
                if (!PlayerPoint(c, offlinePlayer, out var at)) continue;
                chosen.Add(at);
            }
            for (int i = 0; i < chosen.Count; i++)
            {
                var t = new GameObject("PlayerSpawn_" + (i + 1)).transform;
                t.SetParent(spawns, false);
                t.SetPositionAndRotation(chosen[i], Quaternion.Euler(0, 90, 0));
            }
            log.Add("spawns: " + chosen.Count + " " + string.Join(" ", chosen.Select(p => p.ToString("F2"))));

            // ---- radio pickups (대원 수만큼 4개, 시작 지점 근처 바닥)
            var pickupsRoot = integration.transform.Find("Integration Pickups");
            if (pickupsRoot != null)
                foreach (Transform t in pickupsRoot.Cast<Transform>().ToList())
                    if (t.name.StartsWith("VOID_LAST_RadioPickup")) Object.DestroyImmediate(t.gameObject);
            var radioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RadioPickupPath);
            var radioSpots = new List<Vector3>();
            foreach (var c in Ring(PlayerStart + new Vector3(1.2f, 0, -1.4f), 0f, 2.2f, 0.45f))
            {
                if (radioSpots.Count >= 4) break;
                if (radioSpots.Any(p => Flat(p - c) < 0.5f) || exclude.Any(p => Flat(p - c) < 0.45f) || chosen.Any(p => Flat(p - c) < 0.7f)) continue;
                if (!FloorPoint(c, out var floor)) continue;
                if (Physics.CheckBox(floor + Vector3.up * 0.12f, new Vector3(0.12f, 0.1f, 0.15f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                radioSpots.Add(floor);
            }
            for (int i = 0; i < radioSpots.Count; i++)
            {
                var r = (GameObject)PrefabUtility.InstantiatePrefab(radioPrefab, pickupsRoot != null ? pickupsRoot : integration.transform);
                r.name = "VOID_LAST_RadioPickup " + (i + 1);
                r.transform.SetPositionAndRotation(radioSpots[i] + Vector3.up * 0.002f, Quaternion.Euler(0, 70 + i * 23, 0));
                if (r.GetComponent<DetectableItem>() == null) r.AddComponent<DetectableItem>();
            }
            log.Add("radios: " + radioSpots.Count);

            // ---- multiplayer objects
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            var voiceGo = new GameObject("VoiceChat (Multiplayer)"); voiceGo.transform.SetParent(root.transform, false);
            var voice = voiceGo.AddComponent<InGameVoiceChat>(); voice.font = font;

            var regGo = new GameObject("Network Pickup Registry"); regGo.transform.SetParent(root.transform, false);
            regGo.AddComponent<NetworkObject>();
            var reg = regGo.AddComponent<NetworkPickupRegistry>();
            reg.syringePickupPrefab = AssetDatabase.LoadAssetAtPath<FpsWorldPickup>(SyringePickupPath);
            reg.radioPickupPrefab = AssetDatabase.LoadAssetAtPath<FpsWorldPickup>(RadioPickupPath);

            var bootGo = new GameObject("Stage2 Network Bootstrap"); bootGo.transform.SetParent(root.transform, false);
            var boot = bootGo.AddComponent<Stage2NetworkBootstrap>();
            boot.offlineOnly = new[] { offlinePlayer, prototypeVoice }.Where(g => g != null).ToArray();
            boot.onlineOnly = new[] { voiceGo };
            log.Add("bootstrap offlineOnly=" + string.Join(",", boot.offlineOnly.Select(g => g.name)));

            // ---- monsters
            int monsters = 0;
            foreach (var hp in Object.FindObjectsByType<MonsterHealth>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                var g = hp.gameObject;
                if (g.GetComponent<NetworkObject>() == null) g.AddComponent<NetworkObject>();
                var nt = g.GetComponent<NetworkTransform>() ?? g.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
                nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.Interpolate = true;
                if (g.GetComponent<MonsterNetworkSync>() == null) g.AddComponent<MonsterNetworkSync>();
                PrefabUtility.RecordPrefabInstancePropertyModifications(nt);
                monsters++;
            }
            log.Add("monsters networked: " + monsters);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            RefreshNetworkObjectHashes(scene);
            return "stage2: " + string.Join(" | ", log);
        }

        /// <summary>씬 오브젝트용 NetworkObject 해시는 씬이 저장된 뒤에야 확정된다 → 다시 계산하고 한 번 더 저장</summary>
        static void RefreshNetworkObjectHashes(Scene scene)
        {
            var validate = typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (no.gameObject.scene != scene) continue;
                validate?.Invoke(no, null);
                EditorUtility.SetDirty(no);
                if (PrefabUtility.IsPartOfPrefabInstance(no)) PrefabUtility.RecordPrefabInstancePropertyModifications(no);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        static IEnumerable<Vector3> Ring(Vector3 center, float r0, float r1, float step)
        {
            yield return center;
            for (float r = Mathf.Max(step, r0); r <= r1 + 0.001f; r += step)
            {
                int n = Mathf.Max(6, Mathf.RoundToInt(2 * Mathf.PI * r / step));
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n;
                    yield return center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                }
            }
        }

        static bool FloorPoint(Vector3 c, out Vector3 floor)
        {
            floor = c;
            var hits = Physics.RaycastAll(c + Vector3.up * 1.2f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.GetComponentInParent<FpsHealth>() == null && h.collider.GetComponentInParent<FpsWorldPickup>() == null)
                .OrderBy(h => h.distance).ToArray();
            if (hits.Length == 0 || hits[0].normal.y < 0.9f || Mathf.Abs(hits[0].point.y - 8.07f) > 0.3f) return false;
            floor = hits[0].point;
            return true;
        }

        static bool PlayerPoint(Vector3 c, GameObject ignore, out Vector3 at)
        {
            at = c;
            if (!FloorPoint(c, out var floor)) return false;
            if (!NavMesh.SamplePosition(floor, out var nav, 0.6f, NavMesh.AllAreas)) return false;
            at = floor + Vector3.up * 0.03f;
            foreach (var col in Physics.OverlapCapsule(at + Vector3.up * 0.4f, at + Vector3.up * 1.6f, 0.38f, ~0, QueryTriggerInteraction.Ignore))
                if (ignore == null || !col.transform.IsChildOf(ignore.transform)) return false;
            return true;
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
