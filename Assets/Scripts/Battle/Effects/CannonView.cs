using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using EmpireBattle.SupportingArt;

namespace EmpireBattle
{
    /// <summary>Crew-operated field pieces with shared muzzle/impact effects.</summary>
    public sealed partial class CannonView : MonoBehaviour
    {
        private BattleView battleView;
        private BattleWorld world;
        private ArtillerySystem artillery;
        private MusketSmoke smoke;
        private Camera cameraView;
        private Mesh cube, sphere, cylinder;
        private Material iron, wood, brass, fire, tracer, soil, flame;
        private Mesh flameQuad;
        private ArtilleryDebris debris;
        private MaterialPropertyBlock flameProperties;
        public System.Func<float> PresentationTime;
        float TimeNow => PresentationTime != null ? PresentationTime() : world.Time;
        private ProceduralSupportingArt.FieldCannonMeshSet model;
        private readonly List<Object> owned = new List<Object>();
        private float[] observedShots;
        private int observedImpact;
        readonly float[] nextHazardPuff=new float[128];
        private readonly Light[] lights = new Light[8];
        private readonly float[] lightEnd = new float[8];
        readonly float[] nextEmber = new float[128];
        readonly int[] nearestFire = new int[4];
        readonly float[] nearestDistance = new float[4];
        private Matrix4x4[] balls, trails;

        public void Initialize(BattleWorld value, ArtillerySystem guns, Camera camera, MusketSmoke clouds)
        {
            flameProperties=new MaterialPropertyBlock();
            battleView=GetComponent<BattleView>();
            world = value; artillery = guns; cameraView = camera; smoke = clouds;
            cube = Primitive(PrimitiveType.Cube); sphere = Primitive(PrimitiveType.Sphere); cylinder = Primitive(PrimitiveType.Cylinder);
            iron = Material(new Color(.11f,.12f,.13f)); wood = Material(new Color(.25f,.19f,.08f));
            brass = Material(new Color(.36f,.29f,.13f));
            fire = new Material(Resources.Load<Material>("CannonFlash"));
            fire.SetColor("_BaseColor", new Color(4f, 1.6f, .25f)); owned.Add(fire);
            model = ProceduralSupportingArt.CreateFieldCannonMeshes();
            owned.Add(model.Wood); owned.Add(model.Iron); owned.Add(model.Bronze);
            var crew = gameObject.AddComponent<ArtilleryCrewView>();
            crew.Initialize(world, artillery, cameraView);
            balls = new Matrix4x4[256];
            trails = new Matrix4x4[256];
            tracer = new Material(Resources.Load<Material>("CannonFlash")){enableInstancing=true};
            tracer.SetColor("_BaseColor",new Color(.95f,.82f,.57f)); owned.Add(tracer);
            soil = Material(new Color(.24f, .16f, .085f));
            var clod = new Mesh { name = "Angular earth clod" };
            clod.vertices = new[] { new Vector3(0,.5f,0), new Vector3(0,-.35f,0), new Vector3(.5f,0,0), new Vector3(-.4f,0,0), new Vector3(0,0,.45f), new Vector3(0,0,-.5f) };
            clod.triangles = new[] {0,4,2,0,3,4,0,5,3,0,2,5,1,2,4,1,4,3,1,3,5,1,5,2}; clod.RecalculateNormals(); clod.RecalculateBounds(); owned.Add(clod);
            fire.enableInstancing = true;
            debris = new ArtilleryDebris(world, clod, soil, wood, fire);
            flame = new Material(Resources.Load<Shader>("ArtilleryFlame")); owned.Add(flame);
            flameQuad = CreateFlameStrands(); owned.Add(flameQuad);
            InitializeBursts();
            observedShots = new float[guns.Guns.Length];
            for (int i = 0; i < observedShots.Length; i++) observedShots[i] = -100;
            for (int i = 0; i < lights.Length; i++)
            {
                var go = new GameObject("Temporary powder flash light"); go.transform.SetParent(transform);
                lights[i] = go.AddComponent<Light>(); lights[i].type = LightType.Point;
                lights[i].color = new Color(1f,.57f,.2f); lights[i].range = 16; lights[i].intensity = 0;
                lights[i].shadows = LightShadows.None;
            }
        }

