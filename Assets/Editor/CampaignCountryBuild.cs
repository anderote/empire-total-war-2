using System;
using EmpireBattle;
using EmpireCampaign.Geography;
using UnityEditor;
using UnityEngine;

public static class CampaignCountryBuild
{
    [MenuItem("Empire Campaign/Verify country campaign")]
    public static void Verify()
    {
        var campaign=new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),
            JsonUtility.FromJson<TheaterData>(Resources.Load<TextAsset>("WorldGeography/world").text),CampaignBalance.Load(),"france");
        string reason;string json=JsonUtility.ToJson(campaign.Save,true);
        Require(campaign.Adopt(JsonUtility.FromJson<CountryCampaignSave>(json),out reason),"native country save codec round trip: "+reason);
        var battle=new BattleWorld(new BattleConfig {FrenchBattalions=2,BritishBattalions=1,SoldiersPerBattalion=500,
            InitialStrengths=new[]{500,267,431},FormationNames=new[]{"Veterans","Depleted","Opposing"},
            FormationExperience=new[]{.1f,.2f,0},FormationSupply=new[]{1f,.5f,1f},FrenchArtillery=0,BritishArtillery=0});
        Require(battle.Config.TotalSoldiers==1198&&battle.CountAlive(ArmySide.French)==767&&battle.CountAlive(ArmySide.British)==431,"exact depleted deployment without phantom soldiers");
        Require(battle.Battalions[2].SoldierStart==767&&battle.Soldiers.BattalionIds[1197]==2,"variable deployment offsets");
        Require(battle.Battalions[1].Name=="Depleted"&&battle.Battalions[1].Experience>0&&battle.Soldiers.RoundsRemaining[500]==15,"names experience and supply mapped");
        battle.ApplyCasualties(1,20);battle.StepMovement(.05f);
        Require(battle.CountAlive(ArmySide.French)==747&&battle.Battalions[1].AliveCount==247,"depleted formations retain casualty/movement accounting");
        battle.MovementJobs?.Dispose();
        bool rejected=false;
        try{new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,InitialStrengths=new[]{500}});}catch(ArgumentException){rejected=true;}
        Require(rejected,"malformed deployment rejected");
        var republic=new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),
            JsonUtility.FromJson<TheaterData>(Resources.Load<TextAsset>("WorldGeography/world").text),CampaignBalance.Load().Revolutionary(),"france",1792,CountryCampaign.RepublicScenario);
        Require(republic.DateLabel=="1792 / Autumn"&&republic.Adopt(JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(republic.Save)),out reason),"native revolutionary save codec: "+reason);
        Require(republic.EndTurn(out reason),"republic first round");
        while(republic.Save.pending!=null)Require(republic.Resolve(republic.Save.pending.id,out reason),"republic encounter");
        Require(republic.DateLabel=="1793 / Spring"&&republic.Save.story.decision=="coalition"&&republic.Adopt(JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(republic.Save)),out reason),"native pending national decision and coalition save");
        Require(republic.DecideRepublic("coalition",1,out reason),"native national decision");
        CampaignResearchVerification.Verify();
        Debug.Log("COUNTRY_CAMPAIGN_NATIVE_VERIFIED");
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
