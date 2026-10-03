using UnityEngine;

namespace EmpireBattle
{
    public static class InfantryRecoveryTiming
    {
        public const float RiseSeconds = 2.4f;
        public static float RiseDuration(int soldier) => RiseSeconds*Mathf.Lerp(.8f,1.3f,RifleFireTiming.Variation(soldier,419));
        public static float GroundPause(int soldier,float damage) => Mathf.Lerp(.35f,1.45f,RifleFireTiming.Variation(soldier,421)) + Mathf.Clamp01(damage/100f)*.7f;

        // Existing recovery pairs alternate back/front; select in the impact's local hemisphere.
        public static int Variant(int soldier,Vector3 impulse,float facing,int count)
        {
            if(count<=0)return -1;
            Vector2 direction=new Vector2(impulse.x,impulse.z);
            int parity=Vector2.Dot(direction,FormationMath.Forward(facing))>=0?1:0;
            int choices=(count+1-parity)/2;
            if(choices<=0)return 0;
            int choice=Mathf.Min(choices-1,(int)(RifleFireTiming.Variation(soldier,137)*choices));
            return parity+choice*2;
        }
        public static int RecoveryVariant(BattleWorld world,int soldier,int count)
        {
            int chosen=world.Soldiers.KnockdownRecoveryVariants[soldier];
            return chosen>=0?Mathf.Min(chosen,Mathf.Max(0,count-1)):Variant(soldier,world.Soldiers.KnockdownImpulses[soldier],world.Soldiers.KnockdownFacings[soldier],count);
        }
        public static Quaternion Facing(Vector3 impulse,float originalFacing,int variant)
        {
            Vector2 direction=new Vector2(impulse.x,impulse.z);
            if(direction.sqrMagnitude<.0001f)return Quaternion.Euler(0,originalFacing*Mathf.Rad2Deg,0);
            float facing=FormationMath.FacingOf(direction)+(variant%2==0?Mathf.PI:0f);
            return Quaternion.Euler(0,facing*Mathf.Rad2Deg,0);
        }
        public static Quaternion AirborneRotation(int soldier,Vector3 impulse,float age,float landing)
        {
            if(age<=0||age>=landing||landing<=.01f)return Quaternion.identity;
            float phase=Mathf.Clamp01(age/landing),envelope=Mathf.Sin(phase*Mathf.PI);
            float power=Mathf.Clamp01(impulse.magnitude/9f);
            Vector3 axis=Vector3.Cross(Vector3.up,new Vector3(impulse.x,0,impulse.z)).normalized;
            if(axis.sqrMagnitude<.01f)axis=Vector3.right;
            float pitch=Mathf.Lerp(20f,110f,power)*Mathf.Lerp(.7f,1.3f,RifleFireTiming.Variation(soldier,423));
            float roll=Mathf.Lerp(-38f,38f,RifleFireTiming.Variation(soldier,427))*power;
            // World-space tumble follows the outward impulse and settles to the matched pose.
            return Quaternion.AngleAxis(envelope*pitch,axis)*Quaternion.AngleAxis(envelope*roll,impulse.normalized);
        }
        public static float Phase(BattleWorld world,int soldier,float clock,out bool rising)
        {
            float remaining=world.Soldiers.KnockdownEnds[soldier]-clock;
            float rise=RiseDuration(soldier);rising=remaining<rise;
            float fall=Mathf.Clamp(world.Soldiers.KnockdownLandings[soldier],.35f,.85f)*Mathf.Lerp(.8f,1.2f,RifleFireTiming.Variation(soldier,431));
            return rising?Mathf.Clamp01(1f-remaining/rise):Mathf.Clamp01((clock-world.Soldiers.KnockdownStarts[soldier])/fall);
        }
        public static Quaternion Rotation(BattleWorld world,int soldier,float clock,int variant)
        {
            var s=world.Soldiers;float age=Mathf.Max(0,clock-s.KnockdownStarts[soldier]);
            float phase=Phase(world,soldier,clock,out bool rising);
            var standing=Quaternion.Euler(0,(rising?s.Facings[soldier]:s.KnockdownFacings[soldier])*Mathf.Rad2Deg,0);
            var fall=s.KnockdownRecoveryVariants[soldier]>=0?Quaternion.Euler(0,s.KnockdownRecoveryFacings[soldier]*Mathf.Rad2Deg,0):Facing(s.KnockdownImpulses[soldier],s.KnockdownFacings[soldier],variant);
            var heading=rising?Quaternion.Slerp(fall,standing,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1f,phase))):Quaternion.Slerp(standing,fall,Mathf.SmoothStep(0,1,age/.25f));
            return AirborneRotation(soldier,s.KnockdownImpulses[soldier],age,s.KnockdownLandings[soldier])*heading;
        }
    }
}
