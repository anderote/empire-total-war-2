using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    /// <summary>Country campaign prototype. Model validates player/AI orders; atlas stays independent.</summary>
    public sealed partial class CountryCampaign
    {
        public readonly RegionCampaign World;
        public readonly CampaignBalance Balance;
        public CountryCampaignSave Save;
        readonly Dictionary<string,Dictionary<string,float>> graph=new Dictionary<string,Dictionary<string,float>>();
        string aiActor;
        bool autoplay;
        public CountryCampaign(RegionCatalog catalog,EconomyProfiles profiles,TheaterData geography,CampaignBalance balance,string player,uint seed=1800,string scenarioId="",GeographyAtlas movementAtlas=null)
        {
            if(player!="france"&&player!="austria"&&player!="prussia")throw new ArgumentException("Unsupported country.");
            if(balance==null||balance.schemaVersion!=1||balance.technologies==null||balance.unitSize<1||balance.unitSize>BattleConfig.MaxPersonnelPerRegiment||balance.manpowerFraction<=0)throw new ArgumentException("Invalid campaign balance.");
            if(scenarioId!=""&&scenarioId!=RepublicScenario||scenarioId==RepublicScenario&&(player!="france"||balance.startYear!=1792||balance.startSeason!=1))throw new ArgumentException("Invalid story scenario.");
            Balance=balance;ValidateResearchTree();World=new RegionCampaign(catalog,profiles);
            Save=new CountryCampaignSave {scenarioId=scenarioId,story=scenarioId==RepublicScenario?new RepublicStory():null,scenarioYear=balance.startYear,playerFaction=player,namingVersion=1,buildingProgressionVersion=CampaignBuildingTiers.Version,world=World.Save,randomState=seed==0?1800:seed};
            foreach(var r in catalog.regions)
                if(r.capital.lon>=-12&&r.capital.lon<=40&&r.capital.lat>=35&&r.capital.lat<=62)
                {Save.activeRegions.Add(r.id);graph.Add(r.id,new Dictionary<string,float>());}
            if(IsRepublic)RestoreRepublicBorders();
            foreach(var id in Save.activeRegions)
            {
                var state=World.State(id);state.economy.materials=balance.initialMaterials;
                if(Progress(state.owner)==null)Save.countries.Add(new CountryProgress {factionId=state.owner});
            }
            Save.countries.Sort((a,b)=>string.CompareOrdinal(a.factionId,b.factionId));
            if(Progress(player)==null)throw new ArgumentException("Country outside scenario.");
            foreach(var c in Save.countries)
            {
                c.startingRegions=Owned(c.factionId).Count;World.FactionState(c.factionId).treasury=balance.startingTreasury;
                var owned=Owned(c.factionId);var ordered=owned.OrderByDescending(r=>World.Economy.Profile(r.regionId).population).ThenBy(r=>r.regionId).ToList();
                string[] front=IsRepublic&&c.factionId=="france"?new[]{"region-lille","region-strasbourg","region-lyon"}:IsRepublic&&c.factionId=="austria"?new[]{"region-brussels","region-innsbruck"}:IsRepublic&&c.factionId=="sardinia"?new[]{"region-turin"}:c.factionId=="france"?new[]{"region-turin","region-strasbourg"}:c.factionId=="austria"?new[]{"region-venice","region-innsbruck"}:c.factionId=="prussia"?new[]{"region-berlin","region-warsaw"}:Array.Empty<string>();
                ordered=ordered.OrderBy(r=>Array.IndexOf(front,r.regionId)<0?99:Array.IndexOf(front,r.regionId)).ToList();
                int armies=c.factionId=="france"||c.factionId=="austria"||c.factionId=="prussia"?2:1;
                if(IsRepublic&&c.factionId=="france")armies=3;
                for(int i=0;i<Math.Min(armies,ordered.Count);i++)
                {
                    var home=ordered[i];home.buildings[0]=new RegionBuilding {kind="Barracks"};
                    var army=NewArmy(c.factionId,home.regionId);
                    army.generalName=GeneralName(c.factionId,i);
                    int count=Math.Min(c.factionId=="prussia"?balance.startingPrussianUnits:balance.startingMajorUnits,(int)(home.economy.population*balance.manpowerFraction)/balance.unitSize);
                    if(armies==1||IsRepublic&&c.factionId=="france"&&i==2)count=Math.Min(count,balance.startingMinorUnits);
                    for(int j=0;j<count;j++){army.units.Add(NewRegiment(home.regionId,balance.unitSize));home.economy.enlisted+=balance.unitSize;if(IsRepublic&&c.factionId=="france"&&j>=count/2)army.units.Last().role="Militia";AssignRegimentName(Save,army,army.units.Last());}
                }
            }
            for(int i=0;i<Save.countries.Count;i++)for(int j=i+1;j<Save.countries.Count;j++)
            {
                string a=Save.countries[i].factionId,b=Save.countries[j].factionId;
                // Authored fictional opening, not a reconstruction of 1800 diplomatic treaties.
                bool war=IsRepublic?(a=="france"||b=="france")&&new[]{"austria","prussia","sardinia"}.Contains(a=="france"?b:a):(a=="austria"&&b=="france");
                Save.treaties.Add(new CampaignTreaty {a=a,b=b,war=war,access=!war,warStart=war?1:0});
            }
            if(player=="france")World.State("region-paris").buildings[0]=new RegionBuilding {kind="Barracks"};
            BuildGraph(geography);BuildSeaGraph(geography);Terrain=new CampaignMovementTerrain(catalog,geography,movementAtlas);
            foreach(var id in Save.activeRegions)Save.politics.Add(new CampaignProvincePolitics{regionId=id,loyalist=World.State(id).owner});
            InitializeRepublicReserve();
            if(IsRepublic){Event("Autumn 1792: the Republic is proclaimed. Austria, Prussia and Sardinia threaten the frontiers. Hold Paris, build regiments and keep France supplied.");return;}
            Event("Country campaign started. Opening France–Austria conflict and neutral access are authored prototype conditions.");
        }
        string Id(string prefix)=>prefix+(Save.nextId++);
        void Event(string message){Save.events.Add("Turn "+Save.world.turn+": "+message);if(Save.events.Count>50)Save.events.RemoveAt(0);}
        public string DateLabel=>Year+" / "+((Save.world.turn-1+Balance.startSeason)%2==0?"Spring":"Autumn");
        public CountryProgress Progress(string faction)=>Save.countries.Find(c=>c.factionId==faction);
        public List<RegionState> Owned(string faction)=>Save.activeRegions.Select(World.State).Where(r=>r.owner==faction).ToList();
        public CampaignArmy Army(string id)=>Save.armies.Find(a=>a.id==id);
        public CampaignTreaty Treaty(string a,string b)=>Save.treaties.Find(t=>(t.a==a&&t.b==b)||(t.a==b&&t.b==a));
        public bool AtWar(string a,string b)=>a!=b&&Treaty(a,b)!=null&&Treaty(a,b).war;
        public bool Active(string region)=>region!=null&&graph.ContainsKey(region);
        public bool CanObserve(CampaignArmy army)=>CanObserve(army,Save.playerFaction);
        bool CanObserve(CampaignArmy army,string actor)
        {
            if(army.owner==actor)return true;if(AtSea(army))return false;
            var scouts=Save.armies.Where(a=>a.owner==actor&&!AtSea(a)&&a.Soldiers>0);
            if(army.stance=="Ambush")return scouts.Any(a=>CampaignMovementTerrain.Distance(Position(a),Position(army))<=InterceptionRangeKm);
            return scouts.Any(a=>CampaignMovementTerrain.Distance(Position(a),Position(army))<=250)||
                Owned(actor).Any(r=>CampaignMovementTerrain.Distance(CityPosition(r.regionId),Position(army))<=120);
        }
        public bool CanOrder(string actor)=>Progress(actor)!=null&&(actor==aiActor||(actor==Save.playerFaction&&Save.phase=="Player"&&!Save.roundRunning))&&Save.pending==null&&Save.outcome=="";
        bool Owns(string actor,string region)=>CanOrder(actor)&&Active(region)&&World.State(region).owner==actor;
        public int Buildings(string region,string kind)=>World.State(region).buildings.Where(b=>b.kind==kind&&CampaignBuildingTiers.IsOperational(b)).Sum(b=>b.level);
        public CampaignTechnology Technology(string id)=>Array.Find(Balance.technologies,t=>t.id==id);
        public bool Knows(string actor,string id)=>Progress(actor).technologies.Contains(id);
        public int ResearchCapacity(string actor)=>Math.Min(Balance.researchCap+Owned(actor).SelectMany(CampaignBuildingTiers.Completed).Select(t=>t.ResearchCeiling).DefaultIfEmpty(0).Max(),(Progress(actor).policy=="Investment"?2:0)+Balance.researchBase+(int)Math.Sqrt(Owned(actor).Count)*2+Owned(actor).Sum(r=>CampaignBuildingTiers.Sum(r,t=>t.ResearchPoints))+(Knows(actor,"education")?3:0));
        public int DevelopmentTechnologyGoal(string actor)=>Balance.developmentTechnologies;
        public int DevelopmentBuildingGoal(string actor)=>Balance.developmentBuildings+(int)Math.Ceiling(Progress(actor).startingRegions*.5f);
        public int CivilianBuildings(string actor)=>Owned(actor).Sum(r=>r.buildings.Count(b=>b.kind!="Empty"&&b.kind!="Barracks"&&b.kind!="Fortress"&&CampaignBuildingTiers.IsOperational(b)));
        public int Movement(string actor)=>Balance.movementKm+(Knows(actor,"logistics")?80:0);
        CampaignArmy NewArmy(string owner,string region)
        {
            var a=new CampaignArmy {id=Id("army-"),owner=owner,regionId=region,name="Independent detachment",movement=Movement(owner)};
            PlaceAtCity(a,region);Save.armies.Add(a);a.name=ArmyDefaultName(RegisterArmyName(Save,a).ordinal);return a;
        }
        CampaignRegiment NewRegiment(string home,int count)=>new CampaignRegiment {id=Id("unit-"),homeRegionId=home,soldiers=count};
        void BuildGraph(TheaterData geography)
        {
            var nodes=new Dictionary<string,string>();
            foreach(var town in geography.settlements.Concat(geography.waypoints??Array.Empty<Settlement>()))
            {
                var r=World.Catalog.ForCity(town.id)??World.Catalog.At(town.lon,town.lat);
                if(r!=null&&Active(r.id))nodes[town.id]=r.id;
            }
            foreach(var route in geography.routes)
            {
                string a,b;if(route.mode!="land"||!nodes.TryGetValue(route.fromId,out a)||!nodes.TryGetValue(route.toId,out b)||a==b)continue;
                float cost=Math.Max(20,route.distanceKm)*(route.surface=="trail"?1.25f:1f);
                if(!graph[a].ContainsKey(b)||cost<graph[a][b]){graph[a][b]=cost;graph[b][a]=cost;}
            }
        }
        bool Passable(string actor,string region,bool logistics)
        {
            if(logistics&&(Besieged(region)||InRevolt(region)))return false;
            var owner=World.State(region).owner;if(owner==actor)return true;
            var t=Treaty(actor,owner);if(t==null)return false;
            return logistics?!t.war&&t.trade:t.war||t.access;
        }
        public CampaignRoute Route(string actor,string from,string to,bool logistics=false)
        {
            if(!Active(from)||!Active(to)||logistics&&(Besieged(from)||Besieged(to)))return null;
            var costs=new Dictionary<string,float> {{from,0}};var prev=new Dictionary<string,string>();var done=new HashSet<string>();
            while(true)
            {
                string node=null;float best=float.MaxValue;
                foreach(var entry in costs)if(!done.Contains(entry.Key)&&entry.Value<best){node=entry.Key;best=entry.Value;}
                if(node==null)return null;if(node==to)break;done.Add(node);
                // A foreign hostile region can be attacked, not used as a free transit corridor.
                if(node!=from&&(AtWar(actor,World.State(node).owner)||Save.armies.Any(a=>!AtSea(a)&&a.regionId==node&&a.Soldiers>0&&AtWar(actor,a.owner))))continue;
                foreach(var edge in graph[node])if(Passable(actor,edge.Key,logistics))
                {float next=best+edge.Value;if(!costs.ContainsKey(edge.Key)||next<costs[edge.Key]){costs[edge.Key]=next;prev[edge.Key]=node;}}
            }
            var result=new CampaignRoute {distance=costs[to]};string at=to;result.regions.Add(at);
            while(at!=from){at=prev[at];result.regions.Insert(0,at);}return result;
        }
        public bool Build(string actor,string region,string kind,out string reason)
            =>Build(actor,region,kind,Array.FindIndex(World.State(region)?.buildings??Array.Empty<RegionBuilding>(),b=>b.kind=="Empty"),out reason);
        public bool Build(string actor,string region,string kind,int slot,out string reason)
        {
            if(!CanBuild(actor,region,kind,slot,out reason))return false;
            int extra=ConstructionCost(actor,region,kind)-RegionCampaign.Cost(kind);
            if(!World.Build(region,kind,slot,out reason))return false;
            World.FactionState(actor).treasury-=extra;World.State(region).buildings[slot].paidCost+=extra;
            return true;
        }
        public bool Upgrade(string actor,string region,int slot,out string reason)
        {
            if(!CanUpgrade(actor,region,slot,out reason))return false;
            var b=World.State(region).buildings[slot];var next=CampaignBuildingTiers.Get(b.kind,b.level+1);int cost=next.Cost;
            World.FactionState(actor).treasury-=cost;b.level=next.Level;b.turnsRemaining=next.Work;b.workRemaining=next.Work;b.totalWork=next.Work;b.paidCost=cost;b.upgrading=true;
            reason="Upgrade funded; production pauses during work.";return true;
        }
        public bool CancelConstruction(string actor,string region,int slot,out string reason)
        {
            reason="No cancellable project.";if(!Owns(actor,region)||slot<0||slot>=World.State(region).buildings.Length)return false;
            var state=World.State(region);var b=state.buildings[slot];if(b.turnsRemaining<=0)return false;
            int refund=b.totalWork>0?(int)(b.paidCost*b.workRemaining/b.totalWork):0;
            World.FactionState(actor).treasury+=refund;
            if(b.upgrading){b.level--;b.turnsRemaining=0;b.workRemaining=0;b.paidCost=0;b.upgrading=false;}
            else state.buildings[slot]=new RegionBuilding();
            reason="Project cancelled; "+refund+" unused funds returned.";return true;
        }
        public bool Research(string actor,string tech,out string reason)
        {
            var t=Technology(tech);var c=Progress(actor);reason="Research unavailable.";
            if(!CanOrder(actor)||t==null||Knows(actor,tech)||!ResearchAvailable(actor,t,out reason))return false;
            if(c.research!=""&&c.research!=tech){reason="Finish the current project before changing focus.";return false;}
            c.research=tech;reason="Research selected: "+t.name;return true;
        }
        public bool Reform(string actor,string policy,out string reason)
        {
            reason="Reform unavailable.";if(!CanOrder(actor)||(policy!="Balanced"&&policy!="Mobilization"&&policy!="Investment"))return false;
            var c=Progress(actor);if(c.policy==policy)return false;
            if(World.FactionState(actor).treasury<200){reason="Reform requires 200 treasury.";return false;}
            World.FactionState(actor).treasury-=200;c.policy=policy;foreach(var r in Owned(actor))r.economy.unrest=Math.Min(100,r.economy.unrest+10);
            reason="Policy changed; transition increases unrest.";return true;
        }
        public bool Recruit(string actor,string region,out string reason)=>Recruit(actor,region,"Regular","",out reason);
        public bool Refit(string actor,string armyId,out string reason)
        {
            var a=Army(armyId);reason="Refit unavailable.";
            if(!CanOrder(actor)||a==null||a.owner!=actor||Committed(a)||World.State(a.regionId).owner!=actor||!Knows(actor,"drill"))return false;
            int level=Knows(actor,"arsenals")?2:1,cost=a.units.Count*60;
            if(a.supply<.8f||(level==2&&Buildings(a.regionId,"Arsenal")==0)){reason="Refit needs supply and a local arsenal for arsenal-grade kit.";return false;}
            if(a.units.All(u=>u.equipment>=level)||World.FactionState(actor).treasury<cost||World.State(a.regionId).economy.materials<a.units.Count*5)return false;
            World.FactionState(actor).treasury-=cost;World.State(a.regionId).economy.materials-=a.units.Count*5;
            a.pendingEquipment=level;a.refitTurns=2;a.movement=0;reason="Army refitting; unavailable until training completes.";return true;
        }
        public bool Demobilize(string actor,string armyId,out string reason)
        {
            var a=Army(armyId);reason="Demobilization unavailable.";
            if(!CanOrder(actor)||a==null||a.owner!=actor||a.units.Count==0||Committed(a)||World.State(a.regionId).owner!=actor)return false;
            var u=a.units[a.units.Count-1];World.State(u.homeRegionId).economy.enlisted-=u.soldiers;a.units.Remove(u);a.movement=0;
            if(a.units.Count==0)Save.armies.Remove(a);reason="Regiment demobilized; surviving personnel return to civilian work.";return true;
        }
        public bool Recover(string actor,string armyId,out string reason)
        {
            var plan=ReplacementPlan(actor,armyId,out reason);if(plan.Count==0)return false;
            foreach(var delivery in plan)
            {
                var unit=delivery.unit;
                World.FactionState(actor).treasury-=delivery.cost;
                delivery.factory.economy.materials=Math.Max(0,delivery.factory.economy.materials-delivery.materials);
                unit.experience=(int)Math.Floor(unit.experience*unit.soldiers/(double)(unit.soldiers+delivery.count));
                unit.progression.Reinforce(unit.soldiers,delivery.count);unit.soldiers+=delivery.count;delivery.home.economy.enlisted+=delivery.count;
            }
            var army=Army(armyId);army.lastRecoveryTurn=Save.world.turn;army.movement=0;
            reason=plan.Sum(p=>p.count)+" replacements delivered / "+plan.Sum(p=>p.cost)+" treasury. Army recovering this turn.";return true;
        }
        public bool Diplomacy(string actor,string other,string action,out string reason)
        {
            reason="Proposal refused.";if(!CanOrder(actor)||actor==other)return false;var t=Treaty(actor,other);if(t==null)return false;
            if(action=="War")
            {
                if(!CanDeclareWar(actor,other,out reason))return false;SetWar(t);
                foreach(var guarantee in Save.treaties.Where(x=>x.alliance&&(x.a==other||x.b==other)).ToArray())
                {string ally=guarantee.a==other?guarantee.b:guarantee.a;if(ally!=actor){var joined=Treaty(actor,ally);if(joined!=null&&Save.world.turn>=joined.peaceUntil)SetWar(joined);}}
                Event(World.Catalog.Faction(actor).name+" declared war on "+World.Catalog.Faction(other).name+".");reason="War declared.";return true;
            }
            if(action=="Peace")return NegotiatePeace(actor,other,Power(actor)<Power(other)?300:0,"","",4,out reason);
            if(action=="Call ally")
            {
                if(!t.alliance||World.FactionState(actor).treasury<200)return false;
                foreach(var opponent in Save.countries.Where(c=>AtWar(actor,c.factionId)))
                {
                    var proposed=Treaty(other,opponent.factionId);
                    if(proposed!=null&&!proposed.war&&Save.world.turn>=proposed.peaceUntil&&Power(actor)+Power(other)>=Power(opponent.factionId)*1.2f)
                    {SetWar(proposed);World.FactionState(actor).treasury-=200;World.FactionState(other).treasury+=200;reason="Ally joins the war with 200 military aid.";Event(reason);return true;}
                }
                reason="Ally considers the war too costly or has a binding truce.";return false;
            }
            if(t.war)return false;
            if(action=="Alliance")
            {
                if(t.alliance){t.alliance=false;reason="Defensive alliance ended.";return true;}
                if(!t.trade||World.FactionState(actor).treasury<200){reason="Alliance requires trade relations and 200 diplomatic funding.";return false;}
                World.FactionState(actor).treasury-=200;t.alliance=true;t.access=true;reason="Defensive alliance agreed for future attacks.";return true;
            }
            if(action=="Trade"){t.trade=!t.trade;reason=t.trade?"Trade agreement signed; deliveries still need routes.":"Trade agreement ended.";return true;}
            if(action=="Access"){t.access=!t.access;reason=t.access?"Mutual military access agreed.":"Military access revoked.";return true;}
            return false;
        }
        void SetWar(CampaignTreaty treaty)
        {treaty.war=true;treaty.trade=false;treaty.access=false;treaty.alliance=false;treaty.warStart=Save.world.turn;treaty.warScoreA=0;}
        public bool Move(string actor,string armyId,string destination,out string reason)
            =>March(actor,armyId,destination,out reason);
        public float ArmyPower(CampaignArmy a)=>a.units.Sum(u=>u.soldiers*RegimentPower(u))*Math.Max(.4f,a.supply)*CommandBonus(a);
        public float Power(string actor)=>Save.armies.Where(a=>a.owner==actor).Sum(ArmyPower);
        float Random01(){uint x=Save.randomState;x^=x<<13;x^=x>>17;x^=x<<5;Save.randomState=x;return (x&0xFFFFFF)/16777216f;}
        void Casualties(CampaignArmy a,float fraction)
        {
            if(a==null)return;
            foreach(var u in a.units)
            {
                int lost=Math.Min(u.soldiers,(int)Math.Ceiling(u.soldiers*fraction));u.soldiers-=lost;
                var e=World.State(u.homeRegionId).economy;e.enlisted-=lost;e.population=Math.Max(1000,e.population-lost);
            }
            a.units.RemoveAll(u=>u.soldiers==0);
        }
        public bool Resolve(string encounterId,out string reason)
        {
            reason="Encounter no longer pending.";
            if(Save.pending==null||Save.pending.id!=encounterId||Save.outcome!="")return false;
            ResolveInternal(encounterId);reason=Save.events.Last();
            if(Save.roundRunning)RunAI();else ContinuePlayerTravel();return true;
        }
        void EnsureGarrison(CampaignEncounter e)
        {
            if(e.garrison==null)e.garrison=new List<CampaignRegiment>();
            if(e.fieldOnly||e.garrison.Count>0 || (EncounterArmies(e,false).Any(a=>AtSettlement(a,e.regionId)) && Buildings(e.regionId,"Fortress")==0) || World.State(e.regionId).owner!=e.defender)return;
            int men=(int)((250+Buildings(e.regionId,"Fortress")*200)*Math.Max(.4f,1-e.siegeTurns*.15f));
            for(int i=0;men>0;i++)
            {
                int count=Math.Min(Balance.unitSize,men);men-=count;
                e.garrison.Add(new CampaignRegiment {id="garrison-"+e.id+"-"+i,homeRegionId=e.regionId,soldiers=count,
                    role="Militia",weapon="musket",doctrine=1});
            }
            if(e.defenderArmyIds.Count>0){e.defenderMen+=e.garrison.Sum(u=>u.soldiers);e.defenderPower+=e.garrison.Sum(u=>u.soldiers)*.8f;}
            else {e.defenderMen=e.garrison.Sum(u=>u.soldiers);e.defenderPower=e.defenderMen*.8f;}
        }
        public List<CampaignRegiment> BattleUnits(CampaignEncounter e,bool attacker)
        {
            if(e==null)return new List<CampaignRegiment>();
            if(attacker)return EncounterArmies(e,true).SelectMany(a=>a.units).ToList();
            EnsureGarrison(e);
            return EncounterArmies(e,false).SelectMany(a=>a.units).Concat(e.garrison).ToList();
        }
        public bool CanFightManually()=>Save.pending!=null && Save.outcome=="" &&
            BattleUnits(Save.pending,true).Count>0 && BattleUnits(Save.pending,false).Count>0 &&
            EncounterArmies(Save.pending,true).Count()<=MaxReinforcingArmies+1 && EncounterArmies(Save.pending,false).Count()<=MaxReinforcingArmies+1;
        public bool CommitTacticalBattle(string encounterId,bool attackerWon,Dictionary<string,int> survivors,out string reason,Dictionary<string,int> earnedXP=null,Dictionary<string,int> earnedKills=null,Dictionary<string,int> earnedHits=null,Dictionary<string,int> shotsFired=null,bool retreated=false,bool draw=false,HashSet<string> participated=null)
        {
            reason="Invalid or stale battle result.";var e=Save.pending;
            if(e==null||e.id!=encounterId||Save.resolvedEncounters.Contains(encounterId)||survivors==null)return false;
            var units=BattleUnits(e,true).Concat(BattleUnits(e,false)).ToList();
            if(survivors.Count!=units.Count||units.Any(u=>!survivors.ContainsKey(u.id)||survivors[u.id]<0||survivors[u.id]>u.soldiers))return false;
            if(earnedXP!=null&&(earnedXP.Count!=units.Count||units.Any(u=>!earnedXP.ContainsKey(u.id)||earnedXP[u.id]<0||earnedXP[u.id]>UnitProgression.MaxXP)))return false;
            if(earnedKills!=null&&(earnedKills.Count!=units.Count||units.Any(u=>!earnedKills.ContainsKey(u.id)||earnedKills[u.id]<0)))return false;
            if(earnedHits!=null&&(earnedHits.Count!=units.Count||units.Any(u=>!earnedHits.ContainsKey(u.id)||earnedHits[u.id]<0)))return false;
            if(shotsFired!=null&&(shotsFired.Count!=units.Count||units.Any(u=>!shotsFired.ContainsKey(u.id)||shotsFired[u.id]<0)))return false;
            if(participated!=null&&(participated.Any(id=>!units.Any(u=>u.id==id))||units.Any(u=>!participated.Contains(u.id)&&(survivors[u.id]!=u.soldiers||earnedXP!=null&&earnedXP[u.id]!=0))))return false;
            int attackingSurvivors=BattleUnits(e,true).Sum(u=>survivors[u.id]);
            int defendingSurvivors=BattleUnits(e,false).Sum(u=>survivors[u.id]);
            if(attackerWon&&attackingSurvivors==0 || !attackerWon&&defendingSurvivors==0&&attackingSurvivors>0)return false;
            ApplyBattleResult(e,attackerWon,survivors,earnedXP,"Tactical",earnedKills,earnedHits,shotsFired,retreated,draw,participated);reason="Battle survivors and XP committed to campaign.";
            if(Save.roundRunning)RunAI();else ContinuePlayerTravel();return true;
        }
        void ApplyBattleResult(CampaignEncounter e,bool attackerWon,Dictionary<string,int> survivors,Dictionary<string,int> earnedXP,string method,Dictionary<string,int> earnedKills=null,Dictionary<string,int> earnedHits=null,Dictionary<string,int> shotsFired=null,bool retreated=false,bool draw=false,HashSet<string> participated=null)
        {
            var units=BattleUnits(e,true).Concat(BattleUnits(e,false)).ToList();
            var attackingIds=new HashSet<string>(BattleUnits(e,true).Select(u=>u.id));
            bool playerAttacks=e.attacker==Save.playerFaction;
            var playerUnits=BattleUnits(e,playerAttacks).ToHashSet();
            int seed=StableServiceSeed(e.id);string date="Turn "+Save.world.turn.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string label=RegimentServiceLedger.BattleLabel(Place(e.regionId),0,seed);
            var report=CaptureBattleReport(e,method);
            foreach(var u in units)
            {
                if(participated!=null&&!participated.Contains(u.id))continue;
                int strengthBefore=u.soldiers,lost=strengthBefore-survivors[u.id];var home=World.State(u.homeRegionId).economy;
                if(!e.garrison.Contains(u))home.enlisted-=lost;home.population=Math.Max(1000,home.population-lost);u.soldiers=survivors[u.id];u.experience++;
                if(earnedKills!=null)u.progression.CreditKills(earnedKills[u.id]);
                if(u.soldiers>0)u.progression.Award(earnedXP!=null?earnedXP[u.id]:EmpireBattle.UnitProgression.BattleXP(u.soldiers+lost,u.soldiers,0,0,0,attackingIds.Contains(u.id)==attackerWon));
                if(playerUnits.Contains(u))
                {
                    bool playerWon=playerAttacks==attackerWon;
                    var outcome=draw?ServiceBattleOutcome.Draw:retreated?ServiceBattleOutcome.Retreat:playerWon?ServiceBattleOutcome.Victory:ServiceBattleOutcome.Defeat;
                    RegimentServiceCommit.Record(u.progression,e.id,label,date,outcome,strengthBefore,u.soldiers,
                        earnedKills==null?0:earnedKills[u.id],earnedHits==null?0:earnedHits[u.id],shotsFired==null?0:shotsFired[u.id]);
                }
            }
            RecordCommanderBattle(e,attackerWon);RecordWarResult(e,attackerWon);
            RecordHistory("Battle",e.regionId,(attackerWon?e.attacker:e.defender)+" won "+(e.fieldOnly?"a field interception":"a battle")+" at "+Place(e.regionId)+". "+method+"; "+units.Sum(u=>survivors[u.id])+" survivors on both sides.");
            foreach(var a in Save.armies)a.units.RemoveAll(u=>u.soldiers==0);
            CommitEncounter(e,attackerWon);CompleteBattleReport(report,e,attackerWon);
        }
        void ResolveInternal(string id)
        {
            var e=Save.pending;if(e==null||e.id!=id||Save.resolvedEncounters.Contains(id))return;
            var result=SimulateAutoresolve(e);
            ApplyBattleResult(e,result.attackerWon,result.survivors,result.xp,"Autoresolve",result.kills);
        }
        static int StableServiceSeed(string value)
        {
            unchecked { uint hash=2166136261;foreach(char c in value??string.Empty){hash^=c;hash*=16777619;}return (int)(hash&0x7fffffff); }
        }
        void CommitEncounter(CampaignEncounter e,bool win)
        {
            var attackers=EncounterArmies(e,true).ToList();var attack=Army(e.attackerArmyId);
            if(win&&(attack==null||attack.Soldiers==0))attack=attackers.FirstOrDefault(a=>a.Soldiers>0);
            var defenders=EncounterArmies(e,false).ToList();
            foreach(var force in attackers.Concat(defenders)){force.movement=0;ClearMarch(force);if(force.owner!=Save.playerFaction)force.lastAIActionTurn=Save.world.turn;}
            if(win&&attack!=null&&attack.Soldiers>0)
            {
                ClearMarch(attack);
                if(!e.fieldOnly&&attack.owner==Save.playerFaction&&World.State(e.regionId).owner!=attack.owner)Save.openingConquest=true;
                if(!e.fieldOnly)PlaceAtCity(attack,e.regionId);ClearVoyage(attack);var state=World.State(e.regionId);if(!e.fieldOnly&&AtWar(attack.owner,state.owner)){RegisterConquest(e.regionId,state.owner,attack.owner);state.owner=attack.owner;state.economy.occupationTurns=3;state.economy.unrest=Math.Min(100,state.economy.unrest+30);for(int i=0;i<state.buildings.Length;i++){var project=state.buildings[i];if(project.turnsRemaining>0){if(project.upgrading){project.level--;project.turnsRemaining=0;project.workRemaining=0;project.paidCost=0;project.upgrading=false;}else state.buildings[i]=new RegionBuilding();}}}
                Save.sieges.RemoveAll(s=>defenders.Any(d=>d.id==s.armyId));
                foreach(var defence in defenders.Where(d=>d.regionId==e.regionId||e.fieldOnly&&d.id==e.defenderArmyId))
                {
                    string retreat=RetreatDestination(e,defence);
                    if(retreat==null)Casualties(defence,1);else{PlaceAtCity(defence,retreat);defence.stance="March";defence.movement=0;ClearMarch(defence);}
                }
            }
            if(!win&&attack!=null){if(e.amphibious&&AtSea(attack))ReturnFromLanding(attack);attack.movement=0;ClearMarch(attack);}
            Save.armies.RemoveAll(a=>a.Soldiers<=0);CleanSieges();Save.resolvedEncounters.Add(e.id);
            Event((win?e.attacker:e.defender)+" won at "+World.State(e.regionId).regionId+"; surviving formations retained.");
            Save.pending=null;Save.phase=Save.roundRunning?"AI":"Player";CheckCampaignDefeat();
        }
        public bool Withdraw(string encounterId,out string reason)
        {
            reason="Withdrawal unavailable.";var e=Save.pending;
            if(e==null||e.id!=encounterId||!CanWithdraw(out reason))return false;
            if(e.defender==Save.playerFaction)
            {
                foreach(var id in e.defenderArmyIds)Casualties(Army(id),.05f);
                CommitEncounter(e,true);Event("Defending armies withdrew, losing stragglers and yielding the region.");
                reason="Armies retreated; the enemy occupies the region.";if(Save.roundRunning)RunAI();else ContinuePlayerTravel();return true;
            }
            var a=Army(e.attackerArmyId);Casualties(a,.05f);if(e.amphibious&&AtSea(a))ReturnFromLanding(a);a.movement=0;Save.armies.RemoveAll(x=>x.Soldiers<=0);
            ClearMarch(a);
            Save.resolvedEncounters.Add(e.id);Save.pending=null;Save.phase=Save.roundRunning?"AI":"Player";
            Event("Attacking army withdrew and lost stragglers.");CheckCampaignDefeat();reason="Withdrawal completed.";if(Save.roundRunning)RunAI();else ContinuePlayerTravel();return true;
        }
        string RetreatDestination(CampaignEncounter e,CampaignArmy army)=>graph[e.fieldOnly?army.regionId:e.regionId].Keys
            .OrderBy(r=>World.State(r).owner==army.owner?0:1).ThenBy(r=>r,StringComparer.Ordinal)
            .FirstOrDefault(r=>Passable(army.owner,r,false)&&!AtWar(army.owner,World.State(r).owner)&&
                !Save.armies.Any(a=>!AtSea(a)&&a.regionId==r&&a.Soldiers>0&&AtWar(army.owner,a.owner)));
        public bool CanWithdraw(out string reason)
        {
            reason="No player encounter is pending.";var e=Save.pending;if(e==null||Save.outcome!="")return false;
            if(e.attacker==Save.playerFaction){reason="Cancel the attack: lose 5% as stragglers and all remaining movement.";return true;}
            if(e.defender!=Save.playerFaction)return false;
            if(e.garrison.Count>0){reason="Local garrisons must defend their settlement; fight or autoresolve.";return false;}
            var armies=e.defenderArmyIds.Select(Army).Where(a=>a!=null).ToList();
            if(armies.Count==0||armies.Any(a=>a.movement<=0||Committed(a)||RetreatDestination(e,a)==null))
            {reason="Retreat needs mobile armies and a safe adjacent region; exhausted, refitting or trapped armies must fight.";return false;}
            reason="Retreat all defenders: yield this region, lose 5% as stragglers and all remaining movement.";return true;
        }
        public bool EndTurn(out string reason)
        {
            reason="Resolve the current encounter first.";
            if(Save.pending==null&&CheckCampaignDefeat()){reason="All your cities were captured and every army was destroyed.";return false;}
            if(Save.pending!=null||Save.outcome!=""||Save.roundRunning)return false;
            if(IsRepublic&&Save.story.decision!=""){reason="Resolve the national decision on the Nation page first.";return false;}
            BeginTurnSummary();Save.roundRunning=true;Save.phase="AI";Save.aiIndex=0;RunAI();reason=Save.pending!=null?"Enemy attack awaits resolution.":"Turn advanced.";return true;
        }
        void RunAI()
        {
            while(Save.outcome==""&&Save.aiIndex<Save.countries.Count)
            {
                var c=Save.countries[Save.aiIndex];if(c.factionId==Save.playerFaction){Save.aiIndex++;continue;}
                aiActor=c.factionId;Think(aiActor);aiActor=null;if(Save.pending!=null)return;
                Save.aiIndex++;
            }
            if(Save.outcome=="")FinishRound();
        }
        // Test/dev entry point: no player input or hidden resource bonuses.
        public void SimulateRound()
        {
            autoplay=true;
            if(Save.pending!=null)ResolveInternal(Save.pending.id);
            aiActor=Save.playerFaction;if(Save.outcome=="")Think(aiActor);aiActor=null;
            if(Save.outcome=="")EndTurn(out _);autoplay=false;
        }
        void Think(string actor)
        {
            var own=Owned(actor);if(own.Count==0){ThinkWithoutCities(actor);return;}var c=Progress(actor);var funds=World.FactionState(actor);
            var enemies=Save.countries.Where(x=>AtWar(actor,x.factionId)&&Owned(x.factionId).Count>0).ToList();
            // Resume this faction after a player battle without repeating purchases or
            // skipping the rest of its armies. Markers persist in pending-battle saves.
            if(c.lastPlanningTurn!=Save.world.turn)
            {
            c.lastPlanningTurn=Save.world.turn;PlanCampaignStrategy(actor);ThinkAdoption(actor);ThinkBlockades(actor);
            int forceLimit=enemies.Count>0?own.Count+5:Math.Min(6,own.Count+3);
            if(enemies.Count==0&&Save.armies.Where(a=>a.owner==actor).Sum(a=>a.units.Count)>forceLimit)
                foreach(var a in Save.armies.Where(a=>a.owner==actor&&a.units.Count>3).ToArray())if(Demobilize(actor,a.id,out _))break;
            if(funds.treasury<Balance.reserveBudget&&c.lastNet<0)foreach(var a in Save.armies.Where(a=>a.owner==actor).ToArray())if(Demobilize(actor,a.id,out _))break;
            if(c.research=="")
            {
                string[] priority=actor=="prussia"?new[]{"industry","agriculture","education","commerce","administration","drill","logistics","irrigation","arsenals"}:new[]{"agriculture","drill","maneuver","industry","education","volleys","light-infantry","arsenals","rifles","logistics","administration","irrigation","commerce"};
                foreach(var id in priority)if(Research(actor,id,out _))break;
            }
            if(funds.treasury>Balance.reserveBudget+200)
            {
                var deficit=own.OrderBy(r=>World.Economy.Forecast(r).foodProduced-World.Economy.Forecast(r).foodNeeded).First();
                var forecast=World.Economy.Forecast(deficit);
                string kind=forecast.foodProduced<forecast.foodNeeded?"Farm":own.Sum(r=>Buildings(r.regionId,"University"))==0?"University":"Workshop";
                foreach(var r in kind=="Farm"?own.OrderBy(r=>World.Economy.Forecast(r).foodProduced-World.Economy.Forecast(r).foodNeeded):own.OrderBy(r=>Buildings(r.regionId,kind)))if(Build(actor,r.regionId,kind,out _))break;
            }
            int units=Save.armies.Where(a=>a.owner==actor).Sum(a=>a.units.Count)+Save.recruitment.Count(q=>q.owner==actor);
            if(funds.treasury>Balance.reserveBudget+Balance.recruitCost&&units<forceLimit&&c.lastNet>=0)
                foreach(var r in own)if(Recruit(actor,r.regionId,out _))break;
            }

            foreach(var army in Save.armies.Where(a=>a.owner==actor).ToArray())
            {
                if(Army(army.id)==null||army.lastAIActionTurn==Save.world.turn)continue;
                army.lastAIActionTurn=Save.world.turn;
                if(AtSea(army)){LandTransport(army);if(Save.pending!=null)return;continue;}
                if(UnderSiegeOrders(army)){var siege=Save.sieges.First(x=>x.armyId==army.id);if(siege.turns>=2)AssaultSiege(actor,siege.id,out _);if(Save.pending!=null)return;continue;}
                if(TrainingBusy(army))continue;
                if(army.marchTarget!=""){AdvanceMarch(army,out _);if(Save.pending!=null)return;continue;}
                if(ThinkStrategicArmy(actor,army)){if(Save.pending!=null)return;continue;}
                // Small local forces join a field army rather than remaining idle forever.
                var receiver=Save.armies.Where(a=>a.id!=army.id&&a.owner==actor&&a.generalName!=""&&!Committed(a)&&a.units.Count+army.units.Count<=MaxArmyRegiments)
                    .Select(a=>new {army=a,route=Route(actor,army.regionId,a.regionId)})
                    .Where(x=>x.route!=null).OrderBy(x=>x.route.distance).FirstOrDefault();
                if(army.units.Count<3&&receiver!=null)
                {if(ForcesCanMeet(receiver.army,army))Transfer(actor,army.id,receiver.army.id,army.units.Select(u=>u.id).ToArray(),out _);
                 else {var p=Position(receiver.army);MarchTo(actor,army.id,p.lon,p.lat,out _,receiver.army.id);}if(Save.pending!=null)return;continue;}
                if(army.units.Count<3)continue;
                bool closeThreat=Save.armies.Any(a=>AtWar(actor,a.owner)&&CanObserve(a,actor)&&(a.regionId==army.regionId||graph[army.regionId].ContainsKey(a.regionId)));
                if(!closeThreat&&funds.treasury>Balance.reserveBudget+army.units.Count*60&&army.units.Any(u=>u.equipment<(Knows(actor,"arsenals")?2:1))&&Refit(actor,army.id,out _))continue;
                var threat=own.Where(r=>Save.armies.Any(a=>AtWar(actor,a.owner)&&CanObserve(a,actor)&&graph[r.regionId].ContainsKey(a.regionId))).Select(r=>Route(actor,army.regionId,r.regionId)).Where(r=>r!=null&&r.distance>0&&r.distance<=army.movement).OrderBy(r=>r.distance).FirstOrDefault();
                if(threat!=null){March(actor,army.id,threat.regions.Last(),out _);if(Save.pending!=null)return;continue;}
                if(army.Soldiers<army.units.Count*Balance.unitSize*.65f&&Recover(actor,army.id,out _))continue;
                if(army.supply<.6f&&World.State(army.regionId).owner!=actor)
                {var home=own.Select(r=>Route(actor,army.regionId,r.regionId)).Where(r=>r!=null&&r.distance<=army.movement).OrderBy(r=>r.distance).FirstOrDefault();if(home!=null)Move(actor,army.id,home.regions.Last(),out _);continue;}
                // Only visible field armies inform target strength; known forts and cities remain strategic facts.
                var targets=enemies.SelectMany(e=>Owned(e.factionId)).Select(r=>new {region=r,route=Route(actor,army.regionId,r.regionId)})
                    .Where(x=>x.route!=null).OrderBy(x=>x.route.distance+KnownDefense(actor,x.region.regionId)*.12f).ToList();
                var target=targets.FirstOrDefault(x=>ArmyPower(army)>=KnownDefense(actor,x.region.regionId)*1.15f);
                if(target!=null)March(actor,army.id,target.region.regionId,out _);
                else if(ThinkSeaTransport(actor,army)){}
                else if(receiver!=null&&receiver.army.units.Count>=army.units.Count)
                {
                    if(receiver.army.regionId==army.regionId)Transfer(actor,army.id,receiver.army.id,army.units.Select(u=>u.id).ToArray(),out _);
                    else March(actor,army.id,receiver.army.regionId,out _);
                }
                if(Save.pending!=null)return;
            }
            if(enemies.Count>0&&c.lastNet<0&&Power(actor)<enemies.Sum(e=>Power(e.factionId))*.7f)Diplomacy(actor,enemies[0].factionId,"Peace",out _);
        }
        void ApplyCapabilities()
        {
            foreach(var c in Save.countries)foreach(var r in Owned(c.factionId))
            {
                r.economy.foodBonus=(Knows(c.factionId,"agriculture")?20:0)+(Knows(c.factionId,"irrigation")?20:0);
                r.economy.wealthBonus=(Knows(c.factionId,"industry")?40:0)+(Knows(c.factionId,"commerce")?40:0);
                r.economy.taxRate=c.policy=="Mobilization"?18:c.policy=="Investment"?12:15;
            }
        }
        void DeliverFood()
        {
            var states=Save.activeRegions.Select(World.State).ToList();var received=new Dictionary<string,float>();var sent=new Dictionary<string,float>();
            foreach(var receiver in states.OrderBy(r=>r.economy.food))
            {
                var rb=World.Economy.Forecast(receiver);float armyFood=Save.armies.Where(a=>!AtSea(a)&&a.regionId==receiver.regionId).Sum(a=>a.Soldiers)/1000f;
                float need=Math.Max(0,rb.foodNeeded+armyFood+rb.foodNeeded*.5f-receiver.economy.food-rb.foodProduced);
                foreach(var donor in states.OrderBy(r=>r.regionId))
                {
                    if(need<=0||donor==receiver)continue;var db=World.Economy.Forecast(donor);
                    float available=Math.Max(0,donor.economy.food-db.foodNeeded*.5f-Save.armies.Where(a=>!AtSea(a)&&a.regionId==donor.regionId).Sum(a=>a.Soldiers)/1000f);if(available<=0)continue;
                    var route=Route(receiver.owner,receiver.regionId,donor.regionId,true);if(route==null||route.distance>Balance.freightRangeKm)continue;
                    float shipped=Math.Min(need,Math.Min(available,FreightCapacity(receiver.regionId)-(received.ContainsKey(receiver.regionId)?received[receiver.regionId]:0)));
                    shipped=Math.Min(shipped,FreightCapacity(donor.regionId)-(sent.ContainsKey(donor.regionId)?sent[donor.regionId]:0));
                    if(donor.owner!=receiver.owner)shipped=Math.Min(shipped,Math.Max(0,World.FactionState(receiver.owner).treasury));
                    if(shipped<=0)continue;
                    sent[donor.regionId]=(sent.ContainsKey(donor.regionId)?sent[donor.regionId]:0)+shipped;
                    donor.economy.food-=shipped;receiver.economy.food+=shipped;need-=shipped;received[receiver.regionId]=(received.ContainsKey(receiver.regionId)?received[receiver.regionId]:0)+shipped;
                    if(donor.owner!=receiver.owner){int money=(int)Math.Ceiling(shipped);World.FactionState(receiver.owner).treasury-=money;World.FactionState(donor.owner).treasury+=money;World.FactionState(receiver.owner).lastTradeBalance-=money;World.FactionState(donor.owner).lastTradeBalance+=money;RecordTradeShipment(donor.owner,receiver.owner,donor.regionId,receiver.regionId,shipped,money);}
                }
            }
        }
        void FinishRound()
        {
            foreach(var f in Save.world.factions)f.lastTradeBalance=0;Save.lastTradeShipments.Clear();
            AdvanceSieges();AdvanceProvincePolitics();ApplyCapabilities();DeliverFood();
            // Economies outside the active scenario never contribute to a European session.
            var active=new RegionSave {regions=Save.activeRegions.Select(World.State).ToArray(),factions=Save.world.factions};
            World.Economy.Advance(active);ApplyPoliticalRevenue();
            foreach(var c in Save.countries)
            {
                var own=Owned(c.factionId);var faction=World.FactionState(c.factionId);
                c.lastAdministration=own.Count*Math.Max(3,Balance.administrationPerRegion-(Knows(c.factionId,"administration")?3:0))+Math.Max(0,own.Count-8)*5+Save.treaties.Count(t=>t.alliance&&(t.a==c.factionId||t.b==c.factionId))*2;
                c.lastMilitaryCost=Save.armies.Where(a=>a.owner==c.factionId).Sum(a=>a.units.Count*Balance.armyUnitUpkeep+(a.generalName!=""?4:0));
                c.lastResearchCost=0;faction.treasury-=c.lastAdministration+c.lastMilitaryCost;faction.lastExpenses+=c.lastAdministration+c.lastMilitaryCost;
                if(c.research!=""&&faction.treasury>=Balance.researchFunding)
                {
                    faction.treasury-=Balance.researchFunding;faction.lastExpenses+=Balance.researchFunding;c.lastResearchCost=Balance.researchFunding;
                    c.researchProgress+=ResearchCapacity(c.factionId);var t=Technology(c.research);
                    if(c.researchProgress>=t.cost){c.technologies.Add(t.id);c.research="";c.researchProgress=0;Event(c.factionId+" completed "+t.name+".");}
                }
                c.lastNet=faction.lastTaxes-faction.lastExpenses;
            }
            AdvanceBlockades();
            foreach(var r in active.regions)
            {
                r.economy.materials=Math.Min(MaterialCapacity(r.regionId),r.economy.materials+MaterialProduction(r.regionId));
                float work=ConstructionCapacity(r.regionId);
                foreach(var b in r.buildings)
                {
                    if(b.turnsRemaining<=0||Besieged(r.regionId)||InRevolt(r.regionId))continue;if(b.workRemaining<=0)b.workRemaining=b.turnsRemaining;
                    float amount=Math.Min(b.workRemaining,Math.Min(work,r.economy.materials/Balance.constructionMaterials));
                    b.workRemaining-=amount;work-=amount;r.economy.materials=Math.Max(0,r.economy.materials-amount*Balance.constructionMaterials);
                    b.turnsRemaining=(int)Math.Ceiling(b.workRemaining-.0001f);if(b.turnsRemaining==0){b.workRemaining=0;b.upgrading=false;b.paidCost=0;}
                }
                if(r.economy.occupationTurns>0)r.economy.occupationTurns--;
                // Prototype six-month demographic rates; no claim of historical calibration.
                if(r.economy.wellbeing>=70&&r.economy.unrest<30)r.economy.population=Math.Min(10000000,r.economy.population+Math.Max(1,r.economy.population/1000));
                else if(r.economy.wellbeing<20)r.economy.population=Math.Max(Math.Max(1000,r.economy.enlisted+1000),r.economy.population-Math.Max(1,r.economy.population/1000));
                r.economy.unrest=Math.Max(0,Math.Min(100,r.economy.unrest+(r.economy.wellbeing<40?5:-2)+(Progress(r.owner).policy=="Mobilization"?2:Progress(r.owner).policy=="Investment"?-2:0)));
            }
            foreach(var q in Save.recruitment.ToArray())
            {
                var home=World.State(q.regionId);if(home.owner!=q.owner){home.economy.enlisted-=q.soldiers;Save.recruitment.Remove(q);continue;}
                if(Besieged(q.regionId)||InRevolt(q.regionId))continue;
                q.turnsRemaining--;if(q.turnsRemaining>0)home.economy.food=Math.Max(0,home.economy.food-q.soldiers/1000f);
                if(q.turnsRemaining==0){var army=RecruitmentDestination(q);var trained=NewRegiment(q.regionId,q.soldiers);trained.role=q.role;trained.weapon=q.weapon;trained.doctrine=q.doctrine;ApplySavedRecruitment(q,trained);if(army.units.Count>=MaxArmyRegiments)army=NewArmy(q.owner,q.regionId);AssignRegimentName(Save,army,trained,string.IsNullOrEmpty(q.targetArmyId));army.units.Add(trained);army.movement=0;Save.recruitment.Remove(q);Event(q.owner+" completed a regiment.");if(q.owner==Save.playerFaction)RecordHistory("Recruitment",q.regionId,trained.name+" ready for "+army.name+" at "+Place(q.regionId)+".");}
            }
            foreach(var a in Save.armies)
            {
                if(a.commanderWounds>0)a.commanderWounds--;
                if(AtSea(a)){a.movement=0;a.supply=Math.Max(.6f,a.supply);if(a.seaTurns>0)a.seaTurns--;continue;}
                var local=World.State(a.regionId);float needed=a.Soldiers/1000f*Math.Max(.5f,1-a.units.Average(u=>u.loadout.Modifiers.supply));
                bool access=local.owner==a.owner||(Treaty(a.owner,local.owner)!=null&&Treaty(a.owner,local.owner).access&&!AtWar(a.owner,local.owner))||HasSecuredProcurement(a);
                float delivered=access?Math.Min(needed,local.economy.food):0;
                if(local.owner!=a.owner){delivered=Math.Min(delivered,Math.Max(0,World.FactionState(a.owner).treasury));int price=(int)Math.Ceiling(delivered);World.FactionState(a.owner).treasury-=price;World.FactionState(a.owner).lastTradeBalance-=price;World.FactionState(local.owner).treasury+=price;World.FactionState(local.owner).lastTradeBalance+=price;RecordTradeShipment(local.owner,a.owner,local.regionId,a.regionId,delivered,price);}local.economy.food-=delivered;a.supply=needed>0?delivered/needed:0;
                ApplyTerrainAttrition(a);if(a.supply<.5f)Casualties(a,.02f);
                if(a.refitTurns>0){a.refitTurns--;if(a.refitTurns==0){foreach(var u in a.units)u.equipment=Math.Max(u.equipment,a.pendingEquipment);a.pendingEquipment=0;}}
                AdvanceTraining(a);AdvanceEquipment(a);AdvanceItems(a);
                a.movementDebt=0;a.movement=TrainingBusy(a)||UnderSiegeOrders(a)?0:MovementAllowance(a);
            }
            Save.armies.RemoveAll(a=>a.Soldiers==0);
            foreach(var c in Save.countries){var f=World.FactionState(c.factionId);c.lastNet=f.lastTaxes+f.lastTradeBalance-f.lastExpenses;}
            CompleteTurnSummary();Save.world.turn++;ExpireTruces();Save.roundRunning=false;Save.phase="Player";Save.aiIndex=0;if(CheckCampaignDefeat())return;AdvanceRepublicStory();
            ContinuePlayerTravel();
        }
        public bool Eliminated(string faction)=>Owned(faction).Count==0&&!Save.armies.Any(a=>a.owner==faction&&a.Soldiers>0);
        bool CheckCampaignDefeat()
        {
            if(!Eliminated(Save.playerFaction))return false;
            if(Save.outcome!="Defeat")Event("All your cities have been captured and every army and detachment has been destroyed.");
            Save.outcome="Defeat";Save.victoryPath="All cities captured and all armies destroyed";
            CompleteTurnSummary();Save.roundRunning=false;Save.phase="Player";Save.aiIndex=0;return true;
        }
        void ThinkWithoutCities(string actor)
        {
            foreach(var army in Save.armies.Where(a=>a.owner==actor&&a.Soldiers>0).ToArray())
            {
                if(army.lastAIActionTurn==Save.world.turn)continue;
                army.lastAIActionTurn=Save.world.turn;
                if(AtSea(army)){LandTransport(army);if(Save.pending!=null)return;continue;}
                if(UnderSiegeOrders(army)){var siege=Save.sieges.First(x=>x.armyId==army.id);if(siege.turns>=2)AssaultSiege(actor,siege.id,out _);if(Save.pending!=null)return;continue;}
                if(TrainingBusy(army))continue;
                if(army.marchTarget!="")AdvanceMarch(army,out _);
                else
                {
                    var target=Save.activeRegions.Where(id=>id!=army.regionId&&AtWar(actor,World.State(id).owner))
                        .Select(id=>new{region=id,route=Route(actor,army.regionId,id)}).Where(x=>x.route!=null)
                        .OrderBy(x=>x.route.distance+KnownDefense(actor,x.region)*.12f).FirstOrDefault();
                    if(target!=null)March(actor,army.id,target.region,out _);else ThinkSeaTransport(actor,army);
                }
                if(Save.pending!=null||Save.outcome!="")return;
            }
        }
        public bool Adopt(CountryCampaignSave candidate,out string reason)
        {
            reason="Incompatible country save.";
            if(candidate==null||(candidate.schemaVersion!=1&&candidate.schemaVersion!=2&&candidate.schemaVersion!=3&&candidate.schemaVersion!=4&&candidate.schemaVersion!=5&&candidate.schemaVersion!=6)||candidate.playerFaction!=Save.playerFaction||candidate.activeRegions==null||candidate.countries==null||candidate.armies==null||candidate.recruitment==null||candidate.treaties==null||candidate.events==null||candidate.resolvedEncounters==null||candidate.world==null||candidate.nextId<1||candidate.randomState==0)return false;
            // Unity's inline class serializer writes a null encounter as an empty object.
            // Only that exact placeholder is absent; partial or active encounters still fail validation.
            var placeholder=candidate.pending;
            if(candidate.phase!="Encounter"&&placeholder!=null&&
                string.IsNullOrEmpty(placeholder.id)&&string.IsNullOrEmpty(placeholder.attackerArmyId)&&string.IsNullOrEmpty(placeholder.defenderArmyId)&&
                string.IsNullOrEmpty(placeholder.regionId)&&string.IsNullOrEmpty(placeholder.attacker)&&string.IsNullOrEmpty(placeholder.defender)&&
                placeholder.attackerMen==0&&placeholder.defenderMen==0&&placeholder.attackerPower==0&&placeholder.defenderPower==0&&
                (placeholder.defenderArmyIds==null||placeholder.defenderArmyIds.Count==0)&&(placeholder.garrison==null||placeholder.garrison.Count==0))candidate.pending=null;
            if(candidate.scenarioYear!=Balance.startYear||candidate.world.turn<1||candidate.buildingProgressionVersion<0||candidate.buildingProgressionVersion>CampaignBuildingTiers.Version)return false;
            int candidateYear=Balance.startYear+(candidate.world.turn-1+Balance.startSeason)/2;
            if(!ValidRepublicSave(candidate))return false;
            if(candidate.activeRegions.Count!=Save.activeRegions.Count||!new HashSet<string>(candidate.activeRegions).SetEquals(Save.activeRegions)||candidate.countries.Count!=Save.countries.Count)return false;
            if(candidate.outcome!=""&&candidate.outcome!="Victory"&&candidate.outcome!="Defeat"&&candidate.outcome!="Campaign complete")return false;
            if(!candidate.roundRunning&&candidate.phase=="AI"||candidate.roundRunning&&candidate.phase=="Player")return false;
            if(candidate.phase!="Player"&&candidate.phase!="AI"&&candidate.phase!="Encounter")return false;
            if(candidate.aiIndex<0||candidate.aiIndex>candidate.countries.Count||((candidate.pending!=null)!=(candidate.phase=="Encounter")))return false;
            var factions=new HashSet<string>();
            foreach(var c in candidate.countries)
            {
                if(c==null||Progress(c.factionId)==null||c.startingRegions!=Progress(c.factionId).startingRegions||c.lastPlanningTurn<0||c.lastPlanningTurn>candidate.world.turn||c.holdTurns<0||c.prosperityTurns<0||c.holdTurns>candidate.world.turn||c.prosperityTurns>candidate.world.turn||!factions.Add(c.factionId)||c.technologies==null||c.technologies.Distinct().Count()!=c.technologies.Count||c.technologies.Any(t=>Technology(t)==null)||float.IsNaN(c.researchProgress)||float.IsInfinity(c.researchProgress)||c.researchProgress<0||(c.research!=""&&Technology(c.research)==null)||(c.policy!="Balanced"&&c.policy!="Investment"&&c.policy!="Mobilization"))return false;
                if(!ValidResearchProgress(c,candidateYear))return false;
            }
            if(candidate.lastTradeShipments==null)candidate.lastTradeShipments=new List<CampaignTradeShipment>();
            if(candidate.lastTradeShipments.Count>10000||candidate.lastTradeShipments.Any(t=>t==null||!factions.Contains(t.seller)||!factions.Contains(t.buyer)||t.seller==t.buyer||!candidate.activeRegions.Contains(t.origin)||!candidate.activeRegions.Contains(t.destination)||t.resource!="food"||float.IsNaN(t.quantity)||float.IsInfinity(t.quantity)||t.quantity<=0||t.payment<0||t.payment>1000000))return false;
            if(candidate.schemaVersion==1)MigrateMilitary(candidate);
            if(candidate.schemaVersion==2)MigrateProgression(candidate);
            if(candidate.schemaVersion<4)
            {
                foreach(var a in candidate.armies){a.generalName=GeneralName(a.owner,0);ClearMarch(a);}
                candidate.schemaVersion=4;
            }
            if(candidate.schemaVersion<6&&!MigrateMapMovement(candidate))return false;
            if(candidate.sieges==null)candidate.sieges=new List<CampaignSiege>();if(candidate.blockades==null)candidate.blockades=new List<CampaignBlockade>();
            var ids=new HashSet<string>();var enlisted=new Dictionary<string,int>();foreach(var r in candidate.activeRegions)enlisted[r]=0;
            foreach(var a in candidate.armies)
            {
                reason="Invalid army state: "+a?.id;
                if(a==null||!ids.Add(a.id)||!factions.Contains(a.owner)||!Active(a.regionId)||a.units==null||a.units.Count==0||a.movement<0||a.movement>2000||a.lastAIActionTurn<0||a.lastAIActionTurn>candidate.world.turn||a.refitTurns<0||a.refitTurns>2||a.pendingEquipment<0||a.pendingEquipment>2||float.IsNaN(a.supply)||a.supply<0||a.supply>1)return false;
                if(!ValidMapMarch(a)||!ValidVoyage(a)||a.generalName==null||a.generalName.Length>100||a.units.Count>MaxArmyRegiments)return false;
                foreach(var u in a.units){reason="Invalid regiment state: "+u?.id;if(u==null||!ids.Add(u.id)||!Active(u.homeRegionId)||u.soldiers<1||u.soldiers>Balance.unitSize||u.equipment<0||u.equipment>UnitProgression.MaxQuality||u.experience<0||(!string.IsNullOrEmpty(u.name)&&!SavedRegiment.SafeName(u.name,60))||!ValidMilitary(u,candidateYear)||!ValidProgression(u)||!ValidLoadout(u,candidateYear))return false;
                    if(!ValidProcurementReceipt(candidate,a,u))return false;
                    if(u.trainingTurns>0&&!candidate.countries.Find(c=>c.factionId==a.owner).technologies.Contains(Course(u.course).research))return false;
                    if(u.upgradeTurns>0&&(a.refitTurns>0||!candidate.countries.Find(c=>c.factionId==a.owner).technologies.Contains(u.pendingEquipment>=2?"arsenals":"drill")))return false;
                    if(u.kitTurns>0&&(a.refitTurns>0||UnitEquipment.Find(u.pendingItem).Research!=""&&!candidate.countries.Find(c=>c.factionId==a.owner).technologies.Contains(UnitEquipment.Find(u.pendingItem).Research)))return false;
                    if(u.loadout.items.Any(id=>UnitEquipment.Find(id).Research!=""&&!candidate.countries.Find(c=>c.factionId==a.owner).technologies.Contains(UnitEquipment.Find(id).Research)))return false;
                    enlisted[u.homeRegionId]+=u.soldiers;}
            }
            foreach(var q in candidate.recruitment){reason="Invalid recruitment: "+q?.id;if(q==null||!ids.Add(q.id)||!factions.Contains(q.owner)||!Active(q.regionId)||q.soldiers!=Balance.unitSize||q.paidCost<0||(q.targetArmyId!=null&&q.targetArmyId.Length>100)||!ValidSavedRecruitment(q,candidateYear,candidate)||q.paidMaterials<0||q.turnsRemaining<1||q.turnsRemaining>Balance.trainingTurns||q.totalTurns<0||q.totalTurns>Balance.trainingTurns||(q.totalTurns>0&&q.turnsRemaining>q.totalTurns)||!ValidRecruitment(q,candidateYear))return false;enlisted[q.regionId]+=q.soldiers;}
            reason="Invalid naval or siege orders.";if(!ValidOperations(candidate,ids))return false;
            reason="Invalid strategic campaign state.";if(!ValidStrategy(candidate))return false;
            reason="Invalid diplomacy state.";
            if(candidate.treaties.Count!=Save.treaties.Count)return false;var pairs=new HashSet<string>();
            foreach(var t in candidate.treaties){if(t==null||t.a==t.b||!factions.Contains(t.a)||!factions.Contains(t.b)||!pairs.Add(string.CompareOrdinal(t.a,t.b)<0?t.a+"|"+t.b:t.b+"|"+t.a)||t.war&&(t.access||t.trade||t.alliance)||t.goodwillA<0||t.goodwillA>100||t.goodwillB<0||t.goodwillB>100)return false;}
            reason="Invalid battle report.";if(candidate.resolvedEncounters.Distinct().Count()!=candidate.resolvedEncounters.Count||!ValidBattleReport(candidate))return false;
            if(candidate.pending!=null)
            {
                reason="Invalid pending encounter.";var e=candidate.pending;
                if(e.garrison==null)e.garrison=new List<CampaignRegiment>();
                if(e.attackerArmyIds==null||e.attackerArmyIds.Count==0)e.attackerArmyIds=new List<string>{e.attackerArmyId};
                if(!ValidEncounterSupport(candidate,e))return false;
                if(e.defenderArmyIds==null || e.defenderArmyIds.Distinct().Count()!=e.defenderArmyIds.Count ||
                    !string.IsNullOrEmpty(e.defenderArmyId)&&!e.defenderArmyIds.Contains(e.defenderArmyId) ||
                    e.garrison.Any(u=>u==null||string.IsNullOrEmpty(u.id)||u.homeRegionId!=e.regionId||u.soldiers<1||u.soldiers>Balance.unitSize||u.weapon!="musket"||u.doctrine!=1||!ValidProgression(u)||!ValidLoadout(u,candidateYear)||u.upgradeTurns!=0||u.kitTurns!=0)||
                    e.garrison.Select(u=>u.id).Distinct().Count()!=e.garrison.Count||e.garrison.Any(u=>ids.Contains(u.id)))return false;if(!candidate.armies.Any(a=>a.id==e.attackerArmyId&&a.owner==e.attacker)||(!string.IsNullOrEmpty(e.defenderArmyId)&&!ids.Contains(e.defenderArmyId))||!Active(e.regionId)||!factions.Contains(e.attacker)||!factions.Contains(e.defender)||e.defenderArmyIds==null||e.defenderArmyIds.Any(id=>!candidate.armies.Any(a=>a.id==id&&a.owner==e.defender&&!AtSea(a)))||e.attackerPower<=0||e.defenderPower<=0||float.IsNaN(e.attackerPower)||float.IsNaN(e.defenderPower)||float.IsInfinity(e.attackerPower)||float.IsInfinity(e.defenderPower)||candidate.resolvedEncounters.Contains(e.id))return false;
            }
            reason="Regional manpower does not match persistent armies and recruitment.";
            foreach(var r in candidate.world.regions??Array.Empty<RegionState>())if(r!=null&&enlisted.ContainsKey(r.regionId)&&(!factions.Contains(r.owner)||r.economy==null||r.economy.enlisted!=enlisted[r.regionId]))return false;
            reason="Invalid campaign ID allocation.";int highest=0;
            foreach(var id in ids.Concat(candidate.resolvedEncounters).Concat(candidate.pending!=null?new[]{candidate.pending.id}:Array.Empty<string>()))
            {
                int n;if(string.IsNullOrEmpty(id)||!int.TryParse(id.Substring(id.LastIndexOf('-')+1),out n)||n<1)return false;highest=Math.Max(highest,n);
            }
            if(candidate.nextId<=highest)return false;
            reason="Invalid persistent army/regiment naming.";if(!RestoreNaming(candidate))return false;
            reason="Invalid regional economy state.";if(!World.RestoreState(candidate.world))return false;
            if(candidate.buildingProgressionVersion==0)
            {
                foreach(var unit in candidate.armies.SelectMany(a=>a.units))unit.legacyFacilityOrder=unit.kitTurns>0||unit.trainingTurns>0||unit.upgradeTurns>0;
                candidate.buildingProgressionVersion=CampaignBuildingTiers.Version;
            }
            bool retiredEnding=candidate.outcome!="";candidate.outcome="";candidate.victoryPath="";
            Save=candidate;CheckCampaignDefeat();if(retiredEnding&&Save.outcome=="")Event("Legacy campaign ending retired. Territorial play continues without victory quotas or a turn limit.");
            reason="Country campaign restored.";return true;
        }
    }
}
