using System;
using System.Collections.Generic;
using UnityEngine;
namespace EmpireBattle.Art
{
    // The authored closed hand has fingers along local +Z and palm cavity along +Y.
    // Grip orientation is derived from the cylinder axis and the forearm, not an FBX wrist guess.
    public sealed partial class ClosedGripAuthoring
    {
        readonly Transform root, rifle, rod, leftMesh, rightMesh;
        readonly AnatomicalArmIK armIK; readonly Vector3 muzzleLocal;
        readonly float postureDrop;
        public float MaxContactError { get; private set; }
        public float MaxWristAngle { get; private set; }
        Vector3 lastCorrection;
        bool firingPose;
        readonly Dictionary<string,Transform> bones=new Dictionary<string,Transform>();
        public ClosedGripAuthoring(GameObject soldier,Transform weapon,Transform ramrod,float chestDrop=0)
        {
            postureDrop=chestDrop;root=soldier.transform;rifle=weapon;rod=ramrod;armIK=new AnatomicalArmIK(soldier,chestDrop);var mesh=rifle.GetComponent<MeshFilter>().sharedMesh;Vector3 sum=Vector3.zero;int n=0;foreach(var vertex in mesh.vertices)if(vertex.z>mesh.bounds.max.z-.35f){sum+=vertex;n++;}muzzleLocal=n>0?new Vector3(sum.x/n,sum.y/n,mesh.bounds.max.z):new Vector3(0,0,mesh.bounds.max.z);
            foreach(var t in soldier.GetComponentsInChildren<Transform>())if(!bones.ContainsKey(t.name))bones[t.name]=t;
            leftMesh=bones["Closed left hand"];rightMesh=bones["Closed right hand"];
        }
        public void Apply(bool fire,bool reload,float phase,bool relaxed=false)
        {
            MaxContactError=0;MaxWristAngle=0;
            firingPose=fire&&!reload&&!relaxed;
            if(relaxed){Relax("l");Relax("r");return;}
            if(!reload)
            {
                // Anchor the stock at the sampled trigger hand rather than an inherited FBX socket.
                if(fire)rifle.rotation=Quaternion.LookRotation(-root.forward,root.up);
                Vector3 forearm=bones["r_hand"].position-bones["r_foretwist"].position;
                Vector3 fingers=Vector3.ProjectOnPlane(forearm,-rifle.right).normalized;
                Vector3 stock=bones["r_hand"].position+fingers*.075f-rifle.right*.028f;if(fire){var local=root.InverseTransformPoint(stock);local.x=Mathf.Clamp(local.x,.04f,.1f);stock=root.TransformPoint(local);}
                rifle.position=stock-rifle.TransformVector(new Vector3(0,0,-25));if(!fire)rifle.position+=root.up*.18f;else rifle.position+=root.up*Mathf.Max(0,1.2f-postureDrop-root.InverseTransformPoint(stock).y);
            }
            if(reload)
            {
                // Left hand supports the outside of the planted barrel. Right action already has IK targets.
                var leftHand=bones["l_hand"];var leftForearm=leftHand.position-bones["l_foretwist"].position;
                float plant=Mathf.SmoothStep(0,1,Mathf.Clamp01(phase/.15f))*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.88f)/.12f)));
                Vector3 leftNormal=Vector3.Slerp(rifle.up,rifle.right,plant);Vector3 left=leftHand.position+Vector3.ProjectOnPlane(leftForearm,leftNormal).normalized*.075f;
                float leftGrip=Ease((phase-.08f)/.1f)*(1-Ease((phase-.86f)/.1f));
                Vector3 muzzle=rifle.TransformPoint(muzzleLocal);Vector3 barrel=muzzle+rifle.forward*Vector3.Dot(leftHand.position-muzzle,rifle.forward);
                left=Vector3.Lerp(left,barrel-leftNormal*.03f,leftGrip);Grasp("l",left,leftNormal,rifle.forward,true,leftGrip>0,leftGrip);
                var right=bones["r_hand"].position;Vector3 rightForearm=right-bones["r_foretwist"].position;Vector3 rightNormal=root.right;
                Vector3 rightPalm=right+Vector3.ProjectOnPlane(rightForearm,rightNormal).normalized*.075f;
                float rodGrip=Ease((phase-.30f)/.08f)*(1-Ease((phase-.80f)/.1f));
                var rodCentre=rod.position+rod.up*Vector3.Dot(right-rod.position,rod.up);
                rightPalm=Vector3.Lerp(rightPalm,rodCentre-rightNormal*.03f,rodGrip);Grasp("r",rightPalm,rightNormal,rod.up,true,rodGrip>0,rodGrip);

            }
            else
            {
                // Translate the rigid weapon into the intersection of both arm reach volumes.
                // Never stretch a limb or leave a disconnected mitten to preserve an FBX socket.
                for(int pass=0;pass<16;pass++)
                {
                    MaxContactError=0;MaxWristAngle=0;
                    Vector3 stock=rifle.TransformPoint(new Vector3(0,0,-25));
                    Vector3 underside=rifle.TransformPoint(new Vector3(0,1.8f,-18));
                    Grasp("r",stock+rifle.right*.028f,-rifle.right,rifle.forward);Vector3 rightCorrection=lastCorrection;
                    Grasp("l",underside-rifle.up*.022f,rifle.up,rifle.forward);Vector3 leftCorrection=lastCorrection;
                    if(MaxContactError<.003f)break;
                    if(pass<15)rifle.position+=(rightCorrection+leftCorrection)*.6f;
                }
            }
        }
        // Source rig faces -Z: positive Z tips the muzzle behind the shoulder.
        public void ApplyLocomotion(EmpireBattle.AnimationAuthoring.InfantryMotionKind kind,float phase,int variant)
        {
            bool standing=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Idle||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.UnderFire;
            bool upright=standing||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.March||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Walk;
            if(!upright){
                rifle.rotation=root.rotation*Quaternion.LookRotation(new Vector3(.12f,.45f,-1f),Vector3.up);
                Apply(false,false,phase);return;
            }
            firingPose=false;MaxContactError=0;MaxWristAngle=0;
            float wave=Mathf.Sin(phase*Mathf.PI*2f);
            var mesh=rifle.GetComponent<MeshFilter>().sharedMesh;
            Vector3 butt=new Vector3(0,0,mesh.bounds.min.z);
            // March variants use two hands; idle alternates shoulder rest and grounded order arms.
            bool grounded=standing&&variant==2;
            bool twoHands=!standing||variant==1;
            Vector3 axis=grounded?new Vector3(.025f,1f,.06f):new Vector3(twoHands?-.20f:.025f,1f,standing?.30f:.12f);
            rifle.rotation=root.rotation*Quaternion.LookRotation(axis,Vector3.back);
            Vector3 anchor=root.TransformPoint(new Vector3(twoHands?.12f:.25f,grounded?.07f:.87f,twoHands?-.34f:-.18f));
            rifle.position=anchor-rifle.TransformVector(butt);
            for(int pass=0;pass<16;pass++){
                MaxContactError=0;MaxWristAngle=0;
                Vector3 lower=rifle.TransformPoint(butt)+rifle.forward*(grounded?.72f:.04f);
                Vector3 upper=rifle.TransformPoint(butt)+rifle.forward*(grounded?1.03f:.36f);
                Grasp("l",lower-rifle.up*.022f,rifle.up,rifle.forward);Vector3 correction=lastCorrection;
                if(!standing||variant==1){
                    Grasp("r",upper+rifle.right*.028f,-rifle.right,rifle.forward);
                    correction=(correction+lastCorrection)*.5f;
                }else{
                    armIK.Solve("r",root.TransformPoint(new Vector3(-.28f,.88f,-.04f+(standing?0:wave*.08f))),Vector3.down);Relax("r");
                }
                if(MaxContactError<.003f)break;
                if(pass<15)rifle.position+=correction;
            }
        }
        public void ApplyCommand(EmpireBattle.AnimationAuthoring.InfantryMotionKind kind,float phase)
        {
            float wave=Mathf.Sin(phase*Mathf.PI*2);
            if(kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Standard||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Sergeant){
                Grasp("l",root.TransformPoint(new Vector3(.08f,1.05f,-.37f)),root.right,root.up);
                Grasp("r",root.TransformPoint(new Vector3(.08f,1.38f,-.37f)),root.right,root.up);
            }else if(kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Drummer){
                armIK.Solve("l",root.TransformPoint(new Vector3(.24f,1.03f+wave*.035f,-.39f)),Vector3.down);
                armIK.Solve("r",root.TransformPoint(new Vector3(-.24f,1.03f-wave*.035f,-.39f)),Vector3.down);
                Relax("l");Relax("r");
            }else{
                float lift=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Colonel?.65f:0;
                armIK.Solve("r",root.TransformPoint(new Vector3(-.33f,1.15f+lift,-.27f)),root.forward);Relax("r");
                armIK.Solve("l",root.TransformPoint(new Vector3(.29f,.9f+lift,-.04f)),Vector3.down);Relax("l");
            }
        }
        public void ApplyPike(Transform equipment,float phase=0,bool thrust=false)
        {
            MaxContactError=0;MaxWristAngle=0;
            var shaft=equipment.Find("Sergeant pike shaft");
            Vector3 axis=shaft.up;
            float extension=thrust?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.45f,phase))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.52f,1f,phase))):0;
            // The lead hand guides the sliding shaft; the rear hand supplies the thrust.
            Grasp("l",shaft.position-axis*(.12f+extension*.32f),root.right,axis);
            Grasp("r",shaft.position-axis*(.40f+extension*.24f),root.right,axis);
        }
        public void ApplyCaptain(float phase,bool slash,bool firing)
        {
            ApplyCommand(EmpireBattle.AnimationAuthoring.InfantryMotionKind.Officer,phase);
            if(postureDrop>.1f){
                Grasp("r",root.TransformPoint(new Vector3(-.33f,1.15f-postureDrop,-.27f)),root.right,root.up);
                armIK.Solve("l",root.TransformPoint(new Vector3(.29f,.9f-postureDrop,-.04f)),Vector3.down);Relax("l");
            }
            if(firing){float recoil=Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI);Grasp("l",root.TransformPoint(new Vector3(.29f,1.24f+recoil*.02f,-.48f+recoil*.035f)),root.right,-root.forward);}
            if(slash){
                float pulse=Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI);
                Grasp("r",root.TransformPoint(new Vector3(-.33f+pulse*.36f,1.15f-postureDrop+pulse*.07f,-.27f-pulse*.27f)),root.right,root.up);
            }
        }
        public void ApplyMelee(MeleeAction action,float phase)
        {
            firingPose=false;MaxContactError=0;MaxWristAngle=0;
            float extension=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.45f,phase))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.52f,1f,phase)));
            float pulse=Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI);
            Vector3 axis=new Vector3(.05f,.12f,-1);
            Vector3 stock=new Vector3(-.07f,1.08f-postureDrop,-.06f);
            if(action==MeleeAction.Thrust)stock.z-=extension*.38f;
            if(action==MeleeAction.Parry){axis=new Vector3(.42f*pulse,.36f,-1);stock.y+=pulse*.13f;}
            if(action==MeleeAction.ButtStrike){
                // Turn the muzzle behind the soldier before driving the wooden heel
                // forward. Contact is phase .5, shared with MeleePresentationTiming.
                float turn=Ease(phase/.36f)*(1-Ease((phase-.62f)/.38f));
                float blow=Ease((phase-.26f)/.24f)*(1-Ease((phase-.50f)/.40f));
                axis=Vector3.Slerp(axis,new Vector3(.38f,.18f,1),turn);
                stock+=new Vector3(-.13f,.06f,-.27f)*blow;
            }
            if(action==MeleeAction.Stagger||action==MeleeAction.Recoil)stock.z+=pulse*.08f;
            rifle.rotation=root.rotation*Quaternion.LookRotation(axis,Vector3.up);
            rifle.position=root.TransformPoint(stock)-rifle.TransformVector(new Vector3(0,0,-25));
            for(int pass=0;pass<20;pass++)
            {
                MaxContactError=0;MaxWristAngle=0;
                Grasp("r",rifle.TransformPoint(new Vector3(0,0,-25))+rifle.right*.028f,-rifle.right,rifle.forward);Vector3 a=lastCorrection;
                Grasp("l",rifle.TransformPoint(new Vector3(0,1.8f,-15))-rifle.up*.022f,rifle.up,rifle.forward);Vector3 b=lastCorrection;
                if(MaxContactError<.004f)break;if(pass<19)rifle.position+=(a+b)*.55f;
            }
        }
        public void ApplyMountedCombat(MeleeAction action,float phase)
        {
            MaxContactError=0;MaxWristAngle=0;firingPose=false;
            float sweep=Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI);
            float sign=action==MeleeAction.SlashLeft?1:-1;
            Vector3 swordHand=new Vector3(-.32f,1.85f,-.27f);
            if(action==MeleeAction.SlashLeft||action==MeleeAction.SlashRight)swordHand=Vector3.Lerp(swordHand,(sign>0?new Vector3(-.12f,1.89f,-.45f):new Vector3(-.56f,1.78f,-.34f)),sweep);
            Grasp("r",root.TransformPoint(swordHand),root.right,root.up);
            if(bones.TryGetValue("Sabre",out var sword)){sword.position=root.TransformPoint(swordHand);var blade=new Vector3(sign*sweep*.70f,1-sweep*.8f,-sweep*.5f).normalized;sword.rotation=Quaternion.FromToRotation(Vector3.up,root.TransformDirection(blade));}
            
            armIK.Solve("l",root.TransformPoint(new Vector3(.15f,1.70f,-.40f)),root.forward);Relax("l");
        }
        // A low carry keeps the point forward/outside the leg and frees the
        // opposite arm. The same solved grip is used for idle and all gait phases.
        public void ApplySwordCarry(EmpireBattle.AnimationAuthoring.InfantryMotionKind kind,float phase,Transform sword,bool ready=false)
        {
            bool moving=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.March||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Walk
                ||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Jog||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Run||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Charge;
            float swing=moving?Mathf.Sin(phase*Mathf.PI*2):0;
            Vector3 wrist=ready?new Vector3(-.34f,1.12f,-.28f):new Vector3(-.34f,.87f,-.03f+swing*.045f);
            Vector3 blade=ready?new Vector3(-.18f,.74f,-.65f):new Vector3(-.14f,-.55f,-.83f+swing*.04f);
            Vector3 free=ready?new Vector3(.30f,1.08f,-.23f):new Vector3(.30f,.9f,-.06f-swing*.13f);
            PoseSword(sword,wrist,blade,free);
        }
        public void ApplySword(MeleeAction action,float phase,Transform sword)
        {
            // Anticipation, cutting contact and follow-through are distinct. All
            // strikes leave/return to this guard; contact remains exactly phase .5.
            Vector3 wrist=new Vector3(-.34f,1.12f,-.28f),blade=new Vector3(-.18f,.74f,-.65f);
            Vector3 free=new Vector3(.30f,1.08f,-.23f);
            if(action==MeleeAction.SlashLeft||action==MeleeAction.SlashRight)
            {
                bool left=action==MeleeAction.SlashLeft;
                Vector3 wind=left?new Vector3(.09f,1.38f,-.28f):new Vector3(-.47f,1.43f,-.16f);
                Vector3 hit=left?new Vector3(-.46f,1.13f,-.48f):new Vector3(.02f,1.09f,-.51f);
                Vector3 finish=left?new Vector3(-.49f,.98f,-.29f):new Vector3(.12f,.96f,-.29f);
                Vector3 windBlade=left?new Vector3(.65f,.65f,.20f):new Vector3(-.62f,.73f,.20f);
                Vector3 hitBlade=left?new Vector3(-.62f,-.15f,-.78f):new Vector3(.62f,-.15f,-.78f);
                Vector3 finishBlade=left?new Vector3(-.82f,-.35f,-.25f):new Vector3(.82f,-.35f,-.25f);
                if(phase<.25f){float t=Ease(phase/.25f);wrist=Vector3.Lerp(wrist,wind,t);blade=Vector3.Slerp(blade,windBlade,t);}
                else if(phase<.5f){float t=Ease((phase-.25f)/.25f);wrist=Vector3.Lerp(wind,hit,t);blade=Vector3.Slerp(windBlade,hitBlade,t);}
                else if(phase<.68f){float t=Ease((phase-.5f)/.18f);wrist=Vector3.Lerp(hit,finish,t);blade=Vector3.Slerp(hitBlade,finishBlade,t);}
                else{float t=Ease((phase-.68f)/.32f);wrist=Vector3.Lerp(finish,wrist,t);blade=Vector3.Slerp(finishBlade,blade,t);}
                free.z+=Mathf.Sin(phase*Mathf.PI)*.08f;
            }
            if(action==MeleeAction.Parry){float pulse=Mathf.Sin(phase*Mathf.PI);wrist.y+=.20f*pulse;blade=Vector3.Slerp(blade,new Vector3(.75f,.58f,-.15f),pulse);}
            if(action==MeleeAction.Stagger||action==MeleeAction.Recoil)wrist.z+=.12f*Mathf.Sin(phase*Mathf.PI);
            PoseSword(sword,wrist,blade,free);
        }
        void PoseSword(Transform sword,Vector3 wrist,Vector3 blade,Vector3 free)
        {
            firingPose=false;MaxContactError=0;MaxWristAngle=0;
            // Orient the palm to the hilt as well as placing the wrist: rotating
            // the blade independently of a relaxed mitten caused the old stiff grip.
            Vector3 axis=root.TransformDirection(blade.normalized);
            Vector3 inward=Vector3.ProjectOnPlane(root.right,axis).normalized;
            Grasp("r",root.TransformPoint(wrist),inward,axis,false,true);
            armIK.Solve("l",root.TransformPoint(free),Vector3.down);Relax("l");
            sword.position=rightMesh.TransformPoint(new Vector3(0,.03f,.04f));
            Vector3 edge=Vector3.ProjectOnPlane(root.forward,axis).normalized;
            sword.rotation=Quaternion.LookRotation(edge,axis);sword.localScale=Vector3.one;
        }
        public void FollowSword(Transform sword)
        {
            Relax("r");Relax("l");sword.position=rightMesh.TransformPoint(new Vector3(0,.03f,.04f));sword.rotation=rightMesh.rotation;sword.localScale=Vector3.one;
        }
        void Grasp(string side,Vector3 palmCentre,Vector3 inward,Vector3 cylinder,bool keepElbow=false,bool alignCylinder=false,float cylinderWeight=1)
        {
            inward.Normalize();cylinder.Normalize();
            var elbow=bones[side+"_foretwist"];var hand=bones[side+"_hand"];
            // Follow the forearm continuously in the palm plane; a discrete tangent sign would flip wrists.
            Vector3 longDirection=Vector3.ProjectOnPlane(hand.position-elbow.position,inward).normalized;
            if(longDirection.sqrMagnitude<.01f)longDirection=Vector3.Cross(cylinder,inward).normalized;
            Vector3 gripCentre=palmCentre+inward*.03f;Vector3 originalNormal=inward;
            if(alignCylinder){Vector3 tangent=Vector3.ProjectOnPlane(hand.position-elbow.position,cylinder).normalized;if(tangent.sqrMagnitude<.01f)tangent=side=="r"?root.right:-root.right;longDirection=Vector3.Slerp(longDirection,tangent,cylinderWeight).normalized;inward=Vector3.ProjectOnPlane(Vector3.Slerp(originalNormal,Vector3.Cross(cylinder,tangent).normalized,cylinderWeight),longDirection).normalized;palmCentre=gripCentre-inward*.03f;}

            Vector3 pole=keepElbow?elbow.position:root.TransformPoint(new Vector3(side=="l"?.55f:-.5f,1.12f,-.1f));
            for(int iteration=0;iteration<24;iteration++)
            {
                Vector3 wrist=palmCentre-longDirection*.075f;
                armIK.Solve(side,wrist,longDirection,firingPose&&side=="r"?root.InverseTransformPoint(wrist).y-.12f:float.NaN);
                Vector3 projected=Vector3.ProjectOnPlane(hand.position-elbow.position,inward).normalized;
                if(alignCylinder){var next=Vector3.ProjectOnPlane(hand.position-elbow.position,cylinder).normalized;if(next.sqrMagnitude>.01f){Vector3 free=Vector3.ProjectOnPlane(hand.position-elbow.position,originalNormal).normalized;longDirection=Vector3.Slerp(free,next,cylinderWeight).normalized;inward=Vector3.ProjectOnPlane(Vector3.Slerp(originalNormal,Vector3.Cross(cylinder,next).normalized,cylinderWeight),longDirection).normalized;palmCentre=gripCentre-inward*.03f;}}else if(projected.sqrMagnitude>.01f)longDirection=projected;
            }
            var mesh=side=="l"?leftMesh:rightMesh;
            mesh.position=hand.position+longDirection*.035f;
            mesh.rotation=Quaternion.LookRotation(longDirection,inward);
            mesh.localScale=Vector3.one/Mathf.Max(.0001f,hand.lossyScale.x);
            lastCorrection=hand.position+longDirection*.075f-palmCentre;
            MaxContactError=Mathf.Max(MaxContactError,lastCorrection.magnitude);
            MaxWristAngle=Mathf.Max(MaxWristAngle,Vector3.Angle(longDirection,hand.position-elbow.position));
        }
        static float Ease(float t){return Mathf.SmoothStep(0,1,Mathf.Clamp01(t));}
        void Relax(string side)
        {
            var hand=bones[side+"_hand"];var elbow=bones[side+"_foretwist"];var mesh=side=="l"?leftMesh:rightMesh;
            Vector3 longDirection=(hand.position-elbow.position).normalized;
            Vector3 normal=Vector3.ProjectOnPlane(rifle.up,longDirection).normalized;if(normal.sqrMagnitude<.01f)normal=Vector3.ProjectOnPlane(root.forward,longDirection).normalized;
            mesh.position=hand.position+longDirection*.035f;mesh.rotation=Quaternion.LookRotation(longDirection,normal);mesh.localScale=Vector3.one/hand.lossyScale.x;
        }
        void SolveArm(string side,Vector3 target,Vector3 pole,Vector3 palmNormal,Vector3 fingerAxis)
        {
            var upper=bones[side+"_upperarm_twist"];var elbow=bones[side+"_foretwist"];var hand=bones[side+"_hand"];
            Vector3 shoulder=upper.position;float a=Vector3.Distance(shoulder,elbow.position),b=Vector3.Distance(elbow.position,hand.position);
            Vector3 direction=(target-shoulder).normalized;float d=Mathf.Clamp(Vector3.Distance(target,shoulder),Mathf.Abs(a-b)+.001f,a+b-.001f);
            Vector3 bend=Vector3.ProjectOnPlane(pole-shoulder,direction).normalized;float along=(a*a+d*d-b*b)/(2*d);
            float radius=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));Vector3 centre=shoulder+direction*along;
            Vector3 projectedNormal=Vector3.ProjectOnPlane(palmNormal,direction);float normalLength=projectedNormal.magnitude;
            if(fingerAxis.sqrMagnitude>.5f){bend=Vector3.ProjectOnPlane(target-fingerAxis*b-centre,direction).normalized;}else if(radius>.001f&&normalLength>.01f){Vector3 n=projectedNormal/normalLength;Vector3 tangent=Vector3.Cross(direction,n).normalized;if(Vector3.Dot(tangent,bend)<0)tangent=-tangent;float k=Mathf.Clamp(Vector3.Dot(target-centre,palmNormal)/(radius*normalLength),-1,1);bend=n*k+tangent*Mathf.Sqrt(Mathf.Max(0,1-k*k));}
            Vector3 desired=centre+bend*radius;
            upper.rotation=Quaternion.FromToRotation(elbow.position-shoulder,desired-shoulder)*upper.rotation;
            elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,target-elbow.position)*elbow.rotation;
        }
    }
}
