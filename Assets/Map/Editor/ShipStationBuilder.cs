// VOID LAST - Stage2 외부 우주선: Demo_D_SpacePort_A 의 P_Station_Full 을 그대로 2배로 가져와 VOID_LAST_Ship 을 감싼다.
// - 스테이션 축을 월드 x 로 눕혀(조종실 = 서쪽 -x) 2.3배로 키우고, 가운데 모듈(Hangar_01/02 의 Livable_02) 안에 1~5층 내부가 들어가도록 배치
// - 조종실(Command_01)은 5층 NW-Cockpit 통로(z -5.5..-1.5, y32)를 서쪽으로 늘린 연결 통로로 이어지고, 앞쪽 방은 사람 크기로 다시 만든다(ShipHullBuilder.BuildBridge)
// - 꼬리(Link/Solar, 동쪽 +x)는 그대로 가져오기만 한다. 도킹된 Discovery 우주선은 원본처럼 남쪽 베이에 둔다
// - 북쪽 격납 베이(Hangar_01)가 1층에서 계단으로 내려가는 탈출 셔틀 격납고 (ShipHullBuilder.BuildBay)
// 원본 씬의 오브젝트를 복사 → 콜라이더/조명/스크립트 없이 재질 × 칸 단위로 합치고, 내부/격납고/통로/조종실과 겹치는 삼각형은 잘라낸다.
// 사용: ShipHullBuilder.Build() 뒤에 Copy() → Merge("Command") … Merge("Rest") → Finish()
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class ShipStationBuilder
    {
        public const string SourceScene = "Assets/Creepy_Cat/3D Scifi Kit Vol 4/Scenes/Scenes_Pro/Demo_D_SpacePort_A.unity";
        public const string SourceRoot = "P_Station_Full";
        const string RootDir = "Assets/Map/Stage2_Ship";
        const string BakedDir = RootDir + "/Baked";

        // 원본 스테이션 로컬 → 월드: 2.3배, y축 90° (로컬 +z → 월드 +x, 로컬 +x → 월드 -z)
        // 가운데 모듈 로컬 z -24..40 → 월드 x -73.35..73.85 (내부 x -72.8..73.3), 축 높이 2.7 → 조종 모듈 위층 바닥(로컬 12.75) = y32
        public const float S = 2.3f, Yaw = 90f, OX = -18.15f, OY = 2.7f, OZ = -3.5f;
        public static Vector3 L2W(Vector3 l) { return new Vector3(OX + S * l.z, OY + S * l.y, OZ - S * l.x); }

        // 조종실(조종 모듈 앞쪽 방) / 5층 연결 통로 — ShipHullBuilder, ship_layout(F5 K_NW-Cockpit) 과 같은 값
        public const float BXR = -216.5f, BXF = -200.6f, BZ0 = -13.5f, BZ1 = 6.5f, BFloor = 32f, BTop = 36.5f;
        public const float TZ0 = -5.5f, TZ1 = -1.5f, TH = 4f, TX1 = -72.8f, ModuleFaceX = -73.35f;

        // 북쪽 격납 베이 (Hangar_01 반쪽, 원본 _Collisions 기준): 바닥 윗면 y-35.8, 천장 13.9, 입구(우주) z-159, 뒷벽 z-82.9
        public const float BayFloor = -35.8f, BayTop = 13.9f, BayX0 = -54.5f, BayX1 = 54.6f, BayZ0 = -159f, BayZ1 = -82.9f;
        // 1층 발사 관제실(NE) 북쪽 통로(F1 K_NE-Bay)가 뒷벽으로 나오는 자리 / 위층 발판 / 동쪽 벽을 따라 내려가는 계단 / 셔틀 자리
        public const float ExitX0 = 35.5f, ExitX1 = 40.5f, LandX0 = 35f, LandX1 = 40.6f, LandZ0 = -88.9f;
        public const float StX0 = 36f, StX1 = 40.5f, Run = 5.5f, Land = 1.8f;
        public const int Flights = 9;
        public const float ShipX = 0f, ShipZ = -120f;

        /// <summary>원본 삼각형을 잘라낼 공간: 내부 5개 층 / 발사 격납고 / 5층 연결 통로(외피 포함) / 조종실 방.</summary>
        public static readonly Bounds[] KeepOut =
        {
            MinMax(new Vector3(-73.3f, -1.2f, -66.8f), new Vector3(73.8f, 37.5f, 59.8f)),      // 0 내부 1~5층
            MinMax(new Vector3(ExitX0 - 0.5f, -0.6f, BayZ1 - 0.7f), new Vector3(ExitX1 + 0.5f, 4.5f, -66f)),        // 1 1층 → 베이 통로 (모듈 속)
            MinMax(new Vector3(LandX0 - 0.4f, -1.5f, LandZ0 - 0.7f), new Vector3(LandX1 + 0.05f, 4.5f, BayZ1 - 0.1f)), // 2 위층 발판
            MinMax(new Vector3(BXF - 0.3f, BFloor - 1.1f, TZ0 - 1.5f), new Vector3(-72.5f, BFloor + TH + 1.9f, TZ1 + 1.5f)), // 3 5층 연결 통로(외피 포함)
            MinMax(new Vector3(BXR - 0.9f, BFloor - 0.8f, BZ0 - 0.9f), new Vector3(BXF + 0.7f, BTop + 1.1f, BZ1 + 0.9f)),   // 4 조종실 방
            MinMax(new Vector3(StX0 - 0.6f, BayFloor + 0.2f, LandZ0 - Flights * Run - (Flights - 1) * Land - 0.6f), new Vector3(StX1 + 0.15f, 1.6f, LandZ0)), // 5 계단
            MinMax(new Vector3(ShipX - 12f, BayFloor + 0.2f, ShipZ - 14f), new Vector3(ShipX + 12f, BayFloor + 14f, ShipZ + 14f)),                       // 6 셔틀 자리
        };

        public static readonly string[] Groups = { "Command", "Hangar_01", "Hangar_02", "Discovery", "Rest" };

        // ==================================================================== 1. 복사
        public static string Copy()
        {
            var scene = SceneManager.GetSceneByPath(ShipMapBuilder.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) return "Stage2_Ship scene not loaded";
            SceneManager.SetActiveScene(scene);
            var ext = FindExterior(scene);
            if (ext == null) return "VOID_LAST_Hull/Exterior missing (run ShipHullBuilder.Build first)";
            var oldTemp = ext.Find("_StationTemp"); if (oldTemp) Object.DestroyImmediate(oldTemp.gameObject);
            var oldSt = ext.Find("Station"); if (oldSt) Object.DestroyImmediate(oldSt.gameObject);

            var src = SceneManager.GetSceneByPath(SourceScene);
            bool opened = false;
            if (!src.IsValid() || !src.isLoaded) { src = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive); opened = true; }
            GameObject orig = null;
            foreach (var r in src.GetRootGameObjects()) if (r.name == SourceRoot) orig = r;
            if (orig == null) { if (opened) EditorSceneManager.CloseScene(src, true); return "source root missing"; }
            SceneManager.SetActiveScene(scene);
            // 비활성 상태로 복사 (반사 프로브/조명이 URP 에 등록됐다가 지워지면 렌더 오류가 남)
            bool wasActive = orig.activeSelf;
            orig.SetActive(false);
            var copy = Object.Instantiate(orig);
            orig.SetActive(wasActive);
            SceneManager.MoveGameObjectToScene(copy, scene);
            if (opened) EditorSceneManager.CloseScene(src, true);
            SceneManager.SetActiveScene(scene);

            copy.name = "_StationTemp";
            copy.transform.SetParent(ext, false);
            copy.transform.SetPositionAndRotation(new Vector3(OX, OY, OZ), Quaternion.Euler(0f, Yaw, 0f));
            copy.transform.localScale = Vector3.one * S;
            // 도킹된 Discovery 는 원본 그대로 남쪽 베이(Hangar_02)에 둔다 — 북쪽 베이는 탈출 셔틀 발사용으로 비워 둠

            // 쓰지 않는 것: 이펙트, 반사 프로브, 조종실 모듈 실내 키트(→ 가벼운 가짜 실내로 교체)
            int removed = 0;
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.name == "_FX_Particles" || t.name == "Reflection Probe") { Object.DestroyImmediate(t.gameObject); removed++; }
            }
            var kit = copy.transform.Find("P_Station_Command_01/P_Station_Livable_Module_02/_Interior_Kit");
            if (kit) { Object.DestroyImmediate(kit.gameObject); removed++; }
            var fake = copy.transform.Find("P_Station_Command_01/P_Station_Livable_Module_02/_Interior_Fake_A");
            if (fake) fake.gameObject.SetActive(true);
            // 비활성 오브젝트는 버린다 (합칠 때 어차피 제외)
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
                if (t != null && t != copy.transform && !t.gameObject.activeSelf) { Object.DestroyImmediate(t.gameObject); removed++; }
            // 조명 / 반사 프로브 / 오디오 / 스크립트 제거 (렌더링 전용 임시 사본, 합친 뒤 통째로 지운다)
            int comps = 0;
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c is Light || c is ReflectionProbe || c is AudioSource || c is MonoBehaviour || c is LODGroup)
                {
                    try { Object.DestroyImmediate(c); comps++; } catch (Exception) { }
                }
            }
            copy.SetActive(true);
            Physics.SyncTransforms();
            string bayCols = ExtractBayColliders(scene, copy.transform);

            long verts = 0; int mrs = 0;
            foreach (var mr in copy.GetComponentsInChildren<MeshRenderer>(true)) { var mf = mr.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) { verts += mf.sharedMesh.vertexCount; mrs++; } }
            EditorSceneManager.MarkSceneDirty(scene);
            return "copied mr=" + mrs + " verts=" + (verts / 1000) + "k removedGO=" + removed + " removedComp=" + comps + " " + bayCols;
        }

        /// <summary>
        /// 북쪽 격납 베이(Hangar_01)의 원본 충돌체를 VOID_LAST_Hull/LaunchHangar/Structure/StationBay 로 옮긴다 (외부 선체를 꺼도 충돌은 유지).
        /// 내부 1~5층 쪽(z > -66.3), 물리 소품(리지드바디)은 제외하고, 통로/발판/계단/셔틀 자리와 겹치는 _Collisions 상자는 그 부분만 빼고 다시 만든다.
        /// </summary>
        static string ExtractBayColliders(Scene scene, Transform copy)
        {
            Transform hangar = null;
            foreach (var r in scene.GetRootGameObjects()) if (r.name == "VOID_LAST_Hull") hangar = r.transform.Find("LaunchHangar/Structure");
            if (hangar == null) return "bayColliders=noTarget";
            var old = hangar.Find("StationBay"); if (old) Object.DestroyImmediate(old.gameObject);
            var target = new GameObject("StationBay").transform; target.SetParent(hangar, false);
            var src = copy.Find("P_Station_Hangar_01");
            if (src == null) return "bayColliders=noSource";
            var region = MinMax(new Vector3(-62f, -45f, -162f), new Vector3(70f, 25f, -66.3f));
            int kept = 0, split = 0, dropped = 0;
            foreach (var c in src.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) continue;
                if (c.attachedRigidbody != null) { dropped++; continue; }
                var b = c.bounds;
                if (b.max.z > -66.3f || !region.Intersects(b)) continue;
                bool hit = false;
                for (int k = 1; k < KeepOut.Length; k++) if (Overlap(KeepOut[k], b) > 0.01f) { hit = true; break; }
                if (hit)
                {
                    if (c is BoxCollider && c.transform.parent != null && c.transform.parent.name == "_Collisions")
                    {
                        var pieces = new List<Bounds> { b };
                        for (int k = 1; k < KeepOut.Length; k++)
                        {
                            var next = new List<Bounds>();
                            foreach (var pc in pieces) Subtract(pc, KeepOut[k], next);
                            pieces = next;
                        }
                        foreach (var pc in pieces)
                        {
                            var g = new GameObject(c.name + "_Part"); g.transform.SetParent(target, false);
                            g.transform.position = pc.center;
                            g.AddComponent<BoxCollider>().size = pc.size;
                        }
                        split++;
                    }
                    else dropped++;
                    continue;
                }
                var go = new GameObject(c.name);
                go.transform.SetParent(target, false);
                go.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
                go.transform.localScale = c.transform.lossyScale;
                if (c is BoxCollider bc) { var n = go.AddComponent<BoxCollider>(); n.center = bc.center; n.size = bc.size; }
                else if (c is MeshCollider mc) { if (mc.sharedMesh == null) { Object.DestroyImmediate(go); continue; } var n = go.AddComponent<MeshCollider>(); n.sharedMesh = mc.sharedMesh; n.convex = mc.convex; }
                else if (c is SphereCollider sc) { var n = go.AddComponent<SphereCollider>(); n.center = sc.center; n.radius = sc.radius; }
                else if (c is CapsuleCollider cc) { var n = go.AddComponent<CapsuleCollider>(); n.center = cc.center; n.radius = cc.radius; n.height = cc.height; n.direction = cc.direction; }
                else { Object.DestroyImmediate(go); continue; }
                kept++;
            }
            return "bayColliders kept=" + kept + " split=" + split + " dropped=" + dropped;
        }

        static float Overlap(Bounds a, Bounds b)
        {
            var mn = Vector3.Max(a.min, b.min); var mx = Vector3.Min(a.max, b.max);
            var d = mx - mn; if (d.x <= 0 || d.y <= 0 || d.z <= 0) return 0f;
            return d.x * d.y * d.z;
        }

        /// <summary>상자 a 에서 상자 cut 을 뺀 나머지를 최대 6개 상자로 out 에 넣는다.</summary>
        static void Subtract(Bounds a, Bounds cut, List<Bounds> outList)
        {
            if (Overlap(a, cut) <= 0.0001f) { outList.Add(a); return; }
            Vector3 mn = a.min, mx = a.max;
            for (int ax = 0; ax < 3; ax++)
            {
                if (cut.min[ax] > mn[ax]) { var hi = mx; hi[ax] = cut.min[ax]; outList.Add(MinMax(mn, hi)); mn[ax] = cut.min[ax]; }
                if (cut.max[ax] < mx[ax]) { var lo = mn; lo[ax] = cut.max[ax]; outList.Add(MinMax(lo, mx)); mx[ax] = cut.max[ax]; }
            }
        }

        static Transform FindExterior(Scene scene)
        {
            foreach (var r in scene.GetRootGameObjects()) if (r.name == "VOID_LAST_Hull") return r.transform.Find("Exterior");
            return null;
        }

        static string GroupOf(Transform t, Transform temp)
        {
            var top = t; while (top.parent != null && top.parent != temp) top = top.parent;
            switch (top.name)
            {
                case "P_Station_Command_01": return "Command";
                case "P_Station_Hangar_01": return "Hangar_01";
                case "P_Station_Hangar_02": return "Hangar_02";
                case "P_SpaceShip_Discovery_Full": return "Discovery";
                default: return "Rest";
            }
        }

        // ==================================================================== 2. 합치기 (+ 잘라내기)
        [StructLayout(LayoutKind.Sequential)]
        struct CV
        {
            public Vector3 p;
            public ushort nx, ny, nz, nw;
            public ushort tx, ty, tz, tw;
            public Vector2 uv;
        }
        struct V { public Vector3 p, n; public Vector4 t; public Vector2 uv; }
        class Bucket { public Material mat; public List<CV> v = new List<CV>(); public List<int> idx = new List<int>(); }

        public static string Merge(string group)
        {
            var scene = SceneManager.GetSceneByPath(ShipMapBuilder.ScenePath);
            var ext = FindExterior(scene);
            var temp = ext != null ? ext.Find("_StationTemp") : null;
            if (temp == null) return "no temp";
            var station = ext.Find("Station"); if (station == null) { station = new GameObject("Station").transform; station.SetParent(ext, false); }
            var old = station.Find(group); if (old) Object.DestroyImmediate(old.gameObject);
            var visual = new GameObject(group).transform; visual.SetParent(station, false);

            string folderName = "Station_" + group;
            string folder = BakedDir + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(BakedDir)) AssetDatabase.CreateFolder(RootDir, "Baked");
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            AssetDatabase.CreateFolder(BakedDir, folderName);

            const float cell = 96f;
            var buckets = new Dictionary<string, Bucket>();
            var cache = new Dictionary<Mesh, (Vector3[] v, Vector3[] n, Vector4[] t, Vector2[] uv, int[][] sub)>();
            int used = 0, skipped = 0, clippedTris = 0, droppedTris = 0;
            var boxes = new List<Bounds>(4);
            var polys = new List<List<V>>(); var next = new List<List<V>>();

            foreach (var mr in temp.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                if (GroupOf(mr.transform, temp) != group) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh; var mats = mr.sharedMaterials;
                if (mats.Length == 0 || !mesh.isReadable) { skipped++; continue; }
                var wb = mr.bounds;
                // 내부 공간 안에 통째로 들어가는 것은 버림 / 통로·조종실·격납고에 걸치는 작은 소품은 통째로 버림
                if (Contains(KeepOut[0], wb)) { skipped++; continue; }
                boxes.Clear();
                bool small = Mathf.Max(wb.size.x, wb.size.y, wb.size.z) < 40f;
                bool drop = false;
                for (int k = 0; k < KeepOut.Length; k++)
                {
                    if (!KeepOut[k].Intersects(wb)) continue;
                    if (k > 0 && small) { drop = true; break; }
                    boxes.Add(KeepOut[k]);
                }
                if (drop) { skipped++; continue; }

                if (!cache.TryGetValue(mesh, out var data))
                {
                    var subs = new int[mesh.subMeshCount][];
                    for (int s = 0; s < mesh.subMeshCount; s++) subs[s] = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetIndices(s) : new int[0];
                    data = (mesh.vertices, mesh.normals, mesh.tangents, mesh.uv, subs);
                    cache[mesh] = data;
                }
                used++;
                var m = mr.localToWorldMatrix;           // visual 은 원점/단위 변환
                var nm = m.inverse.transpose;
                bool flip = m.determinant < 0;
                var ctr = wb.center;
                string cellKey = Mathf.FloorToInt(ctr.x / cell) + "_" + Mathf.FloorToInt(ctr.y / cell) + "_" + Mathf.FloorToInt(ctr.z / cell);

                // 정점 변환 (이 렌더러 한 번)
                int vc = data.v.Length;
                var wv = new V[vc];
                for (int i = 0; i < vc; i++)
                {
                    Vector4 tg = data.t.Length > 0 ? data.t[i] : new Vector4(1, 0, 0, 1);
                    var t3 = m.MultiplyVector(new Vector3(tg.x, tg.y, tg.z)).normalized;
                    wv[i] = new V
                    {
                        p = m.MultiplyPoint3x4(data.v[i]),
                        n = data.n.Length > 0 ? nm.MultiplyVector(data.n[i]).normalized : Vector3.up,
                        t = new Vector4(t3.x, t3.y, t3.z, flip ? -tg.w : tg.w),
                        uv = data.uv.Length > 0 ? data.uv[i] : Vector2.zero
                    };
                }

                for (int s = 0; s < data.sub.Length; s++)
                {
                    var mat = mats[Mathf.Min(s, mats.Length - 1)];
                    if (mat == null || data.sub[s].Length == 0) continue;
                    string key = cellKey + "_" + mat.GetInstanceID();
                    if (!buckets.TryGetValue(key, out var bk)) buckets[key] = bk = new Bucket { mat = mat };
                    var idxs = data.sub[s];
                    var remap = new Dictionary<int, int>();
                    for (int k = 0; k < idxs.Length; k += 3)
                    {
                        int i0 = idxs[k], i1 = flip ? idxs[k + 2] : idxs[k + 1], i2 = flip ? idxs[k + 1] : idxs[k + 2];
                        if (boxes.Count == 0 || !TriTouches(wv[i0].p, wv[i1].p, wv[i2].p, boxes))
                        {
                            bk.idx.Add(Map(bk, remap, wv, i0)); bk.idx.Add(Map(bk, remap, wv, i1)); bk.idx.Add(Map(bk, remap, wv, i2));
                            continue;
                        }
                        clippedTris++;
                        polys.Clear();
                        polys.Add(new List<V> { wv[i0], wv[i1], wv[i2] });
                        foreach (var b in boxes)
                        {
                            next.Clear();
                            foreach (var poly in polys)
                            {
                                if (!b.Intersects(PolyBounds(poly))) { next.Add(poly); continue; }
                                ClipOutside(poly, b, next);
                            }
                            var sw = polys; polys = next; next = sw;
                        }
                        if (polys.Count == 0) droppedTris++;
                        foreach (var poly in polys)
                        {
                            int baseI = bk.v.Count;
                            foreach (var pv in poly) bk.v.Add(ToCV(pv));
                            for (int q = 1; q + 1 < poly.Count; q++) { bk.idx.Add(baseI); bk.idx.Add(baseI + q); bk.idx.Add(baseI + q + 1); }
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
            long totalV = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in buckets)
                {
                    var bk = kv.Value;
                    if (bk.idx.Count == 0) continue;
                    var mesh = new Mesh();
                    bool small = bk.v.Count < 65000;
                    mesh.SetVertexBufferParams(bk.v.Count, layout);
                    mesh.SetVertexBufferData(bk.v, 0, 0, bk.v.Count);
                    if (small)
                    {
                        var u16 = new ushort[bk.idx.Count]; for (int i = 0; i < u16.Length; i++) u16[i] = (ushort)bk.idx[i];
                        mesh.SetIndexBufferParams(u16.Length, IndexFormat.UInt16);
                        mesh.SetIndexBufferData(u16, 0, 0, u16.Length);
                    }
                    else
                    {
                        mesh.SetIndexBufferParams(bk.idx.Count, IndexFormat.UInt32);
                        mesh.SetIndexBufferData(bk.idx, 0, 0, bk.idx.Count);
                    }
                    mesh.subMeshCount = 1;
                    mesh.SetSubMesh(0, new SubMeshDescriptor(0, bk.idx.Count, MeshTopology.Triangles));
                    mesh.RecalculateBounds();
                    string nmn = folderName + "_" + Safe(bk.mat.name) + "_" + kv.Key.Split('_')[0] + "_" + kv.Key.Split('_')[1] + "_" + kv.Key.Split('_')[2];
                    nmn = nmn.Replace("-", "m") + "_" + created.Count;
                    mesh.name = nmn;
                    AssetDatabase.CreateAsset(mesh, folder + "/" + nmn + ".asset");
                    created.Add(mesh);
                    totalV += bk.v.Count;
                    var go = new GameObject(nmn);
                    go.transform.SetParent(visual, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = bk.mat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    r.renderingLayerMask = ExteriorLayerMask;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
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
            EditorSceneManager.MarkSceneDirty(scene);
            return group + ": renderers=" + used + " skipped=" + skipped + " meshes=" + created.Count + " verts=" + (totalV / 1000) + "k clippedTris=" + clippedTris + " droppedTris=" + droppedTris;
        }

        /// <summary>스테이션 메시 렌더링 레이어 (기본 + 1번, 나중에 외부 조명만 분리할 때 쓰도록 표시)</summary>
        public const uint ExteriorLayerMask = 1u | 2u;

        // ==================================================================== 3. 마무리
        public static string Finish()
        {
            var scene = SceneManager.GetSceneByPath(ShipMapBuilder.ScenePath);
            var ext = FindExterior(scene);
            if (ext == null) return "no exterior";
            var temp = ext.Find("_StationTemp"); if (temp) Object.DestroyImmediate(temp.gameObject);
            var station = ext.Find("Station");
            BuildTunnelSkin(station);
            // 격납고 발사구는 가운데 모듈 남쪽 반쪽의 열린 격납 베이(도킹 패드 A/B 위)로 바로 열린다 → 따로 통로를 만들지 않고 KeepOut 으로 길만 비운다
            var oldCh = station.Find("Launch_Channel"); if (oldCh) Object.DestroyImmediate(oldCh.gameObject);
            BuildSun(ext);
            string nav = BakeBayNav(ext.parent);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            long v = 0; int n = 0;
            foreach (var mf in station.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) { v += mf.sharedMesh.vertexCount; n++; }
            return "station meshes=" + n + " verts=" + (v / 1000) + "k " + nav;
        }

        /// <summary>격납 베이(계단/발판/셔틀/원본 베이 충돌체) 내비메시 + 1층 통로 끝과 잇는 링크.</summary>
        static string BakeBayNav(Transform hull)
        {
            var bay = hull.Find("LaunchHangar"); if (bay == null) return "bayNav=none";
            foreach (var s in bay.GetComponents<NavMeshSurface>()) Object.DestroyImmediate(s);
            var oldLink = bay.Find("NavLink_F1_Bay"); if (oldLink) Object.DestroyImmediate(oldLink.gameObject);
            Physics.SyncTransforms();
            var surf = bay.gameObject.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.Children;
            surf.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surf.BuildNavMesh();
            string res = "bayNav=none";
            if (surf.navMeshData != null)
            {
                string p = RootDir + "/NavMesh_LaunchHangar.asset";
                AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(surf.navMeshData, p);
                res = "bayNav=ok";
            }
            var linkGo = new GameObject("NavLink_F1_Bay");
            linkGo.transform.SetParent(bay, false);
            linkGo.transform.position = new Vector3((ExitX0 + ExitX1) * 0.5f, 0f, BayZ1);
            var link = linkGo.AddComponent<NavMeshLink>();
            link.startPoint = new Vector3(0, 0, 1.5f);
            link.endPoint = new Vector3(0, 0, -1.5f);
            link.width = ExitX1 - ExitX0 - 1f;
            link.bidirectional = true;
            return res;
        }

        /// <summary>5층 → 조종실 연결 통로의 바깥 외피 (사각 관 + 보강 링). 안쪽 벽/바닥/천장은 ShipMapBuilder 가 만든다.</summary>
        static void BuildTunnelSkin(Transform station)
        {
            var old = station.Find("Tunnel_Skin"); if (old) Object.DestroyImmediate(old.gameObject);
            var t = new GameObject("Tunnel_Skin").transform; t.SetParent(station, false);
            var trim = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_TrimGrey.mat");
            float z0 = TZ0 - 0.9f, z1 = TZ1 + 0.9f, y0 = BFloor - 0.9f, y1 = BFloor + TH + 1.4f;
            float x0 = BXF + 0.15f, x1 = ModuleFaceX - 0.05f;     // 조종실 앞벽 ~ 가운데 모듈 서쪽 면
            SkinBox(t, "Skin_S", MinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z0 + 0.3f)), trim);
            SkinBox(t, "Skin_N", MinMax(new Vector3(x0, y0, z1 - 0.3f), new Vector3(x1, y1, z1)), trim);
            SkinBox(t, "Skin_Top", MinMax(new Vector3(x0, y1 - 0.3f, z0), new Vector3(x1, y1, z1)), trim);
            SkinBox(t, "Skin_Bottom", MinMax(new Vector3(x0, y0, z0), new Vector3(x1, y0 + 0.3f, z1)), trim);
            // 보강 링: 외피 바깥에만 4개의 띠 (통로 안을 막지 않도록)
            for (float x = x0 + 3f; x < x1 - 1f; x += 7.5f)
            {
                SkinBox(t, "Rib_S", MinMax(new Vector3(x, y0 - 0.35f, z0 - 0.35f), new Vector3(x + 0.8f, y1 + 0.35f, z0)), trim);
                SkinBox(t, "Rib_N", MinMax(new Vector3(x, y0 - 0.35f, z1), new Vector3(x + 0.8f, y1 + 0.35f, z1 + 0.35f)), trim);
                SkinBox(t, "Rib_Top", MinMax(new Vector3(x, y1, z0 - 0.35f), new Vector3(x + 0.8f, y1 + 0.35f, z1 + 0.35f)), trim);
                SkinBox(t, "Rib_Bottom", MinMax(new Vector3(x, y0 - 0.35f, z0 - 0.35f), new Vector3(x + 0.8f, y0, z1 + 0.35f)), trim);
            }
        }

        /// <summary>발사 격납고 발사구(z100) → 가운데 모듈 남쪽 면까지 뚫은 발사 통로 안벽 + 유도등.</summary>
        static void BuildLaunchChannel(Transform station)
        {
            var old = station.Find("Launch_Channel"); if (old) Object.DestroyImmediate(old.gameObject);
            var t = new GameObject("Launch_Channel").transform; t.SetParent(station, false);
            var trim = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_TrimGrey.mat");
            var lamp = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/Stage2_NewMap/M_NM_EmergencyLamp.mat");
            float x0 = 19.6f, x1 = 62.4f, ix0 = 21f, ix1 = 61f, top = 13f, z0 = 100f, z1 = 154f;
            SkinBox(t, "Channel_Floor", MinMax(new Vector3(x0, -0.6f, z0), new Vector3(x1, 0f, z1)), trim);
            SkinBox(t, "Channel_Wall_W", MinMax(new Vector3(x0, 0f, z0), new Vector3(ix0, top + 1.6f, z1)), trim);
            SkinBox(t, "Channel_Wall_E", MinMax(new Vector3(ix1, 0f, z0), new Vector3(x1, top + 1.6f, z1)), trim);
            SkinBox(t, "Channel_Ceiling", MinMax(new Vector3(x0, top, z0), new Vector3(x1, top + 1.6f, z1)), trim);
            for (float z = z0 + 4f; z < z1 - 1f; z += 8f)
                SkinBox(t, "Channel_Frame", MinMax(new Vector3(ix0, top - 0.5f, z), new Vector3(ix1, top, z + 0.6f)), trim);
            if (lamp)
                foreach (float x in new[] { ix0 + 0.05f, ix1 - 0.15f })
                {
                    SkinBox(t, "Channel_GuideLamp", MinMax(new Vector3(x, 0.35f, z0), new Vector3(x + 0.1f, 0.5f, z1)), lamp);
                    SkinBox(t, "Channel_GuideLamp", MinMax(new Vector3(x, top - 0.9f, z0), new Vector3(x + 0.1f, top - 0.75f, z1)), lamp);
                }
        }

        static void SkinBox(Transform parent, string name, Bounds b, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = b.center; go.transform.localScale = b.size;
            var r = go.GetComponent<MeshRenderer>();
            if (mat) r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = ExteriorLayerMask;
        }

        /// <summary>
        /// 외부 선체를 비추는 햇빛 + 보조광 (그림자 없음). Exterior 아래에 있어서 선체 안(1~5층/연결 통로)에서는 Exterior 와 함께 꺼지고,
        /// 에디터 씬 뷰에서는 ShipHullVisibility 가 씬 카메라가 선체 안에 있을 때만 끈다.
        /// </summary>
        static void BuildSun(Transform ext)
        {
            var old = ext.Find("Station_Sun"); if (old) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("Station_Sun");
            go.transform.SetParent(ext, false);
            go.transform.rotation = Quaternion.Euler(28f, -35f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional; l.color = new Color(1f, 0.93f, 0.84f); l.intensity = 1.35f; l.shadows = LightShadows.None;
            var go2 = new GameObject("Station_Fill");
            go2.transform.SetParent(go.transform, false);
            go2.transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            var l2 = go2.AddComponent<Light>();
            l2.type = LightType.Directional; l2.color = new Color(0.55f, 0.65f, 0.9f); l2.intensity = 0.35f; l2.shadows = LightShadows.None;
            var vis = ext.parent != null ? ext.parent.GetComponent<ShipHullVisibility>() : null;
            if (vis != null) { vis.exteriorLights = new[] { l, l2 }; EditorUtility.SetDirty(vis); }
        }

        // ==================================================================== geometry helpers
        static int Map(Bucket bk, Dictionary<int, int> remap, V[] wv, int vi)
        {
            if (!remap.TryGetValue(vi, out int ni)) { ni = bk.v.Count; remap[vi] = ni; bk.v.Add(ToCV(wv[vi])); }
            return ni;
        }

        static CV ToCV(V v)
        {
            return new CV
            {
                p = v.p,
                nx = Mathf.FloatToHalf(v.n.x), ny = Mathf.FloatToHalf(v.n.y), nz = Mathf.FloatToHalf(v.n.z), nw = 0,
                tx = Mathf.FloatToHalf(v.t.x), ty = Mathf.FloatToHalf(v.t.y), tz = Mathf.FloatToHalf(v.t.z), tw = Mathf.FloatToHalf(v.t.w),
                uv = v.uv
            };
        }

        static bool TriTouches(Vector3 a, Vector3 b, Vector3 c, List<Bounds> boxes)
        {
            var tb = new Bounds(a, Vector3.zero); tb.Encapsulate(b); tb.Encapsulate(c);
            foreach (var bx in boxes) if (bx.Intersects(tb)) return true;
            return false;
        }

        static Bounds PolyBounds(List<V> poly)
        {
            var b = new Bounds(poly[0].p, Vector3.zero);
            for (int i = 1; i < poly.Count; i++) b.Encapsulate(poly[i].p);
            return b;
        }

        /// <summary>볼록 다각형에서 상자 바깥 부분만 out 에 추가 (상자 안 부분은 버림).</summary>
        static void ClipOutside(List<V> poly, Bounds b, List<List<V>> outList)
        {
            var cur = poly;
            for (int i = 0; i < 6; i++)
            {
                int ax = i >> 1; bool isMin = (i & 1) == 0;
                float val = isMin ? b.min[ax] : b.max[ax];
                var ins = new List<V>(cur.Count + 2); var outs = new List<V>(cur.Count + 2);
                int n = cur.Count;
                for (int k = 0; k < n; k++)
                {
                    var p = cur[k]; var q = cur[(k + 1) % n];
                    float dp = isMin ? p.p[ax] - val : val - p.p[ax];
                    float dq = isMin ? q.p[ax] - val : val - q.p[ax];
                    if (dp >= 0) ins.Add(p); else outs.Add(p);
                    if ((dp >= 0) != (dq >= 0))
                    {
                        float t = dp / (dp - dq);
                        var mv = LerpV(p, q, t);
                        ins.Add(mv); outs.Add(mv);
                    }
                }
                if (outs.Count >= 3) outList.Add(outs);
                if (ins.Count < 3) return;
                cur = ins;
            }
        }

        static V LerpV(V a, V b, float t)
        {
            return new V
            {
                p = Vector3.Lerp(a.p, b.p, t),
                n = Vector3.Lerp(a.n, b.n, t).normalized,
                t = new Vector4(Mathf.Lerp(a.t.x, b.t.x, t), Mathf.Lerp(a.t.y, b.t.y, t), Mathf.Lerp(a.t.z, b.t.z, t), a.t.w),
                uv = Vector2.Lerp(a.uv, b.uv, t)
            };
        }

        static bool Contains(Bounds outer, Bounds inner) { return outer.Contains(inner.min) && outer.Contains(inner.max); }
        static string Safe(string s) { foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }
        public static Bounds MinMax(Vector3 a, Vector3 b) { var bb = new Bounds(); bb.SetMinMax(Vector3.Min(a, b), Vector3.Max(a, b)); return bb; }
    }
}
#endif
