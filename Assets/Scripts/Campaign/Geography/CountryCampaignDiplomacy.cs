using System;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        public int WarScore(string actor,string other){var t=Treaty(actor,other);return t==null?0:t.a==actor?t.warScoreA:-t.warScoreA;}
        int ProvinceValue(string region)=>string.IsNullOrEmpty(region)?0:250+Buildings(region,"Fortress")*100+World.Economy.Forecast(World.State(region)).taxes*3;
        public bool EvaluatePeace(string actor,string other,int payment,string giveRegion,string demandRegion,int truceTurns,out string reason)
        {
            reason="Peace requires a war, affordable terms and a two-, four- or six-turn truce.";
            var t=Treaty(actor,other);
            if(t==null||!t.war||!new[]{2,4,6}.Contains(truceTurns)||payment<-10000||payment>10000)return false;
            if(payment>0&&payment>World.FactionState(actor).treasury||payment<0&&-payment>World.FactionState(other).treasury){reason="The paying faction cannot afford this settlement.";return false;}
            if(!string.IsNullOrEmpty(giveRegion)&&(!Active(giveRegion)||World.State(giveRegion).owner!=actor)||
                !string.IsNullOrEmpty(demandRegion)&&(!Active(demandRegion)||World.State(demandRegion).owner!=other))return false;
            int age=Math.Max(0,Save.world.turn-t.warStart);
            float military=20*(Power(actor)-Power(other))/Math.Max(100,Power(actor)+Power(other));
            float offer=(payment+ProvinceValue(giveRegion)-ProvinceValue(demandRegion))/25f;
            float pressure=Math.Min(30,age*5)+WarScore(actor,other)*.6f+military+offer-(truceTurns-2)*2;
            reason=pressure>=15?"These peace terms are acceptable.":"They refuse these peace terms. Offer more, reduce your demands, or improve your position in the war.";
            return pressure>=15;
        }
        public bool NegotiatePeace(string actor,string other,int payment,string giveRegion,string demandRegion,int truceTurns,out string reason)
        {
            reason="Peace cannot be signed during a battle or another faction's turn.";
            if(!CanOrder(actor)||!EvaluatePeace(actor,other,payment,giveRegion,demandRegion,truceTurns,out reason))return false;
            var t=Treaty(actor,other);World.FactionState(actor).treasury-=payment;World.FactionState(other).treasury+=payment;
            CedeTreatyProvince(giveRegion,other);CedeTreatyProvince(demandRegion,actor);
            t.war=false;t.trade=false;t.alliance=false;t.access=true;t.peaceUntil=Save.world.turn+truceTurns;
            CleanSieges();Save.blockades.RemoveAll(b=>(b.owner==actor&&World.State(b.regionId).owner==other)||(b.owner==other&&World.State(b.regionId).owner==actor));
            reason=actor+" and "+other+" signed a "+truceTurns+"-turn truce; net payment "+payment+" from "+actor+". Temporary access allows armies to withdraw.";
            RecordHistory("Peace","",reason);Event(reason);return true;
        }
        void RecordWarResult(CampaignEncounter e,bool win)
        {
            var t=Treaty(e.attacker,e.defender);if(t==null)return;
            int score=(win?1:-1)*(e.fieldOnly?6:10)*(t.a==e.attacker?1:-1);t.warScoreA=Math.Max(-100,Math.Min(100,t.warScoreA+score));
        }
        void ExpireTruces()
        {
            foreach(var t in Save.treaties.Where(t=>!t.war&&t.peaceUntil>0&&t.peaceUntil==Save.world.turn&&!t.alliance))
            {
                // Withdrawal is part of the signed treaty, not a free offensive redeployment.
                foreach(var a in Save.armies.Where(a=>!AtSea(a)&&(a.owner==t.a&&World.State(a.regionId).owner==t.b||a.owner==t.b&&World.State(a.regionId).owner==t.a)))
                {
                    var home=Owned(a.owner).OrderBy(r=>{var x=World.Catalog.regions.First(z=>z.id==a.regionId).capital;var y=World.Catalog.regions.First(z=>z.id==r.regionId).capital;return Math.Abs(x.lon-y.lon)+Math.Abs(x.lat-y.lat);}).FirstOrDefault();
                    if(home!=null){PlaceAtCity(a,home.regionId);a.movement=0;ClearMarch(a);a.stance="March";}
                }
                t.access=false;RecordHistory("Truce expired","",t.a+"–"+t.b+" truce expired. Remaining visiting armies withdrew where a home province was available.");
            }
        }
    }
}
