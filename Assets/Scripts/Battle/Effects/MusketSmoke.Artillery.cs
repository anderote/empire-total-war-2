using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class MusketSmoke
    {
        public void EmitPoisonGas(Vector3 center,float born,int seed,bool burst,float strength=1f)
        {
            if(world==null||random==null||strength<=0)return;
            for(int i=0;i<(burst?28:10)&&gasPuffCount<PoisonGasCapacity;i++)
            {
                float angle=i*2.399963f+seed;
                float radius=Mathf.Sqrt(RifleFireTiming.Variation(seed+i,227))*(burst?6f:ArtillerySystem.PoisonGasRadius*.8f);
                var p=new Vector2(center.x+Mathf.Sin(angle)*radius,center.z+Mathf.Cos(angle)*radius);
                int id=AddPuff(world.Terrain.SurfacePoint(p,.65f),Vector3.zero,born,
                    Mathf.Lerp(5f,7f,RifleFireTiming.Variation(seed+i,229)),false);
                puffs[id].GunPuff=true;puffs[id].PoisonGas=true;gasPuffCount++;
                puffs[id].Jet=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(burst?3f:.35f);
                puffs[id].Tint=new Color(.72f,.61f,.14f);
                puffs[id].Opacity=(burst?1.6f:1.25f)*strength;puffs[id].Lifetime=6f;
            }
        }
        public void EmitLimeCloud(Vector3 center,float born,int seed,bool burst,float strength=1f)
        {
            if(world==null||random==null||strength<=0)return;
            int count=burst?24:8;
            for(int i=0;i<count;i++)
            {
                float angle=i*2.399963f+seed;
                float radius=Mathf.Sqrt(RifleFireTiming.Variation(seed+i,211))*(burst?5f:12f);
                Vector2 p=new Vector2(center.x+Mathf.Sin(angle)*radius,center.z+Mathf.Cos(angle)*radius);
                Vector3 source=world.Terrain.SurfacePoint(p,burst?.35f:.8f);
                int id=AddPuff(source,Vector3.zero,born,Mathf.Lerp(3.8f,6f,RifleFireTiming.Variation(seed+i,223)),false);
                puffs[id].GunPuff=true;puffs[id].LimeDust=true;
                puffs[id].Jet=new Vector3(Mathf.Sin(angle),.2f,Mathf.Cos(angle))*(burst?3f:.35f);
                puffs[id].Tint=new Color(.83f,.81f,.69f);
                puffs[id].Opacity=(burst?.8f:.5f)*strength;puffs[id].Lifetime=burst?5f:4f;
            }
        }
        /// <summary>Buoyant smoke starts at the flame tips and detaches into the shared wind.</summary>
        public void EmitFireSmoke(Vector3 source,float born,float width,float strength,bool smouldering=false)
        {
            if(world==null||random==null||strength<=0||firePuffCount>=FireSmokeCapacity)return;
            int id=AddPuff(source,Vector3.zero,born,width,false);
            puffs[id].FireSmoke=true;puffs[id].GunPuff=true;
            firePuffCount++;
            puffs[id].Jet=Vector3.up*.25f;
            puffs[id].Tint=smouldering?new Color(.09f,.085f,.075f):new Color(.19f,.18f,.17f);
            puffs[id].Opacity=(smouldering?2.2f:1.1f)*Mathf.Clamp01(strength);
            puffs[id].Lifetime=smouldering?8f:5.5f;
        }
        public void EmitArtilleryBurst(ArtilleryImpact hit,Vector3 source)
        {
            if(world==null||random==null)return;
            bool carcass=hit.AmmunitionType==ArtilleryAmmunition.Carcass;
            // Several narrow, uneven jets preserve the force and direction of the blast.
            // Only ground strikes throw dirt; contact bursts above ground emit powder smoke.
            if(hit.TerrainStrike)
            {
                for(int jet=0;jet<7;jet++)
                {
                    int seed=hit.Sequence*37+jet;
                    float angle=jet*2.399963f+RifleFireTiming.Variation(seed,3);
                    Vector3 direction=new Vector3(Mathf.Sin(angle)*3.1f,Mathf.Lerp(4.8f,10f,RifleFireTiming.Variation(seed,5)),Mathf.Cos(angle)*3.1f);
                    if(carcass)direction=direction*.6f+ArtilleryEffectShape.Forward(hit.Direction)*2f;
                    for(int segment=0;segment<4;segment++)
                    {
                        float along=(segment+1)/4f;
                        int id=AddPuff(source,Vector3.zero,hit.Time,.48f+along*.6f,false);
                        puffs[id].GunPuff=true;puffs[id].BlastJet=true;
                        puffs[id].Jet=direction*along;
                        puffs[id].Tint=new Color(.26f,.20f,.13f);
                        puffs[id].Opacity=1.15f;puffs[id].Lifetime=1.5f+along*1.2f;
                    }
                }
                for(int i=0;i<10;i++)
                {
                    float angle=i*2.399963f+hit.Sequence;
                    int id=AddPuff(source,Vector3.zero,hit.Time,1.4f,false);
                    puffs[id].GunPuff=true;puffs[id].GroundBlast=true;
                    puffs[id].Jet=new Vector3(Mathf.Sin(angle),.05f,Mathf.Cos(angle))*(1.5f+RifleFireTiming.Variation(i+hit.Sequence,7)*1.8f);
                    puffs[id].Tint=new Color(.48f,.39f,.27f);puffs[id].Opacity=.45f;puffs[id].Lifetime=2.4f;
                }
            }
            for(int i=0;i<9;i++)
            {
                float angle=i*2.399963f+hit.Sequence;
                int id=AddPuff(source+Vector3.up*.2f,Vector3.zero,hit.Time,1.5f+RifleFireTiming.Variation(i+hit.Sequence,11),false);
                puffs[id].GunPuff=true;
                puffs[id].Jet=new Vector3(Mathf.Sin(angle)*1.2f,.7f+(i%3)*.65f,Mathf.Cos(angle)*1.2f);
                puffs[id].Tint=new Color(.43f,.42f,.38f);puffs[id].Opacity=.56f;puffs[id].Lifetime=5f+i*.3f;
            }
        }
    }
}
