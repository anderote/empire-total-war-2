using System;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class CampaignResearchVerification
{
    [MenuItem("Empire Campaign/Verify research battle capabilities")]
    public static void Verify()
    {
        var config=new BattleConfig {FrenchBattalions=2,BritishBattalions=1,SoldiersPerBattalion=40,
            FrenchArtillery=0,BritishArtillery=0,CampaignDeployment=true,
            FormationWeapons=new[]{"musket","rifle","magazine"},FormationDoctrine=new[]{0,(int)MilitaryEquipment.AllDoctrine,1},
            FormationQuality=new[]{0f,.08f,0f}};
        var world=new BattleWorld(config);var militia=world.Battalions[0];var rifle=world.Battalions[1];var magazine=world.Battalions[2];
        // Compare issued weapon reloads independently of the optional bayonet penalty.
        foreach(var unit in world.Battalions)world.SetBayonets(unit.Id,false);
        Require(!world.CanForm(militia,FormationKind.Square)&&world.CanForm(rifle,FormationKind.Square),"actual regimental square permissions");
        world.SetFormation(militia.Id,FormationKind.Square);Require(militia.Formation==FormationKind.Line,"simulation rejects untrained square order");
        world.SetFireMode(militia.Id,InfantryFireMode.ByPlatoon);Require(militia.FireMode==InfantryFireMode.ByPlatoon,"all infantry can select platoon fire by rank");
        world.SetFireMode(rifle.Id,InfantryFireMode.ByPlatoon);Require(rifle.FireMode==InfantryFireMode.ByPlatoon,"adopted controlled fire order");
        Require(world.EffectiveRange(militia)==150&&world.EffectiveRange(rifle)==225&&Mathf.Abs(world.BaseReload(rifle)-24*.96f)<.001f,"mixed weapon range and reload remain separate");
        Require(world.Precision(rifle)>1.2f&&rifle.Experience==0,"equipment quality does not grant experience");
        Require(WeaponBallistics.TrySolve(new Vector3(0,1,0),new Vector3(0,1,300),out _,out _,world.AimedRange(rifle))
            &&!WeaponBallistics.TrySolve(new Vector3(0,1,0),new Vector3(0,1,300),out _,out _,world.AimedRange(militia)),"rifle range also reaches ballistics solver");
        world.SetFormation(rifle.Id,FormationKind.Square);Require(Mathf.Abs(rifle.FormationReadyTime-world.Time-3.4f)<.001f,"maneuver course reduces minimum formation transition");
        Require(!RifleFireTiming.SoldierReady(world,rifle,rifle.SoldierStart),"formation delay cannot be bypassed by firing scheduler");
        int soldier=magazine.SoldierStart;
        for(int i=0;i<8;i++)
        {world.Soldiers.RoundsRemaining[soldier]--;float interval=world.ReloadAfterShot(magazine,soldier);Require(interval==(i==7?20f:2.5f),"finite magazine cycle "+i);}
        Require(world.Soldiers.RoundsRemaining[soldier]==22&&world.Soldiers.MagazineRounds[soldier]==8,"magazine replenishment conserves reserve rounds");
        magazine.Formation=FormationKind.Skirmish;magazine.Doctrine=(int)MilitaryEquipment.AllDoctrine;
        world.Soldiers.Positions[soldier]=magazine.Position+FormationMath.SlotOffset(magazine,world.Soldiers.FormationSlots[soldier]);
        world.Soldiers.Facings[soldier]=FormationMath.SlotFacing(magazine,world.Soldiers.FormationSlots[soldier]);
        world.Soldiers.MovementSpeeds[soldier]=0;world.Soldiers.RoundsRemaining[soldier]=10;world.Soldiers.MagazineRounds[soldier]=8;
        for(int i=0;i<10;i++)
        {
            world.Soldiers.ReloadReadyTimes[soldier]=0;
            RifleFireTiming.ScheduleIndependent(world,magazine,soldier,magazine.Position+Vector2.up*100);
            Require(world.Soldiers.RoundsRemaining[soldier]==9-i,"individual scheduler consumes exactly one charge");
            if(i==7)Require(world.Soldiers.MagazineRounds[soldier]==2,"individual magazine refill contains only remaining reserve");
        }
        world.Soldiers.ReloadReadyTimes[soldier]=0;
        Require(!RifleFireTiming.ScheduleIndependent(world,magazine,soldier,magazine.Position+Vector2.up*100)
            &&world.Soldiers.MagazineRounds[soldier]==0,"empty individual magazine cannot manufacture ammunition");
        world.MovementJobs?.Dispose();
        bool rejected=false;try{new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,FormationWeapons=new[]{"musket","unknown"}});}catch(ArgumentException){rejected=true;}
        Require(rejected,"unknown issued equipment rejected");
        var standalone=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=12,
            FrenchArtillery=0,BritishArtillery=0,FormationWeapons=Array.Empty<string>(),FormationDoctrine=Array.Empty<int>(),FormationQuality=Array.Empty<float>()});
        Require(standalone.CanForm(standalone.Battalions[0],FormationKind.Square)&&standalone.EffectiveRange(standalone.Battalions[0])==standalone.Config.MusketRange,"empty Unity optional arrays preserve standalone scenario");
        standalone.MovementJobs?.Dispose();VerifyCampaignAI();Debug.Log("CAMPAIGN_RESEARCH_BATTLE_VERIFIED");
    }
    static void VerifyCampaignAI()
    {
        var world=new BattleWorld(new BattleConfig {FrenchBattalions=1,FrenchCavalry=1,BritishBattalions=1,SoldiersPerBattalion=50,
            FrenchArtillery=0,BritishArtillery=0,CampaignDeployment=true,EnableAI=true,
            FormationWeapons=new[]{"musket","musket","musket"},FormationDoctrine=new[]{15,0,0}});
        var cavalry=world.Battalions[1];var militia=world.Battalions[2];
        world.Battalions[0].Position=new Vector2(200,200);cavalry.Position=new Vector2(30,0);militia.Position=Vector2.zero;
        new TacticalAI(world).Step(1);
        Require(militia.Formation==FormationKind.Line&&Mathf.Abs(militia.DesiredFacing-Mathf.PI/2)<.001f,"untrained AI responds with a valid order instead of stopping at rejected square");
        militia.Role=UnitRole.Skirmisher;militia.Doctrine=(int)MilitaryDoctrine.LightInfantry;new TacticalAI(world).Step(1);
        Require(militia.Formation==FormationKind.Skirmish,"trained campaign light infantry use skirmish deployment");
        militia.Doctrine=(int)MilitaryDoctrine.Square;new TacticalAI(world).Step(1);
        Require(militia.Formation==FormationKind.Square,"trained AI can respond to mounted threat with square");
        world.SetFormation(cavalry.Id,FormationKind.Square);
        Require(cavalry.Formation!=FormationKind.Square,"shared permission gate preserves cavalry formation restrictions");
        world.MovementJobs?.Dispose();
    }
    static void Require(bool ok,string name){if(!ok)throw new Exception(name);}
}
