using UnityEngine;
namespace EmpireBattle
{
    public sealed partial class CombatSystem
    {
        // Combat troops remain the priority; an empty enemy formation does not hide its fleeing mounts.
        LooseHorse FindLooseHorse(BattalionState unit, float facing)
        {
            LooseHorse best = null;
            float nearest = world.AimedRange(unit) * world.AimedRange(unit);
            foreach (var horse in world.LooseHorses.Values)
            {
                if (!horse.Targetable || horse.Side == unit.Side) continue;
                Vector2 delta = horse.Position - unit.Position;
                float distance = delta.sqrMagnitude;
                if (distance > nearest || (distance > .01f && Vector2.Dot(FormationMath.Forward(facing), delta.normalized)
                    < (unit.Formation == FormationKind.Square ? .707f : WeaponBallistics.MinimumFacingDot))) continue;
                if (world.Terrain.BlocksLine(unit.Position, horse.Position) || FriendlyBlocksFire(unit, horse.Position)) continue;
                best = horse; nearest = distance;
            }
            return best;
        }

        void ResolveHorseHit(int pending)
        {
            int rider = pendingHorses[pending];
            if (!world.LooseHorses.TryGetValue(rider, out var horse)) return;
            int shooter = pendingShooters[pending];
            Vector3 origin = shooter >= 0 ? world.Soldiers.FireOrigins[shooter]
                : world.Terrain.SurfacePoint(world.Battalions[pendingAttackers[pending]].Position, 1.5f);
            Vector3 end = world.Terrain.SurfacePoint(horse.Position, 1.2f);
            LaunchBullet(origin,end,pendingDamage[pending],pendingAttackers[pending],shooter,
                shooter>=0?world.Soldiers.FireTimes[shooter]:world.Time,pendingVisual[pending]);
        }
    }
}
