using System;
using System.Collections;
using System.IO;
using System.Reflection;
using EmpireBattle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Actual battle pose selection, instancing and bayonet attachments rendered by
// CrowdInfantryRenderer in an isolated offscreen editor scene; no native app.
public static class BayonetBattleReview
{
    static readonly bool Short=Array.IndexOf(Environment.GetCommandLineArgs(),"--bayonet-short")>=0;
    static bool Video=Array.IndexOf(Environment.GetCommandLineArgs(),"--bayonet-video")>=0;
    static readonly bool OffsetReview=Array.IndexOf(Environment.GetCommandLineArgs(),"--bayonet-offset-review")>=0;
    static readonly bool Preview=Array.IndexOf(Environment.GetCommandLineArgs(),"--melee-review-preview")>=0;
    static readonly string Output=Preview?"Artifacts/BayonetPresentation/Battle":OffsetReview?"Artifacts/BayonetPresentation/BayonetOffsetReview":"Artifacts/BayonetPresentation/Battle";
    public static void Capture()
    {
        MeleeRefinementVerification.VerifySimulation();
        MeleeRefinementVerification.Verify();
        var routine=CombinedFrames();string error=null;
        void OnLog(string text,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)error=text;}
        Application.logMessageReceived+=OnLog;
        void Tick(){try{
            if(error!=null)throw new Exception(error);
            if(routine.MoveNext())return;
            EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;EditorApplication.Exit(0);
        }catch(Exception e){EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;(routine as IDisposable)?.Dispose();Debug.LogException(e);EditorApplication.Exit(1);}}
        EditorApplication.update+=Tick;
    }
    static IEnumerator CombinedFrames()
    {
        if(Short){Video=Array.IndexOf(Environment.GetCommandLineArgs(),"--bayonet-video")>=0;var minimal=Frames();while(minimal.MoveNext())yield return minimal.Current;yield break;}
        bool videos=Video;Video=false;var sheets=Frames();while(sheets.MoveNext())yield return sheets.Current;
        if(videos){Video=true;var films=Frames();while(films.MoveNext())yield return films.Current;}
    }
    static IEnumerator Frames()
    {
        Directory.CreateDirectory(Output);var setup=EditorSceneManager.GetSceneManagerSetup();
        var names=OffsetReview?new[]{"musket-fixed","musket-bare","musket-kneel-fixed","musket-kneel-bare","magazine-fixed","magazine-bare","magazine-kneel-fixed","magazine-kneel-bare","single-fixed","single-bare","single-kneel-fixed","single-kneel-bare","musket-fixed-walk","musket-bare-walk","magazine-fixed-walk","magazine-bare-walk","single-fixed-walk","single-bare-walk","musket-fixed-run","musket-bare-run"}:(Video||Short)?new[]{"musket-fixed","musket-bare"}:new[]{"musket-fixed","musket-bare","magazine-fixed","magazine-bare","single-fixed","single-bare","musket-fixed-walk","musket-bare-walk","musket-fixed-run","musket-bare-run"};
        try{
            foreach(string name in names){
                if(Preview&&(name.StartsWith("india")||name.StartsWith("france")))continue;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                bool sword=!name.StartsWith("musket")&&!name.StartsWith("magazine")&&!name.StartsWith("single");
                bool strike=false,ready=name.EndsWith("ready"),running=name.EndsWith("run"),fix=name.Contains("fixed");
                var config=new BattleConfig{AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0};
                var world=new BattleWorld(config);var unit=world.Battalions[0];var s=world.Soldiers;
                unit.Role=sword?UnitRole.Swordsman:UnitRole.Line;unit.Faction=name.StartsWith("ming")?"ming":name.StartsWith("india")?"india":"france";
                unit.Weapon=MilitaryEquipment.Weapon(name.StartsWith("magazine")?"magazine":name.StartsWith("single")?"breechloader":"musket");
                bool kneeling=name.Contains("kneel");
                unit.FireAtWill=kneeling;unit.FireCycleActive=kneeling;unit.BayonetsFixed=fix;unit.BayonetOrderTime=0;
                unit.Position=unit.Destination=Vector2.zero;unit.Facing=unit.DesiredFacing=0;unit.AliveCount=1;
                world.Battalions[1].Position=new Vector2(100,100);world.Battalions[1].AliveCount=0;
                for(int i=0;i<s.Alive.Length;i++){s.Alive[i]=i==0;s.Captains[i]=s.PikeSergeants[i]=false;s.Positions[i]=new Vector2(100,100);s.FireTimes[i]=-1;}
                s.Positions[0]=Vector2.zero;s.Facings[0]=0;s.KneelTimes[0]=kneeling?-1:float.PositiveInfinity;
                var center=world.Terrain.SurfacePoint(Vector2.zero);
                var camera=new GameObject("Battle melee review").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.2f;camera.nearClipPlane=.02f;camera.farClipPlane=25;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.2f,.24f);
                var sun=new GameObject("Battle review sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;sun.transform.rotation=Quaternion.Euler(45,-30,0);
                RenderSettings.fog=false;RenderSettings.ambientLight=Color.gray;
                Shader.SetGlobalFloat("_BattleAtlasBlend",0);Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.7f,.75f,.85f,1));Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.6f,.65f,.7f,1));Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.4f,.45f,.5f,1));
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=center+Vector3.down*.03f;floor.transform.localScale=new Vector3(30,.04f,30);
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.21f,.24f,.2f));floor.GetComponent<Renderer>().sharedMaterial=material;
                int width=Video?600:300,height=Video?720:360;var target=new RenderTexture(width,height,24);camera.targetTexture=target;
                var tile=new Texture2D(width,height,TextureFormat.RGB24,false);var sheet=new Texture2D(width*6,height*2,TextureFormat.RGB24,false);
                using(RendererOwner renderer=new RendererOwner(world,camera)){
                    try{
                        for(int view=0;view<(Video?1:2);view++){
                            camera.transform.position=center+(view==0?new Vector3(3,1.8f,6):new Vector3(6,1.6f,.3f));camera.transform.LookAt(center+Vector3.up*1.02f);
                            // Reset each view's readiness to an actual relaxed first frame.
                            // Handling is sampled directly; one renderer is reused across views.
                            for(int col=0;col<(Video?103:6);col++){
                                float phase=Video?Mathf.Min(1,col/72f):col/5f;
                                var action=sword?(name.EndsWith("left")?MeleeAction.SlashLeft:MeleeAction.SlashRight):MeleeSystem.SelectAttack(false,false,fix,0);
                                float clock=Video?col/30f:phase*BayonetPresentationTiming.Duration;
                                typeof(BattleWorld).GetProperty("Time").SetValue(world,clock);
                                unit.InMelee=strike;unit.Moving=name.EndsWith("walk")||running;unit.Running=running;unit.AttackMode=ready&&col>0?InfantryAttackMode.BayonetCharge:InfantryAttackMode.None;
                                s.MovementSpeeds[0]=unit.Moving?(running?2.2f:1.2f):0;s.GaitPhases[0]=Mathf.Repeat(phase,.9999f);
                                s.MeleeActions[0]=strike?action:MeleeAction.None;s.MeleeStarts[0]=0;s.MeleeEnds[0]=strike?MeleeSystem.AttackDuration(action)+.001f:-1;s.MeleeTargets[0]=strike?8:-1;
                                yield return null;
                                renderer.Value.Render(GeometryUtility.CalculateFrustumPlanes(camera),clock);camera.Render();
                                if(renderer.Value.VisibleSoldiers!=1)throw new Exception("Battle review should render one isolated subject: "+name+" count="+renderer.Value.VisibleSoldiers);
                                if(renderer.Value.VisibleTransferBayonets!=(clock<BayonetPresentationTiming.Duration?1:0)||renderer.Value.VisibleBayonets!=(fix&&clock>=BayonetPresentationTiming.Duration?1:0))throw new Exception("Battle blade submission disagrees with fixed state: "+name+" blades="+renderer.Value.VisibleBayonets);
                                RenderTexture.active=target;tile.ReadPixels(new Rect(0,0,width,height),0,0);tile.Apply();if(Video){string frames=Output+"/VideoFrames/"+name;Directory.CreateDirectory(frames);File.WriteAllBytes(frames+"/frame"+col.ToString("D4")+".png",tile.EncodeToPNG());}else sheet.SetPixels(col*width,(1-view)*height,width,height,tile.GetPixels());
                            }
                        }
                        if(!Video){sheet.Apply();File.WriteAllBytes(Output+"/"+name+".png",sheet.EncodeToPNG());}Debug.Log("BAYONET_BATTLE_CAPTURED "+name);
                    }finally{RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(sheet);UnityEngine.Object.DestroyImmediate(material);}
                }
            }
            File.WriteAllText(Output+"/index.txt",OffsetReview?"Actual BattleWorld/CrowdInfantryRenderer. Rifle butt forward-clearance review, both sides and chronological samples; standing, kneeling and walking for musket, magazine and single-shot, plus musket run. Fixed/bare transitions preserve the weapon socket and hand contacts. No native player/campaign launched.\n":"Actual BattleWorld/CrowdInfantryRenderer transfer: belt withdrawal, hand carry, socket seating; reverse removal and sheathing. Standing muskets/cartridge rifles and moving musket fix/unfix. Rows front/side; columns chronological. No native player/campaign launched.\n");
            Debug.Log("BAYONET_BATTLE_VISUAL_REVIEW_COMPLETE");
        }finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }
    sealed class RendererOwner:IDisposable
    {
        public CrowdInfantryRenderer Value;
        public RendererOwner(BattleWorld w,Camera c){Reset(w,c);}
        public void Reset(BattleWorld w,Camera c){Value?.Dispose();Value=new CrowdInfantryRenderer(w,c);}
        public void Dispose()=>Value?.Dispose();
    }
}
