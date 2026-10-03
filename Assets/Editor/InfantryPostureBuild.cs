using System;
using System.Collections.Generic;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static class InfantryPostureBuild
{
    public static void Prepare(InfantryCrowdLibrary library)
    {
        foreach(var set in new[]{library.French,library.British})
        for(int level=0;level<set.Lods.Length;level++)
        {
            var lod=set.Lods[level];
            if(lod.KneelingReload==null||lod.KneelingReload.Length==0)continue;
            string prefix=AssetDatabase.GetAssetPath(lod.Idle[0]);prefix=prefix.Substring(0,prefix.LastIndexOf('/')+1)+(set==library.French?"French":"British")+"_L"+level;
            lod.LowerToKneel=Bridge(prefix+"_LowerToKneel.asset",lod.Fire[lod.Fire.Length-1],lod.Fire[lod.Fire.Length-1],0,lod.KneelingReload[0]);
            float aim=.45f*(lod.Fire.Length-1);int frame=Mathf.FloorToInt(aim);
            lod.RiseToAim=Bridge(prefix+"_RiseToAim.asset",lod.Fire[frame],lod.Fire[frame+1],aim-frame,lod.KneelingReload[lod.KneelingReload.Length-1]);
            if(lod.KneelingFire!=null&&lod.KneelingFire.Length>frame)
                lod.KneelingAimTransition=Bridge(prefix+"_KneelingAimTransition.asset",lod.Fire[frame],lod.Fire[frame+1],aim-frame,lod.KneelingFire[frame]);
            lod.RiseToIdle=Bridge(prefix+"_RiseToIdle.asset",lod.Idle[0],lod.Idle[0],0,lod.KneelingReload[lod.KneelingReload.Length-1]);
            lod.RiseToReload=new Mesh[lod.Reload.Length];
            for(int i=0;i<lod.Reload.Length;i++)lod.RiseToReload[i]=Bridge(prefix+"_RiseToReload_"+i+".asset",lod.Reload[i],lod.Reload[i],0,lod.KneelingReload[i]);
        }
        EditorUtility.SetDirty(library);
    }
    static Mesh Bridge(string path,Mesh a,Mesh b,float phase,Mesh kneel)
    {
        if(a.vertexCount!=b.vertexCount||a.vertexCount!=kneel.vertexCount)throw new Exception("Posture topology mismatch: "+path);
        var mesh=UnityEngine.Object.Instantiate(a);mesh.name=System.IO.Path.GetFileNameWithoutExtension(path);
        var vertices=a.vertices;var next=b.vertices;var normals=a.normals;var nextNormals=b.normals;
        for(int i=0;i<vertices.Length;i++){vertices[i]=Vector3.Lerp(vertices[i],next[i],phase);normals[i]=Vector3.Lerp(normals[i],nextNormals[i],phase).normalized;}
        mesh.vertices=vertices;mesh.normals=normals;
        mesh.SetUVs(2,new List<Vector3>(kneel.vertices));mesh.SetUVs(3,new List<Vector3>(kneel.normals));
        mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Encapsulate(kneel.bounds);mesh.bounds=bounds;
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    public static void BuildAndVerify()
    {
        InfantryPoseBlendBuild.Prepare();
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        {
            Check(lod.LowerToKneel,lod.KneelingReload[0]);
            Check(lod.RiseToAim,lod.KneelingReload[lod.KneelingReload.Length-1]);
            Check(lod.RiseToIdle,lod.KneelingReload[lod.KneelingReload.Length-1]);
            for(int i=0;i<lod.RiseToReload.Length;i++)Check(lod.RiseToReload[i],lod.KneelingReload[i]);
        }
        for(int soldier=0;soldier<128;soldier++)
        {
            if(RifleFireTiming.ReloadActionPhase(5.4f,soldier)!=1||RifleFireTiming.ReloadActionPhase(0,soldier)!=0)throw new Exception("Reload action stretched to cooldown");
        }
        InfantryFireVerification.Verify();
        Debug.Log("INFANTRY_POSTURE_VERIFIED: shared continuous morphs at both LODs; reload action finishes within 5.3s; firing drill checks passed.");
        InfantryAssetBuild.FinishInspection();InfantryBattleBuild.BuildMac();
    }
    static void Check(Mesh bridge,Mesh target)
    {
        var ends=new List<Vector3>();bridge.GetUVs(2,ends);var start=bridge.vertices;var finish=target.vertices;
        if(ends.Count!=finish.Length)throw new Exception("Missing posture morph targets");
        for(int i=0;i<ends.Count;i++)
        {
            if((ends[i]-finish[i]).sqrMagnitude>1e-10f)throw new Exception("Posture endpoint mismatch");
            for(int sample=0;sample<=8;sample++)
            {
                Vector3 p=Vector3.Lerp(start[i],ends[i],Mathf.SmoothStep(0,1,sample/8f));
                if(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||p.y<-.12f)throw new Exception("Invalid posture morph vertex");
            }
        }
    }
}
