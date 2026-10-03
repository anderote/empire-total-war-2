using System;
using System.Collections.Generic;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class InfantryOrderEdgeVerification
{
    static void Require(bool value, string message)
    { if (!value) throw new Exception(message); }
    static BattleWorld World(int count = 100) => new BattleWorld(new BattleConfig {
        BattalionsPerSide = 1, SoldiersPerBattalion = count, InitialSeparation = 100,
        EnableAI = false, EnableLandscape = false, UseDetailedInfantry = false });
    static void Step(BattleWorld world, float seconds)
    { for (int i = 0; i < Mathf.CeilToInt(seconds / .05f); i++) world.StepMovement(.05f); }
    static int Musketeer(BattleWorld world, BattalionState unit)
    {
        for (int slot = 0; slot < unit.AliveCount; slot++)
        {
            int soldier = world.SoldierAtSlot(unit, slot);
            if (!world.Soldiers.PikeSergeants[soldier] && !world.Soldiers.Captains[soldier]) return soldier;
        }
        throw new Exception("Fixture requires a musketeer");
    }

    public static void Verify()
    {
        var failures = new List<string>();
        foreach (Action check in new Action[] { AmmunitionOnReform, HaltFacing, ColumnFire, SmallSquarePose, ReloadAfterReassignment, CommandAboutFace })
        {
            try { check(); Debug.Log("INFANTRY_EDGE_PASSED " + check.Method.Name); }
            catch (Exception error) { failures.Add(check.Method.Name + ": " + error.Message); Debug.Log("INFANTRY_EDGE_FAILED " + failures[failures.Count - 1]); }
        }
        if (failures.Count > 0) throw new Exception(string.Join("\n", failures));
        Debug.Log("INFANTRY_ORDER_EDGES_VERIFIED");
    }
    static void AmmunitionOnReform()
    {
        var world = World(250); var unit = world.Battalions[0]; var shots = new int[250];
        Require(RifleFireTiming.ScheduleNextGroup(world, unit, shots) > 0, "Partial platoon fires");
        var rounds = (int[])world.Soldiers.RoundsRemaining.Clone();
        var ready = (float[])world.Soldiers.ReloadReadyTimes.Clone();
        world.SetFormation(0, FormationKind.Column);
        for (int i = 0; i < unit.SoldierCount; i++)
        {
            Require(world.Soldiers.RoundsRemaining[i] == rounds[i], "Changing formation discarded an unfired cartridge for soldier " + i);
            Require(world.Soldiers.ReloadReadyTimes[i] == ready[i], "Reforming must preserve individual reloads");
        }
    }
    static void HaltFacing()
    {
        var world = World(); var unit = world.Battalions[0];
        world.IssueMove(0, unit.Position + Vector2.up * 30, Mathf.PI);
        Step(world, 2); float facing = unit.Facing; Vector2 position = unit.Position;
        world.Halt(0); Step(world, 8);
        Require(Vector2.Distance(position, unit.Position) < .001f, "Halt must stop translation");
        Require(Mathf.Abs(FormationMath.AngleDifference(facing, unit.Facing)) < .001f, "Halt continued turning toward a cancelled arrival facing");
    }
    static void ColumnFire()
    {
        var world = World(); var unit = world.Battalions[0];
        world.SetFormation(0, FormationKind.Column); Step(world, 35);
        for (int slot = 0; slot < unit.AliveCount; slot++)
        {
            int soldier = world.SoldierAtSlot(unit, slot);
            world.Soldiers.Positions[soldier] = unit.Position + FormationMath.SlotOffset(unit, slot);
            world.Soldiers.Facings[soldier] = unit.Facing;
            world.Soldiers.MovementSpeeds[soldier] = 0;
        }
        var shooters = new int[100];
        int count = RifleFireTiming.ScheduleNextGroup(world, unit, shooters);
        Require(count > 0, "Ready column front-rank musketeers can fire");
        for (int i = 0; i < count; i++)
            Require(world.Soldiers.FormationSlots[shooters[i]] < unit.Files, "Column rear rank fired through standing front rank");
    }
    static Mesh Pose(BattleWorld world, BattalionState unit, int soldier, bool firing, float time)
    {
        var renderer = new CrowdInfantryRenderer(world, null);
        typeof(CrowdInfantryRenderer).GetField("clock", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(renderer, time);
        var lod = Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary").French.Lods[0];
        return (Mesh)typeof(CrowdInfantryRenderer).GetMethod("Pose", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(renderer, new object[] { lod, unit, firing, soldier, 0f });
    }
    static void SmallSquarePose()
    {
        var world = World(7); var unit = world.Battalions[0];
        world.SetFormation(0, FormationKind.Square); Step(world, 35);
        Require(!unit.Reforming, "Small square finishes reforming before pose inspection");
        int soldier = Musketeer(world, unit);
        Require(RifleFireTiming.ScheduleIndependent(world, unit, soldier, unit.Position + Vector2.up * 100), "Small square soldier fires");
        Require(float.IsFinite(world.Soldiers.KneelTimes[soldier]), "Independent shot establishes finite recoil/reload timing");
        Require(world.Soldiers.FireOrigins[soldier].y < 1.2f, "Kneeling square musketeer fires from kneeling height");
        var lod = Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary").French.Lods[0];
        Mesh pose = Pose(world, unit, soldier, true, world.Soldiers.FireTimes[soldier] + 3);
        Require(Array.IndexOf(lod.KneelingReload, pose) >= 0, "Single-rank square reload pose=" + pose.name
            + " kneeling=" + RifleFireTiming.IsKneeling(world, unit, soldier, world.Soldiers.FireTimes[soldier] + 3));
    }
    static void ReloadAfterReassignment()
    {
        var world = World(); var unit = world.Battalions[0]; int soldier = Musketeer(world, unit);
        RifleFireTiming.ScheduleNextGroup(world, unit, new int[100]);
        unit.FireAtWill = false;
        // A shooter can move into a non-firing rear slot after a pivot or reformation.
        var lod = Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary").French.Lods[0];
        Mesh pose = Pose(world, unit, soldier, false, world.Soldiers.FireTimes[soldier] + 3);
        Require(Array.IndexOf(lod.Reload, pose) >= 0, "Reload animation disappears when a fired soldier is reassigned behind firing ranks");
    }
    static void CommandAboutFace()
    {
        foreach (int count in new[] { 250, 252 })
        {
            var world = World(count); var unit = world.Battalions[0];
            var before = (Vector2[])world.Soldiers.Positions.Clone();
            world.IssueMove(0, unit.Position, Mathf.PI); Step(world, 15);
            Require(!unit.Moving && !unit.Reforming, "Regiment with command figures settles after about-face");
            for (int i = 0; i < count; i++)
            {
                Require(Vector2.Distance(before[i], world.Soldiers.Positions[i]) < FormationMath.RankSpacing(unit.Spacing) * 4,
                    "About-face keeps command figures and displaced musketeers nearby");
                if (world.Soldiers.PikeSergeants[i] || world.Soldiers.Captains[i])
                    Require(world.Soldiers.FormationSlots[i] < unit.Files, "Living command figures regain front rank");
            }
        }
    }
    public static void VerifyAll()
    {
        Verify();
        FormationTacticsVerification.Verify();
        InfantryManeuverVerification.VerifyMovementOrders();
        InfantryFireVerification.VerifyDrills();
        InfantryPresentationVerification.Verify();
        BattleOrdersVerification.Verify();
        InfantryPerformanceVerification.VerifyMovement();
    }
    public static void BuildPlaytests() { VerifyAll(); InfantryBattleBuild.BuildMatchedPlaytests(); }
}
