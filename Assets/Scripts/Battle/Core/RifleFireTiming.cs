using UnityEngine;

namespace EmpireBattle
{
    public enum InfantryFiringStance { Standing, Kneeling }
    // Deterministic presentation timing, separate from formation-level hit resolution.
    public static class RifleFireTiming
    {
        public const float SoldierTurnRate = 4.2f;
        public const float LatestShotDelay = .4f;
        public const float KneelDelay = .45f;
        public const float LowerDuration = .26f;
        public const float RaiseDuration = .3f;
        public const float ReloadActionDuration = 4.5f;
        public static float ReloadActionPhase(float elapsed,int soldier) =>
            Mathf.Clamp01(elapsed/(ReloadActionDuration*Mathf.Lerp(.94f,1.06f,Variation(soldier,23))));
        public static float Variation(int soldier, int salt = 0)
        {
            unchecked
            {
                uint value = (uint)(soldier * 747796405 + salt * 2891336453L);
                value = ((value >> (int)((value >> 28) + 4)) ^ value) * 277803737u;
                value = (value >> 22) ^ value;
                return (value & 65535) / 65535f;
            }
        }
        public static float Delay(int soldier, int unit, float volley)
        {
            int salt = unit * 31 + Mathf.RoundToInt(volley * 20f);
            // A main burst with a few slower responses, rather than a uniform firing line.
            float response = Variation(soldier, salt);
            return response * response * LatestShotDelay;
        }

        public static float GroupInterval(BattleWorld world, BattalionState unit, bool nextRank)
        {
            int salt = unit.Id * 97 + unit.FireGroup * 31 + Mathf.RoundToInt(unit.LastVolleyTime * 20f);
            float variation = Variation(unit.Id, salt);
            float interval = nextRank ? world.Config.RankFireInterval : world.Config.PlatoonFireInterval;
            // Different pauses on every handoff; stable under replay and independent of render frames.
            return Mathf.Max(nextRank ? LatestShotDelay + RaiseDuration + KneelDelay + .35f : LatestShotDelay + RaiseDuration + .1f,
                interval * Mathf.Lerp(nextRank ? .75f : .65f, nextRank ? 1.35f : 1.45f, variation));
        }

        public static float ReloadDuration(BattleWorld world, BattalionState unit) =>
            Mathf.Max(1f, world.BaseReload(unit)) * (1f + unit.Fatigue * .004f) * CadenceMultiplier(unit);

