// VOID LAST - 5층 우주선 맵 생성기 (기획서 기반 + 층별 랜덤 레이아웃)
// 레이아웃 데이터: Assets/Map/Stage2_Ship/ship_layout.json (층마다 시드로 생성한 방/복도/벽/출입구)
// 메뉴: Tools/VOID LAST/Build Ship Map (전체 재생성)
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.AI.Navigation;

namespace VoidLast.MapTools
{
    public static class ShipMapBuilder
    {
        public const string RootDir = "Assets/Map/Stage2_Ship";
        public const string ScenePath = "Assets/Map/Scenes/Stage2_Ship.unity";
        const string KitDir = "Assets/Creepy_Cat/3D Scifi Kit Vol 4/Prefabs";
        const string BakedDir = RootDir + "/Baked";
        const float StairDoorX = 42f, Shaft = 16f, FloorStep = 8f;

        // ------------------------------------------------------------------ data
        [Serializable] public class P { public float x, z; }
        [Serializable] public class R { public float x0, z0, x1, z1; }
        [Serializable] public class RoomD { public string id, name; public float h, x0, z0, x1, z1; }
        [Serializable] public class CorD { public string id, a, b, kind; public float w, h; public P[] pts; public R[] rects; }
        [Serializable] public class WallD { public string o, owner, kind; public float line, a, b, y0, y1; public int n; }
        [Serializable] public class DoorD { public string o, e1, e2; public float line, a, b; public int n; }
        [Serializable] public class FloorD { public int n, seed; public float y; public string key, zone; public RoomD[] rooms; public CorD[] cors; public WallD[] walls; public DoorD[] doors; }
        [Serializable] public class LayoutD { public FloorD[] floors; }

        static LayoutD _data;
        static Dictionary<string, GameObject> _kit;
        static Material _trim, _lampMat;
        static GameObject _lightPf, _sparkPf;
        static System.Random _rng;

        static void Load()
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(RootDir + "/ship_layout_v2.json");
            _data = JsonUtility.FromJson<LayoutD>(ta.text);
            _kit = new Dictionary<string, GameObject>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { KitDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var n = System.IO.Path.GetFileNameWithoutExtension(p);
                if (!_kit.ContainsKey(n)) _kit[n] = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            _trim = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_TrimGrey.mat");
            _lampMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/Stage2_NewMap/M_NM_EmergencyLamp.mat");
            _lightPf = AssetDatabase.LoadAssetAtPath<GameObject>(RootDir + "/Prefabs/P_EmergencyLight.prefab");
            _sparkPf = AssetDatabase.LoadAssetAtPath<GameObject>(RootDir + "/Prefabs/P_SparkBurst.prefab");
        }

        static FloorD GetFloor(int n) { foreach (var f in _data.floors) if (f.n == n) return f; return null; }

        // ------------------------------------------------------------------ menu
        [MenuItem("Tools/VOID LAST/Build Ship Map")]
        public static void BuildAll()
        {
            CreateScene();
            for (int n = 1; n <= 5; n++) BuildFloor(n);
            Finish(true);
        }

        public static string CreateScene()
        {
            Load();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.012f, 0.014f, 0.016f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.035f, 0.05f, 0.055f);
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.reflectionIntensity = 0.15f;
            RenderSettings.skybox = null;

            var vol = new GameObject("Global Volume");
            var v = vol.AddComponent<Volume>();
            v.isGlobal = true;
            v.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Creepy_Cat/DefaultVolumeProfile.asset");

            var root = new GameObject("VOID_LAST_Ship");
            root.AddComponent<ShipFloorVisibility>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            return "scene created";
        }

        static Transform ShipRoot()
        {
            var r = GameObject.Find("VOID_LAST_Ship");
            if (r == null) { r = new GameObject("VOID_LAST_Ship"); r.AddComponent<ShipFloorVisibility>(); }
            return r.transform;
        }

