using System;
using System.IO;
using System.Linq;
using Empire.UI;
using EmpireBattle;
using EmpireCampaign.Persistence;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        CountryCampaign countryCampaign;
        bool atlasSandbox,atlasLayersOpen;
        string countryPage="Map",selectedArmyId="",selectedRegimentId="",countryMessage="",provinceSort="Name";
        bool showFutureResearch,confirmNewCampaign;
        bool? resumeAvailable;
        void StartCountry(string faction,bool checkpointNewCampaign=false)
        {
            countryCampaign=new CountryCampaign(catalog,EconomyProfiles.Load(),Atlas.Data,faction=="france"?CampaignBalance.Load().Revolutionary():CampaignBalance.Load(),faction,1800,faction=="france"?CountryCampaign.RepublicScenario:"",Atlas);
            regionCampaign=countryCampaign.World;atlasSandbox=false;selectedArmyId="";selectedRegimentId="";campaignPopup="";countryPage="Map";campaignMenu=false;nextPolitics=0;
            var home=countryCampaign.Owned(faction).FirstOrDefault(s=>s.regionId=="region-paris")??countryCampaign.Owned(faction).OrderByDescending(s=>s.economy.population).First();
            SelectCampaignRegion(home.regionId);var capital=selectedRegion.capital;Focus(capital.lon,capital.lat,1700);
            countryMessage="France: recruit at a barracks, then select an army and right-click anywhere on land to march. Long marches continue each turn.";
            confirmNewCampaign=false;
            if(checkpointNewCampaign)SaveCountry(false);
        }
        void SelectCampaignRegion(string id)
        {
            var region=Array.Find(catalog.regions,r=>r.id==id);if(region==null)return;
            int index=Array.FindIndex(Atlas.Data.settlements,t=>t.id==region.capital.id);
            if(index>=0)Select(index);selectedArmyId="";selectedRegimentId="";campaignRegimentSelection.Clear();selectedBuildingSlot=-1;campaignPopup="";choosingCampaignDestination=false;
        }
        CampaignArmy SelectedCampaignArmy()=>countryCampaign?.Army(selectedArmyId);
        void SelectCampaignArmy(CampaignArmy army)
        {SelectCampaignRegion(army.regionId);selectedArmyId=army.id;selectedRegimentId="";countryPage="Map";campaignPopup="";regimentScroll=Vector2.zero;var p=CampaignArmyPosition(army,0).normalized;Focus(Mathf.Atan2(-p.x,p.z)*Mathf.Rad2Deg,Mathf.Asin(p.y)*Mathf.Rad2Deg,950);}
        bool CanManageRegion(RegionState state)=>countryCampaign!=null?
            countryCampaign.CanOrder(countryCampaign.Save.playerFaction)&&countryCampaign.Active(state.regionId)&&state.owner==countryCampaign.Save.playerFaction:atlasSandbox;
        void DrawCountryControls()
        {if(GUILayout.Button("Return to nation selection",button)){atlasSandbox=false;countryPage="Map";}}
        bool SaveCountry(bool announce=true)
        {
            try
            {
                string json=JsonUtility.ToJson(countryCampaign.Save,true);
                if(announce)File.WriteAllText(Path.Combine(Application.persistentDataPath,"country-campaign.json"),json);
                var checkpoints=CampaignCheckpoints();
                if(!checkpoints.TrySave(json,IsCompatibleCampaignJson,out string checkpointReason))
                {
                    Debug.LogWarning("Campaign autosave checkpoint failed: "+checkpointReason);
                    if(!announce){countryMessage="Campaign autosave failed.";return false;}
                    countryMessage="Manual campaign save updated; autosave checkpoint failed.";
                }
                else{resumeAvailable=true;if(announce)countryMessage="Manual campaign save updated; autosave checkpoint saved separately.";}
            }
            catch(Exception error) when(error is IOException||error is UnauthorizedAccessException){countryMessage="Campaign could not be saved.";return false;}
            campaignMenu=false;return true;
        }
        CampaignCheckpointStore CampaignCheckpoints()=>new CampaignCheckpointStore(Path.Combine(Application.persistentDataPath,"CampaignCheckpoints"));
        bool IsCompatibleCampaignJson(string json)=>TryRestoreCampaign(json,out _,out _);
        bool TryRestoreCampaign(string json,out CountryCampaign restored,out string reason)
        {
            restored=null;reason="No compatible campaign save is available.";
            try
            {
                var loaded=JsonUtility.FromJson<CountryCampaignSave>(json);
                if(loaded==null||string.IsNullOrEmpty(loaded.playerFaction))return false;
                restored=new CountryCampaign(catalog,EconomyProfiles.Load(),Atlas.Data,loaded.scenarioId==CountryCampaign.RepublicScenario?CampaignBalance.Load().Revolutionary():CampaignBalance.Load(),loaded.playerFaction,1800,loaded.scenarioId??"",Atlas);
                if(restored.Adopt(loaded,out reason))return true;
                restored=null;return false;
            }
            catch(Exception){restored=null;return false;}
        }
        bool HasResumeCampaign()
        {
            if(resumeAvailable.HasValue)return resumeAvailable.Value;
            if(CampaignCheckpoints().HasValidCheckpoint(IsCompatibleCampaignJson))return (resumeAvailable=true).Value;
            string path=Path.Combine(Application.persistentDataPath,"country-campaign.json");
            try{return (resumeAvailable=File.Exists(path)&&IsCompatibleCampaignJson(File.ReadAllText(path))).Value;}catch(Exception){resumeAvailable=false;return false;}
        }
        void LoadCountry(bool preferCheckpoint=true)
        {
            try
            {
                string json=null;long sequence=0;
                bool fromCheckpoint=preferCheckpoint&&CampaignCheckpoints().TryLoad(IsCompatibleCampaignJson,out json,out sequence);
                if(!fromCheckpoint)json=File.ReadAllText(Path.Combine(Application.persistentDataPath,"country-campaign.json"));
                if(TryRestoreCampaign(json,out var restored,out countryMessage))
                {countryCampaign=restored;regionCampaign=restored.World;selectedArmyId="";selectedRegimentId="";campaignPopup="";countryPage="Map";nextPolitics=0;confirmNewCampaign=false;if(!fromCheckpoint)SaveCountry(false);}
                if(fromCheckpoint)countryMessage="Resumed campaign checkpoint "+sequence+".";
            }
            catch(Exception){countryMessage="No compatible campaign save is available.";}
            campaignMenu=false;
        }
        bool CampaignOrderButton(string label,bool allowed=true)
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&allowed&&countryCampaign.CanOrder(countryCampaign.Save.playerFaction);
            bool clicked=GUILayout.Button(label,campaignAction);GUI.enabled=enabled;return clicked;
        }
        void DrawCampaignPage(float w,float h)
        {
            var r=CampaignWindow(w,h);CampaignFrame(r);
            GUI.Label(new Rect(r.x+25,r.y+16,r.width-85,32),countryPage.ToUpperInvariant(),campaignTitle);
            if(GUI.Button(new Rect(r.xMax-58,r.y+16,34,30),"X",campaignAction)){countryPage="Map";return;}
            GUILayout.BeginArea(new Rect(r.x+25,r.y+65,r.width-50,r.height-83));
            campaignPageScroll=GUILayout.BeginScrollView(campaignPageScroll);
            switch(countryPage)
            {
                case "Nation":case "Government":DrawCampaignGovernment(r.width-66);break;
                case "Dispatches":DrawCampaignDispatches();break;
                case "Research":DrawCampaignResearchBrowser(r.width-66);break;
                case "Diplomacy":DrawCampaignRelations(r.width-66);break;
                case "Armies":case "Lists":DrawCampaignLists(r.width-66);break;
                case "Building browser":DrawCampaignBuildingBrowser(r.width-66);break;
                case "Operations":DrawCampaignOperations();break;
                case "Battles":DrawCampaignBattleReport();break;
                case "Provinces":DrawCampaignRegions();break;
                case "Events":foreach(var entry in countryCampaign.Save.events.AsEnumerable().Reverse()){GUILayout.Label(entry,text);GUILayout.Space(12);}break;
                case "Map settings":DrawCampaignMapSettings();break;
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void DrawCampaignNation()
        {
            string actor=countryCampaign.Save.playerFaction;var c=countryCampaign.Progress(actor);var own=countryCampaign.Owned(actor);
            var forecasts=own.Select(r=>regionCampaign.Economy.Forecast(r)).ToArray();
            GUILayout.Label("ECONOMY AT A GLANCE",campaignCaption);
            GUILayout.Label("Treasury "+regionCampaign.FactionState(actor).treasury.ToString("N0")+"  ·  local contributions "+forecasts.Sum(f=>f.NetIncome).ToString("+0;-0;0")+"  ·  "+own.Sum(r=>r.buildings.Count(b=>b.kind=="Empty"))+" open building plots",text);
            GUILayout.Label("Local contributions are before national army, administration and research costs. Last turn's national balance includes those costs.",small);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Cities with open plots",campaignAction)){provinceFilter="Build slots";countryPage="Provinces";campaignPageScroll=Vector2.zero;}
            if(GUILayout.Button("Recruitment cities",campaignAction)){provinceFilter="Recruitment";countryPage="Provinces";campaignPageScroll=Vector2.zero;}
            GUILayout.EndHorizontal();GUILayout.Space(14);
            GUILayout.Label(countryCampaign.FactionName(actor)+"  /  "+own.Count+" regions  /  "+c.technologies.Count+" discoveries",heading);
            GUILayout.Label("Policy: "+c.policy+"  /  last turn balance "+c.lastNet.ToString("+0;-0;0"),text);
            GUILayout.Label("Regional administration "+c.lastAdministration+"  ·  army upkeep "+c.lastMilitaryCost+"  ·  research funding "+c.lastResearchCost,small);
            GUILayout.Label(countryCampaign.OpeningAdvice,text);
            if(countryCampaign.IsRepublic)DrawRepublicStory();
            else
            {
            GUILayout.Space(20);GUILayout.Label("CHAPTER I / THE FRENCH FRONTIER",campaignCaption);
            GUILayout.Label("Fund recruitment: "+(countryCampaign.Save.openingRecruitment?"complete":"recruit at a barracks")+"  ·  Issue a march: "+(countryCampaign.Save.openingMarch?"complete":"right-click a destination")+"  ·  Secure enemy territory: "+(countryCampaign.Save.openingConquest?"complete":"defeat Austria in a province"),text);
            GUILayout.Label("Generals give +5% campaign command strength and cost 4 upkeep each turn. Detachments can march independently and transfer regiments when forces meet. Each army holds 80 regiments; four nearby armies can reinforce. Battles admit reserves in waves, with 20,000 troops on the field at once.",small);
            GUILayout.Space(20);GUILayout.Label("OPEN TERRITORIAL CAMPAIGN",campaignCaption);
            GUILayout.Label("Expand, defend, negotiate and develop your territory freely. There are no victory quotas, development requirements or turn limit. Defeat requires all cities captured AND all armies and detachments destroyed.",text);
            }
            GUILayout.Space(20);GUILayout.Label("GOVERNMENT POLICY",campaignCaption);
            GUILayout.BeginHorizontal();foreach(string policy in new[]{"Balanced","Investment","Mobilization"})
                if(CampaignOrderButton(policy+(c.policy==policy?" / current":" / 200"),c.policy!=policy))countryCampaign.Reform(actor,policy,out countryMessage);GUILayout.EndHorizontal();
            GUILayout.Label("Balanced: 15% tax. Investment: 12% tax, more construction and research capacity. Mobilization: 18% tax, more manpower and unrest. Reforms have transition costs.",small);
        }
        void DrawCampaignResearch(float width)
        {
            string actor=countryCampaign.Save.playerFaction;var progress=countryCampaign.Progress(actor);
            GUILayout.Label("National knowledge enables paid adoption by individual regiments.",text);
            GUILayout.Label("Capacity "+countryCampaign.ResearchCapacity(actor)+" work / turn  ·  funding "+countryCampaign.Balance.researchFunding+" treasury / turn",small);
            showFutureResearch=EmpireTheme.Toggle(showFutureResearch,"Show future and planned branches");GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            foreach(string branch in new[]{"Civil","Military","Industry"})
            {
                GUILayout.BeginVertical(GUILayout.Width((width-24)/3));GUILayout.Label(branch.ToUpperInvariant(),campaignCaption);GUILayout.Space(8);
                foreach(var tech in countryCampaign.Balance.technologies.Where(t=>t.branch==branch&&(showFutureResearch||(t.minYear<=countryCampaign.Year&&string.IsNullOrEmpty(t.unavailableReason)))))
                {
                    bool known=progress.technologies.Contains(tech.id),active=progress.research==tech.id;
                    bool available=countryCampaign.ResearchAvailable(actor,tech,out string blocked);
                    var card=GUILayoutUtility.GetRect(1,218,GUILayout.ExpandWidth(true));CampaignFrame(card,true);
                    CampaignIcon(new Rect(card.x+12,card.y+13,24,24),branch=="Military"?"Armies":"Research",CampaignInk);
                    GUI.Label(new Rect(card.x+48,card.y+10,card.width-62,43),tech.name,cardTitle);
                    GUI.Label(new Rect(card.x+14,card.y+58,card.width-28,59),tech.effect,cardText);
                    string needs=string.Join(" + ",countryCampaign.Prerequisites(tech).Select(id=>countryCampaign.Technology(id).name));
                    GUI.Label(new Rect(card.x+14,card.y+120,card.width-28,42),known?"Knowledge established":!available?blocked:needs!=""?"Requires: "+needs:"Foundation research",cardSmall);
                    float work=active?progress.researchProgress:0;CampaignProgress(new Rect(card.x+14,card.y+173,card.width-28,4),known?1:work/tech.cost,CampaignBronze);
                    int turns=Mathf.CeilToInt((tech.cost-work)/Mathf.Max(1,countryCampaign.ResearchCapacity(actor)));
                    bool enabled=GUI.enabled;GUI.enabled=enabled&&!known&&!active&&available&&countryCampaign.CanOrder(actor);
                    string label=known?"COMPLETED":active?"RESEARCHING  /  "+work.ToString("F0")+" of "+tech.cost:available?"RESEARCH  /  ~"+turns+" turns":"LOCKED";
                    if(GUI.Button(new Rect(card.x+12,card.y+185,card.width-24,28),label,campaignAction))countryCampaign.Research(actor,tech.id,out countryMessage);
                    GUI.enabled=enabled;GUILayout.Space(10);
                }
                GUILayout.EndVertical();GUILayout.Space(10);
            }
            GUILayout.EndHorizontal();
        }
        void DrawCampaignDiplomacy()
        {
            string actor=countryCampaign.Save.playerFaction;
            foreach(var other in countryCampaign.Save.countries.Where(c=>c.factionId!=actor).OrderByDescending(c=>c.factionId==diplomacyFocus))
            {
                var treaty=countryCampaign.Treaty(actor,other.factionId);GUILayout.Label(CampaignFactionName(other.factionId),heading);
                GUILayout.Label((treaty.war?"AT WAR":"At peace")+"  ·  "+(treaty.trade?"Trade agreement":"No trade agreement")+"  ·  "+(treaty.alliance?"Defensive allies":"No alliance")+"  ·  "+(treaty.access?"Military access":"No military access"),small);
                GUILayout.BeginHorizontal();
                if(!treaty.war)
                {
                    bool canDeclare=countryCampaign.CanDeclareWar(actor,other.factionId,out string warReason);
                    if(CampaignOrderButton("Declare war…",canDeclare))OpenCampaignWar(other.factionId);
                }
                if(!treaty.war)
                {
                    if(CampaignOrderButton(treaty.trade?"End trade":"Arrange trade"))countryCampaign.Diplomacy(actor,other.factionId,"Trade",out countryMessage);
                    if(CampaignOrderButton(treaty.alliance?"End alliance":"Alliance / 200"))countryCampaign.Diplomacy(actor,other.factionId,"Alliance",out countryMessage);
                    if(CampaignOrderButton(treaty.access?"Revoke access":"Military access"))countryCampaign.Diplomacy(actor,other.factionId,"Access",out countryMessage);
                }
                GUILayout.EndHorizontal();
                if(!treaty.war&&countryCampaign.Save.world.turn<treaty.peaceUntil)GUILayout.Label("Truce / "+(treaty.peaceUntil-countryCampaign.Save.world.turn)+" turns remaining",small);
                if(treaty.alliance&&CampaignOrderButton("Call ally / 200 military aid"))countryCampaign.Diplomacy(actor,other.factionId,"Call ally",out countryMessage);
                if(treaty.war)DrawPeaceNegotiation(other.factionId);
                GUILayout.Space(18);
            }
        }
        void DrawCampaignForces()
        {
            foreach(var army in countryCampaign.Save.armies.Where(a=>a.owner==countryCampaign.Save.playerFaction).ToArray())
            {
                var region=Array.Find(catalog.regions,r=>r.id==army.regionId);
                if(GUILayout.Button(army.name+"   /   "+region.capital.name+"   /   "+army.Soldiers.ToString("N0")+" soldiers",campaignAction))SelectCampaignArmy(army);
                GUILayout.Label(CampaignForceStatus(army),small);
                GUILayout.Label(army.units.Count+" regiments  ·  movement "+army.movement+" points  ·  supply "+(army.supply*100).ToString("F0")+"%"+(CountryCampaign.Busy(army)?"  ·  training/refitting":""),small);
                GUILayout.Label(army.units.Count(u=>u.progression.AvailablePoints>0)+" regiments have unspent stat points. Select a regiment to manage gear, stats and abilities.",small);
                GUILayout.Space(12);
            }
            foreach(var q in countryCampaign.Save.recruitment.Where(q=>q.owner==countryCampaign.Save.playerFaction).ToArray())
            {GUILayout.Label(q.role+" at "+CampaignPlace(q.regionId)+" / "+q.turnsRemaining+" turns"+(countryCampaign.Besieged(q.regionId)?" / PAUSED BY SIEGE":""),small);
             if(CampaignOrderButton("Cancel recruitment / refund unfinished training")&&countryCampaign.CancelRecruitment(q.owner,q.id,out countryMessage))SaveCountry(false);}
        }
        void DrawCampaignRegions()
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;var owned=c.Owned(actor);
            GUILayout.Label(owned.Count+" cities  ·  "+owned.Sum(r=>r.buildings.Count(b=>b.kind=="Empty"))+" open plots  ·  "+owned.Sum(r=>r.buildings.Count(b=>b.turnsRemaining>0))+" construction projects  ·  "+c.Save.recruitment.Count(q=>q.owner==actor)+" regiments training",text);
            GUILayout.BeginHorizontal();
            foreach(string filter in new[]{"All","Build slots","Recruitment","Needs attention"})
                if(GUILayout.Button((provinceFilter==filter?"> ":"")+filter,campaignAction))provinceFilter=filter;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();GUILayout.Label("Sort by",small);
            foreach(string sort in new[]{"Name","Food","Unrest","Income"})if(GUILayout.Button((provinceSort==sort?"> ":"")+sort,campaignAction))provinceSort=sort;
            GUILayout.EndHorizontal();
            var regions=owned.Where(r=>provinceFilter=="All"||provinceFilter=="Build slots"&&r.buildings.Any(b=>b.kind=="Empty")||
                provinceFilter=="Recruitment"&&c.Buildings(r.regionId,"Barracks")>0||provinceFilter=="Needs attention"&&(r.economy.wellbeing<40||r.economy.unrest>=70||c.Besieged(r.regionId)||c.InRevolt(r.regionId))).ToArray();
            var listing=provinceSort=="Food"?regions.OrderBy(r=>r.economy.wellbeing):provinceSort=="Unrest"?regions.OrderByDescending(r=>r.economy.unrest):provinceSort=="Income"?regions.OrderByDescending(r=>regionCampaign.Economy.Forecast(r).NetIncome):regions.OrderBy(r=>CampaignPlace(r.regionId));
            if(regions.Length==0)GUILayout.Label("No cities match this filter.",small);
            foreach(var state in listing)
            {
                var region=Array.Find(catalog.regions,r=>r.id==state.regionId);var balance=regionCampaign.Economy.Forecast(state);
                GUILayout.Space(12);GUILayout.Label(region.capital.name+" / "+catalog.Province(region.provinceId).name,heading);
                GUILayout.Label("Food "+state.economy.wellbeing+"%  ·  unrest "+state.economy.unrest+"%  ·  local income "+balance.NetIncome.ToString("+0;-0;0")+"  ·  "+state.economy.materials.ToString("F0")+" materials",small);
                GUILayout.Label(state.buildings.Count(b=>b.kind=="Empty")+" open plots  ·  "+state.buildings.Count(b=>b.turnsRemaining>0)+" projects  ·  "+c.FreeTrainingPlaces(state.regionId)+" free training places"+(c.Besieged(state.regionId)||c.InRevolt(state.regionId)?" / WORK PAUSED":""),small);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Locate city",campaignAction)){SelectCampaignRegion(region.id);countryPage="Map";Focus(region.capital.lon,region.capital.lat,950);}
                if(GUILayout.Button("Build / upgrade",campaignAction))OpenCampaignBuilding(state.regionId);
                if(GUILayout.Button("Recruit",campaignAction))OpenCampaignRecruitment(state.regionId);
                GUILayout.EndHorizontal();DrawProvinceGovernment(state.regionId);
            }
        }
        void DrawCampaignMapSettings()
        {
            showCityTasks=EmpireTheme.Toggle(showCityTasks,"City task markers: empty plots, construction, recruitment and warnings");
            GUILayout.Label("+ plots = empty building slots  ·  build = projects underway  ·  train = recruiting regiments. Siege, revolt, food and unrest badges flag cities needing attention.",small);
            bool political=EmpireTheme.Toggle(showFactionColors,"Faction territory colors");if(political!=showFactionColors){showFactionColors=political;reliefMaterial.SetFloat("_PoliticalOpacity",political?.35f:0);}
            showRoads=EmpireTheme.Toggle(showRoads,"Roads");showTrails=EmpireTheme.Toggle(showTrails,"Trails");showRivers=EmpireTheme.Toggle(showRivers,"Rivers");showRiverways=EmpireTheme.Toggle(showRiverways,"Navigable riverways");
            bool sea=EmpireTheme.Toggle(showSea,"Maritime connections");if(sea!=showSea){showSea=sea;seaRoutes.SetActive(sea);}
            showMountains=EmpireTheme.Toggle(showMountains,"Mountain labels");DrawAtmosphereControls();DrawLandmarkControls();
            GUILayout.Space(12);GUILayout.BeginHorizontal();if(GUILayout.Button("Globe",campaignAction))Focus(20,25,22000);if(GUILayout.Button("Europe",campaignAction))Focus(9,49,4800);if(GUILayout.Button("Reset camera",campaignAction)){yaw=0;pitch=64;Focus(-1,52,2350);}GUILayout.EndHorizontal();
            GUILayout.Space(14);GUILayout.Label("FIND A SETTLEMENT",campaignCaption);GUI.SetNextControlName("townSearch");search=GUILayout.TextField(search);
            if(search.Length>0)foreach(var town in Atlas.Data.settlements.Where(t=>t.name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).Take(30))
                if(GUILayout.Button(town.name,campaignAction)){SelectCampaignRegion(catalog.ForCity(town.id).id);countryPage="Map";Focus(town.lon,town.lat,850);GUI.FocusControl(null);}
        }


        void DrawCampaignOutcome(float w,float h)
        {
            var r=CampaignWindow(w,h);CampaignFrame(r);GUILayout.BeginArea(new Rect(r.x+32,r.y+28,r.width-64,r.height-56));
            GUILayout.Label(countryCampaign.Save.outcome.ToUpperInvariant(),campaignTitle);GUILayout.Space(20);GUILayout.Label(countryCampaign.Save.victoryPath,text);
            if(countryCampaign.Save.lastBattle!=null&&GUILayout.Button("Review final battle",campaignAction))countryPage="Battles";
            if(GUILayout.Button("Save campaign",campaignAction))SaveCountry();if(GUILayout.Button("Start a new campaign",campaignAction))confirmNewCampaign=true;
            GUILayout.EndArea();
        }
        void DrawCountryArmies(float w,float h)
        {
            if(countryCampaign==null)return;var armyLabels=CampaignArmyLabelRects(w,h);
            var selectedForce=SelectedCampaignArmy();

            DrawCampaignMovementOverlay(selectedForce,w,h);
            foreach(var army in countryCampaign.Save.armies)
            {
                if(!armyLabels.TryGetValue(army.id,out var rect))continue;
                bool own=army.owner==countryCampaign.Save.playerFaction;
                bool selectedForceBadge=selectedArmyId==army.id,hot=campaignHoveredArmy==army;
                CampaignFrame(rect,true);
                var accent=selectedForceBadge?CampaignBronze:own?catalog.Faction(army.owner).MapColor:
                    countryCampaign.AtWar(countryCampaign.Save.playerFaction,army.owner)?new Color(.85f,.25f,.2f):CampaignBronze;
                EmpireTheme.Fill(new Rect(rect.x,rect.y,5,rect.height),accent);
                if(selectedForceBadge||hot)EmpireTheme.Fill(new Rect(rect.x+5,rect.y,rect.width-5,3),accent);
                GUI.Label(new Rect(rect.x+12,rect.y+4,rect.width-16,22),army.name,cardText);
                string status=own?(CountryCampaign.AtSea(army)?"AT SEA":countryCampaign.Committed(army)?"BUSY":army.movement<=0?"NEXT TURN":army.marchTarget!=""?"MARCHING":"READY"):
                    countryCampaign.AtWar(countryCampaign.Save.playerFaction,army.owner)?"ENEMY":"AT PEACE";
                GUI.Label(new Rect(rect.x+12,rect.y+27,rect.width-16,18),status+" / "+army.Soldiers.ToString("N0")+" troops"+(own?" / "+army.movement+" MP":" / "+CampaignArmyOwnerLabel(army.owner)),cardSmall);
            }
        }
        System.Collections.Generic.Dictionary<string,Rect> CampaignArmyLabelRects(float w,float h)
        {
            var labels=new System.Collections.Generic.Dictionary<string,Rect>();
            if(countryCampaign==null)return labels;
            var stacks=new System.Collections.Generic.Dictionary<string,int>();
            foreach(var army in countryCampaign.Save.armies.Where(a=>countryCampaign.CanObserve(a)))
            {
                var position=CampaignArmyPosition(army,16);
                if(Vector3.Dot(position.normalized,cam.transform.position-position)<=0)continue;
                var p=cam.WorldToScreenPoint(position);if(p.z<=0)continue;
                int row=stacks.TryGetValue(ArmyStackKey(army),out int count)?count:0;stacks[ArmyStackKey(army)]=row+1;
                var rect=new Rect(p.x/uiScale-70,(Screen.height-p.y)/uiScale+36+row*54,228,50);
                while(labels.Values.Any(other=>other.Overlaps(rect))&&rect.yMax<h-104)rect.y+=54;
                if(CampaignLabelFits(rect,w,h))labels.Add(army.id,rect);
            }
            return labels;
        }
        System.Collections.IEnumerator FranceCampaignQA(string directory)
        {
            qaRunning=true;Directory.CreateDirectory(directory);yield return new WaitForSecondsRealtime(3);
            StartCountry("france");
            if(!countryCampaign.Recruit("france","region-paris",out countryMessage))throw new Exception(countryMessage);
            string[] views={"france-opening","general-army","independent-detachment","persistent-march","france-story","first-coalition","civilian-decision"};
            var army=countryCampaign.Save.armies.First(a=>a.owner=="france");
            for(int i=0;i<views.Length;i++)
            {
                if(i==1)SelectCampaignArmy(army);
                if(i==2)
                {
                    if(!countryCampaign.Detach("france",army.id,new[]{army.units[0].id},out string id,out countryMessage))throw new Exception(countryMessage);
                    army=countryCampaign.Army(id);SelectCampaignArmy(army);
                }
                if(i==3)
                {
                    string target=countryCampaign.Save.activeRegions.First(r=>r!=army.regionId&&countryCampaign.World.State(r).owner=="france"&&countryCampaign.Route("france",army.regionId,r)!=null);
                    army.movement=25;if(!countryCampaign.March("france",army.id,target,out countryMessage))throw new Exception(countryMessage);
                    var restored=new CountryCampaign(catalog,EconomyProfiles.Load(),Atlas.Data,CampaignBalance.Load().Revolutionary(),"france",1800,CountryCampaign.RepublicScenario,Atlas);
                    if(!restored.Adopt(JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(countryCampaign.Save)),out string reason))throw new Exception(reason);
                    countryCampaign=restored;regionCampaign=restored.World;army=restored.Army(army.id);
                    Vector3 position=CampaignArmyPosition(army,16);
                    if(position.magnitude<GeographyAtlas.EarthRadiusKm)throw new Exception("Army marker below globe.");
                    var labels=CampaignArmyLabelRects(Screen.width/uiScale,Screen.height/uiScale).ToArray();
                    for(int left=0;left<labels.Length;left++)for(int right=left+1;right<labels.Length;right++)
                        if(labels[left].Value.Overlaps(labels[right].Value))throw new Exception("Army labels overlap.");
                }
                if(i==4)countryPage="Nation";
                if(i==5)
                {
                    if(!countryCampaign.EndTurn(out countryMessage))throw new Exception(countryMessage);
                    while(countryCampaign.Save.pending!=null)if(!countryCampaign.Resolve(countryCampaign.Save.pending.id,out countryMessage))throw new Exception(countryMessage);
                    if(countryCampaign.Save.story.decision!="coalition"||!countryCampaign.AtWar("france","spain")||!countryCampaign.AtWar("france","britain"))throw new Exception("First Coalition did not enter.");
                    var saved=JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(countryCampaign.Save));
                    if(!countryCampaign.Adopt(saved,out countryMessage))throw new Exception(countryMessage);
                }
                if(i==6)
                {
                    int money=countryCampaign.World.FactionState("france").treasury;
                    if(!countryCampaign.DecideRepublic("coalition",0,out countryMessage)||countryCampaign.World.FactionState("france").treasury!=money-200)throw new Exception("National decision did not pay its cost.");
                }
                yield return new WaitForSecondsRealtime(1);yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(directory,views[i]+".png"));yield return new WaitForSecondsRealtime(.4f);
            }
            File.WriteAllText(Path.Combine(directory,"verification.json"),"{\"passed\":true,\"checks\":[\"French recruitment\",\"independent detachment\",\"persistent march\",\"native save codec\",\"army marker above globe\"]}");
            Debug.Log("FRANCE_CAMPAIGN_QA_PASSED");qaRunning=false;
            if(Environment.GetCommandLineArgs().Contains("-qaExit"))Application.Quit();
        }
        System.Collections.IEnumerator CampaignUIQA(string directory)
        {
            qaRunning=true;Directory.CreateDirectory(directory);
            yield return new WaitForSecondsRealtime(4);
            string[] views={"nation-selection","province","construction","army","regiment","research","diplomacy","province-list","events","map-settings"};
            countryCampaign=null;atlasSandbox=false;countryPage="Map";campaignMenu=false;
            for(int i=0;i<views.Length;i++)
            {
                if(i==1)StartCountry("france");
                if(i==2){selectedBuildingSlot=Array.FindIndex(regionCampaign.State(selectedRegion.id).buildings,b=>b.kind=="Empty");campaignPopup="Construction";}
                if(i==3){campaignPopup="";SelectCampaignArmy(countryCampaign.Save.armies.First(a=>a.owner=="france"));}
                if(i==4)selectedRegimentId=SelectedCampaignArmy().units[0].id;
                if(i>=5){selectedRegimentId="";campaignPopup="";countryPage=i==5?"Research":i==6?"Diplomacy":i==7?"Provinces":i==8?"Events":"Map settings";campaignPageScroll=Vector2.zero;}
                yield return new WaitForSecondsRealtime(.8f);yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(directory,views[i]+".png"));yield return new WaitForSecondsRealtime(.4f);
            }
            File.WriteAllText(Path.Combine(directory,"review.txt"),"Captured campaign UI views at "+Screen.width+"x"+Screen.height+". Inspect images for clipping, readable text and layout. Capture is not a visual pass.");
            qaRunning=false;countryPage="Map";Debug.Log("CAMPAIGN_UI_QA_CAPTURED");
            if(Environment.GetCommandLineArgs().Contains("-qaExit"))Application.Quit();
        }

    }
}
