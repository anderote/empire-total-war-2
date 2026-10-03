using System;
using System.Collections.Generic;
using UnityEngine;
namespace EmpireBattle.Art
{
    // Offline arm/socket handling, sampled into the same GPU pose meshes as infantry.
    public sealed class BayonetOrderAuthoring
    {
        readonly GameObject rig;
        readonly Transform root,gun;
        readonly Dictionary<string,Transform> bones=new Dictionary<string,Transform>();
        readonly AnatomicalArmIK arms;
        readonly bool kneeling;
        readonly Vector3 muzzle;
        public float ContactError {get;private set;}
        public BayonetOrderAuthoring(GameObject value,Transform weapon,bool knee)
        {
            rig=value;root=value.transform;gun=weapon;kneeling=knee;arms=new AnatomicalArmIK(value,knee?.43f:0);
            foreach(var bone in value.GetComponentsInChildren<Transform>())if(!bones.ContainsKey(bone.name))bones[bone.name]=bone;
            var mesh=gun.GetComponent<MeshFilter>().sharedMesh;
            var sum=Vector3.zero;int count=0;foreach(var vertex in mesh.vertices)if(vertex.z>mesh.bounds.max.z-.35f){sum+=vertex;count++;}
            muzzle=count>0?new Vector3(sum.x/count,sum.y/count,mesh.bounds.max.z):new Vector3(0,0,mesh.bounds.max.z);
        }
        static float Ease(float a,float b,float phase)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,phase));
        public Vector3 BladeSocket {get;private set;}
        public Quaternion BladeRotation {get;private set;}
        public Vector3 BeltSocket {get;private set;}
        public Quaternion BeltRotation {get;private set;}
        public void Sample(float phase)
        {
            float drop=kneeling?.43f:0;
            BeltSocket=root.TransformPoint(new Vector3(-.34f,.92f-drop,-.30f));
            BeltRotation=Quaternion.LookRotation(-root.up,-root.forward);
            float hold=Ease(0,.22f,phase)*(1-Ease(.78f,1,phase));
            Vector3 original=gun.TransformPoint(muzzle);
            var turn=Quaternion.LookRotation(root.TransformDirection(new Vector3(0,.86f,-.5f)),root.up);
            gun.rotation=Quaternion.Slerp(gun.rotation,turn,hold);
            Vector3 socket=root.TransformPoint(new Vector3(.08f,1.48f-drop,-.38f));
            gun.position+=Vector3.Lerp(original,socket,hold)-gun.TransformPoint(muzzle);
            // Keep the stock clear of the torso while the rifle is raised. The
            // source rig faces -Z; this becomes forward after the bake's 180 turn.
            // Keep a modest clearance so the arm IK can still reach the barrel.
            gun.position+=root.TransformDirection(Vector3.back)*.08f;
            var mounted=gun.TransformPoint(muzzle)+gun.right*.03f;
            // Withdraw along the sheath, turn the free blade, then slide its socket
            // onto the barrel. The same path in reverse removes and sheathes it.
            float carry=Ease(.32f,.56f,phase);
            var free=BeltSocket+root.up*(.46f*Ease(.15f,.32f,phase));
            var heldRotation=Quaternion.Slerp(BeltRotation,gun.rotation,carry);
            var heldSocket=Vector3.Lerp(free,mounted+gun.forward*.075f,carry);
            heldSocket=Vector3.Lerp(heldSocket,mounted,Ease(.56f,.64f,phase));
            float grasp=Ease(0,.15f,phase)*(1-Ease(.73f,1,phase));
            Vector3 direction=heldRotation*Vector3.forward,normal=heldRotation*Vector3.up;
            normal=Quaternion.AngleAxis(Mathf.Sin(Mathf.InverseLerp(.64f,.73f,phase)*Mathf.PI)*24,direction)*normal;
            Vector3 wrist=heldSocket-direction*.085f-normal*.025f;
            Grasp("l",gun.TransformPoint(muzzle)-gun.forward*.22f-gun.right*.028f,gun.forward,gun.right,hold);
            Grasp("r",wrist,direction,normal,grasp);
            var hand=bones["Closed right hand"];
            var palm=hand.position+hand.forward*.05f+hand.up*.025f;
            BladeSocket=phase<=.15f?BeltSocket:phase>=.64f?mounted:palm;
            BladeRotation=phase<=.15f?BeltRotation:phase>=.64f?gun.rotation:hand.rotation;
            ContactError=phase>.15f&&phase<.64f?Vector3.Distance(palm,heldSocket):0;
        }
        void Grasp(string side,Vector3 target,Vector3 direction,Vector3 normal,float amount)
        {
            if(amount<=.00001f)return;
            var hand=bones[side+"_hand"];var original=hand.position;
            var mesh=bones[side=="l"?"Closed left hand":"Closed right hand"];
            direction=Vector3.Slerp(mesh.forward,direction,amount).normalized;
            normal=Vector3.Slerp(mesh.up,normal,amount).normalized;
            arms.Solve(side,Vector3.Lerp(original,target,amount),direction);
            mesh.position=hand.position+direction*.035f;
            mesh.rotation=Quaternion.LookRotation(direction,normal);
            mesh.localScale=Vector3.one/Mathf.Max(.00001f,hand.lossyScale.x);
        }
    }
}
