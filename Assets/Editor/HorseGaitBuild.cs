using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Imports our authored period horse clips without rebaking battle libraries.</summary>
public static class HorseGaitBuild
{
    public const string Folder="Assets/Art/CavalryHorses";
    [Serializable] public sealed class Manifest {public Model[] models;}
    [Serializable] public sealed class Model {public string name,source_pack,fbx;public string[] source_parts;public Mapping[] materials;public Clip[] clips;public int triangles,bones;}
    [Serializable] public sealed class Mapping {public string name,texture;}
    [Serializable] public sealed class Clip {public string name;public float duration,stride,duty,max_target_error,loop_error;public float[] contacts;public int frames;}
    [Serializable] sealed class Verification {public List<Result> models=new List<Result>();}
    [Serializable] sealed class Result {public string name;public int clips,triangles,bones;public float maxLoopError,minimumVertexHeight,maximumHoofTravel,maxStanceHeightChange,maxStanceTravelError;}

    [MenuItem("Empire/Cavalry/Import authored horses and refresh Workbench")]
    public static void Prepare()
    {
        const string source="AssetSources/CavalryHorses";
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(source+"/manifest.json"));
        Directory.CreateDirectory(Folder+"/Textures");Directory.CreateDirectory(Folder+"/Materials");
        foreach(var model in manifest.models)
        {
            File.Copy(source+"/"+model.fbx,Folder+"/"+model.fbx,true);
            foreach(var mapping in model.materials)File.Copy(source+"/"+mapping.texture,Folder+"/"+mapping.texture,true);
        }
        File.Copy(source+"/manifest.json",Folder+"/manifest.json",true);
        AssetDatabase.Refresh();
        foreach(var model in manifest.models)
        {
            string path=Folder+"/"+model.fbx;
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType=ModelImporterAnimationType.Generic;
            importer.importAnimation=true;importer.isReadable=true;
            importer.animationCompression=ModelImporterAnimationCompression.Off;
            importer.importCameras=false;importer.importLights=false;
            importer.optimizeGameObjects=false;
            var clips=importer.defaultClipAnimations;
            if(clips.Length!=model.clips.Length)throw new Exception($"Horse {model.name}: expected {model.clips.Length} takes, imported {clips.Length}");
            foreach(var clip in clips)
            {
                var authored=model.clips.FirstOrDefault(c=>clip.takeName==c.name||clip.takeName.EndsWith("|"+c.name)||clip.name==c.name);
                if(authored==null)throw new Exception("Unknown horse take: "+clip.takeName);
                clip.name=authored.name;clip.loopTime=true;clip.loopPose=false;
                clip.wrapMode=WrapMode.Loop;
            }
            importer.clipAnimations=clips;
            foreach(var mapping in model.materials)
            {
                string materialPath=Folder+"/Materials/"+mapping.name+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,materialPath);}
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/"+mapping.texture);
                if(!texture)throw new Exception("Horse texture not imported: "+mapping.texture);
                material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
                material.SetFloat("_Smoothness",.18f);material.enableInstancing=true;
                EditorUtility.SetDirty(material);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),mapping.name),material);
            }
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
        Verify();AssetWorkbenchBuild.Prepare();
        Debug.Log("CAVALRY_HORSES_PREPARED four period mounts, seven authored gait clips each");
    }

    public static void Verify()
    {
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder+"/manifest.json"));
        var receipt=new Verification();
        foreach(var spec in manifest.models)
        {
            string path=Folder+"/"+spec.fbx;
            var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                foreach(var animator in model.GetComponentsInChildren<Animator>())animator.enabled=false;
                var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();
                var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
                if(clips.Length!=7)throw new Exception("Missing gait clips: "+spec.name);
                var result=new Result{name=spec.name,clips=clips.Length,triangles=skins.Sum(s=>s.sharedMesh.triangles.Length/3),bones=skins.SelectMany(s=>s.bones).Distinct().Count(),minimumVertexHeight=float.PositiveInfinity};
                if(result.bones!=28||result.triangles!=spec.triangles)throw new Exception("Horse topology changed: "+spec.name);
                foreach(var skin in skins)
                    if(skin.bones.Any(b=>!b)||skin.sharedMaterials.Any(m=>!m||!m.GetTexture("_BaseMap")))throw new Exception("Incomplete horse skin/material: "+spec.name);
                var hoof=model.GetComponentsInChildren<Transform>().First(t=>t.name=="clavicle.L");
                Debug.Log("HORSE_IMPORT_BOUNDS "+spec.name+" "+string.Join("; ",skins.Select(s=>$"{s.name} scale={s.transform.lossyScale} bounds={s.bounds}")));
                foreach(var clip in clips)
                {
                    var expected=spec.clips.Single(c=>c.name==clip.name);
                    if(!clip.isLooping||Mathf.Abs(clip.length-expected.duration)>.025f)throw new Exception("Invalid gait timing: "+clip.name+" "+clip.length);
                    clip.SampleAnimation(model,0);
                    var start=skins.SelectMany(s=>SampleVertices(s,model.transform)).ToArray();
                    Vector3 firstHoof=hoof.position;
                    clip.SampleAnimation(model,clip.length);
                    var end=skins.SelectMany(s=>SampleVertices(s,model.transform)).ToArray();
                    float seam=start.Zip(end,(a,b)=>Vector3.Distance(a,b)).Max();result.maxLoopError=Mathf.Max(result.maxLoopError,seam);
                    if(seam>.002f)throw new Exception("Horse loop seam: "+clip.name+" "+seam);
                    float travel=0;
                    for(int frame=0;frame<16;frame++)
                    {
                        clip.SampleAnimation(model,clip.length*frame/16f);
                        travel=Mathf.Max(travel,Vector3.Distance(firstHoof,hoof.position));
                        foreach(var p in skins.SelectMany(s=>SampleVertices(s,model.transform)))
                        {
                            if(float.IsNaN(p.sqrMagnitude)||float.IsInfinity(p.sqrMagnitude)||p.magnitude>5)throw new Exception("Invalid horse deformation: "+spec.name+"/"+clip.name+" vertex="+p);
                            result.minimumVertexHeight=Mathf.Min(result.minimumVertexHeight,p.y);
                        }
                    }
                    result.maximumHoofTravel=Mathf.Max(result.maximumHoofTravel,travel);
                    if(clip.name!="Idle"&&travel<.25f)throw new Exception("Horse gait does not animate: "+clip.name);
                    if(expected.stride>0)
                    {
                        string[] feet={"clavicle.L","clavicle.R","h_hoof.L","h_hoof.R"};
                        for(int leg=0;leg<feet.Length;leg++)
                        {
                            var foot=model.GetComponentsInChildren<Transform>().First(t=>t.name==feet[leg]);
                            clip.SampleAnimation(model,clip.length*Mathf.Repeat(expected.contacts[leg]+expected.duty*.15f,1));var a=foot.position;
                            clip.SampleAnimation(model,clip.length*Mathf.Repeat(expected.contacts[leg]+expected.duty*.85f,1));var b=foot.position;
                            float height=Mathf.Abs(a.y-b.y);
                            float drift=Mathf.Abs(new Vector2(a.x-b.x,a.z-b.z).magnitude-expected.stride*expected.duty*.7f);
                            result.maxStanceHeightChange=Mathf.Max(result.maxStanceHeightChange,height);
                            result.maxStanceTravelError=Mathf.Max(result.maxStanceTravelError,drift);
                            if(height>.02f||drift>.025f)throw new Exception($"Horse stance contact failed: {spec.name}/{clip.name}/{feet[leg]} height={height} travelError={drift}");
                        }
                    }
                    Debug.Log($"HORSE_CLIP_VERIFIED {spec.name}/{clip.name} seconds={clip.length:F2} hoofTravel={travel:F3} seam={seam:F6}");
                }
                if(result.minimumVertexHeight<-.02f)throw new Exception("Horse penetrates floor: "+spec.name+" "+result.minimumVertexHeight);
                receipt.models.Add(result);
            }
            finally {UnityEngine.Object.DestroyImmediate(model);}
        }
        Directory.CreateDirectory("Artifacts/CavalryAssetResearch");
        File.WriteAllText("Artifacts/CavalryAssetResearch/unity-verification.json",JsonUtility.ToJson(receipt,true));
        Debug.Log("CAVALRY_GAITS_VERIFIED "+receipt.models.Count+" mounts: loop seams, motion, textures, bones and bounds");
    }
    static Vector3[] SampleVertices(SkinnedMeshRenderer skin,Transform root)
    {
        var mesh=new Mesh();
        try {skin.BakeMesh(mesh);var matrix=root.worldToLocalMatrix*skin.transform.localToWorldMatrix;return mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();}
        finally {UnityEngine.Object.DestroyImmediate(mesh);}
    }
}
