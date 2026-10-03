using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EmpireBattle.Art;
using EmpireWorkbench;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AssetWorkbenchBuild
{
    const string CatalogPath="Assets/Workbench/WorkbenchCatalog.asset";
    const string ScenePath="Assets/Scenes/AssetWorkbench.unity";
    [MenuItem("Empire/Asset Workbench/Refresh catalog and scene")]
    public static void Prepare()
    {
        Directory.CreateDirectory("Assets/Workbench");
        PrepareSampleMaterials();PrepareSourceAnimations();
        var entries=new List<ReviewAsset>();
        foreach(string guid in AssetDatabase.FindAssets("t:InfantryCrowdLibrary"))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var lib=AssetDatabase.LoadAssetAtPath<InfantryCrowdLibrary>(path);
            AddInfantry(entries,guid+"/france","France / line infantry",path,"france","line",lib.French,lib);
            AddInfantry(entries,guid+"/britain","Britain / line infantry",path,"britain","line",lib.British,lib);
        }
        foreach(string guid in AssetDatabase.FindAssets("t:UnitVisualLibrary"))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var lib=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(path);
            var dress=path.Contains("/NapoleonicOutfits/")?NapoleonicOutfits.All.FirstOrDefault(o=>path.Contains("/"+o.Id+"/")):null;
            string title=dress!=null?dress.Name+(path.Contains("/CartridgeSingle/")?" / breech-loading rifle":path.Contains("/CartridgeMagazine/")?" / magazine rifle":""):lib.Faction+" / "+lib.Family;
            AddInfantry(entries,guid,title,path,lib.Faction,lib.Family,lib.Set,null);
        }
        foreach(string guid in AssetDatabase.FindAssets("t:CartridgeRifleLibrary"))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var lib=AssetDatabase.LoadAssetAtPath<CartridgeRifleLibrary>(path);
            foreach(var field in typeof(CartridgeRifleLibrary).GetFields(BindingFlags.Public|BindingFlags.Instance))
            {
                if(field.FieldType!=typeof(InfantryCrowdSet))continue;
                AddInfantry(entries,guid+"/"+field.Name,"Cartridge rifle / "+field.Name,path,field.Name.Contains("French")?"france":"britain","rifle",field.GetValue(lib) as InfantryCrowdSet,null);
            }
        }
        foreach(string guid in AssetDatabase.FindAssets("t:CavalryCrowdLibrary"))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var lib=AssetDatabase.LoadAssetAtPath<CavalryCrowdLibrary>(path);
            bool regional=path.Contains("RegionalCavalry"),outfit=path.Contains("/NapoleonicOutfits/");
            var dress=outfit?NapoleonicOutfits.All.FirstOrDefault(o=>path.Contains("/"+o.Id+"/")):null;
            foreach(var side in new[]{"French","British"})
            {
                if((regional||outfit)&&side=="British")continue;
                var set=side=="French"?lib.French:lib.British;if(set?.Lods==null)continue;
                string faction=outfit?dress.Faction:regional?path.Split('/')[3]:side=="French"?"france":"britain";
                var tracks=new List<ReviewTrack>();
                for(int lod=0;lod<set.Lods.Length;lod++)foreach(var clip in set.Lods[lod].Clips??Array.Empty<MountedClip>())
                    if(clip.Poses?.Length>0)tracks.Add(new ReviewTrack {Id=clip.Motion.ToString(),Name=clip.Motion.ToString(),Lod=lod,Poses=clip.Poses,Loop=clip.Loop,Duration=MountedDuration(clip.Motion)});
                entries.Add(new ReviewAsset {Id=guid+"/"+side,Name=outfit?dress.Name:faction+" / cavalry",Category="Units",Faction=faction,Family="cavalry",SourcePath=path,Material=set.Material,Tracks=tracks.ToArray()});
            }
        }
        foreach(string path in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/Art/",StringComparison.Ordinal)&&(p.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase))).OrderBy(p=>p))
        {
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!model)continue;
            var tracks=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Select(c=>new ReviewTrack {Id=c.name,Name=c.name,Animation=c,Duration=Mathf.Max(.01f,c.length),Loop=c.isLooping}).ToArray();
            string modelName=Path.GetFileNameWithoutExtension(path);
            if(path.Contains("/CavalryHorses/"))
            {
                string[] order={"Idle","Walk","Trot","CanterLeft","CanterRight","GallopLeft","GallopRight"};
                tracks=tracks.OrderBy(t=>Array.IndexOf(order,t.Id)).ToArray();
                modelName=modelName=="HussarBay"?"French hussar — bay":modelName=="CuirassierChestnut"?"French cuirassier — chestnut":modelName=="ArabianA"?"Arabian — bay":"Arabian — grey";
            }
            entries.Add(new ReviewAsset {Id=AssetDatabase.AssetPathToGUID(path),Name=(path.Contains("/CavalryHorses/")?"Cavalry horse / ":path.Contains("/SourceAnimations/")?"Source motion / ":"")+modelName,Category="Models",SourcePath=path,Model=model,Tracks=tracks});
        }
        foreach(var entry in entries.Where(e=>e.Category=="Units"))
        {
            if(entry.SourcePath.StartsWith("Assets/Resources/"))
            {entry.ResourcePath=entry.SourcePath.Substring("Assets/Resources/".Length);entry.ResourcePath=entry.ResourcePath.Substring(0,entry.ResourcePath.LastIndexOf('.'));}
            else entry.BundlePath="Workbench/rosters";
            entry.SetField=entry.SourcePath.Contains("InfantryCrowdLibrary")||entry.SourcePath.Contains("CavalryCrowdLibrary")||entry.SourcePath.Contains("CartridgeRifles")?entry.Id.Substring(entry.Id.LastIndexOf('/')+1):"Set";
            if(entry.SetField=="france")entry.SetField="French";if(entry.SetField=="britain")entry.SetField="British";
            if(entry.SourcePath.Contains("/CartridgeSingle/"))entry.Weapon="breechloader";
            if(entry.SourcePath.Contains("/CartridgeMagazine/"))entry.Weapon="magazine";
            if(entry.SourcePath.Contains("CartridgeRifles"))entry.Weapon=entry.SetField.Contains("Single")?"breechloader":"magazine";
            WorkbenchUnitLoader.Release(entry);
        }
        entries=entries.OrderBy(e=>e.Category=="Units"?0:1).ThenBy(e=>e.Name).ToList();
        var catalog=AssetDatabase.LoadAssetAtPath<WorkbenchCatalog>(CatalogPath);
        if(!catalog){catalog=ScriptableObject.CreateInstance<WorkbenchCatalog>();AssetDatabase.CreateAsset(catalog,CatalogPath);}
        catalog.Entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        // Restore the editor's open scenes after generating this independent scene.
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var browser=new GameObject("Asset Workbench").AddComponent<AssetWorkbench>();browser.Catalog=catalog;browser.ProjectRoot=Directory.GetCurrentDirectory();
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
        finally {if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);}
        Verify();
        Directory.CreateDirectory("Artifacts/Workbench");
        File.WriteAllText("Artifacts/Workbench/catalog.json",JsonUtility.ToJson(new CatalogReceipt {Assets=entries.Select(e=>new EntryReceipt {Id=e.Id,Name=e.Name,Category=e.Category,SourcePath=e.SourcePath,Tracks=e.Tracks.Length,Lods=e.Tracks.Select(t=>t.Lod).Distinct().Count()}).ToArray()},true));
        Debug.Log($"WORKBENCH_PREPARED {entries.Count} assets, {entries.Sum(e=>e.Tracks.Length)} animation/LOD tracks");
    }
    static void AddInfantry(List<ReviewAsset> entries,string id,string name,string path,string faction,string family,InfantryCrowdSet set,InfantryCrowdLibrary lib)
    {
        if(set?.Lods==null||!set.Material)return;
        var tracks=new List<ReviewTrack>();
        for(int i=0;i<set.Lods.Length;i++)Collect(set.Lods[i],"",i,tracks,lib);
        entries.Add(new ReviewAsset {Id=id,Name=name,Category="Units",SourcePath=path,Faction=faction,Family=family,Material=set.Material,Tracks=tracks.ToArray()});
    }
    static void Collect(object value,string path,int lod,List<ReviewTrack> tracks,InfantryCrowdLibrary lib)
    {
        if(value==null)return;
        if(value is Mesh[] poses)
        {
            // These are separate standing/kneeling bridges for each reload pose,
            // not the successive frames of a reload animation.
            if(path=="RiseToReload")
            {for(int i=0;i<poses.Length;i++)Collect(poses[i],path+"/"+i,lod,tracks,lib);return;}
            if(poses.Length>0&&poses.All(p=>p))tracks.Add(new ReviewTrack {Id=path,Name=path,Poses=poses,Lod=lod,Duration=Duration(path,lib),Loop=IsLoop(path)});
            return;
        }
        if(value is Mesh mesh)
        {
            if(!mesh)return;
            bool rising=path.StartsWith("RiseTo"),morph=rising||path=="LowerToKneel"||path=="KneelingAimTransition";
            tracks.Add(new ReviewTrack {Id=path,Name=path,Poses=new[]{mesh},Lod=lod,
                Duration=morph?(rising?EmpireBattle.RifleFireTiming.RaiseDuration:EmpireBattle.RifleFireTiming.LowerDuration):1,
                Loop=false,Morph=morph,ReverseMorph=rising});return;
        }
        if(value is Array array)
        {
            for(int i=0;i<array.Length;i++)
            {
                var item=array.GetValue(i);string label=i.ToString();
                if(item is InfantryMeleeClip clip)label=clip.Action.ToString();
                if(path=="Command")label=new[]{"Captain","Drummer","Standard","Pike sergeant","Colonel"}.ElementAtOrDefault(i)??label;
                Collect(item,path+"/"+label,lod,tracks,lib);
            }
            return;
        }
        var type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||value is UnityEngine.Object)return;
        foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))Collect(field.GetValue(value),string.IsNullOrEmpty(path)?field.Name:path+"/"+field.Name,lod,tracks,lib);
    }
    static bool IsLoop(string name)
    {
        if(name=="GrenadeThrow")return false;
        if(name.StartsWith("Melee/"))return false; // All melee clips include their endpoint.
        if(name.StartsWith("UnderFireVariants/"))return true;
        return !(new[]{"Reload","Fire","Death","Knockdown","GetUp","Hit","Strike","Thrust","Slash","Stagger","Recoil"}.Any(name.Contains));
    }
    static float Duration(string name,InfantryCrowdLibrary lib)
    {
        if(name=="GrenadeThrow")return EmpireBattle.GrenadeSystem.ThrowDuration;
        if(name.StartsWith("UnderFireVariants/"))return 2.4f;
        if(name.EndsWith("/OfficerFire"))return .75f;
        if(name.Contains("Reload"))return lib?lib.ReloadDuration:5;
        if(name.Contains("Fire"))return lib?lib.FireDuration:3.333333f;
        if(name.Contains("March")||name.Contains("Run")||name.Contains("Charge")||name.Contains("Walk"))return .9f;
        if(name.Contains("Death")||name.Contains("Knockdown")||name.Contains("GetUp"))return 2;
        if(name.StartsWith("Melee")||name.Contains("Thrust")||name.Contains("Slash"))return 1;
        return lib?lib.IdleDuration:3.333333f;
    }
    static float MountedDuration(MountedMotion motion)=>motion==MountedMotion.MountedFire||motion==MountedMotion.PistolFire?EmpireBattle.CombatSystem.MountedFireDuration:motion==MountedMotion.MountedReload?20:motion==MountedMotion.PistolReload?18:motion==MountedMotion.Idle||motion==MountedMotion.HorseIdle?3:motion.ToString().Contains("Fall")||motion==MountedMotion.RiderTumble?2:1;
    public static void RefreshAnimationMetadata()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<WorkbenchCatalog>(CatalogPath);
        if(!catalog)throw new Exception("Prepare the workbench catalog first");
        var lib=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var entry in catalog.Entries.Where(e=>e.Category=="Units"&&e.Family!="cavalry"))
        foreach(var track in entry.Tracks)
        {
            bool rising=track.Id.StartsWith("RiseTo");
            track.Morph=rising||track.Id=="LowerToKneel"||track.Id=="KneelingAimTransition";
            track.ReverseMorph=rising;
            track.Loop=!track.Morph&&IsLoop(track.Id);
            track.Duration=track.Morph?(rising?EmpireBattle.RifleFireTiming.RaiseDuration:EmpireBattle.RifleFireTiming.LowerDuration)
                :Duration(track.Id,entry.Family=="line"&&(entry.Faction=="france"||entry.Faction=="britain")?lib:null);
        }
        EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
    }
    static void PrepareSampleMaterials()
    {
        const string receipt="AssetSources/ModSamples/selection.json";
        if(!File.Exists(receipt))return;
        var samples=JsonUtility.FromJson<SampleReceipt>("{\"Items\":"+File.ReadAllText(receipt)+"}");
        Directory.CreateDirectory("Assets/Art/ModSamples/Materials");
        foreach(var sample in samples.Items)
        {
            var importer=AssetImporter.GetAtPath(sample.fbx) as ModelImporter;
            if(!importer)throw new Exception("Sample not imported: "+sample.fbx);
            foreach(var row in sample.materials)
            {
                string path="Assets/Art/ModSamples/Materials/"+row.name+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(row.texture);
                if(!texture)throw new Exception("Sample texture missing: "+row.texture);
                mat.SetTexture("_BaseMap",texture);mat.SetColor("_BaseColor",Color.white);
                mat.SetFloat("_Smoothness",.2f);mat.SetFloat("_Cull",row.cutout?0:2);
                mat.SetFloat("_AlphaClip",row.cutout?1:0);mat.SetFloat("_Cutoff",.4f);
                if(row.cutout){mat.EnableKeyword("_ALPHATEST_ON");mat.renderQueue=2450;mat.SetOverrideTag("RenderType","TransparentCutout");}
                EditorUtility.SetDirty(mat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),row.name),mat);
            }
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }
    static void PrepareSourceAnimations()
    {
        const string path="Assets/Art/ModSamples/Materials/SourceMotionMannequin.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_BaseColor",new Color(.45f,.62f,.75f));EditorUtility.SetDirty(material);
        foreach(string file in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/Art/ModSamples/SourceAnimations/")&&p.EndsWith(".fbx")))
        {
            var importer=AssetImporter.GetAtPath(file) as ModelImporter;
            importer.animationType=ModelImporterAnimationType.Generic;
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(file);
            foreach(var mat in model.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct())
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),mat.name),material);
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }
    [Serializable]sealed class SampleReceipt {public SampleRow[] Items;}
    [Serializable]sealed class SampleRow {public string fbx;public SampleMaterial[] materials;}
    [Serializable]sealed class SampleMaterial {public string name,texture;public bool cutout;}
    [MenuItem("Empire/Asset Workbench/Open browser")]
    public static void Open()
    {
        if(EditorApplication.isPlaying){Debug.LogWarning("Leave the current Play mode session before opening the workbench.");return;}
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        Prepare();EditorSceneManager.OpenScene(ScenePath);EditorApplication.isPlaying=true;
    }
    [MenuItem("Empire/Asset Workbench/Build standalone development app")]
    public static void BuildMac()
    {
        string output="Builds/Development/ETW2 Asset Workbench.app";
        var args=Environment.GetCommandLineArgs();int outputFlag=Array.IndexOf(args,"--workbench-output");
        if(outputFlag>=0&&outputFlag+1<args.Length)output=args[outputFlag+1];
        using(var check=new System.Diagnostics.Process {StartInfo=new System.Diagnostics.ProcessStartInfo("/bin/ps","-axo args=") {UseShellExecute=false,RedirectStandardOutput=true}})
        {
            check.Start();string listing=check.StandardOutput.ReadToEnd();check.WaitForExit();
            string binary=Path.GetFullPath(output)+"/Contents/MacOS/ETW2 Asset Workbench";
            if(listing.Split('\n').Any(line=>line.Trim()==binary||line.Trim().StartsWith(binary+" ",StringComparison.Ordinal)))
                throw new Exception("Workbench app is running; choose a different --workbench-output path.");
        }
        Prepare();
        var catalog=AssetDatabase.LoadAssetAtPath<WorkbenchCatalog>(CatalogPath);
        string bundleDirectory="Assets/StreamingAssets/Workbench";
        Directory.CreateDirectory(bundleDirectory);
        var bundled=catalog.Entries.Where(e=>!string.IsNullOrEmpty(e.BundlePath)).Select(e=>e.SourcePath).Distinct().ToArray();
        if(bundled.Length>0)
        {
            var manifest=BuildPipeline.BuildAssetBundles(bundleDirectory,new[]{new AssetBundleBuild {assetBundleName="rosters",assetNames=bundled}},BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneOSX);
            if(!manifest)throw new Exception("Workbench unit bundle failed");
        }
        AssetDatabase.Refresh();
        string product=PlayerSettings.productName;var target=UnityEditor.Build.NamedBuildTarget.Standalone;
        string identifier=PlayerSettings.GetApplicationIdentifier(target);
        try
        {
            PlayerSettings.productName="ETW2 Asset Workbench";PlayerSettings.SetApplicationIdentifier(target,"com.andrewcote.etw2.workbench");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.None});
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Workbench build failed: "+result.summary.result);
            Debug.Log("WORKBENCH_BUILD_PASSED "+result.summary.totalSize+" bytes");
        }
        finally {PlayerSettings.productName=product;PlayerSettings.SetApplicationIdentifier(target,identifier);if(Directory.Exists(bundleDirectory))Directory.Delete(bundleDirectory,true);if(File.Exists(bundleDirectory+".meta"))File.Delete(bundleDirectory+".meta");AssetDatabase.Refresh();}
    }
    public static void InspectSourceMotion()
    {
        string path="Assets/Art/ModSamples/SourceAnimations/source-katana_slashright_a.fbx";
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);var model=UnityEngine.Object.Instantiate(prefab);
        var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
        foreach(var clip in clips)
        {
            Debug.Log("SOURCE_CLIP "+clip.name+" duration "+clip.length+" bindings "+AnimationUtility.GetCurveBindings(clip).Length);
            foreach(var binding in AnimationUtility.GetCurveBindings(clip).Take(8))Debug.Log("SOURCE_BINDING "+binding.path+" "+binding.propertyName);
            var hand=model.GetComponentsInChildren<Transform>().First(t=>t.name=="hand.R");
            foreach(var animator in model.GetComponentsInChildren<Animator>())animator.enabled=false;
            clip.SampleAnimation(model,clip.length*.2f);Debug.Log("SOURCE_HAND20 "+hand.position);
            clip.SampleAnimation(model,clip.length*.6f);Debug.Log("SOURCE_HAND60 "+hand.position);
        }
        UnityEngine.Object.DestroyImmediate(model);
    }
    public static void Verify()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<WorkbenchCatalog>(CatalogPath);
        if(!catalog||catalog.Entries.Length==0)throw new Exception("Missing catalog");
        if(catalog.Entries.Select(e=>e.Id).Distinct().Count()!=catalog.Entries.Length)throw new Exception("Duplicate stable asset ID");
        foreach(var entry in catalog.Entries)
        {
            WorkbenchUnitLoader.Load(entry);
            if(entry.Category=="Units"&&!entry.Material)throw new Exception("Missing unit material: "+entry.Name);
            if(entry.Tracks.GroupBy(t=>t.Id+"/"+t.Lod).Any(g=>g.Count()>1))throw new Exception("Duplicate track: "+entry.Name);
            foreach(var track in entry.Tracks)
                if(!track.Animation&&(track.Poses==null||track.Poses.Length==0||track.Poses.Any(p=>!p)))throw new Exception("Invalid track: "+entry.Name+"/"+track.Name);
            WorkbenchUnitLoader.Release(entry);
        }
        var path=Path.Combine("Artifacts/Workbench","StoreTest-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new WorkbenchStore(path);
            store.SaveNote(new ReviewNote {AssetId="asset",TrackId="reload",Text="Fix elbow",NormalizedTime=.4f});
            store.SaveNote(new ReviewNote {AssetId="asset",TrackId="reload",Text="Fix elbow and wrist",NormalizedTime=.5f});
            var reopened=new WorkbenchStore(path);if(reopened.Notebook.Notes.Count!=1||reopened.Find("asset","reload").NormalizedTime!=.5f)throw new Exception("Note roundtrip failed");
            reopened.ExportQueue();
            var preset=new ReviewPreset {Name="Test"};preset.Effects.SmokeSize=2;preset.Effects.BloodAmount=0;
            store.SavePreset(preset);var loaded=store.LoadPreset("Test");if(loaded.Effects.SmokeSize!=2||loaded.Effects.BloodAmount!=0)throw new Exception("Preset roundtrip failed");
            if(WorkbenchStore.SafeName("../escape").Contains("/"))throw new Exception("Unsafe preset filename");
        }
        finally {if(Directory.Exists(path))Directory.Delete(path,true);}
        VerifyEffectTuning();
        Debug.Log("WORKBENCH_VERIFIED catalog integrity, unique stable IDs, note/preset persistence, pending queue export");
    }
    static void VerifyEffectTuning()
    {
        var original=EmpireBattle.BattleEffectTuning.Current;
        var root=new GameObject("Workbench effect verification");
        try
        {
            var world=new EmpireBattle.BattleWorld(new EmpireBattle.BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=16,EnableLandscape=false});
            var camera=root.AddComponent<Camera>();
            var blood=root.AddComponent<EmpireBattle.BattleBlood>();blood.Initialize(world,camera,null);
            EmpireBattle.BattleEffectTuning.Current=new EmpireBattle.BattleEffectProfile {BloodAmount=0};
            blood.EmitSpray(Vector3.up,Vector3.forward,true,0);
            var field=typeof(EmpireBattle.BattleBlood).GetField("drops",BindingFlags.NonPublic|BindingFlags.Instance);
            Func<int> active=()=>((Array)field.GetValue(blood)).Cast<object>().Count(d=>(bool)d.GetType().GetField("Active").GetValue(d));
            if(active()!=0)throw new Exception("Zero blood spray setting did not suppress emission");
            EmpireBattle.BattleEffectTuning.Current.BloodAmount=1;
            blood.EmitSpray(Vector3.up,Vector3.forward,true,0);
            int count=active();if(count<=0)throw new Exception("Normal blood spray setting emitted nothing");
            EmpireBattle.BattleEffectTuning.Current.BloodAmount=3;
            for(int i=0;i<100;i++)blood.EmitSpray(Vector3.up,Vector3.forward,true,0);
            if(active()!=4096)throw new Exception("Blood pool was not bounded");
            var smoke=root.AddComponent<EmpireBattle.MusketSmoke>();smoke.Initialize(world,camera);
            EmpireBattle.BattleEffectTuning.Current.Wind=0;if(smoke.WindDisplacement(0,1)!=Vector3.zero)throw new Exception("Zero wind failed");
            if(world.Time!=0||world.DeathCount!=0||world.CountAlive(EmpireBattle.ArmySide.French)!=16)throw new Exception("Visual profile changed simulation");
            Debug.Log("WORKBENCH_EFFECTS_VERIFIED zero/normal/max blood spray, bounded pool, zero wind, combat state unchanged");
        }
        finally {UnityEngine.Object.DestroyImmediate(root);EmpireBattle.BattleEffectTuning.Current=original;}
    }
    [Serializable]sealed class CatalogReceipt {public EntryReceipt[] Assets;}
    [Serializable]sealed class EntryReceipt {public string Id,Name,Category,SourcePath;public int Tracks,Lods;}
}
