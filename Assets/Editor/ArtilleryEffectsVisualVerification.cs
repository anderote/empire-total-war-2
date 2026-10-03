using System;
using System.IO;
using System.Reflection;
using EmpireBattle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ArtilleryEffectsVisualVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    const string Output="Artifacts/ArtilleryEffectsReview";
    static void Require(bool value,string message) { if(!value)throw new Exception("Artillery effects: "+message); }
    static void Call(object target,string method,params object[] args) => target.GetType().GetMethod(method,Hidden).Invoke(target,args);
    static BattleWorld World() => new BattleWorld(new BattleConfig { AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=8,
        FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false,EnableAI=false });

    public static void Verify()
    {
        CarcassFireVerification.PrepareTextures();
        foreach(string name in new[]{"ArtilleryBurst","ArtilleryFlame","ArtilleryEmberBed","MusketSmoke","CarcassFire"})
        {
            Shader shader=Resources.Load<Shader>(name);Require(shader,"Missing shader "+name);
            foreach(var message in ShaderUtil.GetShaderMessages(shader))
                Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,name+": "+message.message);
        }
        for(int seed=-50;seed<50;seed++)for(int i=0;i<28;i++)
        {
            Vector2 p=ArtilleryEffectShape.FlamePoint(seed,i);
            Require(p.x>=-6&&p.x<=6&&p.y>=-2&&p.y<=12,"Fuel remains inside the compact terrain mesh");
            if(i%7!=0)Require(p.y>1,"Thrown filling lands ahead of the impact");
            Vector3 forward=ArtilleryEffectShape.WorldOffset(p,Vector3.forward);
            Vector3 right=ArtilleryEffectShape.WorldOffset(p,Vector3.right);
            Require((Quaternion.Euler(0,90,0)*forward-right).sqrMagnitude<.00001f,"Footprint rotates with incoming trajectory");
        }
        Require(ArtilleryEffectShape.Forward(Vector3.down)==Vector3.forward,"Vertical impact fallback is finite");
        var world=World();var artillery=new ArtillerySystem(world);
        var shot=new CannonProjectile { Position=new Vector3(100,0,0),Velocity=new Vector3(90,-20,40),AmmunitionType=ArtilleryAmmunition.Carcass };
        Call(artillery,"Explode",shot);
        Require(artillery.Hazards[0].Direction==shot.Velocity,"Hazard retains impact velocity after projectile reuse");
        Require(artillery.Hazards[0].Remaining==90&&ArtillerySystem.CarcassFireRadius==18,"Presentation keeps existing hazard gameplay");
        var root=new GameObject("Artillery effect pool check");var cameraRoot=new GameObject("Pool camera");
        try
        {
            var smoke=root.AddComponent<MusketSmoke>();smoke.Initialize(world,cameraRoot.AddComponent<Camera>());
            var hit=new ArtilleryImpact { Sequence=1,Time=0,Position=shot.Position,Direction=Vector3.down,
                AmmunitionType=ArtilleryAmmunition.Explosive,ExplosiveShell=true,TerrainStrike=true };
            smoke.EmitArtilleryBurst(hit,hit.Position);
            Require(smoke.TrackedPuffs==47,"Ground explosion contains dirt jets, dust skirt and powder smoke");
            hit.TerrainStrike=false;smoke.EmitArtilleryBurst(hit,hit.Position+Vector3.up*3);
            Require(smoke.TrackedPuffs==56,"Air contact emits powder without floating ground dirt");
            for(int i=0;i<3700;i++)smoke.EmitArtilleryBurst(hit,hit.Position);
            Require(smoke.TrackedPuffs==32768,"Sustained bombardment respects shared particle pool capacity");
            Require(world.Time==0&&world.DeathCount==0,"Presentation cannot advance simulation or damage troops");
        }
        finally { UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraRoot); }
        Debug.Log("ARTILLERY_EFFECTS_VERIFIED: directional compact fuel, persistent trajectory, ground/contact bursts, bounded smoke pool, shaders, unchanged combat");
        CarcassFireVerification.Verify();
    }

    public static void Capture()
    {
        var routine=CaptureFrames();
        string failure=null;
        void OnLog(string message,string stack,LogType type)
        { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=message; }
        Application.logMessageReceived+=OnLog;
        void Tick()
        {
            try
            {
                if(failure!=null)throw new Exception("Render verification logged an error: "+failure);
                if(routine.MoveNext())return;
                if(failure!=null)throw new Exception("Render verification logged an error: "+failure);
                EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;EditorApplication.Exit(0);
            }
            catch(Exception e)
            {
                EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;(routine as IDisposable)?.Dispose();
                Debug.LogException(e);EditorApplication.Exit(1);
            }
        }
        EditorApplication.update+=Tick;
    }
    static System.Collections.IEnumerator CaptureFrames()
    {
        ArtilleryImpactRevisionVerification.Verify();
        PoisonGasVerification.Verify();
        Verify();Directory.CreateDirectory(Output);
        var setup=EditorSceneManager.GetSceneManagerSetup();
        var profile=BattleEffectTuning.Current;
        BattleEffectTuning.ReloadGamePreset();
        try
        {
            foreach(var type in new[]{ArtilleryAmmunition.Explosive,ArtilleryAmmunition.Carcass,ArtilleryAmmunition.Lime,ArtilleryAmmunition.PoisonGas})
            {
                bool burning=type==ArtilleryAmmunition.Carcass,lime=type==ArtilleryAmmunition.Lime,gas=type==ArtilleryAmmunition.PoisonGas;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var world=World();var artillery=new ArtillerySystem(world);
                var camera=new GameObject("Offscreen artillery camera").AddComponent<Camera>();
                Vector3 center=new Vector3(100,0,0);
                camera.transform.position=center+new Vector3(12,8,-15);camera.transform.LookAt(center+new Vector3(0,1.8f,3));
                if(gas){camera.transform.position=center+new Vector3(24,9,-27);camera.transform.LookAt(center+Vector3.up);}
                camera.fieldOfView=46;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.3f,.36f);
                var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.transform.rotation=Quaternion.Euler(45,-35,0);
                RenderSettings.ambientLight=new Color(.55f,.55f,.55f);RenderSettings.fog=false;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=center+Vector3.down*.5f;floor.transform.localScale=new Vector3(70,1,70);
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.26f,.28f,.18f);floor.GetComponent<Renderer>().sharedMaterial=material;
                // Two soldier-height silhouettes make effect scale reviewable.
                for(int i=0;i<2;i++)
                {
                    var marker=GameObject.CreatePrimitive(PrimitiveType.Capsule);marker.transform.position=center+new Vector3(-4-i*2,.9f,3);
                    marker.transform.localScale=new Vector3(.45f,.9f,.45f);marker.GetComponent<Renderer>().sharedMaterial=material;
                }
                GameObject burningSoldier=null;
                if(burning)
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/NapoleonicInfantry/Prefabs/FrenchInspection.prefab");
                    burningSoldier=UnityEngine.Object.Instantiate(prefab);
                    foreach(var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/NapoleonicInfantry/Animations/musket_idle.FBX"))
                        if(asset is AnimationClip clip&&!clip.name.StartsWith("__preview")){clip.SampleAnimation(burningSoldier,.2f);break;}
                    burningSoldier.transform.position=center+new Vector3(3,0,3);
                    world.Soldiers.Positions[0]=new Vector2(103,3);
                }
                var root=new GameObject("Artillery presentation");var smoke=root.AddComponent<MusketSmoke>();smoke.Initialize(world,camera,sun);
                var cannon=root.AddComponent<CannonView>();cannon.Initialize(world,artillery,camera,smoke);
                var marks=root.AddComponent<ArtilleryGroundMarks>();marks.Initialize(world,artillery,camera,smoke);
                Vector3 incoming=new Vector3(0,-30,100);
                Call(artillery,"EmitImpact",center,true,true,incoming,type);
                if(burning||lime||gas)Call(artillery,"Explode",new CannonProjectile {Position=center,Velocity=incoming,AmmunitionType=type});
                var ground=new Mesh {name="Review terrain including impact crater"};
                var vertices=new Vector3[71*71];var triangles=new int[70*70*6];int index=0;
                for(int z=0;z<71;z++)for(int x=0;x<71;x++)
                {
                    int v=z*71+x;vertices[v]=world.Terrain.SurfacePoint(new Vector2(center.x+x-35,center.z+z-35));
                    if(x==70||z==70)continue;
                    triangles[index++]=v;triangles[index++]=v+71;triangles[index++]=v+1;
                    triangles[index++]=v+1;triangles[index++]=v+71;triangles[index++]=v+72;
                }
                ground.vertices=vertices;ground.triangles=triangles;ground.RecalculateNormals();ground.RecalculateBounds();
                floor.transform.position=Vector3.zero;floor.transform.localScale=Vector3.one;floor.GetComponent<MeshFilter>().sharedMesh=ground;
                var target=new RenderTexture(960,720,24);camera.targetTexture=target;var image=new Texture2D(960,720,TextureFormat.RGB24,false);
                try
                {
                    float now=0;
                    bool ignited=false;
                    foreach(float time in burning?new[]{.12f,.45f,2f,3.5f,7.5f,12f}:new[]{.04f,.25f,.7f,2.5f,12f})
                    {
                        while(now<time)
                        {
                            now=Mathf.Min(now+1f/30f,time);
                            typeof(BattleWorld).GetProperty("Time").SetValue(world,now);
                            if(burning||lime||gas)artillery.Hazards[0].Remaining=(burning?90:gas?ArtillerySystem.PoisonGasDuration:ArtillerySystem.LimeDuration)-now;
                            if(burning&&now>=2&&!ignited){Call(artillery,"PulseHazard",artillery.Hazards[0]);ignited=true;}
                            if(burning&&now>3)
                            {
                                world.Soldiers.Positions[0]=new Vector2(103+Mathf.Min(6,(now-3)*2),3);
                                burningSoldier.transform.position=world.Terrain.SurfacePoint(world.Soldiers.Positions[0]);
                            }
                            Call(cannon,"Update");Call(marks,"LateUpdate");Call(smoke,"LateUpdate");camera.Render();
                            if(now<time)yield return null;
                        }
                        RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();
                        File.WriteAllBytes(Output+$"/{(burning?"carcass":lime?"lime":gas?"poison-gas":"shell")}-{time:F2}.png",image.EncodeToPNG());
                        yield return null;
                        if(burning&&(time==3.5f||time==12f))
                        {
                            Vector3 oldPosition=camera.transform.position;Quaternion oldRotation=camera.transform.rotation;
                            Vector3 focus=time==3.5f?burningSoldier.transform.position+Vector3.up: center+new Vector3(0,1,2);
                            camera.transform.position=focus+new Vector3(5,3,-7);camera.transform.LookAt(focus);
                            Call(cannon,"Update");Call(marks,"LateUpdate");Call(smoke,"LateUpdate");camera.Render();RenderTexture.active=target;
                            image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();
                            File.WriteAllBytes(Output+(time==3.5f?"/burning-unit-close.png":"/fire-smoke-close.png"),image.EncodeToPNG());
                            yield return null;
                            camera.transform.SetPositionAndRotation(oldPosition,oldRotation);
                        }
                    }
                    if(burning)
                    {
                        artillery.Hazards[0].Direction=Vector3.right;
                        camera.transform.position=center+new Vector3(0,21,-8);camera.transform.LookAt(center+Vector3.right*4);
                        Call(cannon,"Update");Call(marks,"LateUpdate");Call(smoke,"LateUpdate");camera.Render();RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes(Output+"/carcass-rotated-overhead.png",image.EncodeToPNG());
                    }
                }
                finally
                {
                    RenderTexture.active=null;camera.targetTexture=null;target.Release();
                    UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(ground);
                }
            }
            Verify();
            Debug.Log("ARTILLERY_EFFECTS_CAPTURED "+Output);
        }
        finally
        {
            BattleEffectTuning.Current=profile;
            if(Array.Exists(setup,s=>s.isLoaded&&s.isActive&&!string.IsNullOrEmpty(s.path)))EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
    }
}
