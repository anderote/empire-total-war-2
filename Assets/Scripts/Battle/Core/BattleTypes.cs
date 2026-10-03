using System;
using UnityEngine;

namespace EmpireBattle
{
    public enum ArtilleryAmmunition { Roundshot, Canister, Explosive, Carcass, Lime, PoisonGas }
    public enum ArmySide { French, British }
    public enum FormationKind { Line, Column, Skirmish, Square, Diamond }
    public enum UnitKind { Infantry, Cavalry }
    // Keep the old numeric rank value readable; it now resolves to platoon fire.
    public enum InfantryFireMode { ByRank = 0, ByPlatoon = 1, Volley = 2, FireAtWill = 3 }
    public enum InfantryAttackMode { None, FirePosition, BayonetCharge }
    public enum MeleeAction { None, Ready, Thrust, Parry, ButtStrike, Recoil, Stagger, Retreat, SlashLeft, SlashRight, Brake, TurnAway, Stumble }
    public enum ChargePhase { None, Approach, Impact, Engaged, Recovering }
    public enum BattleOutcome { Ongoing, FrenchVictory, BritishVictory, Draw }
    public enum ArmyTactics { Balanced, HoldGround, Assault, Envelop, Skirmish }

    [Serializable]
    public sealed class BattleConfig
    {
        public const int MaxPersonnelPerRegiment = 500;
        [NonSerialized] public bool AssetReview;
        public string ScenarioId;
        public ArmyTactics EnemyTactics;
        public bool ExplicitArmyCounts, PreserveInitialFormations;
        public FormationKind[] InitialFormations;
        public int BattalionsPerSide = 40;
        public int SoldiersPerBattalion = 250;
        // Zero preserves the symmetric BattalionsPerSide setting for existing scenarios.
        public int FrenchBattalions, BritishBattalions;
        public int FrenchArtillery = 4, BritishArtillery = 4;
        public int FrenchCavalry, BritishCavalry, RidersPerSquadron = 60;
        public int FrenchInfantryCount => ExplicitArmyCounts || FrenchBattalions > 0 ? FrenchBattalions : BattalionsPerSide;
        public int FrenchBattalionCount => FrenchInfantryCount + FrenchCavalry;
        public int BritishInfantryCount => ExplicitArmyCounts || BritishBattalions > 0 ? BritishBattalions : BattalionsPerSide;
        public int BritishBattalionCount => BritishInfantryCount + BritishCavalry;
        public static BattleConfig CreatePlaytest() => new BattleConfig
        {
            FrenchBattalions = 30, BritishBattalions = 60,
            FrenchArtillery = 60, BritishArtillery = 4, FrenchCavalry = 2, BritishCavalry = 2
        };
        // Optional campaign deployment. Defaults preserve the standalone battle scenarios.
        public int[] InitialStrengths;
        public int[] FrenchBatterySizes, BritishBatterySizes;
        public UnitRole[] FormationRoles;
        public int[] FormationTiers,ArtilleryTiers;
        public string PlayerFaction="france",EnemyFaction="britain";
        public string[] FormationNames,FormationWeapons;
        public int[] FormationDoctrine;
        public float[] FormationQuality;
        public float[] FormationExperience,FormationSupply;
        public UnitProgression[] FormationProgression,ArtilleryProgression;
        public int[] ArtilleryEquipment;
        public UnitLoadout[] FormationLoadouts,ArtilleryLoadouts;
        public string PlayerCountryName="France",EnemyCountryName="Britain";
        public Color PlayerCountryColor=new Color(.16f,.32f,.75f),EnemyCountryColor=new Color(.78f,.14f,.13f);
        public bool CampaignDeployment, RegimentalDeployment, RequireDeployment, RegionalMap;
        public int MaxActiveCampaignTroops=20000;
        public float[] FormationArrivalSeconds;
        public BattleMapProfile Map;
        public float WeaponWetness;
        public int Seed = 1815;
        public float ShotHorseDeathChance = .25f;
        public float FieldHalfSize = 1200f;
        public float InitialSeparation = 300f;
        public float MarchSpeed = 1.2f;
        public float MusketRange = 150f;
        public float ReloadSeconds = 15f;
        public int PlatoonFiles = 16;
        public float PlatoonFireInterval = .85f;
        public float RankFireInterval = 2.6f;
        public bool EnableAI = true;
        public bool EnableLandscape = true;
        public bool UseDetailedInfantry = true;
        public int TotalSoldiers
        {
            get
            {
                if(InitialStrengths==null||InitialStrengths.Length==0)return checked((FrenchInfantryCount+BritishInfantryCount)*SoldiersPerBattalion+(FrenchCavalry+BritishCavalry)*RidersPerSquadron);
                int total=0;foreach(int n in InitialStrengths)total=checked(total+n);return total;
            }
        }
    }

