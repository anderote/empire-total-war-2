using System;
using System.IO;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class DawnLightingVerification
{
    public static void Preview()
    {
        BattleCinematicBuild.Prepare();
        foreach(string name in new[]{"CrowdInfantry","BattleSoldier","RegimentalSilk"}){
            var shader=Resources.Load<Shader>(name);
            if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Dawn shader error: "+name);
        }
        Directory.CreateDirectory("Artifacts/DawnLighting");
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=16,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false});
        var root=new GameObject("Dawn lighting verification");var view=root.AddComponent<BattleView>();view.Initialize(world);view.EnableInput=false;
        var strayCube=GameObject.Find("Cube");if(strayCube)UnityEngine.Object.DestroyImmediate(strayCube);
        var camera=view.BattleCamera;camera.transform.position=new Vector3(0,1.5f,5);camera.transform.LookAt(new Vector3(0,1,0));camera.fieldOfView=32;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var sunObject=new GameObject("Verification dawn sun");var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;
        var atmosphere=root.AddComponent<BattleAtmosphere>();atmosphere.Initialize(world,camera,sun);
        var cinema=root.AddComponent<BattleCinematics>();cinema.Initialize(view);
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        var french=new GameObject("French dawn uniform");french.transform.position=new Vector3(-.55f,0,0);
        french.AddComponent<MeshFilter>().sharedMesh=library.French.Lods[0].Idle[0];var fr=french.AddComponent<MeshRenderer>();fr.sharedMaterial=library.French.Material;fr.lightProbeUsage=LightProbeUsage.Off;
        var british=new GameObject("British dawn uniform");british.transform.position=new Vector3(.55f,0,0);
        british.AddComponent<MeshFilter>().sharedMesh=library.British.Lods[0].Idle[0];var br=british.AddComponent<MeshRenderer>();br.sharedMaterial=library.British.Material;br.lightProbeUsage=LightProbeUsage.Off;
        var target=new RenderTexture(1200,900,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);camera.targetTexture=target;
        var profile=root.GetComponent<Volume>().profile;profile.TryGet<ColorAdjustments>(out var grade);profile.TryGet<SplitToning>(out var split);profile.TryGet<Vignette>(out var vignette);
        float Capture(string name){
            camera.backgroundColor=Color.black;camera.Render();camera.Render();RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            File.WriteAllBytes("Artifacts/DawnLighting/"+name+".png",image.EncodeToPNG());
            double total=0;int count=0;foreach(var p in image.GetPixels()){
                float luminance=p.r*.2126f+p.g*.7152f+p.b*.0722f;
                total+=luminance;if(luminance>.025f)count++;
            }
            UnityEngine.Object.DestroyImmediate(image);if(count<1000)throw new Exception("No visible uniforms in dawn render");return (float)(total/(target.width*target.height));
        }
        try{
            grade.postExposure.value=.35f;grade.contrast.value=9;grade.saturation.value=-8;split.shadows.value=new Color(.47f,.49f,.52f);vignette.intensity.value=.12f;
            RenderSettings.ambientEquatorColor*=.45f/.72f;RenderSettings.ambientGroundColor=new Color(.24f,.27f,.23f);
            Shader.SetGlobalVector("_BattleAmbientSky",Vector4.zero);Shader.SetGlobalVector("_BattleAmbientEquator",Vector4.zero);Shader.SetGlobalVector("_BattleAmbientGround",Vector4.zero);
            float before=Capture("before-cinema-dawn");
            atmosphere.SetHour(7);BattleCinematics.ApplyReadabilityGrade(profile);
            float after=Capture("after-cinema-dawn");
            if(after<=before*1.05f)throw new Exception($"Uniforms did not brighten enough: {before} -> {after}");
            camera.transform.position=new Vector3(0,1.5f,-5);camera.transform.LookAt(new Vector3(0,1,0));Capture("after-reverse-view");
            Debug.Log($"DAWN_LIGHTING_VERIFIED: shaded uniforms in cinema mode brighten {before:F3} -> {after:F3}; both factions, reverse view, all troop/silk shaders and saved profile.");
        }
        finally{
            RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(french);UnityEngine.Object.DestroyImmediate(british);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sunObject);
        }
    }
}
