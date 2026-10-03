using System;
using System.Collections.Generic;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;
using UnityEditor;
public static class CavalryCombatVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Melee/cavalry: "+message);}
    public static void VerifySimulation()
    {
        var deployment=new BattleWorld(BattleConfig.CreatePlaytest());int infantry=0,cavalry=0;
        foreach(var u in deployment.Battalions){if(u.Kind==UnitKind.Cavalry){cavalry+=u.AliveCount;Require(u.SoldierCount==60,"Mounted squadrons have sixty riders");}else infantry+=u.AliveCount;}
        var mixed=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,FrenchCavalry=1,EnableAI=false,EnableLandscape=false});
        mixed.SetFormation(1,FormationKind.Column);mixed.SetFormation(1,FormationKind.Line);
        Require(mixed.Battalions[1].AliveCount==60,"Mounted capacity is independent of smaller infantry formations");
        Require(infantry==22500&&cavalry==240&&deployment.Soldiers.Alive.Length==22740,"Existing infantry plus four cavalry squadrons allocated");
        var damage=new Dictionary<string,float>();
        foreach(string scenario in new[]{"infantry","frontal","flank","skirmish","square","forming","routing"})
        {
            foreach(float dt in new[]{.025f,.05f,.1f})
            {
                var w=new BattleWorld(MeleeEncounter.Config(scenario));MeleeEncounter.Setup(w,scenario);var combat=new CombatSystem(w);bool formed=false;
                for(int tick=0;tick<Mathf.CeilToInt(16/dt);tick++)
                {
                    // Contact pressure can hold a regiment's centre behind its lead
                    // riders. Start this late-square fixture from actual approaching bodies.
                    if(scenario=="forming"&&!formed&&NearestRiderDistance(w)<12){w.SetFormation(1,FormationKind.Square);formed=true;}
                    w.StepMovement(dt);combat.Step(dt);
                }
                float wounds=0;var defender=w.Battalions[1];for(int i=defender.SoldierStart;i<defender.SoldierStart+defender.SoldierCount;i++)wounds+=100-w.Soldiers.Health[i];
                if(dt==.05f)damage[scenario]=wounds;
                if(scenario=="infantry")Require(combat.Melee.ResolvedStrikes>0&&wounds>0,"Infantry make actual timed strikes at "+dt);
                if(scenario=="square")Require(combat.Melee.RepelledCharges>0&&combat.Melee.ChargeImpacts<12,"Formed square repels main momentum at "+dt);
                if(scenario=="frontal")Require(combat.Melee.ChargeImpacts>0||combat.Melee.RepelledCharges>0,"Frontal charge makes a real impact or is repelled: "+dt);
                if(scenario=="forming")Require(formed,"Late square fixture actually starts reforming");
                if(scenario=="flank"||scenario=="skirmish"||scenario=="routing"||scenario=="forming")Require(combat.Melee.ChargeImpacts>0&&wounds>0,"Charge makes physical contacts: "+scenario+" "+dt);
                foreach(var u in w.Battalions){int alive=0;for(int i=u.SoldierStart;i<u.SoldierStart+u.SoldierCount;i++)if(w.Soldiers.Alive[i])alive++;Require(alive==u.AliveCount,"Casualty roster matches "+scenario);}
                foreach(var p in w.Soldiers.Positions)Require(!float.IsNaN(p.x)&&!float.IsNaN(p.y),"Finite contact positions");
                Debug.Log($"MELEE_CASE_VERIFIED {scenario} dt={dt} wounds={wounds:F1} strikes={combat.Melee.ResolvedStrikes} impacts={combat.Melee.ChargeImpacts} parries={combat.Melee.ParriedStrikes} phase={w.Battalions[0].ChargePhase}");
            }
        }
        Require(damage["forming"]>damage["square"]*2,"Forming square remains vulnerable");
        Require(damage["square"]<damage["flank"],"Square protects better than flank attack");
        var distant=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        foreach(var u in distant.Battalions)u.FireAtWill=false;var c=new CombatSystem(distant);
        for(int tick=0;tick<100;tick++){distant.StepMovement(.05f);c.Step(.05f);}Require(c.Melee.ResolvedStrikes==0&&distant.DeathCount==0,"No abstract distant melee damage");
        VerifyMovementParity();Debug.Log("MELEE_CAVALRY_SIMULATION_PASSED seven encounters at three timesteps, real reach, momentum, square repulsion and casualty accounting");
    }
    static float NearestRiderDistance(BattleWorld w)
    {
        var u=w.Battalions[0];Vector2 target=w.Battalions[1].Position;float best=float.PositiveInfinity;
        for(int i=u.SoldierStart;i<u.SoldierStart+u.SoldierCount;i++)if(w.Soldiers.Alive[i])
            best=Mathf.Min(best,Vector2.Distance(w.Soldiers.Positions[i],target));
        return best;
    }
    public static void VerifySquareContactFire()
    {
        var config=MeleeEncounter.Config("square");config.ReloadSeconds=2;
        var w=new BattleWorld(config);MeleeEncounter.Setup(w,"square");var square=w.Battalions[1];square.FireAtWill=true;
        // Start in physical contact: stronger approach fire can now break a charge before it arrives.
        w.Halt(0);w.SetFormation(0,FormationKind.Line);
        MeleeEncounter.Place(w,w.Battalions[0],new Vector2(0,-6.5f),0);
        var combat=new CombatSystem(w);int contactShots=0;
        for(int tick=0;tick<300;tick++)
        {
            w.StepMovement(.05f);int before=square.ShotsFired;combat.Step(.05f);
            if(square.InMelee)contactShots+=square.ShotsFired-before;
        }
        Require(contactShots>0,"Square gunners fire while their contacted bayonet wall fights");
        Debug.Log("SQUARE_CONTACT_FIRE_VERIFIED shotsDuringContact="+contactShots);
    }
    static void VerifyMovementParity()
    {
        var a=new BattleWorld(MeleeEncounter.Config("frontal"));var b=new BattleWorld(MeleeEncounter.Config("frontal"));MeleeEncounter.Setup(a,"frontal");MeleeEncounter.Setup(b,"frontal");
        using(var jobs=new SoldierMovementJobs(b))
        {
            b.MovementJobs=jobs;var ca=new CombatSystem(a);var cb=new CombatSystem(b);
            for(int tick=0;tick<300;tick++){a.StepMovement(.05f);b.StepMovement(.05f);ca.Step(.05f);cb.Step(.05f);}
            Require(a.DeathCount==b.DeathCount,"Scalar/Burst melee casualty parity");
            for(int i=0;i<a.Soldiers.Alive.Length;i++)Require(Vector2.Distance(a.Soldiers.Positions[i],b.Soldiers.Positions[i])<.03f,"Scalar/Burst mounted movement parity");
            b.MovementJobs=null;
        }
    }
    public static void VerifyScale()
    {
        var w=new BattleWorld(new BattleConfig{BattalionsPerSide=40,SoldiersPerBattalion=250,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        for(int pair=0;pair<40;pair++)
        {
            var a=w.Battalions[pair];var b=w.Battalions[pair+40];a.FireAtWill=b.FireAtWill=false;
            var center=new Vector2((pair%20-9.5f)*45,pair/20*80);
            MeleeEncounter.Place(w,a,center+Vector2.down*5,0);MeleeEncounter.Place(w,b,center+Vector2.up*5,Mathf.PI);
            w.IssueCharge(a.Id,b.Id);w.IssueCharge(b.Id,a.Id);
        }
        var combat=new CombatSystem(w);var costs=new List<float>();
        using(var jobs=new SoldierMovementJobs(w))
        {
            w.MovementJobs=jobs;
            for(int tick=0;tick<200;tick++)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();w.StepMovement(.05f);combat.Step(.05f);
                if(tick>40)costs.Add((float)timer.Elapsed.TotalMilliseconds);
            }
            w.MovementJobs=null;
        }
        costs.Sort();Require(combat.Melee.ResolvedStrikes>1000,"Full army has actual simultaneous contact fights");
        Require(w.CountAlive(ArmySide.French)+w.CountAlive(ArmySide.British)+w.DeathCount==20000,"Full scale melee conserves soldiers");
        Debug.Log($"MELEE_SCALE_VERIFIED soldiers=20000 pairs=40 strikes={combat.Melee.ResolvedStrikes} deaths={w.DeathCount} medianTickMs={costs[costs.Count/2]:F2} p95TickMs={costs[Mathf.FloorToInt(costs.Count*.95f)]:F2}");
    }
    public static void VerifyAssets()
    {
        var infantry=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");var cavalry=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
        Require(infantry&&cavalry,"Combat pose libraries exist");
        void Check(Mesh[] poses){Require(poses!=null&&poses.Length>=16,"Dense combat samples");foreach(var p in poses){Require(p&&p.vertexCount>0,"Combat geometry exists");Require(p.GetUVs3Count()==p.vertexCount,"Combat poses have next-frame GPU data");}}
        foreach(var set in new[]{infantry.French,infantry.British})foreach(var lod in set.Lods){Require(lod.Melee.Length==6,"Six infantry combat actions");foreach(var clip in lod.Melee){Check(clip.Poses);Check(clip.Kneeling);}}
        foreach(var set in new[]{cavalry.French,cavalry.British})foreach(var lod in set.Lods){Require(lod.Clips.Length==Enum.GetValues(typeof(MountedMotion)).Length,"Every mounted gait and casualty animation");foreach(var clip in lod.Clips)Check(clip.Poses);Require(lod.Clip(MountedMotion.HorseFall).Poses[0].vertexCount!=lod.Clip(MountedMotion.Idle).Poses[0].vertexCount,"Horse fall omits rider geometry");}
        Require(!ShaderUtil.ShaderHasError(Shader.Find("EmpireBattle/CrowdInfantry")),"Pose shader compiles");Debug.Log("MELEE_CAVALRY_ASSETS_PASSED standing/kneeling melee, mounted gaits/actions and separate horse/rider casualties");
    }
    public static void BakeAndBuild()
    {
        InfantryAssetBuild.BakeMeleeAndCavalry();VerifySimulation();VerifyAssets();ArtilleryRecoveryVerification.Verify();InfantryBattleBuild.BuildMatchedPlaytests();
    }
    public static void VerifyAll()
    {BattleAudioBuild.Verify();VerifySimulation();VerifyAssets();FormationTacticsVerification.VerifyAll();ArtilleryRecoveryVerification.Verify();ArtilleryAmmunitionVerification.Verify();InfantryPerformanceVerification.Verify();}
    public static void BuildFinal()
    {VerifySimulation();VerifyAssets();VerifySquareContactFire();VerifyScale();FormationTacticsVerification.Verify();InfantryPerformanceVerification.VerifyMovement();ArtilleryRecoveryVerification.Verify();BattlePlaytestBuild.BuildAll();}
    public static void BuildAll()
    {VerifyAll();BattlePlaytestBuild.BuildAll();}
}
static class CombatMeshVerification
{
    public static int GetUVs3Count(this Mesh mesh){var list=new List<Vector3>();mesh.GetUVs(2,list);return list.Count;}
}
