// 계단실 비상등을 벽에 붙인다. 계단실은 위아래로 뚫려 있어 천장 높이(바닥+3.3m)에 두면 공중에 뜬다.
// 메뉴: Tools/VOID LAST/Multiplayer/7c Fix Stair Lamps (원본 씬과 층 씬 전부. 다시 돌려도 안전)
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class StairLampSetup
    {
        // 벽면 위치(월드 x의 절댓값). 층·계단 A/B 모두 같다
        const float DoorWall = 41.84f, FarWall = 58f;
        // 램프는 벽에 닿게, 빛은 벽에 얼룩이 지지 않도록 조금 떼어 놓는다
        const float LampGap = 0.09f, LightGap = 0.5f;
        const float LampHeight = 0.25f;

        /// <summary>문 쪽 계단참의 비상등: 문이 있는 벽으로</summary>
        public static void MountOnDoorWall(GameObject light) => Mount(light, DoorWall, 1f);

        /// <summary>중간참의 비상등: 계단실 맨 안쪽 벽으로</summary>
        public static void MountOnFarWall(GameObject light) => Mount(light, FarWall, -1f);

        /// <param name="inward">벽에서 계단실 안쪽으로 갈 때 |x|가 커지면 +1, 작아지면 -1</param>
        static void Mount(GameObject light, float wall, float inward)
        {
            var root = light.transform;
            var p = root.position;
            float side = Mathf.Sign(p.x);
            root.SetPositionAndRotation(new Vector3(side * (wall + inward * LightGap), p.y, p.z), Quaternion.Euler(0, 90f, 0));
            var lamp = light.GetComponentInChildren<MeshRenderer>(true);
            if (lamp != null)
            {
                lamp.transform.position = new Vector3(side * (wall + inward * LampGap), p.y + LampHeight, p.z);
                EditorUtility.SetDirty(lamp.transform);
                if (PrefabUtility.IsPartOfPrefabInstance(lamp)) PrefabUtility.RecordPrefabInstancePropertyModifications(lamp.transform);
            }
            EditorUtility.SetDirty(root);
            if (PrefabUtility.IsPartOfPrefabInstance(light)) PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        }

        [MenuItem("Tools/VOID LAST/Multiplayer/7c Fix Stair Lamps")]
        public static string FixAll()
        {
            var paths = new List<string> { "Assets/Map/Scenes/Stage2_Ship.unity" };
            for (int f = 1; f <= 5; f++) paths.Add(FloorSceneBuilder.ScenePath(f));
            var log = new List<string>();
            foreach (var path in paths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int door = 0, far = 0;
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!light.name.Contains("EmergencyLight")) continue;
                    var p = light.transform.position;
                    float x = Mathf.Abs(p.x);
                    if (Mathf.Abs(x - 44f) < 0.01f && Mathf.Abs(p.z - 5.5f) < 0.01f) { MountOnDoorWall(light.gameObject); door++; }
                    else if (Mathf.Abs(x - 55f) < 0.01f && Mathf.Abs(p.z) < 0.01f) { MountOnFarWall(light.gameObject); far++; }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                log.Add(scene.name + ": 계단참 " + door + "개, 중간참 " + far + "개");
            }
            string s = string.Join("\n", log);
            Debug.Log("[StairLampSetup] " + s);
            return s;
        }
    }
}
#endif
