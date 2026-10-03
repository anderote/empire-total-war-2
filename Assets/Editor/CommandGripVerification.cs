using System;
using EmpireBattle.AnimationAuthoring;
using EmpireBattle.Art;
using UnityEngine;
public static partial class InfantryAssetBuild
{
    public static void VerifyCommandGrips()
    {
        prepared=true;BuildScene(false,3);
        var inspection=UnityEngine.Object.FindAnyObjectByType<InfantryInspection>();
        foreach(var rig in inspection.Soldiers)foreach(bool kneeling in new[]{false,true})
        {
            inspection.Clips[0].SampleAnimation(rig,0);
            var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
            var rod=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");
            var material=gun.sharedMaterial;
            var equipment=InfantryCommandAuthoring.Create(rig,InfantryMotionKind.Sergeant,material,material,material,0);
            var motion=new ProceduralInfantryRig(rig);var grips=new ClosedGripAuthoring(rig,gun.transform,rod,kneeling?.43f:0);
            if(kneeling)equipment.transform.localPosition=Vector3.down*.43f;
            float worst=0;
            for(int frame=0;frame<=24;frame++)
            {
                float phase=frame/24f;motion.Sample(InfantryMotionKind.Sergeant,phase,3);
                if(kneeling)new ProceduralInfantryRig(rig).Kneel();
                InfantryCommandAuthoring.PikePose(equipment.transform,phase,true);grips.ApplyPike(equipment.transform,phase,true);
                worst=Mathf.Max(worst,grips.MaxContactError);
                if(grips.MaxContactError>.02f)throw new Exception($"Pike grip {rig.name} kneeling={kneeling} phase={phase:F3} error={grips.MaxContactError:F4}m");
            }
            InfantryCommandAuthoring.Destroy(equipment,rig);
            Debug.Log($"COMMAND_GRIP_VERIFIED {rig.name} kneeling={kneeling} maxError={worst:F4}m");
        }
    }
}
