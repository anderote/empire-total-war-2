using System;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class InfantryBehaviorVerification
{
    public static void Verify()
    {
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods){
            Require(lod.IdleVariants.Length==3&&lod.UnderFireVariants.Length==3&&lod.Command.Length==5,"Every faction and LOD has varied idle, reactions and command figures");
            foreach(var v in lod.IdleVariants)Check(v.Poses);
            foreach(var v in lod.UnderFireVariants)Check(v.Poses);
            foreach(var v in lod.Command){Check(v.Idle);Check(v.March);}
        }
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=32,EnableAI=false,EnableLandscape=false});
        var unit=world.Battalions[0];
        Vector2 first=FormationMath.SlotOffset(0,32,8,unit.Spacing,0);
        Require(Mathf.Abs(Vector2.Distance(first,FormationMath.SlotOffset(1,32,8,unit.Spacing,0))-.62f)<.001f,"Close order file spacing");
        Require(Mathf.Abs(Vector2.Distance(first,FormationMath.SlotOffset(8,32,8,unit.Spacing,0))-.9f)<.001f,"Rank depth retains reload room");
        var renderer=new CrowdInfantryRenderer(world,null);
        var clock=typeof(CrowdInfantryRenderer).GetField("clock",BindingFlags.Instance|BindingFlags.NonPublic);
        var method=typeof(CrowdInfantryRenderer).GetMethod("Pose",BindingFlags.Instance|BindingFlags.NonPublic);
        var data=library.French.Lods[0];int soldier=unit.SoldierStart;
        Mesh Pose(float time){clock.SetValue(renderer,time);return (Mesh)method.Invoke(renderer,new object[]{data,unit,true,soldier,0f});}
        int variant=Mathf.Min(2,(int)(RifleFireTiming.Variation(soldier,83)*3));
        Require(Array.IndexOf(data.IdleVariants[variant].Poses,Pose(1))>=0,"Idle uses a stable soldier variant");
        unit.LastIncomingFireTime=0;
        Require(Array.IndexOf(data.UnderFireVariants[variant].Poses,Pose(1))>=0,"Incoming fire selects a staggered brace");
        world.Soldiers.FireTimes[soldier]=1.2f;world.Soldiers.ReloadReadyTimes[soldier]=16;world.Soldiers.KneelTimes[soldier]=1.9f;
        Require(Array.IndexOf(data.Fire,Pose(1.1f))>=0,"Scheduled firing takes priority over bracing");
        Require(Array.IndexOf(data.IdleVariants[variant].Poses,Pose(20))>=0,"Reaction expires rather than looping permanently");
        InfantryPresentationVerification.Verify();InfantryManeuverVerification.Verify();
        Debug.Log("INFANTRY_BEHAVIOR_VERIFIED: close files, deeper ranks, varied idle, incoming-fire reaction, fire priority, both factions/LODs and command equipment.");
    }
    static void Check(Mesh[] poses){
        Require(poses!=null&&poses.Length==12,"Twelve baked frames");
        foreach(var mesh in poses){
            Require(mesh&&mesh.vertexCount==poses[0].vertexCount,"Stable pose topology");
            var targets=new System.Collections.Generic.List<Vector3>();mesh.GetUVs(2,targets);
            Require(targets.Count==mesh.vertexCount,"Interpolation targets exist");
            foreach(var v in mesh.vertices)Require(float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z),"Finite baked vertex");
        }
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception("Infantry behavior: "+message);}
}
