using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    [Serializable] public sealed class RegionEconomyProfile
    {
        public string regionId,template,confidence;
        public int population,foodCapacity,taxableWealth;
    }
    [Serializable] public sealed class EconomyProfiles
    {
        public int schemaVersion;
        public string notes;
        public RegionEconomyProfile[] regions;
        public static EconomyProfiles Load()
        {
            var asset=EmpireCampaign.Geography.CampaignResourceData.Load("CampaignEconomyProfiles");
            if(asset==null)throw new InvalidOperationException("Campaign economy profiles missing.");
            return JsonUtility.FromJson<EconomyProfiles>(asset.text);
        }
    }
    [Serializable] public sealed class RegionEconomyState
    {
        public int population,wellbeing=80;
        public float food;
        public int enlisted,unrest,occupationTurns,foodBonus,wealthBonus,taxRate=15;
        public float materials=80;
    }
    [Serializable] public sealed class FactionEconomyState
    {
        public string factionId;
        public int treasury=2400,lastTaxes,lastExpenses,lastTradeBalance;
    }
    public sealed class RegionEconomyBalance
    {
        public int workers,requiredWorkers,taxableWealth,taxes,expenses;
        public float foodStorage,staffing,foodProduced,foodNeeded,foodShortage,foodAfter,foodWaste;
        public int NetIncome=>taxes-expenses;
    }
    // First local economy layer: no implicit imports, population growth or research projects.
    public sealed class CampaignEconomy
    {
        readonly Dictionary<string,RegionEconomyProfile> profiles=new Dictionary<string,RegionEconomyProfile>();
        public CampaignEconomy(RegionCatalog catalog,EconomyProfiles data)
        {
            if(data==null || data.schemaVersion!=1 || data.regions==null)throw new InvalidOperationException("Invalid economy profile catalogue.");
            foreach(var p in data.regions)
            {
                if(p==null || string.IsNullOrEmpty(p.regionId) || p.population<1000 || p.population>10000000 ||
                    p.foodCapacity<0 || p.foodCapacity>100000 || p.taxableWealth<0 || p.taxableWealth>1000000 || profiles.ContainsKey(p.regionId))
                    throw new InvalidOperationException("Invalid or duplicate economy profile.");
                profiles.Add(p.regionId,p);
            }
            if(profiles.Count!=catalog.regions.Length)throw new InvalidOperationException("Economy roster mismatch.");
            foreach(var r in catalog.regions)if(!profiles.ContainsKey(r.id))throw new InvalidOperationException("Missing regional economy: "+r.id);
        }
        public RegionEconomyProfile Profile(string id)=>profiles[id];
        public RegionEconomyState Initial(string id)
        {
            var p=Profile(id);
            return new RegionEconomyState {population=p.population,food=p.population/1000f*2};
        }
        public RegionEconomyBalance Forecast(RegionState state)
        {
            var p=Profile(state.regionId);var e=state.economy;
            var tiers=CampaignBuildingTiers.Completed(state).ToArray();
            int food=tiers.Sum(t=>t.Food),wealth=tiers.Sum(t=>t.Wealth),jobs=tiers.Sum(t=>t.Workers),upkeep=tiers.Sum(t=>t.Upkeep),storage=tiers.Sum(t=>t.Storage);
            var balance=new RegionEconomyBalance();
            balance.workers=Math.Max(0,(int)(e.population*.4f)-e.enlisted);
            balance.requiredWorkers=(int)(e.population*.30f)+jobs;
            balance.staffing=Math.Min(1f,balance.workers/(float)Math.Max(1,balance.requiredWorkers));
            // Fixed prototype prices and 15% tax on one regional value-added base.
            balance.taxableWealth=(int)((p.taxableWealth+e.wealthBonus+wealth)*balance.staffing*(1-e.unrest*.004f)*(e.occupationTurns>0?.5f:1f));
            balance.taxes=balance.taxableWealth*e.taxRate/100;
            balance.expenses=upkeep;
            balance.foodProduced=(p.foodCapacity+e.foodBonus+food)*balance.staffing;
            balance.foodNeeded=(e.population-e.enlisted)/1000f;
            float available=e.food+balance.foodProduced;
            balance.foodShortage=Math.Max(0,balance.foodNeeded-available);
            float remainder=Math.Max(0,available-balance.foodNeeded);
            balance.foodStorage=e.population/1000f*Math.Min(20,4+storage);
            balance.foodAfter=Math.Min(remainder,balance.foodStorage);
            balance.foodWaste=remainder-balance.foodAfter;
            return balance;
        }
        public void Advance(RegionSave save)
        {
            var factions=new Dictionary<string,FactionEconomyState>();
            foreach(var f in save.factions){f.lastTaxes=0;f.lastExpenses=0;factions.Add(f.factionId,f);}
            foreach(var state in save.regions)
            {
                // Calculate before construction completion: new buildings work next turn.
                var b=Forecast(state);state.economy.food=b.foodAfter;
                state.economy.wellbeing=Math.Max(0,Math.Min(100,state.economy.wellbeing+(b.foodShortage>0?-20:2)));
                var f=factions[state.owner];f.lastTaxes+=b.taxes;f.lastExpenses+=b.expenses;
            }
            foreach(var f in save.factions)f.treasury+=f.lastTaxes-f.lastExpenses;
        }
        public static bool ValidState(RegionEconomyState e)=>e!=null && e.population>=1000 && e.population<=10000000 &&
            e.wellbeing>=0 && e.wellbeing<=100 && !float.IsNaN(e.food) && !float.IsInfinity(e.food) && e.food>=0 && e.food<=e.population/1000f*20 && e.enlisted>=0 && e.enlisted<=e.population && e.unrest>=0 && e.unrest<=100 && e.occupationTurns>=0 && e.occupationTurns<=100 && e.taxRate>=0 && e.taxRate<=30 && e.foodBonus>=0 && e.foodBonus<=1000 && e.wealthBonus>=0 && e.wealthBonus<=10000 && !float.IsNaN(e.materials) && !float.IsInfinity(e.materials) && e.materials>=0 && e.materials<=10000;
    }
}
