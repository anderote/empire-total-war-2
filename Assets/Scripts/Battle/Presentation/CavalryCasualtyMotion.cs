using EmpireBattle.Art;
using UnityEngine;
namespace EmpireBattle
{
    // Presentation travel is bounded independently of projectile velocity and never changes combat results.
    public static class CavalryCasualtyMotion
    {
        public struct Pose
        {
            public Vector3 HorsePosition,RiderPosition;
            public MountedMotion HorseMotion,RiderMotion;
            public float HorsePhase,RiderPhase;
            public float HorseYaw;
        }
        public static Pose Sample(SoldierDeath death,float time)
        {
            float age=Mathf.Max(0,time-death.Born);
            Vector3 forward=Quaternion.Euler(0,death.Facing*Mathf.Rad2Deg,0)*Vector3.forward;
            Vector3 lateral=Vector3.Cross(Vector3.up,forward);
            Vector3 velocity=death.Velocity;velocity.y=0;
            float speed=Mathf.Min(10,velocity.magnitude);
            Vector3 travel=speed>.1f?velocity.normalized:forward;
            Vector3 impact=death.Impulse;impact.y=0;impact=Vector3.ClampMagnitude(impact,1);
            float variation=RifleFireTiming.Variation(death.Soldier,317);
            float sign=variation<.5f?-1:1;
            float duration=speed>5?1.55f:speed>2.5f?1.8f:2.05f;
            float phase=Mathf.Clamp01(age/duration);
            // Ease travel to rest during the fall; corpse positions remain fixed afterward.
            float settle=1-(1-phase)*(1-phase);
            var pose=new Pose{RiderPhase=phase};
            pose.RiderMotion=speed>5
                ?variation<.55f?MountedMotion.RiderTumble:variation<.7f?MountedMotion.RiderFallBack:variation<.85f?MountedMotion.RiderFallSide:MountedMotion.RiderFall
                :variation<.33f?MountedMotion.RiderFallBack:variation<.67f?MountedMotion.RiderFallSide:MountedMotion.RiderFall;
            pose.RiderPosition=death.Position+travel*(Mathf.Min(3.2f,speed*.32f)*settle)
                +lateral*(sign*1.35f*settle)+impact*((death.Cannon?.65f:.18f)*settle);
            // Added lift peaks during separation and reaches zero at the grounded baked endpoint.
            pose.RiderPosition.y=death.Position.y+Mathf.Sin(phase*Mathf.PI)*(speed>5?.55f:.22f);
            pose.HorsePosition=death.Position;
            // Surviving horses are simulated and rendered independently by BattleWorld.
            // Heavy shot buckles the mount promptly; momentum carries it into a short skid.
            float horseDuration=death.Cannon?Mathf.Lerp(1.3f,.9f,Mathf.Clamp01(death.Impulse.magnitude/10f)):1.65f;
            pose.HorseMotion=MountedMotion.HorseFall;pose.HorsePhase=Mathf.Clamp01(age/horseDuration);
            float stop=1-(1-pose.HorsePhase)*(1-pose.HorsePhase);
            pose.HorsePosition+=travel*(Mathf.Min(2.4f,speed*.24f)*stop)+impact*((death.Cannon?1.25f:.25f)*stop);
            pose.HorseYaw=death.Cannon?Vector3.Dot(impact,lateral)*28f*Mathf.SmoothStep(0,1,pose.HorsePhase):0f;
            // Input Position is a terrain point; renderer resamples terrain after horizontal travel.
            pose.HorsePosition.y=0;pose.RiderPosition.y-=death.Position.y;
            return pose;
        }
    }
}
