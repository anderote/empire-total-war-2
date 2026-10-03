using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EmpireBattle;
using EmpireGame;
using UnityEngine;

public static class ArtilleryMovementVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Artillery movement: "+message);}
    static BattleWorld Make(out ArtillerySystem artillery,bool deployment=false,bool terrain=false)
    {
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=8,
            EnableAI=false,EnableLandscape=terrain,FrenchArtillery=4,BritishArtillery=1,RequireDeployment=deployment});
        artillery=new ArtillerySystem(world);
        foreach(var gun in artillery.Guns){gun.FireAtWill=false;gun.Position=new Vector2(gun.Id*12,-100);gun.Facing=0;}
        foreach(var unit in world.Battalions)unit.FireAtWill=false;
        return world;
    }
    public static void Verify()
    {
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var world=Make(out var artillery);var gun=artillery.Guns[0];
            gun.Phase=ArtilleryPhase.Loading;gun.PhaseElapsed=.5f;
            Vector2 start=gun.Position,destination=start+Vector2.up*14;
            Require(artillery.OrderMove(gun.Id,destination,0),"Movement order accepted");
            Require(gun.Position==start&&gun.Moving,"Battle move is not a teleport");
            artillery.Step(1);Require(gun.Position==start,"Crew prepares carriage before travel");
            float pausedTime=gun.PhaseElapsed;int ammo=gun.Ammunition;
            int steps=0;
            while(gun.Moving&&steps++<2000)
            {
                world.StepMovement(dt);artillery.Step(dt);
                Require(gun.Phase==ArtilleryPhase.Loading&&gun.PhaseElapsed==pausedTime&&gun.Ammunition==ammo,
                    "Moving cannot fire or advance reload");
                Require(!world.Terrain.IsBlocked(gun.Position,2.5f),"Gun stays on navigable ground");
            }
            Require(!gun.Moving&&gun.SetupRemaining>0&&Vector2.Distance(gun.Position,destination)<.001f,"Arrival sets up at exact destination");
            var crew=artillery.CrewPosition(gun,0);
            Require(Vector2.Distance(new Vector2(crew.x,crew.z),gun.Position)<4,"Crew travels with the carriage");
            artillery.Step(1);Require(gun.PhaseElapsed==pausedTime,"Setup cannot reload for free");
            artillery.Step(3);Require(gun.PhaseElapsed!=pausedTime||gun.Phase!=ArtilleryPhase.Loading,"Service resumes after setup");
            Require(artillery.OrderMove(gun.Id,destination+Vector2.right*30,0),"Second move accepted");
            artillery.Step(4);Vector2 halt=gun.Position;artillery.ClearTarget(gun.Id,false);artillery.Step(10);
            Require(gun.Position==halt&&!gun.Moving&&!gun.FireAtWill,"Halt stops movement and holds fire");
            Require(!artillery.OrderMove(gun.Id,new Vector2(float.NaN,0),0)&&!artillery.OrderMove(gun.Id,new Vector2(100000,0),0),"Invalid and out-of-field destinations rejected");
            gun.Health=0;Require(!artillery.OrderMove(gun.Id,start,0),"Destroyed gun cannot move");
        }
        {
            Make(out var artillery);var healthy=artillery.Guns[0];var depleted=artillery.Guns[1];
            depleted.Crew[0].Health=depleted.Crew[1].Health=depleted.Crew[2].Health=0;
            Vector2 a=healthy.Position,b=depleted.Position;
            artillery.OrderMove(healthy.Id,a+Vector2.up*100,0);artillery.OrderMove(depleted.Id,b+Vector2.up*100,0);
            artillery.Step(5);
            Require(healthy.Position.y-a.y>depleted.Position.y-b.y,"Missing crew slows hauling");
            depleted.Crew[3].Health=0;artillery.Step(1);Require(!depleted.Moving,"Loss of last crew stops the carriage");
            Require(!artillery.OrderMove(depleted.Id,b,0),"Uncrewed carriage cannot move");
            healthy.LimeExposureRemaining=30;float before=healthy.Position.y;artillery.Step(1);
            Require(healthy.Position.y>before,"Surviving crew can haul a gun out of lime");
        }
        {
            var world=Make(out var artillery,true);var gun=artillery.Guns[0];var area=world.DeploymentArea(gun.Side);
            Vector2 point=new Vector2(area.center.x,area.center.z);
            Require(artillery.OrderMove(gun.Id,point,1)&&gun.Position==point&&!gun.Moving,"Deployment places a gun inside its zone");
            Require(!artillery.OrderMove(gun.Id,Vector2.zero,0),"Cannot deploy across the neutral zone");
            artillery.Step(10);Require(world.Time==0&&gun.Position==point,"Deployment does not advance simulation");
        }
        VerifyObstacle();VerifyCommands();VerifyReplenishment();
        CounterBatteryVerification.Verify();RegimentalGloryVerification.VerifyCombinedRecruitment();
        Directory.CreateDirectory("Artifacts/ArtilleryMovement");
        const string report="PASS: crew-hauled moves at three timesteps, preparation/setup, no mobile firing or free reload, halt, crew losses, lime escape, deployment zone, field/obstacle checks and detours; selected battery move/attack/bombard commands and spacing; partial/full replenishment, cost, retained XP, save and next deployment; counter-battery and recruitment regressions. Offline only; no player or campaign launched.";
        File.WriteAllText("Artifacts/ArtilleryMovement/verification.txt",report);Debug.Log("ARTILLERY_MOVEMENT_VERIFIED "+report);
    }
    static void VerifyObstacle()
    {
        var world=Make(out var artillery,false,true);var gun=artillery.Guns[0];
        Require(world.Terrain.FarmCenters.Length>0,"Terrain fixture has a farm");
        var center=world.Terrain.FarmCenters[0];var half=world.Terrain.FarmHalfExtents[0];
        gun.Position=center-Vector2.up*(half.y+12);var destination=center+Vector2.up*(half.y+12);
        Require(!artillery.OrderMove(gun.Id,center,0),"Building footprint rejects gun destination");
        Require(artillery.OrderMove(gun.Id,destination,0),"Finds a clear detour around farm");
        for(int i=0;i<6000&&gun.Moving;i++)
        {artillery.Step(.05f);Require(!world.Terrain.IsBlocked(gun.Position,2.5f),"Detour never crosses building");}
        Require(!gun.Moving&&Vector2.Distance(gun.Position,destination)<.001f,"Detour reaches destination");
    }
    static void VerifyCommands()
    {
        var world=Make(out var artillery);var root=new GameObject("Offline artillery orders");
        try
        {
            var view=root.AddComponent<BattleView>();
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(BattleView).GetField("world",hidden).SetValue(view,world);
            typeof(BattleView).GetField("selected",hidden).SetValue(view,new bool[world.Battalions.Length]);
            view.InitializeArtilleryCommands(artillery);view.SelectGun(0,false);
            var destination=new Vector2(18,-50);view.OrderSelectedGuns(-1,destination);
            Require(artillery.Guns.Take(4).All(g=>g.Moving)&&!artillery.Guns[4].Moving,"Right-click moves selected battery only");
            Require(Vector2.Distance(artillery.Guns[1].Destination,artillery.Guns[0].Destination)==12,"Movement preserves gun spacing");
            view.OrderHalt();Require(artillery.Guns.All(g=>!g.Moving),"Shared halt command stops artillery");
            view.OrderSelectedGuns(-1,destination,true);
            Require(artillery.Guns.Take(4).All(g=>g.HasGroundTarget&&!g.Moving),"Explicit bombardment retains ground fire");
            view.OrderSelectedGuns(-1,artillery.Guns[4].Position);
            Require(artillery.Guns.Take(4).All(g=>g.TargetBattery==artillery.Guns[4].UnitId&&!g.Moving),"Clicking enemy artillery still attacks individual cannons");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    static void VerifyReplenishment()
    {
        var company=new GloryCompany();company.Save.commanderXP=4000;company.Save.coins=10000;
        Require(company.Recruit(10),"Recruit battery fixture");var battery=company.Save.regiments.Last();
        battery.progression.Award(850);int xp=battery.progression.xp;var kit=battery.loadout;
        battery.soldiers=0;int price=GloryCompany.ReplacementCosts[battery.tier];company.Save.coins=price*2+price/2;
        Require(company.AffordableReplacements(battery)==2&&company.Replenish(battery),"Can buy two guns without funding the whole battery");
        Require(battery.soldiers==2&&company.Save.coins==price/2&&battery.progression.xp==xp&&battery.loadout==kit,"Exact replacement cost retains XP and kit");
        Require(GloryCompany.Valid(company.Save),"Partially replenished battery is saveable");
        var config=company.Deployment(company.Choices()[0]);var guns=new ArtillerySystem(new BattleWorld(config));
        Require(guns.Guns.Count(g=>g.Side==ArmySide.French)==2&&guns.Guns.Where(g=>g.Side==ArmySide.French).All(g=>g.Health==180&&g.LivingCrew==4),"Replacements deploy as crewed working guns");
        company.Save.inBattle=true;company.Save.coins=price*2;Require(!company.Replenish(battery),"Replenishment remains a between-battle service");
        company.Save.inBattle=false;Require(company.Replenish(battery)&&battery.soldiers==4&&company.Save.coins==0,"Can finish replenishing later");
    }
}
