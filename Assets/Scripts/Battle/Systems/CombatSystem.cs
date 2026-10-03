using Unity.Profiling;
using System;
using UnityEngine;

namespace EmpireBattle
{
    public sealed class CombatShot
    {
        public int Sequence;
        public Vector3 Start, End;
        public float Time, Duration;
        public bool Hit, Killed, Grounded;
        public int TargetSoldier = -1;
        public int TargetHorse = -1;
    }

    /// <summary>Formation fire scheduling with individual ballistic soldier contacts.</summary>
    public sealed partial class CombatSystem
    {
        static readonly ProfilerMarker PerformanceMarker = new ProfilerMarker("Battle.Combat");
        private readonly BattleWorld world;
        private readonly EmpireBattle.Art.InfantryCrowdLibrary crowdLibrary;
        private readonly System.Random random;
        public CombatShot[] Shots { get; }
        public int ShotSequence { get; private set; }
        private readonly int[] pendingTargets, pendingAttackers, pendingHorses;
        private readonly float[] pendingDamage, pendingMoraleImpact, moraleCasualtyBonus;
        private readonly bool[] pendingVisual;
        private readonly int[] initialAlive, volleyShooters, missedShooters, pendingShooters;
        private int pendingCount;
        public MeleeSystem Melee {get;}
        public GrenadeSystem Grenades {get;}
        private readonly float[] lastThreatTime;
        private readonly bool[] threatened;
        // One retained target per firing wall (ordinary formations use wall zero).
        private readonly int[,] firingTargets;

        public CombatSystem(BattleWorld world)
        {
            this.world = world;
            crowdLibrary=world.Config.UseDetailedInfantry?Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary"):null;
            random = new System.Random(world.Config.Seed ^ 7127);
            int count = world.Battalions.Length;
            firingTargets = new int[count,4];
            int capacity = Mathf.Max(128, world.Config.TotalSoldiers * 2);
            pendingTargets = new int[capacity]; pendingAttackers = new int[capacity]; pendingHorses = new int[capacity];
            pendingDamage = new float[capacity]; pendingVisual = new bool[capacity];
            pendingMoraleImpact=new float[capacity];moraleCasualtyBonus=new float[count];
            initialAlive = new int[count];
            volleyShooters = new int[world.Config.SoldiersPerBattalion];
            missedShooters = new int[world.Config.SoldiersPerBattalion];
            pendingShooters = new int[capacity];
            Shots = new CombatShot[2048];
            for (int i = 0; i < Shots.Length; i++) Shots[i] = new CombatShot();
            Melee = new MeleeSystem(world);Grenades=new GrenadeSystem(world);
            captainFireTargets=new int[world.Soldiers.Alive.Length];
            lastThreatTime = new float[count];
            threatened = new bool[count];
            for (int i = 0; i < count; i++) lastThreatTime[i] = -100f;
        }

