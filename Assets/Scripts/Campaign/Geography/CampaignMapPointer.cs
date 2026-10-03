using System.Collections.Generic;
using System.Linq;
using Empire.UI;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        enum CampaignCursor { Default, Select, Move, Attack, Blocked }
        readonly Dictionary<CampaignCursor,Texture2D> campaignCursors=new Dictionary<CampaignCursor,Texture2D>();
        CampaignCursor campaignCursor,appliedCampaignCursor;
        CampaignArmy campaignHoveredArmy;
        string campaignPointerRegion="",campaignPointerHint="";
        bool campaignPointerAllowed,campaignPressOnMap,campaignPointerSettlement;
        CampaignMapPoint campaignPointerDestination;
        string campaignPointerArmyId="",landPreviewKey="",landPreviewReason="";
        bool landPreviewAllowed;
        float landPreviewTime;
        CampaignLandPath cachedHoverLandPath;

        void ResetCampaignPointer()
        {
            campaignHoveredArmy=null;campaignPointerRegion="";campaignPointerHint="";
            campaignPointerAllowed=false;campaignPointerDestination=null;campaignPointerArmyId="";campaignPointerSettlement=false;campaignCursor=CampaignCursor.Default;
            campaignMovePreview="";previewKey="";hoverLandPath=null;
        }
        void UpdateCampaignPointer(Vector2 mouse,Vector2 ui,float w,float h)
        {
            if(countryCampaign==null||countryPage!="Map"||countryCampaign.Save.pending!=null||countryCampaign.Save.outcome!="")return;
            var labels=CampaignArmyLabelRects(w,h);
            foreach(var pair in labels)
                if(pair.Value.Contains(ui)){campaignHoveredArmy=countryCampaign.Army(pair.Key);break;}
            if(campaignHoveredArmy==null)
            {
                float nearest=float.MaxValue;
                foreach(var army in countryCampaign.Save.armies.Where(a=>countryCampaign.CanObserve(a)))
                {
                    if(!armyTokens.TryGetValue(army.id,out var token)||!token.activeSelf)continue;
                    var at=token.transform.position;
                    if(Vector3.Dot(at.normalized,cam.transform.position-at)<=0)continue;
                    var basePoint=cam.WorldToScreenPoint(at);
                    var topPoint=cam.WorldToScreenPoint(at+token.transform.up*token.transform.localScale.x*1.55f);
                    if(basePoint.z<=0||topPoint.z<=0)continue;
                    var bottom=new Vector2(basePoint.x/uiScale,(Screen.height-basePoint.y)/uiScale);
                    var top=new Vector2(topPoint.x/uiScale,(Screen.height-topPoint.y)/uiScale);
                    var hit=Rect.MinMaxRect(Mathf.Min(bottom.x,top.x)-22,Mathf.Min(bottom.y,top.y)-16,
                        Mathf.Max(bottom.x,top.x)+22,Mathf.Max(bottom.y,top.y)+16);
                    float delta=Vector2.SqrMagnitude(ui-(bottom+top)*.5f);
                    if(hit.Contains(ui)&&delta<nearest){campaignHoveredArmy=army;nearest=delta;}
                }
            }
            var actor=countryCampaign.Save.playerFaction;
            if(campaignHoveredArmy!=null)
            {campaignPointerRegion=campaignHoveredArmy.regionId;campaignPointerDestination=countryCampaign.Position(campaignHoveredArmy);campaignPointerArmyId=campaignHoveredArmy.id;}
            else if(hovered>=0)
            {
                var city=catalog.ForCity(Atlas.Data.settlements[hovered].id);
                if(city!=null){campaignPointerRegion=city.id;campaignPointerDestination=countryCampaign.CityPosition(city.id);campaignPointerSettlement=true;}
            }
            else if(GlobePoint(mouse,out var location))
            {campaignPointerDestination=new CampaignMapPoint(location.x,location.y);campaignPointerRegion=countryCampaign.Terrain.Ground(campaignPointerDestination).regionId;}
            var selectedForce=SelectedCampaignArmy();
            if(selectedForce==null||campaignHoveredArmy==selectedForce)
            {
                campaignCursor=CampaignCursor.Select;
                campaignPointerHint=campaignHoveredArmy!=null?"SELECT / "+campaignHoveredArmy.name+"\n"+CampaignForceStatus(campaignHoveredArmy):"INSPECT / "+CampaignPlace(campaignPointerRegion);
                return;
            }
            if(campaignPointerDestination==null)return;
            bool hostile=countryCampaign.AtWar(actor,campaignHoveredArmy?.owner??regionCampaign.State(campaignPointerRegion)?.owner);
            string key=selectedForce.id+"/"+selectedForce.longitude+"/"+selectedForce.latitude+"/"+selectedForce.movement+"/"+
                Mathf.RoundToInt(campaignPointerDestination.lon*20)+"/"+Mathf.RoundToInt(campaignPointerDestination.lat*20)+"/"+campaignPointerArmyId+"/"+campaignPointerSettlement;
            if(key!=landPreviewKey||Time.unscaledTime-landPreviewTime>1)
            {
                landPreviewKey=key;landPreviewTime=Time.unscaledTime;
                landPreviewAllowed=countryCampaign.CanMarchTo(actor,selectedForce.id,campaignPointerDestination.lon,campaignPointerDestination.lat,out landPreviewReason,campaignPointerArmyId,campaignPointerSettlement);
            }
            campaignPointerAllowed=landPreviewAllowed;
            if(landPreviewAllowed&&Time.unscaledTime-landPreviewTime<.01f)cachedHoverLandPath=countryCampaign.LandPath(selectedForce,campaignPointerDestination);
            hoverLandPath=landPreviewAllowed?cachedHoverLandPath:null;
            campaignCursor=campaignPointerAllowed?(hostile?CampaignCursor.Attack:CampaignCursor.Move):CampaignCursor.Blocked;
            string action=campaignPointerAllowed?(hostile?"ATTACK":campaignHoveredArmy!=null?"JOIN ARMY":"MARCH"):"CANNOT ORDER";
            campaignPointerHint=action+" / "+(campaignHoveredArmy?.name??(campaignPointerSettlement?CampaignPlace(campaignPointerRegion):"Open territory"))+"\n"+landPreviewReason;
        }
        void HandleCampaignPointerClick(bool right)
        {
            if(countryCampaign==null||countryPage!="Map")
            {
                if(!right&&hovered>=0)Select(hovered);
                else if(!right&&GlobePoint(Input.mousePosition,out var location))
                {var region=catalog.At(location.x,location.y);if(region!=null)SelectCampaignRegion(region.id);}
                return;
            }
            if(!right&&!choosingCampaignDestination)
            {
                if(campaignHoveredArmy!=null&&campaignHoveredArmy.owner==countryCampaign.Save.playerFaction){SelectCampaignArmy(campaignHoveredArmy);return;}
                if(campaignPointerRegion!="")
                {
                    if(hovered>=0&&cityTaskRects.TryGetValue(hovered,out var task)&&task.Contains(new Vector2(Input.mousePosition.x/uiScale,(Screen.height-Input.mousePosition.y)/uiScale)))OpenCampaignCityTasks(hovered);
                    else SelectCampaignRegion(campaignPointerRegion);
                }
                return;
            }
            var army=SelectedCampaignArmy();if(army==null||campaignPointerDestination==null)return;
            if(!campaignPointerAllowed){countryMessage=landPreviewReason;return;}
            var ids=army.units.Where(u=>campaignRegimentSelection.Contains(u.id)).Select(u=>u.id).ToArray();
            if(ids.Length>0&&ids.Length<army.units.Count)
            {
                if(!countryCampaign.Detach(army.owner,army.id,ids,out string detached,out countryMessage))return;
                army=countryCampaign.Army(detached);selectedArmyId=detached;campaignRegimentSelection.Clear();
            }
            if(countryCampaign.MarchTo(army.owner,army.id,campaignPointerDestination.lon,campaignPointerDestination.lat,out countryMessage,campaignPointerArmyId,campaignPointerSettlement))
            {
                if(countryCampaign.Army(army.id)==null&&countryCampaign.Army(campaignPointerArmyId)!=null)selectedArmyId=campaignPointerArmyId;
                SaveCountry(false);
            }
            choosingCampaignDestination=false;landPreviewKey="";nextPolitics=0;
        }
        void DrawCampaignPointerHint(float w,float h)
        {
            if(campaignPointerHint==""||campaignMenu||campaignPopup!=""||countryPage!="Map")return;
            var mouse=new Vector2(Input.mousePosition.x/uiScale,(Screen.height-Input.mousePosition.y)/uiScale);
            float width=Mathf.Min(380,w-24);
            var rect=new Rect(Mathf.Clamp(mouse.x+26,12,w-width-12),Mathf.Clamp(mouse.y+26,82,Mathf.Max(82,h-232)),width,126);
            CampaignFrame(rect);
            GUI.Label(new Rect(rect.x+12,rect.y+8,rect.width-24,rect.height-16),campaignPointerHint,small);
        }
        void ApplyCampaignCursor()
        {
            var kind=Application.isFocused&&!qaRunning?campaignCursor:CampaignCursor.Default;
            if(kind==appliedCampaignCursor)return;
            Texture2D texture=null;
            if(kind!=CampaignCursor.Default&&!campaignCursors.TryGetValue(kind,out texture))
            {texture=CreateCampaignCursor(kind);campaignCursors.Add(kind,texture);}
            Cursor.SetCursor(texture,Vector2.zero,CursorMode.Auto);appliedCampaignCursor=kind;
        }
        Texture2D CreateCampaignCursor(CampaignCursor kind)
        {
            const int size=32;var pixels=new Color32[size*size];
            Color32 color=kind==CampaignCursor.Attack?new Color32(255,96,74,255):kind==CampaignCursor.Blocked?new Color32(255,177,89,255):new Color32(246,226,164,255);
            void Pixel(int x,int y,Color32 c){if(x>=0&&x<size&&y>=0&&y<size)pixels[(size-1-y)*size+x]=c;}
            void Line(int x,int y,int endX,int endY)
            {
                int steps=Mathf.Max(Mathf.Abs(endX-x),Mathf.Abs(endY-y));
                for(int i=0;i<=steps;i++)
                {
                    int px=Mathf.RoundToInt(Mathf.Lerp(x,endX,i/(float)Mathf.Max(1,steps))),py=Mathf.RoundToInt(Mathf.Lerp(y,endY,i/(float)Mathf.Max(1,steps)));
                    for(int a=-2;a<=2;a++)for(int b=-2;b<=2;b++)Pixel(px+a,py+b,new Color32(20,23,25,255));
                }
                for(int i=0;i<=steps;i++)Pixel(Mathf.RoundToInt(Mathf.Lerp(x,endX,i/(float)Mathf.Max(1,steps))),Mathf.RoundToInt(Mathf.Lerp(y,endY,i/(float)Mathf.Max(1,steps))),color);
            }
            Line(1,1,1,16);Line(1,1,12,12);Line(1,16,5,12);Line(5,12,12,12);
            if(kind==CampaignCursor.Attack){Line(15,16,28,29);Line(28,16,15,29);Line(14,26,18,30);Line(25,30,29,26);}
            else if(kind==CampaignCursor.Blocked)
            {for(int i=0;i<16;i++){float a=i*Mathf.PI/8,b=(i+1)*Mathf.PI/8;Line(22+Mathf.RoundToInt(7*Mathf.Cos(a)),22+Mathf.RoundToInt(7*Mathf.Sin(a)),22+Mathf.RoundToInt(7*Mathf.Cos(b)),22+Mathf.RoundToInt(7*Mathf.Sin(b)));}Line(17,17,27,27);}
            else if(kind==CampaignCursor.Move){Line(15,29,28,16);Line(20,16,28,16);Line(28,16,28,24);}
            else {Line(16,17,16,28);Line(16,28,28,28);Line(28,28,28,17);Line(28,17,16,17);}
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Campaign "+kind+" cursor"};
            texture.SetPixels32(pixels);texture.Apply();return texture;
        }
        void RestoreCampaignCursor()
        {if(appliedCampaignCursor!=CampaignCursor.Default)Cursor.SetCursor(null,Vector2.zero,CursorMode.Auto);appliedCampaignCursor=CampaignCursor.Default;}
        void OnDisable()=>RestoreCampaignCursor();
    }
}
