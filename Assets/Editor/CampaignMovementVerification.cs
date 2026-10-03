using System;
using System.Linq;
using EmpireBattle;
using EmpireCampaign.Geography;
using UnityEngine;

// Opt-in only. Do not attach this verifier to builds or resume campaign QA automatically.
public static class CampaignMovementVerification
{
    static int checks;
    static void Require(bool value,string message){checks++;if(!value)throw new Exception("Campaign movement: "+message);}
    [UnityEditor.MenuItem("ETW2/Verify Campaign Movement (explicit QA only)")]
    public static void Verify()
    {
        checks=0;var atlas=new GeographyAtlas();var c=new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),atlas.Data,CampaignBalance.Load(),"france",1,"",atlas);
        var main=c.Save.armies.First(a=>a.owner=="france");var origin=c.Position(main);CampaignMapPoint destination=null;
        for(int i=0;i<16;i++)
        {
            var p=new CampaignMapPoint(origin.lon+(float)Math.Cos(i*Math.PI/8)*.3f,origin.lat+(float)Math.Sin(i*Math.PI/8)*.2f);
            if(c.Terrain.Ground(p).regionId==main.regionId&&c.CanMarchTo("france",main.id,p.lon,p.lat,out _)&&c.LandPath(main,p).distance>10){destination=p;break;}
        }
        Require(destination!=null,"an off-city destination is reachable");int before=main.movement;
        Require(c.MarchTo("france",main.id,destination.lon,destination.lat,out var reason),reason);
        Require(main.movement<before&&!c.AtSettlement(main),"field movement spends points without snapping to a city");
        string unit=main.units[0].id;Require(c.Detach("france",main.id,new[]{unit},out var id,out reason),reason);var detached=c.Army(id);
        Require(c.ForcesCanMeet(main,detached)&&main.movement==detached.movement,"detach inherits position and remaining points");
        detached.movement=1;Require(c.MarchTo("france",id,origin.lon,origin.lat,out reason),reason);var stopped=c.Position(detached);
        Require(c.Halt("france",id,out reason)&&CampaignMovementTerrain.Distance(stopped,c.Position(detached))<.001f&&detached.movement==0,"halt preserves field position and spent points");
        var candidate=JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(c.Save));Require(c.Adopt(candidate,out reason),"field save round trip: "+reason);
        candidate=JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(c.Save));candidate.armies.First(a=>a.id==id).longitude=float.NaN;
        Require(!c.Adopt(candidate,out reason),"nonfinite map positions are rejected");
        var ocean=new CampaignMapPoint(-30,45);Require(c.Terrain.Ground(ocean).blocked,"open water blocks land movement");
        var desert=c.Terrain.Ground(new CampaignMapPoint(10,26));Require(desert.attrition>0&&desert.supplyLoss>0,"Sahara has heat attrition and supply loss");
        var mountain=c.Terrain.Ground(new CampaignMapPoint(6.865f,45.833f));Require(mountain.blocked||mountain.cost>2,"high Alps block or slow movement");
        VerifyReinforcementLimit(atlas);VerifyReserves();Debug.Log("CAMPAIGN_MOVEMENT_VERIFIED checks="+checks);
    }
    static void VerifyReinforcementLimit(GeographyAtlas atlas)
    {
        var c=new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),atlas.Data,CampaignBalance.Load(),"france",1,"",atlas);
        var main=c.Save.armies.First(a=>a.owner=="france");string reason;
        foreach(var unit in main.units.Skip(1).ToArray())Require(c.Detach("france",main.id,new[]{unit.id},out _,out reason),reason);
        var enemy=c.Save.armies.First(a=>a.owner=="austria");var p=c.Position(main);
        enemy.longitude=p.lon;enemy.latitude=p.lat;enemy.regionId=main.regionId;enemy.mapPositionKnown=true;
        Require(c.MarchTo("france",main.id,p.lon,p.lat,out reason,enemy.id),reason);
        Require(c.Save.pending!=null&&c.Save.pending.fieldOnly&&c.Save.pending.attackerArmyIds.Count==5,"main army plus exactly four reinforcements; field fight does not capture a city");
        Require(c.Save.pending.defenderArmyIds.Count<=5,"defender reinforcement limit");
        var full=c.Save.armies.First(a=>a.owner=="france");c.Save.pending=null;c.Save.phase="Player";
        while(full.units.Count<CountryCampaign.MaxArmyRegiments)full.units.Add(new CampaignRegiment{id="capacity-"+full.units.Count,soldiers=1,homeRegionId=full.regionId});
        Require(!c.CanAssignRecruits("france",full.regionId,full.id,out reason),"80-unit army cannot reserve a 81st recruitment place");
    }
    static void VerifyReserves()
    {
        var config=new BattleConfig{CampaignDeployment=true,RequireDeployment=true,RegionalMap=true,EnableAI=false,
            FrenchBattalions=80,BritishBattalions=80,SoldiersPerBattalion=500,FrenchArtillery=0,BritishArtillery=0,
            FormationWeapons=Enumerable.Repeat("musket",160).ToArray(),FormationDoctrine=Enumerable.Repeat(1,160).ToArray()};
        var world=new BattleWorld(config);
        Require(world.CountAlive(ArmySide.French)+world.CountAlive(ArmySide.British)<=20000,"initial deployment respects the combined troop cap");
        Require(world.Battalions.Sum(b=>b.SurvivorCount)==80000,"off-field reserves remain living campaign soldiers");
        Require(world.Battalions.Where(b=>b.InReserve).All(b=>!b.HasEnteredBattle&&b.AliveCount==0),"reserves cannot fight or be selected on the field");
        Require(world.BeginBattle(out var reason),reason);
        var regiment=world.Battalions.First(b=>b.Side==ArmySide.French&&!b.InReserve);world.ApplyCasualties(regiment.Id,regiment.SoldierCount);
        int reserves=world.ReserveTroops(ArmySide.French);world.StepMovement(21);
        Require(world.ReserveTroops(ArmySide.French)<reserves,"vacated capacity admits a reinforcement wave");
        Require(world.CountAlive(ArmySide.French)+world.CountAlive(ArmySide.British)<=20000,"reinforcement waves preserve the combined troop cap");
        Require(world.Battalions.Sum(b=>b.SurvivorCount)==79500,"wave admission conserves survivors");
    }
}
