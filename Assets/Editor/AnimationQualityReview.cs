using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Offline review of production meshes. Does not build or launch a game or save a scene.
public static class AnimationQualityReview
{
    sealed class Track
    {
        public string Name;
        public Mesh[] Poses;
        public Material Material;
        public bool Loop, Morph;
    }
    public static void Capture()
    {
        string output="Artifacts/AnimationQuality/Before";
        var args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"--animation-review-output");
        if(flag>=0&&flag+1<args.Length)output=args[flag+1];
        Directory.CreateDirectory(output);
        var setup=EditorSceneManager.GetSceneManagerSetup();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var tracks=new List<Track>();
        var infantry=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var faction in new[]{"French","British"})
        {
            var set=faction=="French"?infantry.French:infantry.British;var lod=set.Lods[0];
            void Add(string name,Mesh[] poses,bool loop=false,bool morph=false)
            {if(poses?.Length>0)tracks.Add(new Track{Name=faction+"-"+name,Poses=poses,Material=set.Material,Loop=loop,Morph=morph});}
            Add("01-fire",lod.Fire);Add("02-reload",lod.Reload);Add("03-kneeling-reload",lod.KneelingReload);
            Add("04-lower-to-kneel",new[]{lod.LowerToKneel},false,true);Add("05-kneeling-fire",lod.KneelingFire);
            Add("06-walk",lod.Locomotion[1].Walk,true);Add("07-march",lod.Locomotion[1].March,true);Add("08-run",lod.Locomotion[1].Run,true);
            for(int v=0;v<lod.DeathVariants.Length;v++)Add("09-death-"+v,lod.DeathVariants[v].Poses);
            for(int r=0;r<4;r++)
            {
                Add("10-command-"+r+"-march",lod.Command[r].March,true);
                if(r==0)Add("11-captain-slash",lod.Command[r].OfficerSlash);
                if(r==3){Add("12-sergeant-thrust",lod.Command[r].PikeThrust);Add("13-sergeant-kneeling",lod.Command[r].KneelingStrike);}
            }
            foreach(var clip in lod.Melee)Add("30-melee-"+clip.Action,clip.Poses);
        }
        var cavalry=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
        foreach(var clip in cavalry.French.Lods[0].Clips)
            tracks.Add(new Track{Name="French-20-cavalry-"+clip.Motion,Poses=clip.Poses,Material=cavalry.French.Material,Loop=clip.Loop});
        var camera=new GameObject("Animation review camera").AddComponent<Camera>();
        camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.16f,.20f);
        camera.nearClipPlane=.01f;camera.farClipPlane=100;
        var sun=new GameObject("Review light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(45,-30,0);
        RenderSettings.ambientLight=Color.gray;RenderSettings.fog=false;
        Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.7f,.75f,.85f,1));
        Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.65f,.67f,.7f,1));
        Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.45f,.45f,.5f,1));
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Contact reference floor";
        ground.transform.position=Vector3.down*.01f;ground.transform.localScale=Vector3.one*3;
        var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.12f,.15f,.18f));
        ground.GetComponent<Renderer>().sharedMaterial=floorMaterial;
        var item=new GameObject("Production pose");var filter=item.AddComponent<MeshFilter>();var renderer=item.AddComponent<MeshRenderer>();
        const int width=280,height=300,columns=6;
        var target=new RenderTexture(width,height,24);camera.targetTexture=target;
        var tile=new Texture2D(width,height,TextureFormat.RGB24,false);
        var properties=new MaterialPropertyBlock();var report=new List<string>();
        try
        {
            foreach(var track in tracks)
            {
                var bounds=track.Poses[0].bounds;foreach(var pose in track.Poses)bounds.Encapsulate(pose.bounds);
                camera.orthographicSize=Mathf.Max(1.15f,Mathf.Max(bounds.size.y,bounds.size.z)*.57f);
                var sheet=new Texture2D(width*columns,height*2,TextureFormat.RGB24,false);
                renderer.sharedMaterial=track.Material;
                for(int view=0;view<2;view++)
                {
                    camera.transform.position=bounds.center+(view==0?new Vector3(3,1.2f,6):new Vector3(6,.5f,0));camera.transform.LookAt(bounds.center);
                    for(int col=0;col<columns;col++)
                    {
                        float phase=col/(float)(columns-1);
                        int index=AnimationPlayback.Frame(track.Poses.Length,phase,track.Loop,out float blend);
                        filter.sharedMesh=track.Poses[index];
                        properties.SetFloat("_PoseBlend",track.Morph?phase:blend);
                        renderer.SetPropertyBlock(properties);camera.Render();RenderTexture.active=target;
                        tile.ReadPixels(new Rect(0,0,width,height),0,0);tile.Apply();sheet.SetPixels(col*width,(1-view)*height,width,height,tile.GetPixels());
                    }
                }
                sheet.Apply();File.WriteAllBytes(Path.Combine(output,track.Name+".png"),sheet.EncodeToPNG());UnityEngine.Object.DestroyImmediate(sheet);
                report.Add(track.Name+": "+track.Poses.Length+" poses; "+(track.Morph?"posture morph":track.Loop?"loop":"one shot"));
            }
            File.WriteAllLines(Path.Combine(output,"capture-index.txt"),report);
            item.SetActive(false);CaptureCrew(camera,target,tile,output);
            Debug.Log("ANIMATION_QUALITY_CAPTURED "+tracks.Count+" production tracks, front and side, six phases: "+output);
        }
        finally
        {
            RenderTexture.active=null;camera.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(tile);
            UnityEngine.Object.DestroyImmediate(floorMaterial);
            if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.CloseScene(scene,true);
        }
    }

    static void CaptureCrew(Camera camera,RenderTexture target,Texture2D tile,string output)
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,FrenchArtillery=1,BritishArtillery=0,EnableLandscape=false,EnableAI=false});
        var guns=new ArtillerySystem(world);var gun=guns.Guns[0];gun.Position=Vector2.zero;gun.Facing=0;
        var owner=new GameObject("Artillery animation review");var crew=owner.AddComponent<ArtilleryCrewView>();crew.Initialize(world,guns,camera);
        var meshes=EmpireBattle.SupportingArt.ProceduralSupportingArt.CreateFieldCannonMeshes();
        var materials=new List<Material>();var parts=new List<Transform>();
        foreach(var pair in new[]{(meshes.Wood,new Color(.3f,.22f,.10f)),(meshes.Iron,new Color(.13f,.14f,.15f)),(meshes.Bronze,new Color(.42f,.31f,.14f))})
        {
            var part=new GameObject("Gun part");part.transform.SetParent(owner.transform);part.AddComponent<MeshFilter>().sharedMesh=pair.Item1;
            var material=new Material(Resources.Load<Material>("CannonLit"));material.SetColor("_BaseColor",pair.Item2);materials.Add(material);
            part.AddComponent<MeshRenderer>().sharedMaterial=material;parts.Add(part.transform);
        }
        camera.orthographicSize=3.3f;camera.transform.position=new Vector3(5,4,6);camera.transform.LookAt(new Vector3(0,.9f,-.15f));
        int width=target.width,height=target.height;
        try
        {
            foreach(var phase in new[]{ArtilleryPhase.Ready,ArtilleryPhase.Firing,ArtilleryPhase.Recoil,ArtilleryPhase.Sponging,ArtilleryPhase.Loading,ArtilleryPhase.Ramming,ArtilleryPhase.RunUp,ArtilleryPhase.Aiming})
            {
                gun.Phase=phase;gun.PhaseElapsed=0;
                var sheet=new Texture2D(width*6,height,TextureFormat.RGB24,false);
                for(int col=0;col<6;col++)
                {
                    float time=gun.PhaseDuration(phase)*col/5f;
                    do
                    {
                        float step=Mathf.Min(.02f,Mathf.Max(0,time-gun.PhaseElapsed));
                        if(step>0){world.StepMovement(step);gun.PhaseElapsed+=step;guns.StepCrewPositions(gun,step);}
                        crew.Sample();
                    }while(gun.PhaseElapsed<time-.0001f);
                    foreach(var part in parts)part.position=guns.GunCenter(gun);
                    Vector3 pivot=new Vector3(0,.9f,-.23f);Quaternion tilt=Quaternion.Euler(-gun.Elevation,0,0);
                    parts[2].position+=pivot-tilt*pivot;parts[2].rotation=tilt;
                    camera.Render();RenderTexture.active=target;tile.ReadPixels(new Rect(0,0,width,height),0,0);tile.Apply();
                    sheet.SetPixels(col*width,0,width,height,tile.GetPixels());
                }
                sheet.Apply();File.WriteAllBytes(Path.Combine(output,"Artillery-"+(int)phase+"-"+phase+".png"),sheet.EncodeToPNG());UnityEngine.Object.DestroyImmediate(sheet);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);foreach(var material in materials)UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(meshes.Wood);UnityEngine.Object.DestroyImmediate(meshes.Iron);UnityEngine.Object.DestroyImmediate(meshes.Bronze);
        }
    }
}
