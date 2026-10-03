using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmpireBattle
{
    public sealed partial class BattleCasualtyView
    {
        public const int MaxLooseEquipment=MaxRagdolls*4;
        readonly RagdollEquipment equipment=new RagdollEquipment();
        sealed class LooseEquipment
        {
            public GameObject Root;
            public Rigidbody Body;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public BoxCollider Collider;
            public int Soldier;
            public float Born;
            public bool Active;
            public Collider[] Owners;
            public StandardCloth[] Flags;
            public Matrix4x4 FlagLocal;
        }
        struct RestingEquipment {public Mesh Mesh;public Material Material;public Matrix4x4 Matrix;public int Soldier;public StandardCloth[] Flags;public Matrix4x4 FlagLocal;}
        readonly List<LooseEquipment> looseEquipment=new List<LooseEquipment>();
        readonly List<RestingEquipment> restingEquipment=new List<RestingEquipment>();
        readonly List<StandardCloth[]> droppedStandards=new List<StandardCloth[]>();
        public int ActiveLooseEquipment {get;private set;}
        public int RestingLooseEquipment=>restingEquipment.Count;

        void SpawnLooseEquipment(Ragdoll rag,SoldierDeath death)
        {
            if(rag.Set.DetachedParts==null)return;
            foreach(var part in rag.Set.DetachedParts)
            {
                LooseEquipment item=null;
                foreach(var candidate in looseEquipment)if(!candidate.Active){item=candidate;break;}
                if(item==null&&looseEquipment.Count==MaxLooseEquipment)
                {
                    // Keep the solver bounded even if corpses free slots before their gear.
                    item=looseEquipment[0];foreach(var candidate in looseEquipment)if(candidate.Born<item.Born)item=candidate;
                    FreezeLooseEquipment(item,true);
                }
                if(item==null)
                {
                    item=new LooseEquipment{Root=new GameObject("Detached casualty equipment"){layer=30}};
                    SceneManager.MoveGameObjectToScene(item.Root,physicsScene);
                    item.Filter=item.Root.AddComponent<MeshFilter>();item.Renderer=item.Root.AddComponent<MeshRenderer>();
                    item.Collider=item.Root.AddComponent<BoxCollider>();item.Collider.contactOffset=.002f;
                    item.Body=item.Root.AddComponent<Rigidbody>();item.Body.interpolation=RigidbodyInterpolation.Interpolate;
                    item.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                    looseEquipment.Add(item);
                }
                item.Root.SetActive(false);item.Body.isKinematic=true;
                item.Filter.sharedMesh=part.Mesh;item.Renderer.sharedMaterial=rag.Set.Material;
                item.Flags=null;item.FlagLocal=Matrix4x4.identity;
                var bounds=part.Mesh.bounds;
                if(part.Kind==DetachedEquipmentKind.Standard)
                {
                    int member=CommandPartyLayout.StandardMember(world,world.Battalions[death.UnitId],death.Soldier);
                    if(member>=0)
                    {
                        item.Flags=StandardCloth.ForBattalion(world,world.Battalions[death.UnitId],member);
                        droppedStandards.Add(item.Flags);
                        item.FlagLocal=Matrix4x4.TRS(rag.Set.Positions[part.Bone],rag.Set.Rotations[part.Bone],Vector3.one).inverse;
                        foreach(var flag in item.Flags)foreach(var point in flag.Positions)bounds.Encapsulate(item.FlagLocal.MultiplyPoint3x4(point));
                    }
                }
                item.Collider.center=bounds.center;
                item.Collider.size=Vector3.Max(bounds.size,Vector3.one*.025f);
                var bone=rag.Bones[part.Bone];item.Root.transform.SetPositionAndRotation(bone.position,bone.rotation);
                item.Soldier=death.Soldier;item.Born=VisualTime;item.Active=true;
                item.Body.mass=part.Hat?.18f:part.Kind==DetachedEquipmentKind.Backpack?6f:3.5f;
                item.Root.SetActive(true);item.Body.isKinematic=false;
                if(item.Owners!=null)foreach(var collider in item.Owners)if(collider)Physics.IgnoreCollision(item.Collider,collider,false);
                // No initial collision with the owner: equipment separates cleanly from hands/head.
                item.Owners=rag.Root.GetComponentsInChildren<Collider>();
                foreach(var collider in item.Owners)Physics.IgnoreCollision(item.Collider,collider);
                var facing=Quaternion.Euler(0,death.Facing*Mathf.Rad2Deg,0);
                float side=RifleFireTiming.Variation(death.Soldier,part.Hat?421:419)<.5f?-1:1;
                Vector3 inherited=Vector3.ClampMagnitude(death.Velocity,6f)+Vector3.ClampMagnitude(death.Impulse,death.Cannon?12f:2f)*.6f;
                item.Body.linearVelocity=inherited+facing*new Vector3(side*(part.Hat?1.8f:1.0f),part.Hat?2.7f:1.2f,.5f);
                item.Body.angularVelocity=facing*new Vector3(3f,side*4f,part.Hat?5f:2.5f);
                item.Body.WakeUp();ActiveLooseEquipment++;
            }
        }
        void StepLooseEquipment()
        {
            foreach(var item in looseEquipment)if(item.Active)
            {
                float age=VisualTime-item.Born;
                bool quiet=item.Body.IsSleeping()||(item.Body.linearVelocity.sqrMagnitude<.02f&&item.Body.angularVelocity.sqrMagnitude<.04f);
                var bounds=item.Collider.bounds;
                bool grounded=bounds.min.y<=world.Terrain.HeightAt(new Vector2(bounds.center.x,bounds.center.z))+.15f;
                if(age>1f&&quiet&&grounded)FreezeLooseEquipment(item,false);
                else if(age>8f)FreezeLooseEquipment(item,true);
            }
        }
        void FreezeLooseEquipment(LooseEquipment item,bool groundIt)
        {
            if(groundIt)
            {
                var bounds=item.Collider.bounds;var position=item.Root.transform.position;
                position.y+=world.Terrain.HeightAt(new Vector2(bounds.center.x,bounds.center.z))-bounds.min.y+.005f;
                item.Root.transform.position=position;
            }
            restingEquipment.Add(new RestingEquipment{Mesh=item.Filter.sharedMesh,Material=item.Renderer.sharedMaterial,
                Matrix=item.Root.transform.localToWorldMatrix,Soldier=item.Soldier,Flags=item.Flags,FlagLocal=item.FlagLocal});
            item.Body.linearVelocity=item.Body.angularVelocity=Vector3.zero;item.Body.isKinematic=true;
            item.Root.SetActive(false);item.Active=false;ActiveLooseEquipment--;
        }
        void RenderLooseEquipment()
        {
            foreach(var active in looseEquipment)if(active.Active&&active.Flags!=null)
                foreach(var flag in active.Flags)
                {
                    flag.Advance(VisualTime,active.Root.transform.rotation,false);
                    flag.Draw(active.Root.transform.localToWorldMatrix*active.FlagLocal,cameraView);
                }
            foreach(var item in restingEquipment)
            {
                var bound=item.Mesh.bounds;bound.center=item.Matrix.MultiplyPoint3x4(bound.center);bound.extents=Vector3.one*bound.extents.magnitude;
                if(!GeometryUtility.TestPlanesAABB(frustum,bound))continue;
                if(!batches.TryGetValue(item.Mesh,out var batch)){batch=new Batch{Material=item.Material};batches.Add(item.Mesh,batch);}
                batch.Blends[batch.Count]=0;batch.Matrices[batch.Count++]=item.Matrix;
                if(batch.Count==256)Flush(item.Mesh,batch);
                if(item.Flags!=null)foreach(var flag in item.Flags)flag.Draw(item.Matrix*item.FlagLocal,cameraView);
            }
        }
        void ClearLooseEquipment(int unitId)
        {
            foreach(var item in looseEquipment)if(item.Active&&world.Soldiers.BattalionIds[item.Soldier]==unitId)
            {
                item.Body.linearVelocity=item.Body.angularVelocity=Vector3.zero;item.Body.isKinematic=true;
                item.Root.SetActive(false);item.Active=false;ActiveLooseEquipment--;
            }
            restingEquipment.RemoveAll(item=>world.Soldiers.BattalionIds[item.Soldier]==unitId);
        }
        void DisposeLooseEquipment()
        {
            foreach(var flags in droppedStandards)foreach(var flag in flags)flag.Dispose();droppedStandards.Clear();
        }
    }
}
