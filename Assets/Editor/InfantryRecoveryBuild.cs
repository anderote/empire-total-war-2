using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;

public static partial class InfantryAssetBuild
{
    static void BakeGroundedLod(GameObject rig,AnimationClip clip,InfantryCrowdLod lod,Dictionary<string,Rect> rects,string output,int level,StringBuilder report,bool compact=false)
    {
        lod.RecoveryVariants=new InfantryRecoveryVariant[4];
        for(int variant=0;variant<4;variant++)
        {
            var recovery=new InfantryRecoveryVariant();lod.RecoveryVariants[variant]=recovery;
            recovery.Knockdown=BakePoses(rig,clip,compact?13:25,rects,output,level,report,InfantryMotionKind.Knockdown,false,variant);
            recovery.GetUp=BakePoses(rig,clip,compact?13:25,rects,output,level,report,InfantryMotionKind.GetUp,false,variant);
            foreach(var poses in new[]{recovery.Knockdown,recovery.GetUp})foreach(var mesh in poses)
            {
                if(mesh.vertexCount!=lod.Idle[0].vertexCount)throw new Exception("Recovery topology differs from standing infantry");
                mesh.uv=lod.Idle[0].uv;EditorUtility.SetDirty(mesh);
            }
            CopyEndpoint(recovery.Knockdown[recovery.Knockdown.Length-1],recovery.GetUp[0]);
            CopyEndpoint(lod.Idle[0],recovery.GetUp[recovery.GetUp.Length-1]);
        }
        lod.Knockdown=lod.RecoveryVariants[0].Knockdown;lod.GetUp=lod.RecoveryVariants[0].GetUp;
        lod.DeathVariants=new InfantryDeathVariant[compact?1:10];
        for(int variant=0;variant<lod.DeathVariants.Length;variant++)
        {
            var poses=BakePoses(rig,clip,compact?12:16,rects,output,level,report,InfantryMotionKind.Death,false,variant);
            foreach(var mesh in poses){mesh.uv=lod.Idle[0].uv;EditorUtility.SetDirty(mesh);}
            lod.DeathVariants[variant]=new InfantryDeathVariant{Poses=poses};
        }
        lod.Death=lod.DeathVariants[0].Poses;
    }
    static void CopyEndpoint(Mesh source,Mesh target)
    {
        target.vertices=source.vertices;target.normals=source.normals;target.RecalculateBounds();EditorUtility.SetDirty(target);
    }
    static void SyncCommandRecovery(InfantryCrowdLod lod)
    {
        if(lod.Command==null)return;
        foreach(var role in lod.Command)
        {
            bool officer=role.OfficerKnockdown!=null&&role.OfficerKnockdown.Length>0;
                var down=officer?role.OfficerKnockdown:role.PikeKnockdown;var up=officer?role.OfficerGetUp:role.PikeGetUp;
            if(down==null||down.Length==0||up==null||up.Length==0||role.Idle==null||role.Idle.Length==0)continue;
            if(down[down.Length-1].vertexCount!=up[0].vertexCount||role.Idle[0].vertexCount!=up[up.Length-1].vertexCount)throw new Exception("Command recovery topology mismatch");
            CopyEndpoint(down[down.Length-1],up[0]);CopyEndpoint(role.Idle[0],up[up.Length-1]);
        }
    }
    public static void BakeRecovery()
    {
        prepared=true;
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        library.hideFlags=HideFlags.DontUnloadUnusedAsset;
        var report=new StringBuilder();
        for(int level=0;level<2;level++)
        {
            BuildScene(false,level==0?3:5);
            var inspection=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
            for(int side=0;side<2;side++)
            {
                var rig=inspection.Soldiers[side];
                var lod=(side==0?library.French:library.British).Lods[level];
                var rects=new Dictionary<string,Rect>();
                foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)
                    foreach(var m in r.sharedMaterials)rects[m.name]=new Rect(0,0,1,1);
                BakeGroundedLod(rig,inspection.Clips[0],lod,rects,"Assets/Resources/BakedInfantry",level,report);
            }
        }
        BuildScene(false,3);
        library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/infantry-recovery-bake.txt",report.ToString());
        InfantryPoseBlendBuild.Prepare();
        ArtilleryRecoveryVerification.Verify();
        Debug.Log("INFANTRY_RECOVERY_BAKED four matched flat fall/recovery variants and ten grounded deaths, both factions and LODs");
    }
    public static void BuildGroundedPreview(){BakeRecovery();RefreshBehaviorInspection();}
    public static void FinishGroundedPlaytests(){BakeRecovery();FinishGroundedRosterAndPlayers();}
    static void FinishGroundedRosterAndPlayers()
    {
        BakeRegimentPresentation(true);
        groundedRosterOnly=true;
        try{BakeUnitRoster();}finally{groundedRosterOnly=false;}
        GroundedInfantryVerification.Verify();
        RefreshBehaviorInspection();
        BattlePlaytestBuild.BuildAll();
    }
    public static void BuildCombatEffects()
    {
        if(new DriveInfo(Directory.GetCurrentDirectory()).AvailableFreeSpace<2L*1024*1024*1024)throw new Exception("GROUND_CONTACT_BUILD_NEEDS_DISK_SPACE: free at least 2 GB before baking.");
        FinishGroundedPlaytests();
    }
}
