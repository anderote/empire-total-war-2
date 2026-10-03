using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    [Serializable] public class RegionPolygon { public GeoPath[] rings; }
    [Serializable] public class RegionSite : GeoPoint { public string id,name,kind; }
    [Serializable] public class CampaignRegion
    {
        public string id,name,provinceId,owner;
        public Settlement capital;
        public int buildSlots;
        public RegionSite[] sites;
        public RegionPolygon[] polygons;
        public bool Contains(float lon,float lat)
        {
            foreach(var polygon in polygons)
            {
                if(polygon.rings.Length==0||!Inside(polygon.rings[0].points,lon,lat))continue;
                bool hole=false;
                for(int i=1;i<polygon.rings.Length;i++)if(Inside(polygon.rings[i].points,lon,lat)){hole=true;break;}
                if(!hole)return true;
            }
            return false;
        }
        static bool Inside(GeoPoint[] points,float x,float y)
        {
            bool yes=false;
            for(int i=0,j=points.Length-1;i<points.Length;j=i++)
            {
                var a=points[i];var b=points[j];
                if((a.lat>y)!=(b.lat>y) && x<(b.lon-a.lon)*(y-a.lat)/(b.lat-a.lat)+a.lon)yes=!yes;
            }
            return yes;
        }
    }
    [Serializable] public class CampaignProvince { public string id,name; public string[] regionIds; }
    [Serializable] public class CampaignFaction
    {
        public string id,name,color;
        public Color MapColor { get { Color value;return ColorUtility.TryParseHtmlString(color,out value)?value:Color.gray; } }
    }
    [Serializable] public class RegionCatalog
    {
        public CampaignFaction[] factions;
        public GeoPath[] provinceBorders,regionBorders;
        public string scenarioName,politicsNotes;
        public int politicalRasterWidth,politicalRasterHeight;
        public int schemaVersion;public CampaignProvince[] provinces;public CampaignRegion[] regions;
        public static RegionCatalog Load()=>JsonUtility.FromJson<RegionCatalog>(EmpireCampaign.Geography.CampaignResourceData.Load("WorldGeography/campaign").text);
        public CampaignRegion ForCity(string id){foreach(var r in regions)if(r.capital.id==id)return r;return null;}
        public CampaignProvince Province(string id){foreach(var p in provinces)if(p.id==id)return p;return null;}
        public CampaignRegion At(float lon,float lat){foreach(var r in regions)if(r.Contains(lon,lat))return r;return null;}
        public CampaignFaction Faction(string id){foreach(var f in factions)if(f.id==id)return f;return null;}
    }
    [Serializable] public class RegionBuilding { public string kind="Empty";public int turnsRemaining;public int level=1;public float workRemaining,totalWork;public int paidCost;public bool upgrading; public int raidDamage,raidRepairLiability; }
    [Serializable] public class RegionState
    {
        public string regionId,owner;public RegionBuilding[] buildings;
        public RegionEconomyState economy;
    }
    [Serializable] public class RegionSave
    {
        public int schemaVersion=2,turn=1,treasury=2400; // treasury retained only for schema-1 migration.
        public RegionState[] regions;
        public FactionEconomyState[] factions;
    }
    /// <summary>Shared regional construction slots and city-based ownership. Satellite sites never own territory.</summary>
    public sealed class RegionCampaign
    {
        public readonly RegionCatalog Catalog;
        public readonly CampaignEconomy Economy;
        public RegionSave Save;
        public RegionCampaign(RegionCatalog catalog,EconomyProfiles profiles=null)
        {
            Catalog=catalog;Economy=new CampaignEconomy(catalog,profiles??EconomyProfiles.Load());
            Save=new RegionSave {regions=new RegionState[catalog.regions.Length],factions=InitialFactions(2400)};
            for(int i=0;i<Save.regions.Length;i++)
            {
                var r=catalog.regions[i];var s=new RegionState {regionId=r.id,owner=r.owner,buildings=new RegionBuilding[r.buildSlots],economy=Economy.Initial(r.id)};
                for(int j=0;j<s.buildings.Length;j++)s.buildings[j]=new RegionBuilding();Save.regions[i]=s;
            }
        }
        public RegionState State(string id){foreach(var s in Save.regions)if(s.regionId==id)return s;return null;}
        FactionEconomyState[] InitialFactions(int budget)
        {
            var states=new FactionEconomyState[Catalog.factions.Length];
            for(int i=0;i<states.Length;i++)states[i]=new FactionEconomyState {factionId=Catalog.factions[i].id,treasury=budget};
            return states;
        }
        public FactionEconomyState FactionState(string id){foreach(var f in Save.factions)if(f.factionId==id)return f;return null;}
        public static int Cost(string kind)=>CampaignBuildingTiers.Get(kind,1)?.Cost??0;
        public bool Build(string regionId,string kind,out string reason)
            =>Build(regionId,kind,Array.FindIndex(State(regionId)?.buildings??Array.Empty<RegionBuilding>(),b=>b.kind=="Empty"),out reason);
        public bool Build(string regionId,string kind,int slot,out string reason)
        {
            var state=State(regionId);int cost=Cost(kind);
            if(state==null||cost<=0){reason="Unknown region or building.";return false;}
            if(slot<0||slot>=state.buildings.Length||state.buildings[slot].kind!="Empty"){reason="Choose an empty building slot.";return false;}
            var faction=FactionState(state.owner);
            if(faction.treasury<cost){reason="Insufficient owning faction treasury.";return false;}
            faction.treasury-=cost;state.buildings[slot]=new RegionBuilding {kind=kind,turnsRemaining=kind=="University"?3:2,workRemaining=kind=="University"?3:2,totalWork=kind=="University"?3:2,paidCost=cost};
            reason=kind+" construction started in this region.";return true;
        }
        public void EndTurn()
        {
            Economy.Advance(Save);Save.turn++;
            foreach(var state in Save.regions)foreach(var b in state.buildings)
                if(b.turnsRemaining>0){b.turnsRemaining--;b.workRemaining=b.turnsRemaining;if(b.turnsRemaining==0){b.upgrading=false;b.paidCost=0;}}
        }
        public bool CaptureCity(string settlementId,string faction)
        {
            var region=Catalog.ForCity(settlementId);
            if(region==null||Catalog.Faction(faction)==null)return false;
            State(region.id).owner=faction;return true;
        }
        public bool Restore(string json)
        {
            RegionSave candidate;
            try{candidate=JsonUtility.FromJson<RegionSave>(json);}catch{return false;}
            return RestoreState(candidate);
        }
        public bool RestoreState(RegionSave candidate)
        {
            if(candidate==null||(candidate.schemaVersion!=1&&candidate.schemaVersion!=2)||candidate.regions==null||candidate.regions.Length!=Catalog.regions.Length)return false;
            var seen=new HashSet<string>();
            foreach(var s in candidate.regions)
            {
                if(s==null)return false;
                var expected=State(s.regionId);
                if(expected==null||Catalog.Faction(s.owner)==null||!seen.Add(s.regionId)||s.buildings==null||s.buildings.Length!=expected.buildings.Length)return false;
                foreach(var b in s.buildings)if(!CampaignBuildingTiers.ValidBuilding(b))return false;
                // Older native saves can contain a tiny negative residue after fractional construction spending.
                if(s.economy!=null&&s.economy.materials<0&&s.economy.materials>=-.0001f)s.economy.materials=0;
                if(candidate.schemaVersion==2&&!CampaignEconomy.ValidState(s.economy))return false;
            }
            if(candidate.turn<1||candidate.turn>=1000000)return false;
            if(candidate.schemaVersion==1)
            {
                if(candidate.treasury<0||candidate.treasury>1000000000)return false;
                // Old budget was available everywhere in the atlas sandbox. Preserve that
                // starting purchasing power for each faction, not a historical redistribution.
                candidate.factions=InitialFactions(candidate.treasury);
                foreach(var s in candidate.regions)s.economy=Economy.Initial(s.regionId);
                candidate.schemaVersion=2;
            }
            if(candidate.factions==null||candidate.factions.Length!=Catalog.factions.Length)return false;
            seen.Clear();
            foreach(var f in candidate.factions)
                if(f==null||Catalog.Faction(f.factionId)==null||!seen.Add(f.factionId)||f.treasury < -1000000000||f.treasury>1000000000||f.lastTaxes<0||f.lastExpenses<0)return false;
            Save=candidate;return true;
        }
    }
}