        static Transform Child(Transform p, string name)
        {
            var t = p.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(p, false); }
            return t;
        }

        // ------------------------------------------------------------------ floor build
        public static string LastStats = "";

        public static string BuildFloor(int n)
        {
            if (_data == null) Load();
            var F = GetFloor(n);
            _rng = new System.Random(F.seed * 31 + 7);
            var ship = ShipRoot();
            string fname = "F" + n + "_" + F.key;
            var old = ship.Find(fname);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var froot = new GameObject(fname).transform;
            froot.SetParent(ship, false);
            froot.SetSiblingIndex(Mathf.Max(0, n - 1));

            var ctx = new Ctx { F = F, root = froot };
            ctx.structure = Child(froot, "Structure");
            ctx.temp = Child(froot, "_DressingTemp");
            ctx.lights = Child(froot, "Lights");
            ctx.props = Child(froot, "Props");
            ctx.fx = Child(froot, "FX");
            ctx.markers = Child(froot, "Markers");
            ctx.doors = Child(froot, "Bulkheads");
            foreach (var r in F.rooms) ctx.rooms[r.id] = r;
            foreach (var c in F.cors) ctx.cors[(c.kind == "maint" ? "M_" : "K_") + c.id] = c;

            BuildSlabs(ctx);
            BuildWalls(ctx);
            BuildDoors(ctx);
            BuildShafts(ctx);
            BuildPipes(ctx);
            BuildLights(ctx);
            BuildProps(ctx);
            BuildMarkers(ctx);
            BuildFx(ctx);
            int merged = MergeTemp(ctx);

            EditorSceneManager.MarkSceneDirty(froot.gameObject.scene);
            EditorSceneManager.SaveScene(froot.gameObject.scene);
            return fname + ": mergedMeshes=" + merged + " lights=" + ctx.lights.childCount + " props=" + ctx.props.childCount +
                   " colliders=" + froot.GetComponentsInChildren<Collider>(true).Length + " skipped=" + ctx.skipped + " [" + LastStats + "]";
        }

        class Ctx
        {
            public FloorD F;
            public Transform root, structure, temp, lights, props, fx, markers, doors;
            public Dictionary<string, RoomD> rooms = new Dictionary<string, RoomD>();
            public Dictionary<string, CorD> cors = new Dictionary<string, CorD>();
            public List<Bounds> placed = new List<Bounds>();
            public Dictionary<string, List<Rect>> keepOut = new Dictionary<string, List<Rect>>();
            public int skipped;
            public float Y => F.y;
        }

        // ------------------------------------------------------------------ helpers
        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Quaternion? rot = null)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = center;
            g.transform.rotation = rot ?? Quaternion.identity;
            var bc = g.AddComponent<BoxCollider>();
            bc.size = size;
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return g;
        }

        static GameObject VisBox(Transform parent, Vector3 center, Vector3 size, Material mat, Quaternion? rot = null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.position = center;
            g.transform.rotation = rot ?? Quaternion.identity;
            g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        static Bounds GB(GameObject go)
        {
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static GameObject Inst(string name, Transform parent)
        {
            if (!_kit.TryGetValue(name, out var pf)) { Debug.LogWarning("[ShipMap] missing prefab " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        static int Hash(float a, float b, float c = 0)
        {
            unchecked
            {
                int h = (int)(a * 73.13f) * 73856093 ^ (int)(b * 19.7f) * 19349663 ^ (int)(c * 11.3f) * 83492791;
                return h & 0x7fffffff;
            }
        }

        /// <summary>벽 모듈: 앞면(+z)이 normal 방향을 보도록, 앞면 중심을 face 위치에 맞춘다.</summary>
        static GameObject Module(Transform parent, string name, Vector3 normal, Vector3 faceBottomCenter, float w, float h, float depthMax)
        {
            var go = Inst(name, parent);
            if (go == null) return null;
            var lb = GB(go);
            float sx = w / Mathf.Max(0.01f, lb.size.x), sy = h / Mathf.Max(0.01f, lb.size.y);
            float depth = Mathf.Max(0.01f, lb.size.z);
            float sz = depth > depthMax ? depthMax / depth : 1f;
            go.transform.localScale = new Vector3(sx, sy, sz);
            lb = GB(go);
            var piv = new Vector3(-lb.center.x, -lb.min.y, -lb.max.z);
            var rot = Quaternion.LookRotation(normal, Vector3.up);
            go.transform.rotation = rot;
            go.transform.position = rot * piv + faceBottomCenter;
            return go;
        }

        static void Tile(Transform parent, string name, float yaw, Vector3 c, float sx, float sz, bool alignTop)
        {
            var go = Inst(name, parent);
            if (go == null) return;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var b = GB(go);
            bool swap = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f;
            float wx = sx / b.size.x, wz = sz / b.size.z;
            go.transform.localScale = swap ? new Vector3(wz, 1, wx) : new Vector3(wx, 1, wz);
            b = GB(go);
            go.transform.position += new Vector3(c.x - b.center.x, alignTop ? c.y - b.max.y : c.y - b.min.y, c.z - b.center.z);
        }

        // 정점 수가 적은 모듈 위주 (Electric_02 2.5k, Rounded_Simple 0.9k, Rounded_TV 2k) + 디테일 모듈 소량
        static string[] Palette(Ctx c, string owner)
        {
            if (owner.StartsWith("M_")) return new[] { "P_Wall_Electric_Panel_02_LP", "P_Wall_Electric_Panel_01_LP", "P_Wall_Hangar_Half_01_LP" };
            if (owner.StartsWith("K_")) return new[] { "P_Wall_Electric_Panel_02_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Rounded_TV_01_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Rounded_Simple_01_LP" };
            string rid = owner.Substring(2);
            switch (c.F.key)
            {
                case "Hangar": return new[] { "P_Wall_Electric_Panel_02_LP", "P_Wall_Technic_Simple_01_LP", "P_Wall_Electric_Panel_01_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Electric_Panel_02_LP", "P_Wall_Rounded_Simple_01_LP" };
                case "Cargo": return new[] { "P_Wall_Electric_Panel_02_LP", "P_Wall_Technic_Simple_02_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Rounded_TV_01_LP", "P_Wall_Rounded_Simple_01_LP" };
                case "Control": return new[] { "P_Wall_Rounded_TV_01_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Electric_Panel_02_LP", "P_Wall_Technic_Simple_03_LP", "P_Wall_Rounded_TV_01_LP", "P_Wall_Rounded_Simple_01_LP" };
                case "Power": return new[] { "P_Wall_Electric_Panel_01_LP", "P_Wall_Electric_Panel_02_LP", "P_Wall_Technic_Simple_02_LP", "P_Wall_Electric_Panel_02_LP", "P_Wall_Rounded_Simple_01_LP" };
                default:
                    if (rid == "NW") return new[] { "P_Wall_Wardrobe_Bed_01_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Wardrobe_Simple_01_LP", "P_Wall_Rounded_Simple_01_LP" };
                    if (rid == "W") return new[] { "P_Wall_Rounded_Simple_01_LP", "P_Wall_Rounded_TV_01_LP", "P_Wall_Rounded_Simple_01_LP" };
                    return new[] { "P_Wall_Wardrobe_Simple_01_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Rounded_TV_01_LP", "P_Wall_Rounded_Simple_01_LP", "P_Wall_Rounded_Simple_01_LP" };
            }
        }

        static Vector3 WallNormal(string o, int n) => o == "x" ? new Vector3(n, 0, 0) : new Vector3(0, 0, n);
        static Vector3 WallPoint(string o, float line, float along, float y) => o == "x" ? new Vector3(line, y, along) : new Vector3(along, y, line);

        // ------------------------------------------------------------------ slabs (floors / ceilings)
        static void BuildSlabs(Ctx c)
        {
            float Y = c.Y;
            var roomsT = Child(c.structure, "Rooms");
            var corT = Child(c.structure, "Corridors");
            var tF = Child(c.temp, "Floors");
            var tC = Child(c.temp, "Ceilings");
            foreach (var r in c.rooms.Values)
            {
                float sx = r.x1 - r.x0, sz = r.z1 - r.z0, cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2;
                var g = Box(roomsT, "F" + c.F.n + "_" + r.id + "_" + r.name, new Vector3(cx, Y - 0.15f, cz), new Vector3(sx, 0.3f, sz));
                Box(g.transform, "Ceiling", new Vector3(cx, Y + r.h + 0.15f, cz), new Vector3(sx, 0.3f, sz));
                TileRoom(tF, tC, Y, r.h, r.x0, r.z0, r.x1, r.z1);
            }
            foreach (var kv in c.cors)
            {
                var cr = kv.Value;
                string pre = cr.kind == "maint" ? "_Maint_" : cr.kind == "stair" ? "_StairCorridor_" : "_Corridor_";
                var g = new GameObject("F" + c.F.n + pre + cr.id); g.transform.SetParent(corT, false);
                foreach (var q in cr.rects)
                {
                    float sx = q.x1 - q.x0, sz = q.z1 - q.z0, cx = (q.x0 + q.x1) / 2, cz = (q.z0 + q.z1) / 2;
                    Box(g.transform, "Floor", new Vector3(cx, Y - 0.15f, cz), new Vector3(sx, 0.3f, sz));
                    Box(g.transform, "Ceiling", new Vector3(cx, Y + cr.h + 0.15f, cz), new Vector3(sx, 0.3f, sz));
                    TileRect(tF, tC, Y, cr.h, q.x0, q.z0, q.x1, q.z1, cr.kind == "maint" ? "maint" : "corr");
                }
            }
        }

        /// <summary>방: 벽을 따라 2.5m 테두리 + 안쪽 패턴 (가벼운 패턴 위주, 큰 격납고는 5m 타일)</summary>
        static void TileRoom(Transform tF, Transform tC, float Y, float h, float x0, float z0, float x1, float z1)
        {
            const float e = 2.5f;
            TileRect(tF, tC, Y, h, x0, z0, x1, z0 + e, "ring");
            TileRect(tF, tC, Y, h, x0, z1 - e, x1, z1, "ring");
            TileRect(tF, tC, Y, h, x0, z0 + e, x0 + e, z1 - e, "ring");
            TileRect(tF, tC, Y, h, x1 - e, z0 + e, x1, z1 - e, "ring");
            TileRect(tF, tC, Y, h, x0 + e, z0 + e, x1 - e, z1 - e, h >= 6 ? "hangar" : "roomIn");
        }

        static void TileRect(Transform tF, Transform tC, float Y, float h, float x0, float z0, float x1, float z1, string kind)
        {
            float tile = kind == "hangar" ? 5f : 2.5f;
            float sx = x1 - x0, sz = z1 - z0;
            float tileX = tile, tileZ = tile;
            if (kind == "corr") { if (sx >= sz) tileX = 5f; else tileZ = 5f; }   // 복도 바닥: 길이 방향 5m
            int nx = Mathf.Max(1, Mathf.RoundToInt(sx / tileX)), nz = Mathf.Max(1, Mathf.RoundToInt(sz / tileZ));
            float tx = sx / nx, tz = sz / nz;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var cpos = new Vector3(x0 + (i + 0.5f) * tx, 0, z0 + (j + 0.5f) * tz);
                    bool edge = i == 0 || j == 0 || i == nx - 1 || j == nz - 1;
                    int s = Hash(cpos.x, cpos.z, Y) & 3;
                    string pf; string cf; float cyaw = 0;
                    switch (kind)
                    {
                        case "hangar":
                            pf = "P_Floor_Hangar_Pattern_01";
                            cf = (j % 3 == 1) ? "P_Roof_Hangar_Strip_01" : "P_Roof_Hangar_Pattern_01";
                            break;
                        case "roomIn":
                            // 흰 패널 위주 + 3칸에 1칸 붉은 그레이팅 (정점 절약하면서 Stage2 느낌 유지)
                            pf = ((i + j) % 3 == 0) ? "P_Floor_Pattern_04_LP" : "P_Floor_Pattern_05_LP";
                            cf = (j % 2 == 1) ? "P_Roof_Strip_Pattern_01_LP" : "P_Roof_Pattern_01_LP";
                            break;
                        case "ring":
                            pf = "P_Floor_Pattern_05_LP";
                            cf = "P_Roof_Pattern_01_LP";
                            break;
                        case "maint":
                            pf = "P_Floor_Pattern_03_LP_B";
                            cf = "P_Roof_Strip_Pattern_01_LP";
                            if (sz > sx) cyaw = 90;
                            break;
                        case "corr":
                            // 긴 복도가 많아져서 그레이팅은 한 칸 걸러 한 번, 천장 스트립은 세 칸에 한 번
                            pf = ((sx < sz ? j : i) % 3 == 0) ? "P_Floor_Pattern_04_LP" : "P_Floor_Pattern_05_LP";
                            cf = ((sx < sz ? j : i) % 3 == 1) ? "P_Roof_Strip_Pattern_01_LP" : "P_Roof_Pattern_01_LP";
                            if (cf == "P_Roof_Strip_Pattern_01_LP" && sx < sz) cyaw = 90;
                            break;
                        default:
                            pf = edge ? "P_Floor_Pattern_05_LP" : (s == 0 ? "P_Floor_Pattern_03_LP_B" : "P_Floor_Pattern_04_LP");
                            if (edge && nx > 2 && nz > 2)
                            {
                                cf = "P_Roof_Pattern_Angle_01_LP";
                                cyaw = (i == 0) ? 90 : (i == nx - 1) ? -90 : (j == 0) ? 0 : 180;
                            }
                            else cf = s == 1 ? "P_Roof_Strip_Pattern_01_LP" : "P_Roof_Pattern_01_LP";
                            break;
                    }
                    cpos.y = Y + 0.006f;
                    Tile(tF, pf, 0, cpos, tx, tz, false);
                    if (kind == "corr" || kind == "maint" || kind == "roomIn") continue;
                    cpos.y = Y + h - 0.004f;
                    Tile(tC, cf, cyaw, cpos, tx, tz, true);
                }
            if (kind == "corr" || kind == "maint" || kind == "roomIn")
            {
                // 복도 천장은 어둡고 잘 안 보이므로 5m 타일로 늘려 정점 절약
                int cx = Mathf.Max(1, Mathf.RoundToInt(sx / 5f)), cz = Mathf.Max(1, Mathf.RoundToInt(sz / 5f));
                float ux = sx / cx, uz = sz / cz;
                for (int i = 0; i < cx; i++)
                    for (int j = 0; j < cz; j++)
                    {
                        var cp = new Vector3(x0 + (i + 0.5f) * ux, Y + h - 0.004f, z0 + (j + 0.5f) * uz);
                        bool strip = kind == "maint" || ((sx < sz ? j : i) % 3 == 1) || (kind == "roomIn" && j % 2 == 1);
                        Tile(tC, strip ? "P_Roof_Strip_Pattern_01_LP" : "P_Roof_Pattern_01_LP", (strip && sx < sz) ? 90 : 0, cp, ux, uz, true);
                    }
            }
        }

        // ------------------------------------------------------------------ walls
        static void BuildWalls(Ctx c)
        {
            float Y = c.Y;
            var col = Child(c.structure, "Walls");
            var vis = Child(c.temp, "Walls");
            foreach (var w in c.F.walls)
            {
                float L = w.b - w.a;
                if (L < 0.25f) continue;
                var N = WallNormal(w.o, w.n);
                float y0 = Y + w.y0, y1 = Y + w.y1, H = y1 - y0;
                float mid = (w.a + w.b) / 2;
                // collider just behind the face
                var cc = WallPoint(w.o, w.line, mid, (y0 + y1) / 2) - N * 0.2f;
                var size = w.o == "x" ? new Vector3(0.4f, H, L) : new Vector3(L, H, 0.4f);
                Box(col, (w.kind == "step" ? "Step_" : "Wall_") + w.owner, cc, size);

                if (w.kind == "step")
                {
                    int ns = Mathf.Max(1, Mathf.RoundToInt(L / 5f)); float ws = L / ns;
                    for (int i = 0; i < ns; i++)
                        Module(vis, "P_Wall_Hangar_Half_01_LP", N, WallPoint(w.o, w.line, w.a + (i + 0.5f) * ws, y0), ws, H, 0.3f);
                    continue;
                }
                var pal = Palette(c, w.owner);
                if (L < 1.3f) { Module(vis, "P_Wall_Insert_Small_01_LP", N, WallPoint(w.o, w.line, mid, y0), L, H, 0.3f); continue; }
                float modW = (w.owner.StartsWith("K_") || w.owner.StartsWith("M_")) ? 6f : 5f;
                int n = Mathf.Max(1, Mathf.RoundToInt(L / modW)); float wd = L / n;
                for (int i = 0; i < n; i++)
                {
                    float a = w.a + (i + 0.5f) * wd;
                    string pf = pal[Hash(a, w.line, Y) % pal.Length];
                    if (wd < 2.4f) pf = "P_Wall_Rounded_Simple_01_LP";
                    float bottom = H <= 4.3f ? H : 3f;
                    Module(vis, pf, N, WallPoint(w.o, w.line, a, y0), wd, bottom, 0.3f);
                    float rem = H - bottom, yy = y0 + bottom;
                    while (rem > 0.05f)
                    {
                        float seg = rem >= 4.5f ? Mathf.Min(6f, rem) : rem;
                        if (rem - seg < 1f) seg = rem;
                        Module(vis, seg > 3.5f ? "P_Wall_Hangar_Normal_01_LP" : "P_Wall_Hangar_Half_01_LP", N, WallPoint(w.o, w.line, a, yy), wd, seg, 0.3f);
                        yy += seg; rem -= seg;
                    }
                }
            }
        }

        static float HeightOf(Ctx c, string eid)
        {
            if (eid.StartsWith("R_")) return c.rooms[eid.Substring(2)].h;
            if (eid.StartsWith("S_")) return 4f;
            return c.cors.TryGetValue(eid, out var cr) ? cr.h : 4f;
        }

        // ------------------------------------------------------------------ doorways
        static void BuildDoors(Ctx c)
        {
            float Y = c.Y;
            var col = Child(c.structure, "DoorFrames");
            var vis = Child(c.temp, "DoorFrames");
            foreach (var d in c.F.doors)
            {
                float L = d.b - d.a;
                c.cors.TryGetValue(d.e1, out var cr);
                bool maint = d.e1.StartsWith("M_");
                bool wide = cr != null && cr.w > 5.5f;
                float dw = maint ? L : wide ? Mathf.Min(L, L - 1f) : Mathf.Min(L, 4f);
                float dh = maint ? 3f : wide ? 5.5f : 3.5f;
                float hmin = Mathf.Min(HeightOf(c, d.e1), HeightOf(c, d.e2));
                var N = WallNormal(d.o, d.n);
                float mid = (d.a + d.b) / 2;
                float jamb = (L - dw) / 2;
                string tag = "F" + c.F.n + "_Door_" + d.e1.Substring(2) + "_" + d.e2.Substring(2);
                var parts = new List<(float a, float b, float y0, float y1)>();
                if (jamb > 0.05f)
                {
                    parts.Add((d.a, d.a + jamb, 0, hmin));
                    parts.Add((d.b - jamb, d.b, 0, hmin));
                }
                if (hmin - dh > 0.05f) parts.Add((d.a + jamb, d.b - jamb, dh, hmin));
                foreach (var p in parts)
                {
                    float l = p.b - p.a, hh = p.y1 - p.y0, m = (p.a + p.b) / 2;
                    var cc = WallPoint(d.o, d.line, m, Y + (p.y0 + p.y1) / 2);
                    Box(col, tag, cc, d.o == "x" ? new Vector3(0.3f, hh, l) : new Vector3(l, hh, 0.3f));
                    if (p.y0 > 0.1f)
                        VisBox(vis, cc, d.o == "x" ? new Vector3(0.3f, hh, l) : new Vector3(l, hh, 0.3f), _trim);
                    else
                    {
                        Module(vis, "P_Wall_Insert_Small_01_LP", N, WallPoint(d.o, d.line, m, Y + p.y0) + N * 0.15f, l, hh, 0.15f);
                        Module(vis, "P_Wall_Insert_Small_01_LP", -N, WallPoint(d.o, d.line, m, Y + p.y0) - N * 0.15f, l, hh, 0.15f);
                        VisBox(vis, cc, d.o == "x" ? new Vector3(0.28f, hh, l - 0.02f) : new Vector3(l - 0.02f, hh, 0.28f), _trim);
                    }
                }
                // trim around the opening (both sides)
                for (int s = -1; s <= 1; s += 2)
                {
                    var off = N * (0.17f * s);
                    var t0 = WallPoint(d.o, d.line, mid - dw / 2, Y) + off;
                    var t1 = WallPoint(d.o, d.line, mid + dw / 2, Y) + off;
                    var up = new Vector3(0, dh / 2, 0);
                    var sideSz = d.o == "x" ? new Vector3(0.06f, dh, 0.18f) : new Vector3(0.18f, dh, 0.06f);
                    VisBox(vis, t0 + up, sideSz, _trim);
                    VisBox(vis, t1 + up, sideSz, _trim);
                    var topSz = d.o == "x" ? new Vector3(0.06f, 0.18f, dw) : new Vector3(dw, 0.18f, 0.06f);
                    if (dh < hmin - 0.01f || maint) VisBox(vis, WallPoint(d.o, d.line, mid, Y + dh) + off, topSz, _trim);
                }
                // keep-out zones for props in front of the opening
                foreach (var e in new[] { d.e1, d.e2 })
                {
                    if (!e.StartsWith("R_")) continue;
                    var ro = c.rooms[e.Substring(2)];
                    var inward = e == d.e1 ? N : -N;
                    Vector3 p0 = WallPoint(d.o, d.line, d.a - 1.2f, 0), p1 = WallPoint(d.o, d.line, d.b + 1.2f, 0) + inward * 3.2f;
                    AddKeep(c, ro.id, Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.z, p1.z), Mathf.Max(p0.x, p1.x), Mathf.Max(p0.z, p1.z)));
                }
                // stair bulkhead (스토리 단계별로 여는 격벽문: 기본은 닫힘)
                if (d.e2.StartsWith("S_"))
                {
                    string k = d.e2.Substring(2);
                    var holder = new GameObject("F" + c.F.n + "_Bulkhead_Stair" + k);
                    holder.transform.SetParent(c.doors, false);
                    var center = WallPoint(d.o, d.line, mid, Y);
                    holder.transform.position = center;
                    var bc = holder.AddComponent<BoxCollider>();
                    bc.center = new Vector3(0, dh / 2, 0);
                    bc.size = d.o == "x" ? new Vector3(0.35f, dh, dw) : new Vector3(dw, dh, 0.35f);
                    var door = Inst("P_Ship_BigDoors_Fake_01", holder.transform);
                    if (door != null)
                    {
                        var b = GB(door);
                        door.transform.localScale = new Vector3(dw / b.size.x, dh / b.size.y, 0.6f);
                        b = GB(door);
                        var piv = new Vector3(-b.center.x, -b.min.y, -b.center.z);
                        var rot = Quaternion.LookRotation(N, Vector3.up);
                        door.transform.rotation = rot;
                        door.transform.position = rot * piv + center;
                        foreach (var cl in door.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(cl);
                        foreach (var r in door.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    // 문짝 모서리 틈을 막는 뒷판
                    VisBox(vis, center + Vector3.up * (dh / 2) - N * 0.12f, d.o == "x" ? new Vector3(0.05f, dh, dw) : new Vector3(dw, dh, 0.05f), _trim);
                }
            }
        }

        static void AddKeep(Ctx c, string rid, Rect r)
        {
            if (!c.keepOut.TryGetValue(rid, out var l)) c.keepOut[rid] = l = new List<Rect>();
            l.Add(r);
        }

        // ------------------------------------------------------------------ stair shafts
        static void BuildShafts(Ctx c)
        {
            float Y = c.Y;
            foreach (var k in new[] { "A", "B" })
            {
                float d = k == "A" ? -1f : 1f;              // direction from door into the shaft
                float doorX = d * StairDoorX;               // A: -42, B: +42
                float far = doorX + d * Shaft;               // far wall
                float cxs = doorX + d * Shaft / 2;
                var st = new GameObject("F" + c.F.n + "_Stair" + k).transform; st.SetParent(c.structure, false);
                var vis = Child(c.temp, "Stair" + k);
                float top = c.F.n == 5 ? Y + 4f : Y + FloorStep;
                float bandH = top - Y;
                // perimeter walls of this floor band (inner faces)
                void ShaftWall(string o, float line, float a, float b, int n, float y0, float y1)
                {
                    float L = b - a, H = y1 - y0; if (L < 0.2f || H < 0.1f) return;
                    var N = WallNormal(o, n);
                    var cc = WallPoint(o, line, (a + b) / 2, (y0 + y1) / 2) - N * 0.2f;
                    Box(st, "ShaftWall", cc, o == "x" ? new Vector3(0.4f, H, L) : new Vector3(L, H, 0.4f));
                    int m = Mathf.Max(1, Mathf.RoundToInt(L / 5f)); float wd = L / m;
                    for (int i = 0; i < m; i++)
                    {
                        float al = a + (i + 0.5f) * wd;
                        float h0 = Mathf.Min(3f, H);
                        if (y0 <= Y + 0.01f)
                        {
                            Module(vis, (i % 2 == 0) ? "P_Wall_Electric_Panel_02_LP" : "P_Wall_Rounded_Simple_01_LP", N, WallPoint(o, line, al, y0), wd, h0, 0.3f);
                            if (H - h0 > 0.05f) Module(vis, "P_Wall_Hangar_Normal_01_LP", N, WallPoint(o, line, al, y0 + h0), wd, H - h0, 0.3f);
                        }
                        else Module(vis, "P_Wall_Hangar_Normal_01_LP", N, WallPoint(o, line, al, y0), wd, H, 0.3f);
                    }
                }
                int inN = (int)d; // normal pointing into the shaft from the door wall
                ShaftWall("x", doorX, -8f, -2.5f, inN, Y, Y + 4f);
                ShaftWall("x", doorX, 2.5f, 8f, inN, Y, Y + 4f);
                ShaftWall("x", doorX, -8f, 8f, inN, Y + 4f, top);
                ShaftWall("x", far, -8f, 8f, -inN, Y, top);
                float xa = Mathf.Min(doorX, far), xb = Mathf.Max(doorX, far);
                ShaftWall("z", 8f, xa, xb, -1, Y, top);
                ShaftWall("z", -8f, xa, xb, 1, Y, top);
                // landing at floor level
                if (c.F.n == 1)
                {
                    Box(st, "ShaftFloor", new Vector3(cxs, Y - 0.15f, 0), new Vector3(Shaft, 0.3f, Shaft));
                    TileTop(vis, xa, -8f, xb, 8f, Y);
                }
                else
                {
                    float lx0 = Mathf.Min(doorX, doorX + d * 4f), lx1 = Mathf.Max(doorX, doorX + d * 4f);
                    Box(st, "Landing", new Vector3((lx0 + lx1) / 2, Y - 0.15f, 0), new Vector3(4f, 0.3f, Shaft));
                    VisBox(vis, new Vector3((lx0 + lx1) / 2, Y - 0.2f, 0), new Vector3(4f, 0.4f, Shaft), _trim);
                    TileTop(vis, lx0, -8f, lx1, 8f, Y);

                    // mid landing (Y-4)
                    float mx0 = Mathf.Min(doorX + d * 10f, far), mx1 = Mathf.Max(doorX + d * 10f, far);
                    Box(st, "MidLanding", new Vector3((mx0 + mx1) / 2, Y - 4f - 0.15f, 0), new Vector3(6f, 0.3f, Shaft));
                    VisBox(vis, new Vector3((mx0 + mx1) / 2, Y - 4f - 0.2f, 0), new Vector3(6f, 0.4f, Shaft), _trim);
                    TileTop(vis, mx0, -8f, mx1, 8f, Y - 4f);

                    // flight 1: landing -> mid (north half), flight 2: mid -> lower landing (south half)
                    Flight(st, vis, doorX + d * 4f, d, Y, 0.25f, 8f);
                    Flight(st, vis, doorX + d * 10f, -d, Y - 4f, -8f, -0.25f);
                    // central divider between the two flights
                    float dx0 = Mathf.Min(doorX + d * 4f, doorX + d * 10f), dx1 = Mathf.Max(doorX + d * 4f, doorX + d * 10f);
                    float dyTop = c.F.n == 5 ? Y + 1.1f : Y;
                    Box(st, "Divider", new Vector3((dx0 + dx1) / 2, (Y - 8f + dyTop) / 2, 0), new Vector3(6f, dyTop - (Y - 8f), 0.3f));
                    VisBox(vis, new Vector3((dx0 + dx1) / 2, (Y - 8f + dyTop) / 2, 0), new Vector3(6f, dyTop - (Y - 8f), 0.3f), _trim);
                }
                if (c.F.n == 5)
                {
                    Box(st, "ShaftCeiling", new Vector3(cxs, top + 0.15f, 0), new Vector3(Shaft, 0.3f, Shaft));
                    TileCeil(vis, xa, -8f, xb, 8f, top);
                    // top landing: void above the incoming flight (south half) -> rail
                    float rx = doorX + d * 4f;
                    Box(st, "TopRail", new Vector3(rx, Y + 0.55f, -4.1f), new Vector3(0.1f, 1.1f, 7.8f));
                    VisBox(vis, new Vector3(rx, Y + 1.05f, -4.1f), new Vector3(0.08f, 0.08f, 7.8f), _trim);
                    for (int i = 0; i <= 4; i++) VisBox(vis, new Vector3(rx, Y + 0.55f, -0.3f - i * 1.9f), new Vector3(0.06f, 1.1f, 0.06f), _trim);
                }
                // landing light + mid landing light
                MakeLight(c, new Vector3(doorX + d * 2f, Y + 3.3f, 5.5f), 1.3f, 8f, 0);
                if (c.F.n > 1) MakeLight(c, new Vector3(far - d * 3f, Y - 4f + 3.3f, 0f), 1.2f, 8f, 1);
            }
        }

        static void TileTop(Transform vis, float x0, float z0, float x1, float z1, float y)
        {
            float sx = x1 - x0, sz = z1 - z0;
            int nx = Mathf.Max(1, Mathf.RoundToInt(sx / 2.5f)), nz = Mathf.Max(1, Mathf.RoundToInt(sz / 2.5f));
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++)
                    Tile(vis, "P_Floor_Pattern_04_LP", 0, new Vector3(x0 + (i + 0.5f) * sx / nx, y + 0.006f, z0 + (j + 0.5f) * sz / nz), sx / nx, sz / nz, false);
        }

        static void TileCeil(Transform vis, float x0, float z0, float x1, float z1, float y)
        {
            float sx = x1 - x0, sz = z1 - z0;
            int nx = Mathf.Max(1, Mathf.RoundToInt(sx / 2.5f)), nz = Mathf.Max(1, Mathf.RoundToInt(sz / 2.5f));
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++)
                    Tile(vis, "P_Roof_Pattern_01_LP", 0, new Vector3(x0 + (i + 0.5f) * sx / nx, y - 0.004f, z0 + (j + 0.5f) * sz / nz), sx / nx, sz / nz, true);
        }

        /// <summary>startX에서 dir 방향으로 6m 진행하며 4m 내려가는 계단 (20단) + 경사 콜라이더.</summary>
        static void Flight(Transform col, Transform vis, float startX, float dir, float topY, float z0, float z1)
        {
            const int steps = 20; const float run = 0.3f, rise = 0.2f;
            float wz = z1 - z0, cz = (z0 + z1) / 2;
            for (int i = 0; i < steps; i++)
            {
                float xa = startX + dir * i * run, xb = startX + dir * (i + 1) * run;
                float t = topY - (i + 1) * rise;
                VisBox(vis, new Vector3((xa + xb) / 2, t - 0.15f, cz), new Vector3(run, 0.3f, wz), _trim);
            }
            // nosing strip on every other step
            float len = Mathf.Sqrt(6f * 6f + 4f * 4f);
            float ang = Mathf.Atan2(4f, 6f) * Mathf.Rad2Deg;
            var mid = new Vector3(startX + dir * 3f, topY - 2f, cz);
            var rot = Quaternion.Euler(0, 0, dir > 0 ? -ang : ang);
            var g = Box(col, "StairRamp", mid + rot * new Vector3(0, -0.12f, 0), new Vector3(len, 0.2f, wz), rot);
            // side stringer on the wall side
            float sz = z1 > 0 ? z1 - 0.05f : z0 + 0.05f;
            VisBox(vis, new Vector3(startX + dir * 3f, topY - 2f - 0.25f, sz), new Vector3(len, 0.4f, 0.1f), _trim, rot);
        }

        // ------------------------------------------------------------------ pipes
        static void PipeRun(Transform parent, Vector3 a, Vector3 b, float dia)
        {
            var d = b - a; float L = d.magnitude; if (L < 0.3f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(L / 6.5f)); float sl = L / n;   // 3.25m 파이프를 2배로 늘려 정점 절반
            var rot = Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.9f ? Vector3.right : Vector3.up);
            for (int i = 0; i < n; i++)
            {
                var go = Inst("P_Ship_Interior_Pipe_01", parent); if (go == null) return;
                go.transform.localScale = new Vector3(dia / 0.19f, dia / 0.19f, sl / 3.25f);
                go.transform.rotation = rot;
                var gb = GB(go);
                go.transform.position += (a + d.normalized * (sl * (i + 0.5f))) - gb.center;
            }
        }

        static void BuildPipes(Ctx c)
        {
            float Y = c.Y;
            var vis = Child(c.temp, "Pipes");
            foreach (var cr in c.cors.Values)
            {
                float side = (Hash(cr.pts[0].x, cr.pts[0].z) & 1) == 0 ? 1 : -1;
                for (int i = 0; i < cr.pts.Length - 1; i++)
                {
                    var p = new Vector3(cr.pts[i].x, 0, cr.pts[i].z); var q = new Vector3(cr.pts[i + 1].x, 0, cr.pts[i + 1].z);
                    var dir = (q - p).normalized; var perp = new Vector3(-dir.z, 0, dir.x) * side;
                    float off = cr.w / 2 - (cr.kind == "maint" ? 0.45f : 0.7f);
                    // extend through the bend
                    var a = p - dir * (i == 0 ? 0 : cr.w / 2 - 0.7f); var b = q + dir * (i == cr.pts.Length - 2 ? 0 : cr.w / 2 - 0.7f);
                    PipeRun(vis, a + perp * off + Vector3.up * (Y + cr.h - 0.35f), b + perp * off + Vector3.up * (Y + cr.h - 0.35f), 0.26f);
                    PipeRun(vis, a + perp * (off - 0.35f) + Vector3.up * (Y + cr.h - 0.3f), b + perp * (off - 0.35f) + Vector3.up * (Y + cr.h - 0.3f), 0.17f);
                    if (cr.kind == "maint")
                        PipeRun(vis, a - perp * off + Vector3.up * (Y + 0.35f), b - perp * off + Vector3.up * (Y + 0.35f), 0.3f);
                }
            }
            foreach (var r in c.rooms.Values)
            {
                bool alongX = (r.x1 - r.x0) >= (r.z1 - r.z0);
                float s = (Hash(r.x0, r.z0) & 1) == 0 ? 1 : -1;
                float yy = Y + r.h - 0.45f;
                if (alongX)
                {
                    float z = s > 0 ? r.z1 - 0.9f : r.z0 + 0.9f;
                    PipeRun(vis, new Vector3(r.x0, yy, z), new Vector3(r.x1, yy, z), 0.3f);
                    PipeRun(vis, new Vector3(r.x0, yy + 0.1f, z - s * 0.4f), new Vector3(r.x1, yy + 0.1f, z - s * 0.4f), 0.18f);
                }
                else
                {
                    float x = s > 0 ? r.x1 - 0.9f : r.x0 + 0.9f;
                    PipeRun(vis, new Vector3(x, yy, r.z0), new Vector3(x, yy, r.z1), 0.3f);
                    PipeRun(vis, new Vector3(x - s * 0.4f, yy + 0.1f, r.z0), new Vector3(x - s * 0.4f, yy + 0.1f, r.z1), 0.18f);
                }
            }
        }

        // ------------------------------------------------------------------ lights
        static void MakeLight(Ctx c, Vector3 pos, float intensity, float range, int modeHint, Color? color = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(_lightPf, c.lights);
            go.name = "F" + c.F.n + "_EmergencyLight_" + c.lights.childCount.ToString("00");
            go.transform.position = pos;
            var l = go.GetComponent<Light>();
            l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            if (color.HasValue) l.color = color.Value;
            var fl = go.GetComponent("EmergencyLightFlicker");
            var so = new SerializedObject(fl);
            int r = _rng.Next(100);
            int mode = modeHint == 2 ? 1 : (r < 62 ? 0 : r < 87 ? 2 : 1); // Flicker / Pulse / Broken
            so.FindProperty("mode").enumValueIndex = mode;
            so.FindProperty("baseIntensity").floatValue = intensity;
            so.FindProperty("seed").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildLights(Ctx c)
        {
            float Y = c.Y;
            foreach (var r in c.rooms.Values)
            {
                float sx = r.x1 - r.x0, sz = r.z1 - r.z0;
                int nx = Mathf.Clamp(Mathf.RoundToInt(sx / 11f), 1, 3), nz = Mathf.Clamp(Mathf.RoundToInt(sz / 10f), 1, 3);
                for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++)
                    {
                        var p = new Vector3(r.x0 + (i + 0.5f) * sx / nx, Y + r.h - 0.35f, r.z0 + (j + 0.5f) * sz / nz);
                        MakeLight(c, p, r.h >= 6 ? 1.8f : 1.3f, r.h >= 6 ? 11f : 8f, 0);
                    }
            }
            foreach (var cr in c.cors.Values)
            {
                bool maint = cr.kind == "maint";
                float step = maint ? 9f : 10f;
                float total = 0;
                for (int i = 0; i < cr.pts.Length - 1; i++) total += Mathf.Abs(cr.pts[i + 1].x - cr.pts[i].x) + Mathf.Abs(cr.pts[i + 1].z - cr.pts[i].z);
                int count = Mathf.Max(1, Mathf.RoundToInt(total / step));
                for (int k = 0; k < count; k++)
                {
                    float dist = total * (k + 0.5f) / count;
                    for (int i = 0; i < cr.pts.Length - 1; i++)
                    {
                        var p = new Vector3(cr.pts[i].x, 0, cr.pts[i].z); var q = new Vector3(cr.pts[i + 1].x, 0, cr.pts[i + 1].z);
                        float L = (q - p).magnitude;
                        if (dist <= L || i == cr.pts.Length - 2)
                        {
                            var pos = Vector3.Lerp(p, q, Mathf.Clamp01(dist / Mathf.Max(0.01f, L))); pos.y = Y + cr.h - 0.3f;
                            if (maint) MakeLight(c, pos, 0.9f, 5.5f, 0, new Color(1f, 0.33f, 0.08f));
                            else MakeLight(c, pos, 1.2f, 6.5f, 0);
                            break;
                        }
                        dist -= L;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ props
        struct PropReq { public string pf; public char mode; public int count; public PropReq(string p, char m, int n) { pf = p; mode = m; count = n; } }

        static PropReq[] Theme(string key, string rid)
        {
            PropReq Wl(string p, int n) => new PropReq(p, 'W', n);
            PropReq Fl(string p, int n) => new PropReq(p, 'F', n);
            switch (key + "_" + rid)
            {
                // 1F 격납고
                case "Hangar_NW": return new[] { Wl("P_Table_01_D", 2), Wl("P_Shelf_01", 2), Wl("P_Wardrobe_02", 1), Wl("P_Electric_Battery_01", 1), Fl("P_Crate_04", 3), Fl("P_Chariot_01", 1) };
                case "Hangar_N": return new[] { Wl("P_Generator_Ship_04", 1), Wl("P_Generator_Ship_02", 1), Wl("P_Technic_Console_01", 2), Wl("P_Shelf_01", 1), Fl("P_Crate_01", 2), Fl("P_Pipe_Valve_01", 1) };
                case "Hangar_NE": return new[] { Wl("P_Desktop_01_A", 2), Wl("P_Technic_Console_01_D", 2), Wl("P_Holo_Display_02", 1), Wl("P_Server_01", 2), Fl("P_Chair_01", 3) };
                case "Hangar_W": return new[] { Wl("P_Wardrobe_01_C", 6), Wl("P_Info_Terminal_01", 1), Wl("P_Wall_Bench_01", 2), Wl("P_Oxygen_Tank_01_C", 3), Fl("P_Crate_04", 2) };
                case "Hangar_C": return new[] { Fl("P_Lifting_Machine_01_C", 2), Fl("P_Plateform_Hoist_01", 2), Fl("P_Container_01_C", 2), Fl("P_Chariot_01", 3), Fl("P_Crate_04", 6), Wl("P_Barel_01_Pile_A", 2) };
                case "Hangar_E": return new[] { Wl("P_Technic_Console_01_D", 2), Wl("P_Electric_Battery_01", 2), Wl("P_Oxygen_Tank_01_C", 3), Fl("P_Crate_04", 2) };
                case "Hangar_SW": return new[] { Fl("P_Container_01_C", 2), Fl("P_Crate_04", 5), Wl("P_Shelf_01", 2), Fl("P_Chariot_01", 1), Wl("P_Barel_01_Pile_A", 2) };
                case "Hangar_S": return new[] { Fl("P_Drill_Machine_01", 1), Wl("P_Electric_Generator_01", 2), Wl("P_Table_01_D", 1), Fl("P_Chariot_01", 1), Fl("P_Crate_04", 3) };
                case "Hangar_SE": return new[] { Fl("P_Pipe_Valve_01", 3), Wl("P_Barel_01_Pile_A", 3), Wl("P_Oxygen_Tank_01_C", 4), Fl("P_Cylinder_Glass_02", 2), Wl("P_Technic_Console_01", 1) };
                // 2F 화물/검역
                case "Cargo_NW": return new[] { Wl("P_Shelf_01", 4), Wl("P_Wardrobe_02", 2), Wl("P_Electric_Battery_01", 1), Fl("P_Crate_04", 4) };
                case "Cargo_N": return new[] { Wl("P_Table_01_D", 2), Wl("P_Desktop_01_A", 1), Wl("P_Shelf_01", 2), Fl("P_Chariot_01", 1), Fl("P_Crate_01", 2) };
                case "Cargo_NE": return new[] { Wl("P_Desktop_01_A", 2), Wl("P_Technic_Console_01_D", 2), Wl("P_Holo_Display_03", 1), Wl("P_Server_01", 1), Fl("P_Chair_02", 3) };
                case "Cargo_W": return new[] { Wl("P_Wall_Bench_01", 3), Wl("P_Wardrobe_01_C", 5), Wl("P_Info_Terminal_01", 1), Fl("P_Crate_04", 2) };
                case "Cargo_C": return new[] { Wl("P_Barel_01_Pile_A", 2), Wl("P_Technic_Console_01", 1) };
                case "Cargo_E": return new[] { Fl("P_Crate_04", 6), Fl("P_Chariot_01", 2), Wl("P_Shelf_01", 2), Wl("P_Technic_Console_01", 1) };
                case "Cargo_SW": return new[] { Wl("P_Pipe_Valve_01", 2), Wl("P_Barel_01_Pile_A", 3), Wl("P_Technic_Console_01_D", 1), Wl("P_Oxygen_Tank_01_C", 3), Fl("P_Cylinder_Glass_02", 2) };
                case "Cargo_S": return new[] { Fl("P_Container_01_C", 3), Fl("P_Crate_04", 3) };
                case "Cargo_SE": return new[] { Wl("P_Pod_Glass_01", 3), Wl("P_Capsule_Glass_01", 2), Fl("P_Crate_04", 2), Fl("P_Trashcan_01_B", 2) };
                // 3F 통제
                case "Control_NW": return new[] { Wl("P_Desktop_01_A", 1), Wl("P_Server_01", 2), Wl("P_Wardrobe_02", 2), Wl("P_Technic_Console_01", 1), Fl("P_Chair_01", 2) };
                case "Control_N": return new[] { Fl("P_Ship_Interior_PilotChair_01", 2), Wl("P_Technic_Console_01_D", 3), Wl("P_Holo_Display_02", 1), Wl("P_Technic_Console_01_D", 2) };
                case "Control_NE": return new[] { Wl("P_Technic_Console_01_D", 2), Wl("P_Desktop_01_A", 1), Wl("P_Technic_Console_01", 2), Fl("P_Chair_02", 2) };
                case "Control_W": return new[] { Wl("P_Info_Terminal_01", 2), Wl("P_Wardrobe_01_C", 4), Wl("P_Wall_Bench_01", 2), Fl("P_Crate_04", 1) };
                case "Control_C": return new[] { Fl("P_Technic_Console_01_D", 4), Fl("P_Technic_Console_01_D", 2), Wl("P_Holo_Display_03", 1), Wl("P_Server_01", 2) };
                case "Control_E": return new[] { Wl("P_Info_Terminal_01", 1), Wl("P_Technic_Console_01", 2), Wl("P_Wardrobe_01_C", 4), Fl("P_Fence_01", 2) };
                case "Control_SW": return new[] { Wl("P_Server_01", 3), Wl("P_Server_01", 4), Wl("P_Desktop_01_A", 1), Fl("P_Chair_01", 1) };
                case "Control_S": return new[] { Wl("P_Shelf_01", 2) };
                case "Control_SE": return new[] { Wl("P_Chair_Four_Places_01", 3), Wl("P_Holo_Display_02", 1), Fl("P_Flower_Pot_01", 2), Fl("P_Chair_02", 2) };
                // 4F 동력
                case "Power_NW": return new[] { Wl("P_Electric_Battery_01", 5), Wl("P_Technic_Console_01_D", 1), Fl("P_Electric_Generator_01", 1) };
                case "Power_N": return new[] { Wl("P_Technic_Console_01_D", 2), Wl("P_Desktop_01_A", 1), Wl("P_Server_01", 2), Fl("P_Chair_01", 2) };
                case "Power_NE": return new[] { Wl("P_Shelf_01", 3), Wl("P_Electric_Battery_01", 3), Fl("P_Crate_04", 3) };
                case "Power_W": return new[] { Wl("P_Wardrobe_01_C", 5), Wl("P_Wall_Bench_01", 2), Wl("P_Info_Terminal_01", 1), Wl("P_Oxygen_Tank_01_C", 2) };
                case "Power_C": return new[] { Wl("P_Electric_Battery_01", 4), Wl("P_Technic_Console_01", 2) };
                case "Power_E": return new[] { Fl("P_Crate_04", 3), Wl("P_Wardrobe_02", 2), Wl("P_Info_Terminal_01", 1), Fl("P_Chariot_01", 1) };
                case "Power_SW": return new[] { Wl("P_Pipe_Valve_01", 3), Fl("P_Air_Fan_01", 2), Wl("P_Cylinder_Glass_02", 3), Wl("P_Technic_Console_01", 1) };
                case "Power_S": return new[] { Wl("P_Pipe_Valve_01", 4), Fl("P_Cylinder_Glass_02", 2), Fl("P_Barel_01_Pile_A", 2) };
                case "Power_SE": return new[] { Wl("P_Table_01_D", 2), Fl("P_Drill_Machine_01", 1), Wl("P_Shelf_01", 2), Fl("P_Crate_04", 2), Fl("P_Chariot_01", 1) };
                // 5F 승무원
                case "Crew_NW": return new[] { Wl("P_Bed_Double_01", 4), Wl("P_Wardrobe_01_C", 4), Fl("P_Stool_01", 2), Wl("P_Trashcan_01_B", 2) };
                case "Crew_N": return new[] { Wl("P_Chair_Four_Places_01", 3), Wl("P_Holo_Display_02", 1), Wl("P_Hydroponic_Plant_01", 1), Fl("P_Table_01_D", 1), Fl("P_Flower_Pot_01", 1) };
                case "Crew_NE": return new[] { Wl("P_Technic_Console_01_D", 2), Wl("P_Technic_Console_01", 2), Wl("P_Server_01", 2), Fl("P_Chair_01", 2) };
                case "Crew_W": return new[] { Wl("P_Heal_Pod_01", 2), Wl("P_Heal_Pod_01_B", 1), Wl("P_Capsule_Glass_01", 1), Fl("P_Bed_Plateform_01", 2), Fl("P_Microscope_01", 1) };
                case "Crew_C": return new[] { Fl("P_Table_01_C", 3), Wl("P_Store_Counter_01", 2), Wl("P_Wardrobe_02", 1), Wl("P_Trashcan_01_B", 2), Wl("P_Hydroponic_Plant_01", 1) };
                case "Crew_E": return new[] { Wl("P_Wall_Bench_01", 3), Wl("P_Info_Terminal_01", 1), Wl("P_Chair_Four_Places_01", 2), Fl("P_Flower_Pot_01", 2) };
                case "Crew_SW": return new[] { Wl("P_Wardrobe_02", 3), Wl("P_Wardrobe_01_C", 4), Wl("P_Electric_Generator_01", 1), Fl("P_Trashcan_01_B", 2) };
                case "Crew_S": return new[] { Fl("P_Chair_Four_Places_01", 4), Fl("P_Chair_Four_Places_01_B", 2), Wl("P_Holo_Display_02", 1), Fl("P_Flower_Pot_01", 1) };
                case "Crew_SE": return new[] { Wl("P_Shelf_01", 4), Fl("P_Crate_04", 4), Wl("P_Wardrobe_02", 1) };
            }
            return new PropReq[0];
        }

        static void CleanProp(GameObject go)
        {
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && (mb.GetType().Name == "AddTorqueAtStart")) mb.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static bool Fits(Ctx c, RoomD r, Bounds b, float pad)
        {
            if (b.min.x < r.x0 + 0.02f || b.max.x > r.x1 - 0.02f || b.min.z < r.z0 + 0.02f || b.max.z > r.z1 - 0.02f) return false;
            if (b.max.y > c.Y + r.h - 0.25f) return false;
            var e = b; e.Expand(new Vector3(pad * 2, 0, pad * 2));
            foreach (var p in c.placed) if (p.Intersects(e)) return false;
            if (c.keepOut.TryGetValue(r.id, out var ko))
            {
                var fr = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                foreach (var k in ko) if (k.Overlaps(fr)) return false;
            }
            return true;
        }

        /// <summary>벽에 붙이는 배치. side: 0=-x벽 1=+x벽 2=-z벽 3=+z벽.</summary>
        static GameObject PlaceWall(Ctx c, RoomD r, string pf, int side, float along)
        {
            var go = Inst(pf, c.props); if (go == null) return null;
            float yaw = side == 0 ? 90 : side == 1 ? -90 : side == 2 ? 0 : 180;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var b = GB(go);
            Vector3 off;
            if (side == 0) off = new Vector3(r.x0 + 0.04f - b.min.x, 0, along - b.center.z);
            else if (side == 1) off = new Vector3(r.x1 - 0.04f - b.max.x, 0, along - b.center.z);
            else if (side == 2) off = new Vector3(along - b.center.x, 0, r.z0 + 0.04f - b.min.z);
            else off = new Vector3(along - b.center.x, 0, r.z1 - 0.04f - b.max.z);
            off.y = c.Y - b.min.y + 0.002f;
            go.transform.position += off;
            b = GB(go);
            if (!Fits(c, r, b, 0.15f)) { UnityEngine.Object.DestroyImmediate(go); return null; }
            c.placed.Add(b); CleanProp(go);
            return go;
        }

        static GameObject PlaceFloor(Ctx c, RoomD r, string pf, Vector3 at, float yaw, float pad)
        {
            var go = Inst(pf, c.props); if (go == null) return null;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var b = GB(go);
            go.transform.position += new Vector3(at.x - b.center.x, c.Y - b.min.y + 0.002f, at.z - b.center.z);
            b = GB(go);
            if (!Fits(c, r, b, pad)) { UnityEngine.Object.DestroyImmediate(go); return null; }
            c.placed.Add(b); CleanProp(go);
            return go;
        }

        static float Rnd(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        static void BuildProps(Ctx c)
        {
            foreach (var r in c.rooms.Values)
            {
                float cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2;
                // 순찰 지점용으로 방 중심은 비워 둔다
                AddKeep(c, r.id, Rect.MinMaxRect(cx - 1.6f, cz - 1.6f, cx + 1.6f, cz + 1.6f));
                SpecialRoom(c, r);
                foreach (var req in Theme(c.F.key, r.id))
                {
                    int done = 0;
                    for (int t = 0; t < 60 && done < req.count; t++)
                    {
                        GameObject g;
                        if (req.mode == 'W')
                        {
                            int side = _rng.Next(4);
                            float along = side < 2 ? Rnd(r.z0 + 1f, r.z1 - 1f) : Rnd(r.x0 + 1f, r.x1 - 1f);
                            g = PlaceWall(c, r, req.pf, side, along);
                            // 같은 벽을 따라 줄지어 놓기 (사물함 등)
                            if (g != null && req.count >= 4)
                            {
                                var b = GB(g); float stepA = side < 2 ? b.size.z + 0.04f : b.size.x + 0.04f;
                                done++;
                                for (int k = 1; done < req.count && k < req.count; k++)
                                {
                                    if (PlaceWall(c, r, req.pf, side, along + stepA * k) != null) done++; else break;
                                }
                                continue;
                            }
                        }
                        else
                        {
                            var at = new Vector3(Rnd(r.x0 + 2f, r.x1 - 2f), 0, Rnd(r.z0 + 2f, r.z1 - 2f));
                            float yaw = _rng.Next(4) * 90f + Rnd(-12f, 12f);
                            g = PlaceFloor(c, r, req.pf, at, yaw, 0.7f);
                        }
                        if (g != null) done++;
                    }
                    if (done < req.count) c.skipped += req.count - done;
                }
            }
        }

        static void SpecialRoom(Ctx c, RoomD r)
        {
            float cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2, Y = c.Y;
            string key = c.F.key + "_" + r.id;
            if (key == "Hangar_E")
            {
                // 보조 우주선 (탑승/탈출 지점)
                var ship = Inst("P_BubbleShip_Mark_01_White", c.props);
                if (ship != null)
                {
                    ship.name = "F1_E_Escape_Shuttle";
                    ship.transform.rotation = Quaternion.Euler(0, 90, 0);
                    var b = GB(ship);
                    ship.transform.position += new Vector3(cx + 1.5f - b.center.x, Y - b.min.y, cz - b.center.z);
                    CleanProp(ship);
                    c.placed.Add(GB(ship));
                }
            }
            else if (key == "Cargo_C")
            {
                // 컨테이너 두 줄 + 사이 5m 통로 + 양끝 회전 공간
                for (int row = -1; row <= 1; row += 2)
                    for (int i = -1; i <= 1; i++)
                    {
                        var g = Inst("P_Container_01_C", c.props); if (g == null) continue;
                        g.transform.rotation = Quaternion.Euler(0, 90, 0);
                        var b = GB(g);
                        g.transform.position += new Vector3(cx + i * 6.75f - b.center.x, Y - b.min.y, cz + row * (2.5f + b.size.z / 2) - b.center.z);
                        CleanProp(g); c.placed.Add(GB(g));
                    }
                AddKeep(c, r.id, Rect.MinMaxRect(cx - 12f, cz - 2.5f, cx + 12f, cz + 2.5f));
            }
            else if (key == "Power_C")
            {
                // 중앙 보조 발전기 약 8x6m, 양쪽 5m 이상 통로
                var gen = new GameObject("F4_C_AuxGenerator"); gen.transform.SetParent(c.props, false);
                gen.transform.position = new Vector3(cx, Y, cz);
                var bc = gen.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 1.6f, 0); bc.size = new Vector3(8f, 3.2f, 6f);
                VisBox(c.temp, new Vector3(cx, Y + 0.15f, cz), new Vector3(8.4f, 0.3f, 6.4f), _trim);
                string[] parts = { "P_Generator_Ship_02", "P_Generator_Ship_03", "P_Generator_Ship_03", "P_Generator_Ship_02" };
                for (int i = 0; i < 4; i++)
                {
                    var g = Inst(parts[i], gen.transform); if (g == null) continue;
                    g.transform.rotation = Quaternion.Euler(0, i < 2 ? 90 : -90, 0);
                    var b = GB(g);
                    float px = cx + (i % 2 == 0 ? -1.7f : 1.7f), pz = cz + (i < 2 ? 1.5f : -1.5f);
                    g.transform.position += new Vector3(px - b.center.x, Y + 0.3f - b.min.y, pz - b.center.z);
                    foreach (var cl in g.GetComponentsInChildren<Collider>()) cl.enabled = false;
                    CleanProp(g);
                }
                PipeRun(c.temp, new Vector3(cx - 2.5f, Y + 3f, cz), new Vector3(cx - 2.5f, Y + r.h, cz), 0.45f);
                PipeRun(c.temp, new Vector3(cx + 2.5f, Y + 3f, cz), new Vector3(cx + 2.5f, Y + r.h, cz), 0.45f);
                c.placed.Add(new Bounds(new Vector3(cx, Y + 1.6f, cz), new Vector3(8.2f, 3.2f, 6.2f)));
                AddKeep(c, r.id, Rect.MinMaxRect(cx - 9.5f, cz - 3.2f, cx + 9.5f, cz + 3.2f));
            }
            else if (key == "Control_S")
            {
                // 서버 선반 열 (은신 공간)
                bool alongX = (r.x1 - r.x0) >= (r.z1 - r.z0);
                for (int row = -1; row <= 1; row++)
                    for (int i = -2; i <= 2; i++)
                    {
                        if (row == 0 && i == 0) continue;
                        var at = alongX ? new Vector3(cx + i * 2.2f, 0, cz + row * 4.2f) : new Vector3(cx + row * 4.2f, 0, cz + i * 2.2f);
                        PlaceFloor(c, r, "P_Server_01", at, alongX ? 0 : 90, 0.05f);
                    }
            }
            else if (key == "Crew_S")
            {
                // 휴게실 가벽
                for (int s = -1; s <= 1; s += 2)
                {
                    bool alongX = (r.x1 - r.x0) >= (r.z1 - r.z0);
                    var center = alongX ? new Vector3(cx + s * 4f, Y, cz + s * 2.5f) : new Vector3(cx + s * 2.5f, Y, cz + s * 4f);
                    var n = alongX ? Vector3.forward : Vector3.right;
                    var size = alongX ? new Vector3(4f, 2.4f, 0.3f) : new Vector3(0.3f, 2.4f, 4f);
                    var g = Box(c.structure, "F5_S_Partition", center + Vector3.up * 1.2f, size);
                    Module(c.temp, "P_Wall_Flat_Half_02_LP", n, center + n * 0.15f, 4f, 2.4f, 0.14f);
                    Module(c.temp, "P_Wall_Flat_Half_02_LP", -n, center - n * 0.15f, 4f, 2.4f, 0.14f);
                    VisBox(c.temp, center + Vector3.up * 2.45f, alongX ? new Vector3(4.05f, 0.1f, 0.34f) : new Vector3(0.34f, 0.1f, 4.05f), _trim);
                    c.placed.Add(new Bounds(center + Vector3.up * 1.2f, size));
                }
            }
        }

        // ------------------------------------------------------------------ markers (missions / monsters / save)
        static Transform Marker(Ctx c, string name, Vector3 pos, int icon)
        {
            var g = new GameObject(name);
            g.transform.SetParent(c.markers, false);
            g.transform.position = pos;
            var tex = EditorGUIUtility.IconContent("sv_icon_dot" + icon + "_pix16_gizmo").image as Texture2D;
            if (tex != null) EditorGUIUtility.SetIconForObject(g, tex);
            return g.transform;
        }

        static Vector3 RoomPoint(Ctx c, string rid, float up = 0.1f)
        {
            var r = c.rooms[rid];
            return new Vector3((r.x0 + r.x1) / 2, c.Y + up, (r.z0 + r.z1) / 2);
        }

        /// <summary>방 중심 근처에서 소품과 겹치지 않는 지점.</summary>
        static Vector3 FreePoint(Ctx c, string rid, float up = 0.1f)
        {
            var r = c.rooms[rid];
            var p0 = RoomPoint(c, rid, up);
            for (int ring = 0; ring < 8; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4, d = ring * 1.2f;
                    var p = p0 + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                    if (p.x < r.x0 + 1 || p.x > r.x1 - 1 || p.z < r.z0 + 1 || p.z > r.z1 - 1) continue;
                    var probe = new Bounds(p + Vector3.up * 0.9f, new Vector3(1.2f, 1.6f, 1.2f));
                    bool hit = false;
                    foreach (var b in c.placed) if (b.Intersects(probe)) { hit = true; break; }
                    if (!hit) return p;
                }
            return p0;
        }

        static Vector3 CorridorMid(Ctx c, string corId, bool ceiling)
        {
            foreach (var cr in c.cors.Values)
                if (cr.id == corId)
                {
                    int i = cr.pts.Length / 2 - (cr.pts.Length % 2 == 0 ? 1 : 0);
                    var p = cr.pts.Length % 2 == 0 ? Vector3.Lerp(new Vector3(cr.pts[i].x, 0, cr.pts[i].z), new Vector3(cr.pts[i + 1].x, 0, cr.pts[i + 1].z), 0.5f) : new Vector3(cr.pts[i].x, 0, cr.pts[i].z);
                    p.y = c.Y + (ceiling ? cr.h - 0.3f : 0.1f);
                    return p;
                }
            return Vector3.zero;
        }

        static void Patrol(Ctx c, string monster, string[] route, string extraCorridor = null)
        {
            var parent = Marker(c, "F" + c.F.n + "_Monster_" + monster, FreePoint(c, route[0]), 6);
            for (int i = 0; i < route.Length; i++)
            {
                Vector3 p = route[i] == "~maint" && extraCorridor != null ? CorridorMid(c, extraCorridor, false) : FreePoint(c, route[i]);
                var m = Marker(c, "Patrol_" + (i + 1).ToString("00") + "_" + route[i].Replace("~", ""), p, 6);
                m.SetParent(parent, true);
            }
        }

        static void BuildMarkers(Ctx c)
        {
            int n = c.F.n; string F = "F" + n + "_";
            switch (n)
            {
                case 1:
                    Marker(c, F + "SavePoint_W", FreePoint(c, "W"), 3);
                    Marker(c, F + "Mission1_InstallPowerModule_E", FreePoint(c, "E"), 1);
                    Marker(c, F + "Mission2_OpenHangar_NE", FreePoint(c, "NE"), 1);
                    Marker(c, F + "Mission3_LoadData_Ignition_E", FreePoint(c, "E") + new Vector3(0, 0, 3f), 1);
                    Marker(c, F + "Escape_Board_Shuttle_E", RoomPoint(c, "E") + new Vector3(-4f, 0, 0), 2);
                    Patrol(c, "LargeVisual", new[] { "C", "S", "SE", "E", "C" });
                    Marker(c, F + "Monster_CeilingAmbush_W-NW", CorridorMid(c, "NW-W", true), 6);
                    break;
                case 2:
                    Marker(c, F + "SavePoint_E", FreePoint(c, "E"), 3);
                    Marker(c, F + "Mission_PowerModule_NW", FreePoint(c, "NW"), 1);
                    Marker(c, F + "Mission_IntrusionTrace_SE", FreePoint(c, "SE"), 1);
                    Marker(c, F + "Mission_FuelSupply_SW", FreePoint(c, "SW"), 1);
                    Marker(c, F + "Exit_W_to_StairA", RoomPoint(c, "W"), 2);
                    Patrol(c, "SmallScout", new[] { "C", "E", "SE", "E", "C" });
                    Patrol(c, "Tentacle", new[] { "N", "C", "W", "NW", "W", "C", "N" });
                    break;
                case 3:
                    Marker(c, F + "SavePoint_W", FreePoint(c, "W"), 3);
                    Marker(c, F + "Mission_AdminCard_NW", FreePoint(c, "NW"), 1);
                    Marker(c, F + "Mission_BridgeStatus_N", FreePoint(c, "N"), 1);
                    Marker(c, F + "Mission_HangarUnlock_NE", FreePoint(c, "NE"), 1);
                    Marker(c, F + "Exit_E_to_StairB", RoomPoint(c, "E"), 2);
                    Patrol(c, "Visual", new[] { "C", "N", "NE", "E", "C" });
                    Patrol(c, "SmallScout", new[] { "S", "SW", "W", "C", "W", "SW", "S" });
                    break;
                case 4:
                    Marker(c, F + "SavePoint_E", FreePoint(c, "E"), 3);
                    Marker(c, F + "Mission_PowerCell_NE", FreePoint(c, "NE"), 1);
                    Marker(c, F + "Mission_GeneratorStart_C", RoomPoint(c, "C") + new Vector3(0, 0, -4.2f), 1);
                    Marker(c, F + "Mission_PowerDistribution_NW", FreePoint(c, "NW"), 1);
                    Marker(c, F + "Exit_W_to_StairA", RoomPoint(c, "W"), 2);
                    Patrol(c, "Tentacle", new[] { "S", "C", "N", "C", "S" });
                    Patrol(c, "SmallScout", new[] { "SE", "E", "C", "S", "SE" });
                    break;
                case 5:
                    Marker(c, F + "PlayerStart_NW", FreePoint(c, "NW"), 3);
                    Marker(c, F + "SavePoint_NW", FreePoint(c, "NW") + new Vector3(1.5f, 0, 0), 3);
                    Marker(c, F + "Item_SurvivorTrace_N", FreePoint(c, "N"), 4);
                    Marker(c, F + "Item_SurvivorTrace_W", FreePoint(c, "W"), 4);
                    Marker(c, F + "Item_SurvivorTrace_S", FreePoint(c, "S"), 4);
                    Marker(c, F + "Mission_EmergencyComms_NE", FreePoint(c, "NE"), 1);
                    Marker(c, F + "Exit_E_to_StairB", RoomPoint(c, "E"), 2);
                    Patrol(c, "SmallScout", new[] { "C", "N", "NW", "W", "~maint", "C" }, "Maint_W-C");
                    Marker(c, F + "Monster_CeilingAmbush_E-SE", CorridorMid(c, "E-SE", true), 6);
                    break;
            }
        }

        static void BuildFx(Ctx c)
        {
            // 큰 방 바닥 안개 + 고장난 배선 스파크
            var fog = _kit.ContainsKey("Particle_GroundFog") ? _kit["Particle_GroundFog"] : null;
            var ids = new List<string>(c.rooms.Keys);
            var fogRooms = new List<string> { "C" };
            for (int i = 0; i < 2; i++) fogRooms.Add(ids[_rng.Next(ids.Count)]);
            foreach (var rid in fogRooms)
            {
                if (fog == null) break;
                var g = (GameObject)PrefabUtility.InstantiatePrefab(fog, c.fx);
                g.transform.position = RoomPoint(c, rid, 0.05f);
            }
            var list = new List<CorD>(c.cors.Values);
            for (int i = 0; i < 3; i++)
            {
                var cr = list[_rng.Next(list.Count)];
                if (cr.kind == "stair") continue;
                var p = CorridorMid(c, cr.id, true);
                var g = (GameObject)PrefabUtility.InstantiatePrefab(_sparkPf, c.fx);
                g.name = "F" + c.F.n + "_Spark_" + i;
                g.transform.position = p;
            }
        }

        // ------------------------------------------------------------------ merge (memory optimization)
        [StructLayout(LayoutKind.Sequential)]
        struct CV
        {
            public Vector3 p;
            public ushort nx, ny, nz, nw;
            public ushort tx, ty, tz, tw;
            public Vector2 uv;
        }

        class Bucket { public Material mat; public List<CV> v = new List<CV>(); public List<ushort> idx = new List<ushort>(); }

        /// <summary>
        /// _DressingTemp 아래 모든 정적 장식(벽/바닥/천장/배관/계단)을 층×재질×32m 칸 단위로 합친다.
        /// 정점 포맷: 위치 f32x3, 노멀 f16x4, 탄젠트 f16x4, UV f32x2 (36B, 기본 56B 대비 약 36% 절감)
        /// 인덱스는 16비트(칸당 정점 6만 이하), 저장 후 Read/Write 끔, 그림자 끔.
        /// </summary>
        static int MergeTemp(Ctx c)
        {
            string folder = BakedDir + "/F" + c.F.n;
            if (!AssetDatabase.IsValidFolder(BakedDir)) AssetDatabase.CreateFolder(RootDir, "Baked");
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            AssetDatabase.CreateFolder(BakedDir, "F" + c.F.n);

            LastStats = "";
            foreach (Transform g in c.temp)
            {
                long gv = 0;
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) gv += mf.sharedMesh.vertexCount;
                LastStats += g.name + "=" + (gv / 1000) + "k ";
            }
            var visual = Child(c.root, "Visual");
            var w2l = visual.worldToLocalMatrix;
            const float cell = 32f;
            var buckets = new Dictionary<string, List<Bucket>>();
            var cache = new Dictionary<Mesh, (Vector3[] v, Vector3[] n, Vector4[] t, Vector2[] uv, int[][] sub)>();
            var tri = new int[3];
            foreach (var mr in c.temp.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh; var mats = mr.sharedMaterials;
                if (mats.Length == 0) continue;
                if (!cache.TryGetValue(mesh, out var data))
                {
                    var subs = new int[mesh.subMeshCount][];
                    for (int s = 0; s < mesh.subMeshCount; s++) subs[s] = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetIndices(s) : new int[0];
                    data = (mesh.vertices, mesh.normals, mesh.tangents, mesh.uv, subs);
                    cache[mesh] = data;
                }
                var ctr = mr.bounds.center;
                string cellKey = Mathf.FloorToInt(ctr.x / cell) + "_" + Mathf.FloorToInt(ctr.z / cell);
                var m = w2l * mr.localToWorldMatrix;
                var nm = m.inverse.transpose;
                bool flip = m.determinant < 0;
                for (int s = 0; s < data.sub.Length; s++)
                {
                    var mat = mats[Mathf.Min(s, mats.Length - 1)];
                    if (mat == null || data.sub[s].Length == 0) continue;
                    string key = cellKey + "_" + mat.GetInstanceID();
                    if (!buckets.TryGetValue(key, out var bl)) buckets[key] = bl = new List<Bucket>();
                    var idxs = data.sub[s];
                    if (bl.Count == 0 || bl[bl.Count - 1].v.Count + idxs.Length > 60000) bl.Add(new Bucket { mat = mat });
                    var bk = bl[bl.Count - 1];
                    var remap = new Dictionary<int, int>();
                    for (int k = 0; k < idxs.Length; k += 3)
                    {
                        tri[0] = idxs[k]; tri[1] = flip ? idxs[k + 2] : idxs[k + 1]; tri[2] = flip ? idxs[k + 1] : idxs[k + 2];
                        for (int q = 0; q < 3; q++)
                        {
                            int vi = tri[q];
                            if (!remap.TryGetValue(vi, out int ni))
                            {
                                ni = bk.v.Count; remap[vi] = ni;
                                var nrm = data.n.Length > 0 ? nm.MultiplyVector(data.n[vi]).normalized : Vector3.up;
                                Vector4 tg = data.t.Length > 0 ? data.t[vi] : new Vector4(1, 0, 0, 1);
                                var t3 = m.MultiplyVector(new Vector3(tg.x, tg.y, tg.z)).normalized;
                                float tw = flip ? -tg.w : tg.w;
                                bk.v.Add(new CV
                                {
                                    p = m.MultiplyPoint3x4(data.v[vi]),
                                    nx = Mathf.FloatToHalf(nrm.x), ny = Mathf.FloatToHalf(nrm.y), nz = Mathf.FloatToHalf(nrm.z), nw = 0,
                                    tx = Mathf.FloatToHalf(t3.x), ty = Mathf.FloatToHalf(t3.y), tz = Mathf.FloatToHalf(t3.z), tw = Mathf.FloatToHalf(tw),
                                    uv = data.uv.Length > 0 ? data.uv[vi] : Vector2.zero
                                });
                            }
                            bk.idx.Add((ushort)ni);
                        }
                    }
                }
            }

            var layout = new[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            };
            var created = new List<Mesh>();
            int made = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in buckets)
                {
                    int part = 0;
                    foreach (var bk in kv.Value)
                    {
                        if (bk.idx.Count == 0) continue;
                        var mesh = new Mesh();
                        mesh.SetVertexBufferParams(bk.v.Count, layout);
                        mesh.SetVertexBufferData(bk.v, 0, 0, bk.v.Count);
                        mesh.SetIndexBufferParams(bk.idx.Count, IndexFormat.UInt16);
                        mesh.SetIndexBufferData(bk.idx, 0, 0, bk.idx.Count);
                        mesh.subMeshCount = 1;
                        mesh.SetSubMesh(0, new SubMeshDescriptor(0, bk.idx.Count, MeshTopology.Triangles));
                        mesh.RecalculateBounds();
                        string nm = "F" + c.F.n + "_" + bk.mat.name + "_" + kv.Key.Replace("-", "m") + "_" + part++;
                        mesh.name = nm;
                        AssetDatabase.CreateAsset(mesh, folder + "/" + nm + ".asset");
                        created.Add(mesh);
                        var go = new GameObject(nm);
                        go.transform.SetParent(visual, false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var r = go.AddComponent<MeshRenderer>();
                        r.sharedMaterial = bk.mat;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = false;
                        r.lightProbeUsage = LightProbeUsage.Off;
                        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                        made++;
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var mesh in created)
            {
                var so = new SerializedObject(mesh);
                var p = so.FindProperty("m_IsReadable");
                if (p != null) { p.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();
            UnityEngine.Object.DestroyImmediate(c.temp.gameObject);
            return made;
        }

        // ------------------------------------------------------------------ finish
        public static string Finish(bool bakeNav)
        {
            if (_data == null) Load();
            var ship = ShipRoot();
            var vis = ship.GetComponent<ShipFloorVisibility>() ?? ship.gameObject.AddComponent<ShipFloorVisibility>();
            var floors = new List<ShipFloorVisibility.Floor>();
            string nav = "";
            foreach (Transform f in ship)
            {
                if (!f.name.StartsWith("F")) continue;
                int n = f.name[1] - '0';
                var fd = GetFloor(n);
                var toggles = new List<GameObject>();
                foreach (var nm in new[] { "Visual", "Lights", "FX" }) { var t = f.Find(nm); if (t) toggles.Add(t.gameObject); }
                var rends = new List<Renderer>();
                var props = f.Find("Props"); if (props) rends.AddRange(props.GetComponentsInChildren<Renderer>(true));
                var bh = f.Find("Bulkheads"); if (bh) rends.AddRange(bh.GetComponentsInChildren<Renderer>(true));
                floors.Add(new ShipFloorVisibility.Floor { name = f.name, floorY = fd != null ? fd.y : n * 8 - 8, toggles = toggles.ToArray(), propRenderers = rends.ToArray() });

                if (bakeNav)
                {
                    var surf = f.GetComponent<NavMeshSurface>() ?? f.gameObject.AddComponent<NavMeshSurface>();
                    surf.collectObjects = CollectObjects.Children;
                    surf.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
                    surf.BuildNavMesh();
                    if (surf.navMeshData != null)
                    {
                        string p = RootDir + "/NavMesh_" + f.name + ".asset";
                        AssetDatabase.DeleteAsset(p);
                        AssetDatabase.CreateAsset(surf.navMeshData, p);
                        nav += f.name + " ";
                    }
                }
            }
            floors.Sort((a, b) => a.floorY.CompareTo(b.floorY));
            vis.floors = floors.ToArray();
            EditorUtility.SetDirty(vis);
            var scene = ship.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return "floors=" + floors.Count + " navmesh=" + nav;
        }
    }
}
#endif
