using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    public sealed partial class CannonView
    {
        Mesh burstQuad;
        Material burstMaterial;
        MaterialPropertyBlock burstProperties;
        BattleFireSprites fireSprites;
        void InitializeBursts()
        {
            burstQuad=new Mesh { name="Ragged artillery flash billboard" };
            burstQuad.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            burstQuad.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
            burstQuad.triangles=new[]{0,1,2,0,2,3};burstQuad.RecalculateBounds();owned.Add(burstQuad);
            burstMaterial=new Material(Resources.Load<Shader>("ArtilleryBurst"));owned.Add(burstMaterial);
            burstProperties=new MaterialPropertyBlock();
            fireSprites=new BattleFireSprites();
            InitializeBurningUnits();
        }
        void DrawGroundFlames(Vector3 root,Vector3 incoming,float time,int seed,float strength,float spread)
        {
            bool close=(cameraView.transform.position-root).sqrMagnitude<80f*80f;
            for(int patch=0;patch<ArtilleryEffectShape.PatchCount;patch++)
            {
                Vector3 pool=ArtilleryEffectShape.FuelPatch(seed,patch);
                for(int layer=0;layer<(close?2:1);layer++)
                {
                    Vector2 local=layer==0?new Vector2(pool.x,pool.y):ArtilleryEffectShape.FlamePoint(seed,patch);
                    Vector3 point=root+ArtilleryEffectShape.WorldOffset(local*spread,incoming);
                    point.y=world.Terrain.HeightAt(new Vector2(point.x,point.z))+.025f;
                    float height=Mathf.Lerp(1.3f,2.15f,RifleFireTiming.Variation(seed+patch+layer,167));
                    fireSprites.Add(point,pool.z*(layer==0?1.15f:.8f),height*(layer==0?1:.7f),seed+patch*13+layer*113,strength*(layer==0?.9f:.55f));
                }
            }
        }
        void DrawBurstCard(Vector3 position,Vector2 size,float age,int seed,float strength,float fuel=0)
        {
            burstProperties.SetVector("_BurstData",new Vector4(age,seed%997,strength,fuel));
            Graphics.DrawMesh(burstQuad,Matrix4x4.TRS(position,cameraView.transform.rotation,
                new Vector3(size.x,size.y,1)),burstMaterial,0,cameraView,0,burstProperties,ShadowCastingMode.Off,false);
        }
        void DrawBursts()
        {
            foreach(var hit in artillery.Impacts)
            {
                if(hit==null||!hit.Detonates||hit.Sequence<=0)continue;
                float age=TimeNow-hit.Time;
                if(age<0||age>1.2f)continue;
                bool carcass=hit.AmmunitionType==ArtilleryAmmunition.Carcass;
                if(age<.28f)
                {
                    float flash=Mathf.Pow(1f-age/.28f,1.5f)*1.6f*BattleEffectTuning.Current.ExplosionFlash;
                    float extent=(1.4f+age*12f)*Mathf.Lerp(1,1.2f,SlowMotionEffects.Amount(battleView,hit.Position));
                    DrawBurstCard(hit.Position+Vector3.up*.8f,new Vector2(extent*1.7f,extent),age,hit.Sequence,flash);
                }
                // Brief tongues of ignited powder fan out, then disappear into the dirt cloud.
                for(int jet=0;jet<7;jet++)
                {
                    int jetSeed=hit.Sequence*37+jet;
                    float life=Mathf.Lerp(.32f,.62f,RifleFireTiming.Variation(jetSeed,179));
                    float t=age/life;if(t>=1)continue;
                    float angle=jet*2.399963f+hit.Sequence;
                    Vector3 direction=new Vector3(Mathf.Sin(angle),Mathf.Lerp(.35f,1.5f,RifleFireTiming.Variation(jetSeed,181)),Mathf.Cos(angle)).normalized;
                    float length=Mathf.Lerp(2.5f,4.6f,RifleFireTiming.Variation(jetSeed,191))*(1f-Mathf.Exp(-age*35f));
                    fireSprites.AddJet(hit.Position+Vector3.up*.25f+direction*age*1.4f,direction,
                        .8f+length*.16f,length,jetSeed,Mathf.Pow(1f-t,1.2f)*BattleEffectTuning.Current.ExplosionFlash);
                }
                if(!carcass)continue;
                // Ballistic visual droplets land in the same forward fan as the ground pools.
                int seed=Mathf.RoundToInt(hit.Position.x*13f+hit.Position.z*19f);
                for(int i=1;i<=18;i++)
                {
                    int patch=1+(i-1)%6;
                    float duration=.65f+RifleFireTiming.Variation(seed+i,97)*.45f;
                    float t=age/duration;if(t>=1)continue;
                    Vector2 local=ArtilleryEffectShape.FlamePoint(seed,patch+7*((i-1)/6));
                    Vector3 end=hit.Position+ArtilleryEffectShape.WorldOffset(local,hit.Direction);
                    end.y=world.Terrain.HeightAt(new Vector2(end.x,end.z))+.09f;
                    Vector3 point=Vector3.Lerp(hit.Position+Vector3.up*.18f,end,t);
                    point.y+=4f*t*(1-t)*Mathf.Lerp(.7f,1.8f,RifleFireTiming.Variation(seed+i,101));
                    float size=Mathf.Lerp(.18f,.4f,RifleFireTiming.Variation(seed+i,103));
                    DrawBurstCard(point,new Vector2(size,size*1.7f),age,seed+i,(1f-t)*.85f,1);
                }
            }
        }
    }
}
