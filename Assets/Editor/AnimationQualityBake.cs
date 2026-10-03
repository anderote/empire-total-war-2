using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EmpireBattle;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;

public static partial class InfantryAssetBuild
{
    // Refresh common locomotion and retreat; preserve all existing atlas coordinates
    // and GUIDs. No player build or playtest publication is involved.
    public static void BakeAnimationQuality()
    {
        prepared=true;const string output="Assets/Resources/BakedInfantry";
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        library.hideFlags=HideFlags.DontUnloadUnusedAsset;var report=new StringBuilder();
        try
        {
            for(int level=0;level<2;level++)
            {
                BuildScene(false,level==0?3:5);
                var inspection=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
                for(int side=0;side<2;side++)
                {
                    var rig=inspection.Soldiers[side];var lod=(side==0?library.French:library.British).Lods[level];
                    var rects=new Dictionary<string,Rect>();
                    foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)rects[m.name]=new Rect(0,0,1,1);
                    Mesh[] Refresh(Mesh[] old,InfantryMotionKind kind,bool loop,int variant=-1)
                    {
                        var uv=old.Select(m=>m.uv).ToArray();
                        var poses=BakePoses(rig,inspection.Clips[0],old.Length,rects,output,level,report,kind,false,variant);
                        for(int i=0;i<poses.Length;i++)
                        {
                            var mesh=poses[i];var next=poses[i+1<poses.Length?i+1:loop?0:i];
                            if(mesh.vertexCount!=uv[i].Length)throw new Exception("Animation topology changed: "+mesh.name);
                            mesh.uv=uv[i];mesh.SetUVs(2,new List<Vector3>(next.vertices));mesh.SetUVs(3,new List<Vector3>(next.normals));
                            var bounds=mesh.bounds;bounds.Encapsulate(next.bounds);mesh.bounds=bounds;EditorUtility.SetDirty(mesh);
                        }
                        return poses;
                    }
                    var clip=lod.Melee.First(c=>c.Action==MeleeAction.Retreat);
                    clip.Poses=Refresh(clip.Poses,InfantryMotionKind.Retreat,false);
                    lod.March=Refresh(lod.March,InfantryMotionKind.March,true);
                    lod.Run=Refresh(lod.Run,InfantryMotionKind.Run,true);lod.Charge=Refresh(lod.Charge,InfantryMotionKind.Charge,true);
                    for(int v=0;v<lod.Locomotion.Length;v++)
                    {
                        var gait=lod.Locomotion[v];gait.Walk=Refresh(gait.Walk,InfantryMotionKind.Walk,true,v);
                        gait.March=Refresh(gait.March,InfantryMotionKind.March,true,v);gait.Jog=Refresh(gait.Jog,InfantryMotionKind.Jog,true,v);
                        gait.Run=Refresh(gait.Run,InfantryMotionKind.Run,true,v);gait.Charge=Refresh(gait.Charge,InfantryMotionKind.Charge,true,v);
                    }
                    var cloth=rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.enabled&&r.name.Contains(side==0?"french_infantry":"british_infantry")).sharedMaterial;
                    var metal=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Authored ramrod").sharedMaterial;
                    var wood=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod")).sharedMaterial;
                    var kinds=new[]{InfantryMotionKind.Officer,InfantryMotionKind.Drummer,InfantryMotionKind.Standard,InfantryMotionKind.Sergeant};
                    for(int role=0;role<kinds.Length;role++)
                    {
                        inspection.Clips[0].SampleAnimation(rig,0);
                        var equipment=InfantryCommandAuthoring.Create(rig,kinds[role],cloth,metal,wood,side,true);
                        try
                        {
                            foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)rects[m.name]=new Rect(0,0,1,1);
                            lod.Command[role].March=Refresh(lod.Command[role].March,kinds[role],true,1);
                        }
                        finally{InfantryCommandAuthoring.Destroy(equipment,rig);}
                    }
                }
            }
            BuildScene(false,3);
            EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Artifacts/AnimationQuality");File.WriteAllText("Artifacts/AnimationQuality/locomotion-bake.txt",report.ToString());
            AssetWorkbenchBuild.Prepare();
            Debug.Log("ANIMATION_QUALITY_BAKED grounded locomotion, four command roles and retreat, both factions and LODs; workbench posture tracks refreshed");
        }
        finally{library.hideFlags=HideFlags.None;}
    }
}
