// 층 씬 계단실 천장 막기 (보기용)
// 층 씬에는 그 층과 바로 윗층 계단실까지만 남아 있어서, 계단실 맨 위가 뚫려 우주(바깥)가 보인다.
// 각 층 씬의 A·B 계단실마다, 남아 있는 계단실 조각의 가장 높은 곳 바로 위에 얇은 판을 하나씩 덮는다.
// 빛·그림자·충돌 없음 (계단실 벽과 같은 재질). 위치는 씬에 있는 계단실 조각 크기로 계산하므로 층을 다시 나눠도 맞는다.
// 메뉴: Tools/VOID LAST/Multiplayer/7e Cap Stairwell Ceiling (Visual). 메뉴 9로 층을 다시 나눌 때도 자동 실행된다.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoidLast.MapTools
{
    public static class StairCeilingSetup
    {
        const string RootName = "Stairwell Ceiling (Visual)";
        const float UMin = 40.2f, UMax = 58.6f, ZHalf = 8.6f;   // 계단실 바닥 넓이: |x| 40.2~58.6, |z| 8.6 안 (계단실 조각 크기로 잰 값)
        const float Thickness = 0.3f;

        [MenuItem("Tools/VOID LAST/Multiplayer/7e Cap Stairwell Ceiling (Visual)")]
        public static void CapAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[StairCeiling] play 중에는 실행할 수 없음"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new List<string>();
            for (int f = 5; f >= 1; f--)
            {
                var scene = EditorSceneManager.OpenScene(FloorSceneBuilder.ScenePath(f), OpenSceneMode.Single);
                log.Add(scene.name + ": " + BuildInOpenScene(f));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log("[StairCeiling] " + string.Join(" | ", log));
        }

        /// <summary>열린 층 씬의 A·B 계단실 맨 위에 천장 판을 (다시) 놓는다</summary>
        public static string BuildInOpenScene(int floor)
        {
            var ship = GameObject.Find("VOID_LAST_Ship");
            if (ship == null) return "stair ceiling: VOID_LAST_Ship 없음";
            var old = ship.transform.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var root = new GameObject(RootName).transform;
            root.SetParent(ship.transform, false);
            var log = new List<string>();
            foreach (int side in new[] { -1, 1 })
            {
                bool any = false;
                var b = new Bounds();
                Material material = null;
                float bestArea = 0f;
                foreach (var r in ship.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.transform.IsChildOf(root)) continue;
                    // 계단실 안에 통째로 들어 있는 조각만 (방·복도 바닥처럼 걸쳐 있는 큰 메시는 빼고 높이를 잰다)
                    var rb = r.bounds;
                    float u0 = Mathf.Min(rb.min.x * side, rb.max.x * side), u1 = Mathf.Max(rb.min.x * side, rb.max.x * side);
                    if (u0 < UMin - 0.6f || u1 > UMax + 0.6f || rb.min.z < -ZHalf - 0.6f || rb.max.z > ZHalf + 0.6f) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    // 재질: 계단실에서 가장 넓은 면(벽)의 재질
                    float area = r.bounds.size.y * Mathf.Max(r.bounds.size.x, r.bounds.size.z);
                    if (area > bestArea && r.sharedMaterial != null) { bestArea = area; material = r.sharedMaterial; }
                }
                if (!any) { log.Add((side < 0 ? "A" : "B") + " 계단실 없음"); continue; }
                var plain = PlainMaterial();
                if (plain != null) material = plain;   // 모든 층·계단이 같은 회색 천장으로 보이게 (그림 모음 재질은 늘어나 보여서 안 씀)

                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "Ceiling " + (side < 0 ? "A" : "B");
                Object.DestroyImmediate(cap.GetComponent<Collider>());
                cap.transform.SetParent(root, false);
                // 계단실 넓이 전체, 가장 높은 곳 바로 위
                cap.transform.position = new Vector3(side * (UMin + UMax) * 0.5f, b.max.y + Thickness * 0.5f, 0f);
                cap.transform.localScale = new Vector3(UMax - UMin, Thickness, ZHalf * 2f);
                var mr = cap.GetComponent<MeshRenderer>();
                if (material != null) mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(cap, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                log.Add((side < 0 ? "A" : "B") + " 높이 " + b.max.y.ToString("0.0"));
            }
            return "stair ceiling: " + string.Join(", ", log);
        }

        /// <summary>무늬 없는 회색 재질 (계단실 테두리용) (없으면 null → 계단실에서 가장 넓은 면의 재질)</summary>
        static Material PlainMaterial()
        {
            foreach (var guid in AssetDatabase.FindAssets("M_Stage2_TrimGrey t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == "M_Stage2_TrimGrey") return m;
            }
            return null;
        }
    }
}
#endif
