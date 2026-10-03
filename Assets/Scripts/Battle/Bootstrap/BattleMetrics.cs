using System;
using System.Diagnostics;

namespace EmpireBattle
{
    // Explicit subsystem wall times supplement Unity's CPU Timeline in ordinary players.
    // Disabled outside benchmark captures; no per-frame allocations.
    public static class BattleMetrics
    {
        public enum Stage { Movement, Combat, AI, Artillery, CasualtyStep, InfantryRender, Smoke, Landscape, Earthworks, Corpses, Blood, Projectiles, Flashes, Cannons, HUD, GroundMarks, RagdollGround, RagdollPhysics, CorpseScatter, CorpseFreeze, GoreStep, GroundBake, BloodStep, BodyContacts, ContactMovement, MovementJobs, InfantryBounds, ContactIndex, ContactPairs, CrowdSubmit, Count }
        public static bool Enabled;
        public static bool AllocationCountersSupported;
        public static int CrowdDrawCalls,CrowdInstances;
        public static readonly double[] Milliseconds = new double[(int)Stage.Count];
        public static readonly double[] Maximum = new double[(int)Stage.Count];
        public static readonly int[] Calls = new int[(int)Stage.Count];
        public static readonly long[] AllocatedBytes = new long[(int)Stage.Count];
        public static Scope Measure(Stage stage) => new Scope(stage);
        public readonly struct Scope : IDisposable
        {
            readonly int stage;
            readonly long start;
            readonly long allocated;
            public Scope(Stage value) { stage=(int)value; start=Enabled?Stopwatch.GetTimestamp():0; allocated=Enabled&&AllocationCountersSupported?GC.GetAllocatedBytesForCurrentThread():0; }
            public void Dispose()
            {
                if(start==0)return;
                double ms=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                Milliseconds[stage]+=ms; Maximum[stage]=Math.Max(Maximum[stage],ms); Calls[stage]++;
                if(AllocationCountersSupported)AllocatedBytes[stage]+=GC.GetAllocatedBytesForCurrentThread()-allocated;
            }
        }
        public static void Reset() { CrowdDrawCalls=CrowdInstances=0; Array.Clear(Milliseconds,0,Milliseconds.Length); Array.Clear(Maximum,0,Maximum.Length); Array.Clear(Calls,0,Calls.Length); Array.Clear(AllocatedBytes,0,AllocatedBytes.Length); }
    }
}
