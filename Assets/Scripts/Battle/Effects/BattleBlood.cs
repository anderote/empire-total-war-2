using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace EmpireBattle
{
    /// <summary>Ballistic droplets and permanent terrain-conforming stains; pools spread and dry in simulation time.</summary>
    public sealed partial class BattleBlood : MonoBehaviour
    {
        readonly Plane[] frustum = new Plane[6];
        struct Mark { public Vector3 Position; public float Born, Size, Seed; public int Soldier; public bool Pool, Waiting, Splash; }
        struct Drop { public bool Active; public Vector3 Position, Velocity; public float Born, Size, Seed; }
        const int DropCapacity=4096;
        public const float SprayDrawDistance=800f,MarkDrawDistance=1200f;
        readonly Drop[] drops=new Drop[DropCapacity];
        readonly List<Mark> marks=new List<Mark>();
        readonly List<int> waitingMarks=new List<int>();
        readonly Matrix4x4[] matrices=new Matrix4x4[256];
        readonly Vector4[] data=new Vector4[256];
        BattleView battleView;
        BattleWorld world;Camera cameraView;BattleCasualtyView casualties;
        Material material,dropletMaterial;Mesh patch,droplet;Texture2D heights;MaterialPropertyBlock properties;
        System.Random random;int cursor;float now;
        public int PermanentMarks => marks.Count;
        public int MusketWoundSprays { get; private set; }
        public int FragmentTrailDrops { get; private set; }
        public int ActiveDroplets { get; private set; }
        public void Initialize(BattleWorld value,Camera camera,BattleCasualtyView owner)
        {
            battleView=GetComponent<BattleView>();
            world=value;cameraView=camera;casualties=owner;random=new System.Random(value.Config.Seed^73129);
            var shader=Resources.Load<Shader>("BattleBlood");
            if(!shader){Debug.LogError("Battle blood shader missing.");enabled=false;return;}
            material=new Material(shader){enableInstancing=true,name="Spreading wet blood"};properties=new MaterialPropertyBlock();
            dropletMaterial=new Material(material){name="Airborne blood"};
            dropletMaterial.SetFloat("_CraterStencilComp",(float)CompareFunction.Always);
            heights=new Texture2D(257,257,TextureFormat.RFloat,false,true){name="Blood terrain heights",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var values=new float[257*257];float half=world.Config.FieldHalfSize;
            for(int z=0;z<257;z++)for(int x=0;x<257;x++)values[z*257+x]=world.Terrain.HeightAt(new Vector2(Mathf.Lerp(-half,half,x/256f),Mathf.Lerp(-half,half,z/256f)));
            heights.SetPixelData(values,0);heights.Apply(false,true);material.SetTexture("_TerrainHeights",heights);material.SetFloat("_FieldHalf",half);
            patch=new Mesh{name="Subdivided blood surface"};const int n=9;var vertices=new Vector3[n*n];var uv=new Vector2[n*n];var triangles=new int[(n-1)*(n-1)*6];int k=0;
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){int a=z*n+x;vertices[a]=new Vector3(x/(float)(n-1)*2-1,0,z/(float)(n-1)*2-1);uv[a]=new Vector2(x/(float)(n-1),z/(float)(n-1));if(x==n-1||z==n-1)continue;triangles[k++]=a;triangles[k++]=a+n;triangles[k++]=a+1;triangles[k++]=a+1;triangles[k++]=a+n;triangles[k++]=a+n+1;}
            patch.vertices=vertices;patch.uv=uv;patch.triangles=triangles;patch.RecalculateNormals();patch.RecalculateBounds();
            // The shader displaces vertices onto terrain; generous bounds prevent CPU draw culling before displacement.
            patch.bounds=new Bounds(Vector3.zero,new Vector3(2,1000,2));
            world.MusketWounded+=EmitMusketWound;
            droplet=new Mesh{name="Blood droplet octahedron"};droplet.vertices=new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back};droplet.triangles=new[]{0,4,3,0,3,5,0,5,2,0,2,4,1,3,4,1,5,3,1,2,5,1,4,2};droplet.RecalculateNormals();droplet.RecalculateBounds();
        }
        float Range(float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
        public void Emit(SoldierDeath death,float time)
        {
            if(!material)return;
            waitingMarks.Add(marks.Count);
            marks.Add(new Mark{Position=death.Position,Born=time+1.25f,Size=(death.Cannon?Range(.91f,1.4f):Range(.49f,.91f))*Mathf.Lerp(1f,1.3f,SlowMotionEffects.Amount(battleView,death.Position)),Seed=Range(0,100),Soldier=death.Soldier,Pool=true,Waiting=true});
            Vector3 direction=death.Impulse.sqrMagnitude>.01f?death.Impulse.normalized:
                new Vector3(Mathf.Sin(death.Facing),0,Mathf.Cos(death.Facing));
            EmitSpray(death.Position+Vector3.up*.95f,direction,death.Cannon,time);
        }
        public void EmitSpray(Vector3 position,Vector3 direction,bool cannon,float time)
        {
            if(!material)return;
            float emphasis=SlowMotionEffects.Amount(battleView,position);
            int count=Mathf.Clamp(BattleEffectTuning.BloodCount(SlowMotionEffects.BloodCount(cannon,emphasis)*BattleEffectTuning.Current.BloodAmount),0,DropCapacity);
            for(int i=0;i<count;i++)
            {
                // A narrow initial jet plus a wider fan of heavy droplets reads at battle camera distance.
                float spread=i<count/3?.35f:cannon?1.8f:1.4f;
                drops[cursor]=new Drop{Active=true,Position=position+Vector3.up*Range(-.15f,.15f),
                    Velocity=direction*Range(cannon?2f:1.7f,cannon?5.5f:5.2f)+new Vector3(Range(-spread,spread),Range(.7f,cannon?3f:3.5f),Range(-spread,spread)),
                    Born=time,Size=Range(cannon?.018f:.022f,cannon?.045f:.055f)*BattleEffectTuning.Current.BloodSize,Seed=Range(0,100)};
                cursor=(cursor+1)%DropCapacity;
            }
        }
        void EmitMusketWound(int soldier,Vector3 direction)
        {
            if(!material)return;
            bool rider=world.Battalions[world.Soldiers.BattalionIds[soldier]].Kind==UnitKind.Cavalry;
            Vector3 position=world.SoldierPosition(soldier,world.Time)+Vector3.up*(rider?1.8f:1.05f);
            // Deaths have their own spray and pool. Wounds leave ballistic spatter only.
            EmitSpray(position,direction.sqrMagnitude>.001f?direction.normalized:Vector3.forward,false,casualties?casualties.VisualTime:now);
            MusketWoundSprays++;
        }
        public void EmitFragmentTrail(Vector3 position,Vector3 velocity,float time)
        {
            if(!material)return;
            for(int i=0;i<BattleEffectTuning.BloodCount(2);i++)
            {
                drops[cursor]=new Drop{Active=true,Position=position,
                    Velocity=velocity*.45f+new Vector3(Range(-.45f,.45f),Range(-.6f,.15f),Range(-.45f,.45f)),
                    Born=time,Size=Range(.025f,.05f),Seed=Range(0,100)};
                cursor=(cursor+1)%DropCapacity;FragmentTrailDrops++;
            }
        }
        public void EmitFragmentLanding(Vector3 position,float time)
        {
            if(!material)return;
            marks.Add(new Mark{Position=position,Born=time,Size=Range(.3f,.55f),Seed=Range(0,100),Soldier=-1,Splash=true});
        }
        public void Step(float dt,float time)
        {
            now=time;ActiveDroplets=0;
            if(world.Config.AssetReview&&marks.Count>4096)
            {
                int removed=marks.Count-4096;marks.RemoveRange(0,removed);waitingMarks.Clear();
                for(int i=0;i<marks.Count;i++)if(marks[i].Waiting)waitingMarks.Add(i);
            }
            EmitMeleeContacts(time);
            if(BattleOptimizationSettings.FastForwardOptimizations)
            {
                for(int slot=waitingMarks.Count-1;slot>=0;slot--)
                {
                    int index=waitingMarks[slot];
                    if(!SettleMark(index,time))continue;
                    waitingMarks[slot]=waitingMarks[waitingMarks.Count-1];waitingMarks.RemoveAt(waitingMarks.Count-1);
                }
            }
            else for(int i=0;i<marks.Count;i++)if(marks[i].Waiting)SettleMark(i,time);
            for(int i=0;i<drops.Length;i++)
            {
                var drop=drops[i];if(!drop.Active)continue;
                Vector3 previous=drop.Position;drop.Position=WeaponBallistics.PositionAt(previous,drop.Velocity,dt);drop.Velocity+=Vector3.down*(9.81f*dt);
                float height=world.Terrain.HeightAt(new Vector2(drop.Position.x,drop.Position.z));
                if(drop.Position.y<=height)
                {
                    // Refine the first terrain intersection so fast spatter does not deposit beyond its landing point.
                    Vector3 low=previous,high=drop.Position;for(int r=0;r<6;r++){var mid=(low+high)*.5f;if(mid.y>world.Terrain.HeightAt(new Vector2(mid.x,mid.z)))low=mid;else high=mid;}
                    // Every visible droplet leaves a permanent wet ground splash.
                    marks.Add(new Mark{Position=high,Born=time,Size=Range(.14f,.32f),Seed=drop.Seed,Soldier=-1,Splash=true});
                    drop.Active=false;
                }
                if(time-drop.Born>3)drop.Active=false;
                drops[i]=drop;if(drop.Active)ActiveDroplets++;
            }
        }
        public void RetireReviewSoldiers(int unitId)
        {
            if(!world.Config.AssetReview)throw new System.InvalidOperationException("Review blood retirement requires a workbench world.");
            for(int i=0;i<marks.Count;i++)
            {
                var mark=marks[i];if(!mark.Waiting||world.Soldiers.BattalionIds[mark.Soldier]!=unitId)continue;
                mark.Position=casualties.BloodAnchor(mark.Soldier);
                mark.Position.y=world.Terrain.HeightAt(new Vector2(mark.Position.x,mark.Position.z));
                mark.Waiting=false;marks[i]=mark;
            }
        }
        bool SettleMark(int index,float time)
        {
            var mark=marks[index];
            if(!mark.Waiting)return true;
            if(casualties&&casualties.IsPhysical(mark.Soldier)){mark.Born=time+.15f;marks[index]=mark;return false;}
            if(time<mark.Born)return false;
            if(casualties)mark.Position=casualties.BloodAnchor(mark.Soldier);
            mark.Position.y=world.Terrain.HeightAt(new Vector2(mark.Position.x,mark.Position.z));
            mark.Waiting=false;marks[index]=mark;return true;
        }
        public void WarmRendering(Camera camera)
        {
            if(!material)return;
            // Camera-specific draw primes the instanced surface and droplet variants without creating marks.
            matrices[0]=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one);
            data[0]=new Vector4(0,1,1,1);properties.SetVectorArray("_BloodData",data);
            Graphics.DrawMeshInstanced(patch,0,material,matrices,1,properties,ShadowCastingMode.Off,false,30,camera,LightProbeUsage.Off);
            matrices[0]=Matrix4x4.TRS(Vector3.up,Quaternion.identity,Vector3.one*.05f);
            Graphics.DrawMeshInstanced(droplet,0,dropletMaterial,matrices,1,properties,ShadowCastingMode.Off,false,30,camera,LightProbeUsage.Off);
        }
        void LateUpdate()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Blood);
            if(!material||!cameraView)return;
            material.SetFloat("_BloodTime",now);int count=0;
            dropletMaterial.SetFloat("_BloodTime",now);
            var eye=cameraView.transform.position;GeometryUtility.CalculateFrustumPlanes(cameraView,frustum);
            float poolScale=BattleEffectTuning.BloodPoolScale;
            foreach(var mark in marks)
            {
                if(mark.Waiting||(mark.Position-eye).sqrMagnitude>MarkDrawDistance*MarkDrawDistance||!GeometryUtility.TestPlanesAABB(frustum,new Bounds(mark.Position,Vector3.one*Mathf.Max(3f,mark.Size*2.5f))))continue;
                matrices[count]=Matrix4x4.TRS(mark.Position,Quaternion.Euler(0,mark.Seed*113,0),new Vector3(mark.Size*poolScale,1,mark.Size*poolScale*(.7f+Mathf.Repeat(mark.Seed,.5f))));
                // Pooling starts during the body's settling period; show the
                // accumulated blood as soon as its stable ground anchor is ready.
                data[count++]=new Vector4(mark.Pool?mark.Born-3.5f:mark.Born,mark.Seed,mark.Pool?1:mark.Splash?-1:0,1);
                if(count==256){Draw(patch,count);count=0;}
            }
            Draw(patch,count);count=0;
            foreach(var drop in drops)if(drop.Active)
            {
                if((drop.Position-eye).sqrMagnitude>SprayDrawDistance*SprayDrawDistance||!GeometryUtility.TestPlanesAABB(frustum,new Bounds(drop.Position,Vector3.one*Mathf.Max(.3f,drop.Size*8))))continue;
                float size=drop.Size;
                matrices[count]=Matrix4x4.TRS(drop.Position,Quaternion.FromToRotation(Vector3.up,drop.Velocity.normalized),new Vector3(size,size*Mathf.Clamp(drop.Velocity.magnitude*.3f,1.2f,3f),size));data[count++]=new Vector4(drop.Born,drop.Seed,0,0);
                if(count==256){Draw(droplet,count);count=0;}
            }
            Draw(droplet,count);
        }
        void Draw(Mesh mesh,int count)
        {
            if(count==0)return;
            properties.SetVectorArray("_BloodData",data);
            Graphics.DrawMeshInstanced(mesh,0,mesh==droplet?dropletMaterial:material,matrices,count,properties,ShadowCastingMode.Off,false,0,cameraView,LightProbeUsage.Off);
        }
        void OnDestroy(){if(world!=null)world.MusketWounded-=EmitMusketWound;DestroyOwned(material);DestroyOwned(dropletMaterial);DestroyOwned(patch);DestroyOwned(droplet);DestroyOwned(heights);}
        static void DestroyOwned(Object value)
        {
            if(!value)return;
            if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);
        }
    }
}
