using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    [Serializable] public sealed class CampaignProvincePolitics
    {
        public string regionId,loyalist,policy="Garrison";
        public int unrestTurns,rebels,lastReliefTurn;
    }
    [Serializable] public sealed class CampaignHistoryEntry
    {
        public int turn;public string kind,regionId,text;
    }
    [Serializable] public sealed class CampaignTurnSummary
    {
        public int turn,treasuryBefore,treasuryAfter,citiesBefore,citiesAfter,troopsBefore,troopsAfter;
        public bool complete;
        public List<string> notices=new List<string>();
    }
    public sealed class CampaignAlert
    {
        public string kind,text,regionId,armyId;public int priority;
    }
    public sealed partial class CountryCampaign
    {
        public bool SetStance(string actor,string armyId,string stance,out string reason)
        {
            reason="Stance requires a free stationary army with movement remaining.";var a=Army(armyId);
            if(!CanOrder(actor)||a==null||a.owner!=actor||Committed(a)||a.marchTarget!=""||!new[]{"March","Guard","Fortify","Ambush"}.Contains(stance)||a.stance==stance)return false;
            if(stance!="March"&&(a.movement<=0||a.supply<.5f))return false;
            a.stance=stance;if(stance!="March")a.movement=0;
            reason=stance=="March"?"March stance: existing spent movement is not restored.":stance+" prepared. The army holds its position and can intercept nearby enemies.";
            Event(reason);return true;
        }
        public int CommanderLevel(CampaignArmy a)=>a==null||a.generalName==""?0:Math.Min(5,1+a.commanderXP/100);
        public float CommandBonus(CampaignArmy a)=>CommanderLevel(a)==0?1:1+(.05f+(CommanderLevel(a)-1)*.02f)*(a.commanderWounds>0?.5f:1);
        public int ArmyMovement(CampaignArmy a)=>(int)(Movement(a.owner)*(a.commanderTrait=="Logistician"&&a.commanderWounds==0?1.15f:1));
        public float FieldReadiness(CampaignEncounter e,CampaignArmy a)
        {
            if(a==null)return 1;
            float bonus=a.owner==e.defender&&a.stance=="Fortify"?.12f:0;
            if(e.ambush&&a.owner==e.defender)bonus+=.12f;
            if(a.commanderTrait=="Tactician"&&a.commanderWounds==0)bonus+=.05f;
            return 1+bonus;
        }
        public string MovementPreview(string armyId,string destination)
        {
            var a=Army(armyId);if(a==null||!Active(destination))return "Select an army and destination.";
            var p=CityPosition(destination);var path=LandPath(a,p);
            return path==null?"No traversable land path.":MovementPreview(a,path,p);
        }
        void RecordHistory(string kind,string region,string message)
        {
            Save.history.Add(new CampaignHistoryEntry{turn=Save.world.turn,kind=kind,regionId=region??"",text=message});
            if(Save.history.Count>200)Save.history.RemoveAt(0);
            if(Save.turnSummary.turn==Save.world.turn&&!Save.turnSummary.complete&&Save.turnSummary.notices.Count<40)Save.turnSummary.notices.Add(message);
        }
        void BeginTurnSummary()
        {
            string actor=Save.playerFaction;Save.turnSummary=new CampaignTurnSummary {turn=Save.world.turn,
                treasuryBefore=World.FactionState(actor).treasury,citiesBefore=Owned(actor).Count,troopsBefore=Save.armies.Where(a=>a.owner==actor).Sum(a=>a.Soldiers)};
        }
        void CompleteTurnSummary()
        {
            var s=Save.turnSummary;if(s.turn<=0)return;string actor=Save.playerFaction;
            s.treasuryAfter=World.FactionState(actor).treasury;s.citiesAfter=Owned(actor).Count;s.troopsAfter=Save.armies.Where(a=>a.owner==actor).Sum(a=>a.Soldiers);s.complete=true;
        }
        public List<CampaignAlert> CampaignAlerts()
        {
            string actor=Save.playerFaction;var alerts=new List<CampaignAlert>();
            foreach(var a in Save.armies.Where(a=>a.owner==actor))
            {
                if(a.supply<.6f)alerts.Add(new CampaignAlert{kind="Supply",priority=1,armyId=a.id,regionId=a.regionId,text=a.name+" has "+(int)(a.supply*100)+"% supply."});
                if(!Committed(a)&&a.movement>0&&a.marchTarget==""&&a.stance=="March")alerts.Add(new CampaignAlert{kind="Idle army",priority=3,armyId=a.id,regionId=a.regionId,text=a.name+" has no orders and "+a.movement+" movement points."});
            }
            foreach(var r in Owned(actor))
            {
                var p=Politics(r.regionId);
                if(p.rebels>0||r.economy.unrest>=70)alerts.Add(new CampaignAlert{kind=p.rebels>0?"Revolt":"Unrest",priority=0,regionId=r.regionId,text=Place(r.regionId)+": unrest "+r.economy.unrest+", insurgents "+p.rebels+". Garrison or fund relief."});
                if(Besieged(r.regionId)||Save.armies.Any(a=>AtWar(actor,a.owner)&&CanObserve(a,actor)&&!AtSea(a)&&(a.regionId==r.regionId||graph[r.regionId].ContainsKey(a.regionId))))
                    alerts.Add(new CampaignAlert{kind="Threatened city",priority=0,regionId=r.regionId,text=Place(r.regionId)+(Besieged(r.regionId)?" is besieged.":" has enemy armies nearby.")});
            }
            if(IsRepublic&&Save.story.decision!="")alerts.Add(new CampaignAlert{kind="National decision",priority=0,text=DecisionTitle});
            return alerts.OrderBy(a=>a.priority).ThenBy(a=>a.regionId,StringComparer.Ordinal).ToList();
        }
        string Place(string region)=>World.Catalog.regions.FirstOrDefault(r=>r.id==region)?.capital.name??region;
    }
}
