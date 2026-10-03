using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Presentation-only emphasis; particle pools and combat rules stay unchanged.</summary>
    public static class SlowMotionEffects
    {
        public static float Strength(float speed)
            => float.IsNaN(speed) || float.IsInfinity(speed) ? 0f : Mathf.Clamp01((1f-speed)/.9f);

        public static float Amount(BattleView view, Vector3 position)
        {
            if (!view || view.GetSpeed == null) return 0f;
            float strength=Strength(view.GetSpeed());
            if (strength<=0f || !view.BattleCamera) return 0f;
            return strength * DistanceWeight((position-view.BattleCamera.transform.position).sqrMagnitude);
        }

        public static float DistanceWeight(float squaredDistance)
            => 1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(150f*150f,450f*450f,squaredDistance));

        public static int BloodCount(bool cannon, float amount)
            => Mathf.RoundToInt((cannon?48:40)*Mathf.Lerp(1f,1.35f,Mathf.Clamp01(amount)));
        public static float SmokeSize(float amount) => Mathf.Lerp(1f,1.25f,Mathf.Clamp01(amount));
        public static float SmokeDensity(float amount) => Mathf.Lerp(1f,1.45f,Mathf.Clamp01(amount));
    }
}
