using UnityEngine;
namespace EmpireBattle
{
    public static class MeleePresentationTiming
    {
        public static float Phase(MeleeAction action,float start,float end,float clock)
        {
            float duration=Mathf.Max(.01f,end-start),age=Mathf.Clamp(clock-start,0,duration);
            // Authored attacks reach full extension at half the clip. Keep that pose
            // on the simulation contact timestamp, including the faster butt strike.
            if(MeleeSystem.IsStrike(action))
            {
                float contact=Mathf.Min(MeleeSystem.ImpactDelay(action),duration*.9f);
                return age<=contact?.5f*age/contact:.5f+.5f*(age-contact)/(duration-contact);
            }
            // These actions are consequences of contact, so begin in the deflected
            // pose and recover rather than winding up after the hit already occurred.
            if(action==MeleeAction.Parry||action==MeleeAction.Recoil||action==MeleeAction.Stagger)return .5f+.5f*age/duration;
            return age/duration;
        }
    }
}
