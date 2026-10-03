using System;
using System.Linq;
using EmpireCampaign.Geography;

public static class StrategyChecks
{
    static void Freeze(CountryCampaign c)
    {foreach(var p in c.Save.countries)p.lastPlanningTurn=c.Save.world.turn;foreach(var a in c.Save.armies)a.lastAIActionTurn=c.Save.world.turn;}
    static void Turn(CountryCampaign c)
    {Freeze(c);if(!c.EndTurn(out string why))throw new Exception(why);int limit=100;while(c.Save.pending!=null&&limit-->0)c.Resolve(c.Save.pending.id,out _);if(limit<=0)throw new Exception("Strategy turn failed to finish");}
    public static void Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        string why;
        foreach(string stance in new[]{"Guard","Fortify","Ambush"})
        {
            var c=fresh();var a=c.Save.armies.First(a=>a.owner=="france");var defender=c.Save.armies.First(a=>a.owner=="austria");
            a.regionId="region-paris";a.movement=2000;defender.regionId="region-brussels";c.World.State(defender.regionId).owner="austria";c.World.State("region-antwerp").owner="austria";
            c.Save.playerFaction="austria";check(c.SetStance("austria",defender.id,stance,out why)&&defender.movement==0,"Preparing stance spends movement: "+stance);c.Save.playerFaction="france";
            if(stance=="Ambush")check(!c.CanObserve(defender),"Ambush hidden from distant player scouts");
            uint random=c.Save.randomState;string preview=c.MovementPreview(a.id,"region-lille");check(preview.Contains("km")&&random==c.Save.randomState,"Movement preview is read-only");
            check(c.Move("france",a.id,"region-lille",out why)&&c.Save.pending?.fieldOnly==true,"Enemy stance intercepts a threatened approach: "+why);
            var e=c.Save.pending;check(e.defenderArmyIds.Contains(defender.id)&&e.garrison.Count==0&&e.ambush==(stance=="Ambush"),"Interception uses defending army, no city garrison");
            check(!c.CanBesiege(out _),"Field interception cannot turn into a settlement siege");
            check(c.Adopt(clone(c),out why),"Interception saves: "+why);a=c.Army(a.id);defender=c.Army(defender.id);e=c.Save.pending;
            var survivors=c.BattleUnits(e,true).Concat(c.BattleUnits(e,false)).ToDictionary(u=>u.id,u=>Math.Max(1,u.soldiers-10));
            string cityOwner=c.World.State(e.regionId).owner;
            check(c.CommitTacticalBattle(e.id,true,survivors,out why)&&c.World.State(e.regionId).owner==cityOwner,"Winning interception does not capture a city");
            check(a.movement==0&&defender.regionId!="region-brussels"&&defender.stance=="March","Defeated interceptor retreats and loses stance");
            check(a.commanderXP==40&&a.commanderWins==1&&defender.commanderWounds==2&&c.WarScore("france","austria")==6,"Battle awards commander XP, wounds and war score exactly once");
            check(c.Save.history.Any(h=>h.kind=="Battle")&&c.Adopt(clone(c),out why),"Battle history and commanders persist: "+why);
        }
        {
            var c=fresh();var a=c.Save.armies.First(a=>a.owner=="france");a.regionId="region-paris";a.commanderXP=100;
            int funds=c.World.FactionState("france").treasury;
            check(c.DevelopCommander("france",a.id,"Logistician",out why)&&c.World.FactionState("france").treasury==funds-100&&c.ArmyMovement(a)>c.Movement("france"),"Paid commander specialty changes movement");
            check(!c.DevelopCommander("france",a.id,"Tactician",out _),"Commander cannot buy a second specialty");
            a.commanderWounds=2;check(c.ArmyMovement(a)==c.Movement("france"),"Wounded commander specialty suspended");
            check(c.ReplaceCommander("france",a.id,out why)&&a.commanderXP==0&&a.commanderTrait==""&&a.commanderWounds==0,"Paid succession resets commander progress");
            check(c.Adopt(clone(c),out why),"Succession saves: "+why);
        }
        {
            var c=fresh();var t=c.Treaty("france","austria");c.Save.world.turn=7;t.warScoreA=t.a=="france"?70:-70;
            c.World.FactionState("france").treasury=5000;int ours=5000,theirs=c.World.FactionState("austria").treasury;
            check(c.EvaluatePeace("france","austria",500,"","region-venice",6,out why),"Victories plus payment can secure territorial peace: "+why);
            check(c.NegotiatePeace("france","austria",500,"","region-venice",6,out why),"Peace terms sign atomically: "+why);
            check(!t.war&&t.peaceUntil==13&&c.World.State("region-venice").owner=="france"&&c.World.FactionState("france").treasury==ours-500&&c.World.FactionState("austria").treasury==theirs+500,"Treaty transfers the agreed province and payment");
            check(!c.NegotiatePeace("france","austria",500,"","",6,out _)&&!c.Diplomacy("france","austria","War",out _),"Signed treaty cannot pay twice or be broken during truce");
            check(c.Adopt(clone(c),out why),"Territorial peace saves: "+why);
            var bad=clone(c);bad.treaties[0].warScoreA=101;check(!c.Adopt(bad,out _),"Out-of-range war score rejected");
        }
        {
            var c=fresh();foreach(var t in c.Save.treaties)t.war=false;
            var r=c.World.State("region-paris");r.economy.unrest=95;c.Politics(r.regionId).policy="Exploit";
            check(c.CampaignAlerts().Any(a=>a.kind=="Unrest"&&a.regionId==r.regionId),"High unrest produces a warning");
            Turn(c);Turn(c);
            check(c.InRevolt(r.regionId)&&!c.Recruit("france",r.regionId,out _),"Sustained unrest creates a revolt and stops recruitment");
            c.World.FactionState("france").treasury=2000;
            check(c.ProvincialRelief("france",r.regionId,out why)&&!c.ProvincialRelief("france",r.regionId,out _),"Provincial relief can be paid once per turn");
            var army=c.Save.armies.First(a=>a.owner=="france");army.regionId=r.regionId;int men=army.Soldiers;
            Turn(c);check(!c.InRevolt(r.regionId)&&army.Soldiers<men&&c.Save.history.Any(h=>h.kind=="Pacification"),"Garrison suppresses revolt at a real troop cost");
            check(c.Save.turnSummary.complete&&c.Save.turnSummary.turn==c.Save.world.turn-1&&c.Save.turnSummary.troopsAfter==c.Save.armies.Where(a=>a.owner=="france").Sum(a=>a.Soldiers),"Turn summary captures final campaign state");
            check(c.Adopt(clone(c),out why),"Revolt and summary state saves: "+why);
        }
        {
            var c=fresh();string city="region-lille";var r=c.World.State(city);r.owner="austria";r.economy.unrest=95;
            var p=c.Politics(city);p.loyalist="france";p.policy="Exploit";
            for(int i=0;i<4;i++)Turn(c);
            check(r.owner=="france"&&c.Save.armies.Any(a=>a.owner=="france"&&a.regionId==city&&a.name.StartsWith("Restoration militia")),"Neglected occupation can restore its former faction with militia");
            check(c.Adopt(clone(c),out why),"Restoration conserves manpower and saves: "+why);
        }
        {
            var c=fresh();c.Save.world.turn=5;
            foreach(string enemy in new[]{"austria","prussia","sardinia"}){var t=c.Treaty("france",enemy);t.war=true;t.access=t.trade=t.alliance=false;t.warStart=1;}
            c.SimulateRound();
            check(c.Save.history.Any(h=>h.kind=="Peace"&&h.text.StartsWith("france and")),"AI under multi-front pressure negotiates before exhausting its budget");
            check(c.Progress("france").strategicPlanTurn==5&&!string.IsNullOrEmpty(c.Progress("france").strategicIntent),"AI records its strategic intent once per turn");
            check(c.Adopt(clone(c),out why),"Strategic plans and AI peace save: "+why);
        }
        {
            var c=fresh();var legacy=clone(c);legacy.politics=null;legacy.history=null;legacy.turnSummary=null;
            check(c.Adopt(legacy,out why)&&c.Save.politics.Count==c.Save.activeRegions.Count,"Legacy saves initialize province administration and history");
            var bad=clone(c);bad.armies[0].stance="Teleport";check(!c.Adopt(bad,out _),"Unknown stance rejected");
            bad=clone(c);bad.politics[0].rebels=-1;check(!c.Adopt(bad,out _),"Negative insurgents rejected");
            check(c.CampaignAlerts().Any(a=>a.kind=="Idle army"),"Idle armies appear in turn-management alerts");
        }
    }
}
