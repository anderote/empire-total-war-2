using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Development review only: deliberately excludes the campaign scene and saved launchers.
public static class PeriodUIReviewBuild
{
    public static void Build()
    {
        foreach (string name in new[] { "IMFellEnglish", "IMFellEnglish-Italic", "Lato-Regular", "Lato-Bold" })
            if (!Resources.Load<Font>("UI/Fonts/" + name)) throw new Exception("Missing bundled UI font: " + name);
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/LineBattle.unity" },
            locationPathName = "Artifacts/PeriodUIReview/Period UI Review.app",
            target = BuildTarget.StandaloneOSX, options = BuildOptions.None });
        if (result.summary.result != BuildResult.Succeeded) throw new Exception("Period UI build failed: " + result.summary.result);
        Debug.Log("PERIOD_UI_BUILD_PASSED");
    }
}
