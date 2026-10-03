using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static class WeaponBudgetVerification
{
    public static void BakeAndVerify()
    {
        string output="Artifacts/WeaponBudget";Directory.CreateDirectory(output);
        var receipt=new StringBuilder();
        for(int family=0;family<2;family++)for(int level=0;level<3;level++)
        {
            var mesh=CartridgeWeaponAuthoring.Prepare(family==1,level);
            VerifyMesh(mesh);
            int triangles=mesh.triangles.Length/3;
            int budget=level==0?CartridgeWeaponAuthoring.CloseBudget:level==1?CartridgeWeaponAuthoring.CrowdBudget:CartridgeWeaponAuthoring.DistantBudget;
            Require(triangles<=budget,"Weapon budget exceeded");
            Require(mesh.bounds.size.z>57 && mesh.bounds.size.z<60,"Weapon length changed away from grip calibration");
            receipt.AppendLine($"{(family==1?"Single-shot":"Magazine")} L{level}: {triangles} triangles / {budget} maximum");
            ExportObj(mesh,output+"/"+(family==1?"SingleShot":"Magazine")+"_L"+level+".obj");
        }
        InfantryAssetBuild.PrepareCartridgeRifles();
        var library=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
        Require(library && library.WeaponBakeVersion==CartridgeWeaponAuthoring.Version,"Wrong weapon bake version");
        var shader=Shader.Find("EmpireBattle/CrowdInfantry");Require(shader && !ShaderUtil.ShaderHasError(shader),"Crowd shader failed");
        int poseCount=0;
        foreach(var set in new[]{library.French,library.British,library.SingleFrench,library.SingleBritish})
        {
            Require(set!=null && set.Lods.Length==3 && set.Material && set.Material.GetTexture("_BaseMap"),"Missing cartridge atlas or LOD");
            Require(set.MuzzleOffset.y>1.2f && set.MuzzleOffset.y<1.8f && set.MuzzleOffset.z>.7f && set.MuzzleOffset.z<1.6f,"Muzzle attachment outside expected fire position");
            float previous=0;
            foreach(var lod in set.Lods)
            {
                Require(lod.MaxDistance>previous,"LOD distances must increase");previous=lod.MaxDistance;
                foreach(var poses in new[]{lod.Idle,lod.March,lod.Run,lod.Charge})poseCount+=Blend(poses,true);
                foreach(var poses in new[]{lod.Fire,lod.KneelingFire,lod.Reload,lod.KneelingReload,lod.Hit,lod.Death,lod.Knockdown,lod.GetUp})poseCount+=Blend(poses,false);
                Require(lod.Fire[0].vertexCount==lod.KneelingFire[0].vertexCount && lod.Reload[0].vertexCount==lod.KneelingReload[0].vertexCount,"Kneeling morph topology differs");
            }
            set.Material.shader=shader;set.Material.shaderKeywords=Array.Empty<string>();set.Material.enableInstancing=true;EditorUtility.SetDirty(set.Material);
        }
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        receipt.AppendLine($"Verified {poseCount} poses: valid triangles, finite positions/normals/UVs, matching animation topology, blend streams, faction atlases, muzzle attachments and three LOD distances.");
        receipt.AppendLine("Original authored meshes; external model downloads remain unavailable. No native game build or playtest publication.");
        File.WriteAllText(output+"/verification.txt",receipt.ToString());
        ExportPosePreviews();
        Debug.Log("WEAPON_BUDGET_VERIFIED\n"+receipt);
    }
    [Serializable] sealed class PosePreview
    {
        public Vector3[] vertices,normals;
        public Vector2[] uv;
        public int[] triangles;
        public string texture;
    }
    public static void ExportPosePreviews()
    {
        var library=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
        Require(library && library.WeaponBakeVersion==CartridgeWeaponAuthoring.Version,"Missing validated cartridge bake");
        string output="Artifacts/WeaponBudget";Directory.CreateDirectory(output);
        var sets=new[]{library.French,library.French,library.British,library.SingleBritish};
        var meshes=new[]{library.French.Lods[0].Fire[3],library.French.Lods[0].Reload[12],library.British.Lods[0].KneelingFire[3],library.SingleBritish.Lods[0].Fire[3]};
        string[] names={"MagazineFire","MagazineReload","MagazineKneeling","SingleShotFire"};
        for(int i=0;i<meshes.Length;i++)
        {
            var mesh=meshes[i];var data=new PosePreview{vertices=mesh.vertices,normals=mesh.normals,uv=mesh.uv,triangles=mesh.triangles,texture=AssetDatabase.GetAssetPath(sets[i].Material.GetTexture("_BaseMap"))};
            File.WriteAllText(output+"/"+names[i]+".json",JsonUtility.ToJson(data));
        }
    }
    static int Blend(Mesh[] poses,bool loop)
    {
        Require(poses!=null && poses.Length>0,"Missing animation poses");
        int[] indices=poses[0].triangles;
        for(int i=0;i<poses.Length;i++)
        {
            var mesh=poses[i];VerifyMesh(mesh);
            int[] actual=mesh.triangles;Require(actual.Length==indices.Length,"Animation triangle count changes");
            for(int j=0;j<actual.Length;j++)Require(actual[j]==indices[j],"Animation vertex order changes");
            var next=poses[i+1<poses.Length?i+1:loop?0:i];Require(mesh.vertexCount==next.vertexCount,"Animation vertex count changes");
            mesh.SetUVs(2,new List<Vector3>(next.vertices));mesh.SetUVs(3,new List<Vector3>(next.normals));
            var bounds=mesh.bounds;bounds.Encapsulate(next.bounds);mesh.bounds=bounds;EditorUtility.SetDirty(mesh);
        }
        return poses.Length;
    }
    static void VerifyMesh(Mesh mesh)
    {
        Require(mesh && mesh.vertexCount>0,"Missing mesh");var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;
        Require(normals.Length==vertices.Length && uv.Length==vertices.Length,"Missing normals or texture coordinates");
        foreach(var index in mesh.triangles)Require(index>=0 && index<vertices.Length,"Invalid triangle index");
        for(int i=0;i<vertices.Length;i++)
        {
            Require(Finite(vertices[i].x)&&Finite(vertices[i].y)&&Finite(vertices[i].z),"Nonfinite vertex");
            Require(Finite(normals[i].x)&&Finite(normals[i].y)&&Finite(normals[i].z),"Nonfinite normal");
            Require(Finite(uv[i].x)&&Finite(uv[i].y)&&uv[i].x>=0&&uv[i].x<=1&&uv[i].y>=0&&uv[i].y<=1,"Invalid texture coordinate");
        }
    }
    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void ExportObj(Mesh mesh,string path)
    {
        var text=new StringBuilder();var culture=CultureInfo.InvariantCulture;
        foreach(var p in mesh.vertices)text.AppendLine("v "+p.x.ToString(culture)+" "+p.y.ToString(culture)+" "+p.z.ToString(culture));
        foreach(var p in mesh.uv)text.AppendLine("vt "+p.x.ToString(culture)+" "+p.y.ToString(culture));
        foreach(var p in mesh.normals)text.AppendLine("vn "+p.x.ToString(culture)+" "+p.y.ToString(culture)+" "+p.z.ToString(culture));
        var indices=mesh.triangles;for(int i=0;i<indices.Length;i+=3){text.Append("f");for(int k=0;k<3;k++){int v=indices[i+k]+1;text.Append($" {v}/{v}/{v}");}text.AppendLine();}
        File.WriteAllText(path,text.ToString());
    }
}
