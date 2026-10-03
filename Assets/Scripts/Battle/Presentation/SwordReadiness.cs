using UnityEngine;
namespace EmpireBattle
{
    // Presentation-only state; never delays, changes or schedules a combat hit.
    public sealed class SwordReadiness
    {
        readonly float[] amounts, clocks;
        readonly bool[] initialized;
        public SwordReadiness(int count){amounts=new float[count];clocks=new float[count];initialized=new bool[count];}
        public float Sample(BattleWorld world,BattalionState unit,int soldier,float clock)
        {
            if(unit.Role!=UnitRole.Swordsman)return 0;
            var s=world.Soldiers;
            bool active=s.MeleeEnds[soldier]>clock;
            bool ready=!InfantryContactMovement.Fleeing(unit)&&(unit.InMelee||unit.AttackMode==InfantryAttackMode.BayonetCharge||active);
            if(!initialized[soldier]){initialized[soldier]=true;amounts[soldier]=ready?1:0;}
            else amounts[soldier]=Mathf.MoveTowards(amounts[soldier],ready?1:0,Mathf.Max(0,clock-clocks[soldier])/(ready?.38f:.5f));
            clocks[soldier]=clock;
            // An actual attack owns its pose. Keep the carry endpoint ready so
            // finishing a strike returns continuously to guard before lowering.
            if(active&&MeleeSystem.IsStrike(s.MeleeActions[soldier]))amounts[soldier]=1;
            return Mathf.SmoothStep(0,1,amounts[soldier]);
        }
        public static bool UsesCarry(BattalionState unit,MeleeAction action,float end,float clock)
            =>unit.Role==UnitRole.Swordsman&&(end<=clock||action==MeleeAction.None||action==MeleeAction.Ready);
    }
}
