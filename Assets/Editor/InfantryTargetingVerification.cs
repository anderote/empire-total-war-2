using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class InfantryTargetingVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Infantry targeting: "+message);}
    static BattleWorld Fixture(InfantryFireMode mode,FormationKind formation)
    {
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=64,InitialSeparation=70,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        var attacker=world.Battalions[0];attacker.Formation=formation;attacker.Files=64;attacker.FireMode=mode;
        MeleeEncounter.Place(world,attacker,Vector2.zero,0);attacker.FormationReadyTime=0;
        var target=world.Battalions[1];target.Files=64;target.FireAtWill=false;
        MeleeEncounter.Place(world,target,new Vector2(10,70),Mathf.PI);
        // Leave an empty formation centre: every aim must still resolve to a living member.
        for(int i=28;i<36;i++)world.KillSoldier(target.SoldierStart+i,DeathCause.Musket,Vector3.zero,Vector3.zero);
        return world;
    }
    public static void Verify()
    {
        foreach(bool jobs in new[]{false,true})foreach(var mode in new[]{InfantryFireMode.Volley,InfantryFireMode.ByPlatoon,InfantryFireMode.FireAtWill})
            VerifyFire(mode,FormationKind.Line,jobs);
        foreach(var formation in new[]{FormationKind.Skirmish,FormationKind.Square})VerifyFire(InfantryFireMode.FireAtWill,formation,false);
        VerifySelectionAndOffsets();
        InfantryFireVerification.VerifyDrills();
        MusketContactVerification.Verify();
        Debug.Log("INFANTRY_TARGETING_VERIFIED individual live targets, shared aim/muzzle/bullet endpoints, target distribution, gaps/deaths, all firing modes, square/skirmish, both movement paths and drill/contact regressions");
    }
    static void VerifyFire(InfantryFireMode mode,FormationKind formation,bool jobs)
    {
        var world=Fixture(mode,formation);var attacker=world.Battalions[0];var target=world.Battalions[1];
        using var movement=jobs?new SoldierMovementJobs(world):null;world.MovementJobs=movement;
        var combat=new CombatSystem(world);combat.Step(.05f);
        var selected=new HashSet<int>();var shooters=new List<int>();int offCentre=0;
        for(int id=attacker.SoldierStart;id<attacker.SoldierStart+attacker.SoldierCount;id++)
        {
            if(world.Soldiers.FireTimes[id]<0)continue;
            int victim=world.Soldiers.FireTargetSoldiers[id];
            Require(victim>=target.SoldierStart&&victim<target.SoldierStart+target.SoldierCount&&world.Soldiers.Alive[victim],"Every shooter chooses a living member of its target unit");
            Vector3 aim=world.Soldiers.FireAimPoints[id];
            Require((aim-InfantryTargeting.AimPoint(world,victim)).sqrMagnitude<.00001f,"Aim point is the selected individual's body");
            Vector3 delta=aim-world.Soldiers.FireOrigins[id];delta.y=0;
            Require(Vector3.Dot(delta.normalized,world.Soldiers.FireDirections[id])>.99999f,"Muzzle and smoke face the same individual");
            Vector2 toCentre=(target.Position-world.Soldiers.Positions[id]).normalized;
            if(Vector2.Dot(toCentre,new Vector2(world.Soldiers.FireDirections[id].x,world.Soldiers.FireDirections[id].z))<.999999f)offCentre++;
            selected.Add(victim);shooters.Add(id);
        }
        Require(shooters.Count>1&&selected.Count>1&&offCentre>0,$"Fire spreads over actual soldiers: {mode}/{formation} shooters={shooters.Count} targets={selected.Count}");
        var bullets=(IList)typeof(CombatSystem).GetField("bullets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(combat);
        int aimed=0,misses=0;
        foreach(var bullet in bullets)
        {
            var type=bullet.GetType();int shooter=(int)type.GetField("Shooter").GetValue(bullet);
            var origin=(Vector3)type.GetField("Origin").GetValue(bullet);var velocity=(Vector3)type.GetField("Velocity").GetValue(bullet);
            float duration=(float)type.GetField("Duration").GetValue(bullet);
            Vector3 end=WeaponBallistics.PositionAt(origin,velocity,duration);
            Require(origin==world.Soldiers.FireOrigins[shooter],"Ballistic shot starts at the individually aimed muzzle");
            if((end-world.Soldiers.FireAimPoints[shooter]).sqrMagnitude<.001f)aimed++;else misses++;
        }
        Require(bullets.Count==shooters.Count,"Every scheduled soldier emits one aimed or dispersed bullet");
        if(mode==InfantryFireMode.Volley)Require(aimed>0,"Accurate bullets use the selected aim point");
        for(int step=0;step<10;step++)world.StepMovement(.025f);
        foreach(int shooter in shooters)
        {
            float expected=FormationMath.FacingOf(new Vector2(world.Soldiers.FireDirections[shooter].x,world.Soldiers.FireDirections[shooter].z));
            Require(Mathf.Abs(FormationMath.AngleDifference(world.Soldiers.Facings[shooter],expected))<.025f,"Soldier turns toward its target during aim/recoil in "+(jobs?"jobs":"managed")+" movement");
        }
        Debug.Log($"INDIVIDUAL_AIM mode={mode} formation={formation} jobs={jobs} shooters={shooters.Count} distinctTargets={selected.Count} aimedBullets={aimed} dispersedBullets={misses}");
    }
    static void VerifySelectionAndOffsets()
    {
        var world=Fixture(InfantryFireMode.Volley,FormationKind.Line);var attacker=world.Battalions[0];var target=world.Battalions[1];
        int shooter=world.SoldierAtSlot(attacker,10),first=InfantryTargeting.Select(world,attacker,shooter,target);
        Require(first>=0,"Living target selected");world.KillSoldier(first,DeathCause.Musket,Vector3.zero,Vector3.zero);
        int second=InfantryTargeting.Select(world,attacker,shooter,target);
        Require(second>=0&&second!=first,"Next aim replaces a dead target");
        Require(InfantryTargeting.Select(world,attacker,shooter,attacker)<0,"Cannot target friendly soldiers");
        for(int i=target.SoldierStart;i<target.SoldierStart+target.SoldierCount;i++)world.Soldiers.Positions[i]=new Vector2(0,-70);
        int rounds=world.Soldiers.RoundsRemaining[shooter];
        Require(RifleFireTiming.ScheduleNextGroup(world,attacker,new int[64],null,target)==0,"Members outside firing arc do not aim at an empty nominal centre");
        Require(world.Soldiers.RoundsRemaining[shooter]==rounds,"No target does not consume ammunition");
        foreach(bool independent in new[]{false,true})
        {
            world=Fixture(independent?InfantryFireMode.FireAtWill:InfantryFireMode.Volley,FormationKind.Line);attacker=world.Battalions[0];target=world.Battalions[1];
            var appearance=new InfantryCrowdSet{MuzzleOffset=new Vector3(-.3f,1.4f,1.1f),LockOffset=new Vector3(-.2f,1.4f,.3f)};
            shooter=world.SoldierAtSlot(attacker,10);int victim=InfantryTargeting.Select(world,attacker,shooter,target);
            if(independent)Require(RifleFireTiming.ScheduleIndependent(world,attacker,shooter,world.Soldiers.Positions[victim],appearance,victim),"Independent shot schedules");
            else RifleFireTiming.ScheduleNextGroup(world,attacker,new int[64],appearance,target);
            var delta=world.Soldiers.FireAimPoints[shooter]-world.Soldiers.FireOrigins[shooter];delta.y=0;
            Require(Vector3.Dot(delta.normalized,world.Soldiers.FireDirections[shooter])>.99999f,"Off-centre weapon geometry still points at its target");
        }
    }
}
