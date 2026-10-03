using System;
using System.Linq;
using Empire.UI;
using EmpireBattle;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        Vector2 provinceTownScroll,buildingScroll;
        readonly System.Collections.Generic.HashSet<string> campaignRegimentSelection=new System.Collections.Generic.HashSet<string>();
        void DrawCampaignProvince(float w,float h)
        {
            var r=CampaignTray(w,h);CampaignFrame(r);var province=catalog.Province(selectedRegion.provinceId);
            var state=regionCampaign.State(selectedRegion.id);bool domestic=state.owner==countryCampaign.Save.playerFaction;
            GUI.Label(new Rect(r.x+22,r.y+12,340,28),province.name.ToUpperInvariant(),campaignTitle);
            GUI.Label(new Rect(r.x+22,r.y+42,330,20),selectedRegion.capital.name+"  /  "+CampaignFactionName(state.owner),campaignCaption);
            if(GUI.Button(new Rect(r.xMax-44,r.y+12,28,27),"X",campaignAction)){selected=-1;selectedRegion=null;campaignPopup="";DrawSelectedRegion();return;}
            provinceTownScroll=GUI.BeginScrollView(new Rect(r.x+385,r.y+13,r.width-451,43),provinceTownScroll,new Rect(0,0,Mathf.Max(r.width-471,province.regionIds.Length*128),25));
            for(int i=0;i<province.regionIds.Length;i++)
            {
                var region=Array.Find(catalog.regions,t=>t.id==province.regionIds[i]);
                if(EmpireTheme.Button(new Rect(i*128,0,120,25),region.capital.name,button,region.id==selectedRegion.id))SelectCampaignRegion(region.id);
            }
            GUI.EndScrollView();
            if(!countryCampaign.Active(state.regionId)||!domestic)
            {
                GUI.Label(new Rect(r.x+22,r.y+83,r.width-44,30),domestic?"Outside the European campaign":"Foreign territory",campaignTitle);
                GUI.Label(new Rect(r.x+22,r.y+124,r.width-44,48),domestic?"This region is available for inspection only.":"The province's settlements and controller are known. Its economy and construction details are concealed.",text);
                if(countryCampaign.Active(state.regionId))
                {
                    bool war=countryCampaign.AtWar(countryCampaign.Save.playerFaction,state.owner);
                    GUI.Label(new Rect(r.x+22,r.y+183,r.width-44,24),war?"AT WAR / Select a force to attack this city.":"AT PEACE / Negotiate access or declare war before invading.",campaignCaption);
                    if(GUI.Button(new Rect(r.x+22,r.y+223,210,36),war?"Attack city…":"Plan invasion…",campaignAction))OpenCampaignOrders(state.regionId);
                    if(GUI.Button(new Rect(r.x+246,r.y+223,210,36),"Diplomacy",campaignAction))OpenCampaignDiplomacy(state.owner);
                }
                return;
            }
            var forecast=regionCampaign.Economy.Forecast(state);
            string[] stats={"POPULATION","LOCAL CONTRIBUTION","FOOD SECURITY","UNREST","MATERIALS"};
            string[] values={state.economy.population.ToString("N0"),forecast.NetIncome.ToString("+0;-0;0"),state.economy.wellbeing+"%",state.economy.unrest+"%",state.economy.materials.ToString("F0")};
            float statWidth=(r.width-40)/5;
            for(int i=0;i<5;i++){GUI.Label(new Rect(r.x+22+i*statWidth,r.y+72,statWidth,17),stats[i],campaignCaption);GUI.Label(new Rect(r.x+22+i*statWidth,r.y+92,statWidth,24),values[i],text);}
            float contentWidth=Mathf.Max(r.width-40,state.buildings.Length*116);
            buildingScroll=GUI.BeginScrollView(new Rect(r.x+20,r.y+125,r.width-40,108),buildingScroll,new Rect(0,0,contentWidth,90));
            for(int i=0;i<state.buildings.Length;i++)
            {
                var slot=state.buildings[i];var card=new Rect(i*116,0,108,89);CampaignFrame(card,true);
                if(!CampaignArt.Building(new Rect(card.x+5,card.y+5,98,36),slot.kind,slot.level))CampaignIcon(new Rect(card.x+38,card.y+9,32,32),slot.kind,CampaignInk);
                GUI.Label(new Rect(card.x+8,card.y+43,92,23),slot.kind=="Empty"?"Build here":slot.kind,cardText);
                GUI.Label(new Rect(card.x+8,card.y+68,92,17),slot.turnsRemaining>0?slot.workRemaining.ToString("0.#")+" work left":slot.kind=="Empty"?"Available slot":!CampaignBuildingTiers.IsOperational(slot)?"Ruined / tier "+slot.level:"Tier "+slot.level,cardSmall);
                bool enabled=GUI.enabled;GUI.enabled=enabled&&CanManageRegion(state);
                if(GUI.Button(card,new GUIContent("",slot.kind=="Empty"?"Add construction to an available regional slot":"Inspect, upgrade or cancel "+slot.kind),GUIStyle.none))OpenCampaignBuilding(state.regionId,i);
                GUI.enabled=enabled;
                if(slot.turnsRemaining>0)CampaignProgress(new Rect(card.x+8,card.y+84,92,3),slot.totalWork>0?1-slot.workRemaining/slot.totalWork:0,CampaignBronze);
            }
            GUI.EndScrollView();
            GUI.Label(new Rect(r.x+22,r.y+239,r.width-448,34),"Food "+forecast.foodProduced.ToString("F1")+" / "+forecast.foodNeeded.ToString("F1")+" needed  ·  "+state.buildings.Count(b=>b.kind=="Empty")+" open plots",small);
            bool was=GUI.enabled;GUI.enabled=was&&CanManageRegion(state);
            if(GUI.Button(new Rect(r.xMax-420,r.y+236,190,32),"Construction",campaignAction))OpenCampaignBuilding(state.regionId);
            if(GUI.Button(new Rect(r.xMax-218,r.y+236,196,32),"Recruit regiments",campaignAction))OpenCampaignRecruitment(state.regionId);
            GUI.enabled=was;
        }

        static string BuildingDescription(string kind)=>CampaignBuildingTiers.Get(kind,1)?.Effects??"Shared regional building plot.";

        readonly RegimentManagementPanel campaignManagement=new RegimentManagementPanel();
        string managementArmy="";

    }
}