    public sealed class BattalionState
    {
        public readonly System.Collections.Generic.List<BattleOrder> PendingOrders = new System.Collections.Generic.List<BattleOrder>();
        public int GrenadesRemaining;
        public bool LastStandUsed;
        public float LastStandUntil;
        public InfantryWeapon Weapon;
        public UnitRole Role;
        public int TroopTier;
        public RegimentBaseStats BaseStats=>RegimentBaseStats.For(TroopTier,Role);
        public float MaxHealth=>BaseStats.Health+Progression.MaxHealth-100;
        public float MaxMorale=>BaseStats.Morale+Progression.MaxMorale-100;
        public string Faction;
        public int Doctrine=(int)MilitaryEquipment.AllDoctrine;
        public float EquipmentQuality,FormationReadyTime;
        public UnitProgression Progression=new UnitProgression();
        public UnitLoadout Loadout=new UnitLoadout();
        public EquipmentModifiers Kit;
        public int Misfires;
        public int Id;
        public ArmySide Side;
        public UnitKind Kind;
        public string Name;
        public int SoldierStart;
        public int SoldierCount;
        public int AliveCount; // Troops still present on the field.
        public int EscapedCount;
        public bool InReserve;
        public bool HasEnteredBattle=true;
        public float ArrivalTime;
        public int SurvivorCount => AliveCount + EscapedCount + (InReserve?SoldierCount:0);
        public Vector2 Position;
        public Vector2 Destination;
        public float Facing; // radians: zero faces +Z; +pi/2 faces +X
        public float DesiredFacing;
        public FormationKind Formation = FormationKind.Line;
        public int Files = 125;
        public int SquareDepth; // zero selects an automatic depth; orders can choose deeper walls
        public float Spacing = .62f;
        [NonSerialized] public float ReviewRankSpacing; // Dedicated workbench grid only.
        public float LimeObscuration, LimeExposureUntil;
        // Exposure injuries belong to this battle state, never to saved regiment progression.
        [NonSerialized] public float BattleGasExposure, BattleLimeExposure;
        public float GasMovementMultiplier=>1f-.4f*Mathf.Clamp01(BattleGasExposure);
        public float LastIncomingFireTime = -100f;
        public float Morale = 100f;
        public float Fatigue;
        public float Cohesion = 1f;
        public int Ammunition = 30;
        public int StartingAmmunition = 30;
        public float ReloadRemaining;
        public InfantryFireMode FireMode = InfantryFireMode.ByPlatoon;
        public int FireGroup, FireCycleFiles, FireCycleRanks, FireCyclePlatoons;
        public InfantryFireMode FireCycleMode;
        public bool FireCycleActive, FireCycleSpentRound;
        public float NextFireTime, FireCycleReadyTime;
        public int LastVolleySize;
        public bool FireAtWill = true;
        public bool Routing;
        public bool Moving;
        public bool Reforming;
        public float FormationError;
        public float ActualSpeed;
        public bool Running;
        public bool Trotting; // Cavalry intermediate pace; Running selects gallop.
        public bool Withdrawing;
        public int AttackTargetId = -1;
        public bool PursueTargets;
        public bool MeleeMode;
        public InfantryAttackMode AttackMode;
        public bool InMelee;
        public bool Bracing;
        public bool BayonetsFixed = false;
        public float BayonetOrderTime = -100f;
        public float BayonetOrderInitialPhase;
        public float BraceStarted = -100f;
        public float ImpactSpeed, ChargeElapsed, MountedSpeed, ChargeRecoveryUntil, LastImpactTime = -100f;
        public int ChargeCycle;
        public bool ChargeSpent;
        public Vector2 ChargeDirection;
        public ChargePhase ChargePhase;
        public float LastMeleeTime = -100f;
        public float LastVolleyTime = -100f;
        public int Kills;
        public int ShotsFired;
        public int Hits;
        public float Experience;
        public string MoraleReason = "Steady";
    }

