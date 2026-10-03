using UnityEngine;

public static class CampaignBattleIntegrationBuild
{
    // Run in an isolated source/assets checkpoint so another task cannot change a player mid-build.
    public static void VerifyAndBuild()
    {
        CampaignBattleIntegrationVerification.Verify();
        CampaignBattleIntegrationVerification.VerifySaveCodec();
        CampaignCountryBuild.Verify();
        ArtilleryRecoveryVerification.Verify();
        ArtilleryAmmunitionVerification.Verify();
        BattlePlaytestBuild.BuildAll();
        CampaignBuild.BuildMac();
        Debug.Log("CAMPAIGN_BATTLE_MATCHED_BUILDS_PASSED");
    }
}
