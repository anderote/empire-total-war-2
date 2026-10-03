using System;
using System.IO;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

// Export actual baked battle meshes for a reproducible, headless visual review.
public static class RegionalArmyPreview
{
    [Serializable] class Pose {public string name,texture;public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles;}
    public static void Finish()
    {
        TroopSourceBuild.Build();RegionalArmyVerification.Verify();Export();
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        var runtime=new GameObject("Ming versus Indian States").AddComponent<EmpireBattle.BattleRuntime>();
        runtime.Config=EmpireBattle.UnitRoster.CreateBattle("ming","india");
        Directory.CreateDirectory("Assets/Scenes");UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,"Assets/Scenes/RegionalArmyBattle.unity");
    }
    public static void Export()
    {
        Directory.CreateDirectory("Artifacts/RegionalArmies/BakedPreviews");
        foreach(var faction in new[]{"ming","india"})
        {
            foreach(var family in new[]{"line","light","sword"})
            {
                var set=Resources.Load<UnitVisualLibrary>("RegionalRoster/"+faction+"-"+family+"/Library").Set;
                Save(faction+"-"+family,set.Lods[0].Idle[0],set.Material);
                Save(faction+"-"+family+"-march",set.Lods[0].March[3],set.Material);
            }
            var mounted=Resources.Load<CavalryCrowdLibrary>("RegionalCavalry/"+faction+"/CavalryCrowdLibrary").French;
            Save(faction+"-cavalry",mounted.Lods[0].Clip(MountedMotion.Idle).Poses[0],mounted.Material);
        }
        Debug.Log("REGIONAL_PREVIEWS_EXPORTED");
    }
    static void Save(string name,Mesh mesh,Material material)
    {
        var pose=new Pose{name=name,vertices=mesh.vertices,normals=mesh.normals,uv=mesh.uv,triangles=mesh.triangles,texture=AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"))};
        File.WriteAllText("Artifacts/RegionalArmies/BakedPreviews/"+name+".json",JsonUtility.ToJson(pose));
    }
}