        public static InfantryFireMode NormalizeMode(InfantryFireMode mode)=>mode==InfantryFireMode.ByRank?InfantryFireMode.ByPlatoon:mode;
        public static InfantryFireMode EffectiveMode(BattalionState unit)=>unit.Kind==UnitKind.Cavalry||unit.FireMode==InfantryFireMode.FireAtWill||unit.Formation==FormationKind.Skirmish||unit.Formation==FormationKind.Square
            ?InfantryFireMode.FireAtWill:NormalizeMode(unit.FireMode);
        public static bool IndependentFire(BattalionState unit) => EffectiveMode(unit)==InfantryFireMode.FireAtWill;
        // Independent soldiers work at their own pace; controlled volleys take longer
        // to prepare. Damage and accuracy of each bullet remain identical across drills.
        public static float CadenceMultiplier(BattalionState unit)=>.9f*(EffectiveMode(unit)==InfantryFireMode.FireAtWill?.8f:EffectiveMode(unit)==InfantryFireMode.Volley?1.2f:1f);
        public static float MoraleImpact(BattalionState unit)=>EffectiveMode(unit)==InfantryFireMode.Volley?2.5f:EffectiveMode(unit)==InfantryFireMode.ByPlatoon?1.5f:1f;
        // Most rise to fire; a stable minority keep firing from one knee.
        public static InfantryFiringStance SkirmishFiringStance(int soldier) => Variation(soldier,137)<.25f
            ? InfantryFiringStance.Kneeling : InfantryFiringStance.Standing;
        // Skirmishers wait and reload on one knee; only a man's own aiming
        // window brings him up. No formation-wide posture or reload clock.
        public static InfantryFiringStance Stance(BattleWorld world, BattalionState unit, int soldier, float now)
        {
            return IsKneeling(world,unit,soldier,now) ? InfantryFiringStance.Kneeling : InfantryFiringStance.Standing;
        }
        public static float MuzzleHeight(InfantryFiringStance stance) => stance == InfantryFiringStance.Kneeling ? 1.02f : 1.45f;
        static float AimFacing(Vector2 start,Vector2 target,float muzzleX)
        {
            Vector2 delta=target-start;
            // The musket is offset from the soldier's root. Aim its barrel line, not the root.
            return FormationMath.FacingOf(delta)-Mathf.Asin(Mathf.Clamp(muzzleX/Mathf.Max(.1f,delta.magnitude),-.95f,.95f));
        }
        public static bool IsKneeling(BattleWorld world, BattalionState unit, int soldier, float now)
        {
            if (world.Soldiers.Captains[soldier]&&now>=world.Soldiers.FireTimes[soldier]&&now-world.Soldiers.FireTimes[soldier]<.75f) return false;
            if (unit.Moving || unit.Routing || unit.Withdrawing || world.Soldiers.MovementSpeeds[soldier] > .04f) return false;
            if (unit.Formation == FormationKind.Skirmish)
            {
                if (unit.Reforming || !unit.FireAtWill || unit.InMelee) return false;
                if (world.Soldiers.Captains[soldier] || world.Soldiers.PikeSergeants[soldier]) return false;
                float shot = world.Soldiers.FireTimes[soldier];
                // A queued shot owns its raise/aim/recoil interval. Completed shots
                // return to the default kneeling posture throughout reload and idle.
                return SkirmishFiringStance(soldier)==InfantryFiringStance.Kneeling
                    || shot < 0 || now < world.Soldiers.FireVolleys[soldier]
                    || now >= shot+KneelDelay;
            }
            if (unit.Formation == FormationKind.Square)
                return FormationMath.IsBayonetWall(unit, world.Soldiers.FormationSlots[soldier])&&SoldierReady(world,unit,soldier);
            if(unit.Formation==FormationKind.Line&&unit.FireMode==InfantryFireMode.FireAtWill)return unit.FireAtWill&&!unit.InMelee&&world.Soldiers.FormationSlots[soldier]<FormationMath.EffectiveFiles(unit);
            return unit.Formation == FormationKind.Line && unit.FireAtWill && !unit.InMelee
                && (unit.FireCycleActive || now < unit.FireCycleReadyTime + .15f + .65f * Variation(soldier, 41))
                && now >= world.Soldiers.KneelTimes[soldier];
        }

        // Stop a drill on movement or orders. Already fired muskets must still reload.
        public static void Interrupt(BattleWorld world, BattalionState unit)
        {
            unit.FireCycleActive = false;
            // Individual reload clocks own spent cartridges. A fresh order can
            // immediately use loaded men instead of inheriting the slowest reload.
            unit.NextFireTime = world.Time;
        }

        public static bool Ready(BattleWorld world, BattalionState unit) =>
            world.Time >= unit.NextFireTime && (unit.FireCycleActive || unit.Ammunition > 0);

