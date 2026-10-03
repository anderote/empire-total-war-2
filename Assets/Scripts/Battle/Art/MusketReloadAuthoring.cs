using System;
using System.Collections.Generic;
using UnityEngine;
namespace EmpireBattle.Art
{
    // Offline authoring only. Coordinates here match the source rig: facing -Z, feet at Y=0.
    // The rifle has its own root-space support; wrist animation cannot invert it.
    public sealed class MusketReloadAuthoring
    {
        readonly Transform root, rifle, rod;
        readonly bool kneeling, cartridge;
        readonly Quaternion reloadTilt;
        readonly AnatomicalArmIK armIK; readonly Vector3 muzzleLocal;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Vector3[] positions, scales;
        readonly Quaternion[] rotations;
        readonly Transform[] rest;
        readonly Vector3 riflePosition, rifleScale;
        readonly Quaternion rifleRotation;
        readonly Vector3 leftRest, rightRest;
        readonly Quaternion leftRotation, rightRotation;
        readonly Vector3 butt = new Vector3(.04f, .035f, -.35f);
        readonly float minZ, maxZ, weaponLength;
        public Vector3 ButtPosition { get; private set; }
        public Vector3 MuzzlePosition { get; private set; }
        public Vector3 RodTop { get; private set; }
        public float RightHandError { get; private set; }
        public float LeftHandError { get; private set; }

        public MusketReloadAuthoring(GameObject soldier, Transform weapon, Transform ramrod,bool kneeling=false,float kneeTilt=25f,float kneeButtZ=-.75f,bool cartridge=false)
        {
            this.cartridge=cartridge;this.kneeling=kneeling; reloadTilt=Quaternion.Euler(kneeling?kneeTilt:0,0,0);if(kneeling)butt=new Vector3(.12f,.035f,kneeButtZ);
            root = soldier.transform; rifle = weapon; rod = ramrod; armIK=new AnatomicalArmIK(soldier,kneeling?.43f:0);
            rifle.SetParent(root, true);
            var selected = new List<Transform>();
            foreach (var t in soldier.GetComponentsInChildren<Transform>())
                if (!bones.ContainsKey(t.name)) { bones.Add(t.name, t); selected.Add(t); }
            rest = selected.ToArray(); positions = new Vector3[rest.Length];
            scales = new Vector3[rest.Length]; rotations = new Quaternion[rest.Length];
            for (int i = 0; i < rest.Length; i++) { positions[i] = rest[i].localPosition; scales[i] = rest[i].localScale; rotations[i] = rest[i].localRotation; }
            riflePosition = root.InverseTransformPoint(rifle.position);
            rifleRotation = Quaternion.Inverse(root.rotation) * rifle.rotation;
            rifleScale = rifle.lossyScale;
            var bounds = rifle.GetComponent<MeshFilter>().sharedMesh.bounds;
            minZ = bounds.min.z; maxZ = bounds.max.z;
            Vector3 sum=Vector3.zero;int points=0;foreach(var vertex in rifle.GetComponent<MeshFilter>().sharedMesh.vertices)if(vertex.z>maxZ-.35f){sum+=vertex;points++;}muzzleLocal=points>0?new Vector3(sum.x/points,sum.y/points,maxZ):new Vector3(0,0,maxZ);
            weaponLength = (maxZ - minZ) * rifleScale.z;
            leftRest = root.InverseTransformPoint(bones["l_hand"].position);
            rightRest = root.InverseTransformPoint(bones["r_hand"].position);
            leftRotation = bones["l_hand"].rotation; rightRotation = bones["r_hand"].rotation;
        }

