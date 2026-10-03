using System;
using System.IO;
using EmpireGame;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnifiedGameBuild
{
    public static readonly string[] Scenes={"Assets/Scenes/MainMenu.unity","Assets/Scenes/Campaign.unity","Assets/Scenes/LineBattle.unity"};
    [MenuItem("Empire/Create unified game scenes")]
    public static void PrepareScenes()
    {
        BattleBuild.CreateScene();CampaignBuild.CreateScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Game application").AddComponent<GameApplication>();
        Directory.CreateDirectory("Assets/Scenes");EditorSceneManager.SaveScene(scene,Scenes[0]);
        EditorBuildSettings.scenes=Array.ConvertAll(Scenes,path=>new EditorBuildSettingsScene(path,true));
    }
    static void PrepareResourceData()
    {
        string output="Assets/StreamingAssets/ResourceData";
        foreach(string source in Directory.GetFiles("Assets","*",SearchOption.AllDirectories))
        {
            string normalized=source.Replace('\\','/');int start=normalized.LastIndexOf("/Resources/",StringComparison.Ordinal);
            if(start<0||(Path.GetExtension(source)!=".json"&&Path.GetExtension(source)!=".bytes"))continue;
            string target=Path.Combine(output,normalized.Substring(start+11));
            Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target,true);
        }
        AssetDatabase.Refresh();
    }
    [MenuItem("Empire/Build unified game")]
    public static void BuildMac()
    {
        string output="Builds/ETW2.app";
        var arguments=Environment.GetCommandLineArgs();int outputFlag=Array.IndexOf(arguments,"--unified-output");
        if(outputFlag>=0){if(outputFlag+1>=arguments.Length)throw new ArgumentException("Missing --unified-output path");output=arguments[outputFlag+1];}
        BattleBuild.Verify();CampaignCountryBuild.Verify();
        BattleEffectsVerification.PrepareResources();ArtilleryBuild.PrepareCrewAssets();InfantryAssetBuild.PrepareCartridgeRifles();InfantryPoseBlendBuild.Prepare();
        PrepareScenes();
        PrepareResourceData();
        string product=PlayerSettings.productName;var target=UnityEditor.Build.NamedBuildTarget.Standalone;
        string identifier=PlayerSettings.GetApplicationIdentifier(target);
        try
        {
            PlayerSettings.productName="ETW2";PlayerSettings.SetApplicationIdentifier(target,"com.andrewcote.etw2.game");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=Scenes,locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Unified game build failed: "+report.summary.result);
            Debug.Log("UNIFIED_GAME_BUILD_PASSED campaign, standalone scenarios and tactical campaign battles");
        }
        finally {PlayerSettings.productName=product;PlayerSettings.SetApplicationIdentifier(target,identifier);}
    }
}
