using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    [Serializable] public sealed class RepublicStory
    {
        public int stability=60,lastReliefTurn,napoleonStage,napoleonPrestige;
        public string government="Convention",decision="",ending="";
        public List<string> milestones=new List<string>();
    }
    public sealed partial class CountryCampaign
    {
        public const string RepublicScenario="republic-1792";
        public bool IsRepublic=>Save.scenarioId==RepublicScenario;
        static readonly string[] storyEvents={"coalition","toulon","thermidor","directory","italy","campo-formio","second-coalition","brumaire","constitution"};
        public string StoryChapter=>Year<1793?"I / THE REPUBLIC AT WAR":Year<1795?"II / WAR AND REVOLUTION":Year<1796?"III / THE DIRECTORY":Year<1799?"IV / THE ITALIAN OPPORTUNITY":Year<1804?"V / WHO SHALL GOVERN FRANCE?":"VI / THE FUTURE OF FRANCE";
        public string NapoleonStatus=>Save.story.napoleonStage==0?"Bonaparte: junior artillery officer":Save.story.napoleonStage==1?"Bonaparte: general, awaiting a field command":"Bonaparte: field commander / "+Save.story.napoleonPrestige+" victories";
        public string CoalitionLabel=>string.Join(", ",Save.countries.Where(c=>AtWar("france",c.factionId)).Select(c=>FactionName(c.factionId)));
        public string FactionName(string id)
        {
            if(IsRepublic)
            {
                if(id=="france")return Save.story.government=="Empire"?"French Empire":"French Republic";
                if(id=="batavian")return "Dutch Republic";
                if(id=="helvetic")return "Swiss Confederacy";
                if(id=="ligurian")return "Republic of Genoa";
            }
            return World.Catalog.Faction(id).name;
        }
        void RestoreRepublicBorders()
        {
            // Region geography is the shared atlas. Correct the key pre-conquest frontier controllers.
            foreach(var id in new[]{"region-brussels","region-antwerp","region-milan"})World.State(id).owner="austria";
            World.State("region-turin").owner="sardinia";World.State("region-bologna").owner="papal";
        }
        bool Once(string id)
        {
            if(Save.story.milestones.Contains(id))return false;
            Save.story.milestones.Add(id);return true;
        }
        void CoalitionWar(string faction)
        {
            var t=Treaty("france",faction);if(t==null||t.war||Owned(faction).Count==0)return;
            // A negotiated truce is binding; historical announcements never revoke a player's treaty.
            if(t.peaceUntil>Save.world.turn){Event(FactionName(faction)+" joins the coalition diplomatically, but its current truce with France is respected.");return;}
            t.war=true;t.access=false;t.trade=false;t.alliance=false;t.warStart=Save.world.turn;t.warScoreA=0;
            Event(FactionName(faction)+" enters the war against France. Its armies must recruit, supply and march under the normal campaign rules.");
        }
        void Offer(string id,string message){Save.story.decision=id;Event(message+" Open Nation to decide.");}
        void AdvanceRepublicStory()
        {
            if(!IsRepublic)return;
            var s=Save.story;var own=Owned("france");
            int enemies=Save.countries.Count(c=>AtWar("france",c.factionId));
            bool supplied=own.Count>0&&own.All(r=>r.economy.wellbeing>=50);
            s.stability=Math.Max(0,Math.Min(100,s.stability+(supplied?1:-2)-(enemies>=5?1:0)-(Progress("france").policy=="Mobilization"?1:0)-(World.State("region-paris").owner!="france"?3:0)));
            if(s.stability<35)foreach(var r in own)r.economy.unrest=Math.Min(100,r.economy.unrest+3);
            if(Year>=1793&&Once("coalition"))
            {
                foreach(var enemy in new[]{"britain","batavian","spain","portugal","naples"})CoalitionWar(enemy);
                foreach(var id in new[]{"region-nantes","region-brest","region-marseille"})if(World.State(id).owner=="france")World.State(id).economy.unrest=Math.Min(100,World.State(id).economy.unrest+25);
                Offer("coalition","1793: the First Coalition expands. Revolt and shortages threaten the west and south. Fund mobilization or protect civilian confidence.");
            }
            if(Year>=1793&&(Save.world.turn-1+Balance.startSeason)%2==1&&(World.State("region-marseille").owner=="france"||Save.history.Count(h=>h.kind=="Battle"&&h.text.StartsWith("france won"))>=2)&&Once("toulon"))
            {s.napoleonStage=1;Event("1793 / Toulon: in the historical campaign Bonaparte's artillery service brought promotion to general. He now becomes eligible for a later field command. Toulon is represented within the Marseille region; no siege result or territory is awarded.");}
            if(Year>=1794&&(Save.world.turn-1+Balance.startSeason)%2==1&&Once("thermidor"))
            {s.stability=Math.Max(0,s.stability-8);Offer("thermidor","1794 / Thermidor: the Convention faces the aftermath of the Terror. Invest in reconciliation or maintain emergency controls.");}
            if(Year>=1795&&(Save.world.turn-1+Balance.startSeason)%2==1&&Once("directory"))
            {s.government="Directory";Event("1795: the Directory replaces the Convention. Royalist opposition and military dependence leave the new government fragile. Seek separate peace where possible.");}
            if(Year>=1796&&Once("italy"))Offer("italy","1796: choose whether to give General Bonaparte an existing French field army for the Italian campaign. No soldiers or equipment are created by appointment.");
            if(Year>=1797&&(Save.world.turn-1+Balance.startSeason)%2==1&&!s.milestones.Contains("campo-formio")&&new[]{"region-turin","region-milan"}.All(id=>World.State(id).owner=="france"))
            {Once("campo-formio");Offer("campo-formio","Italian success opens a Campo Formio peace opportunity with Austria. You may negotiate or keep campaigning.");}
            if(Year>=1799&&Once("second-coalition"))
            {foreach(var enemy in new[]{"austria","russia","britain","ottoman","naples"})CoalitionWar(enemy);Event("1799: a Second Coalition challenges France. Prussia and Spain do not automatically re-enter. The Egyptian expedition remains historical context outside this European theater.");}
            if(Year>=1799&&(Save.world.turn-1+Balance.startSeason)%2==1&&Once("brumaire"))Offer("brumaire","1799 / Brumaire: the government faces a constitutional crisis. Back a Bonapartist Consulate or defend a civilian republic.");
            if(Year>=1804&&Once("constitution"))Offer("constitution","1804: decide France's settlement. A Consulate can become an Empire; a civilian republic can affirm its constitution.");
            if(autoplay&&s.decision!=""&&!DecideRepublic(s.decision,0,out _))DecideRepublic(s.decision,1,out _);
        }
        public string DecisionTitle=>Save.story.decision=="coalition"?"THE LEVEE EN MASSE":Save.story.decision=="thermidor"?"AFTER THERMIDOR":Save.story.decision=="italy"?"A COMMAND FOR BONAPARTE":Save.story.decision=="campo-formio"?"PEACE IN ITALY":Save.story.decision=="brumaire"?"18 BRUMAIRE":Save.story.decision=="constitution"?"REPUBLIC OR EMPIRE":"";
        public string[] DecisionOptions
        {
            get
            {
                switch(Save.story.decision)
                {
                    case "coalition":return new[]{"Fund mobilization / 200 / 18% tax, more manpower, -8 stability","Keep normal levies / no cost / +3 stability"};
                    case "thermidor":return new[]{"Reconcile communities / 200 / +12 stability, reduce unrest","Retain emergency controls / no cost / -6 stability"};
                    case "italy":return new[]{"Appoint Bonaparte / 100 / assign an existing field army","Retain the current command / no cost / +4 stability"};
                    case "campo-formio":return new[]{"Accept peace / no cost / four-turn Austrian truce","Continue the war / no cost / +3 stability"};
                    case "brumaire":return new[]{"Back the Consulate / 250 / Bonaparte command and 2 victories required / +15 stability","Defend the civilian Republic / no cost / +4 stability"};
                    case "constitution":return Save.story.government=="Consulate"?new[]{"Proclaim the Empire / 300 / +8 stability","Preserve the Consulate / no cost / +4 stability"}:new[]{"Affirm a civilian constitution / 150 / +8 stability","Preserve the wartime Republic / no cost / -4 stability"};
                    default:return Array.Empty<string>();
                }
            }
        }
        public bool DecideRepublic(string decision,int option,out string reason)
        {
            reason="Decision unavailable.";
            if(!IsRepublic||!CanOrder("france")||decision==""||Save.story.decision!=decision||option<0||option>1)return false;
            var s=Save.story;var funds=World.FactionState("france");int cost=0,stability=0;
            switch(decision)
            {
                case "coalition":cost=option==0?200:0;stability=option==0?-8:3;break;
                case "thermidor":cost=option==0?200:0;stability=option==0?12:-6;break;
                case "italy":cost=option==0?100:0;stability=option==0?0:4;break;
                case "campo-formio":stability=option==0?0:3;break;
                case "brumaire":cost=option==0?250:0;stability=option==0?15:4;break;
                case "constitution":cost=option==0?(s.government=="Consulate"?300:150):0;stability=option==0?8:s.government=="Consulate"?4:-4;break;
                default:return false;
            }
            var army=Save.armies.Where(a=>a.owner=="france"&&!Committed(a)).OrderBy(a=>a.regionId=="region-lyon"?0:1).ThenByDescending(a=>a.Soldiers).FirstOrDefault();
            if(decision=="italy"&&option==0&&(army==null||s.napoleonStage<1)){reason="A stationary French field army and Bonaparte's promotion are required.";return false;}
            if(decision=="brumaire"&&option==0&&(s.napoleonStage!=2||s.napoleonPrestige<2||!Save.armies.Any(a=>a.owner=="france"&&a.generalName=="Napoleon Bonaparte"))){reason="Bonaparte needs a surviving field command and two campaign victories before you can back his Consulate.";return false;}
            if(cost>0&&funds.treasury<cost){reason="Insufficient treasury: "+cost+" required.";return false;}
            string selectedOption=DecisionOptions[option];
            funds.treasury-=cost;s.stability=Math.Max(0,Math.Min(100,s.stability+stability));
            if(decision=="coalition")Progress("france").policy=option==0?"Mobilization":"Balanced";
            if(decision=="coalition"&&option==1||decision=="thermidor"&&option==0)foreach(var r in Owned("france"))r.economy.unrest=Math.Max(0,r.economy.unrest-(decision=="coalition"?5:15));
            if(decision=="italy"&&option==0){if(!army.customName)army.name="Army of Italy";army.generalName="Napoleon Bonaparte";army.commanderXP=army.commanderWounds=army.commanderWins=0;army.commanderTrait="";s.napoleonStage=2;}
            if(decision=="campo-formio"&&option==0){var t=Treaty("france","austria");t.war=false;t.access=false;t.trade=false;t.alliance=false;t.peaceUntil=Save.world.turn+4;}
            if(decision=="brumaire")s.government=option==0?"Consulate":"Civilian Republic";
            if(decision=="constitution"){s.ending=s.government=="Consulate"?(option==0?"Empire":"Consulate"):"Republic";s.government=s.ending=="Republic"?"Civilian Republic":s.ending;}
            Event(DecisionTitle+": "+selectedOption+". Paid "+cost+" treasury.");s.decision="";reason="Decision enacted. Territorial play continues.";return true;
        }
        public bool FundRepublicRelief(out string reason)
        {
            reason="Relief unavailable.";if(!IsRepublic||!CanOrder("france"))return false;
            if(Save.story.lastReliefTurn==Save.world.turn){reason="Relief has already been funded this turn.";return false;}
            var funds=World.FactionState("france");if(funds.treasury<200){reason="Relief requires 200 treasury.";return false;}
            funds.treasury-=200;Save.story.lastReliefTurn=Save.world.turn;Save.story.stability=Math.Min(100,Save.story.stability+6);
            foreach(var r in Owned("france"))r.economy.unrest=Math.Max(0,r.economy.unrest-10);
            reason="Civilian relief funded: +6 stability and -10 regional unrest.";Event(reason);return true;
        }
        bool ValidRepublicSave(CountryCampaignSave c)
        {
            if((c.scenarioId??"")!=(Save.scenarioId??""))return false;
            if(!IsRepublic)return string.IsNullOrEmpty(c.scenarioId); // old 1800 saves stay in their original scenario
            var s=c.story;if(c.schemaVersion<5||s==null||s.milestones==null||s.stability<0||s.stability>100||s.lastReliefTurn<0||s.lastReliefTurn>c.world.turn||s.napoleonStage<0||s.napoleonStage>2||s.napoleonPrestige<0||s.napoleonPrestige>100)return false;
            if(!new[]{"Convention","Directory","Civilian Republic","Consulate","Empire"}.Contains(s.government)||!new[]{"","Republic","Consulate","Empire"}.Contains(s.ending))return false;
            if(s.milestones.Distinct().Count()!=s.milestones.Count||s.milestones.Any(id=>!storyEvents.Contains(id))||s.decision!=""&&(!new[]{"coalition","thermidor","italy","campo-formio","brumaire","constitution"}.Contains(s.decision)||!s.milestones.Contains(s.decision)))return false;
            int year=Balance.startYear+(c.world.turn-1+Balance.startSeason)/2;
            if(s.milestones.Any(id=>year<(id=="coalition"||id=="toulon"?1793:id=="thermidor"?1794:id=="directory"?1795:id=="italy"?1796:id=="campo-formio"?1797:id=="constitution"?1804:1799)))return false;
            if(s.government=="Directory"&&!s.milestones.Contains("directory")||new[]{"Civilian Republic","Consulate","Empire"}.Contains(s.government)&&!s.milestones.Contains("brumaire")||s.government=="Empire"&&s.ending!="Empire")return false;
            return (s.napoleonStage==0||s.milestones.Contains("toulon"))&&(s.napoleonStage<2||s.milestones.Contains("italy"))&&(s.ending==""||s.milestones.Contains("constitution"));
        }
    }
}
