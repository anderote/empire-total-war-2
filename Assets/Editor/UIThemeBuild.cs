using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Build the existing scenes for UI review without regenerating gameplay scenes.</summary>
public static class UIThemeBuild
{
    [MenuItem("Empire UI/Build review apps")]
    public static void BuildPreviews()
    {
        Directory.CreateDirectory("Builds/UI Review");
        Build("Assets/Scenes/Campaign.unity", "Campaign");
        Build("Assets/Scenes/LineBattle.unity", "Battle");
        Debug.Log("UI_REVIEW_BUILDS_PASSED");
    }
    public static void BuildBattlePreview() => Build("Assets/Scenes/LineBattle.unity", "Battle");

    static void Build(string scene, string name)
    {
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { scene }, locationPathName = "Builds/UI Review/" + name + ".app",
            target = BuildTarget.StandaloneOSX, options = BuildOptions.None });
        if (result.summary.result != BuildResult.Succeeded) throw new Exception(name + " UI review build failed: " + result.summary.result);
    }
}
