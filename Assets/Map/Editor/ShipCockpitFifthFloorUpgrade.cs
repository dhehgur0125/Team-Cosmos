#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using Geo=VoidLast.MapTools.ShipCockpitFifthFloorGeometry;

namespace VoidLast.MapTools
{
    public static class ShipCockpitFifthFloorUpgrade
    {
        public const string Folder="Assets/Map/Stage2_Ship/CockpitF5Upgrade";
        const string Stamp="CockpitF5_Upgrade_Applied";
        static Material trim, lamp, backing;
        static Dictionary<string,GameObject> kit;
        static int assetNumber;
        public static string LastReport;
        static readonly Vector3[] Route={new Vector3(-47,32,-14),new Vector3(-70,32,-14),new Vector3(-70,32,-3.5f),new Vector3(-80.5f,32,-3.5f)};

        [MenuItem("Tools/VOID LAST/Upgrade Cockpit To Fifth Floor (Preserve Scene)")]
        public static void Menu(){Debug.Log(Apply(true));}

        public static string Apply(bool save)
        {
            var scene=SceneManager.GetActiveScene();
            if(Application.isPlaying||scene.path!=ShipMapBuilder.ScenePath)throw new InvalidOperationException("Open Stage2_Ship in Edit Mode first.");
            var ship=GameObject.Find("VOID_LAST_Ship");var hull=GameObject.Find("VOID_LAST_Hull");
            if(ship==null||hull==null)throw new InvalidOperationException("Existing ship and hull are required.");
            if(hull.transform.Find(Stamp)!=null)return "Cockpit fifth-floor upgrade already applied.";
            Load();assetNumber=0;
            var f3=ship.transform.Find("F3_Control");var f5=ship.transform.Find("F5_Crew");var cockpit=hull.transform.Find("Cockpit");
            float previousFloor=cockpit.Find("Structure/Cockpit_Floor").GetComponent<BoxCollider>().bounds.max.y;
            if(Mathf.Abs(previousFloor-16)>.02f&&Mathf.Abs(previousFloor-32)>.02f)throw new InvalidOperationException("Unexpected cockpit floor: "+previousFloor);
            var marker=Child(hull.transform,Stamp);Undo.RegisterCreatedObjectUndo(marker.gameObject,"Upgrade cockpit to F5");
            if(previousFloor<24)
            {
                var keys=OldOuterShellKeys(previousFloor);
                Geo.MoveMatchingShell(hull.transform.Find("Exterior/Visual"),keys,16,Folder+"/Meshes");
                Undo.RecordObject(cockpit,"Raise cockpit to F5");cockpit.position+=Vector3.up*16;
                foreach(var t in cockpit.GetComponentsInChildren<Transform>(true))if(t.name.StartsWith("F3_Cockpit")||t.name=="NavLink_F3_Cockpit")
                {Undo.RecordObject(t.gameObject,"Rename fifth-floor cockpit marker");t.name=t.name.Replace("F3_","F5_");}
            }

            // Remove the old 12m branch only; the F3 NW-Maint cross-connection remains intact.
            var oldBranch=f3.Find("Structure/Corridors/F3_Corridor_W-Cockpit");
            if(oldBranch!=null)
            {
                foreach(var t in f3.GetComponentsInChildren<Transform>(true).Where(t=>t!=null&&(t==oldBranch||t.name=="Wall_K_W-Cockpit"||t.name=="Step_K_W-Cockpit"||t.name=="F3_Door_W-Cockpit_Maint_SW-NW")).ToArray())
                    if(t!=null)Undo.DestroyObjectImmediate(t.gameObject);
                Cut(f3.Find("Visual"),BoundsOf(new Vector3(-81,15.6f,-6.6f),new Vector3(-68.51f,20.6f,-.4f)),"F3_OldBranch");
            }
            if(previousFloor<24)foreach(var light in f3.Find("Lights").GetComponentsInChildren<Light>(true).Where(l=>l.transform.position.x<-68.5f&&l.transform.position.z>-7&&l.transform.position.z<0))
            {Undo.RecordObject(light.gameObject,"Retire sealed branch light");light.gameObject.SetActive(false);}
            var seals=Child(marker,"Seals");var sealVisual=Child(seals,"_Temp");
            if(previousFloor<24)SealOldHull(sealVisual);
            if(oldBranch!=null||!HasColliderAt(new Vector3(-68.5f,17.4f,-3.5f)))
            {
                Solid(seals,"F3_Closed_CockpitAccess",new Vector3(-68.5f,17.5f,-3.5f),new Vector3(.4f,3,5),backing,false);
                var closedWall=seals.Find("F3_Closed_CockpitAccess");closedWall.SetParent(f3.Find("Structure/Walls"),true);Undo.RegisterCreatedObjectUndo(closedWall.gameObject,"Seal third-floor cockpit access");
                Wall(sealVisual,new Vector3(-68.28f,16,-6),new Vector3(-68.28f,19,-1),Vector3.right);
                BoxVisual(sealVisual,"SealedBulkheadFrame",new Vector3(-68.25f,17.5f,-3.5f),new Vector3(.12f,2.75f,4.75f),trim);
                BoxVisual(sealVisual,"ClosedIndicator",new Vector3(-68.16f,18.3f,-3.5f),new Vector3(.03f,.08f,1.3f),lamp);
            }
            Merge(sealVisual,Child(seals,"Visual"),"SealedF3");

            // Cut only the new opening through the existing fifth-floor wall and port hull skin.
            Cut(f5.Find("Visual"),BoundsOf(new Vector3(-50.3f,31.98f,-16),new Vector3(-48.9f,36.02f,-12)),"F5_Entry");
            SplitEntryColliders(f5);
            Cut(hull.transform.Find("Exterior/Visual"),BoundsOf(new Vector3(-76.2f,31.98f,-5.5f),new Vector3(-72.3f,36.02f,-1.5f)),"Hull_F5Opening");

            Transform connection=f5.Find("F5_CockpitAccess");
            bool generated=f5.Find("Structure/Corridors/F5_Corridor_NW-Cockpit")!=null;
            if(connection==null&&!generated)connection=BuildConnection(f5);
            var collar=Child(hull.transform.Find("Exterior"),"F5_CockpitCollar");Undo.RegisterCreatedObjectUndo(collar.gameObject,"Join raised cockpit canopy to hull");BuildCollar(collar);
            if(connection!=null)
            {
                Bake(connection.gameObject,"F5_CockpitAccess_NavMesh");
                AddLink(connection,"NavLink_F5_MainCorridor",new Vector3(-46,32,-14),new Vector3(-52,32,-14),2.8f);
                var visibility=ship.GetComponent<ShipFloorVisibility>();
                Undo.RecordObject(visibility,"Register fifth-floor cockpit passage");
                var floor=visibility.floors.First(f=>Mathf.Abs(f.floorY-32)<.1f);
                floor.propRenderers=floor.propRenderers.Concat(connection.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
                floor.toggles=floor.toggles.Concat(new[]{connection.Find("Lights").gameObject}).Distinct().ToArray();EditorUtility.SetDirty(visibility);
            }
            Bake(cockpit.gameObject,"F5_Cockpit_NavMesh");
            var existingLink=cockpit.GetComponentsInChildren<NavMeshLink>(true).FirstOrDefault();
            if(existingLink!=null){Undo.RecordObject(existingLink.gameObject,"Update cockpit floor link");existingLink.name="NavLink_F5_Cockpit";existingLink.transform.position=new Vector3(-80.5f,32,-3.5f);existingLink.UpdateLink();}
            if(previousFloor<24)Bake(f3.gameObject,"F3_ClosedCockpit_NavMesh");

            var hullVisibility=hull.GetComponent<ShipHullVisibility>();Undo.RecordObject(hullVisibility,"Update fifth-floor cockpit visibility");
            hullVisibility.cockpitBounds=BoundsOf(new Vector3(-132,31,-19.4f),new Vector3(-68,46.2f,12.4f));
            hullVisibility.cockpitVisibleY=new Vector2(30,48);EditorUtility.SetDirty(hullVisibility);
            Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            if(save)EditorSceneManager.SaveScene(scene);
            LastReport="cockpitFloor=32; accessWidth=4; centerlineLength=44; oldF3BranchClosed=true; previousFloor="+previousFloor;
            return LastReport;
        }

        static void Load()
        {
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Map/Stage2_Ship","CockpitF5Upgrade");
            foreach(var child in new[]{"Meshes","NavMesh"})if(!AssetDatabase.IsValidFolder(Folder+"/"+child))AssetDatabase.CreateFolder(Folder,child);
            trim=AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_TrimGrey.mat");
            lamp=AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/Stage2_NewMap/M_NM_EmergencyLamp.mat");
            backing=AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Materials/M_Stage2_Backing.mat")??trim;
            kit=new Dictionary<string,GameObject>();
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Creepy_Cat/3D Scifi Kit Vol 4/Prefabs"}))
            {var path=AssetDatabase.GUIDToAssetPath(guid);var name=System.IO.Path.GetFileNameWithoutExtension(path);if(!kit.ContainsKey(name))kit[name]=AssetDatabase.LoadAssetAtPath<GameObject>(path);}
        }
        static Transform Child(Transform parent,string name)
        {
            var existing=parent.Find(name);if(existing)return existing;var g=new GameObject(name);SceneManager.MoveGameObjectToScene(g,parent.gameObject.scene);g.transform.SetParent(parent,false);return g.transform;
        }
        static Bounds BoundsOf(Vector3 a,Vector3 b){return new Bounds((a+b)*.5f,b-a);}
        static Bounds RenderBounds(GameObject g)
        {
            var renderers=g.GetComponentsInChildren<MeshRenderer>(true);var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
        }
        static GameObject Fit(string name,Transform parent,Quaternion rotation,Vector3 min,Vector3 max,bool topOnly=false)
        {
            GameObject pf;if(!kit.TryGetValue(name,out pf))throw new InvalidOperationException("Missing kit prefab: "+name);
            var g=Object.Instantiate(pf);SceneManager.MoveGameObjectToScene(g,parent.gameObject.scene);g.transform.SetParent(parent,false);g.transform.rotation=rotation;g.transform.localScale=Vector3.one;g.transform.position=Vector3.zero;
            foreach(var b in g.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null)Object.DestroyImmediate(b);
            foreach(var b in g.GetComponentsInChildren<Light>(true))Object.DestroyImmediate(b);
            foreach(var b in g.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(b);
            var box=RenderBounds(g);var want=max-min;var s=Vector3.one;
            for(int j=0;j<3;j++){var axis=rotation*(j==0?Vector3.right:j==1?Vector3.up:Vector3.forward);int target=Mathf.Abs(axis.x)>.5f?0:Mathf.Abs(axis.y)>.5f?1:2;s[j]=topOnly&&target==1?1:want[target]/Mathf.Max(.0001f,box.size[target]);}
            g.transform.localScale=s;box=RenderBounds(g);g.transform.position+=new Vector3(min.x-box.min.x,topOnly?max.y-box.max.y:min.y-box.min.y,min.z-box.min.z);return g;
        }
        static void Solid(Transform parent,string name,Vector3 center,Vector3 size,Material material,bool visible=true)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(g,parent.gameObject.scene);g.name=name;g.transform.SetParent(parent,false);g.transform.position=center;g.transform.localScale=size;
            g.GetComponent<Renderer>().sharedMaterial=material;if(!visible)Object.DestroyImmediate(g.GetComponent<MeshRenderer>());
        }
        static void BoxVisual(Transform parent,string name,Vector3 center,Vector3 size,Material material)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(g,parent.gameObject.scene);g.name=name;g.transform.SetParent(parent,false);g.transform.position=center;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(g.GetComponent<Collider>());
        }
        static void Wall(Transform parent,Vector3 min,Vector3 max,Vector3 inward)
        {
            bool vertical=Mathf.Abs(inward.x)>.5f;float length=vertical?max.z-min.z:max.x-min.x;int count=Mathf.Max(1,Mathf.CeilToInt(length/5));float step=length/count;
            for(int i=0;i<count;i++)
            {
                var a=min;var b=max;if(vertical){a.z+=i*step;b.z=a.z+step;a.x-=.16f;b.x+=.16f;}else{a.x+=i*step;b.x=a.x+step;a.z-=.16f;b.z+=.16f;}
                Fit(i%3==1?"P_Wall_Electric_Panel_02_LP":"P_Wall_Rounded_Simple_01_LP",parent,Quaternion.LookRotation(inward,Vector3.up),a,b);
            }
        }
        static void Merge(Transform temp,Transform visual,string name)
        {
            var byMaterial=new Dictionary<Material,Geo.Buffer>();
            foreach(var mf in temp.GetComponentsInChildren<MeshFilter>(false))
            {
                var renderer=mf.GetComponent<MeshRenderer>();if(renderer==null||!renderer.enabled||mf.sharedMesh==null)continue;
                var source=Geo.Read(mf);var mats=renderer.sharedMaterials;
                for(int sm=0;sm<source.triangles.Length;sm++)
                {
                    var mat=mats[Math.Min(sm,mats.Length-1)];Geo.Buffer buffer;
                    if(!byMaterial.TryGetValue(mat,out buffer))byMaterial[mat]=buffer=new Geo.Buffer {triangles=new[]{new List<int>()}};
                    int offset=buffer.vertices.Count;buffer.vertices.AddRange(source.vertices);buffer.triangles[0].AddRange(source.triangles[sm].Select(i=>i+offset));
                }
            }
            foreach(var pair in byMaterial)
            {
                string meshName=name+"_"+(assetNumber++).ToString("000")+"_"+pair.Key.name;
                var mesh=Geo.Save(Geo.Build(pair.Value,visual.worldToLocalMatrix,meshName),Folder+"/Meshes/"+meshName+".asset");
                var g=Child(visual,meshName).gameObject;g.AddComponent<MeshFilter>().sharedMesh=mesh;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=pair.Key;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            }
            Object.DestroyImmediate(temp.gameObject);
        }
        static bool HasColliderAt(Vector3 p){return Physics.CheckSphere(p,.08f,~0,QueryTriggerInteraction.Ignore);}
        static void Cut(Transform root,Bounds bounds,string prefix)
        {
            foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true))
                Geo.Subtract(mf,bounds,Folder+"/Meshes/"+prefix+"_"+mf.name+".asset");
        }
        static void Bake(GameObject root,string name)
        {
            var surface=root.GetComponent<NavMeshSurface>()??root.AddComponent<NavMeshSurface>();Undo.RecordObject(surface,"Update cockpit access navigation");
            surface.collectObjects=CollectObjects.Children;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.BuildNavMesh();
            if(surface.navMeshData==null)throw new InvalidOperationException("NavMesh failed: "+name);
            string path=Folder+"/NavMesh/"+name+".asset";var prior=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if(prior==null)AssetDatabase.CreateAsset(surface.navMeshData,path);
            else{EditorUtility.CopySerialized(surface.navMeshData,prior);surface.RemoveData();surface.navMeshData=prior;surface.AddData();EditorUtility.SetDirty(prior);}
            EditorUtility.SetDirty(surface);
        }
        static void AddLink(Transform parent,string name,Vector3 a,Vector3 b,float width)
        {
            var holder=Child(parent,name);holder.position=a;var link=holder.gameObject.AddComponent<NavMeshLink>();link.startPoint=Vector3.zero;link.endPoint=b-a;link.width=width;link.bidirectional=true;link.UpdateLink();
        }

        static void SplitEntryColliders(Transform f5)
        {
            foreach(var c in f5.Find("Structure/Walls").GetComponentsInChildren<BoxCollider>(true).Where(c=>c.name=="Wall_K_NW-W").ToArray())
            {
                var b=c.bounds;if(b.min.x>-49.4f||b.max.x<-50.2f||b.min.z>=-12||b.max.z<=-16)continue;
                foreach(var segment in new[]{new Vector2(b.min.z,Mathf.Min(-16,b.max.z)),new Vector2(Mathf.Max(-12,b.min.z),b.max.z)})
                {
                    if(segment.y-segment.x<.01f)continue;
                    var g=new GameObject("Wall_K_NW-W_F5AccessJamb");g.transform.SetParent(c.transform.parent,false);g.transform.position=new Vector3(b.center.x,b.center.y,(segment.x+segment.y)*.5f);
                    var box=g.AddComponent<BoxCollider>();box.size=new Vector3(b.size.x,b.size.y,segment.y-segment.x);Undo.RegisterCreatedObjectUndo(g,"Cockpit passage wall jamb");
                }
                Undo.DestroyObjectImmediate(c.gameObject);
            }
        }
        static Transform BuildConnection(Transform f5)
        {
            var root=Child(f5,"F5_CockpitAccess");Undo.RegisterCreatedObjectUndo(root.gameObject,"Create fifth-floor cockpit passage");
            var structure=Child(root,"Structure");var temp=Child(root,"_Temp");var lights=Child(root,"Lights");
            var rectangles=new[]{new Vector4(-72,-16,-49.5f,-12),new Vector4(-72,-12,-68,-1.5f),new Vector4(-80.5f,-5.5f,-72,-1.5f)};
            for(int r=0;r<rectangles.Length;r++)
            {
                var b=rectangles[r];float w=b.z-b.x,l=b.w-b.y;
                Solid(structure,"F5_Access_Floor_"+r,new Vector3((b.x+b.z)*.5f,31.85f,(b.y+b.w)*.5f),new Vector3(w,.3f,l),backing,false);
                Solid(structure,"F5_Access_Ceiling_"+r,new Vector3((b.x+b.z)*.5f,36.15f,(b.y+b.w)*.5f),new Vector3(w,.3f,l),backing,false);
                BoxVisual(temp,"CeilingBacking",new Vector3((b.x+b.z)*.5f,36.12f,(b.y+b.w)*.5f),new Vector3(w,.24f,l),backing);
                int nx=Mathf.Max(1,Mathf.CeilToInt(w/4)),nz=Mathf.Max(1,Mathf.CeilToInt(l/4));
                for(int i=0;i<nx;i++)for(int j=0;j<nz;j++)
                {
                    var lo=new Vector3(Mathf.Lerp(b.x,b.z,i/(float)nx),31.7f,Mathf.Lerp(b.y,b.w,j/(float)nz));
                    var hi=new Vector3(Mathf.Lerp(b.x,b.z,(i+1f)/nx),32.013f,Mathf.Lerp(b.y,b.w,(j+1f)/nz));
                    Fit("P_Floor_Pattern_03_LP_B",temp,Quaternion.identity,lo,hi,true);
                    lo.y=35.78f;hi.y=36;Fit("P_Roof_Pattern_01_LP",temp,Quaternion.Euler(180,0,0),lo,hi);
                }
            }
            var edge=new[]{new Vector2(-49.5f,-16),new Vector2(-72,-16),new Vector2(-72,-5.5f),new Vector2(-80.5f,-5.5f),new Vector2(-80.5f,-1.5f),new Vector2(-68,-1.5f),new Vector2(-68,-12),new Vector2(-49.5f,-12)};
            for(int i=0;i<edge.Length-1;i++)
            {
                if(i==3)continue; // The west edge is the cockpit doorway.
                var a=edge[i];var b=edge[i+1];var inward=new Vector3(b.y-a.y,0,-(b.x-a.x)).normalized;
                var min=new Vector3(Mathf.Min(a.x,b.x),32,Mathf.Min(a.y,b.y));var max=new Vector3(Mathf.Max(a.x,b.x),36,Mathf.Max(a.y,b.y));
                Solid(structure,"F5_Access_Wall_"+i,(min+max)*.5f,new Vector3(Mathf.Max(.3f,max.x-min.x),4,Mathf.Max(.3f,max.z-min.z)),backing,false);
                Wall(temp,min,max,inward);
            }
            // Thresholds cover the exact visual cuts in the original floor and ceiling.
            Fit("P_Floor_Pattern_03_LP_B",temp,Quaternion.identity,new Vector3(-50.3f,31.7f,-16),new Vector3(-48.7f,32.025f,-12),true);
            BoxVisual(temp,"EntryCeilingThreshold",new Vector3(-49.5f,36.03f,-14),new Vector3(1.7f,.12f,4),trim);
            foreach(float x in new[]{-49.5f,-66f,-60f,-78.5f})
            {
                float z=x<-75?-3.5f:-14;
                foreach(float side in new[]{-1f,1f})BoxVisual(temp,"BulkheadSide",new Vector3(x,33.75f,z+side*1.94f),new Vector3(.28f,3.5f,.12f),trim);
                Solid(structure,"BulkheadHeader",new Vector3(x,35.78f,z),new Vector3(.28f,.44f,4),trim,false);
                BoxVisual(temp,"BulkheadHeader",new Vector3(x,35.78f,z),new Vector3(.28f,.44f,4),trim);
                BoxVisual(temp,"BulkheadIndicator",new Vector3(x+.16f,35.6f,z),new Vector3(.03f,.08f,1.6f),lamp);
            }
            // The original cockpit doorway is 5m wide. Close the 0.5m margins around the new 4m corridor.
            foreach(float z in new[]{-5.75f,-1.25f})
            {
                Solid(structure,"CockpitDoorAdapter",new Vector3(-80.5f,34,z),new Vector3(.4f,4,.5f),trim,false);
                BoxVisual(temp,"CockpitDoorAdapter",new Vector3(-80.5f,34,z),new Vector3(.4f,4,.5f),trim);
            }
            foreach(var p in new[]{new Vector3(-53,35.5f,-14),new Vector3(-63,35.5f,-14),new Vector3(-70,35.5f,-9),new Vector3(-77,35.5f,-3.5f)})
            {
                var holder=Child(lights,"F5_CockpitAccess_Emergency_"+lights.childCount);holder.position=p;
                var light=holder.gameObject.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.07f,.04f);light.intensity=1.1f;light.range=6.5f;light.shadows=LightShadows.None;
                BoxVisual(temp,"EmergencyLens",p+Vector3.up*.32f,new Vector3(.45f,.08f,.3f),lamp);
            }
            foreach(var pair in new[]{("F5_CockpitAccess_Entry",new Vector3(-47,32.1f,-14)),("F5_CockpitAccess_Vestibule",new Vector3(-63,32.1f,-14)),("F5_CockpitAccess_Cockpit",new Vector3(-80.5f,32.1f,-3.5f))})
                Child(root,pair.Item1).position=pair.Item2;
            Merge(temp,Child(root,"Visual"),"F5Access");return root;
        }
        static void SealOldHull(Transform temp)
        {
            // Original west plating skipped exactly columns 4/5, band 2, around the old opening.
            float step=(ShipHullBuilder.Z1-ShipHullBuilder.Z0)/10f;float h=(ShipHullBuilder.Y1-ShipHullBuilder.Y0)/5f;
            for(int i=4;i<=5;i++)
            {
                float z0=ShipHullBuilder.Z0+i*step,z1=z0+step,y0=ShipHullBuilder.Y0+2*h;
                Fit("P_Wall_Ext_Module_01_NoLight",temp,Quaternion.LookRotation(Vector3.right,Vector3.up),new Vector3(-74,y0,z0),new Vector3(-72.8f,y0+h,z1));
            }
        }
        static void BuildCollar(Transform root)
        {
            var temp=Child(root,"_Temp");
            // A sloped pressure collar joins the raised canopy to the existing hull roof.
            var corners=new[]{new Vector3(-80.1f,31.95f,-18.4f),new Vector3(-80.1f,45.2f,-18.4f),new Vector3(-72.6f,36.6f,-18.4f),new Vector3(-72.6f,31.95f,-18.4f),new Vector3(-80.1f,31.95f,11.4f),new Vector3(-80.1f,45.2f,11.4f),new Vector3(-72.6f,36.6f,11.4f),new Vector3(-72.6f,31.95f,11.4f)};
            var mesh=new Mesh { name="F5_Cockpit_PressureCollar" };mesh.vertices=corners;
            var tris=new[]{0,1,2,0,2,3,4,7,6,4,6,5,1,5,6,1,6,2,0,3,7,0,7,4};
            mesh.triangles=tris;mesh.RecalculateNormals();mesh.RecalculateBounds();
            var g=Child(temp,"UpperCollar").gameObject;g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=trim;
            foreach(float z in new[]{-18.4f,11.4f})BoxVisual(temp,"CollarLowerTrim",new Vector3(-76.35f,32,z),new Vector3(7.5f,.35f,.35f),trim);
            Merge(temp,Child(root,"Visual"),"CockpitCollar");Object.DestroyImmediate(mesh);
        }
        static HashSet<string> OldOuterShellKeys(float floor)
        {
            var scene=EditorSceneManager.NewPreviewScene();GameObject shell=null;
            try
            {
                shell=Object.Instantiate(kit["P_Ship_Exterior_Cockpit_01"]);SceneManager.MoveGameObjectToScene(shell,scene);
                shell.transform.rotation=Quaternion.Euler(0,90,0);shell.transform.localScale=Vector3.one*3.3f;shell.transform.position=new Vector3(-74,floor-3.3f,-3.5f);
                var moved=new GameObject("CockpitInteriorTemplate");SceneManager.MoveGameObjectToScene(moved,scene);
                var move=new HashSet<string>(new[]{"Interior_01","Decal_A 1","Cockpit_Struct_01","Glass_01","Ship_Interior_Lamp_02","Light_01"});
                var drop=new HashSet<string>(new[]{"Ship_Interior_WallDoor_01","Ship_Interior_WallDoor_02","Ship_Interior_WallSep_01","Ship_Interior_Plateform_01","_Colliders","P_SpaceShipLightAuto"});
                foreach(var tr in shell.GetComponentsInChildren<Transform>(true))
                {
                    if(tr==null||tr==shell.transform||!move.Contains(tr.name))continue;
                    bool parentMoved=false;for(var p=tr.parent;p!=null;p=p.parent)if(p.IsChildOf(moved.transform)){parentMoved=true;break;}
                    if(!parentMoved)tr.SetParent(moved.transform,true);
                }
                foreach(var tr in shell.GetComponentsInChildren<Transform>(true))if(tr!=null&&tr!=shell.transform&&drop.Contains(tr.name))Object.DestroyImmediate(tr.gameObject);
                var keys=new HashSet<string>();
                foreach(var mf in shell.GetComponentsInChildren<MeshFilter>(false))
                {
                    var renderer=mf.GetComponent<MeshRenderer>();if(renderer==null||!renderer.enabled||mf.sharedMesh==null)continue;
                    var data=Geo.Read(mf);var mats=renderer.sharedMaterials;
                    for(int sm=0;sm<data.triangles.Length;sm++){var ix=data.triangles[sm];for(int i=0;i<ix.Count;i+=3)keys.Add(Geo.TriangleKey(data.vertices[ix[i]],data.vertices[ix[i+1]],data.vertices[ix[i+2]],mats[Math.Min(sm,mats.Length-1)]));}
                }
                return keys;
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
#endif
