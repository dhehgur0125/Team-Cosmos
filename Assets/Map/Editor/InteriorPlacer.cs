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
                go.transform.position += new Vector3(x - b.center.x, dy, z - b.center.z);
                placed++;
            }
            Physics.SyncTransforms();

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
