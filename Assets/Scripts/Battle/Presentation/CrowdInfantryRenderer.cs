using Unity.Profiling;
using System;
using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    // Baked shared poses: no Animator, skeleton or GameObject per battlefield soldier.
    public sealed class CrowdInfantryRenderer
    {
        static readonly ProfilerMarker PerformanceMarker = new ProfilerMarker("Battle.InfantryRender");
        sealed class Batch
        {
            public Mesh Mesh; public Material Material; public int Count; public bool Active;
            public Batch Far, VeryFar; public bool CastShadows=true;
            public Matrix4x4[] Matrices;
            public float[] Blends, MeleeBlends;
            public Vector4[] Uniforms;
            readonly Upload[] uploads=new Upload[10];
            sealed class Upload
            {
                public float[] Blends,MeleeBlends;
                public Vector4[] Uniforms;
                public MaterialPropertyBlock Properties=new MaterialPropertyBlock();
            }
            public MaterialPropertyBlock UploadProperties()
            {
                if(!BattleOptimizationSettings.RenderWorkOptimizations||Count>256)
                {
                    Properties.SetFloatArray(PoseBlendId,Blends);
                    if(MeleeBlends!=null)Properties.SetFloatArray(MeleeBlendId,MeleeBlends);
                    Properties.SetVectorArray(UniformId,Uniforms);
                    return Properties;
                }
                int bucket=0,size=1;
                while(size<Count){size*=2;bucket++;}
                // Unity retains the first array length on a property block. Each
                // power-of-two bucket therefore owns its block and fixed buffers.
                var upload=uploads[bucket];
                if(upload==null)uploads[bucket]=upload=new Upload {Blends=new float[size],Uniforms=new Vector4[size],MeleeBlends=MeleeBlends!=null?new float[size]:null};
                Array.Copy(Blends,upload.Blends,Count);Array.Copy(Uniforms,upload.Uniforms,Count);
                upload.Properties.SetFloatArray(PoseBlendId,upload.Blends);
                if(MeleeBlends!=null){Array.Copy(MeleeBlends,upload.MeleeBlends,Count);upload.Properties.SetFloatArray(MeleeBlendId,upload.MeleeBlends);}
                upload.Properties.SetVectorArray(UniformId,upload.Uniforms);
                return upload.Properties;
            }
            readonly int capacity;
            public Batch(int capacity){this.capacity=capacity;}
            public void EnsureBuffers(){if(Matrices!=null)return;Matrices=new Matrix4x4[capacity];Blends=new float[capacity];if(Material.shader.name=="EmpireBattle/CrowdSword")MeleeBlends=new float[capacity];Uniforms=new Vector4[capacity];Properties=new MaterialPropertyBlock();}
            public MaterialPropertyBlock Properties;
        }
        static readonly int MeleeBlendId=Shader.PropertyToID("_MeleeBlend");
        readonly SwordReadiness swordReadiness;
        static readonly int PoseBlendId = Shader.PropertyToID("_PoseBlend");
        static readonly int UniformId = Shader.PropertyToID("_RegimentUniform");
        float clock;
        bool registeringDistant;
        readonly List<Mesh> distantMeshes=new List<Mesh>();
        readonly bool modernInstancing;
        readonly int capacity;
        readonly Matrix4x4[] transforms;
        readonly Vector3[] worldPositions, framePositions;
        readonly BattleRenderBounds.SurfaceCache[] surfaceCache;
        readonly float[] transformFacings;
        readonly bool[] transformReady;
        public int DrawCalls {get;private set;}
        readonly float[] posture,poseTimes,reloadPhases;
        readonly float[] commandTravel;
        readonly Vector2[] commandPositions;
        readonly bool[] poseInitialized,riseAiming;
        readonly int[] riseReloadFrames;
        readonly BattleWorld world;
        readonly Camera camera;
        readonly InfantryCrowdLibrary library;
        readonly BayonetPresentationLibrary bayonets;
        readonly Dictionary<Mesh,Batch> transferBatches=new Dictionary<Mesh,Batch>(),stowedBatches=new Dictionary<Mesh,Batch>();
        readonly Dictionary<Mesh,Batch> bladeBatches=new Dictionary<Mesh,Batch>();
        readonly List<Mesh> bladeMeshes=new List<Mesh>();
        readonly Dictionary<Mesh,Batch> lookup = new Dictionary<Mesh,Batch>();
        readonly HashSet<InfantryCrowdSet> registered=new HashSet<InfantryCrowdSet>();
        readonly List<Batch> activeBatches = new List<Batch>();
        readonly Dictionary<int,StandardCloth[]> standards=new Dictionary<int,StandardCloth[]>();
        readonly List<Mesh> standardMeshes=new List<Mesh>();
        public BattleCasualtyView Casualties { get; set; }
        public bool Ready { get; }
        public int VisibleSoldiers { get; private set; }
        public int VisibleBayonets { get; private set; }
        public int VisibleTransferBayonets {get;private set;}
        public int CloseSoldiers { get; private set; }
        public CrowdInfantryRenderer(BattleWorld value,Camera view,bool modern=true,int batchCapacity=511)
        {
            world=value;camera=view;modernInstancing=modern;capacity=Mathf.Clamp(batchCapacity,1,511);
            int count=world.Soldiers.Alive.Length;
            swordReadiness=new SwordReadiness(count);
            surfaceCache=new BattleRenderBounds.SurfaceCache[count];
            transforms=new Matrix4x4[count];worldPositions=new Vector3[count];framePositions=new Vector3[count];transformFacings=new float[count];
            transformReady=new bool[count];
            posture=new float[count];poseTimes=new float[count];reloadPhases=new float[count];
            poseInitialized=new bool[count];riseAiming=new bool[count];riseReloadFrames=new int[count];
            commandTravel=new float[world.Battalions.Length];commandPositions=new Vector2[world.Battalions.Length];
            foreach(var unit in world.Battalions)commandPositions[unit.Id]=unit.Position;
            library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
            bayonets=Resources.Load<BayonetPresentationLibrary>("Bayonets/Library");
            Ready=Valid(library?library.French:null)&&Valid(library?library.British:null);
            if(!Ready)return;
            foreach(var unit in world.Battalions)if(unit.Kind==UnitKind.Infantry){var set=library.Resolve(unit);if(Valid(set))Register(set);if(UnitRoster.HasCommandParty(unit)){var command=library.ResolveCommand(unit);if(Valid(command))Register(command);}}
        }
        float CartridgeReloadPhase(BattalionState unit,int soldier,float phase)
        {
            if(!CartridgeRifleLibrary.UsesCartridges(unit.Weapon)||unit.Weapon.Magazine<=1)return phase;
            bool cycling=world.Soldiers.ReloadDurations[soldier]<world.BaseReload(unit)*.6f;
            return cycling?(phase<.5f?phase*.4f:.7f+(phase-.5f)*.6f):phase;
        }
        static bool Valid(InfantryCrowdSet set) => set!=null && set.Material && set.Lods!=null && set.Lods.Length>0
            && set.Lods[0].Idle!=null && set.Lods[0].Idle.Length>0 && set.Lods[0].Idle[0];
        void Register(InfantryCrowdSet set)
        {
            if(!registered.Add(set))return;
            set.Material.enableInstancing=true;
            foreach(var lod in set.Lods)
            {
                registeringDistant=!world.Config.AssetReview&&lod==set.Lods[set.Lods.Length-1];
                var order=bayonets?bayonets.Resolve(set.Material,Array.IndexOf(set.Lods,lod)):null;
                if(order!=null){foreach(var poses in new[]{order.Standing,order.Kneeling,order.March,order.Run,order.UnfixMarch,order.UnfixRun})RegisterPoses(poses,set.Material);
                    if(order.Transfer!=null)foreach(var track in order.Transfer)if(track!=null)for(int i=0;i<track.Bodies.Length;i++)if(!transferBatches.ContainsKey(track.Bodies[i]))transferBatches.Add(track.Bodies[i],new Batch(capacity){Mesh=track.Props[i],Material=bayonets.Steel});}
                RegisterPoses(new[]{lod.LowerToKneel,lod.RiseToAim,lod.RiseToIdle,lod.KneelingAimTransition},set.Material);
                RegisterPoses(lod.RiseToReload,set.Material);RegisterPoses(lod.GrenadeThrow,set.GrenadeMaterial?set.GrenadeMaterial:set.Material);
                if(lod.Melee!=null)foreach(var clip in lod.Melee){RegisterPoses(clip.Poses,set.Material);RegisterPoses(clip.Kneeling,set.Material);}
                if(lod.DeathVariants!=null)foreach(var v in lod.DeathVariants)RegisterPoses(v.Poses,set.Material);
                if(lod.IdleVariants!=null)foreach(var v in lod.IdleVariants)RegisterPoses(v.Poses,set.Material);
                if(lod.UnderFireVariants!=null)foreach(var v in lod.UnderFireVariants)RegisterPoses(v.Poses,set.Material);
                if(lod.RecoveryVariants!=null)foreach(var v in lod.RecoveryVariants){RegisterPoses(v.Knockdown,set.Material);RegisterPoses(v.GetUp,set.Material);}
                if(lod.Command!=null)for(int role=0;role<lod.Command.Length;role++){
                    var v=lod.Command[role];if(v==null)continue;RegisterPoses(v.Idle,set.Material,role==2);RegisterPoses(v.March,set.Material,role==2);
                    foreach(var poses in new[]{v.PikeReady,v.PikeThrust,v.PikeKnockdown,v.PikeGetUp,v.OfficerReady,v.OfficerSlash,v.OfficerFire,v.OfficerKnockdown,v.OfficerGetUp,v.KneelingIdle,v.KneelingStrike})RegisterPoses(poses,set.Material);
                }
                if(lod.Locomotion!=null)foreach(var variant in lod.Locomotion)foreach(var poses in variant.Loops())RegisterPoses(poses,set.Material);
                foreach(var poses in new[]{lod.Idle,lod.Fire,lod.KneelingFire,lod.Reload,lod.KneelingReload,lod.March,lod.Run,lod.Charge,lod.Hit,lod.Death,lod.Knockdown,lod.GetUp})
                    RegisterPoses(poses,set.Material);
            }
        }
        void RegisterPoses(Mesh[] poses,Material material,bool standard=false)
        {
            if(poses==null)return;
            foreach(var mesh in poses)if(mesh&&!lookup.ContainsKey(mesh))
            {
                var rendered=standard?PrepareStandard(mesh):mesh;
                if(standard)standardMeshes.Add(rendered);
                var batch=new Batch(capacity){Mesh=rendered,Material=material};
                lookup.Add(mesh,batch);
                if(!standard&&bayonets&&bayonets.Steel&&bayonets.Locate(mesh,out var socket,out var rotation))
                {
                    var nextSocket=socket;var nextRotation=rotation;
                    if(!bayonets.Locate(mesh,out nextSocket,out nextRotation,true)){nextSocket=socket;nextRotation=rotation;}
                    float beltHeight=mesh.bounds.max.y<1.5f?.49f:.92f;
                    var belt=new Vector3(.34f,beltHeight,.30f);var beltRotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
                    var blade=BayonetPresentationLibrary.Equipment(socket,rotation,belt,beltRotation);
                    var nextBlade=BayonetPresentationLibrary.Blade(nextSocket,nextRotation,nextSocket,nextRotation);
                    var next=new List<Vector3>(blade.vertices);var nextNormals=new List<Vector3>(blade.normals);
                    for(int i=0;i<nextBlade.vertexCount;i++){next[i]=nextBlade.vertices[i];nextNormals[i]=nextBlade.normals[i];}
                    blade.SetUVs(2,next);blade.SetUVs(3,nextNormals);var bounds=blade.bounds;bounds.Encapsulate(nextBlade.bounds);blade.bounds=bounds;DestroyOwnedMesh(nextBlade);bladeMeshes.Add(blade);
                    var stowed=BayonetPresentationLibrary.Equipment(belt,beltRotation,belt,beltRotation);bladeMeshes.Add(stowed);
                    stowedBatches.Add(mesh,new Batch(capacity){Mesh=stowed,Material=bayonets.Steel});
                    bladeBatches.Add(mesh,new Batch(capacity){Mesh=blade,Material=bayonets.Steel});
                }
                if(registeringDistant)
                {
                    batch.Far=CreateDistantBatch(batch,.12f,true);
                    batch.VeryFar=CreateDistantBatch(batch,.24f,false);
                }
            }
        }
        Batch CreateDistantBatch(Batch source,float cell,bool shadows)
        {
            var mesh=DistantUnitMesh.Build(source.Mesh,cell);
            if(mesh!=source.Mesh)distantMeshes.Add(mesh);
            return new Batch(capacity){Mesh=mesh,Material=source.Material,CastShadows=shadows};
        }
        static Batch AtDistance(Batch batch,float squared)
        {
            int tier=DistantUnitMesh.Tier(squared);
            return tier==2&&batch.VeryFar!=null?batch.VeryFar:tier>0&&batch.Far!=null?batch.Far:batch;
        }
        // Legacy libraries combine the bearer, staff and cloth. Remove only the
        // cloth triangles above the head; keep the atlas, body and pose morphs.
        public static Mesh PrepareStandard(Mesh source)
        {
            var mesh=UnityEngine.Object.Instantiate(source);mesh.name=source.name+" raised staff";
            var points=mesh.vertices;var uv=mesh.uv;var triangles=mesh.triangles;var kept=new List<int>(triangles.Length);
            bool Cloth(int i)=>points[i].y>1.98f&&points[i].x<-.079f&&points[i].z>.16f;
            for(int i=0;i<triangles.Length;i+=3)
                if(!(Cloth(triangles[i])&&Cloth(triangles[i+1])&&Cloth(triangles[i+2])
                    &&(uv[triangles[i]]-uv[triangles[i+1]]).sqrMagnitude<1e-12f
                    &&(uv[triangles[i]]-uv[triangles[i+2]]).sqrMagnitude<1e-12f))
                {kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);}
            Vector3 Raise(Vector3 p){
                if(p.y>1.6f&&Mathf.Abs(p.x+.08f)<.045f&&Mathf.Abs(p.z-.37f)<.055f)p.y=1.6f+(p.y-1.6f)*1.36f;
                return p;
            }
            for(int i=0;i<points.Length;i++)points[i]=Raise(points[i]);mesh.vertices=points;
            var next=new List<Vector3>();mesh.GetUVs(2,next);
            for(int i=0;i<next.Count;i++)next[i]=Raise(next[i]);if(next.Count>0)mesh.SetUVs(2,next);
            mesh.triangles=kept.ToArray();mesh.RecalculateBounds();return mesh;
        }
        public void Render(Plane[] frustum,float presentationTime)
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.InfantryRender);
            using var performanceScope = PerformanceMarker.Auto();
            clock=Mathf.Max(world.Time,presentationTime);
            VisibleSoldiers=VisibleBayonets=VisibleTransferBayonets=CloseSoldiers=DrawCalls=0;
            if(!Ready)return;
            foreach(var unit in world.Battalions)
            {
                commandTravel[unit.Id]+=Vector2.Distance(commandPositions[unit.Id],unit.Position);
                commandPositions[unit.Id]=unit.Position;
            }
            foreach(var unit in world.Battalions)if(unit.Kind==UnitKind.Infantry){var set=library.Resolve(unit);if(Valid(set))Register(set);if(UnitRoster.HasCommandParty(unit)){var command=library.ResolveCommand(unit);if(Valid(command))Register(command);}}
            foreach(var batch in activeBatches){batch.Count=0;batch.Active=false;}
            activeBatches.Clear();
            Vector3 eye=camera.transform.position;
            foreach(var unit in world.Battalions)
            {
                if(unit.AliveCount<=0||unit.Kind==UnitKind.Cavalry)continue;
                var bounds=BattleRenderBounds.Regiment(world,unit,clock,framePositions,surfaceCache);
                if(!GeometryUtility.TestPlanesAABB(frustum,bounds))continue;
                bool clipped=!BattleRenderBounds.FullyInside(frustum,bounds);
                var set=library.Resolve(unit);
                var commandSet=world.Config.AssetReview||!UnitRoster.HasCommandParty(unit)?set:library.ResolveCommand(unit);
                var uniform=RegimentBaseStats.Uniform(unit.TroopTier);
                // Stable bearer IDs are shared by every man in the regiment.
                // Resolving faction/role and walking the roster for every soldier
                // repeats the same work tens of thousands of times per frame.
                int firstBearer=CommandPartyLayout.StandardBearer(world,unit,0);
                int secondBearer=CommandPartyLayout.StandardBearer(world,unit,1);
                for(int soldier=unit.SoldierStart;soldier<unit.SoldierStart+unit.SoldierCount;soldier++)
                {
                    if(!world.Soldiers.Alive[soldier]||(Casualties&&Casualties.HasSurvivorVisual(soldier)))continue;
                    // Culling already sampled the exact interpolated/airborne
                    // position. Reuse it instead of a second terrain query.
                    Vector3 position=framePositions[soldier];float facing=world.Soldiers.Facings[soldier];
                    if(clipped&&!BattleRenderBounds.PointVisible(frustum,position))continue;
                    var previous=worldPositions[soldier];
                    bool moved=!transformReady[soldier]||position.x!=previous.x||position.y!=previous.y||position.z!=previous.z;
                    if(moved||facing!=transformFacings[soldier])
                    {
                        if(!transformReady[soldier]||facing!=transformFacings[soldier])transforms[soldier]=UprightMatrix(position,facing);
                        else
                        {
                            // A marching soldier usually keeps his heading. Only
                            // translation changes between interpolated frames.
                            transforms[soldier].m03=position.x;transforms[soldier].m13=position.y;transforms[soldier].m23=position.z;
                        }
                        worldPositions[soldier]=position;transformFacings[soldier]=facing;transformReady[soldier]=true;
                    }
                    if (world.Soldiers.IsKnockedDown(soldier, clock))
                    {
                        var recoverySet=!world.Config.AssetReview&&(world.Soldiers.Captains[soldier]||world.Soldiers.PikeSergeants[soldier])?commandSet:set;
                        int count=recoverySet.Lods[0].RecoveryVariants?.Length??0;
                        bool command=!world.Config.AssetReview&&(world.Soldiers.Captains[soldier]||world.Soldiers.PikeSergeants[soldier]);
                        int variant=command?0:InfantryRecoveryTiming.RecoveryVariant(world,soldier,count);
                        float phase=InfantryRecoveryTiming.Phase(world,soldier,clock,out bool rising);
                        float grounded=clock-world.Soldiers.KnockdownStarts[soldier]>=world.Soldiers.KnockdownLandings[soldier]?(rising?1f-phase:1f):0f;
                        var rotation=InfantryRecoveryTiming.Rotation(world,soldier,clock,Mathf.Max(0,variant));
                        transforms[soldier]=Matrix4x4.TRS(position,InfantryGroundContact.Rotation(world.Terrain,position,rotation,grounded),Vector3.one);
                        // Rebuild the standing transform when recovery finishes, even if the man hasn't moved.
                        transformReady[soldier] = false;
                    }
                    float squared=(position-eye).sqrMagnitude;
                    int level=0;
                    while(level<set.Lods.Length-1 && squared>set.Lods[level].MaxDistance*set.Lods[level].MaxDistance)level++;
                    // Close detail has a fixed budget even when the camera enters a dense formation.
                    if(level==0&&set.Lods.Length>1&&CloseSoldiers>=256)level=1;
                    if(level==0)CloseSoldiers++;
                    if(soldier==firstBearer||soldier==secondBearer)continue;
                    var poseSet=!world.Config.AssetReview&&(world.Soldiers.Captains[soldier]||world.Soldiers.PikeSergeants[soldier])?commandSet:set;
                    Mesh mesh=Pose(poseSet.Lods[Mathf.Min(level,poseSet.Lods.Length-1)],unit,FormationMath.IsFiringSlot(unit,world.Soldiers.FormationSlots[soldier]),soldier,out float blend);
                    if(!mesh||!lookup.TryGetValue(mesh,out var batch))continue;
                    batch=AtDistance(batch,squared);Activate(batch);
                    batch.Blends[batch.Count]=blend;
                    if(batch.MeleeBlends!=null)batch.MeleeBlends[batch.Count]=swordReadiness.Sample(world,unit,soldier,clock);
                    batch.Uniforms[batch.Count]=uniform;
                    batch.Matrices[batch.Count++]=transforms[soldier];

                    if(world.CanFixBayonets(unit)&&!world.Soldiers.Captains[soldier]&&!world.Soldiers.PikeSergeants[soldier]&&squared<=140*140)
                    {
                        Batch equipment=null;
                        bool handling=BayonetPresentationTiming.Handling(world,unit,soldier,clock,out _);
                        if(handling&&transferBatches.TryGetValue(mesh,out var transfer)){equipment=transfer;VisibleTransferBayonets++;}
                        else if(unit.BayonetsFixed&&bladeBatches.TryGetValue(mesh,out var mounted)){equipment=mounted;VisibleBayonets++;}
                        else if(!unit.BayonetsFixed&&stowedBatches.TryGetValue(mesh,out var stowed))equipment=stowed;
                        if(equipment!=null){Activate(equipment);equipment.Blends[equipment.Count]=blend;equipment.Matrices[equipment.Count++]=transforms[soldier];if(equipment.Count==equipment.Matrices.Length)Flush(equipment);}
                    }
                    VisibleSoldiers++;
                    if(batch.Count==batch.Matrices.Length)Flush(batch);
                }
            }
            RenderCommand(eye,frustum);
            foreach(var batch in activeBatches)Flush(batch);
        }
        void Activate(Batch batch)
        {
            batch.EnsureBuffers();
            // A full batch may flush several times in one frame. Keep it on the
            // active list exactly once so its final partial batch is submitted.
            if(batch.Active)return;
            batch.Active=true;activeBatches.Add(batch);
        }
        public static Matrix4x4 UprightMatrix(Vector3 position,float facing)
        {
            float sine=Mathf.Sin(facing),cosine=Mathf.Cos(facing);
            return new Matrix4x4 {m00=cosine,m02=sine,m11=1,m20=-sine,m22=cosine,
                m03=position.x,m13=position.y,m23=position.z,m33=1};
        }
        Mesh Pose(InfantryCrowdLod lod,BattalionState unit,bool firingSoldier,int soldier,out float blend)
        {
            float throwAge=clock-world.Soldiers.GrenadeThrowStarts[soldier];
            if(throwAge>=0&&throwAge<GrenadeSystem.ThrowDuration&&lod.GrenadeThrow?.Length>0&&!world.Soldiers.IsKnockedDown(soldier,clock))
            {posture[soldier]=0;poseInitialized[soldier]=false;return Sample(lod.GrenadeThrow,throwAge/GrenadeSystem.ThrowDuration,false,out blend);}

            if (world.Soldiers.IsKnockedDown(soldier, clock))
            {
                float knockdownAge = clock - world.Soldiers.KnockdownStarts[soldier];
                float remaining = world.Soldiers.KnockdownEnds[soldier] - clock;
                float landing=world.Soldiers.KnockdownLandings[soldier];
                float phase=InfantryRecoveryTiming.Phase(world,soldier,clock,out bool rising);
                var pike=world.Soldiers.PikeSergeants[soldier]&&lod.Command!=null&&lod.Command.Length>3?lod.Command[3]:null;
                var captain=world.Soldiers.Captains[soldier]&&lod.Command!=null&&lod.Command.Length>0?lod.Command[0]:null;
                var poses=captain!=null?(rising?captain.OfficerGetUp:captain.OfficerKnockdown):pike!=null?(rising?pike.PikeGetUp:pike.PikeKnockdown):rising?lod.GetUp:lod.Knockdown;
                if(captain==null&&pike==null&&lod.RecoveryVariants!=null&&lod.RecoveryVariants.Length>0)
                {
                    int index=InfantryRecoveryTiming.RecoveryVariant(world,soldier,lod.RecoveryVariants.Length);
                    var recovery=lod.RecoveryVariants[index];poses=rising?recovery.GetUp:recovery.Knockdown;
                }
                posture[soldier] = 0; poseInitialized[soldier] = false;
                return Sample(poses, phase, false, out blend);
            }
            if(world.Soldiers.Captains[soldier]&&lod.Command!=null&&lod.Command.Length>0)
            {
                bool kneelingOfficer=RifleFireTiming.IsKneeling(world,unit,soldier,clock);
                var officer=lod.Command[0];var captainAction=world.Soldiers.MeleeActions[soldier];
                bool strike=MeleeSystem.IsStrike(captainAction)&&world.Soldiers.MeleeEnds[soldier]>clock;
                bool moving=world.Soldiers.MovementSpeeds[soldier]>.08f;
                float shotAge=clock-world.Soldiers.FireTimes[soldier];
                if(strike&&officer.OfficerSlash!=null&&officer.OfficerSlash.Length>0)
                    return Sample(kneelingOfficer&&officer.KneelingStrike!=null&&officer.KneelingStrike.Length>0?officer.KneelingStrike:officer.OfficerSlash,MeleePresentationTiming.Phase(captainAction,world.Soldiers.MeleeStarts[soldier],world.Soldiers.MeleeEnds[soldier],clock),false,out blend);
                if(!moving&&shotAge>=0&&shotAge<.75f&&officer.OfficerFire!=null&&officer.OfficerFire.Length>0)
                    return Sample(officer.OfficerFire,shotAge/.75f,false,out blend);
                bool combat=unit.InMelee||unit.AttackMode==InfantryAttackMode.BayonetCharge;
                var poses=kneelingOfficer&&officer.KneelingIdle!=null&&officer.KneelingIdle.Length>0?officer.KneelingIdle:combat&&!moving&&officer.OfficerReady!=null&&officer.OfficerReady.Length>0?officer.OfficerReady:moving?officer.March:officer.Idle;
                return Sample(poses,moving?AnimationPlayback.InfantryGait(world,soldier,clock):Mathf.Repeat(clock/library.IdleDuration+world.Soldiers.GaitPhases[soldier],1),true,out blend);
            }
            if(world.Soldiers.PikeSergeants[soldier]&&lod.Command!=null&&lod.Command.Length>3)
            {
                bool kneelingPike=RifleFireTiming.IsKneeling(world,unit,soldier,clock);
                var pike=lod.Command[3];
                bool strike=world.Soldiers.MeleeActions[soldier]==MeleeAction.Thrust&&world.Soldiers.MeleeEnds[soldier]>clock;
                bool combat=unit.Bracing||unit.InMelee||unit.AttackMode==InfantryAttackMode.BayonetCharge||world.Soldiers.MeleeEnds[soldier]>clock;
                bool moving=world.Soldiers.MovementSpeeds[soldier]>.08f;
                float phase=strike?MeleePresentationTiming.Phase(MeleeAction.Thrust,world.Soldiers.MeleeStarts[soldier],world.Soldiers.MeleeEnds[soldier],clock):moving?AnimationPlayback.InfantryGait(world,soldier,clock):Mathf.Repeat(clock/library.IdleDuration+world.Soldiers.GaitPhases[soldier],1);
                return Sample(strike&&kneelingPike&&pike.KneelingStrike!=null&&pike.KneelingStrike.Length>0?pike.KneelingStrike:strike&&pike.PikeThrust!=null&&pike.PikeThrust.Length>0?pike.PikeThrust:kneelingPike&&pike.KneelingIdle!=null&&pike.KneelingIdle.Length>0?pike.KneelingIdle:combat&&!moving&&pike.PikeReady!=null&&pike.PikeReady.Length>0?pike.PikeReady:moving?pike.March:pike.Idle,phase,!strike,out blend);
            }

            if(bayonets&&BayonetPresentationTiming.Handling(world,unit,soldier,clock,out float handling))
            {
                var set=library.Resolve(unit);var order=bayonets.Resolve(set.Material,Array.IndexOf(set.Lods,lod));
                if(order!=null)
                {
                    float speed=world.Soldiers.MovementSpeeds[soldier];
                    bool knee=RifleFireTiming.IsKneeling(world,unit,soldier,clock);
                    var poses=speed>.04f?(unit.BayonetsFixed?(unit.Running?order.Run:order.March):(unit.Running?order.UnfixRun:order.UnfixMarch)):knee?order.Kneeling:order.Standing;
                    posture[soldier]=knee?1:0;poseTimes[soldier]=clock;
                    return Sample(poses,unit.BayonetsFixed||speed>.04f?handling:1-handling,false,out blend);
                }
            }
            bool squareWall=unit.Formation==FormationKind.Square&&!unit.Moving&&!unit.Reforming
                &&FormationMath.IsBayonetWall(unit,world.Soldiers.FormationSlots[soldier])
                &&!FormationMath.IsFiringSlot(unit,world.Soldiers.FormationSlots[soldier]);
            bool brace=unit.BayonetsFixed&&(squareWall||unit.Bracing&&InfantryContactMovement.IsBraceRank(unit,world.Soldiers.FormationSlots[soldier]));
            MeleeAction action=world.Soldiers.MeleeActions[soldier];
            // An unfix command can arrive between simulation ticks. Never show
            // the previous blade-led strike after its bayonet has disappeared.
            if(action==MeleeAction.Thrust&&!unit.BayonetsFixed&&!world.Soldiers.Captains[soldier]
                &&!world.Soldiers.PikeSergeants[soldier]&&unit.Role!=UnitRole.Swordsman)action=MeleeAction.Ready;
            bool battleLine=world.ContactMovement.BattleLines[unit.Id]&&world.Soldiers.MovementSpeeds[soldier]<.08f;
            if((brace||battleLine)&&world.Soldiers.MeleeEnds[soldier]<=clock)action=MeleeAction.Ready;
            if(!SwordReadiness.UsesCarry(unit,action,world.Soldiers.MeleeEnds[soldier],clock)&&(brace||battleLine||world.Soldiers.MeleeEnds[soldier]>clock||(unit.InMelee&&!InfantryContactMovement.Fleeing(unit)&&world.Soldiers.MeleeTargets[soldier]>=0&&world.Soldiers.MovementSpeeds[soldier]<.08f)||(unit.AttackMode==InfantryAttackMode.BayonetCharge&&world.Soldiers.MovementSpeeds[soldier]<.08f))&&lod.Melee!=null)
            {
                if(action==MeleeAction.None)action=MeleeAction.Ready;
                if(action==MeleeAction.Recoil)action=MeleeAction.Stagger;
                foreach(var clip in lod.Melee)if(clip.Action==action)
                {
                    bool wall=!unit.Moving&&!unit.Reforming&&(unit.Formation==FormationKind.Square&&FormationMath.IsBayonetWall(unit,world.Soldiers.FormationSlots[soldier])||brace&&world.Soldiers.FormationSlots[soldier]<FormationMath.EffectiveFiles(unit));
                    float phase=world.Soldiers.MeleeEnds[soldier]>clock?MeleePresentationTiming.Phase(action,world.Soldiers.MeleeStarts[soldier],world.Soldiers.MeleeEnds[soldier],clock):0;
                    posture[soldier]=0;poseInitialized[soldier]=false;
                    return Sample(wall&&clip.Kneeling!=null&&clip.Kneeling.Length>0?clip.Kneeling:clip.Poses,phase,false,out blend);
                }
            }
            bool kneeling=RifleFireTiming.IsKneeling(world,unit,soldier,clock);
            float age=clock-world.Soldiers.FireTimes[soldier];
            bool scheduled=world.Soldiers.FireTimes[soldier]>=0;
            float previous=posture[soldier];
            if(!poseInitialized[soldier]){posture[soldier]=kneeling?(RifleFireTiming.IndependentFire(unit)?1f:Mathf.Clamp01((clock-world.Soldiers.KneelTimes[soldier])/RifleFireTiming.LowerDuration)):0;poseInitialized[soldier]=true;}
            else posture[soldier]=Mathf.MoveTowards(previous,kneeling?1:0,Mathf.Max(0,clock-poseTimes[soldier])/(kneeling?RifleFireTiming.LowerDuration:RifleFireTiming.RaiseDuration));
            poseTimes[soldier]=clock;
            if(!kneeling&&previous>=.999f){riseAiming[soldier]=scheduled&&age<0;riseReloadFrames[soldier]=Mathf.RoundToInt(reloadPhases[soldier]*Mathf.Max(0,lod.Reload.Length-1));}
            if(posture[soldier]>.0001f&&posture[soldier]<.9999f)
            {
                Mesh transition=lod.LowerToKneel;
                if(!kneeling){transition=riseAiming[soldier]?lod.RiseToAim:world.Soldiers.ReloadReadyTimes[soldier]<=clock?lod.RiseToIdle:
                    lod.RiseToReload!=null&&lod.RiseToReload.Length>0?lod.RiseToReload[Mathf.Clamp(riseReloadFrames[soldier],0,lod.RiseToReload.Length-1)]:lod.RiseToIdle;}
                if(RifleFireTiming.IndependentFire(unit)&&lod.KneelingAimTransition)transition=lod.KneelingAimTransition;
                if(transition){blend=Mathf.SmoothStep(0,1,posture[soldier]);return transition;}
            }
            if(world.Soldiers.MovementSpeeds[soldier]>.04f)
            {
                var motion=unit.AttackMode==InfantryAttackMode.BayonetCharge?lod.Charge:
                    unit.Running||InfantryContactMovement.Fleeing(unit)?lod.Run:lod.March;
                if(lod.Locomotion!=null&&lod.Locomotion.Length>0)
                {
                    int index=Mathf.Min(lod.Locomotion.Length-1,(int)(RifleFireTiming.Variation(soldier,71)*lod.Locomotion.Length));
                    var variant=lod.Locomotion[index];
                    float speed=world.Soldiers.MovementSpeeds[soldier];
                    var selected=unit.AttackMode==InfantryAttackMode.BayonetCharge?variant.Charge:
                        unit.Running||InfantryContactMovement.Fleeing(unit)?(speed<1.65f?variant.Jog:variant.Run):speed<.85f?variant.Walk:variant.March;
                    if(selected!=null&&selected.Length>0)motion=selected;
                }
                if(motion!=null&&motion.Length>0)
                {
                    return Sample(motion,AnimationPlayback.InfantryGait(world,soldier,clock),true,out blend);
                }
            }
            else if (unit.Formation == FormationKind.Square && FormationMath.IsBayonetWall(unit,world.Soldiers.FormationSlots[soldier])
                && !FormationMath.IsFiringSlot(unit,world.Soldiers.FormationSlots[soldier]) && kneeling && lod.RiseToAim)
            {
                // Aimed musket and fixed bayonet, held continuously in the kneeling wall.
                if(lod.KneelingFire!=null&&lod.KneelingFire.Length>0)return Sample(lod.KneelingFire,.45f,false,out blend);
                blend = 1f; return lod.RiseToAim;
            }
            else if((firingSoldier || (scheduled && world.Soldiers.ReloadReadyTimes[soldier] > clock))&&!unit.Moving)
            {
                if ((unit.Formation == FormationKind.Skirmish || (unit.Formation == FormationKind.Square
                    && FormationMath.IsBayonetWall(unit,world.Soldiers.FormationSlots[soldier]))) && kneeling)
                {
                    bool reloading = scheduled && age > RifleFireTiming.KneelDelay && world.Soldiers.ReloadReadyTimes[soldier] > clock;
                    if (reloading && lod.KneelingReload != null && lod.KneelingReload.Length > 0)
                        return Sample(lod.KneelingReload,RifleFireTiming.ReloadActionPhase(age-RifleFireTiming.KneelDelay,soldier),false,out blend);
                    if(lod.KneelingFire!=null&&lod.KneelingFire.Length>0)
                    {
                        float phase=scheduled&&age>=0&&age<RifleFireTiming.KneelDelay ? .5f+.5f*age/RifleFireTiming.KneelDelay : .45f;
                        return Sample(lod.KneelingFire,phase,false,out blend);
                    }
                    if (lod.RiseToAim) { blend = 1f; return lod.RiseToAim; }
                }
                if(scheduled&&age<0&&(RifleFireTiming.IndependentFire(unit)||age>=-(RifleFireTiming.LatestShotDelay+RifleFireTiming.RaiseDuration))&&lod.Fire!=null&&lod.Fire.Length>0)
                    return Sample(lod.Fire,.45f+.05f*Mathf.Clamp01(1f+age/RifleFireTiming.LatestShotDelay),false,out blend);
                float remaining=Mathf.Max(0,world.Soldiers.ReloadReadyTimes[soldier]-clock);
                float duration=world.Soldiers.ReloadDurations[soldier];
                float recover=Mathf.Max(.1f,world.Soldiers.KneelTimes[soldier]-world.Soldiers.FireTimes[soldier]);
                if(scheduled&&age>=0&&age<recover&&lod.Fire!=null&&lod.Fire.Length>0)
                    return Sample(lod.Fire,.5f+.5f*age/recover,false,out blend);
                if(RifleFireTiming.IsKneeling(world,unit,soldier,clock)&&lod.KneelingReload!=null&&lod.KneelingReload.Length>0)
                {
                    reloadPhases[soldier]=CartridgeRifleLibrary.UsesCartridges(unit.Weapon)?Mathf.Clamp01(1f-remaining/Mathf.Max(.1f,duration)):RifleFireTiming.ReloadActionPhase(clock-world.Soldiers.KneelTimes[soldier]-RifleFireTiming.LowerDuration,soldier);
                    return Sample(lod.KneelingReload,CartridgeReloadPhase(unit,soldier,reloadPhases[soldier]),false,out blend);
                }
                if(remaining>0)
                {
                    if(lod.Reload!=null&&lod.Reload.Length>0)
                    {
                        reloadPhases[soldier]=CartridgeRifleLibrary.UsesCartridges(unit.Weapon)?Mathf.Clamp01(1f-remaining/Mathf.Max(.1f,duration)):RifleFireTiming.ReloadActionPhase(age-recover-RifleFireTiming.LowerDuration,soldier);
                        return Sample(lod.Reload,CartridgeReloadPhase(unit,soldier,reloadPhases[soldier]),false,out blend);
                    }
                }
            }
            int idleVariant=Mathf.Min(2,(int)(RifleFireTiming.Variation(soldier,83)*3));
            var idle=lod.IdleVariants!=null&&lod.IdleVariants.Length>idleVariant?lod.IdleVariants[idleVariant].Poses:lod.Idle;
            // Stagger restrained bracing after a real incoming volley; never override firing/reloading.
            float reaction=clock-unit.LastIncomingFireTime-RifleFireTiming.Variation(soldier,89)*.45f;
            if(reaction>=0&&reaction<2.4f&&lod.UnderFireVariants!=null&&lod.UnderFireVariants.Length>idleVariant)
                return Sample(lod.UnderFireVariants[idleVariant].Poses,reaction/2.4f,true,out blend);
            return Sample(idle,Mathf.Repeat(clock+RifleFireTiming.Variation(soldier,53)*library.IdleDuration+unit.Id*.17f,Mathf.Max(.1f,library.IdleDuration))/Mathf.Max(.1f,library.IdleDuration),true,out blend);
        }
        void RenderCommand(Vector3 eye,Plane[] frustum)
        {
            if(world.Config.AssetReview)return;
            foreach(var unit in world.Battalions){
                if(unit.AliveCount<=0||!UnitRoster.HasCommandParty(unit))continue;
                var set=library.ResolveCommand(unit);
                int platoons=CommandPartyLayout.Platoons(unit,world.Config.PlatoonFiles);
                for(int role=0;role<5;role++)for(int member=0;member<CommandPartyLayout.Count(unit,role,platoons);member++){
                    var offset=CommandPartyLayout.Offset(unit,role,member,platoons);
                    var pos=world.Terrain.SurfacePoint(unit.Position+FormationMath.Right(unit.Facing)*offset.x+FormationMath.Forward(unit.Facing)*offset.y);
                    if(role==2)
                    {
                        int bearer=CommandPartyLayout.StandardBearer(world,unit,member);
                        if(bearer<0||!world.Soldiers.Alive[bearer])continue;
                        pos=world.SoldierPosition(bearer,clock);
                    }
                    float distance=(pos-eye).sqrMagnitude;if(distance>250*250)continue;
                    if(!BattleRenderBounds.PointVisible(frustum,pos))continue;
                    int level=distance>set.Lods[0].MaxDistance*set.Lods[0].MaxDistance?set.Lods.Length-1:0;
                    var lod=set.Lods[level];if(lod.Command==null||lod.Command.Length<=role||lod.Command[role]==null)continue;
                    bool moving=unit.Moving||unit.Routing;
                    var poses=moving?lod.Command[role].March:lod.Command[role].Idle;
                    float phase=moving?(commandTravel[unit.Id]+Mathf.Clamp(clock-world.Time,0,.05f)*unit.ActualSpeed)/(role==4?1.9f:.65f/.62f):clock/library.IdleDuration;
                    var mesh=Sample(poses,Mathf.Repeat(phase+unit.Id*.17f+member*.23f+role*.11f,1),true,out float blend);
                    if(!mesh||!lookup.TryGetValue(mesh,out var batch))continue;
                    batch=AtDistance(batch,distance);Activate(batch);
                    batch.Blends[batch.Count]=blend;
                    batch.Uniforms[batch.Count]=RegimentBaseStats.Uniform(unit.TroopTier);
                    batch.Matrices[batch.Count++]=Matrix4x4.TRS(pos,Quaternion.Euler(0,unit.Facing*Mathf.Rad2Deg,0),Vector3.one);
                    if(role==2){
                        int key=unit.Id*8+member;
                        if(!standards.TryGetValue(key,out var flags)){
                            flags=StandardCloth.ForBattalion(world,unit,member);
                            standards.Add(key,flags);
                        }
                        var rotation=Quaternion.Euler(0,unit.Facing*Mathf.Rad2Deg,0);
                        foreach(var flag in flags){
                            if(distance<80*80)flag.Advance(clock,rotation,moving);else flag.AdvanceDistant(clock,moving);
                            flag.Draw(Matrix4x4.TRS(pos,rotation,Vector3.one),camera);DrawCalls++;
                        }
                    }
                    if(batch.Count==batch.Matrices.Length)Flush(batch);
                }
            }
        }
        static void DestroyOwnedMesh(Mesh mesh)
        {
            if(Application.isPlaying)UnityEngine.Object.Destroy(mesh);else UnityEngine.Object.DestroyImmediate(mesh);
        }
        public void Dispose()
        {
            foreach(var flags in standards.Values)foreach(var flag in flags)flag.Dispose();standards.Clear();
            foreach(var mesh in standardMeshes)DestroyOwnedMesh(mesh);standardMeshes.Clear();
            foreach(var mesh in bladeMeshes)DestroyOwnedMesh(mesh);bladeMeshes.Clear();bladeBatches.Clear();
            foreach(var mesh in distantMeshes)DestroyOwnedMesh(mesh);distantMeshes.Clear();
        }
        static Mesh Sample(Mesh[] poses,float phase,bool loop,out float blend)
        {
            blend=0;
            if(poses==null||poses.Length==0)return null;
            int index=AnimationPlayback.Frame(poses.Length,phase,loop,out blend);
            return poses[index];
        }
        void Flush(Batch batch)
        {
            if(batch.Count==0)return;
            using var metric=BattleMetrics.Measure(BattleMetrics.Stage.CrowdSubmit);
            var properties=batch.UploadProperties();
            if(modernInstancing)
            {
                var parameters=new RenderParams(batch.Material) { camera=camera,matProps=properties,
                    shadowCastingMode=batch.CastShadows?ShadowCastingMode.On:ShadowCastingMode.Off,receiveShadows=true,lightProbeUsage=LightProbeUsage.Off };
                Graphics.RenderMeshInstanced(parameters,batch.Mesh,0,batch.Matrices,batch.Count);
            }
            else Graphics.DrawMeshInstanced(batch.Mesh,0,batch.Material,batch.Matrices,batch.Count,properties,
                batch.CastShadows?ShadowCastingMode.On:ShadowCastingMode.Off,true,0,camera,LightProbeUsage.Off);
            DrawCalls++;
            if(BattleMetrics.Enabled){BattleMetrics.CrowdDrawCalls++;BattleMetrics.CrowdInstances+=batch.Count;}
            batch.Count=0;
        }
    }
}
