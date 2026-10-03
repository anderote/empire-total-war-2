using System;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class InfantryPresentationVerification
{
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=8,EnableLandscape=false,EnableAI=false});
        var unit=world.Battalions[0];unit.Formation=FormationKind.Line;unit.FireCycleActive=true;unit.Moving=false;unit.FireAtWill=true;
        int soldier=unit.SoldierStart;
        while(world.Soldiers.PikeSergeants[soldier]||world.Soldiers.Captains[soldier])soldier++;
        world.Soldiers.MovementSpeeds[soldier]=0;world.Soldiers.FireTimes[soldier]=0;
        world.Soldiers.KneelTimes[soldier]=.7f;world.Soldiers.ReloadReadyTimes[soldier]=15;world.Soldiers.ReloadDurations[soldier]=15;
        var view=new CrowdInfantryRenderer(world,null);
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");var lod=library.French.Lods[0];
        var clock=typeof(CrowdInfantryRenderer).GetField("clock",BindingFlags.Instance|BindingFlags.NonPublic);
        var method=typeof(CrowdInfantryRenderer).GetMethod("Pose",BindingFlags.Instance|BindingFlags.NonPublic);
        Mesh Pose(float time,out float blend)
        {
            clock.SetValue(view,time);object[] args={lod,unit,true,soldier,0f};var mesh=(Mesh)method.Invoke(view,args);blend=(float)args[4];return mesh;
        }
        for(int frame=0;frame<=70;frame++)Pose(frame*.01f,out _);
        var mid=Pose(.86f,out float amount);
        Require(mid==lod.LowerToKneel&&amount>.4f&&amount<.65f,"Lowering uses an intermediate posture");
        Require(Pose(.86f,out float paused)==mid&&Mathf.Abs(amount-paused)<.00001f,"Paused presentation does not advance posture");
        Pose(1.03f,out _);
        Require(Pose(7f,out _)==lod.KneelingReload[lod.KneelingReload.Length-1],"Reload action ends before the combat cooldown");
        Pose(15f,out _);
        world.Soldiers.FireTimes[soldier]=15.5f;world.Soldiers.KneelTimes[soldier]=16.2f;
        Require(Pose(15.19f,out float raised)==lod.RiseToAim&&raised>.4f&&raised<.6f,"Rising has a midpoint before the shot");
        Require(Pose(15.4f,out _)!=lod.RiseToAim,"Rise finishes before discharge");
        unit.Running=true;unit.Moving=true;world.Soldiers.MovementSpeeds[soldier]=2.04f;
        var run=Pose(15.6f,out _);int variant=Mathf.Min(2,(int)(RifleFireTiming.Variation(soldier,71)*3));
        Require(Array.IndexOf(lod.Locomotion[variant].Run,run)>=0,"Normal running speed selects the run cycle");
        unit.Running=unit.Moving=false;world.Soldiers.MovementSpeeds[soldier]=0;
        unit.Formation=FormationKind.Skirmish;world.Soldiers.FireTimes[soldier]=-100;world.Soldiers.ReloadReadyTimes[soldier]=0;
        Pose(20,out _);
        Require(lod.KneelingFire!=null&&lod.KneelingFire.Length==lod.Fire.Length,"Dedicated kneeling aim/fire assets exist");
        Require(Array.IndexOf(lod.KneelingFire,Pose(21,out _))>=0,"Idle skirmishers default to a kneeling ready pose");
        world.Soldiers.FireVolleys[soldier]=21;
        world.Soldiers.FireTimes[soldier]=23;
        world.Soldiers.ReloadReadyTimes[soldier]=38;
        Pose(21.1f,out _);Pose(21.5f,out _);
        var aim=Pose(22,out _);
        var expected=RifleFireTiming.SkirmishFiringStance(soldier)==InfantryFiringStance.Kneeling?lod.KneelingFire:lod.Fire;
        Require(Array.IndexOf(expected,aim)>=0,"Skirmisher uses his individual firing stance during aim");
        Pose(23.6f,out _);Pose(24f,out _);
        Require(Array.IndexOf(lod.KneelingReload,Pose(24.5f,out _))>=0,"Skirmisher returns to one knee for his reload");
        world.Soldiers.FireTimes[soldier]=-100;
        unit.Formation=FormationKind.Square;unit.Reforming=false;
        Require(Array.IndexOf(lod.KneelingFire,Pose(25,out _))>=0,"Single-rank square retains kneeling fire");
        // A full square reserves its outer perimeter for the bayonet wall.
        world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=120,EnableLandscape=false,EnableAI=false});
        unit=world.Battalions[0];unit.Formation=FormationKind.Square;unit.Reforming=unit.Moving=false;
        world.SetBayonets(unit.Id,true);
        soldier=world.SoldierAtSlot(unit,0);
        while(world.Soldiers.PikeSergeants[soldier]||world.Soldiers.Captains[soldier])soldier++;
        view=new CrowdInfantryRenderer(world,null);
        bool ready=false;var squarePose=Pose(25,out _);
        foreach(var clip in lod.Melee)if(clip.Action==MeleeAction.Ready)ready=Array.IndexOf(clip.Kneeling,squarePose)>=0;
        Require(ready,"Square front rank continuously holds a kneeling bayonet brace");
        Debug.Log("INFANTRY_PRESENTATION_VERIFIED: continuous lowering/rising, pause, five-second action and reachable run cycle.");
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception("Infantry presentation: "+message);}
    public static void VerifyAndBuild(){Verify();InfantryBattleBuild.BuildMac();}
}
