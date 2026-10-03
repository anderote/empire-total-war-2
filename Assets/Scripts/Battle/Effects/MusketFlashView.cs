using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    // Instanced per-rifle flashes/sparks; eight shared nearby lights rather than one per shot.
    public sealed class MusketFlashView : MonoBehaviour
    {
        BattleWorld world;
        Camera cameraView;
        Light sun;
        Mesh quad;
        Material material;
        MaterialPropertyBlock properties;
        public Func<float> PresentationTime;
        readonly Matrix4x4[] matrices = new Matrix4x4[256];
        readonly Vector4[] colors = new Vector4[256];
        readonly Light[] lights = new Light[8];
        readonly float[] nearest = new float[8];
        int count;
        public int VisibleFlashes { get; private set; }
        public void Initialize(BattleWorld value, Camera camera, Light sunlight)
        {
            world=value;cameraView=camera;sun=sunlight;
            var template=Resources.Load<Material>("MusketFlashVariant");
            material=template ? new Material(template) : new Material(Resources.Load<Shader>("MusketFlash"));
            material.enableInstancing=true;
            quad=new Mesh { name="Musket flash billboard" };
            quad.vertices=new[] { new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0) };
            quad.uv=new[] { Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
            quad.triangles=new[] {0,1,2,0,2,3};quad.RecalculateBounds();
            properties=new MaterialPropertyBlock();
            for(int i=0;i<lights.Length;i++)
            {
                var go=new GameObject("Shared musket flash light");go.transform.SetParent(transform);
                lights[i]=go.AddComponent<Light>();lights[i].type=LightType.Point;
                lights[i].color=new Color(1f,.62f,.24f);lights[i].range=4f;
                lights[i].shadows=LightShadows.None;lights[i].intensity=0f;
            }
        }
        void LateUpdate()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Flashes);
            if(world==null||!cameraView||!material)return;
            float now=PresentationTime!=null?PresentationTime():world.Time;
            Vector3 eye=cameraView.transform.position;
            float brightness=sun?Mathf.Lerp(1.25f,.6f,Mathf.Clamp01(sun.intensity)):1f;
            count=VisibleFlashes=0;
            for(int i=0;i<lights.Length;i++){lights[i].intensity=0;nearest[i]=float.MaxValue;}
            for(int soldier=0;soldier<world.Soldiers.FireTimes.Length;soldier++)
            {
                float age=now-world.Soldiers.FireTimes[soldier];
                if(age<0||age>.24f)continue;
                Vector3 muzzle=world.Soldiers.FireOrigins[soldier],forward=world.Soldiers.FireDirections[soldier];
                float squared=(muzzle-eye).sqrMagnitude;
                if(squared>2000f*2000f)continue;
                bool cartridge=EmpireBattle.Art.CartridgeRifleLibrary.UsesCartridges(world.Battalions[world.Soldiers.BattalionIds[soldier]].Weapon);
                float seed=RifleFireTiming.Variation(soldier,71);
                if(age<.10f)
                {
                    float fade=1-age/.10f;
                    float distance=Mathf.Sqrt(squared);
                    float pixelScale=Mathf.Max(1f,distance*.002f)*(world.Soldiers.Captains[soldier]?.65f:1f);
                    Add(muzzle+forward*.06f,cameraView.transform.rotation,new Vector3((.42f+seed*.16f)*pixelScale,.28f*pixelScale,1),new Color(1.7f,.78f,.16f,fade*brightness));
                    if(!cartridge&&!world.Soldiers.Captains[soldier])Add(world.Soldiers.FireLockOrigins[soldier],cameraView.transform.rotation,Vector3.one*(.16f*pixelScale),new Color(1.4f,.65f,.12f,fade*.8f*brightness));
                    VisibleFlashes++;
                    // Keep only the closest eight flashes, with a short range and no shadows.
                    int farthest=0;
                    for(int i=1;i<nearest.Length;i++)if(nearest[i]>nearest[farthest])farthest=i;
                    if(squared<60f*60f&&squared<nearest[farthest])
                    {nearest[farthest]=squared;lights[farthest].transform.position=muzzle;lights[farthest].intensity=fade*brightness*1.6f;}
                }
                if(squared>160f*160f)continue;
                // Short ballistic powder sparks, occasional at the lock and always at the muzzle.
                for(int spark=0;spark<4;spark++)
                {
                    if(spark==3&&(cartridge||seed>.28f))continue;
                    float variation=RifleFireTiming.Variation(soldier,spark+89);
                    Vector3 right=new Vector3(forward.z,0,-forward.x);
                    Vector3 velocity=forward*(2.5f+variation*4f)+right*((variation-.5f)*2f)+Vector3.up*(.5f+seed*2f);
                    Vector3 origin=muzzle;
                    if(spark==3){origin=world.Soldiers.FireLockOrigins[soldier];velocity=right*.7f+Vector3.up*.9f;}
                    Vector3 position=origin+velocity*age-Vector3.up*(4.905f*age*age);
                    Add(position,cameraView.transform.rotation*Quaternion.Euler(0,0,variation*180),new Vector3(.016f,.045f,1),new Color(1.6f,.6f,.05f,(1-age/.24f)*brightness));
                }
            }
            Flush();
        }
        void Add(Vector3 position,Quaternion rotation,Vector3 scale,Color color)
        {
            matrices[count]=Matrix4x4.TRS(position,rotation,scale);colors[count++]=color;
            if(count==matrices.Length)Flush();
        }
        void Flush()
        {
            if(count==0)return;
            properties.SetVectorArray("_FlashColor",colors);
            Graphics.DrawMeshInstanced(quad,0,material,matrices,count,properties,ShadowCastingMode.Off,false,gameObject.layer,cameraView,LightProbeUsage.Off);
            count=0;
        }
        void OnDestroy(){if(material)Destroy(material);if(quad)Destroy(quad);}
    }
}
