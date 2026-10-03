using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using EmpireBattle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Offline review, not gameplay recording: scripted state changes at a fixed
// simulation clock feed the production battle renderer, assets and GPU morphs.
// One PNG per 1/30 second; the encoder never invents or interpolates frames.
public static class MeleeMotionVideoReview
{
    const int Fps=30,Width=640,Height=640,Frames=420;
    const string Output="Artifacts/MeleeRefinement/VideoFrames";
    static readonly PropertyInfo Clock=typeof(BattleWorld).GetProperty("Time");
    public static void Capture()
    {
        IEnumerator routine=Record();string error=null;
        void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)error=message;}
        Application.logMessageReceived+=Log;
        void Tick(){try{
            if(error!=null)throw new Exception(error);
            if(routine.MoveNext())return;
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;EditorApplication.Exit(0);
        }catch(Exception e){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;(routine as IDisposable)?.Dispose();Debug.LogException(e);EditorApplication.Exit(1);}}
        EditorApplication.update+=Tick;
    }
    static IEnumerator Record()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();Directory.CreateDirectory(Output);
        try{
            foreach(string name in new[]{"sword-close","sword-context","rifle-fixed","rifle-bare"}){
                bool sword=name.StartsWith("sword"),fixedBlade=name=="rifle-fixed",alwaysWide=name=="sword-context";
                string path=Output+"/"+name;Directory.CreateDirectory(path);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var world=new BattleWorld(new BattleConfig{AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
                var unit=world.Battalions[0];var soldiers=world.Soldiers;
                unit.Role=sword?UnitRole.Swordsman:UnitRole.Line;unit.Faction=sword?"ming":"france";unit.Weapon=MilitaryEquipment.Weapon("musket");
                unit.FireAtWill=false;unit.BayonetsFixed=fixedBlade;unit.BayonetOrderTime=-100;
                unit.Facing=unit.DesiredFacing=0;unit.Routing=false;unit.Running=false;unit.Reforming=false;
                world.Battalions[1].Position=new Vector2(100,100);world.Battalions[1].AliveCount=0;
                for(int i=0;i<soldiers.Alive.Length;i++){
                    soldiers.Alive[i]=false;soldiers.Captains[i]=soldiers.PikeSergeants[i]=false;
                    soldiers.Positions[i]=new Vector2(100,100);soldiers.Velocities[i]=Vector2.zero;soldiers.FireTimes[i]=-1;
                }
                var camera=new GameObject("Production motion video review").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.nearClipPlane=.02f;camera.farClipPlane=35;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.2f,.24f);
                var sun=new GameObject("Motion review sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;sun.transform.rotation=Quaternion.Euler(45,-30,0);
                RenderSettings.fog=false;RenderSettings.ambientLight=Color.gray;
                Shader.SetGlobalFloat("_BattleAtlasBlend",0);Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.7f,.75f,.85f,1));Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.6f,.65f,.7f,1));Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.4f,.45f,.5f,1));
                var ground=Ground(world);var groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMaterial.SetColor("_BaseColor",new Color(.26f,.29f,.24f));
                var floor=new GameObject("Terrain surface review context");floor.AddComponent<MeshFilter>().sharedMesh=ground;floor.AddComponent<MeshRenderer>().sharedMaterial=groundMaterial;
                var target=new RenderTexture(Width,Height,24);camera.targetTexture=target;var tile=new Texture2D(Width,Height,TextureFormat.RGB24,false);
                var report=new StringBuilder("frame,seconds,state,action,travel_metres,gait_phase,visible_soldiers,visible_bayonets\n");
                float distance=0;var gaits=new float[8];
                for(int i=0;i<8;i++)gaits[i]=i*.17f;
                var renderer=new CrowdInfantryRenderer(world,camera);
                try{
                        for(int frame=0;frame<Frames;frame++){
                            float time=frame/(float)Fps,dt=frame==0?0:1f/Fps;Clock.SetValue(world,time);
                            bool wide=alwaysWide||!sword&&time>=6;
                            int count=wide?8:1;unit.AliveCount=count;
                            bool combat=sword?time>=5&&time<9.5f:time<8.4f;
                            float speed=sword?WalkSpeed(time,0,5)+WalkSpeed(time,9.5f,12.3f):WalkSpeed(time,8.4f,12);
                            distance+=speed*dt;unit.Position=unit.Destination=new Vector2(0,distance);unit.InMelee=combat;unit.Moving=speed>.01f;
                            unit.AttackMode=sword&&time>=3.4f&&time<9.5f?InfantryAttackMode.BayonetCharge:InfantryAttackMode.None;
                            for(int i=0;i<8;i++){
                                soldiers.Alive[i]=i<count;
                                Vector2 slot=wide?new Vector2((i%4-1.5f)*.95f,-i/4*.95f):Vector2.zero;
                                soldiers.Positions[i]=unit.Position+slot;soldiers.Facings[i]=0;soldiers.Velocities[i]=Vector2.zero;soldiers.MovementSpeeds[i]=speed;
                                float stride=FormationMath.SoldierStride(i,false,speed,unit.AttackMode==InfantryAttackMode.BayonetCharge,false,FormationMath.ExpandedInfantryGaits(unit));
                                gaits[i]=Mathf.Repeat(gaits[i]+speed*dt/stride,1);soldiers.GaitPhases[i]=gaits[i];
                                var action=combat?MeleeAction.Ready:MeleeAction.None;float start=0,end=-1;
                                float delay=wide?i*.06f:0;
                                foreach(float candidate in sword?new[]{5.3f,6.9f}:new[]{.8f,3.6f,6.2f}){
                                    var strike=sword?(candidate<6?MeleeAction.SlashRight:MeleeAction.SlashLeft):MeleeSystem.SelectAttack(false,false,fixedBlade,0);
                                    float began=candidate+delay,finished=began+MeleeSystem.AttackDuration(strike);
                                    if(combat&&time>=began&&time<finished){action=strike;start=began;end=finished;break;}
                                }
                                soldiers.MeleeActions[i]=action;soldiers.MeleeStarts[i]=start;soldiers.MeleeEnds[i]=end;soldiers.MeleeTargets[i]=combat?8:-1;
                            }
                            var centre=world.Terrain.SurfacePoint(unit.Position);
                            camera.orthographicSize=wide?3.45f:sword?1.55f:1.7f;
                            camera.transform.position=centre+(wide?new Vector3(7,4.6f,7):sword?new Vector3(3,1.8f,6):new Vector3(6,1.8f,3));
                            camera.transform.LookAt(centre+new Vector3(0,wide?.75f:1.02f,wide?-.45f:0));
                            yield return null;
                            renderer.Render(GeometryUtility.CalculateFrustumPlanes(camera),time);camera.Render();
                            if(renderer.VisibleSoldiers!=count)throw new Exception("Video subject visibility mismatch "+name+" frame "+frame+": "+renderer.VisibleSoldiers+" vs "+count);
                            RenderTexture.active=target;tile.ReadPixels(new Rect(0,0,Width,Height),0,0);tile.Apply();
                            File.WriteAllBytes(path+"/"+frame.ToString("D5")+".png",tile.EncodeToPNG());
                            string state=combat?"combat":speed>.01f?(sword&&time<3.4f?"relaxed_walk":"disengaging_walk"):"post_combat_idle";
                            if(sword&&time>=3.4f&&time<5)state="raise_guard_while_walking";
                            report.AppendLine(string.Join(",",frame,time.ToString("F6",CultureInfo.InvariantCulture),state,soldiers.MeleeActions[0],distance.ToString("F6",CultureInfo.InvariantCulture),gaits[0].ToString("F6",CultureInfo.InvariantCulture),renderer.VisibleSoldiers,renderer.VisibleBayonets));
                        }
                        File.WriteAllText(Output+"/"+name+".csv",report.ToString());Debug.Log("MELEE_VIDEO_FRAMES_COMPLETE "+name+" 420 frames at 30fps");
                }finally{RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(ground);UnityEngine.Object.DestroyImmediate(groundMaterial);renderer.Dispose();}
            }
            File.WriteAllText(Output+"/provenance.txt","Production BattleWorld/CrowdInfantryRenderer and baked assets. Scripted state review at exact t=frame/30; 420 frames per view (14 seconds). Standard renderer pose interpolation only. No generative frames, optical flow, gameplay damage simulation, native player, campaign test or game publication. Sword close/context and fixed/bare musket panels. Includes walking guard transition, strikes, disengaging walk, post-combat idle.\n");
            Debug.Log("MELEE_MOTION_VIDEO_CAPTURE_COMPLETE");
        }finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }
    static float WalkSpeed(float time,float start,float end)
    {
        if(time<start||time>=end)return 0;
        return 1.2f*Mathf.SmoothStep(0,1,(time-start)/.2f)*(1-Mathf.SmoothStep(0,1,(time-(end-.2f))/.2f));
    }
    static Mesh Ground(BattleWorld world)
    {
        const int nx=21,nz=29;var points=new Vector3[nx*nz];var triangles=new int[(nx-1)*(nz-1)*6];int n=0;
        for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)points[z*nx+x]=world.Terrain.SurfacePoint(new Vector2(x-10,z-7))+Vector3.down*.01f;
        for(int z=0;z<nz-1;z++)for(int x=0;x<nx-1;x++){
            int a=z*nx+x;triangles[n++]=a;triangles[n++]=a+nx;triangles[n++]=a+1;triangles[n++]=a+1;triangles[n++]=a+nx;triangles[n++]=a+nx+1;
        }
        var mesh=new Mesh{name="Actual sampled terrain review ground",vertices=points,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
}
