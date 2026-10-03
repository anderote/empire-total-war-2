using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class InfantryPerformanceBuild
{
    public static void VerifyAndBuildProfile() { InfantryPerformanceVerification.Verify(); Build(true); }
    public static void BuildMatchedPlaytests() { ArtilleryRecoveryVerification.Verify(); InfantryPerformanceVerification.Verify(); RoundshotHitVerification.BuildMatchedPlaytests(); }
    public static void VerifyAndBuildRelease() { InfantryPerformanceVerification.Verify(); Build(false); }
    public static void BuildFastForwardPlaytests() { CavalryCombatVerification.VerifyAll(); CavalryCombatVerification.VerifySquareContactFire(); CavalryCombatVerification.VerifyScale(); BattlePlaytestBuild.BuildAll(); }
    public static void BuildProfile() => Build(true);
    public static void BuildRelease() => Build(false);
    static void Build(bool development)
    {
        BattleCinematicBuild.Prepare();
        bool previous=PlayerSettings.enableFrameTimingStats;
        try
        {
            PlayerSettings.enableFrameTimingStats=true;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{"Assets/Scenes/InfantryBattle.unity"},
                locationPathName="Builds/Infantry Performance.app",target=BuildTarget.StandaloneOSX,
                options=development?BuildOptions.Development:BuildOptions.None });
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Performance player build failed");
            Debug.Log("INFANTRY_PERFORMANCE_BUILD_PASSED development="+development);
        }
        finally { PlayerSettings.enableFrameTimingStats=previous; }
    }
}
