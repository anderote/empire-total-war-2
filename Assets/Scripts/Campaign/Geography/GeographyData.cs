using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    [Serializable] public class GeoPoint { public float lon, lat; }
    [Serializable] public class GeoPath { public string name; public GeoPoint[] points; }
    [Serializable] public class Settlement : GeoPoint
    {
        public string id, name, kind, source;
        public bool port;
        public int displayRank;
        public bool catalogOnly;
        public string country;
        // Optional campaign-art overrides; these do not change settlement gameplay.
        public string artSize,fortification,resourceArt;
    }
    [Serializable] public class TravelRoute : GeoPath
    {
        public string fromId, toId, mode, surface, terrainAtlas;
        public bool provisional;
        public float distanceKm, terrainCost;
    }
    [Serializable] public class MountainLabel : GeoPoint { public string name; }
    [Serializable] public class TheaterData
    {
        public int schemaVersion, width, height;
        public string name;
        public float west, south, east, north, referenceLatitude;
        public Settlement[] settlements,waypoints;
        public GeoPath[] coastlines, rivers, lakes;
        public TravelRoute[] routes;
        public MountainLabel[] mountains;
    }

    // Keep campaign data outside the large native resource archive in standalone builds.
    public sealed class CampaignResourceData
    {
        public readonly byte[] bytes;
        public string text => System.Text.Encoding.UTF8.GetString(bytes);
        CampaignResourceData(byte[] value) { bytes=value; }
        public static CampaignResourceData Load(string resource)
        {
            string root=System.IO.Path.Combine(Application.streamingAssetsPath,"ResourceData",resource);
            foreach(string extension in new[]{".json",".bytes"})
                if(System.IO.File.Exists(root+extension))return new CampaignResourceData(System.IO.File.ReadAllBytes(root+extension));
            var asset=Resources.Load<TextAsset>(resource);
            return asset==null?null:new CampaignResourceData(asset.bytes);
        }
    }

    /// <summary>Offline measured geography. No faction, economy, or date assumptions.</summary>
    public sealed class GeographyAtlas
    {
        public readonly TheaterData Data;
        public readonly short[] Elevation;
        public readonly byte[] DryLand;
        public const float EarthRadiusKm=6371f;
        public readonly GeographyAtlas Detail;
        public GeographyAtlas RemoteDetail;
        public readonly bool Spherical;
        public readonly Dictionary<string, Settlement> Towns = new Dictionary<string, Settlement>();
        public float WidthKm => (Data.east - Data.west) * 111.32f * Mathf.Cos(Data.referenceLatitude * Mathf.Deg2Rad);
        public float DepthKm => (Data.north - Data.south) * 111.32f;
        public GeographyAtlas(string resource="WorldGeography/world",bool spherical=true)
        {
            Spherical=spherical;
            var json = CampaignResourceData.Load(resource);
            if (json == null) throw new InvalidOperationException("Run Tools/Campaign/Geography/build_europe.py first.");
            Data = JsonUtility.FromJson<TheaterData>(json.text);
            string folder=resource.Substring(0,resource.LastIndexOf('/'));
            var raw = CampaignResourceData.Load(folder+"/elevation").bytes;
            DryLand = CampaignResourceData.Load(folder+"/landmask").bytes;
            int count = Data.width * Data.height;
            if (raw.Length != count * 2 || DryLand.Length != count)
                throw new InvalidOperationException("Geography raster sizes do not match metadata.");
            Elevation = new short[count];
            for (int i = 0; i < count; i++) Elevation[i] = (short)(raw[i*2] | raw[i*2+1] << 8);
            foreach (var town in Data.settlements) Towns.Add(town.id, town);
            if(Data.waypoints!=null)foreach(var town in Data.waypoints)if(!Towns.ContainsKey(town.id))Towns.Add(town.id,town);
            if(spherical) Detail=new GeographyAtlas("EuropeGeography/europe",false);
        }
        public Vector2 Project(float lon, float lat) => new Vector2(
            (lon-Data.west)/(Data.east-Data.west)*WidthKm-WidthKm/2,
            (lat-Data.south)/(Data.north-Data.south)*DepthKm-DepthKm/2);
        public Vector2 Unproject(Vector2 km) => new Vector2(
            (km.x+WidthKm/2)/WidthKm*(Data.east-Data.west)+Data.west,
            (km.y+DepthKm/2)/DepthKm*(Data.north-Data.south)+Data.south);
        public float HeightMeters(float lon, float lat)
        {
            if(Detail!=null && Detail.Contains(lon,lat))return Detail.HeightMeters(lon,lat);
            if(RemoteDetail!=null && RemoteDetail.Contains(lon,lat))return RemoteDetail.HeightMeters(lon,lat);
            float x = Mathf.Clamp01((lon-Data.west)/(Data.east-Data.west))*(Data.width-1);
            float y = Mathf.Clamp01((lat-Data.south)/(Data.north-Data.south))*(Data.height-1);
            int ix = Mathf.Min((int)x,Data.width-2), iy = Mathf.Min((int)y,Data.height-2);
            return Mathf.Lerp(Mathf.Lerp(Elevation[iy*Data.width+ix],Elevation[iy*Data.width+ix+1],x-ix),
                Mathf.Lerp(Elevation[(iy+1)*Data.width+ix],Elevation[(iy+1)*Data.width+ix+1],x-ix),y-iy);
        }
        public bool IsDry(float lon, float lat)
        {
            if(Detail!=null && Detail.Contains(lon,lat))return Detail.IsDry(lon,lat);
            if(RemoteDetail!=null && RemoteDetail.Contains(lon,lat))return RemoteDetail.IsDry(lon,lat);
            if(lon<Data.west || lon>Data.east || lat<Data.south || lat>Data.north) return false;
            int x = Mathf.RoundToInt((lon-Data.west)/(Data.east-Data.west)*(Data.width-1));
            int y = Mathf.RoundToInt((lat-Data.south)/(Data.north-Data.south)*(Data.height-1));
            return DryLand[y*Data.width+x] != 0;
        }
        // Deliberate vertical exaggeration for a strategic miniature, retaining metres in data.
        public Vector3 World(float lon, float lat, float lift=0)
        {
            if(Spherical)return Normal(lon,lat)*(EarthRadiusKm+Mathf.Max(0,HeightMeters(lon,lat))*.035f+lift);
            Vector2 p = Project(lon,lat);
            return new Vector3(p.x, Mathf.Max(0,HeightMeters(lon,lat))*.035f+lift,p.y);
        }
        public bool Contains(float lon,float lat)=>lon>=Data.west&&lon<=Data.east&&lat>=Data.south&&lat<=Data.north;
        public static Vector3 Normal(float lon,float lat)
        {
            float a=lon*Mathf.Deg2Rad,b=lat*Mathf.Deg2Rad;
            return new Vector3(-Mathf.Cos(b)*Mathf.Sin(a),Mathf.Sin(b),Mathf.Cos(b)*Mathf.Cos(a));
        }
        public static Vector3 North(float lon,float lat)
        {
            float a=lon*Mathf.Deg2Rad,b=lat*Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(b)*Mathf.Sin(a),Mathf.Cos(b),-Mathf.Sin(b)*Mathf.Cos(a));
        }
        public List<TravelRoute> Connected(string id)
        {
            var result = new List<TravelRoute>();
            foreach(var r in Data.routes) if(r.fromId==id || r.toId==id) result.Add(r);
            return result;
        }
    }
}
