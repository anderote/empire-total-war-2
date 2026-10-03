using System;
using System.Linq;
using Empire.UI;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        Vector2 campaignDialogScroll;
        readonly System.Collections.Generic.Dictionary<int,Rect> cityTaskRects=new System.Collections.Generic.Dictionary<int,Rect>();
        string recruitTargetArmy="",orderDestination="",orderArmy="",warFaction="",warReturnPopup="",diplomacyFocus="";
        string provinceFilter="All",armyRenameText="";
        string buildingPreviewKind="Farm";
        GUIStyle buildingTierHeading;
        bool chooseRecruitArmy,showCityTasks=true,choosingCampaignDestination;

        void OpenCampaignRecruitment(string region,string army="")
        {
            if(selectedRegion?.id!=region)SelectCampaignRegion(region);
            recruitTargetArmy=army??"";chooseRecruitArmy=false;campaignDialogScroll=Vector2.zero;
            campaignPopup="Recruitment";countryPage="Map";choosingCampaignDestination=false;
        }
        void OpenCampaignBuilding(string region,int slot=-1)
        {
            if(selectedRegion?.id!=region)SelectCampaignRegion(region);
            var state=regionCampaign.State(region);
            selectedBuildingSlot=slot>=0?slot:Array.FindIndex(state.buildings,b=>b.kind=="Empty");
            if(selectedBuildingSlot<0)selectedBuildingSlot=Array.FindIndex(state.buildings,b=>b.turnsRemaining>0);
            if(selectedBuildingSlot<0)selectedBuildingSlot=0;
            campaignPopup="Construction";countryPage="Map";campaignDialogScroll=Vector2.zero;choosingCampaignDestination=false;
        }
        void OpenCampaignOrders(string destination,string army="")
        {
            orderDestination=destination;orderArmy=army??"";campaignPopup="Orders";
            countryPage="Map";campaignDialogScroll=Vector2.zero;choosingCampaignDestination=false;
        }
        void OpenCampaignWar(string faction)
        {
            warFaction=faction;warReturnPopup=campaignPopup;campaignPopup="War";campaignDialogScroll=Vector2.zero;
        }
        void CloseCampaignDialog()
        {campaignPopup=campaignPopup=="War"?warReturnPopup:"";campaignDialogScroll=Vector2.zero;}
        void OpenCampaignDiplomacy(string faction)
        {SelectDiplomaticNation(faction);OpenCampaignPage("Diplomacy");}
        bool CampaignRectButton(Rect r,string label,bool allowed=true)
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&allowed;
            bool pressed=GUI.Button(r,label,campaignAction);GUI.enabled=enabled;
            return pressed;
        }
        void DrawCampaignConstruction(float w,float h)
        {
            var r=CampaignUILayout.Dialog(w,h);CampaignFrame(r);
            string title=campaignPopup=="Orders"?"MARCH & ATTACK / "+CampaignPlace(orderDestination):
                campaignPopup=="War"?"DECLARE WAR / "+CampaignFactionName(warFaction):
                campaignPopup=="RenameArmy"?"RENAME ARMY":campaignPopup=="Transfers"?"TRANSFER REGIMENTS":campaignPopup.ToUpperInvariant()+" / "+(selectedRegion?.capital.name??"");
            GUI.Label(new Rect(r.x+24,r.y+16,r.width-92,36),title,campaignTitle);
            if(GUI.Button(new Rect(r.xMax-56,r.y+16,32,32),"X",campaignAction)){CloseCampaignDialog();return;}
            GUI.Label(new Rect(r.x+24,r.y+55,r.width-48,23),"TREASURY  "+regionCampaign.FactionState(countryCampaign.Save.playerFaction).treasury.ToString("N0"),campaignCaption);
            GUILayout.BeginArea(new Rect(r.x+24,r.y+88,r.width-48,r.height-158));
            campaignDialogScroll=GUILayout.BeginScrollView(campaignDialogScroll);
            float width=r.width-68;
            if(campaignPopup=="Army actions")DrawCampaignArmyActions();
            else if(campaignPopup=="Orders")DrawCampaignOrderPlanner();
            else if(campaignPopup=="War")DrawCampaignWarConfirmation();
            else if(campaignPopup=="Transfers")DrawCampaignTransfers();
            else if(campaignPopup=="RenameArmy")DrawCampaignArmyRename();
            else if(selectedRegion!=null)
            {
                var state=regionCampaign.State(selectedRegion.id);
                if(campaignPopup=="Recruitment")DrawCampaignRecruitment(state,width);
                else DrawCampaignBuildingChoices(state,width);
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
            EmpireTheme.Fill(new Rect(r.x+24,r.yMax-60,r.width-48,1),EmpireTheme.Line);
            GUI.Label(new Rect(r.x+24,r.yMax-48,r.width-48,42),countryMessage,small);
        }
        void DrawCampaignArmyRename()
        {
            var army=SelectedCampaignArmy();if(army==null){CloseCampaignDialog();return;}
            GUILayout.Label(army.name,heading);
            GUILayout.Label("Choose a name. Regiment names and their original recruitment identity stay unchanged.",text);
            armyRenameText=GUILayout.TextField(armyRenameText,60);
            if(CampaignOrderButton("Save name"))
            {if(countryCampaign.RenameArmy(army.owner,army.id,armyRenameText,out countryMessage)){SaveCountry(false);CloseCampaignDialog();}}
        }
        void DrawCampaignTierArt(BuildingTier tier,float width,string status)
        {
            if(buildingTierHeading==null)buildingTierHeading=new GUIStyle(heading){wordWrap=true};
            GUILayout.Space(8);GUILayout.BeginHorizontal();
            float artWidth=Math.Min(240,width*.32f);var art=GUILayoutUtility.GetRect(artWidth,artWidth*2/3,GUILayout.Width(artWidth));
            if(!CampaignArt.Building(art,tier.Kind,tier.Level)){CampaignFrame(art,true);CampaignIcon(new Rect(art.center.x-24,art.center.y-24,48,48),tier.Kind,CampaignInk);}
            GUILayout.Space(12);GUILayout.BeginVertical();
            GUILayout.Label("TIER "+tier.Level+" / "+tier.Name+(status==""?"":" / "+status),buildingTierHeading);
            GUILayout.Label(tier.Effects,text);
            GUILayout.Label(CampaignBuildingTiers.Unlocks(tier),small);
            string research=tier.Research==""?"No research prerequisite":countryCampaign.Technology(tier.Research)?.name??tier.Research;
            GUILayout.Label((tier.Level==1?"Base construction: ":"Upgrade: ")+tier.Cost+" treasury · "+tier.Work+" work · "+research,small);
            GUILayout.EndVertical();GUILayout.EndHorizontal();
        }
        void DrawCampaignBuildingChoices(RegionState state,float width)
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;
            GUILayout.Label(state.buildings.Count(b=>b.kind=="Empty")+" open plots · "+state.buildings.Count(b=>b.turnsRemaining>0)+" projects · "+state.economy.materials.ToString("F0")+" / "+c.MaterialCapacity(state.regionId)+" materials",text);
            GUILayout.Label("City output: "+c.ConstructionCapacity(state.regionId).ToString("0.##")+" work / turn, shared in plot order. Each work uses "+c.Balance.constructionMaterials+" materials. Staffing and unrest affect food and wealth.",small);
            if(c.Besieged(state.regionId)||c.InRevolt(state.regionId))GUILayout.Label("WORK PAUSED / Resolve the siege or revolt to resume construction.",text);
            for(int start=0;start<state.buildings.Length;start+=4)
            {
                GUILayout.BeginHorizontal();
                for(int i=start;i<Math.Min(start+4,state.buildings.Length);i++)
                {
                    var b=state.buildings[i];string status=b.kind=="Empty"?"Empty plot":b.kind+" "+b.level+(b.raidDamage>=CampaignBuildingTiers.RaidDurability(b.level)?" / RUIN":"");
                    if(GUILayout.Button((selectedBuildingSlot==i?"> ":"")+(i+1)+" / "+status,campaignAction)){selectedBuildingSlot=i;GUIUtility.ExitGUI();}
                }
                GUILayout.EndHorizontal();
            }
            if(selectedBuildingSlot<0||selectedBuildingSlot>=state.buildings.Length)return;
            var slot=state.buildings[selectedBuildingSlot];GUILayout.Space(12);string kind=slot.kind;
            if(kind!="Empty")
            {
                var current=CampaignBuildingTiers.Get(kind,slot.level);
                GUILayout.Label(current.Name+" / "+kind+" tier "+slot.level+" of "+CampaignBuildingTiers.MaxLevel(kind),heading);
                if(slot.raidDamage>0)
                {
                    bool ruined=slot.raidDamage>=CampaignBuildingTiers.RaidDurability(slot.level);
                    GUILayout.Label((ruined?"RUINED / No local output or equipment unlocks.":"RAID DAMAGE / Still operational.")+" "+c.FacilityRaidUses(state.regionId,selectedBuildingSlot)+" raid uses remain. Repair liability "+slot.raidRepairLiability+" treasury.",text);
                    bool repair=c.CanRepairFacility(actor,state.regionId,selectedBuildingSlot,out string whyRepair);GUILayout.Label(whyRepair,small);
                    if(CampaignOrderButton("Full repair / "+c.RepairCost(state.regionId,selectedBuildingSlot)+" treasury + "+c.RepairMaterials(state.regionId,selectedBuildingSlot)+" materials",repair)&&c.RepairFacility(actor,state.regionId,selectedBuildingSlot,out countryMessage))SaveCountry(false);
                }
                if(slot.turnsRemaining>0)
                {
                    GUILayout.Label((slot.upgrading?"UPGRADING / ":"BUILDING / ")+slot.workRemaining.ToString("0.##")+" work remaining · production and unlocks paused",small);
                    CampaignProgress(GUILayoutUtility.GetRect(1,6,GUILayout.ExpandWidth(true)),slot.totalWork>0?1-slot.workRemaining/slot.totalWork:0,CampaignBronze);
                    int refund=slot.totalWork>0?(int)(slot.paidCost*slot.workRemaining/slot.totalWork):0;
                    if(CampaignOrderButton("Cancel project / refund "+refund,CanManageRegion(state))&&c.CancelConstruction(actor,state.regionId,selectedBuildingSlot,out countryMessage))SaveCountry(false);
                }
                else
                {
                    var next=CampaignBuildingTiers.Get(kind,slot.level+1);bool allowed=c.CanUpgrade(actor,state.regionId,selectedBuildingSlot,out string reason);GUILayout.Label(reason,small);
                    if(next!=null&&CampaignOrderButton("Upgrade to "+next.Name+" / "+next.Cost+" treasury / "+next.Work+" work",allowed)&&c.Upgrade(actor,state.regionId,selectedBuildingSlot,out countryMessage))SaveCountry(false);
                }
            }
            else
            {
                GUILayout.Label("CHOOSE A BUILDING / PLOT "+(selectedBuildingSlot+1),campaignCaption);
                for(int row=0;row<2;row++)
                {GUILayout.BeginHorizontal();for(int col=0;col<4;col++){string choice=CampaignBuildingTiers.Kinds[row*4+col];if(GUILayout.Button((choice==buildingPreviewKind?"> ":"")+choice,campaignAction))buildingPreviewKind=choice;}GUILayout.EndHorizontal();}
                kind=buildingPreviewKind;var first=CampaignBuildingTiers.Get(kind,1);bool allowed=c.CanBuild(actor,state.regionId,kind,selectedBuildingSlot,out string reason);GUILayout.Label(reason,small);
                if(CampaignOrderButton("Build "+first.Name+" / "+c.ConstructionCost(actor,state.regionId,kind)+" treasury / "+first.Work+" work",allowed)&&c.Build(actor,state.regionId,kind,selectedBuildingSlot,out countryMessage))SaveCountry(false);
            }
            GUILayout.Space(12);GUILayout.Label("LOCAL PROGRESSION / "+kind.ToUpperInvariant(),campaignCaption);
            GUILayout.Label("Each tier replaces the previous tier. Unlocks use the highest operational building in this province; duplicate low tiers do not combine. Existing issued kit is retained when an army leaves.",small);
            foreach(var tier in CampaignBuildingTiers.All.Where(t=>t.Kind==kind))
                DrawCampaignTierArt(tier,width,slot.kind=="Empty"?"":tier.Level==slot.level?(slot.turnsRemaining>0?"IN PROGRESS":CampaignBuildingTiers.IsOperational(slot)?"CURRENT":"RUINED"):tier.Level==slot.level+1?"NEXT":"");
        }

        void OpenRecruitingCities()
        {provinceFilter="Recruitment";countryPage="Provinces";campaignPopup="";campaignPageScroll=Vector2.zero;}
        void OpenCampaignCityTasks(int townIndex)
        {
            var region=catalog.ForCity(Atlas.Data.settlements[townIndex].id);if(region==null)return;
            SelectCampaignRegion(region.id);countryPage="Map";
            var state=regionCampaign.State(region.id);
            if(countryCampaign.Besieged(region.id)||countryCampaign.InRevolt(region.id))return;
            if(state.buildings.Any(b=>b.kind=="Empty"||b.turnsRemaining>0))OpenCampaignBuilding(region.id);
            else if(countryCampaign.Save.recruitment.Any(q=>q.regionId==region.id))OpenCampaignRecruitment(region.id);
        }
        void DrawCampaignTransfers()
        {
            var army=SelectedCampaignArmy();if(army==null){GUILayout.Label("Select an army to transfer regiments.",text);return;}
            GUILayout.Label("Forces must meet within 5 km and finish training. A force holds up to 80 regiments.",text);
            var targets=countryCampaign.Save.armies.Where(a=>a.owner==army.owner&&a.id!=army.id&&countryCampaign.ForcesCanMeet(a,army)).ToArray();
            if(targets.Length==0)GUILayout.Label("No other friendly force is here. Move one to "+CampaignPlace(army.regionId)+" first.",small);
            foreach(var target in targets)
            {
                var selectedIds=army.units.Where(u=>campaignRegimentSelection.Contains(u.id)).Select(u=>u.id).ToArray();
                int count=selectedRegimentId!=""?1:selectedIds.Length>0?selectedIds.Length:army.units.Count;
                bool ready=!countryCampaign.Committed(army)&&!countryCampaign.Committed(target)&&!countryCampaign.InTransit(army)&&!countryCampaign.InTransit(target)&&count+target.units.Count<=CountryCampaign.MaxArmyRegiments;
                if(CampaignOrderButton("Transfer "+count+" to "+target.name+" / "+target.units.Count+" regiments",ready))
                {
                    var ids=selectedRegimentId!=""?new[]{selectedRegimentId}:selectedIds.Length>0?selectedIds:army.units.Select(u=>u.id).ToArray();
                    if(countryCampaign.Transfer(army.owner,army.id,target.id,ids,out countryMessage)){selectedArmyId=target.id;selectedRegimentId="";campaignPopup="";SaveCountry(false);}
                }
                if(!ready)GUILayout.Label("Finish movement/training or free regiment places first.",small);
            }
        }
        void DrawCampaignOrderPlanner()
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;
            if(!c.Active(orderDestination)){GUILayout.Label("Choose a city within the active campaign.",text);return;}
            string owner=c.World.State(orderDestination).owner;bool hostile=c.AtWar(actor,owner);
            GUILayout.Label(CampaignPlace(orderDestination)+" / "+CampaignFactionName(owner)+(owner==actor?" / YOUR CITY":hostile?" / AT WAR":" / AT PEACE"),heading);
            if(owner!=actor&&!hostile)
            {
                GUILayout.Label("Attacking requires war. Military access permits peaceful movement only.",text);
                bool allowed=c.CanDeclareWar(actor,owner,out string reason);GUILayout.Label(reason,small);
                GUILayout.BeginHorizontal();
                if(CampaignOrderButton("Declare war…",allowed))OpenCampaignWar(owner);
                if(GUILayout.Button("Open diplomacy",campaignAction))OpenCampaignDiplomacy(owner);
                GUILayout.EndHorizontal();
            }
            var army=c.Army(orderArmy);
            GUILayout.Space(12);GUILayout.Label("CHOOSE AN ARMY",campaignCaption);
            var forces=c.Save.armies.Where(a=>a.owner==actor).OrderBy(a=>c.Route(actor,a.regionId,orderDestination)?.distance??float.MaxValue).ToArray();
            if(forces.Length==0)GUILayout.Label("Recruit an army at an owned city with a barracks first.",text);
            foreach(var force in forces)
                if(GUILayout.Button((force.id==orderArmy?"> ":"")+force.name+" / "+CampaignPlace(force.regionId)+" / "+force.Soldiers.ToString("N0")+" soldiers",campaignAction))orderArmy=force.id;
            if(army==null)return;
            GUILayout.Space(12);GUILayout.Label(army.name,heading);
            bool canMarch=c.CanMarch(actor,army.id,orderDestination,out string why);GUILayout.Label(why,text);
            if(army.marchTarget!=""&&CampaignOrderButton("Halt current march")&&c.Halt(actor,army.id,out countryMessage))SaveCountry(false);
            string label=(hostile?"March to attack ":"March to ")+CampaignPlace(orderDestination);
            if(CampaignOrderButton(label,canMarch)&&c.March(actor,army.id,orderDestination,out countryMessage))
            {SelectCampaignArmy(army);SaveCountry(false);nextPolitics=0;}
            if(!canMarch&&GUILayout.Button("Sea transport & siege orders",campaignAction))
            {SelectCampaignArmy(army);countryPage="Operations";campaignPopup="";campaignPageScroll=Vector2.zero;}
        }
        void DrawCampaignWarConfirmation()
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;
            bool allowed=c.CanDeclareWar(actor,warFaction,out string why);
            GUILayout.Label("War with "+CampaignFactionName(warFaction),heading);GUILayout.Label(why,text);
            GUILayout.Label("Armies can attack each other's territory. Existing trade, access and alliance agreements between your countries end immediately.",text);
            var allies=c.WarAllies(actor,warFaction).Select(CampaignFactionName).ToArray();
            GUILayout.Space(12);GUILayout.Label(allies.Length>0?"Defensive allies that will also join the war: "+string.Join(", ",allies):"No additional defensive allies will join under current treaties.",text);
            GUILayout.Space(18);GUILayout.BeginHorizontal();
            if(CampaignOrderButton("Confirm declaration of war",allowed)&&c.Diplomacy(actor,warFaction,"War",out countryMessage))
            {campaignPopup=warReturnPopup;campaignDialogScroll=Vector2.zero;previewKey="";SaveCountry(false);nextPolitics=0;}
            if(GUILayout.Button("Back",campaignAction))CloseCampaignDialog();
            GUILayout.EndHorizontal();
        }
        string CampaignCityTasks(RegionState state)
        {
            if(countryCampaign==null||!showCityTasks||state==null||state.owner!=countryCampaign.Save.playerFaction||!countryCampaign.Active(state.regionId))return "";
            if(countryCampaign.Besieged(state.regionId))return "  [SIEGE]";
            if(countryCampaign.InRevolt(state.regionId))return "  [REVOLT]";
            int empty=state.buildings.Count(b=>b.kind=="Empty"),work=state.buildings.Count(b=>b.turnsRemaining>0),training=countryCampaign.Save.recruitment.Count(q=>q.regionId==state.regionId);
            return (empty>0?"  [+"+empty+" plots]":"")+(work>0?"  [build "+work+"]":"")+(training>0?"  [train "+training+"]":"")+(state.economy.unrest>=70?"  [unrest]":state.economy.wellbeing<40?"  [food]":"");
        }
        string CampaignCityTaskDetails(RegionState state)
        {
            if(state==null||countryCampaign==null||state.owner!=countryCampaign.Save.playerFaction)return "";
            return "+ plots: empty building slots. Build: projects underway. Train: regiments recruiting.\nClick the task badges to open city management; click the name to inspect the city.";
        }
    }
}
