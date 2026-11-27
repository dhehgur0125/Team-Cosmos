using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static class LootRevisionValidation
    {
        static LootRevisionValidation(){EditorApplication.update+=Requested;}
        static void Requested()
        {
            const string request="Library/LootRevisionValidate.request";
            if(!File.Exists(request)||File.ReadAllText(request)!="validate"){EditorApplication.update-=Requested;return;}
            if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode){return;}
            EditorApplication.update-=Requested;
            File.WriteAllText(request,"running");
            try {Run();File.WriteAllText(request,"complete");}
            catch(Exception e){File.WriteAllText("Library/LootRevisionValidate.report",e.ToString());File.WriteAllText(request,"failed");Debug.LogException(e);}
        }
        static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
        static void Check(bool pass,string name,StringBuilder log){if(!pass)throw new Exception(name);log.AppendLine("PASS "+name);}
        [MenuItem("VOID LAST/Loot/Validate Blueprint and Battery Revision")]
        public static void Run()
        {
            var log=new StringBuilder();
            var preview=EditorSceneManager.OpenPreviewScene("Assets/Map/Scenes/Floors/Stage2_Ship_2f.unity");
            var oldActive=SceneManager.GetActiveScene();
            var oldFeedback=Object.FindObjectsByType<LootFeedback>(FindObjectsSortMode.None);
            GameObject actor=null;
            try
            {
                var boxes=preview.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LootContainer>(true)).ToArray();
                foreach(var box in boxes)box.SetSeed(123);
                Check(boxes.Length==31,"2F retains 31 containers",log);
                Check(boxes.Where(b=>b.CanScan).All(b=>b.MissionReward!=null && FarmingItems.IsMissionItem(b.MissionReward.Item)),"Only active mission crates scan",log);
                Check(boxes.SelectMany(b=>b.rewards.Concat(new[]{b.randomReward})).Where(p=>p!=null&&FarmingItems.IsMap(p.Item)).All(p=>p.Item==FarmingItemKind.Map),"2F contains only 2F maps",log);
                foreach(var kind in new[]{FarmingItemKind.MapFloor1,FarmingItemKind.Map,FarmingItemKind.MapFloor3,FarmingItemKind.MapFloor4,FarmingItemKind.MapFloor5})
                {
                    Check(FarmingItems.CanLootOnFloor(kind,"Stage2_Ship_2f")==(kind==FarmingItemKind.Map),"Floor loot filter "+kind,log);
                    var prefab=FarmingItems.PickupPrefab(kind);
                    Check(prefab!=null&&prefab.Item==kind,"Prefab item identity "+kind,log);
                    Check(prefab.transform.Find("Model").GetComponentsInChildren<Renderer>().Length==1,"Combined model "+kind,log);
                }
                actor=new GameObject("Battery validation");SceneManager.MoveGameObjectToScene(actor,preview);
                var health=actor.AddComponent<FpsHealth>();health.RestoreFromCarryOver(100);
                var motor=actor.AddComponent<FpsMotor>();
                var equipment=actor.AddComponent<FpsEquipment>();
                var inventory=actor.AddComponent<FpsInventory>();
                Set(inventory,"health",health);Set(inventory,"motor",motor);Set(inventory,"equipment",equipment);
                Set(equipment,"selectedSlot",FpsEquipment.EquipmentSlot.FarmingItem);
                var unique=actor.AddComponent<FpsUniqueItem>();unique.Assign(UniqueItemKind.Scanner);
                var detector=actor.AddComponent<ItemDetector>();
                Set(detector,"scannerSpent",1);
                inventory.RestoreItems(new[]{FarmingItemKind.Battery});
                Check(inventory.TryStartUse(),"Battery begins with depleted scanner",log);
                inventory.TickUse(3.1f,true,false);
                Check(!inventory.IsUsing && inventory.ItemAt(0)==FarmingItemKind.None && detector.ScannerUsesRemaining==detector.scannerMaxUses,"Battery consumes once, recharges scanner, releases use state",log);
                inventory.RestoreItems(new[]{FarmingItemKind.Battery});
                Check(!inventory.TryStartUse() && inventory.HasItem(FarmingItemKind.Battery),"Full scanner keeps battery",log);
                unique.Assign(UniqueItemKind.Radio);
                Check(!inventory.TryStartUse() && inventory.HasItem(FarmingItemKind.Battery),"Radio rejects battery",log);
                unique.Assign(UniqueItemKind.Scanner);Set(detector,"scannerSpent",1);
                Check(inventory.TryStartUse(),"Second use begins",log);
                inventory.TickUse(0.2f,true,false);inventory.TickUse(0,false,false);
                Check(!inventory.IsUsing && inventory.HasItem(FarmingItemKind.Battery),"Release cancels without consuming",log);
                Set(inventory,"batteryPendingSlot",0);Set(inventory,"batteryRequestId",7u);
                Set(inventory,"batteryPendingSince",Time.realtimeSinceStartup-10f);
                Check(!inventory.IsUsingOnOther && inventory.UseProgress==1,"Pending battery uses correct animation/progress",log);
                inventory.CompleteBatteryRecharge(true,6);
                Check(inventory.BatteryRequestPending&&inventory.HasItem(FarmingItemKind.Battery),"Stale result ignored",log);
                inventory.TickUse(0,false,false);
                Check(!inventory.IsUsing&&inventory.HasItem(FarmingItemKind.Battery),"Missing network response releases state and keeps battery",log);
                var map=actor.AddComponent<FpsMapItem>();
                Set(map,"inventory",inventory);Set(map,"input",actor.GetComponent<FpsInput>());
                foreach(var kind in new[]{FarmingItemKind.MapFloor1,FarmingItemKind.Map,FarmingItemKind.MapFloor3,FarmingItemKind.MapFloor4,FarmingItemKind.MapFloor5})
                {
                    inventory.RestoreItems(new[]{kind});
                    typeof(FpsMapItem).GetMethod("Open",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(map,null);
                    var panel=(GameObject)typeof(FpsMapItem).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(map);
                    var image=panel.GetComponentInChildren<UnityEngine.UI.RawImage>();
                    Check(image.texture==Resources.Load<Texture2D>("FloorPlans/Stage2_Ship_"+FarmingItems.MapFloor(kind)+"f"),"Opened correct blueprint "+kind,log);
                    map.Close();
                    Object.DestroyImmediate(panel);Set(map,"panel",null);
                }
                RenderBlueprint();
                File.WriteAllText("Library/LootRevisionValidate.report",log.ToString());
            }
            finally
            {
                if(actor!=null)Object.DestroyImmediate(actor);
                foreach(var feedback in Object.FindObjectsByType<LootFeedback>(FindObjectsSortMode.None))
                    if(!oldFeedback.Contains(feedback))Object.DestroyImmediate(feedback.gameObject);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        [MenuItem("VOID LAST/Loot/Render Blueprint Preview")]
        public static void RenderBlueprint()
        {
            var preview=new PreviewRenderUtility();
            try
            {
                var model=Object.Instantiate(FarmingItems.PickupPrefab(FarmingItemKind.Map).transform.Find("Model").gameObject);
                preview.AddSingleGO(model);
                preview.camera.transform.position=new Vector3(0.30f,0.48f,-0.42f);
                preview.camera.transform.LookAt(new Vector3(0,0.01f,0));
                preview.camera.nearClipPlane=0.01f;preview.camera.farClipPlane=10;
                preview.camera.backgroundColor=new Color(0.035f,0.045f,0.065f);
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.fieldOfView=38;
                preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(40,30,0);
                preview.lights[1].intensity=0.7f;preview.ambientColor=Color.gray;
                var result=new RenderTexture(800,650,24);result.Create();
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(preview.camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=result});
                var previous=RenderTexture.active;RenderTexture.active=result;
                var texture=new Texture2D(800,650,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,800,650),0,0);texture.Apply();RenderTexture.active=previous;
                File.WriteAllBytes("Library/BlueprintPreview.png",texture.EncodeToPNG());Object.DestroyImmediate(texture);result.Release();Object.DestroyImmediate(result);
            }
            finally{preview.Cleanup();}
        }
    }
}
