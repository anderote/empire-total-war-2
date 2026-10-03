using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EmpireCampaign.Geography;

public static class CampaignEncounterChecks
{
    public static void Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        {
            var c=fresh();var saved=clone(c);saved.pending=new CampaignEncounter();
            check(c.Adopt(saved,out _)&&c.Save.pending==null,"Unity inline empty encounter placeholder restores as absent");
            saved=clone(c);saved.pending=new CampaignEncounter{id="encounter-999"};
            check(!c.Adopt(saved,out _),"Partial encounter placeholder is not discarded");
            saved=clone(c);saved.phase="Encounter";saved.pending=new CampaignEncounter();
            check(!c.Adopt(saved,out _),"Empty active encounter remains invalid");
        }
        CampaignRoute Frontier(CountryCampaign c)
        {
            CampaignRoute best=null;
            foreach(var home in c.Owned("france"))foreach(var target in c.Owned("austria"))
            {var r=c.Route("france",home.regionId,target.regionId);if(r!=null&&(best==null||r.distance<best.distance))best=r;}
            return best;
        }
        {
            var c=fresh();var route=Frontier(c);var a=c.Save.armies.First(x=>x.owner=="france");var d=c.Save.armies.First(x=>x.owner=="austria");
            string target=route.regions.Last();a.regionId=route.regions[route.regions.Count-2];a.movement=2000;
            foreach(var other in c.Save.armies.Where(x=>x.owner=="austria"))other.regionId=c.Owned("austria").First(r=>r.regionId!=target).regionId;
            d.regionId=target;d.movement=400;
            foreach(var b in c.World.State(target).buildings)if(b.kind=="Fortress")b.kind="Empty";
            check(c.Move("france",a.id,target,out _),"Defensive retreat fixture creates field encounter");
            c.Save.playerFaction="austria";
            check(c.CanWithdraw(out _),"Mobile defending force can choose a safe retreat");
            d.movement=0;check(!c.CanWithdraw(out _),"Exhausted defenders cannot repeatedly evade battle");d.movement=400;
            int men=d.Soldiers,xp=d.units.Sum(u=>u.progression.xp);string id=c.Save.pending.id;
            check(c.Withdraw(id,out _)&&d.regionId!=target&&d.movement==0&&d.Soldiers<men,"Defensive retreat relocates survivors and spends movement with straggler losses");
            check(c.World.State(target).owner=="france"&&c.Save.pending==null&&d.units.Sum(u=>u.progression.xp)==xp,"Withdrawing yields the region without awarding battle XP");
            check(!c.Withdraw(id,out _)&&c.Adopt(clone(c),out _),"Retreat commits once and remains saveable");
        }
        foreach(int fort in new[]{0,1,2})foreach(bool standingArmy in new[]{false,true})foreach(bool win in new[]{false,true})
        {
            var c=fresh();var route=Frontier(c);check(route!=null,"Integration fixture has a real land frontier");
            string region=route.regions.Last(),origin=route.regions[route.regions.Count-2];
            var attacker=c.Save.armies.First(a=>a.owner=="france");var defender=c.Save.armies.First(a=>a.owner=="austria");
            attacker.regionId=origin;attacker.movement=2000;
            foreach(var a in c.Save.armies.Where(a=>a.owner=="austria"))
                a.regionId=c.Owned("austria").First(r=>r.regionId!=region).regionId;
            if(standingArmy)foreach(var army in c.Save.armies.Where(a=>a.owner=="austria"))army.regionId=region;
            var state=c.World.State(region);
            if(fort>0)state.buildings[0]=new RegionBuilding{kind="Fortress",level=fort};
            state.buildings[1]=new RegionBuilding{kind="Farm",level=1,turnsRemaining=2,workRemaining=2,totalWork=2,paidCost=200};
            check(c.Move("france",attacker.id,region,out _),"Attack crosses a legal frontier");
            var e=c.Save.pending;check(e!=null&&c.CanFightManually(),"Field, militia and fortified encounters can all deploy");
            if(fort>0||!standingArmy)
            {c.Save.playerFaction="austria";check(!c.CanWithdraw(out _),"Local garrison cannot abandon a fortified or militia battle");c.Save.playerFaction="france";}
            int expectedMilitia=!standingArmy||fort>0?250+fort*200:0;
            check(e.garrison.Sum(u=>u.soldiers)==expectedMilitia,"Fort level and standing defence determine militia strength");
            check(e.defenderMen==c.BattleUnits(e,false).Sum(u=>u.soldiers),"Encounter preview matches actual defenders");
            if(standingArmy)check(e.defenderArmyIds.Count==c.Save.armies.Count(a=>a.owner=="austria"),"All regional reinforcements deploy once");
            check(c.Adopt(clone(c),out _),"Pending siege/militia encounter survives save and restore");e=c.Save.pending;
            attacker=c.Army(e.attackerArmyId);
            var units=c.BattleUnits(e,true).Concat(c.BattleUnits(e,false)).ToList();
            var survivors=units.ToDictionary(u=>u.id,u=>u.soldiers-10);
            var bad=new Dictionary<string,int>(survivors);bad.Remove(units[0].id);
            int populationBefore=c.World.State(units[0].homeRegionId).economy.population;
            check(!c.CommitTacticalBattle(e.id,win,bad,out _)&&c.Save.pending==e&&c.World.State(units[0].homeRegionId).economy.population==populationBefore,
                "Invalid partial survivor report is atomic");
            bad=new Dictionary<string,int>(survivors);foreach(var u in c.BattleUnits(e,true))bad[u.id]=0;
            check(!c.CommitTacticalBattle(e.id,true,bad,out _),"An annihilated attacker cannot capture territory");
            var duplicate=clone(c);duplicate.pending.defenderArmyIds.AddRange(duplicate.pending.defenderArmyIds);
            if(e.defenderArmyIds.Count>0)check(!c.Adopt(duplicate,out _),"Duplicate reinforcement army cannot inflate deployment");
            string id=e.id;
            check(c.CommitTacticalBattle(id,win,survivors,out _),"Exact tactical result commits");
            check(attacker.units.All(u=>u.soldiers==survivors[u.id]),"Attacker survivors do not suffer a second autoresolve");
            if(standingArmy)foreach(var armyId in e.defenderArmyIds)
            {
                var remaining=c.Army(armyId);
                if(remaining!=null)
                {
                    check(remaining.units.All(u=>u.soldiers==survivors[u.id]),"Surviving reinforcements retain exact tactical casualties");
                    check(win?remaining.regionId!=region&&remaining.movement==0:remaining.regionId==region,"Defenders retreat on loss and hold on victory");
                }
            }
            check(c.World.State(region).owner==(win?"france":"austria"),"Territory changes only after an attacking victory");
            check(win?c.World.State(region).economy.occupationTurns==3:c.World.State(region).economy.occupationTurns==0,"Capture applies occupation only to conquered territory");
            check(win?state.buildings[1].kind=="Empty"||c.World.State(region).buildings[1].kind=="Empty":c.World.State(region).buildings[1].turnsRemaining==2,
                "Conquest cancels unfinished enemy construction; a failed assault leaves it alone");
            if(fort>0)check(c.Buildings(region,"Fortress")==fort,"Completed fort survives a change of owner");
            check(c.Save.pending==null&&c.Save.phase=="Player"&&c.Save.resolvedEncounters.Count(x=>x==id)==1,"Encounter clears and commits exactly once");
            check(!c.CommitTacticalBattle(id,win,survivors,out _),"Replay cannot award duplicate territory or casualties");
            var manpower=c.Save.activeRegions.ToDictionary(r=>r,r=>0);
            foreach(var army in c.Save.armies)foreach(var u in army.units)manpower[u.homeRegionId]+=u.soldiers;
            foreach(var q in c.Save.recruitment)manpower[q.regionId]+=q.soldiers;
            foreach(var entry in manpower)check(c.World.State(entry.Key).economy.enlisted==entry.Value,"Militia/capture conserve enlisted manpower");
            check(c.Adopt(clone(c),out _),"Resolved encounter restores without orphaned armies or militia");
        }
        {
            var c=fresh();var route=c.Owned("austria").SelectMany(h=>c.Owned("france").Select(t=>c.Route("austria",h.regionId,t.regionId)))
                .Where(r=>r!=null).OrderBy(r=>r.distance).First();
            string target=route.regions.Last(),origin=route.regions[route.regions.Count-2];
            foreach(var a in c.Save.armies.Where(a=>a.owner=="france"))a.regionId=c.Owned("france").First(r=>r.regionId!=target).regionId;
            var attackers=c.Save.armies.Where(a=>a.owner=="austria").ToArray();
            foreach(var a in attackers){a.regionId=origin;a.movement=2000;a.marchTarget=target;}
            // Keep the second force outside supporting range so this exercises two separate interruptions.
            var secondRoute=c.Owned("austria").Select(r=>c.Route("austria",r.regionId,target))
                .Where(r=>r!=null&&r.regions.Count>2).OrderBy(r=>r.distance).First();
            attackers[1].regionId=secondRoute.regions[0];attackers[1].marchNext=secondRoute.regions[1];attackers[1].marchProgress=1;
            check(c.EndTurn(out _)&&c.Save.pending?.attacker=="austria","Enemy march pauses the turn for player battle choice");
            check(c.Save.countries[c.Save.aiIndex].factionId=="austria","AI retains the current faction while its battle is pending");
            int treasury=c.World.FactionState("austria").treasury,queues=c.Save.recruitment.Count(q=>q.owner=="austria");
            string first=c.Save.pending.attackerArmyId;check(c.Adopt(clone(c),out _),"Interrupted AI planning and army markers persist");
            var e=c.Save.pending;var survivors=c.BattleUnits(e,true).Concat(c.BattleUnits(e,false)).ToDictionary(u=>u.id,u=>Math.Max(1,u.soldiers-20));
            bool committed=c.CommitTacticalBattle(e.id,false,survivors,out _);
            check(committed&&c.Save.pending?.attacker=="austria"&&c.Save.pending.attackerArmyId!=first,"Remaining army of the same AI faction still gets its turn after a battle");
            check(c.World.FactionState("austria").treasury==treasury&&c.Save.recruitment.Count(q=>q.owner=="austria")==queues,"Resuming AI does not repeat purchases or recruitment");
            e=c.Save.pending;survivors=c.BattleUnits(e,true).Concat(c.BattleUnits(e,false)).ToDictionary(u=>u.id,u=>Math.Max(1,u.soldiers-20));
            check(c.CommitTacticalBattle(e.id,false,survivors,out _)&&c.Save.world.turn==2&&!c.Save.roundRunning&&c.CanOrder("france"),"Two interrupted battles complete one global turn and return control");
            check(c.Adopt(clone(c),out _),"Resumed AI round remains saveable");
        }
        {
            var c=fresh();var route=Frontier(c);var a=c.Save.armies.First(x=>x.owner=="france");
            string home=a.units[0].homeRegionId;
            foreach(var u in a.units)c.World.State(u.homeRegionId).economy.enlisted-=u.soldiers;
            a.units.RemoveRange(1,a.units.Count-1);a.units[0].soldiers=1;c.World.State(home).economy.enlisted++;
            a.regionId=route.regions[route.regions.Count-2];a.movement=2000;
            check(c.Move("france",a.id,route.regions.Last(),out _),"One-man withdrawal fixture creates encounter");
            string id=c.Save.pending.id;check(c.Withdraw(id,out _)&&!c.Save.armies.Any(x=>x.id==a.id),"Last-man withdrawal removes the empty army");
            check(c.Adopt(clone(c),out _),"Withdrawal save remains valid");
        }
        {
            var c=fresh();CampaignRoute route=null;
            foreach(var from in c.Owned("france"))foreach(var to in c.Owned("france"))
            {var candidate=c.Route("france",from.regionId,to.regionId);if(candidate!=null&&candidate.regions.Count>=3){route=candidate;break;}}
            check(route!=null,"Transit fixture has an intermediate friendly region");
            string blocked=route.regions[1];c.Save.armies.First(a=>a.owner=="austria").regionId=blocked;
            var alternate=c.Route("france",route.regions[0],route.regions.Last());
            check(alternate==null||!alternate.regions.Skip(1).Take(alternate.regions.Count-2).Contains(blocked),"Enemy army blocks free transit even on friendly territory");
        }
    }
}
