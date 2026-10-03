using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EmpireBattle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Review-only fixture: uses the game's firing, collision, damage and presentation paths.
// No hooks into BattleRuntime, user games, saved releases or effect tuning are required.
public sealed class ArtilleryEffectsViewer : EditorWindow
{
    [Serializable] public sealed class Config
    {
        public string ammunition="Roundshot", output;
        public int seed=1815, soldiers=120, files=20, fps=30, width=960, height=540;
        public float range=100, seconds=16, fireAt=1, closeAt=1.7f, spacing=1.1f;
        public void Validate()
        {
            if(!Enum.TryParse(ammunition,true,out ArtilleryAmmunition ammo)||!Enum.IsDefined(typeof(ArtilleryAmmunition),ammo))throw new ArgumentException("Unknown ammunition: "+ammunition);
            if(soldiers<20||soldiers>240||files<5||files>40||soldiers%files!=0)throw new ArgumentException("Use 20–240 soldiers in complete rows of 5–40 files.");
            if(fps<15||fps>60||width<320||width>1920||height<240||height>1080||width%2!=0||height%2!=0)throw new ArgumentException("Invalid bounded video dimensions/rate.");
            if(!Finite(range)||range<40||range>180||!Finite(seconds)||seconds<6||seconds>40||!Finite(spacing)||spacing<.8f||spacing>1.4f)throw new ArgumentException("Invalid range, duration or spacing.");
            if(!Finite(fireAt)||!Finite(closeAt)||fireAt<.5f||closeAt<fireAt+.2f||closeAt>fireAt+1||seconds<closeAt+3)throw new ArgumentException("Invalid shot/camera timing.");
            if(Mathf.Abs(seconds*fps-Mathf.Round(seconds*fps))>.001f)throw new ArgumentException("Duration must contain an integer number of frames.");
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
    }
    [Serializable] public sealed class Impact
    {
        public int sequence; public float time; public Vector3 position; public bool terrain; public string ammunition;
    }
    [Serializable] public sealed class Death
    {
        public int soldier; public float time; public Vector3 position; public string cause;
    }
    [Serializable] public sealed class Receipt
    {
        public string renderer="Actual Unity game renderer", unityVersion, effectPresetSha256, simulationDigest;
        public Config config; public int frames, shots, deaths, wounded, impacts;
        public float firstImpact=-1; public Vector3 gunPosition, targetPosition, wideCamera, closeCamera;
        public List<Impact> impactEvents=new List<Impact>(); public List<Death> deathEvents=new List<Death>();
    }
    [Serializable] sealed class SimulationRecord
    {
        public string ammunition; public int seed,soldiers,files,shots; public float range,spacing,seconds,fireAt;
        public List<Impact> impacts; public List<Death> deaths; public float[] health;
    }
    internal sealed class Fixture
    {
        public readonly BattleWorld world; public readonly ArtillerySystem artillery; public readonly Config config;
        public readonly Receipt receipt; int tick, observedImpact; bool fired;
        const float Dt=1f/120f;
        public Fixture(Config c)
        {
            c.Validate();config=c;
            world=new BattleWorld(new BattleConfig{AssetReview=true,Seed=c.seed,BattalionsPerSide=1,SoldiersPerBattalion=c.soldiers,
                FrenchArtillery=1,BritishArtillery=0,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=true});
            foreach(var unit in world.Battalions)
            {
                unit.Position=unit.Destination=unit.Side==ArmySide.British?new Vector2(0,30):new Vector2(-400,-400);
                unit.Facing=unit.DesiredFacing=unit.Side==ArmySide.British?Mathf.PI:0;
                unit.Files=c.files;unit.Spacing=unit.ReviewRankSpacing=c.spacing;unit.FireAtWill=false;
                for(int slot=0;slot<unit.SoldierCount;slot++)
                {
                    int id=unit.SoldierStart+slot;world.Soldiers.FormationSlots[id]=slot;
                    world.Soldiers.Positions[id]=unit.Position+FormationMath.SlotOffset(unit,slot);
                    world.Soldiers.Facings[id]=unit.Facing;world.Soldiers.Velocities[id]=Vector2.zero;
                }
            }
            artillery=new ArtillerySystem(world);
            var gun=artillery.Guns[0];gun.Position=new Vector2(0,30-c.range);gun.Facing=0;gun.FireAtWill=false;
            gun.LastShotTime=-100;
            receipt=new Receipt{config=c,unityVersion=Application.unityVersion,
                gunPosition=world.Terrain.SurfacePoint(gun.Position),targetPosition=new Vector3(0,0,30)};
            world.SoldierDied+=d=>receipt.deathEvents.Add(new Death{soldier=d.Soldier,time=world.Time,position=d.Position,cause=d.Cause.ToString()});
        }
        public void Advance(float to)
        {
            int target=Mathf.RoundToInt(to*120);
            while(tick<target)
            {
                if(!fired&&tick>=Mathf.RoundToInt(config.fireAt*120))
                {
                    fired=true;
                    if(!artillery.FireReviewShot(0,1,(ArtilleryAmmunition)Enum.Parse(typeof(ArtilleryAmmunition),config.ammunition,true)))throw new Exception("Review gun refused to fire.");
                }
                // Movement handles knockdown recovery/reformation; no AI or musket combat is advanced.
                world.StepMovement(Dt);artillery.Step(Dt);tick++;
                for(int seq=observedImpact+1;seq<=artillery.ImpactSequence;seq++)
                {
                    var hit=artillery.Impacts[(seq-1)%artillery.Impacts.Length];
                    if(hit.Sequence!=seq)throw new Exception("Impact receipt overflow.");
                    receipt.impactEvents.Add(new Impact{sequence=seq,time=hit.Time,position=hit.Position,terrain=hit.TerrainStrike,ammunition=hit.AmmunitionType.ToString()});
                    if(receipt.firstImpact<0)receipt.firstImpact=hit.Time;
                }
                observedImpact=artillery.ImpactSequence;
            }
        }
        public void Finish()
        {
            receipt.shots=artillery.Guns[0].ShotsFired;receipt.deaths=world.DeathCount;receipt.impacts=artillery.ImpactSequence;
            receipt.wounded=0;
            for(int i=world.Battalions[1].SoldierStart;i<world.Soldiers.Alive.Length;i++)if(world.Soldiers.Alive[i]&&world.Soldiers.Health[i]<world.Battalions[1].MaxHealth)receipt.wounded++;
            var result=new SimulationRecord{ammunition=config.ammunition,seed=config.seed,soldiers=config.soldiers,files=config.files,
                shots=receipt.shots,range=config.range,spacing=config.spacing,seconds=config.seconds,fireAt=config.fireAt,
                impacts=receipt.impactEvents,deaths=receipt.deathEvents,health=world.Soldiers.Health};
            receipt.simulationDigest=Hash(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(result)));
            if(receipt.shots!=1||receipt.impacts==0)throw new Exception("Fixture did not record one shot and an actual impact.");
        }
    }
    internal sealed class Session : IDisposable
    {
        static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly Scene scene, previous; readonly GameObject root; readonly Material floorMaterial;
        readonly Mesh floorMesh; readonly Texture2D image; readonly CrowdInfantryRenderer crowd;
        readonly CannonView cannon; readonly ArtilleryGroundMarks marks; readonly MusketSmoke smoke;
        readonly BattleCasualtyView casualties; readonly ArtilleryCrewView crew; readonly BattleEffectProfile priorProfile;
        public readonly Fixture fixture; public readonly Camera camera; public readonly RenderTexture target;
        float visualTime; int floorRevision=-1;
        readonly Vector3[] floorVertices=new Vector3[161*241];
        static void Call(object target,string method)=>target.GetType().GetMethod(method,Hidden).Invoke(target,null);
        public Session(Config c)
        {
            fixture=new Fixture(c);previous=SceneManager.GetActiveScene();
            priorProfile=BattleEffectTuning.Current;BattleEffectTuning.ReloadGamePreset();
            fixture.receipt.effectPresetSha256=File.Exists("Assets/Resources/BattleEffectPreset.json")?Hash(File.ReadAllBytes("Assets/Resources/BattleEffectPreset.json")):"defaults";
            scene=SceneManager.CreateScene("Artillery review "+Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(scene);
            root=new GameObject("Artillery review fixture");
            camera=new GameObject("Review camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
            camera.nearClipPlane=.1f;camera.farClipPlane=1000;camera.fieldOfView=48;camera.aspect=c.width/(float)c.height;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.57f,.68f,.76f);
            var sun=new GameObject("Fixed review sun").AddComponent<Light>();sun.transform.SetParent(root.transform);
            sun.type=LightType.Directional;sun.intensity=1.3f;sun.transform.rotation=Quaternion.Euler(45,-35,0);
            RenderSettings.ambientLight=new Color(.55f,.55f,.55f);RenderSettings.fog=false;
            var floor=new GameObject("Shared terrain surface");floor.transform.SetParent(root.transform);
            floorMesh=new Mesh{name="Review terrain"};floor.AddComponent<MeshFilter>().sharedMesh=floorMesh;
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.color=new Color(.26f,.28f,.18f);
            floor.AddComponent<MeshRenderer>().sharedMaterial=floorMaterial;
            UpdateFloor();
            target=new RenderTexture(c.width,c.height,24);target.Create();camera.targetTexture=target;
            image=new Texture2D(c.width,c.height,TextureFormat.RGB24,false);
            casualties=root.AddComponent<BattleCasualtyView>();casualties.Initialize(fixture.world,camera);
            smoke=root.AddComponent<MusketSmoke>();smoke.Initialize(fixture.world,camera,sun);
            cannon=root.AddComponent<CannonView>();cannon.Initialize(fixture.world,fixture.artillery,camera,smoke);
            marks=root.AddComponent<ArtilleryGroundMarks>();marks.Initialize(fixture.world,fixture.artillery,camera,smoke);
            crew=root.GetComponent<ArtilleryCrewView>();
            crowd=new CrowdInfantryRenderer(fixture.world,camera){Casualties=casualties};
            if(!crowd.Ready)throw new Exception("Actual infantry renderer is unavailable; bake assets before capture.");
            camera.enabled=false;
            foreach(var behaviour in root.GetComponentsInChildren<MonoBehaviour>())behaviour.enabled=false;
        }
        void UpdateFloor()
        {
            if(floorRevision==fixture.world.Terrain.DamageRevision)return;
            floorRevision=fixture.world.Terrain.DamageRevision;
            const int n=161,rows=241;var vertices=floorVertices;var triangles=new int[(n-1)*(rows-1)*6];int at=0;
            for(int z=0;z<rows;z++)for(int x=0;x<n;x++)
            {
                int v=z*n+x;vertices[v]=fixture.world.Terrain.SurfacePoint(new Vector2(x-80,z-170));
                if(x==n-1||z==rows-1)continue;
                triangles[at++]=v;triangles[at++]=v+n;triangles[at++]=v+1;
                triangles[at++]=v+1;triangles[at++]=v+n;triangles[at++]=v+n+1;
            }
            floorMesh.vertices=vertices;floorMesh.triangles=triangles;floorMesh.RecalculateNormals();floorMesh.RecalculateBounds();
        }
        public void Render(float time)
        {
            SceneManager.SetActiveScene(scene);
            fixture.Advance(time);casualties.Step(time-visualTime);visualTime=time;
            var c=fixture.config;
            Vector3 wide=new Vector3(c.range*.85f,c.range*.5f,30-c.range*.5f),close=new Vector3(18,9,10);
            fixture.receipt.wideCamera=wide;fixture.receipt.closeCamera=close;
            bool near=time>=c.closeAt;camera.transform.position=near?close:wide;
            camera.transform.LookAt(near?new Vector3(0,1,30):new Vector3(0,1,30-c.range*.5f));
            UpdateFloor();
            crew.Sample();Call(cannon,"Update");Call(marks,"LateUpdate");Call(smoke,"LateUpdate");Call(casualties,"LateUpdate");Call(casualties.Blood,"LateUpdate");
            crowd.Render(GeometryUtility.CalculateFrustumPlanes(camera),fixture.world.Time);camera.Render();
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
        }
        public void WriteFrame(string path)
        {
            var prior=RenderTexture.active;
            try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=prior;}
        }
        public void Dispose()
        {
            crowd?.Dispose();
            if(camera)camera.targetTexture=null;
            if(root)UnityEngine.Object.DestroyImmediate(root);
            if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(image)UnityEngine.Object.DestroyImmediate(image);
            if(floorMesh)UnityEngine.Object.DestroyImmediate(floorMesh);
            if(floorMaterial)UnityEngine.Object.DestroyImmediate(floorMaterial);
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(scene.IsValid()&&scene.isLoaded){if(Application.isPlaying)SceneManager.UnloadSceneAsync(scene);else EditorSceneManager.CloseScene(scene,true);}
            BattleEffectTuning.Current=priorProfile;
        }
    }
    static string Hash(byte[] bytes)
    {using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    public static void Verify()
    {
        foreach(var ammo in new[]{"Roundshot","Explosive"})
        {
            string prior=null;
            for(int repeat=0;repeat<2;repeat++)
            {
                var f=new Fixture(new Config{ammunition=ammo});f.Advance(f.config.seconds);f.Finish();
                if(f.receipt.deaths==0)throw new Exception(ammo+" missed the fixed block.");
                if(prior!=null&&prior!=f.receipt.simulationDigest)throw new Exception(ammo+" simulation is not repeatable.");
                prior=f.receipt.simulationDigest;
                Debug.Log("ARTILLERY_VIEWER_REPEAT "+ammo+" "+JsonUtility.ToJson(f.receipt));
            }
        }
        Debug.Log("ARTILLERY_VIEWER_VERIFIED: same-seed trajectories, impacts and casualties agree across two runs per known ammunition.");
    }
    const string PendingCapture="ETW2.ArtilleryEffectsViewer.PendingCapture";
    [InitializeOnLoadMethod] static void ResumeCapture()
    {if(SessionState.GetBool(PendingCapture,false))EditorApplication.update+=StartWhenPlaying;}
    public static void Capture()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Use the capture runner for batch export.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(PendingCapture,true);EditorApplication.update+=StartWhenPlaying;EditorApplication.EnterPlaymode();
    }
    static void StartWhenPlaying()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        EditorApplication.update-=StartWhenPlaying;SessionState.SetBool(PendingCapture,false);BeginCapture();
    }
    static void BeginCapture()
    {
        Session session=null;int frame=0;string failure=null;
        void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)failure=message;}
        void Tick()
        {
            try
            {
                if(failure!=null)throw new Exception("Renderer logged: "+failure);
                var c=session.fixture.config;int count=Mathf.RoundToInt(c.seconds*c.fps);
                if(frame<count)
                {
                    session.Render(frame/(float)c.fps);session.WriteFrame(Path.Combine(c.output,$"frame-{frame:D5}.png"));frame++;return;
                }
                session.fixture.Advance(c.seconds);session.fixture.receipt.frames=frame;session.fixture.Finish();
                File.WriteAllText(Path.Combine(c.output,"receipt.json"),JsonUtility.ToJson(session.fixture.receipt,true));
                EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;session.Dispose();
                Debug.Log("ARTILLERY_VIEWER_CAPTURED "+frame+" actual renderer frames: "+c.output);EditorApplication.Exit(0);
            }
            catch(Exception e)
            {
                EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;session?.Dispose();Debug.LogException(e);EditorApplication.Exit(1);
            }
        }
        try
        {
            var path=Environment.GetEnvironmentVariable("ETW2_ARTILLERY_VIDEO_CONFIG");
            if(string.IsNullOrEmpty(path))throw new ArgumentException("ETW2_ARTILLERY_VIDEO_CONFIG must identify a JSON config.");
            var c=JsonUtility.FromJson<Config>(File.ReadAllText(path));c.Validate();
            if(string.IsNullOrEmpty(c.output)||!Path.IsPathRooted(c.output)||Directory.Exists(c.output))throw new ArgumentException("Frame output must be a fresh absolute directory.");
            Directory.CreateDirectory(c.output);Application.logMessageReceived+=OnLog;
            session=new Session(c);EditorApplication.update+=Tick;
        }
        catch(Exception e){Application.logMessageReceived-=OnLog;session?.Dispose();Debug.LogException(e);EditorApplication.Exit(1);}
    }
    Config settings=new Config();Session preview;bool playing;double lastTick;float time;
    [MenuItem("Empire/Review/Artillery effects viewer")]
    static void Open()=>GetWindow<ArtilleryEffectsViewer>("Artillery effects");
    void OnGUI()
    {
        var ammo=(ArtilleryAmmunition)Enum.Parse(typeof(ArtilleryAmmunition),settings.ammunition);
        settings.ammunition=((ArtilleryAmmunition)EditorGUILayout.EnumPopup("Ammunition",ammo)).ToString();
        settings.seed=EditorGUILayout.IntField("Seed",settings.seed);
        settings.range=EditorGUILayout.Slider("Range (m)",settings.range,40,180);
        EditorGUILayout.LabelField("120 infantry · 20 files · fixed sun · 120 Hz simulation");
        EditorGUILayout.LabelField("Saved game effect preset; actual projectile and casualty systems.");
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button("Reset / play")){if(!Application.isPlaying){Debug.LogWarning("Open an empty scene and enter Play Mode to review actual casualty physics.");return;}Stop();preview=new Session(JsonUtility.FromJson<Config>(JsonUtility.ToJson(settings)));time=0;playing=true;lastTick=EditorApplication.timeSinceStartup;}
            if(GUILayout.Button(playing?"Pause":"Resume")&&preview!=null){playing=!playing;lastTick=EditorApplication.timeSinceStartup;}
            if(GUILayout.Button("Stop"))Stop();
        }
        if(preview!=null)
        {
            EditorGUILayout.LabelField($"{preview.fixture.config.ammunition} · {time:F2}s · deaths {preview.fixture.world.DeathCount}");
            var rect=GUILayoutUtility.GetAspectRect(settings.width/(float)settings.height);GUI.DrawTexture(rect,preview.target,ScaleMode.ScaleToFit,false);
        }
        EditorGUILayout.HelpBox("Export labeled MP4s with python3 Tools/artillery_effects_video.py --config Tools/artillery_effects_video.json --output <fresh-directory>. Run captures only during an allocated rendering slot.",MessageType.Info);
    }
    void OnEnable()=>EditorApplication.update+=TickPreview;
    void OnDisable(){EditorApplication.update-=TickPreview;Stop();}
    void Stop(){playing=false;preview?.Dispose();preview=null;}
    void TickPreview()
    {
        if(!playing||preview==null)return;
        double now=EditorApplication.timeSinceStartup;if(now-lastTick<1.0/settings.fps)return;lastTick=now;
        try{preview.Render(time);time+=1f/settings.fps;if(time>=preview.fixture.config.seconds)playing=false;Repaint();}
        catch(Exception e){Stop();Debug.LogException(e);}
    }
}
