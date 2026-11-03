/* [ 코드 설명 ] 기존 Character_test 씬에서 크로스헤어 상태 전환·중앙 정렬·로컬 표시만 검사합니다. 플레이어 상태는 기존 API로 구동하며 씬과 네트워크 시스템을 저장하거나 변경하지 않습니다. */
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
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static class FpsCrosshairValidation
    {
        const string Pending = "VoidLast.Crosshair.Validation";
        const string Output = "Assets/Player/Validation";
        static IEnumerator checks;
        static StringBuilder report;
        static int frame;
        static bool background;
        static FpsCrosshairValidation() => EditorApplication.playModeStateChanged += Changed;

        [MenuItem("VOID LAST/Player/Validate Crosshair Only")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            SessionState.SetString(Pending + ".Start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Pending, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Player/Character_test.unity");
            EditorApplication.EnterPlaymode();
        }
        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Pending + ".Start", ""));
                SessionState.SetBool(Pending, false);
            }
        }
        static void Run()
        {
            Directory.CreateDirectory(Output);
            report = new StringBuilder("VOID LAST crosshair-only Play Mode checks\n" + DateTime.UtcNow.ToString("O") + "\n");
            background = Application.runInBackground; Application.runInBackground = true;
            frame = -1; checks = Checks(); EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (frame == Time.frameCount) return; frame = Time.frameCount;
            try { if (checks.MoveNext()) return; }
            catch (Exception e) { report.AppendLine("FAILED: " + e); Debug.LogException(e); }
            EditorApplication.update -= Tick;
            Application.runInBackground = background;
            File.WriteAllText(Output + "/CrosshairResults.txt", report.ToString());
            EditorApplication.ExitPlaymode();
        }
        static void Check(bool pass, string message)
        {
            report.AppendLine((pass ? "PASS: " : "FAIL: ") + message);
            if (!pass) throw new InvalidOperationException(message);
        }
        static IEnumerator Settle()
        {
            float until = Time.unscaledTime + 0.65f;
            while (Time.unscaledTime < until) yield return null;
        }
        static float Gap(Image[] lines) => Mathf.Abs(lines[0].rectTransform.anchoredPosition.y) - lines[0].rectTransform.sizeDelta.y * 0.5f;
        static void Move(FpsMotor motor, Vector2 input, bool run = false, bool crouch = false, int count = 1)
        {
            for (int i = 0; i < count; i++) motor.Simulate(input, run, crouch, 1f / 60);
        }
        static IEnumerator Checks()
        {
            Check(SceneManager.GetActiveScene().path == "Assets/Player/Character_test.unity", "Existing Character_test scene in actual Play Mode");
            var player = Object.FindAnyObjectByType<FpsMotor>().gameObject;
            var motor = player.GetComponent<FpsMotor>(); var equip = player.GetComponent<FpsEquipment>();
            var hud = player.GetComponent<FpsHud>(); var pistol = player.GetComponent<FpsPistol>(); var hp = player.GetComponent<FpsHealth>();
            int ammo = pistol.Magazine, reserve = pistol.Reserve; float health = hp.Current;
            player.GetComponent<FpsInput>().enabled = false;
            player.GetComponent<FpsLook>().enabled = false;
            motor.enabled = equip.enabled = false;
            var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(0, 0.02f, -6), Quaternion.identity);
            capsule.enabled = true; Physics.SyncTransforms();
            var cross = player.GetComponentInChildren<FpsCrosshair>(true);
            Check(cross != null, "Crosshair inherited from existing FPS prefab");
            var dot = cross.GetComponent<Image>();
            var lines = cross.GetComponentsInChildren<Image>().Where(i => i != dot).ToArray();
            Check(lines.Length == 4 && lines.All(i => !i.raycastTarget), "Four non-interactive crosshair arms");
            Move(motor, Vector2.zero, count: 30); equip.Process(1, false, false);
            var wait = Settle(); while (wait.MoveNext()) yield return null;
            Check(motor.IsGrounded && Mathf.Abs(Gap(lines) - 6) < 0.15f && lines.All(i => i.enabled) && !dot.enabled, "Idle pistol: four arms, gap 6, no center dot");
            Move(motor, Vector2.up); yield return null; yield return null;
            Check(Gap(lines) > 6 && Gap(lines) < 10, "Walk gap transitions smoothly instead of snapping");
            wait = Settle(); while (wait.MoveNext()) yield return null;
            Check(motor.LocalVelocity.y > 2 && Mathf.Abs(Gap(lines) - 10) < 0.15f, "Walking gap 10 from actual motor velocity");
            Move(motor, Vector2.up, run: true);
            wait = Settle(); while (wait.MoveNext()) yield return null;
            Check(motor.IsRunning && Mathf.Abs(Gap(lines) - 16) < 0.15f, "Running gap 16");
            Move(motor, Vector2.up, crouch: true, count: 20);
            wait = Settle(); while (wait.MoveNext()) yield return null;
            Check(motor.IsCrouching && Mathf.Abs(Gap(lines) - 3) < 0.15f, "Moving while crouched uses narrow gap 3");
            Move(motor, Vector2.zero, crouch: true);
            wait = Settle(); while (wait.MoveNext()) yield return null;
            Check(Mathf.Abs(Gap(lines) - 3) < 0.15f, "Stationary crouch retains narrow gap");
            Move(motor, Vector2.zero, count: 30);
            motor.Simulate(Vector2.zero, false, false, 1f / 60, true);
            wait = Settle(); while (wait.MoveNext()) yield return null;
            Check(motor.IsAirborne && Mathf.Abs(Gap(lines) - 22) < 0.15f, "Real motor jump/airborne state widens gap to 22");
            Move(motor, Vector2.zero, count: 100);
            wait = Settle(); while (wait.MoveNext()) yield return null;
            Check(motor.IsGrounded && Mathf.Abs(Gap(lines) - 6) < 0.15f, "Landing returns to idle gap");
            equip.Process(1, true, false); yield return null; yield return null;
            Check(equip.IsAiming && !dot.enabled && lines.All(i => !i.enabled), "ADS hides all crosshair graphics");
            equip.Process(1, false, false); yield return null; yield return null;
            Check(lines.All(i => i.enabled) && !dot.enabled, "Aim release restores pistol crosshair");
            foreach (int slot in new[] { 2, 3, 4, 5, 6 })
            {
                equip.Process(slot, false, false); yield return null; yield return null;
                Check(dot.enabled && lines.All(i => !i.enabled), "Slot " + slot + " retains original interaction dot");
            }
            equip.Process(1, false, false);
            motor.SetNetworkProxy(true); yield return null; yield return null;
            Check(!dot.enabled && lines.All(i => !i.enabled), "Remote motor state cannot show crosshair");
            motor.SetNetworkProxy(false); equip.IsNetworkProxy = true; yield return null; yield return null;
            Check(!dot.enabled && lines.All(i => !i.enabled), "Remote equipment state cannot show crosshair");
            equip.IsNetworkProxy = false; hud.enabled = false; yield return null; yield return null;
            Check(!dot.enabled && lines.All(i => !i.enabled), "Existing owner-only HUD switch also hides crosshair");
            hud.enabled = true;
            var remoteAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Prefabs/Player/VOID_LAST_FPS_Network.prefab");
            var remote = Object.Instantiate(remoteAsset, new Vector3(100, 100, 100), Quaternion.identity);
            yield return null;
            var remoteCross = remote.GetComponentInChildren<FpsCrosshair>(true);
            Check(remoteCross != null && !remoteCross.gameObject.activeInHierarchy && !remote.GetComponent<FpsHud>().enabled, "Unspawned network variant inherits crosshair under inactive owner-only HUD");
            Object.Destroy(remote);
            wait = Settle(); while (wait.MoveNext()) yield return null;
            Check(lines.All(i => i.enabled) && hud.HealthText.Contains("100") && pistol.Magazine == ammo && pistol.Reserve == reserve && hp.Current == health, "Local HUD restored; health/ammo unchanged");
            // Exercise the same RectTransforms at different camera target aspect ratios, without project/Game View settings.
            var canvas = cross.GetComponentInParent<Canvas>(); var camera = player.GetComponentInChildren<Camera>();
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; float oldPlane = canvas.planeDistance;
            var oldTarget = camera.targetTexture; float oldAspect = camera.aspect;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 0.5f;
                foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768), new Vector2Int(2560, 1080) })
                {
                    var rt = new RenderTexture(size.x, size.y, 24); rt.Create(); camera.targetTexture = rt; camera.aspect = (float)size.x / size.y;
                    yield return null; Canvas.ForceUpdateCanvases();
                    var center = camera.WorldToViewportPoint(dot.rectTransform.position);
                    Check(Vector2.Distance(new Vector2(center.x, center.y), Vector2.one * 0.5f) < 0.001f
                        && lines.All(i => i.rectTransform.anchorMin == Vector2.one * 0.5f && i.rectTransform.anchorMax == Vector2.one * 0.5f),
                        "Centered at " + size.x + "x" + size.y);
                    if (size.x == 1280)
                    {
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                        var previous = RenderTexture.active; RenderTexture.active = rt;
                        var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                        File.WriteAllBytes(Output + "/Crosshair.png", image.EncodeToPNG());
                        RenderTexture.active = previous; Object.Destroy(image);
                    }
                    camera.targetTexture = oldTarget; rt.Release(); Object.Destroy(rt);
                }
            }
            finally { canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane; camera.targetTexture = oldTarget; camera.aspect = oldAspect; }
            Canvas.ForceUpdateCanvases();
            Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(null, dot.rectTransform.position);
            Check(Vector2.Distance(screenCenter, new Vector2(Screen.width, Screen.height) * 0.5f) < 1, "Original Screen Space Overlay remains at actual screen center");
            Check(cross.GetComponentsInChildren<MonoBehaviour>().All(c => c != null), "No missing crosshair components");
            report.AppendLine("COMPLETE. State APIs driven in Character_test; no live multiplayer session or Stage2/Lobby edits.");
        }
    }
}
