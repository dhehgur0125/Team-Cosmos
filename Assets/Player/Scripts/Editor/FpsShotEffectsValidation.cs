/* [ 코드 설명 ] Character_test에서 기존 권총 전투 검사를 재사용하고 금속/일반 벽 입자, 섬광 수명, 기존 원격 발사 재생 경로만 추가 검증합니다. 테스트 오브젝트는 런타임에만 생성하며 씬과 네트워크 코드는 저장하지 않습니다. */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static partial class FpsShotEffectsValidation
    {
        const string Pending = "VoidLast.ShotEffects.Validation";
        static IEnumerator checks;
        static StringBuilder report;
        static int frame;
        static bool background;
        static int runtimeErrors;
        public static string Progress { get; private set; }
        static FpsShotEffectsValidation() => EditorApplication.playModeStateChanged += Changed;
        [MenuItem("VOID LAST/Player/Validate Pistol Effects Only")]
        public static void Begin() => Start(false);
        public static void BeginVisualsOnly() => Start(true);
        public static void BeginPresentation() => Start(false, true);
        public static void BeginPresentationPreview() => Start(true, true);
        static void Start(bool visualsOnly, bool presentation = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            SessionState.SetString(Pending + ".Start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(Pending + ".VisualsOnly", visualsOnly);
            SessionState.SetBool(Pending + ".Presentation", presentation);
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
            report = new StringBuilder("VOID LAST pistol effects - actual Character_test Play Mode\n" + DateTime.UtcNow.ToString("O") + "\n");
            background = Application.runInBackground; Application.runInBackground = true; frame = -1;
            runtimeErrors = 0; Application.logMessageReceived += OnLog;
            checks = Checks(); EditorApplication.update += Tick;
        }
        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            runtimeErrors++; report.AppendLine("RUNTIME ERROR: " + message);
        }
        static void Tick()
        {
            if (Time.frameCount == frame) return; frame = Time.frameCount;
            try { if (checks.MoveNext()) return; }
            catch (Exception e) { report.AppendLine("FAILED: " + e); Debug.LogException(e); }
            EditorApplication.update -= Tick; Application.runInBackground = background;
            Application.logMessageReceived -= OnLog;
            string prefix = SessionState.GetBool(Pending + ".Presentation", false) ? "PistolPresentation" : "PistolEffects";
            File.WriteAllText("Assets/Player/Validation/" + prefix + (SessionState.GetBool(Pending + ".VisualsOnly", false) ? "VisualResults.txt" : "Results.txt"), report.ToString());
            EditorApplication.ExitPlaymode();
        }
        static void Check(bool value, string message)
        {
            Progress = message; report.AppendLine((value ? "PASS: " : "FAIL: ") + message);
            if (!value) throw new InvalidOperationException(message);
        }
        static IEnumerator Wait(float seconds) { float end = Time.time + seconds; while (Time.time < end) yield return null; }
        static IEnumerator Checks()
        {
            Check(SceneManager.GetActiveScene().path == "Assets/Player/Character_test.unity", "Existing FPS test scene used; Stage2/Lobby not saved");
            var player = Object.FindAnyObjectByType<FpsPistol>().gameObject;
            player.GetComponent<FpsInput>().enabled = false; player.GetComponent<FpsMotor>().enabled = false;
            player.GetComponent<FpsEquipment>().enabled = false; player.GetComponent<FpsLook>().enabled = false;
            if (!SessionState.GetBool(Pending + ".VisualsOnly", false))
            {
                Progress = "Existing combat checks, including real 30-second mark expiry";
                var combat = FpsValidation.CheckCombat(player, report); while (combat.MoveNext()) yield return combat.Current;
            }
            else report.AppendLine("Visual/effect checks only; existing combat/lifetime checks were not repeated.");
            if (SessionState.GetBool(Pending + ".Presentation", false))
            {
                var presentation = CheckPistolPresentation(player); while (presentation.MoveNext()) yield return presentation.Current;
                yield break;
            }

            var pistol = player.GetComponent<FpsPistol>(); var effects = player.GetComponent<FpsPistolEffects>();
            var equip = player.GetComponent<FpsEquipment>(); var view = player.GetComponent<FpsWeaponView>();
            var camera = player.GetComponentInChildren<Camera>(); var pivot = camera.transform.parent;
            var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(1000, 1000, 1000), Quaternion.identity); capsule.enabled = true;
            pivot.localRotation = Quaternion.identity; pistol.enabled = effects.enabled = view.enabled = true;
            equip.Process(1, false, false); view.Refresh(1);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary material response wall";
            wall.transform.position = camera.transform.position + Vector3.forward * 4; wall.transform.localScale = new Vector3(4, 4, 0.2f);
            var observer = new GameObject("Temporary effects camera", typeof(Camera));
            try
            {
                var renderer = wall.GetComponent<Renderer>();
                var close = observer.GetComponent<Camera>(); close.enabled = false; close.nearClipPlane = 0.02f;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_TrimGrey.mat");
                Check(renderer.sharedMaterial != null && renderer.sharedMaterial.GetFloat("_Metallic") >= 0.5f, "Existing metal wall material identified without modifying it");
                Physics.SyncTransforms();
                Check(pistol.TryFire(), "Metal wall shot succeeds through existing hitscan");
                var metal = effects.LastImpact;
                Check(metal != null && metal.name.StartsWith("VOID_LAST_PistolImpact") && metal.GetComponent<ParticleSystemRenderer>().renderMode == ParticleSystemRenderMode.Stretch, "Metal wall selects existing impact prefab with short stretched sparks");
                Check(effects.LocalFlash.main.startSize.constant >= 0.2f && effects.LocalFlash.textureSheetAnimation.numTilesX == 2
                    && effects.LocalFlash.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap").name == "Muzzle_Flash_A", "Existing local/world flash uses larger warm three-frame flame atlas");
                effects.LocalFlash.Simulate(0.008f, false, false, false); effects.WorldFlash.Simulate(0.008f, false, false, false); metal.Simulate(0.045f, false, false, false);
                Check(metal.particleCount > 0 && metal.particleCount <= 9, "Metal impact emits bounded spark burst");
                FpsValidation.Capture(camera, "PistolFX_Hip.png"); FpsValidation.CaptureObserver(player, "PistolFX_Observer.png", true);
                close.transform.position = pistol.LastHit.point + pistol.LastHit.normal * 0.7f + Vector3.right * 0.2f; close.transform.LookAt(pistol.LastHit.point);
                FpsValidation.Capture(close, "PistolFX_Metal.png");
                Check(metal.GetComponentsInChildren<Collider>().Length == 0 && metal.GetComponentsInChildren<Rigidbody>().Length == 0 && !metal.trails.enabled && !metal.collision.enabled, "Sparks have no collision, rigidbodies or trails");
                var light = effects.WorldFlash.GetComponentInChildren<Light>(); var particles = new ParticleSystem.Particle[1];
                effects.LocalFlash.GetParticles(particles);
                Check(particles[0].startLifetime >= 0.06f && particles[0].startLifetime <= 0.08f && light.range <= 1.3f && light.intensity <= 0.4f && light.shadows == LightShadows.None, "Brief visible flame and restrained shadowless light");
                effects.LocalFlash.Play(); effects.WorldFlash.Play(); metal.Play();
                var wait = Wait(0.15f); while (wait.MoveNext()) yield return null;
                Check(!effects.IsFlashing && !light.enabled, "Muzzle flash and light turn off promptly");
                wait = Wait(pistol.ShotInterval); while (wait.MoveNext()) yield return null;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_Backing.mat");
                Check(!renderer.sharedMaterial.HasProperty("_Metallic") || renderer.sharedMaterial.GetFloat("_Metallic") < 0.5f, "Existing nonmetal wall material identified");
                equip.Process(1, true, false); view.Refresh(1);
                Check(pistol.TryFire() && equip.IsAiming, "ADS shot preserves aim and existing ammo/recoil path");
                var dust = effects.LastImpact;
                Check(dust != null && dust.name.StartsWith("VOID_LAST_PistolDustImpact") && dust != metal, "Nonmetal wall selects small dust burst");
                dust.Simulate(0.09f, false, false, false); effects.LocalFlash.Simulate(0.008f, false, false, false);
                Check(dust.particleCount > 0 && dust.particleCount <= 6 && dust.GetComponentsInChildren<Collider>().Length == 0
                    && dust.GetComponentsInChildren<Rigidbody>().Length == 0 && !dust.collision.enabled, "Dust uses at most six nonphysical particles");
                FpsValidation.Capture(camera, "PistolFX_ADS.png"); FpsValidation.Capture(close, "PistolFX_Dust.png");
                effects.LocalFlash.Play(); dust.Play();
                wait = Wait(0.7f); while (wait.MoveNext()) yield return null;
                Check(metal == null && dust == null, "Both impact styles destroy themselves after particles expire");

                int ammo = pistol.Magazine, marks = FpsBulletMark.ActiveCount; var priorHit = pistol.LastHit;
                pistol.PlayNetworkShot();
                Check(effects.IsFlashing && effects.WorldFlash.particleCount == 1 && pistol.Magazine == ammo && FpsBulletMark.ActiveCount == marks
                    && pistol.LastHit.point == priorHit.point, "Existing PlayNetworkShot replays muzzle flash without damage, hit or ammo changes");
                bool localOnly = false, externalOnly = false;
                Action<ScriptableRenderContext, Camera> inspect = (context, rendered) =>
                {
                    if (rendered == camera) localOnly = !effects.LocalFlash.GetComponent<Renderer>().forceRenderingOff && effects.WorldFlash.GetComponent<Renderer>().forceRenderingOff;
                    if (rendered == close) externalOnly = effects.LocalFlash.GetComponent<Renderer>().forceRenderingOff && !effects.WorldFlash.GetComponent<Renderer>().forceRenderingOff;
                };
                RenderPipelineManager.beginCameraRendering += inspect;
                try { FpsValidation.Capture(camera, "PistolFX_LocalReplay.png"); FpsValidation.Capture(close, "PistolFX_RemoteReplay.png"); }
                finally { RenderPipelineManager.beginCameraRendering -= inspect; }
                Check(localOnly && externalOnly, "Existing camera visibility separates first-person and world flashes");
                Check(player.GetComponentsInChildren<LineRenderer>(true).Length == 0 && player.GetComponentsInChildren<TrailRenderer>(true).Length == 0, "No projectile, beam or tracer rendering added");
                Check(runtimeErrors == 0, "No new Console errors/exceptions during this run");
                report.AppendLine("COMPLETE. Existing RPC code was preserved; live network peers and synchronized impact points were not tested.");
            }
            finally { Object.Destroy(wall); Object.Destroy(observer); }
        }
    }
}
