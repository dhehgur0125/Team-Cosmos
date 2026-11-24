#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VoidLast.MapTools
{
    // Editor-only mesh copies. Source kit meshes and previously baked assets are never overwritten.
    internal static class ShipCockpitFifthFloorGeometry
    {
        internal struct V
        {
            public Vector3 p, n;
            public Vector4 tangent;
            public Vector2 uv;
            public static V Lerp(V a, V b, float t)
            {
                var tangent = Vector4.Lerp(a.tangent, b.tangent, t);
                var xyz = new Vector3(tangent.x, tangent.y, tangent.z).normalized;
                return new V { p = Vector3.Lerp(a.p,b.p,t), n = Vector3.Lerp(a.n,b.n,t).normalized,
                    tangent = new Vector4(xyz.x,xyz.y,xyz.z,tangent.w < 0 ? -1 : 1), uv = Vector2.Lerp(a.uv,b.uv,t) };
            }
        }
        internal sealed class Buffer
        {
            public List<V> vertices = new List<V>();
            public List<int>[] triangles;
        }
        internal static Buffer Read(MeshFilter mf)
        {
            var mesh=mf.sharedMesh;
            var result=new Buffer { triangles=new List<int>[mesh.subMeshCount] };
            using(var array=MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var data=array[0];
                var p=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp);
                var n=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp);
                var t=new NativeArray<Vector4>(data.vertexCount,Allocator.Temp);
                var uv=new NativeArray<Vector2>(data.vertexCount,Allocator.Temp);
                try
                {
                    data.GetVertices(p);
                    if(mesh.HasVertexAttribute(VertexAttribute.Normal))data.GetNormals(n);
                    if(mesh.HasVertexAttribute(VertexAttribute.Tangent))data.GetTangents(t);
                    if(mesh.HasVertexAttribute(VertexAttribute.TexCoord0))data.GetUVs(0,uv);
                    var matrix=mf.transform.localToWorldMatrix;var normals=matrix.inverse.transpose;
                    for(int i=0;i<p.Length;i++)
                    {
                        var tangent=matrix.MultiplyVector(new Vector3(t[i].x,t[i].y,t[i].z)).normalized;
                        result.vertices.Add(new V { p=matrix.MultiplyPoint3x4(p[i]), n=normals.MultiplyVector(n[i]).normalized,
                            tangent=new Vector4(tangent.x,tangent.y,tangent.z,t[i].w*(matrix.determinant<0?-1:1)), uv=uv[i] });
                    }
                    for(int sm=0;sm<data.subMeshCount;sm++)
                    {
                        var sub=data.GetSubMesh(sm);var ix=new List<int>(sub.indexCount);
                        for(int i=sub.indexStart;i<sub.indexStart+sub.indexCount;i++)
                            ix.Add((mesh.indexFormat==IndexFormat.UInt32?data.GetIndexData<int>()[i]:data.GetIndexData<ushort>()[i])+sub.baseVertex);
                        result.triangles[sm]=ix;
                    }
                }
                finally{p.Dispose();n.Dispose();t.Dispose();uv.Dispose();}
            }
            return result;
        }
        internal static Mesh Build(Buffer b, Matrix4x4 toLocal, string name)
        {
            var used=new Dictionary<int,int>();var verts=new List<V>();
            var subs=new List<int>[b.triangles.Length];
            for(int sm=0;sm<subs.Length;sm++)
            {
                subs[sm]=new List<int>(b.triangles[sm].Count);
                foreach(int old in b.triangles[sm])
                {
                    int mapped;if(!used.TryGetValue(old,out mapped)){mapped=verts.Count;used[old]=mapped;verts.Add(b.vertices[old]);}
                    subs[sm].Add(mapped);
                }
            }
            var mesh=new Mesh { name=name,indexFormat=IndexFormat.UInt32 };
            var normalMatrix=toLocal.inverse.transpose;
            mesh.SetVertices(verts.Select(v=>toLocal.MultiplyPoint3x4(v.p)).ToList());
            mesh.SetNormals(verts.Select(v=>normalMatrix.MultiplyVector(v.n).normalized).ToList());
            mesh.SetTangents(verts.Select(v=>{var t=toLocal.MultiplyVector(new Vector3(v.tangent.x,v.tangent.y,v.tangent.z)).normalized;return new Vector4(t.x,t.y,t.z,v.tangent.w*(toLocal.determinant<0?-1:1));}).ToList());
            mesh.SetUVs(0,verts.Select(v=>v.uv).ToList());mesh.subMeshCount=subs.Length;
            for(int sm=0;sm<subs.Length;sm++)mesh.SetTriangles(subs[sm],sm,false);
            mesh.RecalculateBounds();return mesh;
        }
        internal static Mesh Save(Mesh mesh,string path)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
            EditorUtility.CopySerialized(mesh,existing);EditorUtility.SetDirty(existing);Object.DestroyImmediate(mesh);return existing;
        }
        static float Distance(V v,int axis,float value,bool less)
        {return less?value-v.p[axis]:v.p[axis]-value;}
        static List<V> Clip(List<V> input,int axis,float value,bool less)
        {
            var output=new List<V>();if(input.Count==0)return output;
            var previous=input[input.Count-1];float dp=Distance(previous,axis,value,less);
            foreach(var current in input)
            {
                float dc=Distance(current,axis,value,less);bool pi=dp>=0,ci=dc>=0;
                if(pi!=ci)output.Add(V.Lerp(previous,current,dp/(dp-dc)));
                if(ci)output.Add(current);previous=current;dp=dc;
            }
            return output;
        }
        static void Append(Buffer result,int sub,List<V> polygon)
        {
            if(polygon.Count<3)return;int start=result.vertices.Count;result.vertices.AddRange(polygon);
            for(int i=1;i+1<polygon.Count;i++)
            {
                if(Vector3.Cross(polygon[i].p-polygon[0].p,polygon[i+1].p-polygon[0].p).sqrMagnitude<1e-12f)continue;
                result.triangles[sub].Add(start);result.triangles[sub].Add(start+i);result.triangles[sub].Add(start+i+1);
            }
        }
        // Partition each triangle into outside polygons, discarding only the exact cutout volume.
        internal static bool Subtract(MeshFilter mf,Bounds cut,string assetPath)
        {
            var renderer=mf.GetComponent<MeshRenderer>();if(renderer==null||!renderer.bounds.Intersects(cut))return false;
            var input=Read(mf);var result=new Buffer { triangles=input.triangles.Select(s=>new List<int>()).ToArray() };
            bool touched=false;
            for(int sm=0;sm<input.triangles.Length;sm++)
            {
                var ix=input.triangles[sm];
                for(int i=0;i<ix.Count;i+=3)
                {
                    var tri=new List<V>{input.vertices[ix[i]],input.vertices[ix[i+1]],input.vertices[ix[i+2]]};
                    var box=new Bounds(tri[0].p,Vector3.zero);box.Encapsulate(tri[1].p);box.Encapsulate(tri[2].p);
                    if(!box.Intersects(cut)){Append(result,sm,tri);continue;}
                    var remainder=tri;var outside=new List<List<V>>();
                    for(int axis=0;axis<3&&remainder.Count>0;axis++)
                    {
                        outside.Add(Clip(remainder,axis,cut.min[axis],true));remainder=Clip(remainder,axis,cut.min[axis],false);
                        if(remainder.Count==0)break;
                        outside.Add(Clip(remainder,axis,cut.max[axis],false));remainder=Clip(remainder,axis,cut.max[axis],true);
                    }
                    if(remainder.Count>=3)touched=true;
                    foreach(var polygon in outside)Append(result,sm,polygon);
                }
            }
            if(!touched)return false;
            Undo.RecordObject(mf,"Open cockpit access passage");
            mf.sharedMesh=Save(Build(result,mf.transform.worldToLocalMatrix,mf.name+"_F5Access"),assetPath);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mf);EditorUtility.SetDirty(mf);return true;
        }
        internal static string PointKey(Vector3 p)
        {return Math.Round(p.x*200)+","+Math.Round(p.y*200)+","+Math.Round(p.z*200);}
        internal static string TriangleKey(V a,V b,V c,Material material)
        {
            var points=new[]{PointKey(a.p),PointKey(b.p),PointKey(c.p)};Array.Sort(points,StringComparer.Ordinal);
            return material.GetEntityId().ToString()+":"+string.Join(";",points);
        }
        internal static void MoveMatchingShell(Transform visual,HashSet<string> keys,float rise,string directory)
        {
            var found=new HashSet<string>();
            foreach(var mf in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=mf.GetComponent<MeshRenderer>();var box=renderer.bounds;
                if(box.min.x>-73||box.max.x<-132||box.min.z>14||box.max.z<-21)continue;
                var buffer=Read(mf);var materials=renderer.sharedMaterials;bool changed=false;
                for(int sm=0;sm<buffer.triangles.Length;sm++)
                {
                    var ix=buffer.triangles[sm];
                    for(int i=0;i<ix.Count;i+=3)
                    {
                        var a=buffer.vertices[ix[i]];var b=buffer.vertices[ix[i+1]];var c=buffer.vertices[ix[i+2]];
                        var key=TriangleKey(a,b,c,materials[Math.Min(sm,materials.Length-1)]);if(!keys.Contains(key))continue;
                        found.Add(key);changed=true;
                        foreach(var pair in new[]{(a,0),(b,1),(c,2)}){var v=pair.Item1;v.p+=Vector3.up*rise;ix[i+pair.Item2]=buffer.vertices.Count;buffer.vertices.Add(v);}
                    }
                }
                if(!changed)continue;Undo.RecordObject(mf,"Raise cockpit exterior");
                mf.sharedMesh=Save(Build(buffer,mf.transform.worldToLocalMatrix,mf.name+"_RaisedCockpit"),directory+"/"+mf.name+"_Raised.asset");
                PrefabUtility.RecordPrefabInstancePropertyModifications(mf);EditorUtility.SetDirty(mf);
            }
            if(found.Count!=keys.Count)throw new InvalidOperationException("Cockpit exterior match incomplete: "+found.Count+"/"+keys.Count);
        }
    }
}
#endif
