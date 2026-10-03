using System;
using System.IO;
using System.Text;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only structural motion checks; visual acceptance remains a separate asset gate.</summary>
public static class InfantryMotionVerification
{
    public static void VerifyExpandedLibrary()
    {
        Verify();
        var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        if(!library)throw new Exception("Missing expanded crowd library.");
        int loops=0,deaths=0;
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        {
            if(lod.Locomotion==null||lod.Locomotion.Length!=3||lod.DeathVariants==null||lod.DeathVariants.Length!=10)
                throw new Exception("Expected three locomotion and ten death variants at every faction/LOD.");
            foreach(var v in lod.Locomotion)foreach(var poses in v.Loops()){CheckFrames(poses);loops++;}
            foreach(var death in lod.DeathVariants)
            {
                CheckFrames(death.Poses);deaths++;
                var end=death.Poses[death.Poses.Length-1];
                if(end.bounds.min.y<-.002f)throw new Exception("Settled corpse or gear penetrates ground: "+end.name);
            }
            for(int a=0;a<lod.DeathVariants.Length;a++)for(int b=a+1;b<lod.DeathVariants.Length;b++)
            {
                var av=lod.DeathVariants[a].Poses[11].vertices;var bv=lod.DeathVariants[b].Poses[11].vertices;
                float difference=0;for(int i=0;i<av.Length;i++)difference+=(av[i]-bv[i]).sqrMagnitude;
                if(difference/av.Length<.0001f)throw new Exception("Duplicate settled death variants.");
            }
        }
        Debug.Log($"EXPANDED_INFANTRY_VERIFIED: {loops} movement loops, {deaths} distinct grounded death sequences, both factions and LODs.");
    }
    static void CheckFrames(Mesh[] poses)
    {
        if(poses==null||poses.Length!=12)throw new Exception("Expanded sequences must contain 12 frames.");
        foreach(var mesh in poses)
        {
            if(!mesh||mesh.vertexCount!=poses[0].vertexCount)throw new Exception("Expanded sequence topology mismatch.");
            var targets=new System.Collections.Generic.List<Vector3>();mesh.GetUVs(2,targets);
            if(targets.Count!=mesh.vertexCount)throw new Exception("Missing GPU interpolation targets: "+mesh.name);
            foreach(var v in mesh.vertices)if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z))throw new Exception("Nonfinite expanded pose.");
        }
    }
    [MenuItem("Empire Battle/Verify authored infantry motions")]
    public static void Verify()
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/NapoleonicInfantry/Prefabs/FrenchInspection.prefab");
        if(!source)throw new Exception("Build the French inspection prefab before motion verification.");
        AnimationClip idle=null;
        foreach(var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/NapoleonicInfantry/Animations/musket_idle.FBX"))
            if(asset is AnimationClip clip&&!clip.name.StartsWith("__preview"))idle=clip;
        if(!idle)throw new Exception("Musket idle base clip is unavailable.");
        var root=UnityEngine.Object.Instantiate(source);
        try
        {
            root.transform.position=new Vector3(4f,0,-2f);
            root.transform.rotation=Quaternion.Euler(0,37f,0);
            idle.SampleAnimation(root,0f);
            var rig=new ProceduralInfantryRig(root);
            if(!rig.HasRequiredBones)throw new Exception("The authoring skeleton does not contain all 28 required bones.");
            Vector3 rootPosition=root.transform.position,rootScale=root.transform.localScale;
            Quaternion rootRotation=root.transform.rotation;
            var report=new StringBuilder();
            foreach(InfantryMotionKind kind in Enum.GetValues(typeof(InfantryMotionKind)))
            {
                for(int variant=0;variant<(kind==InfantryMotionKind.Death?10:3);variant++)
                {
                float maximumRadius=0f;
                for(int frame=0;frame<=64;frame++)
                {
                    rig.Sample(kind,frame/64f,variant);
                    if(!rig.ValidatePose(out string issue))throw new Exception(kind+" frame "+frame+": "+issue);
                    foreach(Transform bone in rig.Bones)maximumRadius=Mathf.Max(maximumRadius,Vector3.Distance(rootPosition,bone.position));
                    if((root.transform.position-rootPosition).sqrMagnitude>1e-10f||Quaternion.Angle(root.transform.rotation,rootRotation)>.0001f
                        ||(root.transform.localScale-rootScale).sqrMagnitude>1e-10f)
                        throw new Exception(kind+" unexpectedly changed the root transform.");
                }
                if(kind==InfantryMotionKind.March||kind==InfantryMotionKind.Walk||kind==InfantryMotionKind.Jog||kind==InfantryMotionKind.Run||kind==InfantryMotionKind.Charge)
                {
                    rig.Sample(kind,0f,variant);
                    var positions=new Vector3[rig.BoneCount];var rotations=new Quaternion[rig.BoneCount];
                    for(int i=0;i<rig.BoneCount;i++){positions[i]=rig.Bones[i].localPosition;rotations[i]=rig.Bones[i].localRotation;}
                    rig.Sample(kind,1f,variant);
                    for(int i=0;i<rig.BoneCount;i++)
                        if((positions[i]-rig.Bones[i].localPosition).sqrMagnitude>1e-8f||Quaternion.Angle(rotations[i],rig.Bones[i].localRotation)>.02f)
                            throw new Exception(kind+" loop endpoints differ at "+rig.Bones[i].name);
                }
                report.AppendLine(kind+" variant "+variant+": 65 finite bounded samples; root fixed; maximum bone radius "+maximumRadius.ToString("0.000")+"m.");
            }
            }
            rig.Sample(InfantryMotionKind.Run,.37f);rig.Sample(InfantryMotionKind.Hit,1f);
            var endPositions=new Vector3[rig.BoneCount];var endRotations=new Quaternion[rig.BoneCount];
            for(int i=0;i<rig.BoneCount;i++){endPositions[i]=rig.Bones[i].localPosition;endRotations[i]=rig.Bones[i].localRotation;}
            rig.ResetPose();
            for(int i=0;i<rig.BoneCount;i++)
                if((endPositions[i]-rig.Bones[i].localPosition).sqrMagnitude>1e-10f||Quaternion.Angle(endRotations[i],rig.Bones[i].localRotation)>.02f)
                    throw new Exception("Hit endpoint does not restore the captured pose.");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/procedural-infantry-verification.txt",report.ToString());
            Debug.Log("PROCEDURAL_INFANTRY_VERIFIED\n"+report);
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
