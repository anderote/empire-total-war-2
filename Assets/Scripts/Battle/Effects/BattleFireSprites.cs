using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    /// <summary>Shared flipbook fire: fixed buffers, no emitter objects or per-frame allocations.</summary>
    public sealed class BattleFireSprites : System.IDisposable
    {
        const int BatchSize=256;
        public const int MaxCards=4096;
        readonly Matrix4x4[] matrices=new Matrix4x4[BatchSize];
        readonly Vector4[] data=new Vector4[BatchSize];
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        readonly Plane[] planes=new Plane[6];
        readonly Mesh quad;
        readonly Material material;
        Camera camera;
        Quaternion rotation;
        int count;
        float time;
        public int Cards {get;private set;}
        public int DrawCalls {get;private set;}
        public BattleFireSprites()
        {
            quad=new Mesh {name="Pooled flipbook fire quad"};
            quad.vertices=new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(.5f,1,0),new Vector3(-.5f,1,0)};
            quad.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};quad.triangles=new[]{0,1,2,0,2,3};quad.RecalculateBounds();
            material=new Material(Resources.Load<Shader>("CarcassFire")){enableInstancing=true};
            material.SetTexture("_MainTex",Resources.Load<Texture2D>("CarcassFireFlipbook"));
        }
        public void Begin(Camera view,float clock)
        {
            camera=view;time=clock;Cards=DrawCalls=count=0;
            GeometryUtility.CalculateFrustumPlanes(view,planes);
            float tilt=Mathf.Lerp(.1f,.65f,Mathf.Abs(view.transform.forward.y));
            Vector3 up=Vector3.Slerp(Vector3.up,view.transform.up,tilt);
            rotation=Quaternion.LookRotation(Vector3.Cross(view.transform.right,up),up);
        }
        public void Add(Vector3 root,float width,float height,int seed,float strength)
            => AddOriented(root,width,height,seed,strength,rotation);

        public void AddJet(Vector3 root,Vector3 direction,float width,float length,int seed,float strength)
        {
            Vector3 up=Vector3.ProjectOnPlane(direction,camera.transform.forward);
            if(up.sqrMagnitude<.01f)up=camera.transform.up;
            up.Normalize();
            AddOriented(root,width,length,seed,strength,Quaternion.LookRotation(camera.transform.forward,up));
        }

        void AddOriented(Vector3 root,float width,float height,int seed,float strength,Quaternion orientation)
        {
            if(Cards>=MaxCards||strength<=0||!GeometryUtility.TestPlanesAABB(planes,new Bounds(root+orientation*Vector3.up*height*.5f,Vector3.one*(width+height))))return;
            matrices[count]=Matrix4x4.TRS(root,orientation,new Vector3(width,height,1));
            float variation=RifleFireTiming.Variation(seed,163);
            data[count]=new Vector4(time*Mathf.Lerp(21f,29f,variation)+variation*60f,strength,seed%2==0?1:0,0);
            count++;Cards++;
            if(count==BatchSize)Flush();
        }
        public void Flush()
        {
            if(count==0)return;
            properties.SetVectorArray("_FireData",data);
            Graphics.DrawMeshInstanced(quad,0,material,matrices,count,properties,ShadowCastingMode.Off,false,0,camera,LightProbeUsage.Off);
            DrawCalls++;count=0;
        }
        public void Dispose()
        {
            if(Application.isPlaying){Object.Destroy(quad);Object.Destroy(material);}
            else {Object.DestroyImmediate(quad);Object.DestroyImmediate(material);}
        }
    }
}