        public static int ScheduleNextGroup(BattleWorld world, BattalionState unit, int[] shooters,
            EmpireBattle.Art.InfantryCrowdSet appearance = null, BattalionState target = null, Vector2? fixedTarget = null)
        {
            unit.FireMode=NormalizeMode(unit.FireMode);
            if (unit.FireCycleActive && (unit.FireCycleFiles != FormationMath.EffectiveFiles(unit) || unit.FireCycleMode != unit.FireMode))
                Interrupt(world, unit);
            if (!Ready(world, unit)) return 0;
            if (!unit.FireCycleActive)
            {
                unit.FireCycleActive = true;
                unit.FireGroup = 0;
                unit.FireCycleFiles = FormationMath.EffectiveFiles(unit);
                unit.FireCycleRanks = unit.Formation == FormationKind.Line && unit.FireMode!=InfantryFireMode.Volley
                    ? Mathf.CeilToInt(FormationMath.FiringSoldierCount(unit) / (float)unit.FireCycleFiles) : 1;
                unit.FireCycleMode = unit.FireMode;
                unit.FireCyclePlatoons = unit.Formation == FormationKind.Line && unit.FireMode == InfantryFireMode.ByPlatoon
                    ? Mathf.CeilToInt(unit.FireCycleFiles / (float)Mathf.Max(1, world.Config.PlatoonFiles)) : 1;
                unit.FireCycleReadyTime = world.Time;
                unit.FireCycleSpentRound = false;
                // Men still waiting in rear groups stay down; only the group about to
                // discharge rises, through its new per-soldier KneelTimes below.
            }
            // Platoons sweep each rank in turn; rank firing is part of this drill,
            // not a separate player-selectable mode.
            int rank = unit.FireGroup / unit.FireCyclePlatoons;
            int platoon = unit.FireGroup % unit.FireCyclePlatoons;
            int firstFile = platoon * unit.FireCycleFiles / unit.FireCyclePlatoons;
            int lastFile = (platoon + 1) * unit.FireCycleFiles / unit.FireCyclePlatoons;
            // Replacement soldiers also lower their heads before the rear ranks fire.
            for (int slot = 0; slot < Mathf.Min(unit.AliveCount, rank * unit.FireCycleFiles); slot++)
            {
                int soldier = world.SoldierAtSlot(unit, slot);
                if (soldier < 0) continue; // Casualties leave holes until the next movement tick.
                world.Soldiers.KneelTimes[soldier] = Mathf.Min(world.Time, world.Soldiers.KneelTimes[soldier]);
            }
            int start = rank * unit.FireCycleFiles + firstFile;
            int end = unit.Formation == FormationKind.Line && unit.FireMode!=InfantryFireMode.Volley
                ? Mathf.Min(unit.AliveCount, rank * unit.FireCycleFiles + lastFile) : FormationMath.FiringSoldierCount(unit);
            int count = ScheduleSlots(world, unit, shooters, start, end, appearance, true, target, fixedTarget);
            if (count > 0 && !unit.FireCycleSpentRound) { unit.Ammunition--; unit.FireCycleSpentRound = true; }
            unit.FireGroup++;
            if (unit.FireGroup >= unit.FireCycleRanks * unit.FireCyclePlatoons)
            {
                unit.FireCycleActive = false;
                world.RefreshAmmunition(unit);
                // A wider volley needs more time to dress and coordinate the entire line.
                float dress=unit.FireMode==InfantryFireMode.Volley?Mathf.Min(2f,.35f+unit.FireCycleFiles*.025f):0f;
                // A single-rank sweep can restart as its first platoon reloads.
                // Deeper controlled drills still finish the rear ranks' reloads
                // before coordinating another front-to-rear firing sequence.
                float ready=unit.FireCycleReadyTime;
                if(unit.FireMode==InfantryFireMode.ByPlatoon&&unit.FireCycleRanks==1)
                {
                    ready=world.Time;
                    int firstPlatoon=Mathf.Min(unit.AliveCount,unit.FireCycleFiles/unit.FireCyclePlatoons);
                    for(int slot=0;slot<firstPlatoon;slot++)
                    {
                        int soldier=world.SoldierAtSlot(unit,slot);
                        if(soldier>=0&&!world.Soldiers.PikeSergeants[soldier]&&!world.Soldiers.Captains[soldier]&&world.Soldiers.RoundsRemaining[soldier]>0)
                            ready=Mathf.Max(ready,world.Soldiers.ReloadReadyTimes[soldier]);
                    }
                }
                unit.NextFireTime = Mathf.Max(world.Time + .2f, ready+ dress);
            }
            else unit.NextFireTime = world.Time + (count>0?GroupInterval(world, unit, platoon + 1 == unit.FireCyclePlatoons):.08f);
            unit.ReloadRemaining = Mathf.Max(0f, Mathf.Max(unit.FireCycleReadyTime, unit.NextFireTime) - world.Time);
            if (count > 0) { unit.LastVolleyTime = world.Time; unit.LastVolleySize = count; }
            return count;
        }

