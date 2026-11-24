// 층 씬마다 고치 시스템을 놓고, 고치가 생길 수 있는 자리를 골라 저장한다.
// 메뉴: Tools/VOID LAST/Multiplayer/6b Build Cocoon System (층 씬 5개에 적용. 다시 돌려도 안전)
//  - 자리 고르는 법: 방마다 문에서 출발해 대원이 지나갈 수 있는 칸만 따라가고(걸어서 갈 수 있는 곳), 그중 고치가 들어갈 공간이 있고 길을 막지 않는 칸을 고른다.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class CocoonSetup
    {
        const string LayoutPath = "Assets/Map/Stage2_Ship/ship_layout_v2.json";
        const string PlayerPrefabPath = "Assets/Player/Prefabs/Player/VOID_LAST_FPS_Network.prefab";
        const float CellSize = 0.8f, SpotSpacing = 4f, DoorClearance = 2.5f;
        const int SpotsPerRoom = 4;

        [Serializable] class Layout { public LayoutFloor[] floors; }
        [Serializable] class LayoutFloor { public int n; public float y; public LayoutRoom[] rooms; public LayoutDoor[] doors; }
        [Serializable] class LayoutRoom { public string id, name; public float x0, z0, x1, z1; }
        [Serializable] class LayoutDoor { public string o, e1, e2; public float line, a, b; }

        [MenuItem("Tools/VOID LAST/Multiplayer/6b Build Cocoon System")]
        public static string BuildAllFloors()
        {
            var log = new List<string>();
            for (int floor = 5; floor >= 1; floor--)
            {
                var scene = EditorSceneManager.OpenScene(FloorSceneBuilder.ScenePath(floor), OpenSceneMode.Single);
                log.Add(scene.name + ": " + BuildInOpenScene(floor));
                StairDoorSetup.SaveWithNetworkHashes(scene);
            }
            string s = string.Join("\n", log);
            Debug.Log("[CocoonSetup] " + s);
            return s;
        }

        /// <summary>지금 열려 있는 층 씬에 고치 시스템을 (다시) 만든다. 저장은 하지 않음</summary>
        public static string BuildInOpenScene(int floor)
        {
            var multi = GameObject.Find(MultiplayerSetup.MultiRootName);
            if (multi == null) throw new InvalidOperationException("먼저 Multiplayer 설정을 실행하세요");
            var old = multi.transform.Find("Cocoon System");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var go = new GameObject("Cocoon System");
            go.transform.SetParent(multi.transform, false);
            go.AddComponent<NetworkObject>();
            var system = go.AddComponent<CocoonSystem>();
            system.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);
            system.material = F2MissionItemAssets.Mat("M_Cocoon", new Color(0.32f, 0.08f, 0.07f), 0f, 0.65f, new Color(0.25f, 0.02f, 0.02f));

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var body = player != null ? player.GetComponent<CharacterController>() : null;
            if (body != null) system.size = new Vector3(body.radius * 2f, body.height, body.radius * 2f) * 1.2f;

            Physics.SyncTransforms();
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            LayoutFloor data = null;
            foreach (var f in layout.floors) if (f.n == floor) data = f;
            if (data == null) throw new InvalidOperationException(floor + "층 배치 정보 없음");

            var spots = new List<Vector3>();
            var counts = new List<string>();
            for (int i = 0; i < data.rooms.Length; i++)
            {
                int before = spots.Count;
                FindSpots(data, data.rooms[i], system.size, new System.Random(floor * 100 + i), spots);
                counts.Add(data.rooms[i].name + " " + (spots.Count - before));
            }
            system.spawnSpots = spots.ToArray();
            EditorUtility.SetDirty(system);
            return "고치 자리 " + spots.Count + "곳 (" + string.Join(", ", counts) + ")";
        }

        static void FindSpots(LayoutFloor floor, LayoutRoom room, Vector3 size, System.Random random, List<Vector3> output)
        {
            int nx = Mathf.Max(1, Mathf.FloorToInt((room.x1 - room.x0) / CellSize));
            int nz = Mathf.Max(1, Mathf.FloorToInt((room.z1 - room.z0) / CellSize));
            Vector3 Point(int x, int z) => new Vector3(room.x0 + (x + 0.5f) * CellSize, floor.y, room.z0 + (z + 0.5f) * CellSize);

            // 1) 문에서 출발해 걸어갈 수 있는 칸만 표시한다
            var doors = new List<Vector3>();
            var reached = new bool[nx, nz];
            var queue = new Queue<Vector2Int>();
            string me = "R_" + room.id;
            foreach (var door in floor.doors)
            {
                if (door.e1 != me && door.e2 != me) continue;
                float mid = (door.a + door.b) * 0.5f;
                var center = door.o == "x" ? new Vector3(door.line, floor.y, mid) : new Vector3(mid, floor.y, door.line);
                doors.Add(center);
                foreach (var sign in new[] { -1f, 1f })
                {
                    var seed = center + (door.o == "x" ? Vector3.right : Vector3.forward) * (sign * 1.2f);
                    int x = Mathf.FloorToInt((seed.x - room.x0) / CellSize), z = Mathf.FloorToInt((seed.z - room.z0) / CellSize);
                    if (x < 0 || z < 0 || x >= nx || z >= nz || reached[x, z] || !CanStand(Point(x, z), floor.y)) continue;
                    reached[x, z] = true;
                    queue.Enqueue(new Vector2Int(x, z));
                }
            }
            var steps = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var step in steps)
                {
                    var next = cell + step;
                    if (next.x < 0 || next.y < 0 || next.x >= nx || next.y >= nz || reached[next.x, next.y] || !CanStand(Point(next.x, next.y), floor.y)) continue;
                    reached[next.x, next.y] = true;
                    queue.Enqueue(next);
                }
            }

            // 2) 고치가 들어갈 공간이 있고, 주변이 트여 있어 길을 막지 않는 칸을 후보로 한다
            var candidates = new List<Vector3>();
            var half = new Vector3(size.x * 0.75f, size.y * 0.5f - 0.05f, size.z * 0.75f);   // 돌아가 있어도 들어가도록 조금 넉넉하게
            for (int x = 1; x < nx - 1; x++)
                for (int z = 1; z < nz - 1; z++)
                {
                    if (!reached[x, z]) continue;
                    bool open = true;
                    for (int dx = -1; dx <= 1 && open; dx++) for (int dz = -1; dz <= 1 && open; dz++) if (!reached[x + dx, z + dz]) open = false;
                    if (!open) continue;
                    var p = Point(x, z);
                    bool nearDoor = false;
                    foreach (var door in doors) if (Vector3.Distance(p, door) < DoorClearance) { nearDoor = true; break; }
                    if (nearDoor) continue;
                    if (Physics.CheckBox(p + Vector3.up * (size.y * 0.5f + 0.05f), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    candidates.Add(p);
                }

            // 3) 섞어서 서로 떨어진 자리를 몇 곳 고른다
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var swap = candidates[i]; candidates[i] = candidates[j]; candidates[j] = swap;
            }
            var chosen = new List<Vector3>();
            foreach (var p in candidates)
            {
                bool close = false;
                foreach (var c in chosen) if (Vector3.Distance(p, c) < SpotSpacing) { close = true; break; }
                if (close) continue;
                chosen.Add(p);
                if (chosen.Count >= SpotsPerRoom) break;
            }
            output.AddRange(chosen);
        }

        /// <summary>그 칸에 바닥이 있고 대원이 설 공간이 있는지</summary>
        static bool CanStand(Vector3 p, float floorY)
        {
            if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 1.6f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(hit.point.y - floorY) > 0.25f) return false;
            return !Physics.CheckCapsule(p + Vector3.up * 0.65f, p + Vector3.up * 1.6f, 0.35f, ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
#endif
