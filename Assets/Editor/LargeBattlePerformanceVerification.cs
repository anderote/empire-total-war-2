using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using EmpireBattle;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Headless, deterministic scale capture. Does not build or launch a player.
public static class LargeBattlePerformanceVerification
{
    [Serializable] public sealed class Sample
    {
        public string scenario, state;
        public int soldiers, ticks;
        public double movementMs, combatMs, boundsMs;
        public int contactGridBuilds, contactRegimentTicks;
    }
    [Serializable] public sealed class Report { public List<Sample> samples = new List<Sample>(); }

    public static void Capture()
    {
        string label = "current";
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--scale-label") label = args[i + 1];
        var report = new Report();
        // Compile/warm Burst before timing any scenario.
        Run("march", 500, 3);
        foreach (int count in new[] { 4000, 12000, 24000 })
            foreach (string scenario in new[] { "idle", "march", "fire" })
            {
                var sample = Run(scenario, count, 60);
                report.samples.Add(sample);
                Debug.Log($"BATTLE_SCALE {scenario} soldiers={count} movement_ms={sample.movementMs:F3} combat_ms={sample.combatMs:F3} bounds_ms={sample.boundsMs:F3} state={sample.state}");
            }
        string path = "Artifacts/LargeBattleOptimization/" + label + ".json";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Debug.Log("BATTLE_SCALE_CAPTURE_PASSED " + path);
    }

    static Sample Run(string scenario, int count, int ticks)
    {
        var world = new BattleWorld(new BattleConfig {
            BattalionsPerSide = Math.Max(1, count / 1000), SoldiersPerBattalion = count < 1000 ? count / 2 : 500,
            EnableAI = false, EnableLandscape = false, UseDetailedInfantry = false,
            FrenchArtillery = 0, BritishArtillery = 0, InitialSeparation = scenario == "fire" ? 70 : 400 });
        using var jobs = new SoldierMovementJobs(world);
        world.MovementJobs = jobs;
        var combat = new CombatSystem(world);
        foreach (var unit in world.Battalions)
        {
            unit.FireAtWill = scenario == "fire";
            if (scenario == "march") world.IssueMove(unit.Id, unit.Position + FormationMath.Forward(unit.Facing) * 80, unit.Facing);
        }
        double movement = 0, fighting = 0, bounds = 0;
        int grids=0,contactRegiments=0;
        for (int tick = 0; tick < ticks; tick++)
        {
            long start = Stopwatch.GetTimestamp();
            world.StepMovement(.05f);
            long middle = Stopwatch.GetTimestamp();
            combat.Step(.05f);
            long end = Stopwatch.GetTimestamp();
            foreach (var unit in world.Battalions) BattleRenderBounds.Regiment(world, unit, world.Time + .025f);
            long rendered = Stopwatch.GetTimestamp();
            movement += middle - start; fighting += end - middle; bounds += rendered - end;
            grids+=world.BodyContacts.LastGridBuilds;contactRegiments+=world.BodyContacts.LastContactRegiments;
        }
        double ms = 1000.0 / Stopwatch.Frequency / ticks;
        var result = new Sample { scenario = scenario, soldiers = world.Soldiers.Alive.Length, ticks = ticks,
            movementMs = movement * ms, combatMs = fighting * ms, boundsMs = bounds * ms, state = State(world, combat) };
        result.contactGridBuilds=grids;result.contactRegimentTicks=contactRegiments;
        world.MovementJobs = null;
        return result;
    }

    static string State(BattleWorld world, CombatSystem combat)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(world.Time); writer.Write(world.DeathCount); writer.Write(combat.ShotSequence);
        foreach (var u in world.Battalions)
        {
            writer.Write(u.Position.x); writer.Write(u.Position.y); writer.Write(u.Facing);
            writer.Write(u.AliveCount); writer.Write(u.Morale); writer.Write(u.Cohesion); writer.Write(u.Ammunition);
        }
        var s = world.Soldiers;
        for (int i = 0; i < s.Alive.Length; i++)
        {
            writer.Write(s.Alive[i]); writer.Write(s.Positions[i].x); writer.Write(s.Positions[i].y);
            writer.Write(s.Facings[i]); writer.Write(s.GaitPhases[i]); writer.Write(s.FireTimes[i]);
            writer.Write(s.FireTargetSoldiers[i]); writer.Write(s.FormationSlots[i]);
        }
        writer.Flush();
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
    }
}