        // A straggler sits out this discharge. Neither his platoon nor the regiment waits.
        public static bool SoldierReady(BattleWorld world, BattalionState unit, int soldier)
        {
            int slot = world.Soldiers.FormationSlots[soldier];
            Vector2 target = unit.Position + FormationMath.SlotOffset(unit, slot);
            return world.Time-world.Soldiers.GrenadeThrowStarts[soldier]>=GrenadeSystem.ThrowDuration && world.Time>=unit.FormationReadyTime && world.Soldiers.Alive[soldier] && !world.Soldiers.IsKnockedDown(soldier,world.Time) && world.Soldiers.MovementSpeeds[soldier] <= .04f
                && (world.Soldiers.Positions[soldier] - target).sqrMagnitude <= .35f * .35f
                && Mathf.Abs(FormationMath.AngleDifference(world.Soldiers.Facings[soldier], FormationMath.SlotFacing(unit,slot))) <= .12f;
        }

        static bool ClearFront(BattleWorld world, BattalionState unit, int soldier, int slot)
        {
            if (unit.Formation != FormationKind.Line && unit.Formation != FormationKind.Column) return true;
            int files = FormationMath.EffectiveFiles(unit), rank = slot / files;
            Vector2 forward = FormationMath.Forward(unit.Facing), right = FormationMath.Right(unit.Facing);
            float lateral = Vector2.Dot(world.Soldiers.Positions[soldier] - unit.Position, right);
            for (int row = 0; row < rank; row++)
            {
                int rowFiles = Mathf.Min(files, unit.AliveCount-row*files);
                int file = Mathf.RoundToInt(lateral/unit.Spacing + (rowFiles-1)*.5f);
                for (int column = Mathf.Max(0,file-1); column <= Mathf.Min(rowFiles-1,file+1); column++)
                {
                    int front = world.SoldierAtSlot(unit,row*files+column);
                    if (front < 0) continue;
                    Vector2 delta = world.Soldiers.Positions[front] - world.Soldiers.Positions[soldier];
                    if (Vector2.Dot(delta,forward) > 0 && Mathf.Abs(Vector2.Dot(delta,right)) < .45f
                        && !IsKneeling(world,unit,front,world.Time)) return false;
                }
            }
            return true;
        }

