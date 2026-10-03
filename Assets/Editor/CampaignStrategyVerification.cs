using System;
using System.IO;
using System.Linq;
using EmpireBattle;
using EmpireCampaign.Geography;
using UnityEngine;

// A single -batchmode -nographics editor invocation; no scenes, windows, player builds or user saves.
public static class CampaignStrategyVerification
{
    static int checks;
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    static CountryCampaign Fresh(bool republic=false,uint seed=1)=>new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),
        JsonUtility.FromJson<TheaterData>(Resources.Load<TextAsset>("WorldGeography/world").text),republic?CampaignBalance.Load().Revolutionary():CampaignBalance.Load(),"france",seed,republic?CountryCampaign.RepublicScenario:"");
    static void RoundTrip(CountryCampaign c)
    {
        string json=JsonUtility.ToJson(c.Save,true);bool valid=c.Adopt(JsonUtility.FromJson<CountryCampaignSave>(json),out string why);
        if(!valid){Directory.CreateDirectory("Artifacts/CampaignValidation");File.WriteAllText("Artifacts/CampaignValidation/native-failed-save.json",json);}
        Require(valid,"Native save round trip at turn "+c.Save.world.turn+": "+why);
    }
    public static void Verify()
    {
        checks=0;var atlas=new GeographyAtlas();
        var oversizedBalance=CampaignBalance.Load();oversizedBalance.unitSize=BattleConfig.MaxPersonnelPerRegiment+1;
        bool rejectedOversizedBalance=false;try{new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),JsonUtility.FromJson<TheaterData>(Resources.Load<TextAsset>("WorldGeography/world").text),oversizedBalance,"france");}catch(ArgumentException){rejectedOversizedBalance=true;}
        Require(rejectedOversizedBalance,"campaign balance rejects unit sizes above the 500-person cap");
        var cappedCampaign=Fresh();var oversizedSave=JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(cappedCampaign.Save));oversizedSave.armies[0].units[0].soldiers=BattleConfig.MaxPersonnelPerRegiment+1;
        Require(!cappedCampaign.Adopt(oversizedSave,out _),"campaign save validation rejects a 501-person regiment");
        foreach(string stance in new[]{"Guard","Fortify","Ambush"})
        {
            var c=Fresh();var attacker=c.Save.armies.First(a=>a.owner=="france");var defender=c.Save.armies.First(a=>a.owner=="austria");
            attacker.regionId="region-paris";attacker.movement=2000;defender.regionId="region-brussels";
            c.World.State(defender.regionId).owner="austria";c.World.State("region-antwerp").owner="austria";
            foreach(var u in defender.units)u.equipment=2;defender.commanderXP=100;defender.commanderTrait="Tactician";
            c.Save.playerFaction="austria";Require(c.SetStance("austria",defender.id,stance,out _),"Defensive stance");c.Save.playerFaction="france";
            Require(c.Move("france",attacker.id,"region-lille",out _)&&c.Save.pending.fieldOnly,"Interception pending");RoundTrip(c);
            var e=c.Save.pending;var config=CampaignBattleSetup.Create(c,e,atlas,out var units);
            Require(!config.Map.Settlement&&config.Map.FortLevel==0,"Interception deploys a field battle");
            Require(config.FormationQuality.All(q=>q>=0&&q<=.2f),"Readiness stays inside tactical quality bounds");
            var world=new BattleWorld(config);
            try
            {
                Require(world.BeginBattle(out string why),"Campaign deployment starts: "+why);
                var ai=new TacticalAI(world);var combat=new CombatSystem(world);
                for(int i=0;i<20;i++){ai.Step(.1f);world.StepMovement(.1f);combat.Step(.1f);}
                world.ApplyCasualties(0,10);world.StepMovement(.1f);
                var survivors=units.Select((u,i)=>new{u.id,men=world.Battalions[i].AliveCount}).ToDictionary(x=>x.id,x=>x.men);
                Require(c.CommitTacticalBattle(e.id,true,survivors,out why),"Tactical survivors return to campaign: "+why);
                Require(!c.CommitTacticalBattle(e.id,true,survivors,out _),"Native battle result cannot replay");RoundTrip(c);
            }
            finally{world.MovementJobs?.Dispose();}
        }
        for(uint seed=1;seed<=2;seed++)
        {
            var c=Fresh(true,seed);RoundTrip(c);
            for(int turn=0;turn<12&&c.Save.outcome=="";turn++)
            {
                int before=c.Save.world.turn;c.SimulateRound();Require(c.Save.world.turn>before||c.Save.outcome=="Defeat","Background campaign advances");RoundTrip(c);
                Require(c.Save.turnSummary.complete,"Native end-turn summary completes");
            }
        }
        var naval=Fresh();foreach(var t in naval.Save.treaties)t.war=false;
        var force=naval.Save.armies.First(a=>a.owner=="france");force.regionId="region-marseille";naval.World.State("region-ajaccio").owner="france";
        Require(naval.Sail("france",force.id,"region-ajaccio",out _),"Native voyage embarks");RoundTrip(naval);
        Require(naval.EndTurn(out _),"Native voyage advances");RoundTrip(naval);
        Require(!CountryCampaign.AtSea(naval.Army(force.id)),"Native friendly arrival");
        Directory.CreateDirectory("Artifacts/CampaignValidation");
        File.WriteAllText("Artifacts/CampaignValidation/native-strategy-verification.json","{\"result\":\"passed\",\"checks\":"+checks+",\"method\":\"Single Unity batchmode/nographics session: native saves, tactical deployment/simulation/result return, two 12-turn campaigns and transport\",\"visibleWindows\":0,\"playerBuilds\":0,\"userSavesTouched\":false}\n");
        Debug.Log("CAMPAIGN_STRATEGY_VERIFIED checks="+checks);
    }
}
