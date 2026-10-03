using System;
using EmpireBattle;
using UnityEngine;

public static class RoundshotHitVerification
{
    static void Require(bool value, string message)
    { if (!value) throw new Exception("Roundshot hit verification: " + message); }

    static BattleWorld World(out ArtillerySystem artillery)
    {
        var world = new BattleWorld(new BattleConfig { BattalionsPerSide = 1,
            SoldiersPerBattalion = 8, EnableAI = false, EnableLandscape = false,
            FrenchArtillery = 0, BritishArtillery = 0 });
        artillery = new ArtillerySystem(world);
        // Deliberately outside the regiment's nominal bounds: hits must use actual men.
        int start = world.Battalions[1].SoldierStart;
        for (int i = 0; i < 8; i++) world.Soldiers.Positions[start + i] = new Vector2(110 + i, 0);
        for (int i = 0; i < 4; i++) world.Soldiers.Positions[start + i] = new Vector2(100, i * 2);
        return world;
    }

    static CannonProjectile Shot(ArtillerySystem artillery, Vector3 origin, Vector3 velocity,
        ArtilleryAmmunition ammunition = ArtilleryAmmunition.Roundshot)
    {
        var shot = artillery.Projectiles[0];
        shot.Active = true; shot.FromCamera = true; shot.Side = ArmySide.French;
        shot.Position = origin; shot.Velocity = velocity; shot.AmmunitionType = ammunition;
        return shot;
    }

    public static void Verify()
    {
        foreach (float dt in new[] { .025f, .05f, .1f })
        {
            var world = World(out var artillery);
            int start = world.Battalions[1].SoldierStart, events = 0;
            world.SoldierDied += _ => events++;
            var shot = Shot(artillery, new Vector3(100, 1, -1), new Vector3(0, 0, 80));
            for (int i = 0; i < Mathf.RoundToInt(.2f / dt); i++) artillery.Step(dt);
            Require(world.DeathCount == 4 && events == 4 && shot.HitCount == 4,
                "A ball kills all four struck ranks across ticks, without the old three-hit cap");
            for (int i = 0; i < 8; i++)
                Require(world.Soldiers.Alive[start + i] == (i >= 4), "Only the intersected soldiers die");
            Require(world.Battalions[1].AliveCount == 4, "Living roster matches deaths");
            for (int i = 0; i < 4; i++)
                Require(world.Deaths[start + i].Position == new Vector3(100, 0, i * 2)
                    && world.Deaths[start + i].Cause == DeathCause.Roundshot, "Corpse is at the struck soldier");
            Require(!world.KillSoldier(start, DeathCause.Roundshot, Vector3.zero, Vector3.zero)
                && events == 4, "An already dead soldier cannot die again");
            world.StepMovement(.05f);
            world.ApplyCasualties(1, 99);
            Require(world.DeathCount == 8 && events == 8, "Reformation and subsequent roster casualties remain valid");
        }
        foreach (float height in new[] { 1f, 3f })
        {
            var world = World(out var artillery);
            Shot(artillery, new Vector3(height == 1f ? 102 : 100, height, -1), new Vector3(0, 0, 80));
            artillery.Step(.1f);
            Require(world.DeathCount == 0, "A lateral miss or shot above the men kills nobody");
        }
        {
            var world = World(out var artillery);
            int start = world.Battalions[1].SoldierStart;
            // Reverse roster order; a canister pellet must hit the nearest man, then stop.
            world.Soldiers.Positions[start] = new Vector2(100, 6);
            world.Soldiers.Positions[start + 3] = new Vector2(100, 0);
            var pellet = Shot(artillery, new Vector3(100, 1, -1), new Vector3(0, 0, 80), ArtilleryAmmunition.Canister);
            artillery.Step(.1f);
            Require(world.DeathCount == 1 && !world.Soldiers.Alive[start + 3]
                && world.Soldiers.Alive[start] && !pellet.Active && pellet.Position.z < 0,
                "Canister stops at its first actual contact in flight order");
        }
        {
            var world = World(out var artillery);
            world.SetFormation(1, FormationKind.Skirmish);
            Shot(artillery, new Vector3(100, 1, -1), new Vector3(0, 0, 80));
            artillery.Step(.1f);
            Require(world.DeathCount == 4, "A direct hit is lethal even in skirmish formation");
        }
        Debug.Log("ROUNDSHOT_HITS_VERIFIED: exact victims, successive ranks, timestep consistency, misses, corpse positions, roster accounting and canister flight order.");
    }

    public static void BuildMatchedPlaytests()
    {
        Verify();
        BallisticsVerification.Verify();
        CasualtyVerification.Verify();
        InfantryBattleBuild.BuildMatchedPlaytests();
    }
}
