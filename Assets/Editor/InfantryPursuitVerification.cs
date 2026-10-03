using System;
using System.Collections.Generic;
using EmpireBattle;
using UnityEngine;

public static class InfantryPursuitVerification
{
    static void Require(bool value,string message)
    {if(!value)throw new Exception("Infantry pursuit: "+message);}

    static BattleWorld Fixture(int survivors=4)
    {
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=32,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        MeleeEncounter.Place(world,world.Battalions[0],Vector2.zero,0);
        MeleeEncounter.Place(world,world.Battalions[1],new Vector2(0,18),0);
        foreach(var unit in world.Battalions)unit.FireAtWill=false;
        for(int i=32+survivors;i<64;i++)world.KillSoldier(i,DeathCause.Musket,Vector3.zero,Vector3.zero);
        world.Battalions[1].Routing=true;world.Withdraw(1);world.IssueCharge(0,1);
        return world;
    }

    static int CheckAssignments(BattleWorld world)
    {
        var targets=new HashSet<int>();int hunters=0;
        for(int i=0;i<32;i++)
        {
            int target=world.ContactMovement.PursuitTargets[i];if(target<0)continue;
            Require(world.Soldiers.Alive[target]&&target>=32,"Assignments only use living fugitives");
            Require(targets.Add(target),"At most one hunter per fugitive even when heavily outnumbered");
            hunters++;
        }
        return hunters;
    }

    public static void Verify()
    {
        VerifyScarcity();VerifyInterception();VerifyFatigue();VerifyOrders();
        foreach(float dt in new[]{.025f,.05f,.1f})VerifyGathering(dt);
        Debug.Log("INFANTRY_PURSUIT_RESERVES_VERIFIED capped targets, closer interception, walking recovery, loose gathering and order overrides");
    }

    static void VerifyScarcity()
    {
        var world=Fixture();world.ContactMovement.Prepare();
        Require(CheckAssignments(world)==4,"Four survivors need four hunters");
        var assignments=(int[])world.ContactMovement.PursuitTargets.Clone();
        world.ContactMovement.Prepare();
        for(int i=0;i<32;i++)Require(assignments[i]==world.ContactMovement.PursuitTargets[i],"Assignments remain stable");
        int spare=Array.FindIndex(world.ContactMovement.PursuitReserves,x=>x);
        Require(spare>=0,"Surplus infantry gather");
        Vector2 before=world.ContactMovement.Targets[spare];
        // Move a claimed fugitive close to a spare. Local movement must not bypass
        // the reservation and start a second chase in between assignment updates.
        world.Soldiers.Positions[32]=world.Soldiers.Positions[spare]+Vector2.up*3;
        world.ContactMovement.Prepare();
        Require(Vector2.Distance(before,world.ContactMovement.Targets[spare])<.0001f,"Unassigned men do not chase local claimed fugitives");
        int hunter=Array.FindIndex(assignments,x=>x>=0),victim=assignments[hunter];
        world.KillSoldier(hunter,DeathCause.Musket,Vector3.zero,Vector3.zero);
        world.ContactMovement.Prepare();
        Require(CheckAssignments(world)==4,"A fallen hunter is replaced immediately");
        Require(Array.IndexOf(world.ContactMovement.PursuitTargets,victim)>=0,"His fugitive is not left unclaimed");
        world.KillSoldier(victim,DeathCause.Musket,Vector3.zero,Vector3.zero);
        world.ContactMovement.Prepare();Require(CheckAssignments(world)==3,"Dead fugitives release excess hunters");
    }

    static void VerifyInterception()
    {
        var world=Fixture(1);world.ContactMovement.Prepare();
        int original=Array.IndexOf(world.ContactMovement.PursuitTargets,32);
        int closer=Array.FindIndex(world.ContactMovement.PursuitReserves,x=>x);
        world.Soldiers.Positions[original]=world.Soldiers.Positions[32]-Vector2.up*35;
        world.Soldiers.Positions[closer]=world.Soldiers.Positions[32]-Vector2.up*2;
        world.StepMovement(.45f);
        Require(world.ContactMovement.PursuitTargets[closer]==32,"A substantially closer spare takes over");
        Require(CheckAssignments(world)==1&&world.ContactMovement.PursuitReserves[original],"Replaced hunter joins reserves");
        // Separation of logical centres must not force real close hunters to reform.
        world.Battalions[1].Position=new Vector2(0,250);world.ContactMovement.Prepare();
        Require(world.ContactMovement.Pursuing[0]&&CheckAssignments(world)==1,"Dispersed pursuit survives centre separation");
    }

