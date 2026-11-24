// 층 인테리어(칸막이·엄폐물·잔해)를 배치표 파일대로 원본 씬(Stage2_Ship)에 놓는다.
// 배치표: Assets/Map/Stage2_Ship/Interior/Layout_F{층}.txt
//   #그룹이름
//   프리팹이름|x|z|회전Y|높이(바닥 기준, ^ = 천장에 붙임)|크기배율|기울임Z
// x,z 는 물체 중심(바운드 중심) 좌표. 다시 실행하면 그 층의 Interior 를 지우고 새로 놓는다.
// 놓은 뒤에는 "9 Split Stage2 Into Floor Scenes" 또는 FloorSceneBuilder.BuildFloor(층) 으로 층 씬에 반영.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VoidLast.MapTools
{
    public static class InteriorPlacer
    {
        static readonly string[] FloorRoots = { "", "F1_Hangar", "F2_Cargo", "F3_Control", "F4_Power", "F5_Crew" };
        // SciFi Populate 는 HDRP 전용 셰이더라 URP 에서 분홍색으로 나와서 뺀다
        static readonly string[] PrefabDirs = { "Assets/BefourStudios/SciFiEngineersRoom", "Assets/BefourStudios/SciFiSpaceModularEnvironment", "Assets/BefourStudios/BioHorrorSciFiEnvironment", "Assets/Creepy_Cat" };
        const float RoomHeight = 4f;

        const string ColdDir = "Assets/Map/Stage2_Ship/Interior/Materials/Cold";

        /// <summary>
        /// 그 층(5층은 조종실 포함)의 Wall_Atlas 재질을 차가운 금속 톤 복사본으로 바꾼다.
        /// 게임 안에서는 실내에 있으면 외부 햇빛이 꺼져서(ShipHullVisibility) 빨간 비상등만 남는다. 너무 어둡게 잡으면 안 보인다.
        /// 원본 에셋 재질은 건드리지 않고 복사본(ColdDir)을 씬에서만 바꿔 끼운다.
        /// </summary>
        public static string ApplyColdMaterials(int floor)
        {
            var sc = EditorSceneManager.GetActiveScene();
            if (sc.path != FloorSceneBuilder.MasterPath) return "원본 씬(Stage2_Ship)을 먼저 열어야 함: " + sc.path;
            if (!AssetDatabase.IsValidFolder(ColdDir)) AssetDatabase.CreateFolder("Assets/Map/Stage2_Ship/Interior/Materials", "Cold");
            var targets = new List<Transform>();
            foreach (var r in sc.GetRootGameObjects())
            {
                if (r.name == "VOID_LAST_Ship") targets.Add(r.transform.Find(FloorRoots[floor]));
                if (r.name == "VOID_LAST_Hull" && floor == 5) targets.Add(r.transform.Find("Cockpit"));
            }
            var map = new Dictionary<Material, Material>();
            int swapped = 0;
            foreach (var t in targets)
            {
                if (t == null) continue;
                foreach (var rend in t.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = rend.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || !m.name.StartsWith("Wall_Atlas_") || m.name.EndsWith("_Cold")) continue;
                        Material cold;
                        if (!map.TryGetValue(m, out cold)) { cold = ColdCopy(m); map[m] = cold; }
                        mats[i] = cold; changed = true;
                    }
                    if (changed) { rend.sharedMaterials = mats; swapped++; }
                }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(sc);
            return "F" + floor + " 차가운 재질: 렌더러 " + swapped + "개, 재질 " + map.Count + "종";
        }

        static Material ColdCopy(Material src)
        {
            string p = ColdDir + "/" + src.name + "_Cold.mat";
            var cold = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (cold == null) { cold = new Material(src) { name = src.name + "_Cold" }; AssetDatabase.CreateAsset(cold, p); }
            else cold.CopyPropertiesFromMaterial(src);
            Color c = src.GetColor("_BaseColor");
            string n = src.name;
            if (n.EndsWith("_White")) c = new Color(0.62f, 0.68f, 0.74f);        // 흰 패널 → 차가운 강철색
            else if (n.EndsWith("_Grey_A")) c = new Color(0.45f, 0.49f, 0.54f);
            else if (n.EndsWith("_Grey_B")) c = new Color(0.24f, 0.26f, 0.29f);
            else if (n.EndsWith("_Red")) c = new Color(0.40f, 0.10f, 0.08f);     // 빨강 → 어두운 녹빛
            else if (n.EndsWith("_Yellow")) c = new Color(0.55f, 0.38f, 0.02f);
            else if (n.EndsWith("_Orange")) c = new Color(0.45f, 0.20f, 0.04f);
            else if (n.EndsWith("_Blue")) c = new Color(0.06f, 0.14f, 0.24f);
            cold.SetColor("_BaseColor", c);
            if (cold.HasProperty("_Color")) cold.SetColor("_Color", c);
            if (cold.HasProperty("_EmissionColor")) cold.SetColor("_EmissionColor", src.GetColor("_EmissionColor") * 0.7f);   // 패널 불빛도 줄인다
            EditorUtility.SetDirty(cold);
            return cold;
        }

        const string BakedDir = "Assets/Map/Stage2_Ship/Interior/Baked";
        const string BioSeamless = "Assets/BefourStudios/BioHorrorSciFiEnvironment/Art/Materials/M_BioHorror_Seamless.mat";
        const float BioTile = 3f;   // 바이오매스 무늬 한 장이 덮는 길이(m)

        /// <summary>네 점으로 양면 사각형을 만든다. 무늬는 실제 크기에 맞춰 반복된다.</summary>
        static void Patch(Transform parent, string name, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            float u = Vector3.Distance(p0, p3) / BioTile, v = Vector3.Distance(p0, p1) / BioTile;
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { p0, p1, p2, p3 };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, v), new Vector2(u, v), new Vector2(u, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };   // 앞뒤 양면
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(BioSeamless);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>울퉁불퉁한 얼룩 여러 장을 메시 하나로 모은다.</summary>
        sealed class SplatMesh
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();
            public int Count;

            public void Add(Vector3 center, Vector3 normal, float radius, System.Random rng)
            {
                Vector3 tan = Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, normal).normalized;
                Vector3 bit = Vector3.Cross(normal, tan);
                float spin = (float)rng.NextDouble() * Mathf.PI * 2f, stretch = 0.7f + (float)rng.NextDouble() * 0.8f;
                Vector3 c = center + normal * (0.03f + (float)rng.NextDouble() * 0.02f);   // 겹친 얼룩끼리 깜빡이지 않게 높이를 조금씩 다르게
                Vector2 uvOff = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                int i0 = v.Count;
                const int seg = 14;
                // 꼭짓점마다 따로 난수를 쓰면 별 모양으로 뾰족해진다. 느린 물결 몇 개를 더해 번진 얼룩 모양을 만든다.
                float p1 = (float)rng.NextDouble() * 6.28f, p2 = (float)rng.NextDouble() * 6.28f, p3 = (float)rng.NextDouble() * 6.28f;
                v.Add(c); uv.Add(uvOff);
                for (int i = 0; i < seg; i++)
                {
                    float ang = spin + i * Mathf.PI * 2f / seg;
                    float r = radius * (0.72f + 0.16f * Mathf.Sin(2f * ang + p1) + 0.11f * Mathf.Sin(3f * ang + p2) + 0.07f * Mathf.Sin(5f * ang + p3));
                    float px = Mathf.Cos(ang) * r * stretch, py = Mathf.Sin(ang) * r;
                    v.Add(c + tan * px + bit * py);
                    uv.Add(uvOff + new Vector2(px, py) / BioTile);
                }
                for (int i = 0; i < seg; i++)
                {
                    int p = i0 + 1 + i, q = i0 + 1 + (i + 1) % seg;
                    t.Add(i0); t.Add(p); t.Add(q);
                    t.Add(i0); t.Add(q); t.Add(p);   // 양면
                }
                Count++;
            }

            public void Build(Transform parent, string name, string assetPath = null)
            {
                if (Count == 0) return;
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                // 씬에 넣으면 원본 씬과 층 씬에 두 번 저장돼 파일이 커진다. 따로 에셋으로 둔다.
                if (assetPath != null) AssetDatabase.CreateAsset(mesh, assetPath);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(BioSeamless);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static readonly string[] SprayGrowths = { "SM_BioMassSet_01_03", "SM_BioMassSet_02_01", "SM_BioMassSet_04_03", "SM_BioMassSet_01_01", "SM_ShroomSet_1", "SM_ShroomSet_2", "SM_ShroomSet_3", "SM_ShroomSet_4" };

        /// <summary>
        /// 구역 안에서 아래·옆으로 광선을 쏴서 맞은 표면(우주선 구조물·소품·앞서 놓은 인테리어)에 얼룩을 붙인다.
        /// 일부 자리에는 작은 덩어리도 붙인다 (충돌을 꺼서 장식으로만).
        /// </summary>
        static int Spray(string assetPath, Transform grp, Transform floorRoot, Dictionary<string, string> dict, List<Vector3> keepClear, float floorY,
            float x0, float z0, float x1, float z1, int count, int seed, float ylo, float yhi)
        {
            var rng = new System.Random(seed);
            var splats = new SplatMesh();
            var ship = floorRoot.parent;
            int growths = 0;
            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(x0, x1, (float)rng.NextDouble()), z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
                bool down = rng.NextDouble() < 0.55;
                Vector3 origin, dir; float max;
                if (down) { origin = new Vector3(x, floorY + yhi, z); dir = Vector3.down; max = yhi - ylo + 0.3f; }
                else
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    origin = new Vector3(x, floorY + Mathf.Lerp(ylo + 0.3f, yhi - 0.3f, (float)rng.NextDouble()), z);
                    dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)); max = 7f;
                }
                RaycastHit hit;
                if (!Physics.Raycast(origin, dir, out hit, max, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!hit.collider.transform.IsChildOf(ship)) continue;   // 임무 장치·몬스터·대원에는 붙이지 않는다
                bool near = false;
                foreach (var k in keepClear) if (Mathf.Abs(k.y - hit.point.y) < 3f && (new Vector2(k.x - hit.point.x, k.z - hit.point.z)).sqrMagnitude < 1.3f * 1.3f) { near = true; break; }
                if (near) continue;
                bool onFloor = hit.normal.y > 0.7f && hit.point.y < floorY + 0.3f;
                // 바닥이 아닌 곳(소품 윗면·벽)은 가장자리 밖으로 삐져나오면 떠 보이므로 작게
                float radius = onFloor ? Mathf.Lerp(0.6f, 1.9f, (float)rng.NextDouble()) : Mathf.Lerp(0.2f, 0.55f, (float)rng.NextDouble());
                splats.Add(hit.point, hit.normal, radius, rng);
                // 큰 얼룩 둘레에 작은 얼룩을 겹쳐 덩어리진 윤곽을 만든다
                Vector3 st = Mathf.Abs(hit.normal.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, hit.normal).normalized, sbt = Vector3.Cross(hit.normal, st);
                int sat = onFloor ? 3 : 2;
                for (int s = 0; s < sat; s++)
                {
                    float sa = (float)rng.NextDouble() * Mathf.PI * 2f, sd = radius * Mathf.Lerp(0.5f, 0.9f, (float)rng.NextDouble());
                    splats.Add(hit.point + (st * Mathf.Cos(sa) + sbt * Mathf.Sin(sa)) * sd, hit.normal, radius * Mathf.Lerp(0.35f, 0.7f, (float)rng.NextDouble()), rng);
                }

                if (rng.NextDouble() < 0.15)
                {
                    string name = SprayGrowths[rng.Next(SprayGrowths.Length)];
                    if (!dict.ContainsKey(name)) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(dict[name]));
                    go.transform.SetParent(grp, true);
                    go.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                    go.transform.localScale *= Mathf.Lerp(0.4f, 0.9f, (float)rng.NextDouble());
                    foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
                    var rs = go.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        var b = rs[0].bounds;
                        foreach (var r in rs) b.Encapsulate(r.bounds);
                        Vector3 n = hit.normal;
                        float half = Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.y) * b.extents.y + Mathf.Abs(n.z) * b.extents.z;
                        go.transform.position += hit.point + n * half * 0.7f - b.center;   // 표면에 살짝 박히게
                    }
                    growths++;
                }
            }
            splats.Build(grp, "BioSplats", assetPath);
            return splats.Count + growths;
        }

        /// <summary>배치표 그룹 이름(#Blockers / #Burst / #Hazard)에 따라 반응하는 바이오매스로 만든다.</summary>
        static void AttachGrowth(GameObject go, string group, Bounds b, int id)
        {
            BioGrowthKind kind;
            if (group.StartsWith("Blockers")) kind = BioGrowthKind.Blocker;
            else if (group.StartsWith("Burst")) kind = BioGrowthKind.Burst;
            else if (group.StartsWith("Hazard")) kind = BioGrowthKind.Hazard;
            else return;
            var g = go.AddComponent<BioGrowth>();
            g.id = id; g.kind = kind;
            if (kind == BioGrowthKind.Hazard) g.radius = Mathf.Max(b.extents.x, b.extents.z) + 1f;
            if (kind != BioGrowthKind.Blocker) return;
            // 부서지면 몬스터 길도 열려야 하므로 길찾기 굽기에서는 빼고, 살아 있는 동안만 길을 깎는 장애물을 붙인다
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var block = new GameObject("NavBlock");
            block.transform.SetParent(go.transform, false);
            block.transform.position = b.center;
            block.transform.rotation = Quaternion.identity;   // 부모가 돌아가 있어도 상자는 세계 축에 맞춘다
            Vector3 ls = block.transform.lossyScale;
            var ob = block.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            ob.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            ob.size = new Vector3(b.size.x / ls.x, b.size.y / ls.y, b.size.z / ls.z);
            ob.carving = true;
        }

        /// <summary>원본 씬의 멀티플레이 루트에 BioGrowthSystem이 없으면 만든다 (층 씬으로 나눌 때 같이 복사된다).</summary>
        static void EnsureGrowthSystem(UnityEngine.SceneManagement.Scene sc)
        {
            foreach (var r in sc.GetRootGameObjects())
            {
                if (r.name != MultiplayerSetup.MultiRootName || r.transform.Find("Bio Growth System") != null) continue;
                var go = new GameObject("Bio Growth System");
                go.transform.SetParent(r.transform, false);
                go.AddComponent<Unity.Netcode.NetworkObject>();
                go.AddComponent<BioGrowthSystem>();
            }
        }

        const string InfestedDir = "Assets/Map/Stage2_Ship/Interior/Materials/Infested";

        /// <summary>
        /// 오염된 층용: 차가운 재질(_Cold)을 때 낀 복사본(_Infested)으로 바꾼다. ApplyColdMaterials 다음에 실행.
        /// 색을 탁하게 하고 얼룩 무늬(Dirt_B)를 덧입힌다. grime 0~1 = 얼룩 세기.
        /// </summary>
        public static string ApplyInfestedMaterials(int floor, float grime)
        {
            var sc = EditorSceneManager.GetActiveScene();
            if (sc.path != FloorSceneBuilder.MasterPath) return "원본 씬(Stage2_Ship)을 먼저 열어야 함: " + sc.path;
            if (!AssetDatabase.IsValidFolder(InfestedDir)) AssetDatabase.CreateFolder("Assets/Map/Stage2_Ship/Interior/Materials", "Infested");
            Transform fl = null;
            foreach (var r in sc.GetRootGameObjects()) if (r.name == "VOID_LAST_Ship") fl = r.transform.Find(FloorRoots[floor]);
            if (fl == null) return "층 루트 없음";
            var dirt = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Creepy_Cat/3D Scifi Kit Vol 4/Textures/Dirt_B.png");
            var dirtN = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Creepy_Cat/3D Scifi Kit Vol 4/Textures/Dirt_B_Norm.png");
            var map = new Dictionary<Material, Material>();
            int swapped = 0;
            foreach (var rend in fl.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.name.StartsWith("Wall_Atlas_")) continue;
                    bool cold = m.name.EndsWith("_Cold"), inf = m.name.EndsWith("_F" + floor + "Infested");
                    if (!cold && !inf) continue;
                    Material dst;
                    if (!map.TryGetValue(m, out dst))
                    {
                        string baseName = cold ? m.name.Substring(0, m.name.Length - 5) : m.name.Substring(0, m.name.Length - ("_F" + floor + "Infested").Length);
                        var src = AssetDatabase.LoadAssetAtPath<Material>(ColdDir + "/" + baseName + "_Cold.mat");
                        if (src == null) continue;
                        string p = InfestedDir + "/" + baseName + "_F" + floor + "Infested.mat";
                        dst = AssetDatabase.LoadAssetAtPath<Material>(p);
                        if (dst == null) { dst = new Material(src) { name = baseName + "_F" + floor + "Infested" }; AssetDatabase.CreateAsset(dst, p); }
                        else dst.CopyPropertiesFromMaterial(src);
                        Color c = src.GetColor("_BaseColor");
                        c = Color.Lerp(c, new Color(c.r * 0.78f, c.g * 0.74f, c.b * 0.62f), grime);   // 누렇고 탁하게
                        dst.SetColor("_BaseColor", c);
                        if (dst.HasProperty("_Color")) dst.SetColor("_Color", c);
                        if (dirt != null)
                        {
                            dst.SetTexture("_DetailAlbedoMap", dirt);
                            dst.SetTextureScale("_DetailAlbedoMap", new Vector2(3, 3));
                            dst.SetFloat("_DetailAlbedoMapScale", grime);
                            if (dirtN != null) { dst.SetTexture("_DetailNormalMap", dirtN); dst.SetFloat("_DetailNormalMapScale", grime); }
                            dst.EnableKeyword("_DETAIL_MULX2");
                        }
                        dst.SetFloat("_Smoothness", Mathf.Lerp(dst.GetFloat("_Smoothness"), 0.75f, grime * 0.5f));   // 젖은 듯 번들거리게
                        EditorUtility.SetDirty(dst);
                        map[m] = dst;
                    }
                    if (mats[i] != dst) { mats[i] = dst; changed = true; }
                }
                if (changed) { rend.sharedMaterials = mats; swapped++; }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(sc);
            return "F" + floor + " 오염 재질: 렌더러 " + swapped + "개, 재질 " + map.Count + "종";
        }

        public static string Apply(int floor, bool bakeNav)
        {
            var sc = EditorSceneManager.GetActiveScene();
            if (sc.path != FloorSceneBuilder.MasterPath) return "원본 씬(Stage2_Ship)을 먼저 열어야 함: " + sc.path;
            Transform fl = null;
            foreach (var r in sc.GetRootGameObjects()) if (r.name == "VOID_LAST_Ship") fl = r.transform.Find(FloorRoots[floor]);
            if (fl == null) return "층 루트 없음";
            string file = "Assets/Map/Stage2_Ship/Interior/Layout_F" + floor + ".txt";
            if (!File.Exists(file)) return "배치표 없음: " + file;

            var dict = new Dictionary<string, string>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", PrefabDirs))
            {
                string p = AssetDatabase.GUIDToAssetPath(g), name = Path.GetFileNameWithoutExtension(p);
                if (!dict.ContainsKey(name)) dict[name] = p;
            }

            var old = fl.Find("Interior");
            if (old) Object.DestroyImmediate(old.gameObject);
            var interior = new GameObject("Interior").transform;
            interior.SetParent(fl, false);

            float floorY = (floor - 1) * 8f;
            // 임무 장치·줍는 물건 자리에는 얼룩을 뿌리지 않는다 (알아보기 어려워지므로)
            var keepClear = new List<Vector3>();
            foreach (var r in sc.GetRootGameObjects())
            {
                var m = r.name == "VOID_LAST_Multiplayer" ? r.transform.Find("F" + floor + " Mission") : null;
                if (m != null) foreach (Transform c in m) keepClear.Add(c.position);
                var pk = r.name == "VOID_LAST_Integration_Test" ? r.transform.Find("Integration Pickups") : null;
                if (pk != null) foreach (Transform c in pk) keepClear.Add(c.position);
            }
            // 이 층의 예전 얼룩 메시 에셋을 지우고 새로 만든다
            if (!AssetDatabase.IsValidFolder(BakedDir)) AssetDatabase.CreateFolder("Assets/Map/Stage2_Ship/Interior", "Baked");
            foreach (var g in AssetDatabase.FindAssets("F" + floor + "_Splats_", new[] { BakedDir })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(g));
            int splatIndex = 0, growthIndex = 0;
            var ci = CultureInfo.InvariantCulture;
            Transform grp = interior;
            int placed = 0;
            var missing = new HashSet<string>();
            foreach (var raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                if (line[0] == '#') { grp = new GameObject(line.Substring(1)).transform; grp.SetParent(interior, false); continue; }
                var a = line.Split('|');
                // 바닥·벽에 바르는 바이오매스 면 (충돌 없음 → 길찾기에 영향 없음)
                //   @BioFloor|x0|z0|x1|z1          바닥 사각형
                //   @BioWall|x0|z0|x1|z1[|높이]    벽을 따라가는 선분 (벽면에서 살짝 띄운 좌표로 적는다)
                //   @BioSplat|x|z|반지름[|seed]                       바닥에 울퉁불퉁한 얼룩 하나
                //   @BioSpray|x0|z0|x1|z1|개수|seed[|y아래|y위]      구역 안 표면(바닥·벽·소품·컨테이너·계단)에 얼룩을 흩뿌림. y는 바닥 기준
                if (a[0] == "@BioSplat")
                {
                    var sb1 = new SplatMesh();
                    var rng1 = new System.Random(a.Length > 4 ? int.Parse(a[4]) : placed);
                    sb1.Add(new Vector3(float.Parse(a[1], ci), floorY, float.Parse(a[2], ci)), Vector3.up, float.Parse(a[3], ci), rng1);
                    sb1.Build(grp, "BioSplat");
                    placed++;
                    continue;
                }
                if (a[0] == "@BioSpray")
                {
                    Physics.SyncTransforms();   // 앞에서 놓은 물체에도 묻도록
                    float ylo = a.Length > 7 ? float.Parse(a[7], ci) : 0f, yhi = a.Length > 8 ? float.Parse(a[8], ci) : RoomHeight - 0.1f;
                    placed += Spray(BakedDir + "/F" + floor + "_Splats_" + (splatIndex++) + ".asset", grp, fl, dict, keepClear, floorY,
                        float.Parse(a[1], ci), float.Parse(a[2], ci), float.Parse(a[3], ci), float.Parse(a[4], ci),
                        int.Parse(a[5]), int.Parse(a[6]), ylo, yhi);
                    continue;
                }
                if (a[0] == "@BioFloor" || a[0] == "@BioWall")
                {
                    float px0 = float.Parse(a[1], ci), pz0 = float.Parse(a[2], ci), px1 = float.Parse(a[3], ci), pz1 = float.Parse(a[4], ci);
                    if (a[0] == "@BioFloor")
                    {
                        float y = floorY + 0.04f;
                        Patch(grp, "BioFloor", new Vector3(px0, y, pz0), new Vector3(px0, y, pz1), new Vector3(px1, y, pz1), new Vector3(px1, y, pz0));
                    }
                    else
                    {
                        float h = a.Length > 5 ? float.Parse(a[5], ci) : RoomHeight - 0.1f;
                        Patch(grp, "BioWall", new Vector3(px0, floorY, pz0), new Vector3(px0, floorY + h, pz0), new Vector3(px1, floorY + h, pz1), new Vector3(px1, floorY, pz1));
                    }
                    placed++;
                    continue;
                }
                if (!dict.ContainsKey(a[0])) { missing.Add(a[0]); continue; }
                float x = float.Parse(a[1], ci), z = float.Parse(a[2], ci), ry = float.Parse(a[3], ci);
                bool top = a.Length > 4 && a[4] == "^";
                float yoff = a.Length > 4 && !top && a[4] != "" ? float.Parse(a[4], ci) : 0f;
                float scale = a.Length > 5 && a[5] != "" ? float.Parse(a[5], ci) : 1f;
                float tilt = a.Length > 6 ? float.Parse(a[6], ci) : 0f;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(dict[a[0]]));
                go.transform.SetParent(grp, true);
                go.transform.rotation = Quaternion.Euler(0, ry, 0) * Quaternion.Euler(0, 0, tilt);
                go.transform.localScale *= scale;
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float dy = top ? floorY + RoomHeight - 0.05f - b.max.y : floorY + yoff - b.min.y;
                Vector3 shift = new Vector3(x - b.center.x, dy, z - b.center.z);
                go.transform.position += shift;
                placed++;
                b.center += shift;
                AttachGrowth(go, grp.name, b, floor * 10000 + (growthIndex++));
            }
            Physics.SyncTransforms();
            EnsureGrowthSystem(sc);

            string nav = "";
            if (bakeNav)
            {
                // ShipMapBuilder 와 같은 방식으로 다시 굽는다
                var surf = fl.GetComponent<NavMeshSurface>();
                surf.BuildNavMesh();
                string p = "Assets/Map/Stage2_Ship/NavMesh_" + fl.name + ".asset";
                AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(surf.navMeshData, p);
                nav = ", navmesh 다시 구움";
            }
            EditorSceneManager.MarkSceneDirty(sc);
            return "F" + floor + " 배치 " + placed + "개" + nav + (missing.Count > 0 ? ", 없는 프리팹: " + string.Join(",", missing) : "");
        }
    }
}
#endif
