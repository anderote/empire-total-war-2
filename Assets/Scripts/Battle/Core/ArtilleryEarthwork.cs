using UnityEngine;

namespace EmpireBattle
{
    // One deterministic shape shared by ballistics, ground geometry, debris and smoke anchors.
    public sealed class ArtilleryEarthwork
    {
        public Vector2 Center, Forward, Right;
        public float Radius, HalfLength, Depth, Seed;
        public bool Shell;
        public static ArtilleryEarthwork FromImpact(ArtilleryImpact hit)
        {
            var forward = new Vector2(hit.Direction.x, hit.Direction.z);
            forward = forward.sqrMagnitude > .0001f ? forward.normalized : Vector2.up;
            float seed = hit.Sequence * 1.371f;
            float variation = .86f + .28f * Mathf.Abs(Mathf.Sin(seed * 7.13f));
            float energy = Mathf.Clamp(hit.Speed / 150f, .25f, 1.8f);
            float incidence = Mathf.Clamp01(-hit.Direction.y);
            bool shell = hit.ExplosiveShell;
            float radius = (shell ? 2.1f * Mathf.Sqrt(energy) : Mathf.Lerp(.24f, .65f, incidence) * Mathf.Sqrt(energy)) * variation;
            if(hit.AmmunitionType == ArtilleryAmmunition.Carcass)radius *= .55f;
            float craterDepthScale = hit.AmmunitionType == ArtilleryAmmunition.Carcass ? .35f : 1f;
            float length = shell ? radius * Mathf.Lerp(1.35f, 1f, incidence) : Mathf.Clamp(hit.Speed * .045f * (1f - incidence), .6f, 8f);
            return new ArtilleryEarthwork { Center = new Vector2(hit.Position.x, hit.Position.z) + (shell ? Vector2.zero : forward * length * .5f),
                Forward = forward, Right = new Vector2(forward.y, -forward.x), Radius = radius,
                HalfLength = shell ? length : length * .5f, Depth = (shell ? .65f : Mathf.Lerp(.12f, .45f, incidence)) * energy * variation * craterDepthScale, Seed = seed, Shell = shell };
        }
        public float Extent => Mathf.Max(Radius, HalfLength) * 1.3f;
        public float Displacement(Vector2 point)
        {
            Vector2 delta = point - Center;
            float u = Vector2.Dot(delta, Right) / Radius, v = Vector2.Dot(delta, Forward) / HalfLength;
            // Conservative bounds from the maximum irregular radius (1.145 for
            // shells, 1.4 for furrows). Most cell samples lie outside the cut;
            // reject them before atan/sin/exp without changing its actual shape.
            if (BattleOptimizationSettings.HitchOptimizations && (Shell ? u*u+v*v>=2.062f : Mathf.Abs(v)>=1f)) return 0;
            if (!Shell) u -= (.28f * Mathf.Sin(v * 3.5f + Seed) + .12f * Mathf.Sin(v * 9.3f - Seed)) * Mathf.Clamp01(1f - v * v);
            if (BattleOptimizationSettings.HitchOptimizations && !Shell && Mathf.Abs(u)>=1.751f) return 0;
            float angle = Mathf.Atan2(v, u);
            float irregular = Shell ? 1f + .09f * Mathf.Sin(angle * 3f + Seed) + .055f * Mathf.Sin(angle * 7f - Seed * 2f)
                : 1f + .24f * Mathf.Sin(v * 4.5f + Seed) + .16f * Mathf.Sin(v * 13f - Seed);
            float r = (Shell ? Mathf.Sqrt(u * u + v * v) : Mathf.Abs(u)) / irregular;
            float end = Shell ? 1f : Mathf.SmoothStep(0, 1, Mathf.Clamp01((1f - Mathf.Abs(v)) * 4f));
            if (r >= 1.25f || end <= 0f) return 0;
            float bite = Shell ? 1f : .75f + .3f * Mathf.Sin(v * 7.7f + Seed) + .12f * Mathf.Sin(v * 19f - Seed);
            float basin = -Depth * Mathf.Max(.35f,bite) * Mathf.Pow(Mathf.Clamp01(1f - r * r / .72f), 1.4f);
            float rim = Depth * (Shell ? .3f : .13f) * Mathf.Exp(-Mathf.Pow((r - .88f) / .15f, 2));
            float fade = Mathf.Clamp01((1.25f - r) / .15f);
            float rubble = .5f + .5f * Mathf.Sin(point.x * 10f + Seed) * Mathf.Sin(point.y * 9f - Seed);
            float clumps = Shell ? .6f + .75f * rubble : .12f + 1.5f * rubble * rubble * rubble;
            return (basin + rim * clumps) * end * fade;
        }
    }
}
