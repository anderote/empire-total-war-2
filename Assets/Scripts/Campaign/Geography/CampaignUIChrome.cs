using System;
using System.Collections.Generic;
using System.Linq;
using Empire.UI;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        static readonly Color CampaignBronze=PaperUI.Accent,CampaignPaper=PaperUI.Paper,CampaignInk=PaperUI.Ink,CampaignDark=PaperUI.Paper;
        GUIStyle campaignTitle,campaignCaption,cardTitle,cardText,cardSmall,campaignAction;
        Texture2D campaignSealDisk,campaignOverviewTexture;
        byte[] campaignOverviewSamples;Color32[] campaignOverviewPixels;
        float nextOverview;int overviewTurn=-1;
        Vector2 campaignPageScroll,regimentScroll;
        readonly List<Rect> campaignHitRects=new List<Rect>();
        string campaignPopup="";
        int selectedBuildingSlot=-1;
        bool campaignMenu,campaignOptions,campaignNavigation,campaignOverviewOpen;
        string lastCampaignToast="";float campaignToastUntil;
        readonly OptionsPanel campaignOptionsPanel=new OptionsPanel();
        readonly UITooltip campaignTips=new UITooltip();
        Rect CampaignTray(float w,float h){var r=CampaignUILayout.Tray(w,h);if(campaignOverviewOpen)r.width-=238;return r;}
        Rect CampaignWindow(float w,float h)=>CampaignUILayout.Window(w,h);
        Rect CampaignOverview(float w,float h)=>CampaignUILayout.Overview(w,h);
        bool CampaignBlocksPoint(Vector2 point,float w,float h)
        {
            if(campaignMenu||campaignOptions||selectedRegimentId!=""||campaignPopup!="")return true;
            if(credits)return true;
            if(countryCampaign==null)return point.y<72||point.y>=h-96||(atlasSandbox?point.x>w-304:new Rect((w-950)/2,150,950,h-270).Contains(point));
            if(new Rect(18,16,500,42).Contains(point)||new Rect(w-94,16,76,36).Contains(point)||new Rect(18,h-58,268,40).Contains(point)||new Rect(w-260,h-58,242,40).Contains(point))return true;
            if(campaignNavigation&&new Rect(18,h-460,224,394).Contains(point))return true;
            if(countryCampaign==null&&atlasSandbox)return point.x>w-304;
            if(countryCampaign==null)return new Rect((w-950)/2,150,950,h-270).Contains(point);
            if(campaignOverviewOpen&&CampaignOverview(w,h).Contains(point))return true;
            if(countryCampaign.Save.pending!=null||countryCampaign.Save.outcome!="")return CampaignWindow(w,h).Contains(point);
            if(countryPage!="Map"&&CampaignWindow(w,h).Contains(point))return true;
            if(countryPage=="Map"&&(selectedRegion!=null||SelectedCampaignArmy()!=null)&&CampaignTray(w,h).Contains(point))return true;
            foreach(var rect in campaignHitRects)if(rect.Contains(point))return true;
            return false;
        }
        bool CampaignLabelFits(Rect rect,float w,float h)=>rect.x>=12&&rect.xMax<=w-12&&rect.y>=82&&rect.yMax<h-104
            &&!CampaignBlocksPoint(new Vector2(rect.x,rect.y),w,h)&&!CampaignBlocksPoint(new Vector2(rect.xMax,rect.yMax),w,h);
        void EnsureCampaignStyles()
        {
            if(campaignTitle!=null)return;
            campaignTitle=PaperUI.Title(24);
            campaignCaption=PaperUI.Text(12,true,true);
            cardTitle=EmpireTheme.Display(19,CampaignInk);cardTitle.wordWrap=true;
            cardText=EmpireTheme.Text(14,CampaignInk);cardText.wordWrap=true;
            cardSmall=EmpireTheme.Text(12,PaperUI.Muted);cardSmall.wordWrap=true;
            campaignAction=PaperUI.Button(13);campaignAction.fixedHeight=36;
            campaignSealDisk=new Texture2D(64,64,TextureFormat.RGBA32,false){name="Campaign seal",filterMode=FilterMode.Bilinear};
            var pixels=new Color[4096];for(int y=0;y<64;y++)for(int x=0;x<64;x++)pixels[y*64+x]=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32))<=31?Color.white:Color.clear;
            campaignSealDisk.SetPixels(pixels);campaignSealDisk.Apply(false,true);
        }
        void CampaignFrame(Rect rect,bool parchment=false)
        {
            PaperUI.Panel(rect);
        }
        void CampaignProgress(Rect rect,float fraction,Color color)
        {EmpireTheme.Fill(rect,new Color(.12f,.11f,.09f,.35f));EmpireTheme.Fill(new Rect(rect.x,rect.y,rect.width*Mathf.Clamp01(fraction),rect.height),color);}
        void CampaignLine(Vector2 a,Vector2 b,float width,Color color)
        {
            var matrix=GUI.matrix;GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);
            EmpireTheme.Fill(new Rect(a.x,a.y-width/2,Vector2.Distance(a,b),width),color);GUI.matrix=matrix;
        }
        // Original geometric emblems: no borrowed game textures or dependency on symbol fonts.
        void CampaignIconStroke(Vector2 a,Vector2 b,float width,Color color)
        {
            // Axis-aligned samples respect scroll-view clipping; rotating GUI.matrix inside a clipped group does not.
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/(width*.6f)));
            for(int i=0;i<=steps;i++)
            {var p=Vector2.Lerp(a,b,i/(float)steps);EmpireTheme.Fill(new Rect(p.x-width*.5f,p.y-width*.5f,width,width),color);}
        }
        void CampaignIcon(Rect r,string kind,Color color)
        {
            float x=r.x,y=r.y,s=r.width;
            if(kind=="Armies"||kind=="Regular"||kind=="Rifle"||kind=="Militia")
            {CampaignIconStroke(new Vector2(x+s*.2f,y+s*.15f),new Vector2(x+s*.8f,y+s*.85f),3,color);CampaignIconStroke(new Vector2(x+s*.8f,y+s*.15f),new Vector2(x+s*.2f,y+s*.85f),3,color);CampaignIconStroke(new Vector2(x+s*.12f,y+s*.66f),new Vector2(x+s*.36f,y+s*.88f),3,color);CampaignIconStroke(new Vector2(x+s*.64f,y+s*.88f),new Vector2(x+s*.88f,y+s*.66f),3,color);}
            else if(kind=="Research"||kind=="University")
            {EmpireTheme.Fill(new Rect(x+s*.4f,y+s*.1f,s*.2f,s*.44f),color);CampaignIconStroke(new Vector2(x+s*.4f,y+s*.45f),new Vector2(x+s*.15f,y+s*.85f),3,color);CampaignIconStroke(new Vector2(x+s*.6f,y+s*.45f),new Vector2(x+s*.85f,y+s*.85f),3,color);CampaignIconStroke(new Vector2(x+s*.15f,y+s*.85f),new Vector2(x+s*.85f,y+s*.85f),3,color);}
            else if(kind=="Farm"||kind=="Granary")
            {CampaignIconStroke(new Vector2(x+s*.5f,y+s*.15f),new Vector2(x+s*.5f,y+s*.9f),2,color);for(int n=0;n<3;n++){float yy=y+s*(.28f+n*.16f);CampaignIconStroke(new Vector2(x+s*.5f,yy+s*.12f),new Vector2(x+s*.25f,yy),3,color);CampaignIconStroke(new Vector2(x+s*.5f,yy+s*.12f),new Vector2(x+s*.75f,yy),3,color);}}
            else
            {EmpireTheme.Fill(new Rect(x+s*.16f,y+s*.4f,s*.68f,s*.45f),color);for(int n=0;n<3;n++)EmpireTheme.Fill(new Rect(x+s*(.16f+n*.27f),y+s*.2f,s*.14f,s*.24f),color);EmpireTheme.Fill(new Rect(x+s*.43f,y+s*.58f,s*.14f,s*.27f),CampaignDark);}
        }
        void CampaignSeal(Rect rect,string faction)
        {
            var previous=GUI.color;GUI.color=EmpireTheme.RenderColor(CampaignBronze);GUI.DrawTexture(rect,campaignSealDisk);
            var color=catalog.Faction(faction).MapColor*.65f;color.a=1;GUI.color=EmpireTheme.RenderColor(color);
            GUI.DrawTexture(new Rect(rect.x+5,rect.y+5,rect.width-10,rect.height-10),campaignSealDisk);GUI.color=previous;
            CampaignIcon(new Rect(rect.x+13,rect.y+13,rect.width-26,rect.height-26),"Nation",CampaignPaper);
        }
        void DrawCampaignInterface(float w,float h)
        {
            EnsureCampaignStyles();campaignHitRects.Clear();
            bool dialogWasOpen=campaignPopup!="";
            if(campaignOptions)
            {
                EmpireTheme.Fill(new Rect(0,0,w,h),new Color(.02f,.03f,.035f,.92f));
                if(campaignOptionsPanel.Draw(new Rect((w-820)/2,(h-700)/2,820,700)))campaignOptions=false;return;
            }
            var managementForce=SelectedCampaignArmy();var managementUnit=managementForce?.units.Find(u=>u.id==selectedRegimentId);
            if(managementUnit!=null&&countryPage=="Map"&&campaignPopup==""&&countryCampaign.Save.pending==null){DrawCampaignRegimentDetails(w,h,managementForce,managementUnit);return;}
            bool backgroundEnabled=GUI.enabled;GUI.enabled=backgroundEnabled&&campaignPopup=="";
            string faction=countryCampaign?.Save.playerFaction;
            if(countryCampaign==null)
            {
                CampaignFrame(new Rect(0,0,w,68));GUI.Label(new Rect(24,12,330,36),"EMPIRE",campaignTitle);
                if(GUI.Button(new Rect(w-174,17,150,34),atlasSandbox?"Choose a country":"Explore the atlas",campaignAction))atlasSandbox=!atlasSandbox;
                if(atlasSandbox)DrawSidebar(w,h);else DrawCountrySelection(w,h);DrawCampaignDock(w,h);GUI.enabled=backgroundEnabled;return;
            }
            var funds=regionCampaign.FactionState(faction);
            var header=new Rect(18,16,500,42);CampaignFrame(header);
            GUI.Label(new Rect(31,26,193,22),CampaignFactionName(faction),text);
            GUI.Label(new Rect(220,28,146,20),countryCampaign.DateLabel,small);
            if(GUI.Button(new Rect(359,21,149,32),new GUIContent(funds.treasury.ToString("N0"),"Treasury / open government and trade"),campaignAction))OpenCampaignPage("Government");
            if(GUI.Button(new Rect(w-94,16,76,36),"Menu",campaignAction))campaignMenu=!campaignMenu;
            if(campaignOverviewOpen)DrawCampaignOverview(w,h);DrawCampaignDock(w,h);
            if(countryCampaign.Save.pending!=null)DrawCampaignBattle(w,h);
            else if(countryCampaign.Save.outcome!=""&&countryPage!="Battles")DrawCampaignOutcome(w,h);
            else if(countryPage!="Map")DrawCampaignPage(w,h);
            else if(SelectedCampaignArmy()!=null)DrawCampaignArmyTray(w,h);
            else if(selectedRegion!=null)DrawCampaignProvince(w,h);
            if(campaignMenu)DrawCampaignMenu(w,h);
            if(confirmNewCampaign)DrawCampaignNewCampaignConfirmation(w,h);
            if(credits)DrawCredits(w,h);
            GUI.enabled=backgroundEnabled;
            // Start newly opened GUILayout dialogs on a fresh layout event.
            if(!dialogWasOpen&&campaignPopup!="")GUIUtility.ExitGUI();
            if(dialogWasOpen&&campaignPopup!=""&&countryCampaign.Save.pending==null)
            {
                EmpireTheme.Fill(new Rect(0,0,w,h),new Color(.02f,.03f,.025f,.68f));
                DrawCampaignConstruction(w,h);
            }
            campaignTips.Draw(new Rect(0,0,w,h),true);
        }
        void DrawCampaignMetric(Rect r,string label,string value)
        {GUI.Label(new Rect(r.x,r.y,r.width,16),label,campaignCaption);GUI.Label(new Rect(r.x,r.y+16,r.width,27),value,campaignTitle);}
        void OpenCampaignPage(string page)
        {
            countryPage=page;campaignNavigation=false;campaignPopup="";choosingCampaignDestination=false;
            campaignPageScroll=Vector2.zero;GUI.FocusControl(null);
        }
        void EndCampaignTurn()
        {
            campaignPopup="";campaignNavigation=false;choosingCampaignDestination=false;
            if(countryCampaign.EndTurn(out countryMessage)){SaveCountry(false);if(countryCampaign.Save.pending==null)countryPage="Map";}
            nextPolitics=0;
        }
        void DrawCampaignDock(float w,float h)
        {
            if(countryCampaign==null)
            {
                CampaignFrame(new Rect(0,h-96,w,96));GUI.Label(new Rect(24,h-79,w-70,24),"Drag to pan  /  wheel to zoom  /  right-drag to rotate  /  WASD to travel",text);return;
            }
            if(EmpireTheme.Button(new Rect(18,h-58,132,40),"Campaign",campaignAction,campaignNavigation))campaignNavigation=!campaignNavigation;
            int warnings=countryCampaign.CampaignAlerts().Count(a=>a.priority<=1);
            if(warnings>0&&GUI.Button(new Rect(158,h-58,128,40),"Orders / "+warnings,campaignAction))OpenCampaignPage("Dispatches");
            if(EmpireTheme.Button(new Rect(w-260,h-58,88,40),"Overview",campaignAction,campaignOverviewOpen))campaignOverviewOpen=!campaignOverviewOpen;
            bool enabled=GUI.enabled;GUI.enabled=enabled&&countryCampaign.CanOrder(countryCampaign.Save.playerFaction);
            if(GUI.Button(new Rect(w-164,h-58,146,40),countryCampaign.Save.phase=="Player"?"End turn  >":countryCampaign.Save.phase,campaignAction))EndCampaignTurn();GUI.enabled=enabled;
            if(campaignNavigation)
            {
                var panel=new Rect(18,h-460,224,394);CampaignFrame(panel);campaignHitRects.Add(panel);
                string[] labels={"Government & trade","Research","Diplomacy","Lists","Building browser","Dispatches","Battle reports","Operations","Map settings"};
                string[] pages={"Government","Research","Diplomacy","Lists","Building browser","Dispatches","Battles","Operations","Map settings"};
                for(int i=0;i<pages.Length;i++)if(EmpireTheme.Button(new Rect(panel.x+10,panel.y+10+i*42,panel.width-20,36),labels[i],campaignAction,countryPage==pages[i]))OpenCampaignPage(countryPage==pages[i]?"Map":pages[i]);
            }
            if(lastCampaignToast!=countryMessage){lastCampaignToast=countryMessage;campaignToastUntil=Time.unscaledTime+8;}
            if(countryMessage!=""&&Time.unscaledTime<campaignToastUntil)
            {
                var toast=new Rect(300,h-59,w-584,40);CampaignFrame(toast);GUI.Label(new Rect(toast.x+10,toast.y+5,toast.width-20,30),countryMessage,small);campaignHitRects.Add(toast);
            }
        }
        void DrawCountrySelection(float w,float h)
        {
            var r=new Rect((w-950)/2,150,950,Mathf.Min(540,h-270));CampaignFrame(r);
            GUI.Label(new Rect(r.x+28,r.y+24,850,34),"FRANCE / THE REPUBLIC, 1792",campaignTitle);
            GUI.Label(new Rect(r.x+30,r.y+62,850,24),"Autumn 1792 to 1804: save the Republic, fight the coalitions and shape France’s future.",text);
            bool hasResume=HasResumeCampaign();bool previousEnabled=GUI.enabled;
            GUI.enabled=previousEnabled&&hasResume;
            if(GUI.Button(new Rect(r.x+28,r.yMax-48,230,32),"RESUME LAST CAMPAIGN",campaignAction))LoadCountry();
            GUI.enabled=previousEnabled;
            GUI.Label(new Rect(r.x+275,r.yMax-46,r.width-305,28),countryMessage,small);
            CampaignSeal(new Rect(r.x+45,r.y+118,100,100),"france");
            GUI.Label(new Rect(r.x+173,r.y+118,700,30),"THE REVOLUTIONARY WARS",campaignTitle);
            GUI.Label(new Rect(r.x+173,r.y+164,680,90),"The new Republic faces Austria, Prussia and Sardinia. Britain, the Dutch Republic and Spain enter in 1793. Defend the northern frontier, Rhine and Alps, raise regiments and keep France supplied.",text);
            GUI.Label(new Rect(r.x+45,r.y+269,850,67),"Begin with the Army of the North, Army of the Rhine and an Alpine reserve. Manage unrest and coalition wars; appoint Bonaparte in 1796, then choose a civilian Republic, Consulate or Empire.",text);
            if(GUI.Button(new Rect(r.x+173,r.yMax-105,320,40),hasResume?"START A NEW CAMPAIGN":"BEGIN FRANCE STORY CAMPAIGN",campaignAction))
            {if(hasResume)confirmNewCampaign=true;else StartCountry("france",true);}
        }
        void DrawCampaignNewCampaignConfirmation(float w,float h)
        {
            EmpireTheme.Fill(new Rect(0,0,w,h),new Color(.015f,.02f,.025f,.76f));
            var r=new Rect((w-520)/2,(h-210)/2,520,210);CampaignFrame(r);
            GUI.Label(new Rect(r.x+24,r.y+20,r.width-48,34),"BEGIN A NEW CAMPAIGN?",campaignTitle);
            GUI.Label(new Rect(r.x+24,r.y+66,r.width-48,54),"The current campaign stays saved. Start a fresh French campaign, or cancel and return to your current campaign.",text);
            if(GUI.Button(new Rect(r.x+24,r.yMax-56,225,36),"START NEW CAMPAIGN",campaignAction))StartCountry("france",true);
            if(GUI.Button(new Rect(r.xMax-249,r.yMax-56,225,36),"CANCEL",campaignAction))confirmNewCampaign=false;
        }
        void DrawCampaignOverview(float w,float h)
        {
            var r=CampaignOverview(w,h);CampaignFrame(r);GUI.Label(new Rect(r.x+12,r.y+8,196,20),"STRATEGIC OVERVIEW",campaignCaption);
            var map=new Rect(r.x+10,r.y+34,r.width-20,93);UpdateCampaignOverview();GUI.DrawTexture(map,campaignOverviewTexture);
            foreach(var region in catalog.regions.Where(t=>countryCampaign.Active(t.id)))
            {
                var owner=regionCampaign.State(region.id).owner;float xx=map.x+(region.capital.lon+12)/52f*map.width,yy=map.y+(62-region.capital.lat)/27f*map.height;
                EmpireTheme.Fill(new Rect(xx-2,yy-2,4,4),catalog.Faction(owner).MapColor);
            }
            float fx=map.x+(focusLon+12)/52f*map.width,fy=map.y+(62-focusLat)/27f*map.height;
            if(map.Contains(new Vector2(fx,fy))){CampaignLine(new Vector2(fx-5,fy),new Vector2(fx+5,fy),1,CampaignPaper);CampaignLine(new Vector2(fx,fy-5),new Vector2(fx,fy+5),1,CampaignPaper);}
            if(GUI.enabled&&Event.current.type==EventType.MouseDown&&Event.current.button==0&&map.Contains(Event.current.mousePosition))
            {var p=Event.current.mousePosition;Focus(-12+(p.x-map.x)/map.width*52,62-(p.y-map.y)/map.height*27,1800);Event.current.Use();}
            if(GUI.Button(new Rect(r.x+10,r.yMax-29,94,22),"Europe",button))Focus(9,49,4800);
            if(GUI.Button(new Rect(r.x+111,r.yMax-29,101,22),"Map layers",button)){countryPage=countryPage=="Map settings"?"Map":"Map settings";campaignPageScroll=Vector2.zero;}
        }
        void UpdateCampaignOverview()
        {
            const int width=202,height=93;
            if(campaignOverviewTexture==null)
            {
                campaignOverviewTexture=new Texture2D(width,height,TextureFormat.RGBA32,false){name="Campaign political overview",filterMode=FilterMode.Point};
                campaignOverviewSamples=new byte[width*height];campaignOverviewPixels=new Color32[width*height];
                var ids=EmpireCampaign.Geography.CampaignResourceData.Load("WorldGeography/region-ids").bytes;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    float lon=-12+(x+.5f)/width*52,lat=35+(y+.5f)/height*27;
                    int sx=Mathf.Clamp((int)((lon+180)/360*catalog.politicalRasterWidth),0,catalog.politicalRasterWidth-1);
                    int sy=Mathf.Clamp((int)((lat+90)/180*catalog.politicalRasterHeight),0,catalog.politicalRasterHeight-1);
                    campaignOverviewSamples[y*width+x]=ids[sy*catalog.politicalRasterWidth+sx];
                }
            }
            if(overviewTurn==countryCampaign.Save.world.turn&&Time.unscaledTime<nextOverview)return;
            var colors=new Color32[256];colors[0]=EmpireTheme.RenderColor(new Color(.12f,.18f,.18f));
            for(int i=0;i<catalog.regions.Length;i++)colors[i+1]=EmpireTheme.RenderColor(catalog.Faction(regionCampaign.State(catalog.regions[i].id).owner).MapColor*.7f+new Color(.08f,.06f,.035f,.3f));
            for(int i=0;i<campaignOverviewPixels.Length;i++)campaignOverviewPixels[i]=colors[campaignOverviewSamples[i]];
            campaignOverviewTexture.SetPixels32(campaignOverviewPixels);campaignOverviewTexture.Apply(false,false);overviewTurn=countryCampaign.Save.world.turn;nextOverview=Time.unscaledTime+.5f;
        }
        void DrawCampaignMenu(float w,float h)
        {
            var r=new Rect(w-274,77,250,315);campaignHitRects.Add(r);CampaignFrame(r);
            GUILayout.BeginArea(new Rect(r.x+16,r.y+15,r.width-32,r.height-30));
            if(GUILayout.Button("Save campaign",campaignAction))SaveCountry();
            if(GUILayout.Button("Load campaign",campaignAction))LoadCountry(false);
            if(GUILayout.Button("Game options",campaignAction)){campaignOptions=true;campaignMenu=false;}
            if(GUILayout.Button("Map sources",campaignAction)){credits=!credits;campaignMenu=false;}
            if(GUILayout.Button("New campaign",campaignAction)){confirmNewCampaign=true;campaignMenu=false;}
            if(EmpireGame.GameApplication.Available&&GUILayout.Button("Main menu",campaignAction)){if(countryCampaign==null||SaveCountry(false))EmpireGame.GameApplication.ReturnToMenu();}
            if(GUILayout.Button("Close menu",campaignAction))campaignMenu=false;
            GUILayout.EndArea();
        }
    }
    /// <summary>Shared logical coordinates for rendering, pointer exclusion and review.</summary>
    public static class CampaignUILayout
    {
        public static float Scale(float width,float height)=>(float)Math.Max(.4,Math.Min(1.6*GamePreferences.InterfaceScale,Math.Min(height/1000,width/1280)));
        public static Rect Tray(float width,float height)=>new Rect(24,height-352,width-48,278);
        public static Rect Window(float width,float height)
        {float span=Math.Min(1010,width-96);return new Rect((width-span)/2,94,span,Math.Min(620,height-300));}
        public static Rect Dialog(float width,float height)
        {float span=Math.Min(980,width-64),heightLimit=Math.Min(720,height-190);return new Rect((width-span)/2,84,span,heightLimit);}
        public static Rect Overview(float width,float height)=>new Rect(width-246,height-240,222,164);
    }

}