        public static bool ScheduleIndependent(BattleWorld world, BattalionState unit, int soldier, Vector2 target,
            EmpireBattle.Art.InfantryCrowdSet appearance = null, int targetSoldier = -1)
        {
            int slot = world.Soldiers.FormationSlots[soldier];
            if (world.Soldiers.PikeSergeants[soldier] || world.Soldiers.Captains[soldier] || world.Soldiers.RoundsRemaining[soldier] <= 0 || world.Soldiers.ReloadReadyTimes[soldier] > world.Time || !SoldierReady(world,unit,soldier)) return false;
            // Both the kneeling outer rank and standing firing rank can shoot on the approach.
            // Once contacted, the outer rank must hold the bayonet wall.
            if (unit.Formation == FormationKind.Square && !FormationMath.IsFiringSlot(unit,slot)) return false;
            if (unit.Formation == FormationKind.Square && unit.InMelee && FormationMath.IsBayonetWall(unit,slot)) return false;
            if(!ClearFront(world,unit,soldier,slot))return false;
            if(!world.Ignites(unit,soldier))
            {
                world.Soldiers.RoundsRemaining[soldier]--;
                world.Soldiers.ReloadDurations[soldier]=world.ReloadAfterShot(unit,soldier)*(1+unit.Fatigue*.004f)*CadenceMultiplier(unit);
                world.Soldiers.ReloadReadyTimes[soldier]=world.Time+world.Soldiers.ReloadDurations[soldier];
                return false;
            }
            Vector3 muzzle = appearance != null ? appearance.MuzzleOffset : new Vector3(0,1.45f,1.2f);
            Vector3 flash = appearance != null ? appearance.LockOffset : new Vector3(.08f,1.45f,.35f);
            float facing = AimFacing(world.Soldiers.Positions[soldier],target,muzzle.x);
            float aimLead = Mathf.Abs(FormationMath.AngleDifference(world.Soldiers.Facings[soldier],facing))/SoldierTurnRate;
            var firingStance = unit.Formation == FormationKind.Skirmish ? SkirmishFiringStance(soldier)
                : IsKneeling(world,unit,soldier,world.Time) ? InfantryFiringStance.Kneeling : InfantryFiringStance.Standing;
            // Give each man time to rise and deliberately aim; vary this on every shot.
            float settle = unit.Formation == FormationKind.Skirmish
                ? RaiseDuration+Mathf.Lerp(.12f,.65f,Variation(soldier,unit.Id*31+Mathf.RoundToInt(world.Time*20f)+149))
                : .12f;
            float fire = world.Time + Mathf.Max(aimLead,settle) + Delay(soldier,unit.Id,world.Time);
            world.Soldiers.RoundsRemaining[soldier]--;
            float duration = world.ReloadAfterShot(unit,soldier) * (1f+unit.Fatigue*.004f) * CadenceMultiplier(unit) * Mathf.Lerp(.92f,1.12f,Variation(soldier,Mathf.RoundToInt(world.Time*20)));
            world.Soldiers.FireTimes[soldier] = fire;
            world.Soldiers.FireVolleys[soldier] = world.Time;
            // Independent fire must replace the initial infinite kneel timestamp too:
            // presentation uses this timestamp to end recoil and begin the reload.
            world.Soldiers.KneelTimes[soldier] = fire + KneelDelay;
            world.Soldiers.ReloadDurations[soldier] = duration;
            world.Soldiers.ReloadReadyTimes[soldier] = fire + duration;
            Quaternion rotation = Quaternion.Euler(0,facing*Mathf.Rad2Deg,0);
            float drop = 1.45f-MuzzleHeight(firingStance);
            muzzle.y -= drop; flash.y -= drop;
            Vector3 origin = world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier]);
            world.Soldiers.FireOrigins[soldier] = origin + rotation*muzzle;
            world.Soldiers.FireLockOrigins[soldier] = origin + rotation*flash;
            world.Soldiers.FireDirections[soldier] = rotation*Vector3.forward;
            world.Soldiers.FireTargetSoldiers[soldier]=targetSoldier;
            world.Soldiers.FireAimPoints[soldier]=targetSoldier>=0?InfantryTargeting.AimPoint(world,targetSoldier):world.Terrain.SurfacePoint(target,1.2f);
            unit.LastVolleyTime = world.Time;
            unit.LastVolleySize = 1;
            return true;
        }

        public static int ScheduleVolley(BattleWorld world, BattalionState unit, int[] shooters, EmpireBattle.Art.InfantryCrowdSet appearance = null)
        {
            return ScheduleSlots(world, unit, shooters, 0, FormationMath.FiringSoldierCount(unit), appearance);
        }
        static int ScheduleSlots(BattleWorld world, BattalionState unit, int[] shooters, int start, int end,
            EmpireBattle.Art.InfantryCrowdSet appearance, bool requireReady = false, BattalionState target = null, Vector2? fixedTarget = null)
        {
            int count = 0;
            Vector3 forward = new Vector3(Mathf.Sin(unit.Facing), 0, Mathf.Cos(unit.Facing));
            Quaternion rotation=Quaternion.Euler(0,unit.Facing*Mathf.Rad2Deg,0);
            Vector3 muzzleOffset=appearance!=null?appearance.MuzzleOffset:new Vector3(0,1.45f,1.2f);
            Vector3 lockOffset=appearance!=null?appearance.LockOffset:new Vector3(.08f,1.45f,.35f);
            for (int slot = start; slot < end; slot++)
            {
                int soldier = world.SoldierAtSlot(unit,slot);
                if (soldier < 0 || world.Time-world.Soldiers.GrenadeThrowStarts[soldier]<GrenadeSystem.ThrowDuration || world.Soldiers.PikeSergeants[soldier] || world.Soldiers.Captains[soldier] || !world.Soldiers.Alive[soldier] || world.Soldiers.IsKnockedDown(soldier,world.Time) || world.Soldiers.RoundsRemaining[soldier] <= 0 || world.Soldiers.ReloadReadyTimes[soldier] > world.Time) continue;
                if (requireReady && (!SoldierReady(world, unit, soldier) || !ClearFront(world, unit, soldier, slot))) continue;
                int targetSoldier=target!=null?InfantryTargeting.Select(world,unit,soldier,target):-1;
                if(target!=null&&targetSoldier<0)continue;
                Vector3 aim=targetSoldier>=0?InfantryTargeting.AimPoint(world,targetSoldier):fixedTarget.HasValue?world.Terrain.SurfacePoint(fixedTarget.Value,1.2f):Vector3.zero;
                float facing=target!=null||fixedTarget.HasValue?AimFacing(world.Soldiers.Positions[soldier],new Vector2(aim.x,aim.z),muzzleOffset.x):unit.Facing;
                rotation=Quaternion.Euler(0,facing*Mathf.Rad2Deg,0);forward=rotation*Vector3.forward;
                if(!world.Ignites(unit,soldier))
                {
                    world.Soldiers.RoundsRemaining[soldier]--;
                    world.Soldiers.ReloadDurations[soldier]=world.ReloadAfterShot(unit,soldier)*(1+unit.Fatigue*.004f)*CadenceMultiplier(unit);
                    world.Soldiers.ReloadReadyTimes[soldier]=world.Time+world.Soldiers.ReloadDurations[soldier];
                    unit.FireCycleReadyTime=Mathf.Max(unit.FireCycleReadyTime,world.Soldiers.ReloadReadyTimes[soldier]);
                    continue;
                }
                // A kneeling man gets a real rise/aim window before his muzzle flash and shot.
                float raiseLead=IsKneeling(world,unit,soldier,world.Time)?RaiseDuration:0f;
                if(target!=null||fixedTarget.HasValue)raiseLead=Mathf.Max(raiseLead,Mathf.Abs(FormationMath.AngleDifference(world.Soldiers.Facings[soldier],facing))/SoldierTurnRate);
                float fire = world.Time + raiseLead + Delay(soldier, unit.Id, world.Time);
                int timingSalt = unit.Id * 31 + Mathf.RoundToInt(world.Time * 20f);
                float kneelDelay = KneelDelay + .45f * Variation(soldier, timingSalt + 17);
                world.Soldiers.FireTimes[soldier] = fire;
                world.Soldiers.RoundsRemaining[soldier]--;
                world.Soldiers.ReloadDurations[soldier] = world.ReloadAfterShot(unit,soldier) * (1f+unit.Fatigue*.004f) * CadenceMultiplier(unit)
                    * Mathf.Lerp(1f, 1.12f, Variation(soldier, timingSalt + 23));
                world.Soldiers.ReloadReadyTimes[soldier] = fire + world.Soldiers.ReloadDurations[soldier];
                unit.FireCycleReadyTime = Mathf.Max(unit.FireCycleReadyTime, world.Soldiers.ReloadReadyTimes[soldier]);
                world.Soldiers.KneelTimes[soldier] = fire + kneelDelay;
                world.Soldiers.FireVolleys[soldier] = world.Time;
                world.Soldiers.FireOrigins[soldier] = world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier]) + rotation*muzzleOffset;
                world.Soldiers.FireLockOrigins[soldier]=world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier])+rotation*lockOffset;
                world.Soldiers.FireDirections[soldier] = forward;
                world.Soldiers.FireTargetSoldiers[soldier]=targetSoldier;world.Soldiers.FireAimPoints[soldier]=aim;
                shooters[count++] = soldier;
            }
            return count;
        }
    }
}
