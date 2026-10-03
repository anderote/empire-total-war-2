using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Shared phase rules for production poses and the animation review workbench.</summary>
    public static class AnimationPlayback
    {
        public static int Frame(int count,float phase,bool loop,out float blend)
        {
            blend=0;
            if(count<=1)return 0;
            if(float.IsNaN(phase)||float.IsInfinity(phase))phase=0;
            float frame=(loop?Mathf.Repeat(phase,1):Mathf.Clamp01(phase))*(loop?count:count-1);
            int index=Mathf.Min(count-1,Mathf.FloorToInt(frame));
            blend=index==count-1&&!loop?0:frame-index;
            return index;
        }

        public static float InfantryGait(BattleWorld world,int soldier,float clock)
        {
            var unit=world.Battalions[world.Soldiers.BattalionIds[soldier]];
            float stride=FormationMath.SoldierStride(soldier,unit.Running||InfantryContactMovement.Fleeing(unit),world.Soldiers.MovementSpeeds[soldier],unit.AttackMode==InfantryAttackMode.BayonetCharge,world.Soldiers.Captains[soldier]||world.Soldiers.PikeSergeants[soldier],FormationMath.ExpandedInfantryGaits(unit));
            return Mathf.Repeat(world.Soldiers.GaitPhases[soldier]
                +Mathf.Clamp(clock-world.Time,0,.05f)*world.Soldiers.MovementSpeeds[soldier]/stride,1);
        }

        public static float MountedGait(float phase,float speed,float clock,float simulationTime)
        {
            // Idle gait phases stop advancing in movement simulation; breathing must keep playing.
            if(speed<=.08f)return Mathf.Repeat(phase+clock/3f,1);
            float stride=speed>5?4f:speed>2.5f?2.6f:1.9f;
            return Mathf.Repeat(phase+Mathf.Clamp(clock-simulationTime,0,.05f)*speed/stride,1);
        }
    }
}
