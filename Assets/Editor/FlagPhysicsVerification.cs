using System;
using System.IO;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static class FlagPhysicsVerification
{
    static void Require(bool pass,string message){if(!pass)throw new Exception("Flag physics: "+message);}
    public static void Verify()
    {
        var shader=Resources.Load<Shader>("RegimentalSilk");Require(shader&&!ShaderUtil.ShaderHasError(shader),"Silk shader compiles");
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        foreach(var poses in new[]{lod.Command[2].Idle,lod.Command[2].March})foreach(var source in poses){
            var prepared=CrowdInfantryRenderer.PrepareStandard(source);
            Require(source.triangles.Length-prepared.triangles.Length>1500,"Old cloth is removed from "+source.name);
            Require(prepared.bounds.max.y>3.55f,"Raised staff clears ranks");
            Require(source.bounds.max.y<3.3f,"Source meshes stay unchanged");
            var original=source.vertices;var raised=prepared.vertices;
            for(int i=0;i<original.Length;i++)if(Mathf.Abs(original[i].z-.37f)>.055f||Mathf.Abs(original[i].x+.08f)>.045f)
                Require(original[i]==raised[i],"Raising the staff preserves the bearer");
            UnityEngine.Object.DestroyImmediate(prepared);
        }
        float greatestStretch=0;
        foreach(float angle in new[]{0f,90f,180f,270f}){
            using var flag=new StandardCloth(1.4f,1.02f,new Color(.1f,.25f,.7f),true,true,7);
            var initial=(Vector3[])flag.Positions.Clone();
            for(int frame=0;frame<600;frame++){
                flag.Advance(frame/60f,Quaternion.Euler(0,angle,0),frame>=300);
                var p=flag.Positions;
                for(int i=0;i<p.Length;i++){
                    Require(float.IsFinite(p[i].x)&&float.IsFinite(p[i].y)&&float.IsFinite(p[i].z),"Finite cloth under all wind directions");
                    if(i%17==0)Require((p[i]-initial[i]).sqrMagnitude<1e-10f,"Hoist remains pinned");
                    if(i%17<16)greatestStretch=Mathf.Max(greatestStretch,Vector3.Distance(p[i],p[i+1])/(1.4f/16));
                    Require(Vector3.Distance(p[i],initial[i])<3,"Fabric stays attached");
                }
            }
            Require(Vector3.Distance(flag.Positions[16],initial[16])>.1f,"Free edge responds to forces");
            var paused=(Vector3[])flag.Positions.Clone();flag.Advance(599/60f,Quaternion.identity,false);
            for(int i=0;i<paused.Length;i++)Require(paused[i]==flag.Positions[i],"Pause freezes cloth");
            flag.Advance(1000,Quaternion.identity,true);flag.Advance(0,Quaternion.identity,false);
            Require(float.IsFinite(flag.Positions[16].z),"Culling gap and time reset remain stable");
        }
        Require(greatestStretch<1.45f,"Fabric tension limits stretch: "+greatestStretch);
        using(var distant=new StandardCloth(1.4f,1.02f,Color.blue,true,true,2)){
            distant.AdvanceDistant(1,false);var frozen=(Vector3[])distant.Positions.Clone();distant.AdvanceDistant(1,false);
            for(int i=0;i<frozen.Length;i++)Require(frozen[i]==distant.Positions[i],"Distant LOD freezes while paused");
            distant.Advance(1.1f,Quaternion.identity,true);Require(float.IsFinite(distant.Positions[16].z),"Distant/near transition is stable");
        }
        Debug.Log("FLAG_PHYSICS_VERIFIED: raised staff, cloth replacement, 10-second loops at four headings, marching, pinned hoist, pause/reset; max stretch="+greatestStretch);
    }
    public static void Preview()
    {
        Verify();Directory.CreateDirectory("Artifacts/FlagPhysics");
        var cameraObject=new GameObject("Flag preview camera");var camera=cameraObject.AddComponent<Camera>();
        camera.transform.position=new Vector3(-3.4f,3.6f,6.7f);camera.transform.LookAt(new Vector3(-.5f,1.9f,.37f));
        camera.fieldOfView=32;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.31f,.38f);
        var lightObject=new GameObject("Flag preview sun");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(35,-40,0);
        var target=new RenderTexture(1200,900,24);camera.targetTexture=target;
        using var flag=new StandardCloth(1.4f,1.02f,new Color(.08f,.22f,.64f),false,true,3);
        var clothObject=new GameObject("Preview silk");clothObject.AddComponent<MeshFilter>().sharedMesh=flag.Mesh;clothObject.AddComponent<MeshRenderer>().sharedMaterial=flag.Material;
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        var bearerMesh=CrowdInfantryRenderer.PrepareStandard(library.French.Lods[0].Command[2].Idle[0]);
        var bearer=new GameObject("Raised standard bearer");bearer.AddComponent<MeshFilter>().sharedMesh=bearerMesh;bearer.AddComponent<MeshRenderer>().sharedMaterial=library.French.Material;
        using var pennant=new StandardCloth(.82f,.16f,new Color(.08f,.22f,.64f),false,true,4,3.56f);
        var pennantObject=new GameObject("Preview pennant");pennantObject.AddComponent<MeshFilter>().sharedMesh=pennant.Mesh;pennantObject.AddComponent<MeshRenderer>().sharedMaterial=pennant.Material;
        try{
            for(int frame=0;frame<=360;frame++){
                flag.Advance(frame/60f,Quaternion.identity,false);
                pennant.Advance(frame/60f,Quaternion.identity,false);
                if(frame!=120&&frame!=240&&frame!=360)continue;
                camera.Render();RenderTexture.active=target;
                var output=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);output.ReadPixels(new Rect(0,0,target.width,target.height),0,0);output.Apply();
                File.WriteAllBytes("Artifacts/FlagPhysics/cloth-"+frame+".png",output.EncodeToPNG());UnityEngine.Object.DestroyImmediate(output);
            }
        }
        finally{
            RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(clothObject);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);
            UnityEngine.Object.DestroyImmediate(bearer);UnityEngine.Object.DestroyImmediate(bearerMesh);UnityEngine.Object.DestroyImmediate(pennantObject);
        }
        Debug.Log("FLAG_PHYSICS_PREVIEW_COMPLETE");
    }
}
