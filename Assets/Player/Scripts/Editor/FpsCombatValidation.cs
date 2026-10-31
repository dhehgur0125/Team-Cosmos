/* [ 코드 설명 ] Character_test의 실제 Play Mode에서 사격 차단·탄약 계산·재장전 취소·전역 탄흔 제한과 30초 수명을 검증합니다. 테스트 벽과 보조 플레이어는 런타임에만 생성합니다. */
using System.Collections;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static partial class FpsValidation
    {
        static IEnumerator CheckCombat(GameObject player, StringBuilder report)
        {
            var pistol = player.GetComponent<FpsPistol>(); var equipment = player.GetComponent<FpsEquipment>();
            var view = player.GetComponent<FpsWeaponView>(); var hud = player.GetComponent<FpsHud>();
            var camera = player.GetComponentInChildren<Camera>(); var pivot = camera.transform.parent;
            var weaponRoot = camera.transform.Find("WeaponRoot"); var ammo = player.transform.Find("FPS HUD/Ammo").GetComponent<Text>();
            var effects = player.GetComponent<FpsPistolEffects>(); effects.enabled = true;
            Require(pistol != null && pistol.Magazine == 10 && pistol.Reserve == 30, "Initial magazine/reserve = 10 / 30", report);
            pistol.enabled = true; view.enabled = true; hud.enabled = true;
            equipment.Process(2, false, false); equipment.Process(1, false, false);
            Require(!pistol.TryFire() && !pistol.TryReload(), "Pistol cannot fire/reload during equip", report);
            view.Refresh(0.31f);
            var hipPosition = weaponRoot.localPosition; var hipRotation = weaponRoot.localRotation;
            Require(effects.LocalFlash != null && effects.WorldFlash != null && !effects.IsFlashing && !effects.LocalFlash.main.playOnAwake, "Both muzzle effects exist and stay off before a successful shot", report);
            Require(Vector3.Distance(effects.LocalFlash.transform.localPosition, new Vector3(0, 0.0214f, 0.19f)) < 0.001f, "Muzzle effect is just beyond measured barrel front", report);
            Require(!pistol.TryReload() && ammo.text == "10 / 30" && ammo.enabled && ammo.rectTransform.anchorMin == new Vector2(1, 0), "Full magazine rejects reload; white ammo UI anchored bottom-right", report);
            Teleport(player, new Vector3(1000, 1000, 1000)); pivot.localRotation = Quaternion.identity;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary combat test wall";
            wall.transform.position = camera.transform.position + Vector3.forward * 4; wall.transform.localScale = new Vector3(4, 4, 0.2f);
            var own = new GameObject("Temporary own-model collider", typeof(BoxCollider)); own.transform.SetParent(player.transform);
            own.transform.position = camera.transform.position + Vector3.forward; own.transform.localScale = Vector3.one * 0.2f;
            Physics.SyncTransforms(); int events = 0; System.Action<RaycastHit> observeHit = hit => events++; pistol.Hit += observeHit;
            float lastMarkTime = Time.time;
            try
            {
                Require(pistol.TryFire() && pistol.Magazine == 9 && pistol.Reserve == 30 && ammo.text == "9 / 30", "One shot consumes exactly one magazine round and updates HUD", report);
                Require(pistol.HasLastHit && pistol.LastHit.collider.gameObject == wall && events == 1, "Center-camera hitscan ignores own colliders and reports nearest wall hit", report);
                view.Refresh(0); var cameraRotation = camera.transform.rotation;
                Require(effects.IsFlashing && effects.LocalFlash.particleCount == 1 && effects.WorldFlash.particleCount == 1, "Successful shot emits one short flash in each camera representation", report);
                var flashLight = effects.WorldFlash.GetComponent<Light>();
                Require(flashLight.enabled && flashLight.intensity <= 0.2f && flashLight.range <= 1.1f, "Shot enables only a weak short-range muzzle light", report);
                Require(view.RecoilAmount == 1 && weaponRoot.localPosition.z < hipPosition.z - 0.02f && weaponRoot.localPosition.y > hipPosition.y && Mathf.DeltaAngle(hipRotation.eulerAngles.x, weaponRoot.localEulerAngles.x) < -3, "Shot kicks the gun upward/backward from its base pose", report);
                var impact = effects.LastImpact;
                Require(impact != null && Vector3.Distance(impact.transform.position, pistol.LastHit.point + pistol.LastHit.normal * 0.003f) < 0.001f && Vector3.Dot(impact.transform.forward, pistol.LastHit.normal) > 0.999f, "Environmental impact follows hit point and surface normal", report);
                Require(impact.GetComponent<Collider>() == null && !impact.trails.enabled && player.GetComponentsInChildren<LineRenderer>(true).Length == 0 && player.GetComponentsInChildren<TrailRenderer>(true).Length == 0, "Effects add no colliders, projectiles, line or luminous trail renderers", report);
                effects.LocalFlash.Simulate(0.007f, false, false, false); effects.WorldFlash.Simulate(0.007f, false, false, false); impact.Simulate(0.025f, false, false, false);
                Capture(camera, "Shot_Hip_FPS.png"); CaptureObserver(player, "Shot_Observer.png", true);
                var closeUp = new GameObject("Temporary impact observer", typeof(Camera));
                try
                {
                    var impactCamera = closeUp.GetComponent<Camera>(); impactCamera.enabled = false; impactCamera.nearClipPlane = 0.02f;
                    closeUp.transform.position = pistol.LastHit.point + pistol.LastHit.normal * 0.35f + Vector3.right * 0.1f;
                    closeUp.transform.LookAt(pistol.LastHit.point);
                    Require(impact.particleCount > 0, "Impact emits its brief particle burst", report);
                    Capture(impactCamera, "Shot_Impact_Closeup.png");
                }
                finally { Object.DestroyImmediate(closeUp); }
                effects.LocalFlash.Play(); effects.WorldFlash.Play(); impact.Play();
                Require(!effects.LocalFlash.GetComponent<Renderer>().forceRenderingOff && !effects.WorldFlash.GetComponent<Renderer>().forceRenderingOff, "Muzzle visibility flags restore after local and observer rendering", report);
                var first = Object.FindObjectsByType<FpsBulletMark>().Single();
                Require(Vector3.Distance(first.transform.position, pistol.LastHit.point + pistol.LastHit.normal * 0.002f) < 0.0001f && Vector3.Dot(first.transform.forward, pistol.LastHit.normal) > 0.999f && first.GetComponentInChildren<Collider>() == null, "Mark follows impact point/normal with offset and no Collider", report);
                Require(!pistol.TryFire() && pistol.Magazine == 9, "Shot interval rejects immediate second request", report);
                Require(effects.LastImpact == impact, "Rejected fire creates no additional impact", report);
                float recoveredAt = Time.time + 0.45f; while (Time.time < recoveredAt) yield return null;
                Require(!effects.IsFlashing && !flashLight.enabled && effects.LocalFlash.particleCount == 0 && effects.WorldFlash.particleCount == 0 && impact == null, "Flash/light end quickly and impact destroys itself automatically", report);
                Require(view.RecoilAmount == 0 && Vector3.Distance(weaponRoot.localPosition, hipPosition) < 0.001f && Quaternion.Angle(weaponRoot.localRotation, hipRotation) < 0.01f && camera.transform.rotation == cameraRotation, "Hip recoil returns exactly with no camera kick", report);
                for (int shot = 2; shot <= 8; shot++)
                {
                    float until = Time.time + pistol.ShotInterval + 0.02f; while (Time.time < until) yield return null;
                    pivot.localRotation = Quaternion.Euler((shot % 3 - 1) * 3, (shot - 4) * 3, 0); Physics.SyncTransforms();
                    Require(pistol.TryFire() && pistol.LastHit.collider.gameObject == wall && FpsBulletMark.ActiveCount == Mathf.Min(shot, 7), "Wall shot " + shot + " maintains global FIFO count", report);
                    view.Refresh(0);
                    Require(view.RecoilAmount == 1 && Vector3.Distance(weaponRoot.localPosition, hipPosition) < 0.03f && effects.IsFlashing, "Repeated shot restarts bounded recoil/flash without accumulating position: " + shot, report);
                    if (shot == 8) Require(first == null || !first.gameObject.activeSelf, "Eighth impact removes oldest mark immediately", report);
                }
                Capture(camera, "Bullet_Marks_FPS.png");
                var secondShooter = CombatClone(6, 30);
                try
                {
                    var secondCamera = secondShooter.GetComponentInChildren<Camera>(true);
                    Teleport(secondShooter, player.transform.position + Vector3.right * 0.2f); secondCamera.transform.parent.LookAt(wall.transform.position);
                    var secondPistol = secondShooter.GetComponent<FpsPistol>(); secondShooter.GetComponent<FpsWeaponView>().Refresh(0.5f); Physics.SyncTransforms();
                    var oldMarks = Object.FindObjectsByType<FpsBulletMark>();
                    Require(secondPistol.TryFire() && FpsBulletMark.ActiveCount == 7 && oldMarks.Count(m => m == null || !m.gameObject.activeSelf) == 1, "Second shooter shares the same world-wide seven-mark limit", report);
                    lastMarkTime = Time.time;
                }
                finally { Object.Destroy(secondShooter); }
                float next = Time.time + pistol.ShotInterval + 0.02f; while (Time.time < next) yield return null;
                pivot.localRotation = Quaternion.identity; Require(pistol.TryFire() && pistol.Magazine == 1, "Ninth shot remains unobstructed by existing marks", report); lastMarkTime = Time.time;
                next = Time.time + pistol.ShotInterval + 0.02f; while (Time.time < next) yield return null;
                pivot.localRotation = Quaternion.Euler(-90, 0, 0); int before = FpsBulletMark.ActiveCount;
                var previousImpact = effects.LastImpact;
                Require(pistol.TryFire() && !pistol.HasLastHit && FpsBulletMark.ActiveCount == before && pistol.Magazine == 0 && ammo.text == "0 / 30", "Miss creates no mark; empty magazine displays zero", report);
                Require(effects.IsFlashing && effects.LastImpact == previousImpact, "Miss still flashes but creates no impact", report);
                Require(!pistol.TryFire(), "Empty magazine blocks firing", report);
                pivot.localRotation = Quaternion.identity; equipment.Process(1, true, false); view.Refresh(0.5f);
                Require(pistol.TryReload() && !equipment.IsAiming && !pistol.TryReload() && !pistol.TryFire(), "Reload starts once, clears ADS and blocks firing", report);
                Require(!effects.IsFlashing && view.RecoilAmount == 0 && effects.LocalFlash.particleCount == 0, "Reload clears flash and recoil before lowering", report);
                pistol.TickReload(0.6f); view.Refresh(0.2f); equipment.Process(0, true, false);
                Require(pistol.Magazine == 0 && pistol.Reserve == 30 && !equipment.IsAiming && weaponRoot.localPosition.y < -0.5f, "Reload holds gun lowered without moving ammunition or allowing ADS", report);
                Capture(camera, "Pistol_Reload_FPS.png");
                equipment.Process(4, false, false);
                Require(!pistol.IsReloading && pistol.Magazine == 0 && pistol.Reserve == 30 && !view.IsVisible && !ammo.enabled && !pistol.TryFire(), "Slot switch cancels reload with unchanged ammo and hides gun/UI", report);
                equipment.Process(1, false, false); view.Refresh(0.5f);
                Require(weaponRoot.localPosition.y > -0.25f && pistol.TryReload(), "Re-equipping restores pose and allows restarting reload", report);
                pistol.TickReload(pistol.ReloadDuration - 0.01f);
                Require(pistol.Magazine == 0 && pistol.Reserve == 30, "Ammo stays unchanged immediately before reload completes", report);
                pistol.TickReload(0.02f); view.Refresh(0.1f);
                Require(!pistol.IsReloading && pistol.Magazine == 10 && pistol.Reserve == 20 && ammo.text == "10 / 20", "Empty reload transfers 0/30 to 10/20 only at completion", report);
                equipment.Process(1, true, false); view.Refresh(0.5f);
                Require(equipment.IsAiming && view.AimBlend > 0.99f, "ADS recovers after completed reload", report);
                ScreenCapture.CaptureScreenshot("Assets/Player/Validation/Pistol_Ammo_HUD.png");
                float readyAt = Time.time + pistol.ShotInterval + 0.02f; while (Time.time < readyAt) yield return null;
                var aimedPosition = weaponRoot.localPosition; var aimedRotation = weaponRoot.localRotation;
                Require(pistol.TryFire(), "ADS shot succeeds after reload", report); view.Refresh(0); lastMarkTime = Time.time;
                Require(Vector3.Distance(weaponRoot.localPosition, aimedPosition) > 0.01f && Vector3.Distance(weaponRoot.localPosition, aimedPosition) < 0.025f, "ADS uses smaller model recoil on top of aim pose", report);
                effects.LocalFlash.Simulate(0.007f, false, false, false); Capture(camera, "Shot_ADS_FPS.png"); effects.LocalFlash.Play();
                recoveredAt = Time.time + 0.45f; while (Time.time < recoveredAt) yield return null;
                Require(Vector3.Distance(weaponRoot.localPosition, aimedPosition) < 0.001f && Quaternion.Angle(weaponRoot.localRotation, aimedRotation) < 0.01f, "ADS recoil returns without drifting the sight alignment", report);
                Require(pistol.TryFire(), "Shot before rapid slot switch succeeds", report); lastMarkTime = Time.time;
                equipment.Process(2, false, false);
                Require(!effects.IsFlashing && view.RecoilAmount == 0 && effects.LocalFlash.particleCount == 0 && !view.IsVisible, "Rapid slot switch clears flash/recoil immediately", report);
                equipment.Process(1, false, false); view.Refresh(0.5f);
                Require(Vector3.Distance(weaponRoot.localPosition, hipPosition) < 0.001f && !effects.IsFlashing, "Re-equipping restores normal pose with no stale flash", report);
                foreach (var values in new[] { new[] { 6, 30, 10, 26 }, new[] { 0, 5, 5, 0 } })
                {
                    var clone = CombatClone(values[0], values[1]);
                    try
                    {
                        var test = clone.GetComponent<FpsPistol>(); clone.GetComponent<FpsWeaponView>().Refresh(0.5f);
                        Require(test.TryReload(), "Reload accepts configured ammo " + values[0] + "/" + values[1], report);
                        test.TickReload(test.ReloadDuration);
                        Require(test.Magazine == values[2] && test.Reserve == values[3], "Reload result " + values[2] + "/" + values[3], report);
                        if (test.Reserve == 0) Require(!test.TryReload(), "Zero reserve rejects reload", report);
                        if (values[0] == 6)
                        {
                            Teleport(clone, player.transform.position + Vector3.forward * 2); Physics.SyncTransforms();
                            pivot.LookAt(clone.GetComponent<CharacterController>().bounds.center);
                            readyAt = Time.time + pistol.ShotInterval + 0.02f; while (Time.time < readyAt) yield return null;
                            previousImpact = effects.LastImpact; before = FpsBulletMark.ActiveCount;
                            Require(pistol.TryFire() && pistol.HasLastHit && pistol.LastHit.collider.GetComponentInParent<FpsHealth>() == clone.GetComponent<FpsHealth>(), "Hitscan reaches the explicitly aimed character capsule", report);
                            Require(effects.LastImpact == previousImpact && FpsBulletMark.ActiveCount == before, "Character hit produces neither environment impact nor wall mark", report);
                            pivot.localRotation = Quaternion.identity;
                        }
                        clone.GetComponent<FpsHealth>().TakeDamage(10000);
                        Require(!test.TryFire() && !test.TryReload(), "Dead player cannot fire or reload", report);
                    }
                    finally { Object.Destroy(clone); }
                }
                report.AppendLine("Waiting for actual 30-second bullet-mark expiry (no time-scale changes).");
                while (Time.time < lastMarkTime + FpsBulletMark.Lifetime + 0.2f) yield return null;
                Require(FpsBulletMark.ActiveCount == 0 && Object.FindObjectsByType<FpsBulletMark>().Length == 0, "Every mark expires after 30 seconds of real Play Mode time", report);
            }
            finally
            {
                pistol.Hit -= observeHit; Object.Destroy(own); Object.Destroy(wall);
                pistol.enabled = false; view.enabled = false; effects.enabled = false;
                equipment.Process(1, false, false); Teleport(player, new Vector3(0, 0.02f, -3)); pivot.localRotation = Quaternion.identity;
            }
            yield return null;
        }
        static GameObject CombatClone(int magazine, int reserve)
        {
            var inactive = new GameObject("Temporary combat initialization"); inactive.SetActive(false);
            var clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FpsAssetSetup.PrefabPath), inactive.transform);
            var config = new SerializedObject(clone.GetComponent<FpsPistol>());
            config.FindProperty("startingMagazine").intValue = magazine; config.FindProperty("startingReserve").intValue = reserve; config.ApplyModifiedPropertiesWithoutUndo();
            clone.transform.position = new Vector3(1100, 1000, 1100); clone.transform.SetParent(null); Object.Destroy(inactive);
            foreach (var behaviour in clone.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
            clone.GetComponentInChildren<Camera>(true).enabled = false; clone.GetComponentInChildren<AudioListener>(true).enabled = false;
            clone.transform.Find("FPS HUD").gameObject.SetActive(false);
            return clone;
        }
    }
}
