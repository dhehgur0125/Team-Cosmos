/* [ 코드 설명 ]
1. Character_test Play Mode에서 아래를 볼 때 각 장비의 3인칭 모델이 중복 표시되는지 검사합니다.
2. 일반 캡처와 3인칭 모델을 완전히 숨긴 기준 캡처의 픽셀을 비교하고, 다른 카메라의 전신 표시도 확인합니다. 씬은 저장하지 않습니다.
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
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static class FpsEquipmentVisibilityValidation
    {
        const string Key = "VoidLast.EquipmentVisibility";
        static IEnumerator checks;
        static StringBuilder report;
        static int frame, errors;
        static bool background, before;
        public static string Progress { get; private set; }
        static FpsEquipmentVisibilityValidation() => EditorApplication.playModeStateChanged += Changed;
        public static void Begin(bool baseline = false)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first");
            SessionState.SetBool(Key,true);SessionState.SetBool(Key+".Before",baseline);
            SessionState.SetString(Key+".Scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Player/Character_test.unity");
            EditorApplication.EnterPlaymode();
        }
        static void Changed(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=Run;
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
                EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".Scene",""));SessionState.SetBool(Key,false);
            }
        }
        static void Run()
        {
            before=SessionState.GetBool(Key+".Before",false);errors=0;frame=-1;
            report=new StringBuilder("Equipment camera visibility / "+(before?"BEFORE":"AFTER")+" / "+DateTime.UtcNow.ToString("O")+"\n");
            background=Application.runInBackground;Application.runInBackground=true;Application.logMessageReceived+=Log;
            checks=Checks();EditorApplication.update+=Tick;
        }
        static void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;report.AppendLine("ERROR: "+message);}}
        static void Tick()
        {
            if(frame==Time.frameCount)return;frame=Time.frameCount;
            try{if(checks.MoveNext())return;}catch(Exception e){report.AppendLine("FAILED: "+e);}
            EditorApplication.update-=Tick;(checks as IDisposable)?.Dispose();Application.logMessageReceived-=Log;Application.runInBackground=background;
            report.AppendLine("Console errors during run="+errors);
            File.WriteAllText("Assets/Player/Validation/EquipmentVisibility_"+(before?"Before":"After")+".txt",report.ToString());EditorApplication.ExitPlaymode();
        }
        static IEnumerator Wait(float seconds){float end=Time.time+seconds;while(Time.time<end)yield return null;}
        static void Check(bool ok,string message){report.AppendLine((ok?"PASS: ":"FAIL: ")+message);Progress=message;}
        static Renderer[] List(FpsLocalVisibility visibility,string field)
        {
            var p=new SerializedObject(visibility).FindProperty(field);var result=new Renderer[p.arraySize];
            for(int i=0;i<result.Length;i++)result[i]=p.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
            return result.Where(r=>r!=null).ToArray();
        }
        static Texture2D Capture(Camera camera)
        {
            var rt=new RenderTexture(960,720,24);rt.Create();var image=new Texture2D(960,720,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();return image;}
            finally{RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);}
        }
        static IEnumerator Checks()
        {
            var player=Object.FindAnyObjectByType<FpsPistol>().gameObject;
            if(!before)
            {
                // Character_test's offline prefab has no ItemDetector. The actual network variant builds Scanner views in Awake.
                player.SetActive(false);
                player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FpsPistol02Setup.Network));
                player.name="Temporary equipment visibility player";
                report.AppendLine("Using existing VOID_LAST_FPS_Network prefab offline; includes ItemDetector/Scanner. No network session started.");
            }
            player.GetComponent<FpsInput>().enabled=false;player.GetComponent<FpsLook>().enabled=false;player.GetComponent<FpsMotor>().enabled=false;player.GetComponent<FpsEquipment>().enabled=false;
            foreach(var listener in player.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
            var controller=player.GetComponent<CharacterController>();controller.enabled=false;player.transform.SetPositionAndRotation(new Vector3(1000,1000.03f,1000),Quaternion.identity);controller.enabled=true;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(1000,999.75f,1000);floor.transform.localScale=new Vector3(24,.5f,24);
            // Keep the temporary reference background out of GPU multi-camera occlusion history too.
            var floorBlock=new MaterialPropertyBlock();floorBlock.SetFloat("_VisibilityTestFloor",1);floor.GetComponent<Renderer>().SetPropertyBlock(floorBlock);
            var camera=player.GetComponentInChildren<Camera>(true);camera.transform.parent.localRotation=Quaternion.Euler(65,0,0);
            var equipment=player.GetComponent<FpsEquipment>();var unique=player.GetComponent<FpsUniqueItem>();var inventory=player.GetComponent<FpsInventory>();
            var visibility=player.GetComponent<FpsLocalVisibility>();var animator=player.GetComponentInChildren<Animator>();
            player.GetComponent<FpsFlashEmitter>().enabled=true;
            string[] cases=before?new[]{"Healer","Syringe","Battery"}:new[]{"Healer","Stun","Radio","Scanner","Syringe","Radio pickup","Battery","QuarantineCard","Cutter","PowerModule","FuelCoupler","Scanner pickup","Pistol"};
            for(int index=0;index<cases.Length;index++)
            {
                string name=cases[index];Progress=name;bool isUnique=name=="Healer"||name=="Stun"||name=="Radio"||name=="Scanner";
                if(name=="Pistol")equipment.Process(1,false,false);
                else if(isUnique){unique.Assign((UniqueItemKind)Enum.Parse(typeof(UniqueItemKind),name));equipment.Process(3,false,false);}
                else
                {
                    var kind=(FarmingItemKind)Enum.Parse(typeof(FarmingItemKind),name.Replace(" pickup",""));int slot=index%3;
                    var items=new FarmingItemKind[3];items[slot]=kind;inventory.RestoreItems(items);equipment.Process(4+slot,false,false);
                }
                animator.enabled=true;var wait=Wait(.7f);while(wait.MoveNext())yield return null;animator.enabled=false;
                var world=List(visibility,"worldRenderers");var fp=List(visibility,"firstPersonRenderers");
                var activeWorld=world.Where(r=>r.gameObject.activeInHierarchy&&r.enabled&&r is MeshRenderer).ToArray();
                var activeFp=fp.Where(r=>r.gameObject.activeInHierarchy&&r.enabled&&r is MeshRenderer).ToArray();
                Check(activeWorld.Length>0&&activeFp.Length>0,name+": local and world models exist");
                bool localOk=false,observerOk=false;
                Action<ScriptableRenderContext,Camera> inspect=(context,rendered)=>
                {if(rendered==camera)localOk=world.All(r=>!r.enabled||!r.gameObject.activeInHierarchy||r.forceRenderingOff)&&activeFp.All(r=>!r.forceRenderingOff);
                 else observerOk=activeWorld.All(r=>!r.forceRenderingOff)&&activeFp.All(r=>r.forceRenderingOff);};
                RenderPipelineManager.beginCameraRendering+=inspect;
                Texture2D normal=null,reference=null;
                var worldPrevious=world.Select(r=>r.forceRenderingOff).ToArray();var fpPrevious=fp.Select(r=>r.forceRenderingOff).ToArray();
                try
                {
                    FpsValidation.CaptureObserver(player,"Visibility_"+(before?"Before_":"After_")+name.Replace(" ","_")+"_Observer.png",true);
                    normal=Capture(camera);
                    File.WriteAllBytes("Assets/Player/Validation/Visibility_"+(before?"Before_":"After_")+name.Replace(" ","_")+".png",normal.EncodeToPNG());
                    Check(localOk&&observerOk,name+": own camera hides world items; observer hides FPS items");
                    // Keep the reference hidden across frames so the GPU cache also sees the intended state.
                    visibility.enabled=false;foreach(var r in world)r.forceRenderingOff=true;foreach(var r in fp)r.forceRenderingOff=false;
                    wait=Wait(.12f);while(wait.MoveNext())yield return null;
                    reference=Capture(camera);var a=normal.GetPixels32();var b=reference.GetPixels32();int different=0;
                    if(name=="Healer")File.WriteAllBytes("Assets/Player/Validation/Visibility_"+(before?"Before_":"After_")+"Healer_Reference.png",reference.EncodeToPNG());
                    for(int i=0;i<a.Length;i++)if(Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)>45)different++;
                    Check(different<=50,name+": visible image matches hidden-world reference; different pixels="+different);
                }
                finally
                {
                    RenderPipelineManager.beginCameraRendering-=inspect;
                    for(int i=0;i<world.Length;i++)if(world[i]!=null)world[i].forceRenderingOff=worldPrevious[i];
                    for(int i=0;i<fp.Length;i++)if(fp[i]!=null)fp[i].forceRenderingOff=fpPrevious[i];
                    visibility.enabled=true;if(normal!=null)Object.DestroyImmediate(normal);if(reference!=null)Object.DestroyImmediate(reference);
                }
            }
            report.AppendLine("Actual Character_test Play Mode, camera pitch 65 degrees. Temporary inventory/selection only; no scene/prefab saves. Networking not exercised.");
        }
    }
}
