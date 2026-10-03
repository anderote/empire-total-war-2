using System;
using System.IO;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Separate app, shared full-army defaults with the main battle playtest.
public static class InfantryBattleBuild
{
    public static void BuildMatchedPlaytests() { BattlePlaytestBuild.BuildAll(); }

    public static void BuildMac()
    {
        BattleCinematicBuild.Prepare();
        BattleEffectsVerification.PrepareResources();
        ArtilleryBuild.PrepareCrewAssets();
        ArtilleryBuild.Verify();
        var library = Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        if (!library || library.French == null || library.British == null)
            throw new Exception("The baked French/British infantry library is required.");
        foreach (var set in new[] { library.French, library.British })
            if (!set.Material || set.Lods == null || set.Lods.Length == 0 ||
                set.Lods[0].Idle == null || set.Lods[0].Idle.Length == 0 || !set.Lods[0].Idle[0])
                throw new Exception("The infantry library has incomplete materials or poses.");
        BattleBuild.Verify();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var runtime = new GameObject("Infantry battle runtime").AddComponent<BattleRuntime>();
        // Match the main battle's shared playtest configuration instead of maintaining a smaller scenario.
        runtime.Config = BattleConfig.CreatePlaytest();
        Directory.CreateDirectory("Assets/Scenes");
        const string scenePath = "Assets/Scenes/InfantryBattle.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = "Builds/Infantry Battle.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.CleanBuildCache
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Infantry battle build failed: " + report.summary.result);
        Debug.Log("INFANTRY_BATTLE_BUILD_PASSED " + report.summary.totalSize);
    }
}
