using System;
using System.IO;
using System.Linq;
using System.Text;
using EmpireBattle;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEngine;
public static partial class InfantryAssetBuild
{
    public static void VerifySupplementalGrips()
    {
        PrepareOutfitImports();var report=new StringBuilder();float worst=0;
        for(int level=0;level<2;level++)
        {
            var rig=OutfitRig(NapoleonicOutfits.All.First(o=>o.Id=="france-guard"),level,report);
            var gun=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));var rod=rig.transform.Find("Authored ramrod");var idle=OutfitClip("musket_idle");
            var grenade=GameObject.CreatePrimitive(PrimitiveType.Sphere);var standing=new ClosedGripAuthoring(rig,gun.transform,rod);
            for(int frame=0;frame<=22;frame++){idle.SampleAnimation(rig,0);standing.ApplyGrenade(frame/22f,grenade.transform);worst=Mathf.Max(worst,standing.MaxContactError);report.AppendLine($"Grenade L{level} frame{frame}: contact={standing.MaxContactError:F5} wrist={standing.MaxWristAngle:F2}");}
            var cloth=rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.enabled).sharedMaterial;
            InfantryCommandAuthoring.Create(rig,InfantryMotionKind.Colonel,cloth,MaterialFor("Ramrod",0),gun.sharedMaterial,0);
            var motion=new ProceduralInfantryRig(rig);var grip=new ClosedGripAuthoring(rig,gun.transform,rod,-.65f);
            var lance=WeaponProp(rig.transform,"Test lance",true);var carbine=WeaponProp(rig.transform,"Test firearm",false);var pistol=WeaponProp(rig.transform,"Test pistol",false,true);
            foreach(var r in pistol.GetComponentsInChildren<Renderer>())if(r.name!="Loading ramrod")UnityEngine.Object.DestroyImmediate(r.gameObject);SourceWeapon(pistol.transform,"new_flintlock_pistol.FBX",level,true);
            foreach(string name in new[]{"SwordLeft","SwordRight","LanceReady","LanceThrust","CarbineCarry","PistolCarry","CarbineFire","PistolFire","CarbineReload","PistolReload"})
            for(int frame=0;frame<32;frame++)
            {
                float phase=frame/31f;motion.SampleMounted(name=="SwordLeft"?MeleeAction.SlashLeft:name=="SwordRight"?MeleeAction.SlashRight:MeleeAction.None,phase,0);grip.ApplyMountedCombat(name=="SwordLeft"?MeleeAction.SlashLeft:name=="SwordRight"?MeleeAction.SlashRight:MeleeAction.None,phase);
                if(name.StartsWith("Lance"))grip.ApplyLance(lance.transform,phase,name=="LanceThrust");
                else if(name.EndsWith("Carry"))grip.ApplyMountedCarry((name.StartsWith("Pistol")?pistol:carbine).transform,name.StartsWith("Pistol"));
                else if(!name.StartsWith("Sword"))grip.ApplyMountedFirearm((name.StartsWith("Pistol")?pistol:carbine).transform,phase,name.EndsWith("Reload"),name.StartsWith("Pistol"));
                worst=Mathf.Max(worst,grip.MaxContactError);report.AppendLine($"{name} L{level} frame{frame}: contact={grip.MaxContactError:F5} wrist={grip.MaxWristAngle:F2}");
            }
        }
        File.WriteAllText("Artifacts/NapoleonicOutfits/grip-verification.txt",report.ToString());
        if(worst>.03f)throw new Exception("Supplemental weapon grip gap exceeds 3cm: "+worst);
        Debug.Log("NAPOLEONIC_GRIPS_VERIFIED maxContact="+worst);
    }
}
