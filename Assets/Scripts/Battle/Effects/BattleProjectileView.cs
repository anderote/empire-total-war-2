using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    public static class ProjectileVisibility
    {
        // Distance adjusts streak length; streak visibility is retained at every zoom.
        public static float DistanceBlend(Camera camera,Vector3 position)
            => Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(45f,220f,Vector3.Distance(camera.transform.position,position)));
        public static float Width(Camera camera,Vector3 position,float pixels,float minimum,float maximum,float atlasBlend=0f)
        {
            float depth=Mathf.Max(.1f,Vector3.Dot(position-camera.transform.position,camera.transform.forward));
            float height=camera.orthographic?2f*camera.orthographicSize:2f*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
            return Mathf.Clamp(height*pixels/Mathf.Max(1,camera.pixelHeight),minimum,Mathf.Lerp(maximum,64f,atlasBlend));
        }
    }

    // Sampled bullets and bounded debris/dust; effects use the originating rifle's fire time.
    public sealed class BattleProjectileView : MonoBehaviour
    {
        struct Event
        {
            public Vector3 Start,End,Normal;
            public float Born,Duration;
            public bool Hit,Killed,Active,Impact,ImpactEmitted,Tracer;
            public SmallArmsSurface Surface;
            public CombatShot Source;
            public int Sequence;
        }
        readonly Event[] events=new Event[2048];
        readonly Matrix4x4[] shots=new Matrix4x4[256],tracers=new Matrix4x4[256],blood=new Matrix4x4[256];
        BattleWorld world;CombatSystem combat;Camera cameraView;BattleView battleView;
        MusketSmoke smoke;BattleAudio audio;SceneryImpactMap scenery;
        Mesh cube;Material bulletMaterial,bloodMaterial,tracerMaterial;
        readonly Material[] debrisMaterials=new Material[4];
        readonly Matrix4x4[][] debris=new[] { new Matrix4x4[256],new Matrix4x4[256],new Matrix4x4[256],new Matrix4x4[256] };
        readonly int[] debrisCounts=new int[4];
        int observed,cursor;
        public Func<float> PresentationTime;
        public int ImpactsEmitted { get; private set; }
        public void Initialize(BattleWorld value,CombatSystem source,Camera camera,MusketSmoke clouds=null,BattleAudio sounds=null,SceneryImpactMap surfaces=null)
        {
            battleView=GetComponent<BattleView>();
            source.BulletScenery=surfaces;
            world=value;combat=source;cameraView=camera;smoke=clouds;audio=sounds;scenery=surfaces;
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube=Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);Destroy(primitive);
            var template=Resources.Load<Material>("CannonLit");
            bulletMaterial=new Material(template){enableInstancing=true};
            bloodMaterial=new Material(template){enableInstancing=true};
            tracerMaterial=new Material(Resources.Load<Material>("CannonFlash")){enableInstancing=true};
            tracerMaterial.SetColor("_BaseColor",new Color(.85f,.76f,.53f));
            for(int i=0;i<debrisMaterials.Length;i++)
            {debrisMaterials[i]=new Material(template){enableInstancing=true};debrisMaterials[i].SetColor("_BaseColor",SurfaceColor((SmallArmsSurface)i));}
            bulletMaterial.SetColor("_BaseColor",new Color(.22f,.20f,.17f));
            bloodMaterial.SetColor("_BaseColor",new Color(.30f,.015f,.012f));

        }
        void LateUpdate()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Projectiles);
            if(world==null||!bulletMaterial||!cameraView)return;
            float atlas=battleView?battleView.AtlasBlend:0f;
            tracerMaterial.SetColor("_BaseColor",Color.Lerp(new Color(.85f,.76f,.53f),new Color(.12f,.16f,.20f),atlas));
            int first=Mathf.Max(observed+1,combat.ShotSequence-combat.Shots.Length+1);
            for(int sequence=first;sequence<=combat.ShotSequence;sequence++)
            {
                var shot=combat.Shots[(sequence-1)%combat.Shots.Length];
                if(shot.Sequence!=sequence)continue;
                var e=new Event { Source=shot,Sequence=shot.Sequence,Start=shot.Start,End=shot.End,Born=shot.Time,Duration=shot.Duration,
                    Hit=shot.Hit,Killed=shot.Killed,Active=true,Tracer=shot.Sequence%3==0,Impact=shot.Grounded,Surface=SmallArmsSurface.Dirt,Normal=Vector3.up };
                if(!e.Hit&&scenery!=null)
                {
                    // Follow the curved path instead of a center-to-center line through scenery.
                    Vector3 velocity=(e.End-e.Start)/e.Duration+Vector3.up*(4.905f*e.Duration);
                    Vector3 previous=e.Start;
                    for(int segment=1;segment<=16;segment++)
                    {
                        float t=e.Duration*segment/16f;
                        Vector3 next=e.Start+velocity*t-Vector3.up*(4.905f*t*t);
                        if(scenery.Trace(previous,next,out Vector3 position,out Vector3 normal,out SmallArmsSurface surface))
                        {
                            float fraction=Vector3.Distance(previous,position)/Mathf.Max(.001f,Vector3.Distance(previous,next));
                            e.End=position;e.Normal=normal;e.Surface=surface;e.Impact=true;
                            e.Duration=e.Duration*(segment-1+fraction)/16f;break;
                        }
                        previous=next;
                    }
                }
                events[cursor]=e;cursor=(cursor+1)%events.Length;
            }
            observed=combat.ShotSequence;
            float now=PresentationTime!=null?PresentationTime():world.Time;
            int sc=0,bc=0,tc=0;
            Array.Clear(debrisCounts,0,debrisCounts.Length);
            for(int i=0;i<events.Length;i++)
            {
                var e=events[i];if(!e.Active)continue;
                if(e.Source!=null&&e.Source.Sequence==e.Sequence&&(e.Source.Hit||e.Source.Grounded)&&e.Source.Duration<=e.Duration)
                {
                    e.End=e.Source.End;e.Duration=e.Source.Duration;e.Hit=e.Source.Hit;
                    e.Killed=e.Source.Killed;e.Impact=e.Source.Grounded;
                    if(e.Source.Grounded){e.Surface=SmallArmsSurface.Dirt;e.Normal=Vector3.up;}
                    events[i]=e;
                }
                float age=now-e.Born;
                if(age>e.Duration+3f){events[i].Active=false;continue;}
                if(age>=0&&age<e.Duration)
                {
                    Vector3 velocity=(e.End-e.Start)/e.Duration+Vector3.up*(4.905f*e.Duration);
                    Vector3 position=e.Start+velocity*age-Vector3.up*(4.905f*age*age);
                    Vector3 tangent=velocity-Vector3.up*(9.81f*age);
                    float distant=ProjectileVisibility.DistanceBlend(cameraView,position);
                    if(e.Tracer&&age>.0001f)
                    {
                        float tailTime=Mathf.Min(age,Mathf.Lerp(.008f,.035f,distant));
                        Vector3 tail=WeaponBallistics.PositionAt(e.Start,velocity,age-tailTime);
                        float width=ProjectileVisibility.Width(cameraView,position,Mathf.Lerp(.9f,1.4f,atlas),.012f,.6f,atlas);
                        tracers[tc++]=Matrix4x4.TRS((position+tail)*.5f,Quaternion.LookRotation(position-tail),
                            new Vector3(width,width,Vector3.Distance(position,tail)));
                        if(tc==tracers.Length){Draw(tracers,tc,tracerMaterial);tc=0;}
                    }
                    float shotWidth=Mathf.Lerp(.012f,ProjectileVisibility.Width(cameraView,position,1.3f,.012f,.6f,atlas),atlas);
                    shots[sc++]=Matrix4x4.TRS(position,Quaternion.LookRotation(tangent),new Vector3(shotWidth,shotWidth,Mathf.Max(.65f,shotWidth*2)));
                    if(sc==shots.Length){Draw(shots,sc,bulletMaterial);sc=0;}
                }
                else if(age>=e.Duration)
                {
                    float t=age-e.Duration;
                    if(e.Impact&&!e.Hit&&!e.ImpactEmitted)
                    {
                        events[i].ImpactEmitted=true;ImpactsEmitted++;
                        Color tint=e.Surface==SmallArmsSurface.Masonry?new Color(.65f,.61f,.51f,.4f):
                            e.Surface==SmallArmsSurface.Foliage?new Color(.36f,.42f,.22f,.14f):new Color(.45f,.36f,.23f,.36f);
                        if(t<.5f)
                        {
                            if(e.Surface==SmallArmsSurface.Dirt)
                            {
                                Vector3 incomingVelocity=(e.End-e.Start)/Mathf.Max(.001f,e.Duration)
                                    -Vector3.up*(WeaponBallistics.Gravity*.5f*e.Duration);
                                smoke?.EmitGroundDust(e.End,incomingVelocity,e.Born+e.Duration);
                            }
                            else smoke?.EmitImpact(e.End,e.Normal,tint,.3f);
                            audio?.SmallArmsImpact(e.End,i);
                        }
                    }
                    if(e.Hit)
                    {
                        for(int fleck=0;fleck<BattleEffectTuning.BloodCount(3);fleck++)
                        {
                            float angle=i*2.39f+fleck*2.1f;
                            Vector3 pos=e.End+new Vector3(Mathf.Cos(angle)*t*.5f,t*.7f-2*t*t,Mathf.Sin(angle)*t*.5f);
                            float ground=world.Terrain.HeightAt(new Vector2(pos.x,pos.z))+.035f;pos.y=Mathf.Max(pos.y,ground);
                            float size=(e.Killed?.07f:.045f)*Mathf.Clamp01((3-t)*2);
                            blood[bc++]=Matrix4x4.TRS(pos,Quaternion.identity,new Vector3(size,pos.y<=ground+.01f?.015f:size,size));
                            if(bc==blood.Length){Draw(blood,bc,bloodMaterial);bc=0;}
                        }
                    }
                    else if(e.Impact&&t<1.3f)
                    {
                        for(int fleck=0;fleck<5;fleck++)
                        {
                            float seed=RifleFireTiming.Variation(i,fleck+117),angle=seed*Mathf.PI*2;
                            bool leaf=e.Surface==SmallArmsSurface.Foliage;
                            Vector3 velocity=e.Normal*(.5f+seed)+new Vector3(Mathf.Cos(angle),1.2f+seed,Mathf.Sin(angle))*(leaf?.7f:1.1f);
                            Vector3 position=e.End+velocity*t-Vector3.up*((leaf?1.4f:4.905f)*t*t);
                            float ground=world.Terrain.HeightAt(new Vector2(position.x,position.z))+.02f;position.y=Mathf.Max(ground,position.y);
                            float size=(leaf?.065f:.035f)*(1-t/1.3f);
                            var scale=leaf?new Vector3(size,.009f,size*1.4f):Vector3.one*size;
                            int kind=(int)e.Surface;
                            debris[kind][debrisCounts[kind]++]=Matrix4x4.TRS(position,Quaternion.Euler(t*250+seed*360,angle*Mathf.Rad2Deg,t*170),scale);
                            if(debrisCounts[kind]==debris[kind].Length){Draw(debris[kind],debrisCounts[kind],debrisMaterials[kind]);debrisCounts[kind]=0;}
                        }
                    }
                }
            }
            Draw(shots,sc,bulletMaterial);Draw(tracers,tc,tracerMaterial);Draw(blood,bc,bloodMaterial);
            for(int kind=0;kind<4;kind++)Draw(debris[kind],debrisCounts[kind],debrisMaterials[kind]);
        }
        static Color SurfaceColor(SmallArmsSurface kind) => kind==SmallArmsSurface.Foliage?new Color(.24f,.34f,.09f):
            kind==SmallArmsSurface.Masonry?new Color(.62f,.57f,.45f):kind==SmallArmsSurface.Wood?new Color(.38f,.24f,.10f):new Color(.34f,.27f,.15f);
        void Draw(Matrix4x4[] matrices,int count,Material material)
        {if(count>0)Graphics.DrawMeshInstanced(cube,0,material,matrices,count,null,ShadowCastingMode.Off,true,0,cameraView,LightProbeUsage.Off);}
        void OnDestroy(){if(cube)Destroy(cube);if(bulletMaterial)Destroy(bulletMaterial);if(tracerMaterial)Destroy(tracerMaterial);if(bloodMaterial)Destroy(bloodMaterial);foreach(var material in debrisMaterials)if(material)Destroy(material);}
    }
}
