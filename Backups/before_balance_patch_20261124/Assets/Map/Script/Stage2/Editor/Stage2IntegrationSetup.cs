/* [ 코드 설명 ] 기존 Stage2_Ship 구조를 보존하며 F2 화물 구역에 원본 FPS·몬스터 3종·주사기·기존 스캔/보이스 HUD를 연결합니다. 큰 몬스터용 F2 NavMesh는 별도 에셋으로 만들고 기존 데이터와 프로젝트 설정을 보존합니다. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VoidLast.Monster;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.Integration.Editor
{
    public static class Stage2IntegrationSetup
    {
        public const string ScenePath = "Assets/Map/Scenes/Stage2_Ship.unity";
        public const string PlayerPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab";
        public const string PickupPath = "Assets/Player/Prefabs/Item/VOID_LAST_SyringePickup.prefab";
        public const string NavPath = "Assets/Map/Stage2_Ship/Navigation/Stage2_Integration_F2_NavMesh.asset";
        public const string RootName = "VOID_LAST_Integration_Test";
        public static readonly Vector3 PlayerStart = new Vector3(-18, 8.1f, -28.5f);

        [MenuItem("VOID LAST/Integration/1 - Add Stage2 Test Setup If Missing")]
        public static void Create()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath) throw new InvalidOperationException("Open Stage2_Ship in Edit Mode.");
            if (GameObject.Find(RootName) != null) { Debug.Log("Existing integration objects preserved."); return; }
            if (Object.FindObjectsByType<FpsHealth>().Length > 0 || Object.FindObjectsByType<Camera>().Length > 0) throw new InvalidOperationException("Inspect existing player/camera before adding another.");
            var playerAsset = RequiredPrefab(PlayerPath);
            var pickupAsset = RequiredPrefab(PickupPath);
            var eyedAsset = RequiredPrefab("Assets/Monster/Prefabs/VOID_LAST_EyedLargeMonster.prefab");
            var blindAsset = RequiredPrefab("Assets/Monster/Prefabs/VOID_LAST_BlindMonster.prefab");
            var smallAsset = RequiredPrefab("Assets/Monster/Prefabs/VOID_LAST_SmallMonster.prefab");
            BuildF2Navigation();

            var root = new GameObject(RootName);
            var player = Instance(playerAsset, root.transform, SafePoint(PlayerStart), 90);
            var camera = player.GetComponentInChildren<Camera>(true);
            var detector = player.AddComponent<ItemDetector>();
            detector.activateKey = KeyCode.Z;
            detector.xrayShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/item/Shaders/ItemXRayGlow.shader");
            detector.sonarShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/item/Shaders/SonarWave.shader");
            detector.pingShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/item/Shaders/EchoPing.shader");
            player.AddComponent<FpsPickupDetectionBridge>();
            player.AddComponent<MonsterMeleeTestBridge>();
            var cameraData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (cameraData != null) { cameraData.requiresDepthTexture = true; PrefabUtility.RecordPrefabInstancePropertyModifications(cameraData); }

            var waypoints = new GameObject("Integration Waypoints").transform; waypoints.SetParent(root.transform, false);
            var eyed = Instance(eyedAsset, root.transform, SafePoint(new Vector3(-20, 8.1f, 10)), 180);
            var blind = Instance(blindAsset, root.transform, SafePoint(new Vector3(-20, 8.1f, -15)), 90);
            var small = Instance(smallAsset, root.transform, SafePoint(new Vector3(10, 8.1f, -10)), 270);
            var patrol = Points(waypoints, "Eyed Patrol", new[] { new Vector3(-20, 8.1f, 0), new Vector3(-20, 8.1f, 10), new Vector3(-10, 8.1f, 10) });
            var wander = Points(waypoints, "Blind Wander", new[] { new Vector3(-20, 8.1f, -25), new Vector3(-20, 8.1f, -20), new Vector3(-20, 8.1f, -10) });
            var smallPoints = Points(waypoints, "Small Search", new[] { new Vector3(10, 8.1f, -10), new Vector3(10, 8.1f, 10), new Vector3(10, 8.1f, -20) });
            var candidates = new List<Vector3>();
            for (int x = -30; x <= -10; x += 5) for (int z = -25; z <= 15; z += 5) candidates.Add(new Vector3(x, 8.1f, z));
            var search = Points(waypoints, "Eyed Search", candidates.ToArray());
            SetPoints(eyed.GetComponent<EyedMonsterAI>(), "patrolPoints", patrol);
            SetPoints(eyed.GetComponent<EyedMonsterAI>(), "searchPoints", search);
            SetPoints(blind.GetComponent<BlindMonsterAI>(), "wanderPoints", wander);
            SetPoints(small.GetComponent<SmallMonsterAI>(), "searchPoints", smallPoints);
            foreach (var monster in new[] { eyed, blind, small })
            {
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(monster.transform.position, player.transform.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("No complete path to player from " + monster.name);
            }

            var items = new GameObject("Integration Pickups").transform; items.SetParent(root.transform, false);
            foreach (var offset in new[] { new Vector3(0.9f, 0, -0.45f), new Vector3(1.4f, 0, 0), new Vector3(1.9f, 0, 0.45f) })
            {
                var pickup = Instance(pickupAsset, items, player.transform.position + offset + Vector3.up * 0.2f, 90);
                pickup.AddComponent<DetectableItem>();
                var bounds = pickup.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                Physics.SyncTransforms();
                // Use floor below the body, ignoring this newly instantiated pickup itself.
                var hits = Physics.RaycastAll(pickup.transform.position + Vector3.up, Vector3.down, 2, ~0, QueryTriggerInteraction.Ignore).Where(h => !h.transform.IsChildOf(pickup.transform)).OrderBy(h => h.distance).ToArray();
                if (hits.Length == 0) throw new InvalidOperationException("No floor under pickup.");
                pickup.transform.position += Vector3.up * (hits[0].point.y + 0.015f - bounds.min.y);
                PrefabUtility.RecordPrefabInstancePropertyModifications(pickup.transform);
            }

            var voiceObject = new GameObject("VoiceChat - Existing Vivox Prototype"); voiceObject.transform.SetParent(root.transform, false);
            var document = voiceObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/VoiceChat/UI/RadioPanelSettings.asset");
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/VoiceChat/UI/RadioHUD.uxml");
            var voice = voiceObject.AddComponent<StandaloneRadioTest>(); voice.dummyPlayerTransform = player.transform; voice.placeHudTopRight = true;
            if (document.panelSettings == null || document.visualTreeAsset == null) throw new InvalidOperationException("Voice UI references missing.");
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = player;
            Debug.Log("Stage2 integration saved: FPS, three existing monsters, three scannable syringe pickups, Z scan and existing Vivox HUD. Original prefabs preserved.");
        }

        static GameObject RequiredPrefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidOperationException("Missing prefab: " + path);
        static GameObject Instance(GameObject asset, Transform parent, Vector3 position, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0)); PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform); return go;
        }
        public static Vector3 SafePoint(Vector3 point)
        {
            if (!TryPoint(point, out var result)) throw new InvalidOperationException("Blocked/off-mesh placement: " + point);
            return result;
        }
        static bool TryPoint(Vector3 point, out Vector3 result)
        {
            result = point;
            if (!NavMesh.SamplePosition(point, out var hit, 1, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - 8.1f) > 0.5f) return false;
            result = hit.position + Vector3.up * 0.03f;
            return !Physics.CheckCapsule(result + Vector3.up * 0.71f, result + Vector3.up * 2.09f, 0.69f, ~0, QueryTriggerInteraction.Ignore);
        }
        static Transform[] Points(Transform parent, string label, Vector3[] positions)
        {
            var points = new List<Transform>();
            foreach (var position in positions)
            {
                if (!TryPoint(position, out var sample)) continue;
                var point = new GameObject(label + " " + (points.Count + 1)).transform; point.SetParent(parent, false); point.position = sample; points.Add(point);
            }
            if (points.Count == 0) throw new InvalidOperationException("No usable " + label + " points.");
            return points.ToArray();
        }
        static void SetPoints(Component component, string name, Transform[] points)
        {
            var data = new SerializedObject(component); var field = data.FindProperty(name); field.arraySize = points.Length;
            for (int i = 0; i < points.Length; i++) field.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/'); Folder(path.Substring(0, i)); AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
        }
        static void BuildF2Navigation()
        {
            var surface = Object.FindObjectsByType<NavMeshSurface>().Single(s => s.name == "F2_Cargo");
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath) != null) { surface.RemoveData(); surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath); surface.AddData(); return; }
            var settings = surface.GetBuildSettings(); settings.agentRadius = 0.85f; settings.agentHeight = 2.8f; settings.agentClimb = 0.3f; settings.overrideVoxelSize = true; settings.voxelSize = 0.12f;
            var bounds = new Bounds(new Vector3(-1.75f, 10, 3), new Vector3(145, 5, 115));
            var markups = new List<NavMeshBuildMarkup>();
            foreach (var modifier in NavMeshModifier.activeModifiers)
                if (modifier.AffectsAgentType(settings.agentTypeID)) markups.Add(new NavMeshBuildMarkup { root = modifier.transform, overrideArea = modifier.overrideArea, area = modifier.area, ignoreFromBuild = modifier.ignoreFromBuild });
            var sources = new List<NavMeshBuildSource>(); NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            sources.RemoveAll(s => s.component != null && (s.component.GetComponentInParent<NavMeshAgent>() != null || s.component.GetComponent<NavMeshObstacle>() != null || s.component is Collider c && c.isTrigger));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("F2 integration NavMesh build failed.");
            Folder("Assets/Map/Stage2_Ship/Navigation"); AssetDatabase.CreateAsset(data, NavPath);
            surface.RemoveData(); surface.navMeshData = data; surface.AddData();
            EditorUtility.SetDirty(surface); PrefabUtility.RecordPrefabInstancePropertyModifications(surface);
            Directory.CreateDirectory("Docs/IntegrationReports");
            File.WriteAllText("Docs/IntegrationReports/Navigation.txt", "F2-only integration bake: radius=0.85 height=2.8 climb=0.3 voxel=0.12; bounds=" + bounds + "; sources=" + sources.Count + "\nOriginal seven map NavMesh assets preserved. F2 scene surface references separate integration data; other six surfaces remain unchanged. ProjectSettings and Packages unchanged.\n");
        }
    }
}
