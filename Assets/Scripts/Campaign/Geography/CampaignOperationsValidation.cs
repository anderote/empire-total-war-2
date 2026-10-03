using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        bool ValidVoyage(CampaignArmy a)
        {
            // Missing fields in older inline saves are an unembarked army.
            a.seaFrom=a.seaFrom??"";a.seaTarget=a.seaTarget??"";
            if(!AtSea(a))return a.seaFrom==""&&a.seaTurns==0&&a.seaDuration==0;
            return HasPort(a.regionId)&&SeaRoute(a.seaFrom,a.seaTarget)!=null&&a.seaDuration>=1&&a.seaDuration<=100&&
                a.seaTurns>=0&&a.seaTurns<=a.seaDuration&&a.movement==0&&a.marchTarget==""&&a.marchProgress==0&&!a.units.Any(u=>u==null)&&!TrainingBusy(a);
        }
        bool ValidOperations(CountryCampaignSave candidate,HashSet<string> ids)
        {
            var besiegers=new HashSet<string>();var cities=new HashSet<string>();
            foreach(var s in candidate.sieges)
            {
                if(s==null||!ids.Add(s.id)||!Active(s.regionId)||s.turns<0||s.turns>20||!candidate.countries.Any(c=>c.factionId==s.defender)||!besiegers.Add(s.armyId)||!cities.Add(s.regionId))return false;
                var a=candidate.armies.Find(x=>x.id==s.armyId);
                if(a==null||AtSea(a)||a.owner==s.defender||!SiegeInRange(a,s.regionId))return false;
            }
            var contracts=new HashSet<string>();
            foreach(var b in candidate.blockades)
                if(b==null||!ids.Add(b.id)||!candidate.countries.Any(c=>c.factionId==b.owner)||SeaRoute(b.origin,b.regionId)==null||b.turns<1||b.turns>3||!contracts.Add(b.owner+"/"+b.regionId))return false;
            return true;
        }
        bool ValidEncounterSupport(CountryCampaignSave candidate,CampaignEncounter e)
        {
            if(!Active(e.regionId)||e.siegeTurns<0||e.siegeTurns>20||e.attackerArmyIds==null||!e.attackerArmyIds.Contains(e.attackerArmyId)||
                e.attackerArmyIds.Distinct().Count()!=e.attackerArmyIds.Count||e.defenderArmyIds==null||e.attackerArmyIds.Count>MaxReinforcingArmies+1||e.defenderArmyIds.Count>MaxReinforcingArmies+1||!e.mapPositionKnown||!CampaignMovementTerrain.Valid(EncounterPosition(e)))return false;
            bool Nearby(CampaignArmy a)=>CampaignMovementTerrain.Distance(Position(a),EncounterPosition(e))<=ReinforcementRangeKm;
            foreach(var id in e.attackerArmyIds)
            {
                var a=candidate.armies.Find(x=>x.id==id);if(a==null||a.owner!=e.attacker)return false;
                if(id==e.attackerArmyId){if(e.amphibious!=AtSea(a)||e.amphibious&&(a.seaTarget!=e.regionId||a.seaTurns!=0))return false;}
                else if(e.amphibious||AtSea(a)||!Nearby(a))return false;
            }
            return e.defenderArmyIds.All(id=>candidate.armies.Any(a=>a.id==id&&a.owner==e.defender&&!AtSea(a)&&Nearby(a)));
        }
    }
}
