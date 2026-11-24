/* [ 코드 설명 ] 층마다 파밍 아이템(최대 탄알집 6개, 주사기 4개)을 정해진 자리에 놓는 에디터 메뉴입니다.
   인원별 보정: 아이템마다 PlayerCountGate(몇 번째 아이템인지)를 붙여, 시작 인원이 모자라면 층에 들어올 때 뺀다 (1인 3·2, 2인 4·3, 3인 5·3, 4인 6·4 — GameBalance 표).
   6번째 탄알집·4번째 주사기(4인 전용)는 기획 배치도에 없어서, 그 층 첫 주사기·첫 탄알집 자리 옆(약 0.8m)에 놓습니다.
   자리는 "VOID LAST 파밍 배치도"(claude.ai/artifact/TnoeG6sUjtQyMvCLz9BRxi)와 같은 표(Spots)이며, 높이는 놓을 때 아래로 광선을 쏴서
   그 자리의 상자·책상·바닥 윗면에 맞춥니다. 각 층 씬에 "Farming Items" 묶음을 (다시) 만들고, 그 밖에 씬에 놓여 있던 시험용 주사기는 지웁니다.
   메뉴 9(층 씬 다시 나누기)도 층마다 이것을 자동으로 실행합니다. 놓인 아이템은 NetworkPickupRegistry가 모든 대원 화면에서 맞춰 줍니다. */
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoidLast.Player;

namespace VoidLast.MapTools
{
    public static class FarmingItemsSetup
    {
        const string RootName = "Farming Items";
        const string AmmoPrefabPath = "Assets/Resources/FarmingItems/AmmoPack.prefab";
        const string SyringePrefabPath = "Assets/Player/Prefabs/Item/VOID_LAST_SyringePickup.prefab";

        // 층, 종류(A = 탄알집, H = 주사기), 방, x, z
        static readonly (int floor, char kind, string room, float x, float z)[] Spots =
        {
            (5,'H',"의무실",-50.3f,21.7f), (5,'H',"승무원 침실",-53.8f,-33.8f), (5,'H',"휴게실",11.7f,34.7f),
            (5,'A',"공동 생활실",-29.3f,-26.3f), (5,'A',"비상 통신실",11.2f,-55.8f), (5,'A',"승무원 로비",15.7f,-18.3f), (5,'A',"세탁 및 설비실",-22.3f,46.7f), (5,'A',"생활 물자 보관실",25.7f,35.2f),

            (4,'H',"정비 구역 입구",46.2f,-32.3f), (4,'H',"냉각 설비실",2.7f,26.2f), (4,'H',"전력 제어실",8.2f,-40.8f),
            (4,'A',"배전실",-30.3f,-22.3f), (4,'A',"전력 셀 보관실",26.7f,-57.3f), (4,'A',"배관 구역",14.2f,45.7f), (4,'A',"정비 작업장",51.7f,35.7f), (4,'A',"A 계단 대기실",-24.8f,20.7f),

            (3,'H',"관측실",20.2f,-45.3f), (3,'H',"보안실",-31.8f,21.7f), (3,'H',"보안 출입실",47.2f,-20.8f),
            (3,'A',"서버 관리실",-37.3f,-56.8f), (3,'A',"중앙 관제실",19.2f,-3.8f), (3,'A',"함교",-16.8f,22.7f), (3,'A',"격납고 관제실",21.2f,30.7f), (3,'A',"데이터 보관실",-9.3f,-43.8f),

            (2,'H',"검역 관제실",35.7f,-31.3f), (2,'H',"컨테이너 보관실",-26.3f,25.2f), (2,'H',"정비 보관실",3.7f,-38.8f),
            (2,'A',"장비 작업실",2.2f,-7.3f), (2,'A',"중앙 화물창",-4.8f,23.2f), (2,'A',"연료 공급실",-70.3f,32.7f), (2,'A',"검역 보관실",14.7f,39.7f), (2,'A',"A 계단 대기실",-38.3f,-35.3f),

            (1,'H',"보조 우주선",32.7f,40.2f), (1,'H',"상부 정비실",4.7f,-28.8f), (1,'H',"화물 적재실",-38.8f,33.7f),
            (1,'A',"정비 작업실",-25.3f,-45.3f), (1,'A',"발사 관제실",44.7f,-36.3f), (1,'A',"중앙 격납고",17.7f,4.2f), (1,'A',"지상 정비 구역",-9.8f,35.7f), (1,'A',"연료 접속실",20.7f,22.2f),
        };

