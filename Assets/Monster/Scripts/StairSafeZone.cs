/* [ 코드 설명 ] 계단실을 몬스터가 들어오지 못하는 안전한 곳으로 만듭니다 (몬스터 위협 강화 기획, 2026-11-24 결정).
   - 층 씬(Stage2_Ship_○f)에서만 켜집니다. 계단실 = 계단 문 안쪽 |x| 42.2~59, |z| 9 (A는 x −쪽, B는 x +쪽).
   - 몬스터는 계단실 안의 대원을 보거나 느끼지 못하고(목표에서 뺌), 공격해도 피해를 주지 못합니다.
     계단실 안에서 걸어도 발소리(MoveNoise)가 나지 않습니다. 총소리는 그대로라 문 앞까지는 옵니다.
   - 계단 문마다 길 찾기 막이(NavMeshObstacle, 길을 깎아 냄)를 실행 중에 놓아 몬스터가 문을 지나 걸어 들어오지 못합니다.
   씬을 고칠 필요는 없습니다 (씬이 열릴 때 코드로 만듦). */
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VoidLast.Player;

namespace VoidLast.Monster
{
    public static class StairSafeZone
    {
        public const float DoorU = 42f;          // 계단 문 위치 |x|
        const float InnerU = 42.2f, OuterU = 59f, HalfZ = 9f;

        /// <summary>지금 씬이 층 씬이라 안전 구역이 켜져 있는지</summary>
        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Active = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FirstScene() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single && scene != SceneManager.GetActiveScene()) return;
            int floor = FloorOf(scene.name);
            Active = floor > 0;
            if (Active) PlaceDoorBlockers(scene, floor);
        }

        /// <summary>"Stage2_Ship_3f" → 3, 층 씬이 아니면 0</summary>
        static int FloorOf(string sceneName)
        {
            const string prefix = "Stage2_Ship_";
            if (string.IsNullOrEmpty(sceneName) || !sceneName.StartsWith(prefix) || !sceneName.EndsWith("f")) return 0;
            return int.TryParse(sceneName.Substring(prefix.Length, sceneName.Length - prefix.Length - 1), out int f) && f >= 1 && f <= 5 ? f : 0;
        }

        /// <summary>이 위치가 계단실 안인지</summary>
        public static bool Contains(Vector3 position)
        {
            if (!Active) return false;
            float u = Mathf.Abs(position.x);
            return u > InnerU && u < OuterU && Mathf.Abs(position.z) < HalfZ;
        }

        /// <summary>이 대원이 계단실 안에 있어 몬스터가 노리거나 다치게 할 수 없는지</summary>
        public static bool Contains(FpsHealth player) => player != null && Contains(player.transform.position);

        public const float DoorClearRadius = 14f;

        /// <summary>
        /// 계단실 안이거나 계단 문 앞(문에서 DoorClearRadius m 안)인지. 대원이 층에 도착하는 자리라
        /// 몬스터의 순찰·수색 지점으로 쓰지 않는다 (몬스터가 문 앞에 서 있다가 도착한 대원과 바로 마주치지 않게).
        /// </summary>
        public static bool NearDoor(Vector3 position)
        {
            if (!Active) return false;
            float dx = Mathf.Abs(position.x) - DoorU, dz = position.z;
            return Contains(position) || dx * dx + dz * dz < DoorClearRadius * DoorClearRadius;
        }

        /// <summary>계단 문 앞 지점을 뺀 목록 (다 빠지면 원래 목록을 그대로 돌려줌)</summary>
        public static Transform[] AwayFromDoors(Transform[] points)
        {
            if (!Active || points == null) return points;
            var kept = new System.Collections.Generic.List<Transform>(points.Length);
            foreach (var p in points) if (p != null && !NearDoor(p.position)) kept.Add(p);
            return kept.Count > 0 ? kept.ToArray() : points;
        }

        /// <summary>계단 문마다 길 찾기를 끊는 막이 (몬스터만 막음. 대원·총알은 그대로 지나감)</summary>
        static void PlaceDoorBlockers(Scene scene, int floor)
        {
            const string name = "Stair Safe Zone Blockers (Runtime)";
            foreach (var root in scene.GetRootGameObjects()) if (root.name == name) return;
            var holder = new GameObject(name);
            SceneManager.MoveGameObjectToScene(holder, scene);
            float y = (floor - 1) * 8f;
            foreach (int side in new[] { -1, 1 })
            {
                var go = new GameObject(side < 0 ? "Door Blocker A" : "Door Blocker B");
                go.transform.SetParent(holder.transform, false);
                // 문 바로 안쪽 (몬스터는 자기 몸 반지름만큼 앞에서 멈춘다). 계단이 위아래로 4m씩 이어져 높이를 넉넉히
                go.transform.position = new Vector3(side * (DoorU + 0.6f), y + 0.5f, 0f);
                var obstacle = go.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(1.2f, 10f, HalfZ * 2f);
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
            }
        }
    }
}
