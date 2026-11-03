// 2층(정비 및 화물 구역) 임무 장치 배치
// 메뉴: Tools/VOID LAST/Multiplayer/6 Build F2 Mission
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
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

            // 1) 정비 보관실: 전력 모듈 보관함
            // 방 가운데(마커)에는 바닥 안개 효과가 짙어서, 같은 방 북쪽 벽 앞에 둔다
            var moduleAt = Marker("F2_Mission_PowerModule_NW") + new Vector3(0f, 0f, -6.7f);
            var module = Device(root, "Power Module Rack", "P_Electric_Battery_01", moduleAt, 0f, lamp,
                MissionInteractable.Kind.PowerModule, "보조 우주선용 전력 모듈 꺼내기", 3f);
            log.Add(Describe(module));

            // 2) 연료 공급실: 연료 공급 제어반 + 밸브
            var fuelAt = Marker("F2_Mission_FuelSupply_SW");
            var fuel = Device(root, "Fuel Supply Console", "P_Technic_Console_01_B", fuelAt, 90f, lamp,
                MissionInteractable.Kind.FuelPump, "연료 공급 장치 가동", 2f);
            var valve = Kit("P_Pipe_Valve_01");
            if (valve != null)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(valve, fuel.transform);
                v.name = "Fuel Valve";
                v.transform.position = fuelAt + new Vector3(0, 0, 1.9f);
                v.transform.rotation = Quaternion.Euler(0, 90, 0);
            }
            log.Add(Describe(fuel));

            // 3) 검역 보관실: 승무원 기록 단말기 (선택)
            var trace = Device(root, "Crew Log Terminal", "P_Info_Terminal_01", Marker("F2_Mission_IntrusionTrace_SE"), 180f, lamp,
                MissionInteractable.Kind.IntrusionLog, "승무원 기록 확인", 1f);
            trace.statusLight.color = new Color(0.3f, 0.85f, 1f);
            log.Add(Describe(trace));

            // 4) 2층 → 1층 계단 입구 안내등 (탈출 준비가 끝나면 켜짐)
            var rallyLights = new List<Light>();
            foreach (var x in new[] { -41.2f, 41.2f })
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
            dir.powerModule = module; dir.fuelPump = fuel; dir.intrusionLog = trace; dir.rallyLights = rallyLights.ToArray();
            dir.fuelRoom = new Bounds(new Vector3(-62f, 10.2f, 26f), new Vector3(21f, 5f, 17f));     // 연료 공급실 (x -72~-52, z 18~34)
            dir.rallyZones = new[]
            {
                new Bounds(new Vector3(-50f, 5f, 0f), new Vector3(15.4f, 8.5f, 15.4f)),   // 2층→1층 A 계단 안 (x -58~-42)
                new Bounds(new Vector3(50f, 5f, 0f), new Vector3(15.4f, 8.5f, 15.4f)),    // 2층→1층 B 계단 안 (x 42~58)
            };
            dir.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MultiplayerSetup.FontAssetPath);

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
            string s = "F2 mission: " + string.Join(" | ", log);
            Debug.Log("[F2MissionSetup] " + s);
            return s;
        }

        static string Describe(MissionInteractable d) => d.name + " @" + d.transform.position.ToString("F1");

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
