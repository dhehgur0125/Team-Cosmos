/* [ 코드 설명 ] 기존 리소스를 보존하며 Monster 내부에 Capsule 프리팹·두 방과 복도 테스트 씬·NavMesh 데이터를 생성합니다. 이미 있는 씬은 덮어쓰지 않으며 프로젝트 Agent 설정은 변경하지 않습니다. */
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEngine.SceneManagement;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.Monster.Editor
{
    public static class MonsterPrototypeSetup
    {
        public const string ScenePath = "Assets/Monster/Scenes/Monster_test.unity";
        public const string PrefabPath = "Assets/Monster/Prefabs/VOID_LAST_EyedLargeMonster.prefab";
        public const string PlayerPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS.prefab";
        [MenuItem("VOID LAST/Monster/1 - Create Prototype Scene If Missing")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            Folder("Assets/Monster/Prefabs"); Folder("Assets/Monster/Materials"); Folder("Assets/Monster/Scenes"); Folder("Assets/Monster/Validation");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) { Debug.Log("Existing Monster_test preserved. Open it to continue."); return; }
            var playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            if (playerAsset == null || playerAsset.GetComponent<FpsPistol>() == null) throw new InvalidOperationException("Existing FPS prefab was not found.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            bool saved = false;
            try
            {
                var floorMaterial = Material("PrototypeFloor", new Color(0.18f, 0.22f, 0.25f));
                var wallMaterial = Material("PrototypeWalls", new Color(0.36f, 0.4f, 0.44f));
                var obstacleMaterial = Material("PrototypeObstacles", new Color(0.4f, 0.24f, 0.1f));
                var geometry = new GameObject("Monster Test Geometry");
                Cube("Floor", new Vector3(0, -0.25f, 0), new Vector3(32, 0.5f, 32), floorMaterial, geometry.transform);
                Cube("West boundary", new Vector3(-16, 1.75f, 0), new Vector3(0.4f, 3.5f, 32), wallMaterial, geometry.transform);
                Cube("East boundary", new Vector3(16, 1.75f, 0), new Vector3(0.4f, 3.5f, 32), wallMaterial, geometry.transform);
                Cube("North boundary", new Vector3(0, 1.75f, 16), new Vector3(32, 3.5f, 0.4f), wallMaterial, geometry.transform);
                Cube("South boundary", new Vector3(0, 1.75f, -16), new Vector3(32, 3.5f, 0.4f), wallMaterial, geometry.transform);
                Wall("Hall west south", -3, -8, 0, wallMaterial, geometry.transform);
                Wall("Hall west north", -3, 3, 12, wallMaterial, geometry.transform);
                Wall("Hall east south", 3, -8, 3.5f, wallMaterial, geometry.transform);
                Wall("Hall east north", 3, 6.5f, 12, wallMaterial, geometry.transform);
                foreach (float x in new[] { -7.5f, 7.5f })
                {
                    Cube("Room south wall", new Vector3(x, 1.75f, -2), new Vector3(9, 3.5f, 0.4f), wallMaterial, geometry.transform);
                    Cube("Room north wall", new Vector3(x, 1.75f, 10), new Vector3(9, 3.5f, 0.4f), wallMaterial, geometry.transform);
                    Cube("Room outer wall", new Vector3(Mathf.Sign(x) * 12, 1.75f, 4), new Vector3(0.4f, 3.5f, 12), wallMaterial, geometry.transform);
                }
                Cube("Courtyard cover", new Vector3(-4, 1.35f, -11), new Vector3(2.5f, 2.7f, 1.5f), obstacleMaterial, geometry.transform);
                Cube("Hall obstacle", new Vector3(1.35f, 0.85f, -4), new Vector3(1.2f, 1.7f, 1.8f), obstacleMaterial, geometry.transform);
                Cube("Left room cover", new Vector3(-8, 1.4f, 5), new Vector3(2, 2.8f, 1.4f), obstacleMaterial, geometry.transform);
                var patrol = Points("Patrol Points", new[] { new Vector3(0, 0, -7), new Vector3(-1, 0, -1), new Vector3(0, 0, 8), new Vector3(-1, 0, 11) });
                var search = Points("Search Points", new[] { new Vector3(-6, 0, 1.5f), new Vector3(-9, 0, 7), new Vector3(6, 0, 5), new Vector3(9, 0, 8), new Vector3(0, 0, 4), new Vector3(0, 0, -4) });
                Bake(geometry);
                BuildPrefab(playerAsset.GetComponent<FpsPistol>().Damage * 10);
                var monster = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
                monster.transform.SetPositionAndRotation(new Vector3(0, 0.02f, 5), Quaternion.Euler(0, 180, 0));
                var ai = monster.GetComponent<EyedMonsterAI>(); var fields = new SerializedObject(ai);
                SetPoints(fields.FindProperty("patrolPoints"), patrol); SetPoints(fields.FindProperty("searchPoints"), search); fields.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerAsset, scene); player.transform.position = new Vector3(0, 0.03f, -13);
                player.AddComponent<MonsterMeleeTestBridge>();
                var sunlight = new GameObject("Prototype Sun", typeof(Light)); var light = sunlight.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; sunlight.transform.rotation = Quaternion.Euler(45, -35, 0);
                RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.4f);
                EditorSceneManager.SaveScene(scene, ScenePath); saved = true;
                if (previous.IsValid() && previous.isLoaded && !previous.isDirty) EditorSceneManager.CloseScene(previous, true);
                Selection.activeGameObject = monster;
                Debug.Log("Monster_test created with baked navigation, 2 rooms, one FPS instance and one monster. Pistol damage=" + playerAsset.GetComponent<FpsPistol>().Damage);
            }
            finally { if (!saved) { EditorSceneManager.CloseScene(scene, true); if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); } }
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return; int slash = path.LastIndexOf('/'); Folder(path.Substring(0, slash)); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
        static Material Material(string name, Color color)
        {
            string path = "Assets/Monster/Materials/" + name + ".mat"; var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color }; AssetDatabase.CreateAsset(material, path); return material;
        }
        static void Cube(string name, Vector3 position, Vector3 size, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
        }
        static void Wall(string name, float x, float from, float to, Material material, Transform parent) => Cube(name, new Vector3(x, 1.75f, (from + to) * 0.5f), new Vector3(0.4f, 3.5f, to - from), material, parent);
        static Transform[] Points(string name, Vector3[] positions)
        {
            var root = new GameObject(name); var result = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++) { result[i] = new GameObject(name + " " + (i + 1)).transform; result[i].SetParent(root.transform); result[i].position = positions[i]; }
            return result;
        }
        static void SetPoints(SerializedProperty field, Transform[] points) { field.arraySize = points.Length; for (int i = 0; i < points.Length; i++) field.GetArrayElementAtIndex(i).objectReferenceValue = points[i]; }
        [MenuItem("VOID LAST/Monster/3 - Rebake Prototype Navigation")]
        public static void Rebake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open Monster_test in Edit Mode first.");
            var geometry = GameObject.Find("Monster Test Geometry");
            if (geometry == null) throw new InvalidOperationException("Monster test geometry was not found.");
            Bake(geometry, true);
            Debug.Log("Monster navigation rebaked with 0.85m clearance for the 0.7m capsule. Project Agent settings unchanged.");
        }
        static void Bake(GameObject geometry, bool updateExisting = false)
        {
            // Leave corner/voxel clearance beyond the physical 0.7m capsule radius.
            var settings = NavMesh.GetSettingsByIndex(0); settings.agentRadius = 0.85f; settings.agentHeight = 2.8f; settings.agentClimb = 0.25f;
            settings.overrideVoxelSize = true; settings.voxelSize = 0.12f;
            var markups = new List<NavMeshBuildMarkup>();
            foreach (var collider in geometry.GetComponentsInChildren<Collider>())
                if (collider.name != "Floor") markups.Add(new NavMeshBuildMarkup { root = collider.transform, overrideArea = true, area = 1 });
            var sources = new List<NavMeshBuildSource>(); NavMeshBuilder.CollectSources(geometry.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            const string path = "Assets/Monster/Scenes/Monster_test_NavMesh.asset";
            var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            var bounds = new Bounds(Vector3.zero, new Vector3(40, 12, 40));
            if (data != null)
            {
                if (!updateExisting) throw new InvalidOperationException("Existing NavMesh data preserved; inspect before rebuilding.");
                if (!NavMeshBuilder.UpdateNavMeshData(data, settings, sources, bounds)) throw new InvalidOperationException("NavMesh update failed.");
                EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            }
            else
            {
                data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
                if (data == null) throw new InvalidOperationException("NavMesh build failed.");
                AssetDatabase.CreateAsset(data, path);
            }
            var surface = geometry.GetComponent<NavMeshSurface>(); if (surface == null) surface = geometry.AddComponent<NavMeshSurface>();
            surface.RemoveData(); surface.agentTypeID = settings.agentTypeID; surface.collectObjects = CollectObjects.Children; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.navMeshData = data; surface.AddData();
        }
        static void BuildPrefab(float maximumHealth)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
            var root = new GameObject("VOID_LAST_EyedLargeMonster");
            try
            {
                var agent = root.AddComponent<NavMeshAgent>(); agent.agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID; agent.radius = 0.7f; agent.height = 2.8f; agent.speed = 2; agent.angularSpeed = 240; agent.acceleration = 16; agent.stoppingDistance = 0.2f;
                var collider = root.AddComponent<CapsuleCollider>(); collider.radius = 0.7f; collider.height = 2.8f; collider.center = Vector3.up * 1.4f;
                var hp = root.AddComponent<MonsterHealth>(); var hpFields = new SerializedObject(hp); hpFields.FindProperty("maximumHealth").floatValue = maximumHealth; hpFields.ApplyModifiedPropertiesWithoutUndo();
                root.AddComponent<MonsterVision>(); root.AddComponent<MonsterMelee>(); var ai = root.AddComponent<EyedMonsterAI>();
                var pivot = new GameObject("Capsule Visual").transform; pivot.SetParent(root.transform, false);
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name = "Body"; body.transform.SetParent(pivot, false); body.transform.localPosition = Vector3.up * 1.4f; body.transform.localScale = Vector3.one * 1.4f; Object.DestroyImmediate(body.GetComponent<Collider>());
                body.GetComponent<Renderer>().sharedMaterial = Material("MonsterBody", new Color(0.22f, 0.45f, 0.52f));
                foreach (float x in new[] { -0.22f, 0.22f })
                {
                    var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere); eye.name = "Eye"; eye.transform.SetParent(pivot, false); eye.transform.localPosition = new Vector3(x, 2.3f, 0.55f); eye.transform.localScale = Vector3.one * 0.18f; Object.DestroyImmediate(eye.GetComponent<Collider>()); eye.GetComponent<Renderer>().sharedMaterial = Material("MonsterEyes", new Color(0.95f, 0.85f, 0.35f));
                }
                Cube("Forward marker", new Vector3(0, 1.8f, 0.76f), new Vector3(0.14f, 0.14f, 0.35f), Material("MonsterEyes", Color.yellow), pivot); Object.DestroyImmediate(pivot.Find("Forward marker").GetComponent<Collider>());
                var fields = new SerializedObject(ai); fields.FindProperty("visualPivot").objectReferenceValue = pivot; fields.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>(); fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