        [MenuItem("Tools/VOID LAST/Multiplayer/6f Place Farming Items")]
        public static void PlaceAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[FarmingItemsSetup] play 중에는 실행할 수 없음"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new List<string>();
            for (int floor = 5; floor >= 1; floor--)
            {
                var scene = EditorSceneManager.OpenScene(FloorSceneBuilder.ScenePath(floor), OpenSceneMode.Single);
                log.Add(scene.name + ": " + BuildInOpenScene(floor));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log("[FarmingItemsSetup] " + string.Join(" | ", log));
        }

        /// <summary>지금 열려 있는 층 씬에 파밍 아이템을 (다시) 놓는다. 저장은 하지 않음</summary>
        public static string BuildInOpenScene(int floor)
        {
            var ammo = AssetDatabase.LoadAssetAtPath<GameObject>(AmmoPrefabPath);
            var syringe = AssetDatabase.LoadAssetAtPath<GameObject>(SyringePrefabPath);
            if (ammo == null || syringe == null) return "파밍 아이템 프리팹을 찾지 못함";
            float floorY = (floor - 1) * 8f;
            Physics.SyncTransforms();

            // 예전 묶음과, 묶음 밖에 놓여 있던 시험용 주사기 지우기
            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            int removed = 0;
            foreach (var p in Object.FindObjectsByType<FpsWorldPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (p.Item != FarmingItemKind.Syringe || PrefabUtility.IsPartOfPrefabAsset(p)) continue;
                // 주사기 프리팹 하나로 놓인 것만 지운다 (다른 프리팹 안에 들어 있는 것은 건드리지 않음)
                var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(p.gameObject);
                if (instanceRoot != null && instanceRoot != p.gameObject) continue;
                Object.DestroyImmediate(p.gameObject); removed++;
            }

            var root = new GameObject(RootName).transform;
            int placed = 0, ammoCount = 0, healCount = 0;
            var missing = new List<string>();
            var spots = Spots.Where(s => s.floor == floor).ToList();
            // 4인 전용 추가 자리: 첫 주사기 옆에 탄알집, 첫 탄알집 옆에 주사기
            var firstHeal = spots.FirstOrDefault(s => s.kind == 'H');
            var firstAmmo = spots.FirstOrDefault(s => s.kind == 'A');
            if (firstHeal.room != null) spots.Add((floor, 'A', firstHeal.room + " 추가", firstHeal.x + 0.8f, firstHeal.z + 0.4f));
            if (firstAmmo.room != null) spots.Add((floor, 'H', firstAmmo.room + " 추가", firstAmmo.x + 0.8f, firstAmmo.z + 0.4f));
            foreach (var s in spots)
            {
                if (!Physics.Raycast(new Vector3(s.x, floorY + 2.6f, s.z), Vector3.down, out var hit, 3.4f, ~0, QueryTriggerInteraction.Ignore))
                { missing.Add(s.room); continue; }
                bool isAmmo = s.kind == 'A';
                var go = (GameObject)PrefabUtility.InstantiatePrefab(isAmmo ? ammo : syringe, root);
                int n = isAmmo ? ++ammoCount : ++healCount;
                go.name = (isAmmo ? "AmmoPack " : "Syringe ") + n + " (" + s.room + ")";
                var gate = go.AddComponent<PlayerCountGate>();
                gate.rule = isAmmo ? PlayerCountGate.Rule.AmmoPack : PlayerCountGate.Rule.Syringe;
                gate.index = n;
                // 같은 자리라도 조금씩 다른 방향 (자리 좌표로 정해서 다시 실행해도 같음)
                float yaw = Mathf.Repeat(s.x * 37.1f + s.z * 53.7f, 360f);
                go.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, yaw, 0f));
                // 모델 밑면이 윗면에 닿도록 높이 맞추기
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    float bottom = renderers.Min(r => r.bounds.min.y);
                    go.transform.position += Vector3.up * (hit.point.y - bottom + 0.005f);
                }
                placed++;
            }
            string result = "탄알집 " + ammoCount + " · 주사기 " + healCount + " 배치";
            if (removed > 0) result += ", 예전 주사기 " + removed + "개 지움";
            if (missing.Count > 0) result += ", 바닥을 못 찾음: " + string.Join(", ", missing);
            return result;
        }
    }
}
