using System;
using System.Collections.Generic;
using EmpireBattle;
using UnityEngine;

// Background-only regression coverage: no player launch, build or publication.
public static class InfantryResponsivenessVerification
{
    static void Require(bool value,string message)
    {if(!value)throw new Exception("Infantry responsiveness: "+message);}
    static BattleWorld World(int count=250)=>new BattleWorld(new BattleConfig {
        BattalionsPerSide=1,SoldiersPerBattalion=count,InitialSeparation=100,
        FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false });
    static void Step(BattleWorld world,float seconds)
    {for(int i=0;i<Mathf.CeilToInt(seconds/.05f);i++)world.StepMovement(.05f);}

    public static void Verify()
    {
        var failures=new List<string>();
        foreach(Action check in new Action[] { MoveWhileForming,LoadedMenRetarget,RunSpeed,JobParity,
            InfantryFireVerification.VerifyDrills,InfantryManeuverVerification.VerifyMovementOrders,
            InfantryOrderEdgeVerification.Verify,InfantryFireModeVerification.Verify })
        {
            try {check();Debug.Log("INFANTRY_RESPONSE_PASSED "+check.Method.Name);}
            catch(Exception e){failures.Add(check.Method.Name+": "+e.Message);Debug.LogError("INFANTRY_RESPONSE_FAILED "+failures[failures.Count-1]);}
        }
        if(failures.Count>0)throw new Exception(string.Join("\n",failures));
        Debug.Log("INFANTRY_RESPONSIVENESS_VERIFIED");
    }

    static void MoveWhileForming()
    {
        foreach(bool running in new[]{false,true})
        {
            var w=World();var u=w.Battalions[0];u.Running=running;
            Vector2 start=u.Position,goal=start+new Vector2(30,0);
            w.IssueMarch(u.Id,goal);Step(w,.1f);
            Require(u.Position.x>start.x+.04f,"Quarter-turn march starts immediately");
            w.SetFormation(u.Id,FormationKind.Column);
            Vector2 before=u.Position;Step(w,.5f);
            Require(u.Position.x>before.x+.2f&&u.Reforming,"Regiment advances while changing into column");
            Step(w,80);
            Require(Vector2.Distance(u.Position,goal)<.2f&&!u.Reforming,"Moving column reaches and settles at its destination");
            goal=u.Position-new Vector2(20,0);start=u.Position;
            w.IssueMarch(u.Id,goal);Step(w,.1f);
            Require(u.Position.x<start.x-.04f,"Reversed march starts without a formation-wide wait");
            Step(w,60);
            Require(Vector2.Distance(u.Position,goal)<.2f&&!u.Reforming,"Reversed column settles without leaving stragglers");
        }
    }

    static void LoadedMenRetarget()
    {
        var w=World();var u=w.Battalions[0];var enemy=w.Battalions[1];
        var shooters=new int[u.SoldierCount];
        Require(RifleFireTiming.ScheduleNextGroup(w,u,shooters)>0,"Initial platoon fires");
        int spent=shooters[0];float reload=w.Soldiers.ReloadReadyTimes[spent];
        var rounds=(int[])w.Soldiers.RoundsRemaining.Clone();
        MeleeEncounter.Place(w,enemy,u.Position+new Vector2(25,95),Mathf.PI);
        Require(w.IssueRangedAttack(u.Id,enemy.Id),"Retarget order is accepted");
        Require(u.NextFireTime<=w.Time,"Target change releases the old drill wait");
        int fired=0;
        for(int tick=0;tick<40&&fired==0;tick++)
        {w.StepMovement(.05f);fired=RifleFireTiming.ScheduleNextGroup(w,u,shooters);}
        Require(fired>0&&w.Time<2f,"Loaded platoon engages the new direction within two seconds");
        Require(w.Soldiers.ReloadReadyTimes[spent]==reload&&w.Soldiers.RoundsRemaining[spent]==rounds[spent],"Retargeting preserves the spent cartridge and ongoing reload");
        for(int i=0;i<fired;i++)Require(shooters[i]!=spent,"Reloading soldier cannot fire again early");
        Debug.Log($"INFANTRY_RETARGET seconds={w.Time:F2} loadedShooters={fired} spentMusketReady={reload:F2}");
    }

    static void RunSpeed()
    {
        var walk=World(64);var run=World(64);var a=walk.Battalions[0];var b=run.Battalions[0];
        Vector2 start=a.Position;b.Running=true;
        walk.IssueMarch(0,start+Vector2.up*50);run.IssueMarch(0,start+Vector2.up*50);
        Step(walk,3);Step(run,3);
        float ratio=Vector2.Distance(b.Position,start)/Vector2.Distance(a.Position,start);
        Require(ratio>1.9f&&ratio<2f,"Run speed is about 15% faster than the former 1.7x march pace");
        Debug.Log($"INFANTRY_RUN marchRatio={ratio:F2}");
    }

    static void JobParity()
    {
        var scalar=World(100);var parallel=World(100);
        using(var jobs=new SoldierMovementJobs(parallel))
        {
            parallel.MovementJobs=jobs;
            for(int tick=0;tick<600;tick++)
            {
                foreach(var w in new[]{scalar,parallel})
                {
                    var u=w.Battalions[0];
                    if(tick==0){u.Running=true;w.IssueMarch(0,u.Position+new Vector2(20,30));}
                    if(tick==40)w.SetFormation(0,FormationKind.Column);
                    if(tick==160)w.IssueMarch(0,u.Position-new Vector2(15,20));
                    if(tick==300)w.Halt(0);
                    w.StepMovement(.05f);
                }
                for(int i=0;i<scalar.Soldiers.Alive.Length;i++)
                {
                    Require(Vector2.Distance(scalar.Soldiers.Positions[i],parallel.Soldiers.Positions[i])<.001f,"Scalar and job soldier movement agree");
                    Require(Mathf.Abs(FormationMath.AngleDifference(scalar.Soldiers.Facings[i],parallel.Soldiers.Facings[i]))<.001f,"Scalar and job turn rates agree");
                    Require(scalar.Soldiers.MovementSpeeds[i]<scalar.Config.MarchSpeed*1.95f*1.2f*1.15f*1.061f+.001f,"Reforming never teleports a running soldier");
                }
            }
        }
    }
}
