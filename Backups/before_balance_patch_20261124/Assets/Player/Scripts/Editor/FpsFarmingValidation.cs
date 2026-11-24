/* [ 코드 설명 ] 실제 Character_test Play Mode에서 HP·Raycast 획득·3칸 보관·사용 취소/완료·안전 드랍·Trigger 재획득·HUD를 검증하고 화면을 기록합니다. 테스트 상태는 씬에 저장하지 않습니다. */
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    public static partial class FpsValidation
    {
        static IEnumerator CheckFarming(GameObject player, StringBuilder report)
        {
            var health = player.GetComponent<FpsHealth>(); var inventory = player.GetComponent<FpsInventory>();
            var equipment = player.GetComponent<FpsEquipment>(); var interactor = player.GetComponent<FpsItemInteractor>();
            var itemView = player.GetComponent<FpsItemView>(); var hud = player.GetComponent<FpsHud>();
            var weapon = player.GetComponent<FpsWeaponView>(); var camera = player.GetComponentInChildren<Camera>(true);
            var pivot = camera.transform.parent; var motor = player.GetComponent<FpsMotor>();
            // Drive held-button values explicitly while physics, Animator, UI and rendering run in Play Mode.
            inventory.enabled = false; hud.enabled = itemView.enabled = true;
            player.GetComponent<FpsAnimation>().enabled = true;
            Require(health.Current == 100 && health.Maximum == 100 && hud.HealthText == "HP 100 / 100", "HP starts at 100 and HUD displays current/max", report);
            var debug = player.GetComponent<FpsHealthDebugInput>();
            Require(debug != null && AssetDatabase.LoadAssetAtPath<GameObject>(FpsAssetSetup.PrefabPath).GetComponent<FpsHealthDebugInput>() == null, "H debug input exists only as Character_test scene override", report);
            debug.HalveCurrentHealth(); Require(health.Current == 50 && hud.HealthText == "HP 50 / 100", "H test halves 100 to 50 and updates HUD immediately", report);
            debug.HalveCurrentHealth(); Require(health.Current == 25 && hud.HealthText == "HP 25 / 100", "H test halves 50 to 25", report);
            health.Heal(1000); Require(health.Current == 100 && !health.Heal(30), "Healing clamps to max and rejects full HP", report);
            var pickups = Object.FindObjectsByType<FpsWorldPickup>().OrderBy(p => p.name).ToArray();
            Require(pickups.Length == 3 && pickups.All(p => !p.IsDropped), "Three initial map syringes require F", report);
            Teleport(player, pickups[0].transform.position + Vector3.up * 0.02f);
            for (float time = 0; time < 0.3f; time += Time.deltaTime) yield return null;
            Require(inventory.ItemAt(0) == FarmingItemKind.None && !pickups[0].IsClaimed, "Walking over initial map syringe does not auto-pickup", report);
            foreach (var pickup in pickups)
            {
                var position = pickup.transform.position; Teleport(player, new Vector3(position.x, 0.02f, position.z - 1.1f));
                if (pickup == pickups[0])
                {
                    pivot.LookAt(pickup.GetComponent<BoxCollider>().bounds.center + Vector3.right * 0.09f); Physics.SyncTransforms(); interactor.UpdateTarget();
                    Require(interactor.Target == pickup, "F detection accepts a small 9cm aiming offset", report);
                    pivot.LookAt(pickup.GetComponent<BoxCollider>().bounds.center + Vector3.left * 0.5f); interactor.UpdateTarget();
                    Require(interactor.Target != pickup, "F detection does not grab an item half a metre off aim", report);
                    var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    blocker.name = "Temporary closed drawer face"; blocker.transform.position = Vector3.Lerp(camera.transform.position, pickup.GetComponent<BoxCollider>().bounds.center, 0.65f);
                    blocker.transform.localScale = new Vector3(0.7f, 0.7f, 0.12f); Physics.SyncTransforms();
                    pivot.LookAt(pickup.GetComponent<BoxCollider>().bounds.center); interactor.UpdateTarget();
                    Require(interactor.Target == null && !interactor.TryPickup() && !pickup.IsClaimed, "Closed surface blocks F pickup even within assist volume", report);
                    Object.Destroy(blocker); yield return null; Physics.SyncTransforms();
                }
                pivot.LookAt(pickup.GetComponent<BoxCollider>().bounds.center); Physics.SyncTransforms();
                bool picked = interactor.TryPickup();
                if (!picked)
                {
                    var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                    report.AppendLine("Pickup diagnostics: alive=" + inventory.IsAlive + " origin=" + ray.origin + " direction=" + ray.direction + " target=" + pickup.GetComponent<BoxCollider>().bounds);
                    foreach (var hit in Physics.RaycastAll(ray, 3, ~0, QueryTriggerInteraction.Ignore)) report.AppendLine("Hit " + hit.collider.name + " distance=" + hit.distance);
                }
                Require(picked, "Center-camera F pickup succeeds: " + pickup.name, report);
                Require(!pickup.TryCollect(inventory), "Same world syringe cannot be acquired twice", report);
                yield return null;
            }
            Require(inventory.IsFull && Enumerable.Range(0, 3).All(i => inventory.ItemAt(i) == FarmingItemKind.Syringe), "Three non-stacking syringes occupy slots 4/5/6", report);
            var hotbarRoot = player.transform.Find("FPS HUD/Farming Hotbar");
            var group = hotbarRoot.GetComponent<CanvasGroup>();
            foreach (int slot in new[] { 4, 5, 6, 6 })
            {
                group.alpha = 0; equipment.Process(slot, false, false);
                Require(group.alpha == 1 && inventory.SelectedIndex == slot - 4, "Slot selection/reselection wakes hidden hotbar: " + slot, report);
                var cell = hotbarRoot.Find("Slot " + slot).GetComponent<RectTransform>();
                var icon = cell.Find("Icon").GetComponent<Image>();
                Require(cell.sizeDelta.x == cell.sizeDelta.y && icon.enabled && icon.sprite != null && cell.Find("Label").GetComponent<Text>().text == slot.ToString(), "Square slot uses item image and number without item name: " + slot, report);
            }
            var healthFill = player.transform.Find("FPS HUD/Health Fill").GetComponent<Image>();
            Require(healthFill.sprite == null && healthFill.rectTransform.sizeDelta.x == 400 && !player.transform.Find("FPS HUD/Health Background").GetComponent<Image>().enabled, "HP bar is 400 wide with no black background/end caps", report);
            var extra = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FpsFarmingSetup.PickupPath)).GetComponent<FpsWorldPickup>();
            extra.transform.position = player.transform.position + new Vector3(0, 0.08f, 1.1f); Physics.SyncTransforms(); pivot.LookAt(extra.GetComponent<BoxCollider>().bounds.center);
            Require(!interactor.TryPickup() && extra.gameObject.activeSelf && !extra.IsClaimed, "Full hotbar rejects F pickup and keeps world item", report);
            Object.Destroy(extra.gameObject);
            Teleport(player, new Vector3(0, 0.02f, -3)); pivot.localRotation = Quaternion.identity;
            foreach (int slot in new[] { 4, 5, 6 })
            {
                equipment.Process(slot, false, false); itemView.Refresh(0.5f); weapon.Refresh(0.5f);
                Require(inventory.SelectedIndex == slot - 4 && itemView.IsVisible && !weapon.IsVisible, "Farming selection displays syringe and hides pistol: " + slot, report);
            }
            Require(!inventory.TryStartUse() && inventory.IsFull, "Full HP prevents use without consuming", report);
            equipment.Process(1, false, false); itemView.Refresh(0.1f); weapon.Refresh(0.05f);
            Require(!itemView.IsVisible && weapon.IsVisible && !interactor.TryDrop(), "Slot 1 hides syringe and cannot Q-drop pistol", report);
            float lowY = camera.transform.Find("WeaponRoot").localPosition.y; weapon.Refresh(0.3f);
            Require(camera.transform.Find("WeaponRoot").localPosition.y > lowY + 0.15f, "Pistol rises from below within 0.3 second equip transition", report);
            equipment.Process(4, false, false); weapon.Refresh(0); equipment.Process(2, false, false); weapon.Refresh(0);
            Require(!weapon.IsVisible && !itemView.IsVisible && !interactor.TryDrop(), "Rapid equip cancellation and melee Q do not create items", report);
            equipment.Process(3, false, false); Require(!interactor.TryDrop(), "Unique item slot cannot Q-drop", report);
            equipment.Process(4, false, false); health.TakeDamage(50);
            float itemLowY = itemView.ItemRoot.localPosition.y;
            Require(!inventory.TryStartUse(), "Item use is blocked at start of equip", report);
            itemView.Refresh(0.2f);
            Require(!itemView.CanUse && !inventory.TryStartUse(), "Item use is blocked halfway through equip", report);
            itemView.Refresh(0.2f);
            Require(itemView.CanUse && itemView.ItemRoot.localPosition.y > itemLowY + 0.3f, "Item rises and becomes usable after its separate 0.4-second equip", report);
            Require(inventory.TryStartUse() && !inventory.TryStartUse() && !interactor.TryDrop(), "Use starts once; duplicate use and Q are blocked", report);
            for (float time = 0; time < 0.5f; time += Time.deltaTime) { inventory.TickUse(Time.deltaTime, true, false); yield return null; }
            equipment.Process(5, false, false);
            inventory.TickUse(0, true, false);
            itemView.Refresh(0.5f);
            Require(!inventory.IsUsing && health.Current == 50 && inventory.ItemAt(0) == FarmingItemKind.Syringe, "Changing slot cancels use without healing or consuming", report);
            var pump = itemView.ItemRoot.GetChild(0).Find("Pump");
            Require(pump != null && itemView.PumpProgress == 0 && pump.localPosition.sqrMagnitude < 0.000001f, "Cancelling use restores the original pump position", report);
            Require(inventory.TryStartUse(), "Next selected syringe can start after cancellation", report);
            for (float time = 0; time < 1.5f; time += Time.deltaTime) { inventory.TickUse(Time.deltaTime, true, false); yield return null; }
            Require(health.Current == 50 && inventory.ItemAt(1) == FarmingItemKind.Syringe && inventory.IsUsing, "No gradual healing or early consumption during 3-second use", report);
            Require(Vector3.Dot(itemView.ItemRoot.forward, camera.transform.forward) < -0.7f, "Use pose turns syringe needle toward player", report);
            Require(itemView.PumpProgress > 0.2f && itemView.PumpProgress < 0.7f && pump.localPosition.z > 0.01f && pump.localPosition.z < 0.04f, "Syringe Pump child depresses gradually at half use progress", report);
            Require(itemView.WorldItem != null && itemView.WorldItem.gameObject.activeSelf, "Held syringe has active world representation", report);
            Require(Mathf.Abs(itemView.WorldItem.GetChild(0).Find("Pump").localPosition.z - pump.localPosition.z) < 0.001f, "World syringe pump follows the same use progress", report);
            ScreenCapture.CaptureScreenshot("Assets/Player/Validation/Syringe_Using_HUD.png");
            bool pumpCaptured = false;
            while (inventory.IsUsing)
            {
                if (!pumpCaptured && inventory.UseProgress > 0.85f) { ScreenCapture.CaptureScreenshot("Assets/Player/Validation/Syringe_Pump_Pressed.png"); pumpCaptured = true; }
                inventory.TickUse(Time.deltaTime, true, false); yield return null;
            }
            Require(health.Current == 80 && inventory.ItemAt(1) == FarmingItemKind.None && hud.HealthText == "HP 80 / 100", "At completion, heal max HP 30% once and empty only selected slot", report);
            yield return null;
            Require(!itemView.IsVisible, "Consumed syringe disappears from FPS view", report);
            Require(itemView.PumpProgress == 0 && pump.localPosition.sqrMagnitude < 0.000001f, "Consumption resets pump for the next syringe", report);
            equipment.Process(4, false, false); itemView.Refresh(0.5f); Require(inventory.TryStartUse(), "Second syringe use starts at 80 HP", report);
            while (inventory.IsUsing) { inventory.TickUse(Time.deltaTime, true, false); yield return null; }
            Require(health.Current == 100 && inventory.ItemAt(0) == FarmingItemKind.None && inventory.ItemAt(2) == FarmingItemKind.Syringe, "80 HP plus syringe clamps to 100 and consumes one", report);
            Require(inventory.TryAdd(FarmingItemKind.Syringe) && inventory.TryAdd(FarmingItemKind.Syringe), "New pickups fill earlier empty slots first", report);
            var holdChecks = CheckHeldAndOtherPlayer(player, report);
            while (holdChecks.MoveNext()) yield return holdChecks.Current;
            equipment.Process(4, false, false); itemView.Refresh(0.5f);
            Move(motor, Vector2.zero, false, false, 1);
            for (float time = 0; time < 0.4f; time += Time.deltaTime) yield return null;
            var posedAnimator = player.GetComponentInChildren<Animator>(); var handPose = posedAnimator.GetComponent<FpsHeldItemPose>();
            Require(itemView.WorldItem.parent == posedAnimator.GetBoneTransform(HumanBodyBones.RightHand) && itemView.WorldItem.gameObject.activeSelf, "World syringe is mounted on the original right hand", report);
            Require(handPose.RightWeight > 0.99f && handPose.LeftWeight < 0.01f && Vector3.Distance(posedAnimator.GetBoneTransform(HumanBodyBones.RightHand).position, handPose.RightTarget) < 0.08f, "Syringe uses only right-hand grip IK", report);
            Require(Vector3.Dot(itemView.WorldItem.forward, player.transform.forward) > 0.97f, "World syringe needle follows the forward hand grip", report);
            CaptureObserver(player, "Syringe_Grip_Observer.png", true);
            hud.SendMessage("Update");
            Require(player.transform.Find("FPS HUD/Context").GetComponent<Text>().text == "", "Holding syringe shows no central control-key instructions", report);
            Require(itemView.ItemRoot.Find("FPS Right Glove").GetComponent<MeshFilter>().sharedMesh != null && camera.transform.Find("WeaponRoot/FPS Right Glove").GetComponent<MeshFilter>().sharedMesh != null, "Existing astronaut glove copy is attached to syringe and pistol", report);
            bool localVisible = false, observerHidden = false;
            var syringeRenderers = itemView.ItemRoot.GetComponentsInChildren<Renderer>(true);
            var worldSyringeRenderers = itemView.WorldItem.GetComponentsInChildren<Renderer>(true);
            System.Action<ScriptableRenderContext, Camera> observe = (context, rendered) =>
            {
                if (rendered == camera) localVisible = syringeRenderers.All(r => !r.forceRenderingOff) && worldSyringeRenderers.All(r => r.forceRenderingOff);
                else observerHidden = syringeRenderers.All(r => r.forceRenderingOff) && worldSyringeRenderers.All(r => !r.forceRenderingOff);
            };
            RenderPipelineManager.beginCameraRendering += observe;
            try { Capture(camera, "Syringe_Held_FPS.png"); CaptureObserver(player, "Syringe_Observer.png"); }
            finally { RenderPipelineManager.beginCameraRendering -= observe; }
            Require(localVisible && observerHidden && syringeRenderers.All(r => !r.forceRenderingOff), "Local camera sees FPS syringe only; observer sees hand-mounted world syringe only; flags restore", report);
            ScreenCapture.CaptureScreenshot("Assets/Player/Validation/Farming_Full_HUD.png");
            for (float time = 0; time < 3.8f; time += Time.unscaledDeltaTime) yield return null;
            Require(hud.HotbarAlpha < 0.01f && inventory.IsFull && inventory.SelectedIndex == 0, "Hotbar fades after inactivity while preserving contents/selection", report);
            inventory.ShowHotbar(); Require(hud.HotbarAlpha == 1, "Selecting same farming slot wakes hotbar immediately", report);
            // Check spawn clearance against a real temporary wall, then drop into free floor space.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary drop obstruction";
            wall.transform.position = player.transform.position + new Vector3(0, 1, 0.6f); wall.transform.localScale = new Vector3(2, 2, 0.15f); Physics.SyncTransforms();
            Require(!interactor.TryDrop() && inventory.IsFull, "Wall blocks Q drop without losing item", report);
            Object.Destroy(wall); yield return null; Physics.SyncTransforms();
            Require(interactor.TryDrop() && inventory.ItemAt(0) == FarmingItemKind.None, "Q creates a physical world syringe and clears selected slot", report);
            var dropped = interactor.LastDropped;
            Require(dropped.IsDropped && !dropped.GetComponent<Rigidbody>().isKinematic && !dropped.TryCollect(inventory), "Dropped item uses Rigidbody and rejects immediate reacquisition", report);
            // Move with the item during lockout: even after cooldown the owner must leave first.
            for (float time = 0; time < 1.5f; time += Time.deltaTime)
            {
                var position = dropped.transform.position; Teleport(player, new Vector3(position.x + 0.45f, 0.02f, position.z));
                yield return null;
            }
            Require(dropped != null && !dropped.IsClaimed && !dropped.DropperLeftRange && inventory.ItemAt(0) == FarmingItemKind.None, "Cooldown alone does not auto-pickup while dropper remains in range", report);
            var dropPosition = dropped.transform.position;
            Require(dropPosition.y < 0.3f && dropPosition.y > -0.1f, "Rigidbody syringe falls onto test floor", report);
            Teleport(player, new Vector3(dropPosition.x + 2, 0.02f, dropPosition.z));
            for (float time = 0; time < 0.3f; time += Time.deltaTime) yield return null;
            Require(dropped.DropperLeftRange, "Dropped item arms after owner leaves trigger range", report);
            inventory.TryAdd(FarmingItemKind.Syringe);
            Teleport(player, new Vector3(dropPosition.x + 0.45f, 0.02f, dropPosition.z));
            for (float time = 0; time < 0.4f; time += Time.deltaTime) yield return null;
            Require(dropped != null && !dropped.IsClaimed && inventory.IsFull, "Full inventory blocks dropped-item auto-pickup", report);
            inventory.TryRemoveSelected(out _);
            for (float time = 0; time < 0.4f; time += Time.deltaTime) yield return null;
            Require((dropped == null || dropped.IsClaimed) && inventory.IsFull, "Returning to dropped syringe auto-picks it up without F when space opens", report);
            var dot = player.transform.Find("FPS HUD/Center Dot").GetComponent<RectTransform>();
            Require(dot.anchorMin == Vector2.one * 0.5f && dot.anchorMax == dot.anchorMin && dot.anchoredPosition == Vector2.zero && dot.GetComponent<Image>().sprite != null, "White dot is anchored to exact screen center with circle sprite", report);
            var hpRect = player.transform.Find("FPS HUD/Health").GetComponent<RectTransform>();
            Require(hpRect.anchorMin == new Vector2(0, 1) && player.GetComponentInChildren<CanvasScaler>().uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize, "HP remains top-left and HUD scales with resolution", report);
            itemView.Refresh(0.5f); health.TakeDamage(50); Require(inventory.TryStartUse(), "Death-cancellation test starts a syringe", report);
            health.TakeDamage(10000); inventory.TickUse(0, true, false); yield return null;
            Require(health.IsDead && health.Current == 0 && motor.IsDead && !inventory.IsUsing && inventory.IsFull && !itemView.IsVisible && !health.Heal(100), "Zero HP invokes existing death, cancels use, preserves syringe and blocks resurrection", report);
            Require(File.Exists("Assets/Player/Validation/Syringe_Using_HUD.png") && File.Exists("Assets/Player/Validation/Farming_Full_HUD.png"), "Actual Game View HUD screenshots saved", report);
        }
        static IEnumerator CheckHeldAndOtherPlayer(GameObject player, StringBuilder report)
        {
            var inventory = player.GetComponent<FpsInventory>(); var health = player.GetComponent<FpsHealth>();
            var equipment = player.GetComponent<FpsEquipment>(); var interactor = player.GetComponent<FpsItemInteractor>();
            var motor = player.GetComponent<FpsMotor>(); var view = player.GetComponent<FpsItemView>();
            var camera = player.GetComponentInChildren<Camera>(true); var pivot = camera.transform.parent;
            equipment.Process(4, false, false); view.Refresh(0.5f); health.TakeDamage(20);
            Require(inventory.TryStartUse(), "LMB held self-use begins", report);
            var before = player.transform.position; Move(motor, Vector2.right, true, false);
            Require(Vector3.Distance(before, player.transform.position) < 0.4f && !motor.IsRunning, "Syringe use slows movement to 15 percent and blocks sprint", report);
            inventory.TickUse(1, true, false); inventory.TickUse(0, false, false);
            Require(!inventory.IsUsing && health.Current == 80 && inventory.IsFull, "Releasing LMB before 3 seconds cancels without heal/consumption", report);
            before = player.transform.position; Move(motor, Vector2.right, false, false);
            Require(Vector3.Distance(before, player.transform.position) > 2, "Cancelling use restores normal movement speed", report);
            health.Heal(100); Teleport(player, new Vector3(0, 0.02f, -3));
            var other = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FpsAssetSetup.PrefabPath), new Vector3(0, 0.02f, -1.4f), Quaternion.Euler(0, 180, 0));
            other.name = "Temporary other player";
            try
            {
                foreach (var behaviour in other.GetComponents<MonoBehaviour>()) if (!(behaviour is FpsHealth) && !(behaviour is FpsLocalVisibility)) behaviour.enabled = false;
                other.GetComponentInChildren<Camera>(true).enabled = false; other.GetComponentInChildren<AudioListener>(true).enabled = false;
                other.transform.Find("FPS HUD").gameObject.SetActive(false);
                var recipient = other.GetComponent<FpsHealth>(); recipient.TakeDamage(50);
                Physics.SyncTransforms(); pivot.LookAt(other.GetComponent<CharacterController>().bounds.center);
                Require(interactor.FindUseTarget() == recipient && inventory.TryStartUse(recipient), "RMB selects nearby other player and begins held healing", report);
                inventory.TickUse(0.8f, false, true); view.Refresh(0.5f);
                Require(view.ItemRoot.localPosition.z > 0.4f && Vector3.Dot(view.ItemRoot.forward, camera.transform.forward) > 0.8f, "Other-player use extends syringe forward", report);
                inventory.TickUse(0, false, false);
                Require(!inventory.IsUsing && recipient.Current == 50 && inventory.IsFull, "Releasing RMB cancels target healing without consuming", report);
                Require(inventory.TryStartUse(recipient), "Can restart other-player use after release", report);
                other.transform.position += Vector3.forward * 3; Physics.SyncTransforms(); inventory.TickUse(0.1f, false, true);
                Require(!inventory.IsUsing && recipient.Current == 50 && inventory.IsFull, "Target moving beyond 2.2m cancels healing", report);
                other.transform.position = new Vector3(0, 0.02f, -1.4f); Physics.SyncTransforms();
                Require(inventory.TryStartUse(recipient), "Other player can be targeted again in range", report);
                var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                barrier.transform.position = player.transform.position + new Vector3(0, 1, 0.8f); barrier.transform.localScale = new Vector3(1, 2, 0.15f); Physics.SyncTransforms();
                inventory.TickUse(0.1f, false, true);
                Require(!inventory.IsUsing && inventory.IsFull, "A wall appearing between players cancels target healing", report);
                Object.Destroy(barrier); yield return null; Physics.SyncTransforms();
                Require(inventory.TryStartUse(recipient), "Target healing restarts after obstruction clears", report);
                equipment.Process(5, false, false); inventory.TickUse(0, false, true);
                Require(!inventory.IsUsing && inventory.IsFull, "Switching slots cancels other-player healing", report);
                equipment.Process(4, false, false); view.Refresh(0.5f); Require(inventory.TryStartUse(recipient), "Other-player completion test starts", report);
                for (float time = 0; time < 1.5f; time += Time.deltaTime) { inventory.TickUse(Time.deltaTime, false, true); yield return null; }
                Require(recipient.Current == 50 && health.Current == 100, "Target healing is deferred until full hold completes", report);
                ScreenCapture.CaptureScreenshot("Assets/Player/Validation/Syringe_OtherPlayer_HUD.png");
                while (inventory.IsUsing) { inventory.TickUse(Time.deltaTime, false, true); yield return null; }
                Require(recipient.Current == 80 && health.Current == 100 && inventory.ItemAt(0) == FarmingItemKind.None, "RMB 3-second hold heals only target by 30 and consumes source syringe once", report);
                inventory.TryAdd(FarmingItemKind.Syringe);
            }
            finally { Object.Destroy(other); pivot.localRotation = Quaternion.identity; }
            yield return null;
        }
    }
}
