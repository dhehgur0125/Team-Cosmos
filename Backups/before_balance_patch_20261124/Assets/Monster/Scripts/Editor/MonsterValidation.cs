/* [ 코드 설명 ] 실제 Monster_test Play Mode에서 NavMesh 이동·시야·대상 유지·공격 회피·수색·권총 10발 사망을 검사합니다. 임시 플레이어와 몬스터는 저장하지 않고 종료 시 원래 Play 시작 씬을 복원합니다. */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.Monster.Editor
{
    [InitializeOnLoad]
    public static class MonsterValidation
    {
        const string Pending = "VoidLast.Monster.Validation";
        static IEnumerator checks;
        static StringBuilder report;
        static int lastFrame;
        static bool background;
        static GameObject template;
        public static string Progress { get; private set; }
        static MonsterValidation() => EditorApplication.playModeStateChanged += Changed;
        [MenuItem("VOID LAST/Monster/2 - Run Play Mode Validation")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            SessionState.SetString(Pending + ".Previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Pending, true); EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MonsterPrototypeSetup.ScenePath); EditorApplication.EnterPlaymode();
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Pending, false); EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Pending + ".Previous", "")); AssetDatabase.Refresh();
            }
        }
        static void Run()
        {
            background = Application.runInBackground; Application.runInBackground = true;
            report = new StringBuilder("VOID LAST Monster actual Unity Play Mode validation\n" + DateTime.UtcNow.ToString("O") + "\n");
            lastFrame = -1; checks = RunChecks(); EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            try { if (checks.MoveNext()) return; }
            catch (Exception e) { report.AppendLine("FAILED: " + e); Debug.LogException(e); }
            EditorApplication.update -= Tick; Application.runInBackground = background;
            File.WriteAllText("Assets/Monster/Validation/PlayModeResults.txt", report.ToString()); EditorApplication.ExitPlaymode();
        }
        static void Check(bool condition, string message)
        {
            Progress = message; report.AppendLine((condition ? "PASS: " : "FAIL: ") + message);
            if (!condition) throw new InvalidOperationException(message);
        }
        static IEnumerator Wait(Func<bool> condition, float seconds, string message)
        {
            Progress = "Waiting: " + message; float end = Time.time + seconds;
            while (!condition() && Time.time < end) yield return null;
            Check(condition(), message);
        }
        static IEnumerator Delay(float seconds) { float end = Time.time + seconds; while (Time.time < end) yield return null; }
        static void PlacePlayer(GameObject player, Vector3 position)
        {
            var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false; player.transform.SetPositionAndRotation(position + Vector3.up * 0.03f, Quaternion.identity); capsule.enabled = true; Physics.SyncTransforms();
        }
        static void ControlPlayer(GameObject player, bool observer = false)
        {
            player.GetComponent<FpsInput>().enabled = false; player.GetComponent<FpsMotor>().enabled = false; player.GetComponent<FpsLook>().enabled = false; player.GetComponent<FpsEquipment>().enabled = false;
            if (observer) { player.GetComponentInChildren<Camera>().enabled = false; player.GetComponentInChildren<AudioListener>().enabled = false; player.transform.Find("FPS HUD").gameObject.SetActive(false); }
        }
        static GameObject Spawn(Vector3 position, float yaw = 0, bool stationary = false)
        {
            var go = Object.Instantiate(template, position + Vector3.up * 0.02f, Quaternion.Euler(0, yaw, 0)); go.name = "Validation Monster";
            if (stationary) { var fields = new SerializedObject(go.GetComponent<EyedMonsterAI>()); fields.FindProperty("patrolPoints").arraySize = 0; fields.ApplyModifiedPropertiesWithoutUndo(); }
            go.SetActive(true); go.GetComponent<NavMeshAgent>().Warp(position); Physics.SyncTransforms(); return go;
        }
        static bool Shoot(GameObject player, Vector3 point)
        {
            var camera = player.GetComponentInChildren<Camera>(true); camera.transform.parent.LookAt(point);
            player.GetComponent<FpsEquipment>().Process(1, false, false); player.GetComponent<FpsWeaponView>().Refresh(1); Physics.SyncTransforms();
            return player.GetComponent<FpsPistol>().TryFire();
        }
        static IEnumerator RunChecks()
        {
            Check(Application.isPlaying && SceneManager.GetActiveScene().path == MonsterPrototypeSetup.ScenePath, "Running actual Monster_test in Play Mode");
            template = Object.FindAnyObjectByType<EyedMonsterAI>().gameObject; template.SetActive(false);
            var player = Object.FindAnyObjectByType<FpsHealth>().gameObject; ControlPlayer(player);
            var hp = player.GetComponent<FpsHealth>(); var pistol = player.GetComponent<FpsPistol>();
            Check(NavMesh.CalculateTriangulation().indices.Length > 0 && template.GetComponent<MonsterHealth>().Maximum == pistol.Damage * 10, "Baked NavMesh exists and HP equals ten real pistol hits");
            var lungeChecks = RunLungeChecks(player); while (lungeChecks.MoveNext()) yield return lungeChecks.Current;
            player.SetActive(false);
            var monster = Spawn(new Vector3(0, 0, 5)); var ai = monster.GetComponent<EyedMonsterAI>();
            Check(ai.NavigationReady, "Large capsule is on the baked NavMesh");
            var waiting = Wait(() => ai.PatrolDestinations >= 3, 32, "Random patrol reaches multiple designated destinations with waits"); while (waiting.MoveNext()) yield return waiting.Current;
            Capture("Monster_Overview.png"); Object.Destroy(monster); yield return null;
            monster = Spawn(new Vector3(-6, 0, 6)); ai = monster.GetComponent<EyedMonsterAI>(); ai.enabled = false;
            var agent = monster.GetComponent<NavMeshAgent>(); var path = new NavMeshPath();
            Check(agent.CalculatePath(new Vector3(0, 0, 6), path) && path.status == NavMeshPathStatus.PathComplete && path.corners.Length >= 3, "Path from closed room to hall bends around wall through doorway");
            agent.isStopped = false; agent.speed = 4.5f; agent.SetPath(path); bool passedDoor = false, clearOfWalls = true; float deadline = Time.time + 12;
            while (Vector3.Distance(monster.transform.position, new Vector3(0, 0, 6)) > 0.4f && Time.time < deadline)
            {
                var pos = monster.transform.position;
                if (Mathf.Abs(pos.x + 3) < 0.8f && pos.z > 0.5f && pos.z < 2.5f) passedDoor = true;
                var overlap = Physics.OverlapCapsule(pos + Vector3.up * 0.72f, pos + Vector3.up * 2.08f, 0.7f).FirstOrDefault(c => !c.transform.IsChildOf(monster.transform));
                if (overlap != null && clearOfWalls) report.AppendLine("Overlap diagnostic: " + overlap.name + " at " + pos.ToString("F3") + " bounds=" + overlap.bounds);
                clearOfWalls &= overlap == null;
                yield return null;
            }
            Check(passedDoor && Vector3.Distance(monster.transform.position, new Vector3(0, 0, 6)) < 0.4f, "Agent physically traverses doorway instead of crossing wall");
            Check(clearOfWalls, "NavMesh moving capsule stays clear of solid walls");
            agent.isStopped = true; agent.ResetPath(); monster.transform.rotation = Quaternion.identity;
            player.SetActive(true); PlacePlayer(player, new Vector3(0, 0, 12)); var vision = monster.GetComponent<MonsterVision>();
            Check(vision.CanSee(hp), "Player in forward 120-degree/15m view is visible");
            var capsule = player.GetComponent<CharacterController>(); capsule.height = 1.15f; capsule.center = Vector3.up * 0.575f; Physics.SyncTransforms();
            Check(vision.CanSee(hp), "Crouching remains visible without cover"); capsule.height = 1.85f; capsule.center = Vector3.up * 0.925f;
            PlacePlayer(player, new Vector3(-7, 0, 6)); Check(!vision.CanSee(hp), "Room wall blocks vision");
            PlacePlayer(player, new Vector3(0, 0, 0)); Check(!vision.CanSee(hp), "Player behind monster is outside field of view");
            PlacePlayer(player, new Vector3(0, 0, -13)); Check(!vision.CanSee(hp), "Distant player outside 15m is not detected");
            Object.Destroy(monster); yield return null;

            monster = Spawn(new Vector3(0, 0, 2), 0, true); ai = monster.GetComponent<EyedMonsterAI>(); PlacePlayer(player, new Vector3(0, 0, -6));
            Check(Shoot(player, player.transform.position + Vector3.up * 50), "Real pistol can fire a miss");
            var delay = Delay(0.3f); while (delay.MoveNext()) yield return delay.Current;
            Check(ai.State == EyedMonsterAI.Mode.Patrol && ai.Target == null && monster.GetComponent<MonsterHealth>().Current == 300, "Gunshot/miss behind monster does not trigger hearing or damage");
            Check(Shoot(player, monster.GetComponent<Collider>().bounds.center), "Real Raycast hits monster from behind");
            Check(ai.Target == hp && ai.State == EyedMonsterAI.Mode.Chase && monster.GetComponent<MonsterHealth>().Current == 270, "Direct hit identifies visible attacker and starts chase");
            Object.Destroy(monster); yield return null;

            monster = Spawn(new Vector3(0, 0, 8), 180, true); ai = monster.GetComponent<EyedMonsterAI>(); PlacePlayer(player, Vector3.zero);
            waiting = Wait(() => ai.Target == hp, 1, "First visible player becomes chase target"); while (waiting.MoveNext()) yield return waiting.Current;
            var other = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrototypeSetup.PlayerPath)); ControlPlayer(other, true); PlacePlayer(other, new Vector3(1, 0, -1));
            delay = Delay(0.2f); while (delay.MoveNext()) yield return delay.Current;
            Check(ai.Target == hp && Shoot(other, monster.GetComponent<Collider>().bounds.center) && ai.Target == hp, "Second visible player and second shooter do not steal visible target");
            PlacePlayer(player, new Vector3(-7, 0, 6)); PlacePlayer(other, new Vector3(0, 0, 1));
            waiting = Wait(() => ai.Target == other.GetComponent<FpsHealth>(), 1, "Losing old target allows switching to another visible player"); while (waiting.MoveNext()) yield return waiting.Current;
            other.GetComponent<FpsHealth>().TakeDamage(1000);
            waiting = Wait(() => ai.Target != other.GetComponent<FpsHealth>(), 1, "Dead chase target is discarded"); while (waiting.MoveNext()) yield return waiting.Current;
            Object.Destroy(other); Object.Destroy(monster); yield return null;

            monster = Spawn(new Vector3(0, 0, 6), 0, true); ai = monster.GetComponent<EyedMonsterAI>(); PlacePlayer(player, new Vector3(-7, 0, 6));
            delay = Delay(0.25f); while (delay.MoveNext()) yield return delay.Current;
            Check(Shoot(player, monster.GetComponent<Collider>().bounds.center) && monster.GetComponent<MonsterHealth>().Current == 300 && ai.Target == null, "Wall blocks actual bullet damage and detection");
            Vector3 hiddenShot = player.transform.position;
            monster.GetComponent<MonsterHealth>().TakeHit(30, player, monster.transform.position); // Explicitly test unseen-attacker metadata, not a through-wall bullet.
            Check(ai.State == EyedMonsterAI.Mode.Investigate && ai.Target == null && ai.LastKnownPosition == hiddenShot, "Unseen-hit reaction investigates only attack-time position (damage API case)");
            PlacePlayer(player, new Vector3(-9, 0, 9));
            delay = Delay(0.3f); while (delay.MoveNext()) yield return delay.Current;
            Check(ai.LastKnownPosition == hiddenShot, "Hidden attacker movement does not update remembered position");
            Object.Destroy(monster); yield return null;

            monster = Spawn(new Vector3(0, 0, 6), 180); ai = monster.GetComponent<EyedMonsterAI>(); PlacePlayer(player, new Vector3(0, 0, -1));
            waiting = Wait(() => ai.Target == hp, 1, "Search scenario acquires visible player"); while (waiting.MoveNext()) yield return waiting.Current;
            Vector3 memory = ai.LastKnownPosition; PlacePlayer(player, new Vector3(-7, 0, 6));
            waiting = Wait(() => ai.State == EyedMonsterAI.Mode.Investigate, 1, "Breaking sight starts last-seen-position investigation"); while (waiting.MoveNext()) yield return waiting.Current;
            Check(ai.LastKnownPosition == memory, "Investigation uses last sighting rather than hidden room position"); player.SetActive(false);
            waiting = Wait(() => ai.State == EyedMonsterAI.Mode.Search, 12, "Monster reaches remembered location before local search"); while (waiting.MoveNext()) yield return waiting.Current;
            float searchedAt = Time.time;
            waiting = Wait(() => ai.SearchDestinations > 0, 2, "Search chooses a reachable authored room/corridor point"); while (waiting.MoveNext()) yield return waiting.Current;
            Check(ai.Reachable(ai.Destination, out _), "Chosen search destination has a complete NavMesh path"); Capture("Monster_Search.png");
            waiting = Wait(() => ai.State == EyedMonsterAI.Mode.Return, 12, "Search timeout starts return to patrol region"); while (waiting.MoveNext()) yield return waiting.Current;
            Check(Time.time - searchedAt >= 9.7f, "Local search lasts approximately ten seconds");
            waiting = Wait(() => ai.State == EyedMonsterAI.Mode.Patrol, 18, "Unsuccessful search returns to original patrol points"); while (waiting.MoveNext()) yield return waiting.Current;
            Object.Destroy(monster); yield return null; player.SetActive(true); hp.Heal(1000);

            // Fresh shooter validates the actual 10-round magazine against actual Raycast damage.
            var shooter = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrototypeSetup.PlayerPath)); ControlPlayer(shooter, true); PlacePlayer(shooter, new Vector3(0, 0, -6)); player.SetActive(false);
            monster = Spawn(new Vector3(0, 0, 6), 180, true); var monsterHealth = monster.GetComponent<MonsterHealth>(); var gun = shooter.GetComponent<FpsPistol>();
            for (int shot = 1; shot <= 10; shot++)
            {
                Check(Shoot(shooter, monster.GetComponent<Collider>().bounds.center), "Actual pistol hit " + shot);
                Check(monsterHealth.Current == 300 - shot * gun.Damage && monsterHealth.IsDead == (shot == 10), "HP/death threshold after shot " + shot);
                delay = Delay(gun.ShotInterval + 0.02f); while (delay.MoveNext()) yield return delay.Current;
            }
            Check(gun.Magazine == 0 && !gun.TryFire() && gun.TryReload(), "Ten hits empty magazine; empty fire is blocked and reload starts");
            waiting = Wait(() => !gun.IsReloading, 2, "Existing timed pistol reload completes"); while (waiting.MoveNext()) yield return waiting.Current;
            Check(gun.Magazine == 10 && gun.Reserve == 20, "Existing ammo transfer remains 0/30 to 10/20"); Object.Destroy(shooter); Object.Destroy(monster); yield return null;
            player.SetActive(true); PlacePlayer(player, new Vector3(0, 0, -13)); hp.Heal(1000);
            var motor = player.GetComponent<FpsMotor>(); Vector3 start = player.transform.position;
            for (int i = 0; i < 60; i++) motor.Simulate(Vector2.up, false, false, 1f / 60);
            Check(player.transform.position.z > start.z + 2.1f, "Existing CharacterController movement still works");
            PlacePlayer(player, Vector3.zero); monster = Spawn(new Vector3(0, 0, 1.4f), 180, true); monster.GetComponent<EyedMonsterAI>().enabled = false;
            player.GetComponentInChildren<Camera>().transform.parent.LookAt(monster.GetComponent<Collider>().bounds.center);
            player.GetComponent<FpsEquipment>().Process(2, false, true);
            delay = Delay(0.4f); while (delay.MoveNext()) yield return delay.Current;
            Check(monster.GetComponent<MonsterHealth>().Current == 280, "Existing player melee event applies prototype-only 20 damage through test-scene bridge");
            report.AppendLine("\nALL MONSTER AUTOMATED CHECKS PASSED. Inputs were driven through runtime APIs; physical controls and final balance require manual play.");
        }
        static IEnumerator RunLungeChecks(GameObject player)
        {
            var hp = player.GetComponent<FpsHealth>(); var motor = player.GetComponent<FpsMotor>();
            var monster = Spawn(Vector3.zero, 0, true); monster.GetComponent<EyedMonsterAI>().enabled = false;
            var melee = monster.GetComponent<MonsterMelee>(); var agent = monster.GetComponent<NavMeshAgent>();
            Check(Mathf.Approximately(melee.Windup, 0.3f) && Mathf.Approximately(melee.AttackDistance, 2.5f) && Mathf.Approximately(melee.LungeDuration, 0.2f) && Mathf.Approximately(melee.LungeDistance, 1.5f) && Mathf.Approximately(melee.RecoveryDuration, 0.8f), "Prefab contains requested lunge timings and distances");
            PlacePlayer(player, new Vector3(0, 0, 2.45f)); player.transform.rotation = Quaternion.Euler(0, 180, 0);
            Check(melee.TryStart(hp), "Attack starts at 2.45m, within new 2.5m trigger range");
            float started = Time.time, strikeAt = -1, recoveryAt = -1; Vector3 lungeEnd = Vector3.zero;
            while (melee.IsBusy && Time.time - started < 2)
            {
                // Actual S movement, looking toward the monster: continuous 2.2m/s retreat.
                motor.Simulate(Vector2.down, false, false, Time.deltaTime);
                if (melee.Phase == MonsterMelee.AttackPhase.Strike && strikeAt < 0) strikeAt = Time.time;
                if (melee.Phase == MonsterMelee.AttackPhase.Recovery && recoveryAt < 0) { recoveryAt = Time.time; lungeEnd = monster.transform.position; }
                yield return null;
            }
            report.AppendLine("Lunge metrics: strike=" + (strikeAt - started).ToString("F3") + "s recovery=" + (recoveryAt - started).ToString("F3") + "s end=" + (Time.time - started).ToString("F3") + "s travel=" + lungeEnd.z.ToString("F3") + "m HP=" + hp.Current);
            Check(hp.Current == 70 && melee.PlayersHit == 1, "Continuous backward walking is hit for exactly 30, once");
            Check(Mathf.Abs(lungeEnd.z - 1.5f) < 0.06f && Mathf.Abs(lungeEnd.x) < 0.01f, "Unobstructed lunge advances 1.5m along the locked direction");
            Check(strikeAt - started >= 0.28f && strikeAt - started < 0.38f && recoveryAt - strikeAt > 0.14f && recoveryAt - strikeAt < 0.27f && Time.time - recoveryAt >= 0.75f, "Measured windup/lunge/recovery match approximately 0.3/0.2/0.8 seconds");
            Check(agent.updatePosition && agent.updateRotation && Vector3.Distance(agent.nextPosition, monster.transform.position) < 0.02f, "Attack returns synchronized NavMesh position and rotation control");
            Object.Destroy(monster); yield return null; hp.Heal(1000);

            monster = Spawn(Vector3.zero, 0, true); monster.GetComponent<EyedMonsterAI>().enabled = false; melee = monster.GetComponent<MonsterMelee>();
            PlacePlayer(player, new Vector3(0, 0, 2.45f)); Check(melee.TryStart(hp), "Side dodge begins against a committed attack");
            var wait = Wait(() => melee.DirectionLocked, 0.4f, "Direction locks before forward movement"); while (wait.MoveNext()) yield return wait.Current;
            Quaternion locked = monster.transform.rotation;
            // Existing motor only sprints forward: turn to the side, then W+Shift. No teleport dodge.
            player.transform.rotation = Quaternion.Euler(0, 90, 0); started = Time.time;
            while (melee.IsBusy && Time.time - started < 2) { motor.Simulate(Vector2.up, true, false, Time.deltaTime); yield return null; }
            Check(hp.Current == 100 && player.transform.position.x > 1, "Timed lateral run using the real CharacterController dodges the attack");
            Check(Quaternion.Angle(locked, monster.transform.rotation) < 0.1f && Mathf.Abs(monster.transform.position.x) < 0.01f, "Lunge never rotates or curves after the player dodges sideways");
            Object.Destroy(monster); yield return null;

            monster = Spawn(Vector3.zero, 0, true); monster.GetComponent<EyedMonsterAI>().enabled = false; melee = monster.GetComponent<MonsterMelee>();
            PlacePlayer(player, new Vector3(0, 0, 2.45f));
            var extra = new GameObject("Temporary extra player collider", typeof(BoxCollider)); extra.transform.SetParent(player.transform, false); extra.transform.localPosition = Vector3.up; Physics.SyncTransforms();
            Check(melee.TryStart(hp), "Stationary target with multiple colliders starts attack");
            wait = Wait(() => !melee.IsBusy, 2, "Repeated hit-volume checks finish recovery"); while (wait.MoveNext()) yield return wait.Current;
            Check(hp.Current == 70 && melee.PlayersHit == 1, "Multiple colliders and repeated strike frames still deal only 30");
            Object.Destroy(extra); Object.Destroy(monster); yield return null; hp.Heal(1000);

            monster = Spawn(Vector3.zero, 0, true); monster.GetComponent<EyedMonsterAI>().enabled = false; melee = monster.GetComponent<MonsterMelee>();
            PlacePlayer(player, new Vector3(0, 0, 2.45f)); Check(melee.TryStart(hp), "Wall scenario starts while target is initially visible");
            wait = Wait(() => melee.DirectionLocked, 0.4f, "Wall scenario commits direction"); while (wait.MoveNext()) yield return wait.Current;
            var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube); barrier.name = "Temporary thin unbaked wall"; barrier.transform.position = new Vector3(0, 1.5f, 1.2f); barrier.transform.localScale = new Vector3(3, 3, 0.05f); Physics.SyncTransforms();
            wait = Wait(() => !melee.IsBusy, 2, "Lunge against thin unbaked obstacle finishes"); while (wait.MoveNext()) yield return wait.Current;
            Check(monster.transform.position.z < 0.5f && monster.transform.position.z >= 0 && hp.Current == 100, "Capsule sweep stops at thin wall; no body penetration or damage through wall");
            Object.Destroy(barrier); Object.Destroy(monster); yield return null;

            monster = Spawn(new Vector3(0, 0, 14.2f), 0, true); monster.GetComponent<EyedMonsterAI>().enabled = false; melee = monster.GetComponent<MonsterMelee>();
            PlacePlayer(player, new Vector3(0, 0, 15.35f)); Check(melee.TryStart(hp), "Navigation-edge attack starts on valid mesh");
            wait = Wait(() => melee.DirectionLocked, 0.4f, "Navigation-edge attack commits"); while (wait.MoveNext()) yield return wait.Current;
            player.SetActive(false);
            wait = Wait(() => !melee.IsBusy, 2, "Navigation-edge attack finishes safely"); while (wait.MoveNext()) yield return wait.Current;
            Check(monster.transform.position.z < 15.05f && monster.GetComponent<NavMeshAgent>().isOnNavMesh, "NavMesh boundary truncates straight lunge before leaving the walkable surface");
            Object.Destroy(monster); yield return null; player.SetActive(true);

            monster = Spawn(Vector3.zero, 0, true); var ai = monster.GetComponent<EyedMonsterAI>(); melee = monster.GetComponent<MonsterMelee>();
            PlacePlayer(player, new Vector3(0, 0, 2.45f));
            wait = Wait(() => melee.IsBusy, 1, "Live AI starts lunge automatically"); while (wait.MoveNext()) yield return wait.Current;
            PlacePlayer(player, new Vector3(0, 0, 8));
            wait = Wait(() => melee.Phase == MonsterMelee.AttackPhase.Recovery, 1, "Live AI enters stationary recovery"); while (wait.MoveNext()) yield return wait.Current;
            Vector3 stopped = monster.transform.position; float recoverStart = Time.time; bool stayed = true;
            Capture("Monster_Windup.png");
            while (melee.IsBusy && Time.time - recoverStart < 1.2f) { stayed &= Vector3.Distance(stopped, monster.transform.position) < 0.02f; yield return null; }
            Check(stayed && Time.time - recoverStart >= 0.75f, "AI does not resume chase during the 0.8-second recovery");
            wait = Wait(() => ai.State == EyedMonsterAI.Mode.Chase && monster.transform.position.z > stopped.z + 0.15f, 1, "AI resumes normal NavMesh chase after recovery"); while (wait.MoveNext()) yield return wait.Current;
            Object.Destroy(monster); yield return null;

            foreach (bool duringLunge in new[] { false, true })
            {
                monster = Spawn(Vector3.zero, 0, true); ai = monster.GetComponent<EyedMonsterAI>(); melee = monster.GetComponent<MonsterMelee>();
                PlacePlayer(player, new Vector3(0, 0, 2.45f));
                wait = Wait(() => duringLunge ? melee.Phase == MonsterMelee.AttackPhase.Strike : melee.IsBusy, 1, duringLunge ? "Death case reaches moving lunge" : "Death case reaches windup"); while (wait.MoveNext()) yield return wait.Current;
                Vector3 diedAt = monster.transform.position; float before = hp.Current;
                monster.GetComponent<MonsterHealth>().TakeHit(1000, player, diedAt);
                var delay = Delay(1); while (delay.MoveNext()) yield return delay.Current;
                Check(ai.State == EyedMonsterAI.Mode.Dead && !melee.IsBusy && !monster.GetComponent<NavMeshAgent>().enabled && hp.Current == before && Vector3.Distance(diedAt, monster.transform.position) < 0.01f, duringLunge ? "Death during lunge immediately cancels movement and later damage" : "Death during windup cancels lunge and damage");
                if (duringLunge) Capture("Monster_Death.png"); Object.Destroy(monster); yield return null;
            }
            hp.Heal(1000);
        }
        static void Capture(string file)
        {
            var go = new GameObject("Temporary monster observer", typeof(Camera)); var camera = go.GetComponent<Camera>(); camera.enabled = false;
            go.transform.position = new Vector3(20, 27, -23); go.transform.LookAt(new Vector3(0, 0, 1)); camera.fieldOfView = 55;
            var rt = new RenderTexture(960, 720, 24); rt.Create(); var image = new Texture2D(960, 720, TextureFormat.RGB24, false); var previous = RenderTexture.active;
            try { RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); image.Apply(); File.WriteAllBytes("Assets/Monster/Validation/" + file, image.EncodeToPNG()); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); }
        }
    }
}
