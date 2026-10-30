/* [ 코드 설명 ] Character_test Play Mode에서 대각선 혼합·하체 점프·권총 조준·슬롯·카메라별 전신/권총 표시를 기존 검증에 추가합니다. 테스트용 관찰 카메라와 상태는 Play 종료 시 사라집니다. */
using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoidLast.Player.Editor
{
    public static partial class FpsValidation
    {
        static IEnumerator CheckJumpAndWeapon(GameObject player, StringBuilder report)
        {
            var motor = player.GetComponent<FpsMotor>(); var equipment = player.GetComponent<FpsEquipment>();
            var animation = player.GetComponent<FpsAnimation>(); var animator = player.GetComponentInChildren<Animator>();
            var view = player.GetComponent<FpsWeaponView>(); var camera = player.GetComponentInChildren<Camera>();
            var weaponRoot = camera.transform.Find("WeaponRoot");
            var worldSocket = animator.GetBoneTransform(HumanBodyBones.RightHand).Find("WorldWeaponSocket");
            Require(view != null && weaponRoot != null && worldSocket != null, "FPS WeaponRoot and right-hand world socket exist", report);
            foreach (var root in new[] { weaponRoot, worldSocket })
            {
                var gun = root.GetChild(0);
                Require(gun.Find("Button") != null && gun.Find("Clip") != null && gun.Find("Trigger") != null, "Original gun child structure preserved: " + root.name, report);
                Require(gun.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "Mounted gun colliders disabled: " + root.name, report);
                Require(gun.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported)), "Gun material/shader references: " + root.name, report);
            }
            var controller = (AnimatorController)animator.runtimeAnimatorController;
            Require(controller.layers[2].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg) &&
                controller.layers[2].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg) &&
                !controller.layers[2].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body) &&
                !controller.layers[2].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root), "Jump mask affects legs only, excluding torso and root", report);
            animator.SetBool("Crouch", false); animator.SetInteger("Slot", 1); animator.SetLayerWeight(1, 1); animator.SetFloat("Gait", 1); Advance(animator, 1.5f);
            foreach (Vector2 direction in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            {
                animator.SetFloat("MoveX", direction.normalized.x); animator.SetFloat("MoveY", direction.normalized.y); Advance(animator, 0.2f);
                var clips = animator.GetCurrentAnimatorClipInfo(0).Where(c => c.weight > 0.001f).ToArray();
                Require(clips.Length == 2 && clips.All(c => c.clip != FpsAssetSetup.Clip("Idle") && c.clip != FpsAssetSetup.Clip("Standard Run")) && clips.All(c => c.weight > 0.35f && c.weight < 0.65f), "Diagonal blends adjacent directions without Idle/Run: " + direction, report);
                animator.speed = 0;
                for (int frame = 0; frame < 4; frame++) yield return null;
                CaptureObserver(player, "Diagonal_" + (direction.y > 0 ? "W" : "S") + (direction.x > 0 ? "D" : "A") + ".png");
                animator.speed = 1;
            }
            animator.SetBool("Crouch", true); animator.SetFloat("CrouchAmount", 1); animator.SetFloat("CrouchDirection", 1); Advance(animator, 1);
            Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == FpsAssetSetup.Clip("Crouched Walking") && c.weight > 0.95f), "Forward crouch diagonal keeps full movement weight", report);
            animator.SetFloat("CrouchDirection", -1); Advance(animator, 0.2f);
            Require(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == FpsAssetSetup.Clip("Walk Crouching Backward") && c.weight > 0.95f), "Backward crouch diagonal keeps full movement weight", report);
            animator.SetBool("Crouch", false); animator.SetFloat("Gait", 0); Advance(animator, 1);
            Teleport(player, new Vector3(0, 0.01f, -3)); Move(motor, Vector2.zero, false, false);
            // Local velocity must follow the character basis even after a world yaw change.
            player.transform.rotation = Quaternion.Euler(0, 90, 0);
            var old = player.transform.position; Move(motor, Vector2.up, false, false, 15);
            Require(player.transform.position.x > old.x + 0.4f && Mathf.Abs(motor.LocalVelocity.x) < 0.05f && motor.LocalVelocity.y > 2, "Movement/animation velocity uses character-local coordinates", report);
            Teleport(player, new Vector3(0, 0.01f, -3)); Move(motor, Vector2.zero, false, false);
            equipment.Process(1, false, false); view.Refresh(1); animation.enabled = true;
            for (float elapsed = 0; elapsed < 1.6f; elapsed += Time.deltaTime) yield return null;
            Require(view.IsVisible && worldSocket.gameObject.activeSelf, "Slot 1 shows both representations to their respective cameras", report);
            var hipPosition = weaponRoot.localPosition;
            Capture(camera, "Pistol_Hip_FPS.png");
            equipment.Process(1, true, false); view.Refresh(0.04f);
            Require(view.AimBlend > 0 && view.AimBlend < 0.9f && weaponRoot.localPosition != hipPosition, "ADS interpolates rather than snapping", report);
            for (float elapsed = 0; elapsed < 0.6f; elapsed += Time.deltaTime) { view.Refresh(Time.deltaTime); yield return null; }
            Require(view.AimBlend > 0.99f && Mathf.Abs(weaponRoot.localPosition.x) < 0.005f, "ADS reaches center position", report);
            var muzzle = weaponRoot.TransformPoint(new Vector3(0, 0.025f, 0.16f));
            var viewport = camera.WorldToViewportPoint(muzzle);
            Require(Vector2.Distance(new Vector2(viewport.x, viewport.y), new Vector2(0.5f, 0.5f)) < 0.06f && Vector3.Dot(weaponRoot.TransformDirection(Vector3.forward), camera.transform.forward) > 0.99f, "Pistol muzzle (+Z) axis and sights align with view center", report);
            Capture(camera, "Pistol_ADS_FPS.png");
            bool localCorrect = false, observerCorrect = false;
            var worldRenderers = animator.GetComponentsInChildren<Renderer>(true);
            var fpsRenderers = weaponRoot.GetComponentsInChildren<Renderer>(true);
            Action<ScriptableRenderContext, Camera> observe = (context, rendered) =>
            {
                if (rendered == camera) localCorrect = worldRenderers.All(r => r.forceRenderingOff) && fpsRenderers.All(r => !r.forceRenderingOff);
                else observerCorrect = worldRenderers.All(r => !r.forceRenderingOff) && fpsRenderers.All(r => r.forceRenderingOff);
            };
            RenderPipelineManager.beginCameraRendering += observe;
            try { Capture(camera, "Pistol_ADS_FPS.png"); CaptureObserver(player, "Pistol_Observer.png"); }
            finally { RenderPipelineManager.beginCameraRendering -= observe; }
            Require(localCorrect && observerCorrect, "Local camera sees gun only; observer sees body/world gun only", report);
            Require(worldRenderers.Concat(fpsRenderers).All(r => !r.forceRenderingOff), "Per-camera renderer state restored after rendering", report);
            var localBefore = weaponRoot.localPosition;
            player.transform.rotation = Quaternion.Euler(0, 70, 0); camera.transform.parent.localRotation = Quaternion.Euler(-25, 0, 0);
            yield return null;
            Require(weaponRoot.parent == camera.transform && Vector3.Distance(weaponRoot.localPosition, localBefore) < 0.001f && Vector3.Dot(weaponRoot.TransformDirection(Vector3.forward), camera.transform.forward) > 0.99f, "Weapon follows camera yaw/pitch in local space", report);
            camera.transform.parent.localRotation = Quaternion.identity; player.transform.rotation = Quaternion.identity;
            equipment.Process(1, false, false);
            for (float elapsed = 0; elapsed < 0.6f; elapsed += Time.deltaTime) { view.Refresh(Time.deltaTime); yield return null; }
            Require(Vector3.Distance(weaponRoot.localPosition, hipPosition) < 0.005f, "Right-click release returns to hip position", report);
            foreach (int slot in new[] { 2, 3, 4, 5, 6 })
            {
                equipment.Process(slot, true, false); view.Refresh(0.1f);
                Require(!view.IsVisible && !worldSocket.gameObject.activeSelf && !equipment.IsAiming && (int)equipment.SelectedSlot == slot, "Non-pistol slot hides both weapons: " + slot, report);
            }
            Capture(camera, "Unarmed_FPS.png");
            equipment.Process(1, true, false); view.Refresh(1);
            // Physics jump is independent of clip duration, including a repeated jump request in mid-air.
            motor.Simulate(Vector2.zero, false, false, 1f / 120, true);
            float launchVelocity = motor.VerticalSpeed;
            Require(motor.IsAirborne && launchVelocity > 0, "Grounded jump takes off", report);
            motor.Simulate(Vector2.zero, false, false, 1f / 120, true);
            Require(motor.VerticalSpeed < launchVelocity, "Mid-air second jump is rejected", report);
            float apex = player.transform.position.y;
            bool captured = false;
            for (float elapsed = 0; elapsed < 3 && !motor.IsGrounded; elapsed += Time.deltaTime)
            {
                motor.Simulate(Vector2.zero, false, false, Mathf.Min(Time.deltaTime, 0.05f));
                apex = Mathf.Max(apex, player.transform.position.y);
                if (!captured && motor.VerticalSpeed <= 0 && motor.IsAirborne)
                {
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    Require(animator.GetCurrentAnimatorStateInfo(2).IsName("Jump") && animator.GetCurrentAnimatorStateInfo(1).IsName("Pistol Idle") && animator.GetLayerWeight(1) > 0.9f, "Airborne leg animation preserves pistol upper body and ADS", report);
                    CaptureObserver(player, "Jump_Observer.png"); captured = true;
                }
                yield return null;
            }
            Require(captured && motor.IsGrounded && Mathf.Abs(apex - motor.JumpHeight) < 0.15f, "Jump reaches configured height and lands: apex=" + apex.ToString("F3"), report);
            for (float elapsed = 0; elapsed < 0.4f; elapsed += Time.deltaTime) yield return null;
            Require(animator.GetLayerWeight(2) < 0.01f && !animator.GetBool("Airborne"), "Landing fades Jump layer and restores locomotion", report);
            Move(motor, Vector2.zero, false, true); old = player.transform.position;
            motor.Simulate(Vector2.zero, false, true, 1f / 60, true);
            Require(motor.IsGrounded && motor.VerticalSpeed <= 0 && player.transform.position.y < old.y + 0.02f, "Crouched jump is rejected", report);
            Move(motor, Vector2.zero, false, false);
            animation.enabled = false;
        }
    }
}
