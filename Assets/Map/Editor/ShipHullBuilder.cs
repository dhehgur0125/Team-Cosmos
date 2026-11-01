// VOID LAST - Stage2 우주선 외부 선체 + 1층 발사 격납고 생성기
// VOID_LAST_Ship(5층 내부)를 감싸는 외부 선체와, 1층 "보조 우주선" 방에서 연결 통로로 이어지는 발사 격납고를 만든다.
// 메뉴: Tools/VOID LAST/Build Ship Hull + Launch Hangar
// 연결 통로(E-Hangar)와 E방 남쪽 문은 ship_layout_v2.json → ShipMapBuilder.BuildFloor(1) 에서 만든다.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class ShipHullBuilder
    {
        const string RootDir = "Assets/Map/Stage2_Ship";
        const string BakedDir = RootDir + "/Baked";
        const string KitDir = "Assets/Creepy_Cat/3D Scifi Kit Vol 4/Prefabs";

        // 선체가 감싸야 하는 내부 공간 (VOID_LAST_Ship 렌더러 외곽 -72.3..72.8 / -0.42..36.0 / -65.8..58.8 + 여유)
        public const float X0 = -72.8f, X1 = 73.3f, Z0 = -66.3f, Z1 = 59.3f, Y0 = -0.8f, Y1 = 36.6f;
        const float MidY = (Y0 + Y1) * 0.5f, MidZ = (Z0 + Z1) * 0.5f;
        const int Rows = 5;

        // 발사 격납고 내부 공간 (1층 높이, 선체 남쪽(+z)으로 돌출)
        public const float HX0 = 17f, HX1 = 65f, HZ0 = 60f, HZ1 = 100f, HTop = 16f;
        public const float LinkX0 = 37.5f, LinkX1 = 44.5f, LinkH = 6f;      // ShipMapBuilder 연결 통로와 동일
        public const float MouthX0 = 21f, MouthX1 = 61f, MouthH = 13f;       // 발사구 (+z 방향 우주로 열림)
        const float WallD = 0.6f;                                            // 격납고 벽 두께
        // 조종실(선수 조종석 내부): 3층 바닥(y=16)과 같은 높이, 3층 정비 통로에서 연결 통로(z -6..-1)로 들어온다
        public const float CkFloor = 16f, CkScale = 3.3f, CkBackX = -74f, CkWallX = -80.5f, CkFrontX = -127f;
        public const float CkDoorZ0 = -6f, CkDoorZ1 = -1f, CkDoorH = 4f;
        const float CkFlatZ0 = -10f, CkFlatZ1 = 4f;                           // 조종실 평평한 바닥 폭 (양옆은 경사)
        const float CkWallZ0 = -18.4f, CkWallZ1 = 11.4f, CkTop = 29.2f;       // 뒤쪽 출입벽 범위
        const float RoofTop = HTop + 0.65f;                                  // 격납고 지붕 바깥면

        static Dictionary<string, GameObject> _kit;
        static Material _trim, _lamp;
        static GameObject _lightPf;
        static int _lightCount;
        public static string LastStats = "";

        [MenuItem("Tools/VOID LAST/Build Ship Hull + Launch Hangar")]
        public static void BuildMenu() { Debug.Log("[ShipHull] " + Build()); }

        public static string Build()
        {
            Load();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ShipMapBuilder.ScenePath) scene = EditorSceneManager.OpenScene(ShipMapBuilder.ScenePath);
            var old = GameObject.Find("VOID_LAST_Hull");
            if (old != null) Object.DestroyImmediate(old);
            _lightCount = 0;

            var root = new GameObject("VOID_LAST_Hull").transform;
            var ship = GameObject.Find("VOID_LAST_Ship");
            if (ship != null) root.SetSiblingIndex(ship.transform.GetSiblingIndex() + 1);

            // ---------------- 발사 격납고
            var hangar = Child(root, "LaunchHangar");
            var hStruct = Child(hangar, "Structure");
            var hTemp = Child(hangar, "_Temp");
            var hProps = Child(hangar, "Props");
            var hLights = Child(hangar, "Lights");
            var hMarkers = Child(hangar, "Markers");
            BuildHangarShell(hStruct, hTemp);
            BuildHangarDressing(hTemp, hProps, hLights, hMarkers);

            // ---------------- 외부 선체
            var ext = Child(root, "Exterior");
            var eTemp = Child(ext, "_Temp");
            BuildHullPlating(eTemp);
            BuildHullDecks(eTemp);
            BuildBowAndStern(eTemp);
            var cockpit = Child(root, "Cockpit");
            var cStruct = Child(cockpit, "Structure");
            var cTemp = Child(cockpit, "_Temp");
            var cProps = Child(cockpit, "Props");
            var cLights = Child(cockpit, "Lights");
            var cMarkers = Child(cockpit, "Markers");
            BuildCockpit(eTemp, Child(cockpit, "Interior"), cStruct, cTemp, cProps, cLights, cMarkers);
            BuildRings(eTemp);
            BuildTopside(eTemp);
            BuildLettering(eTemp);
            BuildSpace(Child(ext, "Space"));
            // 발사구 너머 배경: 스카이박스가 없으면 빈 화면이 흰색으로 나와서 우주 스카이박스를 지정 (주변광은 Flat 유지)
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Creepy_Cat/3D Scifi Kit Vol 4/Meshes/Materials/Skyboxes/Skybox_SpacePort.mat");
            if (sky != null) RenderSettings.skybox = sky;

            string stats = "hangarTemp=" + (CountVerts(hTemp) / 1000) + "k exteriorTemp=" + (CountVerts(eTemp) / 1000) + "k";
            int mh = Merge(hTemp, Child(hangar, "Visual"), "Hull_Hangar", 32f);
            int me = Merge(eTemp, Child(ext, "Visual"), "Hull_Exterior", 48f);
            int mc = Merge(cTemp, Child(cockpit, "Visual"), "Hull_Cockpit", 32f);

            // ---------------- 내비메시 (격납고) + 1층 연결 통로와 잇는 링크
            string nav = BakeHangarNav(hangar.gameObject) + " " + BakeCockpitNav(cockpit.gameObject);

            // ---------------- 표시 최적화
            var vis = root.gameObject.AddComponent<ShipHullVisibility>();
            vis.exterior = ext.gameObject;
            vis.hangarToggles = new[] { hangar.Find("Visual").gameObject, hLights.gameObject };
            vis.hangarPropRenderers = hProps.GetComponentsInChildren<Renderer>(true);
            vis.interiorBounds = MinMax(new Vector3(X0, Y0, Z0), new Vector3(X1, Y1 + 0.5f, Z1));
            vis.hangarBounds = MinMax(new Vector3(HX0 - 1f, Y0, Z1), new Vector3(HX1 + 1f, HTop + 1f, HZ1 + 0.5f));
            vis.launchMouthZ = HZ1;
            vis.cockpitToggles = new[] { cockpit.Find("Visual").gameObject, cockpit.Find("Interior").gameObject, cLights.gameObject };
            vis.cockpitPropRenderers = cProps.GetComponentsInChildren<Renderer>(true);
            vis.cockpitBounds = MinMax(new Vector3(CkFrontX - 3f, CkFloor - 1f, CkWallZ0 - 1f), new Vector3(X0, CkTop + 1f, CkWallZ1 + 1f));
            vis.cockpitBackX = CkWallX;
            vis.cockpitFrontX = -118f;
            EditorUtility.SetDirty(vis);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            LastStats = stats + " mergedHangar=" + mh + " mergedExterior=" + me + " mergedCockpit=" + mc + " props=" + hProps.childCount + "/" + cProps.childCount + " lights=" + hLights.childCount + "/" + cLights.childCount + " " + nav;
            return LastStats;
        }

        // ==================================================================== hangar
        static void BuildHangarShell(Transform col, Transform vis)
        {
            // 바닥 / 천장 / 벽 콜라이더
            Box(col, "Hangar_Floor", MinMax(new Vector3(HX0, -0.3f, HZ0 - 0.3f), new Vector3(HX1, 0f, HZ1 + 1.5f)));
            Box(col, "Hangar_Ceiling", MinMax(new Vector3(HX0, HTop, HZ0), new Vector3(HX1, HTop + 0.3f, HZ1)));
            Box(col, "Hangar_Wall_W", MinMax(new Vector3(HX0 - 0.4f, 0, HZ0), new Vector3(HX0, HTop, HZ1)));
            Box(col, "Hangar_Wall_E", MinMax(new Vector3(HX1, 0, HZ0), new Vector3(HX1 + 0.4f, HTop, HZ1)));
            Box(col, "Hangar_Wall_N_W", MinMax(new Vector3(HX0, 0, HZ0 - 0.4f), new Vector3(LinkX0, HTop, HZ0)));
            Box(col, "Hangar_Wall_N_E", MinMax(new Vector3(LinkX1, 0, HZ0 - 0.4f), new Vector3(HX1, HTop, HZ0)));
            Box(col, "Hangar_Wall_N_Top", MinMax(new Vector3(LinkX0, LinkH, HZ0 - 0.4f), new Vector3(LinkX1, HTop, HZ0)));
            Box(col, "Hangar_Wall_S_W", MinMax(new Vector3(HX0, 0, HZ1), new Vector3(MouthX0, HTop, HZ1 + 0.4f)));
            Box(col, "Hangar_Wall_S_E", MinMax(new Vector3(MouthX1, 0, HZ1), new Vector3(HX1, HTop, HZ1 + 0.4f)));
            Box(col, "Hangar_Wall_S_Top", MinMax(new Vector3(MouthX0, MouthH, HZ1), new Vector3(MouthX1, HTop, HZ1 + 0.4f)));
            // 발사구: 플레이어가 우주로 걸어 나가지 않도록 막는 투명 벽 (엔딩 연출에서 비활성화)
            Box(col, "LaunchMouth_PlayerBlocker", MinMax(new Vector3(MouthX0, 0, HZ1 + 0.2f), new Vector3(MouthX1, MouthH, HZ1 + 0.6f)));

            // 바닥 타일 (윗면을 y=0.013 에 맞춤) - 착륙 패드 구역은 다른 패턴
            float tx = (HX1 - HX0) / 10f, tz = (HZ1 - HZ0) / 8f;
            for (int i = 0; i < 10; i++)
                for (int j = 0; j < 8; j++)
                {
                    float x0 = HX0 + i * tx, z0 = HZ0 + j * tz;
                    bool pad = i >= 3 && i <= 6 && j >= 2 && j <= 6;
                    string pf = pad ? "P_Floor_Hangar_Pattern_03" : ((i + j) % 2 == 0 ? "P_Floor_Hangar_Pattern_01" : "P_Floor_Hangar_Pattern_02");
                    FitTop(pf, vis, Quaternion.identity, new Vector3(x0, 0.006f, z0), new Vector3(x0 + tx, 0.013f, z0 + tz));
                }
            // 발사구 앞 턱(바깥 데크 끝) + 경고등 띠
            VisBox(vis, MinMax(new Vector3(MouthX0 - 1.2f, -0.8f, HZ1), new Vector3(MouthX1 + 1.2f, 0.012f, HZ1 + 1.5f)), _trim);
            VisBox(vis, MinMax(new Vector3(MouthX0, 0.012f, HZ1 + 0.1f), new Vector3(MouthX1, 0.03f, HZ1 + 0.35f)), _lamp);

            // 천장 타일 (아랫면이 실내)
            for (int i = 0; i < 10; i++)
                for (int j = 0; j < 8; j++)
                {
                    float x0 = HX0 + i * tx, z0 = HZ0 + j * tz;
                    string pf = j % 3 == 1 ? "P_Roof_Hangar_Strip_01" : "P_Roof_Hangar_Pattern_01";
                    Fit(pf, vis, Quaternion.identity, new Vector3(x0, HTop - 0.25f, z0), new Vector3(x0 + tx, HTop, z0 + tz));
                }
            // 천장 보 (발사구 방향과 직각)
            for (int k = 1; k < 4; k++)
            {
                float z = HZ0 + k * (HZ1 - HZ0) / 4f;
                VisBox(vis, MinMax(new Vector3(HX0, HTop - 1.1f, z - 0.5f), new Vector3(HX1, HTop - 0.2f, z + 0.5f)), _trim);
            }

            // 벽: 실내면(+z)이 격납고 안을 보도록 배치, 바깥면(흰/주황)이 그대로 선체 외벽이 된다
            HangarWall(vis, new Vector3(0, 0, 1), HZ0, HX0, LinkX0, 0, HTop);
            HangarWall(vis, new Vector3(0, 0, 1), HZ0, LinkX1, HX1, 0, HTop);
            HangarWall(vis, new Vector3(0, 0, 1), HZ0, LinkX0, LinkX1, LinkH, HTop);
            HangarWall(vis, new Vector3(1, 0, 0), HX0, HZ0, HZ1, 0, HTop);
            HangarWall(vis, new Vector3(-1, 0, 0), HX1, HZ0, HZ1, 0, HTop);
            HangarWall(vis, new Vector3(0, 0, -1), HZ1, HX0, MouthX0, 0, HTop);
            HangarWall(vis, new Vector3(0, 0, -1), HZ1, MouthX1, HX1, 0, HTop);
            HangarWall(vis, new Vector3(0, 0, -1), HZ1, MouthX0, MouthX1, MouthH, HTop);

            // 연결 통로 출입구 테두리
            VisBox(vis, MinMax(new Vector3(LinkX0 - 0.35f, 0, HZ0 - 0.05f), new Vector3(LinkX0, LinkH, HZ0 + 0.3f)), _trim);
            VisBox(vis, MinMax(new Vector3(LinkX1, 0, HZ0 - 0.05f), new Vector3(LinkX1 + 0.35f, LinkH, HZ0 + 0.3f)), _trim);
            VisBox(vis, MinMax(new Vector3(LinkX0 - 0.35f, LinkH, HZ0 - 0.05f), new Vector3(LinkX1 + 0.35f, LinkH + 0.4f, HZ0 + 0.3f)), _trim);
            VisBox(vis, MinMax(new Vector3(LinkX0, LinkH + 0.1f, HZ0 + 0.3f), new Vector3(LinkX1, LinkH + 0.2f, HZ0 + 0.34f)), _lamp);

            // 발사구 프레임 + 경고등
            VisBox(vis, MinMax(new Vector3(MouthX0 - 1.2f, 0, HZ1 - 0.6f), new Vector3(MouthX0, MouthH + 1.4f, HZ1 + 1.5f)), _trim);
            VisBox(vis, MinMax(new Vector3(MouthX1, 0, HZ1 - 0.6f), new Vector3(MouthX1 + 1.2f, MouthH + 1.4f, HZ1 + 1.5f)), _trim);
            VisBox(vis, MinMax(new Vector3(MouthX0 - 1.2f, MouthH, HZ1 - 0.6f), new Vector3(MouthX1 + 1.2f, MouthH + 1.4f, HZ1 + 1.5f)), _trim);
            VisBox(vis, MinMax(new Vector3(MouthX0 + 0.02f, 0.2f, HZ1 - 0.62f), new Vector3(MouthX0 + 0.12f, MouthH - 0.2f, HZ1 - 0.4f)), _lamp);
            VisBox(vis, MinMax(new Vector3(MouthX1 - 0.12f, 0.2f, HZ1 - 0.62f), new Vector3(MouthX1 - 0.02f, MouthH - 0.2f, HZ1 - 0.4f)), _lamp);
            VisBox(vis, MinMax(new Vector3(MouthX0 + 0.2f, MouthH - 0.12f, HZ1 - 0.62f), new Vector3(MouthX1 - 0.2f, MouthH - 0.02f, HZ1 - 0.4f)), _lamp);

            // 바깥 지붕 데크 + 지붕/바닥 테두리
            float ox0 = HX0 - WallD, ox1 = HX1 + WallD, oz1 = HZ1 + 1.5f;
            DeckTiles(vis, ox0, Z1, ox1, oz1, RoofTop, true, 12f);
            VisBox(vis, MinMax(new Vector3(ox0 - 0.05f, HTop - 0.1f, Z1), new Vector3(ox1 + 0.05f, RoofTop, oz1)), _trim, true);
            DeckTiles(vis, ox0, Z1, ox1, oz1, Y0, false, 12f);
            VisBox(vis, MinMax(new Vector3(ox0 - 0.05f, Y0, Z1), new Vector3(ox1 + 0.05f, -0.01f, HZ1)), _trim, true);
            // 바깥 모서리 기둥
            foreach (var x in new[] { ox0, ox1 })
                VisBox(vis, MinMax(new Vector3(x - 0.7f, Y0, HZ1 - 0.2f), new Vector3(x + 0.7f, RoofTop + 0.3f, HZ1 + 1.6f)), _trim);
        }

        /// <summary>격납고 벽 한 면: 0~6m 하단 / 6~12m 중단 / 12~16m 상단, 5m 폭 모듈.</summary>
        static void HangarWall(Transform vis, Vector3 n, float line, float a, float b, float y0, float y1)
        {
            float L = b - a; if (L < 0.2f) return;
            int cols = Mathf.Max(1, Mathf.RoundToInt(L / 5f)); float w = L / cols;
            var rot = Quaternion.LookRotation(n, Vector3.up);
            float[] bands = { 0f, 6f, 12f, HTop };
            for (int r = 0; r < 3; r++)
            {
                float by0 = Mathf.Max(y0, bands[r]), by1 = Mathf.Min(y1, bands[r + 1]);
                if (by1 - by0 < 0.1f) continue;
                for (int i = 0; i < cols; i++)
                {
                    float s0 = a + i * w, s1 = s0 + w;
                    string pf = r == 0 ? ((Hash(s0, line) % 4 == 0) ? "P_Wall_Hangar_Door_01" : (i % 2 == 0 ? "P_Wall_Hangar_Normal_01_LP" : "P_Wall_Hangar_Insert_01_LP"))
                              : r == 1 ? "P_Wall_Hangar_Normal_01_LP" : "P_Wall_Hangar_Half_01_LP";
                    if (r == 0 && by1 - by0 < 5.5f) pf = "P_Wall_Hangar_Half_01_LP";
                    Fit(pf, vis, rot, WallMin(n, line, s0, by0, WallD), WallMax(n, line, s1, by1, WallD));
                }
            }
            // 기둥 (10m 간격)
            if (y0 < 0.1f)
                for (float s = a + 10f; s < b - 2f; s += 10f)
                    Fit("P_Hangar_Pilar_01", vis, rot, WallMin(n, line, s - 0.6f, 0f, -0.5f), WallMax(n, line, s + 0.6f, y1, -0.5f));
        }

        // 벽 모듈 박스: 실내면이 line 에 오고, 모듈은 바깥쪽(-n)으로 depth 만큼. depth<0 이면 실내 쪽으로 돌출.
        static Vector3 WallMin(Vector3 n, float line, float s, float y, float depth)
        {
            bool alongX = Mathf.Abs(n.z) > 0.5f;
            float l0 = depth >= 0 ? line - n.x * depth - n.z * depth : line;
            float l1 = depth >= 0 ? line : line - n.x * depth - n.z * depth;
            float lo = Mathf.Min(l0, l1);
            return alongX ? new Vector3(s, y, lo) : new Vector3(lo, y, s);
        }
        static Vector3 WallMax(Vector3 n, float line, float s, float y, float depth)
        {
            bool alongX = Mathf.Abs(n.z) > 0.5f;
            float l0 = depth >= 0 ? line - n.x * depth - n.z * depth : line;
            float l1 = depth >= 0 ? line : line - n.x * depth - n.z * depth;
            float hi = Mathf.Max(l0, l1);
            return alongX ? new Vector3(s, y, hi) : new Vector3(hi, y, s);
        }

        static void BuildHangarDressing(Transform vis, Transform props, Transform lights, Transform markers)
        {
            const float cx = (HX0 + HX1) * 0.5f;     // 41
            const float shipZ = 82f;

            // 착륙 고정대 4개 + 패드 경계 표시등
            foreach (var dx in new[] { -5.5f, 5.5f })
                foreach (var dz in new[] { -5.5f, 5.5f })
                    PlaceProp("P_Ship_Exterior_FixDock_01", props, 0f, 1f, new Vector3(cx + dx, 0.013f, shipZ + dz));
            float px0 = HX0 + 3 * (HX1 - HX0) / 10f, px1 = HX0 + 7 * (HX1 - HX0) / 10f, pz0 = HZ0 + 2 * 5f, pz1 = HZ0 + 7 * 5f;
            VisBox(vis, MinMax(new Vector3(px0, 0.013f, pz0), new Vector3(px1, 0.03f, pz0 + 0.15f)), _lamp);
            VisBox(vis, MinMax(new Vector3(px0, 0.013f, pz1 - 0.15f), new Vector3(px1, 0.03f, pz1)), _lamp);
            VisBox(vis, MinMax(new Vector3(px0, 0.013f, pz0), new Vector3(px0 + 0.15f, 0.03f, pz1)), _lamp);
            VisBox(vis, MinMax(new Vector3(px1 - 0.15f, 0.013f, pz0), new Vector3(px1, 0.03f, pz1)), _lamp);

            // 보조 우주선 (탈출선) - 조종석이 발사구(+z)를 보도록
            var shuttle = Inst("P_BubbleShip_Mark_01_White", props);
            if (shuttle != null)
            {
                shuttle.name = "EscapeShuttle_BubbleShip";
                var body = FindDeep(shuttle.transform, "P_Bubble_Body_01");
                var cockpit = FindDeep(shuttle.transform, "P_Bubble_Cockpit_01");
                float yaw = 0f;
                if (body != null && cockpit != null)
                {
                    var bb = MB(body.gameObject).center; var cb = MB(cockpit.gameObject).center;
                    var f = new Vector3(cb.x - bb.x, 0, cb.z - bb.z);
                    if (f.sqrMagnitude > 0.001f) yaw = -Vector3.SignedAngle(Vector3.forward, f.normalized, Vector3.up);
                }
                shuttle.transform.rotation = Quaternion.Euler(0, yaw, 0);
                shuttle.transform.localScale = Vector3.one * 2.0f;
                var b = MB(shuttle);
                shuttle.transform.position += new Vector3(cx - b.center.x, 0.55f - b.min.y, shipZ - b.center.z);
                foreach (var rb in shuttle.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
                foreach (var l in shuttle.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
                foreach (var r in shuttle.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            }

            // 발사 관제 콘솔 (연결 통로 입구 양옆, 격납고 쪽을 봄)
            PlaceProp("P_Technic_Console_01_D", props, 0f, 1f, new Vector3(LinkX0 - 3.5f, 0.013f, HZ0 + 1.2f));
            PlaceProp("P_Technic_Console_01_D", props, 0f, 1f, new Vector3(LinkX1 + 3.5f, 0.013f, HZ0 + 1.2f));
            // 서쪽 벽: 화물 컨테이너/상자
            PlaceProp("P_Container_01", props, 90f, 1f, new Vector3(HX0 + 2.2f, 0.013f, 66f));
            PlaceProp("P_Container_01_B", props, 90f, 1f, new Vector3(HX0 + 2.2f, 0.013f, 73f));
            PlaceProp("P_Container_01_C", props, 90f, 1f, new Vector3(HX0 + 2.2f, 0.013f, 90f));
            PlaceProp("P_Crate_01", props, 15f, 1f, new Vector3(HX0 + 6f, 0.013f, 69.5f));
            PlaceProp("P_Crate_03", props, -10f, 1f, new Vector3(HX0 + 5.5f, 0.013f, 86f));
            PlaceProp("P_Barel_01_Pile_A", props, 0f, 1f, new Vector3(HX0 + 3f, 0.013f, 96.5f));
            // 동쪽 벽: 연료 공급 설비 (2층 '연료 확보' 미션과 연결)
            PlaceProp("P_Station_Citern_Kit_02", props, 0f, 1f, new Vector3(HX1 - 3.5f, 0.013f, 79f));
            PlaceProp("P_Tank_Cistern_01", props, -90f, 0.55f, new Vector3(HX1 - 4f, 0.013f, 90f));
            PlaceProp("P_Lifting_Machine_01", props, 180f, 1f, new Vector3(HX1 - 5f, 0.013f, 67f));
            PlaceProp("P_Chariot_01", props, 30f, 1f, new Vector3(cx + 10.5f, 0.013f, shipZ - 9f));
            PlaceProp("P_Barel_02", props, 0f, 1f, new Vector3(HX1 - 2f, 0.013f, 95.5f));
            PlaceProp("P_Barel_03", props, 0f, 1f, new Vector3(HX1 - 3.2f, 0.013f, 96.6f));

            // 조명: 우주선 위 작업등 2 + 비상등
            Spot(lights, new Vector3(cx - 6f, HTop - 1.3f, shipZ - 3f), 4f, 26f, new Color(0.75f, 0.85f, 1f));
            Spot(lights, new Vector3(cx + 6f, HTop - 1.3f, shipZ + 3f), 4f, 26f, new Color(0.75f, 0.85f, 1f));
            foreach (var z in new[] { 67f, 80f, 93f })
            {
                Emergency(lights, new Vector3(HX0 + 0.5f, 10.5f, z), 1.6f, 12f, 0);
                Emergency(lights, new Vector3(HX1 - 0.5f, 10.5f, z), 1.6f, 12f, 0);
            }
            Emergency(lights, new Vector3(cx, LinkH + 1f, HZ0 + 0.8f), 1.5f, 9f, 2);
            Emergency(lights, new Vector3(cx, MouthH + 0.6f, HZ1 - 1.2f), 1.8f, 14f, 1);

            // 마커 (스토리/게임플레이 연결용)
            Marker(markers, "F1_Hangar_Entry_FromShuttleRoom", new Vector3(cx, 0.1f, HZ0 + 1f), 1);
            Marker(markers, "F1_Hangar_LaunchControl", new Vector3(LinkX1 + 3.5f, 0.1f, HZ0 + 2.6f), 2);
            Marker(markers, "F1_Escape_Shuttle_Board", new Vector3(cx - 9.5f, 0.1f, shipZ), 3);
            Marker(markers, "F1_Hangar_FuelSupply", new Vector3(HX1 - 6f, 0.1f, 79f), 2);
            Marker(markers, "F1_Monster_Large_Approach_FromLink", new Vector3(cx, 0.1f, HZ0 - 8f), 6);
            Marker(markers, "F1_Hangar_LaunchMouth", new Vector3(cx, MouthH * 0.5f, HZ1 + 0.5f), 4);
        }

        // ==================================================================== exterior hull
        static void BuildHullPlating(Transform t)
        {
            // 북(-z, 좌현) / 서(-x, 선수) / 동(+x, 선미) : 전체 5단
            Face(t, new Vector3(0, 0, -1), Z0, X0, X1, Y0, Y1, Rows, 1);
            Face(t, new Vector3(-1, 0, 0), X0, Z0, Z1, Y0, Y1, Rows, 2);
            Face(t, new Vector3(1, 0, 0), X1, Z0, Z1, Y0, Y1, Rows, 3);
            // 남(+z, 우현): 격납고 구간은 격납고 지붕 위만
            float hx0 = HX0 - WallD - 0.2f, hx1 = HX1 + WallD + 0.2f;
            Face(t, new Vector3(0, 0, 1), Z1, X0, hx0, Y0, Y1, Rows, 4);
            Face(t, new Vector3(0, 0, 1), Z1, hx1, X1, Y0, Y1, Rows, 5);
            Face(t, new Vector3(0, 0, 1), Z1, hx0, hx1, RoofTop, Y1, 3, 6);
            // 모서리 기둥
            foreach (var x in new[] { X0, X1 })
                foreach (var z in new[] { Z0, Z1 })
                {
                    float sx = x < 0 ? -1 : 1, sz = z < 0 ? -1 : 1;
                    VisBox(t, MinMax(new Vector3(x, Y0 - 0.2f, z), new Vector3(x + sx * 2.8f, Y1 + 0.4f, z + sz * 2.8f)), _trim);
                }
        }

        /// <summary>선체 한 면을 외벽 패널로 덮는다. 외벽 모듈은 -z 면이 바깥면.</summary>
        // 외벽 패널을 두지 않는 구역 (조종실 연결 통로가 선체를 지나는 곳)
        static readonly Bounds[] PlateSkip =
        {
            MinMax(new Vector3(X0 - 3f, CkFloor - 0.5f, CkDoorZ0 - 0.5f), new Vector3(X0 + 0.5f, CkFloor + CkDoorH + 0.5f, CkDoorZ1 + 0.5f)),
        };

        static void Face(Transform t, Vector3 n, float line, float a, float b, float y0, float y1, int rows, int faceId)
        {
            float L = b - a; if (L < 0.5f) return;
            int cols = Mathf.Max(1, Mathf.RoundToInt(L / 12.5f)); float w = L / cols;
            float rh = (y1 - y0) / rows;
            var rot = Quaternion.LookRotation(-n, Vector3.up);   // 프리팹 -z → 바깥
            for (int r = 0; r < rows; r++)
            {
                int band = rows == Rows ? r : r + 2;              // 짧은 면은 위쪽 3단 무늬
                for (int i = 0; i < cols; i++)
                {
                    int h = Hash(i * 7 + faceId * 131, r * 13 + faceId);
                    string pf; float depth;
                    switch (band)
                    {
                        case 0: pf = "P_Wall_Back_Rectangular_01"; depth = 2.2f; break;
                        case 1: pf = h % 3 == 0 ? "P_Wall_Ext_Module_03" : "P_Wall_Back_Window_01"; depth = 1.2f; break;
                        case 2: pf = h % 2 == 0 ? "P_Wall_Ext_Module_01_NoLight" : "P_Wall_Back_Normal_01"; depth = 1.2f; break;
                        case 3: pf = h % 4 == 0 ? "P_Wall_Back_Normal_01" : "P_Wall_Back_Window_01"; depth = 1.2f; break;
                        default: pf = "P_Wall_Ext_Module_04"; depth = 2.5f; break;
                    }
                    float s0 = a + i * w, s1 = s0 + w, yy0 = y0 + r * rh, yy1 = yy0 + rh;
                    Vector3 mn, mx;
                    if (Mathf.Abs(n.z) > 0.5f)
                    {
                        float z0 = n.z > 0 ? line : line - depth, z1 = n.z > 0 ? line + depth : line;
                        mn = new Vector3(s0, yy0, z0); mx = new Vector3(s1, yy1, z1);
                    }
                    else
                    {
                        float x0 = n.x > 0 ? line : line - depth, x1 = n.x > 0 ? line + depth : line;
                        mn = new Vector3(x0, yy0, s0); mx = new Vector3(x1, yy1, s1);
                    }
                    var pb = MinMax(mn, mx); bool skip = false;
                    foreach (var sk in PlateSkip) if (sk.Intersects(pb)) skip = true;
                    if (!skip) Fit(pf, t, rot, mn, mx);
                }
                // 단 사이 가로 띠
                if (r > 0)
                {
                    float yb = y0 + r * rh;
                    Vector3 mn, mx;
                    if (Mathf.Abs(n.z) > 0.5f) { mn = new Vector3(a, yb - 0.18f, n.z > 0 ? line : line - 2.6f); mx = new Vector3(b, yb + 0.18f, n.z > 0 ? line + 2.6f : line); }
                    else { mn = new Vector3(n.x > 0 ? line : line - 2.6f, yb - 0.18f, a); mx = new Vector3(n.x > 0 ? line + 2.6f : line, yb + 0.18f, b); }
                    VisBox(t, MinMax(mn, mx), _trim);
                }
            }
        }

        static void BuildHullDecks(Transform t)
        {
            const float m = 2.8f;
            DeckTiles(t, X0 - m, Z0 - m, X1 + m, Z1 + m, Y1, true, 15f);
            DeckTiles(t, X0 - m, Z0 - m, X1 + m, Z1 + m, Y0, false, 15f);
            // 갑판 테두리
            VisBox(t, MinMax(new Vector3(X0 - m, Y1 - 0.1f, Z0 - m), new Vector3(X1 + m, Y1 + 0.9f, Z0 - m + 0.8f)), _trim);
            VisBox(t, MinMax(new Vector3(X0 - m, Y1 - 0.1f, Z1 + m - 0.8f), new Vector3(X1 + m, Y1 + 0.9f, Z1 + m)), _trim);
            VisBox(t, MinMax(new Vector3(X0 - m, Y1 - 0.1f, Z0 - m), new Vector3(X0 - m + 0.8f, Y1 + 0.9f, Z1 + m)), _trim);
            VisBox(t, MinMax(new Vector3(X1 + m - 0.8f, Y1 - 0.1f, Z0 - m), new Vector3(X1 + m, Y1 + 0.9f, Z1 + m)), _trim);
            // 바닥면 용골
            VisBox(t, MinMax(new Vector3(X0 + 6f, Y0 - 2.4f, MidZ - 3f), new Vector3(X1 - 6f, Y0, MidZ + 3f)), _trim);
        }

        static void DeckTiles(Transform t, float x0, float z0, float x1, float z1, float y, bool up, float tile)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / tile)), nz = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / tile));
            float sx = (x1 - x0) / nx, sz = (z1 - z0) / nz;
            var rot = up ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    string pf = ((i + j) % 3 == 0) ? "P_Floor_Hangar_Pattern_03" : ((i + j) % 2 == 0 ? "P_Floor_Hangar_Pattern_01" : "P_Floor_Hangar_Pattern_02");
                    float a = x0 + i * sx, c = z0 + j * sz;
                    Fit(pf, t, rot, new Vector3(a, up ? y : y - 0.05f, c), new Vector3(a + sx, up ? y + 0.05f : y, c + sz));
                }
        }

        static void BuildBowAndStern(Transform t)
        {
            // 선수(-x): 조종석 + 양옆 카울링
            // 조종석(P_Ship_Exterior_Cockpit_01)은 BuildCockpit 에서 내부와 함께 배치
            foreach (var s in new[] { -1f, 1f })
                PlaceU("P_Ship_Exterior_Cowling_01", t, Quaternion.Euler(0, 90, 0), 2.5f, new Vector3(X0 - 1f, Y0 + 1f, MidZ + s * 39f), new Vector3(1f, 0f, 0.5f));
            // 선미(+x): 엔진 2단 × 3열 (분사구가 +x)
            foreach (var yy in new[] { MidY - 9.3f, MidY + 9.3f })
                foreach (var zz in new[] { MidZ - 38f, MidZ, MidZ + 38f })
                    PlaceU("P_Ship_Exterior_Reactor_01", t, Quaternion.Euler(0, -90, 0), 2.35f, new Vector3(X1 + 1.2f, yy, zz), new Vector3(0f, 0.5f, 0.5f));
        }

        static void BuildRings(Transform t)
        {
            const float scale = 1.45f;
            float R = 55.7f * scale;
            foreach (var xr in new[] { -58f, 9.5f })
            {
                var c = new Vector3(xr, MidY, MidZ);
                // P_Station_Ring_Half_01 은 실제로 1/4 원호 (원점 = 원 중심, x<=0, y>=0) → x축 둘레로 90°씩 4개
                for (int k = 0; k < 4; k++)
                {
                    var q = Inst("P_Station_Ring_Half_01", t);
                    if (q == null) continue;
                    q.transform.localScale = Vector3.one * scale;
                    q.transform.rotation = Quaternion.AngleAxis(k * 90f, Vector3.right) * Quaternion.Euler(0, 90, 0);
                    q.transform.position = c;
                }
                float rin = R - 4.5f;
                // 선체와 링을 잇는 지지대 4개
                Fit("P_Station_Part_Base_03", t, Quaternion.Euler(90, 0, 0), new Vector3(xr - 1.6f, Y1 + 0.5f, MidZ - 3f), new Vector3(xr + 1.6f, MidY + rin, MidZ + 3f));
                Fit("P_Station_Part_Base_03", t, Quaternion.Euler(90, 0, 0), new Vector3(xr - 1.6f, MidY - rin, MidZ - 3f), new Vector3(xr + 1.6f, Y0 - 2f, MidZ + 3f));
                Fit("P_Station_Part_Base_03", t, Quaternion.identity, new Vector3(xr - 1.6f, MidY - 3f, MidZ - rin), new Vector3(xr + 1.6f, MidY + 3f, Z0 - 2f));
                Fit("P_Station_Part_Base_03", t, Quaternion.identity, new Vector3(xr - 1.6f, MidY - 3f, Z1 + 2f), new Vector3(xr + 1.6f, MidY + 3f, MidZ + rin));
            }
        }

        static void BuildTopside(Transform t)
        {
            float deck = Y1 + 0.05f;
            // 함교 (선수 쪽 갑판 위)
            PlaceU("P_Ship_HAB_Module_01_Empty", t, Quaternion.Euler(0, -90, 0), 2.0f, new Vector3(-38f, deck, MidZ), new Vector3(0.5f, 0f, 0.5f));
            // 안테나 탑
            PlaceU("P_Roof_Ext_Module_09", t, Quaternion.identity, 1.2f, new Vector3(X0 + 10f, deck, MidZ - 30f), new Vector3(0.5f, 0f, 0.5f));
            PlaceU("P_Roof_Ext_Module_09", t, Quaternion.Euler(0, 180, 0), 1.0f, new Vector3(X0 + 10f, deck, MidZ + 30f), new Vector3(0.5f, 0f, 0.5f));
            PlaceU("P_Roof_Ext_Module_B", t, Quaternion.identity, 1.4f, new Vector3(X1 - 20f, deck, MidZ - 42f), new Vector3(0.5f, 0f, 0.5f));
            PlaceU("P_Roof_Ext_Module_B", t, Quaternion.Euler(0, 180, 0), 1.4f, new Vector3(X1 - 20f, deck, MidZ + 42f), new Vector3(0.5f, 0f, 0.5f));
            // 연료 탱크 (선미 갑판)
            foreach (var zz in new[] { -16f, 16f })
                foreach (var xx in new[] { X1 - 38f, X1 - 22f })
                    PlaceU("P_Ship_Exterior_FuelTank_01", t, Quaternion.identity, 2.2f, new Vector3(xx, deck, MidZ + zz), new Vector3(0.5f, 0f, 0.5f));
            // 환기 모듈 (좌현/우현 갑판 가장자리)
            foreach (var xx in new[] { -30f, -8f, 30f, 48f })
            {
                PlaceU("P_Roof_Ext_Module_06_NoLight", t, Quaternion.identity, 1.6f, new Vector3(xx, deck, Z0 + 5f), new Vector3(0.5f, 0f, 0.5f));
                PlaceU("P_Roof_Ext_Module_06_NoLight", t, Quaternion.Euler(0, 180, 0), 1.6f, new Vector3(xx, deck, Z1 - 5f), new Vector3(0.5f, 0f, 0.5f));
            }
            // 화물칸 (좌현 갑판 중앙)
            PlaceU("P_Ship_Exterior_CargoHold_01_B", t, Quaternion.Euler(0, 90, 0), 2.0f, new Vector3(-12f, deck, MidZ - 30f), new Vector3(0.5f, 0f, 0.5f));
            PlaceU("P_Ship_Exterior_CargoHold_01_B", t, Quaternion.Euler(0, 90, 0), 2.0f, new Vector3(-12f, deck, MidZ + 30f), new Vector3(0.5f, 0f, 0.5f));
            // 태양광 패널 (돛대 위, 수평)
            foreach (var xx in new[] { -25f, 42f })
            {
                const float ps = 0.7f;                      // 49 x 38 m
                float bottom = Y1 + 13.5f;
                var arr = PlaceU("P_Solar_Power_Panel_03", t, Quaternion.identity, ps, new Vector3(xx, bottom, MidZ), new Vector3(0.5f, 0f, 0.5f));
                float hub = arr != null ? MB(arr).center.y : bottom + 19f;
                Fit("P_Station_Part_Base_03", t, Quaternion.Euler(90, 0, 0), new Vector3(xx - 1.4f, deck, MidZ - 2.5f), new Vector3(xx + 1.4f, hub, MidZ + 2.5f));
            }
        }

        static void BuildLettering(Transform t)
        {
            // 좌현(북쪽 -z 면)과 우현(격납고 위 남쪽 +z 면)에 "VOID LAST" - 두 링(x=-58, 9.5) 사이 가운데
            const string text = "VOID LAST";
            float h = 7f, w = 5f, gap = 1.2f;
            float total = text.Length * w + (text.Length - 1) * gap;
            const float cx = -24.25f;
            float yPort = MidY + 3f, yStar = RoofTop + 6.5f;
            // 어두운 바탕 띠 (흰 외벽 위에서 글자가 읽히도록)
            VisBox(t, MinMax(new Vector3(cx - total / 2f - 2f, yPort - 1.2f, Z0 - 1.55f), new Vector3(cx + total / 2f + 2f, yPort + h + 1.2f, Z0 - 1.25f)), _trim);
            VisBox(t, MinMax(new Vector3(cx - total / 2f - 2f, yStar - 1.2f, Z1 + 1.25f), new Vector3(cx + total / 2f + 2f, yStar + h + 1.2f, Z1 + 1.55f)), _trim);
            // 좌현(-z 쪽에서 +z 를 봄): 왼쪽 -x → 오른쪽 +x 로 읽힘
            Letters(t, text, new Vector3(0, 0, -1), Z0 - 1.6f, cx - total / 2f, 1f, yPort, w, h, gap);
            // 우현(+z 쪽에서 -z 를 봄): 왼쪽 +x → 오른쪽 -x 로 읽힘
            Letters(t, text, new Vector3(0, 0, 1), Z1 + 1.6f, cx + total / 2f, -1f, yStar, w, h, gap);
        }

        static void Letters(Transform t, string text, Vector3 n, float line, float start, float dir, float y, float w, float h, float gap)
        {
            float s = start;
            foreach (char ch in text)
            {
                if (ch != ' ')
                {
                    {
                        // 글자 프리팹은 앞면이 +z 인 얇은 판 → 앞면이 바깥(n)을 보도록 돌린 뒤 상자에 맞춘다.
                        float a0 = dir > 0 ? s : s - w, a1 = a0 + w;
                        float f = n.z + n.x > 0 ? line + 0.6f : line - 0.6f;   // 판은 두께가 0 → 바탕 띠보다 0.6m 바깥
                        Vector3 mn = Mathf.Abs(n.z) > 0.5f ? new Vector3(a0, y, f) : new Vector3(f, y, a0);
                        Vector3 mx = Mathf.Abs(n.z) > 0.5f ? new Vector3(a1, y + h, f) : new Vector3(f, y + h, a1);
                        Fit("P_Letter_" + ch, t, Quaternion.LookRotation(n, Vector3.up), mn, mx);
                    }
                }
                s += dir * (w + gap);
            }
        }

        static void BuildSpace(Transform t)
        {
            // 별 (발사구 앞 / 선체 주변)
            foreach (var p in new[] { new Vector3(41f, 20f, 260f), new Vector3(-200f, 40f, -40f), new Vector3(220f, 40f, -40f) })
            {
                var s = Inst("Particle_Stars_Box_01", t);
                if (s != null) s.transform.position = p;
            }
            // 행성 (발사구 너머 아래쪽)
            var planet = Inst("P_Planet_Fake_01", t);
            if (planet != null)
            {
                var b = MB(planet);
                float d = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (d > 0.01f) planet.transform.localScale = Vector3.one * (700f / d);
                b = MB(planet);
                planet.transform.position += new Vector3(160f, -330f, 760f) - b.center;
            }
            // 선수 조종실 캐노피 너머로 보이는 두 번째 행성 (조종실 정면 약간 위)
            var planet2 = Inst("P_Planet_Fake_01", t);
            if (planet2 != null)
            {
                var b = MB(planet2);
                float d = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (d > 0.01f) planet2.transform.localScale = Vector3.one * (420f / d);
                planet2.transform.rotation = Quaternion.Euler(20f, 140f, 0f);
                b = MB(planet2);
                planet2.transform.position += new Vector3(-740f, 150f, -140f) - b.center;   // 앉은 눈높이에서 캐노피(위쪽 8~30°) 안에 들어오도록
            }
        }


        // ==================================================================== cockpit (조종실)
        /// <summary>
        /// 선수 조종석 모듈을 3.3배로 배치하고(내부 바닥 = 3층 y16), 바깥 껍데기는 외부 선체로 합치고
        /// 내벽/유리/조명은 충돌을 살려 조종실 내부로 옮긴 뒤 사람 크기 소품으로 꾸민다.
        /// </summary>
        static void BuildCockpit(Transform extTemp, Transform interior, Transform col, Transform vis, Transform props, Transform lights, Transform markers)
        {
            var shell = Inst("P_Ship_Exterior_Cockpit_01", extTemp);
            if (shell == null) return;
            PrefabUtility.UnpackPrefabInstance(shell, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            shell.transform.rotation = Quaternion.Euler(0, 90, 0);        // 프리팹 앞(-z) → 선수(-x)
            shell.transform.localScale = Vector3.one * CkScale;
            shell.transform.position = new Vector3(CkBackX, CkFloor - CkScale * 1f, MidZ);   // 내벽 바닥(로컬 y=1) = y16

            var all = shell.GetComponentsInChildren<Transform>(true);
            var moveNames = new HashSet<string> { "Interior_01", "Decal_A 1", "Cockpit_Struct_01", "Glass_01", "Ship_Interior_Lamp_02", "Light_01" };
            var dropNames = new HashSet<string> { "Ship_Interior_WallDoor_01", "Ship_Interior_WallDoor_02", "Ship_Interior_WallSep_01", "Ship_Interior_Plateform_01", "_Colliders", "P_SpaceShipLightAuto" };
            foreach (var tr in all)
            {
                if (tr == null || tr == shell.transform || !moveNames.Contains(tr.name)) continue;
                bool parentMoved = false;
                for (var p = tr.parent; p != null; p = p.parent) if (p.IsChildOf(interior)) { parentMoved = true; break; }
                if (!parentMoved) tr.SetParent(interior, true);
            }
            foreach (var tr in shell.GetComponentsInChildren<Transform>(true))
                if (tr != null && tr != shell.transform && dropNames.Contains(tr.name)) Object.DestroyImmediate(tr.gameObject);
            foreach (var l in shell.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
            foreach (var l in interior.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
            foreach (var r in interior.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.lightProbeUsage = LightProbeUsage.Off; }

            // 바닥 콜라이더(평평한 구간) + 뒤쪽 출입벽 콜라이더
            Box(col, "Cockpit_Floor", MinMax(new Vector3(CkFrontX, CkFloor - 0.3f, CkFlatZ0), new Vector3(CkWallX, CkFloor, CkFlatZ1)));
            Box(col, "Cockpit_BackWall_S", MinMax(new Vector3(CkWallX, CkFloor, CkWallZ0), new Vector3(CkWallX + 0.5f, CkTop, CkDoorZ0)));
            Box(col, "Cockpit_BackWall_N", MinMax(new Vector3(CkWallX, CkFloor, CkDoorZ1), new Vector3(CkWallX + 0.5f, CkTop, CkWallZ1)));
            Box(col, "Cockpit_BackWall_Top", MinMax(new Vector3(CkWallX, CkFloor + CkDoorH, CkDoorZ0), new Vector3(CkWallX + 0.5f, CkTop, CkDoorZ1)));

            // 사람 크기 바닥 타일 (조종석 내벽 바닥 위, 윗면 y16.013)
            int nx = 12, nz = 4;
            float tx = (CkWallX - (CkFrontX + 3f)) / nx, tz = (CkFlatZ1 - CkFlatZ0) / nz;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float x1 = CkWallX - i * tx, x0 = x1 - tx, z0 = CkFlatZ0 + j * tz;
                    string pf = (j == 1 || j == 2) ? ((i % 3 == 1) ? "P_Floor_Pattern_04_LP" : "P_Floor_Pattern_03_LP_B") : "P_Floor_Pattern_05_LP";
                    FitTop(pf, vis, Quaternion.identity, new Vector3(x0, CkFloor + 0.006f, z0), new Vector3(x1, CkFloor + 0.013f, z0 + tz));
                }

            // 뒤쪽 출입벽 (조종실 쪽을 보는 면), 연결 통로 자리만 비움
            var nIn = new Vector3(-1, 0, 0);
            CockpitWall(vis, nIn, CkWallZ0, CkDoorZ0, CkFloor, CkFloor + CkDoorH);
            CockpitWall(vis, nIn, CkDoorZ1, CkWallZ1, CkFloor, CkFloor + CkDoorH);
            CockpitWall(vis, nIn, CkWallZ0, CkWallZ1, CkFloor + CkDoorH, CkFloor + 10f);
            CockpitWall(vis, nIn, CkWallZ0, CkWallZ1, CkFloor + 10f, CkTop);
            // 출입구 테두리 + 경고등
            VisBox(vis, MinMax(new Vector3(CkWallX - 0.3f, CkFloor, CkDoorZ0 - 0.35f), new Vector3(CkWallX + 0.05f, CkFloor + CkDoorH, CkDoorZ0)), _trim);
            VisBox(vis, MinMax(new Vector3(CkWallX - 0.3f, CkFloor, CkDoorZ1), new Vector3(CkWallX + 0.05f, CkFloor + CkDoorH, CkDoorZ1 + 0.35f)), _trim);
            VisBox(vis, MinMax(new Vector3(CkWallX - 0.3f, CkFloor + CkDoorH, CkDoorZ0 - 0.35f), new Vector3(CkWallX + 0.05f, CkFloor + CkDoorH + 0.4f, CkDoorZ1 + 0.35f)), _trim);
            VisBox(vis, MinMax(new Vector3(CkWallX - 0.34f, CkFloor + CkDoorH + 0.12f, CkDoorZ0), new Vector3(CkWallX - 0.3f, CkFloor + CkDoorH + 0.24f, CkDoorZ1)), _lamp);

            float cz = (CkFlatZ0 + CkFlatZ1) * 0.5f;    // -3
            float y = CkFloor + 0.013f;
            // 앞쪽: 2인 조종석 (프리팹 +z 가 앞 → -90° 로 캐노피(-x)를 봄)
            CockpitProp("P_Ship_Interior_PilotChairBase_01", props, -90f, new Vector3(-115f, y, cz));
            CockpitProp("P_Technic_Console_01_D", props, 90f, new Vector3(-112f, y, CkFlatZ0 + 0.6f));
            CockpitProp("P_Technic_Console_01_D", props, 90f, new Vector3(-112f, y, CkFlatZ1 - 0.6f));
            // 가운데: 홀로 상황판 (양면 홀로그램, 입구/조종석 양쪽에서 보임)
            CockpitProp("P_Holo_Display_03", props, 90f, new Vector3(-106f, y, cz));
            // 양옆: 항법/통신 업무석
            foreach (var x in new[] { -99f, -91f })
            {
                // 책상 프리팹은 +z 쪽에 의자/모니터 → 남쪽 책상은 0°, 북쪽 책상은 180° 로 가운데를 봄
                CockpitProp("P_Desktop_01_A", props, 0f, new Vector3(x, y, CkFlatZ0 + 1.1f));
                CockpitProp("P_Desktop_01_A", props, 180f, new Vector3(x, y, CkFlatZ1 - 1.1f));
            }
            // 뒤쪽: 서버 랙 / 보관함 / 발전기 (출입구 양옆)
            foreach (var z in new[] { CkFlatZ0 + 0.7f, CkFlatZ0 + 1.6f, CkFlatZ0 + 2.5f })
                CockpitProp("P_Server_01", props, 90f, new Vector3(CkWallX - 0.8f, y, z));
            foreach (var z in new[] { CkFlatZ1 - 0.7f, CkFlatZ1 - 1.6f, CkFlatZ1 - 2.5f })
                CockpitProp("P_Server_01", props, 90f, new Vector3(CkWallX - 0.8f, y, z));
            CockpitProp("P_Ship_Interior_Storage_01", props, 90f, new Vector3(-86f, y, CkFlatZ0 + 1.7f));
            CockpitProp("P_Generator_Ship_02", props, 0f, new Vector3(-86.5f, y, CkFlatZ1 - 1.8f));
            CockpitProp("P_Crate_02", props, 25f, new Vector3(-95f, y, cz + 1.2f));

            // 조명: 조종석 위 작업등, 홀로 상황판 빛, 비상등
            Spot(lights, new Vector3(-114f, CkFloor + 7f, cz), 4.5f, 14f, new Color(0.75f, 0.85f, 1f));
            Spot(lights, new Vector3(-95f, CkFloor + 7f, cz), 3f, 14f, new Color(0.75f, 0.85f, 1f));
            var holo = new GameObject("Cockpit_HoloGlow"); holo.transform.SetParent(lights, false); holo.transform.position = new Vector3(-104.5f, CkFloor + 2.5f, cz);
            var hl = holo.AddComponent<Light>(); hl.type = LightType.Point; hl.color = new Color(0.3f, 0.85f, 1f); hl.intensity = 1.4f; hl.range = 9f; hl.shadows = LightShadows.None;
            foreach (var x in new[] { -88f, -102f })
            {
                Emergency(lights, new Vector3(x, CkFloor + 5.5f, CkFlatZ0 - 3.2f), 1.5f, 11f, 0);
                Emergency(lights, new Vector3(x, CkFloor + 5.5f, CkFlatZ1 + 3.2f), 1.5f, 11f, 0);
            }
            Emergency(lights, new Vector3(CkWallX - 0.6f, CkFloor + CkDoorH + 0.7f, (CkDoorZ0 + CkDoorZ1) * 0.5f), 1.4f, 8f, 1);

            // 마커
            Marker(markers, "F3_Cockpit_Entry", new Vector3(CkWallX - 1.5f, CkFloor + 0.1f, (CkDoorZ0 + CkDoorZ1) * 0.5f), 1);
            Marker(markers, "F3_Cockpit_HoloStatus", new Vector3(-103.5f, CkFloor + 0.1f, cz), 2);
            Marker(markers, "F3_Cockpit_NavLog", new Vector3(-95f, CkFloor + 0.1f, CkFlatZ1 - 3f), 2);
            Marker(markers, "F3_Cockpit_PilotSeat", new Vector3(-111.5f, CkFloor + 0.1f, cz), 3);
            Marker(markers, "F3_Monster_Cockpit_Window", new Vector3(-126f, CkFloor + 6f, cz), 6);
            Marker(markers, "F3_Cockpit_Hide_BehindServers", new Vector3(CkWallX - 2.2f, CkFloor + 0.1f, CkFlatZ0 + 1.6f), 5);
        }

        /// <summary>조종실 뒤쪽 출입벽: 실내면이 n(-x) 을 보도록, 5m 폭 모듈.</summary>
        static void CockpitWall(Transform vis, Vector3 n, float a, float b, float y0, float y1)
        {
            float L = b - a; if (L < 0.2f) return;
            int cols = Mathf.Max(1, Mathf.RoundToInt(L / 5f)); float w = L / cols;
            var rot = Quaternion.LookRotation(n, Vector3.up);
            for (int i = 0; i < cols; i++)
            {
                float s0 = a + i * w;
                string pf = y0 < CkFloor + 0.1f ? (i % 2 == 0 ? "P_Wall_Electric_Panel_02_LP" : "P_Wall_Rounded_TV_01_LP")
                          : (y1 - y0 > 4.5f ? "P_Wall_Hangar_Normal_01_LP" : "P_Wall_Hangar_Half_01_LP");
                Fit(pf, vis, rot, new Vector3(CkWallX, y0, s0), new Vector3(CkWallX + 0.5f, y1, s0 + w));
            }
        }

        static GameObject CockpitProp(string name, Transform parent, float yaw, Vector3 groundCenter)
        {
            var go = PlaceU(name, parent, Quaternion.Euler(0, yaw, 0), 1f, groundCenter, new Vector3(0.5f, 0f, 0.5f));
            if (go == null) return null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
            return go;
        }

        static string BakeCockpitNav(GameObject cockpit)
        {
            var surf = cockpit.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.Children;
            surf.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surf.BuildNavMesh();
            string res = "cockpitNav=none";
            if (surf.navMeshData != null)
            {
                string p = RootDir + "/NavMesh_Cockpit.asset";
                AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(surf.navMeshData, p);
                res = "cockpitNav=" + p;
            }
            var linkGo = new GameObject("NavLink_F3_Cockpit");
            linkGo.transform.SetParent(cockpit.transform, false);
            linkGo.transform.position = new Vector3(CkWallX, CkFloor, (CkDoorZ0 + CkDoorZ1) * 0.5f);
            var link = linkGo.AddComponent<NavMeshLink>();
            link.startPoint = new Vector3(1.5f, 0, 0);
            link.endPoint = new Vector3(-1.5f, 0, 0);
            link.width = CkDoorZ1 - CkDoorZ0 - 1f;
            link.bidirectional = true;
            return res;
        }

        // ==================================================================== nav
        static string BakeHangarNav(GameObject hangar)
        {
            var surf = hangar.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.Children;
            surf.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surf.BuildNavMesh();
            string res = "nav=none";
            if (surf.navMeshData != null)
            {
                string p = RootDir + "/NavMesh_LaunchHangar.asset";
                AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(surf.navMeshData, p);
                res = "nav=" + p;
            }
            var linkGo = new GameObject("NavLink_ShuttleRoom_Hangar");
            linkGo.transform.SetParent(hangar.transform, false);
            linkGo.transform.position = new Vector3((LinkX0 + LinkX1) * 0.5f, 0f, HZ0);
            var link = linkGo.AddComponent<NavMeshLink>();
            link.startPoint = new Vector3(0, 0, -1.5f);
            link.endPoint = new Vector3(0, 0, 1.5f);
            link.width = LinkX1 - LinkX0 - 1f;
            link.bidirectional = true;
            return res;
        }

        // ==================================================================== helpers
        static void Load()
        {
            _kit = new Dictionary<string, GameObject>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { KitDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var n = System.IO.Path.GetFileNameWithoutExtension(p);
                if (!_kit.ContainsKey(n)) _kit[n] = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            _trim = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_TrimGrey.mat");
            _lamp = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/Stage2_NewMap/M_NM_EmergencyLamp.mat");
            _lightPf = AssetDatabase.LoadAssetAtPath<GameObject>(RootDir + "/Prefabs/P_EmergencyLight.prefab");
        }

        static Transform Child(Transform p, string name)
        {
            var t = p.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(p, false); }
            return t;
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        static Bounds MinMax(Vector3 a, Vector3 b)
        {
            var mn = Vector3.Min(a, b); var mx = Vector3.Max(a, b);
            return new Bounds((mn + mx) * 0.5f, mx - mn);
        }

        static int Hash(float a, float b)
        {
            unchecked
            {
                int h = (int)(a * 73.13f) * 73856093 ^ (int)(b * 19.7f) * 19349663;
                return h & 0x7fffffff;
            }
        }

        static GameObject Inst(string name, Transform parent)
        {
            if (!_kit.TryGetValue(name, out var pf) || pf == null) { Debug.LogWarning("[ShipHull] missing prefab " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        /// <summary>MeshFilter 기준 월드 바운드 (파티클/텍스트 등은 제외).</summary>
        static Bounds MB(GameObject go)
        {
            Bounds b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                var lb = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
                var c = m.MultiplyPoint3x4(lb.center);
                var ex = lb.extents;
                var ax = m.MultiplyVector(new Vector3(ex.x, 0, 0)); var ay = m.MultiplyVector(new Vector3(0, ex.y, 0)); var az = m.MultiplyVector(new Vector3(0, 0, ex.z));
                var e = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y), Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
                var wb = new Bounds(c, e * 2f);
                if (first) { b = wb; first = false; } else b.Encapsulate(wb);
            }
            return b;
        }

        /// <summary>프리팹을 rot 로 돌린 뒤 축별 비율로 늘려 [min,max] 상자를 정확히 채운다 (90° 단위 회전 전용).</summary>
        static GameObject Fit(string name, Transform parent, Quaternion rot, Vector3 min, Vector3 max)
        {
            var go = Inst(name, parent); if (go == null) return null;
            go.transform.rotation = rot;
            var b = MB(go);
            var want = Vector3.Max(max - min, Vector3.one * 0.001f);
            var s = Vector3.one;
            for (int j = 0; j < 3; j++)
            {
                var w = rot * (j == 0 ? Vector3.right : j == 1 ? Vector3.up : Vector3.forward);
                int wi = Mathf.Abs(w.x) > 0.5f ? 0 : Mathf.Abs(w.y) > 0.5f ? 1 : 2;
                float cur = b.size[wi];
                s[j] = cur > 0.0005f ? want[wi] / cur : 1f;
            }
            go.transform.localScale = s;
            b = MB(go);
            go.transform.position += min - b.min;
            return go;
        }

        /// <summary>바닥 타일처럼 얇은 모듈: 가로/세로만 맞추고 윗면을 max.y 에 붙인다(두께는 원래대로).</summary>
        static GameObject FitTop(string name, Transform parent, Quaternion rot, Vector3 min, Vector3 max)
        {
            var go = Inst(name, parent); if (go == null) return null;
            go.transform.rotation = rot;
            var b = MB(go);
            go.transform.localScale = new Vector3((max.x - min.x) / Mathf.Max(0.001f, b.size.x), 1f, (max.z - min.z) / Mathf.Max(0.001f, b.size.z));
            b = MB(go);
            go.transform.position += new Vector3(min.x - b.min.x, max.y - b.max.y, min.z - b.min.z);
            return go;
        }

        /// <summary>균일 스케일로 배치하고, 바운드 상의 pivot01 지점이 at 에 오게 한다.</summary>
        static GameObject PlaceU(string name, Transform parent, Quaternion rot, float scale, Vector3 at, Vector3 pivot01)
        {
            var go = Inst(name, parent); if (go == null) return null;
            go.transform.rotation = rot;
            go.transform.localScale = Vector3.one * scale;
            var b = MB(go);
            var p = b.min + Vector3.Scale(b.size, pivot01);
            go.transform.position += at - p;
            return go;
        }

        /// <summary>격납고 소품: 콜라이더 유지, 바닥에 세우고 그림자 끔.</summary>
        static GameObject PlaceProp(string name, Transform parent, float yaw, float scale, Vector3 groundCenter)
        {
            var go = PlaceU(name, parent, Quaternion.Euler(0, yaw, 0), scale, groundCenter, new Vector3(0.5f, 0f, 0.5f));
            if (go == null) return null;
            if (groundCenter.y < 1f)
            {
                // 벽에 박히지 않도록 격납고 안쪽으로 밀어 넣는다
                var b = MB(go); var d = Vector3.zero;
                if (b.min.x < HX0 + 0.25f) d.x = HX0 + 0.25f - b.min.x; else if (b.max.x > HX1 - 0.25f) d.x = HX1 - 0.25f - b.max.x;
                if (b.min.z < HZ0 + 0.25f) d.z = HZ0 + 0.25f - b.min.z; else if (b.max.z > HZ1 - 0.25f) d.z = HZ1 - 0.25f - b.max.z;
                go.transform.position += d;
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
            return go;
        }

        static GameObject Box(Transform parent, string name, Bounds b)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = b.center;
            g.AddComponent<BoxCollider>().size = b.size;
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return g;
        }

        static GameObject VisBox(Transform parent, Bounds b, Material mat, bool hollowTopBottom = false)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.position = b.center;
            g.transform.localScale = b.size;
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        static void Spot(Transform parent, Vector3 pos, float intensity, float range, Color color)
        {
            var go = new GameObject("Hangar_WorkLight_" + parent.childCount.ToString("00"));
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.spotAngle = 85f; l.innerSpotAngle = 45f;
            l.intensity = intensity; l.range = range; l.color = color; l.shadows = LightShadows.None;
        }

        static void Emergency(Transform parent, Vector3 pos, float intensity, float range, int mode)
        {
            if (_lightPf == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(_lightPf, parent);
            go.name = "Hangar_EmergencyLight_" + (_lightCount++).ToString("00");
            go.transform.position = pos;
            var l = go.GetComponent<Light>();
            l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            var fl = go.GetComponent("EmergencyLightFlicker");
            if (fl != null)
            {
                var so = new SerializedObject(fl);
                so.FindProperty("mode").enumValueIndex = mode;           // 0 Flicker / 1 Pulse / 2 Broken
                so.FindProperty("baseIntensity").floatValue = intensity;
                so.FindProperty("seed").intValue = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void Marker(Transform parent, string name, Vector3 pos, int icon)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var tex = EditorGUIUtility.IconContent("sv_icon_dot" + icon + "_pix16_gizmo").image as Texture2D;
            if (tex != null) EditorGUIUtility.SetIconForObject(g, tex);
        }

        static long CountVerts(Transform t)
        {
            long v = 0;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) v += mf.sharedMesh.vertexCount;
            return v;
        }

        // ==================================================================== merge
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
        /// temp 아래 모든 정적 장식을 재질 × cell 칸 단위로 합친다 (ShipMapBuilder 와 같은 36B 정점 포맷, 16비트 인덱스,
        /// 저장 후 Read/Write 끔, 그림자 끔). 읽을 수 없는 메시는 합치지 않고 그대로 옮긴다.
        /// </summary>
        static int Merge(Transform temp, Transform visual, string folderName, float cell)
        {
            string folder = BakedDir + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(BakedDir)) AssetDatabase.CreateFolder(RootDir, "Baked");
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            AssetDatabase.CreateFolder(BakedDir, folderName);

            var w2l = visual.worldToLocalMatrix;
            var buckets = new Dictionary<string, List<Bucket>>();
            var cache = new Dictionary<Mesh, (Vector3[] v, Vector3[] n, Vector4[] t, Vector2[] uv, int[][] sub)>();
            var keep = new List<MeshRenderer>();
            var tri = new int[3];
            foreach (var mr in temp.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh; var mats = mr.sharedMaterials;
                if (mats.Length == 0) continue;
                if (!mesh.isReadable) { keep.Add(mr); continue; }
                if (!cache.TryGetValue(mesh, out var data))
                {
                    var subs = new int[mesh.subMeshCount][];
                    for (int s = 0; s < mesh.subMeshCount; s++) subs[s] = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetIndices(s) : new int[0];
                    data = (mesh.vertices, mesh.normals, mesh.tangents, mesh.uv, subs);
                    cache[mesh] = data;
                }
                var ctr = mr.bounds.center;
                string cellKey = Mathf.FloorToInt(ctr.x / cell) + "_" + Mathf.FloorToInt(ctr.y / cell) + "_" + Mathf.FloorToInt(ctr.z / cell);
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
                        string nm = folderName + "_" + bk.mat.name + "_" + kv.Key.Replace("-", "m") + "_" + part++;
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
            // 읽을 수 없는 메시는 렌더러만 복사
            foreach (var mr in keep)
            {
                var go = new GameObject(mr.name);
                go.transform.SetParent(visual, true);
                go.transform.SetPositionAndRotation(mr.transform.position, mr.transform.rotation);
                go.transform.localScale = mr.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mr.GetComponent<MeshFilter>().sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                made++;
            }
            AssetDatabase.SaveAssets();
            Object.DestroyImmediate(temp.gameObject);
            return made;
        }
    }
}
#endif
