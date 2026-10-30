/* [ 코드 설명 ] 기존 Player 테스트 씬에서 실제 Play Mode 검증을 실행하고 결과·렌더 이미지를 Player/Validation에 저장합니다. 종료 시 원래 열린 씬과 Play 시작 씬 설정을 복원합니다. */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static partial class FpsValidation
    {
        const string Pending = "VoidLast.Player.Validation.Pending";
        const string PreviousStart = "VoidLast.Player.Validation.PreviousStart";
        static IEnumerator checks;
        static StringBuilder currentReport;
        static int lastFrame;
        static bool previousBackground;
        static FpsValidation() => EditorApplication.playModeStateChanged += Changed;
        [MenuItem("VOID LAST/Player/3 - Run Play Mode Validation")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            SessionState.SetString(PreviousStart, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Pending, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(FpsAssetSetup.ScenePath);
            EditorApplication.EnterPlaymode();
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousStart, ""));
                SessionState.SetBool(Pending, false);
                AssetDatabase.Refresh();
            }
        }
        static void Require(bool value, string message, StringBuilder report)
        {
            report.AppendLine((value ? "PASS: " : "FAIL: ") + message);
            if (!value) throw new InvalidOperationException(message);
        }
        static void Advance(Animator animator, float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds * 60); i++) animator.Update(1f / 60);
        }
        static void Teleport(GameObject player, Vector3 position)
        {
            var c = player.GetComponent<CharacterController>(); c.enabled = false; player.transform.SetPositionAndRotation(position, Quaternion.identity); c.enabled = true; Physics.SyncTransforms();
        }
        static void Move(FpsMotor motor, Vector2 direction, bool sprint, bool crouch, int frames = 60)
        {
            for (int i = 0; i < frames; i++) { motor.Simulate(direction, sprint, crouch, 1f / 60); Physics.SyncTransforms(); }
        }
        static void Run()
        {
            currentReport = new StringBuilder("VOID LAST — Unity Play Mode validation\n" + DateTime.UtcNow.ToString("O") + "\nUnity " + Application.unityVersion + "\n\n");
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            lastFrame = -1;
            checks = RunChecks(currentReport);
            EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (checks.MoveNext()) return;
            }
            catch (Exception exception) { currentReport.AppendLine("\nFAILED: " + exception); Debug.LogException(exception); }
            EditorApplication.update -= Tick;
            File.WriteAllText("Assets/Player/Validation/PlayModeResults.txt", currentReport.ToString());
            checks = null;
            Application.runInBackground = previousBackground;
            EditorApplication.ExitPlaymode();
        }
        static IEnumerator RunChecks(StringBuilder report)
        {
            {
                Require(Application.isPlaying && SceneManager.GetActiveScene().path == FpsAssetSetup.ScenePath, "Running actual Character_test scene in Play Mode", report);
                var player = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "VOID_LAST_FPS");
                var motor = player.GetComponent<FpsMotor>(); var capsule = player.GetComponent<CharacterController>();
                var equipment = player.GetComponent<FpsEquipment>(); var animation = player.GetComponent<FpsAnimation>();
                var look = player.GetComponent<FpsLook>(); var animator = player.GetComponentInChildren<Animator>();
                foreach (var behaviour in player.GetComponents<MonoBehaviour>()) if (!(behaviour is FpsLocalVisibility)) behaviour.enabled = false;
                Require(animator.avatar.isHuman && animator.avatar.isValid && !animator.applyRootMotion, "Valid astronaut Humanoid; Apply Root Motion disabled", report);
                foreach (var t in player.GetComponentsInChildren<Transform>(true)) Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "No missing script: " + t.name, report);
                foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Require(renderer.sharedMesh != null && renderer.sharedMaterials.All(m => m != null), "Mesh/material references: " + renderer.name, report);
                foreach (string file in FpsAssetSetup.Files)
                {
                    var clip = FpsAssetSetup.Clip(file); var importer = (ModelImporter)AssetImporter.GetAtPath("Assets/Player/Animator/" + file + ".fbx");
                    var settings = importer.clipAnimations.Single();
                    Require(clip.humanMotion && settings.lockRootRotation && settings.lockRootHeightY && settings.lockRootPositionXZ, "Humanoid/root bake: " + file, report);
                    Require(settings.loopTime == (Array.IndexOf(FpsAssetSetup.Files, file) < 9 || file == "Pistol Idle"), "Loop setting: " + file, report);
                }
                Teleport(player, new Vector3(0, 0.1f, -6)); Move(motor, Vector2.zero, false, false);
                Require(capsule.isGrounded && Mathf.Abs(player.transform.position.y) < 0.06f, "Ground collision and gravity", report);
                var start = player.transform.position; Move(motor, Vector2.up, false, false); float walked = Vector3.Distance(start, player.transform.position);
                Require(Mathf.Abs(walked - 2.2f) < 0.1f, "Walk distance in one simulated second: " + walked.ToString("F3"), report);
                Teleport(player, new Vector3(0, 0.01f, -6)); start = player.transform.position; Move(motor, Vector2.up, true, false);
                Require(Vector3.Distance(start, player.transform.position) > walked * 1.8f && motor.IsRunning, "Shift sprint speed", report);
                Teleport(player, new Vector3(0, 0.01f, -6)); start = player.transform.position; Move(motor, Vector2.one, false, false);
                Require(Mathf.Abs(Vector3.Distance(start, player.transform.position) - walked) < 0.1f, "Diagonal input is normalized", report);
                Teleport(player, new Vector3(0, 0.01f, -6)); start = player.transform.position; Move(motor, Vector2.up, true, true);
                Require(motor.IsCrouching && !motor.IsRunning && Mathf.Abs(capsule.height - 1.15f) < 0.01f && Vector3.Distance(start, player.transform.position) < walked * 0.6f, "Crouch speed and capsule height", report);
                for (float elapsed = 0; elapsed < 0.5f; elapsed += Time.deltaTime) { look.SendMessage("LateUpdate"); yield return null; }
                Require(Mathf.Abs(player.transform.Find("CameraPivot").localPosition.y - 1.02f) < 0.05f, "Camera eases to crouched eye height: " + player.transform.Find("CameraPivot").localPosition.y, report);
                Teleport(player, new Vector3(0, 0.01f, 5)); Move(motor, Vector2.zero, false, false);
                Require(!motor.CanStand() && motor.IsCrouching && capsule.height < 1.16f, "Low ceiling blocks standing after Ctrl release", report);
                Teleport(player, new Vector3(0, 0.01f, -6)); Move(motor, Vector2.zero, false, false);
                Require(motor.CanStand() && !motor.IsCrouching && Mathf.Abs(capsule.height - 1.85f) < 0.01f, "Standing restores capsule in clear space", report);
                for (float elapsed = 0; elapsed < 0.5f; elapsed += Time.deltaTime) { look.SendMessage("LateUpdate"); yield return null; }
                Require(Mathf.Abs(player.transform.Find("CameraPivot").localPosition.y - 1.67f) < 0.05f, "Camera restores standing eye height: " + player.transform.Find("CameraPivot").localPosition.y, report);
                Teleport(player, new Vector3(3, 0.01f, 3)); Move(motor, Vector2.right, false, false, 120);
                Require(player.transform.position.x < 4.5f, "Wall blocks movement", report);
                equipment.Process(1, true, false); Require(equipment.IsAiming && (int)equipment.SelectedSlot == 1, "Pistol slot / right-button aim state", report);
                equipment.Process(1, false, false); Require(!equipment.IsAiming, "Aim release", report);
                equipment.Process(2, true, false); Require(!equipment.IsAiming && (int)equipment.SelectedSlot == 2, "Melee slot blocks pistol aim", report);
                equipment.Process(3, true, false); Require(!equipment.IsAiming && (int)equipment.SelectedSlot == 3, "Item slot selection only", report);
                look.Rotate(new Vector2(100, 10000)); var pivot = player.transform.Find("CameraPivot");
                Require(Mathf.Abs(Mathf.DeltaAngle(0, pivot.localEulerAngles.x) + 80) < 0.1f, "Mouse pitch upper clamp", report);
                look.Rotate(new Vector2(0, -20000)); Require(Mathf.Abs(Mathf.DeltaAngle(0, pivot.localEulerAngles.x) - 80) < 0.1f, "Mouse pitch lower clamp", report);
                Require(Mathf.Abs(player.transform.eulerAngles.y - 8) < 0.1f, "Mouse yaw sensitivity", report);
                equipment.Process(1, true, false); animation.enabled = true;
                for (float elapsed = 0; elapsed < 0.5f; elapsed += Time.deltaTime) yield return null;
                Require(animator.GetInteger("Slot") == 1 && animator.GetBool("Aim") && animator.GetLayerWeight(1) > 0.9f, "Runtime equipment-to-Animator integration", report);
                equipment.Process(2, false, true);
                for (float elapsed = 0; elapsed < 0.3f; elapsed += Time.deltaTime) yield return null;
                Require(animator.GetCurrentAnimatorStateInfo(1).IsTag("Melee") && animator.GetLayerWeight(1) > 0.5f, "Equipment attack event drives upper-body melee", report);
                equipment.Process(3, false, false);
                for (float elapsed = 0; elapsed < 0.5f; elapsed += Time.deltaTime) yield return null;
                Require(animator.GetInteger("Slot") == 3 && animator.GetLayerWeight(1) < 0.05f, "Item slot fades upper body out", report);
                animation.enabled = false;
                Teleport(player, new Vector3(0, 0.01f, -3)); pivot.localRotation = Quaternion.identity;
                animator.Rebind(); animator.SetInteger("Slot", 1); animator.SetLayerWeight(1, 1); Advance(animator, 1.7f);
                Require(animator.GetCurrentAnimatorStateInfo(1).IsName("Pistol Idle"), "Upper body Equip Pistol -> Pistol Idle", report);
                animator.SetFloat("Gait", 1); animator.SetFloat("MoveY", 1); animator.SetBool("Aim", true); Advance(animator, 0.6f);
                Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == FpsAssetSetup.Clip("Walking") && c.weight > 0.9f), "Forward blend tree selects Walking", report);
                Require(animator.GetCurrentAnimatorStateInfo(1).IsName("Pistol Idle"), "Pistol upper body remains active over walking (dedicated aim clip absent)", report);
                animator.speed = 0;
                for (int frame = 0; frame < 4; frame++) yield return null;
                Capture(player.GetComponentInChildren<Camera>(), "FPS_View.png");
                yield return null;
                CaptureObserver(player, "Pistol_Walk.png");
                animator.speed = 1;
                animator.SetFloat("MoveY", -1); Advance(animator, 0.3f);
                Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == FpsAssetSetup.Clip("Walking Backwards") && c.weight > 0.9f), "Backward blend tree", report);
                animator.SetFloat("MoveY", 0); animator.SetFloat("MoveX", -1); Advance(animator, 0.3f);
                Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == FpsAssetSetup.Clip("Left Strafe Walk") && c.weight > 0.9f), "Left strafe blend tree", report);
                animator.SetFloat("MoveX", 1); Advance(animator, 0.3f);
                Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == FpsAssetSetup.Clip("Right Strafe Walk") && c.weight > 0.9f), "Right strafe blend tree", report);
                animator.SetFloat("Gait", 2); animator.SetFloat("MoveX", 0); animator.SetFloat("MoveY", 1); Advance(animator, 0.3f);
                Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == FpsAssetSetup.Clip("Standard Run") && c.weight > 0.9f), "Run blend tree", report);
                animator.SetFloat("Gait", 0); animator.SetFloat("MoveY", 0); animator.SetBool("Crouch", true); Advance(animator, 1.1f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Crouched"), "Crouch Down -> Crouched", report);
                animator.speed = 0;
                for (int frame = 0; frame < 4; frame++) yield return null;
                CaptureObserver(player, "Pistol_Crouch.png");
                animator.speed = 1;
                animator.SetFloat("CrouchAmount", 1); animator.SetFloat("CrouchDirection", -1); Advance(animator, 0.3f);
                Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == FpsAssetSetup.Clip("Walk Crouching Backward") && c.weight > 0.9f), "Crouch backward blend tree", report);
                animator.SetBool("Crouch", false); animator.SetFloat("MoveY", 0); Advance(animator, 1.1f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Standing"), "Stand Up -> Standing", report);
                animator.SetInteger("Slot", 2); Advance(animator, 0.3f); animator.SetTrigger("Melee"); Advance(animator, 0.3f);
                Require(animator.GetCurrentAnimatorStateInfo(1).IsTag("Melee"), "Melee attack on upper body", report);
                Advance(animator, 1.2f); Require(!animator.GetCurrentAnimatorStateInfo(1).IsTag("Melee"), "Melee returns after one shot", report);
                var headRenderers = player.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("Helmet") || r.name.Contains("Face_")).ToArray();
                Require(headRenderers.All(r => !r.forceRenderingOff), "Local visibility restored after rendering; observer retains head/helmet", report);
                var featureChecks = CheckJumpAndWeapon(player, report);
                while (featureChecks.MoveNext()) yield return featureChecks.Current;
                var farmingChecks = CheckFarming(player, report);
                while (farmingChecks.MoveNext()) yield return farmingChecks.Current;
                player.GetComponent<FpsHealth>().TakeDamage(float.MaxValue); Advance(animator, 0.4f);
                animation.enabled = true;
                yield return null;
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Death") && motor.IsDead && animator.GetLayerWeight(1) == 0, "Death state, movement lock, upper body disabled", report);
                Require(animator.GetLayerWeight(2) == 0, "Death keeps lower-body Jump layer disabled", report);
                player.GetComponent<FpsWeaponView>().Refresh(0.1f);
                Require(!player.GetComponent<FpsWeaponView>().IsVisible, "Death hides mounted pistol", report);
                start = player.transform.position; Move(motor, Vector2.up, true, false); Require(Vector2.Distance(new Vector2(start.x, start.z), new Vector2(player.transform.position.x, player.transform.position.z)) < 0.01f, "No horizontal movement after death", report);
                report.AppendLine("\nALL AUTOMATED CHECKS PASSED. Physical keyboard/mouse feel, final weapon offsets and residual foot sliding require manual review.");
            }
        }
        static void CaptureObserver(GameObject player, string file, bool closeUp = false)
        {
            var go = new GameObject("Temporary validation observer");
            try
            {
                var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 45; camera.nearClipPlane = 0.05f;
                go.transform.position = player.transform.position + (closeUp ? new Vector3(0.95f, 1.55f, 1.45f) : new Vector3(2.6f, 1.6f, 3.2f));
                go.transform.LookAt(player.transform.position + Vector3.up * (closeUp ? 1.35f : 0.95f));
                Capture(camera, file);
            }
            finally { Object.DestroyImmediate(go); }
        }
        static void Capture(Camera camera, string file)
        {
            var rt = new RenderTexture(960, 720, 24); rt.Create();
            var texture = new Texture2D(960, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); texture.Apply();
                File.WriteAllBytes("Assets/Player/Validation/" + file, texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); rt.Release(); Object.DestroyImmediate(rt); }
        }
    }
}
