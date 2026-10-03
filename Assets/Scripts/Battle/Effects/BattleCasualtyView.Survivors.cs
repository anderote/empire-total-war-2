using System;
using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleCasualtyView
    {
        public const int MaxSurvivorRagdolls=12;
        public const float SurvivorRagdollDistance=120f, SurvivorMinimumImpulse=2f;
        const float RecoveryBlendSeconds=.4f;
        sealed class SurvivorMotion
        {
            public bool Active,Frozen;
            public InfantryCrowdLod Lod;
            public GameObject BlendObject;
            public Mesh BlendMesh;
            public MeshRenderer Renderer;
            public Vector3[] From,FromNormals,To,ToNormals,Vertices,Normals;
            public float BlendStart,BlendEnd;
        }
        Ragdoll[] survivorRigs;
        readonly Dictionary<InfantryCrowdSet,InfantryRagdollSet> survivorSets=new Dictionary<InfantryCrowdSet,InfantryRagdollSet>();
        public int ActiveSurvivorRagdolls { get; private set; }
        public int SurvivorRecoveries { get; private set; }
        public bool HasSurvivorVisual(int soldier)=>survivorRigs!=null&&soldier>=0&&soldier<survivorRigs.Length&&survivorRigs[soldier]!=null;

        void InitializeSurvivors()
        {
            survivorRigs=new Ragdoll[world.Soldiers.Alive.Length];
            if(!rigs||!crowd||!world.Config.UseDetailedInfantry)return;
            foreach(var unit in world.Battalions)
            {
                if(unit.Kind!=UnitKind.Infantry)continue;
                var appearance=crowd.Resolve(unit);
                if(survivorSets.ContainsKey(appearance))continue;
                var lod=appearance.Lods[0];
                if(lod.RecoveryVariants==null||lod.RecoveryVariants.Length<2)continue;
                // Keep the complete outfit and its exact crowd vertex ordering. Survivors
                // do not shed equipment, dismember, or create corpse records.
                var set=appearances.Resolve(appearance,unit.Side==ArmySide.French?rigs.French:rigs.British);
                if(set!=null)survivorSets.Add(appearance,set);
            }
            world.SoldierKnockedDown+=OnKnockdown;
        }
        void OnKnockdown(int soldier)
        {
            var s=world.Soldiers;var unit=world.Battalions[s.BattalionIds[soldier]];
            if(ActiveSurvivorRagdolls>=MaxSurvivorRagdolls||ActiveRagdolls>=MaxRagdolls||HasSurvivorVisual(soldier)
                ||unit.Kind!=UnitKind.Infantry||s.Captains[soldier]||s.PikeSergeants[soldier]
                ||CommandPartyLayout.StandardMember(world,unit,soldier)>=0
                ||s.KnockdownImpulses[soldier].sqrMagnitude<SurvivorMinimumImpulse*SurvivorMinimumImpulse
                ||(cameraView.transform.position-s.KnockdownOrigins[soldier]).sqrMagnitude>SurvivorRagdollDistance*SurvivorRagdollDistance)return;
            var appearance=crowd.Resolve(unit);
            if(!survivorSets.TryGetValue(appearance,out var set))return;
            var lod=appearance.Lods[0];
            foreach(var pair in lod.RecoveryVariants)
                if(pair.GetUp==null||pair.GetUp.Length<2||pair.GetUp[0].vertexCount!=set.Mesh.vertexCount)return;
            Ragdoll rag=null;
            foreach(var candidate in pool)if(candidate.Soldier<0&&candidate.Set==set){rag=candidate;break;}
            if(rag==null)foreach(var candidate in pool)if(candidate.Soldier<0){rag=candidate;break;}
            if(rag==null&&pool.Count<MaxRagdolls){rag=CreateRig(set);pool.Add(rag);}
            if(rag==null)return;
            rag.Root.SetActive(false);
            foreach(var prop in rag.Props){prop.SetActive(false);Destroy(prop);}rag.Props.Clear();
            rag.Set=set;rag.Skin.enabled=true;rag.Skin.sharedMesh=set.Mesh;rag.Skin.sharedMaterials=new[]{set.Material};
            rag.Root.transform.SetPositionAndRotation(s.KnockdownOrigins[soldier],Quaternion.Euler(0,s.KnockdownFacings[soldier]*Mathf.Rad2Deg,0));
            var source=new SoldierDeath { Soldier=soldier,UnitId=unit.Id,Kind=unit.Kind,Born=world.Time,
                GaitPhase=s.GaitPhases[soldier],Moving=s.MovementSpeeds[soldier]>.04f,Running=unit.Running,
                LastFireTime=s.FireTimes[soldier],ReloadRemaining=Mathf.Max(0,s.ReloadReadyTimes[soldier]-world.Time) };
            var pose=StartingPose(set,source);
            for(int i=0;i<rag.Bones.Length;i++)rag.Bones[i].SetPositionAndRotation(rag.Root.transform.TransformPoint(pose.Positions[i]),rag.Root.transform.rotation*pose.Rotations[i]);
            rag.Soldier=soldier;rag.Born=VisualTime;rag.QuietTime=0;
            if(rag.Survivor==null)rag.Survivor=new SurvivorMotion();
            var motion=rag.Survivor;motion.Active=true;motion.Frozen=false;motion.Lod=lod;
            if(motion.BlendObject)motion.BlendObject.SetActive(false);
            survivorRigs[soldier]=rag;ActiveSurvivorRagdolls++;ActiveRagdolls++;
            rag.Root.SetActive(true);
            Vector3 impulse=s.KnockdownImpulses[soldier];
            Vector3 tumble=Vector3.Cross(Vector3.up,impulse.normalized)*Mathf.Lerp(3f,5.5f,RifleFireTiming.Variation(soldier,443));
            Vector3 spin=tumble+Vector3.up*Mathf.Lerp(-1.2f,1.2f,RifleFireTiming.Variation(soldier,449));
            Vector3 pivot=rag.Bones[0].position+Vector3.up*.15f;
            foreach(var body in rag.Bodies)
            {
                body.isKinematic=false;body.linearVelocity=impulse+Vector3.Cross(spin,body.worldCenterOfMass-pivot);
                body.angularVelocity=spin;
                body.WakeUp();
            }
            CapturePose(rag,true);
        }
        void ConstrainSurvivor(Ragdoll rag)
        {
            if(rag.Survivor==null||!rag.Survivor.Active||rag.Survivor.Frozen)return;
            // The visual skeleton follows the authoritative horizontal throw. Ragdoll
            // availability never changes hit positions, range, health, or recovery time.
            Vector3 target=world.SoldierPosition(rag.Soldier,world.Time);
            Vector3 correction=target-rag.Bones[0].position;correction.y=0;
            for(int i=0;i<rag.Bones.Length;i++)
            {
                rag.Positions[i]=rag.Bones[i].position+correction;
                rag.PreviousPositions[i]+=correction;
            }
            for(int i=0;i<rag.Bones.Length;i++)rag.Bones[i].position=rag.Positions[i];
        }
        void StepSurvivor(Ragdoll rag)
        {
            int id=rag.Soldier;var motion=rag.Survivor;
            if(!world.Soldiers.Alive[id]||!world.Soldiers.IsKnockedDown(id,world.Time)){ReleaseSurvivor(rag);return;}
            float riseStart=world.Soldiers.KnockdownEnds[id]-InfantryRecoveryTiming.RiseDuration(id);
            if(!motion.Frozen&&(rag.QuietTime>=.15f||world.Time>=riseStart-RecoveryBlendSeconds))BeginSurvivorRecovery(rag,riseStart);
            if(!motion.Frozen||world.Time<motion.BlendStart)return;
            float phase=Mathf.SmoothStep(0,1,Mathf.InverseLerp(motion.BlendStart,motion.BlendEnd,world.Time));
            for(int i=0;i<motion.Vertices.Length;i++)
            {
                motion.Vertices[i]=Vector3.Lerp(motion.From[i],motion.To[i],phase);
                motion.Normals[i]=Vector3.Lerp(motion.FromNormals[i],motion.ToNormals[i],phase).normalized;
            }
            motion.BlendMesh.vertices=motion.Vertices;motion.BlendMesh.normals=motion.Normals;motion.BlendMesh.RecalculateBounds();
            if(world.Time>=motion.BlendEnd){SurvivorRecoveries++;ReleaseSurvivor(rag);}
        }
        void BeginSurvivorRecovery(Ragdoll rag,float riseStart)
        {
            int id=rag.Soldier;var motion=rag.Survivor;var set=rag.Set;
            int torso=Array.IndexOf(set.Names,"spine2"),head=Array.IndexOf(set.Names,"head");
            Vector3 front=rag.Bones[torso].rotation*(Quaternion.Inverse(set.Rotations[torso])*Vector3.forward);
            int parity=Vector3.Dot(front,Vector3.up)>0?0:1;
            int choices=(motion.Lod.RecoveryVariants.Length+1-parity)/2;
            int variant=parity+2*Mathf.Min(choices-1,(int)(RifleFireTiming.Variation(id,137)*choices));
            Vector3 along=rag.Bones[head].position-rag.Bones[0].position;along.y=0;
            if(parity==0)along=-along;
            float heading=along.sqrMagnitude>.001f?FormationMath.FacingOf(new Vector2(along.x,along.z)):world.Soldiers.KnockdownFacings[id];
            world.Soldiers.KnockdownRecoveryVariants[id]=variant;world.Soldiers.KnockdownRecoveryFacings[id]=heading;
            var target=motion.Lod.RecoveryVariants[variant].GetUp[0];
            if(!motion.BlendObject)
            {
                motion.BlendObject=new GameObject("Survivor recovery blend"){layer=30};motion.BlendObject.transform.SetParent(rag.Root.transform,false);
                motion.BlendObject.AddComponent<MeshFilter>();motion.Renderer=motion.BlendObject.AddComponent<MeshRenderer>();
            }
            if(motion.BlendMesh)Destroy(motion.BlendMesh);
            motion.BlendMesh=new Mesh{name="Temporary survivor recovery"};motion.BlendMesh.MarkDynamic();
            rag.Skin.BakeMesh(motion.BlendMesh);
            motion.From=motion.BlendMesh.vertices;motion.FromNormals=motion.BlendMesh.normals;
            Vector3 landed=world.SoldierPosition(id,world.Time);
            var rotation=InfantryGroundContact.Rotation(world.Terrain,landed,Quaternion.Euler(0,heading*Mathf.Rad2Deg,0),1f);
            var targetMatrix=Matrix4x4.TRS(landed,rotation,Vector3.one);
            var conversion=targetMatrix.inverse*rag.Root.transform.localToWorldMatrix;
            for(int i=0;i<motion.From.Length;i++)
            {motion.From[i]=conversion.MultiplyPoint3x4(motion.From[i]);motion.FromNormals[i]=conversion.MultiplyVector(motion.FromNormals[i]).normalized;}
            motion.To=target.vertices;motion.ToNormals=target.normals;
            motion.Vertices=new Vector3[motion.From.Length];motion.Normals=new Vector3[motion.From.Length];
            motion.BlendMesh.vertices=motion.From;motion.BlendMesh.normals=motion.FromNormals;motion.BlendMesh.RecalculateBounds();
            foreach(var body in rag.Bodies){body.linearVelocity=body.angularVelocity=Vector3.zero;body.isKinematic=true;}
            rag.Skin.enabled=false;rag.Root.transform.SetPositionAndRotation(landed,rotation);
            ApplyPose(rag,1f); // Retain the settled skeleton if a follow-up hit kills him during the blend.
            motion.BlendObject.GetComponent<MeshFilter>().sharedMesh=motion.BlendMesh;
            motion.Renderer.sharedMaterial=set.Material;motion.BlendObject.SetActive(true);
            motion.Frozen=true;motion.BlendStart=Mathf.Max(world.Time,riseStart-RecoveryBlendSeconds);motion.BlendEnd=riseStart;
            CapturePose(rag,true);
        }
        void ReleaseSurvivor(Ragdoll rag)
        {
            int id=rag.Soldier;var motion=rag.Survivor;
            if(motion==null||!motion.Active)return;
            motion.Active=false;survivorRigs[id]=null;
            foreach(var body in rag.Bodies){if(!body.isKinematic)body.linearVelocity=body.angularVelocity=Vector3.zero;body.isKinematic=true;}
            if(motion.BlendObject)motion.BlendObject.SetActive(false);
            if(motion.BlendMesh){Destroy(motion.BlendMesh);motion.BlendMesh=null;}
            motion.From=motion.FromNormals=motion.To=motion.ToNormals=motion.Vertices=motion.Normals=null;
            rag.Skin.enabled=true;rag.Root.SetActive(false);rag.Soldier=-1;
            ActiveRagdolls--;ActiveSurvivorRagdolls--;
        }
        bool ConvertSurvivorDeath(SoldierDeath death)
        {
            if(!HasSurvivorVisual(death.Soldier))return false;
            var rag=survivorRigs[death.Soldier];var motion=rag.Survivor;
            // A second hit can kill a tumbling survivor without restarting his flight.
            motion.Active=false;survivorRigs[death.Soldier]=null;ActiveSurvivorRagdolls--;
            if(motion.BlendObject)motion.BlendObject.SetActive(false);
            if(motion.BlendMesh){Destroy(motion.BlendMesh);motion.BlendMesh=null;}
            motion.From=motion.FromNormals=motion.To=motion.ToNormals=motion.Vertices=motion.Normals=null;
            rag.Skin.enabled=true;rag.Set=equipment.Resolve(rag.Set);rag.Skin.sharedMesh=rag.Set.Mesh;
            corpseMaterials[death.Soldier]=rag.Set.Material;physical[death.Soldier]=true;rag.Born=VisualTime;rag.QuietTime=0;
            SpawnLooseEquipment(rag,death);
            foreach(var body in rag.Bodies){body.isKinematic=false;body.AddForce(death.Impulse*.5f,ForceMode.VelocityChange);body.WakeUp();}
            CapturePose(rag,true);return true;
        }
        void ClearSurvivors(int unit)
        {
            foreach(var rag in pool)if(rag.Survivor!=null&&rag.Survivor.Active&&world.Soldiers.BattalionIds[rag.Soldier]==unit)ReleaseSurvivor(rag);
        }
        void DisposeSurvivors()
        {
            if(world!=null)world.SoldierKnockedDown-=OnKnockdown;
            foreach(var rag in pool)if(rag.Survivor!=null&&rag.Survivor.BlendMesh)
            {if(Application.isPlaying)Destroy(rag.Survivor.BlendMesh);else DestroyImmediate(rag.Survivor.BlendMesh);}
            survivorSets.Clear();
        }
    }
}