    public sealed class SoldierBuffer
    {
        public Vector2[] Positions, Velocities;
        public bool[] Alive;
        public bool[] PikeSergeants; // Persistent identities: casualties never promote replacements.
        public bool[] Captains;
        public int[] SidearmRounds, SidearmReserves;
        public float[] SidearmReadyTimes;
        public int[] BattalionIds;
        public float[] Health;
        public float[] KnockdownStarts, KnockdownEnds, KnockdownLandings, KnockdownFacings;
        public int[] KnockdownRecoveryVariants;
        public float[] KnockdownRecoveryFacings;
        public Vector3[] KnockdownOrigins, KnockdownImpulses, KnockdownLandingPositions;
        public bool IsKnockedDown(int soldier, float time) => Alive[soldier] && KnockdownEnds[soldier] > time;
        public int[] FormationSlots, RoundsRemaining,MagazineRounds;
        public float[] Facings, MovementSpeeds, GaitPhases;
        public float[] GrenadeThrowStarts;
        public float[] FireTimes, FireVolleys, ReloadReadyTimes, ReloadDurations, KneelTimes;
        public Vector3[] FireOrigins, FireLockOrigins, FireDirections;
        public int[] FireTargetSoldiers;
        public Vector3[] FireAimPoints;
        public int[] Kills;
        public float[] Experience;
        public int[] MeleeTargets, ImpactCycles;
        public MeleeAction[] MeleeActions;
        public float[] MeleeStarts, MeleeEnds, MeleeReadyTimes;
        public bool[] StrikeResolved;
        public SoldierBuffer(int count)
        {
            Positions = new Vector2[count]; Velocities = new Vector2[count];
            Alive = new bool[count]; PikeSergeants = new bool[count];
            Captains=new bool[count]; SidearmRounds=new int[count]; SidearmReserves=new int[count]; SidearmReadyTimes=new float[count];
            BattalionIds = new int[count];
            Health = new float[count];
            KnockdownRecoveryVariants = new int[count]; Array.Fill(KnockdownRecoveryVariants,-1);
            KnockdownRecoveryFacings = new float[count]; KnockdownFacings = new float[count]; KnockdownStarts = new float[count]; KnockdownEnds = new float[count]; KnockdownLandings = new float[count];
            KnockdownOrigins = new Vector3[count]; KnockdownImpulses = new Vector3[count]; KnockdownLandingPositions = new Vector3[count];
            FormationSlots = new int[count]; RoundsRemaining = new int[count]; MagazineRounds = new int[count];
            for (int i = 0; i < count; i++) RoundsRemaining[i] = 30;
            Facings = new float[count]; MovementSpeeds = new float[count]; GaitPhases = new float[count];
            GrenadeThrowStarts=new float[count];Array.Fill(GrenadeThrowStarts,-100f);
            FireTimes = new float[count]; FireVolleys = new float[count];
            ReloadReadyTimes = new float[count]; ReloadDurations = new float[count]; KneelTimes = new float[count];
            for (int i = 0; i < count; i++) KneelTimes[i] = float.PositiveInfinity;
            FireOrigins = new Vector3[count]; FireLockOrigins = new Vector3[count]; FireDirections = new Vector3[count];
            FireTargetSoldiers=new int[count];FireAimPoints=new Vector3[count];
            for(int i=0;i<count;i++)FireTargetSoldiers[i]=-1;
            for (int i = 0; i < count; i++) FireTimes[i] = FireVolleys[i] = -100f;
            Kills = new int[count];
            Experience = new float[count];
            MeleeTargets=new int[count];ImpactCycles=new int[count];MeleeActions=new MeleeAction[count];
            MeleeStarts=new float[count];MeleeEnds=new float[count];MeleeReadyTimes=new float[count];StrikeResolved=new bool[count];
            for(int i=0;i<count;i++){MeleeTargets[i]=-1;ImpactCycles[i]=-1;}
        }
    }
}
