// Stage2_Ship(5개 층이 모두 들어 있는 원본 씬)을 층마다 따로 된 씬으로 나눈다.
// 메뉴: Tools/VOID LAST/Multiplayer/9 Split Stage2 Into Floor Scenes
//  - 원본: Assets/Map/Scenes/Stage2_Ship.unity (편집은 여기서 하고, 이 메뉴로 다시 나누면 됨)
//  - 결과: Assets/Map/Scenes/Floors/Stage2_Ship_1f ~ 5f.unity
//  - 각 층 씬에는 그 층 + 위층에서 내려오는 계단(도착 연출용)만 남기고, 층 이동(FloorTransition)·도착 지점을 넣는다.
//  - 빌드 설정: 로비 + 층 씬 5개 (원본은 빌드에서 뺌), 로비 게임 시작 씬 = 5층
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class FloorSceneBuilder
    {
        public const string MasterPath = "Assets/Map/Scenes/Stage2_Ship.unity";
        public const string FloorDir = "Assets/Map/Scenes/Floors";
        public const string FirstFloorScene = "Stage2_Ship_5f";
        static readonly string[] FloorRoots = { "", "F1_Hangar", "F2_Cargo", "F3_Control", "F4_Power", "F5_Crew" };
        static readonly string[] StartMarkers = { "", "F1_SavePoint_W", "", "F3_SavePoint_W", "F4_SavePoint_E", "F5_PlayerStart_NW" };

        public static string SceneName(int floor) => "Stage2_Ship_" + floor + "f";
        public static string ScenePath(int floor) => FloorDir + "/" + SceneName(floor) + ".unity";

        [MenuItem("Tools/VOID LAST/Multiplayer/9 Split Stage2 Into Floor Scenes")]
        public static string BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(FloorDir)) AssetDatabase.CreateFolder("Assets/Map/Scenes", "Floors");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // 지우고 다시 만들 층 씬이 열려 있지 않도록
            var log = new List<string>();
            for (int f = 5; f >= 1; f--) log.Add(BuildFloor(f));
            UpdateBuildSettings();
            log.Add(PatchLobby());
            AssetDatabase.SaveAssets();
            string s = string.Join("\n", log);
            Debug.Log("[FloorSceneBuilder]\n" + s);
            return s;
        }

        public static string BuildFloor(int floor)
        {
            string path = ScenePath(floor);
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            if (!AssetDatabase.CopyAsset(MasterPath, path)) throw new Exception("씬 복사 실패: " + path);
            AssetDatabase.ImportAsset(path);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            float Y = (floor - 1) * 8f;
            var log = new List<string>();

            var ship = Root(scene, "VOID_LAST_Ship");
            // 1) 계단 연출에 필요한 이웃 층 부분을 떼어 둔다
            //    - 위층(F n+1): 위에서 내려오는 계단(경사로·중간참)과 그 둘레 → 도착 연출
            //    - 아래층(F n-1): 이 층에서 내려가는 계단의 아래쪽 벽 → 내려가는 연출 (없으면 계단 밖 우주가 보임)
            if (floor < 5) log.Add("stairwell up " + Borrow(ship.transform, floor, floor + 1, Y, Y + 8.5f));
            if (floor > 1) log.Add("stairwell down " + Borrow(ship.transform, floor, floor - 1, Y - 8.5f, Y - 0.05f));

            // 2) 다른 층 지우기
            for (int f = 1; f <= 5; f++)
                if (f != floor) Kill(ship.transform.Find(FloorRoots[f]));
            var interior = Root(scene, "Stage2_Ship_Interior", false);
            if (interior != null)
                foreach (var t in interior.transform.Cast<Transform>().ToList())
                    if (!t.name.StartsWith("F" + floor + "_")) Kill(t);
            var hull = Root(scene, "VOID_LAST_Hull", false);
            if (hull != null)
            {
                if (floor != 1) Kill(hull.transform.Find("LaunchHangar"));
                if (floor != 5) Kill(hull.transform.Find("Cockpit"));
            }

            // 3) 테스트/몬스터: 2층에만
            var test = Root(scene, "VOID_LAST_Integration_Test", false);
            var multi = Root(scene, MultiplayerSetup.MultiRootName);
            Vector3 start = Vector3.zero; Quaternion startRot = Quaternion.identity;
            if (floor != 2)
            {
                if (test != null)
                    foreach (var t in test.transform.Cast<Transform>().ToList())
                        if (t.name.Contains("Monster") || t.name == "Integration Waypoints" || t.name == "Integration Pickups") Kill(t);
                Kill(multi.transform.Find("F2 Mission"));
                var marker = FindByName(scene, StartMarkers[floor]);
                start = marker != null ? marker.position : new Vector3(-38.5f, Y, 0f);
                start.y = Y + 0.03f;
                startRot = marker != null ? Quaternion.Euler(0, marker.eulerAngles.y, 0) : Quaternion.identity;
                // 씬을 직접 실행할 때 쓰는 1인 테스트 대원 위치
                var solo = test != null ? test.transform.Find("VOID_LAST_FPS") : null;
                if (solo != null) solo.SetPositionAndRotation(start, startRot);
                // 로비에서 바로 이 층으로 시작할 때의 스폰 지점
                var spawns = multi.transform.Find("Player Spawns");
                if (spawns != null)
                {
                    var offs = new[] { new Vector3(0, 0, 0), new Vector3(-0.9f, 0, 0.6f), new Vector3(0.9f, 0, 0.6f), new Vector3(0, 0, -0.9f) };
                    int i = 0;
                    foreach (Transform sp in spawns) { sp.SetPositionAndRotation(start + offs[i % offs.Length], startRot); i++; }
                }
            }

            // 4) 계단 문 (이 층 것만)
            log.Add(StairDoorSetup.BuildInOpenScene());

            // 5) 계단 문 앞 도착 지점 (아래층으로 내려왔을 때 대원 스폰 위치)
            Kill(multi.transform.Find("Arrival Points"));
            var arrival = new GameObject("Arrival Points").transform;
            arrival.SetParent(multi.transform, false);
            foreach (var side in new[] { 'A', 'B' })
            {
                float s = side == 'A' ? -1f : 1f;
                Physics.SyncTransforms();
                var face = BestFacing(s, Y);   // 문을 등지고 가장 트인 복도 쪽을 봄
                var pts = new[] { new Vector3(38.4f, 0, -0.7f), new Vector3(38.4f, 0, 0.7f), new Vector3(37.0f, 0, -0.7f), new Vector3(37.0f, 0, 0.7f) };
                for (int i = 0; i < pts.Length; i++)
                {
                    var p = new GameObject("Arrival_" + side + "_" + (i + 1)).transform;
                    p.SetParent(arrival, false);
                    p.SetPositionAndRotation(new Vector3(s * pts[i].x, Y + 0.03f, pts[i].z), face);
                }
            }

            // 6) 층 이동
            Kill(multi.transform.Find("Floor Transition"));
            var ftGo = new GameObject("Floor Transition");
            ftGo.transform.SetParent(multi.transform, false);
            ftGo.AddComponent<NetworkObject>();
            var ft = ftGo.AddComponent<FloorTransition>();
            ft.floor = floor;
            ft.nextSceneName = floor > 1 ? SceneName(floor - 1) : "";
            ft.descentSides = floor == 2 ? "A" : "AB";   // 2층은 기획상 A 계단으로만 내려간다
            ft.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);

            StairDoorSetup.SaveWithNetworkHashes(scene);
            int renderers = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Renderer>(true).Length);
            int lights = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<Light>(true).Length);
            return SceneName(floor) + ": renderers " + renderers + ", lights " + lights + " | " + string.Join(" | ", log);
        }

        const string StairwellMeshDir = "Assets/Map/Stage2_Ship/FloorStairwell";

        /// <summary>
        /// 이웃 층(from)에서 계단실 안쪽 부분만 잘라 이 층에 넣는다 (계단 연출용).
        /// 이웃 층의 벽 메시는 여러 방이 한 덩어리로 합쳐져 있으므로, 계단실 안에 있는 삼각형만 남긴 새 메시를 만든다.
        /// → 이웃 층의 방·복도·소품은 하나도 남지 않고, 계단(경사로·중간참)과 계단실 벽·비상등만 남는다.
        /// </summary>
        static int Borrow(Transform ship, int floor, int from, float yMin, float yMax)
        {
            var src = ship.Find(FloorRoots[from]);
            if (src == null) return 0;
            if (!AssetDatabase.IsValidFolder(StairwellMeshDir)) AssetDatabase.CreateFolder("Assets/Map/Stage2_Ship", "FloorStairwell");
            var group = new GameObject("F" + floor + (from > floor ? "_Stairwell_Up" : "_Stairwell_Down") + " (계단 연출용, F" + from + " 계단실 조각)").transform;
            group.SetParent(ship, false);
            float cy = (yMin + yMax) * 0.5f, h = yMax - yMin;
            // 계단실 안쪽만 (x 41.4~58.7, z -8.7~8.7)
            var regions = new[]
            {
                new Bounds(new Vector3(-50.05f, cy, 0f), new Vector3(17.3f, h, 17.4f)),
                new Bounds(new Vector3(50.05f, cy, 0f), new Vector3(17.3f, h, 17.4f)),
            };
            int count = 0;
            var visual = src.Find("Visual");
            if (visual != null)
                foreach (Transform c in visual)
                {
                    var mf = c.GetComponent<MeshFilter>();
                    var mr = c.GetComponent<MeshRenderer>();
                    if (mf == null || mr == null || mf.sharedMesh == null || !regions.Any(b => b.Intersects(mr.bounds))) continue;
                    var mesh = Clip(mf, regions);
                    if (mesh == null) continue;
                    string path = StairwellMeshDir + "/F" + floor + "_from" + from + "_" + c.name + ".asset";
                    if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(mesh, path);
                    var go = new GameObject(c.name + " (계단실)");
                    go.layer = c.gameObject.layer;
                    go.transform.SetParent(group, false);
                    go.transform.SetPositionAndRotation(c.position, c.rotation);
                    go.transform.localScale = c.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = mr.sharedMaterials;
                    r.shadowCastingMode = mr.shadowCastingMode;
                    r.receiveShadows = mr.receiveShadows;
                    GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(c.gameObject));
                    count++;
                }
            // 계단실 안에 달린 비상등 (계단실을 비추는 조명)
            var lights = src.Find("Lights");
            if (lights != null)
                foreach (Transform c in lights.Cast<Transform>().ToList())
                {
                    var l = c.GetComponentInChildren<Light>(true);
                    if (l == null || !regions.Any(b => b.Contains(l.transform.position))) continue;
                    if (PrefabUtility.IsPartOfPrefabInstance(c.gameObject) && !PrefabUtility.IsOutermostPrefabInstanceRoot(c.gameObject)) continue;
                    c.SetParent(group, true);
                    count++;
                }
            return count;
        }

        /// <summary>메시에서 무게중심이 regions 안에 있는 삼각형만 남긴 새 메시 (없으면 null)</summary>
        static Mesh Clip(MeshFilter mf, Bounds[] regions)
        {
            var src = mf.sharedMesh;
            var l2w = mf.transform.localToWorldMatrix;
            var v = src.vertices;
            var n = src.normals; var t = src.tangents; var uv = src.uv; var uv2 = src.uv2; var col = src.colors;
            var world = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) world[i] = l2w.MultiplyPoint3x4(v[i]);
            var remap = new Dictionary<int, int>();
            var keep = new List<int>();
            var subs = new List<List<int>>();
            int kept = 0;
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var tris = src.GetTriangles(sm);
                var outTris = new List<int>();
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    var c = (world[tris[i]] + world[tris[i + 1]] + world[tris[i + 2]]) / 3f;
                    bool inside = false;
                    foreach (var b in regions) if (b.Contains(c)) { inside = true; break; }
                    if (!inside) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int idx = tris[i + k];
                        if (!remap.TryGetValue(idx, out int ni)) { ni = keep.Count; remap[idx] = ni; keep.Add(idx); }
                        outTris.Add(ni);
                    }
                    kept++;
                }
                subs.Add(outTris);
            }
            if (kept == 0) return null;
            var m = new Mesh { name = src.name + "_Stairwell" };
            if (keep.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(keep.Select(i => v[i]).ToList());
            if (n.Length == v.Length) m.SetNormals(keep.Select(i => n[i]).ToList());
            if (t.Length == v.Length) m.SetTangents(keep.Select(i => t[i]).ToList());
            if (uv.Length == v.Length) m.SetUVs(0, keep.Select(i => uv[i]).ToList());
            if (uv2.Length == v.Length) m.SetUVs(1, keep.Select(i => uv2[i]).ToList());
            if (col.Length == v.Length) m.SetColors(keep.Select(i => col[i]).ToList());
            m.subMeshCount = subs.Count;
            for (int sm = 0; sm < subs.Count; sm++) m.SetTriangles(subs[sm], sm);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>도착 지점에서 가장 트인 쪽(복도 방향)을 바라보게</summary>
        static Quaternion BestFacing(float s, float Y)
        {
            var origin = new Vector3(s * 37.7f, Y + 1.5f, 0f);
            Vector3 best = new Vector3(-s, 0, 0); float bestDist = -1f;
            foreach (var dir in new[] { new Vector3(-s, 0, 0), Vector3.forward, Vector3.back })
            {
                float d = Physics.Raycast(origin, dir, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 40f;
                if (d > bestDist + 0.5f) { bestDist = d; best = dir; }
            }
            return Quaternion.LookRotation(best);
        }

        static void UpdateBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path == MasterPath) continue;                    // 원본(전체 층)은 빌드에서 뺀다
                if (s.path.StartsWith(FloorDir + "/")) continue;
                list.Add(s);
            }
            for (int f = 5; f >= 1; f--) list.Add(new EditorBuildSettingsScene(ScenePath(f), true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static string PatchLobby()
        {
            var scene = EditorSceneManager.OpenScene(MultiplayerSetup.LobbyScenePath, OpenSceneMode.Single);
            var lm = Object.FindFirstObjectByType<LobbyManager>(FindObjectsInactive.Include);
            if (lm == null) return "lobby: LobbyManager 없음";
            lm.gameSceneName = FirstFloorScene;
            EditorUtility.SetDirty(lm);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "lobby: game start scene → " + FirstFloorScene;
        }

        static GameObject Root(Scene scene, string name, bool required = true)
        {
            var g = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
            if (g == null && required) throw new Exception("루트 오브젝트 없음: " + name);
            return g;
        }

        static Transform FindByName(Scene scene, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var r in scene.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }

        /// <summary>지우기 (프리팹 인스턴스 안에 있으면 먼저 프리팹 연결을 푼다)</summary>
        static void Kill(Transform t)
        {
            if (t == null) return;
            var go = t.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (root != null) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) return;
            }
            Object.DestroyImmediate(go);
        }
    }
}
#endif
