using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;
public static partial class InfantryAssetBuild
{
    public static void BakeWorkbenchDeaths()
    {
        prepared=true;var report=new StringBuilder();
        var common=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");common.hideFlags=HideFlags.DontUnloadUnusedAsset;
        for(int level=0;level<2;level++)
        {
            BuildScene(false,level==0?3:5);
            var inspection=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
            for(int side=0;side<2;side++)
                BakeReviewDeaths(inspection.Soldiers[side],inspection.Clips[0],(side==0?common.French:common.British).Lods[level],"Assets/Resources/BakedInfantry",level,report);
        }
        common.hideFlags=HideFlags.None;EditorUtility.SetDirty(common);AssetDatabase.SaveAssets();
        foreach(string faction in new[]{"ming","india"})foreach(string family in new[]{"line","light","sword"})
        {
            string output="Assets/Resources/RegionalRoster/"+faction+"-"+family;
            var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(output+"/Library.asset");asset.hideFlags=HideFlags.DontUnloadUnusedAsset;
            for(int level=0;level<2;level++)
            {
                BuildScene(false,level==0?4:5);var inspection=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
                var rig=inspection.Soldiers[0];rig.transform.position=Vector3.zero;rig.name=faction+"_"+family;
                PrepareRegionalRig(rig,faction,family,level);
                if(family=="sword")
                {
                    var sword=new GameObject("Roster sword");sword.transform.SetParent(rig.transform,false);
                    var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var source=cube.GetComponent<MeshFilter>().sharedMesh;
                    var mesh=new Mesh {name="Prototype sword"};mesh.CombineMeshes(new[]{
                        new CombineInstance {mesh=source,transform=Matrix4x4.TRS(new Vector3(0,.47f,0),Quaternion.identity,new Vector3(.035f,.82f,.012f))},
                        new CombineInstance {mesh=source,transform=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(.025f,.16f,.025f))},
                        new CombineInstance {mesh=source,transform=Matrix4x4.TRS(new Vector3(0,.07f,0),Quaternion.identity,new Vector3(.18f,.025f,.035f))}});
                    UnityEngine.Object.DestroyImmediate(cube);sword.AddComponent<MeshFilter>().sharedMesh=mesh;
                    sword.AddComponent<MeshRenderer>().sharedMaterial=MaterialFor("Ramrod",0);
                }
                rosterSword=family=="sword";
                try{BakeReviewDeaths(rig,inspection.Clips[0],asset.Set.Lods[level],output,level,report);}finally{rosterSword=false;}
            }
            asset.hideFlags=HideFlags.None;EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
        }
        AssetDatabase.SaveAssets();File.WriteAllText("Artifacts/Workbench/death-bake.txt",report.ToString());
        InfantryPoseBlendBuild.Prepare();
        AssetWorkbenchBuild.Prepare();
        Debug.Log("WORKBENCH_DEATHS_BAKED: ten distinct impact/stagger/side/knee falls for common, Ming and Indian infantry, two LODs");
    }
    static void BakeReviewDeaths(GameObject rig,AnimationClip clip,InfantryCrowdLod lod,string output,int level,StringBuilder report)
    {
        var rects=new Dictionary<string,Rect>();
        foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)rects[m.name]=new Rect(0,0,1,1);
        lod.DeathVariants=new InfantryDeathVariant[10];
        for(int variant=0;variant<10;variant++)
        {
            var poses=BakePoses(rig,clip,16,rects,output,level,report,InfantryMotionKind.Death,false,variant);
            foreach(var mesh in poses)
            {
                if(mesh.vertexCount!=lod.Idle[0].vertexCount)throw new Exception("Death topology changed: "+rig.name);
                mesh.uv=lod.Idle[0].uv;EditorUtility.SetDirty(mesh);
                foreach(var v in mesh.vertices)if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z))throw new Exception("Invalid death vertex");
                if(mesh.bounds.size.y>3.5f)throw new Exception("Unbounded death pose: "+rig.name);
            }
            lod.DeathVariants[variant]=new InfantryDeathVariant{Poses=poses};
        }
        lod.Death=lod.DeathVariants[0].Poses;
        PrepareRosterBlends(lod);
        for(int a=0;a<10;a++)for(int b=a+1;b<10;b++)
        {
            var va=lod.DeathVariants[a].Poses[8].vertices;var vb=lod.DeathVariants[b].Poses[8].vertices;
            float delta=0;for(int i=0;i<va.Length;i++)delta+=Vector3.Distance(va[i],vb[i]);
            if(delta/va.Length<.01f)throw new Exception("Duplicate death variants");
        }
    }
}