        void SampleCartridge(float phase)
        {
            // Keep the rifle across the chest. Open the bolt, insert cartridges at the
            // receiver and close the bolt; no muzzle loading or ramrod strokes.
            float action=Mathf.Sin(phase*Mathf.PI);
            rifle.rotation=root.rotation*Quaternion.Slerp(rifleRotation,Quaternion.LookRotation(new Vector3(.15f,.20f,-1),Vector3.up),action*.55f);
            rifle.position=root.TransformPoint(riflePosition+new Vector3(0,-.08f,0)*action);
            Vector3 left=rifle.TransformPoint(new Vector3(0,1.8f,-18));
            Vector3 receiver=rifle.TransformPoint(new Vector3(-1.8f,1,-25));
            Vector3 pouch=root.TransformPoint(new Vector3(-.43f,1.10f-(kneeling?.43f:0),-.25f));
            Vector3 right=receiver;
            if(phase<.2f)right=receiver-rifle.forward*(.075f*Mathf.Sin(phase/.2f*Mathf.PI));
            else if(phase<.45f)right=Vector3.Lerp(receiver,pouch,Mathf.SmoothStep(0,1,(phase-.2f)/.25f));
            else if(phase<.7f)right=Vector3.Lerp(pouch,receiver,Mathf.SmoothStep(0,1,(phase-.45f)/.25f));
            else right=receiver+rifle.forward*(.05f*Mathf.Sin((phase-.7f)/.3f*Mathf.PI));
            float recover=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.85f,1,phase));
            left=Vector3.Lerp(left,root.TransformPoint(leftRest),recover);right=Vector3.Lerp(right,root.TransformPoint(rightRest),recover);
            LeftHandError=SolveArm("l",left,root.TransformPoint(new Vector3(.55f,1.02f,-.12f)));
            RightHandError=SolveArm("r",right,root.TransformPoint(new Vector3(-.45f,1.14f,-.12f)));
            bones["l_hand"].rotation=leftRotation;bones["r_hand"].rotation=rightRotation;
            StowRod(rifle,rod);rod.GetComponent<Renderer>().enabled=false;
            ButtPosition=root.InverseTransformPoint(rifle.TransformPoint(new Vector3(0,0,minZ)));
            MuzzlePosition=root.InverseTransformPoint(rifle.TransformPoint(muzzleLocal));
        }
        public void Sample(float phase)
        {
            phase = Mathf.Clamp01(phase);
            for (int i = 0; i < rest.Length; i++) { rest[i].localPosition = positions[i]; rest[i].localRotation = rotations[i]; rest[i].localScale = scales[i]; }
            if(cartridge){SampleCartridge(phase);return;}
            var plantedRotation = reloadTilt * Quaternion.LookRotation(Vector3.up, Vector3.forward);
            Vector3 plantedPosition = butt - plantedRotation * Vector3.Scale(new Vector3(0, 0, minZ), rifleScale);
            float plant = Smooth(phase / .15f), shoulder = Smooth((phase - .88f) / .12f);
            Quaternion rotation = Quaternion.Slerp(rifleRotation, plantedRotation, plant);
            Vector3 position = Vector3.Lerp(riflePosition, plantedPosition, plant);
            rotation = Quaternion.Slerp(rotation, rifleRotation, shoulder);
            position = Vector3.Lerp(position, riflePosition, shoulder);
            rifle.rotation = root.rotation * rotation;
            rifle.position = root.TransformPoint(position);
            ButtPosition = root.InverseTransformPoint(rifle.TransformPoint(new Vector3(0, 0, minZ)));
            MuzzlePosition = root.InverseTransformPoint(rifle.TransformPoint(muzzleLocal));

            Vector3 plantedMuzzle=butt+Quaternion.Inverse(reloadTilt)*(plantedPosition+plantedRotation*Vector3.Scale(muzzleLocal,rifleScale)-butt);float boreX=plantedMuzzle.x,boreZ=plantedMuzzle.z;
            Vector3 left = new Vector3(boreX+.075f, 1.18f, boreZ+.085f);
            Vector3 right;
            float muzzle = plantedMuzzle.y;
            // Cartridge to mouth, then its contents into the muzzle.
            if (phase < .22f) right = Vector3.Lerp(rightRest, new Vector3(-.04f, 1.49f-(kneeling?.43f:0), -.20f), Smooth((phase - .08f) / .14f));
            else if (phase < .35f) right = Vector3.Lerp(new Vector3(-.04f, 1.49f-(kneeling?.43f:0), -.20f), TiltPoint(new Vector3(boreX - .035f, muzzle - .02f, boreZ)), Smooth((phase - .22f) / .13f));
            else if (phase < .48f) right = TiltPoint(new Vector3(boreX - .025f, muzzle + .15f * Smooth((phase - .35f) / .13f), boreZ));
            else if (phase < .75f)
            {
                float stroke = (phase - .48f) / .27f;
                // Two complete down/up compressions; hand remains on the exposed rod.
                float height = .08f + .25f * (.5f + .5f * Mathf.Cos(stroke * Mathf.PI * 4));
                right = TiltPoint(new Vector3(boreX - .025f, muzzle + height - .17f, boreZ));
            }
            else right = Vector3.Lerp(TiltPoint(new Vector3(boreX - .025f, muzzle + .16f, boreZ)), new Vector3(-.14f, .99f-(kneeling?.43f:0), -.34f), Smooth((phase - .75f) / .13f));
            left = Vector3.Lerp(leftRest, TiltPoint(left), plant);
            left = Vector3.Lerp(left, leftRest, shoulder); right = Vector3.Lerp(right, rightRest, shoulder);
            LeftHandError = SolveArm("l", root.TransformPoint(left), root.TransformPoint(new Vector3(.55f, 1.02f, -.12f)));
            RightHandError = SolveArm("r", root.TransformPoint(right), root.TransformPoint(new Vector3(-.45f, 1.14f, -.12f)));
            // Preserve wrist orientation rather than letting a twisting forearm rotate the gun.
            bones["l_hand"].rotation = leftRotation; bones["r_hand"].rotation = rightRotation;

            if (phase < .35f || phase >= .88f) StowRod(rifle, rod);
            else
            {
                float top;
                float stowedTop = root.InverseTransformPoint(rifle.TransformPoint(new Vector3(.9f, .3f, -3f))).y + .475f;
                if (phase < .48f) top = Mathf.Lerp(stowedTop, muzzle + .95f, Smooth((phase - .35f) / .13f));
                else if (phase < .75f) top = muzzle + .08f + .25f * (.5f + .5f * Mathf.Cos((phase - .48f) / .27f * Mathf.PI * 4));
                else top = Mathf.Lerp(muzzle + .33f, stowedTop, Smooth((phase - .75f) / .13f));
                float boreAlignment=Smooth((phase-.43f)/.05f)*(1-Smooth((phase-.75f)/.13f));float rodX=Mathf.Lerp(boreX-.025f,boreX,boreAlignment);
                rod.position = root.TransformPoint(TiltPoint(new Vector3(rodX, top - .475f, boreZ)));
                rod.rotation = root.rotation * reloadTilt; rod.localScale = new Vector3(.006f, .95f, .006f);
                RodTop = new Vector3(rodX, top, boreZ);
            }
        }

        Vector3 TiltPoint(Vector3 point) => butt + reloadTilt * (point-butt);

        float SolveArm(string side, Vector3 target, Vector3 pole)
        {
            return armIK.Solve(side,target,Vector3.zero);
        }

        public bool Validate(float phase, out string error)
        {
            if (!cartridge && phase >= .15f && phase <= .88f)
            {
                if (Mathf.Abs(ButtPosition.y - .035f) > .003f || MuzzlePosition.y < (kneeling?1f:1.4f) || Vector3.Dot(rifle.forward, root.TransformDirection(reloadTilt*Vector3.up)) < .999f)
                { error = $"Unsupported planted rifle: butt {ButtPosition}, muzzle {MuzzlePosition}"; return false; }
            }
            foreach (var bone in rest)
                if (float.IsNaN(bone.position.x) || float.IsNaN(bone.position.y) || float.IsNaN(bone.position.z) || float.IsInfinity(bone.position.x) || float.IsInfinity(bone.position.y) || float.IsInfinity(bone.position.z)) { error = "Nonfinite reload bone"; return false; }
            error = null; return true;
        }

        public static void StowRod(Transform rifle, Transform rod)
        {
            rod.position = rifle.TransformPoint(new Vector3(.9f, .3f, -3f));
            rod.rotation = Quaternion.FromToRotation(Vector3.up, rifle.forward);
            rod.localScale = new Vector3(.004f, .95f, .004f);
        }
        static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3 - 2 * value); }
    }
}
