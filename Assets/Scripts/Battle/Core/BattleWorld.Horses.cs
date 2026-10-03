using System.Collections.Generic;
using UnityEngine;
namespace EmpireBattle
{
    public sealed class LooseHorse
    {
        public int Rider, UnitId;
        public ArmySide Side;
        public Vector2 Position, Exit;
        public float Facing, Speed, RunSpeed, GaitPhase, Health = 100f;
        public bool Escaped;
        public SoldierDeath Death;
        public bool Targetable => Health > 0f && !Escaped;
    }

    public sealed partial class BattleWorld
    {
        // Separate from the rider roster: horses never keep a defeated army alive or count twice as losses.
        public readonly Dictionary<int, LooseHorse> LooseHorses = new Dictionary<int, LooseHorse>();

        void ReleaseHorse(ref SoldierDeath death)
        {
            if (death.Kind != UnitKind.Cavalry || death.Cause != DeathCause.Musket) return;
            // A separate seeded stream preserves the existing soldier combat random sequence.
            var horseRandom = new System.Random(unchecked(Config.Seed ^ death.Soldier * 486187739 ^ 9173));
            if (horseRandom.NextDouble() < Mathf.Clamp01(Config.ShotHorseDeathChance)) return;
            death.HorseSurvived = true;
            float edge = Config.FieldHalfSize + 8f;
            float along = ((float)horseRandom.NextDouble() * 2f - 1f) * Config.FieldHalfSize;
            int side = horseRandom.Next(4);
            Vector2 exit = side == 0 ? new Vector2(-edge, along) : side == 1 ? new Vector2(edge, along)
                : side == 2 ? new Vector2(along, -edge) : new Vector2(along, edge);
            LooseHorses.Add(death.Soldier, new LooseHorse { Rider = death.Soldier, UnitId = death.UnitId,
                Side = death.Side, Position = new Vector2(death.Position.x, death.Position.z), Exit = exit,
                Facing = death.Facing, Speed = Mathf.Min(10f, death.Velocity.magnitude),
                RunSpeed = Mathf.Lerp(7.6f, 9.6f, (float)horseRandom.NextDouble()), GaitPhase = death.GaitPhase });
        }

        void StepLooseHorses(float dt)
        {
            foreach (var horse in LooseHorses.Values)
            {
                if (!horse.Targetable) continue;
                Vector2 delta = horse.Exit - horse.Position;
                horse.Facing = FormationMath.TurnTowards(horse.Facing, FormationMath.FacingOf(delta), dt * 2.4f);
                horse.Speed = Mathf.MoveTowards(horse.Speed, horse.RunSpeed, dt * 4f);
                float alignment = Mathf.Max(0f, Vector2.Dot(FormationMath.Forward(horse.Facing), delta.normalized));
                float travel = Mathf.Min(delta.magnitude, horse.Speed * alignment * dt);
                horse.Position += FormationMath.Forward(horse.Facing) * travel;
                horse.GaitPhase = Mathf.Repeat(horse.GaitPhase + travel / 4f, 1f);
                // Clear the whole model beyond the edge before removing it from rendering and targeting.
                horse.Escaped = Mathf.Abs(horse.Position.x) > Config.FieldHalfSize + 4f
                    || Mathf.Abs(horse.Position.y) > Config.FieldHalfSize + 4f;
            }
        }

        public bool ApplyHorseHit(int rider, float damage, DeathCause cause, Vector3 impulse)
        {
            if (!Finite(damage) || damage <= 0f || !LooseHorses.TryGetValue(rider, out var horse) || !horse.Targetable) return false;
            horse.Health = Mathf.Max(0f, horse.Health - damage);
            if (horse.Health <= 0f)
                horse.Death = new SoldierDeath { Soldier = rider, UnitId = horse.UnitId, Side = horse.Side,
                    Kind = UnitKind.Cavalry, Cause = cause, Position = Terrain.SurfacePoint(horse.Position),
                    Facing = horse.Facing, Born = Time, Impulse = impulse,
                    Velocity = new Vector3(Mathf.Sin(horse.Facing), 0, Mathf.Cos(horse.Facing)) * horse.Speed };
            return true;
        }
    }
}
