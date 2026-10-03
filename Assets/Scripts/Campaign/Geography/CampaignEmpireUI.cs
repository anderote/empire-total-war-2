using System;
using System.Linq;
using Empire.UI;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        string governmentTab="Policies",campaignListTab="Armies",researchBranch="Military",focusedTechnology="",listSearch="",armyListSort="Name";
        bool negotiationsOpen,proposalRegions,proposalTechnologies,opinionsIncoming=true;
        CampaignDiplomaticProposal campaignProposal=new CampaignDiplomaticProposal();
        Vector2 diplomacyFlagsScroll;
        string diplomaticReply="";

        void CampaignTabs(string[] tabs,ref string selection)
        {
            GUILayout.BeginHorizontal();
            foreach(string tab in tabs)if(GUILayout.Button((selection==tab?"• ":"")+tab,campaignAction)){selection=tab;GUI.FocusControl(null);}
            GUILayout.EndHorizontal();GUILayout.Space(14);
        }
        void DrawCampaignGovernment(float width)
        {
            CampaignTabs(new[]{"Policies","Government","Trade"},ref governmentTab);
            var c=countryCampaign;string actor=c.Save.playerFaction;var p=c.Progress(actor);var own=c.Owned(actor);var funds=c.World.FactionState(actor);
            if(governmentTab=="Trade"){DrawCampaignTrade(width);return;}
            if(governmentTab=="Government")
            {
                GUILayout.Label(c.FactionName(actor),heading);
                if(c.IsRepublic)DrawRepublicStory();else GUILayout.Label(c.OpeningAdvice,text);
                return;
            }
            GUILayout.Label("Treasury "+funds.treasury.ToString("N0")+"   /   last turn "+p.lastNet.ToString("+0;-0;0"),heading);
            GUILayout.Label("Taxes "+funds.lastTaxes+"   ·   trade "+funds.lastTradeBalance.ToString("+0;-0;0")+"   ·   expenditure "+funds.lastExpenses,small);
            GUILayout.Space(12);GUILayout.Label("National policy",campaignCaption);
            foreach(string policy in new[]{"Balanced","Investment","Mobilization"})
            {
                GUILayout.BeginHorizontal();
                string effect=policy=="Balanced"?"15% taxation":policy=="Investment"?"12% taxation · construction and research":"18% taxation · manpower and unrest";
                GUILayout.Label(effect,text);
                if(CampaignOrderButton(policy+(p.policy==policy?" / current":" / 200"),p.policy!=policy)&&c.Reform(actor,policy,out countryMessage))SaveCountry(false);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(18);GUILayout.Label("Regional administration",campaignCaption);
            foreach(var state in own.OrderBy(r=>CampaignPlace(r.regionId)))
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(CampaignPlace(state.regionId),campaignAction,GUILayout.Width(220))){SelectCampaignRegion(state.regionId);countryPage="Map";}
                GUILayout.Label("Income "+c.World.Economy.Forecast(state).NetIncome.ToString("+0;-0;0")+"   ·   food "+state.economy.wellbeing+"%   ·   unrest "+state.economy.unrest+"%",text);
                GUILayout.EndHorizontal();
            }
        }
        void DrawCampaignTrade(float width)
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;var own=c.Owned(actor);var funds=c.World.FactionState(actor);
            GUILayout.Label("Trade balance "+funds.lastTradeBalance.ToString("+0;-0;0"),heading);
            GUILayout.Label("Food moves along open supply routes. Foreign deliveries are paid from the buyer's treasury; materials supply local construction and recruitment.",small);
            GUILayout.BeginHorizontal();
            DrawCampaignResourceCard("food","Food",own.Sum(r=>r.economy.food),own.Sum(r=>c.World.Economy.Forecast(r).foodProduced)+" produced / turn",(width-16)/2);
            GUILayout.Space(12);DrawCampaignResourceCard("materials","Materials",own.Sum(r=>r.economy.materials),own.Sum(r=>c.MaterialProduction(r.regionId)).ToString("0.#")+" produced / turn",(width-16)/2);
            GUILayout.EndHorizontal();GUILayout.Space(18);
            foreach(var other in c.Save.countries.Where(x=>x.factionId!=actor&&c.Treaty(actor,x.factionId).trade))
            {
                var shipments=c.Save.lastTradeShipments.Where(s=>s.seller==actor&&s.buyer==other.factionId||s.seller==other.factionId&&s.buyer==actor).ToArray();
                int exports=shipments.Where(s=>s.seller==actor).Sum(s=>s.payment),imports=shipments.Where(s=>s.buyer==actor).Sum(s=>s.payment);
                bool connected=own.Any(a=>c.Owned(other.factionId).Any(b=>{var route=c.Route(actor,a.regionId,b.regionId,true);return route!=null&&route.distance<=c.Balance.freightRangeKm;}));
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(CampaignFactionName(other.factionId),campaignAction,GUILayout.Width(225)))OpenCampaignDiplomacy(other.factionId);
                GUILayout.Label("Exports "+exports+" / imports "+imports+"   ·   "+(connected?"Route available":"No open route"),text);GUILayout.EndHorizontal();
            }
            if(!c.Save.treaties.Any(t=>t.trade&&(t.a==actor||t.b==actor)))
            {GUILayout.Label("No trade agreements.",small);if(GUILayout.Button("Find a trade partner",campaignAction))OpenCampaignPage("Diplomacy");}
            GUILayout.Space(18);GUILayout.Label("Last turn's deliveries",campaignCaption);
            var deliveries=c.Save.lastTradeShipments.Where(s=>s.seller==actor||s.buyer==actor).ToArray();
            if(deliveries.Length==0)GUILayout.Label("No foreign deliveries last turn.",small);
            foreach(var s in deliveries)GUILayout.Label(CampaignPlace(s.origin)+" → "+CampaignPlace(s.destination)+"   /   "+s.quantity.ToString("0.#")+" food   /   "+s.payment+" treasury",text);
            var blockades=c.Save.blockades.Where(b=>c.World.State(b.regionId).owner==actor).ToArray();
            foreach(var b in blockades)GUILayout.Label("Blockaded: "+CampaignPlace(b.regionId),text);
        }
        void DrawCampaignResourceCard(string id,string title,float stock,string production,float width)
        {
            var card=GUILayoutUtility.GetRect(width,95,GUILayout.Width(width));CampaignArt.PaperPanel(card);
            if(!CampaignArt.Image(new Rect(card.x+8,card.y+8,76,76),"Resources/"+id))CampaignIcon(new Rect(card.x+28,card.y+29,35,35),id=="food"?"Farm":"Nation",CampaignInk);
            CampaignArt.Label(new Rect(card.x+96,card.y+8,card.width-106,24),title,18,false,false,true);
            CampaignArt.Label(new Rect(card.x+96,card.y+35,card.width-106,25),stock.ToString("N0"),22,false,false,true);
            CampaignArt.Label(new Rect(card.x+96,card.y+65,card.width-106,20),production,11,true);
        }
        void DrawCampaignLists(float width)
        {
            // Present only entities that exist in the simulation; sea expeditions are transport contracts.
            CampaignTabs(new[]{"Armies","Regions","Naval orders"},ref campaignListTab);
            if(campaignListTab=="Regions"){DrawCampaignRegions();return;}
            if(campaignListTab=="Naval orders"){DrawCampaignOperations();return;}
            GUILayout.BeginHorizontal();GUILayout.Label("Find",small,GUILayout.Width(40));listSearch=GUILayout.TextField(listSearch,60);GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();GUILayout.Label("Sort",small,GUILayout.Width(40));
            foreach(string sort in new[]{"Name","Strength","Movement","Supply"})if(GUILayout.Button((armyListSort==sort?"• ":"")+sort,campaignAction))armyListSort=sort;
            GUILayout.EndHorizontal();GUILayout.Space(10);
            var armies=countryCampaign.Save.armies.Where(a=>a.owner==countryCampaign.Save.playerFaction&&(listSearch==""||a.name.IndexOf(listSearch,StringComparison.OrdinalIgnoreCase)>=0||CampaignPlace(a.regionId).IndexOf(listSearch,StringComparison.OrdinalIgnoreCase)>=0));
            armies=armyListSort=="Strength"?armies.OrderByDescending(a=>a.Soldiers):armyListSort=="Movement"?armies.OrderByDescending(a=>a.movement):armyListSort=="Supply"?armies.OrderBy(a=>a.supply):armies.OrderBy(a=>a.name);
            foreach(var army in armies.ToArray())
            {
                var row=GUILayoutUtility.GetRect(width,74);CampaignArt.PaperPanel(row);
                CampaignArt.Label(new Rect(row.x+12,row.y+8,row.width-126,24),army.name,18,false,false,true);
                string ground=CountryCampaign.AtSea(army)?"At sea":countryCampaign.AtSettlement(army)?CampaignPlace(army.regionId):countryCampaign.ArmyGround(army).name+" / "+CampaignPlace(army.regionId);
                CampaignArt.Label(new Rect(row.x+12,row.y+36,row.width-126,27),army.units.Count+" units · "+army.Soldiers.ToString("N0")+" men · "+army.movement+" MP · "+(army.supply*100).ToString("0")+"% supply · "+ground,12,true,false,false,true);
                if(GUI.Button(new Rect(row.xMax-104,row.y+18,92,36),"Select",campaignAction))SelectCampaignArmy(army);
                GUILayout.Space(8);
            }
            if(!armies.Any())GUILayout.Label("No armies match.",small);
        }
        void DrawCampaignBuildingBrowser(float width)
        {
            GUILayout.Label("Choose a building to inspect its upgrades and requirements.",small);
            for(int first=0;first<CampaignBuildingTiers.Kinds.Length;first+=4)
            {
                GUILayout.BeginHorizontal();
                foreach(string kind in CampaignBuildingTiers.Kinds.Skip(first).Take(4))if(GUILayout.Button((buildingPreviewKind==kind?"• ":"")+kind,campaignAction))buildingPreviewKind=kind;
                GUILayout.EndHorizontal();
            }
            foreach(var tier in CampaignBuildingTiers.All.Where(t=>t.Kind==buildingPreviewKind))DrawCampaignTierArt(tier,width,"");
            if(selectedRegion!=null&&CanManageRegion(regionCampaign.State(selectedRegion.id))&&GUILayout.Button("Manage "+selectedRegion.capital.name,campaignAction))OpenCampaignBuilding(selectedRegion.id);
        }
        void DrawCampaignResearchBrowser(float width)
        {
            CampaignTabs(new[]{"Military","Industry","Civil"},ref researchBranch);
            var c=countryCampaign;string actor=c.Save.playerFaction;var progress=c.Progress(actor);
            GUILayout.Label(progress.research==""?"No research selected":c.Technology(progress.research).name+" / "+progress.researchProgress.ToString("0")+" work completed",text);
            GUILayout.Label(c.ResearchCapacity(actor)+" work / turn   ·   "+c.Balance.researchFunding+" treasury / turn",small);
            showFutureResearch=PaperUI.Toggle(showFutureResearch,"Show future branches");GUILayout.Space(12);
            var techs=c.Balance.technologies.Where(t=>t.branch==researchBranch&&(showFutureResearch||t.minYear<=c.Year&&string.IsNullOrEmpty(t.unavailableReason))).ToArray();
            for(int start=0;start<techs.Length;start+=4)
            {
                GUILayout.BeginHorizontal();
                foreach(var tech in techs.Skip(start).Take(4))
                {
                    float span=(width-30)/4;var card=GUILayoutUtility.GetRect(span,136,GUILayout.Width(span));bool known=c.Knows(actor,tech.id),active=progress.research==tech.id;
                    CampaignArt.PaperPanel(card,focusedTechnology==tech.id,card.Contains(Event.current.mousePosition));
                    if(!CampaignArt.Image(new Rect(card.x+7,card.y+7,card.width-14,61),"Technologies/"+tech.id))CampaignIcon(new Rect(card.center.x-18,card.y+16,36,36),researchBranch=="Military"?"Armies":"Research",CampaignInk);
                    CampaignArt.Label(new Rect(card.x+10,card.y+73,card.width-20,34),tech.name,13,false,true,false,true);
                    int turns=Mathf.CeilToInt((tech.cost-(active?progress.researchProgress:0))/Math.Max(1,c.ResearchCapacity(actor)));
                    bool available=c.ResearchAvailable(actor,tech,out string blocked);
                    CampaignArt.Label(new Rect(card.x+10,card.y+112,card.width-20,17),known?"Complete":active?"Researching / "+turns+" turns":available?turns+" turns":"Locked",11,true);
                    if(GUI.Button(card,new GUIContent("",tech.effect+(blocked!=""?"\n"+blocked:"")),GUIStyle.none))focusedTechnology=tech.id;
                    GUILayout.Space(8);
                }
                GUILayout.EndHorizontal();GUILayout.Space(8);
            }
            var selected=c.Technology(focusedTechnology);if(selected==null||selected.branch!=researchBranch)return;
            GUILayout.Space(10);GUILayout.Label(selected.name,heading);GUILayout.Label(selected.effect,text);
            bool eligible=c.ResearchAvailable(actor,selected,out string why);if(why!="")GUILayout.Label(why,small);
            string prerequisite=string.Join(" · ",c.Prerequisites(selected).Select(id=>c.Technology(id).name));if(prerequisite!="")GUILayout.Label("Requires "+prerequisite,small);
            if(CampaignOrderButton("Research",eligible&&!c.Knows(actor,selected.id)&&progress.research!=selected.id)&&c.Research(actor,selected.id,out countryMessage))SaveCountry(false);
        }
        void DrawCampaignFlag(Rect rect,string faction)
        {
            if(CampaignArt.Image(rect,"Flags/"+faction))return;
            EmpireTheme.Fill(rect,catalog.Faction(faction).MapColor);CampaignIcon(new Rect(rect.center.x-10,rect.center.y-10,20,20),"Nation",CampaignPaper);
        }
        void SelectDiplomaticNation(string faction)
        {diplomacyFocus=faction;negotiationsOpen=false;campaignProposal=new CampaignDiplomaticProposal();diplomaticReply="";proposalRegions=proposalTechnologies=false;}
        void DrawCampaignRelations(float width)
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;
            if(c.Progress(diplomacyFocus)==null)SelectDiplomaticNation(actor);
            var countries=c.Save.countries.ToArray();
            diplomacyFlagsScroll=GUILayout.BeginScrollView(diplomacyFlagsScroll,GUILayout.Height(87));
            var strip=GUILayoutUtility.GetRect(Math.Max(width-16,countries.Length*108),64);
            for(int i=0;i<countries.Length;i++)
            {
                string id=countries[i].factionId;var card=new Rect(strip.x+i*108,strip.y,100,61);CampaignArt.PaperPanel(card,id==diplomacyFocus);
                DrawCampaignFlag(new Rect(card.x+29,card.y+5,42,28),id);CampaignArt.Label(new Rect(card.x+4,card.y+37,92,19),CampaignFactionName(id),10,false,false,false,true);
                if(GUI.Button(card,GUIContent.none,GUIStyle.none))SelectDiplomaticNation(id);
            }
            GUILayout.EndScrollView();GUILayout.Space(8);
            GUILayout.Label(CampaignFactionName(diplomacyFocus),heading);
            if(diplomacyFocus==actor)
            {
                DrawCampaignOpinions(width);return;
            }
            var treaty=c.Treaty(actor,diplomacyFocus);
            GUILayout.Label((treaty.war?"At war":"At peace")+(treaty.trade?" · trade":"")+(treaty.access?" · military access":"")+(treaty.alliance?" · defensive allies":""),text);
            GUILayout.Label("Their opinion: "+CampaignOpinionLabel(c.DiplomaticOpinion(diplomacyFocus,actor)),small);
            if(!negotiationsOpen)
            {
                GUILayout.BeginHorizontal();
                if(CampaignOrderButton("Open negotiations")){negotiationsOpen=true;campaignProposal=new CampaignDiplomaticProposal{peace=treaty.war};diplomaticReply="";}
                if(!treaty.war&&CampaignOrderButton("Declare war…",c.CanDeclareWar(actor,diplomacyFocus,out _)))OpenCampaignWar(diplomacyFocus);
                GUILayout.EndHorizontal();DrawCampaignOpinions(width);return;
            }
            DrawCampaignProposal(width,treaty);
        }
        static string CampaignOpinionLabel(int value)=>value<=-60?"Hostile":value<0?"Unfriendly":value<20?"Neutral":value<50?"Cooperative":"Friendly";
        void DrawCampaignOpinions(float width)
        {
            if(GUILayout.Button(opinionsIncoming?"Opinions of "+CampaignFactionName(diplomacyFocus)+"  ↔":"Opinions held by "+CampaignFactionName(diplomacyFocus)+"  ↔",campaignAction))opinionsIncoming=!opinionsIncoming;
            foreach(var nation in countryCampaign.Save.countries.Where(c=>c.factionId!=diplomacyFocus))
            {
                var treaty=countryCampaign.Treaty(nation.factionId,diplomacyFocus);int opinion=countryCampaign.DiplomaticOpinion(opinionsIncoming?nation.factionId:diplomacyFocus,opinionsIncoming?diplomacyFocus:nation.factionId);
                GUILayout.BeginHorizontal();GUILayout.Label(CampaignFactionName(nation.factionId),text,GUILayout.Width(220));
                GUILayout.Label(CampaignOpinionLabel(opinion)+(treaty.war?" / war":treaty.alliance?" / alliance":treaty.trade?" / trade":""),small);GUILayout.EndHorizontal();
            }
        }
        void DrawCampaignProposal(float width,CampaignTreaty treaty)
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;var p=campaignProposal;
            GUILayout.BeginHorizontal();
            DrawCampaignProposalSide(actor,diplomacyFocus,true,(width-20)/2);GUILayout.Space(16);
            DrawCampaignProposalSide(diplomacyFocus,actor,false,(width-20)/2);GUILayout.EndHorizontal();GUILayout.Space(12);
            GUILayout.Label("Agreements",campaignCaption);
            if(treaty.war)
            {
                p.peace=PaperUI.Toggle(p.peace,"Peace treaty");
                GUILayout.BeginHorizontal();foreach(int turns in new[]{2,4,6})if(GUILayout.Button((p.truceTurns==turns?"• ":"")+turns+"-turn truce",campaignAction))p.truceTurns=turns;GUILayout.EndHorizontal();
            }
            if(!treaty.trade||treaty.war)p.trade=PaperUI.Toggle(p.trade,"Trade agreement");
            if(!treaty.access||treaty.war)p.access=PaperUI.Toggle(p.access,"Mutual military access");
            if(!treaty.alliance||treaty.war)p.alliance=PaperUI.Toggle(p.alliance,"Defensive alliance");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(proposalRegions?"Hide territories":"Add territory terms",campaignAction))proposalRegions=!proposalRegions;
            if(GUILayout.Button(proposalTechnologies?"Hide technologies":"Add technology terms",campaignAction))proposalTechnologies=!proposalTechnologies;
            GUILayout.EndHorizontal();
            if(proposalRegions)
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Offer: "+(p.offeredRegion==""?"none":CampaignPlace(p.offeredRegion)),campaignAction))p.offeredRegion=NextPeaceRegion(p.offeredRegion,actor);
                if(GUILayout.Button("Request: "+(p.demandedRegion==""?"none":CampaignPlace(p.demandedRegion)),campaignAction))p.demandedRegion=NextPeaceRegion(p.demandedRegion,diplomacyFocus);
                GUILayout.EndHorizontal();
            }
            if(proposalTechnologies)
            {
                GUILayout.BeginHorizontal();
                DrawCampaignTechnologyTerms(actor,diplomacyFocus,true,(width-20)/2);GUILayout.Space(16);
                DrawCampaignTechnologyTerms(diplomacyFocus,actor,false,(width-20)/2);GUILayout.EndHorizontal();
            }
            if(diplomaticReply!="")GUILayout.Label(diplomaticReply,text);
            GUILayout.Space(12);GUILayout.BeginHorizontal();
            if(CampaignOrderButton("Submit proposal",!p.Empty))
            {
                if(c.Negotiate(actor,diplomacyFocus,p,out diplomaticReply)){countryMessage=diplomaticReply;campaignProposal=new CampaignDiplomaticProposal();negotiationsOpen=false;SaveCountry(false);}
            }
            if(GUILayout.Button("Close negotiations",campaignAction))negotiationsOpen=false;
            GUILayout.EndHorizontal();
            // Breaking an existing agreement is unilateral; it is kept separate from proposals.
            if(!treaty.war)
            {
                GUILayout.Space(16);GUILayout.BeginHorizontal();
                foreach(string action in new[]{"Trade","Access","Alliance"})
                {
                    bool exists=action=="Trade"?treaty.trade:action=="Access"?treaty.access:treaty.alliance;
                    if(exists&&CampaignOrderButton("End "+action.ToLowerInvariant())&&c.Diplomacy(actor,diplomacyFocus,action,out countryMessage))SaveCountry(false);
                }
                if(treaty.alliance&&CampaignOrderButton("Call ally / 200")&&c.Diplomacy(actor,diplomacyFocus,"Call ally",out countryMessage))SaveCountry(false);
                GUILayout.EndHorizontal();
            }
        }
        void DrawCampaignProposalSide(string giver,string receiver,bool offer,float width)
        {
            var p=campaignProposal;GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label(offer?"Your offer":"Your request",campaignCaption);
            var money=GUILayoutUtility.GetRect(width,83);CampaignArt.PaperPanel(money);
            CampaignArt.Label(new Rect(money.x+10,money.y+9,money.width-20,20),"Treasury",14,false,true);
            int amount=offer?p.offeredMoney:p.demandedMoney;
            if(GUI.Button(new Rect(money.x+10,money.y+39,38,30),"−",campaignAction))amount=Math.Max(0,amount-100);
            string entered=GUI.TextField(new Rect(money.x+54,money.y+39,money.width-108,30),amount.ToString(),6);
            if(int.TryParse(entered,out int value))amount=Math.Max(0,Math.Min(10000,value));
            if(GUI.Button(new Rect(money.xMax-48,money.y+39,38,30),"+",campaignAction))amount=Math.Min(10000,amount+100);
            if(offer)p.offeredMoney=amount;else p.demandedMoney=amount;
            string region=offer?p.offeredRegion:p.demandedRegion;var techs=offer?p.offeredTechnologies:p.demandedTechnologies;
            if(region!="")GUILayout.Label("Territory: "+CampaignPlace(region),text);
            foreach(string id in techs)GUILayout.Label(countryCampaign.Technology(id).name,small);
            GUILayout.EndVertical();
        }
        void DrawCampaignTechnologyTerms(string giver,string receiver,bool offer,float width)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));GUILayout.Label(offer?"Offer knowledge":"Request knowledge",campaignCaption);
            var selected=offer?campaignProposal.offeredTechnologies:campaignProposal.demandedTechnologies;
            foreach(var technology in countryCampaign.Balance.technologies.Where(t=>countryCampaign.Knows(giver,t.id)&&!countryCampaign.Knows(receiver,t.id)&&t.minYear<=countryCampaign.Year&&string.IsNullOrEmpty(t.unavailableReason)))
            {
                bool marked=selected.Contains(technology.id),next=PaperUI.Toggle(marked,technology.name);
                if(next&&!marked)selected.Add(technology.id);else if(!next&&marked)selected.Remove(technology.id);
            }
            GUILayout.EndVertical();
        }
        void DrawCampaignArmyActions()
        {
            var army=SelectedCampaignArmy();if(army==null)return;string actor=countryCampaign.Save.playerFaction;var c=countryCampaign;
            var ids=army.units.Where(u=>campaignRegimentSelection.Contains(u.id)).Select(u=>u.id).ToArray();
            if(ids.Length>0&&ids.Length<army.units.Count&&CampaignOrderButton("Detach "+ids.Length+" selected units",!c.Committed(army))&&c.Detach(actor,army.id,ids,out string detached,out countryMessage)){selectedArmyId=detached;campaignRegimentSelection.Clear();campaignPopup="";SaveCountry(false);return;}
            if(c.AtSettlement(army)&&c.World.State(army.regionId).owner==actor)
            {
                if(CampaignOrderButton("Recruit units"))OpenCampaignRecruitment(army.regionId,army.id);
                if(c.CanRecover(actor,army.id,out int count,out int cost,out _)&&CampaignOrderButton("Replenish / +"+count+" men / "+cost)&&c.Recover(actor,army.id,out countryMessage))SaveCountry(false);
            }
            if(army.generalName==""&&CampaignOrderButton("Promote general / 200",!c.Committed(army))&&c.CommissionGeneral(actor,army.id,out countryMessage))SaveCountry(false);
            if(CampaignOrderButton("Transfer / merge",!c.Committed(army))){campaignPopup="Transfers";campaignDialogScroll=Vector2.zero;}
            if(CampaignOrderButton("Rename")){armyRenameText=army.name;campaignPopup="RenameArmy";campaignDialogScroll=Vector2.zero;}
            DrawCampaignStances(army);
            if(GUILayout.Button("Sea transport & siege orders",campaignAction)){campaignPopup="";OpenCampaignPage("Operations");}
        }
    }
}
