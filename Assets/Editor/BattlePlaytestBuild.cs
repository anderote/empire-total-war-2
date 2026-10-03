// Build the single campaign and battle application; publication is explicitly separate.
public static class BattlePlaytestBuild
{
    public static void BuildAll(){InfantryAssetBuild.UpdateMuzzleAnchors();UnifiedGameBuild.BuildMac();}
}
