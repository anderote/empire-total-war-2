using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        void DrawEconomyBalance(RegionState state)
        {
            var profile=regionCampaign.Economy.Profile(state.regionId);
            var balance=regionCampaign.Economy.Forecast(state);
            var faction=regionCampaign.FactionState(state.owner);
            GUILayout.Space(8);GUILayout.Label("REGIONAL ECONOMY",small);
            GUILayout.Label(profile.template.Replace('_',' ')+" / prototype estimates",small);
            GUILayout.Label("Population "+state.economy.population.ToString("N0")+" / whole region",text);
            GUILayout.Label("Workers "+balance.workers.ToString("N0")+" / jobs "+balance.requiredWorkers.ToString("N0")+
                " / staffed "+(balance.staffing*100).ToString("F0")+"%",small);
            GUILayout.Label("Food stock "+state.economy.food.ToString("F1")+" / capacity "+balance.foodStorage.ToString("F1"),text);
            GUILayout.Label("Next turn: produce "+balance.foodProduced.ToString("F1")+" / need "+balance.foodNeeded.ToString("F1")+
                " / stock after "+balance.foodAfter.ToString("F1"),small);
            if(balance.foodShortage>0)GUILayout.Label("Food shortage forecast: "+balance.foodShortage.ToString("F1"),text);
            if(balance.foodWaste>0)GUILayout.Label("Storage overflow: "+balance.foodWaste.ToString("F1"),small);
            GUILayout.Label("Food security "+state.economy.wellbeing+" / 100",small);
            GUILayout.Label("Taxable wealth "+balance.taxableWealth+" / taxes "+balance.taxes+" / upkeep "+balance.expenses,text);
            GUILayout.Label("Next regional contribution "+balance.NetIncome.ToString("+0;-0;0"),small);
            GUILayout.Label("Faction last turn: taxes "+faction.lastTaxes+" / upkeep "+faction.lastExpenses,small);
            GUILayout.Label("Local stocks only; trade deliveries pending. One food unit feeds 1,000 residents per turn.",small);
            if(faction.treasury<0)GUILayout.Label("Faction in arrears; new construction requires funds.",text);
        }
    }
}
