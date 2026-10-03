using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        readonly Dictionary<string,Dictionary<string,float>> seaGraph=new Dictionary<string,Dictionary<string,float>>();
        public static bool AtSea(CampaignArmy army)=>army!=null&&!string.IsNullOrEmpty(army.seaTarget);
        public bool HasPort(string region)=>region!=null&&seaGraph.ContainsKey(region);
        public IEnumerable<string> Ports=>seaGraph.Keys.OrderBy(id=>id,StringComparer.Ordinal);
        public bool Blockaded(string actor,string port)=>Save.blockades.Any(b=>b.regionId==port&&b.turns>0&&AtWar(actor,b.owner)&&World.State(b.origin).owner==b.owner);
        static float SeaDistance(Settlement a,Settlement b)
        {
            double rad=Math.PI/180,lat=(a.lat+b.lat)*.5*rad;
            return (float)Math.Max(40,Math.Sqrt(Math.Pow((a.lon-b.lon)*Math.Cos(lat),2)+Math.Pow(a.lat-b.lat,2))*111.32*1.15);
        }
        void BuildSeaGraph(TheaterData geography)
        {
            foreach(var region in World.Catalog.regions.Where(r=>Active(r.id)&&r.capital.port))seaGraph[region.id]=new Dictionary<string,float>();
            var towns=geography.settlements.Concat(geography.waypoints??Array.Empty<Settlement>()).GroupBy(t=>t.id).ToDictionary(g=>g.Key,g=>g.First());
            void Link(string a,string b,float distance=0)
            {
                if(a==null||b==null||a==b||!Active(a)||!Active(b))return;
                if(!seaGraph.ContainsKey(a))seaGraph[a]=new Dictionary<string,float>();
                if(!seaGraph.ContainsKey(b))seaGraph[b]=new Dictionary<string,float>();
                float cost=distance>0?distance:SeaDistance(World.Catalog.regions.First(r=>r.id==a).capital,World.Catalog.regions.First(r=>r.id==b).capital);
                seaGraph[a][b]=cost;seaGraph[b][a]=cost;
            }
            foreach(var route in geography.routes.Where(r=>r.mode=="sea"))
            {
                if(!towns.TryGetValue(route.fromId,out var a)||!towns.TryGetValue(route.toId,out var b))continue;
                Link((World.Catalog.ForCity(a.id)??World.Catalog.At(a.lon,a.lat))?.id,(World.Catalog.ForCity(b.id)??World.Catalog.At(b.lon,b.lat))?.id,route.distanceKm);
            }
            // Authored coastal shipping lanes supplement the atlas's sparse sea crossings.
            string[] lanes={"marseille|ajaccio","marseille|genoa","ajaccio|cagliari","cagliari|palermo","naples|catania","messina|catania",
                "brest|rouen","rouen|london","brest|bristol","bristol|liverpool","dublin|cork","liverpool|glasgow","glasgow|belfast",
                "london|amsterdam","amsterdam|rotterdam","rotterdam|antwerp","amsterdam|hamburg","hamburg|oslo","oslo|copenhagen",
                "stockholm|riga","riga|tallinn","tallinn|helsinki","brest|nantes","nantes|bordeaux","bordeaux|bilbao","bilbao|porto",
                "porto|lisbon","lisbon|cadiz","barcelona|valencia","valencia|marseille","palermo|valletta"};
            foreach(var lane in lanes){var ends=lane.Split('|');Link("region-"+ends[0],"region-"+ends[1]);}
        }
        public CampaignRoute SeaRoute(string from,string to)
        {
            if(!HasPort(from)||!HasPort(to)||from==to)return null;
            var costs=new Dictionary<string,float>{{from,0}};var previous=new Dictionary<string,string>();var done=new HashSet<string>();
            while(true)
            {
                string node=costs.Where(x=>!done.Contains(x.Key)).OrderBy(x=>x.Value).ThenBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key).FirstOrDefault();
                if(node==null)return null;if(node==to)break;done.Add(node);
                foreach(var edge in seaGraph[node])if(!costs.ContainsKey(edge.Key)||costs[node]+edge.Value<costs[edge.Key])
                {costs[edge.Key]=costs[node]+edge.Value;previous[edge.Key]=node;}
            }
            var route=new CampaignRoute{distance=costs[to]};string at=to;route.regions.Add(at);
            while(at!=from){at=previous[at];route.regions.Insert(0,at);}return route;
        }
        public int TransportCost(CampaignArmy army,string destination)
        {var route=army==null?null:SeaRoute(army.regionId,destination);return route==null?0:80+(int)Math.Ceiling(army.Soldiers*.025+route.distance*.04);}
        public int TransportTurns(string origin,string destination)=>Math.Max(1,(int)Math.Ceiling((SeaRoute(origin,destination)?.distance??0)/700));
        public bool CanSail(string actor,CampaignArmy army,string destination,out string reason)
        {
            reason="A stationary army must embark from an owned port with movement remaining.";
            if(!CanOrder(actor)||army==null||army.owner!=actor||Busy(army)||!AtSettlement(army)||UnderSiegeOrders(army)||army.movement<=0||World.State(army.regionId).owner!=actor||(Besieged(army.regionId)||InRevolt(army.regionId)))return false;
            if(SeaRoute(army.regionId,destination)==null){reason="No connected shipping route between these ports.";return false;}
            string owner=World.State(destination).owner;
            if(owner!=actor&&!AtWar(actor,owner)&&!(Treaty(actor,owner)?.access??false)){reason="Landing requires military access or war.";return false;}
            if(Blockaded(actor,army.regionId)||Blockaded(actor,destination)){reason="Lift the hostile naval blockade before sailing.";return false;}
            if(World.FactionState(actor).treasury<TransportCost(army,destination)){reason="Insufficient treasury for transports and provisions.";return false;}
            reason="Includes transports and provisions. Enemy ports require a landing battle.";return true;
        }
        public bool Sail(string actor,string armyId,string destination,out string reason)
        {
            var army=Army(armyId);if(!CanSail(actor,army,destination,out reason))return false;
            World.FactionState(actor).treasury-=TransportCost(army,destination);
            army.stance="March";army.seaFrom=army.regionId;army.seaTarget=destination;army.seaTurns=army.seaDuration=TransportTurns(army.regionId,destination);
            ClearMarch(army);army.movement=0;
            reason="Army embarked: "+army.seaTurns+" turn(s). Its troops and equipment travel together.";Event(actor+" embarked for "+destination+".");return true;
        }
        void ClearVoyage(CampaignArmy army){army.seaFrom=army.seaTarget="";army.seaTurns=army.seaDuration=0;}
        void ReturnFromLanding(CampaignArmy army)
        {
            string from=army.seaTarget;
            string home=Ports.Where(p=>World.State(p).owner==army.owner&&SeaRoute(from,p)!=null&&!Blockaded(army.owner,p))
                .OrderBy(p=>SeaRoute(from,p).distance).FirstOrDefault()??army.regionId;
            army.seaFrom=from;army.seaTarget=home;army.seaDuration=army.seaTurns=TransportTurns(from,home);army.movement=0;
        }
        public bool ReturnTransport(string actor,string armyId,out string reason)
        {
            reason="No transport is available to turn back.";var army=Army(armyId);
            if(!CanOrder(actor)||army==null||army.owner!=actor||!AtSea(army))return false;
            if(army.seaTarget==army.regionId){reason="Already returning to the departure port.";return false;}
            if(army.seaTurns==army.seaDuration&&World.State(army.seaFrom).owner==actor)
            {PlaceAtCity(army,army.seaFrom);ClearVoyage(army);reason="Embarkation cancelled; charter cost remains spent.";return true;}
            if(army.seaTurns>0&&World.State(army.seaFrom).owner==actor)
            {string from=army.seaFrom;army.seaFrom=army.seaTarget;army.seaTarget=from;army.seaTurns=Math.Max(1,army.seaDuration-army.seaTurns);}
            else ReturnFromLanding(army);
            reason="Transports returning; the charter is not refunded.";return true;
        }
        void LandTransport(CampaignArmy army)
        {
            if(!AtSea(army)||army.seaTurns>0||Save.pending!=null)return;
            string destination=army.seaTarget,owner=World.State(destination).owner;
            if(Blockaded(army.owner,destination)){Event(army.owner+" transports await relief of the blockade at "+destination+".");return;}
            if(owner!=army.owner&&!AtWar(army.owner,owner)&&!(Treaty(army.owner,owner)?.access??false)){ReturnFromLanding(army);return;}
            if(AtWar(army.owner,owner)||Save.armies.Any(a=>!AtSea(a)&&a.regionId==destination&&AtWar(army.owner,a.owner)))
                CreateEncounter(army,destination,true);
            else {PlaceAtCity(army,destination);army.movement=0;ClearVoyage(army);Event(army.owner+" disembarked at "+destination+".");}
        }
        public bool CommissionBlockade(string actor,string origin,string destination,out string reason)
        {
            reason="Blockade requires an owned, open port, a connected enemy port, 250 treasury and no existing squadron there.";
            if(!Owns(actor,origin)||Besieged(origin)||Blockaded(actor,origin)||SeaRoute(origin,destination)==null||!AtWar(actor,World.State(destination).owner)||
                Save.blockades.Any(b=>b.regionId==destination&&b.owner==actor)||World.FactionState(actor).treasury<250)return false;
            World.FactionState(actor).treasury-=250;Save.blockades.Add(new CampaignBlockade{id=Id("blockade-"),owner=actor,origin=origin,regionId=destination,turns=3});
            reason="Squadron commissioned for three turns. Blockade stops hostile transports and reduces port revenue; upkeep 20 per turn.";Event(actor+" blockaded "+destination+".");return true;
        }
        public bool BreakBlockade(string actor,string port,out string reason)
        {
            reason="Relief requires an owned port under blockade and 200 treasury.";
            if(!Owns(actor,port)||!Blockaded(actor,port)||World.FactionState(actor).treasury<200)return false;
            World.FactionState(actor).treasury-=200;Save.blockades.RemoveAll(b=>b.regionId==port&&AtWar(actor,b.owner));
            reason="Relief squadron opened the shipping lanes.";Event(actor+" lifted the blockade at "+port+".");return true;
        }
        void AdvanceBlockades()
        {
            var charged=new HashSet<string>();
            foreach(var b in Save.blockades.ToArray())
            {
                string defender=World.State(b.regionId).owner;var funds=World.FactionState(b.owner);
                if(World.State(b.origin).owner!=b.owner||!AtWar(b.owner,defender)||funds.treasury<20){Save.blockades.Remove(b);continue;}
                funds.treasury-=20;funds.lastExpenses+=20;
                int loss=Math.Max(10,World.Economy.Forecast(World.State(b.regionId)).taxes/2);
                if(charged.Add(b.regionId)){var affected=World.FactionState(defender);affected.treasury-=loss;affected.lastExpenses+=loss;}
                if(--b.turns==0)Save.blockades.Remove(b);
            }
        }
        bool ThinkSeaTransport(string actor,CampaignArmy army)
        {
            if(AtSea(army)){LandTransport(army);return true;}
            if(Busy(army)||!AtSettlement(army)||UnderSiegeOrders(army)||army.units.Count<3)return false;
            var target=Ports.Where(p=>AtWar(actor,World.State(p).owner)&&Route(actor,army.regionId,p)==null)
                .OrderBy(p=>KnownDefense(actor,p)).ThenBy(p=>p,StringComparer.Ordinal).FirstOrDefault(p=>ArmyPower(army)>KnownDefense(actor,p)*1.25f);
            if(target==null)return false;
            if(CanSail(actor,army,target,out _))return Sail(actor,army.id,target,out _);
            var embark=Owned(actor).Where(r=>HasPort(r.regionId)&&!Blockaded(actor,r.regionId)&&SeaRoute(r.regionId,target)!=null)
                .Select(r=>Route(actor,army.regionId,r.regionId)).Where(r=>r!=null&&r.distance>0).OrderBy(r=>r.distance).FirstOrDefault();
            return embark!=null&&March(actor,army.id,embark.regions.Last(),out _);
        }
        void ThinkBlockades(string actor)
        {
            if(World.FactionState(actor).treasury<Balance.reserveBudget+300)return;
            foreach(var port in Owned(actor).Where(r=>HasPort(r.regionId)))if(BreakBlockade(actor,port.regionId,out _))return;
            if(Save.blockades.Any(b=>b.owner==actor))return;
            foreach(var home in Owned(actor).Where(r=>HasPort(r.regionId)))
            foreach(var target in Ports.Where(p=>AtWar(actor,World.State(p).owner)).OrderBy(p=>p,StringComparer.Ordinal))
                if(SeaRoute(home.regionId,target)?.distance<1200&&CommissionBlockade(actor,home.regionId,target,out _))return;
        }
    }
}
