using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BayonetPresentationVerification
{
    const string Output="Artifacts/BayonetPresentation";
    static void Require(bool value,string message){if(!value)throw new Exception("Bayonet presentation: "+message);}
    public static void BakeAndVerify(){InfantryAssetBuild.BakeBayonetOrders();Verify();Capture();}
    public static void Verify()
    {
        Directory.CreateDirectory(Output);var log=new List<string>();
        BayonetOrdersVerification.Verify();
        var library=Resources.Load<BayonetPresentationLibrary>("Bayonets/Library");Require(library&&library.Steel,"Blade material and pose library exist");
        foreach(string guid in AssetDatabase.FindAssets("t:UnitVisualLibrary",new[]{"Assets/Generated/UnitRoster","Assets/Resources/RegionalRoster"}))
        {var roster=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(AssetDatabase.GUIDToAssetPath(guid));if(!roster||roster.Family=="sword"||roster.BakeVersion<=0)continue;foreach(var lod in roster.Set.Lods)Require(library.Resolve(roster.Set.Material,Array.IndexOf(roster.Set.Lods,lod))!=null,"Roster handling exists: "+roster.Faction+"-"+roster.Family);}
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=64,EnableAI=false,EnableLandscape=false});
        var unit=world.Battalions[0];int soldier=Enumerable.Range(unit.SoldierStart,unit.SoldierCount).First(i=>!world.Soldiers.Captains[i]&&!world.Soldiers.PikeSergeants[i]);
        Require(!unit.BayonetsFixed&&unit.BayonetOrderTime<0,"Initial weapons are bare, no spurious action");
        Require(world.SetBayonets(unit.Id,true)&&unit.BayonetOrderTime==world.Time,"Actual changed order timestamps animation");
        Require(BayonetPresentationTiming.Handling(world,unit,soldier,world.Time+.5f,out var phase)&&phase>0&&phase<1,"Fix handling plays finite progress");
        float started=unit.BayonetOrderTime;world.StepMovement(.1f);world.SetBayonets(unit.Id,true);Require(unit.BayonetOrderTime==started,"Duplicate orders do not restart action");
        world.SetBayonets(unit.Id,false);Require(!unit.BayonetsFixed&&unit.BayonetOrderTime==world.Time,"Unfix removes gameplay state and starts reverse action");
        world.StepMovement(.5f);world.SetBayonets(unit.Id,true);world.StepMovement(.7f);
        float before=BayonetPresentationTiming.Progress(unit,world.Time);world.SetBayonets(unit.Id,false);
        Require(Mathf.Abs(before-(1-BayonetPresentationTiming.Progress(unit,world.Time)))<.00001f,"Rapid reversal keeps the same handling pose");
        unit.InMelee=true;Require(!BayonetPresentationTiming.Handling(world,unit,soldier,world.Time+.1f,out _),"Combat takes priority over socket handling");unit.InMelee=false;
        Require(!BayonetPresentationTiming.Handling(world,unit,soldier,world.Time+BayonetPresentationTiming.Duration+.01f,out _),"Order action finishes");
        int clips=0,poses=0;foreach(var clip in library.Orders)
        {
            foreach(var track in new[]{clip.Standing,clip.Kneeling,clip.March,clip.Run,clip.UnfixMarch,clip.UnfixRun})
            {
                Require(track!=null&&track.Length>=20,"Every weapon/LOD has standing, kneeling and moving order clips");
                foreach(var pose in track)
                {
                    Require(library.Locate(pose,out var socket,out var rotation),"Barrel fit for "+pose.name);
                    Require(library.Locate(pose,out var next,out var nextRotation,true),"Morph barrel fit for "+pose.name);
                    var blade=BayonetPresentationLibrary.Blade(socket,rotation,next,nextRotation);
                    Require(blade.triangles.Length/3==14,"Blade remains a small 14-triangle mesh");
                    Require((blade.vertices[12]-socket).magnitude>.4f,"Bayonet has visible blade length");
                    UnityEngine.Object.DestroyImmediate(blade);poses++;
                }
            }
            Require(clip.Transfer!=null&&clip.Transfer.Length==6,"Six body/prop transfer tracks");
            foreach(var track in clip.Transfer){Require(track!=null&&track.Props.Length==track.Bodies.Length,"Each body has exactly one blade/sheath prop pose");foreach(var prop in track.Props)Require(prop&&prop.vertexCount==21,"One blade and sheath with stable topology");}
            var first=clip.Transfer[0].Props[0].vertices;var last=clip.Transfer[0].Props[clip.Transfer[0].Props.Length-1].vertices;
            Vector3 Start=(first[4]+first[5]+first[6]+first[7])*.25f,End=(last[4]+last[5]+last[6]+last[7])*.25f;
            Require(Vector3.Distance(Start,End)>.4f,"Blade travels from belt to muzzle");library.Locate(clip.Standing[clip.Standing.Length-1],out var seated,out _);Require(Vector3.Distance(End,seated)<.002f,"Final blade is seated on actual barrel");
            clips++;
        }
        var sets=new List<InfantryCrowdSet>();var infantry=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");sets.Add(infantry.French);sets.Add(infantry.British);
        var cartridge=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");if(cartridge)sets.AddRange(new[]{cartridge.French,cartridge.British,cartridge.SingleFrench,cartridge.SingleBritish});
        foreach(string guid in AssetDatabase.FindAssets("t:UnitVisualLibrary",new[]{"Assets/Generated/UnitRoster","Assets/Resources/RegionalRoster"}))
        {var roster=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(AssetDatabase.GUIDToAssetPath(guid));if(roster&&roster.Family!="sword"&&roster.BakeVersion>0)sets.Add(roster.Set);}
        int fitted=0;foreach(var set in sets)foreach(var lod in set.Lods)
        {
            var tracks=new List<Mesh[]>{lod.Idle,lod.Fire,lod.Reload,lod.KneelingReload,lod.March,lod.Run,lod.Charge};
            if(lod.Locomotion!=null)foreach(var gait in lod.Locomotion)tracks.AddRange(gait.Loops());
            if(lod.Melee!=null)foreach(var action in lod.Melee){tracks.Add(action.Poses);tracks.Add(action.Kneeling);}
            foreach(var track in tracks.Where(t=>t!=null))foreach(var pose in track)
            {Require(library.Locate(pose,out _,out _),"Production rifle anchor: "+pose.name);fitted++;}
        }
        log.Add($"PASS order state, duplicate orders, combat priority, completed actions; {clips} order variants / {poses} order poses; {fitted} existing production rifle poses fitted.");
        File.WriteAllLines(Output+"/verification.txt",log);Debug.Log("BAYONET_PRESENTATION_VERIFIED "+log[0]);
    }
    public static void Capture()
    {
        Directory.CreateDirectory(Output);var library=Resources.Load<BayonetPresentationLibrary>("Bayonets/Library");
        var setup=EditorSceneManager.GetSceneManagerSetup();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var camera=new GameObject("Bayonet review camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.2f;camera.backgroundColor=new Color(.12f,.15f,.2f);camera.clearFlags=CameraClearFlags.SolidColor;
        var sun=new GameObject("Review sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(40,-30,0);
        RenderSettings.fog=false;Shader.SetGlobalFloat("_BattleAtlasBlend",0);
        Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.65f,.7f,.8f,1));Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.6f,.65f,.7f,1));Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.4f,.4f,.45f,1));
        var body=new GameObject("Battle order pose");var filter=body.AddComponent<MeshFilter>();var renderer=body.AddComponent<MeshRenderer>();
        var bladeObject=new GameObject("Fixed bayonet");var bladeFilter=bladeObject.AddComponent<MeshFilter>();bladeObject.AddComponent<MeshRenderer>().sharedMaterial=library.Steel;
        var target=new RenderTexture(320,340,24);camera.targetTexture=target;var tile=new Texture2D(320,340,TextureFormat.RGB24,false);var props=new MaterialPropertyBlock();
        try
        {
            foreach(var clip in library.Orders.Where(c=>c.Lod==0))foreach(bool fix in new[]{true,false})
            {
                var sheet=new Texture2D(320*6,340*2,TextureFormat.RGB24,false);
                renderer.sharedMaterial=clip.Uniform;
                for(int view=0;view<2;view++)for(int col=0;col<6;col++)
                {
                    float phase=col/5f;int frame=AnimationPlayback.Frame(clip.Standing.Length,fix?phase:1-phase,false,out float blend);var pose=clip.Standing[frame];filter.sharedMesh=pose;
                    camera.transform.position=new Vector3(view==0?3:6,1.65f,view==0?6:0);camera.transform.LookAt(new Vector3(0,1,0));
                    props.SetFloat("_PoseBlend",blend);renderer.SetPropertyBlock(props);
                    bladeFilter.sharedMesh=clip.Transfer[0].Props[frame];bladeObject.SetActive(true);bladeObject.GetComponent<Renderer>().SetPropertyBlock(props);
                    camera.Render();RenderTexture.active=target;tile.ReadPixels(new Rect(0,0,320,340),0,0);tile.Apply();sheet.SetPixels(col*320,(1-view)*340,320,340,tile.GetPixels());
                }
                sheet.Apply();File.WriteAllBytes(Output+"/"+clip.Name+"-"+(fix?"fix":"unfix")+".png",sheet.EncodeToPNG());UnityEngine.Object.DestroyImmediate(sheet);
            }
            Debug.Log("BAYONET_PRESENTATION_CAPTURED "+Output);
        }
        finally{RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(tile);if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.CloseScene(scene,true);}
    }
}
