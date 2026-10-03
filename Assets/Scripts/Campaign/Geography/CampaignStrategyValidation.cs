using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        bool ValidStrategy(CountryCampaignSave c)
        {
            if(c.world.regions==null)return false;
            c.politics=c.politics??new List<CampaignProvincePolitics>();c.history=c.history??new List<CampaignHistoryEntry>();c.turnSummary=c.turnSummary??new CampaignTurnSummary();
            if(c.politics.Count==0)
                foreach(var id in c.activeRegions)c.politics.Add(new CampaignProvincePolitics{regionId=id,loyalist=c.world.regions.FirstOrDefault(r=>r!=null&&r.regionId==id)?.owner});
            if(c.politics.Count!=c.activeRegions.Count||c.politics.Any(p=>p==null)||c.politics.Select(p=>p.regionId).Distinct().Count()!=c.politics.Count)return false;
            foreach(var p in c.politics)
                if(!Active(p.regionId)||!c.countries.Any(x=>x.factionId==p.loyalist)||!new[]{"Reconcile","Garrison","Exploit"}.Contains(p.policy)||p.rebels<0||p.rebels>2000||p.unrestTurns<0||p.unrestTurns>10||p.lastReliefTurn<0||p.lastReliefTurn>c.world.turn)return false;
            foreach(var a in c.armies)
            {
                if(string.IsNullOrEmpty(a.stance))a.stance="March";a.commanderTrait=a.commanderTrait??"";
                if(!new[]{"March","Guard","Fortify","Ambush"}.Contains(a.stance)||a.commanderXP<0||a.commanderXP>10000||a.commanderWins<0||a.commanderWins>c.resolvedEncounters.Count||a.commanderWounds<0||a.commanderWounds>2||
                    !new[]{"","Tactician","Logistician","Inspiring"}.Contains(a.commanderTrait)||a.lastInterceptTurn<0||a.lastInterceptTurn>c.world.turn||a.lastGarrisonTurn<0||a.lastGarrisonTurn>c.world.turn)return false;
                if(a.stance!="March"&&(AtSea(a)||a.marchTarget!=""||a.marchProgress>0))return false;
            }
            foreach(var p in c.countries)
            {
                p.strategicTarget=p.strategicTarget??"";p.coalitionTarget=p.coalitionTarget??"";p.strategicIntent=p.strategicIntent??"";
                if(p.strategicTarget!=""&&!Active(p.strategicTarget)||p.coalitionTarget!=""&&!Active(p.coalitionTarget)||p.strategicIntent.Length>100||p.strategicPlanTurn<0||p.strategicPlanTurn>c.world.turn)return false;
            }
            if(c.treaties.Any(t=>t==null||t.warScoreA<-100||t.warScoreA>100))return false;
            if(c.history.Count>200||c.history.Any(h=>h==null||h.turn<1||h.turn>c.world.turn||h.text==null||h.text.Length>4000||string.IsNullOrEmpty(h.kind)||h.kind.Length>60||h.regionId==null||h.regionId!=""&&!Active(h.regionId)))return false;
            var s=c.turnSummary;s.notices=s.notices??new List<string>();
            if(s.turn<0||s.turn>c.world.turn||s.citiesBefore<0||s.citiesBefore>c.activeRegions.Count||s.citiesAfter<0||s.citiesAfter>c.activeRegions.Count||s.troopsBefore<0||s.troopsAfter<0||s.notices.Count>40||s.notices.Any(n=>n==null||n.Length>4000))return false;
            var e=c.pending;
            if(e!=null&&(e.ambush&&!e.fieldOnly||e.fieldOnly&&(e.amphibious||e.interceptedArmyId!=e.attackerArmyId||string.IsNullOrEmpty(e.defenderArmyId)||(e.garrison?.Count??0)>0)))return false;
            return true;
        }
    }
}
