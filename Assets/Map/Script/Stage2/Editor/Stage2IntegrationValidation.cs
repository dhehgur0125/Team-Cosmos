/* [ 코드 설명 ] 실제 Stage2_Ship Play Mode에서 원본 FPS·몬스터·탐지·아이템과 UI 참조를 검사합니다. 자동 검증에서는 Vivox Start 전에 컴포넌트를 끄므로 로그인·마이크 송신을 하지 않습니다. 테스트 변경은 씬에 저장하지 않습니다. */
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
using UnityEngine.UIElements;
using VoidLast.Monster;
using VoidLast.Player;
using Object = UnityEngine.Object;

namespace VoidLast.Integration.Editor
{
    [InitializeOnLoad]
    public static class Stage2IntegrationValidation
    {
        const string Pending = "VoidLast.Stage2Integration.Validation";
        static IEnumerator checks;
        static StringBuilder report;
        static int frame;
        static bool background, suppressedVoice;
        public static string Progress { get; private set; }
        static Stage2IntegrationValidation() => EditorApplication.playModeStateChanged += Changed;
        [MenuItem("VOID LAST/Integration/2 - Validate Stage2 Play Mode (No Voice Transmission)")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            SessionState.SetString(Pending + ".Previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Pending, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Stage2IntegrationSetup.ScenePath);
            EditorApplication.EnterPlaymode();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Prepare()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            suppressedVoice = false;
            foreach (var voice in Object.FindObjectsByType<StandaloneRadioTest>()) { voice.enabled = false; suppressedVoice = true; }
            var player = Object.FindAnyObjectByType<FpsHealth>();
            if (player == null) return;
            player.GetComponent<FpsInput>().enabled = false;
            player.GetComponent<FpsMotor>().enabled = false;
            player.GetComponent<FpsLook>().enabled = false;
            player.GetComponent<FpsEquipment>().enabled = false;
            player.GetComponent<FpsInventory>().enabled = false;
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Pending, false);
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Pending + ".Previous", ""));
            }
        }
        static void Run()
        {
            background = Application.runInBackground; Application.runInBackground = true;
            report = new StringBuilder("VOID LAST Stage2 integration - actual Unity Play Mode\n" + DateTime.UtcNow.ToString("O") + "\n");
            frame = -1; checks = Checks(); EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (Time.frameCount == frame) return; frame = Time.frameCount;
            try { if (checks.MoveNext()) return; }
            catch (Exception e) { report.AppendLine("FAILED: " + e); Debug.LogException(e); }
            EditorApplication.update -= Tick; Application.runInBackground = background;
            Directory.CreateDirectory("Docs/IntegrationReports"); File.WriteAllText("Docs/IntegrationReports/PlayModeResults.txt", report.ToString()); EditorApplication.ExitPlaymode();
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
        static void Place(GameObject player, Vector3 position)
        {
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.SetPositionAndRotation(position, Quaternion.identity); cc.enabled = true; Physics.SyncTransforms();
        }
        static bool AimAtPickup(GameObject player, FpsWorldPickup pickup)
        {
            var target = pickup.GetComponentsInChildren<Collider>().First(c => !c.isTrigger).bounds.center;
            player.GetComponentInChildren<Camera>().transform.parent.LookAt(target); Physics.SyncTransforms();
            return player.GetComponent<FpsItemInteractor>().TryPickup();
        }
        static void EnableAI(bool enabled)
        {
            foreach (var ai in Object.FindObjectsByType<EyedMonsterAI>()) ai.enabled = enabled;
            foreach (var ai in Object.FindObjectsByType<BlindMonsterAI>()) ai.enabled = enabled;
            foreach (var ai in Object.FindObjectsByType<SmallMonsterAI>()) ai.enabled = enabled;
        }
        static bool PlaceInSight(GameObject player, Transform monster, MonsterVision vision)
        {
            foreach (float angle in new[] { 0f, -25f, 25f, -45f, 45f })
            {
                Vector3 candidate = monster.position + Quaternion.AngleAxis(angle, Vector3.up) * monster.forward * 4.5f;
                if (!NavMesh.SamplePosition(candidate, out var hit, 0.5f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - monster.position.y) > 0.3f) continue;
                var position = hit.position + Vector3.up * 0.03f;
                if (Physics.OverlapCapsule(position + Vector3.up * 0.32f, position + Vector3.up * 1.53f, 0.29f, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(player.transform))) continue;
                Place(player, position);
                if (vision.CanSee(player.GetComponent<FpsHealth>())) return true;
            }
            return false;
        }
        static IEnumerator Checks()
        {
            Check(Application.isPlaying && SceneManager.GetActiveScene().path == Stage2IntegrationSetup.ScenePath, "Stage2_Ship opened in actual Play Mode");
            Check(suppressedVoice && Object.FindAnyObjectByType<StandaloneRadioTest>() != null && !Object.FindAnyObjectByType<StandaloneRadioTest>().enabled, "Automated run suppressed Vivox Start before login/microphone transmission");
            var player = Object.FindAnyObjectByType<FpsHealth>().gameObject;
            var hp = player.GetComponent<FpsHealth>(); var motor = player.GetComponent<FpsMotor>(); var camera = player.GetComponentInChildren<Camera>();
            var start = player.transform.position;
            Check(Object.FindObjectsByType<FpsHealth>().Length == 1 && Object.FindObjectsByType<Camera>().Count(c => c.enabled) == 1 && Object.FindObjectsByType<AudioListener>().Count(a => a.enabled) == 1, "One FPS player, one active camera and one AudioListener");
            Check(Object.FindObjectsByType<MouseLook>().Length == 0 && Object.FindObjectsByType<PlayerMovement>().Length == 0 && Object.FindObjectsByType<Flashlight>().Length == 0, "No duplicate test movement/look scripts or F-key flashlight on integration player");
            Check(Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>().Length <= 1, "No duplicate EventSystem");
            var voice = Object.FindAnyObjectByType<StandaloneRadioTest>(); var doc = voice.GetComponent<UIDocument>();
            Check(voice.dummyPlayerTransform == player.transform && doc.panelSettings != null && doc.visualTreeAsset != null && voice.placeHudTopRight, "Existing VoiceChat transform, PanelSettings, UXML and separate HUD placement are connected");
            var tree = doc.visualTreeAsset.CloneTree(); Check(tree.Q<Label>("StatusLabel") != null && tree.Q<VisualElement>("MicMeter") != null && tree.Query<Label>().ToList().Any(l => l.text == "V"), "Voice UI loads and displays approved V radio key");

            var eyed = Object.FindAnyObjectByType<EyedMonsterAI>(); var blind = Object.FindAnyObjectByType<BlindMonsterAI>(); var small = Object.FindAnyObjectByType<SmallMonsterAI>();
            Check(eyed != null && blind != null && small != null && Object.FindObjectsByType<MonsterHealth>().Length == 3, "All three original monster types exist once");
            var agents = Object.FindObjectsByType<NavMeshAgent>();
            Check(agents.Length == 3 && agents.All(a => a.isOnNavMesh), "Three monster agents are attached to Stage2 navigation");
            var originalPositions = agents.Select(a => a.transform.position).ToArray();
            // Keep Camera.main available to existing map interaction scripts while hiding the AI target.
            var cameraParent = camera.transform.parent;
            camera.transform.SetParent(null, true);
            player.SetActive(false);
            float patrolEnds = Time.time + 4; bool clearance = true;
            while (Time.time < patrolEnds)
            {
                foreach (var agent in agents)
                {
                    var body = agent.GetComponent<CapsuleCollider>(); var center = body.transform.TransformPoint(body.center); float half = body.height * 0.5f - body.radius;
                    clearance &= !Physics.OverlapCapsule(center - Vector3.up * half, center + Vector3.up * half, body.radius - 0.02f, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(agent.transform));
                }
                yield return null;
            }
            for (int i = 0; i < agents.Length; i++) Check(Vector3.Distance(originalPositions[i], agents[i].transform.position) > 0.2f && agents[i].isOnNavMesh, agents[i].name + " patrol/search physically moves on NavMesh");
            Check(clearance, "Observed patrol paths keep all three physical capsules clear of ship walls/obstacles");
            foreach (var agent in agents) report.AppendLine("Navigation owner: " + agent.name + " -> " + (agent.navMeshOwner != null ? agent.navMeshOwner.name : "none"));
            EnableAI(false); player.SetActive(true); camera.transform.SetParent(cameraParent, true); Place(player, start);

            Check(PlaceInSight(player, eyed.transform, eyed.GetComponent<MonsterVision>()), "Found physically clear visible position for large-monster integration test");
            eyed.enabled = true; float beforeAttack = hp.Current;
            var wait = Wait(() => eyed.Target == hp, 2, "Large monster acquires existing FPS player through vision"); while (wait.MoveNext()) yield return wait.Current;
            wait = Wait(() => hp.Current < beforeAttack, 5, "Large monster navigates closer and lands its existing attack"); while (wait.MoveNext()) yield return wait.Current;
            Check(hp.Current == beforeAttack - eyed.GetComponent<MonsterMelee>().Damage, "Existing large-monster attack applies expected player HP damage in ship");
            eyed.enabled = false; eyed.GetComponent<MonsterMelee>().Cancel(); hp.Heal(1000);
            Check(PlaceInSight(player, small.transform, small.GetComponent<MonsterVision>()), "Found physically clear visible position for small-monster integration test");
            int screams = small.ScreamCount; small.enabled = true;
            wait = Wait(() => small.ScreamCount > screams, 2, "Small monster spots FPS and enters existing scream state"); while (wait.MoveNext()) yield return wait.Current;
            wait = Wait(() => small.State == SmallMonsterAI.Mode.Chase || small.State == SmallMonsterAI.Mode.Attack, 3, "Small monster resumes existing chase after scream"); while (wait.MoveNext()) yield return wait.Current;
            small.enabled = false; small.GetComponent<MonsterMelee>().Cancel(); hp.Heal(1000); Place(player, start);

            for (int i = 0; i < 20; i++) motor.Simulate(Vector2.zero, false, false, 1f / 60);
            Check(motor.IsGrounded && Mathf.Abs(player.transform.position.y - 8) < 0.15f, "Player rests on actual F2 floor without falling");
            float x = player.transform.position.x;
            for (int i = 0; i < 30; i++) motor.Simulate(Vector2.right, false, false, 1f / 60);
            Check(player.transform.position.x > x + 0.8f, "FPS CharacterController movement works in ship geometry");
            float ground = player.transform.position.y, peak = ground;
            motor.Simulate(Vector2.zero, false, false, 1f / 60, true);
            for (int i = 0; i < 90; i++) { motor.Simulate(Vector2.zero, false, false, 1f / 60); peak = Mathf.Max(peak, player.transform.position.y); }
            Check(peak > ground + 0.7f && motor.IsGrounded, "Existing jump launches and lands inside ship");
            for (int i = 0; i < 40; i++) motor.Simulate(Vector2.zero, false, true, 1f / 60);
            Check(motor.IsCrouching && player.GetComponent<CharacterController>().height < 1.3f, "Existing crouch shrinks CharacterController");
            for (int i = 0; i < 40; i++) motor.Simulate(Vector2.zero, false, false, 1f / 60);
            Check(!motor.IsCrouching && player.GetComponent<CharacterController>().height > 1.8f, "Standing restores capsule under ship ceiling");
            var look = player.GetComponent<FpsLook>(); var rotation = player.transform.rotation; look.Rotate(new Vector2(100, 50));
            Check(Quaternion.Angle(rotation, player.transform.rotation) > 1 && Mathf.Abs(camera.transform.parent.localEulerAngles.x) > 1, "Existing FPS yaw/pitch API works");
            Place(player, start); camera.transform.parent.localRotation = Quaternion.identity;

            var equipment = player.GetComponent<FpsEquipment>(); var pistol = player.GetComponent<FpsPistol>();
            equipment.Process(1, false, false); player.GetComponent<FpsWeaponView>().Refresh(1);
            Check(pistol.TryFire() && pistol.Magazine == 9 && pistol.HasLastHit, "Actual pistol fires a Raycast into ship geometry and spends one round");
            Check(pistol.TryReload(), "Existing reload starts");
            wait = Wait(() => !pistol.IsReloading, 3, "Timed reload finishes"); while (wait.MoveNext()) yield return wait.Current;
            Check(pistol.Magazine == 10 && pistol.Reserve == 29, "Reload preserves correct magazine/reserve transfer");
            var hud = player.GetComponent<FpsHud>(); Check(hud.HealthText.Contains("100"), "Existing health HUD has initialized values");
            foreach (int slot in new[] { 4, 5, 6 }) { equipment.Process(slot, false, false); Check(hud.HotbarAlpha > 0.9f, "Hotbar responds to slot " + slot); }

            var detector = player.GetComponent<ItemDetector>(); var pickups = Object.FindObjectsByType<FpsWorldPickup>().OrderBy(p => Vector3.Distance(p.transform.position, player.transform.position)).ToArray();
            Check(pickups.Length == 3 && pickups.All(p => p.GetComponent<DetectableItem>() != null) && detector.activateKey == KeyCode.Z && detector.sonarShader != null && detector.pingShader != null && detector.xrayShader != null, "Three original syringe prefabs are scannable; Z and all three shaders connected");
            blind.enabled = true; int heard = blind.ScansHeard; detector.ActivateDetector();
            Check(blind.ScansHeard == heard + 1, "Existing ItemDetector sends ScanNoise to BlindMonsterAI");
            Check(pickups.All(p => p.GetComponent<DetectableItem>().IsGlowing), "Existing detector schedules item glow for all nearby syringes");
            wait = Wait(() => blind.State == BlindMonsterAI.Mode.Approach || blind.State == BlindMonsterAI.Mode.Grope, 4, "Blind monster reacts after scan wave arrival"); while (wait.MoveNext()) yield return wait.Current;
            blind.enabled = false; Capture(camera, "Stage2_FPS_Scan.png");

            var inventory = player.GetComponent<FpsInventory>(); var interactor = player.GetComponent<FpsItemInteractor>();
            Check(AimAtPickup(player, pickups[0]) && inventory.ItemAt(0) == FarmingItemKind.Syringe, "Actual assisted interaction ray acquires nearby syringe through existing F action");
            hp.TakeDamage(60); equipment.Process(4, false, false); player.GetComponent<FpsItemView>().Refresh(1);
            Check(inventory.TryStartUse(), "Equipped syringe starts existing hold use");
            inventory.TickUse(1, true, false); inventory.TickUse(0.1f, false, false);
            Check(!inventory.IsUsing && inventory.ItemAt(0) == FarmingItemKind.Syringe && hp.Current == 40, "Releasing hold cancels without healing or consuming syringe");
            Check(inventory.TryStartUse(), "Cancelled syringe can be used again");
            inventory.TickUse(inventory.UseDuration, true, false);
            Check(hp.Current == 70 && inventory.ItemAt(0) == FarmingItemKind.None, "Full three-second use heals 30 and consumes one syringe");
            Check(AimAtPickup(player, pickups[1]), "Second existing syringe can be acquired");
            equipment.Process(4, false, false); player.transform.rotation = Quaternion.Euler(0, 90, 0); camera.transform.parent.localRotation = Quaternion.identity;
            Check(interactor.TryDrop() && interactor.LastDropped != null, "Existing Q action creates the original drop prefab");
            wait = Wait(() => interactor.LastDropped.GetComponent<DetectableItem>() != null, 1, "Small integration adapter makes newly dropped syringe detectable"); while (wait.MoveNext()) yield return wait.Current;
            Check(!interactor.LastDropped.TryCollect(inventory), "Drop delay prevents immediate self pickup");
            wait = Wait(() => detector.IsReady, 3, "Scan cooldown becomes ready"); while (wait.MoveNext()) yield return wait.Current;
            detector.ActivateDetector(); Check(interactor.LastDropped.GetComponent<DetectableItem>().IsGlowing, "Dropped item remains compatible with existing scan");
            hp.Heal(1000); Place(player, start); player.transform.rotation = Quaternion.Euler(0, 90, 0); camera.transform.parent.localRotation = Quaternion.identity;
            equipment.Process(1, false, false); player.GetComponent<FpsWeaponView>().Refresh(1);
            Capture(camera, "Stage2_FPS.png");
            report.AppendLine("\nALL INTEGRATION PLAY MODE CHECKS PASSED. Inputs driven through existing runtime APIs. Vivox authentication, live audio, multiple peers, full-map/stair traversal and manual controls were not tested.");
        }
        static void Capture(Camera camera, string file)
        {
            var rt = new RenderTexture(1280, 720, 24); rt.Create(); var image = new Texture2D(1280, 720, TextureFormat.RGB24, false); var previous = RenderTexture.active;
            try { RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); File.WriteAllBytes("Docs/IntegrationReports/" + file, image.EncodeToPNG()); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt); }
        }
    }
}
