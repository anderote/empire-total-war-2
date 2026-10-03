using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle.Art;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace EmpireBattle
{
    /// <summary>Bounded local physics for casualties and surviving blast knockdowns.</summary>
    public sealed partial class BattleCasualtyView : MonoBehaviour
    {
        readonly Plane[] frustum = new Plane[6];
        public const int MaxRagdolls=96;
        const int OrdinaryRagdolls=32;
        const float RagdollDistance=350f, SettleSeconds=5f;
        sealed class Ragdoll
        {
            public GameObject Root;
            public Transform[] Bones;
            public Rigidbody[] Bodies;
            public SkinnedMeshRenderer Skin;
            public InfantryRagdollSet Set;
            public int Soldier=-1;
            public float Born, QuietTime;
            public SurvivorMotion Survivor;
            public Vector3[] PreviousPositions, Positions;
            public Quaternion[] PreviousRotations, Rotations;
            public readonly List<GameObject> Props=new List<GameObject>();
        }
        sealed class Batch
        {
            public Material Material;
            public readonly Matrix4x4[] Matrices=new Matrix4x4[256];
            public readonly float[] Blends=new float[256];
            public readonly MaterialPropertyBlock Properties=new MaterialPropertyBlock();
            public int Count;
        }
        BattleWorld world;
        Camera cameraView;
        InfantryCrowdLibrary crowd;
        InfantryCrowdSet[] corpseSets,commandCorpseSets;
        CavalryCrowdLibrary cavalryCrowd;
        InfantryRagdollLibrary rigs,mountedRigs;
        readonly InfantryRagdollAppearance appearances=new InfantryRagdollAppearance();
        readonly Dictionary<int,Material> corpseMaterials=new Dictionary<int,Material>();
        Scene physicsScene;
        PhysicsScene physics;
        const float PhysicsStep=1f/120f;
        float physicsRemainder;
        readonly List<Ragdoll> pool=new List<Ragdoll>();
        readonly Dictionary<Mesh,Batch> batches=new Dictionary<Mesh,Batch>();
        Mesh[] frozen;
        bool[] physical;
        Vector3[] anchors, flightEnds;
        float[] flightTimes;
        InfantryGroundContact.RestingPoseCache[] restingPoses;
        RagdollGround ground;
        readonly Bounds[] collisionRegions=new Bounds[MaxRagdolls+MaxLooseEquipment];
        void UpdateGroundDamage(float dt)
        {
            if(!BattleOptimizationSettings.FastForwardOptimizations){ground.Update();return;}
            int count=0;
            foreach(var rag in pool)if(rag.Soldier>=0)
            {
                var bounds=new Bounds(rag.Bones[0].position,Vector3.zero);
                foreach(var body in rag.Bodies)
                {
                    bounds.Encapsulate(body.position);
                    bounds.Encapsulate(body.position+body.linearVelocity*dt);
                }
                // Includes body shapes, impulses applied during this tick, bounces
                // and solver correction across both physics substeps.
                bounds.Expand(24f);collisionRegions[count++]=bounds;
            }
            foreach(var item in looseEquipment)if(item.Active)
            {
                var bounds=new Bounds(item.Root.transform.position,Vector3.one*4);
                bounds.Encapsulate(item.Root.transform.position+item.Body.linearVelocity*dt);bounds.Expand(8f);
                collisionRegions[count++]=bounds;
            }
            ground.Update(collisionRegions,count);
        }
        [NonSerialized] public ArtillerySystem Artillery;
        int observedImpact;
        Vector3[] corpseOffsets, corpseVelocity;
        float[] corpseKickTime;
        void ScatterCorpses(float dt)
        {
            if (Artillery == null) return;
            int first = Mathf.Max(observedImpact + 1, Artillery.ImpactSequence - Artillery.Impacts.Length + 1);
            for (int sequence = first; sequence <= Artillery.ImpactSequence; sequence++)
            {
                var hit = Artillery.Impacts[(sequence - 1) % Artillery.Impacts.Length];
                if (hit.Sequence != sequence || !hit.TerrainStrike) continue;
                if (hit.ExplosiveShell && !hit.Detonates) continue;
                float radius = hit.Detonates ? ArtillerySystem.ExplosionRadius(hit.AmmunitionType) : 1.5f;
                for (int j = 0; j < world.DeathCount; j++)
                {
                    int id = world.DeadSoldiers[j]; var death = world.Deaths[id];
                    if (world.Time - death.Born < .1f) continue;
                    Vector3 delta = anchors[id] - hit.Position;
                    float distance = delta.magnitude; if (distance > radius) continue;
                    Vector3 away = new Vector3(delta.x, 0, delta.z);
                    float blastFalloff = 1f - distance / radius;
                    Vector3 impulse = hit.ExplosiveShell ? (away.normalized * 7f + Vector3.up * 5f) * blastFalloff * blastFalloff
                        : new Vector3(hit.Direction.x, .3f, hit.Direction.z) * Mathf.Min(hit.Speed * .07f, 10f);
                    if (IsPhysical(id))
                    {
                        foreach (var rag in pool) if (rag.Soldier == id)
                        {
                            rag.Born = VisualTime;
                            foreach (var body in rag.Bodies) { body.AddForce(impulse, ForceMode.VelocityChange); body.WakeUp(); }
                        }
                    }
                    else { corpseVelocity[id] += impulse; corpseKickTime[id] = VisualTime; }
                }
            }
            observedImpact = Artillery.ImpactSequence;
            for (int j = 0; j < world.DeathCount; j++)
            {
                int id = world.DeadSoldiers[j]; if (corpseVelocity[id].sqrMagnitude < .0001f || IsPhysical(id)) continue;
                Vector3 position = anchors[id];
                Vector3 move = corpseVelocity[id] * dt + Vector3.down * (4.905f * dt * dt);
                position += move; corpseVelocity[id] += Vector3.down * (9.81f * dt);
                float floor = world.Terrain.HeightAt(new Vector2(position.x, position.z)) + .08f;
                if (position.y < floor) { move.y += floor - position.y; position.y = floor; corpseVelocity[id] = Vector3.zero; }
                corpseOffsets[id] += move; anchors[id] = position;
            }
        }
        BattleBlood blood;
        ArtilleryGore gore;
        public int FragmentedBodies => gore?.FragmentedBodies ?? 0;
        public int SeveredLimbs => gore?.SeveredLimbs ?? 0;
        bool renderingWarmed;
        public float VisualTime { get; private set; }
        public int ActiveRagdolls { get; private set; }
        public int PersistentCorpses => world==null?0:world.DeathCount;
        public int FrozenRagdolls { get; private set; }
        public float HighestFrozenBody { get; private set; }
        public double SnapshotMilliseconds { get; private set; }
        public BattleBlood Blood => blood;

        public void Initialize(BattleWorld value,Camera camera)
        {
            world=value;cameraView=camera;
            crowd=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
            corpseSets=new InfantryCrowdSet[world.Battalions.Length];commandCorpseSets=new InfantryCrowdSet[world.Battalions.Length];
            rigs=Resources.Load<InfantryRagdollLibrary>("BakedInfantry/InfantryRagdollLibrary");
            mountedRigs=Resources.Load<InfantryRagdollLibrary>("BakedCavalry/CavalryRagdollLibrary");
            cavalryCrowd=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
            if(mountedRigs&&cavalryCrowd)foreach(var unit in world.Battalions)if(unit.Kind==UnitKind.Cavalry)
                ResolveMountedRig(unit);
            frozen=new Mesh[world.Soldiers.Alive.Length];physical=new bool[frozen.Length];anchors=new Vector3[frozen.Length];
            corpseOffsets = new Vector3[frozen.Length]; corpseVelocity = new Vector3[frozen.Length]; corpseKickTime = new float[frozen.Length];
            flightEnds=new Vector3[frozen.Length];flightTimes=new float[frozen.Length];
            restingPoses=new InfantryGroundContact.RestingPoseCache[frozen.Length];
            physicsScene=SceneManager.CreateScene("Casualty physics "+GetEntityId(),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            physics=physicsScene.GetPhysicsScene();
            BuildGround();
            if(rigs && rigs.French!=null && rigs.British!=null)
                for(int i=0;i<Mathf.Min(OrdinaryRagdolls,frozen.Length);i++)
                {
                    var rag=CreateRig(equipment.Resolve(i%2==0?rigs.French:rigs.British));pool.Add(rag);
                }
            if(rigs)gore=new ArtilleryGore(world,cameraView,rigs);
            if(rigs&&crowd)foreach(var unit in world.Battalions)if(unit.Kind==UnitKind.Infantry)
            {
                // Skin transfer belongs in setup, not in the first canister impact frame.
                ResolveInfantryRig(unit,false,false);
                if(!world.Config.AssetReview&&UnitRoster.HasCommandParty(unit))ResolveStandardRig(unit);
                if(!world.Config.AssetReview)
                {
                    bool captain=false,pike=false;
                    for(int id=unit.SoldierStart;id<unit.SoldierStart+unit.SoldierCount;id++)
                    {captain|=world.Soldiers.Captains[id];pike|=world.Soldiers.PikeSergeants[id];}
                    if(captain)ResolveInfantryRig(unit,true,false);
                    if(pike)ResolveInfantryRig(unit,false,true);
                }
            }
            blood=gameObject.AddComponent<BattleBlood>();blood.Initialize(world,camera,this);
            InitializeSurvivors();
            world.SoldierDied+=OnDeath;
            if(!rigs)Debug.LogWarning("Ragdoll assets missing; casualties will use persistent baked deaths. Run InfantryRagdollBuild.Bake.");
        }
        public void ClearReviewBattalion(int unitId)
        {
            if(!world.Config.AssetReview)throw new InvalidOperationException("Review clearing requires a workbench world.");
            ClearSurvivors(unitId);
            blood.RetireReviewSoldiers(unitId);
            ClearLooseEquipment(unitId);
            foreach(var rag in pool)if(rag.Soldier>=0&&world.Soldiers.BattalionIds[rag.Soldier]==unitId)
            {
                foreach(var body in rag.Bodies){body.linearVelocity=body.angularVelocity=Vector3.zero;body.isKinematic=true;}
                rag.Root.SetActive(false);rag.Soldier=-1;
            }
            var unit=world.Battalions[unitId];
            for(int id=unit.SoldierStart;id<unit.SoldierStart+unit.SoldierCount;id++)
            {
                if(frozen[id]){Destroy(frozen[id]);FrozenRagdolls=Mathf.Max(0,FrozenRagdolls-1);}
                frozen[id]=null;physical[id]=false;corpseMaterials.Remove(id);corpseOffsets[id]=corpseVelocity[id]=Vector3.zero;
                corpseKickTime[id]=flightTimes[id]=0;anchors[id]=world.Terrain.SurfacePoint(world.Soldiers.Positions[id]);
            }
            ActiveRagdolls=pool.Count(r=>r.Soldier>=0);
        }
        void BuildGround() { ground=new RagdollGround(world.Terrain,physicsScene); }
        InfantryRagdollSet ResolveInfantryRig(BattalionState unit,bool captain,bool pike)
        {
            bool command=!world.Config.AssetReview&&(captain||pike);
            var appearance=command?crowd.ResolveCommand(unit):crowd.Resolve(unit);
            // Resolve by actual outfit: campaign opponents can use either uniform.
            if(!command&&appearance==crowd.French){var set=equipment.Resolve(rigs.French);gore?.Prepare(set);return set;}
            if(!command&&appearance==crowd.British){var set=equipment.Resolve(rigs.British);gore?.Prepare(set);return set;}
            var reference=unit.Side==ArmySide.French?rigs.French:rigs.British;
            return equipment.Resolve(appearances.Resolve(appearance,reference,command?(captain?0:3):-1));
        }
        InfantryRagdollSet ResolveStandardRig(BattalionState unit)
        {
            var reference=unit.Side==ArmySide.French?rigs.French:rigs.British;
            return equipment.Resolve(appearances.Resolve(crowd.ResolveCommand(unit),reference,2));
        }
        InfantryRagdollSet ResolveMountedRig(BattalionState unit)
        {
            if(!mountedRigs||!cavalryCrowd)return null;
            var reference=unit.Side==ArmySide.French?mountedRigs.French:mountedRigs.British;
            return equipment.Resolve(appearances.ResolveMounted(cavalryCrowd.Resolve(unit),reference));
        }
        void OnDeath(SoldierDeath death)
        {
            anchors[death.Soldier]=death.Position;
            if(death.Cannon)flightEnds[death.Soldier]=CannonFlightPosition(death,world.Terrain,4f,out flightTimes[death.Soldier]);
            blood.Emit(death,VisualTime);
            if(ConvertSurvivorDeath(death))return;
            bool mounted=death.Kind==UnitKind.Cavalry;
            if((cameraView.transform.position-death.Position).sqrMagnitude>RagdollDistance*RagdollDistance)return;
            if(ActiveRagdolls>=(death.Cannon?MaxRagdolls:OrdinaryRagdolls))return;
            var library=mounted?mountedRigs:rigs;
            if(!library)return;
            // Every nearby death is eligible; distance and active-body budgets retain
            // the animated fallback for distant casualties and simultaneous overflow.
            var set=death.Side==ArmySide.French?library.French:library.British;
            if(mounted)set=ResolveMountedRig(world.Battalions[death.UnitId]);
            if(!mounted)set=ResolveInfantryRig(world.Battalions[death.UnitId],world.Soldiers.Captains[death.Soldier],world.Soldiers.PikeSergeants[death.Soldier]);
            if(!mounted&&CommandPartyLayout.StandardMember(world,world.Battalions[death.UnitId],death.Soldier)>=0)
                set=ResolveStandardRig(world.Battalions[death.UnitId]);
            if(set==null||!set.Mesh)return;
            Ragdoll rag=null;
            foreach(var candidate in pool)if(candidate.Soldier<0 && candidate.Set==set){rag=candidate;break;}
            if(rag==null)foreach(var candidate in pool)if(candidate.Soldier<0){rag=candidate;break;}
            if(rag==null && pool.Count<MaxRagdolls){rag=CreateRig(set);pool.Add(rag);}
            // A burst beyond the physics budget still gets an animated death and a permanent body.
            if(rag==null)return;
            rag.Set=set;corpseMaterials[death.Soldier]=set.Material;
            rag.Root.SetActive(false);
            foreach(var prop in rag.Props){prop.SetActive(false);if(Application.isPlaying)Destroy(prop);else DestroyImmediate(prop);}rag.Props.Clear();
            if(set.Props!=null)foreach(var prop in set.Props)
            {
                var shape=new GameObject("Weapon ground contact"){layer=30};shape.transform.SetParent(rag.Bones[prop.Bone],false);
                shape.transform.localPosition=prop.Center;shape.transform.localRotation=Quaternion.FromToRotation(Vector3.up,prop.Axis);
                var capsule=shape.AddComponent<CapsuleCollider>();capsule.radius=prop.Radius;capsule.height=prop.Length;capsule.contactOffset=.002f;
                rag.Props.Add(shape);
            }
            rag.Skin.enabled=true;rag.Skin.sharedMesh=set.Mesh;rag.Skin.sharedMaterials=new[]{set.Material};
            rag.Root.transform.SetPositionAndRotation(death.Position,Quaternion.Euler(0,death.Facing*Mathf.Rad2Deg,0));
            InfantryBonePose pose=StartingPose(set,death);
            for(int i=0;i<rag.Bones.Length;i++)
                rag.Bones[i].SetPositionAndRotation(rag.Root.transform.TransformPoint(pose.Positions[i]),rag.Root.transform.rotation*pose.Rotations[i]);
            rag.Soldier=death.Soldier;rag.Born=VisualTime;rag.QuietTime=0f;physical[death.Soldier]=true;
            CapturePose(rag,true);
            rag.Root.SetActive(true);
            SpawnLooseEquipment(rag,death);
            if(!mounted&&(set.Material==rigs.French.Material||set.Material==rigs.British.Material)&&gore!=null&&gore.Sever(death,rag.Skin,set))blood.EmitSpray(death.Position+Vector3.up*.9f,death.Impulse.normalized,true,VisualTime);
            if(!mounted&&ArtilleryGore.FullFragmentation(death)&&rag.Skin.sharedMesh.vertexCount==0)
            {
                // An empty persistent marker prevents the baked fallback from drawing an intact soldier.
                frozen[death.Soldier]=new Mesh{name="Fully fragmented casualty "+death.Soldier};
                rag.Root.SetActive(false);rag.Soldier=-1;FrozenRagdolls++;return;
            }
            // Musket hit vectors point from shooter to victim and carry range,
            // whereas artillery vectors already encode a physical blast impulse.
            Vector3 hitImpulse=death.Cannon?death.Impulse:Vector3.ClampMagnitude(death.Impulse,1f);
            Vector3 launch=mounted?MountedLaunchVelocity(death):Vector3.ClampMagnitude(death.Velocity,5f)*.35f+hitImpulse*(death.Cause==DeathCause.Shell?1f:.65f);
            Vector3 tumble=mounted?Quaternion.Euler(0,death.Facing*Mathf.Rad2Deg,0)*Vector3.right*Mathf.Lerp(1.8f,5f,Mathf.Clamp01(death.Velocity.magnitude/8f)):
                Vector3.Cross(Vector3.up,death.Impulse.normalized)*3.5f;
            if(!mounted&&!death.Cannon&&death.Impulse.sqrMagnitude<.001f)
                // Melee and other deaths may have no hit impulse. Give them a gentle
                // forward/backward collapse rather than leaving an upright limp rig.
                tumble=Quaternion.Euler(0,death.Facing*Mathf.Rad2Deg,0)*Vector3.right
                    *(RifleFireTiming.Variation(death.Soldier,317)<.5f?-2f:2f);
            foreach(var body in rag.Bodies)
            {
                body.isKinematic=false;body.linearVelocity=launch;
                body.angularVelocity=tumble+Vector3.up*((death.Soldier%7)-3)*.25f;body.WakeUp();
                // Differential torso impulse knocks the body off its feet rather than translating it upright.
                if(body.name=="pelvis"||body.name=="spine2")body.AddForce((mounted?Vector3.ClampMagnitude(death.Impulse,1)*.75f:hitImpulse)*(body.name=="spine2"?1.1f:.6f),ForceMode.VelocityChange);
            }
            ActiveRagdolls++;
        }
        public static Vector3 MountedLaunchVelocity(SoldierDeath death)
        {
            var rotation=Quaternion.Euler(0,death.Facing*Mathf.Rad2Deg,0);
            float side=RifleFireTiming.Variation(death.Soldier,317)<.5f?-1:1;
            var momentum=death.Velocity;momentum.y=0;
            var hit=death.Impulse;hit.y=0;
            return Vector3.ClampMagnitude(momentum*.72f,6)+rotation*Vector3.right*(side*1.4f)
                +Vector3.ClampMagnitude(hit,1)*(death.Cannon?1.5f:.45f)+Vector3.up*(death.Cannon?1.1f:.45f);
        }
        public bool HasPhysicalCorpse(int soldier)=>physical!=null&&soldier>=0&&soldier<physical.Length&&physical[soldier];
        InfantryBonePose StartingPose(InfantryRagdollSet set,SoldierDeath d)
        {
            if(d.Kind==UnitKind.Cavalry&&set.Mounted!=null&&set.Mounted.Length>0)
                return set.Mounted[Mathf.Clamp(Mathf.FloorToInt(d.GaitPhase*set.Mounted.Length),0,set.Mounted.Length-1)];
            InfantryBonePose[] poses=set.Idle;float phase=Mathf.Repeat(d.Born+d.UnitId*.17f,crowd.IdleDuration)/crowd.IdleDuration;
            if(d.Moving){poses=d.Charging?set.Charge:d.Running?set.Run:set.March;phase=d.GaitPhase;}
            else
            {
                float age=d.Born-d.LastFireTime,recover=Mathf.Max(.1f,crowd.FireDuration*.5f)*Mathf.Lerp(.88f,1.12f,RifleFireTiming.Variation(d.Soldier,17));
                if(age>=0&&age<recover){poses=set.Fire;phase=.5f+.5f*age/recover;}
                else if(d.ReloadRemaining>0){poses=d.ReloadRemaining<=1.3f?set.Fire:set.Reload;phase=d.ReloadRemaining<=1.3f?.5f*(1-d.ReloadRemaining/1.3f):(world.Config.ReloadSeconds-d.ReloadRemaining-recover)/Mathf.Max(.1f,world.Config.ReloadSeconds-recover-1.3f);}
            }
            return poses[Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(phase)*poses.Length),0,poses.Length-1)];
        }
        Ragdoll CreateRig(InfantryRagdollSet set)
        {
            var rag=new Ragdoll{Set=set,Root=new GameObject("Pooled cannon ragdoll"){layer=30},Bones=new Transform[set.Names.Length]};
            SceneManager.MoveGameObjectToScene(rag.Root,physicsScene);
            for(int i=0;i<rag.Bones.Length;i++)
            {
                var bone=new GameObject(set.Names[i]).transform;bone.SetParent(set.Parents[i]<0?rag.Root.transform:rag.Bones[set.Parents[i]],false);
                bone.SetPositionAndRotation(set.Positions[i],set.Rotations[i]);rag.Bones[i]=bone;
            }
            rag.Skin=rag.Root.AddComponent<SkinnedMeshRenderer>();rag.Skin.sharedMesh=set.Mesh;rag.Skin.sharedMaterial=set.Material;rag.Skin.bones=rag.Bones;
            rag.Skin.rootBone=rag.Bones[0];rag.Skin.updateWhenOffscreen=true;
            var bodies=new List<Rigidbody>();
            string[] starts={"pelvis","spine2","head","l_thigh","l_calf","l_foot","r_thigh","r_calf","r_foot","l_upperarm_twist","l_foretwist","l_hand","r_upperarm_twist","r_foretwist","r_hand"};
            string[] ends={"spine","neck","head","l_calf","l_foot","l_toe0","r_calf","r_foot","r_toe0","l_foretwist","l_hand","l_hand","r_foretwist","r_hand","r_hand"};
            for(int i=0;i<starts.Length;i++)
            {
                int index=Array.IndexOf(set.Names,starts[i]),end=Array.IndexOf(set.Names,ends[i]);if(index<0||end<0)continue;
                var bone=rag.Bones[index];var body=bone.gameObject.AddComponent<Rigidbody>();body.mass=i==0?14:i==1?18:i==2?5:i<9?6:2;
                body.excludeLayers=1<<30;
                body.isKinematic=true;body.linearDamping=.12f;body.angularDamping=.65f;body.solverIterations=10;body.solverVelocityIterations=4;
                body.collisionDetectionMode=CollisionDetectionMode.Discrete;
                Vector3 direction=rag.Bones[end].position-bone.position;
                float radius=i==0?.13f:i==1?.16f:i==2?.105f:i<9?.075f:.045f;
                if(direction.sqrMagnitude<.002f)direction=Vector3.up*(i==2?.16f:.09f);
                if(i==1)direction=Vector3.down*.23f;
                var shapeObject=new GameObject("Body shape"){layer=30};
                var shape=shapeObject.transform;shape.SetParent(bone,false);shape.position=bone.position+direction*.5f;shape.rotation=Quaternion.FromToRotation(Vector3.up,direction.normalized);
                var capsule=shape.gameObject.AddComponent<CapsuleCollider>();capsule.contactOffset=.002f;capsule.radius=radius;capsule.height=Mathf.Max(radius*2,direction.magnitude+radius);bodies.Add(body);
            }
            foreach(var body in bodies)
            {
                Rigidbody parent=null;for(var t=body.transform.parent;t&&t!=rag.Root.transform;t=t.parent)if(t.TryGetComponent(out parent))break;
                if(!parent)continue;
                var joint=body.gameObject.AddComponent<CharacterJoint>();joint.connectedBody=parent;joint.enablePreprocessing=false;
                joint.axis=Vector3.left;joint.swingAxis=Vector3.up;
                bool shoulder=body.name.Contains("upperarm"),hip=body.name.Contains("thigh");
                joint.lowTwistLimit=new SoftJointLimit{limit=shoulder?-65:-25};joint.highTwistLimit=new SoftJointLimit{limit=shoulder?65:25};
                joint.swing1Limit=new SoftJointLimit{limit=shoulder?100:hip?80:55};joint.swing2Limit=new SoftJointLimit{limit=shoulder?85:hip?60:35};
                joint.enableProjection=true;joint.projectionDistance=.04f;joint.projectionAngle=12;
            }
            rag.Bodies=bodies.ToArray();
            rag.PreviousPositions=new Vector3[rag.Bones.Length];rag.Positions=new Vector3[rag.Bones.Length];
            rag.PreviousRotations=new Quaternion[rag.Bones.Length];rag.Rotations=new Quaternion[rag.Bones.Length];
            rag.Root.SetActive(false);return rag;
        }
        public void Step(float dt)
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.CasualtyStep);
            if(dt<=0)return;
            VisualTime+=dt;
            // Restore solver poses before applying forces or advancing local physics.
            foreach(var rag in pool)if(rag.Soldier>=0)ApplyPose(rag,1f);
            if (ActiveRagdolls > 0||ActiveLooseEquipment>0) { using var scope=BattleMetrics.Measure(BattleMetrics.Stage.RagdollGround); UpdateGroundDamage(dt); }
            using (BattleMetrics.Measure(BattleMetrics.Stage.CorpseScatter)) ScatterCorpses(dt);
            if(ActiveRagdolls>0||ActiveLooseEquipment>0)
            {
                using (BattleMetrics.Measure(BattleMetrics.Stage.RagdollPhysics))
                {
                    physicsRemainder+=dt;
                    while(physicsRemainder>=PhysicsStep)
                    {
                        foreach(var rag in pool)if(rag.Soldier>=0)
                        {
                            System.Array.Copy(rag.Positions,rag.PreviousPositions,rag.Positions.Length);
                            System.Array.Copy(rag.Rotations,rag.PreviousRotations,rag.Rotations.Length);
                        }
                        physics.Simulate(PhysicsStep);
                        foreach(var rag in pool)if(rag.Soldier>=0)CapturePose(rag,false);
                        physicsRemainder-=PhysicsStep;
                    }
                }
                foreach(var rag in pool)if(rag.Soldier>=0)
                {
                    ConstrainSurvivor(rag);
                    anchors[rag.Soldier]=rag.Bones[0].position;
                    bool asleep=true;foreach(var body in rag.Bodies)if(!body.IsSleeping()){asleep=false;break;}
                    bool grounded=true;
                    float mass=0f,linearEnergy=0f,angularEnergy=0f;
                    foreach(var body in rag.Bodies)
                    {
                        Vector3 p=body.position;
                        if(p.y-world.Terrain.HeightAt(new Vector2(p.x,p.z))>1.5f)grounded=false;
                        mass+=body.mass;
                        linearEnergy+=body.mass*body.linearVelocity.sqrMagnitude;
                        Vector3 spin=Quaternion.Inverse(body.rotation*body.inertiaTensorRotation)*body.angularVelocity;
                        angularEnergy+=Vector3.Dot(Vector3.Scale(spin,spin),body.inertiaTensor);
                    }
                    // Use whole-body kinetic energy: tiny wrist rotations should not
                    // keep an otherwise grounded, motionless corpse in the solver.
                    bool quiet=linearEnergy/Mathf.Max(.01f,mass)<.04f&&angularEnergy/Mathf.Max(.01f,mass)<.015f;
                    rag.QuietTime=grounded&&quiet?rag.QuietTime+dt:0f;
                    if(rag.Survivor!=null&&rag.Survivor.Active){StepSurvivor(rag);continue;}
                    // Allow corpses resting on other corpses, but require sustained stillness:
                    // a momentarily slow body at the apex of a throw is still airborne.
                    if((VisualTime-rag.Born>1f&&asleep)||(VisualTime-rag.Born>=SettleSeconds&&rag.QuietTime>=.4f))Freeze(rag);
                }
            }
            else physicsRemainder=0f;
            StepLooseEquipment();
            using (BattleMetrics.Measure(BattleMetrics.Stage.GoreStep)) gore?.Step(dt,blood,VisualTime);
            using (BattleMetrics.Measure(BattleMetrics.Stage.BloodStep)) blood.Step(dt,VisualTime);
        }
        static void CapturePose(Ragdoll rag,bool initialize)
        {
            for(int i=0;i<rag.Bones.Length;i++)
            {
                rag.Positions[i]=rag.Bones[i].position;rag.Rotations[i]=rag.Bones[i].rotation;
                if(initialize){rag.PreviousPositions[i]=rag.Positions[i];rag.PreviousRotations[i]=rag.Rotations[i];}
            }
        }
        static void ApplyPose(Ragdoll rag,float alpha)
        {
            for(int i=0;i<rag.Bones.Length;i++)
                rag.Bones[i].SetPositionAndRotation(Vector3.Lerp(rag.PreviousPositions[i],rag.Positions[i],alpha),Quaternion.Slerp(rag.PreviousRotations[i],rag.Rotations[i],alpha));
        }
        void Freeze(Ragdoll rag)
        {
            using var scope=BattleMetrics.Measure(BattleMetrics.Stage.CorpseFreeze);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var mesh=new Mesh{name="Persistent cannon corpse "+rag.Soldier,indexFormat=IndexFormat.UInt32};rag.Skin.BakeMesh(mesh);mesh.RecalculateBounds();
            HighestFrozenBody=Mathf.Max(HighestFrozenBody,mesh.bounds.size.y);
            frozen[rag.Soldier]=mesh;anchors[rag.Soldier]=rag.Bones[0].position;
            foreach(var body in rag.Bodies){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.isKinematic=true;}
            rag.Root.SetActive(false);rag.Soldier=-1;ActiveRagdolls--;FrozenRagdolls++;
            SnapshotMilliseconds+=timer.Elapsed.TotalMilliseconds;
        }
        public string ActivePhysicsReport()
        {
            var report=new System.Text.StringBuilder();
            foreach(var rag in pool)if(rag.Soldier>=0)
            {
                float height=0,speed=0,spin=0;
                foreach(var body in rag.Bodies){var p=body.position;height=Mathf.Max(height,p.y-world.Terrain.HeightAt(new Vector2(p.x,p.z)));speed=Mathf.Max(speed,body.linearVelocity.magnitude);spin=Mathf.Max(spin,body.angularVelocity.magnitude);}
                report.Append($" [{rag.Soldier}: height={height:F2} speed={speed:F2} spin={spin:F2} quiet={rag.QuietTime:F2}]");
            }
            return report.ToString();
        }
        public Vector3 BloodAnchor(int soldier) => anchors[soldier];
        public bool ValidateCorpses(out string issue)
        {
            issue=null;
            foreach(var mesh in frozen)if(mesh)
            {
                var b=mesh.bounds;
                if(float.IsNaN(b.center.x)||float.IsNaN(b.center.y)||float.IsNaN(b.center.z)||b.size.magnitude>8f)
                {issue="Invalid settled ragdoll bounds: "+b;return false;}
            }
            return true;
        }
        public bool IsPhysical(int soldier) => physical[soldier]&&frozen[soldier]==null;
        void LateUpdate()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Corpses);
            if(world==null||!crowd||!cameraView)return;
            if(!renderingWarmed)renderingWarmed=WarmRendering();
            GeometryUtility.CalculateFrustumPlanes(cameraView,frustum);
            // Interpolate the visible skeleton between stable solver steps at every playback speed.
            foreach(var rag in pool)if(rag.Soldier>=0)ApplyPose(rag,Mathf.Clamp01(physicsRemainder/PhysicsStep));
            gore?.Render();
            if(BattleOptimizationSettings.RenderWorkOptimizations&&world.DeathCount>0)
                foreach(var unit in world.Battalions)
                {
                    // Outfit selection scans the catalog. Share the current result
                    // across corpses, while still observing live review/loadout edits.
                    corpseSets[unit.Id]=crowd.Resolve(unit);
                    commandCorpseSets[unit.Id]=crowd.ResolveCommand(unit);
                }
            for(int i=0;i<world.DeathCount;i++)
            {
                int id=world.DeadSoldiers[i];var d=world.Deaths[id];
                if(d.Kind==UnitKind.Cavalry&&!HasPhysicalCorpse(id))
                {
                    var rider=CavalryCasualtyMotion.Sample(d,VisualTime).RiderPosition;
                    rider.y+=world.Terrain.HeightAt(new Vector2(rider.x,rider.z));anchors[id]=rider;
                    continue;
                }
                if(IsPhysical(id))continue;
                var rotation=Quaternion.Euler(0,d.Facing*Mathf.Rad2Deg,0);var matrix=Matrix4x4.TRS(d.Position + corpseOffsets[id],rotation,Vector3.one);
                if(frozen[id])
                {
                    if (corpseVelocity[id].sqrMagnitude > .001f)
                    {
                        Vector3 pivot = anchors[id];
                        matrix = Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler((VisualTime - corpseKickTime[id]) * 100f,0,0)) * Matrix4x4.Translate(-pivot) * matrix;
                    }
                    if(frozen[id].vertexCount==0)continue;
                    var bound=frozen[id].bounds;bound.center=matrix.MultiplyPoint3x4(bound.center);bound.extents=Vector3.one*bound.extents.magnitude;
                    if(GeometryUtility.TestPlanesAABB(frustum,bound))
                    {
                        Graphics.DrawMesh(frozen[id],matrix,corpseMaterials[id],0,cameraView);
                        if(frozen[id].subMeshCount>1)Graphics.DrawMesh(frozen[id],matrix,gore.WoundMaterial,0,cameraView,1);
                    }
                    continue;
                }
                Vector3 displayPosition=d.Position;
                if(d.Cannon)
                {
                    float flightAge=Mathf.Max(0,VisualTime-d.Born);
                    float airborne=Mathf.Clamp01(flightAge/Mathf.Max(.01f,flightTimes[id]));
                    displayPosition=flightAge>=flightTimes[id]?flightEnds[id]:WeaponBallistics.PositionAt(d.Position,d.Impulse,flightAge);
                    // Bounded instanced fallback makes blast throws visible from the tactical camera too.
                    rotation=InfantryRecoveryTiming.AirborneRotation(id,d.Impulse,flightAge,flightTimes[id])*rotation;
                    matrix=Matrix4x4.TRS(displayPosition,rotation,Vector3.one);
                    anchors[id]=displayPosition;
                }
                displayPosition += corpseOffsets[id];
                if (corpseVelocity[id].sqrMagnitude > .001f)
                    rotation *= Quaternion.Euler((VisualTime - corpseKickTime[id]) * 135f, 0, (VisualTime - corpseKickTime[id]) * 65f);
                anchors[id] = displayPosition;
                if(!GeometryUtility.TestPlanesAABB(frustum,new Bounds(displayPosition,Vector3.one*6)))continue;
                var unit=world.Battalions[d.UnitId];
                bool command=!world.Config.AssetReview&&(world.Soldiers.PikeSergeants[id]||world.Soldiers.Captains[id]);
                var set=BattleOptimizationSettings.RenderWorkOptimizations?(command?commandCorpseSets[unit.Id]:corpseSets[unit.Id]):command?crowd.ResolveCommand(unit):crowd.Resolve(unit);
                int level=(cameraView.transform.position-d.Position).sqrMagnitude>1600&&set.Lods.Length>1?1:0;
                var lod=set.Lods[level];var poses=lod.Death;
                int variant=InfantryDeathMotion.Variant(d,lod.DeathVariants?.Length??0,world.Config.AssetReview);
                if(variant>=0)poses=lod.DeathVariants[variant].Poses;
                if(lod.DeathVariants?.Length!=10)variant=-1;
                if(!world.Config.AssetReview&&world.Soldiers.PikeSergeants[id]&&lod.Command!=null&&lod.Command.Length>3&&lod.Command[3].PikeDeath!=null){poses=lod.Command[3].PikeDeath;variant=-1;}
                if(!world.Config.AssetReview&&world.Soldiers.Captains[id]&&lod.Command!=null&&lod.Command.Length>0&&lod.Command[0].OfficerDeath!=null){poses=lod.Command[0].OfficerDeath;variant=-1;}
                if(poses==null||poses.Length==0)continue;
                float age=VisualTime-d.Born;
                float duration=InfantryDeathMotion.Duration(d,variant,world.Config.AssetReview);
                float resting=d.Cannon?Mathf.Clamp01((age-flightTimes[id])/.25f):Mathf.Clamp01(age/duration);
                rotation=restingPoses[id].Sample(world.Terrain,displayPosition,rotation,resting);
                matrix=Matrix4x4.TRS(displayPosition,rotation,Vector3.one);
                float frame=Mathf.Clamp01(age/duration)*(poses.Length-1);
                int poseIndex=Mathf.Clamp(Mathf.FloorToInt(frame),0,poses.Length-1);
                var mesh=poses[poseIndex];
                if(!batches.TryGetValue(mesh,out var batch)){batch=new Batch{Material=set.Material};batches.Add(mesh,batch);}
                batch.Blends[batch.Count]=frame-poseIndex;
                batch.Matrices[batch.Count++]=matrix;if(batch.Count==256)Flush(mesh,batch);
            }
            RenderLooseEquipment();
            foreach(var pair in batches)Flush(pair.Key,pair.Value);
        }
        public static Vector3 CannonFlightPosition(SoldierDeath death,BattlefieldTerrain terrain,float age,out float landingDuration)
        {
            Vector3 previous=death.Position;
            const float step=.05f;
            float landing=4f;
            Vector3 end=previous;
            for(int i=1;i<=80;i++)
            {
                float t=i*step;
                Vector3 next=WeaponBallistics.PositionAt(death.Position,death.Impulse,t);
                float ground=terrain.HeightAt(new Vector2(next.x,next.z));
                if(next.y<=ground)
                {
                    float clearance=previous.y-terrain.HeightAt(new Vector2(previous.x,previous.z));
                    float fraction=Mathf.Clamp01(clearance/Mathf.Max(.0001f,clearance+ground-next.y));
                    landing=(i-1+fraction)*step;end=Vector3.Lerp(previous,next,fraction);
                    end.y=terrain.HeightAt(new Vector2(end.x,end.z));break;
                }
                previous=next;end=next;
            }
            landingDuration=landing;
            return age>=landing?end:WeaponBallistics.PositionAt(death.Position,death.Impulse,age);
        }

        bool WarmRendering()
        {
            if(pool.Count<2)return true;
            var cameraObject=new GameObject("Casualty rendering warmup");
            var warm=cameraObject.AddComponent<Camera>();warm.CopyFrom(cameraView);warm.enabled=false;
            warm.cullingMask=1<<30;warm.nearClipPlane=.1f;warm.farClipPlane=20;
            warm.transform.position=new Vector3(0,2,-5);warm.transform.LookAt(Vector3.up);
            var target=RenderTexture.GetTemporary(16,16,24,RenderTextureFormat.Default);
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};
            bool firstActive=pool[0].Root.activeSelf,secondActive=pool[1].Root.activeSelf;
            try
            {
                if(!RenderPipeline.SupportsRenderRequest(warm,request))return false;
                pool[0].Root.SetActive(true);pool[1].Root.SetActive(true);
                blood.WarmRendering(warm);
                var warmTimer=System.Diagnostics.Stopwatch.StartNew();
                RenderPipeline.SubmitRenderRequest(warm,request);
                Debug.Log("CASUALTY_RENDER_WARMUP_MS "+warmTimer.Elapsed.TotalMilliseconds);
                return true;
            }
            finally
            {
                pool[0].Root.SetActive(firstActive);pool[1].Root.SetActive(secondActive);
                RenderTexture.ReleaseTemporary(target);Destroy(cameraObject);
            }
        }
        void Flush(Mesh mesh,Batch batch)
        {
            if(batch.Count==0)return;
            batch.Properties.SetFloatArray("_PoseBlend",batch.Blends);
            Graphics.DrawMeshInstanced(mesh,0,batch.Material,batch.Matrices,batch.Count,batch.Properties,ShadowCastingMode.On,true,0,cameraView,LightProbeUsage.Off);batch.Count=0;
        }
        void OnDestroy()
        {
            DisposeSurvivors();
            if(world!=null)world.SoldierDied-=OnDeath;
            if(frozen!=null)foreach(var mesh in frozen)if(mesh){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
            gore?.Dispose();
            DisposeLooseEquipment();
            equipment.Dispose();
            appearances.Dispose();
            ground?.Dispose();
            if(physicsScene.IsValid())SceneManager.UnloadSceneAsync(physicsScene);
        }
    }
}
