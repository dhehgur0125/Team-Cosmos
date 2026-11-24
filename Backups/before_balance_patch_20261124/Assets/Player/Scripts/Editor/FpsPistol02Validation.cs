/* [ 코드 설명 ]
1. Character_test Play Mode에서 새 권총·ADS·팔 표시·탄창 낙하와 기존 탄약 계산을 확인합니다.
2. 임시 로컬 UDP 피어 두 개로 재장전 표시 전달을 검사하며 씬·맵·프로젝트 네트워크 설정은 저장하지 않습니다.
*/
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static class FpsPistol02Validation
    {
        const string Pending = "VoidLast.Pistol02.Validation";
        static IEnumerator checks;
        static StringBuilder report;
        static int frame, errors;
        static bool background;
        static NetworkManager host, client;
        public static string Progress { get; private set; }
        public static void BeginPreview() { SessionState.SetBool(Pending + ".Preview", true); Begin(); }
        public static void EndPreview() => SessionState.SetBool(Pending + ".Preview", false);
        static FpsPistol02Validation() => EditorApplication.playModeStateChanged += Changed;
        [MenuItem("VOID LAST/Player/Validate Pistol 02")]
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
            report = new StringBuilder("Pistol 02 - Character_test actual Play Mode\n" + DateTime.UtcNow.ToString("O") + "\n");
            background = Application.runInBackground; Application.runInBackground = true; frame = -1; errors = 0;
            Application.logMessageReceived += Log; checks = Checks(); EditorApplication.update += Tick;
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
            File.WriteAllText("Assets/Player/Validation/Pistol02Results.txt", report.ToString()); EditorApplication.ExitPlaymode();
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
            var player = Object.FindAnyObjectByType<FpsPistol>().gameObject; Quiet(player);
            var camera = player.GetComponentInChildren<Camera>(true); var equip = player.GetComponent<FpsEquipment>();
            var pistol = player.GetComponent<FpsPistol>(); var view = player.GetComponent<FpsWeaponView>(); var drop = player.GetComponent<FpsMagazineDrop>();
            var motor = player.GetComponent<FpsMotor>(); var root = camera.transform.Find("WeaponRoot"); var gun = root.Find("SciFiPistol02_2"); var arm = root.Find("FPS Right Glove");
            Place(player, new Vector3(1000, 1000.03f, 1000)); camera.transform.parent.localRotation = Quaternion.identity;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Temporary magazine test floor"; floor.transform.position = new Vector3(1000, 999.75f, 1000); floor.transform.localScale = new Vector3(24, 0.5f, 24);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary ADS target"; wall.transform.position = new Vector3(1000,1001.5f,1010); wall.transform.localScale = new Vector3(12,6,0.2f);
            equip.Process(1,false,false); view.Refresh(1);
            var wait = Wait(0.4f); while(wait.MoveNext())yield return null;
            while(SessionState.GetBool(Pending + ".Preview", false)) yield return null;
            Check(gun != null && gun.Find("Clip").GetComponent<MeshRenderer>() != null && gun.Find("Scope/Dot") != null, "Pistol 02 Clip, Scope and physical Dot are present");
            var mesh = arm.GetComponent<MeshFilter>().sharedMesh;
            Check(AssetDatabase.GetAssetPath(mesh) == FpsPistol02Setup.Arm && mesh.bounds.size.y > 0.35f, "Pistol uses original astronaut forearm geometry, not the short glove-only cutout");
            Check(camera.nearClipPlane == 0.03f && camera.cullingMask == -1 && player.GetComponentInChildren<Animator>().avatar.isValid, "Camera mask, near plane and original Humanoid Avatar remain valid");
            FpsValidation.Capture(camera,"Pistol02_Idle.png");
            for(int stance=0;stance<2;stance++)
            {
                float end=Time.time+0.45f; while(Time.time<end){motor.Simulate(Vector2.up,stance==1,false,Time.deltaTime);yield return null;}
                Check(arm.gameObject.activeInHierarchy && motor.LocalVelocity.magnitude>1, (stance==0?"Walk":"Run")+" retains FPS arm and weapon");
                FpsValidation.Capture(camera,stance==0?"Pistol02_Walk.png":"Pistol02_Run.png");
            }
            motor.Simulate(Vector2.zero,false,false,0.1f); Place(player,new Vector3(1000,1000.03f,1000));
            equip.Process(1,true,false); wait=Wait(0.6f);while(wait.MoveNext())yield return null;
            Vector3 dot = camera.WorldToViewportPoint(gun.Find("Scope/Dot").position);
            Check(Vector2.Distance(new Vector2(dot.x,dot.y),Vector2.one*0.5f)<0.001f && dot.z>0.2f, "ADS physical sight centre aligns with screen centre within 0.1 percent");
            FpsValidation.Capture(camera,"Pistol02_ADS.png");
            Vector3 grip = gun.InverseTransformPoint(arm.position); Vector3 rest = root.localPosition;
            Check(pistol.TryFire() && pistol.Magazine==9 && pistol.LastHit.collider.gameObject==wall && pistol.Damage==30, "ADS shot follows existing camera hitscan, damage and ammo rules");
            Check(Vector3.Distance(pistol.LastHit.point,camera.transform.position+camera.transform.forward*Vector3.Distance(camera.transform.position,pistol.LastHit.point))<0.01f, "Actual hit stays on camera-centre shot direction");
            view.Refresh(0); FpsValidation.Capture(camera,"Pistol02_Shoot.png");
            Check(Vector3.Distance(gun.InverseTransformPoint(arm.position),grip)<0.001f && view.RecoilAmount==1, "Gun and entire FPS arm recoil together");
            wait=Wait(0.6f);while(wait.MoveNext())yield return null;
            Check(view.RecoilAmount==0 && Vector3.Distance(root.localPosition,rest)<0.001f, "Recoil returns without drift");
            Check(pistol.TryReload() && !pistol.TryReload(), "Reload starts once; duplicate request rejected");
            wait=Wait(0.45f);while(wait.MoveNext())yield return null;
            Check(drop.DropsCreated==1 && !drop.ClipVisible && drop.LastDrop!=null, "One real Clip mesh drops at reload timing and original hides");
            FpsValidation.Capture(camera,"Pistol02_Reload.png"); var firstDrop=drop.LastDrop;
            Check(firstDrop.GetComponent<NetworkObject>()==null && Physics.GetIgnoreCollision(firstDrop.GetComponent<Collider>(),player.GetComponent<CharacterController>()), "Dropped magazine has local physics only and cannot push its player");
            wait=Wait(1.2f);while(wait.MoveNext())yield return null;
            Check(!pistol.IsReloading && drop.ClipVisible && pistol.Magazine==10 && pistol.Reserve==29 && drop.DropsCreated==1, "Reload completes with unchanged ammo accounting and restores Clip");
            Check(firstDrop.TouchedEnvironment && firstDrop.transform.position.y>999.98f && firstDrop.Body.linearVelocity.magnitude<0.25f, "Magazine collides with the floor and settles");
            // Cosmetic debris in the firing line must not become a new gameplay target.
            var settledPosition=firstDrop.transform.position;
            firstDrop.Body.isKinematic=true;firstDrop.transform.position=camera.transform.position+camera.transform.forward;Physics.SyncTransforms();
            Check(pistol.TryFire() && pistol.LastHit.collider.gameObject==wall,"Visual dropped magazine does not intercept hitscan");
            firstDrop.transform.position=settledPosition;firstDrop.Body.isKinematic=false;
            wait=Wait(0.25f);while(wait.MoveNext())yield return null;
            equip.Process(1,false,false);view.Refresh(1);Check(pistol.TryReload(),"Cancellable reload begins");
            wait=Wait(0.4f);while(wait.MoveNext())yield return null;
            equip.Process(2,false,false);
            Check(!pistol.IsReloading && drop.ClipVisible && drop.DropsCreated==2 && pistol.Magazine==9,"Switching after drop cancels reload and restores original without duplicate drop");
            equip.Process(1,false,false);view.Refresh(1);Check(pistol.TryReload(),"Early-cancel reload begins");equip.Process(2,false,false);
            Check(drop.DropsCreated==2 && drop.ClipVisible,"Switching before detach creates no dropped magazine");
            equip.Process(1,true,false);view.Refresh(1);wait=Wait(0.5f);while(wait.MoveNext())yield return null;
            FpsValidation.CaptureObserver(player,"Pistol02_World.png",true);
            var worldGun=player.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.RightHand).Find("WorldWeaponSocket/SciFiPistol02_2");
            bool fpsArm=false,worldBody=false,worldGunHidden=false,localGunHidden=false;
            Action<ScriptableRenderContext,Camera> inspect=(context,rendered)=>{if(rendered==camera){fpsArm=!arm.GetComponent<Renderer>().forceRenderingOff;worldGunHidden=worldGun.GetComponent<Renderer>().forceRenderingOff;}else{worldBody=player.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled&&!r.forceRenderingOff);localGunHidden=gun.GetComponent<Renderer>().forceRenderingOff;}};
            RenderPipelineManager.beginCameraRendering+=inspect;
            try{FpsValidation.Capture(camera,"Pistol02_ReturnedADS.png");FpsValidation.CaptureObserver(player,"Pistol02_FullBody.png");}
            finally{RenderPipelineManager.beginCameraRendering-=inspect;}
            Check(fpsArm&&worldBody&&worldGunHidden&&localGunHidden,"Local camera shows FPS arm only; observer shows astronaut and world gun only");
            var front=new GameObject("Temporary muzzle view",typeof(Camera));var frontCamera=front.GetComponent<Camera>();frontCamera.enabled=false;
            front.transform.position=worldGun.TransformPoint(new Vector3(0,.02f,.6f));front.transform.rotation=worldGun.rotation*Quaternion.Euler(0,180,0);
            frontCamera.orthographic=true;frontCamera.orthographicSize=.12f;frontCamera.nearClipPlane=.01f;frontCamera.farClipPlane=2;
            FpsValidation.Capture(frontCamera,"Pistol02_MuzzleFront.png");Object.Destroy(front);
            wait=Wait(4.5f);while(wait.MoveNext())yield return null;
            Check(firstDrop==null,"Dropped magazine expires automatically");
            player.SetActive(false);Object.Destroy(floor);Object.Destroy(wall);
            var online=NetworkChecks();while(online.MoveNext())yield return online.Current;
            Check(errors==0,"No Console errors or exceptions during completed tests");
            report.AppendLine("COMPLETE. Local UDP reload/visual-shot replay tested; separate-PC Relay, Lobby and physical keyboard/mouse feel not tested. No maps or team network code saved.");
        }
        static NetworkManager Manager(string name)
        {
            var go=new GameObject(name);var manager=go.AddComponent<NetworkManager>();var transport=go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1",18781,"127.0.0.1");
            manager.NetworkConfig=new NetworkConfig{NetworkTransport=transport,PlayerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(FpsPistol02Setup.Network),EnableSceneManagement=false,ConnectionApproval=false};return manager;
        }
        static IEnumerator NetworkChecks()
        {
            host=Manager("Temporary pistol host");Check(host.StartHost(),"Local UDP host starts");client=Manager("Temporary pistol client");Check(client.StartClient(),"Local UDP client starts");
            float end=Time.time+12;while((!client.IsConnectedClient||host.ConnectedClients.Count<2||client.SpawnManager.SpawnedObjects.Count<2)&&Time.time<end)yield return null;
            Check(client.IsConnectedClient&&host.ConnectedClients.Count==2,"Two actual NGO peers connected");
            foreach(var manager in new[]{host,client})foreach(var obj in manager.SpawnManager.SpawnedObjectsList)
            {
                if(!obj.IsPlayerObject)continue;Quiet(obj.gameObject);obj.GetComponentInChildren<Camera>(true).enabled=false;
                var sync=obj.GetComponent<NetworkTransform>();sync.SyncPositionX=sync.SyncPositionY=sync.SyncPositionZ=false;sync.SyncRotAngleX=sync.SyncRotAngleY=sync.SyncRotAngleZ=false;sync.Interpolate=false;
            }
            var wait=Wait(0.3f);while(wait.MoveNext())yield return null;
            foreach(var manager in new[]{host,client})foreach(var obj in manager.SpawnManager.SpawnedObjectsList)
                if(obj.IsPlayerObject)Place(obj.gameObject,new Vector3(manager==host?2000:2100,1000,obj.OwnerClientId==0?0:5));
            foreach(var owner in new[]{host.LocalClient.PlayerObject,client.LocalClient.PlayerObject})
            {
                var pistol=owner.GetComponent<FpsPistol>();var equip=owner.GetComponent<FpsEquipment>();equip.Process(1,false,false);pistol.RestoreAmmo(5,30);
                wait=Wait(0.5f);while(wait.MoveNext())yield return null;
                Check(pistol.TryReload(),"Owner "+owner.OwnerClientId+" starts network reload");wait=Wait(0.55f);while(wait.MoveNext())yield return null;
                foreach(var peer in new[]{host,client})
                {
                    var copy=peer.SpawnManager.SpawnedObjects[owner.NetworkObjectId].GetComponent<FpsMagazineDrop>();
                    Check(copy.DropsCreated==1&&!copy.ClipVisible,"Owner "+owner.OwnerClientId+" drops one magazine on "+(peer==host?"host":"client"));
                }
                wait=Wait(1.2f);while(wait.MoveNext())yield return null;
                Check(pistol.Magazine==10&&pistol.Reserve==25,"Network owner retains original ammo calculation");
                foreach(var peer in new[]{host,client})Check(peer.SpawnManager.SpawnedObjects[owner.NetworkObjectId].GetComponent<FpsMagazineDrop>().ClipVisible,"Both peers restore original Clip after reload");
                pistol.RestoreAmmo(5,30);Check(pistol.TryReload(),"Network cancellation reload begins");wait=Wait(0.5f);while(wait.MoveNext())yield return null;
                equip.Process(2,false,false);wait=Wait(0.25f);while(wait.MoveNext())yield return null;
                foreach(var peer in new[]{host,client})
                {
                    var copy=peer.SpawnManager.SpawnedObjects[owner.NetworkObjectId].GetComponent<FpsMagazineDrop>();
                    Check(copy.ClipVisible&&copy.DropsCreated==2,"Slot cancellation restores Clip on both peers without duplicate drop");
                }
                equip.Process(1,false,false);wait=Wait(0.5f);while(wait.MoveNext())yield return null;
                // Replay the existing visual Shot RPC without emitting the global GunNoise event shared by both test peers in this one Editor process.
                var observer=owner.OwnerClientId==host.LocalClientId?client:host;
                var remotePistol=observer.SpawnManager.SpawnedObjects[owner.NetworkObjectId].GetComponent<FpsPistol>();
                int remoteShots=0;Action countShot=()=>remoteShots++;remotePistol.Shot+=countShot;
                pistol.PlayNetworkShot();wait=Wait(0.25f);while(wait.MoveNext())yield return null;
                remotePistol.Shot-=countShot;
                Check(pistol.Magazine==5&&remoteShots==1,"Existing Shot RPC reaches other peer once without changing ammo");
            }
            Check(Object.FindObjectsByType<FpsDroppedMagazine>(FindObjectsSortMode.None).All(m=>m.GetComponent<NetworkObject>()==null),"All peer magazine debris remains local; no spawned network debris");
            client.Shutdown();host.Shutdown();
        }
    }
}
