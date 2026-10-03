using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEngine;
using UnityEngine.Rendering;
namespace EmpireBattle
{
    public sealed class CavalryView
    {
        sealed class Batch
        {
            public Mesh Mesh;public Material Material;public int Count;
            public Batch Far,VeryFar;public bool CastShadows=true;
            public Matrix4x4[] Matrices;public float[] Blends;
            public void EnsureBuffers(){if(Matrices!=null)return;Matrices=new Matrix4x4[256];Blends=new float[256];}
            public readonly MaterialPropertyBlock Properties=new MaterialPropertyBlock();
        }
        readonly BattleWorld world;readonly Camera camera;readonly CavalryCrowdLibrary library;
        readonly List<Batch> distantBatches=new List<Batch>();
        readonly List<Mesh> distantMeshes=new List<Mesh>();
        readonly Dictionary<Mesh,Batch> batches=new Dictionary<Mesh,Batch>();
        readonly float[] previousSpeed,brakeBorn;readonly float[] previousFacing,turnBorn,turnSign;
        public bool Ready=>library&&library.French?.Lods!=null&&library.British?.Lods!=null;
        public BattleCasualtyView Casualties {get;set;}
        public int VisibleRiders {get;private set;}
        public CavalryView(BattleWorld w,Camera view)
        {
            world=w;camera=view;library=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
            previousSpeed=new float[w.Soldiers.Alive.Length];brakeBorn=new float[previousSpeed.Length];previousFacing=new float[previousSpeed.Length];turnBorn=new float[previousSpeed.Length];turnSign=new float[previousSpeed.Length];
            for(int i=0;i<brakeBorn.Length;i++)brakeBorn[i]=turnBorn[i]=-100;
            if(Ready)
            {
                Register(library.French);Register(library.British);
                foreach(var unit in world.Battalions)if(unit.Kind==UnitKind.Cavalry)Register(library.Resolve(unit));
            }
        }
        void Register(CavalryCrowdSet set)
        {
            foreach(var lod in set.Lods)foreach(var clip in lod.Clips)foreach(var mesh in clip.Poses)
                if(!batches.ContainsKey(mesh))
                {
                    var batch=new Batch{Mesh=mesh,Material=set.Material};batches.Add(mesh,batch);
                    if(!world.Config.AssetReview&&lod==set.Lods[set.Lods.Length-1])
                    {
                        batch.Far=CreateDistantBatch(batch,.18f,true);
                        batch.VeryFar=CreateDistantBatch(batch,.36f,false);
                    }
                }
        }
        Batch CreateDistantBatch(Batch source,float cell,bool shadows)
        {
            var mesh=DistantUnitMesh.Build(source.Mesh,cell);
            if(mesh!=source.Mesh)distantMeshes.Add(mesh);
            var batch=new Batch{Mesh=mesh,Material=source.Material,CastShadows=shadows};
            distantBatches.Add(batch);return batch;
        }
        public void Dispose()
        {
            foreach(var mesh in distantMeshes)Object.Destroy(mesh);distantMeshes.Clear();
        }
        void Add(CavalryCrowdSet set,CavalryCrowdLod lod,MountedMotion motion,float phase,Matrix4x4 matrix)
        {
            var clip=lod.Clip(motion)??lod.Clip(MountedMotion.Idle);if(clip==null||clip.Poses.Length==0)return;
            int index=AnimationPlayback.Frame(clip.Poses.Length,phase,clip.Loop,out float blend);
            var mesh=clip.Poses[index];if(!batches.TryGetValue(mesh,out var batch))return;
            int tier=DistantUnitMesh.Tier(((Vector3)matrix.GetColumn(3)-camera.transform.position).sqrMagnitude);
            if(tier==2&&batch.VeryFar!=null)batch=batch.VeryFar;
            else if(tier>0&&batch.Far!=null)batch=batch.Far;
            batch.EnsureBuffers();
            batch.Matrices[batch.Count]=matrix;batch.Blends[batch.Count++]=blend;
            if(batch.Count==256)Flush(batch);
        }
        public void Render(Plane[] frustum,float time)
        {
            if(!Ready)return;VisibleRiders=0;var s=world.Soldiers;
            foreach(var u in world.Battalions)
            {
                if(u.Kind!=UnitKind.Cavalry)continue;
                var set=library.Resolve(u);Register(set);
                string issued=NapoleonicOutfits.MountedWeapon(u.Loadout);
                if(!GeometryUtility.TestPlanesAABB(frustum,BattleRenderBounds.Regiment(world,u,time)))continue;
                for(int i=u.SoldierStart;i<u.SoldierStart+u.SoldierCount;i++)
                {
                    if(!s.Alive[i])continue;
                    var position=world.Terrain.SurfacePoint(s.Positions[i]+s.Velocities[i]*Mathf.Clamp(time-world.Time,0,.05f));float speed=s.MovementSpeeds[i];float facing=s.Facings[i];
                    var lod=set.Lods[(position-camera.transform.position).sqrMagnitude>2500&&set.Lods.Length>1?1:0];
                    float phase=AnimationPlayback.MountedGait(s.GaitPhases[i],speed,time,world.Time);MountedMotion motion=speed>5?MountedMotion.Gallop:speed>2.5f?MountedMotion.Trot:speed>.08f?MountedMotion.Walk:MountedMotion.Idle;
                    if(u.ChargePhase==ChargePhase.Approach&&speed>5)motion=MountedMotion.Charge;
                    if(previousSpeed[i]>3&&speed<previousSpeed[i]-.15f)brakeBorn[i]=time;
                    float turn=FormationMath.AngleDifference(previousFacing[i],facing);if(Mathf.Abs(turn)>.015f){turnBorn[i]=time;turnSign[i]=turn;}
                    if(time-brakeBorn[i]<.75f&&speed<3){motion=MountedMotion.Brake;phase=(time-brakeBorn[i])/.75f;}
                    else if(u.ChargePhase==ChargePhase.Recovering&&Mathf.Abs(FormationMath.AngleDifference(u.Facing,u.DesiredFacing))>.08f){motion=FormationMath.AngleDifference(u.Facing,u.DesiredFacing)<0?MountedMotion.TurnLeft:MountedMotion.TurnRight;phase=s.GaitPhases[i];}
                    if(s.MeleeEnds[i]>time)
                    {
                        var action=s.MeleeActions[i];phase=MeleePresentationTiming.Phase(action,s.MeleeStarts[i],s.MeleeEnds[i],time);
                        motion=action==MeleeAction.Thrust&&issued=="lance"?MountedMotion.LanceThrust:action==MeleeAction.SlashLeft?MountedMotion.SlashLeft:action==MeleeAction.SlashRight?MountedMotion.SlashRight:action==MeleeAction.Stumble?MountedMotion.Stumble:motion;
                    }
                    if(issued=="lance")motion=motion==MountedMotion.Idle?MountedMotion.LanceReady:motion==MountedMotion.Walk?MountedMotion.LanceWalk:motion==MountedMotion.Trot?MountedMotion.LanceTrot:motion==MountedMotion.Gallop?MountedMotion.LanceGallop:motion==MountedMotion.Charge?MountedMotion.LanceCharge:motion;
                    if((issued=="carbine"||issued=="pistol")&&!u.MeleeMode&&!u.InMelee&&u.AttackMode!=InfantryAttackMode.BayonetCharge)
                    {bool small=issued=="pistol";motion=motion==MountedMotion.Walk?(small?MountedMotion.PistolCarryWalk:MountedMotion.CarbineCarryWalk):motion==MountedMotion.Trot?(small?MountedMotion.PistolCarryTrot:MountedMotion.CarbineCarryTrot):motion==MountedMotion.Gallop||motion==MountedMotion.Charge?(small?MountedMotion.PistolCarryGallop:MountedMotion.CarbineCarryGallop):motion;}

                    if((issued=="carbine"||issued=="pistol")&&speed<.08f&&!u.InMelee&&!u.MeleeMode&&!u.Routing&&s.MeleeEnds[i]<=time)
                    {
                        float age=time-s.FireTimes[i];bool pistol=issued=="pistol";
                        if(age>=0&&age<CombatSystem.MountedFireDuration){motion=pistol?MountedMotion.PistolFire:MountedMotion.MountedFire;phase=age/CombatSystem.MountedFireDuration;}
                        else if(s.ReloadReadyTimes[i]>time){motion=pistol?MountedMotion.PistolReload:MountedMotion.MountedReload;phase=1-Mathf.Clamp01((s.ReloadReadyTimes[i]-time)/Mathf.Max(.1f,s.ReloadDurations[i]));}
                        else{motion=pistol?MountedMotion.PistolAim:MountedMotion.MountedAim;phase=0;}
                    }
                    previousSpeed[i]=speed;previousFacing[i]=facing;
                    Add(set,lod,motion,phase,Matrix4x4.TRS(position,Quaternion.Euler(0,facing*Mathf.Rad2Deg,0),Vector3.one));VisibleRiders++;
                }
            }
            for(int d=0;d<world.DeathCount;d++)
            {
                var death=world.Deaths[world.DeadSoldiers[d]];if(death.Kind!=UnitKind.Cavalry)continue;
                var pose=CavalryCasualtyMotion.Sample(death,time);
                Vector3 horse=pose.HorsePosition,rider=pose.RiderPosition;
                horse.y+=world.Terrain.HeightAt(new Vector2(horse.x,horse.z));
                rider.y+=world.Terrain.HeightAt(new Vector2(rider.x,rider.z));
                var bounds=new Bounds(horse,Vector3.one*8);bounds.Encapsulate(new Bounds(rider,Vector3.one*8));
                if(!GeometryUtility.TestPlanesAABB(frustum,bounds))continue;
                var set=library.Resolve(world.Battalions[death.UnitId]);
                var lod=set.Lods[(rider-camera.transform.position).sqrMagnitude>2500&&set.Lods.Length>1?1:0];
                var rotation=Quaternion.Euler(0,death.Facing*Mathf.Rad2Deg,0);
                if(!death.HorseSurvived)Add(set,lod,pose.HorseMotion,pose.HorsePhase,Matrix4x4.TRS(horse,rotation*Quaternion.Euler(0,pose.HorseYaw,0),Vector3.one));
                if(!Casualties||!Casualties.HasPhysicalCorpse(death.Soldier))
                    Add(set,lod,pose.RiderMotion,pose.RiderPhase,Matrix4x4.TRS(rider,rotation,Vector3.one));
            }
            foreach(var horse in world.LooseHorses.Values)
            {
                if(horse.Escaped)continue;
                Vector3 position=world.Terrain.SurfacePoint(horse.Position);
                MountedMotion motion=horse.Speed>5?MountedMotion.HorseGallop:horse.Speed>2.5f?MountedMotion.HorseTrot:horse.Speed>.08f?MountedMotion.HorseWalk:MountedMotion.HorseIdle;
                float phase=AnimationPlayback.MountedGait(horse.GaitPhase,horse.Speed,time,world.Time);
                float casualtyYaw=0;
                if(!horse.Targetable)
                {
                    var pose=CavalryCasualtyMotion.Sample(horse.Death,time);
                    position=pose.HorsePosition;position.y=world.Terrain.HeightAt(new Vector2(position.x,position.z));
                    motion=pose.HorseMotion;phase=pose.HorsePhase;
                    casualtyYaw=pose.HorseYaw;
                }
                if(!GeometryUtility.TestPlanesAABB(frustum,new Bounds(position,Vector3.one*8)))continue;
                var set=library.Resolve(world.Battalions[horse.UnitId]);
                var lod=set.Lods[(position-camera.transform.position).sqrMagnitude>2500&&set.Lods.Length>1?1:0];
                Add(set,lod,motion,phase,Matrix4x4.TRS(position,Quaternion.Euler(0,horse.Facing*Mathf.Rad2Deg+casualtyYaw,0),Vector3.one));
            }
            foreach(var batch in batches.Values)Flush(batch);
            foreach(var batch in distantBatches)Flush(batch);
        }
        void Flush(Batch batch)
        {
            if(batch.Count==0)return;batch.Properties.SetFloatArray("_PoseBlend",batch.Blends);
            Graphics.DrawMeshInstanced(batch.Mesh,0,batch.Material,batch.Matrices,batch.Count,batch.Properties,batch.CastShadows?ShadowCastingMode.On:ShadowCastingMode.Off,true,0,camera,LightProbeUsage.Off);batch.Count=0;
        }
    }
}
