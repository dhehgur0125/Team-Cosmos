// 4층(동력 및 정비 구역) 임무 장치·임무 물건·몬스터 배치
// 메뉴: Tools/VOID LAST/Multiplayer/6e Build F4 Mission (4층 씬에만 적용. 다시 실행하면 지우고 다시 만든다)
// 층 씬을 메뉴 9로 다시 나눌 때도 4층에 함께 실행된다 (FloorSceneBuilder).
// 장치를 놓는 도우미는 F3MissionSetup의 것을 같이 쓴다.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoidLast.Monster;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class F4MissionSetup
    {
        const float Floor = 24f;
        const string RootName = "F4 Mission";
        const string MarkerPath = "VOID_LAST_Ship/F4_Power/Markers/";

        [MenuItem("Tools/VOID LAST/Multiplayer/6e Build F4 Mission")]
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "play 중에는 실행할 수 없음";
            var scene = EditorSceneManager.OpenScene(FloorSceneBuilder.ScenePath(4), OpenSceneMode.Single);
            string s = scene.name + ": " + BuildInOpenScene();
            StairDoorSetup.SaveWithNetworkHashes(scene);
            Debug.Log("[F4MissionSetup] " + s);
            return s;
        }

        /// <summary>지금 열려 있는 4층 씬에 임무를 (다시) 만든다. 저장은 하지 않음</summary>
        public static string BuildInOpenScene()
        {
            F3MissionSetup.UseFloor(4, "F4_Power");
            var multi = GameObject.Find(MultiplayerSetup.MultiRootName);
            if (multi == null) throw new InvalidOperationException("먼저 Multiplayer 설정을 실행하세요");
            if (F2MissionItemAssets.Load(FarmingItemKind.ToolKit) == null) F2MissionItemAssets.BuildToolKit();
            if (F2MissionItemAssets.Load(FarmingItemKind.PowerCell) == null) F2MissionItemAssets.BuildPowerCell();
            var old = multi.transform.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            root.SetParent(multi.transform, false);
            var lamp = F2MissionSetup.LampMaterial();
            var log = new List<string>();
            Physics.SyncTransforms();

            // 1) 정비 작업장 (x 39~55, z 18~38): 작업대 위 수리 공구
            var tool = F3MissionSetup.Item(root, FarmingItemKind.ToolKit, "Tool Kit", new Vector3(40.8f, 0, 20.9f), true);
            log.Add("tool y=" + tool.transform.position.y.ToString("F2"));

            // 2) 전력 셀 보관실 (x 25~45, z -60~-44): 전력 셀 3개 (바닥)
            foreach (var x in new[] { 33f, 35f, 37f })
                F3MissionSetup.Item(root, FarmingItemKind.PowerCell, "Power Cell " + (x - 31f) / 2f, new Vector3(x, 0, -52f), false);

            // 3) 보조 발전기실 (x -7~21, z -5~15): 발전기(전력 셀 끼우기·수리) + 가동 콘솔. 둘 다 방에 있던 소품을 쓴다
            var generator = F3MissionSetup.Wrap(root, "Aux Generator", "F4_C_AuxGenerator", 7f, 5f, new Vector3(7f, Floor, -3f), lamp,
                MissionInteractable.Kind.Generator, "전력 셀 끼우기", 2.5f, 0);
            var start = F3MissionSetup.Wrap(root, "Generator Start Console", "P_Technic_Console_01", 7.8f, 14.7f, new Vector3(7f, Floor, 5f), lamp,
                MissionInteractable.Kind.GeneratorStart, "발전기 가동", 3f, 0);

            // 4) 냉각 밸브 2개: 냉각 설비실 (x -12~4, z 25~45), 배관 구역 (x 13~29, z 28~48). 방에 있던 밸브를 쓴다
            var valves = new[]
            {
                F3MissionSetup.Wrap(root, "Coolant Valve SW", "P_Pipe_Valve_01", -4f, 25.7f, new Vector3(-4f, Floor, 35f), lamp,
                    MissionInteractable.Kind.CoolantValve, "냉각 밸브 열기", 3f, 0),
                F3MissionSetup.Wrap(root, "Coolant Valve S", "P_Pipe_Valve_01", 13.7f, 42f, new Vector3(21f, Floor, 38f), lamp,
                    MissionInteractable.Kind.CoolantValve, "냉각 밸브 열기", 3f, 1),
            };

            // 5) 전력 스위치 2개: 배전실 (x -33~-17, z -32~-12), 전력 제어실 (x -1~15, z -42~-22). 방에 있던 콘솔을 쓴다
            var switches = new[]
            {
                F3MissionSetup.Wrap(root, "Power Switch NW", "P_Technic_Console_01_D", -17.3f, -16.2f, new Vector3(-25f, Floor, -22f), lamp,
                    MissionInteractable.Kind.PowerSwitch, "전력 스위치 올리기", 2f, 0),
                F3MissionSetup.Wrap(root, "Power Switch N", "P_Technic_Console_01_D", 14.7f, -35.2f, new Vector3(7f, Floor, -32f), lamp,
                    MissionInteractable.Kind.PowerSwitch, "전력 스위치 올리기", 2f, 1),
            };

            // 6) A 계단 입구 안내등 (임무를 끝내면 켜짐). 4층은 A 계단으로만 내려간다
            var exitGo = new GameObject("Stair A Exit Light");
            exitGo.transform.SetParent(root, false);
            exitGo.transform.position = new Vector3(-41.2f, Floor + 3.3f, 0f);
            var exitLight = exitGo.AddComponent<Light>();
            exitLight.type = LightType.Point; exitLight.range = 7f; exitLight.intensity = 2.5f; exitLight.color = new Color(1f, 0.62f, 0.12f);
            exitLight.shadows = LightShadows.None; exitLight.enabled = false;
            var floorTransition = Object.FindFirstObjectByType<FloorTransition>(FindObjectsInactive.Include);
            if (floorTransition != null) { floorTransition.descentSides = "A"; EditorUtility.SetDirty(floorTransition); }

            // 7) 진행자 (모든 대원 공통 진행 상태)
            var dirGo = new GameObject("F4 Mission Director");
            dirGo.transform.SetParent(root, false);
            dirGo.AddComponent<NetworkObject>();
            var dir = dirGo.AddComponent<F4MissionDirector>();
            dir.generator = generator; dir.generatorStart = start; dir.valves = valves; dir.switches = switches;
            dir.exitLights = new[] { exitLight };
            dir.generatorRoom = new Bounds(new Vector3(7f, Floor + 2.2f, 5f), new Vector3(27f, 5f, 19f));   // x -7~21, z -5~15 (문턱 제외)
            dir.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);

            // 8) 몬스터
            log.Add(PlaceMonsters(root));

            foreach (var d in new[] { generator, start, valves[0], valves[1], switches[0], switches[1] }) log.Add(d.name + " @" + d.transform.position.ToString("F1"));
            return "F4 mission: " + string.Join(" | ", log);
        }

        /// <summary>맵에 표시된 자리: 촉수형 1(배관 구역 주변) + 쪼꼬미 1(정비 작업장 주변)</summary>
        static string PlaceMonsters(Transform root)
        {
            var monsters = new GameObject("Monsters").transform;
            monsters.SetParent(root, false);
            var log = new List<string>();

            var wander = F3MissionSetup.Points(monsters, "Blind Wander", MarkerPoints("F4_Monster_Tentacle", new Vector2(21, 38)));
            if (wander.Count > 0)
            {
                var blind = F3MissionSetup.Spawn(monsters, "VOID_LAST_BlindMonster", wander[0].position, 0f);
                F3MissionSetup.SetArray(blind.GetComponent<BlindMonsterAI>(), "wanderPoints", wander);
                log.Add("blind wander=" + wander.Count);
            }
            else log.Add("blind: NavMesh 위치를 찾지 못함");

            var search = F3MissionSetup.Points(monsters, "Small Search", MarkerPoints("F4_Monster_SmallScout", new Vector2(47, 28)));
            if (search.Count > 0)
            {
                var small = F3MissionSetup.Spawn(monsters, "VOID_LAST_SmallMonster", search[0].position, 0f);
                F3MissionSetup.SetArray(small.GetComponent<SmallMonsterAI>(), "searchPoints", search);
                log.Add("small search=" + search.Count);
            }
            else log.Add("small: NavMesh 위치를 찾지 못함");
            return "monsters: " + string.Join(", ", log);
        }

        /// <summary>맵 표시(마커)의 자식으로 놓인 순찰 지점들. 없으면 기본 자리 하나</summary>
        static Vector2[] MarkerPoints(string markerName, Vector2 fallback)
        {
            var list = new List<Vector2>();
            var marker = GameObject.Find(MarkerPath + markerName);
            if (marker != null)
                foreach (Transform c in marker.transform)
                {
                    var p = new Vector2(c.position.x, c.position.z);
                    if (!list.Contains(p)) list.Add(p);
                }
            if (list.Count == 0) list.Add(fallback);
            return list.ToArray();
        }
    }
}
#endif
