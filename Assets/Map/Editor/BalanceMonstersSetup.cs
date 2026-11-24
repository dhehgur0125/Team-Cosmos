// 밸런스 패치: 층별 몬스터 수 조정 (기획서 "VOID LAST 밸런스 패치 제안", claude.ai/artifact/P63MB7ZfYKxn6pog1m9FBB)
//  - 5층: 시작 층. 몬스터가 들어왔다는 걸 알려 주는 쪼꼬미 1마리 (시작 방인 승무원 침실에서 먼 동쪽 방들을 돌아다님)
//  - 2층: 쪼꼬미 2 → 1 (시작 방 근처 시험용 쪼꼬미는 3인 이상일 때만), 4인이면 촉수형 1 추가
//  - 1층: 3인 이상이면 쪼꼬미 1 추가, 4인이면 촉수형 1 추가
// 추가 몬스터는 PlayerCountGate가 붙어 시작 인원이 모자라면 층에 들어올 때 빠진다.
// 메뉴: Tools/VOID LAST/Monster/4 Place Balance Monsters (5·2·1층 씬). 메뉴 9로 층을 다시 나눌 때도 자동 실행된다.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoidLast.Monster;

namespace VoidLast.MapTools
{
    public static class BalanceMonstersSetup
    {
        const string RootName = "Balance Monsters";

        [MenuItem("Tools/VOID LAST/Monster/4 Place Balance Monsters")]
        public static void PlaceAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[BalanceMonsters] play 중에는 실행할 수 없음"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = new List<string>();
            foreach (int floor in new[] { 5, 2, 1 })
            {
                var scene = EditorSceneManager.OpenScene(FloorSceneBuilder.ScenePath(floor), OpenSceneMode.Single);
                log.Add(scene.name + ": " + BuildInOpenScene(floor));
                StairDoorSetup.SaveWithNetworkHashes(scene);
            }
            Debug.Log("[BalanceMonsters] " + string.Join(" | ", log));
        }

        /// <summary>열린 층 씬에 인원별 몬스터를 (다시) 놓는다. 5·2·1층 외에는 아무것도 하지 않는다</summary>
        public static string BuildInOpenScene(int floor)
        {
            if (floor != 5 && floor != 2 && floor != 1) return "balance monsters: 없음";
            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(RootName).transform;
            F3MissionSetup.UseFloor(floor, "");
            var log = new List<string>();

            if (floor == 5)
            {
                log.Add(Small(root, "5F Small", 1, new[] { new Vector2(32, 28), new Vector2(4, 29), new Vector2(23, -20), new Vector2(7, -49) }));
            }
            else if (floor == 2)
            {
                // 시작 방 근처 시험용 쪼꼬미: 3인 이상일 때만
                var near = GameObject.Find("VOID_LAST_Integration_Test/TEST_SmallMonster_NearStart");
                if (near != null) { Gate(near, 3); log.Add("시험용 쪼꼬미 → 3인 이상"); }
                else log.Add("시험용 쪼꼬미 없음");
                log.Add(Tentacle(root, "2F Tentacle (4P)", 4, new[] { new Vector2(3, 18), new Vector2(11, -11), new Vector2(-4, -42) }));
            }
            else
            {
                log.Add(Small(root, "1F Small (3P)", 3, new[] { new Vector2(-45, 28), new Vector2(-18, 29), new Vector2(12, 29) }));
                log.Add(Tentacle(root, "1F Tentacle (4P)", 4, new[] { new Vector2(2, -1), new Vector2(12, 29), new Vector2(-18, 29) }));
            }
            F3MissionSetup.UseFloor(3, "F3_Control");   // 다른 메뉴가 기본값으로 쓰도록 되돌림
            return "balance monsters: " + string.Join(", ", log);
        }

        static string Small(Transform root, string name, int minPlayers, Vector2[] spots)
        {
            var points = F3MissionSetup.Points(root, name + " Search", spots);
            if (points.Count == 0) return name + ": NavMesh 위치를 찾지 못함";
            var small = F3MissionSetup.Spawn(root, "VOID_LAST_SmallMonster", points[0].position, 0f);
            small.name = "VOID_LAST_SmallMonster (" + name + ")";
            F3MissionSetup.SetArray(small.GetComponent<SmallMonsterAI>(), "searchPoints", points);
            if (minPlayers > 1) Gate(small, minPlayers);
            return name + " search=" + points.Count;
        }

        static string Tentacle(Transform root, string name, int minPlayers, Vector2[] spots)
        {
            var points = F3MissionSetup.Points(root, name + " Wander", spots);
            if (points.Count == 0) return name + ": NavMesh 위치를 찾지 못함";
            var blind = F3MissionSetup.Spawn(root, "VOID_LAST_BlindMonster", points[0].position, 180f);
            blind.name = "VOID_LAST_BlindMonster (" + name + ")";
            F3MissionSetup.SetArray(blind.GetComponent<BlindMonsterAI>(), "wanderPoints", points);
            if (minPlayers > 1) Gate(blind, minPlayers);
            return name + " wander=" + points.Count;
        }

        static void Gate(GameObject go, int minPlayers)
        {
            var gate = go.GetComponent<PlayerCountGate>();
            if (gate == null) gate = go.AddComponent<PlayerCountGate>();
            gate.rule = PlayerCountGate.Rule.MinPlayers;
            gate.minPlayers = minPlayers;
            EditorUtility.SetDirty(gate);
        }
    }
}
#endif
