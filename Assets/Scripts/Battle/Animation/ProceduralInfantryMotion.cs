using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle.AnimationAuthoring
{
    public enum InfantryMotionKind { March, Run, Charge, Hit, Death, Walk, Recoil, Jog, Idle, UnderFire, Officer, Drummer, Standard, Sergeant, Colonel, Knockdown, GetUp, MeleeReady, Thrust, Parry, ButtStrike, Stagger, Retreat }

    /// <summary>Deterministic pose authoring on the source infantry skeleton; no Animator or root motion.</summary>
    public sealed class ProceduralInfantryRig
    {
        static readonly string[] BoneNames = {
            "pelvis", "l_thigh", "l_calf", "l_foot", "l_toe0", "r_thigh", "r_calf", "r_foot", "r_toe0",
            "spine", "spine1", "spine2", "l_clavicle", "l_upperarm_twist", "l_upperarm_twist1", "l_foretwist", "l_foretwist1", "l_hand", "l_finger0",
            "neck", "head", "r_clavicle", "r_upperarm_twist", "r_upperarm_twist1", "r_foretwist", "r_foretwist1", "r_hand", "r_finger0"
        };
        readonly Transform root;
        readonly Transform[] bones;
        readonly Vector3[] restPositions, restScales;
        readonly Quaternion[] restRotations;
        readonly Dictionary<string,int> indices;
        readonly Transform[] legThighs=new Transform[2],legCalves=new Transform[2],legFeet=new Transform[2];
        readonly Vector3[] baseFootRootPositions=new Vector3[2],kneePoles=new Vector3[2];
        readonly Quaternion[] baseFootRootRotations=new Quaternion[2];
        readonly float[] upperLegLengths=new float[2],lowerLegLengths=new float[2];
        readonly Vector3 forward, right;
        readonly float pelvisHeight;
        public int BoneCount => bones.Length;
        public bool HasRequiredBones => bones.Length==BoneNames.Length;
        /// <summary>Cached authoring bones, ordered as the source contract. Do not mutate this array.</summary>
        public Transform[] Bones => bones;

        /// <param name="root">An upright assembled rig already sampled at its desired musket-holding base pose.</param>
        /// <param name="authoredFacesNegativeZ">The Bannerlord source faces -Z. False supports a normalized +Z rig.</param>
        public ProceduralInfantryRig(GameObject root, bool authoredFacesNegativeZ=true)
        {
            if(!root)throw new ArgumentNullException(nameof(root));
            this.root=root.transform;
            forward=authoredFacesNegativeZ?Vector3.back:Vector3.forward;
            right=Vector3.Cross(Vector3.up,forward);
            var found=new Dictionary<string,Transform>(StringComparer.Ordinal);
            // Only constructor discovery allocates or traverses the hierarchy.
            foreach(Transform transform in root.GetComponentsInChildren<Transform>(true))
                if(!found.ContainsKey(transform.name))found.Add(transform.name,transform);
            var ordered=new List<Transform>(BoneNames.Length);
            indices=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(string name in BoneNames)
            {
                if(!found.TryGetValue(name,out Transform transform))continue;
                indices.Add(name,ordered.Count);ordered.Add(transform);
            }
            bones=ordered.ToArray();restPositions=new Vector3[bones.Length];restScales=new Vector3[bones.Length];restRotations=new Quaternion[bones.Length];
            for(int i=0;i<bones.Length;i++)
            {restPositions[i]=bones[i].localPosition;restScales[i]=bones[i].localScale;restRotations[i]=bones[i].localRotation;}
            Transform pelvis=Bone("pelvis");
            if(!pelvis||!Bone("l_thigh")||!Bone("r_thigh")||!Bone("spine"))
                throw new ArgumentException("Infantry rig requires pelvis, left/right thighs and spine.",nameof(root));
            CaptureLeg(0,"l_thigh","l_calf","l_foot");
            CaptureLeg(1,"r_thigh","r_calf","r_foot");
            pelvisHeight=Mathf.Max(.1f,Vector3.Dot(pelvis.position-this.root.position,this.root.up));
        }

        void CaptureLeg(int side,string thighName,string calfName,string footName)
        {
            Transform thigh=Bone(thighName),calf=Bone(calfName),foot=Bone(footName);
            if(!thigh||!calf||!foot)return;
            legThighs[side]=thigh;legCalves[side]=calf;legFeet[side]=foot;
            upperLegLengths[side]=Vector3.Distance(thigh.position,calf.position);
            lowerLegLengths[side]=Vector3.Distance(calf.position,foot.position);
            baseFootRootPositions[side]=root.InverseTransformPoint(foot.position);
            baseFootRootRotations[side]=Quaternion.Inverse(root.rotation)*foot.rotation;
            kneePoles[side]=(forward+(side==0?-right:right)*.16f).normalized;
        }

        public void ResetPose()
        {
            for(int i=0;i<bones.Length;i++)
            {bones[i].localPosition=restPositions[i];bones[i].localRotation=restRotations[i];bones[i].localScale=restScales[i];}
        }

        public void Sample(InfantryMotionKind kind,float normalizedTime,int variant=0)
        {
            if(float.IsNaN(normalizedTime)||float.IsInfinity(normalizedTime))normalizedTime=0f;
            ResetPose();
            gaitVariant=Mathf.Clamp(variant,0,2);
            switch(kind)
            {
                case InfantryMotionKind.March: Gait(normalizedTime,.65f,.07f,.012f,2.5f);break;
                case InfantryMotionKind.Walk: Gait(normalizedTime,.55f,.06f,.009f,1.5f);break;
                case InfantryMotionKind.Jog: Gait(normalizedTime,.70f,.10f,.019f,3.8f);break;
                case InfantryMotionKind.Run: Gait(normalizedTime,.78f,.13f,.026f,5f);break;
                case InfantryMotionKind.Charge:
                    Gait(normalizedTime,.78f,.13f,.026f,6f);
                    break;
                case InfantryMotionKind.Idle: Stand(normalizedTime,variant,false);break;
                case InfantryMotionKind.UnderFire: Stand(normalizedTime,variant,true);break;
                case InfantryMotionKind.Drummer:
                case InfantryMotionKind.Standard:
                    if(variant==1)Gait(normalizedTime,.65f,.07f,.012f,2.5f);
                    else Stand(normalizedTime,0,false);
                    break;
                case InfantryMotionKind.Officer:
                    if(variant==4||variant==6)Fall(Mathf.Clamp01(normalizedTime),0);
                    else if(variant==5)GetUp(Mathf.Clamp01(normalizedTime));
                    else if(variant==1)Gait(normalizedTime,.65f,.07f,.012f,2.5f);
                    else if(variant==3)CombatPose(InfantryMotionKind.ButtStrike,Mathf.Clamp01(normalizedTime));
                    else Stand(normalizedTime,0,false);
                    break;
                case InfantryMotionKind.Sergeant:
                    if(variant==4||variant==6)Fall(Mathf.Clamp01(normalizedTime),0);
                    else if(variant==5)GetUp(Mathf.Clamp01(normalizedTime));
                    else if(variant==1)Gait(normalizedTime,.65f,.07f,.012f,2.5f);
                    else if(variant==3)CombatPose(InfantryMotionKind.Thrust,Mathf.Clamp01(normalizedTime));
                    else Stand(normalizedTime,0,false);
                    break;
                case InfantryMotionKind.Colonel: Mounted(normalizedTime,variant==1);break;
                case InfantryMotionKind.Hit: React(Mathf.Clamp01(normalizedTime),1f);break;
                case InfantryMotionKind.Recoil: React(Mathf.Clamp01(normalizedTime),.45f);break;
                case InfantryMotionKind.MeleeReady:
                case InfantryMotionKind.Thrust:
                case InfantryMotionKind.Parry:
                case InfantryMotionKind.ButtStrike:
                case InfantryMotionKind.Stagger:
                case InfantryMotionKind.Retreat: CombatPose(kind,Mathf.Clamp01(normalizedTime));break;
                case InfantryMotionKind.Knockdown: Fall(Mathf.Clamp01(normalizedTime),variant);break;
                case InfantryMotionKind.GetUp: GetUp(Mathf.Clamp01(normalizedTime),variant);break;
                case InfantryMotionKind.Death: DeathFall(Mathf.Clamp01(normalizedTime),Mathf.Clamp(variant,0,9));break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public void SampleMounted(MeleeAction action,float phase,float gaitBob=0)
        {
            ResetPose();Mounted(0,false);OffsetPelvis(Vector3.up*gaitBob);
            float wave=Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI);
            if(action==MeleeAction.SlashLeft||action==MeleeAction.SlashRight){Rotate("spine2",Vector3.up,(action==MeleeAction.SlashLeft?-24:24)*wave);Rotate("spine",right,12*wave);}
            if(action==MeleeAction.Brake||action==MeleeAction.Stumble)Rotate("spine",right,-18*wave);
        }
        public void SampleMountedFall(float phase,int variant=1)
        {
            phase=Mathf.Clamp01(phase);ResetPose();Mounted(0,false);
            // The first pose retains the saddle pose. Fast falls tuck and rotate before landing.
            float landing=variant==3?Mathf.InverseLerp(.32f,1f,phase):phase;
            OffsetPelvis(Vector3.down*(.65f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.06f,.84f,landing))));
            Fall(landing,variant==0?0:1);
            if(variant==2)Rotate("pelvis",forward,65f*Mathf.Sin(phase*Mathf.PI));
            if(variant==3)
            {
                Rotate("pelvis",right,360f*Mathf.SmoothStep(0,1,Mathf.Clamp01(phase/.82f)));
                float tuck=Mathf.Sin(phase*Mathf.PI);
                Rotate("l_thigh",right,-50*tuck);Rotate("r_thigh",right,-65*tuck);
                Rotate("l_calf",right,70*tuck);Rotate("r_calf",right,85*tuck);
            }
            SettleGrounded(Mathf.SmoothStep(0,1,Mathf.InverseLerp(.7f,1f,phase)));
        }
        void CombatPose(InfantryMotionKind kind,float phase)
        {
            // A retreat is a backward step. The stationary fighting stance below
            // used to overwrite both solved feet and erase that step entirely.
            if(kind==InfantryMotionKind.Retreat){Gait(1-phase,.45f,.06f,.01f,2);return;}
            float pulse=Mathf.Sin(phase*Mathf.PI);
            OffsetPelvis(Vector3.down*.025f);
            Rotate("spine",right,kind==InfantryMotionKind.Thrust?12*pulse:5);
            if(kind==InfantryMotionKind.ButtStrike)Rotate("spine2",Vector3.up,-22*pulse);
            if(kind==InfantryMotionKind.Parry)Rotate("spine2",Vector3.up,14*pulse);
            if(kind==InfantryMotionKind.Stagger){React(phase,1.3f);Rotate("l_calf",right,20*pulse);}
            for(int side=0;side<2;side++)
            {
                Vector3 target=baseFootRootPositions[side]+forward*(side==0?.14f:-.14f);
                SolveLeg(legThighs[side],legCalves[side],legFeet[side],root.TransformPoint(target),upperLegLengths[side],lowerLegLengths[side],root.TransformDirection(kneePoles[side]));
                legFeet[side].rotation=root.rotation*baseFootRootRotations[side];
            }
        }
        void Mounted(float phase,bool moving)
        {
            OffsetPelvis(Vector3.up*(.65f+(moving?.008f*Mathf.Sin(phase*Mathf.PI*4):0)));
            for(int side=0;side<2;side++){
                Vector3 target=root.TransformPoint(new Vector3(side==0?.38f:-.38f,.68f,-.12f));
                SolveLeg(legThighs[side],legCalves[side],legFeet[side],target,upperLegLengths[side],lowerLegLengths[side],root.TransformDirection(forward+(side==0?-right:right)*.75f));
                legFeet[side].rotation=root.rotation*baseFootRootRotations[side];
            }
        }

        // Offline lower-body pose: left foot planted ahead, right knee down behind it.
        // The upper body and its held weapon move together without scaling bones.
        public void Kneel(float amount = 1f)
        {
            amount = Mathf.Clamp01(amount);
            OffsetPelvis(Vector3.down * (.43f * amount));
            for (int side = 0; side < 2; side++)
            {
                Vector3 target = root.TransformPoint(baseFootRootPositions[side])
                    + root.TransformDirection(forward) * ((side == 0 ? .23f : -.48f) * amount);
                SolveLeg(legThighs[side], legCalves[side], legFeet[side], target,
                    upperLegLengths[side], lowerLegLengths[side], root.TransformDirection(kneePoles[side]));
                legFeet[side].rotation = root.rotation * baseFootRootRotations[side];
            }
        }

        public void LeanIntoWork(float amount)
        {
            amount=Mathf.Clamp01(amount);
            Rotate("spine",right,12f*amount);
            Rotate("spine1",right,6f*amount);
            Rotate("neck",right,-8f*amount);
        }

        void Stand(float time,int variant,bool underFire)
        {
            float phase=Mathf.Repeat(time,1f)*Mathf.PI*2;
            float breath=Mathf.Sin(phase);
            // Slow weight transfer, with both feet anchored instead of sliding the whole model.
            OffsetPelvis(right*(Mathf.Sin(phase)*.009f)+Vector3.up*(breath*.002f));
            Rotate("spine",right,underFire?3f*(1-Mathf.Cos(phase)):breath*.65f);
            Rotate("spine1",Vector3.up,(variant-1)*Mathf.Sin(phase)*1.4f);
            Rotate("head",Vector3.up,Mathf.Sin(phase)*(underFire?4f:variant==2?8f:2f));
            Rotate("head",right,underFire?4f*(1-Mathf.Cos(phase)):breath*.8f);
            for(int side=0;side<2;side++){
                Vector3 target=root.TransformPoint(baseFootRootPositions[side]);
                // One short repositioning step per foot, then a long settled pause.
                if(!underFire&&variant==2){
                    float step=Mathf.InverseLerp(side*.125f,(side+1)*.125f,Mathf.Repeat(time,1));
                    float shuffle=Mathf.Sin(step*Mathf.PI);
                    target+=root.TransformDirection(forward)*(.035f*shuffle)+root.up*(.018f*shuffle);
                }
                SolveLeg(legThighs[side],legCalves[side],legFeet[side],target,upperLegLengths[side],lowerLegLengths[side],root.TransformDirection(kneePoles[side]));
                legFeet[side].rotation=root.rotation*baseFootRootRotations[side];
            }
        }

        int gaitVariant;
        void Gait(float normalizedTime,float strideMeters,float footLiftMeters,float bobMeters,float lean)
        {
            // The loop endpoint maps exactly to phase zero. Root motion remains fixed; feet
            // travel through a planted stance and a lifted swing in root-relative space.
            float character=gaitVariant-1f;
            float stance=footLiftMeters>.08f?.48f:.62f;
            strideMeters*=1f+character*.06f;
            footLiftMeters*=1f+character*.15f;
            lean+=character*.8f;
            float cycle=Mathf.Repeat(normalizedTime,1f);
            float phase=cycle*Mathf.PI*2f;
            float wave=Mathf.Sin(phase);
            OffsetPelvis(Vector3.up*(bobMeters*Mathf.Sin(phase*2f))
                +right*(wave*(.004f+gaitVariant*.003f)));
            Rotate("pelvis",forward,wave*(1.2f+gaitVariant*.6f));
            Rotate("pelvis",Vector3.up,wave*1.1f);
            Rotate("spine",right,lean);
            Rotate("spine1",Vector3.up,-wave*(1.2f+gaitVariant*.6f));
            Rotate("neck",right,-lean*.35f);
            // Solve after the hip and torso have moved so planted feet remain at their targets.
            // Keep the planted targets within leg reach. A straight-legged base pose
            // otherwise clamps the IK at full extension and pulls the sole off the ground.
            float drop=0;
            for(int side=0;side<2;side++)
            {
                if(!legThighs[side])continue;
                Vector3 target=FootTarget(side,Mathf.Repeat(cycle+side*.5f,1),strideMeters,footLiftMeters,stance);
                Vector3 delta=legThighs[side].position-target;
                float reach=upperLegLengths[side]+lowerLegLengths[side]-.008f;
                float horizontal=Vector3.ProjectOnPlane(delta,root.up).sqrMagnitude;
                float height=Mathf.Sqrt(Mathf.Max(0,reach*reach-horizontal));
                drop=Mathf.Max(drop,Vector3.Dot(delta,root.up)-height);
            }
            OffsetPelvis(Vector3.down*Mathf.Min(.18f,drop));
            PlaceFoot(0,cycle,strideMeters,footLiftMeters,stance);
            PlaceFoot(1,Mathf.Repeat(cycle+.5f,1f),strideMeters,footLiftMeters,stance);
        }

        void PlaceFoot(int side,float cycle,float strideMeters,float liftMeters,float stanceFraction)
        {
            Transform thigh=legThighs[side],calf=legCalves[side],foot=legFeet[side];
            if(!thigh||!calf||!foot||upperLegLengths[side]<.001f||lowerLegLengths[side]<.001f)return;
            Vector3 target=FootTarget(side,cycle,strideMeters,liftMeters,stanceFraction);
            SolveLeg(thigh,calf,foot,target,upperLegLengths[side],lowerLegLengths[side],root.TransformDirection(kneePoles[side]));
            foot.rotation=root.rotation*baseFootRootRotations[side];
        }

        Vector3 FootTarget(int side,float cycle,float strideMeters,float liftMeters,float stanceFraction)
        {
            float strideOffset,lift=0f;
            if(cycle<stanceFraction)
            {
                float t=cycle/stanceFraction;
                strideOffset=Mathf.Lerp(strideMeters*.5f,-strideMeters*.5f,t);
            }
            else
            {
                float t=(cycle-stanceFraction)/(1f-stanceFraction);
                float smooth=t*t*(3f-2f*t);
                strideOffset=Mathf.Lerp(-strideMeters*.5f,strideMeters*.5f,smooth);
                lift=Mathf.Sin(t*Mathf.PI)*liftMeters;
            }

            return root.TransformPoint(baseFootRootPositions[side])
                +root.TransformDirection(forward)*(strideOffset)
                +root.up*lift;
        }

        static void SolveLeg(Transform thigh,Transform calf,Transform foot,Vector3 target,
            float upperLength,float lowerLength,Vector3 poleRoot)
        {
            Vector3 hip=thigh.position;
            Vector3 toTarget=target-hip;
            float rawDistance=toTarget.magnitude;
            if(rawDistance<.0001f)return;
            float maximumReach=Mathf.Max(.001f,upperLength+lowerLength-.002f);
            float minimumReach=Mathf.Abs(upperLength-lowerLength)+.002f;
            float distance=Mathf.Clamp(rawDistance,minimumReach,maximumReach);
            Vector3 direction=toTarget/rawDistance;
            target=hip+direction*distance;
            Vector3 pole=Vector3.ProjectOnPlane(poleRoot,direction);
            if(pole.sqrMagnitude<.0001f)pole=Vector3.ProjectOnPlane(Vector3.up,direction);
            if(pole.sqrMagnitude<.0001f)pole=Vector3.ProjectOnPlane(Vector3.right,direction);
            pole.Normalize();
            float along=(upperLength*upperLength-lowerLength*lowerLength+distance*distance)/(2f*distance);
            float height=Mathf.Sqrt(Mathf.Max(0f,upperLength*upperLength-along*along));
            Vector3 desiredKnee=hip+direction*along+pole*height;

            Vector3 currentUpper=calf.position-hip;
            Vector3 desiredUpper=desiredKnee-hip;
            if(currentUpper.sqrMagnitude>.000001f&&desiredUpper.sqrMagnitude>.000001f)
                thigh.rotation=Quaternion.FromToRotation(currentUpper,desiredUpper)*thigh.rotation;

            Vector3 currentLower=foot.position-calf.position;
            Vector3 desiredLower=target-calf.position;
            if(currentLower.sqrMagnitude>.000001f&&desiredLower.sqrMagnitude>.000001f)
                calf.rotation=Quaternion.FromToRotation(currentLower,desiredLower)*calf.rotation;
        }

        void React(float time,float strength)
        {
            if(time<=0f||time>=1f)return;
            float envelope=Mathf.Sin(Mathf.PI*time)*Mathf.Exp(-time*1.3f)*strength;
            OffsetPelvis(-forward*(.075f*envelope)-Vector3.up*(.018f*envelope));
            Rotate("spine",right,-18f*envelope);
            Rotate("spine1",right,-8f*envelope);
            Rotate("spine2",Vector3.up,7f*envelope);
            Rotate("head",right,10f*envelope);
            Rotate("l_calf",right,12f*envelope);Rotate("r_calf",right,8f*envelope);
            Rotate("l_upperarm_twist",right,-5f*envelope);Rotate("r_upperarm_twist",right,-5f*envelope);
        }

        void DeathFall(float time,int variant)
        {
            // Impact, loss of support, gravity-led collapse and a short landing settle.
            // The final grounded contact solver still fits the actual uniform and equipment.
            float delay=variant>=8?.22f:variant>=6?.13f:.035f;
            float progress=Mathf.InverseLerp(delay,.92f,time);
            float fall=progress*progress*(2f-progress);
            Fall(fall,variant,true);
            float hit=Mathf.Sin(Mathf.PI*Mathf.Clamp01(time/.3f))*(1f-Mathf.Clamp01(time/.3f));
            float buckle=Mathf.Sin(Mathf.PI*progress)*(variant>=6?.28f:.07f);
            float stagger=Mathf.Sin(Mathf.PI*progress)*(variant%2==0?-.22f:.27f);
            OffsetPelvis(-Vector3.up*buckle+forward*stagger+right*((variant%3-1)*.14f*Mathf.Sin(Mathf.PI*progress)));
            Rotate("spine",right,(variant%2==0?-24:31)*hit);
            Rotate("spine2",Vector3.up,(variant%3-1)*20*hit);
            Rotate("head",right,-18*hit);
        }
        void Fall(float time,int variant,bool death=false)
        {
            float collapse=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.06f,.84f,time));
            float brace=Mathf.Sin(Mathf.PI*time);
            bool prone=variant%2==1;
            float spread=(variant%5-2)*.025f;
            // A quarter turn around the lateral axis lays the torso on its front/back.
            // Side rolls leave shoulders and bent limbs suspended above the floor.
            bool sideFall=death&&(variant==4||variant==5||variant==8||variant==9);
            Rotate("pelvis",sideFall?forward:right,(sideFall?(variant%2==0?-90f:90f):(prone?90f:-90f))*collapse);
            OffsetPelvis(Vector3.up*(((prone?.14f:.27f)-pelvisHeight)*collapse));
            Rotate("spine",right,8f*brace);
            for(int side=0;side<2;side++)
            {
                float sign=side==0?-1f:1f;
                var foot=legFeet[side];
                Vector3 target=root.InverseTransformPoint(foot.position);
                target.y=Mathf.Lerp(target.y,.075f,collapse);
                target+=right*sign*(.06f+spread)*collapse;
                Vector3 footTarget=root.TransformPoint(target);
                Vector3 kneePole=GroundPole(legThighs[side].position,footTarget,upperLegLengths[side],lowerLegLengths[side],Mathf.Lerp(legCalves[side].position.y,root.TransformPoint(Vector3.up*.075f).y,collapse),root.TransformDirection(right*sign));
                SolveLeg(legThighs[side],legCalves[side],foot,footTarget,upperLegLengths[side],lowerLegLengths[side],kneePole);
                FlattenEnd(foot,Bone(side==0?"l_toe0":"r_toe0"),collapse);
                Transform arm=Bone(side==0?"l_upperarm_twist":"r_upperarm_twist"),elbow=Bone(side==0?"l_foretwist":"r_foretwist"),hand=Bone(side==0?"l_hand":"r_hand");
                float upper=Vector3.Distance(arm.position,elbow.position),lower=Vector3.Distance(elbow.position,hand.position);
                Vector3 shoulder=root.InverseTransformPoint(arm.position);
                Vector3 resting=shoulder+right*sign*(.26f+spread)+forward*((variant%3-1)*.14f);
                resting.y=.075f;
                Vector3 palm=Vector3.Lerp(root.InverseTransformPoint(hand.position),resting,collapse);
                Vector3 handTarget=root.TransformPoint(palm);
                Vector3 elbowPole=GroundPole(arm.position,handTarget,upper,lower,Mathf.Lerp(elbow.position.y,root.TransformPoint(Vector3.up*.055f).y,collapse),root.TransformDirection(right*sign));
                SolveLeg(arm,elbow,hand,handTarget,upper,lower,elbowPole);
                FlattenEnd(hand,Bone(side==0?"l_finger0":"r_finger0"),collapse);
            }
            Rotate("pelvis",Vector3.up,(variant%5-2)*8f*collapse);
        }

        // Offline contact fitting uses the actual uniform, boots and rigid gloves.
        // Fit the torso first, then replant limbs so a thicker pack cannot lift them.
        public void SettleGrounded(float amount)
        {
            amount=Mathf.Clamp01(amount);if(amount<=0f)return;
            var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>();
            float low=float.PositiveInfinity;
            var footLow=new[]{float.PositiveInfinity,float.PositiveInfinity};
            var baked=new Mesh();
            foreach(var skin in skins)if(skin.enabled)
            {
                skin.BakeMesh(baked,true);var points=baked.vertices;var weights=skin.sharedMesh.boneWeights;
                for(int i=0;i<points.Length;i++)
                {
                    float y=Vector3.Dot(skin.transform.TransformPoint(points[i])-root.position,root.up);low=Mathf.Min(low,y);
                    if(i>=weights.Length)continue;
                    var w=weights[i];int index=w.boneIndex0;float weight=w.weight0;
                    if(w.weight1>weight){index=w.boneIndex1;weight=w.weight1;}if(w.weight2>weight){index=w.boneIndex2;weight=w.weight2;}if(w.weight3>weight){index=w.boneIndex3;weight=w.weight3;}
                    string name=skin.bones[index].name;
                    if(weight>.4f&&(name=="l_foot"||name=="l_toe0"))footLow[0]=Mathf.Min(footLow[0],y);
                    if(weight>.4f&&(name=="r_foot"||name=="r_toe0"))footLow[1]=Mathf.Min(footLow[1],y);
                }
            }
            UnityEngine.Object.DestroyImmediate(baked);
            if(float.IsInfinity(low))return;
            float shift=(.008f-low)*amount;OffsetPelvis(Vector3.up*shift);
            for(int side=0;side<2;side++)
            {
                var foot=legFeet[side];Quaternion rotation=foot.rotation;
                Vector3 target=foot.position;
                if(!float.IsInfinity(footLow[side]))target+=root.up*((.008f-footLow[side]-shift)*amount);
                float kneeY=root.TransformPoint(Vector3.up*.075f).y;
                var pole=GroundPole(legThighs[side].position,target,upperLegLengths[side],lowerLegLengths[side],Mathf.Lerp(legCalves[side].position.y,kneeY,amount),root.TransformDirection(right*(side==0?-1:1)));
                SolveLeg(legThighs[side],legCalves[side],foot,target,upperLegLengths[side],lowerLegLengths[side],pole);foot.rotation=rotation;
                string prefix=side==0?"l":"r";
                var arm=Bone(prefix+"_upperarm_twist");var elbow=Bone(prefix+"_foretwist");var hand=Bone(prefix+"_hand");
                Transform glove=null;foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())if(renderer.name==(side==0?"Closed left hand":"Closed right hand")){glove=renderer.transform;break;}
                float palmLow=float.PositiveInfinity;
                if(glove)foreach(var point in glove.GetComponent<MeshFilter>().sharedMesh.vertices)palmLow=Mathf.Min(palmLow,Vector3.Dot(glove.TransformPoint(point)-root.position,root.up));
                Vector3 palm=hand.position;if(!float.IsInfinity(palmLow))palm+=root.up*((.008f-palmLow)*amount);
                float upper=Vector3.Distance(arm.position,elbow.position),lower=Vector3.Distance(elbow.position,hand.position);
                var elbowPole=GroundPole(arm.position,palm,upper,lower,Mathf.Lerp(elbow.position.y,root.TransformPoint(Vector3.up*.055f).y,amount),root.TransformDirection(right*(side==0?-1:1)));
                rotation=hand.rotation;SolveLeg(arm,elbow,hand,palm,upper,lower,elbowPole);hand.rotation=rotation;
            }
        }

        // Choose a point on the IK bend circle at the floor height rather than letting
        // a horizontal pole raise the knees/elbows when the torso rests on its pack.
        static Vector3 GroundPole(Vector3 start,Vector3 target,float upper,float lower,float jointHeight,Vector3 lateral)
        {
            Vector3 delta=target-start;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(upper-lower)+.002f,upper+lower-.002f);
            Vector3 direction=delta.normalized;
            float along=(upper*upper-lower*lower+distance*distance)/(2f*distance);
            float radius=Mathf.Sqrt(Mathf.Max(.000001f,upper*upper-along*along));
            Vector3 vertical=Vector3.ProjectOnPlane(Vector3.up,direction).normalized;
            Vector3 side=Vector3.Cross(direction,vertical).normalized;if(Vector3.Dot(side,lateral)<0)side=-side;
            float tilt=Mathf.Clamp((jointHeight-(start+direction*along).y)/Mathf.Max(.0001f,radius*vertical.y),-1f,1f);
            return vertical*tilt+side*Mathf.Sqrt(1f-tilt*tilt);
        }
        void FlattenEnd(Transform joint,Transform tip,float amount)
        {
            if(!joint||!tip)return;
            Vector3 axis=tip.position-joint.position,tangent=Vector3.ProjectOnPlane(axis,root.up);
            if(tangent.sqrMagnitude<.000001f)tangent=root.TransformDirection(forward)*axis.magnitude;
            joint.rotation=Quaternion.Slerp(joint.rotation,Quaternion.FromToRotation(axis,tangent)*joint.rotation,amount);
        }

        // Forward recovery, with separate support changes rather than reversing the fall.
        void GetUp(float time,int variant=0)
        {
            if(time >= 1f)return;
            var positions=new Vector3[4][];var rotations=new Quaternion[4][];
            var feet=new Vector3[4,2];var footRotations=new Quaternion[4,2];
            for(int key=0;key<4;key++)
            {
                ResetPose();
                if(key==0)Fall(1f,variant);
                else if(key<3)
                {
                    float height=key==1?.36f+(variant%3)*.035f:.60f;
                    OffsetPelvis(Vector3.up*(height-pelvisHeight)-forward*(key==1?.16f:.06f));
                    Rotate("pelvis",right,key==1?(variant%2==1?65f:-35f):18f);
                    Rotate("spine",right,key==1?-12f:12f);
                    Rotate("head",right,-15f);
                    for(int side=0;side<2;side++)
                    {
                        Vector3 foot=baseFootRootPositions[side];
                        foot.y=.06f;foot+=forward*(key==1?(variant%2==1?-.28f:.35f):side==(variant%2)?.22f:-.30f);
                        SolveLeg(legThighs[side],legCalves[side],legFeet[side],root.TransformPoint(foot),upperLegLengths[side],lowerLegLengths[side],root.TransformDirection(forward));
                        legFeet[side].rotation=root.rotation*baseFootRootRotations[side];
                    }
                    string supportSide=variant%2==0?"l":"r";
                    Transform arm=Bone(supportSide+"_upperarm_twist"),elbow=Bone(supportSide+"_foretwist"),hand=Bone(supportSide+"_hand");
                    Vector3 support=right*(variant%2==0?-.32f:.32f)+forward*(key==1?(variant%2==1?.38f:-.28f):.24f)+Vector3.up*.08f;
                    SolveLeg(arm,elbow,hand,root.TransformPoint(support),Vector3.Distance(arm.position,elbow.position),Vector3.Distance(elbow.position,hand.position),root.TransformDirection(-right));
                }
                for(int side=0;side<2;side++){feet[key,side]=root.InverseTransformPoint(legFeet[side].position);footRotations[key,side]=Quaternion.Inverse(root.rotation)*legFeet[side].rotation;}
                positions[key]=new Vector3[bones.Length];rotations[key]=new Quaternion[bones.Length];
                for(int i=0;i<bones.Length;i++){positions[key][i]=bones[i].localPosition;rotations[key][i]=bones[i].localRotation;}
            }
            float segment=time<.36f?time/.36f:time<.68f?1f+(time-.36f)/.32f:2f+(time-.68f)/.32f;
            int a=Mathf.Min(2,Mathf.FloorToInt(segment));float blend=Mathf.SmoothStep(0,1,segment-a);
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(positions[a][i],positions[a+1][i],blend);bones[i].localRotation=Quaternion.Slerp(rotations[a][i],rotations[a+1][i],blend);}
            if(time<=0f)return;
            // Re-solve the feet after blending the torso: FK interpolation alone lifts
            // the boots between planted keys and makes the rising soldier look suspended.
            for(int side=0;side<2;side++)
            {
                Vector3 target=Vector3.Lerp(feet[a,side],feet[a+1,side],blend);
                target.y=Mathf.Lerp(.075f,baseFootRootPositions[side].y,Mathf.Clamp01(time/.28f));
                Quaternion rotation=root.rotation*Quaternion.Slerp(footRotations[a,side],footRotations[a+1,side],blend);
                Vector3 worldTarget=root.TransformPoint(target);
                Vector3 pole=root.TransformDirection(kneePoles[side]);
                if(side!=variant%2&&time<.75f)
                {
                    float kneel=1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.6f,.75f,time));
                    float y=Mathf.Lerp(legCalves[side].position.y,root.TransformPoint(Vector3.up*.085f).y,kneel);
                    pole=GroundPole(legThighs[side].position,worldTarget,upperLegLengths[side],lowerLegLengths[side],y,root.TransformDirection(right*(side==0?-1:1)));
                }
                SolveLeg(legThighs[side],legCalves[side],legFeet[side],worldTarget,upperLegLengths[side],lowerLegLengths[side],pole);
                legFeet[side].rotation=rotation;
            }
        }

        Transform Bone(string name) => indices.TryGetValue(name,out int index)?bones[index]:null;
        void Rotate(string name,Vector3 anatomicalRootAxis,float degrees)
        {
            Transform bone=Bone(name);if(!bone||Mathf.Abs(degrees)<.00001f)return;
            Vector3 worldAxis=root.TransformDirection(anatomicalRootAxis).normalized;
            Vector3 parentAxis=bone.parent?bone.parent.InverseTransformDirection(worldAxis).normalized:worldAxis;
            bone.localRotation=Quaternion.AngleAxis(degrees,parentAxis)*bone.localRotation;
        }
        void OffsetPelvis(Vector3 offsetMetersInRootAxes)
        {
            Transform pelvis=Bone("pelvis");
            // TransformDirection ignores unit conversion scales; the parent inverse converts meters back to FBX units.
            Vector3 worldOffset=root.TransformDirection(offsetMetersInRootAxes);
            pelvis.localPosition+=pelvis.parent?pelvis.parent.InverseTransformVector(worldOffset):worldOffset;
        }

        public bool ValidatePose(out string issue,float maximumRootDistanceMeters=3f)
        {
            for(int i=0;i<bones.Length;i++)
            {
                Transform bone=bones[i];Quaternion rotation=bone.localRotation;
                if(!Finite(bone.position)||!Finite(bone.localPosition)||!Finite(bone.localScale)
                    ||!Finite(rotation.x)||!Finite(rotation.y)||!Finite(rotation.z)||!Finite(rotation.w))
                {issue="Non-finite infantry pose at "+bone.name;return false;}
                if(Vector3.Distance(root.position,bone.position)>maximumRootDistanceMeters)
                {issue="Infantry bone exceeds pose bounds: "+bone.name;return false;}
                if((bone.localScale-restScales[i]).sqrMagnitude>.000001f)
                {issue="Infantry motion changed a bone scale: "+bone.name;return false;}
            }
            issue=null;return true;
        }
        static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
