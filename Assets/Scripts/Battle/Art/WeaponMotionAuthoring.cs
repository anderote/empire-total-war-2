using UnityEngine;
namespace EmpireBattle.Art
{
    public sealed partial class ClosedGripAuthoring
    {
        // The release pose is shared with GrenadeSystem's ballistic origin after the bake's 180-degree rotation.
        public void ApplyGrenade(float phase,Transform grenade)
        {
            MaxContactError=0;MaxWristAngle=0;firingPose=false;
            float time=phase*GrenadeSystem.ThrowDuration;
            var ready=new Vector3(-.28f,1.15f,-.12f);
            var cocked=new Vector3(-.32f,1.73f,.13f);
            var release=new Vector3(-.22f,1.72f,-.52f);
            var follow=new Vector3(-.18f,1.22f,-.50f);
            Vector3 hand=time<.32f?Vector3.Lerp(ready,cocked,Mathf.SmoothStep(0,1,time/.32f)):
                time<GrenadeSystem.ReleaseDelay?Vector3.Lerp(cocked,release,Mathf.SmoothStep(0,1,(time-.32f)/(GrenadeSystem.ReleaseDelay-.32f))):
                Vector3.Lerp(release,follow,Mathf.SmoothStep(0,1,(time-GrenadeSystem.ReleaseDelay)/(.85f-GrenadeSystem.ReleaseDelay)));
            Grasp("r",root.TransformPoint(hand),root.right,-root.forward);
            Grasp("l",root.TransformPoint(new Vector3(.24f,1.12f,-.30f)),root.right,root.up);
            // Park the musket vertically beside the left hand, clear of the throwing arm.
            rifle.rotation=root.rotation*Quaternion.LookRotation(Vector3.up,Vector3.forward);
            rifle.position=leftMesh.position-rifle.TransformVector(new Vector3(0,0,-25));
            if(grenade){grenade.position=root.TransformPoint(hand);grenade.GetComponent<Renderer>().enabled=time<GrenadeSystem.ReleaseDelay;}
        }
        public void ApplyLance(Transform lance,float phase,bool thrust)
        {
            MaxContactError=0;MaxWristAngle=0;firingPose=false;
            float drive=thrust?Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI):0;
            Vector3 direction=Vector3.Slerp(Vector3.up,new Vector3(0,.10f,-1),thrust?1:.18f).normalized;
            Grasp("r",root.TransformPoint(new Vector3(-.30f,1.78f,-.21f-drive*.22f)),root.right,root.TransformDirection(direction));
            Grasp("l",root.TransformPoint(new Vector3(.15f,1.70f,-.40f)),root.right,-root.forward);
            lance.rotation=Quaternion.FromToRotation(Vector3.up,root.TransformDirection(direction));
            lance.position=root.TransformPoint(new Vector3(-.30f,1.78f,-.21f-drive*.22f))-lance.up*.75f;
        }
        public void ApplyMountedCarry(Transform firearm,bool pistol)
        {
            MaxContactError=0;MaxWristAngle=0;firingPose=false;
            var right=new Vector3(-.22f,1.90f,-.28f);
            firearm.rotation=root.rotation*Quaternion.LookRotation(new Vector3(.05f,.55f,-.8f),Vector3.up);
            firearm.position=root.TransformPoint(right)+firearm.up*.1f;
            Grasp("r",root.TransformPoint(right),root.right,firearm.forward);
            Grasp("l",pistol?root.TransformPoint(new Vector3(.15f,1.70f,-.40f)):firearm.TransformPoint(new Vector3(0,.025f,.22f)),root.right,firearm.forward);
        }
        public void ApplyMountedFirearm(Transform firearm,float phase,bool reload,bool pistol)
        {
            MaxContactError=0;MaxWristAngle=0;firingPose=false;
            var aimHand=new Vector3(-.12f,2.02f,-.30f);
            var aimRotation=root.rotation*Quaternion.LookRotation(-Vector3.forward,Vector3.up);
            if(!reload)
            {
                var right=aimHand+Vector3.forward*Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI)*.045f;
                Grasp("r",root.TransformPoint(right),root.right,-root.forward);
                firearm.rotation=aimRotation;firearm.position=root.TransformPoint(right)+firearm.up*.1f;
                Grasp("l",pistol?root.TransformPoint(new Vector3(.15f,1.70f,-.40f)):firearm.TransformPoint(new Vector3(0,.025f,.19f)),root.right,-root.forward);
                StowMountedRod(firearm,pistol);return;
            }
            // Lower from the final fire pose, pass the gun into the left hand, load,
            // then raise back to aim. The first and last poses match the fire track.
            float lower=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.10f,phase));
            float work=Mathf.Clamp01((phase-.10f)/.90f);
            float raise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.85f,1,phase));
            Quaternion loadingRotation=root.rotation*Quaternion.LookRotation(new Vector3(.08f,1,-.20f),Vector3.up);
            Vector3 aimPosition=root.TransformPoint(aimHand)+(aimRotation*Vector3.up)*.1f;
            Vector3 loadingPosition=root.TransformPoint(new Vector3(.10f,pistol?1.72f:1.51f,pistol?-.36f:-.24f));
            firearm.rotation=Quaternion.Slerp(Quaternion.Slerp(aimRotation,loadingRotation,lower),aimRotation,raise);
            firearm.position=Vector3.Lerp(Vector3.Lerp(aimPosition,loadingPosition,lower),aimPosition,raise);
            var pouch=new Vector3(-.28f,1.46f,-.10f);var mouth=new Vector3(-.06f,2.24f,-.26f);
            var sockets=firearm.GetComponent<AuthoredMountedFirearm>();
            Vector3 pan=root.InverseTransformPoint(firearm.TransformPoint(sockets?sockets.Pan:new Vector3(.04f,.015f,.06f)));
            Vector3 muzzle=root.InverseTransformPoint(firearm.TransformPoint(sockets?sockets.Muzzle:new Vector3(0,.025f,pistol?.29f:.765f)));
            Vector3 hand;
            if(work<.15f)hand=Vector3.Lerp(pouch,mouth,Mathf.SmoothStep(0,1,work/.15f));
            else if(work<.30f)hand=Vector3.Lerp(mouth,pan,Mathf.SmoothStep(0,1,(work-.15f)/.15f));
            else if(work<.45f)hand=Vector3.Lerp(pan,muzzle,Mathf.SmoothStep(0,1,(work-.30f)/.15f));
            else if(work<.78f){float stroke=Mathf.Sin((work-.45f)/.33f*Mathf.PI*4);hand=muzzle+root.InverseTransformDirection(firearm.forward)*(.07f+.07f*stroke);}
            else hand=Vector3.Lerp(muzzle,pouch,Mathf.SmoothStep(0,1,(work-.78f)/.22f));
            hand=Vector3.Lerp(Vector3.Lerp(aimHand,hand,lower),aimHand,raise);
            Grasp("r",root.TransformPoint(hand),root.right,Vector3.Slerp(-root.forward,root.up,lower*(1-raise)));
            Vector3 aimLeft=pistol?root.TransformPoint(new Vector3(.15f,1.70f,-.40f)):firearm.TransformPoint(new Vector3(0,.025f,.19f));
            Vector3 loadingLeft=firearm.TransformPoint(sockets?sockets.LoadingGrip:new Vector3(0,0,pistol?.06f:.22f));
            Grasp("l",Vector3.Lerp(aimLeft,loadingLeft,lower*(1-raise)),root.right,firearm.forward);
            var rod=firearm.Find("Loading ramrod");
            if(rod&&work>.45f&&work<.78f)
            {rod.rotation=firearm.rotation*Quaternion.Euler(90,0,0);rod.position=root.TransformPoint(hand)-firearm.forward*rod.localScale.y*.9f;}
            else StowMountedRod(firearm,pistol);
        }
        void StowMountedRod(Transform firearm,bool pistol)
        {
            var rod=firearm.Find("Loading ramrod");if(!rod)return;
            var sockets=firearm.GetComponent<AuthoredMountedFirearm>();
            rod.localRotation=Quaternion.Euler(90,0,0);rod.localPosition=sockets?sockets.RodStow:new Vector3(0,-.025f,pistol?.10f:.40f);
        }
    }
}
