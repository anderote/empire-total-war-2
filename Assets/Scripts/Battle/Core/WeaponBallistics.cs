using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Gameplay ballistics: gravity, range-dependent dispersion and readable projectile speeds.</summary>
    public static class WeaponBallistics
    {
        public const float MuzzleSpeed = 220f;
        public const float Gravity = 9.81f;
        public const float MaximumAimedRange = 247f;
        public const float MinimumFacingDot = .819f;
        public static float FireHalfAngle => Mathf.Acos(MinimumFacingDot);
        public const float MaximumAimedFlight = MaximumAimedRange / MuzzleSpeed;
        // Angular error grows at long range; triangular samples concentrate fire near the aim point.
        public static float Dispersion(float distance, float effectiveRange, float nearSpread, float farSpread)
            => Mathf.Lerp(nearSpread, farSpread, Mathf.Pow(Mathf.Clamp01(distance / effectiveRange), 1.5f));

        public static float MusketHitChance(float distance, float nominal, float cohesion, float fatigue, float experience, UnitKind targetKind = UnitKind.Infantry)
        {
            float fraction = distance / Mathf.Max(1f, nominal);
            return Mathf.Clamp01((Mathf.Lerp(.20f, .025f, Mathf.Clamp01(fraction)) + (targetKind==UnitKind.Cavalry?.87f:.75f) * CloseRangeWeight(distance, nominal))
                * Mathf.Exp(-Mathf.Max(0f, fraction - 1f) * 3f)
                * cohesion * (1f - fatigue * .004f) * (1f + Mathf.Min(.12f, experience * .002f)));
        }

        // Gameplay tuning: decisive at close quarters, with the existing medium/long-range
        // balance intact. Better rifle range must not extend this bonus across the field.
        static float CloseRangeWeight(float distance, float nominal)
            => 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, distance) / Mathf.Min(75f, Mathf.Max(1f, nominal) * .5f));

        public static float MusketDamage(float distance, float nominal, float sample)
            => 60f + Mathf.Clamp01(sample) * 60f + 40f * CloseRangeWeight(distance, nominal);

        // All small arms share the musket's existing lethality baseline. Rifle upgrades
        // increase damage on top of it, so later cartridges never lose the round-ball bonus.
        public static float SmallArmsDamage(float distance,float nominal,float sample,string weapon,UnitKind targetKind)
        {
            float damage=MusketDamage(distance,nominal,sample);
            if(targetKind==UnitKind.Infantry||targetKind==UnitKind.Cavalry)damage+=35f;
            return damage*(MilitaryEquipment.Weapon(weapon)?.DamageMultiplier??1f);
        }

        public static Vector3 PositionAt(Vector3 start, Vector3 velocity, float time)
            => start + velocity * time + Vector3.down * (.5f * Gravity * time * time);

        public static bool TrySolve(Vector3 start, Vector3 end, out Vector3 velocity, out float duration, float aimedRange=MaximumAimedRange)
        {
            Vector3 delta = end - start;
            Vector2 horizontal = new Vector2(delta.x, delta.z);
            float distance = horizontal.magnitude;
            velocity = Vector3.zero; duration = 0f;
            if (distance < .01f) return false;
            float speedSquared = MuzzleSpeed * MuzzleSpeed;
            float discriminant = speedSquared * speedSquared - Gravity * (Gravity * distance * distance + 2f * delta.y * speedSquared);
            if (discriminant < 0f) return false;
            // Rationalized low-angle solution avoids cancellation at short infantry ranges.
            float tangent = (Gravity * distance * distance + 2f * delta.y * speedSquared)
                / (distance * (speedSquared + Mathf.Sqrt(discriminant)));
            float cosine = 1f / Mathf.Sqrt(1f + tangent * tangent);
            float horizontalSpeed = MuzzleSpeed * cosine;
            duration = distance / horizontalSpeed;
            velocity = new Vector3(horizontal.x / distance * horizontalSpeed, horizontalSpeed * tangent, horizontal.y / distance * horizontalSpeed);
            return distance<=aimedRange && duration <= aimedRange / MuzzleSpeed;
        }

        public static bool TraceGround(BattlefieldTerrain terrain, Vector3 start, Vector3 velocity, float duration, out Vector3 end)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(duration * 48f), 4, 48);
            Vector3 previous = start;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 current = PositionAt(start, velocity, duration * i / steps);
                float ground = terrain.HeightAt(new Vector2(current.x, current.z));
                if (current.y <= ground)
                {
                    float previousGround = terrain.HeightAt(new Vector2(previous.x, previous.z));
                    float clearance = previous.y - previousGround;
                    float fraction = clearance / Mathf.Max(.0001f, clearance + ground - current.y);
                    end = Vector3.Lerp(previous, current, Mathf.Clamp01(fraction));
                    end.y = terrain.HeightAt(new Vector2(end.x, end.z));
                    return true;
                }
                previous = current;
            }
            end = previous;
            return false;
        }
    }
}
