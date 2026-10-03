using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
public static partial class InfantryAssetBuild
{
    static void FitSourceSword(GameObject rig,bool heavy,int level)
    {
        var handle=rig.GetComponentsInChildren<Transform>().First(t=>t.name=="Sabre");
        foreach(var renderer in handle.GetComponentsInChildren<Renderer>())UnityEngine.Object.DestroyImmediate(renderer.gameObject);
        SourceWeapon(handle,heavy?"gap_cuirassiersword.FBX":"gap_infantry_sabre.FBX",level,false);
    }
    static void SourceWeapon(Transform parent,string file,int level,bool pistol)
    {
        string path=Root+"/RosterModels/"+file;var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!prefab)throw new Exception("Source weapon missing: "+path);
        var model=UnityEngine.Object.Instantiate(prefab);var renderers=model.GetComponentsInChildren<MeshRenderer>();
        int lod=level==0?4:5;
        var selected=renderers.Where(r=>r.name.ToLowerInvariant().Contains(".lod"+lod)).ToArray();
        if(selected.Length==0)selected=renderers.Where(r=>!r.name.ToLowerInvariant().Contains(".lod")).ToArray();
        if(selected.Length==0)throw new Exception("Source weapon geometry missing: "+file);
        var neutral=pistol?selected[0].transform.worldToLocalMatrix:model.transform.worldToLocalMatrix;
        var matrixList=selected.Select(r=>neutral*r.transform.localToWorldMatrix).ToArray();
        var all=selected.SelectMany((r,i)=>r.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v=>matrixList[i].MultiplyPoint3x4(v))).ToArray();
        var bounds=new Bounds(all[0],Vector3.zero);foreach(var p in all)bounds.Encapsulate(p);
        int axis=bounds.size.x>bounds.size.y&&bounds.size.x>bounds.size.z?0:bounds.size.y>bounds.size.z?1:2;
        bool reverse=!pistol&&file.Contains("infantry_sabre");float sign=reverse?-1:1;
        float length=bounds.size[axis];float anchor=reverse?bounds.max[axis]-length*.12f:bounds.min[axis]+length*(pistol?.22f:.12f);
        float scale=(pistol?.43f:.9f)/length;
        var front=all.Where(v=>v[axis]>bounds.max[axis]-.003f).ToArray();float frontDepth=front.Average(v=>v.z);float upSign=frontDepth>=bounds.center.z?1:-1;
        Vector3 Map(Vector3 p)
        {
            float longAxis=(p[axis]-anchor)*sign*scale;
            int lateral=(axis+1)%3,depth=(axis+2)%3;
            return pistol?new Vector3((p.x-bounds.center.x)*scale,(p.z-bounds.center.z)*upSign*scale,longAxis):new Vector3((p[lateral]-bounds.center[lateral])*scale,longAxis,(p[depth]-bounds.center[depth])*scale);
        }
        for(int i=0;i<selected.Length;i++)
        {
            var source=selected[i].GetComponent<MeshFilter>().sharedMesh;
            for(int sub=0;sub<source.subMeshCount;sub++)
            {
                var mesh=UnityEngine.Object.Instantiate(source);mesh.name=file+" / "+sub;mesh.vertices=source.vertices.Select(v=>Map(matrixList[i].MultiplyPoint3x4(v))).ToArray();var indices=source.GetTriangles(sub);var origin=Map(matrixList[i].MultiplyPoint3x4(Vector3.zero));var x=Map(matrixList[i].MultiplyPoint3x4(Vector3.right))-origin;var y=Map(matrixList[i].MultiplyPoint3x4(Vector3.up))-origin;var z=Map(matrixList[i].MultiplyPoint3x4(Vector3.forward))-origin;if(Vector3.Dot(x,Vector3.Cross(y,z))<0)for(int t=0;t<indices.Length;t+=3){int swap=indices[t+1];indices[t+1]=indices[t+2];indices[t+2]=swap;}mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();
                var part=new GameObject("Source "+file+" "+sub);part.transform.SetParent(parent,false);if(pistol)part.transform.localPosition=Vector3.down*.1f;part.AddComponent<MeshFilter>().sharedMesh=mesh;
                var material=selected[i].sharedMaterials[Math.Min(sub,selected[i].sharedMaterials.Length-1)];string key=material.name=="new_flintlock_wood"?"flintlock_pistol_wood":material.name=="new_flintlock_metal"?"flintlock_pistol_metal":material.name;part.AddComponent<MeshRenderer>().sharedMaterial=MaterialFor(key,0);
            }
        }
        if(pistol)
        {
            var vertices=parent.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Source ")).SelectMany(r=>r.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v=>parent.InverseTransformPoint(r.transform.TransformPoint(v)))).ToArray();
            float end=vertices.Max(v=>v.z);var tip=vertices.Where(v=>v.z>end-.003f).ToArray();Vector3 muzzle=Vector3.zero;foreach(var v in tip)muzzle+=v;muzzle/=tip.Length;
            var sockets=parent.gameObject.AddComponent<EmpireBattle.Art.AuthoredMountedFirearm>();sockets.Muzzle=muzzle;sockets.Pan=new Vector3(.018f,muzzle.y-.005f,.05f);sockets.LoadingGrip=new Vector3(0,muzzle.y-.012f,.075f);sockets.RodStow=new Vector3(0,muzzle.y-.02f,.17f);
        }
        UnityEngine.Object.DestroyImmediate(model);
    }
}
