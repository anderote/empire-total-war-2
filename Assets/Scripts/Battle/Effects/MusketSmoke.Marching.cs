using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class MusketSmoke
    {
        const int MarchDustFrameLimit=64;
        float[] marchDustCredit;
        int[] marchDustSoldier;
        int marchDustUnit;
        float previousMarchDustTime;
        public int MarchDustEmissions { get; private set; }

        void InitializeMarchingDust()
        {
            marchDustCredit=new float[world.Battalions.Length];
            marchDustSoldier=new int[world.Battalions.Length];
            previousMarchDustTime=PresentationTime!=null?PresentationTime():world.Time;
            marchDustUnit=0;MarchDustEmissions=0;
        }

        void EmitMarchingDust(float now)
        {
            float elapsed=now-previousMarchDustTime;
            previousMarchDustTime=now;
            // Pause produces no dust; time jumps never trigger a backlog of footsteps.
            if(elapsed<=0||marchDustCredit==null||world.Battalions.Length==0)return;
            float dt=Mathf.Min(elapsed,.1f);
            if(battleView&&battleView.AtlasBlend>.9f)return;
            int emitted=0,start=marchDustUnit;
            for(int offset=0;offset<world.Battalions.Length;offset++)
            {
                int id=(start+offset)%world.Battalions.Length;
                var unit=world.Battalions[id];
                Vector3 center=world.Terrain.SurfacePoint(unit.Position);
                var half=FormationMath.HalfExtents(unit);
                float extent=Mathf.Max(half.x,half.y)+8;
                if(unit.AliveCount<=0||(center-battleCamera.transform.position).sqrMagnitude>450*450||
                    !GeometryUtility.TestPlanesAABB(frustum,new Bounds(center,new Vector3(extent*2,12,extent*2))))
                {marchDustCredit[id]=0;continue;}
                float pace=Mathf.Clamp(unit.ActualSpeed/Mathf.Max(.2f,world.Config.MarchSpeed),.6f,2.8f);
                marchDustCredit[id]=Mathf.Min(8,marchDustCredit[id]+dt*(5+Mathf.Sqrt(unit.AliveCount)*1.3f)*pace);
                int wanted=Mathf.FloorToInt(marchDustCredit[id]);
                if(wanted==0)continue;
                int moving=0,examined=0;
                // Sample actual movement, so halts, dead men and airborne ragdolls never kick up soil.
                while(moving<wanted&&examined<Mathf.Min(unit.SoldierCount,96))
                {
                    int soldier=unit.SoldierStart+marchDustSoldier[id];
                    marchDustSoldier[id]=(marchDustSoldier[id]+1)%unit.SoldierCount;examined++;
                    float speed=world.Soldiers.MovementSpeeds[soldier];
                    if(!world.Soldiers.Alive[soldier]||speed<.2f||world.Soldiers.IsKnockedDown(soldier,now))continue;
                    moving++;
                    Vector2 point=world.Soldiers.Positions[soldier];
                    if((world.Terrain.SurfacePoint(point)-battleCamera.transform.position).sqrMagnitude>450*450)continue;
                    float soil=world.Config.EnableLandscape?RuralFieldLayout.Surface(point/(world.Config.FieldHalfSize/1500f)).a:.8f;
                    var profile=world.Terrain.Profile;
                    float aridity=profile!=null?profile.Aridity:0,snow=profile!=null?profile.Snow:0;
                    float intensity=Mathf.Lerp(.38f,1f,soil)*Mathf.Lerp(1f,1.5f,aridity)*Mathf.Lerp(1f,.55f,world.Config.WeaponWetness);
                    float running=Mathf.InverseLerp(world.Config.MarchSpeed*1.25f,world.Config.MarchSpeed*2.5f,speed);
                    Color tint=Color.Lerp(new Color(.38f,.33f,.22f),new Color(.53f,.42f,.28f),soil);
                    tint=Color.Lerp(tint,new Color(.68f,.53f,.32f),aridity);
                    tint=Color.Lerp(tint,new Color(.85f,.89f,.91f),snow);
                    Vector2 velocity=world.Soldiers.Velocities[soldier];
                    Vector2 behind=velocity.sqrMagnitude>.001f?velocity.normalized:Vector2.zero;
                    point-=behind*.15f;
                    point+=new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*.18f;
                    int slot=AddPuff(world.Terrain.SurfacePoint(point,.08f),Vector3.zero,now,Mathf.Lerp(.3f,.65f,running)*Mathf.Lerp(.8f,1.2f,soil),false);
                    puffs[slot].MarchDust=true;puffs[slot].GunPuff=true;
                    puffs[slot].Jet=new Vector3(-behind.x*.12f,.08f+running*.12f,-behind.y*.12f);
                    puffs[slot].Lifetime=Mathf.Lerp(1.15f,2.1f,running);
                    puffs[slot].Tint=tint;
                    puffs[slot].Opacity=Mathf.Clamp01(intensity*Mathf.Lerp(.16f,.34f,running));
                    MarchDustEmissions++;emitted++;
                    if(emitted>=MarchDustFrameLimit)
                    {marchDustCredit[id]=0;marchDustUnit=(id+1)%world.Battalions.Length;return;}
                }
                marchDustCredit[id]-=wanted;
                if(moving==0)marchDustCredit[id]=0;
            }
            marchDustUnit=(start+1)%world.Battalions.Length;
        }
    }
}
