using System;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        public CampaignProvincePolitics Politics(string region)=>Save.politics.Find(p=>p.regionId==region);
        public bool InRevolt(string region)=>(Politics(region)?.rebels??0)>0;
        public int GarrisonStrength(string region,string actor)=>Save.armies.Where(a=>a.owner==actor&&a.regionId==region&&AtSettlement(a,region)).Sum(a=>a.Soldiers);
        public bool SetOccupationPolicy(string actor,string region,string policy,out string reason)
        {
            reason="Select an owned province and a valid occupation policy.";
            if(!Owns(actor,region)||!new[]{"Reconcile","Garrison","Exploit"}.Contains(policy))return false;
            var p=Politics(region);if(p.policy==policy)return false;p.policy=policy;
            reason=policy=="Reconcile"?"Reconciliation: -3 unrest and -10% tax revenue per turn.":policy=="Garrison"?"Military administration: garrison suppression improves, normal taxation.":"Extraction: +20% tax revenue, +5 unrest per turn.";
            RecordHistory("Policy",region,Place(region)+": "+policy+" administration.");return true;
        }
        public bool ProvincialRelief(string actor,string region,out string reason)
        {
            reason="Relief costs 150 treasury, once per province per turn.";
            if(!Owns(actor,region)||Politics(region).lastReliefTurn==Save.world.turn||World.FactionState(actor).treasury<150)return false;
            World.FactionState(actor).treasury-=150;var p=Politics(region);p.lastReliefTurn=Save.world.turn;p.rebels=Math.Max(0,p.rebels-100);
            World.State(region).economy.unrest=Math.Max(0,World.State(region).economy.unrest-20);p.unrestTurns=0;
            reason="Relief delivered: -20 unrest, up to 100 insurgents stand down.";RecordHistory("Relief",region,Place(region)+": "+reason);return true;
        }
        void RegisterConquest(string region,string oldOwner,string newOwner)
        {
            var p=Politics(region);p.loyalist=oldOwner;p.policy="Reconcile";p.unrestTurns=0;p.rebels=0;
            RecordHistory("Conquest",region,newOwner+" captured "+Place(region)+" from "+oldOwner+".");
        }
        void AdvanceProvincePolitics()
        {
            foreach(var p in Save.politics)
            {
                var r=World.State(p.regionId);int soldiers=GarrisonStrength(p.regionId,r.owner);
                int suppression=Math.Min(10,soldiers/(p.policy=="Garrison"?150:250));
                r.economy.unrest=Math.Max(0,Math.Min(100,r.economy.unrest-suppression+(p.policy=="Exploit"?5:p.policy=="Reconcile"?-3:0)));
                p.unrestTurns=r.economy.unrest>=85?Math.Min(10,p.unrestTurns+1):0;
                if(p.unrestTurns>=2&&p.rebels==0)
                {p.rebels=250;RecordHistory("Revolt",p.regionId,"Armed revolt in "+Place(p.regionId)+". Training and construction have stopped.");}
                if(p.rebels<=0)continue;
                if(soldiers>0)
                {
                    int defeated=Math.Max(25,soldiers/(p.policy=="Garrison"?3:4));p.rebels=Math.Max(0,p.rebels-defeated);
                    foreach(var army in Save.armies.Where(a=>a.owner==r.owner&&a.regionId==p.regionId&&!AtSea(a)).ToArray())Casualties(army,.01f);
                    if(p.rebels==0){r.economy.unrest=Math.Min(60,r.economy.unrest);p.unrestTurns=0;RecordHistory("Pacification",p.regionId,"Garrison restored order in "+Place(p.regionId)+".");}
                }
                else p.rebels=Math.Min(2000,p.rebels+100);
                // Restoration uprisings respect treaties: a native revolt can obstruct government,
                // but cannot invent a diplomatic faction or silently break a signed truce.
                if(p.rebels>=500&&soldiers==0&&p.loyalist!=r.owner&&AtWar(p.loyalist,r.owner))
                {
                    string occupier=r.owner;r.owner=p.loyalist;r.economy.occupationTurns=0;r.economy.unrest=45;
                    int available=Math.Max(0,(int)(r.economy.population*Balance.manpowerFraction)-r.economy.enlisted);
                    int raised=Math.Min(Math.Min(p.rebels,1000),available);
                    if(raised>0)
                    {
                        var army=NewArmy(r.owner,p.regionId);army.name="Restoration militia of "+Place(p.regionId);army.movement=0;
                        while(raised>0){int men=Math.Min(raised,Balance.unitSize);var unit=NewRegiment(p.regionId,men);unit.role="Militia";AssignRegimentName(Save,army,unit);army.units.Add(unit);r.economy.enlisted+=men;raised-=men;}
                    }
                    p.rebels=0;p.unrestTurns=0;p.policy="Reconcile";
                    RecordHistory("Restoration",p.regionId,Place(p.regionId)+" rose against "+occupier+" and restored "+r.owner+".");
                }
            }
            Save.armies.RemoveAll(a=>a.Soldiers<=0);CleanSieges();
        }
        void ApplyPoliticalRevenue()
        {
            foreach(var p in Save.politics)
            {
                var r=World.State(p.regionId);var f=World.FactionState(r.owner);int tax=World.Economy.Forecast(r).taxes;
                int delta=InRevolt(p.regionId)?-tax:p.policy=="Exploit"?tax/5:p.policy=="Reconcile"?-tax/10:0;
                f.treasury+=delta;f.lastTaxes+=delta;
            }
        }
        public bool DevelopCommander(string actor,string armyId,string trait,out string reason)
        {
            reason="A healthy level-two commander can choose one specialty for 100 treasury.";var a=Army(armyId);
            if(!CanOrder(actor)||a==null||a.owner!=actor||Committed(a)||CommanderLevel(a)<2||a.commanderWounds>0||a.commanderTrait!=""||!new[]{"Tactician","Logistician","Inspiring"}.Contains(trait)||World.FactionState(actor).treasury<100)return false;
            World.FactionState(actor).treasury-=100;a.commanderTrait=trait;
            reason=a.generalName+" specializes as "+trait+".";RecordHistory("Commander",a.regionId,reason);return true;
        }
        public bool ReplaceCommander(string actor,string armyId,out string reason)
        {
            reason="A replacement commander costs 150 and requires a stationary force at home.";var a=Army(armyId);
            if(!CanOrder(actor)||a==null||a.owner!=actor||a.generalName==""||Committed(a)||World.State(a.regionId).owner!=actor||World.FactionState(actor).treasury<150)return false;
            World.FactionState(actor).treasury-=150;string former=a.generalName;
            a.generalName=GeneralName(actor,Save.nextId++);a.commanderXP=a.commanderWounds=a.commanderWins=0;a.commanderTrait="";
            reason=a.generalName+" succeeds "+former+"; the new commander begins at level one.";RecordHistory("Succession",a.regionId,reason);return true;
        }
        void RecordCommanderBattle(CampaignEncounter e,bool won)
        {
            foreach(var a in EncounterArmies(e,true).Concat(EncounterArmies(e,false)))
            {
                if(a.generalName=="")continue;
                if(a.Soldiers==0){RecordHistory("Commander lost",e.regionId,a.generalName+" was lost with "+a.name+".");continue;}
                bool victory=(a.owner==e.attacker)==won;int level=CommanderLevel(a);
                a.commanderXP=Math.Min(10000,a.commanderXP+(victory?40:20));if(victory)a.commanderWins++;
                if(victory&&IsRepublic&&a.generalName=="Napoleon Bonaparte")Save.story.napoleonPrestige=Math.Min(100,Save.story.napoleonPrestige+1);
                if(CommanderLevel(a)>level)RecordHistory("Promotion",e.regionId,a.generalName+" promoted to command level "+CommanderLevel(a)+".");
                if(!victory){a.commanderWounds=Math.Max(2,a.commanderWounds);RecordHistory("Commander wounded",e.regionId,a.generalName+" is wounded; command bonuses are reduced during recovery.");}
            }
        }
        void InitializeRepublicReserve()
        {
            if(!IsRepublic)return;
            var paris=World.State("region-paris");var reserve=NewArmy("france",paris.regionId);reserve.name="Paris National Guard";reserve.stance="Guard";
            for(int i=0;i<3;i++){var u=NewRegiment(paris.regionId,Balance.unitSize);u.role="Militia";AssignRegimentName(Save,reserve,u);reserve.units.Add(u);paris.economy.enlisted+=u.soldiers;}
            foreach(var id in new[]{"region-paris","region-lyon"})
            {var r=World.State(id);int slot=Array.FindIndex(r.buildings,b=>b.kind=="Empty");if(slot>=0)r.buildings[slot]=new RegionBuilding{kind="Farm"};}
            World.FactionState("france").treasury+=400;
        }
    }
}
