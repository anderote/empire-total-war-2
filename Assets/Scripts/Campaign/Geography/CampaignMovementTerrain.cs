using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    [Serializable] public sealed class CampaignMapPoint : GeoPoint
    {
        public CampaignMapPoint() { }
        public CampaignMapPoint(float longitude,float latitude){lon=longitude;lat=latitude;}
    }
    public sealed class CampaignGround
    {
        public string regionId,name;
        public float cost=1,attrition,supplyLoss;
        public bool blocked,road;
    }
    public sealed class CampaignLandPath
    {
        public List<CampaignMapPoint> points=new List<CampaignMapPoint>();
        public float distance,cost;
        public bool hazardous;
    }
    /// <summary>Free destinations over a terrain grid. Roads affect cost, never connectivity.</summary>
    public sealed class CampaignMovementTerrain
    {
        public const float LongitudeStep=.18f,LatitudeStep=.125f;
        readonly RegionCatalog catalog;
        readonly GeographyAtlas atlas;
        readonly Dictionary<int,List<CampaignRegion>> regions=new Dictionary<int,List<CampaignRegion>>();
        readonly Dictionary<int,List<Segment>> roads=new Dictionary<int,List<Segment>>(),rivers=new Dictionary<int,List<Segment>>();
        readonly Dictionary<long,CampaignGround> samples=new Dictionary<long,CampaignGround>();
        sealed class Segment {public GeoPoint a,b;public float bonus;}
        public CampaignMovementTerrain(RegionCatalog catalog,TheaterData geography,GeographyAtlas atlas)
        {
            this.catalog=catalog;this.atlas=atlas;
            foreach(var region in catalog.regions)foreach(var polygon in region.polygons??Array.Empty<RegionPolygon>())
            {
                var points=polygon.rings.FirstOrDefault()?.points;if(points==null||points.Length==0)continue;
                for(int y=(int)Math.Floor(points.Min(p=>p.lat));y<=(int)Math.Floor(points.Max(p=>p.lat));y++)
                for(int x=(int)Math.Floor(points.Min(p=>p.lon));x<=(int)Math.Floor(points.Max(p=>p.lon));x++)
                {int key=RegionKey(x,y);if(!regions.TryGetValue(key,out var list))regions[key]=list=new List<CampaignRegion>();if(!list.Contains(region))list.Add(region);}
            }
            foreach(var route in geography.routes??Array.Empty<TravelRoute>())if(route.mode=="land")Index(roads,route.points,route.surface=="trail"?.9f:.7f);
            foreach(var river in geography.rivers??Array.Empty<GeoPath>())Index(rivers,river.points,1);
        }
        static int RegionKey(int x,int y)=>(y+90)*360+x+180;
        static int FeatureKey(int x,int y)=>(y+180)*720+x+360;
        static long Key(int x,int y)=>((long)x<<32)|(uint)y;
        static void Index(Dictionary<int,List<Segment>> index,GeoPoint[] points,float bonus)
        {
            if(points==null)return;
            for(int i=1;i<points.Length;i++)
            {
                var segment=new Segment{a=points[i-1],b=points[i],bonus=bonus};
                for(int y=(int)Math.Floor((Math.Min(segment.a.lat,segment.b.lat)-.05f)*2);y<=(int)Math.Floor((Math.Max(segment.a.lat,segment.b.lat)+.05f)*2);y++)
                for(int x=(int)Math.Floor((Math.Min(segment.a.lon,segment.b.lon)-.08f)*2);x<=(int)Math.Floor((Math.Max(segment.a.lon,segment.b.lon)+.08f)*2);x++)
                {int key=FeatureKey(x,y);if(!index.TryGetValue(key,out var list))index[key]=list=new List<Segment>();list.Add(segment);}
            }
        }
        public CampaignRegion Region(float lon,float lat)
        {
            if(!regions.TryGetValue(RegionKey((int)Math.Floor(lon),(int)Math.Floor(lat)),out var list))return null;
            return list.FirstOrDefault(r=>r.Contains(lon,lat));
        }
        public static float Distance(GeoPoint a,GeoPoint b)
        {
            double rad=Math.PI/180,lat=(a.lat+b.lat)*.5*rad;
            return (float)(111.32*Math.Sqrt(Math.Pow((a.lon-b.lon)*Math.Cos(lat),2)+Math.Pow(a.lat-b.lat,2)));
        }
        static float Near(Segment s,GeoPoint p)
        {
            float scale=(float)Math.Cos(p.lat*Math.PI/180);
            var a=new Vector2((s.a.lon-p.lon)*scale,s.a.lat-p.lat);var b=new Vector2((s.b.lon-p.lon)*scale,s.b.lat-p.lat);
            var delta=b-a;float t=delta.sqrMagnitude>0?Mathf.Clamp01(-Vector2.Dot(a,delta)/delta.sqrMagnitude):0;
            return (a+delta*t).magnitude*111.32f;
        }
        public CampaignGround Ground(GeoPoint p)
        {
            if(!Valid(p))return new CampaignGround{name="Outside map",blocked=true};
            long key=Key((int)Math.Round(p.lon*40),(int)Math.Round(p.lat*40));
            if(samples.TryGetValue(key,out var ground))return ground;
            var region=Region(p.lon,p.lat);
            ground=new CampaignGround{regionId=region?.id??"",name="Open country"};
            // Settlement anchors survive minor coast/polygon raster disagreement; this never joins islands.
            if(region==null)region=catalog.regions.FirstOrDefault(r=>Distance(r.capital,p)<1.5f);
            ground.regionId=region?.id??"";
            bool city=region!=null&&Distance(region.capital,p)<1.5f;
            if(region==null||atlas!=null&&!atlas.IsDry(p.lon,p.lat)&&!city)
            {ground.name="Water";ground.blocked=true;samples[key]=ground;return ground;}
            int feature=FeatureKey((int)Math.Floor(p.lon*2),(int)Math.Floor(p.lat*2));
            float bonus=1;
            if(roads.TryGetValue(feature,out var nearby))foreach(var s in nearby)if(Near(s,p)<3)bonus=Math.Min(bonus,s.bonus);
            ground.road=bonus<1;
            float height=atlas==null?0:Math.Max(0,atlas.HeightMeters(p.lon,p.lat));
            float relief=atlas==null?0:Math.Max(Math.Abs(height-atlas.HeightMeters(p.lon+.035f,p.lat)),Math.Abs(height-atlas.HeightMeters(p.lon,p.lat+.035f)));
            float snow=CampaignLandscape.Snow(p.lat,height),arid=CampaignLandscape.Aridity(p.lon,p.lat);
            if(height>3000&&!ground.road||height>3800||relief>1100&&!ground.road)
            {ground.name="Impassable mountains";ground.blocked=true;}
            else if(height>1400||relief>350){ground.name="Mountains";ground.cost=2.8f;}
            else if(height>550||relief>150){ground.name="Hills";ground.cost=1.65f;}
            else if(arid>.45f){ground.name="Desert";ground.cost=1.7f;}
            else if(atlas!=null&&CampaignLandscape.Forest(p.lon,p.lat,height)>.55f){ground.name="Forest";ground.cost=1.45f;}
            if(arid>.45f){ground.attrition=.035f*arid;ground.supplyLoss=.25f*arid;}
            if(snow>.35f){ground.name="Snow / "+ground.name;ground.cost*=1.35f;ground.attrition=Math.Max(ground.attrition,.04f*snow);ground.supplyLoss=Math.Max(ground.supplyLoss,.2f*snow);}
            if(ground.road){ground.cost=Math.Max(bonus,ground.cost*bonus);ground.name+=(bonus<.8f?" / road":" / trail");}
            if(rivers.TryGetValue(feature,out nearby)&&nearby.Any(s=>Near(s,p)<1))
            {ground.cost*=ground.road?1.1f:1.6f;ground.name+=" / river crossing";}
            samples[key]=ground;return ground;
        }
        public static bool Valid(GeoPoint p)=>p!=null&&!float.IsNaN(p.lon)&&!float.IsInfinity(p.lon)&&!float.IsNaN(p.lat)&&!float.IsInfinity(p.lat)&&p.lon>=-180&&p.lon<=180&&p.lat>=-85&&p.lat<=85;
        public static CampaignMapPoint Lerp(GeoPoint a,GeoPoint b,float t)=>new CampaignMapPoint(a.lon+(b.lon-a.lon)*t,a.lat+(b.lat-a.lat)*t);
        public float SegmentCost(GeoPoint a,GeoPoint b,Func<string,bool> access,out bool hazardous)
        {
            hazardous=false;float distance=Distance(a,b),cost=0;int steps=Math.Max(1,(int)Math.Ceiling(distance/3.5f));
            for(int i=0;i<=steps;i++)
            {
                var p=Lerp(a,b,i/(float)steps);var ground=Ground(p);
                if(ground.blocked||!access(ground.regionId))return float.PositiveInfinity;
                if(i<steps)cost+=ground.cost*distance/steps;
                hazardous|=ground.attrition>0;
            }
            return cost;
        }
        // Binary heap keeps long overland orders bounded without quadratic open-list scans.
        sealed class Open
        {
            readonly List<(long key,float score,float cost)> rows=new List<(long,float,float)>();
            public int Count=>rows.Count;
            public void Add(long key,float score,float cost)
            {rows.Add((key,score,cost));int i=rows.Count-1;while(i>0){int p=(i-1)/2;if(rows[p].score<=score)break;rows[i]=rows[p];i=p;}rows[i]=(key,score,cost);}
            public (long key,float score,float cost) Pop()
            {var first=rows[0];var last=rows[rows.Count-1];rows.RemoveAt(rows.Count-1);if(rows.Count==0)return first;int i=0;while(i*2+1<rows.Count){int c=i*2+1;if(c+1<rows.Count&&rows[c+1].score<rows[c].score)c++;if(rows[c].score>=last.score)break;rows[i]=rows[c];i=c;}rows[i]=last;return first;}
        }
        static CampaignMapPoint Point(long key)=>new CampaignMapPoint((int)(key>>32)*LongitudeStep,(int)key*LatitudeStep);
        static IEnumerable<long> Adjacent(long key)
        {int x=(int)(key>>32),y=(int)key;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)if(dx!=0||dy!=0)yield return Key(x+dx,y+dy);}
        public CampaignLandPath Find(GeoPoint from,GeoPoint to,Func<string,bool> access)
        {
            if(!Valid(from)||!Valid(to)||Ground(to).blocked||!access(Ground(to).regionId))return null;
            var open=new Open();var costs=new Dictionary<long,float>();var previous=new Dictionary<long,long>();var done=new HashSet<long>();
            int x=(int)Math.Round(from.lon/LongitudeStep),y=(int)Math.Round(from.lat/LatitudeStep);
            // Exact endpoints are connected to all nearby nodes, avoiding coast/grid snapping failures.
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {long k=Key(x+dx,y+dy);float c=SegmentCost(from,Point(k),access,out _);if(float.IsInfinity(c))continue;costs[k]=c;open.Add(k,c+Distance(Point(k),to)*.7f,c);}
            float best=SegmentCost(from,to,access,out _);long end=0;bool direct=!float.IsInfinity(best),found=direct;
            int count=0;
            while(open.Count>0&&count++<60000)
            {
                var row=open.Pop();if(row.score>=best)break;if(done.Contains(row.key)||row.cost>costs[row.key])continue;done.Add(row.key);var p=Point(row.key);
                if(Distance(p,to)<32)
                {float finish=SegmentCost(p,to,access,out _);if(row.cost+finish<best){best=row.cost+finish;end=row.key;direct=false;found=true;}}
                foreach(long k in Adjacent(row.key))
                {if(done.Contains(k))continue;var q=Point(k);if(q.lon<Math.Min(from.lon,to.lon)-6||q.lon>Math.Max(from.lon,to.lon)+6||q.lat<Math.Min(from.lat,to.lat)-6||q.lat>Math.Max(from.lat,to.lat)+6)continue;float c=row.cost+SegmentCost(p,q,access,out _);if(float.IsInfinity(c)||costs.TryGetValue(k,out float old)&&old<=c)continue;costs[k]=c;previous[k]=row.key;open.Add(k,c+Distance(q,to)*.7f,c);}
            }
            if(!found)return null;var path=new CampaignLandPath{cost=best};
            if(!direct){long k=end;path.points.Add(Point(k));while(previous.TryGetValue(k,out long prev)){k=prev;path.points.Add(Point(k));}path.points.Reverse();}
            path.points.Add(new CampaignMapPoint(to.lon,to.lat));var last=from;
            foreach(var p in path.points){path.distance+=Distance(last,p);SegmentCost(last,p,access,out bool hazard);path.hazardous|=hazard;last=p;}
            return path;
        }
        public List<CampaignMapPoint[]> ReachBoundary(GeoPoint from,float allowance,Func<string,bool> access,Func<GeoPoint,bool> blocked)
        {
            var open=new Open();var costs=new Dictionary<long,float>();var reached=new HashSet<long>();
            int x=(int)Math.Round(from.lon/LongitudeStep),y=(int)Math.Round(from.lat/LatitudeStep);
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {long k=Key(x+dx,y+dy);float c=SegmentCost(from,Point(k),access,out _);if(c>allowance)continue;costs[k]=c;open.Add(k,c,c);}
            while(open.Count>0&&reached.Count<16000)
            {
                var row=open.Pop();if(reached.Contains(row.key)||row.cost>allowance)continue;reached.Add(row.key);var p=Point(row.key);
                if(blocked(p)&&Distance(from,p)>3)continue;
                foreach(long k in Adjacent(row.key))
                {if(reached.Contains(k))continue;float c=row.cost+SegmentCost(p,Point(k),access,out _);if(c>allowance||costs.TryGetValue(k,out float old)&&old<=c)continue;costs[k]=c;open.Add(k,c,c);}
            }
            var edges=new List<CampaignMapPoint[]>();
            foreach(long k in reached)
            {
                int gx=(int)(k>>32),gy=(int)k;var p=Point(k);float hx=LongitudeStep/2,hy=LatitudeStep/2;
                if(!reached.Contains(Key(gx-1,gy)))edges.Add(new[]{new CampaignMapPoint(p.lon-hx,p.lat-hy),new CampaignMapPoint(p.lon-hx,p.lat+hy)});
                if(!reached.Contains(Key(gx+1,gy)))edges.Add(new[]{new CampaignMapPoint(p.lon+hx,p.lat-hy),new CampaignMapPoint(p.lon+hx,p.lat+hy)});
                if(!reached.Contains(Key(gx,gy-1)))edges.Add(new[]{new CampaignMapPoint(p.lon-hx,p.lat-hy),new CampaignMapPoint(p.lon+hx,p.lat-hy)});
                if(!reached.Contains(Key(gx,gy+1)))edges.Add(new[]{new CampaignMapPoint(p.lon-hx,p.lat+hy),new CampaignMapPoint(p.lon+hx,p.lat+hy)});
            }
            return edges;
        }
    }
}
