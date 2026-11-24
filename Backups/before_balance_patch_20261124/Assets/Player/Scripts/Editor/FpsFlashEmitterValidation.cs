/*
[ 코드 설명 ]
1. 빛 방출기 전용 테스트 씬에서 충전·취소·횟수·벽 차단·세 몬스터 스턴을 실제 Play Mode로 확인합니다.
2. 임시 로컬 UDP 호스트/클라이언트로 서버 판정과 기존 몬스터 상태 동기화를 검사합니다. 원본 씬과 네트워크 설정은 저장하지 않습니다.
*/
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using VoidLast.Monster;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static class FpsFlashEmitterValidation
    {
        const string Pending = "VoidLast.FlashEmitter.Validation";
        static IEnumerator checks;
        static StringBuilder report;
        static int frame, errors;
        static bool background;
        static NetworkManager host, client;
        public static string Progress { get; private set; }
        static FpsFlashEmitterValidation() => EditorApplication.playModeStateChanged += Changed;
        [MenuItem("VOID LAST/Player/Validate Flash Emitter")]
        public static void Begin() => Start(false);
        public static void BeginPreview() => Start(true);
        static void Start(bool preview)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            SessionState.SetString(Pending + ".Start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(Pending + ".Preview", preview);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(FpsFlashEmitterSetup.ScenePath);
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
            report = new StringBuilder("Flash emitter - actual Play Mode / local UDP host+client\n" + DateTime.UtcNow.ToString("O") + "\n");
            background = Application.runInBackground; Application.runInBackground = true; frame = -1; errors = 0;
            Application.logMessageReceived += Log; checks = SessionState.GetBool(Pending + ".Preview", false) ? Preview() : Checks(); EditorApplication.update += Tick;
        }
        static void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; report.AppendLine("ERROR: " + message); } }
        static void Tick()
        {
            if (Time.frameCount == frame) return; frame = Time.frameCount;
            try { if (checks.MoveNext()) return; }
            catch (Exception e) { report.AppendLine("FAILED: " + e); }
            EditorApplication.update -= Tick; (checks as IDisposable)?.Dispose();
            if (client != null) client.Shutdown(); if (host != null) host.Shutdown();
            Application.runInBackground = background; Application.logMessageReceived -= Log;
            File.WriteAllText("Assets/Player/Validation/" + (SessionState.GetBool(Pending + ".Preview", false) ? "FlashEmitterVisualResults.txt" : "FlashEmitterResults.txt"), report.ToString()); EditorApplication.ExitPlaymode();
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
            var emitter = Object.FindAnyObjectByType<FpsFlashEmitter>(); var player = emitter.gameObject; Quiet(player);
            var camera = player.GetComponentInChildren<Camera>(true); var view = emitter.View; var equipment = player.GetComponent<FpsEquipment>();
            var originalMonster = Object.FindAnyObjectByType<EyedMonsterAI>(); originalMonster.gameObject.SetActive(false);
            Place(player, new Vector3(0, 0.03f, -13)); camera.transform.parent.localRotation = Quaternion.identity;
            equipment.Process(1, false, false); equipment.Process(3, false, false);
            Check(!emitter.TryBeginCharge(), "Cannot charge during equip motion");
            var wait = Wait(0.5f); while (wait.MoveNext()) yield return null;
            Check(emitter.RemainingUses == 3 && view.IsEquipped && view.LocalRoot.gameObject.activeSelf && !view.GaugeVisible, "Capsule model equips with three uses; idle gauge hidden");
            FpsValidation.Capture(camera, "FlashEmitter_FinalHeld.png"); FpsValidation.CaptureObserver(player, "FlashEmitter_FinalWorld.png", true);
            Check(emitter.TryBeginCharge(), "Left press starts 1.5-second charge");
            wait = Wait(0.55f); while (wait.MoveNext()) yield return null;
            Check(view.GaugeVisible && emitter.ChargeProgress > 0.3f && emitter.ChargeProgress < 0.6f, "Gauge fills only while charging");
            Check(!emitter.ReleaseCharge() && emitter.RemainingUses == 3 && !view.GaugeVisible, "Early release cancels freely and immediately hides gauge");
            Check(emitter.TryBeginCharge(), "Charge can restart after cancellation"); equipment.Process(1, false, false);
            Check(!emitter.IsCharging && emitter.RemainingUses == 3 && !view.GaugeVisible, "Slot change cancels charge without consuming");
            equipment.Process(3, false, false); view.Refresh(1);
            Check(emitter.TryBeginCharge(), "Full charge begins");
            wait = Wait(1.65f); while (wait.MoveNext()) yield return null;
            Check(emitter.ChargeProgress == 1 && emitter.RemainingUses == 3 && view.FlashesPlayed == 0 && view.GaugeVisible, "Full charge holds without auto-firing");
            ScreenCapture.CaptureScreenshot("Assets/Player/Validation/FlashEmitter_FinalChargeHUD.png");
            FpsValidation.Capture(camera, "FlashEmitter_FinalCharged.png"); yield return null;
            Check(emitter.ReleaseCharge() && emitter.RemainingUses == 2 && !view.GaugeVisible && emitter.LastStunnedCount == 0, "Release emits once and consumes even with no target");
            wait = Wait(0.05f); while (wait.MoveNext()) yield return null;
            FpsValidation.Capture(camera, "FlashEmitter_FinalFlash.png");
            Check(!emitter.ReleaseCharge() && !emitter.TryBeginCharge() && emitter.CooldownRemaining > 4.8f, "Duplicate release and charging during five-second cooldown rejected");
            equipment.Process(1, false, false); equipment.Process(3, false, false); view.Refresh(1);
            Check(emitter.RemainingUses == 2 && emitter.CooldownRemaining > 4.7f, "Equipment switching preserves uses and cooldown");
            wait = Wait(5.1f); while (wait.MoveNext()) yield return null;
            Check(emitter.TryBeginCharge(), "Charge becomes available after five-second cooldown"); emitter.CancelCharge();
            emitter.cooldownDuration = 0;
            for (int i = 0; i < 2; i++)
            {
                Check(emitter.TryBeginCharge(), "Zero-cooldown charge " + (i + 1));
                wait = Wait(1.6f); while (wait.MoveNext()) yield return null;
                Check(emitter.ReleaseCharge() && emitter.CooldownRemaining == 0, "Zero cooldown allows immediate next use");
            }
            Check(emitter.RemainingUses == 0 && !emitter.TryBeginCharge() && !view.GaugeVisible && view.FlashesPlayed == 3, "Exactly three successful releases; exhausted device cannot charge");

            // Fresh runtime player for independent stun scenarios; no refill API is added to gameplay.
            player.SetActive(false);
            var fresh = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FpsFlashEmitterSetup.PlayerPath)); Quiet(fresh);
            Place(fresh, new Vector3(0, 0.03f, -13)); emitter = fresh.GetComponent<FpsFlashEmitter>(); view = emitter.View;
            view.Refresh(1); emitter.cooldownDuration = 0;
            var monster = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Monster/Prefabs/VOID_LAST_EyedLargeMonster.prefab"), new Vector3(0, 0.03f, -10.7f), Quaternion.Euler(0, 180, 0));
            var ai = monster.GetComponent<EyedMonsterAI>(); var melee = monster.GetComponent<MonsterMelee>(); var hp = monster.GetComponent<MonsterHealth>(); var agent = monster.GetComponent<NavMeshAgent>();
            ai.enabled = false; yield return null;
            Check(agent.isOnNavMesh, "Test monster uses the existing baked NavMesh");
            Check(emitter.TryBeginCharge(), "Charge before interrupting attack");
            wait = Wait(1.6f); while (wait.MoveNext()) yield return null;
            Check(melee.TryStart(fresh.GetComponent<FpsHealth>()), "Monster attack windup starts before flash");
            Check(emitter.ReleaseCharge() && emitter.LastStunnedCount == 1 && ai.State == EyedMonsterAI.Mode.Stunned && !melee.IsBusy && hp.Current == hp.Maximum, "Flash stuns without damage and cancels attack windup");
            ai.enabled = true; var stopPosition = monster.transform.position;
            wait = Wait(2); while (wait.MoveNext()) yield return null;
            Check(ai.State == EyedMonsterAI.Mode.Stunned && Vector3.Distance(monster.transform.position, stopPosition) < 0.02f && fresh.GetComponent<FpsHealth>().Current == 100, "Stunned monster stays still and cannot hurt nearby player");
            var stun = monster.GetComponent<MonsterStun>(); Check(stun.Apply(0.1f) && stun.Remaining > 2.7f, "Duplicate shorter stun does not shorten or corrupt active stun");
            Place(fresh, new Vector3(0, 0.03f, -14));
            wait = Wait(3.2f); while (wait.MoveNext()) yield return null;
            Check(!stun.IsStunned && ai.State != EyedMonsterAI.Mode.Stunned && agent.updateRotation, "After five seconds AI resumes normal behavior");
            ai.enabled = false; melee.Cancel(); agent.Warp(new Vector3(0, 0.03f, -10));
            Place(fresh, new Vector3(0, 0.03f, -13));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary flash LOS blocker"; wall.transform.position = new Vector3(0, 1.6f, -11.7f); wall.transform.localScale = new Vector3(4, 3.2f, 0.2f); Physics.SyncTransforms();
            Check(emitter.ApplyStun() == 0 && !stun.IsStunned, "Wall blocks stun inside radius"); wall.SetActive(false); Physics.SyncTransforms();
            agent.Warp(new Vector3(0, 0.03f, -6)); Physics.SyncTransforms();
            Check(emitter.ApplyStun() == 0, "Monster farther than five metres is excluded");
            agent.Warp(new Vector3(0, 0.03f, -9.5f)); Physics.SyncTransforms();
            Check(emitter.ApplyStun() == 1, "Visible monster inside five metres is affected");
            ai.enabled = true; hp.TakeHit(hp.Maximum, fresh, monster.transform.position);
            Check(!stun.IsStunned && ai.State == EyedMonsterAI.Mode.Dead && !stun.Apply(5), "Death during stun cannot resume AI or accept another stun");
            Object.Destroy(wall); Object.Destroy(monster);
            foreach (string kind in new[] { "Small", "Blind" })
            {
                var other = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Monster/Prefabs/VOID_LAST_" + kind + "Monster.prefab"), new Vector3(0, 0.03f, -9), Quaternion.identity);
                var otherAgent = other.GetComponent<NavMeshAgent>(); yield return null; Physics.SyncTransforms();
                Check(otherAgent.isOnNavMesh && emitter.ApplyStun() == 1, kind + " monster accepts shared stun");
                var otherStun = other.GetComponent<MonsterStun>(); var pos = other.transform.position;
                wait = Wait(5.2f); while (wait.MoveNext())
                {
                    if (otherStun.IsStunned) CheckStill(other.transform.position, pos);
                    yield return null;
                }
                Check(!otherStun.IsStunned && (kind == "Small" ? other.GetComponent<SmallMonsterAI>().State != SmallMonsterAI.Mode.Stunned : other.GetComponent<BlindMonsterAI>().State != BlindMonsterAI.Mode.Stunned), kind + " monster resumes after shared stun timer");
                Object.Destroy(other); yield return null;
            }
            Check(emitter.TryBeginCharge(), "Charge starts before player death"); fresh.GetComponent<FpsHealth>().TakeDamage(100);
            Check(!emitter.IsCharging && !view.GaugeVisible && emitter.RemainingUses == 2, "Player death cancels charge without consuming"); fresh.SetActive(false);
            var online = NetworkChecks(); while (online.MoveNext()) yield return online.Current;
            Check(errors == 0, "No Console errors or exceptions during completed tests");
            report.AppendLine("COMPLETE. Two actual NGO peers over local UDP in one Editor. Separate PCs / Relay / Lobby / Stage2 were not exercised or changed.");
        }
        static void CheckStill(Vector3 current, Vector3 stopped) { if (Vector3.Distance(current, stopped) > 0.03f) throw new InvalidOperationException("Monster moved during stun"); }
        static IEnumerator Preview()
        {
            var emitter = Object.FindAnyObjectByType<FpsFlashEmitter>(); Quiet(emitter.gameObject);
            var camera = emitter.GetComponentInChildren<Camera>(true); var view = emitter.View;
            emitter.GetComponent<FpsEquipment>().Process(3, false, false); view.Refresh(1);
            emitter.ProcessInput(true, true);
            var wait = Wait(1.6f); while (wait.MoveNext()) yield return null;
            Check(emitter.ChargeProgress == 1 && view.GaugeVisible, "Input hold reaches full charge");
            emitter.ProcessInput(false, false);
            Check(view.FlashesPlayed == 1 && !view.GaugeVisible, "Input release emits and hides gauge immediately");
            foreach (var particles in view.LocalRoot.GetComponentsInChildren<ParticleSystem>()) particles.Simulate(0.025f, true, false, true);
            FpsValidation.Capture(camera, "FlashEmitter_ReleaseBurst.png");
            var light = view.WorldRoot.GetComponentInChildren<Light>(true);
            Check(light != null && light.enabled && light.intensity > 0 && light.range == 4, "Short point-light burst has four-metre visual range");
            wait = Wait(0.35f); while (wait.MoveNext()) yield return null;
            Check(!light.enabled, "Flash light turns off after release pulse");
            Check(view.LocalRoot.GetComponentsInChildren<Collider>().Length == 0 && view.LocalRoot.GetComponentsInChildren<LineRenderer>().Length == 0, "Primitive equipment and effect have no collider or laser trail");
            emitter.GetComponent<FpsEquipment>().Process(1, false, false);
            emitter.GetComponent<FpsWeaponView>().Refresh(1);
            Check(!view.LocalRoot.gameObject.activeSelf && emitter.GetComponent<FpsWeaponView>().IsEquipped, "Pistol slot still equips and hides flash model");
            Check(errors == 0, "No presentation Console errors");
            report.AppendLine("COMPLETE. Final flash presentation inspected separately from completed gameplay and UDP checks.");
        }
        static NetworkManager Manager(string name)
        {
            var go = new GameObject(name); var manager = go.AddComponent<NetworkManager>(); var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 18779, "127.0.0.1");
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FpsFlashEmitterSetup.NetworkPath), EnableSceneManagement = false, ConnectionApproval = false };
            manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FpsFlashEmitterSetup.TestMonsterPath) });
            return manager;
        }
        static IEnumerator NetworkChecks()
        {
            host = Manager("Temporary flash host"); Check(host.StartHost(), "Local UDP host starts");
            client = Manager("Temporary flash client"); Check(client.StartClient(), "Local UDP client starts");
            float timeout = Time.time + 12;
            while ((!client.IsConnectedClient || host.ConnectedClients.Count < 2 || client.SpawnManager.SpawnedObjects.Count < 2) && Time.time < timeout) yield return null;
            Check(client.IsConnectedClient && host.ConnectedClients.Count == 2, "Two NGO peers connected through real transport");
            foreach (var manager in new[] { host, client }) foreach (var obj in manager.SpawnManager.SpawnedObjectsList)
            {
                if (!obj.IsPlayerObject) continue; Quiet(obj.gameObject); obj.GetComponentInChildren<Camera>(true).enabled = false;
                var sync = obj.GetComponent<NetworkTransform>(); sync.SyncPositionX = sync.SyncPositionY = sync.SyncPositionZ = false; sync.SyncRotAngleX = sync.SyncRotAngleY = sync.SyncRotAngleZ = false; sync.Interpolate = false;
            }
            var wait = Wait(0.4f); while (wait.MoveNext()) yield return null;
            var enemy = NetworkObject.InstantiateAndSpawn(AssetDatabase.LoadAssetAtPath<GameObject>(FpsFlashEmitterSetup.TestMonsterPath), host, position: new Vector3(0, 0.03f, -9.5f), rotation: Quaternion.identity);
            enemy.GetComponent<EyedMonsterAI>().enabled = false;
            timeout = Time.time + 5; while (!client.SpawnManager.SpawnedObjects.ContainsKey(enemy.NetworkObjectId) && Time.time < timeout) yield return null;
            Check(client.SpawnManager.SpawnedObjects.ContainsKey(enemy.NetworkObjectId), "Existing MonsterNetworkSync spawns on client");
            var enemyCopy = client.SpawnManager.SpawnedObjects[enemy.NetworkObjectId];
            // Both peers share one Editor physics world: remove only the client's duplicate colliders from these runtime tests.
            foreach (var c in enemyCopy.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var owner in new[] { host.LocalClient.PlayerObject, client.LocalClient.PlayerObject })
            {
                foreach (var manager in new[] { host, client }) foreach (var obj in manager.SpawnManager.SpawnedObjectsList)
                    if (obj.IsPlayerObject) Place(obj.gameObject, new Vector3((manager == client ? 2000 : 0) + (obj.OwnerClientId == owner.OwnerClientId ? 0 : 10), 0.03f, -13));
                owner.GetComponent<FpsEquipment>().Process(3, false, false);
                wait = Wait(0.6f); while (wait.MoveNext()) yield return null;
                var emitter = owner.GetComponent<FpsFlashEmitter>();
                Check(emitter.TryBeginCharge() && !emitter.TryBeginCharge(), "Owner " + owner.OwnerClientId + " starts one charge; duplicate begin rejected");
                wait = Wait(0.4f); while (wait.MoveNext()) yield return null;
                Check(emitter.View.GaugeVisible, "Charging owner's gauge is visible");
                foreach (var manager in new[] { host, client })
                {
                    var copy = manager.SpawnManager.SpawnedObjects[owner.NetworkObjectId];
                    if (!copy.IsOwner) Check(!copy.GetComponent<FpsFlashEmitterView>().GaugeVisible, "Remote replica never displays a charge gauge");
                }
                Check(!emitter.ReleaseCharge(), "Network early release cancels");
                wait = Wait(0.25f); while (wait.MoveNext()) yield return null;
                Check(emitter.RemainingUses == 3 && emitter.TryBeginCharge(), "Server preserves uses after cancellation");
                wait = Wait(1.8f); while (wait.MoveNext()) yield return null;
                Check(emitter.ChargeProgress == 1 && emitter.RemainingUses == 3 && emitter.ReleaseCharge() && !emitter.ReleaseCharge(), "Server-timed full charge releases only once");
                wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
                foreach (var manager in new[] { host, client })
                {
                    var copy = manager.SpawnManager.SpawnedObjects[owner.NetworkObjectId].GetComponent<FpsFlashEmitter>();
                    Check(copy.RemainingUses == 2 && copy.CooldownRemaining > 4 && copy.View.FlashesPlayed == 1, "Owner " + owner.OwnerClientId + " has synchronized one-use cost, cooldown and one flash on " + (manager == host ? "host" : "client"));
                }
                Check(enemy.GetComponent<EyedMonsterAI>().State == EyedMonsterAI.Mode.Stunned && enemyCopy.GetComponent<EyedMonsterAI>().State == EyedMonsterAI.Mode.Stunned && enemy.GetComponent<MonsterHealth>().Current == 300, "Server applies radius stun without damage; client receives Stunned state");
                Check(!emitter.TryBeginCharge(), "Network cooldown rejects new charge");
                wait = Wait(5); while (wait.MoveNext()) yield return null;
            }
            enemy.GetComponent<EyedMonsterAI>().enabled = true;
            wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
            Check(enemy.GetComponent<EyedMonsterAI>().State != EyedMonsterAI.Mode.Stunned && enemyCopy.GetComponent<EyedMonsterAI>().State != EyedMonsterAI.Mode.Stunned, "Stun recovery state replicates through existing monster sync");
            var clientEmitter = client.LocalClient.PlayerObject.GetComponent<FpsFlashEmitter>();
            Check(clientEmitter.TryBeginCharge(), "Client starts charge before equipment cancellation");
            wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
            clientEmitter.GetComponent<FpsEquipment>().Process(1, false, false);
            wait = Wait(0.3f); while (wait.MoveNext()) yield return null;
            Check(!clientEmitter.IsCharging && clientEmitter.RemainingUses == 2 && !host.SpawnManager.SpawnedObjects[clientEmitter.GetComponent<NetworkObject>().NetworkObjectId].GetComponent<FpsFlashEmitter>().IsCharging, "Slot cancellation reaches server and preserves remaining uses");
            client.Shutdown(); host.Shutdown();
        }
    }
}
