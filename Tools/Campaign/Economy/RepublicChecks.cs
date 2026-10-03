using System;
using System.Linq;
using EmpireCampaign.Geography;

public static class RepublicChecks
{
    public static void Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        var c=fresh();string why;
        check(c.IsRepublic&&c.DateLabel=="1792 / Autumn","Republic starts in autumn 1792");
        check(c.World.State("region-turin").owner=="sardinia"&&c.World.State("region-brussels").owner=="austria"&&c.World.State("region-milan").owner=="austria","Pre-conquest French frontier ownership");
        check(c.Progress("france").startingRegions==11&&!c.Save.countries.Any(p=>p.factionId=="cisalpine"),"1792 regions and absent future Cisalpine state");
        check(new[]{"austria","prussia","sardinia"}.All(f=>c.AtWar("france",f))&&!c.AtWar("france","britain")&&!c.AtWar("france","spain"),"1792 opening coalition is staged");
        check(c.Save.armies.Count(a=>a.owner=="france")==4&&c.Save.armies.Any(a=>a.name=="Alpine Reserve")&&c.Save.armies.Where(a=>a.owner=="france").SelectMany(a=>a.units).Any(u=>u.role=="Militia"),"Three French fronts plus the Paris National Guard");
        check(c.Save.story.napoleonStage==0&&!c.Save.armies.Any(a=>a.generalName=="Napoleon Bonaparte"),"No premature Napoleon field command");
        check(c.Adopt(clone(c),out why),"Revolutionary save round trip");
        {
            var opening=fresh();var north=opening.Save.armies.First(a=>a.owner=="france"&&a.regionId=="region-lille");
            check(opening.Recruit("france","region-paris","Militia",out why),"Unmodified French opening can buy a new regiment");
            check(opening.March("france",north.id,"region-brussels",out why)&&opening.Save.pending!=null&&opening.CanFightManually(),"Starting northern army can choose a Brussels battle without fixture movement bonuses");
            var battle=opening.Save.pending;var all=opening.BattleUnits(battle,true).Concat(opening.BattleUnits(battle,false)).ToArray();
            check(!opening.EndTurn(out why)&&!opening.Recruit("france","region-paris",out why),"Pending battle blocks unrelated orders and ending the turn");
            // Supply a tactical result to test the campaign boundary, not battlefield AI or rendering.
            var survivors=all.ToDictionary(u=>u.id,u=>u.soldiers-30);var xp=all.ToDictionary(u=>u.id,u=>120);
            check(opening.CommitTacticalBattle(battle.id,true,survivors,out why,xp)&&opening.World.State("region-brussels").owner=="france","Manual battle result captures the city and returns control");
            var veteran=north.units[0];
            check(veteran.progression.AvailablePoints>0&&opening.TrainSkill("france",north.id,veteran.id,EmpireBattle.UnitSkill.Accuracy,out why),"Winning survivors can spend earned skill points");
            int treasury=opening.World.FactionState("france").treasury;
            check(opening.EquipItem("france",north.id,veteran.id,"stout-boots",out why)&&opening.World.FactionState("france").treasury<treasury,"Battle survivors can buy affordable period equipment");
            for(int turn=0;turn<2;turn++)
            {
                if(opening.Save.story.decision!="")check(opening.DecideRepublic(opening.Save.story.decision,1,out why),"Opening national decision has a free continuation");
                check(opening.EndTurn(out why),"French opening advances with paid orders");
                int guard=0;while(opening.Save.pending!=null&&guard++<100)
                {
                    var next=opening.Save.pending;bool playerAttacks=next.attacker=="france";
                    var playerUnits=opening.BattleUnits(next,playerAttacks);var enemyUnits=opening.BattleUnits(next,!playerAttacks);
                    var result=playerUnits.ToDictionary(u=>u.id,u=>Math.Max(1,u.soldiers-5));foreach(var u in enemyUnits)result[u.id]=0;
                    check(opening.CanFightManually()&&opening.CommitTacticalBattle(next.id,playerAttacks,result,out why),"Enemy attack lets the player choose and commit a manual defence");
                }
                check(opening.Save.pending==null&&opening.CanOrder("france"),"Interrupted enemy turn returns player control");
            }
            check(opening.Save.recruitment.All(q=>q.regionId!="region-paris")&&opening.Save.armies.Any(a=>a.owner=="france"&&a.units.Any(u=>u.homeRegionId=="region-paris")),"Paid Paris recruitment delivers an actual field regiment");
            check(veteran.loadout.items.Contains("stout-boots"),"Paid equipment reaches the surviving veteran after a supplied turn");
            check(fresh().Adopt(clone(opening),out why),"Recruit, fight, gain XP, spend a skill and buy kit round-trip together");
        }
        var bad=clone(c);bad.story.stability=101;check(!c.Adopt(bad,out why),"Invalid stability rejects save");
        bad=clone(c);bad.story.milestones.Add("italy");check(!c.Adopt(bad,out why),"Future story milestone rejects save");
        bad=clone(c);bad.scenarioId="";check(!c.Adopt(bad,out why),"Story cannot load as a sandbox");
        // Date/decision fixture: negotiated peace isolates event sequencing from randomized battles.
        Action neutralize=()=>{foreach(var t in c.Save.treaties)t.war=false;};
        neutralize();check(c.EndTurn(out why)&&c.Save.world.turn==2&&c.DateLabel=="1793 / Spring","Half-year date rollover from autumn");
        check(new[]{"britain","batavian","spain","portugal","naples"}.All(f=>c.AtWar("france",f))&&c.Save.story.decision=="coalition","1793 multi-front coalition and domestic decision");
        check(!c.EndTurn(out why)&&c.Save.world.turn==2,"National decision must be resolved before advancing");
        int money=c.World.FactionState("france").treasury,men=c.Save.armies.Sum(a=>a.Soldiers);
        check(c.DecideRepublic("coalition",0,out why)&&c.World.FactionState("france").treasury==money-200&&c.Save.armies.Sum(a=>a.Soldiers)==men&&c.Progress("france").policy=="Mobilization","Mobilization pays transition cost without creating troops");
        check(!c.DecideRepublic("coalition",0,out why),"Decisions cannot award twice");
        money=c.World.FactionState("france").treasury;
        check(c.FundRepublicRelief(out why)&&c.World.FactionState("france").treasury==money-200&&!c.FundRepublicRelief(out why),"Civilian relief costs money and cannot repeat this turn");
        while(c.Save.world.turn<24)
        {
            neutralize();c.World.FactionState("france").treasury=10000;
            if(c.Save.story.decision!="")
            {
                int option=c.Save.story.decision=="brumaire"?1:0;
                check(c.DecideRepublic(c.Save.story.decision,option,out why),"Timeline decision resolves: "+why);
            }
            check(c.EndTurn(out why),"Story round advances");
            while(c.Save.pending!=null)check(c.Resolve(c.Save.pending.id,out why),"Fixture encounter resolves");
            check(c.Save.outcome=="","Sandbox victories cannot prematurely end revolutionary story");
            check(fresh().Adopt(clone(c),out why),"Story milestones and pending decisions survive each half-year");
            if(c.Save.world.turn==3)check(c.Save.story.napoleonStage==1,"Toulon promotion in autumn 1793");
            if(c.Save.world.turn==8)
            {
                check(c.Save.story.decision=="italy","Italian appointment offered in spring 1796");
                men=c.Save.armies.Sum(a=>a.Soldiers);int count=c.Save.armies.Count;
                check(c.DecideRepublic("italy",0,out why)&&c.Save.armies.Sum(a=>a.Soldiers)==men&&c.Save.armies.Count==count&&c.Save.armies.Any(a=>a.name=="Army of Italy"&&a.generalName=="Napoleon Bonaparte"),"Bonaparte takes an existing army with no phantom regiments");
            }
            if(c.Save.world.turn==14)check(c.AtWar("france","russia")&&c.AtWar("france","austria")&&!c.AtWar("france","prussia"),"Second Coalition without automatic Prussian re-entry");
            if(c.Save.world.turn==16)check(c.Save.story.government=="Civilian Republic","Civilian alternative to Brumaire persists");
        }
        check(c.DateLabel=="1804 / Spring"&&c.Save.story.decision=="constitution","1804 constitutional settlement is playable");
        c.World.FactionState("france").treasury=10000;
        check(c.DecideRepublic("constitution",0,out why)&&c.Save.story.ending=="Republic","Republic ending is recorded");
        {
            var legitimacy=fresh();legitimacy.Save.world.turn=16;legitimacy.World.FactionState("france").treasury=10000;
            legitimacy.Save.story.milestones.AddRange(new[]{"coalition","toulon","thermidor","directory","italy","second-coalition","brumaire"});
            legitimacy.Save.story.napoleonStage=2;legitimacy.Save.story.government="Directory";legitimacy.Save.story.decision="brumaire";
            legitimacy.Save.armies.First(a=>a.owner=="france").generalName="Napoleon Bonaparte";
            check(!legitimacy.DecideRepublic("brumaire",0,out why),"Bonaparte cannot claim a Consulate on appointment alone");
            legitimacy.Save.story.napoleonPrestige=2;
            check(legitimacy.DecideRepublic("brumaire",0,out why)&&legitimacy.Save.story.government=="Consulate","Victorious Bonaparte can back the Consulate");
            check(legitimacy.Adopt(clone(legitimacy),out why),"Earned Bonapartist legitimacy saves: "+why);
        }
        var imperial=clone(c);imperial.story.government="Consulate";imperial.story.ending="";imperial.story.decision="constitution";
        check(fresh().Adopt(imperial,out why),"Consulate branch loads");
        var branch=fresh();check(branch.Adopt(imperial,out why)&&branch.DecideRepublic("constitution",0,out why)&&branch.Save.story.ending=="Empire","Empire alternative is playable");
        var endless=fresh();endless.Save.world.turn=80;endless.Save.story.stability=0;
        check(endless.Adopt(clone(endless),out why)&&endless.Save.outcome=="","Save can continue beyond the old turn limit with zero stability");
        var truce=fresh();
        foreach(var t in truce.Save.treaties)t.war=false;
        truce.Save.world.turn=13;truce.Save.story.milestones.AddRange(new[]{"coalition","toulon","thermidor","directory","italy"});truce.Save.story.napoleonStage=1;truce.Save.story.government="Directory";
        truce.Treaty("france","austria").peaceUntil=18;
        check(truce.EndTurn(out why)&&!truce.AtWar("france","austria")&&truce.AtWar("france","russia"),"Second Coalition respects negotiated Austrian truce");
        var fallen=fresh();fallen.World.State("region-paris").owner="austria";
        foreach(var t in fallen.Save.treaties)t.war=false;
        fallen.EndTurn(out why);check(fallen.Save.outcome==""&&fallen.Save.story.stability<60,"Paris loss is a setback, not a forced ending");
        check(fallen.DecideRepublic("coalition",1,out why),"Resolve fixture national decision before retreat");
        foreach(var r in fallen.Owned("france"))r.owner="austria";
        check(!fallen.Eliminated("france"),"An independent surviving army permits play with no cities");
        check(fallen.EndTurn(out why)&&fallen.Save.outcome=="","Armies can keep fighting after every city is captured");
        foreach(var a in fallen.Save.armies.Where(a=>a.owner=="france").ToArray())
        {foreach(var u in a.units)fallen.World.State(u.homeRegionId).economy.enlisted-=u.soldiers;fallen.Save.armies.Remove(a);}
        check(fallen.Eliminated("france")&&!fallen.EndTurn(out why)&&fallen.Save.outcome=="Defeat","Defeat requires the final city and final army both lost");
        var lastDetachment=fresh();var source=lastDetachment.Save.armies.First(a=>a.owner=="france");
        check(lastDetachment.Detach("france",source.id,new[]{source.units[0].id},out string detached,out why),"Create last-detachment fixture");
        foreach(var a in lastDetachment.Save.armies.Where(a=>a.owner=="france"&&a.id!=detached).ToArray())
        {foreach(var u in a.units)lastDetachment.World.State(u.homeRegionId).economy.enlisted-=u.soldiers;lastDetachment.Save.armies.Remove(a);}
        foreach(var r in lastDetachment.Owned("france"))r.owner="austria";
        foreach(var t in lastDetachment.Save.treaties)t.war=false;
        check(lastDetachment.Army(detached).generalName==""&&!lastDetachment.Eliminated("france")&&lastDetachment.EndTurn(out why)&&lastDetachment.Save.outcome=="","A single independent regiment prevents defeat even without a city or general");
        var rebuild=fresh();foreach(var a in rebuild.Save.armies.Where(a=>a.owner=="france").ToArray())
        {foreach(var u in a.units)rebuild.World.State(u.homeRegionId).economy.enlisted-=u.soldiers;rebuild.Save.armies.Remove(a);}
        foreach(var t in rebuild.Save.treaties)t.war=false;
        check(!rebuild.Eliminated("france")&&rebuild.EndTurn(out why)&&rebuild.Save.outcome=="","Cities permit rebuilding after every field army is destroyed");
        check(rebuild.Recruit("france","region-paris",out why),"An army can be rebuilt from a surviving city");
    }
}
