using System;
using System.IO;
using System.Text;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;

public static partial class InfantryAssetBuild
{
    public static void PrepareCartridgeRifles()
    {
        const string output="Assets/Resources/CartridgeRifles";
        var existingLibrary=AssetDatabase.LoadAssetAtPath<CartridgeRifleLibrary>(output+"/Library.asset");
        if(existingLibrary&&existingLibrary.WeaponBakeVersion==CartridgeWeaponAuthoring.Version&&Complete(existingLibrary.French)&&Complete(existingLibrary.British)&&Complete(existingLibrary.SingleFrench)&&Complete(existingLibrary.SingleBritish)){BakeCartridgeDeaths();return;}
        Directory.CreateDirectory(output);Directory.CreateDirectory("Logs");prepared=true;
        var original=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        var result=existingLibrary?existingLibrary:ScriptableObject.CreateInstance<CartridgeRifleLibrary>();if(!existingLibrary)AssetDatabase.CreateAsset(result,output+"/Library.asset");var report=new StringBuilder();
        var atlasMaps=new System.Collections.Generic.Dictionary<string,Rect>[2];
        try
        {
            cartridgeBake=true;
            for(int family=0;family<2;family++)for(int level=0;level<3;level++)
            {
                BuildScene(false,level==0?3:5);
                result=AssetDatabase.LoadAssetAtPath<CartridgeRifleLibrary>(output+"/Library.asset");original=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
                var inspection=UnityEngine.Object.FindAnyObjectByType<InfantryInspection>();
                for(int side=0;side<2;side++)
                {
                    var source=side==0?original.French:original.British;var rig=inspection.Soldiers[side];rig.transform.position=Vector3.zero;
                    var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
                    gun.GetComponent<MeshFilter>().sharedMesh=CartridgeWeaponAuthoring.Prepare(family==1,level);
                    gun.sharedMaterial=CartridgeWeaponAuthoring.PrepareMaterial();
                    string poseOutput=output+(family==1?"/Single":"");Directory.CreateDirectory(poseOutput);
                    var target=side==0?(family==1?result.SingleFrench:result.French):(family==1?result.SingleBritish:result.British);
                    if(target?.Lods?.Length!=3||!target.Material){target=new InfantryCrowdSet{Material=source.Material,Lods=new InfantryCrowdLod[3]};if(family==1){if(side==0)result.SingleFrench=target;else result.SingleBritish=target;}else {if(side==0)result.French=target;else result.British=target;}}
                    if(level==0){atlasMaps[side]=PrepareCartridgeAtlas(rig,poseOutput,out var material);target.Material=material;}
                    var maps=atlasMaps[side];
                    report.AppendLine($"WEAPON {rig.name} single={family==1} L{level}: triangles={gun.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3}");
                    var lod=new InfantryCrowdLod{MaxDistance=level==0?40:level==1?140:100000};target.Lods[level]=lod;
                    Mesh[] Bake(AnimationClip clip,int count,InfantryMotionKind? motion=null,bool kneel=false,int variant=-1)=>BakePoses(rig,clip,count,maps,poseOutput,level,report,motion,kneel,variant);
                    lod.Idle=Bake(inspection.Clips[0],8);lod.Fire=Bake(inspection.Clips[1],8);
                    lod.KneelingFire=Bake(inspection.Clips[1],8,null,true);
                    lod.Reload=Bake(inspection.Clips[2],24);lod.KneelingReload=Bake(inspection.Clips[2],24,null,true);
                    lod.March=Bake(inspection.Clips[0],12,InfantryMotionKind.March);
                    lod.Run=Bake(inspection.Clips[0],12,InfantryMotionKind.Run);
                    lod.Charge=Bake(inspection.Clips[0],12,InfantryMotionKind.Charge);
                    lod.Hit=Bake(inspection.Clips[0],8,InfantryMotionKind.Hit);
                    lod.Death=Bake(inspection.Clips[0],12,InfantryMotionKind.Death);
                    lod.Knockdown=Bake(inspection.Clips[0],12,InfantryMotionKind.Knockdown);
                    lod.GetUp=Bake(inspection.Clips[0],12,InfantryMotionKind.GetUp);
                    RefineMeleeLod(rig,inspection.Clips[0],lod,poseOutput,level,report,false);
                    inspection.Clips[1].SampleAnimation(rig,inspection.Clips[1].length*.4f);
                    gun.transform.SetParent(rig.transform,true);
                    new ClosedGripAuthoring(rig,gun.transform,Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod")).Apply(true,false,.4f);
                    var muzzle=gun.transform.TransformPoint(CartridgeWeaponAuthoring.Muzzle);
                    target.MuzzleOffset=Quaternion.Euler(0,180,0)*rig.transform.InverseTransformPoint(muzzle);
                    target.LockOffset=Quaternion.Euler(0,180,0)*rig.transform.InverseTransformPoint(gun.transform.TransformPoint(CartridgeWeaponAuthoring.Receiver));
                    EditorUtility.SetDirty(result);AssetDatabase.SaveAssets();
                }
            }
            result.WeaponBakeVersion=CartridgeWeaponAuthoring.Version;
            EditorUtility.SetDirty(result);AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/cartridge-rifle-authoring.txt",report.ToString());
            Debug.Log("CARTRIDGE_RIFLE_PRESENTATION_READY shorter rifle, receiver, bolt, magazine and chest-height cartridge loading; single-shot and magazine models, both sides and LODs");
        }
        finally{cartridgeBake=false;}
        BakeCartridgeDeaths();
    }
    public static void BakeCartridgeDeaths()
    {
        const string output="Assets/Resources/CartridgeRifles";
        var library=AssetDatabase.LoadAssetAtPath<CartridgeRifleLibrary>(output+"/Library.asset");
        if(!library)throw new Exception("Prepare cartridge rifles before baking death variants");
        prepared=true;var report=new StringBuilder();int tracks=0;
        library.hideFlags=HideFlags.DontUnloadUnusedAsset;
        try
        {
            cartridgeBake=true;
            for(int family=0;family<2;family++)for(int level=0;level<3;level++)
            {
                var french=family==1?library.SingleFrench:library.French;
                var british=family==1?library.SingleBritish:library.British;
                if(french.Lods[level].DeathVariants?.Length==10&&british.Lods[level].DeathVariants?.Length==10)continue;
                BuildScene(false,level==0?3:5);
                var inspection=UnityEngine.Object.FindAnyObjectByType<InfantryInspection>();
                for(int side=0;side<2;side++)
                {
                    var lod=(side==0?french:british).Lods[level];
                    if(lod.DeathVariants?.Length==10)continue;
                    var rig=inspection.Soldiers[side];rig.transform.position=Vector3.zero;
                    var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
                    gun.GetComponent<MeshFilter>().sharedMesh=CartridgeWeaponAuthoring.Prepare(family==1,level);
                    gun.sharedMaterial=CartridgeWeaponAuthoring.PrepareMaterial();
                    BakeReviewDeaths(rig,inspection.Clips[0],lod,output+(family==1?"/Single":""),level,report);
                    tracks+=10;
                }
                EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
            }
            Debug.Log("CARTRIDGE_DEATH_VARIANTS_READY: "+tracks+" new tracks; ten distinct falls per outfit, rifle family and LOD");
        }
        finally{cartridgeBake=false;library.hideFlags=HideFlags.None;}
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
    }
    // Cartridge meshes have a different topology; pack their own atlas instead of recovering an older crowd atlas.
    static System.Collections.Generic.Dictionary<string,Rect> PrepareCartridgeAtlas(GameObject rig,string output,out Material material)
    {
        var names=new System.Collections.Generic.List<string>();var textures=new System.Collections.Generic.List<Texture2D>();
        foreach(var renderer in rig.GetComponentsInChildren<Renderer>())if(renderer.enabled)
            foreach(var sourceMaterial in renderer.sharedMaterials)if(!names.Contains(sourceMaterial.name))
            {
                names.Add(sourceMaterial.name);var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                var source=sourceMaterial.GetTexture("_BaseMap");
                if(source)texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));
                else{var colour=sourceMaterial.color;texture.SetPixels(new[]{colour,colour,colour,colour});texture.Apply();}
                textures.Add(texture);
            }
        names.Add("CoarseWeapon");var wood=new Texture2D(2,2);var brown=new Color(.2f,.12f,.07f);
        wood.SetPixels(new[]{brown,brown,brown,brown});wood.Apply();textures.Add(wood);
        var atlas=new Texture2D(4096,4096,TextureFormat.RGBA32,false);var packed=atlas.PackTextures(textures.ToArray(),8,4096);
        string atlasPath=output+"/"+rig.name+"Atlas.png";File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());AssetDatabase.ImportAsset(atlasPath);
        var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);importer.maxTextureSize=4096;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
        string materialPath=output+"/"+rig.name+".mat";material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        bool create=!material;if(create)material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.enableInstancing=true;material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));material.SetColor("_BaseColor",Color.white);
        if(create)AssetDatabase.CreateAsset(material,materialPath);else EditorUtility.SetDirty(material);
        var maps=new System.Collections.Generic.Dictionary<string,Rect>();for(int i=0;i<names.Count;i++)maps[names[i]]=packed[i];
        foreach(var texture in textures)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(atlas);return maps;
    }
    static bool Complete(InfantryCrowdSet set)
    {
        if(set?.Lods?.Length!=3||!set.Material)return false;
        foreach(var lod in set.Lods)if(lod?.Idle?.Length!=8||lod.Fire?.Length!=8||lod.KneelingFire?.Length!=8||lod.Reload?.Length!=24||lod.KneelingReload?.Length!=24||lod.March?.Length!=12||lod.Run?.Length!=12||lod.Charge?.Length!=12||lod.Hit?.Length!=8||(lod.Death?.Length!=12&&lod.Death?.Length!=16)||lod.Knockdown?.Length!=12||lod.GetUp?.Length!=12)return false;
        return true;
    }
}
