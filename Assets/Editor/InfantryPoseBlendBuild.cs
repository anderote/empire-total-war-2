using System;
using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static class InfantryPoseBlendBuild
{
    public static void Prepare()
    {
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        var shader=Shader.Find("EmpireBattle/CrowdInfantry");
        if(!library || !shader)throw new Exception("Missing infantry library or blend shader");
        if(ShaderUtil.ShaderHasError(shader))throw new Exception("Infantry blend shader failed to compile");
        int count=0;
        var sets=new List<InfantryCrowdSet>{library.French,library.British};
        var cartridges=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
        if(cartridges)sets.AddRange(new[]{cartridges.French,cartridges.British,cartridges.SingleFrench,cartridges.SingleBritish});
        foreach(var set in sets)
        {
            foreach(var lod in set.Lods)
            {
                if(lod.RecoveryVariants!=null)foreach(var v in lod.RecoveryVariants){count+=Prepare(v.Knockdown,false);count+=Prepare(v.GetUp,false);}
                if(lod.Melee!=null)foreach(var clip in lod.Melee){count+=Prepare(clip.Poses,false);count+=Prepare(clip.Kneeling,false);}
                if(lod.IdleVariants!=null)foreach(var v in lod.IdleVariants)count+=Prepare(v.Poses,true);
                if(lod.UnderFireVariants!=null)foreach(var v in lod.UnderFireVariants)count+=Prepare(v.Poses,true);
                if(lod.Command!=null)foreach(var v in lod.Command){count+=Prepare(v.Idle,true);count+=Prepare(v.March,true);count+=Prepare(v.PikeReady,true);count+=Prepare(v.OfficerReady,true);count+=Prepare(v.KneelingIdle,true);count+=Prepare(v.KneelingStrike,false);foreach(var poses in new[]{v.PikeThrust,v.PikeKnockdown,v.PikeGetUp,v.PikeDeath,v.OfficerSlash,v.OfficerFire,v.OfficerKnockdown,v.OfficerGetUp,v.OfficerDeath})count+=Prepare(poses,false);}
                if(lod.DeathVariants!=null)foreach(var death in lod.DeathVariants)count+=Prepare(death.Poses,false);
                if(lod.Locomotion!=null)foreach(var variant in lod.Locomotion)foreach(var poses in variant.Loops())count+=Prepare(poses,true);
                foreach(var poses in new[]{lod.Idle,lod.March,lod.Run,lod.Charge})count+=Prepare(poses,true);
                foreach(var poses in new[]{lod.Fire,lod.KneelingFire,lod.Reload,lod.KneelingReload,lod.Hit,lod.Death,lod.Knockdown,lod.GetUp})count+=Prepare(poses,false);
            }
            set.Material.shader=shader;
            set.Material.shaderKeywords=Array.Empty<string>();
            set.Material.enableInstancing=true;
            EditorUtility.SetDirty(set.Material);
        }
        InfantryPostureBuild.Prepare(library);
        AssetDatabase.SaveAssets();
        Debug.Log($"INFANTRY_BLEND_OK: {count} poses, consistent vertex topology, interpolated geometry and shadows.");
    }
    static int Prepare(Mesh[] poses,bool loop)
    {
        if(poses==null)return 0;
        for(int i=0;i<poses.Length;i++)
        {
            Mesh mesh=poses[i],next=poses[i+1<poses.Length?i+1:loop?0:i];
            if(!mesh || !next || mesh.vertexCount!=next.vertexCount)throw new Exception("Pose topology mismatch");
            mesh.SetUVs(2,new List<Vector3>(next.vertices));
            mesh.SetUVs(3,new List<Vector3>(next.normals));
            var bounds=mesh.bounds; bounds.Encapsulate(next.bounds); mesh.bounds=bounds;
            EditorUtility.SetDirty(mesh);
        }
        return poses.Length;
    }
}
