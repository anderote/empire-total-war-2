using Unity.Profiling;
using System;
using System.Linq;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Compact battlefield state: one shared order per battalion and no soldier GameObjects.</summary>
    public sealed partial class BattleWorld
    {
        static readonly ProfilerMarker PerformanceMarker = new ProfilerMarker("Battle.Movement");
        public BattleConfig Config { get; }
        public BattlefieldTerrain Terrain { get; }
        public BattalionState[] Battalions { get; }
        public SoldierBuffer Soldiers { get; }
        public SoldierMovementJobs MovementJobs { get; set; }
        public InfantryContactMovement ContactMovement { get; }
        public SoldierContactSolver BodyContacts { get; }
#if UNITY_EDITOR
        public static Func<BattleWorld,SoldierMovementJobs> VerificationMovementFactory;
#endif
        public SoldierDeath[] Deaths { get; }
        public int[] DeadSoldiers { get; }
        public int DeathCount { get; private set; }
        public event Action<SoldierDeath> SoldierDied;
        public event Action<int> SoldierKnockedDown;
        public event Action<int,Vector3> MusketWounded;
        public float Time { get; private set; }
        public BattleOutcome Outcome { get; set; } = BattleOutcome.Ongoing;
        public float PursuitUntil { get; set; }
        public bool CanSimulate => !DeploymentActive;
        readonly System.Random random;
        readonly int[][] livingSlots;
        readonly Vector2[] proposals;
        readonly float[] proposedFacings, priorFacings;
        readonly int[] formationOccupants, reformationScratch, reformationResult;
        readonly FormationAssignment formationAssignment;
        readonly bool[] slotsDirty;
        readonly Vector2[] reformationTargets;
        readonly Vector2[] detourFirst;
        readonly Vector2[] detourSecond;
        readonly int[] detourStage;
        readonly float[] nextDetourTime;
        readonly float[] contactReformSpacing;
        internal readonly InfantryTargeting.Candidate[] TargetingCandidates;

        public BattleWorld(BattleConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            if (config.BattalionsPerSide < 1 || config.FrenchBattalions < 0 || config.BritishBattalions < 0 || config.SoldiersPerBattalion < 1 || config.FieldHalfSize < 20f || config.FrenchCavalry < 0 || config.BritishCavalry < 0 || config.RidersPerSquadron < 1)
                throw new ArgumentOutOfRangeException(nameof(config), "Armies need positive counts and a field half-size of at least 20 meters.");
            if(config.SoldiersPerBattalion>BattleConfig.MaxPersonnelPerRegiment||config.RidersPerSquadron>BattleConfig.MaxPersonnelPerRegiment)
                throw new ArgumentOutOfRangeException(nameof(config),"Personnel regiments cannot exceed 500 troops or riders.");
            // Unity deserializes unused optional arrays as empty rather than null.
            if(config.InitialStrengths?.Length==0)config.InitialStrengths=null;
            if(config.FormationNames?.Length==0)config.FormationNames=null;
            if(config.FormationExperience?.Length==0)config.FormationExperience=null;
            if(config.FormationSupply?.Length==0)config.FormationSupply=null;
            if(!Finite(config.WeaponWetness)||config.WeaponWetness<0||config.WeaponWetness>1)throw new ArgumentException("Invalid weapon wetness.");
            int formationCount=config.FrenchBattalionCount+config.BritishBattalionCount;
            if(config.FrenchBattalionCount<1||config.BritishBattalionCount<1)throw new ArgumentException("Each army needs at least one regiment or cavalry unit.");
            if(config.InitialFormations?.Length==0)config.InitialFormations=null;
            if(config.InitialFormations!=null&&(config.InitialFormations.Length!=formationCount||Array.Exists(config.InitialFormations,f=>!Enum.IsDefined(typeof(FormationKind),f))))throw new ArgumentException("Invalid initial formations.");
            if(config.FormationProgression?.Length==0)config.FormationProgression=null;
            if(config.FormationProgression!=null&&(config.FormationProgression.Length!=formationCount||Array.Exists(config.FormationProgression,p=>p==null||!p.Valid)))throw new ArgumentException("Invalid unit progression deployment.");
            if(config.FormationLoadouts?.Length==0)config.FormationLoadouts=null;
            if(config.FormationLoadouts!=null&&config.FormationLoadouts.Length!=formationCount)throw new ArgumentException("Invalid equipment loadout count.");
            if(config.FormationRoles?.Length==0)config.FormationRoles=null;
            if(config.FormationTiers?.Length==0)config.FormationTiers=null;
            if(config.FormationTiers!=null&&(config.FormationTiers.Length!=formationCount||Array.Exists(config.FormationTiers,t=>t<0||t>10)))throw new ArgumentException("Invalid troop tier deployment.");
            if(config.FormationRoles!=null&&(config.FormationRoles.Length!=formationCount||Array.Exists(config.FormationRoles,r=>!Enum.IsDefined(typeof(UnitRole),r))))throw new ArgumentException("Invalid role deployment.");
            if(config.FormationWeapons?.Length==0)config.FormationWeapons=null;
            if(config.FormationDoctrine?.Length==0)config.FormationDoctrine=null;
            if(config.FormationQuality?.Length==0)config.FormationQuality=null;
            if(config.FormationWeapons!=null&&(config.FormationWeapons.Length!=formationCount||System.Array.Exists(config.FormationWeapons,id=>MilitaryEquipment.Weapon(id)==null)))throw new ArgumentException("Unknown or mismatched weapon deployment.");
            if(config.FormationDoctrine!=null&&(config.FormationDoctrine.Length!=formationCount||System.Array.Exists(config.FormationDoctrine,d=>!MilitaryEquipment.ValidDoctrine(d))))throw new ArgumentException("Invalid doctrine deployment.");
            if(config.FormationQuality!=null&&(config.FormationQuality.Length!=formationCount||System.Array.Exists(config.FormationQuality,q=>!Finite(q)||q<0||q>.2f)))throw new ArgumentException("Invalid quality deployment.");
            if(config.CampaignDeployment&&(config.FormationWeapons==null||config.FormationDoctrine==null))throw new ArgumentException("Campaign deployment requires issued weapons and adopted doctrine.");
            if(config.InitialStrengths!=null)
            {
                if(config.InitialStrengths.Length!=formationCount)throw new ArgumentException("Campaign formation count mismatch.");
                for(int i=0;i<config.InitialStrengths.Length;i++)
                {
                    bool mounted=i<config.FrenchBattalionCount?i>=config.FrenchInfantryCount:i-config.FrenchBattalionCount>=config.BritishInfantryCount;
                    int capacity=mounted?config.RidersPerSquadron:config.SoldiersPerBattalion;
                    int count=config.InitialStrengths[i];
                    if(count<1||count>capacity||count>BattleConfig.MaxPersonnelPerRegiment)throw new ArgumentException("Invalid campaign formation strength.");
                }
            }
            if(config.FormationNames!=null&&config.FormationNames.Length!=formationCount)throw new ArgumentException("Campaign names mismatch.");
            if(config.FormationExperience!=null&&config.FormationExperience.Length!=formationCount)throw new ArgumentException("Campaign experience mismatch.");
            if(config.FormationSupply!=null&&config.FormationSupply.Length!=formationCount)throw new ArgumentException("Campaign supply mismatch.");
            if(config.FormationExperience!=null)foreach(float v in config.FormationExperience)if(!Finite(v)||v<0||v>10)throw new ArgumentException("Invalid campaign experience.");
            if(config.FormationSupply!=null)foreach(float v in config.FormationSupply)if(!Finite(v)||v<0||v>1)throw new ArgumentException("Invalid campaign supply.");
            int total = config.TotalSoldiers;
            Terrain = new BattlefieldTerrain(config);
            random = new System.Random(config.Seed);
            Battalions = new BattalionState[checked(config.FrenchBattalionCount + config.BritishBattalionCount)];
            Soldiers = new SoldierBuffer(total);
            Deaths = new SoldierDeath[total];
            DeadSoldiers = new int[total];
            livingSlots = new int[Battalions.Length][];
            proposals = new Vector2[Battalions.Length];
            proposedFacings = new float[Battalions.Length]; priorFacings = new float[Battalions.Length];
            formationOccupants = new int[total];
            int maxFormationSize=Mathf.Max(config.SoldiersPerBattalion,config.FrenchCavalry+config.BritishCavalry>0?config.RidersPerSquadron:0);
            TargetingCandidates=new InfantryTargeting.Candidate[maxFormationSize];
            reformationScratch = new int[maxFormationSize];
            reformationResult = new int[maxFormationSize];
            formationAssignment = new FormationAssignment(maxFormationSize);
            reformationTargets = new Vector2[maxFormationSize];
            slotsDirty = new bool[Battalions.Length];
            detourFirst = new Vector2[Battalions.Length];
            detourSecond = new Vector2[Battalions.Length];
            detourStage = new int[Battalions.Length];
            nextDetourTime = new float[Battalions.Length];
            contactReformSpacing = new float[Battalions.Length];
            int files = FormationMath.FileCount(config.SoldiersPerBattalion, FormationKind.Line);
            float frontage = files * .62f;
            float lane = frontage + 12f;
            int fieldColumns = Mathf.Clamp(Mathf.FloorToInt((config.FieldHalfSize * 2f - 80f) / lane), 1, 20);
            float frontZ = Mathf.Min(config.InitialSeparation * .5f, config.FieldHalfSize * .6f);
            int deployedOffset=0;
            for (int i = 0; i < Battalions.Length; i++)
            {
                bool french = i < config.FrenchBattalionCount;
                int armyCount = french ? config.FrenchBattalionCount : config.BritishBattalionCount;
                int columns = Mathf.Min(fieldColumns, armyCount);
                int armyIndex = french ? i : i - config.FrenchBattalionCount;
                int row = armyIndex / columns;
                int rowCount = Mathf.Min(columns, armyCount - row * columns);
                float x = (armyIndex % columns - (rowCount - 1) * .5f) * lane;
                float z = (frontZ + row * 40f) * (french ? -1f : 1f);
                bool mounted=armyIndex >= (french?config.FrenchInfantryCount:config.BritishInfantryCount);
                int deployed=config.InitialStrengths!=null?config.InitialStrengths[i]:mounted?config.RidersPerSquadron:config.SoldiersPerBattalion;
                int deployedFiles=mounted?Mathf.CeilToInt(deployed/2f):FormationMath.FileCount(deployed,FormationKind.Line);
                if(mounted){x=(armyIndex%2==0?-1:1)*(Mathf.Min(columns,(french?config.FrenchInfantryCount:config.BritishInfantryCount))*.5f*lane+30);z=(frontZ+35)*(french?-1:1);}
                var battalion = new BattalionState
                {
                    Id = i, Side = french ? ArmySide.French : ArmySide.British, Kind=mounted?UnitKind.Cavalry:UnitKind.Infantry, Spacing=mounted?1.65f:.62f, FireAtWill=!mounted,
                    Name = config.FormationNames!=null?config.FormationNames[i]:(french ? "French " : "British ") + (mounted?"Cavalry "+(armyIndex-(french?config.FrenchInfantryCount:config.BritishInfantryCount)+1): (armyIndex + 1) + " Battalion"),
                    Weapon=config.FormationWeapons!=null?MilitaryEquipment.Weapon(config.FormationWeapons[i]):null,
                    Doctrine=config.FormationDoctrine!=null?config.FormationDoctrine[i]:(int)MilitaryEquipment.AllDoctrine,
                    TroopTier=config.FormationTiers!=null?config.FormationTiers[i]:0,
                    EquipmentQuality=config.FormationQuality!=null?config.FormationQuality[i]:0,
                    Progression=config.FormationProgression!=null?config.FormationProgression[i].Copy():new UnitProgression(),
                    Loadout=config.FormationLoadouts!=null?config.FormationLoadouts[i]?.Copy():new UnitLoadout(),
                    SoldierStart = deployedOffset,
                    SoldierCount = deployed, AliveCount = deployed,
                    Experience=config.FormationExperience!=null?config.FormationExperience[i]*deployed:0,
                    Fatigue=config.FormationSupply!=null?(1-config.FormationSupply[i])*50:0,
                    Ammunition=config.FormationSupply!=null?Mathf.Max(6,Mathf.RoundToInt(30*config.FormationSupply[i])):30,
                    Files = deployedFiles, Facing = french ? 0f : Mathf.PI, DesiredFacing = french ? 0f : Mathf.PI,
                    Position = new Vector2(x, z)
                };
                battalion.Role=config.FormationRoles!=null?config.FormationRoles[i]:mounted?UnitRole.Cavalry:battalion.Weapon?.Id=="rifle"?UnitRole.Rifleman:UnitRole.Line;
                if(mounted!=(battalion.Role==UnitRole.Cavalry))throw new ArgumentException("Mounted role must match cavalry allocation.");
                battalion.Faction=french?config.PlayerFaction:config.EnemyFaction;
                if(battalion.Loadout==null||!battalion.Loadout.Valid(UnitEquipment.RetainedUsers(UnitEquipment.BattleUsers(battalion),battalion.TroopTier)))throw new ArgumentException("Invalid equipment for deployed unit.");
                var issuedWeapon=battalion.Loadout.items.Select(UnitEquipment.Find).FirstOrDefault(item=>item!=null&&!string.IsNullOrEmpty(item.Weapon));
                if(issuedWeapon!=null)battalion.Weapon=MilitaryEquipment.Weapon(issuedWeapon.Weapon);
                battalion.Kit=battalion.Loadout.Modifiers;battalion.GrenadesRemaining=battalion.Progression.Grenades(battalion.Kit.grenades);
                battalion.Ammunition+=battalion.Kit.ammunition+battalion.Progression.AmmunitionBonus(battalion.Weapon);
                battalion.Morale=battalion.MaxMorale+battalion.Kit.moraleReserve;
                battalion.FireAtWill=UnitRoster.HasFirearm(battalion);
                if(!battalion.FireAtWill)battalion.Ammunition=0;
                battalion.StartingAmmunition=battalion.Ammunition;
                battalion.FireMode=UnitRoster.DefaultFireMode(battalion.Role);
                if(config.InitialFormations!=null&&!mounted){battalion.Formation=config.InitialFormations[i];battalion.Files=FormationMath.FileCount(deployed,battalion.Formation);}
                if(EmpireBattle.Art.CartridgeRifleLibrary.UsesCartridges(battalion.Weapon))
                {if(battalion.Formation==FormationKind.Line)battalion.Files=Mathf.CeilToInt(deployed/2f);}
                battalion.Position = ClampToField(battalion, battalion.Position);
                battalion.Destination = battalion.Position;
                Battalions[i] = battalion;
                deployedOffset+=deployed;
                livingSlots[i] = new int[battalion.SoldierCount];
                for (int slot = 0; slot < battalion.SoldierCount; slot++)
                {
                    int index = battalion.SoldierStart + slot;
                    livingSlots[i][slot] = index;
                    formationOccupants[index] = index;
                    Soldiers.FormationSlots[index] = slot;
                    Soldiers.Facings[index] = FormationMath.SlotFacing(battalion,slot);
                    Soldiers.GaitPhases[index] = RifleFireTiming.Variation(index,config.Seed);
                    Soldiers.Alive[index] = true;
                    Soldiers.Health[index] = battalion.MaxHealth;
                    Soldiers.RoundsRemaining[index]=battalion.Ammunition;
                    Soldiers.MagazineRounds[index]=Mathf.Min(battalion.Weapon?.Magazine??1,battalion.Ammunition);
                    Soldiers.Experience[index]=config.FormationExperience!=null?config.FormationExperience[i]:0;
                    Soldiers.BattalionIds[index] = i;
                    Soldiers.Positions[index] = battalion.Position + FormationMath.SlotOffset(battalion,slot);
                }
                InitializeSergeants(battalion);
                InitializeCaptains(battalion);
            }
InitializeCampaignReserves();
            ContactMovement=new InfantryContactMovement(this);
            BodyContacts=new SoldierContactSolver(this);
            InitializeRegionalDeployment();
#if UNITY_EDITOR
            MovementJobs=VerificationMovementFactory?.Invoke(this);
#endif
        }

        public void IssueMove(int id, Vector2 destination, float facing, bool preserveRoute = false, bool preserveAttack = false)
        {
            if (!Valid(id) || !Finite(destination.x) || !Finite(destination.y) || !Finite(facing)) return;
            var b = Battalions[id];
            if(DeploymentActive){TryDeploy(id,destination,facing,out _);return;}
            if(!preserveAttack)ClearPendingOrders(id);
            if (!preserveAttack) { ContactMovement.CancelGathering(id); b.AttackTargetId = -1; b.AttackMode = InfantryAttackMode.None; b.ChargePhase=ChargePhase.None; }
            Vector2 clamped = ClampDestination(id, destination, b.Files, facing);
            if (!preserveRoute && (clamped - b.Destination).sqrMagnitude > 1f) detourStage[id] = 0;
            else if(preserveRoute&&detourStage[id]>0)detourSecond[id]=ClampDestination(id,detourSecond[id]+clamped-b.Destination,b.Files,facing);
            b.Destination = clamped;
            b.DesiredFacing = facing;
            b.Moving = (b.Position - b.Destination).sqrMagnitude > .04f;
            b.Withdrawing = false;
            b.Reforming = b.Reforming || Mathf.Abs(FormationMath.AngleDifference(b.Facing,facing))>.02f;
        }
        public void IssueMarch(int id, Vector2 destination)
        {
            if (!Valid(id)) return;
            Vector2 travel = destination - Battalions[id].Position;
            IssueMove(id, destination, travel.sqrMagnitude > .04f ? FormationMath.FacingOf(travel) : Battalions[id].Facing);
        }
        public void SetFireMode(int id, InfantryFireMode mode)
        {
            if (!Valid(id) || Battalions[id].Routing || !CanFireMode(Battalions[id],mode)) return;
            RifleFireTiming.Interrupt(this, Battalions[id]);
            Battalions[id].FireMode = RifleFireTiming.NormalizeMode(mode);
        }
        public void SetFormation(int id, FormationKind formation)
        {
            if (!Valid(id) || Battalions[id].Routing) return;
            var b = Battalions[id];
            if(!CanForm(b,formation))return;
            ContactMovement.CancelGathering(id);
            if (b.Formation == formation && b.Files == FormationMath.FileCount(b.AliveCount, formation)) return;
            detourStage[id] = 0;
            RifleFireTiming.Interrupt(this, b);
            // Unfired rear groups retain their cartridges when a drill is interrupted.
            // The aggregate ammunition counter must never overwrite individual reserves.
            b.Formation = formation;
            contactReformSpacing[id]=0;
            if(Config.CampaignDeployment)b.FormationReadyTime=Time+(formation==FormationKind.Square?4f:2f)*(MilitaryEquipment.Has(b.Doctrine,MilitaryDoctrine.Maneuver)?.85f:1);
            b.Spacing = formation==FormationKind.Skirmish?(b.Kind==UnitKind.Cavalry?3.2f:2.6f):b.Kind==UnitKind.Cavalry?1.65f:.62f;
            b.Files = b.Kind==UnitKind.Cavalry&&formation==FormationKind.Line?Mathf.CeilToInt(b.AliveCount/2f):FormationMath.FileCount(b.AliveCount, formation);
            b.Destination = ClampToField(b, b.Destination);
            ReassignFormationSlots(b);
            b.Reforming = true;
        }
        /// <summary>Resize open/block formations while preserving their drill and spacing. Fixed shapes only turn.</summary>
        public void SetFrontage(int id, int files)
        {
            if (!Valid(id) || Battalions[id].Routing) return;
            var b = Battalions[id];
            if (!FormationMath.CanResizeFrontage(b)) return;
            ContactMovement.CancelGathering(id);
            int frontage = FormationMath.ClampFrontage(b, files);
            if (b.Files == frontage) return;
            detourStage[id] = 0;
            RifleFireTiming.Interrupt(this, b);
            b.Files = frontage;
            b.Destination = ClampToField(b, b.Destination);
            ReassignFormationSlots(b);
            b.Reforming = true;
        }

        public void AdjustRanks(int id, int change)
        {
            if (!Valid(id) || Battalions[id].Routing || Battalions[id].AliveCount <= 0 || Battalions[id].Formation == FormationKind.Diamond || change == 0) return;
            var unit = Battalions[id];
            int current = FormationMath.RankCount(unit);
            ContactMovement.CancelGathering(id);
            int maximum = unit.Formation == FormationKind.Square ? FormationMath.MaximumSquareRanks(unit.AliveCount) : unit.AliveCount;
            int ranks = Mathf.Clamp(current + change,1,maximum);
            if (ranks == current) return;
            RifleFireTiming.Interrupt(this,unit); detourStage[id] = 0;
            if (unit.Formation == FormationKind.Square) unit.SquareDepth = ranks;
            else
            {
                int files = Mathf.Max(1,Mathf.CeilToInt(unit.AliveCount/(float)ranks));
                // At narrow widths several requested depths map to the same file count.
                // Advance to the next representable block instead of making a dead key.
                if (files == unit.Files) files = Mathf.Clamp(unit.Files + (change > 0 ? -1 : 1),1,unit.AliveCount);
                unit.Files = files;
            }
            unit.Destination = ClampToField(unit,unit.Destination);
            ReassignFormationSlots(unit);unit.Reforming = true;
        }

        public void Halt(int id)
        {
            if (!Valid(id) || Battalions[id].Routing) return;
            ClearPendingOrders(id);
            ContactMovement.CancelGathering(id);
            var b = Battalions[id];
            b.AttackTargetId = -1; b.AttackMode = InfantryAttackMode.None; b.ChargePhase=ChargePhase.None;
            detourStage[id] = 0;
            b.Destination = b.Position;
            b.DesiredFacing = b.Facing;
            b.Moving = false;
            b.Withdrawing = false;
        }
        public void Withdraw(int id)
        {
            if (!Valid(id)) return;
            ClearPendingOrders(id);
            ContactMovement.CancelGathering(id);
            var b = Battalions[id];
            b.AttackTargetId = -1; b.AttackMode = InfantryAttackMode.None; b.ChargePhase=ChargePhase.None;
            detourStage[id] = 0;
            // Retreat beyond the edge so every straggler can clear the field.
            b.Destination = new Vector2(b.Position.x, (b.Side == ArmySide.French ? -1f : 1f)
                * (Config.FieldHalfSize + FormationMath.HalfExtents(b).magnitude + 16f));
            b.Withdrawing = true;
            b.Bracing = false;
            b.Moving = true;
            b.DesiredFacing = FormationMath.FacingOf(b.Destination - b.Position);
            RifleFireTiming.Interrupt(this,b);
            for(int i=b.SoldierStart;i<b.SoldierStart+b.SoldierCount;i++)
            { Soldiers.MeleeActions[i]=MeleeAction.Retreat;Soldiers.MeleeTargets[i]=-1;Soldiers.MeleeEnds[i]=Time;Soldiers.StrikeResolved[i]=true; }
        }

        public bool IssueRangedAttack(int id, int targetId)
        {
            if(Valid(id)&&(Battalions[id].MeleeMode||!UnitRoster.HasFirearm(Battalions[id])))return IssueCharge(id,targetId);
            if (!Valid(id) || !Valid(targetId)) return false;
            var unit = Battalions[id]; var target = Battalions[targetId];
            if (unit.Routing || unit.AliveCount <= 0 || target.Side == unit.Side || target.AliveCount <= 0) return false;
            Halt(id);
            RifleFireTiming.Interrupt(this, unit);
            unit.AttackTargetId = targetId; unit.AttackMode = InfantryAttackMode.FirePosition;
            unit.FireAtWill = true;
            UpdateRangedOrder(unit);
            return true;
        }

        public void SetPursuit(int id, bool enabled)
        {
            if (!Valid(id) || Battalions[id].Routing) return;
            if(!enabled&&IsPursuingFugitives(Battalions[id]))Halt(id);
            Battalions[id].PursueTargets = enabled;
            UpdateRangedOrder(Battalions[id]);
        }

        void UpdateRangedOrders()
        {
            foreach (var unit in Battalions) UpdateRangedOrder(unit);
        }

        void UpdateRangedOrder(BattalionState unit)
        {
            if (unit.AttackMode != InfantryAttackMode.FirePosition || unit.Routing || unit.AliveCount <= 0 || unit.Withdrawing) return;
            BattalionState target = Valid(unit.AttackTargetId) ? Battalions[unit.AttackTargetId] : null;
            if (target == null || target.AliveCount <= 0 || target.Routing || target.Side == unit.Side)
            {
                target = null; float nearest = AimedRange(unit)*AimedRange(unit);
                foreach (var enemy in Battalions)
                {
                    if (enemy.Side == unit.Side || enemy.AliveCount <= 0 || enemy.Routing
                        || Terrain.BlocksLine(unit.Position,enemy.Position)) continue;
                    float distance = (enemy.Position - unit.Position).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; target = enemy; }
                }
                unit.AttackTargetId = target == null ? -1 : target.Id;
            }
            if (target == null)
            {
                unit.AttackMode = InfantryAttackMode.None;
                unit.Destination = unit.Position; unit.Moving = false; detourStage[unit.Id] = 0;
                return;
            }
            Vector2 delta = target.Position - unit.Position;
            float facing = delta.sqrMagnitude > .001f ? FormationMath.FacingOf(delta) : unit.Facing;
            float standoff = Mathf.Max(15f, EffectiveRange(unit) * .8f);
            if (unit.PursueTargets && delta.magnitude > AimedRange(unit))
                IssueMove(unit.Id, target.Position - delta.normalized * standoff, facing, preserveRoute:true, preserveAttack:true);
            else
            {
                unit.Destination = unit.Position; unit.Moving = false; detourStage[unit.Id] = 0;
                if (unit.Formation != FormationKind.Square)
                {
                    bool inArc=delta.sqrMagnitude<.01f||Vector2.Dot(FormationMath.Forward(unit.Facing),delta.normalized)>=WeaponBallistics.MinimumFacingDot;
                    unit.DesiredFacing = inArc ? unit.Facing : facing;
                }
            }
        }

        public void StepMovement(float dt)
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Movement);
            using var performanceScope = PerformanceMarker.Auto();
            if (!Finite(dt) || dt <= 0f || !CanSimulate) return;
            Time += dt;
            AdvanceCampaignReinforcements();
            StepLooseHorses(dt);
            StepKnockdowns();
            UpdateQueuedOrders();
            UpdateRangedOrders();
            UpdateChargeOrders(dt);
            BodyContacts.Capture();
            using(BattleMetrics.Measure(BattleMetrics.Stage.ContactMovement))ContactMovement.Prepare(dt);
            foreach(var unit in Battalions)if(contactReformSpacing[unit.Id]>0
                &&(ContactMovement.Loose[unit.Id]||unit.FormationError<.15f))
            {
                unit.Spacing=contactReformSpacing[unit.Id];contactReformSpacing[unit.Id]=0;
                if(!ContactMovement.Loose[unit.Id]&&unit.AliveCount>0)ReassignFormationSlots(unit);
            }
            // After dispersing, assign nearby slots from physical positions. Keeping
            // old slot ownership would make returning men try to cross each other.
            foreach(var unit in Battalions)if(ContactMovement.WasLoose[unit.Id]&&!ContactMovement.Loose[unit.Id]&&unit.AliveCount>0)
            {
                // Open lanes for officers and displaced rear ranks, then close the
                // formation again once everyone reaches an unobstructed slot.
                if(unit.Kind==UnitKind.Infantry&&unit.Spacing<1.3f)
                {contactReformSpacing[unit.Id]=unit.Spacing;unit.Spacing=1.3f;}
                ReassignFormationSlots(unit);
            }
            foreach(var unit in Battalions)if(slotsDirty[unit.Id]&&unit.AliveCount>0)CompactFormationSlots(unit);
            for (int i = 0; i < Battalions.Length; i++)
            {
                var b = Battalions[i];
                priorFacings[i] = proposedFacings[i] = b.Facing;
                proposals[i] = b.Position;
                if (b.AliveCount <= 0) continue;
                if (b.Routing && !b.Withdrawing) Withdraw(i);
                Vector2 target = b.Destination;
                if (detourStage[i] > 0)
                {
                    target = detourStage[i] == 1 ? detourFirst[i] : detourStage[i] == 2 ? detourSecond[i] : b.Destination;
                    if ((b.Position - target).sqrMagnitude < .16f)
                    {
                        detourStage[i] = detourStage[i] < 3 ? detourStage[i]+1 : 0;
                        target = detourStage[i] == 2 ? detourSecond[i] : b.Destination;
                    }
                }
                Vector2 travel = target - b.Position;
                // Side-step around obstacles before adopting the final deployment facing.
                float facingTarget = detourStage[i] > 0 ? b.Facing : b.DesiredFacing;
                if (b.Moving && detourStage[i]==0 && travel.sqrMagnitude > .04f)
                    facingTarget = FormationMath.FacingOf(travel);
                if(b.Kind==UnitKind.Cavalry)b.MountedSpeed=Mathf.MoveTowards(b.MountedSpeed,MountedDesiredSpeed(b),dt*(b.Moving?2.2f:5f));
                float speed = MovementSpeed(b) * .94f;
                float radius = FormationMath.HalfExtents(b).magnitude;
                // Small course corrections wheel; substantial changes use individual turns
                // and new nearby slots instead of walking the wings around the center.
                float angle = Mathf.Abs(FormationMath.AngleDifference(b.Facing,facingTarget));
                float turnRate = b.Kind==UnitKind.Cavalry?Mathf.Clamp(Mathf.Max(1f,speed)/4f,.3f,.8f):Mathf.Clamp(Mathf.Max(1f,speed) / Mathf.Max(2f,radius),.22f,.7f);
                float pivotAngle = !b.Moving && b.AttackMode==InfantryAttackMode.FirePosition ? .12f : Mathf.PI / 6f;
                bool pivot = angle >= pivotAngle && !ContactMovement.Loose[i];
                if(b.Kind==UnitKind.Cavalry)pivot=false;
                // A fleeing regiment's logical centre follows escape immediately;
                // each man's visible turn is still limited by individual movement.
                proposedFacings[i] = ContactMovement.Loose[i] && InfantryContactMovement.Fleeing(b)
                    ?facingTarget:pivot?facingTarget:FormationMath.TurnTowards(b.Facing,facingTarget,dt*turnRate);
                proposals[i]=InfantryContactMovement.Fleeing(b)?b.Position
                    :Vector2.MoveTowards(b.Position,ClampFootprint(b.Position,FormationMath.HalfExtents(b),proposedFacings[i]),dt*Mathf.Max(1f,speed));
                if (!b.Moving) continue;
                float alignment = travel.sqrMagnitude>.04f ? Vector2.Dot(FormationMath.Forward(proposedFacings[i]),travel.normalized) : 1f;
                // Turn toward travel; individuals face and follow their own escape lanes.
                if (detourStage[i]==0) speed *= Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.35f,1f,alignment));
                if(angle>.08f&&detourStage[i]==0) speed*=.8f;
                // Dress ranks on the move. Stragglers slow the centre, but never
                // hold an entire infantry regiment until its last man is in place.
                if (!ContactMovement.Loose[i])
                {
                    if (b.Kind==UnitKind.Cavalry)
                        // Dress the wedge while advancing. A complete stop can
                        // deadlock riders now that they must pass around each other.
                        speed *= Mathf.Lerp(.5f,1f,Mathf.InverseLerp(b.Spacing * 3f, b.Spacing, b.FormationError));
                    else
                        speed *= Mathf.Lerp(.55f,1f,Mathf.InverseLerp(b.Spacing * 5f,b.Spacing,b.FormationError));
                }
                Vector2 nextCenter = Vector2.MoveTowards(b.Position,target,dt*speed);
                proposals[i] = InfantryContactMovement.Fleeing(b)?nextCenter
                    :ClampFootprint(nextCenter,FormationMath.HalfExtents(b),proposedFacings[i]);
            }
            // Terrain constrains formation proposals; body contact is resolved after
            // individual movement so soldiers can push rather than stop at empty footprints.
            for (int i = 0; i < Battalions.Length; i++)
            {
                var b = Battalions[i];
                if (b.AliveCount <= 0) continue;
                float buildingMargin = FormationMath.HalfExtents(b).magnitude;
                if (Terrain.IsBlocked(proposals[i],buildingMargin) && !Terrain.IsBlocked(b.Position,buildingMargin))
                {
                    proposals[i] = b.Position; proposedFacings[i] = priorFacings[i];
                    if (b.Moving && Time >= nextDetourTime[i])
                    {TryBuildingDetour(b,buildingMargin);nextDetourTime[i]=Time+1f;}
                }
                // Physical bodies resolve contact below, including friendly units.
                // Empty formation footprints must not stop a march before men touch.
            }
            for(int i=0;i<Battalions.Length;i++)
            {
                var b=Battalions[i];
                Vector2 oldCenter=b.Position;
                b.Position=proposals[i];b.Facing=proposedFacings[i];
                if (!ContactMovement.Loose[i] && Mathf.Abs(FormationMath.AngleDifference(priorFacings[i], b.Facing)) >= (!b.Moving && b.AttackMode==InfantryAttackMode.FirePosition ? .12f : Mathf.PI / 6f))
                {
                    RifleFireTiming.Interrupt(this, b);
                    ReassignFormationSlots(b);
                }
                b.ActualSpeed=Vector2.Distance(oldCenter,b.Position)/dt;
                if(b.AliveCount==0)continue;
                int files=FormationMath.EffectiveFiles(b);
                float maxError=0f,maxHeadingError=0f;
                float speed=MovementSpeed(b);
                float correctionSpeed=Mathf.Max(speed*(b.Moving&&b.Kind==UnitKind.Infantry?1.2f:1f),Config.MarchSpeed*1.15f);
                if(MovementJobs!=null) { MovementJobs.Prepare(b,oldCenter,correctionSpeed); continue; }
                Vector2 forward=FormationMath.Forward(b.Facing);
                Vector2 right=new Vector2(forward.y,-forward.x);
                bool expandedGaits=FormationMath.ExpandedInfantryGaits(b);
                for(int slot=0;slot<b.AliveCount;slot++)
                {
                    int index=SoldierAtSlot(b,slot);
                    if (Soldiers.IsKnockedDown(index, Time)) continue;
                    bool loose=ContactMovement.Loose[i];
                    Vector2 target=loose?ContactMovement.Targets[index]:b.Position+(b.Formation == FormationKind.Square || b.Formation == FormationKind.Diamond
                        ? FormationMath.SlotOffset(b,slot)
                        : FormationMath.SlotOffset(slot,b.AliveCount,b.Files,b.Spacing,right,forward));
                    Vector2 before=Soldiers.Positions[index];
                    // Translate with the formation, then use a bounded local correction.
                    Vector2 carried=loose?before:before+(b.Position-oldCenter);
                    float variation=InfantryContactMovement.Pace(index,loose&&InfantryContactMovement.Fleeing(b));
                    Vector2 next=loose?target:Vector2.MoveTowards(carried,target,dt*correctionSpeed*variation);
                    // A turn or reformation must never exceed a man's available walking/running speed.
                    if(!loose)next=Vector2.MoveTowards(before,next,dt*correctionSpeed*variation);
                    Vector2 velocity=(next-before)/dt;
                    float actual=velocity.magnitude;
                    Soldiers.Positions[index]=next;Soldiers.Velocities[index]=velocity;Soldiers.MovementSpeeds[index]=actual;
                    bool running=b.Running||InfantryContactMovement.Fleeing(b);
                    float stride=b.Kind==UnitKind.Cavalry?(actual>5?4f:actual>2.5f?2.6f:1.9f):FormationMath.SoldierStride(index,running,actual,b.AttackMode==InfantryAttackMode.BayonetCharge,Soldiers.Captains[index]||Soldiers.PikeSergeants[index],expandedGaits);
                    bool backing=false;
                    Soldiers.GaitPhases[index]=Mathf.Repeat(Soldiers.GaitPhases[index]+actual*dt/stride*(backing?-1f:1f),1f);
                    float slotFacing=FormationMath.SlotFacing(b,slot);
                    float desired=slotFacing;
                    if(Soldiers.MeleeEnds[index]>Time&&Soldiers.MeleeTargets[index]>=0)desired=ContactMovement.MeleeFacings[index];
                    if(actual>.08f&&!backing&&Soldiers.MeleeEnds[index]<=Time)desired=FormationMath.FacingOf(velocity);
                    bool individualAim=b.Kind==UnitKind.Infantry&&!b.Moving&&!b.Routing&&actual<=.04f
                        &&Soldiers.MeleeEnds[index]<=Time&&Soldiers.FireTimes[index]>=0&&Time<Soldiers.FireTimes[index]+RifleFireTiming.KneelDelay;
                    if(individualAim)
                        desired=FormationMath.FacingOf(new Vector2(Soldiers.FireDirections[index].x,Soldiers.FireDirections[index].z));
                    Soldiers.Facings[index]=loose?ContactMovement.Facings[index]:FormationMath.TurnTowards(Soldiers.Facings[index],desired,dt*(b.Kind==UnitKind.Cavalry?2.8f:RifleFireTiming.SoldierTurnRate));
                    maxError=Mathf.Max(maxError,loose?0f:Vector2.Distance(next,target));
                    maxHeadingError=Mathf.Max(maxHeadingError,loose||individualAim||Soldiers.MeleeEnds[index]>Time?0f:Mathf.Abs(FormationMath.AngleDifference(Soldiers.Facings[index],slotFacing)));
                }
                b.FormationError=maxError;
                // Final deployment facing is relevant only after the march ends.
                b.Reforming=ContactMovement.Loose[b.Id]||Time<b.FormationReadyTime||maxError>.22f||maxHeadingError>.08f||(!b.Moving && Mathf.Abs(FormationMath.AngleDifference(b.Facing,b.DesiredFacing))>.025f);
            }
            if(MovementJobs!=null)
            {
                using(BattleMetrics.Measure(BattleMetrics.Stage.MovementJobs))MovementJobs.Step(dt);
                foreach(var b in Battalions)
                {
                    if(b.AliveCount==0)continue;
                    float maxError=MovementJobs.MaxErrors[b.Id],maxHeadingError=MovementJobs.MaxHeadingErrors[b.Id];
                    b.FormationError=maxError;
                    b.Reforming=ContactMovement.Loose[b.Id]||Time<b.FormationReadyTime||maxError>.22f||maxHeadingError>.08f||(!b.Moving&&Mathf.Abs(FormationMath.AngleDifference(b.Facing,b.DesiredFacing))>.025f);
                }
            }
            BodyContacts.Resolve(dt);
            RemoveEscapedSoldiers();
            // Reciprocal marches need a passing lane once real bodies impede them.
            // Keep physical pushing for ordinary contact and ignore empty footprints.
            foreach(var unit in Battalions)
            {
                if((unit.Position-unit.Destination).sqrMagnitude<.04f)unit.Moving=false;
                int blockerId=BodyContacts.FriendlyBlocker(unit.Id);
                if(blockerId<0||!unit.Moving||ContactMovement.Loose[unit.Id]
                    ||detourStage[unit.Id]>0||Time<nextDetourTime[unit.Id])continue;
                var blocker=Battalions[blockerId];
                if(!blocker.Moving||ContactMovement.Loose[blockerId]
                    ||Vector2.Dot((unit.Destination-unit.Position).normalized,
                        (blocker.Destination-blocker.Position).normalized)>-.5f)continue;
                TryDetour(unit,blocker);nextDetourTime[unit.Id]=Time+1f;
            }
            ContactMovement.UpdatePursuitCenters();
        }

        // AliveCount and the living roster represent troops still on the battlefield.
        // Escapes do not produce corpses, kills, morale damage, or experience rewards.
        void RemoveEscapedSoldiers()
        {
            float edge = Config.FieldHalfSize + 1f;
            foreach(var unit in Battalions)
            {
                if(!InfantryContactMovement.Fleeing(unit))continue;
                for(int slot=unit.AliveCount-1;slot>=0;slot--)
                {
                    int soldier=livingSlots[unit.Id][slot];
                    Vector2 position=Soldiers.Positions[soldier];
                    if(Mathf.Abs(position.x)<=edge&&Mathf.Abs(position.y)<=edge)continue;
                    Soldiers.Alive[soldier]=false;
                    Soldiers.Velocities[soldier]=Vector2.zero;Soldiers.MovementSpeeds[soldier]=0;
                    Soldiers.MeleeActions[soldier]=MeleeAction.None;Soldiers.MeleeTargets[soldier]=-1;
                    unit.AliveCount--;unit.EscapedCount++;
                    FillFallenSlot(unit,soldier);
                    livingSlots[unit.Id][slot]=livingSlots[unit.Id][unit.AliveCount];
                }
                if(unit.AliveCount==0)
                {
                    unit.Moving=unit.Reforming=unit.InMelee=false;unit.ActualSpeed=0;
                    unit.PendingOrders.Clear();
                }
            }
        }

        internal float MovementSpeed(BattalionState unit)
        {
            if(unit.Kind==UnitKind.Cavalry)return Mathf.Max(.1f,unit.MountedSpeed)*(1+unit.Kit.speed)*Terrain.SpeedMultiplier(unit.Position);
            bool charging=unit.AttackMode==InfantryAttackMode.BayonetCharge&&!InfantryContactMovement.Fleeing(unit)&&(!unit.InMelee||IsPursuingFugitives(unit));
            return Mathf.Max(.1f,Config.MarchSpeed)*unit.BaseStats.Speed*(1+unit.Kit.speed)*(charging?2.8f:unit.Running||InfantryContactMovement.Fleeing(unit)?1.95f:1f)
                *(IsPursuingFugitives(unit)&&Battalions[unit.AttackTargetId].Kind==UnitKind.Infantry?1.12f:1f)
                *(unit.Formation==FormationKind.Column?1.15f:unit.Formation==FormationKind.Square?.65f:1f)
                *Mathf.Lerp(1f,.65f,Mathf.Clamp01(unit.Fatigue/100f))*Terrain.SpeedMultiplier(unit.Position);
        }
        public int SoldierAtSlot(BattalionState unit,int slot) => formationOccupants[unit.SoldierStart+slot];
        // Rebuild both lookup directions from physical positions. This runs only on
        // orders or accepted pivots, never every simulation tick, and allocates nothing.
        void ReassignFormationSlots(BattalionState unit)
        {
            int count = 0, files = FormationMath.EffectiveFiles(unit);
            for (int soldier = unit.SoldierStart; soldier < unit.SoldierStart + unit.SoldierCount; soldier++)
                if (Soldiers.Alive[soldier]) reformationScratch[count++] = soldier;
            for (int slot = 0; slot < count; slot++)
                reformationTargets[slot] = unit.Position + FormationMath.SlotOffset(unit, slot);
            formationAssignment.Assign(Soldiers.Positions, reformationScratch, reformationTargets, count, reformationResult);
            for (int slot = 0; slot < unit.SoldierCount; slot++)
            {
                int soldier = slot < count ? reformationResult[slot] : -1;
                formationOccupants[unit.SoldierStart + slot] = soldier;
                if (soldier >= 0) Soldiers.FormationSlots[soldier] = slot;
            }
            PlaceSergeantsInFront(unit);
            PlaceCaptainsNearFront(unit);
            slotsDirty[unit.Id] = false;
        }

        void FillFallenSlot(BattalionState unit,int fallen)
        {
            formationOccupants[unit.SoldierStart+Soldiers.FormationSlots[fallen]]=-1;
            slotsDirty[unit.Id]=true;unit.Reforming=unit.AliveCount>0;
        }
        void CompactFormationSlots(BattalionState unit)
        {
            if (unit.Formation == FormationKind.Square || unit.Formation == FormationKind.Skirmish || unit.Formation == FormationKind.Diamond)
            { ReassignFormationSlots(unit); return; }
            int files=FormationMath.EffectiveFiles(unit);
            int frontSlots=((unit.AliveCount-1)/files)*files;
            // Keep surviving front ranks in place. Fill their holes from the nearest rear
            // soldier, then close the remaining rear rank in physical left-to-right order.
            for(int slot=0;slot<frontSlots;slot++)
            {
                if(formationOccupants[unit.SoldierStart+slot]>=0)continue;
                Vector2 target=unit.Position+FormationMath.SlotOffset(slot,unit.AliveCount,files,unit.Spacing,unit.Facing);
                int best=-1;float nearest=float.PositiveInfinity;
                // In a fighting line, close each hole from the next supporting rank.
                // Cascading those vacancies avoids sending the last rank through
                // several occupied ranks to replace a fallen front-rank soldier.
                int donorStart=ContactMovement.BattleLines[unit.Id]?(slot/files+1)*files:frontSlots;
                for(int donorSlot=donorStart;donorSlot<unit.SoldierCount;donorSlot++)
                {
                    int donor=formationOccupants[unit.SoldierStart+donorSlot];if(donor<0)continue;
                    float squared=(Soldiers.Positions[donor]-target).sqrMagnitude;
                    if(squared<nearest){nearest=squared;best=donorSlot;}
                }
                int replacement=formationOccupants[unit.SoldierStart+best];
                formationOccupants[unit.SoldierStart+slot]=replacement;Soldiers.FormationSlots[replacement]=slot;
                formationOccupants[unit.SoldierStart+best]=-1;
            }
            int rearCount=0;Vector2 right=FormationMath.Right(unit.Facing);
            for(int slot=frontSlots;slot<unit.SoldierCount;slot++)
            {
                int soldier=formationOccupants[unit.SoldierStart+slot];if(soldier<0)continue;
                int insert=rearCount;float position=Vector2.Dot(Soldiers.Positions[soldier],right);
                while(insert>0&&Vector2.Dot(Soldiers.Positions[reformationScratch[insert-1]],right)>position)
                {reformationScratch[insert]=reformationScratch[insert-1];insert--;}
                reformationScratch[insert]=soldier;rearCount++;
            }
            for(int slot=frontSlots;slot<unit.SoldierCount;slot++)formationOccupants[unit.SoldierStart+slot]=-1;
            for(int rear=0;rear<rearCount;rear++)
            {
                int soldier=reformationScratch[rear],slot=frontSlots+rear;
                formationOccupants[unit.SoldierStart+slot]=soldier;Soldiers.FormationSlots[soldier]=slot;
            }
            PlaceSergeantsInFront(unit);
            PlaceCaptainsNearFront(unit);
            slotsDirty[unit.Id]=false;
        }

        void TryBuildingDetour(BattalionState mover, float margin)
        {
            Vector2 travel = mover.Destination - mover.Position;
            if (travel.sqrMagnitude < .04f) return;
            travel.Normalize();
            Vector2 tangent = new Vector2(-travel.y, travel.x);
            float houseRadius = 0f;
            foreach (Vector2 half in Terrain.FarmHalfExtents) houseRadius = Mathf.Max(houseRadius, half.magnitude);
            if(Terrain.GatePath(mover.Position,mover.Destination,margin,out var gateFirst,out var gateSecond))
            {detourFirst[mover.Id]=gateFirst;detourSecond[mover.Id]=gateSecond;detourStage[mover.Id]=1;return;}
            float clearance = margin + houseRadius * 2f + 8f;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Vector2 offset = tangent * clearance * (attempt == 0 ? 1f : -1f);
                Vector2 first = ClampToField(mover, mover.Position + offset);
                Vector2 second = ClampToField(mover, mover.Destination + offset);
                if (Vector2.Dot(first - mover.Position, offset.normalized) < clearance * .85f
                    || Terrain.IsBlocked(first, margin) || Terrain.IsBlocked(second, margin)) continue;
                detourFirst[mover.Id] = first;
                detourSecond[mover.Id] = second;
                detourStage[mover.Id] = 1;
                return;
            }
        }

        void TryDetour(BattalionState mover, BattalionState blocker)
        {
            Vector2 travel = mover.Destination - mover.Position;
            if (travel.sqrMagnitude < .04f) return;
            travel.Normalize();
            Vector2 tangent = new Vector2(-travel.y, travel.x);
            Vector2 mh = FormationMath.HalfExtents(mover), bh = FormationMath.HalfExtents(blocker);
            float clearance = ProjectRadius(mover, mh, tangent) + ProjectRadius(blocker, bh, tangent) + 8f;
            // Opposite ID parity breaks symmetric approaches; traveling in opposite directions also
            // flips the tangent, so choose a stable world-side preference for reciprocal crossings.
            if (tangent.y < -.001f || (Mathf.Abs(tangent.y) < .001f && tangent.x < 0f)) tangent = -tangent;
            float preferred = mover.Id % 2 == 0 ? 1f : -1f;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Vector2 offset = tangent * clearance * (attempt == 0 ? preferred : -preferred);
                Vector2 first = ClampToField(mover, mover.Position + offset);
                Vector2 second = ClampToField(mover, mover.Destination + offset);
                if (Vector2.Dot(first - mover.Position, offset.normalized) < clearance * .85f) continue;
                if (Overlap(mover, first, blocker, blocker.Position) > 0f) continue;
                detourFirst[mover.Id] = first;
                detourSecond[mover.Id] = second;
                detourStage[mover.Id] = 1;
                return;
            }
        }
        static float ProjectRadius(BattalionState b, Vector2 half, Vector2 axis)
        {
            return Mathf.Abs(Vector2.Dot(axis, FormationMath.Right(b.Facing))) * half.x
                + Mathf.Abs(Vector2.Dot(axis, FormationMath.Forward(b.Facing))) * half.y;
        }

        public void ApplyCasualties(int id, int count, DeathCause cause = DeathCause.Melee, Vector3 impulse = default, Vector3 impact = default)
        {
            if (!Valid(id) || count <= 0) return;
            var b = Battalions[id];
            count = Mathf.Min(count, b.AliveCount);
            for (int c = 0; c < count; c++)
            {
                int pick = random.Next(b.AliveCount);
                if (cause == DeathCause.Shell || cause == DeathCause.Fire)
                {
                    float closest = float.PositiveInfinity;
                    for (int slot = 0; slot < b.AliveCount; slot++)
                    {
                        float distance = (Soldiers.Positions[livingSlots[id][slot]] - new Vector2(impact.x, impact.z)).sqrMagnitude;
                        if (distance < closest) { closest = distance; pick = slot; }
                    }
                }
                int index = livingSlots[id][pick];
                RecordDeath(index, cause, impulse, impact);
                Soldiers.Alive[index] = false;
                Soldiers.Health[livingSlots[id][pick]] = 0f;
                b.AliveCount--;
                FillFallenSlot(b,index);
                livingSlots[id][pick] = livingSlots[id][b.AliveCount];
            }
            if (b.AliveCount == 0) b.Moving = false;
        }
        /// <summary>Kill the specific soldier intersected by a projectile, preserving roster and corpse bookkeeping.</summary>
        // Survivors retain their living roster and slot. Normal movement takes over after recovery.
        // Persistent heat wounds without starting a blast knockdown on every pulse.
        public bool ApplyWound(int index, float damage, DeathCause cause, Vector3 impact, Vector3 direction=default)
        {
            if (index < 0 || index >= Soldiers.Alive.Length || !Soldiers.Alive[index] || !Finite(damage) || damage <= 0f) return false;
            Soldiers.Health[index] = Mathf.Max(0f, Soldiers.Health[index] - damage);
            if(Soldiers.Health[index]<=0f)return KillSoldier(index,cause,cause==DeathCause.Musket?direction:Vector3.zero,impact);
            if(cause==DeathCause.Musket)MusketWounded?.Invoke(index,direction);
            return false;
        }

        public bool ApplyArtilleryHit(int index, float damage, DeathCause cause, Vector3 impulse, Vector3 impact)
        {
            if (index < 0 || index >= Soldiers.Alive.Length || !Soldiers.Alive[index] || !Finite(damage) || damage <= 0f) return false;
            Soldiers.Health[index] = Mathf.Max(0f, Soldiers.Health[index] - damage);
            if (Soldiers.Health[index] <= 0f) return KillSoldier(index, cause, impulse, impact);
            if(Battalions[Soldiers.BattalionIds[index]].Kind==UnitKind.Cavalry)
            {
                Soldiers.MeleeActions[index]=MeleeAction.Stumble;Soldiers.MeleeStarts[index]=Time;Soldiers.MeleeEnds[index]=Time+1.1f;Soldiers.StrikeResolved[index]=true;
                Battalions[Soldiers.BattalionIds[index]].MountedSpeed*=.35f;return false;
            }
            if (damage < 4f) return false;
            // A second blast can wound or kill a downed man without restarting his throw.
            if (Soldiers.IsKnockedDown(index, Time)) return false;
            var origin = Terrain.SurfacePoint(Soldiers.Positions[index]);
            var flight = new SoldierDeath { Position = origin, Impulse = impulse };
            Soldiers.KnockdownLandingPositions[index] = BattleCasualtyView.CannonFlightPosition(flight, Terrain, 4f, out float duration);
            Soldiers.KnockdownOrigins[index] = origin; Soldiers.KnockdownImpulses[index] = impulse;
            Soldiers.KnockdownFacings[index] = Soldiers.Facings[index];
            Soldiers.KnockdownRecoveryVariants[index] = -1;
            Soldiers.KnockdownStarts[index] = Time; Soldiers.KnockdownLandings[index] = duration;
            Soldiers.KnockdownEnds[index] = Time + duration + InfantryRecoveryTiming.RiseDuration(index) + InfantryRecoveryTiming.GroundPause(index,damage);
            SoldierKnockedDown?.Invoke(index);
            Soldiers.Velocities[index] = Vector2.zero; Soldiers.MovementSpeeds[index] = 0;
            Soldiers.FireTimes[index] = -100f; Soldiers.KneelTimes[index] = float.PositiveInfinity;
            Soldiers.ReloadReadyTimes[index] = Mathf.Max(Soldiers.ReloadReadyTimes[index], Soldiers.KnockdownEnds[index]);
            Battalions[Soldiers.BattalionIds[index]].Reforming = true;
            return false;
        }

        public Vector3 SoldierPosition(int index, float time)
        {
            if (!Soldiers.IsKnockedDown(index, time)) return Terrain.SurfacePoint(Soldiers.Positions[index]);
            float age = Mathf.Max(0f, time - Soldiers.KnockdownStarts[index]);
            if (age < Soldiers.KnockdownLandings[index])
                return WeaponBallistics.PositionAt(Soldiers.KnockdownOrigins[index], Soldiers.KnockdownImpulses[index], age);
            Vector3 landed = Soldiers.KnockdownLandingPositions[index];
            return Terrain.SurfacePoint(new Vector2(landed.x,landed.z));
        }

        void StepKnockdowns()
        {
            for (int i = 0; i < Soldiers.Alive.Length; i++)
            {
                if (!Soldiers.IsKnockedDown(i, Time)) continue;
                Vector3 position = SoldierPosition(i, Time);
                Soldiers.Positions[i] = new Vector2(position.x, position.z);
                Soldiers.Velocities[i] = Vector2.zero; Soldiers.MovementSpeeds[i] = 0f;
            }
        }

        public bool KillSoldier(int index, DeathCause cause, Vector3 impulse, Vector3 impact)
        {
            if (index < 0 || index >= Soldiers.Alive.Length || !Soldiers.Alive[index]) return false;
            var unit = Battalions[Soldiers.BattalionIds[index]];
            for (int slot = 0; slot < unit.AliveCount; slot++)
            {
                if (livingSlots[unit.Id][slot] != index) continue;
                RecordDeath(index, cause, impulse, impact);
                Soldiers.Alive[index] = false;
                Soldiers.Health[index] = 0f;
                unit.AliveCount--;
                FillFallenSlot(unit, index);
                livingSlots[unit.Id][slot] = livingSlots[unit.Id][unit.AliveCount];
                if (unit.AliveCount == 0) unit.Moving = false;
                return true;
            }
            return false;
        }

        /// <summary>Sample one living defender. Hit and kill credit are applied here exactly once.</summary>
        public int ApplyHit(int battalionId, float damage, int attackerBattalionId)
        {
            if (!Valid(battalionId) || damage <= 0f || !Finite(damage)) return -1;
            var defender = Battalions[battalionId];
            if (defender.AliveCount <= 0) return -1;
            int pick = random.Next(defender.AliveCount);
            int index = livingSlots[battalionId][pick];
            Soldiers.Health[index] = Mathf.Max(0f, Soldiers.Health[index] - damage);
            bool killed = Soldiers.Health[index] <= 0f;
            Vector3 shotDirection=Valid(attackerBattalionId)?
                Terrain.SurfacePoint(Soldiers.Positions[index])-Terrain.SurfacePoint(Battalions[attackerBattalionId].Position):Vector3.forward;
            if(!killed)MusketWounded?.Invoke(index,shotDirection);
            if (killed)
            {
                RecordDeath(index, DeathCause.Musket, Valid(attackerBattalionId) ?
                    Terrain.SurfacePoint(Soldiers.Positions[index]) - Terrain.SurfacePoint(Battalions[attackerBattalionId].Position) : Vector3.forward, default);
                Soldiers.Alive[index] = false;
                defender.AliveCount--;
                FillFallenSlot(defender,index);
                livingSlots[battalionId][pick] = livingSlots[battalionId][defender.AliveCount];
                if (defender.AliveCount == 0) defender.Moving = false;
            }
            if (Valid(attackerBattalionId) && Battalions[attackerBattalionId].Side != defender.Side)
            {
                var attacker = Battalions[attackerBattalionId];
                attacker.Hits++;
                attacker.Experience += killed ? 6f : 1f;
                if (killed) attacker.Kills++;
                if (attacker.AliveCount > 0)
                {
                    int shooter = livingSlots[attackerBattalionId][random.Next(attacker.AliveCount)];
                    Soldiers.Experience[shooter] += killed ? 6f : 1f;
                    if (killed) Soldiers.Kills[shooter]++;
                }
            }
            return index;
        }

        void RecordDeath(int index, DeathCause cause, Vector3 impulse, Vector3 impact)
        {
            var unit = Battalions[Soldiers.BattalionIds[index]];
            // Real shell contacts already carry the blast's range-dependent impulse.
            // Only synthesize a throw for callers that did not provide one.
            if (cause == DeathCause.Shell && impulse.sqrMagnitude < .0001f)
            {
                Vector3 away = Terrain.SurfacePoint(Soldiers.Positions[index]) - impact;
                away.y = 0f;
                float strength = 1f - Mathf.Clamp01(away.magnitude / 15f);
                if (away.sqrMagnitude < .001f) away = new Vector3(Mathf.Sin(index * 2.4f), 0, Mathf.Cos(index * 2.4f));
                float variation = Mathf.Lerp(.88f, 1.12f, RifleFireTiming.Variation(index, 211));
                impulse = (away.normalized * Mathf.Lerp(4f, 12f, strength) + Vector3.up * Mathf.Lerp(4f, 9f, strength)) * variation;
            }
            var death = new SoldierDeath { Soldier = index, Side = unit.Side, Cause = cause, Kind=unit.Kind,
                Position = SoldierPosition(index, Time), Facing = Soldiers.Facings[index],
                Born = Time, Impulse = impulse, Velocity = new Vector3(Soldiers.Velocities[index].x,0,Soldiers.Velocities[index].y), Moving = Soldiers.MovementSpeeds[index]>.04f,
                GaitPhase = Soldiers.GaitPhases[index], LastFireTime = Soldiers.FireTimes[index], Running = unit.Running || unit.Routing,
                Charging = unit.AttackMode == InfantryAttackMode.BayonetCharge, UnitId = unit.Id,
                ReloadRemaining = Mathf.Max(0, Soldiers.ReloadReadyTimes[index]-Time), LastVolleyTime = Soldiers.FireVolleys[index] };
            ReleaseHorse(ref death);
            Deaths[index] = death;
            DeadSoldiers[DeathCount++] = index;
            SoldierDied?.Invoke(death);
        }

        public int CountAlive(ArmySide side)
        {
            int result = 0;
            foreach (var b in Battalions) if (b.Side == side) result += b.AliveCount;
            return result;
        }
        public int CountEffective(ArmySide side)
        {
            int result = 0;
            foreach (var b in Battalions) if (b.Side == side && !b.Routing) result += b.AliveCount+(b.InReserve?b.SoldierCount:0);
            return result;
        }
        bool Valid(int id) => id >= 0 && id < Battalions.Length;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public Vector2 ClampDestination(int id, Vector2 position, int files, float facing, bool lineFrontage = false)
        {
            if (!Valid(id)) return position;
            var b = Battalions[id];
            files = Mathf.Clamp(files, 1, Mathf.Max(1,b.AliveCount));
            int rows = Mathf.CeilToInt(b.AliveCount / (float)files);
            float spacing = b.Spacing;
            Vector2 half = !FormationMath.CanResizeFrontage(b) ? FormationMath.HalfExtents(b)
                : new Vector2(Mathf.Max(1f, files * spacing * .5f), Mathf.Max(1f, rows * FormationMath.RankSpacing(spacing) * .5f));
            return ClampFootprint(position, half, facing);
        }

        Vector2 ClampToField(BattalionState b, Vector2 position)
        {
            return ClampFootprint(position, FormationMath.HalfExtents(b), b.Facing);
        }
        Vector2 ClampFootprint(Vector2 position, Vector2 half, float facing)
        {
            Vector2 right = FormationMath.Right(facing), forward = FormationMath.Forward(facing);
            float marginX = Mathf.Abs(right.x) * half.x + Mathf.Abs(forward.x) * half.y;
            float marginZ = Mathf.Abs(right.y) * half.x + Mathf.Abs(forward.y) * half.y;
            float xLimit = Mathf.Max(0f, Config.FieldHalfSize - marginX - 2f);
            float zLimit = Mathf.Max(0f, Config.FieldHalfSize - marginZ - 2f);
            return new Vector2(Mathf.Clamp(position.x, -xLimit, xLimit), Mathf.Clamp(position.y, -zLimit, zLimit));
        }
        static float Overlap(BattalionState a,Vector2 aPos,BattalionState b,Vector2 bPos) => Overlap(a,aPos,a.Facing,b,bPos,b.Facing);
        static float Overlap(BattalionState a,Vector2 aPos,float aFacing,BattalionState b,Vector2 bPos,float bFacing)
        {
            Vector2 ah = FormationMath.HalfExtents(a), bh = FormationMath.HalfExtents(b);
            Vector2 delta = bPos - aPos;
            // A conservative circle rejects distant units before the oriented-box axes.
            float reach=ah.magnitude+bh.magnitude+3f;
            if(delta.sqrMagnitude>reach*reach)return -1f;
            Vector2 ar = FormationMath.Right(aFacing), af = FormationMath.Forward(aFacing);
            Vector2 br = FormationMath.Right(bFacing), bf = FormationMath.Forward(bFacing);
            float min = float.PositiveInfinity;
            min = Mathf.Min(min, AxisOverlap(ar, ar, af, ah, br, bf, bh, delta));
            min = Mathf.Min(min, AxisOverlap(af, ar, af, ah, br, bf, bh, delta));
            min = Mathf.Min(min, AxisOverlap(br, ar, af, ah, br, bf, bh, delta));
            min = Mathf.Min(min, AxisOverlap(bf, ar, af, ah, br, bf, bh, delta));
            return min;
        }
        static float AxisOverlap(Vector2 axis, Vector2 ar, Vector2 af, Vector2 ah, Vector2 br, Vector2 bf, Vector2 bh, Vector2 delta)
        {
            float radiusA = Mathf.Abs(Vector2.Dot(axis, ar)) * ah.x + Mathf.Abs(Vector2.Dot(axis, af)) * ah.y;
            float radiusB = Mathf.Abs(Vector2.Dot(axis, br)) * bh.x + Mathf.Abs(Vector2.Dot(axis, bf)) * bh.y;
            return radiusA + radiusB + 2f - Mathf.Abs(Vector2.Dot(delta, axis));
        }
    }
}
