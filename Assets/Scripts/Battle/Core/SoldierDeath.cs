using UnityEngine;
namespace EmpireBattle
{
    public enum DeathCause { Musket, Melee, Roundshot, Shell, Fire, Lime, PoisonGas }
    public struct SoldierDeath
    {
        public int Soldier, UnitId;
        public ArmySide Side;
        public DeathCause Cause;
        public UnitKind Kind;
        public Vector3 Position, Impulse, Velocity;
        public float Facing, Born, ReloadRemaining, LastVolleyTime, GaitPhase, LastFireTime;
        public bool Moving, Running, Charging;
        public bool HorseSurvived;
        public bool Cannon => Cause == DeathCause.Roundshot || Cause == DeathCause.Shell;
    }
}
