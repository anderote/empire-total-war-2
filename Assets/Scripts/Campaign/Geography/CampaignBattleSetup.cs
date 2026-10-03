using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    // Kept separate from the scene transition so the entire deployment can be verified.
    public static class CampaignBattleSetup
    {
        public static int StableSeed(string region, string encounter)
        {
            uint hash=2166136261;
            foreach(char c in region+"/"+encounter) hash=(hash^c)*16777619;
            return (int)(hash&0x7fffffff);
        }
        public static BattleMapProfile Profile(CountryCampaign campaign,CampaignEncounter e,GeographyAtlas atlas)
        {
            var region=Array.Find(campaign.World.Catalog.regions,r=>r.id==e.regionId);
            if(region==null)throw new ArgumentException("Encounter region missing.");
            var location=campaign.EncounterPosition(e);
            float lon=location.lon,lat=location.lat,height=Mathf.Max(0,atlas.HeightMeters(lon,lat));
            float relief=0;
            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                relief=Mathf.Max(relief,Mathf.Abs(atlas.HeightMeters(lon+x*.12f,lat+y*.10f)-height));
            float arid=CampaignLandscape.Aridity(lon,lat),forest=CampaignLandscape.Forest(lon,lat,height);
            float snow=CampaignLandscape.Snow(lat,height);
            // Campaign turns are half-years: retain geographic alpine/polar snow and add a cold-season cover.
            if((campaign.Save.world.turn-1+campaign.Balance.startSeason)%2==1 && Mathf.Abs(lat)>48) snow=Mathf.Max(snow,Mathf.InverseLerp(48,65,Mathf.Abs(lat))*.85f);
            bool coast=region.capital.port && height<180;
            var p=new BattleMapProfile {RegionId=region.id,RegionName=region.capital.name,
                Relief=Mathf.Clamp(.45f+relief/220f,.45f,3.5f),Forest=forest,Snow=snow,Aridity=arid,Coast=coast,
                Settlement=!e.fieldOnly&&(e.garrison.Count>0||campaign.Buildings(e.regionId,"Fortress")>0),
                FortLevel=e.fieldOnly?0:Mathf.Min(4,campaign.Buildings(e.regionId,"Fortress")),
                DefenderSide=e.defender==campaign.Save.playerFaction?ArmySide.French:ArmySide.British,
                SettlementRadius=Mathf.Clamp(160+Mathf.Sqrt(campaign.BattleUnits(e,false).Count)*55,180,650)};
            p.Biome=snow>.35f?BattleBiome.Snow:arid>.4f?BattleBiome.Arid:coast?BattleBiome.Coastal:
                p.Relief>1.8f?BattleBiome.Highlands:forest>.5f?BattleBiome.Woodland:BattleBiome.Temperate;
            return p;
        }
        static float Arrival(CountryCampaign campaign,CampaignEncounter e,CampaignRegiment unit,bool playerAttacks)
        {
            bool attacker=campaign.EncounterArmies(e,true).Any(a=>a.units.Contains(unit));
            var forces=campaign.EncounterArmies(e,attacker).ToList();int army=forces.FindIndex(a=>a.units.Contains(unit));
            if(army<0)return 0; // Settlement militia deploy with the defending main army.
            var force=forces[army];int index=force.units.IndexOf(unit);
            return army==0&&index<20?0:20+army*30+(index/20)*20;
        }
        public static BattleConfig Create(CountryCampaign campaign,CampaignEncounter e,GeographyAtlas atlas,out List<CampaignRegiment> deployment)
        {
            if(e==null||campaign.Save.pending!=e||!campaign.CanFightManually())throw new ArgumentException("No supported pending encounter.");
            bool attacks=e.attacker==campaign.Save.playerFaction;
            var player=campaign.BattleUnits(e,attacks);var enemy=campaign.BattleUnits(e,!attacks);
            deployment=player.Concat(enemy).ToList();
            string foe=attacks?e.defender:e.attacker;
            return new BattleConfig {CampaignDeployment=true,RequireDeployment=true,RegionalMap=true,UseDetailedInfantry=true,
                FrenchBattalions=player.Count,BritishBattalions=enemy.Count,SoldiersPerBattalion=campaign.Balance.unitSize,
                FrenchArtillery=0,BritishArtillery=0,Seed=StableSeed(e.regionId,e.id),InitialSeparation=900,
                MaxActiveCampaignTroops=20000,FormationArrivalSeconds=deployment.Select(u=>Arrival(campaign,e,u,attacks)).ToArray(),
                Map=Profile(campaign,e,atlas),InitialStrengths=deployment.Select(u=>u.soldiers).ToArray(),
                PlayerFaction=campaign.Save.playerFaction,EnemyFaction=foe,
                FormationRoles=deployment.Select(u=>UnitRoster.CampaignRole(u.role,u.weapon)).ToArray(),
                FormationNames=deployment.Select(u=>(string.IsNullOrEmpty(u.name)?u.id:u.name)+" / "+u.role).ToArray(),FormationWeapons=deployment.Select(u=>u.weapon).ToArray(),
                FormationDoctrine=deployment.Select(u=>u.doctrine).ToArray(),FormationQuality=deployment.Select(u=>Mathf.Clamp(u.equipment*.04f+campaign.FieldReadiness(e,campaign.Save.armies.FirstOrDefault(a=>a.units.Contains(u)))-1,0,.2f)).ToArray(),
                FormationProgression=deployment.Select(u=>u.progression.Copy()).ToArray(),FormationLoadouts=deployment.Select(u=>u.loadout.Copy()).ToArray(),
                FormationSupply=deployment.Select(u=>campaign.Save.armies.FirstOrDefault(a=>a.units.Contains(u))?.supply??1f).ToArray(),
                PlayerCountryName=campaign.FactionName(campaign.Save.playerFaction),
                EnemyCountryName=campaign.FactionName(foe),
                PlayerCountryColor=campaign.World.Catalog.Faction(campaign.Save.playerFaction).MapColor,
                EnemyCountryColor=campaign.World.Catalog.Faction(foe).MapColor};
        }
    }
}
