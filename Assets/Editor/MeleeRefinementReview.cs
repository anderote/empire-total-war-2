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
public static class MeleeRefinementReview
{
    static readonly bool Preview=Array.IndexOf(Environment.GetCommandLineArgs(),"--melee-review-preview")>=0;
    static readonly string Output=Preview?"Artifacts/MeleeRefinement/Preview":"Artifacts/MeleeRefinement/Visuals";
    public static void VerifyAndCapture(){MeleeRefinementVerification.Verify();BayonetPresentationVerification.Verify();Capture();}
    public static void RefineSwordsAndCapture(){InfantryAssetBuild.BakeSwordStrikeRefinements();VerifyAndCapture();}
    public static void FinishCartridgesAndCapture(){InfantryAssetBuild.BakeCartridgeMeleeRefinements();VerifyAndCapture();}
    public static void VerifyResourcesAndCapture(){MeleeRefinementVerification.VerifySwordResources();Capture();}
    public static void Capture()
    {
        var routine=Frames();string error=null;
        void OnLog(string text,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)error=text;}
        Application.logMessageReceived+=OnLog;
        void Tick(){try{
            if(error!=null)throw new Exception(error);
            if(routine.MoveNext())return;
            EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;EditorApplication.Exit(0);
        }catch(Exception e){EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;(routine as IDisposable)?.Dispose();Debug.LogException(e);EditorApplication.Exit(1);}}
        EditorApplication.update+=Tick;
    }
    static IEnumerator Frames()
    {
        Directory.CreateDirectory(Output);var setup=EditorSceneManager.GetSceneManagerSetup();
        var names=new[]{"ming-walk","ming-run","ming-ready","ming-slash-right","ming-slash-left","india-walk","france-walk","musket-fixed","musket-bare","magazine-fixed","magazine-bare","single-fixed","single-bare"};
        try{
            var args=Environment.GetCommandLineArgs();int caseArg=Array.IndexOf(args,"--melee-review-case");
            foreach(string name in names){
                if(caseArg>=0&&caseArg+1<args.Length&&name!=args[caseArg+1])continue;
                if(Preview&&(name.StartsWith("india")||name.StartsWith("france")))continue;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                bool sword=!name.StartsWith("musket")&&!name.StartsWith("magazine")&&!name.StartsWith("single");
                bool strike=name.Contains("slash")||!sword,ready=name.EndsWith("ready"),running=name.EndsWith("run"),fix=name.EndsWith("fixed");
                var config=new BattleConfig{AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0};
                var world=new BattleWorld(config);var unit=world.Battalions[0];var s=world.Soldiers;
                unit.Role=sword?UnitRole.Swordsman:UnitRole.Line;unit.Faction=name.StartsWith("ming")?"ming":name.StartsWith("india")?"india":"france";
                unit.Weapon=MilitaryEquipment.Weapon(name.StartsWith("magazine")?"magazine":name.StartsWith("single")?"breechloader":"musket");
                unit.FireAtWill=false;unit.BayonetsFixed=fix;unit.BayonetOrderTime=-100;
                unit.Position=unit.Destination=Vector2.zero;unit.Facing=unit.DesiredFacing=0;unit.AliveCount=1;
                world.Battalions[1].Position=new Vector2(100,100);world.Battalions[1].AliveCount=0;
                for(int i=0;i<s.Alive.Length;i++){s.Alive[i]=i==0;s.Captains[i]=s.PikeSergeants[i]=false;s.Positions[i]=new Vector2(100,100);s.FireTimes[i]=-1;}
                s.Positions[0]=Vector2.zero;s.Facings[0]=0;
                var center=world.Terrain.SurfacePoint(Vector2.zero);
                var camera=new GameObject("Battle melee review").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.2f;camera.nearClipPlane=.02f;camera.farClipPlane=25;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.2f,.24f);
                var sun=new GameObject("Battle review sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;sun.transform.rotation=Quaternion.Euler(45,-30,0);
                RenderSettings.fog=false;RenderSettings.ambientLight=Color.gray;
                Shader.SetGlobalFloat("_BattleAtlasBlend",0);Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.7f,.75f,.85f,1));Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.6f,.65f,.7f,1));Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.4f,.45f,.5f,1));
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=center+Vector3.down*.03f;floor.transform.localScale=new Vector3(30,.04f,30);
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.21f,.24f,.2f));floor.GetComponent<Renderer>().sharedMaterial=material;
                const int width=300,height=360;var target=new RenderTexture(width,height,24);camera.targetTexture=target;
                var tile=new Texture2D(width,height,TextureFormat.RGB24,false);var sheet=new Texture2D(width*6,height*2,TextureFormat.RGB24,false);
                using(RendererOwner renderer=new RendererOwner(world,camera)){
                    try{
                        for(int view=0;view<2;view++){
                            camera.transform.position=center+(view==0?new Vector3(3,1.8f,6):new Vector3(6,1.6f,.3f));camera.transform.LookAt(center+Vector3.up*1.02f);
                            // Reset each view's readiness to an actual relaxed first frame.
                            renderer.Reset(world,camera);
                            for(int col=0;col<6;col++){
                                float phase=col/5f;
                                var action=sword?(name.EndsWith("left")?MeleeAction.SlashLeft:MeleeAction.SlashRight):MeleeSystem.SelectAttack(false,false,fix,0);
                                float clock=ready?phase*.65f:strike?phase*MeleeSystem.AttackDuration(action):phase;
                                typeof(BattleWorld).GetProperty("Time").SetValue(world,clock);
                                unit.InMelee=strike;unit.Moving=!strike;unit.Running=running;unit.AttackMode=ready&&col>0?InfantryAttackMode.BayonetCharge:InfantryAttackMode.None;
                                s.MovementSpeeds[0]=strike?0:running?2.2f:1.2f;s.GaitPhases[0]=Mathf.Repeat(phase,.9999f);
                                s.MeleeActions[0]=strike?action:MeleeAction.None;s.MeleeStarts[0]=0;s.MeleeEnds[0]=strike?MeleeSystem.AttackDuration(action)+.001f:-1;s.MeleeTargets[0]=strike?8:-1;
                                yield return null;
                                renderer.Value.Render(GeometryUtility.CalculateFrustumPlanes(camera),clock);camera.Render();
                                if(renderer.Value.VisibleSoldiers!=1)throw new Exception("Battle review should render one isolated subject: "+name+" count="+renderer.Value.VisibleSoldiers);
                                if(renderer.Value.VisibleBayonets!=(fix?1:0))throw new Exception("Battle blade submission disagrees with fixed state: "+name+" blades="+renderer.Value.VisibleBayonets);
                                RenderTexture.active=target;tile.ReadPixels(new Rect(0,0,width,height),0,0);tile.Apply();sheet.SetPixels(col*width,(1-view)*height,width,height,tile.GetPixels());
                            }
                        }
                        sheet.Apply();File.WriteAllBytes(Output+"/"+name+".png",sheet.EncodeToPNG());Debug.Log("MELEE_BATTLE_CAPTURED "+name);
                    }finally{RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(sheet);UnityEngine.Object.DestroyImmediate(material);}
                }
            }
            File.WriteAllText(Output+"/index.txt","Actual BattleWorld/CrowdInfantryRenderer path, production assets and GPU shaders. Rows: front-quarter and side. Columns: six ordered gait/action/readiness samples. Fixed/bare musket, magazine and single-shot rifle; Ming/Indian/French sword carries; moving readiness and both cuts. No native player or campaign launched.\n");
            Debug.Log("MELEE_BATTLE_VISUAL_REVIEW_COMPLETE");
        }finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }
    sealed class RendererOwner:IDisposable
    {
        public CrowdInfantryRenderer Value;
        public RendererOwner(BattleWorld w,Camera c){}
        public void Reset(BattleWorld w,Camera c){Value?.Dispose();Value=new CrowdInfantryRenderer(w,c);}
        public void Dispose()=>Value?.Dispose();
    }
}
