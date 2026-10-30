/* [ 코드 설명 ] 기존 FPS 프리팹에 체력·파밍·임시 UI를 추가하고 Character_test에 주사기 3개를 배치합니다. 원본 모델은 중첩 인스턴스로 재사용하며 해당 Player 에셋만 저장합니다. */
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static class FpsFarmingSetup
    {
        public const string SyringePath = "Assets/Player/Prefabs/Item/SciFiMedInjector01_2.prefab";
        public const string PickupPath = "Assets/Player/Prefabs/Item/VOID_LAST_SyringePickup.prefab";
        [MenuItem("VOID LAST/Player/5 - Apply Health and Farming Update")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SyringePath) == null) throw new InvalidOperationException("Missing original syringe prefab.");
            FpsAssetSaveGuard.Active = true;
            try
            {
                BuildPickup(); UpdatePlayer();
                var scene = SceneManager.GetSceneByPath(FpsAssetSetup.ScenePath);
                if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(FpsAssetSetup.ScenePath, OpenSceneMode.Additive);
                var player = scene.GetRootGameObjects().Single(g => g.GetComponent<FpsMotor>() != null);
                if (player.GetComponent<FpsHealthDebugInput>() == null) player.AddComponent<FpsHealthDebugInput>();
                var group = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Farming Test Items");
                if (group == null)
                {
                    group = new GameObject("Farming Test Items"); SceneManager.MoveGameObjectToScene(group, scene);
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath);
                    for (int i = 0; i < 3; i++)
                    {
                        var item = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                        item.name = "Syringe " + (i + 1); item.transform.SetParent(group.transform);
                        Vector3 offset = new Vector3((i - 1) * 0.8f, 0, 1.5f + (i % 2) * 0.3f);
                        Vector3 position = player.transform.position + player.transform.TransformDirection(offset);
                        if (Physics.Raycast(position + Vector3.up * 3, Vector3.down, out var floor, 6, ~0, QueryTriggerInteraction.Ignore)) position.y = floor.point.y + 0.07f;
                        else position.y = 0.07f;
                        item.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 25 + i * 35, 0));
                    }
                }
                SceneManager.SetActiveScene(scene); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                Debug.Log("VOID LAST: health/farming added to existing Player prefab and Character_test only.");
            }
            finally { FpsAssetSaveGuard.Active = false; }
        }
        static T Ensure<T>(GameObject root) where T : Component => root.GetComponent<T>() ?? root.AddComponent<T>();
        static void Reference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target); serialized.FindProperty(field).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static void References(Object target, string field, Object[] values)
        {
            var serialized = new SerializedObject(target); var array = serialized.FindProperty(field); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static GameObject InstantiateModel(Transform parent)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SyringePath), parent.gameObject.scene);
            model.transform.SetParent(parent, false);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return model;
        }
        static void BuildPickup()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath) != null) return;
            var root = new GameObject("VOID_LAST_SyringePickup");
            try
            {
                var model = InstantiateModel(root.transform); model.transform.localScale = Vector3.one * 1.4f;
                var physical = root.AddComponent<BoxCollider>(); physical.center = new Vector3(0, 0, 0.04f); physical.size = new Vector3(0.075f, 0.075f, 0.38f);
                var body = root.AddComponent<Rigidbody>(); body.mass = 0.15f; body.isKinematic = true; body.useGravity = true; body.interpolation = RigidbodyInterpolation.Interpolate;
                var zone = new GameObject("Automatic Pickup Range"); zone.transform.SetParent(root.transform, false);
                var trigger = zone.AddComponent<SphereCollider>(); trigger.isTrigger = true; trigger.radius = 0.65f;
                var pickup = root.AddComponent<FpsWorldPickup>(); Reference(pickup, "pickupTrigger", trigger);
                PrefabUtility.SaveAsPrefabAsset(root, PickupPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        static void UpdatePlayer()
        {
            var player = PrefabUtility.LoadPrefabContents(FpsAssetSetup.PrefabPath);
            try
            {
                Ensure<FpsHealth>(player); Ensure<FpsInventory>(player);
                var interactor = Ensure<FpsItemInteractor>(player); var view = Ensure<FpsItemView>(player); var hud = Ensure<FpsHud>(player);
                var camera = player.GetComponentInChildren<Camera>(true);
                Reference(interactor, "viewCamera", camera); Reference(interactor, "syringePickupPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath).GetComponent<FpsWorldPickup>());
                var itemRoot = camera.transform.Find("ItemRoot");
                if (itemRoot == null)
                {
                    itemRoot = new GameObject("ItemRoot").transform; itemRoot.SetParent(camera.transform, false);
                    var model = InstantiateModel(itemRoot);
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                Reference(view, "itemRoot", itemRoot);
                Reference(view, "pump", itemRoot.GetChild(0).Find("Pump"));
                if (player.transform.Find("FPS HUD") == null) BuildHud(player.transform, hud);
                FpsPresentationSetup.Configure(player);
                var visibility = player.GetComponent<FpsLocalVisibility>();
                var fpsRenderers = camera.GetComponentsInChildren<Renderer>(true);
                References(visibility, "firstPersonRenderers", fpsRenderers.Cast<Object>().ToArray());
                PrefabUtility.SaveAsPrefabAsset(player, FpsAssetSetup.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }
        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        static Image Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var image = Rect(name, parent, anchor, position, size).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        static Text Label(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var text = Rect(name, parent, anchor, position, size).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = fontSize; text.color = Color.white; text.alignment = alignment; text.raycastTarget = false;
            return text;
        }
        static void BuildHud(Transform parent, FpsHud hud)
        {
            var root = Rect("FPS HUD", parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = root.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var hp = Label("Health", root, new Vector2(0, 1), new Vector2(145, -42), new Vector2(240, 36), 25, TextAnchor.MiddleLeft);
            Panel("Health Background", root, new Vector2(0, 1), new Vector2(145, -75), new Vector2(240, 10), new Color(0.08f, 0.1f, 0.12f, 0.85f));
            var fill = Panel("Health Fill", root, new Vector2(0, 1), new Vector2(145, -75), new Vector2(240, 10), new Color(0.3f, 0.9f, 0.75f));
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            var bar = Rect("Farming Hotbar", root, new Vector2(0.5f, 0), new Vector2(0, 76), new Vector2(500, 90));
            var group = bar.gameObject.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            var labels = new Text[3]; var borders = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                Vector2 position = new Vector2((i - 1) * 162, 0);
                borders[i] = Panel("Slot " + (i + 4), bar, Vector2.one * 0.5f, position, new Vector2(150, 80), Color.gray);
                Panel("Background", borders[i].transform, Vector2.one * 0.5f, Vector2.zero, new Vector2(146, 76), new Color(0.06f, 0.09f, 0.12f, 0.95f));
                labels[i] = Label("Label", borders[i].transform, Vector2.one * 0.5f, Vector2.zero, new Vector2(140, 72), 20, TextAnchor.MiddleCenter);
            }
            var dot = Panel("Center Dot", root, Vector2.one * 0.5f, Vector2.zero, Vector2.one * 6, Color.white);
            dot.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var context = Label("Context", root, Vector2.one * 0.5f, new Vector2(0, -75), new Vector2(700, 36), 21, TextAnchor.MiddleCenter);
            Reference(hud, "healthLabel", hp); Reference(hud, "healthFill", fill); Reference(hud, "hotbar", group); Reference(hud, "contextLabel", context);
            References(hud, "slotLabels", labels.Cast<Object>().ToArray()); References(hud, "slotBorders", borders.Cast<Object>().ToArray());
        }
    }
}
