using System;
using EmpireBattle;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public static class RegionalBattleInspection
    {
        // Native visual verification uses real campaign regions, rather than a second terrain generator.
        public static BattleConfig Configure(BattleConfig original,string[] args)
        {
            int flag=Array.IndexOf(args,"--regional-region");if(flag<0)return original;
            if(flag+1>=args.Length)throw new ArgumentException("Regional inspection requires a campaign region ID.");
            string id=args[flag+1];var catalog=RegionCatalog.Load();var region=Array.Find(catalog.regions,r=>r.id==id);
            if(region==null)throw new ArgumentException("Unknown regional inspection region: "+id);
            var campaign=new CountryCampaign(catalog,EconomyProfiles.Load(),
                JsonUtility.FromJson<TheaterData>(EmpireCampaign.Geography.CampaignResourceData.Load("WorldGeography/world").text),CampaignBalance.Load(),"france");
            var encounter=new CampaignEncounter{id="regional-inspection",regionId=id,attacker="france",defender=region.owner};
            encounter.garrison.Add(new CampaignRegiment{id="inspection-militia",homeRegionId=id,role="Militia",weapon="musket",doctrine=1,soldiers=250});
            var map=CampaignBattleSetup.Profile(campaign,encounter,new GeographyAtlas());map.DefenderSide=ArmySide.British;map.SettlementRadius=260;
            int town=Array.IndexOf(args,"--regional-town");map.Settlement=town>=0;map.FortLevel=town>=0&&town+1<args.Length?Mathf.Clamp(int.Parse(args[town+1]),0,4):0;
            Debug.Log("REGIONAL_BATTLE_INSPECTION region="+id+" biome="+map.Biome+" relief="+map.Relief+" forest="+map.Forest+" snow="+map.Snow+" aridity="+map.Aridity);
            return new BattleConfig{BattalionsPerSide=4,SoldiersPerBattalion=250,FrenchArtillery=0,BritishArtillery=0,
                RequireDeployment=true,RegionalMap=true,Map=map,Seed=CampaignBattleSetup.StableSeed(id,encounter.id),
                InitialSeparation=900,EnableAI=false,EnableLandscape=original.EnableLandscape,UseDetailedInfantry=original.UseDetailedInfantry};
        }
    }
}
