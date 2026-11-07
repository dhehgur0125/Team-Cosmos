// 모든 층의 계단(A·B) 입구 격벽 문을 F로 열고 닫을 수 있게 설정
// 메뉴: Tools/VOID LAST/Multiplayer/7 Build Stair Doors
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class StairDoorSetup
    {
        const string ScenePath = "Assets/Map/Scenes/Stage2_Ship.unity";

        [MenuItem("Tools/VOID LAST/Multiplayer/7 Build Stair Doors")]
        public static string Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string s = BuildInOpenScene();
            SaveWithNetworkHashes(scene);
            Debug.Log("[StairDoorSetup] " + s);
            return s;
        }

        /// <summary>지금 열려 있는 씬에 남아 있는 계단 문들로 Stair Doors를 (다시) 만든다. 저장은 하지 않음</summary>
        public static string BuildInOpenScene()
        {
            var multi = GameObject.Find(MultiplayerSetup.MultiRootName);
            if (multi == null) throw new InvalidOperationException("먼저 Multiplayer 설정(5 Patch Stage2 Scene)을 실행하세요");
            var old = multi.transform.Find("Stair Doors");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var ship = GameObject.Find("VOID_LAST_Ship");
            if (ship == null) throw new InvalidOperationException("VOID_LAST_Ship 없음");
            var roots = ship.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("F") && t.name.Contains("_Bulkhead_Stair"))
                .OrderBy(t => t.name)
                .ToList();

            var go = new GameObject("Stair Doors");
            go.transform.SetParent(multi.transform, false);
            go.AddComponent<NetworkObject>();
            var ctrl = go.AddComponent<StairDoorController>();
            ctrl.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);

            var doors = new List<StairDoorController.Door>();
            var log = new List<string>();
            foreach (var root in roots)
            {
                var blocker = root.GetComponent<BoxCollider>();
                // 문짝 = 렌더러를 가진 큰 문 (데칼은 문짝 자식이라 함께 움직임)
                var leaves = root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != root && t.GetComponent<Renderer>() != null && t.name.Contains("BigDoor"))
                    .ToArray();
                if (leaves.Length == 0)
                    leaves = root.GetComponentsInChildren<Renderer>(true).Select(r => r.transform).Where(t => t != root).ToArray();
                // 움직이는 물체는 정적 배칭에서 빼야 화면에서도 움직임
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

                var old2 = root.Find("Interact Trigger");
                if (old2 != null) Object.DestroyImmediate(old2.gameObject);
                var trig = new GameObject("Interact Trigger");
                trig.layer = 2;   // Ignore Raycast: 총알·다른 상호작용에는 걸리지 않음
                trig.transform.SetParent(root, false);
                var tc = trig.AddComponent<BoxCollider>();
                tc.isTrigger = true;
                if (blocker != null) { tc.center = blocker.center; tc.size = new Vector3(Mathf.Max(blocker.size.x, 1.2f), blocker.size.y, blocker.size.z + 0.2f); }
                else { tc.center = new Vector3(0, 1.75f, 0); tc.size = new Vector3(1.2f, 3.5f, 4.2f); }

                doors.Add(new StairDoorController.Door
                {
                    id = root.name,
                    root = root,
                    leaves = leaves,
                    blocker = blocker,
                    interactTrigger = tc,
                    // 위로 4m 올려 천장(복도 쪽 벽 두께) 속으로 숨김. 계단 쪽으로 0.2m 밀어 벽 안에 쏙 들어가게
                    openOffset = new Vector3(-Mathf.Sign(root.position.x) * 0.2f, 4f, 0f),
                });
                log.Add(root.name + "(" + leaves.Length + ")");
            }
            ctrl.doors = doors.ToArray();
            return "Stair doors: " + doors.Count + " → " + string.Join(", ", log);
        }

        /// <summary>저장 → 씬에 놓인 NetworkObject 해시 다시 계산 → 다시 저장</summary>
        public static void SaveWithNetworkHashes(UnityEngine.SceneManagement.Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // 씬에 놓인 NetworkObject 해시는 저장 후 확정 → 다시 계산하고 저장
            var validate = typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (no.gameObject.scene != scene) continue;
                validate?.Invoke(no, null);
                EditorUtility.SetDirty(no);
                if (PrefabUtility.IsPartOfPrefabInstance(no)) PrefabUtility.RecordPrefabInstancePropertyModifications(no);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
#endif
