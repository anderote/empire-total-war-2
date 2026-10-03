using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        string movementOverlayKey="";
        List<CampaignMapPoint[]> movementEdges=new List<CampaignMapPoint[]>();
        CampaignLandPath hoverLandPath;
        void CampaignGeoLine(GeoPoint a,GeoPoint b,Color color,float width,float w,float h)
        {
            var aw=Atlas.World(a.lon,a.lat,22);var bw=Atlas.World(b.lon,b.lat,22);
            if(Vector3.Dot(aw.normalized,cam.transform.position-aw)<=0||Vector3.Dot(bw.normalized,cam.transform.position-bw)<=0)return;
            var sa=cam.WorldToScreenPoint(aw);var sb=cam.WorldToScreenPoint(bw);if(sa.z<=0||sb.z<=0)return;
            var aa=new Vector2(sa.x/uiScale,(Screen.height-sa.y)/uiScale);var bb=new Vector2(sb.x/uiScale,(Screen.height-sb.y)/uiScale);
            if(!CampaignBlocksPoint(aa,w,h)&&!CampaignBlocksPoint(bb,w,h))CampaignLine(aa,bb,width,color);
        }
        void CampaignRangeRing(GeoPoint p,float km,Color color,float w,float h)
        {
            CampaignMapPoint first=null,previous=null;
            for(int i=0;i<=48;i++)
            {
                double angle=i*Math.PI/24;
                var at=new CampaignMapPoint(p.lon+(float)(Math.Cos(angle)*km/(111.32*Math.Max(.2,Math.Cos(p.lat*Math.PI/180)))),p.lat+(float)(Math.Sin(angle)*km/111.32));
                if(first==null)first=at;
                if(previous!=null)CampaignGeoLine(previous,at,color,2,w,h);previous=at;
            }
        }
        void DrawCampaignMovementOverlay(CampaignArmy army,float w,float h)
        {
            if(army==null||CountryCampaign.AtSea(army)||countryPage!="Map")return;
            var actor=countryCampaign.Save.playerFaction;
            foreach(var force in countryCampaign.Save.armies.Where(a=>!CountryCampaign.AtSea(a)&&countryCampaign.CanObserve(a)))
                CampaignRangeRing(countryCampaign.Position(force),CountryCampaign.InterceptionRangeKm,
                    countryCampaign.AtWar(actor,force.owner)?new Color(.9f,.25f,.18f,.8f):new Color(.3f,.62f,.4f,.6f),w,h);
            foreach(string region in countryCampaign.Save.activeRegions.Where(r=>countryCampaign.AtWar(actor,regionCampaign.State(r).owner)&&countryCampaign.Buildings(r,"Fortress")>0))
                CampaignRangeRing(countryCampaign.CityPosition(region),CountryCampaign.SettlementRangeKm,new Color(.9f,.25f,.18f,.7f),w,h);
            string key=army.id+"/"+army.longitude+"/"+army.latitude+"/"+army.movement+"/"+countryCampaign.Save.world.turn+"/"+
                string.Join(";",countryCampaign.Save.armies.Select(a=>a.id+":"+a.longitude+":"+a.latitude))+"/"+
                string.Join(";",countryCampaign.Save.treaties.Select(t=>t.war+":"+t.access));
            if(key!=movementOverlayKey)
            {movementOverlayKey=key;movementEdges=army.movement>0&&!countryCampaign.Committed(army)?countryCampaign.MovementBoundary(army):new List<CampaignMapPoint[]>();}
            foreach(var edge in movementEdges)CampaignGeoLine(edge[0],edge[1],new Color(.92f,.8f,.35f,.85f),2,w,h);
            var path=hoverLandPath?.points??army.marchPath;GeoPoint previous=countryCampaign.Position(army);
            foreach(var p in path){CampaignGeoLine(previous,p,CampaignBronze,3,w,h);previous=p;}
        }
    }
}
