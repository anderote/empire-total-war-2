using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Shared, deterministic presentation footprint for burning shell filling.</summary>
    public static class ArtilleryEffectShape
    {
        // Truncated p(radius) proportional to radius^-2; stratification preserves the
        // rare large chunks even at modest particle counts. One reciprocal, no rejection loop.
        public static float ClodRadius(int seed,int index,int count)
        {
            float quantile=(index+RifleFireTiming.Variation(seed,9))/Mathf.Max(1,count);
            return 1f/Mathf.Lerp(1f/.035f,1f/.55f,Mathf.Clamp01(quantile));
        }
        public const int PatchCount = 7;
        public static Vector3 Forward(Vector3 incoming)
        {
            incoming.y=0;
            return incoming.sqrMagnitude>.0001f ? incoming.normalized : Vector3.forward;
        }
        public static Vector3 WorldOffset(Vector2 local, Vector3 incoming)
        {
            Vector3 forward=Forward(incoming), right=new Vector3(forward.z,0,-forward.x);
            return right*local.x+forward*local.y;
        }
        // One impact pool, then an irregular fan of fuel thrown ahead of the impact.
        // z holds each pool's radius; x/y are metres across/along the incoming path.
        public static Vector3 FuelPatch(int seed,int patch)
        {
            if(patch==0)return new Vector3(0,0,1.05f);
            float angle=Mathf.Lerp(-.48f,.48f,(patch-1)/5f);
            angle+=(RifleFireTiming.Variation(seed+patch,71)-.5f)*.13f;
            float reach=3f+patch*.85f+RifleFireTiming.Variation(seed+patch,73)*1.3f;
            return new Vector3(Mathf.Sin(angle)*reach,Mathf.Cos(angle)*reach,
                Mathf.Lerp(.65f,1.2f,RifleFireTiming.Variation(seed+patch,79)));
        }
        public static Vector2 FlamePoint(int seed,int strand)
        {
            Vector3 patch=FuelPatch(seed,strand%PatchCount);
            float angle=strand*2.399963f+RifleFireTiming.Variation(seed,83)*6.283185f;
            float radius=patch.z*Mathf.Lerp(.12f,.58f,RifleFireTiming.Variation(seed+strand,89));
            return new Vector2(patch.x+Mathf.Sin(angle)*radius,patch.y+Mathf.Cos(angle)*radius);
        }
    }
}