        public void Step(float dt)
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Combat);
            using var performanceScope = PerformanceMarker.Auto();
            if (dt <= 0f || !world.CanSimulate) return;
            pendingCount = 0;
            for (int i = 0; i < initialAlive.Length; i++) initialAlive[i] = world.Battalions[i].AliveCount;
            Array.Clear(threatened, 0, threatened.Length);
            Array.Clear(moraleCasualtyBonus,0,moraleCasualtyBonus.Length);
            BattalionState[] units = world.Battalions;
            Melee.Step(dt,threatened);Grenades.Step(dt);
            StepCaptains();StepMountedFire(dt);
            for (int i = 0; i < units.Length; i++)
            {
                BattalionState unit = units[i];
                if(unit.Kind==UnitKind.Cavalry)continue;
                unit.ReloadRemaining = Mathf.Max(0f, unit.ReloadRemaining - dt);
                if (unit.AliveCount <= 0 || unit.Routing) { RifleFireTiming.Interrupt(world, unit); continue; }

                if((unit.InMelee&&unit.Formation!=FormationKind.Square)||!UnitRoster.Shoots(unit.Role)){RifleFireTiming.Interrupt(world,unit);continue;}
                if (!unit.FireAtWill || unit.Moving || unit.Withdrawing)
                { RifleFireTiming.Interrupt(world, unit); continue; }
                if (RifleFireTiming.IndependentFire(unit)) { FireIndependently(unit); continue; }
                if (!RifleFireTiming.Ready(world, unit)) continue;
                BattalionState target = SelectFireTarget(unit,0,unit.Facing);
                var horse = target == null ? FindLooseHorse(unit, unit.Facing) : null;
                if (target == null && horse == null) continue;
                Vector2 targetPosition = horse != null ? horse.Position : target.Position;
                if (FriendlyBlocksFire(unit, targetPosition)) continue;
                Vector3 origin = world.Terrain.SurfacePoint(unit.Position, 1.5f);
                Vector3 aimPoint = world.Terrain.SurfacePoint(targetPosition, 1.3f);
                if (!WeaponBallistics.TrySolve(origin, aimPoint, out Vector3 velocity, out float duration,world.AimedRange(unit))
                    || WeaponBallistics.TraceGround(world.Terrain, origin, velocity, duration, out _)) continue;
                float distance = Vector2.Distance(unit.Position, targetPosition);
                float nominal = Mathf.Max(1f, world.EffectiveRange(unit));
                int shooters = RifleFireTiming.ScheduleNextGroup(world, unit, volleyShooters, crowdLibrary ? crowdLibrary.Resolve(unit) : null,target,horse?.Position);
                if (shooters == 0) continue;
                float accuracy = WeaponBallistics.MusketHitChance(distance, nominal, unit.Cohesion,
                    unit.Fatigue, unit.Experience / Mathf.Max(1f, unit.SoldierCount),target?.Kind??UnitKind.Cavalry) * world.Precision(unit) * (target != null ? FormationMath.FireExposure(target) : 1f);
                int visibleHits = 0, hitCount = 0, missedCount = 0;
                for (int shot = 0; shot < shooters; shot++)
                {
                    if (random.NextDouble() >= accuracy) { missedShooters[missedCount++] = volleyShooters[shot]; continue; }
                    QueueHit(target != null ? target.Id : -1, unit.Id, WeaponBallistics.SmallArmsDamage(distance,nominal,(float)random.NextDouble(),unit.Weapon?.Id,target?.Kind??UnitKind.Cavalry), visibleHits < 20, volleyShooters[shot], horse != null ? horse.Rider : -1);
                    visibleHits++; hitCount++;
                }
                int misses = Mathf.Min(24 - Mathf.Min(20, visibleHits), shooters - hitCount);
                for (int miss = 0; miss < missedCount; miss++)
                {
                    int shooter = missedShooters[miss];
                    Vector3 individualAim=world.Soldiers.FireAimPoints[shooter];
                    Vector2 aimPosition=new Vector2(individualAim.x,individualAim.z);
                    float error = WeaponBallistics.Dispersion(distance, nominal, .018f, .16f) * distance / world.Precision(unit);
                    Vector2 forward = (aimPosition - world.Soldiers.Positions[shooter]).normalized;
                    Vector2 right = new Vector2(forward.y, -forward.x);
                    Vector2 spread = aimPosition + right * ((float)(random.NextDouble() - random.NextDouble()) * error)
                        + forward * ((float)(random.NextDouble() - random.NextDouble()) * error * .65f);
                    Vector3 missOrigin = world.Soldiers.FireOrigins[shooter];
                    Vector3 missEnd = new Vector3(spread.x,individualAim.y,spread.y);
                    if (WeaponBallistics.TrySolve(missOrigin, missEnd, out Vector3 missVelocity, out float missTime,world.AimedRange(unit)))
                    {
                        bool grounded = WeaponBallistics.TraceGround(world.Terrain, missOrigin, missVelocity, 2f, out Vector3 groundEnd);
                        LaunchBullet(missOrigin,groundEnd,WeaponBallistics.SmallArmsDamage(distance,nominal,(float)random.NextDouble(),unit.Weapon?.Id,target?.Kind??UnitKind.Cavalry),unit.Id,shooter,world.Soldiers.FireTimes[shooter],miss<misses,1f,grounded);
                    }
                }
                if (target != null)
                {
                    threatened[target.Id] = true;
                    target.LastIncomingFireTime = world.Time + RifleFireTiming.LatestShotDelay;
                }
                unit.ShotsFired += shooters;
            }

