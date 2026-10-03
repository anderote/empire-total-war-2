using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Isolated unified development player for native battle profiling. Never publishes.
public static class LargeBattlePerformanceBuild
{
    const string Output="Builds/Development/ETW2 Battle Performance.app";
    public static void Build()=>BuildPlayer(false);
    public static void UpdateScripts()=>BuildPlayer(true);
    public static void VerifyAndUpdateScripts()
    {LargeBattleOptimizationVerification.VerifyNativeOptimizations();BuildPlayer(true);}
    public static void VerifyRenderWorkAndUpdateScripts()
    {LargeBattleOptimizationVerification.VerifyRenderUploadAndCapture();BuildPlayer(true);}
    public static void VerifyContactMathAndUpdateScripts()
    {LargeBattleOptimizationVerification.VerifyContactMath();BuildPlayer(true);}
    static void BuildPlayer(bool scriptsOnly)
    {
        string executable=Path.GetFullPath(Output)+"/Contents/MacOS/ETW2 Battle Performance";
        var info=new System.Diagnostics.ProcessStartInfo("/bin/ps","-axo args=") {
            UseShellExecute=false,RedirectStandardOutput=true };
        using(var process=System.Diagnostics.Process.Start(info))
        {
            string listing=process.StandardOutput.ReadToEnd();process.WaitForExit();
            foreach(string line in listing.Split('\n'))
                if(line.Trim()==executable||line.Trim().StartsWith(executable+" ",StringComparison.Ordinal))
                    throw new InvalidOperationException("Performance player is running; leave the session intact.");
        }
        string product=PlayerSettings.productName;
        bool timing=PlayerSettings.enableFrameTimingStats;
        var target=UnityEditor.Build.NamedBuildTarget.Standalone;
        string identifier=PlayerSettings.GetApplicationIdentifier(target);
        try
        {
            PlayerSettings.productName="ETW2 Battle Performance";
            PlayerSettings.SetApplicationIdentifier(target,"com.andrewcote.etw2.battle-performance");
            PlayerSettings.enableFrameTimingStats=true;
            var options=BuildOptions.Development;
            if(scriptsOnly)options|=BuildOptions.BuildScriptsOnly;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=UnifiedGameBuild.Scenes,
                locationPathName=Output,target=BuildTarget.StandaloneOSX,options=options});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Battle performance build failed: "+report.summary.result);
            Debug.Log("LARGE_BATTLE_NATIVE_BUILD_PASSED "+Output+" scriptsOnly="+scriptsOnly);
        }
        finally
        {
            PlayerSettings.productName=product;PlayerSettings.enableFrameTimingStats=timing;
            PlayerSettings.SetApplicationIdentifier(target,identifier);
        }
    }
}
