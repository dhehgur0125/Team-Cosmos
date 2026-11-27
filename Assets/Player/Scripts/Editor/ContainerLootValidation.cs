using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static class ContainerLootValidation
    {
        const string Request="Library/ContainerLootValidate.request";
        static ContainerLootValidation(){EditorApplication.delayCall+=RunRequested;}
        static void RunRequested()
        {
            if(!File.Exists(Request)||File.ReadAllText(Request)!="validate")return;
            if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.delayCall+=RunRequested;return;}
            File.WriteAllText(Request,"running");
            try { Run();File.WriteAllText(Request,"complete"); }
            catch(Exception e){File.WriteAllText("Library/ContainerLootValidate.report",e.ToString());File.WriteAllText(Request,"failed");}
        }
        [MenuItem("VOID LAST/Loot/Validate Current Floor")]
        public static void Run()
        {
            var log=new StringBuilder();
            Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);log.AppendLine("PASS "+name);};
            for(int n=1;n<=5;n++)
            {
                string path="Assets/Resources/FloorPlans/Stage2_Ship_"+n+"f.png";
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                check(importer!=null,"Floor "+n+" importer");
                importer.textureType=TextureImporterType.Default;
                importer.npotScale=TextureImporterNPOTScale.None;
                importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.maxTextureSize=2048;importer.SaveAndReimport();
                var texture=Resources.Load<Texture2D>("FloorPlans/Stage2_Ship_"+n+"f");
                check(texture!=null,"Floor "+n+" texture "+texture.width+"x"+texture.height);
            }
            check(FarmingItems.MapFloor(FarmingItemKind.Map)==2 && FarmingItems.MapFloor(FarmingItemKind.MapFloor5)==5,"Floor-specific map selection");
            check(FarmingItems.HudLabel(FarmingItemKind.Map)=="건물도면 · 2층","Map display name");
            var roots=SceneManager.GetActiveScene().GetRootGameObjects();
            var boxes=roots.SelectMany(x=>x.GetComponentsInChildren<LootContainer>(true)).ToArray();
            check(boxes.Length>0,"Scene containers "+boxes.Length);
            check(boxes.Select(x=>x.containerId).Distinct().Count()==boxes.Length,"Unique container IDs");
            var sources=roots.SelectMany(x=>x.GetComponentsInChildren<FpsWorldPickup>(true)).Where(x=>x.storedInContainer).ToArray();
            check(sources.Length>0,"Stored sources "+sources.Length);
            check(sources.All(p=>!p.GetComponentsInChildren<Renderer>(true).Any(v=>v.enabled)),"Stored sources hidden");
            check(sources.All(p=>!p.GetComponentsInChildren<Collider>(true).Any(v=>v.enabled)),"Stored sources have no pickup colliders");
            var temp=new GameObject("Loot validation temporary");temp.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var box=temp.AddComponent<LootContainer>();box.containerId="test";
                check(!box.CanScan,"Unsynced container excluded");
                box.SetSeed(123);
                check(!box.CanScan,"Empty seeded container excluded");
                var pickup=temp.AddComponent<FpsWorldPickup>();
                box.rewards.Add(pickup);
                check(!box.CanScan,"Ordinary reward excluded");
                var pickupState=new SerializedObject(pickup);
                pickupState.FindProperty("item").intValue=(int)FarmingItemKind.QuarantineCard;
                pickupState.ApplyModifiedPropertiesWithoutUndo();
                check(box.CanScan,"Guaranteed available reward detectable");
                box.searched=true;
                check(box.CanScan,"New mission reward in searched crate detectable");
                pickup.enabled=false;
                check(!box.CanScan,"Inactive reward excluded");
            }
            finally {UnityEngine.Object.DestroyImmediate(temp);}
            foreach(var kind in new[]{FarmingItemKind.MapFloor1,FarmingItemKind.Map,FarmingItemKind.MapFloor3,FarmingItemKind.MapFloor4,FarmingItemKind.MapFloor5})
            {
                check(FarmingItems.CanLootOnFloor(kind,"Stage2_Ship_2f")==(kind==FarmingItemKind.Map),"2F loot restriction "+kind);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/FarmingItems/"+kind+".prefab");
                check(prefab!=null && prefab.GetComponent<FpsWorldPickup>().Item==kind,"Blueprint prefab "+kind);
                check(prefab.transform.Find("Model").GetComponentsInChildren<MeshRenderer>().Length==1,"Combined blueprint model "+kind);
            }
            File.WriteAllText("Library/ContainerLootValidate.report",log.ToString());
            Debug.Log(log.ToString());
        }
    }
}
