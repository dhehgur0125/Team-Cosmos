// 소품 충돌·면 고치기 (빌드에서 대원이 소품에 끼고, 어떤 소품은 한쪽 면이 안 보이던 문제, 2026-11-25)
// 1) 부서진 소품 14종 (Assets/Map/Stage2_Ship/Interior/Prefabs, 기울어진 캐비닛·찌그러진 상자 등)
//    - 충돌: 얇고 열린 판 수백 장으로 된 오목한 충돌(MeshCollider)이라 대원이 판 사이로 파고들어 끼었다.
//      → 조각마다 볼록한 덩어리 충돌로 바꿈 (큰 조각 안에 들어가는 작은 조각·20cm보다 작은 조각은 뺌).
//        원래 충돌은 끈 채로 남겨 둠 (씬에서 원래 충돌을 끈 소품은 새 충돌도 끔 — 숨는 틈 등 기존 의도 유지).
//    - 면: 판이 한쪽 면만 있어 뒤에서 보면 사라졌다 → 메시에 뒤집은 면을 덧붙여 양쪽에서 보이게.
// 2) 살덩이·버섯 등 바이오 소품 (BioHorrorSciFiEnvironment): 벽·천장에 붙어 바닥에서 떠 있는 것은 충돌을 끔
//    (벽과 덩어리 사이 틈에 대원이 끼었다). 바닥에 놓인 것은 볼록 충돌로 (오목 충돌이면 바꿈).
// 메뉴: Tools/VOID LAST/Optimize/Fix Prop Colliders And Faces — 소품 프리팹 + 원본 씬 + 층 씬 5개.
// 메뉴 9로 층을 다시 나눌 때도 2)는 자동 실행 (1)은 프리팹이라 그대로 남음).
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VoidLast.MapTools
{
    public static class PropCollisionFix
    {
        const string PropDir = "Assets/Map/Stage2_Ship/Interior/Prefabs";
        const string HullDir = "Assets/Map/Stage2_Ship/Interior/Meshes/Hulls";
        const string SourceScene = "Assets/Map/Scenes/Stage2_Ship.unity";
        public const string HullChildName = "Collision (Convex Parts)";
        const float MinPart = 0.2f;      // 이보다 작은 조각(나사·장식)은 충돌에서 뺌
        const float Skin = 0.02f;        // 납작한 판도 두께가 생기게 앞뒤로 2cm
        const float FloatAbove = 0.4f;   // 바닥에서 이만큼 떠 있으면 벽·천장에 붙은 것으로 봄

        [MenuItem("Tools/VOID LAST/Optimize/Fix Prop Colliders And Faces")]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[PropFix] play 중에는 실행할 수 없음"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new List<string> { FixInteriorPrefabs() };
            var scenes = new List<string> { SourceScene };
            for (int f = 5; f >= 1; f--) scenes.Add(FloorSceneBuilder.ScenePath(f));
            foreach (var path in scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                log.Add(scene.name + ": " + FixOpenScene());
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log("[PropFix] " + string.Join(" | ", log));
        }

        // ───────────────────────── 1) 부서진 소품 프리팹 ─────────────────────────

        public static string FixInteriorPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(HullDir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(HullDir).Replace('\\', '/'), Path.GetFileName(HullDir));
            var doubled = new HashSet<Mesh>();
            var log = new List<string>();
            int flipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    var root = scope.prefabContentsRoot;
                    var original = root.GetComponent<MeshCollider>();
                    if (original == null || original.sharedMesh == null) continue;

                    // 면: 보이는 메시마다 뒤집은 면을 덧붙임
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null && doubled.Add(mf.sharedMesh) && MakeTwoSided(mf.sharedMesh)) flipped++;

                    // 충돌: 조각별 볼록 덩어리
                    var hulls = BuildHullMeshes(original.sharedMesh, root.name);
                    // 다시 실행할 때는 같은 자식을 그대로 쓴다 (씬에서 이 자식을 꺼 둔 표시가 사라지지 않게)
                    var existing = root.transform.Find(HullChildName);
                    var holder = existing != null ? existing.gameObject : new GameObject(HullChildName);
                    foreach (var oldCol in holder.GetComponents<Collider>()) Object.DestroyImmediate(oldCol, true);
                    holder.layer = root.layer;
                    if (existing == null) holder.transform.SetParent(root.transform, false);
                    GameObjectUtility.SetStaticEditorFlags(holder, GameObjectUtility.GetStaticEditorFlags(root));
                    foreach (var h in hulls)
                    {
                        var mc = holder.AddComponent<MeshCollider>();
                        mc.sharedMesh = h;
                        mc.convex = true;
                        mc.sharedMaterial = original.sharedMaterial;
                    }
                    original.enabled = false;   // 씬마다 켜고 끈 상태 표시로만 남김 (FixOpenScene이 봄)
                    log.Add(root.name + " " + hulls.Count);
                }
            }
            AssetDatabase.SaveAssets();
            return "props (" + log.Count + ", two-sided meshes " + flipped + "): " + string.Join(", ", log);
        }

        /// <summary>메시에 뒤집은 면(법선도 반대)을 덧붙여 양쪽에서 보이게. 이미 했으면 false</summary>
        static bool MakeTwoSided(Mesh mesh)
        {
            if (!mesh.isReadable || IsTwoSided(mesh)) return false;
            int n = mesh.vertexCount;
            var v = new List<Vector3>(); mesh.GetVertices(v);
            var nr = new List<Vector3>(); mesh.GetNormals(nr);
            var tg = new List<Vector4>(); mesh.GetTangents(tg);
            var col = new List<Color>(); mesh.GetColors(col);
            var uvs = new List<List<Vector2>>();
            for (int ch = 0; ch < 8; ch++) { var u = new List<Vector2>(); mesh.GetUVs(ch, u); uvs.Add(u); }
            var subs = new List<int[]>();
            for (int s = 0; s < mesh.subMeshCount; s++) subs.Add(mesh.GetTriangles(s));

            v.AddRange(v.GetRange(0, n));
            if (nr.Count == n) for (int i = 0; i < n; i++) nr.Add(-nr[i]);
            if (tg.Count == n) for (int i = 0; i < n; i++) tg.Add(new Vector4(tg[i].x, tg[i].y, tg[i].z, -tg[i].w));
            if (col.Count == n) col.AddRange(col.GetRange(0, n));
            foreach (var u in uvs) if (u.Count == n) u.AddRange(u.GetRange(0, n));

            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : mesh.indexFormat;
            mesh.SetVertices(v);
            if (nr.Count == v.Count) mesh.SetNormals(nr);
            if (tg.Count == v.Count) mesh.SetTangents(tg);
            if (col.Count == v.Count) mesh.SetColors(col);
            for (int ch = 0; ch < 8; ch++) if (uvs[ch].Count == v.Count) mesh.SetUVs(ch, uvs[ch]);
            for (int s = 0; s < subs.Count; s++)
            {
                var t = subs[s];
                var both = new int[t.Length * 2];
                System.Array.Copy(t, both, t.Length);
                for (int i = 0; i < t.Length; i += 3)
                {
                    both[t.Length + i] = t[i] + n;
                    both[t.Length + i + 1] = t[i + 2] + n;
                    both[t.Length + i + 2] = t[i + 1] + n;
                }
                mesh.SetTriangles(both, s, false);
            }
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return true;
        }

        /// <summary>뒤쪽 절반이 앞쪽 절반의 뒤집은 면이면 이미 양면</summary>
        static bool IsTwoSided(Mesh mesh)
        {
            int n = mesh.vertexCount;
            if (n % 2 != 0 || mesh.subMeshCount == 0) return false;
            var t = mesh.GetTriangles(0);
            if (t.Length % 6 != 0 || t.Length == 0) return false;
            int h = t.Length / 2, half = n / 2;
            return t[h] == t[0] + half && t[h + 1] == t[2] + half && t[h + 2] == t[1] + half;
        }

        /// <summary>충돌 메시를 이어진 조각으로 나눠, 볼록 충돌용 메시 목록을 만들어 저장 (작은 조각·큰 조각 안에 드는 조각은 뺌)</summary>
        static List<Mesh> BuildHullMeshes(Mesh source, string name)
        {
            var v = source.vertices; var t = source.triangles;
            // 같은 자리 점 합치기 → 이어진 조각 찾기
            var id = new Dictionary<Vector3Int, int>(); var vid = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 1000), Mathf.RoundToInt(v[i].y * 1000), Mathf.RoundToInt(v[i].z * 1000));
                if (!id.TryGetValue(k, out int x)) { x = id.Count; id[k] = x; }
                vid[i] = x;
            }
            var parent = new int[id.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int i = 0; i < t.Length; i += 3)
            {
                parent[Find(vid[t[i]])] = Find(vid[t[i + 1]]);
                parent[Find(vid[t[i + 1]])] = Find(vid[t[i + 2]]);
            }
            var tris = new Dictionary<int, List<int>>();
            for (int i = 0; i < t.Length; i += 3)
            {
                int r = Find(vid[t[i]]);
                if (!tris.TryGetValue(r, out var l)) tris[r] = l = new List<int>();
                l.Add(i);
            }
            var parts = new List<(Bounds b, List<int> tri)>();
            foreach (var kv in tris)
            {
                var b = new Bounds(v[t[kv.Value[0]]], Vector3.zero);
                foreach (int i in kv.Value) { b.Encapsulate(v[t[i]]); b.Encapsulate(v[t[i + 1]]); b.Encapsulate(v[t[i + 2]]); }
                if (Mathf.Max(b.size.x, b.size.y, b.size.z) >= MinPart) parts.Add((b, kv.Value));
            }
            // 큰 조각부터. 이미 고른 조각의 상자 안에 통째로 드는 조각은 뺌 (상자 안 갈빗대·나사 등)
            parts.Sort((a, c) => (c.b.size.x * c.b.size.y * c.b.size.z).CompareTo(a.b.size.x * a.b.size.y * a.b.size.z));
            var kept = new List<(Bounds b, List<int> tri)>();
            foreach (var p in parts)
            {
                bool inside = false;
                foreach (var k in kept)
                {
                    var grown = k.b; grown.Expand(0.1f);
                    if (grown.Contains(p.b.min) && grown.Contains(p.b.max)) { inside = true; break; }
                }
                if (!inside) kept.Add(p);
            }

            string assetPath = HullDir + "/" + name + "_Hulls.asset";
            AssetDatabase.DeleteAsset(assetPath);
            var meshes = new List<Mesh>();
            var dirs = SphereDirections(HullDirections);
            for (int k = 0; k < kept.Count; k++)
            {
                // 조각의 점들 (납작한 판도 두께가 생기게 면 앞뒤로 Skin만큼 띄운 점 포함)
                var pts = new List<Vector3>();
                foreach (int i in kept[k].tri)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    var nrm = Vector3.Cross(b - a, c - a);
                    nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized * Skin : Vector3.zero;
                    foreach (var off in new[] { Vector3.zero, nrm, -nrm }) { pts.Add(a + off); pts.Add(b + off); pts.Add(c + off); }
                }
                // 볼록 충돌은 면이 255개까지라, 여러 방향으로 가장 바깥에 있는 점만 남긴다 (점 100개 이하 → 면 196개 이하)
                var verts = new List<Vector3>();
                foreach (var d in dirs)
                {
                    int best = 0; float bestDot = float.MinValue;
                    for (int i = 0; i < pts.Count; i++) { float dot = Vector3.Dot(pts[i], d); if (dot > bestDot) { bestDot = dot; best = i; } }
                    bool dup = false;
                    foreach (var p in verts) if ((p - pts[best]).sqrMagnitude < 1e-6f) { dup = true; break; }
                    if (!dup) verts.Add(pts[best]);
                }
                var idx = new List<int>();   // 볼록 충돌은 점만 쓴다. 메시 모양만 갖추려고 부채꼴로 잇는다
                for (int i = 1; i + 1 < verts.Count; i++) { idx.Add(0); idx.Add(i); idx.Add(i + 1); }
                var m = new Mesh { name = name + "_Hull_" + k };
                m.SetVertices(verts);
                m.SetTriangles(idx, 0);
                m.RecalculateBounds();
                if (k == 0) AssetDatabase.CreateAsset(m, assetPath);
                else AssetDatabase.AddObjectToAsset(m, assetPath);
                meshes.Add(m);
            }
            AssetDatabase.ImportAsset(assetPath);
            return meshes;
        }

        const int HullDirections = 100;

        /// <summary>구 위에 고르게 퍼진 방향 n개 (피보나치 나선)</summary>
        static Vector3[] SphereDirections(int n)
        {
            var d = new Vector3[n];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / n, r = Mathf.Sqrt(1f - y * y), a = golden * i;
                d[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            return d;
        }

        // ───────────────────────── 2) 열린 씬 ─────────────────────────

        /// <summary>열린 씬의 소품 충돌을 정리 (원본 씬·층 씬 공통)</summary>
        public static string FixOpenScene()
        {
            int propsOff = 0, bioOff = 0, bioConvex = 0;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var rootGo in scene.GetRootGameObjects())
            {
                // 1) 부서진 소품: 씬에서 원래 충돌을 꺼 둔 것은 새 충돌도 끔
                foreach (var hull in rootGo.GetComponentsInChildren<Transform>(true))
                {
                    if (hull.name != HullChildName || hull.parent == null) continue;
                    var original = hull.parent.GetComponent<MeshCollider>();
                    if (original == null) continue;
                    bool off = OriginalWasDisabled(original);
                    if (hull.gameObject.activeSelf == off) { hull.gameObject.SetActive(!off); }
                    if (off) propsOff++;
                }

                // 2) 바이오 소품
                foreach (var c in rootGo.GetComponentsInChildren<Collider>(true))
                {
                    if (!c.enabled || c.isTrigger) continue;
                    var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(c.gameObject);
                    if (src == null || !AssetDatabase.GetAssetPath(src).Contains("BioHorrorSciFiEnvironment")) continue;
                    var b = c.bounds;
                    if (b.size.y > 6f) continue;   // 방·복도 구조물은 손대지 않음
                    float floorY = Mathf.Floor((b.min.y + 0.5f) / 8f) * 8f;
                    if (b.min.y - floorY > FloatAbove)
                    {
                        Undo.RecordObject(c, "Prop fix");
                        c.enabled = false;
                        bioOff++;
                    }
                    else if (c is MeshCollider mc && !mc.convex)
                    {
                        Undo.RecordObject(mc, "Prop fix");
                        mc.convex = true;
                        bioConvex++;
                    }
                }
            }
            return "props kept off " + propsOff + ", bio wall/ceiling colliders off " + bioOff + ", bio → convex " + bioConvex;
        }

        /// <summary>이 소품 인스턴스에서 원래 충돌을 씬에서 따로 꺼 두었는지 (프리팹 값과 상관없이)</summary>
        static bool OriginalWasDisabled(MeshCollider original)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(original)) return !original.enabled;
            var mods = PrefabUtility.GetPropertyModifications(original.gameObject);
            if (mods != null)
            {
                var srcCol = PrefabUtility.GetCorrespondingObjectFromSource(original);
                foreach (var m in mods)
                    if (m.target == srcCol && m.propertyPath == "m_Enabled") return m.value == "0";
            }
            return false;   // 씬에서 따로 손대지 않음 = 원래 켜져 있던 것
        }
    }
}
#endif
