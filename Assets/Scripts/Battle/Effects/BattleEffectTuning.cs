using System;
using UnityEngine;

namespace EmpireBattle
{
    // Presentation only. These values never change damage, projectile physics or blast radius.
    [Serializable]
    public sealed class BattleEffectProfile
    {
        public float SmokeSize=1, SmokeDensity=1, SmokeLifetime=1, Wind=1;
        public float BloodAmount=1, BloodSize=1, DebrisAmount=1, ExplosionFlash=1;
        public BattleEffectProfile Copy()=>JsonUtility.FromJson<BattleEffectProfile>(JsonUtility.ToJson(this));
        public void Validate()
        {
            SmokeSize=Bound(SmokeSize,.1f,3);SmokeDensity=Bound(SmokeDensity,0,3);
            SmokeLifetime=Bound(SmokeLifetime,.1f,3);Wind=Bound(Wind,0,3);
            BloodAmount=Bound(BloodAmount,0,3);BloodSize=Bound(BloodSize,.1f,3);
            DebrisAmount=Bound(DebrisAmount,0,3);ExplosionFlash=Bound(ExplosionFlash,0,3);
        }
        static float Bound(float v,float min,float max)=>float.IsNaN(v)||float.IsInfinity(v)?1:Mathf.Clamp(v,min,max);
    }
    public static class BattleEffectTuning
    {
        public const float BloodIntensity=.49f;
        public static int BloodCount(float count)=>Mathf.Max(0,Mathf.RoundToInt(count*BloodIntensity));
        public static float BloodPoolScale=>Mathf.Sqrt(BloodIntensity);
        public static BattleEffectProfile Current {get;set;}=new BattleEffectProfile();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()=>ReloadGamePreset();
        public static void ReloadGamePreset()
        {
            Current=new BattleEffectProfile();
            var data=EmpireCampaign.Geography.CampaignResourceData.Load("BattleEffectPreset");
            if(data==null)return;
            try {var value=JsonUtility.FromJson<BattleEffectProfile>(data.text);if(value!=null){value.Validate();Current=value;}}
            catch(Exception e){Debug.LogWarning("Effect preset ignored: "+e.Message);}
        }
    }
}