        private Mesh Primitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            if(Application.isPlaying)Destroy(go);else DestroyImmediate(go);
            owned.Add(mesh); return mesh;
        }
        private Material Material(Color color)
        {
            var material = new Material(Resources.Load<Material>("CannonLit")) { enableInstancing = true };
            material.SetColor("_BaseColor",color); material.SetFloat("_Smoothness",.25f); owned.Add(material); return material;
        }
        private void Part(Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
            => Graphics.DrawMesh(mesh, Matrix4x4.TRS(position,rotation,scale),material,0,cameraView);

        private void Update()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Cannons);
            if (world == null) return;
            fireSprites.Begin(cameraView,TimeNow);
            foreach (var gun in artillery.Guns)
            {
                Quaternion facing = Quaternion.Euler(0, gun.Facing * Mathf.Rad2Deg, 0);
                Vector3 forward = facing * Quaternion.Euler(-gun.Elevation, 0, 0) * Vector3.forward;
                float age = TimeNow - gun.LastShotTime;
                Vector3 center = artillery.GunCenter(gun);
                Vector3 carriageScale=gun.Destroyed?new Vector3(1,.5f,1):Vector3.one;
                Part(model.Wood, gun.Destroyed?iron:wood, center, facing, carriageScale);
                Part(model.Iron, iron, center, facing, carriageScale);
                Vector3 pivot = new Vector3(0, .9f, -.23f);
                Quaternion tilt = gun.Destroyed?Quaternion.Euler(12,0,18):Quaternion.Euler(-gun.Elevation, 0, 0);
                Vector3 scale = new Vector3(1, 1, gun.ExplosiveShell ? .78f : 1f);
                Part(model.Bronze, gun.ExplosiveShell ? iron : brass,
                    center + facing * (pivot - tilt * Vector3.Scale(pivot, scale)) - (gun.Destroyed?Vector3.up*.4f:Vector3.zero), facing * tilt, scale);
                Vector3 muzzle = artillery.Muzzle(gun);
                if (gun.LastShotTime > observedShots[gun.Id])
                {
                    observedShots[gun.Id] = gun.LastShotTime;
                    for (int puff = 0; puff < 5; puff++)
                        smoke.EmitRootedSmoke(muzzle + forward * puff * .22f, forward * (9f - puff) + Vector3.up * .6f,
                            .45f + puff * .3f, new Color(.77f,.76f,.71f,.65f), puff < 2 ? 3 : 8, puff < 2 ? 3f : 26f);
                    Flash(muzzle,gun.Id%2,.16f,5);
                    debris.EmitSparks(muzzle, forward * 16f, TimeNow, gun.ShotsFired * 31 + gun.Id, Mathf.RoundToInt(12*Mathf.Lerp(1f,1.75f,SlowMotionEffects.Amount(battleView,muzzle))));
                }
                if (age >= 0 && age < .13f)
                {
                    float fade = 1f - age / .13f;
                    Quaternion jet = Quaternion.FromToRotation(Vector3.up, forward);
                    DrawMuzzleFlame(muzzle, jet, new Vector3(.65f * fade, 2.8f * fade, .65f * fade), age, gun.Id);

                }
            }
            // Impact ring events are consumed once. Round shot emits dirt; shells get a brief burst.
            int first = Mathf.Max(observedImpact+1,artillery.ImpactSequence-artillery.Impacts.Length+1);
            for (int sequence=first;sequence<=artillery.ImpactSequence;sequence++)
            {
                var hit=artillery.Impacts[(sequence-1)%artillery.Impacts.Length];
                if (hit == null || hit.Sequence != sequence) continue;
                float emphasis=SlowMotionEffects.Amount(battleView,hit.Position);
                debris.Emit(hit,emphasis);
                Vector3 source = hit.TerrainStrike ? world.Terrain.SurfacePoint(new Vector2(hit.Position.x, hit.Position.z), .06f) : hit.Position;
                bool lime = hit.AmmunitionType == ArtilleryAmmunition.Lime;
                bool pellet = hit.AmmunitionType == ArtilleryAmmunition.Canister;
                if (hit.Detonates) smoke.EmitArtilleryBurst(hit, source);
                else if (hit.AmmunitionType==ArtilleryAmmunition.PoisonGas&&hit.ExplosiveShell)smoke.EmitPoisonGas(source,TimeNow,sequence,true);
                else if (lime && hit.ExplosiveShell) smoke.EmitLimeCloud(source,TimeNow,sequence,true);
                else if (hit.TerrainStrike)
                    smoke.EmitRootedSmoke(source, Vector3.up * 2f, pellet ? .4f : 1.8f,
                        new Color(.43f,.35f,.22f,.5f),pellet ? 2 : 10,pellet ? .5f : 2.8f);
                if(hit.Detonates)
                {
                    Flash(hit.Position+Vector3.up,2+sequence%2,.3f*Mathf.Lerp(1f,1.3f,emphasis),13*Mathf.Lerp(1f,1.4f,emphasis)*BattleEffectTuning.Current.ExplosionFlash);
                    if(emphasis>0f)debris.EmitSparks(hit.Position+Vector3.up,Vector3.up*7f,TimeNow,sequence,Mathf.RoundToInt(24*emphasis));
                }
            }
            observedImpact=artillery.ImpactSequence;
            for (int i = 0; i < 4; i++) { nearestFire[i] = -1; nearestDistance[i] = 100f * 100f; }
            for(int h=0;h<artillery.Hazards.Length;h++)
            {
                var hazard=artillery.Hazards[h];if(hazard.Remaining<=0)continue;
                bool burning=hazard.Type==ArtilleryAmmunition.Carcass;
                if(!burning&&TimeNow>=nextHazardPuff[h]&&(cameraView.transform.position-hazard.Position).sqrMagnitude<225f*225f)
                {
                    nextHazardPuff[h]=TimeNow+.8f;
                    if(hazard.Type==ArtilleryAmmunition.PoisonGas)
                        smoke.EmitPoisonGas(hazard.Position,TimeNow,h*31+Mathf.FloorToInt(TimeNow*5),false,Mathf.Clamp01(hazard.Remaining/6f));
                    else smoke.EmitLimeCloud(hazard.Position,TimeNow,h*31+Mathf.FloorToInt(TimeNow*5),false,Mathf.Clamp01(hazard.Remaining/5f));
                }
                if(burning)
                {
                    float distance = (cameraView.transform.position - hazard.Position).sqrMagnitude;
                    if(distance<225f*225f&&TimeNow>=nextHazardPuff[h])
                    {
                        bool near=distance<90f*90f;
                        nextHazardPuff[h]=TimeNow+(near?.2f:.9f);
                        float strength=Mathf.Clamp01(hazard.Remaining/8f);
                        for(int puff=0;puff<(near?3:2);puff++)
                        {
                            Vector2 p=FireScatter(hazard,h,fireSmokeSequence[h]++);
                            smoke.EmitFireSmoke(world.Terrain.SurfacePoint(p,.55f),TimeNow,1.35f,strength);
                        }
                    }
                    for (int slot = 0; slot < 4; slot++)
                    {
                        if (distance >= nearestDistance[slot]) continue;
                        for (int shift = 3; shift > slot; shift--) { nearestFire[shift] = nearestFire[shift-1]; nearestDistance[shift] = nearestDistance[shift-1]; }
                        nearestFire[slot] = h; nearestDistance[slot] = distance; break;
                    }
                    if (TimeNow >= nextEmber[h])
                    {
                        nextEmber[h] = TimeNow + .16f;
                        debris.EmitSparks(world.Terrain.SurfacePoint(FireScatter(hazard,h,Mathf.FloorToInt(TimeNow*6)),.15f), Vector3.up*1.5f, TimeNow,h+Mathf.FloorToInt(TimeNow*10),1);
                    }
                    if (distance > 450f * 450f) continue;
                    Vector3 root = world.Terrain.SurfacePoint(new Vector2(hazard.Position.x,hazard.Position.z), .04f);
                    float fade = Mathf.Clamp01(hazard.Remaining / 8f);
                    int fireSeed = FireSeed(hazard);
                    float spread = Mathf.Clamp01((ArtillerySystem.CarcassBurnDuration-hazard.Remaining)/.9f);
                    DrawGroundFlames(root, hazard.Direction, TimeNow, fireSeed, fade, spread);

                }
            }
            for (int i = 0; i < 4; i++)
            {
                var light = lights[4+i]; light.range = 7f;
                if (nearestFire[i] < 0) { light.intensity = 0; continue; }
                var hazard = artillery.Hazards[nearestFire[i]];
                Vector3 litCenter=hazard.Position+ArtilleryEffectShape.Forward(hazard.Direction)*3f;
                light.transform.position = world.Terrain.SurfacePoint(new Vector2(litCenter.x,litCenter.z),.7f);
                light.intensity = (.6f + .12f * Mathf.Sin(TimeNow*11f+nearestFire[i])) * Mathf.Clamp01(hazard.Remaining/8f);
            }
            DrawBurningUnits();DrawBursts();fireSprites.Flush();
            debris.Draw(TimeNow, cameraView);
            float atlas=battleView?battleView.AtlasBlend:0f;
            tracer.SetColor("_BaseColor",Color.Lerp(new Color(.95f,.82f,.57f),new Color(.12f,.16f,.20f),atlas));
            int ballCount=0,trailCount=0;
            foreach(var ball in artillery.Projectiles)
            {
                if(ball==null || !ball.Active)continue;
                // Extrapolate only the unconsumed fraction of the combat tick. Collision and
                // bounce decisions remain in ArtillerySystem; each skip starts a fresh trail.
                float remainder=Mathf.Clamp(TimeNow-world.Time,0f,.05f);
                Vector3 position=WeaponBallistics.PositionAt(ball.Position,ball.Velocity,remainder);
                position.y=Mathf.Max(position.y,world.Terrain.HeightAt(new Vector2(position.x,position.z))+.08f);
                Vector3 velocity=ball.Velocity+Vector3.down*(9.81f*remainder);
                float segmentAge=ball.SegmentAge+remainder;
                float distant=ProjectileVisibility.DistanceBlend(cameraView,position);
                float size=ball.Shrapnel?ProjectileVisibility.Width(cameraView,position,.65f,.015f,.09f):ProjectileVisibility.Width(cameraView,position,ball.AmmunitionType==ArtilleryAmmunition.Canister?1f:2f,ball.AmmunitionType==ArtilleryAmmunition.Canister?.05f:.26f,1.4f,atlas);
                balls[ballCount++]=Matrix4x4.TRS(position,Quaternion.identity,Vector3.one*size);
                if(ballCount==balls.Length){Graphics.DrawMeshInstanced(sphere,0,iron,balls,ballCount,null,ShadowCastingMode.Off,false,0,cameraView);ballCount=0;}
                if(segmentAge>.0001f&&velocity.sqrMagnitude>.001f)
                {
                    // Restart at each skip so the trail never cuts through the ground.
                    float tailTime=Mathf.Min(segmentAge,ball.Shrapnel?ball.FragmentStreakTime:Mathf.Lerp(.018f,.09f,distant));
                    Vector3 tail=position-velocity*tailTime+Vector3.down*(.5f*9.81f*tailTime*tailTime);
                    tail.y=Mathf.Max(tail.y,world.Terrain.HeightAt(new Vector2(tail.x,tail.z))+.08f);
                    float width=ball.Shrapnel?ProjectileVisibility.Width(cameraView,position,.6f,.01f,.07f):ProjectileVisibility.Width(cameraView,position,1.4f,.035f,.9f,atlas);
                    trails[trailCount++]=Matrix4x4.TRS((position+tail)*.5f,Quaternion.LookRotation(position-tail),
                        new Vector3(width,width,Vector3.Distance(position,tail)));
                    if(trailCount==trails.Length){Graphics.DrawMeshInstanced(cube,0,tracer,trails,trailCount,null,ShadowCastingMode.Off,false,0,cameraView);trailCount=0;}
                }
            }
            if(ballCount>0)Graphics.DrawMeshInstanced(sphere,0,iron,balls,ballCount,null,ShadowCastingMode.Off,false,0,cameraView);
            if(trailCount>0)Graphics.DrawMeshInstanced(cube,0,tracer,trails,trailCount,null,ShadowCastingMode.Off,false,0,cameraView);
            for(int i=0;i<4;i++)
            {
                float remaining=lightEnd[i]-TimeNow;
                if(remaining<=0)lights[i].intensity=0;
                else lights[i].intensity=Mathf.Min(lights[i].intensity,remaining*45f);
            }
        }

        static int FireSeed(ArtillerySystem.Hazard hazard) => Mathf.RoundToInt(hazard.Position.x*13f+hazard.Position.z*19f);
        static Vector2 FireScatter(ArtillerySystem.Hazard hazard,int slot,int pulse)
        {
            Vector2 p=ArtilleryEffectShape.FlamePoint(FireSeed(hazard),(slot+pulse)%28);
            float spread=Mathf.Clamp01((ArtillerySystem.CarcassBurnDuration-hazard.Remaining)/.9f);
            Vector3 point=hazard.Position+ArtilleryEffectShape.WorldOffset(p*spread,hazard.Direction);
            return new Vector2(point.x,point.z);
        }
        public static Mesh CreateFlameStrands()
        {
            const int strands = 28, segments = 24;
            var vertices = new Vector3[strands * (segments + 1) * 2]; var uv = new Vector2[vertices.Length];
            var triangles = new int[strands * segments * 6]; int k = 0;
            for (int strand = 0; strand < strands; strand++) for (int step = 0; step <= segments; step++)
            {
                int i = (strand * (segments + 1) + step) * 2; float u = step / (float)segments;
                vertices[i] = new Vector3(-1,u,strand); vertices[i+1] = new Vector3(1,u,strand);
                uv[i] = new Vector2(0,u); uv[i+1] = new Vector2(1,u);
                if (step == segments) continue;
                triangles[k++] = i; triangles[k++] = i+2; triangles[k++] = i+1;
                triangles[k++] = i+1; triangles[k++] = i+2; triangles[k++] = i+3;
            }
            var mesh = new Mesh { name = "Flowing fire arc strands" }; mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            // Parameter-space vertices encode strand IDs; bounds describe the shader's actual fire volume.
            mesh.bounds = new Bounds(new Vector3(0,.65f,0),new Vector3(3,2,3)); return mesh;
        }
        public void EmitTreeSplinters(Vector3 root, Vector3 direction) => debris.EmitSparks(root, direction * 7f + Vector3.up * 3f, TimeNow, Mathf.RoundToInt(root.x * 31 + root.z), 28, true);
        void DrawMuzzleFlame(Vector3 root, Quaternion rotation, Vector3 scale, float time, int seed)
        {
            float boundedSeed = ((seed % 997 + 997) % 997) * .137f;
            flameProperties.SetVector("_FlameData", new Vector4(time, boundedSeed, 1, 1));
            Graphics.DrawMesh(flameQuad, Matrix4x4.TRS(root, rotation, scale), flame, 0, cameraView, 0, flameProperties, ShadowCastingMode.Off, false);
        }
        private void Flash(Vector3 position,int index,float duration,float intensity)
        {
            lights[index].transform.position=position;
            lights[index].intensity=intensity; lightEnd[index]=TimeNow+duration;
        }
        private void OnDestroy()
        {
            if(artillery!=null)artillery.SoldierIgnited-=IgniteSoldier;
            fireSprites?.Dispose();
            foreach(var item in owned)if(item){if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}
        }
    }
}
