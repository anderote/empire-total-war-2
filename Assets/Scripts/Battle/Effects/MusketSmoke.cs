using Unity.Profiling;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    /// <summary>Bounded visual black-powder smoke, driven by simulation time rather than wall time.</summary>
    public sealed partial class MusketSmoke : MonoBehaviour
    {
        static readonly ProfilerMarker PerformanceMarker = new ProfilerMarker("Battle.Smoke");
        const int Capacity = 32768;
        const int BatchSize = 256;
        struct Puff
        {
            public bool Alive;
            public Vector3 Origin, Jet;
            public Color Tint;
            public float Born, Lifetime, Seed, Width, Opacity, BornGust, SeedRoll;
            public bool Haze, GunPuff, Muzzle, Lock, PersistentPowder, MarchDust, ImpactDust, BlastJet, GroundBlast, FireSmoke, LimeDust, PoisonGas;
        }
        BattleWorld world;
        Camera battleCamera;
        Light sunlight;
        public Func<float> PresentationTime;
        public Vector3 WindVelocity = new Vector3(.66f, 0f, .23f);
        Material material;
        Mesh quad;
        MaterialPropertyBlock properties;
        System.Random random;
        Puff[] puffs;
        Vector3[] centers;
        float[] sortDepth;
        int[] sortIndices;
        int[] liveSlots,liveIndices;
        int liveCount;
        int firePuffCount;
        int gasPuffCount;
        public const int PoisonGasCapacity=2048;
        public int ActiveGasPuffs=>gasPuffCount;
        public const int FireSmokeCapacity=4096;
        public int ActiveFirePuffs=>firePuffCount;
        public int TrackedPuffs=>liveCount;
        readonly Plane[] frustum = new Plane[6];
        readonly Matrix4x4[] matrices = new Matrix4x4[BatchSize];
        readonly Vector4[] colors = new Vector4[BatchSize];
        readonly Vector4[] details = new Vector4[BatchSize];
        float[] observedVolley;
        int cursor;
        static readonly int SmokeColor = Shader.PropertyToID("_SmokeColor");
        static readonly int PuffData = Shader.PropertyToID("_PuffData");
        public int ActivePuffs { get; private set; }
        public int VisiblePuffs { get; private set; }
        BattleView battleView;
        public bool SoftIntersections; // Enable when the active renderer supplies a camera depth texture.

        public void Initialize(BattleWorld value, Camera camera, Light sun = null)
        {
            ReleaseResources();
            world = value;
            battleView=GetComponent<BattleView>();
            battleCamera = camera; sunlight = sun;
            random = new System.Random(world.Config.Seed ^ 95431);
            observedVolley = new float[world.Battalions.Length];
            for (int i = 0; i < observedVolley.Length; i++) observedVolley[i] = world.Battalions[i].LastVolleyTime;
            // Two dedicated slots per rifle keep fresh muzzle/lock smoke safe from
            // eviction by lingering clouds, even when both armies fire together.
            int poolSize = Capacity + world.Soldiers.Alive.Length * 2;
            puffs = new Puff[poolSize];
            liveSlots=new int[poolSize];liveIndices=new int[poolSize];
            Array.Fill(liveIndices,-1);liveCount=0;firePuffCount=gasPuffCount=0;
            centers = new Vector3[poolSize];
            sortDepth = new float[poolSize];
            sortIndices = new int[poolSize];
            ActivePuffs = cursor = 0;
            InitializeMarchingDust();
            Shader shader = Resources.Load<Shader>("MusketSmoke");
            if (!shader) shader = Shader.Find("EmpireBattle/MusketSmoke");
            if (!shader) { Debug.LogError("Musket smoke shader is missing."); enabled = false; return; }
            material = new Material(shader) { enableInstancing = true, name = "Pooled black powder smoke" };
            material.SetTexture("_FireSmokeTex",Resources.Load<Texture2D>("CarcassSmokeFlipbook"));
            quad = new Mesh { name = "Smoke billboard quad" };
            quad.vertices = new[] {new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0)};
            quad.uv = new[] {Vector2.zero, Vector2.right, Vector2.one, Vector2.up};
            quad.triangles = new[] {0,1,2,0,2,3};
            quad.RecalculateBounds();
            properties = new MaterialPropertyBlock();
            enabled = true;
        }

        void LateUpdate()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Smoke);
            using var performanceScope = PerformanceMarker.Auto();
            if (world == null || !battleCamera || !material || !quad) return;
            foreach (BattalionState unit in world.Battalions)
            {
                if (unit.LastVolleyTime <= observedVolley[unit.Id]) continue;
                observedVolley[unit.Id] = unit.LastVolleyTime;
                EmitVolley(unit);
            }
            float now = PresentationTime != null ? PresentationTime() : world.Time;
            float slowMotionStrength=SlowMotionEffects.Strength(battleView&&battleView.GetSpeed!=null?battleView.GetSpeed():1f);
            float extentScale=SlowMotionEffects.SmokeSize(slowMotionStrength);
            ActivePuffs = VisiblePuffs = 0;
            GeometryUtility.CalculateFrustumPlanes(battleCamera,frustum);
            EmitMarchingDust(now);
            Transform view=battleCamera.transform;
            Vector3 cameraPosition=view.position,cameraForward=view.forward,cameraUp=view.up,cameraRight=view.right;
            float currentGust=Mathf.Cos(now*.31f);
            for (int active = 0; active < liveCount;)
            {
                int i=liveSlots[active];
                if(!puffs[i].Alive){RemoveLive(active);continue;}
                Puff puff = puffs[i];
                float age = now - puff.Born;
                if (age < 0f) { active++; continue; } // Keep scheduled late shots until their firing time.
                if (age >= puff.Lifetime * BattleEffectTuning.Current.SmokeLifetime) { puffs[i].Alive = false; RemoveLive(active); continue; }
                active++;
                // One prevailing light breeze; local eddies stretch, rather than launch, the clouds.
                Vector3 wind;
                if(BattleOptimizationSettings.RenderWorkOptimizations)
                {
                    // Birth gust is immutable and the current gust is shared by
                    // every cloud. Preserve the integrated wind exactly.
                    float gust=(.22f/.31f)*(puff.BornGust-currentGust);
                    wind=(WindVelocity*age+new Vector3(gust,0f,gust*.4f))*BattleEffectTuning.Current.Wind;
                }
                else wind=WindDisplacement(puff.Born,now);
                // Lock gas hangs beside the gun before the breeze carries it away.
                if(puff.Lock)wind*=1f-Mathf.Exp(-age*.45f);
                if(puff.MarchDust)wind*=.35f;
                if(puff.ImpactDust)wind*=.12f;
                if(puff.GroundBlast)wind*=.2f;
                float turbulence = puff.GunPuff ? Mathf.Min(age * .35f, .6f) : Mathf.Min(age * .16f, 2.7f);
                Vector3 eddy = new Vector3(Mathf.Sin(puff.Seed + age*.17f), Mathf.Sin(puff.Seed*.7f + age*.12f)*.25f,
                    Mathf.Cos(puff.Seed*.83f + age*.13f)) * turbulence;
                if(puff.Lock)eddy*=.18f;
                if(puff.ImpactDust)eddy*=.12f;
                float rise = (puff.PersistentPowder ? .035f : puff.Haze ? .07f : .17f) * age + .55f * Mathf.Sqrt(age);
                if(puff.Lock)rise=.08f*age+.10f*Mathf.Sqrt(age);
                if(puff.MarchDust)rise=.12f*age+.08f*Mathf.Sqrt(age);
                if(puff.ImpactDust)rise=.04f*Mathf.Sqrt(age);
                if(puff.GroundBlast)rise=.06f*Mathf.Sqrt(age);
                if(puff.FireSmoke){rise=.7f*age;wind*=1f-Mathf.Exp(-age*.8f);}
                if(puff.LimeDust){rise=.12f*age;wind*=.25f;}
                if(puff.PoisonGas)wind=Vector3.ClampMagnitude(wind*.08f,.8f);
                centers[i] = puff.Origin + wind + eddy + Vector3.up * rise + puff.Jet * (1f - Mathf.Exp(-age * 3f));
                if(puff.PoisonGas)
                    centers[i].y=world.Terrain.HeightAt(new Vector2(centers[i].x,centers[i].z))+.65f+Mathf.Sin(puff.Seed+age)*.08f;
                ActivePuffs++;
                float extent=puff.Width*8f*extentScale*BattleEffectTuning.Current.SmokeSize+2f;
                if(!GeometryUtility.TestPlanesAABB(frustum,new Bounds(centers[i],Vector3.one*extent)))continue;
                sortDepth[VisiblePuffs] = -(centers[i] - cameraPosition).sqrMagnitude;
                sortIndices[VisiblePuffs++] = i;
            }
            // Transparent billboards blend back to front, including across instanced batches.
            Array.Sort(sortDepth, sortIndices, 0, VisiblePuffs);
            material.SetFloat("_SmokeTime", now);
            material.SetVector("_SunDirection", sunlight ? -sunlight.transform.forward : Vector3.up);
            material.SetColor("_SunColor", sunlight ? sunlight.color * Mathf.Min(1.5f, sunlight.intensity) : Color.gray);
            material.SetFloat("_SoftParticles", SoftIntersections ? 1f : 0f);
            if (SoftIntersections) battleCamera.depthTextureMode |= DepthTextureMode.Depth;
            int batchCount = 0;
            for (int index = 0; index < VisiblePuffs; index++)
            {
                int id = sortIndices[index];
                Puff puff = puffs[id];
                float age = now - puff.Born;
                float progress = age / (puff.Lifetime * BattleEffectTuning.Current.SmokeLifetime);
                float expansion = 1f - Mathf.Exp(-Mathf.Max(0f,age-.2f) * .16f);
                float width = puff.GunPuff ? puff.Width * (.3f + (1f - Mathf.Exp(-age * 4f)) * 1.2f) : puff.Haze ? puff.Width * (1.6f + expansion*2.8f) : puff.Width * (.16f + expansion*2.4f);
                float height = width * (puff.Haze ? .55f : .8f);
                if(puff.MarchDust){width=puff.Width*(.45f+1.35f*(1f-Mathf.Exp(-age*2.5f)));height=width*.6f;}
                if(puff.ImpactDust){width=puff.Width*(.4f+1.1f*(1f-Mathf.Exp(-age*8f)));height=width*.7f;}
                if(puff.GroundBlast){width=puff.Width*(.4f+1.6f*(1f-Mathf.Exp(-age*4f)));height=width*.32f;}
                if(puff.FireSmoke){width=puff.Width*(.75f+age*.35f);height=width*1.2f;}
                if(puff.LimeDust){width=puff.Width*(.65f+.65f*(1f-Mathf.Exp(-age*3f)));height=width*.65f;}
                if(puff.PoisonGas){width=puff.Width*(.65f+.8f*(1f-Mathf.Exp(-age*1.8f)));height=Mathf.Min(2.6f,width*.4f);}
                float appear = Mathf.Clamp01(age / (puff.GunPuff ? .025f : .12f));
                float fade = Mathf.Pow(1f - progress, puff.PersistentPowder ? .8f : 1.4f);
                float density = puff.Opacity * appear * fade / (1f + age*(puff.PersistentPowder ? .012f : .035f));
                float roll=((BattleOptimizationSettings.RenderWorkOptimizations?puff.SeedRoll:Mathf.Sin(puff.Seed)*20f)+age*.3f)*Mathf.Deg2Rad;
                if(puff.PoisonGas)roll=Mathf.Sin(puff.Seed+age*.1f)*.035f;
                if (puff.Muzzle)
                {
                    // A narrow directional jet rapidly opens into a rolling billow.
                    float jetPhase = Mathf.Exp(-age * 5f);
                    Vector3 projected = puff.Jet - cameraForward * Vector3.Dot(puff.Jet,cameraForward);
                    roll=Mathf.Atan2(Vector3.Dot(projected,cameraUp),Vector3.Dot(projected,cameraRight));
                    width += projected.magnitude * .35f * jetPhase;
                    height *= 1f - .55f * jetPhase;
                }
                if(puff.BlastJet)
                {
                    Vector3 projected=puff.Jet-cameraForward*Vector3.Dot(puff.Jet,cameraForward);
                    roll=Mathf.Atan2(Vector3.Dot(projected,cameraUp),Vector3.Dot(projected,cameraRight));
                    float jetPhase=(1f-Mathf.Exp(-age*12f))*Mathf.Exp(-age*1.1f);
                    width+=projected.magnitude*.65f*jetPhase;
                    height*=1f-.55f*jetPhase;
                }
                float emphasis=slowMotionStrength>0f?slowMotionStrength*SlowMotionEffects.DistanceWeight(-sortDepth[index]):0f;
                float size=SlowMotionEffects.SmokeSize(emphasis);
                size*=BattleEffectTuning.Current.SmokeSize;
                width*=size; height*=size;
                density*=SlowMotionEffects.SmokeDensity(emphasis)*BattleEffectTuning.Current.SmokeDensity;
                matrices[batchCount]=BillboardMatrix(centers[id],cameraRight,cameraUp,cameraForward,width,height,roll);
                colors[batchCount] = new Vector4(puff.Tint.r, puff.Tint.g, puff.Tint.b, density);
                details[batchCount] = new Vector4(puff.Seed, progress, puff.PoisonGas?4f:puff.FireSmoke ? 3f : puff.Muzzle ? 2f : puff.Haze ? 1f : 0f, age);
                batchCount++;
                if (batchCount == BatchSize || index == VisiblePuffs - 1)
                {
                    properties.SetVectorArray(SmokeColor, colors);
                    properties.SetVectorArray(PuffData, details);
                    Graphics.DrawMeshInstanced(quad, 0, material, matrices, batchCount, properties,
                        ShadowCastingMode.Off, false, gameObject.layer, battleCamera, LightProbeUsage.Off);
                    batchCount = 0;
                }
            }
        }

        // Reuse the camera basis for all puffs; avoid two native transform calls
        // per billboard while retaining the same roll, stretch and orientation.
        public static Matrix4x4 BillboardMatrix(Vector3 position,Vector3 right,Vector3 up,Vector3 forward,float width,float height,float roll)
        {
            float sine=Mathf.Sin(roll),cosine=Mathf.Cos(roll);
            var x=(right*cosine+up*sine)*width;var y=(up*cosine-right*sine)*height;
            return new Matrix4x4 {m00=x.x,m10=x.y,m20=x.z,m01=y.x,m11=y.y,m21=y.z,
                m02=forward.x,m12=forward.y,m22=forward.z,m03=position.x,m13=position.y,m23=position.z,m33=1};
        }

        void EmitVolley(BattalionState unit)
        {
            bool smokeless=EmpireBattle.Art.CartridgeRifleLibrary.UsesCartridges(unit.Weapon);
            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(unit.Files / 5f), 4, 24);
            Vector3 forward = new Vector3(Mathf.Sin(unit.Facing), 0, Mathf.Cos(unit.Facing));
            // Every represented shooter gets a short, small muzzle jet. The longer-lived
            // larger samples below represent the merged powder cloud, not skipped shooters.
            for (int soldier = unit.SoldierStart; soldier < unit.SoldierStart+unit.SoldierCount; soldier++)
            {
                // The simulation captured shooters before resolving simultaneous casualties.
                if (world.Soldiers.FireVolleys[soldier] != unit.LastVolleyTime) continue;
                Vector3 muzzle = world.Soldiers.FireOrigins[soldier];
                Vector3 direction = world.Soldiers.FireDirections[soldier];
                float born = world.Soldiers.FireTimes[soldier];
                bool sidearm=world.Soldiers.Captains[soldier];
                // Cartridge rifles retain their muzzle flash but emit no powder smoke.
                if(smokeless&&!sidearm)continue;
                int puff = AddPuff(muzzle,direction,born,sidearm?.65f:1.25f,false,Capacity+soldier*2);
                puffs[puff].GunPuff = puffs[puff].Muzzle = true;
                puffs[puff].Lifetime = sidearm?3f:7f+(float)random.NextDouble()*3f;
                puffs[puff].Opacity = sidearm?.4f:.68f;
                puffs[puff].Jet = direction*(2.6f+(float)random.NextDouble()*1.4f);
                if(sidearm||EmpireBattle.Art.CartridgeRifleLibrary.UsesCartridges(unit.Weapon))continue;
                Vector3 right = new Vector3(direction.z,0,-direction.x);
                int lockPuff = AddPuff(world.Soldiers.FireLockOrigins[soldier],Vector3.zero,
                    born,.46f,false,Capacity+soldier*2+1);
                puffs[lockPuff].GunPuff = puffs[lockPuff].Lock = true;
                puffs[lockPuff].Lifetime = 4f;
                puffs[lockPuff].Opacity = .36f;
                puffs[lockPuff].Jet = -direction*.12f+right*.06f+Vector3.up*.04f;
            }
            if(smokeless)return;
            int ordinal = 0;
            int stride = Mathf.Max(1, unit.LastVolleySize / sampleCount);
            for (int soldier = unit.SoldierStart; soldier < unit.SoldierStart + unit.SoldierCount; soldier++)
            {
                if (world.Soldiers.FireVolleys[soldier] != unit.LastVolleyTime) continue;
                if (ordinal++ % stride != 0) continue;
                Vector3 muzzle = world.Soldiers.FireOrigins[soldier];
                float born = world.Soldiers.FireTimes[soldier];
                float spacing = unit.LastVolleySize * unit.Spacing / sampleCount;
                int cloud = AddPuff(muzzle, forward, born, Mathf.Clamp(spacing, 2f, 5.5f), false);
                puffs[cloud].PersistentPowder=true;
                puffs[cloud].Lifetime=80f+(float)random.NextDouble()*20f;
                if (ordinal % 4 == 0)
                {
                    int haze=AddPuff(muzzle + Vector3.up*.4f, forward, born, Mathf.Clamp(spacing, 2f, 5.5f), true);
                    puffs[haze].PersistentPowder=true;
                    puffs[haze].Lifetime=110f+(float)random.NextDouble()*20f;
                }
            }
        }

        public Vector3 WindDisplacement(float born, float now)
        {
            float age = Mathf.Max(0f,now-born);
            // Integrated gusts preserve continuity: newly spawned smoke starts at its muzzle.
            float gust = (.22f/.31f)*(Mathf.Cos(born*.31f)-Mathf.Cos(now*.31f));
            return (WindVelocity*age + new Vector3(gust,0f,gust*.4f))*BattleEffectTuning.Current.Wind;
        }

        /// <summary>A light, terrain-hugging dirt streak in the bullet's travel direction.</summary>
        public void EmitGroundDust(Vector3 position, Vector3 incomingVelocity, float born)
        {
            if(world == null || random == null) return;
            Vector3 forward=Vector3.ProjectOnPlane(incomingVelocity,Vector3.up).normalized;
            Vector3 right=new Vector3(forward.z,0f,-forward.x);
            // Steep impacts stay compact; grazing shots leave a longer forward puff line.
            float grazing=Vector3.ProjectOnPlane(incomingVelocity,Vector3.up).magnitude/Mathf.Max(.001f,incomingVelocity.magnitude);
            float length=Mathf.Lerp(.2f,1.15f,grazing*grazing);
            for(int i=0;i<4;i++)
            {
                float along=i/3f;
                Vector3 origin=position+forward*(length*along)+right*(((float)random.NextDouble()-.5f)*.10f);
                origin.y=world.Terrain.HeightAt(new Vector2(origin.x,origin.z))+.09f;
                int id=AddPuff(origin,Vector3.zero,born+along*.045f,Mathf.Lerp(.40f,.22f,along),false);
                puffs[id].GunPuff=true;
                puffs[id].ImpactDust=true;
                puffs[id].Tint=new Color(.64f,.59f,.48f,1f);
                puffs[id].Opacity=Mathf.Lerp(.34f,.18f,along);
                puffs[id].Lifetime=.65f+(float)random.NextDouble()*.35f;
                puffs[id].Jet=forward*Mathf.Lerp(.32f,.12f,along)+Vector3.up*(.10f+(float)random.NextDouble()*.09f);
            }
        }

        /// <summary>Short clouds for small-arms scenery impacts.</summary>
        public void EmitImpact(Vector3 position, Vector3 normal, Color tint, float scale, int count = 3)
        {
            if(world == null || random == null) return;
            for(int i=0;i<Mathf.Clamp(count,0,8);i++)
            {
                int id=AddPuff(position+normal*.06f,Vector3.zero,world.Time,scale,false);
                puffs[id].GunPuff=true;
                puffs[id].Tint=tint;
                puffs[id].Opacity=tint.a;
                puffs[id].Lifetime=1.5f+(float)random.NextDouble();
                puffs[id].Jet=normal*(.4f+(float)random.NextDouble()*.5f);
            }
        }

        /// <summary>Shared cloud pool for cannon powder or dust; scale is initial cloud size in meters.</summary>
        public void EmitCloud(Vector3 position, Vector3 initialVelocity, float scale, Color tint, int count)
        {
            if (world == null || random == null || scale <= 0f) return;
            count = Mathf.Clamp(count, 0, 128);
            for (int i = 0; i < count; i++)
            {
                Vector3 spread = new Vector3((float)random.NextDouble()-.5f, (float)random.NextDouble()*.4f,
                    (float)random.NextDouble()-.5f) * scale;
                int id = AddPuff(position + spread, Vector3.zero, world.Time, scale, i%4==0);
                puffs[id].Jet = initialVelocity * (.25f+(float)random.NextDouble()*.2f);
                puffs[id].Tint = tint;
                puffs[id].Opacity = Mathf.Clamp01(tint.a) * (puffs[id].Haze ? .35f : .8f);
            }
        }

        /// <summary>Tight source for crater vents, dirt bursts, flame smoke and powder jets.</summary>
        public void EmitRootedSmoke(Vector3 origin, Vector3 velocity, float width, Color tint, int count, float lifetime)
        {
            if (world == null || random == null || width <= 0) return;
            for (int i = 0; i < Mathf.Clamp(count, 0, 128); i++)
            {
                Vector3 spread = new Vector3((float)random.NextDouble() - .5f, 0, (float)random.NextDouble() - .5f) * Mathf.Min(.18f, width * .1f);
                int id = AddPuff(origin + spread, Vector3.zero, world.Time, width, false);
                puffs[id].Jet = velocity * Mathf.Lerp(.8f, 1.2f, (float)random.NextDouble());
                puffs[id].Lifetime = lifetime * Mathf.Lerp(.85f, 1.15f, (float)random.NextDouble());
                puffs[id].Tint = tint; puffs[id].Opacity = Mathf.Clamp01(tint.a) * .8f;
            }
        }

        void RemoveLive(int active)
        {
            int removed=liveSlots[active],last=liveSlots[--liveCount];
            if(puffs[removed].FireSmoke){firePuffCount--;puffs[removed].FireSmoke=false;}
            if(puffs[removed].PoisonGas){gasPuffCount--;puffs[removed].PoisonGas=false;}
            liveSlots[active]=last;liveIndices[last]=active;liveIndices[removed]=-1;
        }
        int AddPuff(Vector3 origin, Vector3 forward, float born, float width, bool haze, int reservedSlot = -1)
        {
            // A bounded search reuses nearby expired slots; heavy fire evicts the cursor slot.
            int id = reservedSlot >= 0 ? reservedSlot : cursor;
            for (int attempt = 0; reservedSlot < 0 && attempt < 32; attempt++)
            {
                int candidate = (cursor + attempt) % Capacity;
                if (!puffs[candidate].Alive || world.Time - puffs[candidate].Born >= puffs[candidate].Lifetime * BattleEffectTuning.Current.SmokeLifetime)
                { id = candidate; break; }
            }
            if(liveIndices[id]<0){liveIndices[id]=liveCount;liveSlots[liveCount++]=id;}
            if(puffs[id].FireSmoke)firePuffCount--;
            if(puffs[id].PoisonGas)gasPuffCount--;
            puffs[id] = new Puff { Alive=true, Origin=origin, Jet=forward*(haze ? 4f : 8f), Born=born,
                Tint=new Color(.82f,.825f,.79f,1f),
                Lifetime=haze ? 44f+(float)random.NextDouble()*5f : 32f+(float)random.NextDouble()*10f,
                Seed=(float)random.NextDouble()*100f, Width=width*(.85f+(float)random.NextDouble()*.3f),
                Opacity=haze ? .18f : .53f, Haze=haze };
            puffs[id].BornGust=Mathf.Cos(born*.31f);
            puffs[id].SeedRoll=Mathf.Sin(puffs[id].Seed)*20f;
            if (reservedSlot < 0) cursor = (id + 1) % Capacity;
            return id;
        }

        void ReleaseResources()
        {
            if (material) {if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
            if (quad) {if(Application.isPlaying)Destroy(quad);else DestroyImmediate(quad);}
        }
        void OnDestroy() { ReleaseResources(); }
    }
}
