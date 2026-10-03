using System.IO;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InfantryCommandVisualReview
{
    public static void Capture()
    {
        InfantryCommandCoverage.Verify();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        const int width=240,height=320;
        var camera=new GameObject("Command review camera").AddComponent<Camera>();
        camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.13f,.16f,.20f);camera.nearClipPlane=.01f;camera.farClipPlane=100;
        var light=new GameObject("Review light").AddComponent<Light>();
        light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,-30,0);
        RenderSettings.ambientLight=Color.gray;RenderSettings.fog=false;
        Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.7f,.75f,.85f,1));
        Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.65f,.67f,.7f,1));
        Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.45f,.45f,.5f,1));
        var item=new GameObject("Baked command pose");
        var filter=item.AddComponent<MeshFilter>();var renderer=item.AddComponent<MeshRenderer>();
        var properties=new MaterialPropertyBlock();properties.SetFloat("_PoseBlend",0);properties.SetVector("_RegimentUniform",Vector4.one);
        renderer.SetPropertyBlock(properties);
        var target=new RenderTexture(width,height,24);camera.targetTexture=target;
        var tile=new Texture2D(width,height,TextureFormat.RGB24,false);
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        string output="Artifacts/InfantryCommandCoverage/VisualReview";Directory.CreateDirectory(output);
        try
        {
            foreach(string faction in UnitRoster.Factions)
            {
                var unit=new BattalionState{Faction=faction,Role=UnitRole.Guard,Kind=UnitKind.Infantry};
                var set=library.ResolveCommand(unit);renderer.sharedMaterial=set.Material;
                var sheet=new Texture2D(width*5,height*2,TextureFormat.RGB24,false);
                try
                {
                    for(int role=0;role<5;role++)for(int motion=0;motion<2;motion++)
                    {
                        var command=set.Lods[0].Command[role];var poses=motion==0?command.Idle:command.March;
                        filter.sharedMesh=poses[motion==0?0:poses.Length/3];
                        var bounds=filter.sharedMesh.bounds;
                        camera.orthographicSize=Mathf.Max(1.15f,Mathf.Max(bounds.size.y,bounds.size.z)*.62f);
                        camera.transform.position=bounds.center+new Vector3(3,.8f,6);camera.transform.LookAt(bounds.center);
                        camera.Render();RenderTexture.active=target;tile.ReadPixels(new Rect(0,0,width,height),0,0);tile.Apply();
                        sheet.SetPixels(role*width,(1-motion)*height,width,height,tile.GetPixels());
                    }
                    sheet.Apply();File.WriteAllBytes(output+"/"+faction+".png",sheet.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(sheet); }
            }
            File.WriteAllText(output+"/index.txt","Columns: officer, drummer, standard bearer, sergeant, mounted colonel. Rows: idle, march. Actual baked command meshes and faction atlas; live cloth is rendered separately in battle.\n");
            Debug.Log("INFANTRY_COMMAND_VISUAL_REVIEW_CAPTURED factions=10");
        }
        finally
        {
            RenderTexture.active=null;camera.targetTexture=null;target.Release();
            Object.DestroyImmediate(tile);Object.DestroyImmediate(target);
        }
    }
}
