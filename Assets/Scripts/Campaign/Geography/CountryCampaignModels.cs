using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    [Serializable] public sealed class CampaignBalance
    {
        public int startYear=1800,startSeason;
        public CampaignBalance Revolutionary() { var b=(CampaignBalance)MemberwiseClone();b.startYear=1792;b.startSeason=1;return b; }
        public int schemaVersion=1,startingTreasury=2400,recruitCost=180,unitSize=500,trainingTurns=2;
        public int movementKm=400,researchFunding=15,researchBase=10,researchCap=24;
        public int administrationPerRegion=8,armyUnitUpkeep=8,reserveBudget=500,maxTurns=36;
        public int constructionMaterials=12,initialMaterials=80,freightPerRegion=60,freightRangeKm=1200;
        public int startingMajorUnits=6,startingMinorUnits=3,startingPrussianUnits=4,constructionGrowthCost=10;
        public int minimumVictoryTurn=18,militaryGainTarget=4,developmentBuildings=5,developmentTechnologies=3;
        public float manpowerFraction=.06f;
        public CampaignTechnology[] technologies;
        public static CampaignBalance Load()=>JsonUtility.FromJson<CampaignBalance>(EmpireCampaign.Geography.CampaignResourceData.Load("CountryCampaignBalance").text);
    }
    [Serializable] public sealed class CampaignTechnology { public string id,name,branch,prerequisite,effect,unavailableReason=""; public string[] prerequisites; public int cost,minYear; }
    [Serializable] public sealed class CountryProgress
    {
        public string factionId,research="",policy="Balanced";
        public string strategicTarget="",coalitionTarget="",strategicIntent="";
        public int strategicPlanTurn;
        public float researchProgress;
        public List<string> technologies=new List<string>();
        public int startingRegions,holdTurns,prosperityTurns,lastNet,lastMilitaryCost,lastAdministration,lastResearchCost,lastPlanningTurn;
    }
    [Serializable] public sealed class CampaignRegiment
    {
        public string id,homeRegionId,name="";
        public bool originProvenanceKnown;
        public string originArmyId="",originArmyName="",originTownId="",originTownName="",designationSource="",designationType="";
        public int designationSequence;
        public int soldiers,equipment,experience;
        public EmpireBattle.UnitProgression progression=new EmpireBattle.UnitProgression();
        public int upgradeTurns,pendingEquipment;
        public EmpireBattle.UnitLoadout loadout=new EmpireBattle.UnitLoadout();
        public string pendingItem="";
        public int kitTurns;
        public bool legacyFacilityOrder;
        public bool pendingProcurement;
        public string procurementRegionId="",procurementOwnerId="";
        public string role="Regular",weapon="musket",course="";
        public int doctrine=1,trainingTurns;
    }
    [Serializable] public sealed class CampaignArmy
    {
        public string id,owner,regionId,name;
        public bool customName;
        public string generalName="",marchTarget="",marchNext="";
        public string stance="March",commanderTrait="";
        public int commanderXP,commanderWounds,commanderWins,lastInterceptTurn,lastGarrisonTurn;
        public string seaFrom="",seaTarget="";
        public int seaTurns,seaDuration;
        public float marchProgress,movementDebt;
        public bool mapPositionKnown,marchToSettlement;
        public float longitude,latitude;
        public string marchArmyId="";
        public List<CampaignMapPoint> marchPath=new List<CampaignMapPoint>();
        public int movement,refitTurns,lastRecoveryTurn,pendingEquipment,lastAIActionTurn;
        public float supply=1;
        public List<CampaignRegiment> units=new List<CampaignRegiment>();
        public int Soldiers { get {int n=0;foreach(var u in units)n+=u.soldiers;return n;} }
    }
    [Serializable] public sealed class RegimentNamingCounter { public string type; public int issued; }
    [Serializable] public sealed class ArmyNamingRecord
    {
        public string armyId,owner; public int ordinal;
        public List<RegimentNamingCounter> counters=new List<RegimentNamingCounter>();
        public List<string> issuedNames=new List<string>();
    }
    [Serializable] public sealed class TownNamingRecord
    {
        public string owner,regionId,townName;
        public List<RegimentNamingCounter> counters=new List<RegimentNamingCounter>();
        public List<string> issuedNames=new List<string>();
    }
    [Serializable] public sealed class RecruitmentOrder { public string id,owner,regionId,role="Regular",weapon="musket",targetArmyId=""; public int doctrine=1,turnsRemaining,soldiers,paidCost,paidMaterials,totalTurns; public bool libraryRecruit; public EmpireBattle.SavedRegiment savedRegiment; }
    [Serializable] public sealed class CampaignSiege { public string id,armyId,regionId,defender; public int turns; }
    [Serializable] public sealed class CampaignBlockade { public string id,owner,origin,regionId; public int turns; }
    [Serializable] public sealed class CampaignTreaty { public string a,b; public bool war,access,trade,alliance; public int warStart,peaceUntil,warScoreA,goodwillA,goodwillB; }
    [Serializable] public sealed class CampaignTradeShipment
    {
        public string seller,buyer,origin,destination,resource="food";
        public float quantity;
        public int payment;
    }
    [Serializable] public sealed class CampaignEncounter
    {
        public string id,attackerArmyId,defenderArmyId,regionId,attacker,defender;
        public int attackerMen,defenderMen;
        public bool amphibious,fieldOnly,ambush;
        public bool mapPositionKnown;
        public float longitude,latitude;
        public string interceptedArmyId="";
        public int siegeTurns;
        public List<string> attackerArmyIds=new List<string>();
        public List<string> defenderArmyIds=new List<string>();
        public List<CampaignRegiment> garrison=new List<CampaignRegiment>();
        public float attackerPower,defenderPower;
    }
    [Serializable] public sealed class CountryCampaignSave
    {
        public int scenarioYear=1800;
        // Additive naming format; zero identifies saves predating persistent designations.
        public int namingVersion;
        public int buildingProgressionVersion;
        public List<ArmyNamingRecord> armyNaming=new List<ArmyNamingRecord>();
        public List<TownNamingRecord> townNaming=new List<TownNamingRecord>();
        public int schemaVersion=6,nextId=1,aiIndex;
        public string scenarioId="";
        public RepublicStory story;
        public CampaignBattleReport lastBattle;
        public List<CampaignProvincePolitics> politics=new List<CampaignProvincePolitics>();
        public List<CampaignHistoryEntry> history=new List<CampaignHistoryEntry>();
        public CampaignTurnSummary turnSummary=new CampaignTurnSummary();
        public uint randomState=1800;
        public string playerFaction,outcome="",victoryPath="",phase="Player";
        public bool roundRunning;
        public bool openingRecruitment,openingMarch,openingConquest;
        public RegionSave world;
        public List<string> activeRegions=new List<string>();
        public List<CountryProgress> countries=new List<CountryProgress>();
        public List<CampaignArmy> armies=new List<CampaignArmy>();
        public List<RecruitmentOrder> recruitment=new List<RecruitmentOrder>();
        public List<CampaignSiege> sieges=new List<CampaignSiege>();
        public List<CampaignBlockade> blockades=new List<CampaignBlockade>();
        public List<CampaignTreaty> treaties=new List<CampaignTreaty>();
        public List<CampaignTradeShipment> lastTradeShipments=new List<CampaignTradeShipment>();
        public List<string> events=new List<string>(),resolvedEncounters=new List<string>();
        public CampaignEncounter pending;
    }
    public sealed class CampaignRoute { public List<string> regions=new List<string>(); public float distance; }
}
