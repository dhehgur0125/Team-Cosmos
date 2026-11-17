using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace VoidLast.Monster.EditorTools
{
    /// <summary>
    /// 새 몬스터 2종(눈 없는 대형·개구리) 프리팹과 애니메이션을 만들고, 눈 있는 대형의 모델을 바꾼다.
    ///  - 메뉴 Tools/VOID LAST/Monster/1 Setup New Monsters: 프리팹·애니메이션 컨트롤러 생성 (다시 실행해도 같은 결과. 프리팹에서 직접 바꾼 수치는 덮어쓴다)
    ///      눈 없는 대형 = Telluropod, 개구리 = Deinodonte, 눈 있는 대형 = Arthromahre (Monsters Pack Vol 2)
    ///  - 메뉴 Tools/VOID LAST/Monster/2 Place New Monsters In 2F (Test): 2층 씬에 시험용으로 한 마리씩 놓고 멀티플레이 동기화를 붙인다
    ///      (층 씬을 메뉴 9로 다시 만들면 사라지므로 그때 다시 실행)
    /// </summary>
    public static class NewMonsterSetup
    {
        const string Pack = "Assets/Monsters Full Pack Vol 1/Monsters Pack Vol 2/";
        const string AnimDir = "Assets/Monster/Animations/";
        const string PrefabDir = "Assets/Monster/Prefabs/";
        const string EyedPrefab = PrefabDir + "VOID_LAST_EyedLargeMonster.prefab";
        const string BlindLargePrefab = PrefabDir + "VOID_LAST_BlindLargeMonster.prefab";
        const string FrogPrefab = PrefabDir + "VOID_LAST_FrogMonster.prefab";
        const string EyedController = AnimDir + "VOID_LAST_EyedLargeMonster.controller";
        const string BlindLargeController = AnimDir + "VOID_LAST_BlindLargeMonster.controller";
        const string FrogController = AnimDir + "VOID_LAST_FrogMonster.controller";
        const string Floor2Scene = "Assets/Map/Scenes/Floors/Stage2_Ship_2f.unity";
        const string TestRootName = "New Monsters (Test)";

        // 모델 크기 (원본 프리팹 대비)
        const float BlindLargeScale = 0.55f, FrogScale = 0.4f, EyedScale = 0.65f;

        // ================================================================== 1. 프리팹·애니메이션
        [MenuItem("Tools/VOID LAST/Monster/1 Setup New Monsters")]
        public static string Setup()
        {
            var log = new List<string>();

            // ---- 애니메이션 컨트롤러
            BuildController(BlindLargeController, "Telluropod",
                new[] { ("IdleBreathe", 0f, 1f), ("Crawl", 1.0f, 1f), ("Crawl", 1.8f, 1.5f) },
                attack: "BiteAttack", alert: "Roar", hit: "GetHitFront", dead: "Death");
            BuildController(FrogController, "Deinodonte",
                new[] { ("IdleBreathe", 0f, 1f), ("Walk", 2.5f, 1.2f), ("Run", 3.5f, 1f) },
                attack: "Bite", alert: "Roar", hit: "GetHit1", dead: "Death");
            RetargetEyedController();
            log.Add("controllers ok");

            // ---- 프리팹
            log.Add(SwapEyedModel());
            log.Add(BuildBlindLarge());
            log.Add(BuildFrog());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string result = string.Join(" | ", log);
            Debug.Log("[새 몬스터] " + result);
            return result;
        }

        static AnimationClip Clip(string model, string name)
        {
            string path = Pack + model + "/FBX Files/" + model + "@" + name + ".FBX";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (clip == null) throw new System.InvalidOperationException("애니메이션을 찾지 못함: " + path);
            return clip;
        }

        /// <summary>MonsterStateAnimation이 쓰는 상태(Locomotion·Attack·Alert·Hit·Dead·Stunned)를 가진 컨트롤러를 새로 만든다</summary>
        static void BuildController(string path, string model, (string clip, float speed, float timeScale)[] locomotion,
            string attack, string alert, string hit, string dead)
        {
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "AttackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
            controller.AddParameter(new AnimatorControllerParameter { name = "AlertSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
            var machine = controller.layers[0].stateMachine;

            var tree = new BlendTree { name = "Movement", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller);
            foreach (var (clip, speed, timeScale) in locomotion) tree.AddChild(Clip(model, clip), speed);
            var children = tree.children;
            for (int i = 0; i < children.Length; i++) children[i].timeScale = locomotion[i].timeScale;
            tree.children = children;

            var move = machine.AddState("Locomotion"); move.motion = tree;
            machine.defaultState = move;
            var attackState = machine.AddState("Attack"); attackState.motion = Clip(model, attack);
            attackState.speedParameter = "AttackSpeed"; attackState.speedParameterActive = true;
            var alertState = machine.AddState("Alert"); alertState.motion = Clip(model, alert);
            alertState.speedParameter = "AlertSpeed"; alertState.speedParameterActive = true;
            machine.AddState("Hit").motion = Clip(model, hit);
            machine.AddState("Dead").motion = Clip(model, dead);
            var stunned = machine.AddState("Stunned"); stunned.motion = Clip(model, "IdleBreathe"); stunned.speed = 0.15f;
            EditorUtility.SetDirty(controller);
        }

        /// <summary>눈 있는 대형의 기존 컨트롤러(상태 구성은 그대로)에서 동작만 Arthromahre 것으로 바꾼다</summary>
        static void RetargetEyedController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(EyedController);
            foreach (var child in controller.layers[0].stateMachine.states)
            {
                var state = child.state;
                if (state.motion is BlendTree tree)
                {
                    var kids = tree.children;
                    string[] names = { "IdleBreathe", "Walk", "Run" };
                    for (int i = 0; i < kids.Length && i < names.Length; i++) kids[i].motion = Clip("Arthromahre", names[i]);
                    tree.children = kids;
                    EditorUtility.SetDirty(tree);
                }
                else if (state.name == "Attack") state.motion = Clip("Arthromahre", "ClawsAttackRightForward");
                else if (state.name == "Hit") state.motion = Clip("Arthromahre", "GetHitFront");
                else if (state.name == "Dead") state.motion = Clip("Arthromahre", "Death");
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(controller);
        }

        static GameObject AddModel(Transform parent, string model, float scale, string controllerPath)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + model + "/Prefabs/" + model + ".prefab");
            if (source == null) throw new System.InvalidOperationException("모델 프리팹을 찾지 못함: " + model);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = "Model";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;
            var animator = instance.GetComponent<Animator>() ?? instance.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.updateWhenOffscreen = false;
            foreach (var collider in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);   // 맞는 범위는 루트의 캡슐 하나만 쓴다
            return instance;
        }

        static void Set(Object target, params (string name, object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in values)
            {
                var p = so.FindProperty(name);
                if (p == null) throw new System.InvalidOperationException(target.GetType().Name + "에 " + name + " 항목이 없음");
                switch (value)
                {
                    case float f: p.floatValue = f; break;
                    case int i: if (p.propertyType == SerializedPropertyType.Float) p.floatValue = i; else p.intValue = i; break;
                    case bool b: p.boolValue = b; break;
                    case Vector3 v: p.vector3Value = v; break;
                    case Object o: p.objectReferenceValue = o; break;
                    case null: p.objectReferenceValue = null; break;
                    default: throw new System.InvalidOperationException("지원하지 않는 값: " + name);
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void CopyHitFeedback(GameObject target)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(EyedPrefab).GetComponent<MonsterHitFeedback>();
            var feedback = target.AddComponent<MonsterHitFeedback>();
            if (source != null) EditorUtility.CopySerialized(source, feedback);
        }

        // ---------------------------------------------------------------- 눈 있는 대형: 모델만 Arthromahre로
        static string SwapEyedModel()
        {
            var root = PrefabUtility.LoadPrefabContents(EyedPrefab);
            try
            {
                var old = root.transform.Find("Model");
                var oldSource = old != null ? PrefabUtility.GetCorrespondingObjectFromSource(old.gameObject) : null;
                string oldPath = oldSource != null ? AssetDatabase.GetAssetPath(oldSource) : "";
                if (oldPath.Contains("/Arthromahre/"))
                {
                    old.localScale = Vector3.one * EyedScale;
                    PrefabUtility.SaveAsPrefabAsset(root, EyedPrefab);
                    return "eyed: already Arthromahre";
                }
                int sibling = old != null ? old.GetSiblingIndex() : root.transform.childCount;
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var model = AddModel(root.transform, "Arthromahre", EyedScale, EyedController);
                model.transform.SetSiblingIndex(sibling);
                var animation = root.GetComponent<EyedMonsterAnimation>();
                Set(animation, ("animator", model.GetComponentInChildren<Animator>()), ("attackClip", Clip("Arthromahre", "ClawsAttackRightForward")));
                PrefabUtility.SaveAsPrefabAsset(root, EyedPrefab);
                return "eyed: model " + System.IO.Path.GetFileNameWithoutExtension(oldPath) + " -> Arthromahre x" + EyedScale;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------------------------------------------------------------- 눈 없는 대형 (Telluropod)
        static string BuildBlindLarge()
        {
            var root = new GameObject("VOID_LAST_BlindLargeMonster");
            try
            {
                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = 0.9f; agent.height = 2f; agent.speed = 1f; agent.angularSpeed = 120; agent.acceleration = 6; agent.stoppingDistance = 0.3f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = 1.0f; capsule.height = 2.0f; capsule.center = new Vector3(0, 1.0f, 0);
                Set(root.AddComponent<MonsterHealth>(), ("maximumHealth", 600f));
                root.AddComponent<BlindLargeMonsterAI>();   // 수치는 스크립트 기본값 (기획서 값)
                var model = AddModel(root.transform, "Telluropod", BlindLargeScale, BlindLargeController);
                Set(root.AddComponent<MonsterStateAnimation>(), ("animator", model.GetComponentInChildren<Animator>()),
                    ("attackClip", Clip("Telluropod", "BiteAttack")), ("alertClip", Clip("Telluropod", "Roar")));
                CopyHitFeedback(root);
                PrefabUtility.SaveAsPrefabAsset(root, BlindLargePrefab);
                return "blind large: Telluropod x" + BlindLargeScale;
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---------------------------------------------------------------- 개구리 (Deinodonte)
        static string BuildFrog()
        {
            var root = new GameObject("VOID_LAST_FrogMonster");
            try
            {
                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = 0.45f; agent.height = 1.2f; agent.speed = 3.5f; agent.angularSpeed = 480; agent.acceleration = 20; agent.stoppingDistance = 0.3f;
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = 0.55f; capsule.height = 1.2f; capsule.center = new Vector3(0, 0.6f, 0);
                Set(root.AddComponent<MonsterHealth>(), ("maximumHealth", 90f));
                Set(root.AddComponent<MonsterVision>(), ("eyeOffset", new Vector3(0, 0.8f, 0.3f)), ("sightDistance", 15f), ("fieldOfView", 220f));
                // 착지 후 근접 공격: 피해 15, 약 1.2초 간격 (준비 0.3 + 돌진 0.2 + 회복 0.7)
                Set(root.AddComponent<MonsterMelee>(), ("attackDamage", 15f), ("attackStartDistance", 1.8f), ("attackWindupTime", 0.3f),
                    ("lungeDistance", 1.0f), ("lungeDuration", 0.2f), ("attackRecoveryTime", 0.7f), ("hitRange", 1.6f),
                    ("hitWidth", 1.0f), ("hitHeight", 1.5f), ("hitCenterHeight", 0.7f));
                var pivot = new GameObject("Pivot").transform;
                pivot.SetParent(root.transform, false);
                var model = AddModel(pivot, "Deinodonte", FrogScale, FrogController);
                Set(root.AddComponent<FrogMonsterAI>(), ("visualPivot", pivot));
                Set(root.AddComponent<MonsterStateAnimation>(), ("animator", model.GetComponentInChildren<Animator>()),
                    ("attackClip", Clip("Deinodonte", "Bite")), ("alertClip", Clip("Deinodonte", "Roar")));
                CopyHitFeedback(root);
                PrefabUtility.SaveAsPrefabAsset(root, FrogPrefab);
                return "frog: Deinodonte x" + FrogScale;
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ================================================================== 2. 2층에 시험 배치
        [MenuItem("Tools/VOID LAST/Monster/2 Place New Monsters In 2F (Test)")]
        public static string PlaceInFloor2()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "play 중에는 실행할 수 없음";
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Floor2Scene) scene = EditorSceneManager.OpenScene(Floor2Scene, OpenSceneMode.Single);

            var parent = GameObject.Find("VOID_LAST_Integration_Test");
            if (parent == null) return "2층 씬에 VOID_LAST_Integration_Test가 없음";
            var old = parent.transform.Find(TestRootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(TestRootName).transform;
            root.SetParent(parent.transform, false);
            var log = new List<string>();
            Physics.SyncTransforms();

            // ---- 눈 없는 대형: 동쪽 구역을 순찰
            var patrol = new List<Transform>();
            var patrolRoot = new GameObject("Blind Large Patrol").transform; patrolRoot.SetParent(root, false);
            foreach (var p in new[] { new Vector3(20, 8.1f, 0), new Vector3(20, 8.1f, -18), new Vector3(30, 8.1f, 0), new Vector3(20, 8.1f, 18), new Vector3(8, 8.1f, 0) })
            {
                if (!NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) continue;
                var point = new GameObject("Blind Large Patrol " + (patrol.Count + 1)).transform;
                point.SetParent(patrolRoot, false); point.position = hit.position;
                patrol.Add(point);
            }
            if (patrol.Count > 0)
            {
                var large = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BlindLargePrefab), root);
                large.transform.SetPositionAndRotation(patrol[0].position, Quaternion.Euler(0, 270, 0));
                SetArray(large.GetComponent<BlindLargeMonsterAI>(), "patrolPoints", patrol);
                Network(large);
                log.Add("blind large at " + patrol[0].position.ToString("0.0") + " patrol=" + patrol.Count);
            }
            else log.Add("blind large: NavMesh 위치를 찾지 못함");

            // ---- 개구리: 시작 지점에서 동쪽으로 이어지는 복도의 천장
            var ambush = new List<Transform>();
            var ambushRoot = new GameObject("Frog Ambush Points").transform; ambushRoot.SetParent(root, false);
            foreach (var p in new[] { new Vector3(-4, 8.1f, -28.6f), new Vector3(6, 8.1f, -28.6f), new Vector3(-20, 8.1f, -18) })
            {
                if (!NavMesh.SamplePosition(p, out var floor, 3f, NavMesh.AllAreas)) continue;
                if (!Physics.Raycast(floor.position + Vector3.up * 0.5f, Vector3.up, out var ceiling, 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var point = new GameObject("Frog Ambush " + (ambush.Count + 1)).transform;
                point.SetParent(ambushRoot, false);
                point.SetPositionAndRotation(ceiling.point, Quaternion.FromToRotation(Vector3.up, ceiling.normal));
                ambush.Add(point);
            }
            if (ambush.Count > 0)
            {
                var first = ambush[0].position;
                NavMesh.SamplePosition(first + Vector3.down * 2f, out var spawn, 6f, NavMesh.AllAreas);
                var frog = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FrogPrefab), root);
                frog.transform.SetPositionAndRotation(spawn.position, Quaternion.Euler(0, 270, 0));
                SetArray(frog.GetComponent<FrogMonsterAI>(), "ambushPoints", ambush);
                Network(frog);
                log.Add("frog under " + first.ToString("0.0") + " ambush=" + ambush.Count);
            }
            else log.Add("frog: 천장을 찾지 못함");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // 씬 오브젝트용 NetworkObject 해시는 씬이 저장된 뒤에야 확정된다 → 다시 계산하고 한 번 더 저장
            var validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags);
            foreach (var no in root.GetComponentsInChildren<NetworkObject>(true))
            {
                validate?.Invoke(no, null);
                EditorUtility.SetDirty(no);
                PrefabUtility.RecordPrefabInstancePropertyModifications(no);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            string result = string.Join(" | ", log);
            Debug.Log("[새 몬스터 배치] " + result);
            return result;
        }

        const System.Reflection.BindingFlags BindingFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        static void SetArray(Object target, string property, List<Transform> points)
        {
            var so = new SerializedObject(target);
            var array = so.FindProperty(property);
            array.arraySize = points.Count;
            for (int i = 0; i < points.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        /// <summary>기존 몬스터와 같은 멀티플레이 구성: 서버 권한 위치 동기화(좌우 회전만) + MonsterNetworkSync</summary>
        static void Network(GameObject monster)
        {
            if (monster.GetComponent<NetworkObject>() == null) monster.AddComponent<NetworkObject>();
            var nt = monster.GetComponent<NetworkTransform>() ?? monster.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            if (monster.GetComponent<MonsterNetworkSync>() == null) monster.AddComponent<MonsterNetworkSync>();
        }
    }
}
