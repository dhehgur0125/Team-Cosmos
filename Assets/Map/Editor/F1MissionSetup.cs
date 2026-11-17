// 1층(보조 우주선 격납고) 임무 장치·격납고 문·임무 물건·몬스터 배치
// 메뉴: Tools/VOID LAST/Multiplayer/6d Build F1 Mission (1층 씬에만 적용. 다시 실행하면 지우고 다시 만든다)
// 층 씬을 메뉴 9로 다시 나눌 때도 1층에 함께 실행된다 (FloorSceneBuilder).
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
    public static class F1MissionSetup
    {
        const float Floor = 0f;
        const string RootName = "F1 Mission";

        [MenuItem("Tools/VOID LAST/Multiplayer/6d Build F1 Mission")]
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "play 중에는 실행할 수 없음";
            var scene = EditorSceneManager.OpenScene(FloorSceneBuilder.ScenePath(1), OpenSceneMode.Single);
            string s = scene.name + ": " + BuildInOpenScene();
            StairDoorSetup.SaveWithNetworkHashes(scene);
            Debug.Log("[F1MissionSetup] " + s);
            return s;
        }

        /// <summary>지금 열려 있는 1층 씬에 임무를 (다시) 만든다. 저장은 하지 않음</summary>
        public static string BuildInOpenScene()
        {
            F3MissionSetup.UseFloor(1, "F1_Hangar");
            var multi = GameObject.Find(MultiplayerSetup.MultiRootName);
            if (multi == null) throw new InvalidOperationException("먼저 Multiplayer 설정을 실행하세요");
            if (F2MissionItemAssets.Load(FarmingItemKind.ToolKit) == null) F2MissionItemAssets.BuildToolKit();
            var old = multi.transform.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            root.SetParent(multi.transform, false);
            var lamp = F2MissionSetup.LampMaterial();
            var log = new List<string>();
            Physics.SyncTransforms();

            // 1) 연료 접속실 (x 2~22, z 21~37): 2층 리프트가 닿는 자리의 전력 모듈 + 연료 라인 밸브
            F3MissionSetup.Item(root, FarmingItemKind.PowerModule, "Power Module", new Vector3(9f, 0, 26f), false);
            var fuel = F3MissionSetup.Wrap(root, "Fuel Line Valve", "P_Pipe_Valve_01", 5.9f, 24.9f, new Vector3(12f, Floor, 29f), lamp,
                MissionInteractable.Kind.FuelLine, "연료 라인 연결", 4f, 0);

            // 2) 정비 작업실 (x -29~-9, z -59~-43): 작업대 위 공구
            var tool = F3MissionSetup.Item(root, FarmingItemKind.ToolKit, "Tool Kit", new Vector3(-18.8f, 0, -45.1f), true);
            log.Add("tool y=" + tool.transform.position.y.ToString("F2"));

            // 3) 보조 우주선 방 (x 31~51, z 18~42): 전력 모듈 설치 자리(새로 놓음) + 점화 콘솔(방에 있던 콘솔)
            var socket = F3MissionSetup.Place(root, "Power Module Socket", "P_Electric_Battery_01", new Vector3(41f, Floor, 34.8f), 0f, new Vector3(37f, Floor, 30f), lamp,
                MissionInteractable.Kind.ModuleSocket, "전력 모듈 설치", 4f);
            var ignition = F3MissionSetup.Wrap(root, "Ignition Console", "P_Technic_Console_01_D", 44.3f, 41.7f, new Vector3(41f, Floor, 34f), lamp,
                MissionInteractable.Kind.IgnitionConsole, "항로 데이터를 불러와 점화", 3f, 0);

            // 4) 고정 장치 제어반 2개: 화물 적재실 (x -53~-37, z 18~38), 지상 정비 구역 (x -28~-8, z 21~37)
            var clamps = new[]
            {
                F3MissionSetup.Place(root, "Hold Clamp Console SW", "P_Technic_Console_01", new Vector3(-45f, Floor, 31f), 0f, new Vector3(-45f, Floor, 26f), lamp,
                    MissionInteractable.Kind.HoldClamp, "고정 장치 풀기", 2f),
                F3MissionSetup.Place(root, "Hold Clamp Console S", "P_Technic_Console_01", new Vector3(-18f, Floor, 32f), 0f, new Vector3(-18f, Floor, 27f), lamp,
                    MissionInteractable.Kind.HoldClamp, "고정 장치 풀기", 2f),
            };
            clamps[1].index = 1;

            // 5) 발사 관제실 (x 30~46, z -38~-18): 격납고 문 콘솔(방에 있던 콘솔) + 북쪽 격납 베이 통로를 막는 격납고 문
            var hangar = F3MissionSetup.Wrap(root, "Hangar Door Console", "P_Technic_Console_01_D", 32.7f, -37.7f, new Vector3(38f, Floor, -28f), lamp,
                MissionInteractable.Kind.HangarConsole, "격납고 문 열기", 3f, 0);
            var hangarDoor = F3MissionSetup.Door(root, "Hangar Door", new Vector3(38f, Floor, -38f), true, MissionInteractable.Kind.HangarDoor, 0);

            // 6) 진행자 (모든 대원 공통 진행 상태)
            var dirGo = new GameObject("F1 Mission Director");
            dirGo.transform.SetParent(root, false);
            dirGo.AddComponent<NetworkObject>();
            var dir = dirGo.AddComponent<F1MissionDirector>();
            dir.moduleSocket = socket; dir.fuelLine = fuel; dir.clamps = clamps; dir.hangarConsole = hangar; dir.ignitionConsole = ignition;
            dir.hangarDoors = new[] { hangarDoor };
            // 탈출선 탑승 구역: 탑승 발판 끝(x 6.9, y -23.3) ~ 선내 바닥(y -23.9). 격납 베이 표시(F1_Escape_ShipDoor, F1_Escape_Shuttle_Board) 기준
            dir.boardZone = new Bounds(new Vector3(3.5f, -21.5f, -141.5f), new Vector3(12f, 6f, 9f));
            dir.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);

            // 7) 몬스터
            log.Add(PlaceMonsters(root));

            foreach (var d in new[] { fuel, socket, ignition, clamps[0], clamps[1], hangar }) log.Add(d.name + " @" + d.transform.position.ToString("F1"));
            return "F1 mission: " + string.Join(" | ", log);
        }

        /// <summary>
        /// 처음부터: 눈 있는 대형 1(중앙 격납고 순찰) + 쪼꼬미 1(상부 정비실) + 개구리 1(북서 복도 천장).
        /// 격납고 문 뒤 베이 통로: 촉수형 1 + 눈 없는 대형 1 → 문이 열리면(경보) 풀려나고, 점화 소음을 듣고 몰려온다.
        /// </summary>
        static string PlaceMonsters(Transform root)
        {
            var monsters = new GameObject("Monsters").transform;
            monsters.SetParent(root, false);
            var log = new List<string>();

            // ---- 눈 있는 대형: 맵에 표시된 순찰 지점 (중앙 격납고 주변)
            var marks = new List<Vector2>();
            var marker = GameObject.Find("VOID_LAST_Ship/F1_Hangar/Markers/F1_Monster_LargeVisual");
            if (marker != null) foreach (Transform c in marker.transform) marks.Add(new Vector2(c.position.x, c.position.z));
            if (marks.Count == 0) marks.Add(new Vector2(2, -1));
            var patrol = F3MissionSetup.Points(monsters, "Eyed Patrol", marks.ToArray());
            var grid = new List<Vector2>();
            for (int x = -66; x <= 72; x += 6) for (int z = -66; z <= 44; z += 6) grid.Add(new Vector2(x, z));
            var search = F3MissionSetup.Points(monsters, "Eyed Search", grid.ToArray());
            if (patrol.Count > 0)
            {
                var eyed = F3MissionSetup.Spawn(monsters, "VOID_LAST_EyedLargeMonster", patrol[0].position, 180f);
                F3MissionSetup.SetArray(eyed.GetComponent<EyedMonsterAI>(), "patrolPoints", patrol);
                F3MissionSetup.SetArray(eyed.GetComponent<EyedMonsterAI>(), "searchPoints", search);
                log.Add("eyed patrol=" + patrol.Count + " search=" + search.Count);
            }
            else log.Add("eyed: NavMesh 위치를 찾지 못함");

            // ---- 쪼꼬미: 상부 정비실에서 시작 (시작 지점인 격납고 입구는 피한다)
            var smallPoints = F3MissionSetup.Points(monsters, "Small Search", new[] { new Vector2(8, -35), new Vector2(-19, -51), new Vector2(-22, -15), new Vector2(-18, 26) });
            if (smallPoints.Count > 0)
            {
                var small = F3MissionSetup.Spawn(monsters, "VOID_LAST_SmallMonster", smallPoints[0].position, 0f);
                F3MissionSetup.SetArray(small.GetComponent<SmallMonsterAI>(), "searchPoints", smallPoints);
                log.Add("small search=" + smallPoints.Count);
            }
            else log.Add("small: NavMesh 위치를 찾지 못함");

            // ---- 개구리: 정비 작업실로 가는 북서 복도 천장 (맵 표시 F1_Monster_CeilingAmbush_W-NW)
            log.Add(F3MissionSetup.Frog(monsters, "Frog 1", new[] { new Vector2(-30, -63), new Vector2(-41, -48), new Vector2(-19, -63) }));

            // ---- 격납고 문 뒤 (발사 관제실 북쪽 베이 통로 x 35.5~40.5, z -83~-38)
            var wander = F3MissionSetup.Points(monsters, "Blind Wander", new[] { new Vector2(38, -46), new Vector2(38, -54), new Vector2(38, -62) });
            if (wander.Count > 0)
            {
                var blind = F3MissionSetup.Spawn(monsters, "VOID_LAST_BlindMonster", wander[0].position, 180f);
                F3MissionSetup.SetArray(blind.GetComponent<BlindMonsterAI>(), "wanderPoints", wander);
                log.Add("blind wander=" + wander.Count);
            }
            else log.Add("blind: NavMesh 위치를 찾지 못함");
            var largePatrol = F3MissionSetup.Points(monsters, "Blind Large Patrol", new[] { new Vector2(38, -72), new Vector2(38, -64), new Vector2(38, -56), new Vector2(38, -80) });
            if (largePatrol.Count > 0)
            {
                var large = F3MissionSetup.Spawn(monsters, "VOID_LAST_BlindLargeMonster", largePatrol[0].position, 180f);
                F3MissionSetup.SetArray(large.GetComponent<BlindLargeMonsterAI>(), "patrolPoints", largePatrol);
                log.Add("blind large patrol=" + largePatrol.Count);
            }
            else log.Add("blind large: NavMesh 위치를 찾지 못함");
            return "monsters: " + string.Join(", ", log);
        }
    }
}
#endif
