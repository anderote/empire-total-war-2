using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;
public static class NapoleonicPosePreview
{
    [Serializable]sealed class Pose {public string name,texture;public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles;}
    static bool allowMissing;
    public static void ExportAvailable(){allowMissing=true;try{ExportAll();}finally{allowMissing=false;}}
    static readonly List<string> names=new List<string>();
    static void Export(string name,Mesh mesh,Material material)
    {
        if(!mesh||!material)throw new Exception("Missing preview "+name);
        var texture=material.GetTexture("_BaseMap");if(!texture)texture=material.mainTexture;
        File.WriteAllText("Artifacts/NapoleonicOutfits/Poses/"+name+".json",JsonUtility.ToJson(new Pose{name=name,texture=AssetDatabase.GetAssetPath(texture),vertices=mesh.vertices,normals=mesh.normals,uv=mesh.uv,triangles=mesh.triangles}));names.Add(name);
    }
    public static void ExportAll()
    {
        Directory.CreateDirectory("Artifacts/NapoleonicOutfits/Poses");names.Clear();
        foreach(var dress in NapoleonicOutfits.All)
        {
            string root="Assets/Resources/NapoleonicOutfits/"+dress.Id;
            if(dress.Mounted){var lib=AssetDatabase.LoadAssetAtPath<CavalryCrowdLibrary>(root+"/CavalryCrowdLibrary.asset");if(allowMissing&&(!lib||lib.OutfitBakeVersion<4))continue;Export(dress.Id,lib.French.Lods[0].Clip(MountedMotion.Idle).Poses[0],lib.French.Material);}
            else {var lib=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(root+"/Library.asset");if(allowMissing&&(!lib||lib.BakeVersion<2))continue;Export(dress.Id,lib.Set.Lods[0].Idle[0],lib.Set.Material);}
            EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
        }
        var guard=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>("Assets/Resources/NapoleonicOutfits/france-guard/Library.asset");
        foreach(int frame in new[]{0,5,10,11,16,22})Export("grenade-"+frame,guard.Set.Lods[0].GrenadeThrow[frame],guard.Set.Material);
        var cavalry=AssetDatabase.LoadAssetAtPath<CavalryCrowdLibrary>("Assets/Resources/NapoleonicOutfits/france-hussar/CavalryCrowdLibrary.asset");
        if(allowMissing&&!cavalry)cavalry=AssetDatabase.LoadAssetAtPath<CavalryCrowdLibrary>("Assets/Resources/NapoleonicOutfits/france-cuirassier/CavalryCrowdLibrary.asset");
        foreach(var motion in new[]{MountedMotion.SlashRight,MountedMotion.LanceReady,MountedMotion.LanceThrust,MountedMotion.MountedAim,MountedMotion.MountedFire,MountedMotion.MountedReload,MountedMotion.PistolAim,MountedMotion.PistolFire,MountedMotion.PistolReload})
        {if(allowMissing&&(!cavalry||cavalry.OutfitBakeVersion<4))break;var clip=cavalry.French.Lods[0].Clip(motion);foreach(int frame in new[]{0,clip.Poses.Length/3,clip.Poses.Length*2/3,clip.Poses.Length-1}.Distinct())Export(motion+"-"+frame,clip.Poses[frame],cavalry.French.Material);}
        foreach(string faction in new[]{"france","britain","austria","prussia","russia","italy","hungary","ottoman"})
        {var command=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>("Assets/Resources/NapoleonicCommands/"+faction+"/Library.asset");if(allowMissing&&(!command||command.BakeVersion<1))continue;Export("command-"+faction+"-captain",command.Set.Lods[0].Command[0].Idle[0],command.Set.Material);Export("command-"+faction+"-colonel",command.Set.Lods[0].Command[4].Idle[0],command.Set.Material);}
        File.WriteAllLines("Artifacts/NapoleonicOutfits/Poses/index.txt",names);Debug.Log("NAPOLEONIC_PREVIEWS_EXPORTED "+names.Count);
    }
}