            // Launch every scheduled bullet; body contacts resolve during flight.
            for (int h = 0; h < pendingCount; h++)
            {
                if (pendingHorses[h] >= 0)
                {
                    ResolveHorseHit(h);
                    continue;
                }
                int shooter = pendingShooters[h];
                Vector3 origin = world.Soldiers.FireOrigins[shooter];
                int target = world.Soldiers.Captains[shooter] ? captainFireTargets[shooter]
                    : world.Soldiers.FireTargetSoldiers[shooter];
                if(target<0)continue;
                Vector3 endpoint = world.Soldiers.Captains[shooter]?InfantryTargeting.AimPoint(world,target):world.Soldiers.FireAimPoints[shooter];
                LaunchBullet(origin,endpoint,pendingDamage[h],pendingAttackers[h],shooter,
                    world.Soldiers.FireTimes[shooter],pendingVisual[h],pendingMoraleImpact[h]);
            }
            StepBullets();
            for (int i = 0; i < units.Length; i++)
            {
                BattalionState unit = units[i];
                int losses = initialAlive[i] - unit.AliveCount;
                if (losses > 0)
                {
                    unit.Morale -= 140f * (losses+moraleCasualtyBonus[i]) / Mathf.Max(1, unit.SoldierCount) / world.Resolve(unit);
                    unit.Cohesion -= 1.5f * losses / Mathf.Max(1, unit.SoldierCount);
                    unit.MoraleReason = "Taking casualties";
                }
                UpdateCondition(unit, losses, dt);
            }
            CheckOutcome();
        }

        bool ClearSkirmishLane(BattalionState unit, int shooter, Vector2 target)
        {
            Vector2 start=world.Soldiers.Positions[shooter], path=target-start;
            float length=path.magnitude;if(length<.01f)return false;
            Vector2 forward=path/length, right=new Vector2(forward.y,-forward.x);
            float shooterHeight=RifleFireTiming.MuzzleHeight(RifleFireTiming.SkirmishFiringStance(shooter));
            float originY=world.Terrain.SurfacePoint(start,shooterHeight).y;
            float targetY=world.Terrain.SurfacePoint(target,1.2f).y;
            for(int slot=0;slot<unit.AliveCount;slot++)
            {
                int other=world.SoldierAtSlot(unit,slot);if(other<0 || other==shooter)continue;
                Vector2 delta=world.Soldiers.Positions[other]-start;
                float along=Vector2.Dot(delta,forward);
                if(along<=.2f||along>=length||Mathf.Abs(Vector2.Dot(delta,right))>=.35f)continue;
                float otherHeight=RifleFireTiming.MuzzleHeight(RifleFireTiming.Stance(world,unit,other,world.Time))+.2f;
                float rayY=Mathf.Lerp(originY,targetY,along/length);
                if(world.Terrain.SurfacePoint(world.Soldiers.Positions[other],otherHeight).y>=rayY-.08f)return false;
            }
            return true;
        }

        void FireIndependently(BattalionState unit)
        {
            // Individual readiness keeps unfinished slots from firing without silencing settled walls.
            var appearance = crowdLibrary ? crowdLibrary.Resolve(unit) : null;
            int sides = unit.Formation == FormationKind.Square ? 4 : 1;
            for (int side = 0; side < sides; side++)
            {
                float facing = unit.Facing + side * Mathf.PI * .5f;
                BattalionState target = SelectFireTarget(unit,side,facing);
                var horse = target == null ? FindLooseHorse(unit, facing) : null;
                if (target == null && horse == null) continue;
                Vector2 targetPosition = horse != null ? horse.Position : target.Position;
                if (FriendlyBlocksFire(unit,targetPosition)) continue;
                for (int slot = 0; slot < unit.AliveCount; slot++)
                {
                    if (sides == 4 && FormationMath.SquareSide(slot,unit.AliveCount) != side) continue;
                    // The kneeling outer rank fires on the approach, then owns the bayonet wall at contact.
                    if(sides==4&&unit.InMelee&&FormationMath.IsBayonetWall(unit,slot))continue;
                    int soldier = world.SoldierAtSlot(unit,slot);
                    if (soldier < 0) continue;
                    if(world.Soldiers.MeleeEnds[soldier]>world.Time&&MeleeSystem.IsStrike(world.Soldiers.MeleeActions[soldier]))continue;
                    if (world.Soldiers.ReloadReadyTimes[soldier] > world.Time || !RifleFireTiming.SoldierReady(world,unit,soldier)) continue;
                    var stance = unit.Formation == FormationKind.Skirmish ? RifleFireTiming.SkirmishFiringStance(soldier)
                        : RifleFireTiming.IsKneeling(world,unit,soldier,world.Time) ? InfantryFiringStance.Kneeling : InfantryFiringStance.Standing;
                    Vector3 origin = world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier],RifleFireTiming.MuzzleHeight(stance));
                    int targetSoldier=target!=null?InfantryTargeting.Select(world,unit,soldier,target):-1;
                    if(target!=null&&targetSoldier<0)continue;
                    Vector3 end = targetSoldier>=0?InfantryTargeting.AimPoint(world,targetSoldier):world.Terrain.SurfacePoint(targetPosition,1.2f);
                    Vector2 individualTarget=new Vector2(end.x,end.z);
                    if (!WeaponBallistics.TrySolve(origin,end,out var velocity,out float duration,world.AimedRange(unit))
                        || WeaponBallistics.TraceGround(world.Terrain,origin,velocity,duration,out _)) continue;
                    if (!ExplicitFireOrder(unit) && unit.Formation == FormationKind.Skirmish && !ClearSkirmishLane(unit,soldier,individualTarget)) continue;
                    if (!RifleFireTiming.ScheduleIndependent(world,unit,soldier,individualTarget,appearance,targetSoldier)) continue;
                    float distance = Vector2.Distance(world.Soldiers.Positions[soldier],individualTarget);
                    float accuracy = WeaponBallistics.MusketHitChance(distance,world.EffectiveRange(unit),unit.Cohesion,unit.Fatigue,
                        unit.Experience/Mathf.Max(1,unit.SoldierCount),target?.Kind??UnitKind.Cavalry) * world.Precision(unit) * (target != null ? FormationMath.FireExposure(target) : 1f);
                    if (random.NextDouble() < accuracy) QueueHit(target != null ? target.Id : -1,unit.Id,WeaponBallistics.SmallArmsDamage(distance,world.EffectiveRange(unit),(float)random.NextDouble(),unit.Weapon?.Id,target?.Kind??UnitKind.Cavalry),true,soldier,horse != null ? horse.Rider : -1);
                    else
                    {
                        float spread = WeaponBallistics.Dispersion(distance,world.EffectiveRange(unit),.018f,.16f)*distance/world.Precision(unit);
                        end += new Vector3((float)(random.NextDouble()-.5)*spread,0,(float)(random.NextDouble()-.5)*spread);
                        origin = world.Soldiers.FireOrigins[soldier];
                        if (WeaponBallistics.TrySolve(origin,end,out velocity,out duration,world.AimedRange(unit)))
                            LaunchBullet(origin,end,WeaponBallistics.SmallArmsDamage(distance,world.EffectiveRange(unit),(float)random.NextDouble(),unit.Weapon?.Id,target?.Kind??UnitKind.Cavalry),unit.Id,soldier,world.Soldiers.FireTimes[soldier],true);
                    }
                    if(target != null)threatened[target.Id] = true;
                    unit.ShotsFired++;
                }
            }
            int rounds = 0; float ready = 0;
            for (int slot = 0; slot < unit.AliveCount; slot++)
            {
                int soldier = world.SoldierAtSlot(unit,slot);
                if (soldier < 0) continue;
                if (FormationMath.IsFiringSlot(unit,slot)) rounds = Mathf.Max(rounds,world.Soldiers.RoundsRemaining[soldier]);
                ready = Mathf.Max(ready,world.Soldiers.ReloadReadyTimes[soldier]-world.Time);
            }
            unit.Ammunition = rounds; unit.ReloadRemaining = Mathf.Max(0,ready);
        }

        private void QueueHit(int target, int attacker, float damage, bool visual, int shooter = -1, int horse = -1)
        {
            if (pendingCount >= pendingTargets.Length) return;
            pendingTargets[pendingCount] = target; pendingAttackers[pendingCount] = attacker;
            pendingDamage[pendingCount] = damage; pendingVisual[pendingCount] = visual;
            pendingShooters[pendingCount] = shooter; pendingHorses[pendingCount] = horse;
            pendingMoraleImpact[pendingCount]=shooter>=0&&!world.Soldiers.Captains[shooter]
                ?RifleFireTiming.MoraleImpact(world.Battalions[attacker]):1f;
            pendingCount++;
        }
        private void EmitShot(Vector3 start, Vector3 end, float duration, bool hit, int soldier, bool killed, float time, bool grounded, int horse = -1)
        {
            int sequence = ++ShotSequence;
            var shot = Shots[(sequence - 1) % Shots.Length];
            shot.Sequence = sequence; shot.Start = start; shot.End = end;
            shot.Time = time; shot.Duration = Mathf.Max(.01f, duration);
            shot.Hit = hit; shot.TargetSoldier = soldier; shot.TargetHorse = horse; shot.Killed = killed; shot.Grounded = grounded;
        }

        private void UpdateCondition(BattalionState unit, int losses, float dt)
        {
            if (unit.AliveCount <= 0) { unit.Morale = 0f; unit.MoraleReason = "Destroyed"; return; }
            float fatigueRate=world.ContactMovement.Pursuing[unit.Id]||world.ContactMovement.Gathering[unit.Id]
                ?world.ContactMovement.PursuitFatigueRate(unit)
                :(unit.Moving||unit.Reforming)?(unit.Running||unit.Routing?.45f:.15f)*world.FatigueRate(unit):-.25f/world.FatigueRate(unit);
            unit.Fatigue = Mathf.Clamp(unit.Fatigue + fatigueRate * dt, 0f, 100f);
            if (threatened[unit.Id]) lastThreatTime[unit.Id] = world.Time;
            if(unit.Progression.Has(RegimentAbility.FieldMedicine)&&!unit.Moving&&!unit.InMelee&&!unit.Routing&&!threatened[unit.Id]&&world.Time-Mathf.Max(lastThreatTime[unit.Id],unit.LastIncomingFireTime)>20)
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)if(world.Soldiers.Alive[i])world.Soldiers.Health[i]=Mathf.Min(unit.MaxHealth,world.Soldiers.Health[i]+dt*unit.Progression.AbilityRank(RegimentAbility.FieldMedicine));
            float surviving = unit.AliveCount / (float)Mathf.Max(1, unit.SoldierCount);
            float moraleCeiling = unit.MaxMorale + unit.Kit.moraleReserve - (1f - surviving) * 75f / world.Resolve(unit);
            if (unit.Routing)
            {
                unit.MoraleReason = "Routing — heavy losses or morale collapse";
                unit.Morale = Mathf.Max(0f, unit.Morale - dt);
                return; // Rallying is deliberately deferred for this first battle.
            }
            if (threatened[unit.Id])
            {
                unit.Morale -= 0.18f * dt / world.Resolve(unit);
                if (losses == 0) unit.MoraleReason = "Under enemy fire or in melee";
            }
            else if (world.Time - lastThreatTime[unit.Id] > 12f)
            {
                unit.Morale = Mathf.Min(moraleCeiling, unit.Morale + 0.35f * world.Resolve(unit) * dt);
                unit.MoraleReason = unit.Moving ? "Marching" : "Steady";
            }
            unit.Cohesion = Mathf.Clamp(unit.Cohesion + ((unit.Moving||unit.Reforming) ? -0.002f*(MilitaryEquipment.Has(unit.Doctrine,MilitaryDoctrine.Maneuver)?.9f:1f) : 0.015f*(1+unit.Kit.cohesion)) * dt, 0.3f, 1f);
            unit.Morale = Mathf.Clamp(unit.Morale, 0f, moraleCeiling);
            if (unit.Morale <= 25f / world.Resolve(unit) || surviving <= (unit.Role==UnitRole.Militia?.4f:.3f))
            {
                if(world.ResistRout(unit))return;
                unit.Routing = true;
                unit.FireAtWill = false;
                unit.MoraleReason = "Routing — heavy losses or morale collapse";
                world.Withdraw(unit.Id);
            }
            else if (unit.Morale < 45f) unit.MoraleReason = "Wavering — casualties and pressure";
            else if (unit.Fatigue > 75f && !threatened[unit.Id]) unit.MoraleReason = "Exhausted";
        }

        private bool CanFireAt(BattalionState unit, BattalionState target)
        {
            Vector2 delta = target.Position - unit.Position;
            return target.Side != unit.Side && target.AliveCount > 0 && !target.Routing
                && delta.sqrMagnitude <= world.AimedRange(unit) * world.AimedRange(unit)
                && (unit.Formation == FormationKind.Square || delta.sqrMagnitude < .01f || Vector2.Dot(FormationMath.Forward(unit.Facing), delta.normalized) >= WeaponBallistics.MinimumFacingDot)
                && !world.Terrain.BlocksLine(unit.Position, target.Position);
        }

        private BattalionState SelectFireTarget(BattalionState unit,int side,float facing)
        {
            bool Eligible(BattalionState target)
            {
                if(target==null||!CanFireAt(unit,target))return false;
                Vector2 delta=target.Position-unit.Position;
                return delta.sqrMagnitude<.01f||Vector2.Dot(FormationMath.Forward(facing),delta.normalized)
                    >=(unit.Formation==FormationKind.Square?.707f:WeaponBallistics.MinimumFacingDot);
            }
            BattalionState target=unit.AttackMode==InfantryAttackMode.FirePosition
                &&unit.AttackTargetId>=0&&unit.AttackTargetId<world.Battalions.Length
                ?world.Battalions[unit.AttackTargetId]:null;
            if(!Eligible(target))
            {
                int retained=firingTargets[unit.Id,side]-1;
                target=retained>=0?world.Battalions[retained]:null;
                if(!Eligible(target))target=FindEnemy(unit,world.AimedRange(unit),true,facing);
            }
            firingTargets[unit.Id,side]=target==null?0:target.Id+1;
            return target;
        }

        private BattalionState FindEnemy(BattalionState unit, float range, bool requireFacing, float? firingFacing = null)
        {
            BattalionState best = null;
            float bestDistanceSquared = range * range;
            Vector2 forward = FormationMath.Forward(firingFacing ?? unit.Facing);
            foreach (BattalionState other in world.Battalions)
            {
                if (other.Side == unit.Side || other.AliveCount <= 0 || (requireFacing && other.Routing)) continue;
                Vector2 delta = other.Position - unit.Position;
                float squared = delta.sqrMagnitude;
                if (!requireFacing && squared > .01f)
                {
                    Vector2 axis = delta.normalized;
                    Vector2 a = FormationMath.HalfExtents(unit), b = FormationMath.HalfExtents(other);
                    float reach = Mathf.Abs(Vector2.Dot(axis,FormationMath.Right(unit.Facing)))*a.x + Mathf.Abs(Vector2.Dot(axis,FormationMath.Forward(unit.Facing)))*a.y
                        + Mathf.Abs(Vector2.Dot(axis,FormationMath.Right(other.Facing)))*b.x + Mathf.Abs(Vector2.Dot(axis,FormationMath.Forward(other.Facing)))*b.y;
                    float gap = Mathf.Max(0,Mathf.Sqrt(squared)-reach);
                    squared = gap*gap;
                }
                if (squared > bestDistanceSquared) continue;
                // A 70-degree firing arc requires players to face the enemy.
                if (requireFacing && squared > 0.01f && Vector2.Dot(forward, delta.normalized) < (unit.Formation == FormationKind.Square ? .707f : WeaponBallistics.MinimumFacingDot)) continue;
                if (requireFacing && world.Terrain.BlocksLine(unit.Position, other.Position)) continue;
                best = other;
                bestDistanceSquared = squared;
            }
            return best;
        }

        private bool FriendlyBlocksFire(BattalionState shooter, Vector2 targetPosition)
        {
            if(ExplicitFireOrder(shooter))return false;
            Vector2 segment=targetPosition-shooter.Position;
            float length=segment.magnitude;
            if(length<1f)return false;
            Vector2 forward=segment/length,right=new Vector2(forward.y,-forward.x);
            float frontage=FormationMath.HalfExtents(shooter).x;
            float spread=WeaponBallistics.Dispersion(length,world.EffectiveRange(shooter),.018f,.16f)*length/world.Precision(shooter);
            foreach(var friendly in world.Battalions)
            {
                if(friendly.Id==shooter.Id||friendly.Side!=shooter.Side||friendly.AliveCount<=0)continue;
                for(int i=friendly.SoldierStart;i<friendly.SoldierStart+friendly.SoldierCount;i++)
                {
                    if(!world.Soldiers.Alive[i])continue;
                    Vector2 delta=world.Soldiers.Positions[i]-shooter.Position;
                    float along=Vector2.Dot(delta,forward);
                    if(along<=.25f||along>=length+spread)continue;
                    float lane=frontage*Mathf.Clamp01(1f-along/length)+spread*Mathf.Clamp01(along/length)+.6f;
                    if(Mathf.Abs(Vector2.Dot(delta,right))<lane)return true;
                }
            }
            return false;
        }
        static bool ExplicitFireOrder(BattalionState unit)
            => unit.AttackMode==InfantryAttackMode.FirePosition&&unit.AttackTargetId>=0;

        private void CheckOutcome()
        {
            if(world.Outcome!=BattleOutcome.Ongoing)return;
            int french = world.CountEffective(ArmySide.French);
            int british = world.CountEffective(ArmySide.British);
            if (french <= 0 && british <= 0) world.Outcome = BattleOutcome.Draw;
            else if (french <= 0 && !(world.Siege!=null&&world.Siege.Attacker==ArmySide.British)) world.Outcome = BattleOutcome.BritishVictory;
            else if (british <= 0 && !(world.Siege!=null&&world.Siege.Attacker==ArmySide.French)) world.Outcome = BattleOutcome.FrenchVictory;
            if(world.Outcome!=BattleOutcome.Ongoing)world.PursuitUntil=world.Time+20f;
        }
    }
}
