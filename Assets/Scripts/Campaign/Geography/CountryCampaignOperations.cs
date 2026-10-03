using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        public const int ReinforcementRangeKm=180;
        public bool UnderSiegeOrders(CampaignArmy army)=>army!=null&&Save.sieges.Any(s=>s.armyId==army.id);
        public bool Committed(CampaignArmy army)=>Busy(army)||UnderSiegeOrders(army);
        public bool Besieged(string region)=>Save.sieges.Any(s=>s.regionId==region&&Army(s.armyId)!=null&&AtWar(Army(s.armyId).owner,World.State(region).owner));
        public IEnumerable<CampaignArmy> EncounterArmies(CampaignEncounter e,bool attacker)
        {
            if(e==null)return Enumerable.Empty<CampaignArmy>();
            var ids=attacker?(e.attackerArmyIds==null||e.attackerArmyIds.Count==0?new List<string>{e.attackerArmyId}:e.attackerArmyIds):e.defenderArmyIds;
            return ids.Select(Army).Where(a=>a!=null);
        }
        bool CanSupport(CampaignArmy army,string region)=>CanSupport(army,CityPosition(region));
        bool CanSupport(CampaignArmy army,GeoPoint position)
        {
            if(army==null||army.Soldiers<=0||AtSea(army)||Busy(army)||UnderSiegeOrders(army))return false;
            float distance=CampaignMovementTerrain.Distance(Position(army),position);if(distance>ReinforcementRangeKm)return false;
            float cost=Terrain.SegmentCost(Position(army),position,r=>CanTraverse(army.owner,r),out _);
            return !float.IsInfinity(cost)&&(army.movement-army.movementDebt>=cost||army.stance!="March"||distance<=MergeRangeKm);
        }
        void CreateEncounter(CampaignArmy attacker,string destination,bool amphibious=false,int siegeTurns=0,CampaignArmy interceptor=null,bool siegeAssault=false)
        {
            var location=interceptor!=null?Position(attacker):CityPosition(destination);
            var enemy=interceptor??Save.armies.Where(a=>!AtSea(a)&&AtWar(attacker.owner,a.owner)&&a.Soldiers>0&&CampaignMovementTerrain.Distance(Position(a),location)<=SettlementRangeKm)
                .OrderBy(a=>CampaignMovementTerrain.Distance(Position(a),location)).ThenBy(a=>a.id,StringComparer.Ordinal).FirstOrDefault();
            string defender=enemy?.owner??World.State(destination).owner;
            var e=new CampaignEncounter{id=Id("encounter-"),attackerArmyId=attacker.id,defenderArmyId=enemy?.id??"",regionId=destination,
                attacker=attacker.owner,defender=defender,amphibious=amphibious,siegeTurns=siegeTurns,fieldOnly=interceptor!=null,ambush=interceptor?.stance=="Ambush",interceptedArmyId=interceptor!=null?attacker.id:"",
                mapPositionKnown=true,longitude=location.lon,latitude=location.lat};
            e.attackerArmyIds.Add(attacker.id);
            if(!amphibious)e.attackerArmyIds.AddRange(Save.armies.Where(a=>a.id!=attacker.id&&a.owner==attacker.owner&&CanSupport(a,location))
                .OrderBy(a=>CampaignMovementTerrain.Distance(Position(a),location)).ThenBy(a=>a.id,StringComparer.Ordinal).Take(MaxReinforcingArmies).Select(a=>a.id));
            if(enemy!=null)e.defenderArmyIds.Add(enemy.id);
            e.defenderArmyIds.AddRange(Save.armies.Where(a=>a!=enemy&&a.owner==defender&&CanSupport(a,location))
                .OrderBy(a=>CampaignMovementTerrain.Distance(Position(a),location)).ThenBy(a=>a.id,StringComparer.Ordinal).Take(enemy==null?MaxReinforcingArmies+1:MaxReinforcingArmies).Select(a=>a.id));
            e.attackerMen=EncounterArmies(e,true).Sum(a=>a.Soldiers);e.attackerPower=EncounterArmies(e,true).Sum(ArmyPower);
            e.defenderMen=EncounterArmies(e,false).Sum(a=>a.Soldiers);e.defenderPower=EncounterArmies(e,false).Sum(ArmyPower);
            EnsureGarrison(e);e.defenderPower=Math.Max(1,e.defenderPower*(1+(e.fieldOnly?0:Buildings(destination,"Fortress")*.20f)));
            Save.pending=e;
            if(e.attacker!=Save.playerFaction&&!siegeAssault&&siegeTurns==0&&!amphibious&&!e.fieldOnly&&Buildings(destination,"Fortress")>0&&
                !Besieged(destination)&&SiegeInRange(attacker,destination))
            {EstablishSiege(e);return;}
            if(autoplay||(e.attacker!=Save.playerFaction&&e.defender!=Save.playerFaction))ResolveInternal(e.id);
            else Save.phase="Encounter";
        }
        public bool CanBesiege(out string reason)
        {
            reason="Only a land assault on an enemy-owned region can become a siege.";var e=Save.pending;
            if(e==null||e.attacker!=Save.playerFaction&&e.attacker!=aiActor||e.amphibious||e.fieldOnly||World.State(e.regionId).owner!=e.defender||Besieged(e.regionId))return false;
            var a=Army(e.attackerArmyId);if(a==null||!SiegeInRange(a,e.regionId)||UnderSiegeOrders(a))return false;
            reason="Maintain the siege instead of assaulting. Recruitment and land freight stop; defenders lose strength each turn. Your army also suffers siege attrition.";return true;
        }
        public bool Besiege(string encounterId,out string reason)
        {
            if(!CanBesiege(out reason)||Save.pending.id!=encounterId)return false;
            var e=Save.pending;EstablishSiege(e);
            reason="Siege established. Use Operations to assault or lift it.";
            if(Save.roundRunning)RunAI();return true;
        }
        void EstablishSiege(CampaignEncounter e)
        {
            var a=Army(e.attackerArmyId);
            Save.sieges.Add(new CampaignSiege{id=Id("siege-"),armyId=a.id,regionId=e.regionId,defender=e.defender});
            a.movement=0;ClearMarch(a);
            Save.resolvedEncounters.Add(e.id);Save.pending=null;Save.phase=Save.roundRunning?"AI":"Player";
            Event(a.owner+" began a siege of "+e.regionId+".");
        }
        public bool AssaultSiege(string actor,string siegeId,out string reason)
        {
            reason="Siege is unavailable.";var siege=Save.sieges.Find(s=>s.id==siegeId);var army=siege==null?null:Army(siege.armyId);
            if(!CanOrder(actor)||army==null||army.owner!=actor||!AtWar(actor,World.State(siege.regionId).owner))return false;
            Save.sieges.Remove(siege);CreateEncounter(army,siege.regionId,false,siege.turns,siegeAssault:true);reason="Siege assault awaits battle resolution.";return true;
        }
        public bool LiftSiege(string actor,string siegeId,out string reason)
        {
            reason="Siege is unavailable.";var siege=Save.sieges.Find(s=>s.id==siegeId);var army=siege==null?null:Army(siege.armyId);
            if(!CanOrder(actor)||army==null||army.owner!=actor)return false;
            Save.sieges.Remove(siege);army.movement=0;reason="Siege lifted; the army remains at its camp.";return true;
        }
        void CleanSieges()
        {
            Save.sieges.RemoveAll(s=>Army(s.armyId)==null||Army(s.armyId).Soldiers<=0||AtSea(Army(s.armyId))||
                !SiegeInRange(Army(s.armyId),s.regionId)||World.State(s.regionId).owner!=s.defender||!AtWar(Army(s.armyId).owner,s.defender));
        }
        void AdvanceSieges()
        {
            CleanSieges();
            foreach(var siege in Save.sieges.ToArray())
            {
                var attacker=Army(siege.armyId);siege.turns=Math.Min(20,siege.turns+1);
                var state=World.State(siege.regionId);state.economy.food=Math.Max(0,state.economy.food*.6f);state.economy.unrest=Math.Min(100,state.economy.unrest+4);
                foreach(var defence in Save.armies.Where(a=>!AtSea(a)&&AtSettlement(a,siege.regionId)&&a.owner==siege.defender).ToArray())Casualties(defence,.06f);
                Casualties(attacker,.02f);Event("Siege of "+siege.regionId+": defenders lose supplies and strength.");
            }
            Save.armies.RemoveAll(a=>a.Soldiers<=0);CleanSieges();
        }
        void ContinuePlayerTravel()
        {
            foreach(var army in Save.armies.Where(a=>a.owner==Save.playerFaction).ToArray())
            {
                if(Save.pending!=null||Save.outcome!=""||Save.roundRunning)return;
                if(AtSea(army))LandTransport(army);
                else if(army.marchTarget!=""&&!UnderSiegeOrders(army))AdvanceMarch(army,out _);
            }
        }
        public bool CancelRecruitment(string actor,string orderId,out string reason)
        {
            reason="Recruitment order unavailable.";var q=Save.recruitment.Find(x=>x.id==orderId);
            if(!CanOrder(actor)||q==null||q.owner!=actor||World.State(q.regionId).owner!=actor)return false;
            int paid=q.paidCost>0?q.paidCost:q.role=="Militia"?120:q.role=="Rifle"?270:Balance.recruitCost;
            int refund=paid*q.turnsRemaining/Math.Max(1,q.totalTurns>0?q.totalTurns:Balance.trainingTurns);
            World.FactionState(actor).treasury+=refund;World.State(q.regionId).economy.enlisted-=q.soldiers;
            Save.recruitment.Remove(q);reason="Training cancelled: "+refund+" treasury refunded and manpower released. Issued materials are consumed.";return true;
        }
        public string OpeningAdvice
        {
            get
            {
                string actor=Save.playerFaction;
                if(IsRepublic&&Save.story.decision!="")return "A national decision is waiting. Open Nation before ending the turn.";
                if(Save.sieges.Any(s=>World.State(s.regionId).owner==actor))return "A city is besieged. Attack the besieging army's camp to relieve it, or move reinforcements nearby.";
                if(Save.armies.Any(a=>a.owner==actor&&AtSea(a)))return "Transports arrive after campaign turns. Check Operations for arrival, blockade or return orders.";
                if(Save.armies.Any(a=>a.owner==actor&&a.supply<.6f))return "Some armies are undersupplied. Return to friendly food stocks and build farms or granaries before advancing.";
                if(actor=="france"&&!Save.armies.Any(a=>a.owner==actor&&a.regionId=="region-paris"&&!AtSea(a)))return "Paris has no field army. Recruit there and keep a reserve while the northern, Rhine and Alpine armies cover the frontiers.";
                return "Concentrate nearby armies for support, inspect the battle roster before fighting, and keep enough treasury for recruitment, transport and replacements.";
            }
        }
    }
}
