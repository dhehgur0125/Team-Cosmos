using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace VoidLast.Player.Editor
{
    [InitializeOnLoad]
    public static class BlueprintItemSetup
    {
        const string Request="Library/BlueprintItemSetup.request";
        const string Folder="Assets/Resources/FarmingItems";
        static BlueprintItemSetup(){EditorApplication.delayCall+=Requested;}
        static void Requested()
        {
            if(!File.Exists(Request)||File.ReadAllText(Request)!="install")return;
            if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.delayCall+=Requested;return;}
            File.WriteAllText(Request,"running");
            try { Build(); File.WriteAllText(Request,"complete"); }
            catch(Exception e){File.WriteAllText("Library/BlueprintItemSetup.report",e.ToString());File.WriteAllText(Request,"failed");Debug.LogException(e);}
        }
        static Material Material(string name, Color color)
        {
            string path=Folder+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",0.18f);
            EditorUtility.SetDirty(material);return material;
        }
        static GameObject Part(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var part=GameObject.CreatePrimitive(type);part.name=name;part.transform.SetParent(parent,false);
            part.transform.localPosition=position;part.transform.localScale=scale;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial=material;return part;
        }
        [MenuItem("VOID LAST/Loot/Build Floor Blueprint Items")]
        public static void Build()
        {
            var blue=Material("Blueprint_Paper",new Color(0.025f,0.15f,0.38f));
            var ink=Material("Blueprint_Ink",new Color(0.78f,0.92f,0.98f));
            var grid=Material("Blueprint_Grid",new Color(0.10f,0.34f,0.58f));
            var kinds=new[]{FarmingItemKind.MapFloor1,FarmingItemKind.Map,FarmingItemKind.MapFloor3,FarmingItemKind.MapFloor4,FarmingItemKind.MapFloor5};
            foreach(var kind in kinds)
            {
                string path=Folder+"/"+kind+".prefab";
                string source=AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null?path:Folder+"/Map.prefab";
                var root=PrefabUtility.LoadPrefabContents(source);
                try
                {
                    root.name=kind.ToString();
                    var pickup=root.GetComponent<FpsWorldPickup>();
                    var so=new SerializedObject(pickup);so.FindProperty("item").intValue=(int)kind;so.ApplyModifiedPropertiesWithoutUndo();
                    var old=root.transform.Find("Model");if(old!=null)Object.DestroyImmediate(old.gameObject);
                    var model=new GameObject("Model").transform;model.SetParent(root.transform,false);
                    Part(model,"Blue drafting sheet",PrimitiveType.Cube,new Vector3(0,0.012f,0),new Vector3(0.32f,0.003f,0.23f),blue);
                    for(int side=-1;side<=1;side+=2)
                    {
                        var roll=Part(model,"Rolled paper edge",PrimitiveType.Cylinder,new Vector3(side*0.164f,0.024f,0),new Vector3(0.028f,0.118f,0.028f),blue);
                        roll.transform.localRotation=Quaternion.Euler(90,0,0);
                        for(int end=-1;end<=1;end+=2)
                        {
                            var cap=Part(model,"Paper roll end",PrimitiveType.Cylinder,new Vector3(side*0.164f,0.024f,end*0.119f),new Vector3(0.023f,0.0006f,0.023f),ink);
                            cap.transform.localRotation=Quaternion.Euler(90,0,0);
                            var core=Part(model,"Roll core",PrimitiveType.Cylinder,new Vector3(side*0.164f,0.024f,end*0.120f),new Vector3(0.014f,0.0006f,0.014f),blue);
                            core.transform.localRotation=Quaternion.Euler(90,0,0);
                        }
                    }
                    for(int x=-7;x<=7;x++)Part(model,"Draft grid",PrimitiveType.Cube,new Vector3(x*0.02f,0.014f,0),new Vector3(0.0005f,0.0004f,0.21f),grid);
                    for(int z=-5;z<=5;z++)Part(model,"Draft grid",PrimitiveType.Cube,new Vector3(0,0.014f,z*0.02f),new Vector3(0.30f,0.0004f,0.0005f),grid);
                    Action<float,float,float,float> line=(x,z,w,h)=>Part(model,"Printed plan",PrimitiveType.Cube,new Vector3(x,0.0148f,z),new Vector3(w,0.0004f,h),ink);
                    line(0,-0.085f,0.26f,0.0016f);line(0,0.085f,0.26f,0.0016f);
                    line(-0.13f,0,0.0016f,0.17f);line(0.13f,0,0.0016f,0.17f);
                    line(-0.045f,0,0.0016f,0.17f);line(0.045f,0,0.0016f,0.17f);
                    line(-0.086f,0.02f,0.086f,0.0016f);line(0.086f,-0.025f,0.086f,0.0016f);
                    line(0,0.04f,0.09f,0.0016f);line(0,-0.04f,0.09f,0.0016f);
                    for(int n=0;n<FarmingItems.MapFloor(kind);n++)line(-0.11f+n*0.012f,-0.10f,0.006f,0.004f);
                    // Combine printed lines and paper into three submeshes, avoiding dozens of renderers per item.
                    var combined=new GameObject("Blueprint Mesh");
                    combined.transform.SetParent(model,false);
                    var meshes=new System.Collections.Generic.List<Mesh>();
                    foreach(var material in new[]{blue,ink,grid})
                    {
                        var parts=model.GetComponentsInChildren<MeshFilter>().Where(f=>f.GetComponent<Renderer>().sharedMaterial==material).ToArray();
                        var mesh=new Mesh();
                        mesh.CombineMeshes(parts.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=model.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
                        meshes.Add(mesh);
                    }
                    var final=new Mesh();final.name="Blueprint_"+FarmingItems.MapFloor(kind);
                    final.CombineMeshes(meshes.Select(m=>new CombineInstance{mesh=m,transform=Matrix4x4.identity}).ToArray(),false,true);
                    string meshPath=Folder+"/Blueprint_"+FarmingItems.MapFloor(kind)+".asset";
                    var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if(existing!=null){EditorUtility.CopySerialized(final,existing);Object.DestroyImmediate(final);final=existing;}
                    else AssetDatabase.CreateAsset(final,meshPath);
                    foreach(var mesh in meshes)Object.DestroyImmediate(mesh);
                    foreach(Transform child in model.Cast<Transform>().ToArray())if(child.gameObject!=combined)Object.DestroyImmediate(child.gameObject);
                    combined.AddComponent<MeshFilter>().sharedMesh=final;
                    combined.AddComponent<MeshRenderer>().sharedMaterials=new[]{blue,ink,grid};
                    var collider=root.GetComponent<BoxCollider>();
                    if(collider!=null){collider.center=new Vector3(0,0.025f,0);collider.size=new Vector3(0.36f,0.05f,0.25f);}
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Library/BlueprintItemSetup.report","SUCCESS: 5 floor-specific blueprint prefabs; one combined mesh/3 materials each. Map (enum 13) remains floor 2. Other floors are not added to the 2F loot table.");
        }
    }
}
