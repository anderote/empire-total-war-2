using System;
using System.IO;
using System.Text.Json;
using EmpireCampaign.Geography;

// Run against the actual compiled campaign assembly; no replacement economy implementation.
public static class EconomyChecks
{
    static int checks;
    static void Require(bool valid,string message)
    {
        if(!valid)throw new Exception(message);checks++;
    }
    public static void Main()
    {
        var options=new JsonSerializerOptions {IncludeFields=true};
        var catalog=JsonSerializer.Deserialize<RegionCatalog>(File.ReadAllText("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/campaign.json"),options);
        var profiles=JsonSerializer.Deserialize<EconomyProfiles>(File.ReadAllText("Assets/Resources/CampaignEconomyProfiles.json"),options);
        var campaign=new RegionCampaign(catalog,profiles);
        Require(campaign.Save.regions.Length==238,"Full region coverage");
        Require(campaign.Save.factions.Length==catalog.factions.Length,"Full faction coverage");
        var london=catalog.ForCity("london");var state=campaign.State(london.id);
        var britain=campaign.FactionState(state.owner);var france=campaign.FactionState("france");
        int oldBritain=britain.treasury,oldFrance=france.treasury;
        string reason;
        Require(campaign.Build(london.id,"Workshop",out reason),"Workshop order accepted");
        Require(britain.treasury==oldBritain-400&&france.treasury==oldFrance,"Owner alone pays construction");
        var before=campaign.Economy.Forecast(state);
        campaign.EndTurn();
        Require(campaign.Economy.Forecast(state).taxableWealth==before.taxableWealth,"Incomplete workshop produces nothing");
        campaign.EndTurn();
        Require(britain.lastExpenses==0,"Completing workshop has no same-turn output/upkeep");
        Require(campaign.Economy.Forecast(state).taxableWealth>before.taxableWealth,"Completed workshop increases next-turn production");
        campaign.EndTurn();Require(britain.lastExpenses==12,"Completed workshop upkeep charged");
        oldBritain=britain.treasury;oldFrance=france.treasury;
        Require(campaign.CaptureCity("london","france"),"Ownership transfer accepted");
        Require(britain.treasury==oldBritain&&france.treasury==oldFrance,"Capture does not transfer faction treasuries");
        campaign.EndTurn();
        Require(britain.lastExpenses==0&&france.lastExpenses==12,"New owner receives regional accounts");
        foreach(var region in campaign.Save.regions)
        {
            var b=campaign.Economy.Forecast(region);
            float consumed=b.foodNeeded-b.foodShortage;
            Require(Math.Abs(region.economy.food+b.foodProduced-consumed-b.foodAfter-b.foodWaste)<.001f,"Food conservation: "+region.regionId);
            Require(b.staffing<=1&&b.staffing>=0,"Bounded workforce: "+region.regionId);
        }
        var p=campaign.Economy.Profile(london.id);p.foodCapacity=0;state.economy.food=0;
        int security=state.economy.wellbeing;
        Require(campaign.Economy.Forecast(state).foodShortage>0,"No phantom imports into starving region");
        campaign.EndTurn();Require(state.economy.food==0&&state.economy.wellbeing<security,"Shortage is bounded and reduces food security");
        state.economy.food=state.economy.population/1000f*4;p.foodCapacity=10000;
        var overflow=campaign.Economy.Forecast(state);
        Require(overflow.foodWaste>0&&overflow.foodAfter<=overflow.foodNeeded*4,"Storage overflow accounted explicitly");
        state.economy.population=1000;
        var labour=campaign.Economy.Forecast(state);
        Require(labour.staffing<1&&labour.taxableWealth<p.taxableWealth+140,"Labour shortage constrains output");
        Require(!CampaignEconomy.ValidState(new RegionEconomyState {population=1000,food=float.NaN}),"NaN stocks rejected");
        Require(!CampaignEconomy.ValidState(new RegionEconomyState {population=1000,food=-1}),"Negative stocks rejected");
        Require(!CampaignEconomy.ValidState(new RegionEconomyState {population=1000,food=11}),"Over-capacity save stocks rejected");
        for(int i=0;i<20;i++)campaign.EndTurn();
        foreach(var region in campaign.Save.regions)Require(CampaignEconomy.ValidState(region.economy),"Long-run state bounds: "+region.regionId);
        Console.WriteLine("PASS: "+checks+" checks; 238 regions, faction isolation, food conservation, shortage, storage, workforce and building timing.");
    }
}
