using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static partial class InfantryAssetBuild
{
    static void BakeBayonetRosterOrders(List<BayonetWeaponTemplate> weapons,List<BayonetOrderClip> orders,StringBuilder report)
    {
        cartridgeBake=false;
        for(int faction=0;faction<11;faction++)foreach(string family in new[]{"line","light"})
        {
            bool regional=faction>=9;
            string id=regional?(faction==9?"ming":"india"):faction==8?"generic":UnitRoster.Factions[faction];
            if(!regional&&family=="line"&&faction<2)continue;
            string path=(regional?"Assets/Resources/RegionalRoster/":"Assets/Generated/UnitRoster/")+id+"-"+family+"/Library.asset";
            var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(path);if(!asset||asset.BakeVersion<=0)continue;
            asset.hideFlags=HideFlags.DontUnloadUnusedAsset;
            try
            {
                for(int level=0;level<asset.Set.Lods.Length;level++)
                {
                    int lod=level==0?4:5;BuildScene(false,lod);var inspect=UnityEngine.Object.FindAnyObjectByType<InfantryInspection>();
                    var rig=inspect.Soldiers[0];rig.transform.position=Vector3.zero;rig.name=id+"_"+family;
                    if(regional)PrepareRegionalRig(rig,id,family,level);
                    else
                    {
                        foreach(var child in rig.transform.Cast<Transform>().ToArray())if(child.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0)UnityEngine.Object.DestroyImmediate(child.gameObject);
                        var bones=rig.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
                        var recipe=RosterParts[faction==8?0:faction];var parts=new[]{recipe[family=="light"?1:0],recipe[2],recipe[3],recipe[4]};
                        if(family=="light")
                        {
                            if(faction==0){parts[1]="gap_infantry_pants_french_voltigeur.fbx";parts[2]="gap_french_shako_voltigeur.FBX";}
                            if(faction==1){parts[1]="gap_british_rifleman_pants.FBX";parts[2]="gap_british_stovepipe_rifle.FBX";parts[3]="gap_british_equipment_rifle.FBX";}
                            if(faction==2){parts[1]="gap_infantry_pants_austrian_jaeger.FBX";parts[2]="gap_austrian_jaeger_hat.FBX";parts[3]="gap_austrian_equipment_light.FBX";}
                            if(faction==3){parts[1]="gap_infantry_pants_prussian_jaeger.fbx";parts[3]="gap_prussian_equipment_light.FBX";}
                            if(faction==4){parts[1]="gap_russian_jaeger_pants.FBX";parts[2]="gap_russian_shako_jaeger.FBX";parts[3]="gap_infantry_equipment_russian_rifle.FBX";}
                            if(faction==5)parts[2]="gap_italian_shako_voltigeur.FBX";
                        }
                        foreach(string part in parts)AttachRosterPart(rig,bones,part,lod,report);
                    }
                    var gun=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
                    if(level==0&&faction==0&&family=="light")weapons.Add(Template(gun.GetComponent<MeshFilter>().sharedMesh,rig.transform.InverseTransformVector(gun.transform.TransformVector(Vector3.right)).magnitude));
                    var maps=new Dictionary<string,Rect>();foreach(var renderer in rig.GetComponentsInChildren<Renderer>())if(renderer.enabled)foreach(var material in renderer.sharedMaterials)maps[material.name]=new Rect(0,0,1,1);
                    string target="Assets/Resources/Bayonets/Roster/"+id+"-"+family;Directory.CreateDirectory(target);
                    var order=new BayonetOrderClip{Name=id+"-"+family+"-L"+level,Uniform=asset.Set.Material,Lod=level};orders.Add(order);
                    Directory.CreateDirectory(target+"/Unfix");
                    for(int action=0;action<6;action++)
                    {
                        bayonetOrderBake=true;bayonetUnfixBake=action>=4;bayonetOrderKneeling=action==1;bayonetTransferPoses.Clear();
                        var kind=(action==2||action==4)?(EmpireBattle.AnimationAuthoring.InfantryMotionKind?)EmpireBattle.AnimationAuthoring.InfantryMotionKind.Walk:(action==3||action==5)?EmpireBattle.AnimationAuthoring.InfantryMotionKind.Run:null;
                        var poses=BakePoses(rig,inspect.Clips[0],action>=2?32:20,maps,action>=4?target+"/Unfix":target,level,report,kind,action==1);
                        var reference=asset.Set.Lods[level].Idle[0];
                        foreach(var pose in poses){if(pose.vertexCount!=reference.vertexCount)throw new Exception("Bayonet roster topology mismatch: "+pose.name);pose.uv=reference.uv;EditorUtility.SetDirty(pose);}
                        PrepareBayonetMorph(poses);
                        if(action==0)order.Standing=poses;else if(action==1)order.Kneeling=poses;else if(action==2)order.March=poses;else if(action==3)order.Run=poses;else if(action==4)order.UnfixMarch=poses;else order.UnfixRun=poses;
                        SaveTransfer(order,action,poses,target);
                    }
                    bayonetOrderBake=false;
                }
            }
            finally{asset.hideFlags=HideFlags.None;}
        }
    }
}
