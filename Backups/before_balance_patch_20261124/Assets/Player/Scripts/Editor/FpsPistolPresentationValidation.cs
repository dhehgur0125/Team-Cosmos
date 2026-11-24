/* [ 코드 설명 ] Character_test의 기존 사격 검사에 손·권총 상대 위치, 손목 반동, 근거리/원거리 탄착 및 카메라별 총구 조명 검사를 추가합니다. 벽과 보조 카메라는 Play Mode에서만 생성하며 원본 씬은 저장하지 않습니다. */
using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static partial class FpsShotEffectsValidation
    {
        static IEnumerator CheckPistolPresentation(GameObject player)
        {
            var pistol = player.GetComponent<FpsPistol>(); var effects = player.GetComponent<FpsPistolEffects>();
            var equip = player.GetComponent<FpsEquipment>(); var view = player.GetComponent<FpsWeaponView>();
            var camera = player.GetComponentInChildren<Camera>(); var capsule = player.GetComponent<CharacterController>();
            capsule.enabled = false; player.transform.SetPositionAndRotation(new Vector3(1000, 1000, 1000), Quaternion.identity); capsule.enabled = true;
            camera.transform.parent.localRotation = Quaternion.identity;
            pistol.enabled = effects.enabled = view.enabled = true; equip.Process(1, false, false); view.Refresh(1);
            var root = camera.transform.Find("WeaponRoot"); var glove = root.Find("FPS Right Glove"); var gun = root.Find("SciFiPistol01_2");
            var light = effects.WorldFlash.GetComponentInChildren<Light>();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary presentation wall";
            wall.transform.localScale = new Vector3(6, 6, 0.2f);
            var renderer = wall.GetComponent<Renderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_TrimGrey.mat");
            var observerObject = new GameObject("Temporary presentation observer", typeof(Camera));
            var observer = observerObject.GetComponent<Camera>(); observer.enabled = false; observer.nearClipPlane = 0.02f;
            var flashlight = player.GetComponent<FpsWeaponFlashlight>(); bool flashlightWasOn = flashlight != null && flashlight.On;
            try
            {
                Check(glove != null && glove.parent == gun.parent && AssetDatabase.GetAssetPath(glove.GetComponent<MeshFilter>().sharedMesh).EndsWith("FPS_RightGlove.asset"), "Existing glove mesh and pistol share WeaponRoot; no replacement rig");
                Vector3 grip = gun.InverseTransformPoint(glove.position); Quaternion gripRotation = Quaternion.Inverse(gun.rotation) * glove.rotation;
                Func<bool> gripMatches = () => Vector3.Distance(gun.InverseTransformPoint(glove.position), grip) < 0.0002f && Quaternion.Angle(Quaternion.Inverse(gun.rotation) * glove.rotation, gripRotation) < 0.1f;
                wall.transform.position = camera.transform.position + Vector3.forward * 4; Physics.SyncTransforms();
                var wait = Wait(0.4f); while (wait.MoveNext()) yield return null;
                FpsValidation.Capture(camera, "PistolPose_Hip.png");
                equip.Process(1, true, false); view.Refresh(0.03f);
                Check(view.AimBlend > 0 && view.AimBlend < 0.9f && gripMatches(), "ADS transition interpolates while maintaining the grip");
                wait = Wait(0.5f); while (wait.MoveNext()) yield return null;
                FpsValidation.Capture(camera, "PistolPose_ADS.png");
                for (int stance = 0; stance < 2; stance++)
                {
                    equip.Process(1, stance == 1, false); view.Refresh(1);
                    Vector3 rest = root.localPosition; Quaternion restRotation = root.localRotation;
                    if (pistol.Magazine < 3 && pistol.TryReload()) pistol.TickReload(pistol.ReloadDuration);
                    for (int shot = 0; shot < 3; shot++)
                    {
                        wait = Wait(pistol.ShotInterval + 0.03f); while (wait.MoveNext()) yield return null;
                        Check(pistol.TryFire(), (stance == 0 ? "Hip" : "ADS") + " shot " + (shot + 1)); view.Refresh(0);
                        Check(gripMatches() && root.localPosition.z < rest.z && root.localPosition.y > rest.y && Vector3.Distance(root.localPosition, rest) < 0.05f && view.RecoilAmount == 1,
                            "Gun and glove kick up/back together without accumulating offsets");
                        if (shot == 0)
                        {
                            effects.LocalFlash.Simulate(0.008f, false, false, false);
                            FpsValidation.Capture(camera, stance == 0 ? "PistolPose_HipKick.png" : "PistolPose_ADSKick.png"); effects.LocalFlash.Play();
                        }
                    }
                    wait = Wait(0.12f); while (wait.MoveNext()) yield return null;
                    Check(view.RecoilAmount > 0 && view.RecoilAmount < 0.5f && gripMatches(), "Recoil returns progressively with stable grip");
                    wait = Wait(0.4f); while (wait.MoveNext()) yield return null;
                    Check(view.RecoilAmount == 0 && Vector3.Distance(root.localPosition, rest) < 0.001f && Quaternion.Angle(root.localRotation, restRotation) < 0.01f, "Repeated fire returns to the same rest pose");
                }
                equip.Process(1, false, false); view.Refresh(1);
                Check(pistol.TryReload(), "Presentation reload starts"); pistol.TickReload(pistol.ReloadDuration * 0.5f); view.Refresh(0);
                Check(gripMatches() && view.RecoilAmount == 0, "Reload lowers glove and gun together, clearing recoil");
                pistol.TickReload(pistol.ReloadDuration); view.Refresh(1);
                Check(gripMatches() && !pistol.IsReloading, "Grip remains aligned after reload");
                equip.Process(4, false, false); view.Refresh(1); Check(!view.IsVisible && !effects.IsFlashing, "Item slot hides pistol and clears flash");
                equip.Process(1, false, false); view.Refresh(1);

                Check(light != null && light.transform != effects.WorldFlash.transform && light.type == LightType.Point && light.range <= 1.3f && light.intensity <= 0.4f,
                    "One restrained Point Light has its own transform, separate from both particle emitters");
                Check(Vector3.Distance(effects.LocalFlash.transform.localPosition, new Vector3(0, 0.0214f, 0.1611f)) < 0.01f, "Flash origin is within 1 cm of the measured model muzzle");
                for (int distanceIndex = 0; distanceIndex < 2; distanceIndex++)
                {
                    float distance = distanceIndex == 0 ? 1 : 20;
                    wall.transform.position = camera.transform.position + Vector3.forward * distance; Physics.SyncTransforms();
                    wait = Wait(pistol.ShotInterval + 0.03f); while (wait.MoveNext()) yield return null;
                    Check(pistol.TryFire() && pistol.LastHit.collider.gameObject == wall, distance + "m wall receives the existing hitscan"); view.Refresh(0);
                    var impact = effects.LastImpact;
                    Check(impact != null && impact.name.StartsWith("VOID_LAST_PistolImpact") && Vector3.Distance(impact.transform.position, pistol.LastHit.point + pistol.LastHit.normal * 0.003f) < 0.001f,
                        distance + "m sparks spawn at actual wall hit, not the muzzle");
                    Check(Vector3.Dot(impact.transform.forward, pistol.LastHit.normal) > 0.99f && impact.GetComponentsInChildren<Collider>().Length == 0 && impact.GetComponentsInChildren<Rigidbody>().Length == 0,
                        "Impact follows the surface normal and cannot block shots");
                    impact.Simulate(0.04f, false, false, false); effects.LocalFlash.Simulate(0.008f, false, false, false);
                    FpsValidation.Capture(camera, distanceIndex == 0 ? "PistolPose_NearHit.png" : "PistolPose_FarHit.png");
                    report.AppendLine("Light-to-surface distance " + Vector3.Distance(effects.LocalFlash.transform.position, pistol.LastHit.point).ToString("F2") + "m; point range " + light.range + "m (far wall is outside this light).");
                    effects.LocalFlash.Play(); impact.Play();
                }
                wait = Wait(0.2f); while (wait.MoveNext()) yield return null;
                Check(!light.enabled && !effects.IsFlashing, "Short muzzle flash and light leave no residual illumination");
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_Backing.mat");
                Check(pistol.TryFire() && effects.LastImpact.name.StartsWith("VOID_LAST_PistolDustImpact"), "General wall retains existing dust response at the far hit point");
                Check(FpsBulletMark.MaximumCount == 7 && FpsBulletMark.Lifetime == 30, "Bullet-mark seven/30-second constants remain unchanged");

                if (flashlight != null && !flashlight.On) flashlight.Toggle();
                wait = Wait(0.25f); while (wait.MoveNext()) yield return null;
                int ammo = pistol.Magazine; pistol.PlayNetworkShot();
                bool localLight = false, worldLight = false, localOnly = false, worldOnly = false;
                Action<ScriptableRenderContext, Camera> inspect = (context, rendered) =>
                {
                    if (rendered == camera)
                    {
                        localLight = Vector3.Distance(light.transform.position, effects.LocalFlash.transform.position) < 0.001f;
                        localOnly = !effects.LocalFlash.GetComponent<Renderer>().forceRenderingOff && effects.WorldFlash.GetComponent<Renderer>().forceRenderingOff;
                    }
                    if (rendered == observer)
                    {
                        worldLight = Vector3.Distance(light.transform.position, effects.WorldFlash.transform.position) < 0.001f;
                        worldOnly = effects.LocalFlash.GetComponent<Renderer>().forceRenderingOff && !effects.WorldFlash.GetComponent<Renderer>().forceRenderingOff;
                    }
                };
                observer.transform.position = player.transform.position + new Vector3(1.5f, 1.7f, 2); observer.transform.LookAt(player.transform.position + Vector3.up * 1.4f);
                RenderPipelineManager.beginCameraRendering += inspect;
                try { FpsValidation.Capture(camera, "PistolPose_LocalReplay.png"); FpsValidation.Capture(observer, "PistolPose_WorldReplay.png"); }
                finally { RenderPipelineManager.beginCameraRendering -= inspect; }
                Check(localLight && worldLight && localOnly && worldOnly && pistol.Magazine == ammo, "Existing network-shot replay separates local/world flash and routes one light to each visible muzzle without changing ammo");
                wait = Wait(0.2f); while (wait.MoveNext()) yield return null;
                Check(flashlight != null && flashlight.IsLit && !light.enabled, "Existing flashlight stays on after muzzle light expires");
                Check(player.GetComponentsInChildren<LineRenderer>(true).Length == 0 && player.GetComponentsInChildren<TrailRenderer>(true).Length == 0, "No projectile trail or laser renderer added");
                int missing = player.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                Check(missing == 0 && runtimeErrors == 0, "No Missing Script or new Console error during presentation tests");
                report.AppendLine("COMPLETE. Host/client sessions and independent finger animation were not tested; existing RPC and baked glove mesh were preserved.");
            }
            finally
            {
                if (flashlight != null && flashlight.On != flashlightWasOn) flashlight.Toggle();
                Object.Destroy(wall); Object.Destroy(observerObject);
            }
        }
    }
}
