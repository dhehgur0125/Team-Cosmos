// 3층(중앙 통제 구역) 임무 장치·잠긴 문·관리자 카드·몬스터 배치
// 메뉴: Tools/VOID LAST/Multiplayer/6c Build F3 Mission (3층 씬에만 적용. 다시 실행하면 지우고 다시 만든다)
// 층 씬을 메뉴 9로 다시 나눌 때도 3층에 함께 실행된다 (FloorSceneBuilder).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using VoidLast.Monster;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class F3MissionSetup
    {
        const float Floor = 16f;
        const string RootName = "F3 Mission";
        const string PropsPath = "VOID_LAST_Ship/F3_Control/Props";
        const string LayoutPath = "Assets/Map/Stage2_Ship/ship_layout_v2.json";
        const string MonsterDir = "Assets/Monster/Prefabs/";

        [Serializable] class Layout { public LayoutFloor[] floors; }
        [Serializable] class LayoutFloor { public int n; public LayoutRoom[] rooms; public LayoutCorridor[] cors; }
        [Serializable] class LayoutRoom { public string name; public float x0, z0, x1, z1; }
        [Serializable] class LayoutCorridor { public LayoutRect[] rects; }
        [Serializable] class LayoutRect { public float x0, z0, x1, z1; }

        [MenuItem("Tools/VOID LAST/Multiplayer/6c Build F3 Mission")]
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "play 중에는 실행할 수 없음";
            var scene = EditorSceneManager.OpenScene(FloorSceneBuilder.ScenePath(3), OpenSceneMode.Single);
            string s = scene.name + ": " + BuildInOpenScene();
            StairDoorSetup.SaveWithNetworkHashes(scene);
            Debug.Log("[F3MissionSetup] " + s);
            return s;
        }

        /// <summary>지금 열려 있는 3층 씬에 임무를 (다시) 만든다. 저장은 하지 않음</summary>
        public static string BuildInOpenScene()
        {
            var multi = GameObject.Find(MultiplayerSetup.MultiRootName);
            if (multi == null) throw new InvalidOperationException("먼저 Multiplayer 설정을 실행하세요");
            if (F2MissionItemAssets.Load(FarmingItemKind.AdminCard) == null) F2MissionItemAssets.BuildAdminCard();
            var old = multi.transform.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            root.SetParent(multi.transform, false);
            var lamp = F2MissionSetup.LampMaterial();
            var log = new List<string>();
            Physics.SyncTransforms();

            // 1) 서버 관리실 (x -42~-22, z -58~-42): 꺼진 서버 3대. 방에 놓여 있는 서버 랙을 그대로 쓴다
            //    (남쪽 벽의 랙 두 대는 앞에 숨는 자리가 있어 가려지므로 쓰지 않는다)
            var serverRoom = new Vector3(-32f, Floor, -50f);
            var servers = new[]
            {
                Wrap(root, "Server 1", "P_Server_01", -41.5f, -52.9f, serverRoom, lamp, MissionInteractable.Kind.ServerRack, "서버 다시 켜기", 3f, 0),
                Wrap(root, "Server 2", "P_Server_01", -22.5f, -44.6f, serverRoom, lamp, MissionInteractable.Kind.ServerRack, "서버 다시 켜기", 3f, 1),
                Wrap(root, "Server 3", "P_Server_01", -30.7f, -57.5f, serverRoom, lamp, MissionInteractable.Kind.ServerRack, "서버 다시 켜기", 3f, 2),
            };

            // 2) 보안실 (x -43~-27, z 18~38): 서버를 모두 켜면 열리는 문 2개 (서쪽 복도 문, 북쪽 정비 통로 문) + 책상 위 관리자 카드
            var securityDoors = new[]
            {
                Door(root, "Security Door W", new Vector3(-43f, Floor, 29f), false, MissionInteractable.Kind.SecurityDoor, 0),
                Door(root, "Security Door Maint", new Vector3(-38f, Floor, 38f), true, MissionInteractable.Kind.SecurityDoor, 1),
            };
            //    카드는 동쪽 벽 책상 위, 의자에 가려지지 않는 앞쪽 가장자리에 둔다
            var card = Item(root, FarmingItemKind.AdminCard, "Admin Card", new Vector3(-28.15f, 0, 26.9f), true);
            log.Add("card y=" + card.transform.position.y.ToString("F2"));

            // 3) 함교 (x -18~6, z 20~38): 관리자 카드로 여는 문 2개 (동쪽, 북쪽) + 함선 상태 화면
            var bridgeDoors = new[]
            {
                Door(root, "Bridge Door E", new Vector3(6f, Floor, 28f), false, MissionInteractable.Kind.BridgeDoor, 0),
                Door(root, "Bridge Door N", new Vector3(-3f, Floor, 38f), true, MissionInteractable.Kind.BridgeDoor, 1),
            };
            foreach (var d in bridgeDoors)
            {
                var info = d.leaf.parent.GetComponent<MissionInteractable>();
                info.actionLabel = "관리자 카드로 함교 문 열기"; info.holdSeconds = 1.5f;
            }
            var status = Wrap(root, "Bridge Status Display", "P_Holo_Display_02", -17.6f, 31.9f, new Vector3(-6f, Floor, 29f), lamp,
                MissionInteractable.Kind.BridgeStatus, "함선 상태 확인", 2f, 0);

            // 4) 데이터 보관실 (x -11~9, z -58~-42): 항로 데이터 단말. 서버 랙 줄의 빈자리에 새로 놓는다
            var download = Place(root, "Route Data Terminal", "P_Info_Terminal_01", new Vector3(1.2f, Floor, -45.9f), 0f, new Vector3(-1f, Floor, -48f), lamp,
                MissionInteractable.Kind.RouteDownload, "항로 데이터 내려받기 시작", 2f);

            // 5) 중앙 관제실 (x -7~21, z -22~-2): 감시 화면 (누르고 있는 동안 몬스터 위치 지도)
            var monitor = Wrap(root, "Monster Monitor", "P_Holo_Display_03", -6.5f, -7.9f, new Vector3(7f, Floor, -12f), lamp,
                MissionInteractable.Kind.MonsterMonitor, "몬스터 위치 보기", 0.1f, 0);

            // 6) 격납고 관제실 (x 16~32, z 12~32): 항로 데이터 전송 콘솔
            var uplink = Wrap(root, "Hangar Uplink Console", "P_Technic_Console_01", 24.9f, 31.7f, new Vector3(24f, Floor, 22f), lamp,
                MissionInteractable.Kind.HangarUplink, "항로 데이터 전송 · 1층 격납고 잠금 해제", 3f, 0);

            // 7) B 계단 입구 안내등 (임무를 끝내면 켜짐). 3층은 B 계단으로만 내려간다
            var exitGo = new GameObject("Stair B Exit Light");
            exitGo.transform.SetParent(root, false);
            exitGo.transform.position = new Vector3(41.2f, Floor + 3.3f, 0f);
            var exitLight = exitGo.AddComponent<Light>();
            exitLight.type = LightType.Point; exitLight.range = 7f; exitLight.intensity = 2.5f; exitLight.color = new Color(1f, 0.62f, 0.12f);
            exitLight.shadows = LightShadows.None; exitLight.enabled = false;
            var floorTransition = Object.FindFirstObjectByType<FloorTransition>(FindObjectsInactive.Include);
            if (floorTransition != null) { floorTransition.descentSides = "B"; EditorUtility.SetDirty(floorTransition); }

            // 8) 진행자 (모든 대원 공통 진행 상태)
            var dirGo = new GameObject("F3 Mission Director");
            dirGo.transform.SetParent(root, false);
            dirGo.AddComponent<NetworkObject>();
            var dir = dirGo.AddComponent<F3MissionDirector>();
            dir.servers = servers; dir.bridgeStatus = status; dir.routeDownload = download; dir.monsterMonitor = monitor; dir.hangarUplink = uplink;
            dir.securityDoors = securityDoors; dir.bridgeDoors = bridgeDoors;
            dir.exitLights = new[] { exitLight };
            dir.securityRoom = new Bounds(new Vector3(-35f, Floor + 2.2f, 28f), new Vector3(16f, 5f, 20f));   // x -43~-27, z 18~38
            dir.bridgeRoom = new Bounds(new Vector3(-6f, Floor + 2.2f, 29f), new Vector3(24f, 5f, 18f));       // x -18~6, z 20~38
            dir.dataRoom = new Bounds(new Vector3(-1f, Floor + 2.2f, -50f), new Vector3(19f, 5f, 15f));        // x -11~9, z -58~-42 (문턱 제외)
            dir.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);
            log.Add(FillMap(dir));

            // 9) 몬스터
            log.Add(PlaceMonsters(root));

            foreach (var d in new[] { servers[0], servers[1], servers[2], status, download, monitor, uplink }) log.Add(d.name + " @" + d.transform.position.ToString("F1"));
            return "F3 mission: " + string.Join(" | ", log);
        }

        // ================================================================== 장치
        /// <summary>맵에 이미 놓여 있는 소품을 임무 장치로 쓴다 (소품은 그대로 두고, 조준 판정과 표시등만 덧붙인다)</summary>
        static MissionInteractable Wrap(Transform parent, string name, string propName, float x, float z, Vector3 faceToward, Material lamp,
            MissionInteractable.Kind kind, string label, float hold, int index)
        {
            var props = GameObject.Find(PropsPath);
            if (props == null) throw new InvalidOperationException("3층 소품 묶음 없음: " + PropsPath);
            Transform best = null; float bestDistance = 1.5f;
            foreach (Transform c in props.transform)
            {
                if (c.name != propName) continue;
                float d = Vector2.Distance(new Vector2(c.position.x, c.position.z), new Vector2(x, z));
                if (d < bestDistance) { best = c; bestDistance = d; }
            }
            if (best == null) throw new InvalidOperationException("소품 없음: " + propName + " (" + x + ", " + z + ") — 맵이 바뀌었으면 F3MissionSetup의 좌표를 고치세요");
            var rs = best.GetComponentsInChildren<MeshRenderer>();
            if (rs.Length == 0) throw new InvalidOperationException("소품에 메시가 없음: " + propName);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(b.center.x, Floor, b.center.z);
            var device = Finish(go, b, faceToward, lamp, kind, label, hold);
            device.index = index;
            return device;
        }

        /// <summary>장치 소품을 새로 놓는다 (바닥에 맞춤)</summary>
        static MissionInteractable Place(Transform parent, string name, string kitPrefab, Vector3 at, float yaw, Vector3 faceToward, Material lamp,
            MissionInteractable.Kind kind, string label, float hold)
        {
            var src = F2MissionSetup.Kit(kitPrefab);
            if (src == null) throw new InvalidOperationException("소품 프리팹 없음: " + kitPrefab);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src, go.transform);
            model.name = kitPrefab;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var rs = model.GetComponentsInChildren<MeshRenderer>();
            var b = new Bounds(at + Vector3.up, new Vector3(1, 2, 1));
            if (rs.Length > 0) { b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); }
            float lift = at.y - b.min.y;
            model.transform.position += Vector3.up * lift; b.center += Vector3.up * lift;
            return Finish(go, b, faceToward, lamp, kind, label, hold);
        }

        /// <summary>조준 판정용 트리거 + 상태 표시등. 키가 큰 장치는 방 안쪽을 보는 면에, 낮은 장치는 위에 등을 단다</summary>
        static MissionInteractable Finish(GameObject go, Bounds b, Vector3 faceToward, Material lamp, MissionInteractable.Kind kind, string label, float hold)
        {
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = go.transform.InverseTransformPoint(b.center);
            var local = Quaternion.Inverse(go.transform.rotation) * b.size;
            trigger.size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)) + Vector3.one * 0.3f;

            Vector3 lampAt, lightAt;
            if (b.size.y > 1.7f)
            {
                Vector3 to = faceToward - b.center; to.y = 0;
                bool alongX = Mathf.Abs(to.x) > Mathf.Abs(to.z);
                Vector3 dir = alongX ? new Vector3(Mathf.Sign(to.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(to.z));
                lampAt = new Vector3(b.center.x, Floor + 1.6f, b.center.z) + dir * ((alongX ? b.extents.x : b.extents.z) + 0.04f);
                lightAt = lampAt + dir * 0.5f;
            }
            else
            {
                lampAt = new Vector3(b.center.x, b.max.y + 0.08f, b.center.z);
                lightAt = lampAt + Vector3.up * 0.42f;
            }
            var lampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lampGo.name = "Status Lamp";
            Object.DestroyImmediate(lampGo.GetComponent<Collider>());
            lampGo.transform.SetParent(go.transform, false);
            lampGo.transform.position = lampAt;
            lampGo.transform.localScale = Vector3.one * 0.16f;
            var lr = lampGo.GetComponent<MeshRenderer>();
            lr.sharedMaterial = lamp; lr.shadowCastingMode = ShadowCastingMode.Off;
            var lightGo = new GameObject("Status Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.position = lightAt;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point; light.range = 4.5f; light.intensity = 1.5f; light.shadows = LightShadows.None;
            light.color = new Color(1f, 0.62f, 0.12f);

            var d = go.AddComponent<MissionInteractable>();
            d.kind = kind; d.actionLabel = label; d.holdSeconds = hold; d.maxDistance = 2.6f;
            d.statusLight = light; d.statusRenderer = lr;
            return d;
        }

        /// <summary>잠긴 문 (2층 봉쇄 문과 같은 모양). 문 자체가 안내·상호작용 대상이 된다</summary>
        static F2MissionDirector.LockdownDoor Door(Transform parent, string name, Vector3 at, bool widthAlongX, MissionInteractable.Kind kind, int index)
        {
            var door = F2MissionSetup.LockdownDoor(parent, name, at, widthAlongX);
            var info = door.leaf.parent.GetComponent<MissionInteractable>();
            info.kind = kind; info.index = index;
            return door;
        }

        /// <summary>임무 물건을 놓는다. onTop이면 그 지점에 있는 소품(책상·상자) 위에, 아니면 바닥에</summary>
        static FpsWorldPickup Item(Transform parent, FarmingItemKind kind, string name, Vector3 at, bool onTop)
        {
            var prefab = F2MissionItemAssets.Load(kind);
            if (prefab == null) throw new InvalidOperationException("임무 물건 프리팹 없음: " + kind);
            var pickup = (FpsWorldPickup)PrefabUtility.InstantiatePrefab(prefab, parent);
            pickup.name = name;
            float y = Floor;
            if (onTop && Physics.Raycast(new Vector3(at.x, Floor + 3.2f, at.z), Vector3.down, out var hit, 3.4f, ~0, QueryTriggerInteraction.Ignore)) y = hit.point.y;
            pickup.transform.position = new Vector3(at.x, y + 0.01f, at.z);
            return pickup;
        }

        /// <summary>감시 화면 지도에 쓸 방·복도 모양을 맵 배치 파일에서 읽어 넣는다</summary>
        static string FillMap(F3MissionDirector dir)
        {
            if (!File.Exists(LayoutPath)) return "map: 배치 파일 없음 (" + LayoutPath + ")";
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            LayoutFloor data = null;
            foreach (var f in layout.floors) if (f.n == 3) data = f;
            if (data == null) return "map: 3층 배치 정보 없음";
            var rooms = new List<F3MissionDirector.MapRoom>();
            foreach (var r in data.rooms) rooms.Add(new F3MissionDirector.MapRoom { name = r.name, area = Rect.MinMaxRect(r.x0, r.z0, r.x1, r.z1) });
            var corridors = new List<Rect>();
            if (data.cors != null)
                foreach (var c in data.cors)
                    if (c.rects != null) foreach (var r in c.rects) corridors.Add(Rect.MinMaxRect(r.x0, r.z0, r.x1, r.z1));
            dir.mapRooms = rooms.ToArray();
            dir.mapCorridors = corridors.ToArray();
            return "map: rooms " + rooms.Count + ", corridor rects " + corridors.Count;
        }

        // ================================================================== 몬스터
        /// <summary>눈 있는 대형 1(중앙 관제실, 동쪽 구역 순찰) + 쪼꼬미 1(데이터 보관실) + 개구리 2(복도 천장 매복)</summary>
        static string PlaceMonsters(Transform root)
        {
            var monsters = new GameObject("Monsters").transform;
            monsters.SetParent(root, false);
            var log = new List<string>();

            // ---- 눈 있는 대형: 중앙 관제실 → 격납고 관제실 → 보안 출입실을 도는 순찰 (함교는 문이 열린 뒤에만 들어감)
            var patrol = Points(monsters, "Eyed Patrol", new[]
            {
                new Vector2(7, -12), new Vector2(9, 5), new Vector2(9, 16), new Vector2(21, 8), new Vector2(24, 22), new Vector2(38, 8),
                new Vector2(38, -5), new Vector2(44, -12), new Vector2(40, -26), new Vector2(25, -27), new Vector2(25, -14), new Vector2(-6, 29),
            });
            var grid = new List<Vector2>();
            for (int x = -66; x <= 66; x += 6) for (int z = -60; z <= 44; z += 6) grid.Add(new Vector2(x, z));
            var search = Points(monsters, "Eyed Search", grid.ToArray());
            if (patrol.Count > 0)
            {
                var eyed = Spawn(monsters, "VOID_LAST_EyedLargeMonster", patrol[0].position, 180f);
                SetArray(eyed.GetComponent<EyedMonsterAI>(), "patrolPoints", patrol);
                SetArray(eyed.GetComponent<EyedMonsterAI>(), "searchPoints", search);
                log.Add("eyed patrol=" + patrol.Count + " search=" + search.Count);
            }
            else log.Add("eyed: NavMesh 위치를 찾지 못함");

            // ---- 쪼꼬미: 데이터 보관실에서 시작해 맵의 순찰 지점(남쪽 방들과 중앙)을 돌아다님. 입구 방은 시작 지점을 피해서 남쪽 문 앞
            var smallPoints = Points(monsters, "Small Search", new[] { new Vector2(-1, -50), new Vector2(-32, -50), new Vector2(-23, -30), new Vector2(7, -12) });
            if (smallPoints.Count > 0)
            {
                var small = Spawn(monsters, "VOID_LAST_SmallMonster", smallPoints[0].position, 0f);
                SetArray(small.GetComponent<SmallMonsterAI>(), "searchPoints", smallPoints);
                log.Add("small search=" + smallPoints.Count);
            }
            else log.Add("small: NavMesh 위치를 찾지 못함");

            // ---- 개구리: 함교로 가는 복도, 격납고 관제실·B 계단으로 가는 복도의 천장
            log.Add(Frog(monsters, "Frog 1", new[] { new Vector2(9, 5), new Vector2(-10, 16), new Vector2(-22, 30) }));
            //      (동쪽 복도 z -15 근처에는 숨는 자리가 있어서 그 위는 피한다)
            log.Add(Frog(monsters, "Frog 2", new[] { new Vector2(62, -2), new Vector2(50, 12), new Vector2(30, 8) }));
            return "monsters: " + string.Join(", ", log);
        }

        /// <summary>걸어서 갈 수 있는 바닥 위 지점만 골라 빈 오브젝트로 만든다</summary>
        static List<Transform> Points(Transform parent, string label, Vector2[] positions)
        {
            var group = new GameObject(label).transform;
            group.SetParent(parent, false);
            var points = new List<Transform>();
            foreach (var p in positions)
            {
                if (!FloorPoint(p, 0.9f, out var at)) continue;
                var point = new GameObject(label + " " + (points.Count + 1)).transform;
                point.SetParent(group, false);
                point.position = at;
                points.Add(point);
            }
            return points;
        }

        /// <summary>그 (x, z) 근처의 3층 바닥 NavMesh 지점 (소품 위나 다른 층은 제외)</summary>
        static bool FloorPoint(Vector2 xz, float range, out Vector3 at)
        {
            at = default;
            if (!NavMesh.SamplePosition(new Vector3(xz.x, Floor + 0.1f, xz.y), out var hit, range, NavMesh.AllAreas)) return false;
            if (Mathf.Abs(hit.position.y - Floor) > 0.4f) return false;
            at = hit.position;
            return true;
        }

        static GameObject Spawn(Transform parent, string prefabName, Vector3 at, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterDir + prefabName + ".prefab");
            if (prefab == null) throw new InvalidOperationException("몬스터 프리팹 없음: " + prefabName);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.03f, Quaternion.Euler(0, yaw, 0));
            Network(go);
            return go;
        }

        static string Frog(Transform parent, string name, Vector2[] spots)
        {
            var ambushRoot = new GameObject(name + " Ambush Points").transform;
            ambushRoot.SetParent(parent, false);
            var ambush = new List<Transform>();
            foreach (var p in spots)
            {
                if (!FloorPoint(p, 3f, out var floor)) continue;
                if (!Physics.Raycast(floor + Vector3.up * 0.5f, Vector3.up, out var ceiling, 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var point = new GameObject(name + " Ambush " + (ambush.Count + 1)).transform;
                point.SetParent(ambushRoot, false);
                point.SetPositionAndRotation(ceiling.point, Quaternion.FromToRotation(Vector3.up, ceiling.normal));   // 위쪽 = 표면에서 방 안쪽
                ambush.Add(point);
            }
            if (ambush.Count == 0) return name + ": 천장을 찾지 못함";
            if (!FloorPoint(new Vector2(ambush[0].position.x, ambush[0].position.z), 6f, out var spawn)) return name + ": 바닥을 찾지 못함";
            var frog = Spawn(parent, "VOID_LAST_FrogMonster", spawn, 270f);
            frog.name = "VOID_LAST_FrogMonster (" + name + ")";
            SetArray(frog.GetComponent<FrogMonsterAI>(), "ambushPoints", ambush);
            return name + " ambush=" + ambush.Count;
        }

        static void SetArray(Object target, string property, List<Transform> points)
        {
            var so = new SerializedObject(target);
            var array = so.FindProperty(property);
            array.arraySize = points.Count;
            for (int i = 0; i < points.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        /// <summary>2층 몬스터와 같은 멀티플레이 구성: 방장이 정한 위치를 동기화(좌우 회전만) + MonsterNetworkSync</summary>
        static void Network(GameObject monster)
        {
            if (monster.GetComponent<NetworkObject>() == null) monster.AddComponent<NetworkObject>();
            var nt = monster.GetComponent<NetworkTransform>();
            if (nt == null) nt = monster.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            if (monster.GetComponent<MonsterNetworkSync>() == null) monster.AddComponent<MonsterNetworkSync>();
        }
    }
}
#endif
