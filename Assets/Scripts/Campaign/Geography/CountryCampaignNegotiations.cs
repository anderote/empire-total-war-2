using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    // An offer is evaluated as a whole before any money, province or technology changes hands.
    public sealed class CampaignDiplomaticProposal
    {
        public int offeredMoney,demandedMoney,truceTurns=4;
        public string offeredRegion="",demandedRegion="";
        public readonly List<string> offeredTechnologies=new List<string>(),demandedTechnologies=new List<string>();
        public bool peace,trade,access,alliance;
        public bool Empty=>offeredMoney==0&&demandedMoney==0&&offeredRegion==""&&demandedRegion==""&&offeredTechnologies.Count==0&&demandedTechnologies.Count==0&&!peace&&!trade&&!access&&!alliance;
    }

    public sealed partial class CountryCampaign
    {
        public int DiplomaticOpinion(string from,string to)
        {
            if(from==to)return 100;var t=Treaty(from,to);if(t==null)return 0;
            if(t.war)return -100;
            int gifts=t.a==from?t.goodwillA:t.goodwillB;
            int sharedEnemies=Save.countries.Count(c=>AtWar(from,c.factionId)&&AtWar(to,c.factionId));
            return Math.Max(-100,Math.Min(100,gifts+(t.alliance?45:0)+(t.trade?20:0)+(t.access?10:0)+sharedEnemies*10));
        }
        public IEnumerable<CampaignTechnology> TradableTechnologies(string giver,string receiver)=>Balance.technologies.Where(t=>Knows(giver,t.id)&&!Knows(receiver,t.id)&&ResearchAvailable(receiver,t,out _));
        bool ValidTechnologyTransfer(string giver,string receiver,List<string> ids,out string reason)
        {
            reason="Technology terms are unavailable.";
            if(ids==null||ids.Count>Balance.technologies.Length||ids.Distinct().Count()!=ids.Count)return false;
            var learned=new HashSet<string>(Progress(receiver).technologies);
            foreach(string id in ids)
            {
                var tech=Technology(id);
                if(tech==null||!Knows(giver,id)||learned.Contains(id)||Year<tech.minYear||!string.IsNullOrEmpty(tech.unavailableReason))return false;
            }
            // Permit a whole prerequisite chain in one offer, irrespective of the order of its cards.
            learned.UnionWith(ids);
            if(ids.Any(id=>Prerequisites(Technology(id)).Any(p=>!learned.Contains(p)))){reason="Include the missing prerequisite technologies in the offer.";return false;}
            return true;
        }
        public bool EvaluateProposal(string actor,string other,CampaignDiplomaticProposal p,out string reason)
        {
            reason="Choose another nation and add terms to the proposal.";
            var t=Treaty(actor,other);
            if(t==null||p==null||p.Empty||p.offeredRegion==null||p.demandedRegion==null||p.offeredMoney<0||p.demandedMoney<0||p.offeredMoney>10000||p.demandedMoney>10000)return false;
            if(World.FactionState(actor).treasury<p.offeredMoney||World.FactionState(other).treasury<p.demandedMoney){reason="The paying nation cannot afford these terms.";return false;}
            if(p.offeredRegion!=""&&(!Active(p.offeredRegion)||World.State(p.offeredRegion).owner!=actor)||p.demandedRegion!=""&&(!Active(p.demandedRegion)||World.State(p.demandedRegion).owner!=other)){reason="Only an owned campaign province can be offered.";return false;}
            if(!ValidTechnologyTransfer(actor,other,p.offeredTechnologies,out reason)||!ValidTechnologyTransfer(other,actor,p.demandedTechnologies,out reason))return false;
            if(t.war&&!p.peace){reason="A peace treaty must be included before other agreements can be made.";return false;}
            if(p.peace&&!t.war){reason="These nations are already at peace.";return false;}
            if(!p.peace&&(p.trade&&t.trade||p.access&&t.access||p.alliance&&t.alliance)){reason="Remove agreements that are already in force.";return false;}
            if(p.alliance&&!t.trade&&!p.trade){reason="A defensive alliance requires an existing or proposed trade agreement.";return false;}
            int offeredTech=p.offeredTechnologies.Sum(id=>Technology(id).cost*2),demandedTech=p.demandedTechnologies.Sum(id=>Technology(id).cost*2);
            int net=p.offeredMoney-p.demandedMoney;
            if(p.peace)
            {
                if(!EvaluatePeace(actor,other,net,p.offeredRegion,p.demandedRegion,p.truceTurns,out reason))return false;
                // Peace cannot be used to obtain extra technology or an alliance for free.
                if(demandedTech>offeredTech+Math.Max(0,net)||(p.alliance&&net+offeredTech<200)){reason="The additional agreements require a stronger offer.";return false;}
            }
            else
            {
                int value=net+ProvinceValue(p.offeredRegion)-ProvinceValue(p.demandedRegion)+offeredTech-demandedTech;
                int cost=(p.alliance?200:0)+(p.access?50:0);
                int willingness=Math.Max(0,DiplomaticOpinion(other,actor))*2;
                if(value+willingness<cost){reason="They ask for a better offer. Add money, technology or territory, or reduce your demands.";return false;}
            }
            reason="These terms are acceptable.";return true;
        }
        public bool Negotiate(string actor,string other,CampaignDiplomaticProposal p,out string reason)
        {
            reason="Negotiations are unavailable during a battle or another nation's turn.";
            if(!CanOrder(actor)||!EvaluateProposal(actor,other,p,out reason))return false;
            var t=Treaty(actor,other);int net=p.offeredMoney-p.demandedMoney;
            if(p.peace)
            {
                // EvaluateProposal has already checked the complete bundle; this call cannot partially fail.
                if(!NegotiatePeace(actor,other,net,p.offeredRegion,p.demandedRegion,p.truceTurns,out reason))return false;
            }
            else
            {
                World.FactionState(actor).treasury-=net;World.FactionState(other).treasury+=net;
                CedeTreatyProvince(p.offeredRegion,other);CedeTreatyProvince(p.demandedRegion,actor);
            }
            void Learn(string receiver,List<string> ids)
            {
                var progress=Progress(receiver);progress.technologies.AddRange(ids);
                if(ids.Contains(progress.research)){progress.research="";progress.researchProgress=0;}
            }
            Learn(other,p.offeredTechnologies);Learn(actor,p.demandedTechnologies);
            if(p.trade)t.trade=true;if(p.access)t.access=true;if(p.alliance){t.alliance=true;t.access=true;}
            bool gift=!p.peace&&!p.trade&&!p.access&&!p.alliance&&p.demandedMoney==0&&p.demandedRegion==""&&p.demandedTechnologies.Count==0;
            if(gift){int goodwill=Math.Min(100,(p.offeredMoney+p.offeredTechnologies.Sum(id=>Technology(id).cost*2)+ProvinceValue(p.offeredRegion))/10);if(t.a==other)t.goodwillA=Math.Min(100,t.goodwillA+goodwill);else t.goodwillB=Math.Min(100,t.goodwillB+goodwill);}
            reason="Agreement signed with "+FactionName(other)+".";RecordHistory("Diplomacy","",reason);Event(reason);return true;
        }
        void CedeTreatyProvince(string region,string owner)
        {
            if(string.IsNullOrEmpty(region))return;var r=World.State(region);string former=r.owner;
            r.owner=owner;r.economy.occupationTurns=2;r.economy.unrest=Math.Min(100,r.economy.unrest+10);
            var politics=Politics(region);politics.loyalist=former;politics.policy="Reconcile";politics.unrestTurns=0;
            foreach(var q in Save.recruitment.Where(q=>q.regionId==region).ToArray()){r.economy.enlisted-=q.soldiers;Save.recruitment.Remove(q);}
            foreach(var b in r.buildings.Where(b=>b.turnsRemaining>0)){b.turnsRemaining=0;b.workRemaining=0;b.paidCost=0;if(b.upgrading){b.level--;b.upgrading=false;}else b.kind="Empty";}
            RecordHistory("Treaty territory",region,Place(region)+" ceded to "+owner+" by treaty.");
        }
        void RecordTradeShipment(string seller,string buyer,string origin,string destination,float quantity,int payment)
        {
            if(quantity<=0||seller==buyer)return;
            var shipment=Save.lastTradeShipments.Find(s=>s.seller==seller&&s.buyer==buyer&&s.origin==origin&&s.destination==destination);
            if(shipment==null){shipment=new CampaignTradeShipment{seller=seller,buyer=buyer,origin=origin,destination=destination};Save.lastTradeShipments.Add(shipment);}
            shipment.quantity+=quantity;shipment.payment+=payment;
        }
    }
}
