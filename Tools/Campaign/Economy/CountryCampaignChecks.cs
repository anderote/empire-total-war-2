using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EmpireCampaign.Geography;

public static class CountryCampaignChecks
{
    static readonly JsonSerializerOptions json=new JsonSerializerOptions {IncludeFields=true};
    static int assertions;
    static RegionCatalog catalog;
    static EconomyProfiles profiles;
    static TheaterData geography;
    static CampaignBalance balance;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);assertions++;}
    static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path),json);
    static CountryCampaign Fresh(string player="france",uint seed=1)=>new CountryCampaign(catalog,profiles,geography,balance,player,seed);
    static CountryCampaignSave Clone(CountryCampaign c)=>JsonSerializer.Deserialize<CountryCampaignSave>(JsonSerializer.Serialize(c.Save,json),json);
    static void Invariants(CountryCampaign c)
    {
        foreach(var r in c.Save.world.regions)Check(CampaignEconomy.ValidState(r.economy),"Valid regional economy "+r.regionId);
        var ids=new HashSet<string>();var enlisted=c.Save.activeRegions.ToDictionary(r=>r,r=>0);
        foreach(var a in c.Save.armies)
        {
            Check(ids.Add(a.id)&&a.Soldiers>0&&a.movement>=0&&a.supply>=0&&a.supply<=1,"Valid army");
            foreach(var u in a.units){Check(ids.Add(u.id)&&u.soldiers>0&&u.soldiers<=balance.unitSize,"Valid persistent regiment");enlisted[u.homeRegionId]+=u.soldiers;}
        }
        foreach(var q in c.Save.recruitment)enlisted[q.regionId]+=q.soldiers;
        foreach(var r in enlisted)Check(c.World.State(r.Key).economy.enlisted==r.Value,"Manpower conservation "+r.Key);
        Check(c.Save.pending==null&&c.Save.phase=="Player"&&!c.Save.roundRunning,"Round commits exactly once");
    }
    public static void Main(string[] args)
    {
        catalog=Read<RegionCatalog>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/campaign.json");
        profiles=Read<EconomyProfiles>("Assets/Resources/CampaignEconomyProfiles.json");
        geography=Read<TheaterData>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/world.json");
        balance=Read<CampaignBalance>("Assets/Resources/CountryCampaignBalance.json");
        if(args.Length==2&&args[0]=="--adopt-save")
        {
            var saved=Read<CountryCampaignSave>(args[1]);
            var restoredSave=new CountryCampaign(catalog,profiles,geography,saved.scenarioId==CountryCampaign.RepublicScenario?balance.Revolutionary():balance,saved.playerFaction,1,saved.scenarioId);
            Check(restoredSave.Adopt(saved,out string problem),"Saved fixture failed: "+problem);
            Console.WriteLine("PASS: saved fixture restored at turn "+restoredSave.Save.world.turn);return;
        }
        if(args.Contains("--orders-only"))
        {OrderChecks.Run(()=>Fresh(),Check,Clone);Console.WriteLine("PASS: "+assertions+" campaign order checks");return;}
        OrderChecks.Run(()=>Fresh(),Check,Clone);
        StrategyChecks.Run(()=>Fresh(),Check,Clone);
        if(args.Contains("--strategy-only")){Console.WriteLine("PASS: "+assertions+" strategy checks");return;}
        OperationsChecks.Run(()=>Fresh(),Check,Clone);
        if(args.Contains("--operations-only")){Console.WriteLine("PASS: "+assertions+" operations checks");return;}
        if(args.Contains("--rules-only"))
        {
            RepublicChecks.Run(()=>new CountryCampaign(catalog,profiles,geography,balance.Revolutionary(),"france",1,CountryCampaign.RepublicScenario),Check,Clone);
            ArmyChecks.Run(()=>Fresh(),Check,Clone);ResearchChecks.Run(()=>Fresh(),Check,Clone);
            ProgressionChecks.Run(()=>Fresh(),Check,Clone);CampaignEncounterChecks.Run(()=>Fresh(),Check,Clone);
            Console.WriteLine("PASS: "+assertions+" campaign rule checks");return;
        }
        AutoresolveChecks.Run(()=>Fresh(),Check,Clone);
        var c=Fresh();string reason;
        string own=c.Owned("france")[0].regionId,foreign=c.Owned("austria")[0].regionId;
        Check(!c.Build("france",foreign,"Farm",out reason),"Foreign build denied");
        Check(!c.Build("austria",foreign,"Farm",out reason),"Player cannot submit enemy actor orders");
        Check(!c.Build("france","region-cairo","Farm",out reason),"Outside theater build denied");
        Check(!c.Research("france","arsenals",out reason),"Research prerequisite enforced");
        Check(c.Research("france","drill",out reason),"National research selected");
        Check(!c.Research("france","industry",out reason),"No costless research switching");
        Check(c.Route("france","region-paris","region-london")==null,"No Channel land teleport");
        Check(c.Owned("austria").Any(r=>c.Route("france","region-paris",r.regionId)!=null),"Reachable continental frontier");
        var army=c.Save.armies.First(a=>a.owner=="france");int troops=army.Soldiers;
        Check(!c.Move("austria",army.id,foreign,out reason),"Enemy army control denied");
        int armyBudget=army.movement;
        Check(!c.Move("france",army.id,"region-vienna",out reason)&&army.movement==armyBudget,"Unaffordable movement has no side effects");
        Check(c.Build("france",own,"Farm",out reason),"Local construction accepted");
        var building=c.World.State(own).buildings.First(b=>b.kind=="Farm"&&b.turnsRemaining>0);
        c.World.State(own).economy.materials=0;
        c.EndTurn(out reason);while(c.Save.pending!=null)c.Resolve(c.Save.pending.id,out reason);
        Check(building.turnsRemaining>0,"Materials constrain construction");
        Check(c.World.FactionState("portugal").lastTaxes==c.Owned("portugal").Sum(r=>c.World.Economy.Forecast(r).taxes),"Outside territory never taxed into theater");
        Check(c.Save.world.turn==2,"One global economy update per round");
        Check(c.World.FactionState("france").lastExpenses>=c.Progress("france").lastMilitaryCost+c.Progress("france").lastAdministration,"Public obligations accounted");
        Check(c.Adopt(Clone(c),out reason),"Country save validator accepts complete state");
        var bad=Clone(c);bad.armies[0].units[0].soldiers++;Check(!c.Adopt(bad,out reason),"Manpower mismatch rejects save");
        bad=Clone(c);bad.countries[0].researchProgress=float.NaN;Check(!c.Adopt(bad,out reason),"NaN research rejects save");
        bad=Clone(c);bad.nextId=1;Check(!c.Adopt(bad,out reason),"ID reuse rejects save");
        bad=Clone(c);bad.world.regions[0].buildings[0].workRemaining=float.NaN;Check(!c.Adopt(bad,out reason),"NaN construction rejects save");
        var legacy=Clone(c).world;legacy.schemaVersion=1;legacy.treasury=1234;legacy.factions=null;
        foreach(var r in legacy.regions)r.economy=null;
        var migrated=new RegionCampaign(catalog,profiles);Check(migrated.RestoreState(legacy)&&migrated.Save.schemaVersion==2&&migrated.FactionState("france").treasury==1234,"Legacy migration preserves purchasing power");
        var battle=Fresh();var attacking=battle.Save.armies.First(a=>a.owner=="france");
        // Move test position to a real legal one-edge frontier; no fake map connection.
        CampaignRoute frontier=null;
        foreach(var home in battle.Owned("france"))foreach(var target in battle.Owned("austria"))
        {var r=battle.Route("france",home.regionId,target.regionId);if(r!=null&&(frontier==null||r.distance<frontier.distance))frontier=r;}
        Check(frontier!=null,"Reachable conflict frontier");
        attacking.regionId=frontier.regions[frontier.regions.Count-2];attacking.movement=2000;
        Check(battle.Move("france",attacking.id,frontier.regions.Last(),out reason)&&battle.Save.pending!=null,"Player attack creates pending encounter");
        string encounter=battle.Save.pending.id;
        var pendingClone=Clone(battle);Check(battle.Adopt(pendingClone,out reason),"Pending encounter survives save validation");
        Check(battle.Resolve(encounter,out reason),"Encounter commits casualties");
        Check(!battle.Resolve(encounter,out reason),"Duplicate battle result rejected");
        Check(battle.Save.armies.Any(a=>a.units.Any(u=>u.experience>0)),"Battle experience persists");
        Check(battle.Save.lastBattle!=null&&battle.Save.lastBattle.encounterId==encounter&&battle.Save.lastBattle.method=="Autoresolve","Autoresolve records a player battle report");
        Check(Fresh().Adopt(Clone(battle),out reason),"Autoresolve report survives save validation");
        Invariants(battle);
        var manual=Fresh();var attacker=manual.Save.armies.First(a=>a.owner=="france");
        var defender=manual.Save.armies.First(a=>a.owner=="austria");
        attacker.regionId=frontier.regions[frontier.regions.Count-2];attacker.movement=2000;defender.regionId=frontier.regions.Last();
        Check(manual.Move("france",attacker.id,defender.regionId,out reason)&&manual.CanFightManually(),"Supported field encounter eligible for manual battle");
        var encounterManual=manual.Save.pending;
        var survivors=manual.BattleUnits(encounterManual,true).Concat(manual.BattleUnits(encounterManual,false)).ToDictionary(u=>u.id,u=>u.soldiers-10);
        var invalidResult=new Dictionary<string,int>(survivors);invalidResult[attacker.units[0].id]=balance.unitSize+1;
        Check(!manual.CommitTacticalBattle(encounterManual.id,true,invalidResult,out reason),"Manual battle cannot create soldiers");
        Check(manual.CommitTacticalBattle(encounterManual.id,true,survivors,out reason),"Manual result commits exact surviving formations");
        Check(attacker.units[0].soldiers==balance.unitSize-10,"Actual survivors retained without a second autoresolve");
        Check(!manual.CommitTacticalBattle(encounterManual.id,true,survivors,out reason),"Duplicate manual commit rejected");
        var report=manual.Save.lastBattle;
        Check(report!=null&&report.encounterId==encounterManual.id&&report.result=="Victory"&&report.method=="Tactical","Tactical victory creates its campaign report");
        Check(report.regiments.Any(u=>u.unitId==attacker.units[0].id&&u.before==balance.unitSize&&u.survivors==balance.unitSize-10&&u.xpAfter>u.xpBefore),"Battle report shows committed casualties and XP");
        Check(Fresh().Adopt(Clone(manual),out reason),"Tactical report persists alongside troop progression");
        var malformedReport=Clone(manual);malformedReport.lastBattle.regiments[0].xpAfter=-1;
        Check(!Fresh().Adopt(malformedReport,out reason),"Malformed battle XP report rejects a save");
        Invariants(manual);
        var project=Fresh();string projectRegion=project.Owned("france")[0].regionId;int budgetBefore=project.World.FactionState("france").treasury;
        Check(project.Build("france",projectRegion,"Farm",out reason),"Cancellable construction starts");
        int slot=Array.FindIndex(project.World.State(projectRegion).buildings,x=>x.kind=="Farm");
        Check(project.CancelConstruction("france",projectRegion,slot,out reason)&&project.World.FactionState("france").treasury==budgetBefore,"Unworked project refunds unused reservation exactly");
        Check(project.Diplomacy("france","prussia","Trade",out reason),"Trade deal accepted");
        Check(project.Diplomacy("france","prussia","Alliance",out reason),"Defensive alliance forms through diplomacy");
        Check(project.Treaty("france","prussia").alliance&&project.Treaty("france","prussia").access,"Alliance grants access without transferring ownership");
        var demob=project.Save.armies.First(x=>x.owner=="france");int homePopulation=project.World.State(demob.units.Last().homeRegionId).economy.population;
        string homeRegion=demob.units.Last().homeRegionId;int manpowerBefore=project.World.State(homeRegion).economy.enlisted;
        Check(project.Demobilize("france",demob.id,out reason)&&project.World.State(homeRegion).economy.enlisted==manpowerBefore-balance.unitSize&&project.World.State(homeRegion).economy.population==homePopulation,"Demobilization returns workers without killing population");
        var a1=Fresh("austria",42);var a2=Fresh("austria",42);
        for(int i=0;i<10;i++){a1.SimulateRound();a2.SimulateRound();}
        Check(JsonSerializer.Serialize(a1.Save,json)==JsonSerializer.Serialize(a2.Save,json),"Deterministic replay with same seed");
        var restored=Fresh("austria",9);Check(restored.Adopt(Clone(a1),out reason),"Midcampaign restore");
        a1.SimulateRound();restored.SimulateRound();Check(JsonSerializer.Serialize(a1.Save,json)==JsonSerializer.Serialize(restored.Save,json),"Save/resume produces identical next round");
        var openEnded=Fresh();openEnded.Save.world.turn=80;
        Check(Fresh().Adopt(Clone(openEnded),out reason),"Open territorial save exceeds the former turn limit");
        var oldEnding=Clone(openEnded);oldEnding.schemaVersion=4;oldEnding.outcome="Victory";oldEnding.victoryPath="Old objective";
        var resumed=Fresh();Check(resumed.Adopt(oldEnding,out reason)&&resumed.Save.outcome==""&&resumed.Save.victoryPath=="","Legacy completed campaign resumes as open territorial play");
        RepublicChecks.Run(()=>new CountryCampaign(catalog,profiles,geography,balance.Revolutionary(),"france",1,CountryCampaign.RepublicScenario),Check,Clone);
        ArmyChecks.Run(()=>Fresh(),Check,Clone);
        ResearchChecks.Run(()=>Fresh(),Check,Clone);
        ProgressionChecks.Run(()=>Fresh(),Check,Clone);
        CampaignEncounterChecks.Run(()=>Fresh(),Check,Clone);
        var rows=new List<object>();
        foreach(string player in new[]{"france","austria","prussia"})for(uint seed=1;seed<=8;seed++)
        {
            var game=Fresh(player,seed);int rounds=0,battles=0,firstTech=0,minTreasury=balance.startingTreasury;
            for(;rounds<balance.maxTurns&&game.Save.outcome=="";rounds++)
            {
                game.SimulateRound();Invariants(game);
                Check(game.Save.world.turn>=balance.minimumVictoryTurn||game.Save.outcome==""||game.Owned(player).Count==0,"No premature objective victory");battles=game.Save.resolvedEncounters.Count;
                if(firstTech==0&&game.Progress(player).technologies.Count>0)firstTech=game.Save.world.turn;
                minTreasury=Math.Min(minTreasury,game.World.FactionState(player).treasury);
            }
            rows.Add(new {player,seed,rounds,battles,firstTech,minTreasury,outcome=game.Save.outcome,path=game.Save.victoryPath,
                regions=game.Owned(player).Count,treasury=game.World.FactionState(player).treasury,
                technologies=game.Progress(player).technologies.Count,net=game.Progress(player).lastNet,buildings=game.Owned(player).Sum(r=>r.buildings.Count(b=>b.kind!="Empty"&&b.turnsRemaining==0)),armies=game.Save.armies.Count(x=>x.owner==player),
                foodSecurity=game.Owned(player).Count>0?game.Owned(player).Average(r=>r.economy.wellbeing):0});
        }
        var revolutionaryRuns=new List<object>();
        for(uint seed=1;seed<=8;seed++)
        {
            var game=new CountryCampaign(catalog,profiles,geography,balance.Revolutionary(),"france",seed,CountryCampaign.RepublicScenario);
            for(int round=0;round<40&&game.Save.outcome=="";round++)
            {
                int previous=game.Save.world.turn;game.SimulateRound();Invariants(game);
                Check(game.Save.world.turn>previous||game.Save.outcome=="Defeat","Revolutionary AI resolves national decisions and advances");
                Check(game.Save.outcome==""||game.Save.outcome=="Defeat"&&game.Eliminated("france"),"Only total city and army elimination ends revolutionary play");
                var restoredRepublic=new CountryCampaign(catalog,profiles,geography,balance.Revolutionary(),"france",seed,CountryCampaign.RepublicScenario);
                Check(restoredRepublic.Adopt(Clone(game),out reason),"Live coalition campaign round survives save validation");
            }
            revolutionaryRuns.Add(new{seed,turn=game.Save.world.turn,regions=game.Owned("france").Count,armies=game.Save.armies.Count(a=>a.owner=="france"),outcome=game.Save.outcome,government=game.Save.story.government,stability=game.Save.story.stability,battles=game.Save.resolvedEncounters.Count});
        }
        Directory.CreateDirectory("Artifacts/CampaignValidation");
        File.WriteAllText("Artifacts/CampaignValidation/republic-balance.json",JsonSerializer.Serialize(revolutionaryRuns,new JsonSerializerOptions{WriteIndented=true}));
        Directory.CreateDirectory("Artifacts/CampaignValidation");
        File.WriteAllText("Artifacts/CampaignValidation/country-balance.json",JsonSerializer.Serialize(new {assertions,runs=rows.Count,results=rows},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("PASS: "+assertions+" country checks, 24 sandbox campaigns and 8 Revolutionary campaigns; balance report saved.");
    }
}
