using System;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        void PlanCampaignStrategy(string actor)
        {
            var c=Progress(actor);c.strategicPlanTurn=Save.world.turn;
            foreach(var province in Owned(actor).Where(r=>r.economy.unrest>=65))
            {
                if(Politics(province.regionId).policy!="Reconcile")SetOccupationPolicy(actor,province.regionId,"Reconcile",out _);
                if(World.FactionState(actor).treasury>Balance.reserveBudget+150)ProvincialRelief(actor,province.regionId,out _);
            }
            if(IsRepublic&&actor=="france"&&Save.story.stability<45&&World.FactionState(actor).treasury>Balance.reserveBudget+200)FundRepublicRelief(out _);
            // Avoid fighting every enemy indefinitely while spending all available money elsewhere.
            if(Save.countries.Count(p=>AtWar(actor,p.factionId))>=3)
            {
                foreach(var enemy in Save.countries.Where(p=>AtWar(actor,p.factionId)).OrderByDescending(p=>Power(p.factionId)).ToArray())
                {
                    bool signed=false;
                    foreach(int payment in new[]{0,150,300})
                        if(World.FactionState(actor).treasury>=Balance.reserveBudget+payment&&NegotiatePeace(actor,enemy.factionId,payment,"","",4,out _)){signed=true;break;}
                    if(signed)break;
                }
            }
            var siege=Save.sieges.Where(s=>s.defender==actor).OrderBy(s=>s.regionId,StringComparer.Ordinal).FirstOrDefault();
            if(siege!=null){c.strategicTarget=Army(siege.armyId).regionId;c.strategicIntent="Relieve siege";return;}
            // Coalitions share frontier priorities, while each member retains legal routes and budgets.
            var sharedEnemy=Save.countries.Where(p=>AtWar(actor,p.factionId)).OrderByDescending(p=>Save.countries.Count(ally=>AtWar(ally.factionId,p.factionId))).ThenBy(p=>p.factionId,StringComparer.Ordinal).FirstOrDefault();
            if(sharedEnemy==null){c.strategicTarget=c.coalitionTarget="";c.strategicIntent="Secure provinces";return;}
            var ownArmies=Save.armies.Where(a=>a.owner==actor&&!AtSea(a)).ToArray();
            var coalition=Save.armies.Where(a=>!AtSea(a)&&AtWar(a.owner,sharedEnemy.factionId)&&!AtWar(actor,a.owner)).ToArray();
            var priorities=Owned(sharedEnemy.factionId).Select(r=>new{region=r,
                supporters=coalition.Where(a=>Route(a.owner,a.regionId,r.regionId)!=null).Select(a=>a.owner).Distinct().Count()})
                .Where(x=>x.supporters>0).OrderByDescending(x=>x.supporters*200+(x.region.regionId=="region-paris"?500:0)+Buildings(x.region.regionId,"Barracks")*100+
                    World.Economy.Forecast(x.region).taxes-KnownDefense(actor,x.region.regionId)*.05f).ThenBy(x=>x.region.regionId,StringComparer.Ordinal).ToArray();
            c.coalitionTarget=priorities.FirstOrDefault()?.region.regionId??"";
            var objective=priorities.Select(x=>x.region).FirstOrDefault(r=>ownArmies.Any(a=>Route(actor,a.regionId,r.regionId)!=null));
            c.strategicTarget=objective?.regionId??"";c.strategicIntent=objective==null?"Prepare naval expedition":"Concentrate offensive";
            foreach(var r in Owned(actor).Where(r=>InRevolt(r.regionId)||r.economy.unrest>=70).OrderByDescending(r=>r.economy.unrest))
                if(World.FactionState(actor).treasury>Balance.reserveBudget+150)ProvincialRelief(actor,r.regionId,out _);
        }
        bool ThinkStrategicArmy(string actor,CampaignArmy army)
        {
            var own=Owned(actor);var local=World.State(army.regionId);var funds=World.FactionState(actor);
            var visible=Save.armies.Where(a=>AtWar(actor,a.owner)&&CanObserve(a,actor)&&!AtSea(a)).ToArray();
            bool threatened=visible.Any(a=>a.regionId==army.regionId||graph[army.regionId].ContainsKey(a.regionId));
            if(local.owner==actor&&InRevolt(army.regionId))
            {if(army.stance=="March")SetStance(actor,army.id,"Guard",out _);return true;}
            if(army.Soldiers<army.units.Count*Balance.unitSize*.6f)
            {
                if(local.owner==actor&&Recover(actor,army.id,out _))return true;
                var fallback=own.Select(r=>new{r,route=Route(actor,army.regionId,r.regionId)})
                    .Where(x=>x.route!=null&&x.route.distance>0&&!visible.Any(e=>e.regionId==x.r.regionId||graph[x.r.regionId].ContainsKey(e.regionId)))
                    .OrderBy(x=>x.route.distance).FirstOrDefault();
                if(fallback!=null)return March(actor,army.id,fallback.r.regionId,out _);
            }
            var relief=Save.sieges.Where(s=>s.defender==actor).Select(s=>Army(s.armyId)).Where(a=>a!=null)
                .Select(a=>new{army=a,route=Route(actor,army.regionId,a.regionId)}).Where(x=>x.route!=null).OrderBy(x=>x.route.distance).FirstOrDefault();
            if(relief!=null&&ArmyPower(army)>=ArmyPower(relief.army)*.85f){var p=Position(relief.army);return MarchTo(actor,army.id,p.lon,p.lat,out _,relief.army.id);}
            var allies=Save.armies.Where(a=>a.id!=army.id&&a.owner==actor&&!AtSea(a)).ToArray();
            // Keep a real paid regiment at important recruitment centres; never conjure a garrison.
            if(local.owner==actor&&AtSettlement(army)&&Buildings(local.regionId,"Barracks")>0&&army.units.Count>=5&&army.lastGarrisonTurn!=Save.world.turn&&
                !allies.Any(a=>a.regionId==army.regionId)&&!threatened)
            {
                if(Detach(actor,army.id,new[]{army.units.Last().id},out string id,out _))
                {var guard=Army(id);if(!guard.customName)guard.name="Garrison of "+Place(local.regionId);SetStance(actor,id,"Guard",out _);guard.lastAIActionTurn=Save.world.turn;army.lastGarrisonTurn=Save.world.turn;}
            }
            if(army.stance!="March"&&army.units.Count<=3&&local.owner==actor&&Buildings(local.regionId,"Barracks")>0)return true;
            if(threatened&&local.owner==actor)
            {
                float danger=visible.Where(a=>a.regionId==army.regionId||graph[army.regionId].ContainsKey(a.regionId)).Sum(ArmyPower);
                float friendly=ArmyPower(army)+allies.Where(a=>a.regionId==army.regionId).Sum(ArmyPower);
                if(danger>friendly*.9f&&!Besieged(army.regionId))
                {if(army.stance!="Fortify")SetStance(actor,army.id,"Fortify",out _);return true;}
            }
            string target=Progress(actor).strategicTarget;
            if(target!=""&&AtWar(actor,World.State(target).owner)&&Route(actor,army.regionId,target)!=null)
            {
                float ownPower=ArmyPower(army),defense=KnownDefense(actor,target);
                var partner=allies.Where(a=>a.units.Count>=3&&!Committed(a)&&(a.stance=="March"||a.stance=="Guard"&&a.units.Count>3))
                    .Select(a=>new{a,route=Route(actor,army.regionId,a.regionId)}).Where(x=>x.route!=null).OrderBy(x=>x.route.distance).FirstOrDefault();
                if(ownPower<defense*1.15f&&partner!=null&&ownPower+ArmyPower(partner.a)>=defense*1.15f)
                {
                    if(ForcesCanMeet(army,partner.a)&&army.units.Count+partner.a.units.Count<=MaxArmyRegiments)
                        return Transfer(actor,army.id,partner.a.id,army.units.Select(u=>u.id).ToArray(),out _);
                    // The larger force waits; the smaller moves to it, avoiding reciprocal marching.
                    if(army.Soldiers<=partner.a.Soldiers){var p=Position(partner.a);return MarchTo(actor,army.id,p.lon,p.lat,out _,partner.a.id);}
                    if(army.stance=="March")SetStance(actor,army.id,"Guard",out _);return true;
                }
                if(ownPower>=defense*1.15f)return March(actor,army.id,target,out _);
            }
            return false;
        }
    }
}
