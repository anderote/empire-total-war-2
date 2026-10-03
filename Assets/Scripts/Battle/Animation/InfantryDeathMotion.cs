using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Stable presentation choices for the ten authored infantry falls.</summary>
    public static class InfantryDeathMotion
    {
        public const float DefaultDuration = 1.25f;

        // Forward: 1 (quick pitch), 3 (stagger), 7 (buckled collapse).
        // Backward: 0, 2, 6. Sideways: 4, 5, 8, 9.
        // Keep all directions, but let a musket volley visibly break the rank forward.
        static readonly int[] MusketWeights = { 10, 26, 9, 21, 2, 2, 9, 17, 2, 2 };

        public static int Variant(SoldierDeath death, int count, bool assetReview = false)
        {
            if (count <= 0) return -1;
            float choice = RifleFireTiming.Variation(death.Soldier,
                83 + (assetReview ? Mathf.FloorToInt(death.Born * 19) : 0));
            if(!assetReview&&death.Cannon&&death.Kind==UnitKind.Infantry&&count==10)
            {
                Vector2 direction=new Vector2(death.Impulse.x,death.Impulse.z);
                float forward=Vector2.Dot(direction,FormationMath.Forward(death.Facing));
                float side=Vector2.Dot(direction,FormationMath.Right(death.Facing));
                if(Mathf.Abs(side)>Mathf.Abs(forward))return side>0?(choice<.5f?4:8):(choice<.5f?5:9);
                if(forward>0)return choice<.33f?1:choice<.66f?3:7;
                return choice<.33f?0:choice<.66f?2:6;
            }
            // Older/foreign libraries have no guaranteed directional index contract.
            if (assetReview || death.Cause != DeathCause.Musket || death.Kind != UnitKind.Infantry || count != MusketWeights.Length)
                return Mathf.Min(count - 1, (int)(choice * count));
            float ticket = choice * 100f;
            for (int i = 0; i < MusketWeights.Length; i++)
            {
                ticket -= MusketWeights[i];
                if (ticket < 0) return i;
            }
            return count - 1;
        }

        public static float Duration(SoldierDeath death, int variant, bool assetReview = false)
        {
            float variation = RifleFireTiming.Variation(death.Soldier, 89);
            if (!assetReview && death.Cause == DeathCause.Musket && death.Kind == UnitKind.Infantry)
            {
                switch (variant)
                {
                    case 1: return Mathf.Lerp(.94f, 1.12f, variation);
                    case 3: return Mathf.Lerp(1.18f, 1.42f, variation);
                    case 7: return Mathf.Lerp(1.40f, 1.68f, variation);
                }
            }
            return DefaultDuration * Mathf.Lerp(.82f, 1.18f, variation);
        }
    }
}
