using System;
using UnityEngine;

namespace EmpireBattle
{
    // Optional native-player acceptance sequence; ordinary play has no review component.
    public sealed class InfantryOrdersReview : MonoBehaviour
    {
        BattleWorld world;
        int stage, shotsBeforeMove, shotsBeforeSquare;
        int[] rounds;
        float[] reloads;
        public void Initialize(BattleRuntime value)
        {
            world = value.World;
            world.Battalions[1].FireAtWill = false;
            world.IssueRangedAttack(0, 1);
        }
        void Require(bool value, string message)
        { if (!value) throw new Exception(message); }
        void Update()
        {
            if (world == null || stage == 5) return;
            var unit = world.Battalions[0];
            try
            {
                if (stage == 0 && world.Time >= 1)
                {
                    Require(unit.ShotsFired > 0, "Initial stationary platoon fires");
                    shotsBeforeMove = unit.ShotsFired;
                    rounds = (int[])world.Soldiers.RoundsRemaining.Clone();
                    reloads = (float[])world.Soldiers.ReloadReadyTimes.Clone();
                    world.IssueMove(0, unit.Position + Vector2.up * 20, Mathf.PI);
                    stage = 1;
                }
                else if (stage == 1 && world.Time >= 6)
                {
                    Require(unit.ShotsFired == shotsBeforeMove, "March interrupts further fire");
                    world.Halt(0);
                    Require(unit.DesiredFacing == unit.Facing, "Halt cancels arrival turn");
                    world.SetFormation(0, FormationKind.Column);
                    for (int i = 0; i < unit.SoldierCount; i++)
                    {
                        Require(world.Soldiers.RoundsRemaining[i] == rounds[i], "Reforming preserves every cartridge");
                        Require(world.Soldiers.ReloadReadyTimes[i] == reloads[i], "Movement preserves outstanding reloads");
                    }
                    unit.FireAtWill = false;
                    stage = 2;
                }
                else if (stage == 2 && world.Time >= 25)
                {
                    Require(!unit.Moving && !unit.Reforming, "Column settles after interrupted march");
                    world.SetFormation(0, FormationKind.Line);
                    world.IssueRangedAttack(0, 1);
                    stage = 3;
                }
                else if (stage == 3 && world.Time >= 50)
                {
                    Require(unit.ShotsFired > shotsBeforeMove, "Reformed line resumes firing after reload");
                    Require(!unit.Moving && !unit.Reforming, "Firing line finishes reforming");
                    shotsBeforeSquare = unit.ShotsFired;
                    world.SetFormation(0, FormationKind.Square);
                    stage = 4;
                }
                else if (stage == 4 && world.Time >= 75)
                {
                    Require(!unit.Reforming, "Square settles after line fire");
                    Require(unit.ShotsFired > shotsBeforeSquare, "Square resumes independent fire after its reformation");
                    Debug.Log("INFANTRY_ORDERS_NATIVE_PASSED shots=" + unit.ShotsFired + " time=" + world.Time);
                    stage = 5;
                }
            }
            catch (Exception error)
            { Debug.LogError("INFANTRY_ORDERS_NATIVE_FAILED stage=" + stage + " " + error.Message); stage = 5; }
        }
    }
}
