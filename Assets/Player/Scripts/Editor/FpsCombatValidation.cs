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
            Require(pistol != null && pistol.Magazine == 10 && pistol.Reserve == 30, "Initial magazine/reserve = 10 / 30", report);
            pistol.enabled = true; view.enabled = true; hud.enabled = true;
            equipment.Process(2, false, false); equipment.Process(1, false, false);
            Require(!pistol.TryFire() && !pistol.TryReload(), "Pistol cannot fire/reload during equip", report);
            view.Refresh(0.31f);
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
                var first = Object.FindObjectsByType<FpsBulletMark>().Single();
                Require(Vector3.Distance(first.transform.position, pistol.LastHit.point + pistol.LastHit.normal * 0.002f) < 0.0001f && Vector3.Dot(first.transform.forward, pistol.LastHit.normal) > 0.999f && first.GetComponentInChildren<Collider>() == null, "Mark follows impact point/normal with offset and no Collider", report);
                Require(!pistol.TryFire() && pistol.Magazine == 9, "Shot interval rejects immediate second request", report);
                for (int shot = 2; shot <= 8; shot++)
                {
                    float until = Time.time + pistol.ShotInterval + 0.02f; while (Time.time < until) yield return null;
                    pivot.localRotation = Quaternion.Euler((shot % 3 - 1) * 3, (shot - 4) * 3, 0); Physics.SyncTransforms();
                    Require(pistol.TryFire() && pistol.LastHit.collider.gameObject == wall && FpsBulletMark.ActiveCount == Mathf.Min(shot, 7), "Wall shot " + shot + " maintains global FIFO count", report);
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
                Require(pistol.TryFire() && !pistol.HasLastHit && FpsBulletMark.ActiveCount == before && pistol.Magazine == 0 && ammo.text == "0 / 30", "Miss creates no mark; empty magazine displays zero", report);
                Require(!pistol.TryFire(), "Empty magazine blocks firing", report);
                pivot.localRotation = Quaternion.identity; equipment.Process(1, true, false); view.Refresh(0.5f);
                Require(pistol.TryReload() && !equipment.IsAiming && !pistol.TryReload() && !pistol.TryFire(), "Reload starts once, clears ADS and blocks firing", report);
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
                pistol.enabled = false; view.enabled = false;
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
