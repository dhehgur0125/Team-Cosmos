/* [ 코드 설명 ] 기존 Character_test에서 의료 키트의 시간·취소·횟수·표시와 기존 장비를 검사합니다. 별도 런타임 호스트/클라이언트를 로컬 UDP로 연결해 기존 HP 소유권과 의료 키트 RPC도 확인하며 씬·네트워크 설정 에셋은 저장하지 않습니다. */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static class FpsMedicalKitValidation
    {
        const string Pending = "VoidLast.MedicalKit.Validation";
        static IEnumerator checks;
        static StringBuilder report;
        static int frame, errors;
        static bool background;
        static NetworkManager host, client;
        public static string Progress { get; private set; }
        static FpsMedicalKitValidation() => EditorApplication.playModeStateChanged += Changed;
        [MenuItem("VOID LAST/Player/Validate Medical Kit")]
        public static void Begin() => Start(false);
        public static void BeginPreview() => Start(true);
        static void Start(bool preview)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            SessionState.SetString(Pending + ".Start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(Pending + ".Preview", preview);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(FpsAssetSetup.ScenePath);
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
            report = new StringBuilder("Medical kit - Character_test Play Mode\n" + DateTime.UtcNow.ToString("O") + "\n");
            background = Application.runInBackground; Application.runInBackground = true; frame = -1; errors = 0;
            Application.logMessageReceived += Log; checks = SessionState.GetBool(Pending + ".Preview", false) ? Preview() : Checks(); EditorApplication.update += Tick;
        }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; report.AppendLine("ERROR: " + message); } }
        static void Tick()
        {
            if (Time.frameCount == frame) return; frame = Time.frameCount;
            try { if (checks.MoveNext()) return; }
            catch (Exception e) { report.AppendLine("FAILED: " + e); Debug.LogException(e); }
            EditorApplication.update -= Tick; (checks as IDisposable)?.Dispose();
            if (client != null) client.Shutdown(); if (host != null) host.Shutdown();
            Application.runInBackground = background; Application.logMessageReceived -= Log;
            File.WriteAllText("Assets/Player/Validation/" + (SessionState.GetBool(Pending + ".Preview", false) ? "MedicalKitVisualResults.txt" : "MedicalKitResults.txt"), report.ToString()); EditorApplication.ExitPlaymode();
        }
        static void Check(bool ok, string message) { Progress = message; report.AppendLine((ok ? "PASS: " : "FAIL: ") + message); if (!ok) throw new InvalidOperationException(message); }
        static IEnumerator Wait(float seconds) { float end = Time.time + seconds; while (Time.time < end) yield return null; }
        static void Quiet(GameObject player)
        {
            player.GetComponent<FpsInput>().enabled = false; player.GetComponent<FpsLook>().enabled = false;
            player.GetComponent<FpsMotor>().enabled = false; player.GetComponent<FpsEquipment>().enabled = false;
            foreach (var listener in player.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        }
        static void Place(GameObject player, Vector3 position)
        {
            var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false;
            player.transform.SetPositionAndRotation(position, Quaternion.identity); capsule.enabled = true; Physics.SyncTransforms();
        }
        static IEnumerator Checks()
        {
            var player = Object.FindAnyObjectByType<FpsMedicalKit>().gameObject; Quiet(player);
            Place(player, new Vector3(1000, 1000, 1000));
            var kit = player.GetComponent<FpsMedicalKit>(); var view = player.GetComponent<FpsMedicalKitView>();
            var health = player.GetComponent<FpsHealth>(); var equip = player.GetComponent<FpsEquipment>();
            var camera = player.GetComponentInChildren<Camera>(true);
            health.TakeDamage(70); equip.Process(3, false, false);
            Check(!view.IsEquipped && !kit.TryStartUse(), "Slot 3 cannot heal before equip finishes");
            var wait = Wait(0.5f); while (wait.MoveNext()) yield return null;
            Check(view.IsEquipped && kit.RemainingUses == 3 && kit.TryStartUse(), "Slot 3 equips from below and starts self treatment with three uses");
            float begin = Time.time;
            wait = Wait(0.8f); while (wait.MoveNext()) yield return null;
            Check(health.Current == 30 && kit.RemainingUses == 3 && kit.IsUsing, "No early healing or consumption during 1.5-second treatment");
            FpsValidation.Capture(camera, "MedicalKit_Self.png");
            wait = Wait(0.8f); while (wait.MoveNext()) yield return null;
            Check(Time.time - begin >= 1.5f && health.Current == 80 && kit.RemainingUses == 2 && !kit.IsUsing, "Self treatment heals exactly 50 after 1.5 seconds and consumes once");
            Check(kit.TryStartUse(), "Second self treatment can start");
            equip.Process(1, false, false);
            Check(!kit.IsUsing && kit.RemainingUses == 2 && health.Current == 80, "Changing equipment cancels without healing or consuming");

            var target = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FpsAssetSetup.PrefabPath)); Quiet(target);
            target.GetComponentInChildren<Camera>(true).enabled = false; target.transform.Find("FPS HUD").gameObject.SetActive(false);
            Place(target, player.transform.position + Vector3.forward * 1.7f);
            var targetHealth = target.GetComponent<FpsHealth>(); targetHealth.TakeDamage(60);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary medical LOS blocker";
            wall.transform.localScale = new Vector3(3, 3, 0.1f); wall.transform.position = player.transform.position + new Vector3(0, 1, 0.8f); wall.SetActive(false);
            try
            {
                equip.Process(3, false, false); view.Refresh(0.5f);
                camera.transform.parent.LookAt(target.GetComponent<Collider>().bounds.center);
                Check(player.GetComponent<FpsItemInteractor>().FindUseTarget() == targetHealth && kit.TryStartUse(targetHealth), "Camera-center teammate detection starts treatment within 2m");
                Place(target, player.transform.position + Vector3.forward * 3); kit.TickUse(0);
                Check(!kit.IsUsing && kit.RemainingUses == 2 && targetHealth.Current == 40, "Leaving range cancels without consumption");
                Place(target, player.transform.position + Vector3.forward * 1.7f);
                wall.SetActive(true); Physics.SyncTransforms();
                Check(!kit.TryStartUse(targetHealth), "Wall blocks treatment start");
                wall.SetActive(false); Physics.SyncTransforms(); Check(kit.TryStartUse(targetHealth), "Visible teammate can be treated again");
                wall.SetActive(true); Physics.SyncTransforms(); kit.TickUse(0);
                Check(!kit.IsUsing && kit.RemainingUses == 2, "A wall appearing during treatment cancels it");
                wall.SetActive(false); Physics.SyncTransforms(); Check(kit.TryStartUse(targetHealth), "Teammate treatment restarts");
                wait = Wait(0.7f); while (wait.MoveNext()) yield return null;
                FpsValidation.Capture(camera, "MedicalKit_Team.png"); FpsValidation.CaptureObserver(player, "MedicalKit_World.png", true);
                wait = Wait(0.9f); while (wait.MoveNext()) yield return null;
                Check(targetHealth.Current == 90 && kit.RemainingUses == 1, "Teammate gains 50 and shares the same remaining uses");
                targetHealth.Heal(100); Check(!kit.TryStartUse(targetHealth), "Full-health teammate is rejected");
                Check(kit.TryStartUse(), "Final self treatment starts");
                wait = Wait(1.6f); while (wait.MoveNext()) yield return null;
                Check(health.Current == 100 && kit.RemainingUses == 0 && !kit.TryStartUse(), "Final use clamps HP to maximum and shows zero uses");
                health.TakeDamage(50); Check(!kit.TryStartUse(), "Empty medical kit cannot heal an injured player");
                camera.transform.parent.localRotation = Quaternion.identity;
                FpsValidation.Capture(camera, "MedicalKit_Held.png");
                ScreenCapture.CaptureScreenshot("Assets/Player/Validation/MedicalKit_HUD.png");
                yield return null;

                var inventory = player.GetComponent<FpsInventory>(); inventory.TryAdd(FarmingItemKind.Syringe);
                equip.Process(4, false, false); player.GetComponent<FpsItemView>().Refresh(0.5f);
                Check(!view.LocalRoot.gameObject.activeSelf && inventory.UseDuration == 3 && inventory.TryStartUse(), "Farming syringe still equips separately and uses its existing three-second action");
                inventory.TickUse(3, true, false);
                Check(health.Current == 80 && inventory.ItemAt(0) == FarmingItemKind.None, "Existing syringe heals 30 and consumes only that item");
                equip.Process(1, false, false); player.GetComponent<FpsWeaponView>().Refresh(0.5f);
                var pistol = player.GetComponent<FpsPistol>();
                Check(pistol.TryFire() && pistol.Magazine == 9 && pistol.Damage == 30 && pistol.TryReload(), "Pistol firing, damage and reload still operate after medical kit use");
                pistol.TickReload(pistol.ReloadDuration);
                var flashlight = player.GetComponent<FpsWeaponFlashlight>(); flashlight.Toggle();
                wait = Wait(0.25f); while (wait.MoveNext()) yield return null;
                Check(flashlight.IsLit && pistol.Magazine == 10, "Flashlight and reload completion remain functional"); flashlight.Toggle();
                kit.enabled = false; equip.Process(3, false, false); view.Refresh(1);
                Check(!view.LocalRoot.gameObject.activeSelf && !view.IsEquipped, "Disabling medical component leaves slot 3 empty safely");
                Object.Destroy(kit); yield return null; view.Refresh(1);
                equip.Process(1, false, false); player.GetComponent<FpsWeaponView>().Refresh(1);
                Check(player.GetComponent<FpsWeaponView>().IsEquipped, "Removing the medical component leaves pistol and base slots operational");
            }
            finally { Object.Destroy(wall); Object.Destroy(target); }
            player.SetActive(false);
            var network = NetworkChecks(); while (network.MoveNext()) yield return network.Current;
            Check(errors == 0, "No new Console error or exception during these tests");
            report.AppendLine("COMPLETE. Local UDP host/client tested in Editor; Relay, Lobby voice and separate machines were not tested or changed.");
        }
        static NetworkManager Manager(string name)
        {
            var go = new GameObject(name); var manager = go.AddComponent<NetworkManager>(); var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 18777, "127.0.0.1");
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FpsMedicalKitSetup.NetworkPath), EnableSceneManagement = false, ConnectionApproval = false };
            return manager;
        }
        static IEnumerator Preview()
        {
            var kit = Object.FindAnyObjectByType<FpsMedicalKit>(); var player = kit.gameObject; Quiet(player);
            var motor = player.GetComponent<FpsMotor>(); motor.enabled = true;
            var camera = player.GetComponentInChildren<Camera>();
            camera.transform.parent.localRotation = Quaternion.identity;
            var view = player.GetComponent<FpsMedicalKitView>(); player.GetComponent<FpsEquipment>().Process(3, false, false);
            var wait = Wait(0.6f); while (wait.MoveNext()) yield return null;
            var canvas = player.transform.Find("FPS HUD").GetComponent<Canvas>();
            var mode = canvas.renderMode; var oldCamera = canvas.worldCamera;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 0.1f;
                Canvas.ForceUpdateCanvases();
                FpsValidation.Capture(camera, "MedicalKit_FinalHeld.png");
                var label = player.transform.Find("FPS HUD/Medical Kit Uses").GetComponent<UnityEngine.UI.Text>();
                Check(label.enabled && label.text == "의료 키트 3 / 3" && label.font.HasCharacter('의'), "Korean use label displays only for selected local medical kit");
                player.GetComponent<FpsHealth>().TakeDamage(70); Check(kit.TryStartUse(), "Preview self treatment starts");
                wait = Wait(0.7f); while (wait.MoveNext()) yield return null;
                FpsValidation.Capture(camera, "MedicalKit_FinalSelf.png");
                wait = Wait(1f); while (wait.MoveNext()) yield return null;
                Check(label.text == "의료 키트 2 / 3", "Successful treatment updates the local label");
                FpsValidation.Capture(camera, "MedicalKit_HUD.png");
                canvas.renderMode = mode; canvas.worldCamera = oldCamera;
                ScreenCapture.CaptureScreenshot("Assets/Player/Validation/MedicalKit_FinalGameHUD.png");
                wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
                player.GetComponent<FpsEquipment>().Process(4, false, false); view.Refresh(0.1f);
                Check(!label.enabled, "Medical label disappears outside slot 3");
                Check(errors == 0, "No presentation Console errors");
            }
            finally { canvas.renderMode = mode; canvas.worldCamera = oldCamera; }
        }
        static IEnumerator NetworkChecks()
        {
            host = Manager("Temporary medical host"); Check(host.StartHost(), "Existing network prefab starts as a local UDP host");
            client = Manager("Temporary medical client"); Check(client.StartClient(), "Second NGO instance starts as a local UDP client");
            float timeout = Time.time + 12;
            while ((!client.IsConnectedClient || host.ConnectedClients.Count < 2 || client.SpawnManager.SpawnedObjects.Count < 2) && Time.time < timeout) yield return null;
            Check(client.IsConnectedClient && host.ConnectedClients.Count == 2, "Actual host/client transport connection established");
            var ownerHost = host.LocalClient.PlayerObject; var ownerClient = client.LocalClient.PlayerObject;
            foreach (var manager in new[] { host, client }) foreach (var obj in manager.SpawnManager.SpawnedObjectsList)
            {
                if (!obj.IsPlayerObject) continue;
                Quiet(obj.gameObject); obj.GetComponentInChildren<Camera>(true).enabled = false;
                // Each peer's replica occupies a different temporary arena so their colliders cannot occlude each other in this single Editor physics world.
                var transformSync = obj.GetComponent<NetworkTransform>();
                transformSync.SyncPositionX = transformSync.SyncPositionY = transformSync.SyncPositionZ = false;
                transformSync.SyncRotAngleX = transformSync.SyncRotAngleY = transformSync.SyncRotAngleZ = false;
                transformSync.Interpolate = false;
            }
            var wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
            foreach (var manager in new[] { host, client }) foreach (var obj in manager.SpawnManager.SpawnedObjectsList)
                if (obj.IsPlayerObject) Place(obj.gameObject, new Vector3(manager == host ? 2000 : 2100, 1000, obj.OwnerClientId == 0 ? 0 : 1.7f));
            foreach (var owner in new[] { ownerHost, ownerClient }) { owner.GetComponent<FpsHealth>().TakeDamage(70); owner.GetComponent<FpsEquipment>().Process(3, false, false); }
            wait = Wait(0.6f); while (wait.MoveNext()) yield return null;
            foreach (var owner in new[] { ownerHost, ownerClient }) Check(owner.GetComponent<FpsMedicalKit>().TryStartUse(), "Network owner " + owner.OwnerClientId + " starts self treatment");
            wait = Wait(1.9f); while (wait.MoveNext()) yield return null;
            foreach (var manager in new[] { host, client }) foreach (var obj in manager.SpawnManager.SpawnedObjectsList)
                if (obj.IsPlayerObject) Check(obj.GetComponent<FpsHealth>().Current == 80 && obj.GetComponent<FpsMedicalKit>().RemainingUses == 2, "Both peers agree on owner " + obj.OwnerClientId + " self HP and uses");
            foreach (var owner in new[] { ownerHost, ownerClient })
            {
                var manager = owner == ownerHost ? host : client;
                var target = manager.SpawnManager.SpawnedObjectsList.Single(o => o.IsPlayerObject && o.OwnerClientId != owner.OwnerClientId);
                owner.GetComponentInChildren<Camera>(true).transform.parent.LookAt(target.GetComponent<Collider>().bounds.center);
                wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
                var kit = owner.GetComponent<FpsMedicalKit>();
                Check(kit.TryStartUse(target.GetComponent<FpsHealth>()), "Owner " + owner.OwnerClientId + " starts teammate treatment through server");
                Check(!kit.TryStartUse(target.GetComponent<FpsHealth>()), "Duplicate in-flight request is rejected");
                wait = Wait(1.9f); while (wait.MoveNext()) yield return null;
                foreach (var peer in new[] { host, client })
                {
                    var healerCopy = peer.SpawnManager.SpawnedObjects[owner.NetworkObjectId]; var targetCopy = peer.SpawnManager.SpawnedObjects[target.NetworkObjectId];
                    Check(healerCopy.GetComponent<FpsMedicalKit>().RemainingUses == 1 && targetCopy.GetComponent<FpsHealth>().Current == 100,
                        "Owner " + owner.OwnerClientId + " teammate HP and one-use deduction replicate to " + (peer == host ? "host" : "client"));
                }
            }
            ownerClient.GetComponent<FpsHealth>().TakeDamage(50);
            wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
            var clientKit = ownerClient.GetComponent<FpsMedicalKit>(); Check(clientKit.TryStartUse(), "Client starts cancellable network treatment");
            wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
            ownerClient.GetComponent<FpsEquipment>().Process(1, false, false);
            wait = Wait(1.7f); while (wait.MoveNext()) yield return null;
            Check(ownerClient.GetComponent<FpsHealth>().Current == 50 && clientKit.RemainingUses == 1, "Server cancellation preserves client HP and use count");
            client.Shutdown(); host.Shutdown();
        }
    }
}
