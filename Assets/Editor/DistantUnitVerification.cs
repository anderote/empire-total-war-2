using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class DistantUnitVerification
{
    public static void Verify()
    {
        var report=new StringBuilder();
        foreach(string family in new[]{"BakedInfantry","BakedCavalry"})
        foreach(string side in new[]{"British","French"})
        {
            string path=$"Assets/Resources/{family}/{side}_L1_Idle"+(family=="BakedInfantry"?"_V0":"")+"_0.asset";
            var source=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!source)throw new Exception("Missing sample "+path);
            float cell=family=="BakedInfantry"?.12f:.18f;
            int previous=source.triangles.Length;
            for(int tier=1;tier<=2;tier++)
            {
                var mesh=DistantUnitMesh.Build(source,cell*tier);
                if(mesh==source||mesh.triangles.Length>=previous)throw new Exception("LOD fails to reduce geometry: "+path+" tier "+tier);
                previous=mesh.triangles.Length;
                var next=new List<Vector3>();mesh.GetUVs(2,next);
                var normals=new List<Vector3>();mesh.GetUVs(3,normals);
                if(next.Count!=mesh.vertexCount||normals.Count!=mesh.vertexCount||mesh.uv.Length!=mesh.vertexCount)
                    throw new Exception("Missing morph/atlas streams");
                foreach(var point in next)if(!float.IsFinite(point.x)||!float.IsFinite(point.y)||!float.IsFinite(point.z))throw new Exception("Invalid morph position");
                foreach(int index in mesh.triangles)if(index<0||index>=mesh.vertexCount)throw new Exception("Invalid mesh index");
                report.AppendLine($"{family}/{side} tier {tier}: {source.triangles.Length/3} -> {mesh.triangles.Length/3} triangles, {mesh.vertexCount} vertices");
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        if(DistantUnitMesh.Tier(179*179)!=0||DistantUnitMesh.Tier(181*181)!=1||DistantUnitMesh.Tier(351*351)!=2)
            throw new Exception("Distance thresholds");
        // Thin equipment must retain geometry rather than disappearing entirely.
        var thin=new Mesh();thin.vertices=new[]{Vector3.zero,Vector3.right*.01f,Vector3.up};thin.triangles=new[]{0,1,2};
        if(DistantUnitMesh.Build(thin,1f)!=thin)throw new Exception("Thin mesh fallback");
        UnityEngine.Object.DestroyImmediate(thin);
        Directory.CreateDirectory("Artifacts/DistantUnits");
        File.WriteAllText("Artifacts/DistantUnits/verification.txt",report+"DISTANT_UNIT_LOD_PASSED\n");
        Debug.Log(report+"DISTANT_UNIT_LOD_PASSED");
    }
}
