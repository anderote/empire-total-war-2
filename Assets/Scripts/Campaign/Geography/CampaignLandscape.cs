using UnityEngine;

namespace EmpireCampaign.Geography
{
    /// <summary>Art heuristics, not surveyed historical vegetation or mineral deposits.</summary>
    public static class CampaignLandscape
    {
        public static float Aridity(float lon,float lat)
        {
            float sahara=Band(lon,-18,58,8)*Band(lat,14,33,5);
            float centralAsia=Band(lon,48,110,10)*Band(lat,34,46,5);
            float australia=Band(lon,115,146,6)*Band(lat,-33,-18,5);
            float southwest=Band(lon,-119,-101,5)*Band(lat,24,38,4);
            float namib=Band(lon,10,23,4)*Band(lat,-30,-17,4);
            return Mathf.Max(sahara,Mathf.Max(centralAsia,Mathf.Max(australia,Mathf.Max(southwest,namib))));
        }
        static float Band(float x,float a,float b,float edge)=>Mathf.SmoothStep(0,1,Mathf.Clamp01((x-a)/edge))*Mathf.SmoothStep(0,1,Mathf.Clamp01((b-x)/edge));
        public static float Snow(float lat,float height)
        {
            float snowline=Mathf.Lerp(4300,650,Mathf.InverseLerp(25,72,Mathf.Abs(lat)));
            float alpine=Mathf.SmoothStep(0,1,Mathf.InverseLerp(snowline-350,snowline+450,height));
            return Mathf.Max(alpine,Mathf.SmoothStep(0,1,Mathf.InverseLerp(69,80,Mathf.Abs(lat))));
        }
        public static float Forest(float lon,float lat,float height)
        {
            float clusters=Mathf.PerlinNoise((lon+180)*.72f,(lat+90)*.72f);
            float treeline=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(2400,3400,height));
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,.66f,clusters))*(1-Aridity(lon,lat))*(1-Snow(lat,height))*treeline;
        }
        public static Color32 Biome(GeographyAtlas atlas,float lon,float lat)
        {
            if(!atlas.IsDry(lon,lat))return new Color32(0,0,0,0);
            float h=atlas.HeightMeters(lon,lat);
            float dx=.035f/Mathf.Max(.25f,Mathf.Cos(lat*Mathf.Deg2Rad)),dy=.035f;
            bool coast=!atlas.IsDry(Mathf.DeltaAngle(0,lon-dx),lat)||!atlas.IsDry(Mathf.DeltaAngle(0,lon+dx),lat)||!atlas.IsDry(lon,lat-dy)||!atlas.IsDry(lon,lat+dy);
            float beach=coast?1-Mathf.InverseLerp(35,180,h):0;
            return new Color(Forest(lon,lat,h),Snow(lat,h),beach,Aridity(lon,lat));
        }
    }
}
