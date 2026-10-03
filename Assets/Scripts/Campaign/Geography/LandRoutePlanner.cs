using System.Collections.Generic;

namespace EmpireCampaign.Geography
{
    public sealed class LandJourney
    {
        public readonly List<TravelRoute> Routes=new List<TravelRoute>();
        public float DistanceKm, TerrainCost;
    }
    /// <summary>Deterministic least-terrain-cost routing. Maritime transport needs a separate model.</summary>
    public static class LandRoutePlanner
    {
        public static LandJourney Find(GeographyAtlas atlas,string from,string to)
        {
            if(!atlas.Towns.ContainsKey(from)||!atlas.Towns.ContainsKey(to))return null;
            var costs=new Dictionary<string,float>{{from,0}};
            var previous=new Dictionary<string,TravelRoute>();var done=new HashSet<string>();
            while(true)
            {
                string next=null;float best=float.MaxValue;
                foreach(var item in costs)if(!done.Contains(item.Key)&&item.Value<best){next=item.Key;best=item.Value;}
                if(next==null||next==to)break;
                done.Add(next);
                foreach(var edge in atlas.Connected(next))
                {
                    if(edge.mode!="land")continue;
                    string destination=edge.fromId==next?edge.toId:edge.fromId;
                    float cost=best+edge.terrainCost;
                    if(!costs.ContainsKey(destination)||cost<costs[destination])
                    {costs[destination]=cost;previous[destination]=edge;}
                }
            }
            if(!costs.ContainsKey(to))return null;
            var result=new LandJourney {TerrainCost=costs[to]};string cur=to;
            while(cur!=from)
            {
                var edge=previous[cur];result.Routes.Add(edge);result.DistanceKm+=edge.distanceKm;
                cur=edge.fromId==cur?edge.toId:edge.fromId;
            }
            result.Routes.Reverse();return result;
        }
    }
}
