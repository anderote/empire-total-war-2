using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    [Serializable] public sealed class NavigableRiver : GeoPath
    {
        public string id,notes;
        public float maxDraftMeters;
        public bool seaConnected;
    }
    [Serializable] public sealed class RiverwayCatalog
    {
        public int schemaVersion;
        public string notes;
        public NavigableRiver[] reaches;
        public static RiverwayCatalog Load()=>JsonUtility.FromJson<RiverwayCatalog>(EmpireCampaign.Geography.CampaignResourceData.Load("WorldGeography/riverways").text);
        public NavigableRiver Nearby(float lon,float lat,float radiusKm=20)
        {
            NavigableRiver nearest=null;float best=radiusKm;
            foreach(var reach in reaches)
            {
                RiverwayNavigation.Project(reach,new Vector2(lon,lat),out _,out _,out float distance);
                if(distance<best){best=distance;nearest=reach;}
            }
            return nearest;
        }
    }
    public sealed class RiverJourney
    {
        public NavigableRiver River;
        public GeoPoint[] Points;
        public float DistanceKm;
    }
    /// <summary>Shallow-draft ship navigation on continuous authored reaches.
    /// Does not bridge gaps, cataracts, canals or unrelated tributaries.</summary>
    public static class RiverwayNavigation
    {
        public static RiverJourney Find(RiverwayCatalog catalog,Vector2 from,Vector2 to,float draftMeters,float snapKm=5)
        {
            if(draftMeters<=0||snapKm<0||catalog==null)return null;
            RiverJourney best=null;
            foreach(var river in catalog.reaches)
            {
                if(draftMeters>river.maxDraftMeters)continue;
                Project(river,from,out float a,out GeoPoint p,out float da);
                Project(river,to,out float b,out GeoPoint q,out float db);
                if(da>snapKm||db>snapKm)continue;
                bool reverse=a>b;float low=Mathf.Min(a,b),high=Mathf.Max(a,b);
                var points=new List<GeoPoint>{reverse?q:p};
                for(int i=Mathf.FloorToInt(low)+1;i<river.points.Length&&i<high;i++)points.Add(river.points[i]);
                points.Add(reverse?p:q);if(reverse)points.Reverse();
                float distance=0;for(int i=1;i<points.Count;i++)distance+=Distance(points[i-1],points[i]);
                if(best==null||distance<best.DistanceKm)best=new RiverJourney{River=river,Points=points.ToArray(),DistanceKm=distance};
            }
            return best;
        }
        public static void Project(NavigableRiver river,Vector2 position,out float cursor,out GeoPoint projected,out float distanceKm)
        {
            cursor=0;projected=null;distanceKm=float.PositiveInfinity;
            float scale=111.32f*Mathf.Max(.05f,Mathf.Cos(position.y*Mathf.Deg2Rad));
            for(int i=1;i<river.points.Length;i++)
            {
                var a=river.points[i-1];var b=river.points[i];
                Vector2 p=new Vector2(Mathf.DeltaAngle(position.x,a.lon)*scale,(a.lat-position.y)*111.32f);
                Vector2 q=new Vector2(Mathf.DeltaAngle(position.x,b.lon)*scale,(b.lat-position.y)*111.32f);
                Vector2 delta=q-p;float t=delta.sqrMagnitude<.000001f?0:Mathf.Clamp01(-Vector2.Dot(p,delta)/delta.sqrMagnitude);
                float distance=(p+delta*t).magnitude;
                if(distance>=distanceKm)continue;
                distanceKm=distance;cursor=i-1+t;projected=new GeoPoint{lon=a.lon+Mathf.DeltaAngle(a.lon,b.lon)*t,lat=Mathf.Lerp(a.lat,b.lat,t)};
            }
        }
        public static float Distance(GeoPoint a,GeoPoint b)=>Vector3.Distance(GeographyAtlas.Normal(a.lon,a.lat),GeographyAtlas.Normal(b.lon,b.lat))*GeographyAtlas.EarthRadiusKm;
    }
}
