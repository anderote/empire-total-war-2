using System;
using EmpireBattle;
using UnityEngine;

public static class EarthworkSamplingVerification
{
    public static void Verify()
    {
        var random=new System.Random(1815);
        for(int shape=0;shape<40;shape++)
        {
            float angle=(float)random.NextDouble()*6.28f;
            var cut=new ArtilleryEarthwork{Center=new Vector2(17,-31),Forward=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle)),
                Radius=.2f+(float)random.NextDouble()*4,HalfLength=.2f+(float)random.NextDouble()*8,
                Depth=.1f+(float)random.NextDouble(),Seed=shape*1.371f,Shell=shape%2==0};
            cut.Right=new Vector2(cut.Forward.y,-cut.Forward.x);
            for(int sample=0;sample<4000;sample++)
            {
                float u=(float)random.NextDouble()*8-4,v=(float)random.NextDouble()*8-4;
                var p=cut.Center+cut.Right*(u*cut.Radius)+cut.Forward*(v*cut.HalfLength);
                float expected=Reference(cut,p),actual=cut.Displacement(p);
                // The two JIT methods can differ by a few ULPs; preserve nonzero
                // samples and require agreement within ten micrometres of height.
                if((actual==0&&expected!=0)||Mathf.Abs(expected-actual)>1e-5f)throw new Exception("Earthwork sampling changed at "+p+" shell="+cut.Shell+" u="+u+" v="+v+" expected="+expected.ToString("R")+" actual="+actual.ToString("R"));
            }
        }
        Debug.Log("EARTHWORK_SAMPLING_PARITY_VERIFIED 160000 crater/furrow samples");
    }
        static float Reference(ArtilleryEarthwork cut,Vector2 point)
        {
            var Center=cut.Center;var Right=cut.Right;var Forward=cut.Forward;
            float Radius=cut.Radius,HalfLength=cut.HalfLength,Seed=cut.Seed,Depth=cut.Depth;bool Shell=cut.Shell;
            Vector2 delta = point - Center;
            float u = Vector2.Dot(delta, Right) / Radius, v = Vector2.Dot(delta, Forward) / HalfLength;
            if (!Shell) u -= (.28f * Mathf.Sin(v * 3.5f + Seed) + .12f * Mathf.Sin(v * 9.3f - Seed)) * Mathf.Clamp01(1f - v * v);
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
