using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    // One-shot request file allows the same reviewed migration to run after an editor refresh.
    [InitializeOnLoad]
    public static class ContainerLootSetup
    {
        const string Request="Library/ContainerLootInstall.request";
        static ContainerLootSetup() { EditorApplication.delayCall+=Requested; }
        static void Requested()
        {
            if(!File.Exists(Request)||File.ReadAllText(Request)!="install")return;
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { EditorApplication.delayCall+=Requested;return; }
            File.WriteAllText(Request,"running");
            try { Apply();File.WriteAllText(Request,"complete"); }
            catch(Exception e) { File.WriteAllText("Library/ContainerLootInstall.report",e.ToString());File.WriteAllText(Request,"failed");Debug.LogException(e); }
        }
        [MenuItem("VOID LAST/Loot/Convert Current Ship Floor to Containers")]
        public static void Apply()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || !scene.name.StartsWith("Stage2_Ship"))throw new Exception("Open a ship floor in Edit Mode first.");
            if(scene.isDirty)throw new Exception("Scene has unsaved edits. Save it before running the loot migration.");
            string backup="Backups/before_container_loot_20261127/"+scene.name+".unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup))File.Copy(scene.path,backup);
            BuildMap();
            var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            var crateRoots=all.Where(t=>IsCrateName(t.name) && t.GetComponentInParent<FpsInventory>()==null && t.GetComponentInParent<FpsWorldPickup>()==null)
                .Where(t=>!Ancestors(t).Any(a=>IsCrateName(a.name))).Where(t=>t.gameObject.activeInHierarchy).ToArray();
            if(crateRoots.Length==0)throw new Exception("No dentedcase_b / P_Crate_04 containers found. No scene changes saved.");
            var boxes=new List<LootContainer>();
            foreach(var root in crateRoots)
            {
                var box=root.GetComponent<LootContainer>();if(box==null)box=Undo.AddComponent<LootContainer>(root.gameObject);
                box.containerId=scene.name+"/"+Hierarchy(root);EnsureCollider(root.gameObject);boxes.Add(box);
            }
            var sources=all.Select(t=>t.GetComponent<FpsWorldPickup>()).Where(p=>p!=null && !p.IsDropped && !p.storedInContainer).ToArray();
            var assignments=new List<string>();
            foreach(var source in sources)
            {
                var box=boxes.Where(b=>Mathf.Abs(b.transform.position.y-source.transform.position.y)<4f)
                    .OrderBy(b=>Vector3.SqrMagnitude(b.transform.position-source.transform.position)).FirstOrDefault();
                if(box==null)throw new Exception("No same-floor crate for "+source.name);
                float distance=Vector3.Distance(source.transform.position,box.transform.position);
                box.rewards.Add(source);HideSource(source);Dirty(box);
                assignments.Add(source.name+" -> "+box.containerId+" ("+distance.ToString("F1")+"m)");
            }
            // Extra supplies: one possible reward per otherwise ordinary crate; 50% chance per run.
            var kinds=new[]{FarmingItemKind.Battery,FarmingItemKind.Map,FarmingItemKind.AmmoPack,FarmingItemKind.Syringe};
            var syringe=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Prefabs/Item/VOID_LAST_SyringePickup.prefab");
            int random=0;
            foreach(var box in boxes.OrderBy(b=>b.containerId,StringComparer.Ordinal))
            {
                if(box.randomReward!=null || box.rewards.Count>0)continue;
                var kind=kinds[random%kinds.Length];var asset=kind==FarmingItemKind.Syringe?syringe:AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/FarmingItems/"+kind+".prefab");
                if(asset==null)throw new Exception("Missing loot prefab: "+kind);
                var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);go.name="ContainerReward_"+random+"_"+kind;
                go.transform.position=box.transform.position;var pickup=go.GetComponent<FpsWorldPickup>();HideSource(pickup);
                box.randomReward=pickup;Dirty(box);random++;
            }
            // Guarantee a battery and map exist without changing mission guarantees.
            foreach(var kind in new[]{FarmingItemKind.Battery,FarmingItemKind.Map})
            {
                if(boxes.Any(b=>b.rewards.Any(p=>p!=null&&p.Item==kind)))continue;
                var candidate=boxes.FirstOrDefault(b=>b.randomReward!=null && b.randomReward.Item==kind);
                if(candidate!=null){candidate.rewards.Add(candidate.randomReward);candidate.randomReward=null;Dirty(candidate);}
            }
            foreach(var d in all.Select(t=>t.GetComponent<ItemDetector>()).Where(d=>d!=null)) { d.detectRadius=d.scannerRadius=d.missionScanRadius=50f;Dirty(d); }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Library/ContainerLootInstall.report","SUCCESS\nScene: "+scene.path+"\nContainers: "+boxes.Count+"\nMoved pickups: "+sources.Length+"\nRandom candidates: "+random+"\n"+string.Join("\n",assignments));
            AssetDatabase.SaveAssets();Debug.Log("[Loot] Container migration complete: "+boxes.Count+" containers.");
        }
        static bool IsCrateName(string n) { n=n.ToLowerInvariant();return n.Contains("dentedcase_b")||n.Contains("p_crate_04")||n.Contains("p_carete_04"); }
        static IEnumerable<Transform> Ancestors(Transform t) { for(var p=t.parent;p!=null;p=p.parent)yield return p; }
        static string Hierarchy(Transform t) { return (t.parent!=null?Hierarchy(t.parent)+"/":"")+t.name+"["+t.GetSiblingIndex()+"]"; }
        static void Dirty(Component c) { EditorUtility.SetDirty(c);if(PrefabUtility.IsPartOfPrefabInstance(c))PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
        static void EnsureCollider(GameObject root)
        {
            if(root.GetComponentsInChildren<Collider>().Any(c=>c.enabled&&!c.isTrigger))return;
            var rs=root.GetComponentsInChildren<Renderer>();if(rs.Length==0)throw new Exception("Crate has no visible mesh: "+root.name);
            var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
            var col=root.AddComponent<BoxCollider>();col.center=root.transform.InverseTransformPoint(bounds.center);
            var scale=root.transform.lossyScale;col.size=new Vector3(bounds.size.x/Mathf.Max(0.001f,Mathf.Abs(scale.x)),bounds.size.y/Mathf.Max(0.001f,Mathf.Abs(scale.y)),bounds.size.z/Mathf.Max(0.001f,Mathf.Abs(scale.z)));
        }
        static void HideSource(FpsWorldPickup source)
        {
            source.storedInContainer=true;Dirty(source);
            foreach(var r in source.GetComponentsInChildren<Renderer>(true)){r.enabled=false;Dirty(r);}
            foreach(var c in source.GetComponentsInChildren<Collider>(true)){c.enabled=false;Dirty(c);}
            foreach(var d in source.GetComponentsInChildren<DetectableItem>(true)){d.enabled=false;Dirty(d);}
        }
        static void BuildMap()
        {
            const string path="Assets/Resources/FarmingItems/Map.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)return;
            var root=PrefabUtility.LoadPrefabContents("Assets/Resources/FarmingItems/QuarantineCard.prefab");
            try
            {
                root.name="Map";var so=new SerializedObject(root.GetComponent<FpsWorldPickup>());so.FindProperty("item").intValue=(int)FarmingItemKind.Map;so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }finally{PrefabUtility.UnloadPrefabContents(root);}
            var tex=new Texture2D(64,64);var pixels=new Color32[4096];
            for(int y=0;y<64;y++)for(int x=0;x<64;x++){bool paper=x>8&&x<56&&y>6&&y<58;bool line=paper&&((x==15||x==48)&&y>14&&y<50||(y==15||y==48)&&x>14&&x<49||x==31&&y>15&&y<48||y==32&&x>15&&x<48);pixels[y*64+x]=line?new Color32(60,230,235,255):paper?new Color32(15,55,72,255):new Color32(0,0,0,0);}
            tex.SetPixels32(pixels);tex.Apply();string icon="Assets/Resources/FarmingItems/Icons/Map.png";File.WriteAllBytes(icon,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(icon);
            var importer=(TextureImporter)AssetImporter.GetAtPath(icon);importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;importer.SaveAndReimport();
        }
    }
}
