using UnityEngine;
namespace EmpireBattle
{
    public static class BayonetPresentationTiming
    {
        public const float Duration=2.4f;
        public static float Progress(BattalionState unit,float clock)=>Mathf.Clamp01(unit.BayonetOrderInitialPhase+(clock-unit.BayonetOrderTime)/Duration);
        public static bool Handling(BattleWorld world,BattalionState unit,int soldier,float clock,out float phase)
        {
            float age=clock-unit.BayonetOrderTime;
            phase=Progress(unit,clock);
            return age>=0&&phase<1&&world.CanFixBayonets(unit)&&!unit.Routing&&!unit.InMelee
                &&!world.Soldiers.Captains[soldier]&&!world.Soldiers.PikeSergeants[soldier]
                &&!world.Soldiers.IsKnockedDown(soldier,clock)&&world.Soldiers.MeleeEnds[soldier]<=clock;
        }
    }
}
