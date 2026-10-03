using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CampaignArmyPresentationBuild
{
    // A separate, disposable development bundle with a distinct save identity.
    public static void Build()
    {
        string path=Environment.GetEnvironmentVariable("ETW2_CAMPAIGN_UI_QA_APP");
        if(string.IsNullOrEmpty(path)||!Path.IsPathRooted(path))throw new Exception("Explicit absolute QA output required.");
        string product=PlayerSettings.productName;var target=UnityEditor.Build.NamedBuildTarget.Standalone;
        string identifier=PlayerSettings.GetApplicationIdentifier(target);
        try
        {
            PlayerSettings.productName="ETW2 Campaign UI QA";
            PlayerSettings.SetApplicationIdentifier(target,"com.etw2.task24.campaign-ui-qa");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{"Assets/Scenes/Campaign.unity"},
                locationPathName=path,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception(report.summary.result.ToString());
            Debug.Log("CAMPAIGN_ARMY_UI_QA_BUILD_PASSED");
        }
        finally{PlayerSettings.productName=product;PlayerSettings.SetApplicationIdentifier(target,identifier);}
    }
}
