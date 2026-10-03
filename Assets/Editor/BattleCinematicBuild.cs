using System.IO;
using EmpireBattle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BattleCinematicBuild
{
    public static void Prepare()
    {
        // Effects are switched at runtime; retain variants even when build-time analysis
        // cannot observe their eventual Volume state.
        var stripping=GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
        if(stripping!=null)stripping.stripUnusedPostProcessingVariants=false;
        const string path = "Assets/Scripts/Battle/Atmosphere/Resources/BattleCinematicProfile.asset";
        var existing=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if(existing){
            BattleCinematics.ApplyReadabilityGrade(existing);
            foreach(var component in existing.components)EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(existing);AssetDatabase.SaveAssets();return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Battle cinematic grade and close focus";
        BattleCinematics.ConfigureProfile(profile);
        AssetDatabase.CreateAsset(profile, path);
        foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }
}
