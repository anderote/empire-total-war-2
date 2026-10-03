namespace EmpireBattle
{
    // Benchmark-only comparison switch; normal play uses the optimized paths.
    public static class BattleOptimizationSettings
    {
        public static bool ContactMathOptimizations=true;
        public static bool RenderWorkOptimizations=true;
        public static bool HitchOptimizations=true;
        public static bool FastForwardOptimizations=true;
        public static bool CrowdFlow=true;
        public static bool RegimentContacts=true;
        public static bool ArtilleryBroadphase=true;
    }
}
