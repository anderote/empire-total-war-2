using System.Collections.Generic;
using UnityEngine;
namespace EmpireBattle.Art {
 // Offline authoring: choose an elbow on the exact two-bone reach circle, outside the chest.
 public sealed class AnatomicalArmIK {
  readonly float chestDrop; readonly Transform root; readonly Dictionary<string,Transform> bones=new Dictionary<string,Transform>();
  readonly Dictionary<string,Vector3> previous=new Dictionary<string,Vector3>();
  public AnatomicalArmIK(GameObject rig,float chestDrop=0){this.chestDrop=chestDrop;root=rig.transform;foreach(var t in rig.GetComponentsInChildren<Transform>())if(!bones.ContainsKey(t.name))bones[t.name]=t;}
  public float Solve(string side,Vector3 target,Vector3 fingerAxis,float preferredElbowHeight=float.NaN){
   Vector3 localTarget=root.InverseTransformPoint(target)+Vector3.up*chestDrop;if(ChestRadius(localTarget)<1.035f){float radial=Mathf.Pow(localTarget.x/.205f,2)+Mathf.Pow((localTarget.y-1.14f)/.415f,2);localTarget.z=-.15f*Mathf.Sqrt(Mathf.Max(0,1.035f*1.035f-radial));target=root.TransformPoint(localTarget-Vector3.up*chestDrop);}
   var upper=bones[side+"_upperarm_twist"];var elbow=bones[side+"_foretwist"];var hand=bones[side+"_hand"];Vector3 shoulder=upper.position;
   float a=Vector3.Distance(shoulder,elbow.position),b=Vector3.Distance(elbow.position,hand.position),actualDistance=Vector3.Distance(target,shoulder);
   Vector3 direction=(target-shoulder).normalized;float d=Mathf.Clamp(actualDistance,Mathf.Abs(a-b)+.001f,a+b-.001f);Vector3 reachable=shoulder+direction*d;
   float along=(a*a+d*d-b*b)/(2*d),radius=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));Vector3 centre=shoulder+direction*along;
   float sign=side=="l"?1:-1;Vector3 pole=root.TransformPoint(new Vector3(sign*.52f,1.17f-chestDrop,-.08f));
   Vector3 u=Vector3.ProjectOnPlane(pole-centre,direction).normalized;if(u.sqrMagnitude<.01f)u=Vector3.ProjectOnPlane(root.right,direction).normalized;Vector3 v=Vector3.Cross(direction,u);
   Vector3 old=previous.TryGetValue(side,out var remembered)?remembered:elbow.position;Vector3 best=centre+u*radius;float cost=float.PositiveInfinity;
   for(int i=0;i<128;i++){
    float angle=i*Mathf.PI*2/128;Vector3 candidate=centre+radius*(u*Mathf.Cos(angle)+v*Mathf.Sin(angle));
    float clear=Clearance(root,shoulder,candidate,reachable,chestDrop);float lateral=sign*root.InverseTransformPoint(candidate).x;
    float wrist=fingerAxis.sqrMagnitude>.5f?Vector3.Angle(fingerAxis,reachable-candidate):0;
    float score=(candidate-pole).sqrMagnitude*.5f+(candidate-old).sqrMagnitude*2;
    if(!float.IsNaN(preferredElbowHeight))score+=Mathf.Pow(root.InverseTransformPoint(candidate).y-preferredElbowHeight,2)*12f;
    score+=Mathf.Pow(Mathf.Max(0,1.025f-clear),2)*10000+Mathf.Pow(Mathf.Max(0,.115f-lateral),2)*10000;
    score+=Mathf.Pow(Mathf.Max(0,wrist-52)/52,2)*2f;
    if(score<cost){cost=score;best=candidate;}
   }
   upper.rotation=Quaternion.FromToRotation(elbow.position-shoulder,best-shoulder)*upper.rotation;
   elbow.rotation=Quaternion.FromToRotation(hand.position-elbow.position,reachable-elbow.position)*elbow.rotation;
   previous[side]=elbow.position;return Vector3.Distance(hand.position,target);
  }
  // Padded chest ellipsoid includes a 3.5cm sleeve/forearm radius. Skip only the shoulder attachment.
  public static float Clearance(Transform root,Vector3 shoulder,Vector3 elbow,Vector3 hand,float chestDrop=0){float min=float.PositiveInfinity;for(int i=0;i<=20;i++){float t=i/20f;if(t>=.3f)min=Mathf.Min(min,ChestRadius(root.InverseTransformPoint(Vector3.Lerp(shoulder,elbow,t))+Vector3.up*chestDrop));min=Mathf.Min(min,ChestRadius(root.InverseTransformPoint(Vector3.Lerp(elbow,hand,t))+Vector3.up*chestDrop));}return min;}
  public static float ChestRadius(Vector3 p){return new Vector3(p.x/.205f,(p.y-1.14f)/.415f,p.z/.15f).magnitude;}
 }
}
