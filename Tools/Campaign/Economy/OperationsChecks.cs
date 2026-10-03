using System;
using System.Linq;
using System.Collections.Generic;
using EmpireCampaign.Geography;

public static class OperationsChecks
{
    static void Peace(CountryCampaign c){foreach(var t in c.Save.treaties)t.war=false;}
    static CampaignArmy AtPort(CountryCampaign c)
    {var a=c.Save.armies.First(x=>x.owner=="france");a.regionId="region-marseille";a.movement=2000;c.World.State(a.regionId).owner="france";return a;}
    static void Turn(CountryCampaign c)
    {c.EndTurn(out _);int guard=0;while(c.Save.pending!=null&&guard++<100)c.Resolve(c.Save.pending.id,out _);if(guard>=100)throw new Exception("Turn stalled");}
    public static void Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        string why;
        {
            var c=fresh();Peace(c);var a=AtPort(c);c.World.State("region-ajaccio").owner="france";
            int cost=c.TransportCost(a,"region-ajaccio"),funds=c.World.FactionState("france").treasury,men=a.Soldiers;
            check(c.SeaRoute(a.regionId,"region-ajaccio")!=null,"Corsica has a sea route");
            check(c.Sail("france",a.id,"region-ajaccio",out why)&&CountryCampaign.AtSea(a)&&a.movement==0,"Army embarks with spent movement");
            check(c.World.FactionState("france").treasury==funds-cost,"Transport charter paid exactly once");
            check(!c.Move("france",a.id,"region-ajaccio",out _)&&!c.Sail("france",a.id,"region-ajaccio",out _),"Embarked army cannot move or charter twice");
            string id=a.id;check(c.Adopt(clone(c),out why),"Voyage saves: "+why);a=c.Army(id);
            int turns=a.seaTurns;for(int i=0;i<turns;i++)Turn(c);
            check(!CountryCampaign.AtSea(a)&&a.regionId=="region-ajaccio"&&a.Soldiers==men,"Transport lands intact after its travel time");
            check(c.Adopt(clone(c),out why),"Arrival state saves: "+why);
        }
        foreach(bool win in new[]{true,false})
        {
            var c=fresh();Peace(c);c.Treaty("france","austria").war=true;var a=AtPort(c);
            c.World.State("region-ajaccio").owner="austria";
            // Freeze enemy field orders to isolate a player landing without disabling model turn processing.
            foreach(var p in c.Save.countries)p.lastPlanningTurn=c.Save.world.turn;
            foreach(var force in c.Save.armies)force.lastAIActionTurn=c.Save.world.turn;
            check(c.Sail("france",a.id,"region-ajaccio",out why),"Invasion charter accepted");
            check(c.EndTurn(out why)&&c.Save.pending?.amphibious==true,"Enemy port creates a landing battle");
            var e=c.Save.pending;check(e.attackerArmyIds.SequenceEqual(new[]{a.id})&&c.CanFightManually(),"Landing uses transported army and permits tactical battle");
            string id=a.id;check(c.Adopt(clone(c),out why),"Pending landing saves: "+why);a=c.Army(id);e=c.Save.pending;
            var survivors=c.BattleUnits(e,true).Concat(c.BattleUnits(e,false)).ToDictionary(u=>u.id,u=>Math.Max(1,u.soldiers-10));
            check(c.CommitTacticalBattle(e.id,win,survivors,out why),"Landing result commits: "+why);
            check(win?!CountryCampaign.AtSea(a)&&a.regionId=="region-ajaccio"&&c.World.State(a.regionId).owner=="france":CountryCampaign.AtSea(a)&&a.seaTurns>0&&c.World.State("region-ajaccio").owner=="austria","Landing captures on victory or starts a real return voyage on defeat");
            check(c.Adopt(clone(c),out why),"Resolved landing saves: "+why);
        }
        {
            var c=fresh();var a=AtPort(c);c.World.State("region-ajaccio").owner="france";c.World.State("region-genoa").owner="austria";
            c.Save.playerFaction="austria";c.World.FactionState("austria").treasury=1000;
            check(c.CommissionBlockade("austria","region-genoa",a.regionId,out why),"Enemy commissions connected blockade");
            c.Save.playerFaction="france";
            check(c.Blockaded("france",a.regionId)&&!c.Sail("france",a.id,"region-ajaccio",out _),"Blockade stops hostile departures");
            check(c.Adopt(clone(c),out why),"Blockade persists: "+why);a=c.Army(a.id);
            int funds=c.World.FactionState("france").treasury;
            check(c.BreakBlockade("france",a.regionId,out why)&&c.World.FactionState("france").treasury==funds-200,"Paid relief removes blockade");
            check(!c.BreakBlockade("france",a.regionId,out _)&&c.Sail("france",a.id,"region-ajaccio",out _),"Relief cannot double charge and reopens transports");
            check(c.ReturnTransport("france",a.id,out _)&&!CountryCampaign.AtSea(a)&&a.movement==0,"Immediate cancellation restores port but no movement or charter refund");
        }
        {
            var c=fresh();var a=c.Save.armies.First(x=>x.owner=="france");
            string target="region-brussels",origin="region-lille";
            c.World.State(target).owner="austria";c.World.State(origin).owner="france";a.regionId=origin;a.movement=2000;
            var support=c.Save.armies.First(x=>x.owner=="france"&&x.id!=a.id);support.regionId=origin;support.movement=2000;
            check(c.Move("france",a.id,target,out why),"Siege fixture reaches battle choice");
            var e=c.Save.pending;
            check(e.attackerArmyIds.Contains(support.id)&&c.BattleUnits(e,true).Count>=a.units.Count+support.units.Count,"Nearby army contributes its actual formations");
            var bad=clone(c);bad.pending.attackerArmyIds.Add(a.id);check(!c.Adopt(bad,out _),"Duplicate attacker references rejected");
            check(c.Adopt(clone(c),out why),"Supporting armies save: "+why);a=c.Army(a.id);
            int xp=a.units.Sum(u=>u.progression.xp);
            check(c.Besiege(e.id,out why)&&c.Save.pending==null&&c.Besieged(target),"Player may besiege instead of immediate assault");
            check(a.units.Sum(u=>u.progression.xp)==xp&&c.World.State(target).owner=="austria","Establishing a siege grants neither XP nor ownership");
            check(!c.Move("france",a.id,target,out _)&&!c.Detach("france",a.id,new[]{a.units[0].id},out _,out _),"Siege commits its army until assault or lift");
            check(c.Adopt(clone(c),out why),"Persistent siege saves: "+why);
            check(c.AssaultSiege("france",c.Save.sieges.Single().id,out why)&&c.Save.pending!=null&&c.Save.sieges.Count==0,"Siege assault returns to battle choice");
            e=c.Save.pending;var survivors=c.BattleUnits(e,true).Concat(c.BattleUnits(e,false)).ToDictionary(u=>u.id,u=>Math.Max(1,u.soldiers-10));
            check(c.CommitTacticalBattle(e.id,true,survivors,out why)&&c.World.State(target).owner=="france","Siege victory captures region");
            check(c.Army(support.id).regionId==origin&&c.Army(support.id).movement==0,"Supporting force spends movement and stays in its origin");
            check(c.Adopt(clone(c),out why),"Reinforced siege battle saves: "+why);
        }
        {
            var c=fresh();Peace(c);c.Treaty("france","austria").war=true;
            foreach(var region in c.Owned("france").ToArray())region.owner="prussia";
            c.World.State("region-ajaccio").owner="france";c.World.State("region-genoa").owner="austria";
            foreach(var army in c.Save.armies.Where(x=>x.owner=="france"))army.regionId="region-paris";
            var invader=c.Save.armies.First(x=>x.owner=="austria");invader.regionId="region-genoa";invader.movement=2000;
            foreach(var u in invader.units)u.equipment=2;
            foreach(var p in c.Save.countries)p.lastPlanningTurn=c.Save.world.turn;
            foreach(var force in c.Save.armies)force.lastAIActionTurn=c.Save.world.turn;
            invader.lastAIActionTurn=0;
            check(c.EndTurn(out why)&&CountryCampaign.AtSea(invader)&&invader.seaTarget=="region-ajaccio","AI charters transport to an otherwise unreachable island");
            check(c.Adopt(clone(c),out why),"AI transport saves: "+why);
            foreach(var p in c.Save.countries)p.lastPlanningTurn=c.Save.world.turn;
            foreach(var force in c.Save.armies)force.lastAIActionTurn=c.Save.world.turn;
            c.Army(invader.id).lastAIActionTurn=0;
            check(c.EndTurn(out why)&&c.Save.pending?.amphibious==true&&c.Save.pending.defender=="france","AI landing interrupts for player battle choice");
            check(c.Adopt(clone(c),out why),"Interrupted AI landing saves: "+why);
        }
        {
            var c=fresh();Peace(c);c.Treaty("france","austria").war=true;
            string target="region-brussels",origin="region-lille";c.World.State(target).owner="austria";
            c.World.State(target).buildings[0]=new RegionBuilding{kind="Barracks",level=1};c.World.State(target).economy.materials=500;
            c.Save.playerFaction="austria";check(c.Recruit("austria",target,out why),"Besieged recruitment fixture");c.Save.playerFaction="france";
            var q=c.Save.recruitment.Last();int remaining=q.turnsRemaining;
            var army=c.Save.armies.First(x=>x.owner=="france");army.regionId=origin;army.movement=2000;
            check(c.Move("france",army.id,target,out why)&&c.Besiege(c.Save.pending.id,out why),"Attrition siege begins");
            int men=army.Soldiers;
            foreach(var p in c.Save.countries)p.lastPlanningTurn=c.Save.world.turn;
            foreach(var force in c.Save.armies)force.lastAIActionTurn=c.Save.world.turn;
            Turn(c);
            check(c.Save.sieges.Single().turns==1&&army.Soldiers<men&&q.turnsRemaining==remaining,"Siege advances, costs troops, and pauses recruitment");
            check(c.Adopt(clone(c),out why),"Advanced siege saves: "+why);
            check(c.LiftSiege("france",c.Save.sieges.Single().id,out why)&&!c.Besieged(target),"Player may lift an ongoing siege");
        }
        {
            var c=fresh();string home="region-paris";c.World.State(home).owner="france";
            c.World.State(home).buildings[0]=new RegionBuilding{kind="Barracks",level=1};c.World.State(home).economy.materials=500;
            int funds=c.World.FactionState("france").treasury,enlisted=c.World.State(home).economy.enlisted;
            check(c.Recruit("france",home,out why),"Recruitment cancellation fixture");var q=c.Save.recruitment.Last();
            check(!c.CancelRecruitment("austria",q.id,out _),"Foreign recruitment cancellation denied");
            check(c.CancelRecruitment("france",q.id,out why)&&c.World.FactionState("france").treasury==funds&&c.World.State(home).economy.enlisted==enlisted,"Immediate cancellation refunds cost and releases manpower");
            check(!c.CancelRecruitment("france",q.id,out _),"Cancelled order cannot refund twice");
            var legacy=clone(c);legacy.sieges=null;legacy.blockades=null;check(c.Adopt(legacy,out why),"Legacy saves normalize missing operations lists");
            var bad=clone(c);bad.armies[0].seaTarget="region-ajaccio";bad.armies[0].seaTurns=-1;check(!c.Adopt(bad,out _),"Malformed voyage rejected");
        }
    }
}