    static void VerifyFatigue()
    {
        var world=Fixture(4);world.ContactMovement.Prepare();var unit=world.Battalions[0];
        unit.Fatigue=50;
        for(int i=0;i<32;i++)world.Soldiers.MovementSpeeds[i]=world.ContactMovement.PursuitReserves[i]?.5f:4f;
        float strain=world.FatigueRate(unit);
        float expected=(4*.45f*strain-28*.12f/strain)/32;
        Require(Mathf.Abs(world.ContactMovement.PursuitFatigueRate(unit)-expected)<.0001f,"Walking recovery is weighted against active hunters");
        Require(expected<0,"A few hunters do not prevent the walking majority recovering");
        int reserve=Array.FindIndex(world.ContactMovement.PursuitReserves,x=>x);
        world.Soldiers.MeleeTargets[reserve]=32;world.Soldiers.MeleeEnds[reserve]=world.Time+1;
        Require(world.ContactMovement.PursuitFatigueRate(unit)>expected,"A fighting reserve adds fatigue instead of recovering");
        var busy=Fixture(32);busy.ContactMovement.Prepare();
        Require(busy.ContactMovement.PursuitFatigueRate(busy.Battalions[0])>0,"Full pursuit still costs stamina");
    }

    static void Finish(BattleWorld world)
    {
        world.ContactMovement.Prepare();
        for(int i=32;i<64;i++)if(world.Soldiers.Alive[i])world.KillSoldier(i,DeathCause.Musket,Vector3.zero,Vector3.zero);
        world.StepMovement(.05f);
        Require(world.ContactMovement.Gathering[0]&&world.ContactMovement.Loose[0],"Automatic end of chase keeps infantry loose");
        Require(world.Battalions[0].Formation==FormationKind.Line,"Gathering preserves the player's drill");
    }

    static void VerifyOrders()
    {
        for(int order=0;order<5;order++)
        {
            var world=Fixture();Finish(world);
            if(order==0)world.Halt(0);
            if(order==1)world.IssueMove(0,new Vector2(15,0),0);
            if(order==2)world.SetFormation(0,FormationKind.Line);
            if(order==3)world.Withdraw(0);
            if(order==4)world.SetFrontage(0,6);
            world.ContactMovement.Prepare();
            Require(!world.ContactMovement.Gathering[0],"Explicit orders cancel automatic gathering: "+order);
            if(order!=3)Require(!world.ContactMovement.Loose[0],"Explicit orders return control to formation movement: "+order);
        }
    }

    static void VerifyGathering(float dt)
    {
        var world=Fixture();var parallel=Fixture();Finish(world);Finish(parallel);
        world.Battalions[0].Fatigue=parallel.Battalions[0].Fatigue=50;
        var combat=new CombatSystem(world);var otherCombat=new CombatSystem(parallel);
        using(var jobs=new SoldierMovementJobs(parallel))
        {
            parallel.MovementJobs=jobs;
            for(int tick=0;tick<Mathf.CeilToInt(12/dt);tick++)
            {
                world.StepMovement(dt);parallel.StepMovement(dt);combat.Step(dt);otherCombat.Step(dt);
                for(int i=0;i<32;i++)
                {
                    Require(Vector2.Distance(world.Soldiers.Positions[i],parallel.Soldiers.Positions[i])<.035f,"Gathering scalar/Burst parity");
                    Require(world.Soldiers.MovementSpeeds[i]<world.Config.MarchSpeed*1.15f,"Reserves walk instead of charging");
                }
                Require(Vector2.Distance(world.Battalions[0].Position,parallel.Battalions[0].Position)<.035f,"Regiment markers follow actual gathering soldiers");
            }
            parallel.MovementJobs=null;
        }
        Require(world.Battalions[0].Fatigue<49,"Walking/resting reserves regain stamina through actual combat condition updates");
        Require(Mathf.Abs(world.Battalions[0].Fatigue-parallel.Battalions[0].Fatigue)<.01f,"Fatigue scalar/Burst parity");
        Require(world.ContactMovement.Gathering[0],"Loose gathering persists until ordered otherwise");
        int resting=0;float closest=float.PositiveInfinity;
        for(int i=0;i<32;i++)
        {
            if(world.Soldiers.MovementSpeeds[i]<.1f)resting++;
            for(int j=i+1;j<32;j++)closest=Mathf.Min(closest,Vector2.Distance(world.Soldiers.Positions[i],world.Soldiers.Positions[j]));
        }
        Require(resting>24&&closest>1.2f,"Most men settle with open spacing instead of endlessly reforming");
        Debug.Log($"PURSUIT_GATHERING_VERIFIED dt={dt} resting={resting} closest={closest:F2} fatigue={world.Battalions[0].Fatigue:F2}");
    }
}
