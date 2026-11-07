// 2층(화물 및 검역 구역) 임무 장치 배치
// 메뉴: Tools/VOID LAST/Multiplayer/6 Build F2 Mission (원본 씬과 2층 씬에 모두 적용)
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    public static class F2MissionSetup
    {
        const string ScenePath = "Assets/Map/Scenes/Stage2_Ship.unity";
        const string KitDir = "Assets/Creepy_Cat";
        const string LampMatPath = "Assets/Map/Materials/M_Mission_StatusLamp.mat";
        const float Floor = 8.0f;

        [MenuItem("Tools/VOID LAST/Multiplayer/6 Build F2 Mission")]
        public static string Build()
        {
            F2MissionItemAssets.BuildAll();
            var log = new List<string>();
            foreach (var path in new[] { ScenePath, FloorSceneBuilder.ScenePath(2) })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                log.Add(scene.name + ": " + BuildInOpenScene());
                StairDoorSetup.SaveWithNetworkHashes(scene);
            }
            string s = string.Join("\n", log);
            Debug.Log("[F2MissionSetup] " + s);
            return s;
        }

        /// <summary>지금 열려 있는 씬에 2층 임무를 (다시) 만든다. 저장은 하지 않음</summary>
        public static string BuildInOpenScene()
        {
            var multi = GameObject.Find(MultiplayerSetup.MultiRootName);
            if (multi == null) throw new InvalidOperationException("먼저 Multiplayer 설정(5 Patch Stage2 Scene)을 실행하세요");
            var old = multi.transform.Find("F2 Mission");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("F2 Mission").transform;
            root.SetParent(multi.transform, false);
            var lamp = LampMaterial();
            var log = new List<string>();

            Vector3 Marker(string name)
            {
                var m = GameObject.Find("VOID_LAST_Ship/F2_Cargo/Markers/" + name);
                if (m == null) throw new InvalidOperationException("마커 없음: " + name);
                var p = m.transform.position; p.y = Floor; return p;
            }

            Physics.SyncTransforms();

            // 0) 화물 분류실: 검역 카드 / 검역 관제실: 봉쇄 해제 콘솔 / 장비 작업실: 봉쇄 문 2개, 절단기
            Item(root, FarmingItemKind.QuarantineCard, "Quarantine Card", new Vector3(31.9f, 0, 27.7f), true);
            var console = Device(root, "Quarantine Console", "P_Technic_Console_01", new Vector3(29f, Floor, -41f), 0f, lamp,
                MissionInteractable.Kind.QuarantineConsole, "검역 카드로 봉쇄 해제", 2f);
            log.Add(Describe(console));
            var lockdownDoors = new[]
            {
                LockdownDoor(root, "Lockdown Door N-NE", new Vector3(11f, Floor, -19f), true),
                LockdownDoor(root, "Lockdown Door N-C", new Vector3(1f, Floor, -14f), false),
            };
            Item(root, FarmingItemKind.Cutter, "Cutter", new Vector3(10.5f, 0, -14.2f), true);

            // 1) 정비 보관실: 전력 모듈 보관함 (절단기로 자르면 앞에 모듈이 나타남)
            // 방 가운데(마커)에는 바닥 안개 효과가 짙어서, 같은 방 북쪽 벽 앞에 둔다
            var moduleAt = Marker("F2_Mission_PowerModule_NW") + new Vector3(0f, 0f, -6.7f);
            var module = Device(root, "Power Module Rack", "P_Electric_Battery_01", moduleAt, 0f, lamp,
                MissionInteractable.Kind.PowerModule, "절단기로 보관함을 잘라 전력 모듈 꺼내기", 3f);
            log.Add(Describe(module));
            var modulePickup = Item(root, FarmingItemKind.PowerModule, "Power Module", moduleAt + new Vector3(0f, 0f, 1.8f), false);
            modulePickup.gameObject.SetActive(false);

            // 중앙 화물창: 전력 모듈을 1층으로 내려보내는 리프트
            var liftAt = new Vector3(8f, Floor, 27.1f);
            var liftPlatform = CargoLift(root, liftAt, out var liftCargo);
            var liftConsole = Device(root, "Cargo Lift Console", "P_Technic_Console_01", liftAt + new Vector3(-2.5f, 0f, 0.9f), 0f, lamp,
                MissionInteractable.Kind.CargoLift, "전력 모듈을 싣고 1층으로 내려보내기", 3f);
            log.Add(Describe(liftConsole));

            // 연료 커플러 후보 위치 (상자 위 또는 바닥). 판마다 이 중 한 곳에만 나타난다
            var couplerSpots = new List<FpsWorldPickup>();
            foreach (var at in new[]
            {
                new Vector3(-28.9f, 0, 30.2f),   // 컨테이너 보관실
                new Vector3(17.2f, 0, 26.5f),    // 중앙 화물창
                new Vector3(-35.2f, 0, -25.7f),  // A 계단 대기실
                new Vector3(-9.5f, 0, -41.7f),   // 정비 보관실
                new Vector3(8.8f, 0, 50.8f),     // 검역 보관실
                new Vector3(17.9f, 0, -16.6f),   // 장비 작업실
                new Vector3(34.5f, 0, -45f),     // 검역 관제실
                new Vector3(45.6f, 0, 26.4f),    // 화물 분류실
            })
            {
                var spot = Item(root, FarmingItemKind.FuelCoupler, "Fuel Coupler " + (couplerSpots.Count + 1), at, true);
                spot.gameObject.SetActive(false);
                couplerSpots.Add(spot);
            }

            // 2) 연료 공급실: 연료 공급 제어반 + 밸브
            var fuelAt = Marker("F2_Mission_FuelSupply_SW");
            var fuel = Device(root, "Fuel Supply Console", "P_Technic_Console_01_B", fuelAt, 90f, lamp,
                MissionInteractable.Kind.FuelPump, "연료 커플러를 끼우고 충전 시작", 2f);
            var valve = Kit("P_Pipe_Valve_01");
            if (valve != null)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(valve, fuel.transform);
                v.name = "Fuel Valve";
                v.transform.position = fuelAt + new Vector3(0, 0, 1.9f);
                v.transform.rotation = Quaternion.Euler(0, 90, 0);
            }
            log.Add(Describe(fuel));

            // 컨테이너 보관실: 충전 중간에 고장 나는 연료 밸브
            var valve2 = Device(root, "Fuel Valve Junction", "P_Pipe_Valve_01", new Vector3(-41.6f, Floor, 28.5f), 90f, lamp,
                MissionInteractable.Kind.FuelValve, "연료 밸브 고치기", 4f);
            log.Add(Describe(valve2));

            // 3) 검역 보관실: 승무원 기록 단말기 (선택). 조사하면 옆에 배터리가 나타남
            var traceAt = Marker("F2_Mission_IntrusionTrace_SE");
            var trace = Device(root, "Crew Log Terminal", "P_Info_Terminal_01", traceAt, 180f, lamp,
                MissionInteractable.Kind.IntrusionLog, "승무원 기록 확인", 1f);
            trace.statusLight.color = new Color(0.3f, 0.85f, 1f);
            log.Add(Describe(trace));
            var batteryReward = Item(root, FarmingItemKind.Battery, "Battery Reward", traceAt + new Vector3(1.3f, 0f, -1f), false);
            batteryReward.gameObject.SetActive(false);

            // 4) 2층 → 1층 계단 입구 안내등 (탈출 준비가 끝나면 켜짐). 2층은 A 계단으로만 내려간다
            var rallyLights = new List<Light>();
            foreach (var x in new[] { -41.2f })
            {
                var lg = new GameObject(x < 0 ? "Stair A Rally Light" : "Stair B Rally Light").transform;
                lg.SetParent(root, false);
                lg.position = new Vector3(x, 11.3f, 0f);
                var l = lg.gameObject.AddComponent<Light>();
                l.type = LightType.Point; l.range = 7f; l.intensity = 2.5f; l.color = new Color(1f, 0.62f, 0.12f);
                l.shadows = LightShadows.None; l.enabled = false;
                rallyLights.Add(l);
            }

            // 디렉터 (모든 대원 공통 진행 상태)
            var dirGo = new GameObject("F2 Mission Director");
            dirGo.transform.SetParent(root, false);
            dirGo.AddComponent<NetworkObject>();
            var dir = dirGo.AddComponent<F2MissionDirector>();
            dir.quarantineConsole = console; dir.powerModule = module; dir.cargoLift = liftConsole;
            dir.fuelPump = fuel; dir.fuelValve = valve2; dir.intrusionLog = trace; dir.rallyLights = rallyLights.ToArray();
            dir.modulePickup = modulePickup; dir.couplerSpots = couplerSpots.ToArray(); dir.batteryReward = batteryReward;
            dir.lockdownDoors = lockdownDoors;
            dir.liftPlatform = liftPlatform; dir.liftCargo = liftCargo;
            dir.fuelRoom = new Bounds(new Vector3(-62f, 10.2f, 26f), new Vector3(21f, 5f, 17f));     // 연료 공급실 (x -72~-52, z 18~34)
            dir.rallyZones = new[]
            {
                new Bounds(new Vector3(-50f, 5f, 0f), new Vector3(15.4f, 8.5f, 15.4f)),   // 2층→1층 A 계단 안 (x -58~-42)
                new Bounds(new Vector3(50f, 5f, 0f), new Vector3(15.4f, 8.5f, 15.4f)),    // 2층→1층 B 계단 안 (x 42~58)
            };
            dir.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);

            // 2층은 A 계단으로만 내려간다 (층별 씬에만 있음)
            var floorTransition = Object.FindFirstObjectByType<FloorTransition>(FindObjectsInactive.Include);
            if (floorTransition != null) { floorTransition.descentSides = "A"; EditorUtility.SetDirty(floorTransition); }

            // 쪼꼬미가 봉쇄된 장비 작업실 안에서 시작하지 않도록 중앙 화물창으로 옮긴다
            var small = GameObject.Find("VOID_LAST_Integration_Test/VOID_LAST_SmallMonster");
            if (small != null)
            {
                small.transform.position = new Vector3(3f, Floor + 0.07f, 18f);
                EditorUtility.SetDirty(small.transform);
                if (PrefabUtility.IsPartOfPrefabInstance(small)) PrefabUtility.RecordPrefabInstancePropertyModifications(small.transform);
            }

            return "F2 mission: " + string.Join(" | ", log);
        }

        static string Describe(MissionInteractable d) => d.name + " @" + d.transform.position.ToString("F1");

        /// <summary>임무 물건을 놓는다. onTop이면 그 지점 위에 있는 소품(상자·탁자) 위에, 아니면 바닥에 놓는다.</summary>
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

        /// <summary>검역 봉쇄 문: 복도 폭을 재서 그 크기에 맞춘다. widthAlongX면 x축 방향으로 넓은 문.</summary>
        static F2MissionDirector.LockdownDoor LockdownDoor(Transform parent, string name, Vector3 at, bool widthAlongX)
        {
            Vector3 side = widthAlongX ? Vector3.right : Vector3.forward;
            Vector3 probe = at + Vector3.up * 1.5f;
            float a = Physics.Raycast(probe, side, out var h1, 3.5f, ~0, QueryTriggerInteraction.Ignore) ? h1.distance : 2.5f;
            float b = Physics.Raycast(probe, -side, out var h2, 3.5f, ~0, QueryTriggerInteraction.Ignore) ? h2.distance : 2.5f;
            float width = a + b;
            Vector3 center = at + side * ((a - b) * 0.5f);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, Quaternion.Euler(0, widthAlongX ? 0f : 90f, 0));

            // 문짝 (계단 문과 같은 모델)
            var leaf = new GameObject("Leaf").transform;
            leaf.SetParent(go.transform, false);
            var src = Kit("P_Ship_BigDoors_Fake_01");
            if (src != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(src, leaf);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = new Vector3(0.88f, 0.88f, 0.6f);
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                var rs = model.GetComponentsInChildren<MeshRenderer>();
                if (rs.Length > 0)
                {
                    var bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds);
                    float current = widthAlongX ? bounds.size.x : bounds.size.z;
                    if (current > 0.1f) { var s = model.transform.localScale; s.x *= width / current; model.transform.localScale = s; }
                }
            }
            // 문짝 모서리가 비스듬해서 뒤가 비쳐 보이므로 뒤에 철판을 댄다
            F2MissionItemAssets.Part(leaf, "Back Plate", PrimitiveType.Cube, new Vector3(0, 2f, 0), new Vector3(width + 0.1f, 4f, 0.08f),
                F2MissionItemAssets.Mat("M_Item_Dark", new Color(0.09f, 0.1f, 0.11f), 0.6f, 0.45f));
            var warning = new GameObject("Warning Light");
            warning.transform.SetParent(leaf, false);
            warning.transform.localPosition = new Vector3(0, 3.2f, 0);
            var light = warning.AddComponent<Light>();
            light.type = LightType.Point; light.range = 6f; light.intensity = 1.6f; light.color = new Color(1f, 0.15f, 0.1f); light.shadows = LightShadows.None;

            // 길을 막는 콜라이더 + 조준 판정 + 몬스터 길찾기 차단
            var blockerGo = new GameObject("Blocker");
            blockerGo.transform.SetParent(go.transform, false);
            var blocker = blockerGo.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0, 2f, 0);
            blocker.size = new Vector3(width + 0.1f, 4f, 0.35f);
            var obstacle = blockerGo.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = blocker.center;
            obstacle.size = blocker.size + new Vector3(0, 0, 0.3f);
            obstacle.carving = true;

            var triggerGo = new GameObject("Interact Trigger");
            triggerGo.layer = 2;   // Ignore Raycast: 총알에는 걸리지 않음
            triggerGo.transform.SetParent(go.transform, false);
            var trigger = triggerGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = blocker.center;
            trigger.size = new Vector3(width, 4f, 0.8f);

            var info = go.AddComponent<MissionInteractable>();
            info.kind = MissionInteractable.Kind.LockdownDoor;
            info.maxDistance = 3f;

            return new F2MissionDirector.LockdownDoor { leaf = leaf, blocker = blocker, interactTrigger = trigger, obstacle = obstacle, openOffset = new Vector3(0, 4f, 0) };
        }

        /// <summary>화물 리프트: 바닥 구멍 + 기둥과 윗보. 발판은 허리 높이에서 시작해 구멍 아래로 내려간다.</summary>
        static Transform CargoLift(Transform parent, Vector3 at, out GameObject cargo)
        {
            var dark = F2MissionItemAssets.Mat("M_Item_Dark", new Color(0.09f, 0.1f, 0.11f), 0.6f, 0.45f);
            var metal = F2MissionItemAssets.Mat("M_Item_Metal", new Color(0.62f, 0.64f, 0.67f), 0.95f, 0.6f);
            var yellow = F2MissionItemAssets.Mat("M_Item_Yellow", new Color(0.95f, 0.75f, 0.1f), 0.05f, 0.55f);
            var pit = F2MissionItemAssets.Mat("M_Item_Pit", Color.black, 0f, 0f);
            var root = new GameObject("Cargo Lift").transform;
            root.SetParent(parent, false);
            root.position = at;

            const float half = 1.45f, height = 3.1f;
            F2MissionItemAssets.Part(root, "Pit", PrimitiveType.Cube, new Vector3(0, 0.012f, 0), new Vector3(half * 2, 0.012f, half * 2), pit);
            foreach (var s in new[] { -1, 1 })
            {
                F2MissionItemAssets.Part(root, "Edge X", PrimitiveType.Cube, new Vector3(0, 0.02f, s * half), new Vector3(half * 2 + 0.2f, 0.03f, 0.2f), yellow);
                F2MissionItemAssets.Part(root, "Edge Z", PrimitiveType.Cube, new Vector3(s * half, 0.02f, 0), new Vector3(0.2f, 0.03f, half * 2 + 0.2f), yellow);
                F2MissionItemAssets.Part(root, "Beam", PrimitiveType.Cube, new Vector3(0, height, s * half), new Vector3(half * 2 + 0.2f, 0.16f, 0.16f), dark);
                foreach (var u in new[] { -1, 1 })
                    F2MissionItemAssets.Part(root, "Post", PrimitiveType.Cube, new Vector3(u * half, height * 0.5f, s * half), new Vector3(0.16f, height, 0.16f), dark);
            }

            var platform = new GameObject("Lift Platform").transform;
            platform.SetParent(root, false);
            platform.localPosition = new Vector3(0, 0.95f, 0);
            F2MissionItemAssets.Part(platform, "Deck", PrimitiveType.Cube, Vector3.zero, new Vector3(half * 2 - 0.25f, 0.1f, half * 2 - 0.25f), metal);

            // 실은 전력 모듈 (보낼 때만 보임)
            var modulePrefab = F2MissionItemAssets.Load(FarmingItemKind.PowerModule);
            var model = Object.Instantiate(modulePrefab.transform.Find("Model").gameObject, platform, false);
            model.name = "Loaded Module";
            model.transform.localPosition = new Vector3(0, 0.05f, 0);
            model.transform.localScale = Vector3.one * 1.3f;
            model.SetActive(false);
            cargo = model;

            // 기둥 안쪽으로 못 들어가게 막는다 (몬스터 길찾기도 돌아가게)
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0, height * 0.5f, 0);
            box.size = new Vector3(half * 2 + 0.2f, height, half * 2 + 0.2f);
            var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;
            return platform;
        }

        static GameObject Kit(string name)
        {
            foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab", new[] { KitDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        static MissionInteractable Device(Transform parent, string name, string kitPrefab, Vector3 at, float yaw, Material lamp,
            MissionInteractable.Kind kind, string label, float hold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));

            var src = Kit(kitPrefab);
            Bounds b = new Bounds(at + Vector3.up, new Vector3(1, 2, 1));
            if (src != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(src, go.transform);
                model.name = kitPrefab;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                // 위치 계산은 메시만 (스파크 파티클 렌더러의 경계는 크고 엉뚱한 곳에 잡힌다)
                var rs = model.GetComponentsInChildren<MeshRenderer>();
                if (rs.Length > 0) { b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); }
                // 바닥에 맞춤
                float lift = at.y - b.min.y;
                model.transform.position += Vector3.up * lift; b.center += Vector3.up * lift;
            }

            // 조준 판정용 트리거 (이동은 막지 않음)
            var trig = go.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.center = go.transform.InverseTransformPoint(b.center);
            var local = Quaternion.Inverse(go.transform.rotation) * b.size;
            trig.size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)) + Vector3.one * 0.3f;

            // 상태 표시등 (위에 작은 램프 + 빛)
            var lampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lampGo.name = "Status Lamp";
            Object.DestroyImmediate(lampGo.GetComponent<Collider>());
            lampGo.transform.SetParent(go.transform, false);
            lampGo.transform.position = new Vector3(b.center.x, b.max.y + 0.08f, b.center.z);
            lampGo.transform.localScale = Vector3.one * 0.16f;
            var lr = lampGo.GetComponent<MeshRenderer>();
            lr.sharedMaterial = lamp; lr.shadowCastingMode = ShadowCastingMode.Off;
            var lightGo = new GameObject("Status Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.position = new Vector3(b.center.x, b.max.y + 0.5f, b.center.z);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point; light.range = 4.5f; light.intensity = 1.5f; light.shadows = LightShadows.None;
            light.color = new Color(0.25f, 1f, 0.45f);

            var d = go.AddComponent<MissionInteractable>();
            d.kind = kind; d.actionLabel = label; d.holdSeconds = hold; d.maxDistance = 2.6f;
            d.statusLight = light; d.statusRenderer = lr;
            return d;
        }

        static Material LampMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(LampMatPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, LampMatPath);
            }
            mat.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.16f));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.3f, 1f, 0.5f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
#endif
