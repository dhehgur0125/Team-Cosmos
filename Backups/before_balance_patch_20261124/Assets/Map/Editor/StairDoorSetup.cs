// 모든 층의 계단(A·B) 입구 격벽 문을 F로 열고 닫을 수 있게 설정
// 메뉴: Tools/VOID LAST/Multiplayer/7 Build Stair Doors
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class StairDoorSetup
    {
        const string ScenePath = "Assets/Map/Scenes/Stage2_Ship.unity";

        [MenuItem("Tools/VOID LAST/Multiplayer/7 Build Stair Doors")]
        public static string Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string s = BuildInOpenScene();
            SaveWithNetworkHashes(scene);
            Debug.Log("[StairDoorSetup] " + s);
            return s;
        }

        /// <summary>지금 열려 있는 씬에 남아 있는 계단 문들로 Stair Doors를 (다시) 만든다. 저장은 하지 않음</summary>
        public static string BuildInOpenScene()
        {
            var multi = GameObject.Find(MultiplayerSetup.MultiRootName);
            if (multi == null) throw new InvalidOperationException("먼저 Multiplayer 설정(5 Patch Stage2 Scene)을 실행하세요");
            var old = multi.transform.Find("Stair Doors");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var ship = GameObject.Find("VOID_LAST_Ship");
            if (ship == null) throw new InvalidOperationException("VOID_LAST_Ship 없음");
            var roots = ship.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("F") && t.name.Contains("_Bulkhead_Stair"))
                .OrderBy(t => t.name)
                .ToList();

            var go = new GameObject("Stair Doors");
            go.transform.SetParent(multi.transform, false);
            go.AddComponent<NetworkObject>();
            var ctrl = go.AddComponent<StairDoorController>();
            ctrl.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);

            var doors = new List<StairDoorController.Door>();
            var log = new List<string>();
            foreach (var root in roots)
            {
                EnsureBackPlate(root);   // 이름에 BigDoor가 들어 있어 아래에서 문짝과 함께 움직이는 부품으로 잡힌다
                var blocker = root.GetComponent<BoxCollider>();
                // 문짝 = 렌더러를 가진 큰 문 (데칼은 문짝 자식이라 함께 움직임)
                var leaves = root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != root && t.GetComponent<Renderer>() != null && t.name.Contains("BigDoor"))
                    .ToArray();
                if (leaves.Length == 0)
                    leaves = root.GetComponentsInChildren<Renderer>(true).Select(r => r.transform).Where(t => t != root).ToArray();
                // 움직이는 물체는 정적 배칭에서 빼야 화면에서도 움직임
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

                var old2 = root.Find("Interact Trigger");
                if (old2 != null) Object.DestroyImmediate(old2.gameObject);
                var trig = new GameObject("Interact Trigger");
                trig.layer = 2;   // Ignore Raycast: 총알·다른 상호작용에는 걸리지 않음
                trig.transform.SetParent(root, false);
                var tc = trig.AddComponent<BoxCollider>();
                tc.isTrigger = true;
                if (blocker != null) { tc.center = blocker.center; tc.size = new Vector3(Mathf.Max(blocker.size.x, 1.2f), blocker.size.y, blocker.size.z + 0.2f); }
                else { tc.center = new Vector3(0, 1.75f, 0); tc.size = new Vector3(1.2f, 3.5f, 4.2f); }

                doors.Add(new StairDoorController.Door
                {
                    id = root.name,
                    root = root,
                    leaves = leaves,
                    blocker = blocker,
                    interactTrigger = tc,
                    // 위로 4m 올려 천장(복도 쪽 벽 두께) 속으로 숨김. 계단 쪽으로 0.2m 밀어 벽 안에 쏙 들어가게
                    openOffset = new Vector3(-Mathf.Sign(root.position.x) * 0.2f, 4f, 0f),
                });
                log.Add(root.name + "(" + leaves.Length + ")");
            }
            ctrl.doors = doors.ToArray();
            return "Stair doors: " + doors.Count + " → " + string.Join(", ", log);
        }

        const string BackPlateName = "BigDoor Back Plate";

        /// <summary>
        /// 문짝 모서리 틈을 가리는 뒷판. 문이 열릴 때 함께 올라가도록 벽 메시가 아니라 문에 붙인다.
        /// 문짝 두께 한가운데에 있어 앞뒤로 튀어나오지 않고, 올라가면 문짝과 함께 벽 속으로 들어간다.
        /// </summary>
        public static Transform EnsureBackPlate(Transform holder)
        {
            var existing = holder.Find(BackPlateName);
            if (existing != null) return existing;
            var box = holder.GetComponent<BoxCollider>();
            if (box == null) throw new InvalidOperationException("문에 BoxCollider가 없음: " + holder.name);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = BackPlateName;
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(holder, false);
            plate.transform.localPosition = box.center;
            plate.transform.localScale = box.size.x < box.size.z
                ? new Vector3(0.05f, box.size.y, box.size.z)
                : new Vector3(box.size.x, box.size.y, 0.05f);
            var renderer = plate.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var guid in AssetDatabase.FindAssets("M_Stage2_TrimGrey t:Material"))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat != null && mat.name == "M_Stage2_TrimGrey") { renderer.sharedMaterial = mat; break; }
            }
            return plate.transform;
        }

        /// <summary>
        /// 계단 문 뒷판을 벽 메시에서 지우고 문에 붙인다 (한 번만 필요. 다시 돌려도 안전).
        /// 지우는 대상은 문 위치와 크기가 정확히 맞는 삼각형뿐이다: x = ±42.095~42.145, z = ±2, 높이 = 층 바닥~3.5m
        /// </summary>
        [MenuItem("Tools/VOID LAST/Multiplayer/7b Fix Stair Door Back Plates")]
        public static string FixBackPlates()
        {
            var log = new List<string> { RemoveBakedBackPlates() };
            var paths = new List<string> { ScenePath };
            for (int f = 1; f <= 5; f++) paths.Add(FloorSceneBuilder.ScenePath(f));
            foreach (var path in paths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var ctrl = Object.FindFirstObjectByType<StairDoorController>(FindObjectsInactive.Include);
                if (ctrl == null) { log.Add(scene.name + ": 계단 문 없음"); continue; }
                int added = 0;
                foreach (var door in ctrl.doors)
                {
                    if (door == null || door.root == null) continue;
                    var plate = EnsureBackPlate(door.root);
                    if (door.leaves.Contains(plate)) continue;
                    door.leaves = door.leaves.Concat(new[] { plate }).ToArray();
                    added++;
                }
                EditorUtility.SetDirty(ctrl);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                log.Add(scene.name + ": 뒷판 " + added + "개");
            }
            string s = string.Join("\n", log);
            Debug.Log("[StairDoorSetup] " + s);
            return s;
        }

        static bool IsBakedBackPlateVertex(Vector3 v)
        {
            float x = Mathf.Abs(v.x), floorY = Mathf.Round(v.y / 8f) * 8f;
            const float e = 0.004f;
            return (Mathf.Abs(x - 42.095f) < e || Mathf.Abs(x - 42.145f) < e)
                && Mathf.Abs(Mathf.Abs(v.z) - 2f) < e
                && (Mathf.Abs(v.y - floorY) < e || Mathf.Abs(v.y - floorY - 3.5f) < e);
        }

        static string RemoveBakedBackPlates()
        {
            int meshes = 0, triangles = 0;
            var dirs = new[]
            {
                "Assets/Map/Stage2_Ship/Baked/F1", "Assets/Map/Stage2_Ship/Baked/F2", "Assets/Map/Stage2_Ship/Baked/F3",
                "Assets/Map/Stage2_Ship/Baked/F4", "Assets/Map/Stage2_Ship/Baked/F5", "Assets/Map/Stage2_Ship/FloorStairwell",
            };
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", dirs))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
                if (mesh == null || !mesh.name.Contains("M_Stage2_TrimGrey")) continue;
                var verts = mesh.vertices;
                bool changed = false;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var tris = mesh.GetTriangles(sm);
                    var keep = new List<int>(tris.Length);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        if (IsBakedBackPlateVertex(verts[tris[i]]) && IsBakedBackPlateVertex(verts[tris[i + 1]]) && IsBakedBackPlateVertex(verts[tris[i + 2]])) { triangles++; changed = true; continue; }
                        keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                    }
                    if (keep.Count != tris.Length) mesh.SetTriangles(keep, sm, false);
                }
                if (!changed) continue;
                EditorUtility.SetDirty(mesh);
                meshes++;
            }
            AssetDatabase.SaveAssets();
            return "벽 메시 " + meshes + "개에서 삼각형 " + triangles + "개 제거";
        }

        /// <summary>저장 → 씬에 놓인 NetworkObject 해시 다시 계산 → 다시 저장</summary>
        public static void SaveWithNetworkHashes(UnityEngine.SceneManagement.Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // 씬에 놓인 NetworkObject 해시는 저장 후 확정 → 다시 계산하고 저장
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
    }
}
#endif
