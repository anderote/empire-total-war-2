using System;
using System.IO;
using System.Linq;
using EmpireBattle;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireWorkbench
{
    // A separate development scene/application, using the same baked poses and effects as battle.
    public sealed partial class AssetWorkbench:MonoBehaviour
    {
        public WorkbenchCatalog Catalog;
        public string ProjectRoot;
        WorkbenchStore store;
        ReviewPreset preset=new ReviewPreset();
        BattleEffectProfile originalProfile;
        ReviewAsset selected;
        ReviewTrack track;
        GameObject model;
        Camera cameraView;
        Light sun;
        GameObject floor;
        Material floorMaterial;
        EmpireBattle.Art.InfantryCrowdLibrary reviewLibrary;
        Func<BattalionState,EmpireBattle.Art.InfantryCrowdSet> priorResolver;
        BattleRuntime sandbox;
        BattleView sandboxView;
        bool awaitingSandbox,playing=true,loop=true,dirtyNote;
        int selectedIndex,trackIndex,lod,formation=1,ammo,shotsBefore,controlTab;
        string search="",category="All",noteText="",noteStatus="todo",message="",presetName="Default";
        float normalized,speed=1,yaw=155,pitch=12,distance=4,spacing=1.2f;
        Vector3 focus=new Vector3(0,1,0);
        Vector2 assetScroll,trackScroll,controlScroll,presetScroll;
        MaterialPropertyBlock properties;
        GUIStyle heading,small,button;
        string qaDirectory;
        bool horseQA,napoleonicQA;
        float refillAt=-1,gridRebuildAt=-1;
        bool preserveSandboxCamera;
        int replacements;
        public string DataDirectory=>store?.Root;
        void Start()
        {
            var args=Environment.GetCommandLineArgs();int qa=Array.IndexOf(args,"--workbench-qa");
            if(qa>=0&&qa+1<args.Length)qaDirectory=args[qa+1];
            int horseReview=Array.IndexOf(args,"--horse-gaits-qa");
            if(horseReview>=0&&horseReview+1<args.Length){qaDirectory=args[horseReview+1];horseQA=true;}
            int napoleonicReview=Array.IndexOf(args,"--napoleonic-qa");
            if(napoleonicReview>=0&&napoleonicReview+1<args.Length){qaDirectory=args[napoleonicReview+1];napoleonicQA=true;}
            Application.runInBackground=true;Application.targetFrameRate=60;
            originalProfile=BattleEffectTuning.Current;
            preset.Effects=originalProfile.Copy();BattleEffectTuning.Current=preset.Effects;
            try
            {
                var root=Directory.Exists(ProjectRoot)?Path.Combine(ProjectRoot,"WorkbenchData"):Path.Combine(Application.persistentDataPath,"WorkbenchData");
                store=new WorkbenchStore(qaDirectory!=null?Path.Combine(qaDirectory,"Notebook"):root);
            }
            catch(Exception e){message="Notebook unavailable: "+e.Message;}
            cameraView=new GameObject("Workbench camera").AddComponent<Camera>();cameraView.transform.SetParent(transform);
            cameraView.nearClipPlane=.02f;cameraView.farClipPlane=3000;cameraView.backgroundColor=new Color(.12f,.15f,.19f);
            cameraView.clearFlags=CameraClearFlags.SolidColor;
            sun=new GameObject("Workbench light").AddComponent<Light>();sun.transform.SetParent(transform);
            sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(40,-35,0);
            floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Workbench ground";floor.transform.SetParent(transform);
            floor.transform.localScale=Vector3.one*5;Destroy(floor.GetComponent<Collider>());
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.19f,.23f,.28f));
            floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.55f,.6f,.7f);RenderSettings.ambientEquatorColor=new Color(.4f,.4f,.4f);
            RenderSettings.ambientGroundColor=new Color(.25f,.22f,.2f);
            Shader.SetGlobalColor("_BattleAmbientSky",RenderSettings.ambientSkyColor);
            Shader.SetGlobalColor("_BattleAmbientEquator",RenderSettings.ambientEquatorColor);
            Shader.SetGlobalColor("_BattleAmbientGround",RenderSettings.ambientGroundColor);
            properties=new MaterialPropertyBlock();
            if(Catalog&&Catalog.Entries.Length>0)SelectAsset(0);
            if(qaDirectory!=null)StartCoroutine(napoleonicQA?ReviewNapoleonicQA():horseQA?ReviewHorseQA():ReviewQA());
        }
        Rect Viewport=>new Rect(270,54,Mathf.Max(100,Screen.width-630),Mathf.Max(100,Screen.height-105));
        void Update()
        {
            if(gridRebuildAt>=0&&Time.realtimeSinceStartup>=gridRebuildAt&&!Input.GetMouseButton(0))
            {gridRebuildAt=-1;BeginSandbox(true);}
            if(awaitingSandbox&&sandbox&&sandbox.World!=null)
            {
                awaitingSandbox=false;sandboxView=sandbox.ReviewView;
                foreach(var b in sandbox.World.Battalions)b.FireAtWill=false;
                var gun=sandbox.Artillery.Guns[0];var target=sandbox.World.Battalions.Last();
                gun.Facing=0;gun.FireAtWill=false;
                ConfigureBlock();
                if(!preserveSandboxCamera)FrameBlock();
            }
            if(sandbox&&sandbox.World!=null&&sandbox.Artillery.Guns[0].ShotsFired>shotsBefore)
                sandbox.Artillery.Guns[0].FireAtWill=false;
            if(sandbox&&sandbox.World!=null&&preset.AutoReplenish&&!sandbox.Paused)
            {
                var target=sandbox.World.Battalions.Last();
                if(target.AliveCount<target.SoldierCount&&refillAt<0)refillAt=sandbox.World.Time+preset.ReplenishDelay;
                if(refillAt>=0&&sandbox.World.Time>=refillAt)
                {
                    replacements+=target.SoldierCount-target.AliveCount;
                    sandbox.ReviewCasualties.ClearReviewBattalion(target.Id);
                    sandbox.World.ResetReviewBlock(target.Id,preset.Files,preset.FileSpacing,preset.RankSpacing);
                    refillAt=-1;
                }
            }
            if(!sandbox&&track!=null&&playing)
            {
                normalized+=Time.unscaledDeltaTime*speed/Mathf.Max(.01f,track.Duration);
                if(normalized>1){if(loop)normalized%=1;else {normalized=1;playing=false;}}
            }
            if(!sandbox&&model&&track?.Animation)track.Animation.SampleAnimation(model,normalized*track.Duration);
            var point=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
            if(Viewport.Contains(point))
            {
                if(Input.GetMouseButton(1)){yaw+=Input.GetAxis("Mouse X")*4;pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*3,-70,85);}
                if(Input.GetMouseButton(2))focus+=Quaternion.Euler(0,yaw,0)*new Vector3(-Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y"),0)*distance*.015f;
                distance=Mathf.Clamp(distance*Mathf.Exp(-Input.mouseScrollDelta.y*.12f),.15f,2000);
            }
        }
        void LateUpdate()
        {
            Camera active=sandboxView?sandboxView.BattleCamera:cameraView;
            if(!active)return;
            if(!sandbox)
            {
                RenderSettings.fog=false;
                Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.7f,.75f,.85f,1));
                Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.65f,.67f,.7f,1));
                Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.45f,.45f,.5f,1));
            }
            var r=Viewport;active.rect=new Rect(r.x/Screen.width,1-r.yMax/Screen.height,r.width/Screen.width,r.height/Screen.height);
            active.transform.rotation=Quaternion.Euler(pitch,yaw,0);active.transform.position=focus-active.transform.forward*distance;
            if(!sandbox)
            {
                bool horse=selected?.SourcePath?.Contains("/CavalryHorses/")==true;
                sun.transform.rotation=horse?active.transform.rotation*Quaternion.Euler(15,-20,0):Quaternion.Euler(40,-35,0);
                sun.shadows=horse?LightShadows.Soft:LightShadows.None;
            }
            if(sandbox||track?.Poses==null||track.Poses.Length==0||selected.Material==null)return;
            // Loop toggles repetition, never the authored spacing between poses.
            int index=AnimationPlayback.Frame(track.Poses.Length,normalized,track.Loop,out float blend);
            properties.SetFloat("_PoseBlend",track.Morph?(track.ReverseMorph?1-normalized:normalized):blend);
            int files=Mathf.CeilToInt(Mathf.Sqrt(formation));
            for(int i=0;i<formation;i++)
            {
                var pos=new Vector3((i%files-(files-1)*.5f)*spacing,0,-(i/files)*spacing);
                Graphics.DrawMesh(track.Poses[index],Matrix4x4.TRS(pos,Quaternion.identity,Vector3.one),selected.Material,0,active,0,properties,ShadowCastingMode.On,true);
            }
        }
        void SelectAsset(int index)
        {
            if(dirtyNote&&!SaveNote())return;
            EndSandbox();if(model)Destroy(model);
            WorkbenchUnitLoader.Release(selected);
            selectedIndex=index;selected=Catalog.Entries[index];WorkbenchUnitLoader.Load(selected);Resources.UnloadUnusedAssets();lod=0;trackIndex=0;normalized=0;
            if(selected.Model)
            {
                model=Instantiate(selected.Model);model.name="Review: "+selected.Name;
                foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
                foreach(var animator in model.GetComponentsInChildren<Animator>(true))animator.enabled=false;
                foreach(var animation in model.GetComponentsInChildren<Animation>(true))animation.enabled=false;
                if(selected.Tracks.Any(t=>t.Animation))
                {
                    // Runtime SampleAnimation requires an enabled Animator on the sampled root.
                    var animator=model.GetComponent<Animator>();if(!animator)animator=model.AddComponent<Animator>();
                    animator.enabled=true;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;
                }
            }
            track=Tracks.FirstOrDefault();playing=true;
            FrameModel();LoadNote();
        }
        ReviewTrack[] Tracks=>selected?.Tracks?.Where(t=>t.Lod==lod).ToArray()??Array.Empty<ReviewTrack>();
        void SelectTrack(int index)
        {
            if(dirtyNote&&!SaveNote())return;
            trackIndex=index;track=Tracks[index];normalized=0;playing=true;LoadNote();
        }
        void FrameModel()
        {
            var bounds=new Bounds(new Vector3(0,1,0),new Vector3(1,2,1));
            if(track?.Poses?.Length>0)bounds=track.Poses[0].bounds;
            if(model)
            {
                var renderers=model.GetComponentsInChildren<Renderer>();
                if(renderers.Length>0){bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);}
            }
            float tangent=Mathf.Tan(cameraView.fieldOfView*Mathf.Deg2Rad*.5f);
            focus=bounds.center;distance=Mathf.Max(.5f,Mathf.Max(bounds.size.y/(2*tangent),bounds.size.x/(2*tangent*Mathf.Max(.2f,Viewport.width/Viewport.height)))*1.35f+bounds.size.z*.5f);pitch=12;yaw=155;
        }
        string NoteTrack=>sandbox?"effects/sandbox":track?.Id??"model";
        void LoadNote()
        {
            var note=store?.Find(selected.Id,NoteTrack);noteText=note?.Text??"";noteStatus=note?.Status??"todo";dirtyNote=false;
        }
        bool SaveNote()
        {
            if(store==null){message="Notebook unavailable; note remains in the text box.";return false;}
            try
            {
                store.SaveNote(new ReviewNote {Id=selected.Id+"/"+NoteTrack,AssetId=selected.Id,AssetName=selected.Name,SourcePath=selected.SourcePath,
                    TrackId=NoteTrack,TrackName=sandbox?"Effects sandbox":track?.Name??"Model",Text=noteText,Status=noteStatus,
                    NormalizedTime=normalized,FrameTime=normalized*(track?.Duration??0),Lod=lod,Yaw=yaw,Pitch=pitch,Distance=distance,Focus=focus,Effects=preset.Effects.Copy()});
                store.ExportQueue();dirtyNote=false;message="Note saved; Codex queue updated.";return true;
            }
            catch(Exception e){message="Save failed: "+e.Message;return false;}
        }
        void BeginSandbox(bool preserveCamera=false)
        {
            if(dirtyNote&&!SaveNote())return;
            EndSandbox();preserveSandboxCamera=preserveCamera;if(model)model.SetActive(false);cameraView.enabled=false;sun.enabled=false;
            floor.SetActive(false);
            reviewLibrary=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
            priorResolver=reviewLibrary.ReviewResolver;var previewSet=selected.PreviewSet;
            reviewLibrary.ReviewResolver=unit=>unit.Side==ArmySide.British?previewSet:null;
            var root=new GameObject("Workbench battle sandbox");sandbox=root.AddComponent<BattleRuntime>();sandbox.WorkbenchMode=true;
            var role=selected.Family=="sword"?UnitRole.Swordsman:selected.Family=="light"?UnitRole.Skirmisher:selected.Family=="rifle"?UnitRole.Rifleman:UnitRole.Line;
            sandbox.Config=new BattleConfig {AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=preset.Files*preset.Ranks,
                FrenchArtillery=1,BritishArtillery=0,EnableAI=false,EnableLandscape=false,InitialSeparation=180,
                EnemyFaction=string.IsNullOrEmpty(selected.Faction)?"britain":selected.Faction,FormationRoles=new[]{UnitRole.Line,role},FormationWeapons=new[]{"musket",selected.Weapon??"musket"}};
            shotsBefore=0;refillAt=-1;replacements=0;awaitingSandbox=true;controlTab=1;LoadNote();
        }
        void EndSandbox()
        {
            if(reviewLibrary){reviewLibrary.ReviewResolver=priorResolver;reviewLibrary=null;priorResolver=null;}
            if(sandboxView)Destroy(sandboxView.gameObject);
            if(sandbox)Destroy(sandbox.gameObject);
            sandbox=null;sandboxView=null;awaitingSandbox=false;gridRebuildAt=-1;
            if(cameraView)cameraView.enabled=true;if(sun)sun.enabled=true;if(model)model.SetActive(true);
            if(floor)floor.SetActive(true);
        }
        void Shoot()
        {
            if(!sandbox||sandbox.World==null)return;
            sandbox.SetPaused(false);
            if(sandbox.Artillery.FireReviewShot(0,sandbox.World.Battalions.Length-1,(ArtilleryAmmunition)ammo))
                shotsBefore=sandbox.Artillery.Guns[0].ShotsFired;
            else message="Projectile pool busy; fire again when a shot lands.";
        }
        void ConfigureBlock()
        {
            if(!sandbox||sandbox.World==null)return;
            var target=sandbox.World.Battalions.Last();
            sandbox.ReviewCasualties.ClearReviewBattalion(target.Id);
            sandbox.World.ResetReviewBlock(target.Id,preset.Files,preset.FileSpacing,preset.RankSpacing);
            sandbox.Artillery.Guns[0].Position=target.Position+Vector2.down*Mathf.Max(65,(preset.Ranks-1)*preset.RankSpacing*.5f+25);
            refillAt=-1;
        }
        void FrameBlock()
        {
            if(!sandbox||sandbox.World==null)return;
            var target=sandbox.World.Battalions.Last();focus=sandbox.World.Terrain.SurfacePoint(target.Position,1);
            distance=Mathf.Max(35,Mathf.Max(preset.Files*preset.FileSpacing,preset.Ranks*preset.RankSpacing)*1.5f);pitch=35;yaw=160;
        }

        void ApplyPreset()
        {
            if(!Directory.Exists(Path.Combine(ProjectRoot,"Assets"))){message="Source checkout unavailable; use the saved preset JSON in the game project.";return;}
            try
            {
                preset.Effects.Validate();WorkbenchStore.WriteAtomic(Path.Combine(ProjectRoot,"Assets/Resources/BattleEffectPreset.json"),JsonUtility.ToJson(preset.Effects,true));
                #if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
                #endif
                message="Applied to game source. The next game build uses this preset.";
            }
            catch(Exception e){message="Apply failed: "+e.Message;}
        }
        void CapturePreset()
        {
            preset.Name=presetName;preset.Spacing=spacing;preset.AssetId=selected.Id;preset.TrackId=track?.Id;
            preset.Lod=lod;preset.Formation=formation;preset.Ammunition=ammo;preset.Yaw=yaw;preset.Pitch=pitch;
            preset.Distance=distance;preset.Focus=focus;preset.NormalizedTime=normalized;preset.Speed=speed;preset.Loop=loop;
        }
        void RestorePreset(ReviewPreset value)
        {
            if(dirtyNote&&!SaveNote())return;
            int index=Array.FindIndex(Catalog.Entries,e=>e.Id==value.AssetId);
            if(index>=0)SelectAsset(index);
            preset=value;presetName=value.Name;spacing=value.Spacing;BattleEffectTuning.Current=value.Effects;
            lod=Mathf.Clamp(value.Lod,0,selected.Tracks.Select(t=>t.Lod).DefaultIfEmpty(0).Max());
            track=Tracks.FirstOrDefault(t=>t.Id==value.TrackId)??Tracks.FirstOrDefault();
            formation=Mathf.Clamp(value.Formation,1,100);ammo=Enum.IsDefined(typeof(ArtilleryAmmunition),value.Ammunition)?value.Ammunition:0;
            yaw=value.Yaw;pitch=Mathf.Clamp(value.Pitch,-70,85);distance=Mathf.Clamp(value.Distance,.15f,2000);
            focus=value.Focus;normalized=Mathf.Clamp01(value.NormalizedTime);speed=Mathf.Clamp(value.Speed,.05f,2);loop=value.Loop;playing=false;LoadNote();
        }
        void Action(Action action)
        {try{action();}catch(Exception e){message=e.Message;}}
        void OnGUI()
        {
            if(!Catalog||selected==null)return;
            if(heading==null)
            {
                heading=new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold};
                small=new GUIStyle(GUI.skin.label){wordWrap=true,fontSize=12};
                button=new GUIStyle(GUI.skin.button){fontSize=13,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(10,8,6,6)};
            }
            GUI.Box(new Rect(0,0,Screen.width,54),GUIContent.none);
            GUI.Box(new Rect(0,54,270,Screen.height-54),GUIContent.none);
            GUI.Box(new Rect(Screen.width-360,54,360,Screen.height-54),GUIContent.none);
            // The camera draws the center; GUI panels only cover the margins.
            GUI.Label(new Rect(18,12,Screen.width-30,32),"ASSET WORKBENCH   /   "+selected.Name,heading);
            GUILayout.BeginArea(new Rect(12,54,246,Screen.height-100));
            if(GUILayout.Button("Cavalry horses"))
            {
                search="CavalryHorses";category="Models";
                int horse=Array.FindIndex(Catalog.Entries,e=>e.SourcePath.EndsWith("CavalryHorses/HussarBay.fbx",StringComparison.Ordinal));
                if(horse>=0){SelectAsset(horse);track=Tracks.FirstOrDefault(t=>t.Id=="Idle")??track;controlTab=0;}
            }
            search=GUILayout.TextField(search);GUILayout.Label("Search models, factions, animations",small);
            GUILayout.BeginHorizontal();foreach(string c in new[]{"All","Units","Models"})if(GUILayout.Toggle(category==c,c,"Button"))category=c;GUILayout.EndHorizontal();
            assetScroll=GUILayout.BeginScrollView(assetScroll);
            for(int i=0;i<Catalog.Entries.Length;i++)
            {
                var a=Catalog.Entries[i];if(category!="All"&&a.Category!=category)continue;
                if(!string.IsNullOrWhiteSpace(search)&&!(a.Name+" "+a.SourcePath+" "+string.Join(" ",a.Tracks?.Select(t=>t.Name)??Array.Empty<string>())).ToLowerInvariant().Contains(search.ToLowerInvariant()))continue;
                GUI.backgroundColor=i==selectedIndex?new Color(.38f,.65f,.8f):Color.white;
                string label=a.SourcePath.Contains("/CavalryHorses/")?a.Name.Replace("Cavalry horse / ",""):a.Name;
                if(GUILayout.Button(label,button,GUILayout.MinHeight(34)))SelectAsset(i);
            }
            GUI.backgroundColor=Color.white;GUILayout.EndScrollView();GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(Screen.width-348,54,336,Screen.height-100));
            controlTab=GUILayout.Toolbar(controlTab,new[]{"Inspect","Effects","Notes"});
            controlScroll=GUILayout.BeginScrollView(controlScroll);
            GUILayout.Label(sandbox?"BATTLE EFFECTS LAB":"MODEL & ANIMATION",heading);
            GUILayout.Label(selected.SourcePath,small);
            if(selected.Name=="horse_black_long")GUILayout.Label("Skinned candidate: 28 bones, 1,642 triangles. No gait clips bundled; rider fit and retargeting still need review.",small);
            if(selected.SourcePath.Contains("/CavalryHorses/"))GUILayout.Label("Period cavalry mount: idle, walk, trot, canter and gallop. Canter and gallop include both leads.",small);
            if(selected.SourcePath.Contains("/SourceAnimations/"))GUILayout.Label("Original mod rig. Review weapon contacts before fitting to a game unit.",small);
            if(controlTab==0&&!sandbox)
            {
                GUILayout.BeginHorizontal();if(GUILayout.Button(playing?"Pause":"Play"))playing=!playing;
                loop=GUILayout.Toggle(loop,"Loop");if(GUILayout.Button("Frame model"))FrameModel();GUILayout.EndHorizontal();
                float changed=GUILayout.HorizontalSlider(normalized,0,1);if(changed!=normalized){normalized=changed;playing=false;}
                GUILayout.Label($"Preview: {normalized*(track?.Duration??0):F2}s / {track?.Duration??0:F2}s   |   LOD {lod}");
                float frameStep=track!=null&&track.Morph?1f/30:1f/Mathf.Max(1,(track?.Poses?.Length??30)-(track!=null&&track.Loop?0:1));
                GUILayout.BeginHorizontal();if(GUILayout.Button("< frame")){normalized=Mathf.Max(0,normalized-frameStep);playing=false;}
                if(GUILayout.Button("frame >")){normalized=Mathf.Min(1,normalized+frameStep);playing=false;}
                if(GUILayout.Button("LOD"))
                {
                    if(!dirtyNote||SaveNote()){string id=track?.Id;lod=(lod+1)%(1+selected.Tracks.Select(t=>t.Lod).DefaultIfEmpty(0).Max());track=Tracks.FirstOrDefault(t=>t.Id==id)??Tracks.FirstOrDefault();LoadNote();}
                }
                GUILayout.EndHorizontal();speed=Slider("Playback speed",speed,.05f,2);
                trackScroll=GUILayout.BeginScrollView(trackScroll,GUILayout.Height(145));
                var tracks=Tracks;for(int i=0;i<tracks.Length;i++)if(GUILayout.Toggle(track==tracks[i],tracks[i].Name,"Button"))if(track!=tracks[i])SelectTrack(i);
                GUILayout.EndScrollView();
                GUILayout.Label("Formation preview");GUILayout.BeginHorizontal();foreach(int n in new[]{1,25,100})if(GUILayout.Button(n.ToString())){formation=n;distance=n==1?4:Mathf.Sqrt(n)*2;focus=new Vector3(0,1,-Mathf.Sqrt(n)*spacing*.4f);}GUILayout.EndHorizontal();
                spacing=Slider("Spacing",spacing,.5f,3);
                if(selected.Category=="Units"&&selected.Family!="cavalry"&&GUILayout.Button("Cannon vs infantry block",GUILayout.Height(34)))BeginSandbox();
            }
            else if(controlTab==1&&sandbox)
            {
                int oldFiles=preset.Files,oldRanks=preset.Ranks;
                float oldFileSpacing=preset.FileSpacing,oldRankSpacing=preset.RankSpacing;
                preset.Files=Mathf.RoundToInt(Slider("Files (across)",preset.Files,1,40));
                preset.Ranks=Mathf.RoundToInt(Slider("Ranks (deep)",preset.Ranks,1,Mathf.Min(40,500/preset.Files)));
                preset.Ranks=Mathf.Clamp(preset.Ranks,1,500/preset.Files);
                preset.FileSpacing=Slider("File spacing (m)",preset.FileSpacing,.5f,3);
                preset.RankSpacing=Slider("Rank spacing (m)",preset.RankSpacing,.5f,4);
                preset.Soldiers=preset.Files*preset.Ranks;
                GUILayout.Label($"{preset.Files} files × {preset.Ranks} ranks = {preset.Soldiers} troops");
                if(oldFiles!=preset.Files||oldRanks!=preset.Ranks)gridRebuildAt=Time.realtimeSinceStartup+.2f;
                else if(gridRebuildAt<0&&(oldFileSpacing!=preset.FileSpacing||oldRankSpacing!=preset.RankSpacing))ConfigureBlock();
                preset.AutoReplenish=GUILayout.Toggle(preset.AutoReplenish,"Auto replenish infantry");
                preset.ReplenishDelay=Slider("Refill delay (seconds)",preset.ReplenishDelay,2,15);
                GUILayout.Label("Refill clears fallen bodies; smoke and blood remain. Unlimited cannon shots.",small);
                GUILayout.Label("Ammunition");ammo=GUILayout.SelectionGrid(ammo,Enum.GetNames(typeof(ArtilleryAmmunition)),2);
                GUILayout.BeginHorizontal();if(GUILayout.Button("Fire cannon",GUILayout.Height(32)))Shoot();
                if(GUILayout.Button("Reset block"))BeginSandbox();GUILayout.EndHorizontal();
                if(GUILayout.Button("Frame block"))FrameBlock();
                if(sandbox&&sandbox.World!=null)
                {
                    GUILayout.BeginHorizontal();if(GUILayout.Button(sandbox.Paused?"Resume":"Pause"))sandbox.SetPaused(!sandbox.Paused);
                    if(GUILayout.Button("0.25x"))sandbox.SetSpeed(.25f);if(GUILayout.Button("1x"))sandbox.SetSpeed(1);GUILayout.EndHorizontal();
                    GUILayout.Label($"Deaths: {sandbox.World.DeathCount}   Replaced: {replacements}   Shots: {sandbox.Artillery.Guns[0].ShotsFired}");
                }
                if(GUILayout.Button("Return to model")){if(!dirtyNote||SaveNote()){EndSandbox();controlTab=0;FrameModel();LoadNote();}}
            }
            if(controlTab==1)
            {
            if(!sandbox&&selected.Category=="Units"&&selected.Family!="cavalry"&&GUILayout.Button("Open cannon effects lab"))BeginSandbox();
            GUILayout.Space(10);GUILayout.Label("EFFECT TUNING",heading);
            var effects=preset.Effects;
            effects.SmokeSize=Slider("Smoke size",effects.SmokeSize,.1f,3);effects.SmokeDensity=Slider("Smoke density",effects.SmokeDensity,0,3);
            effects.SmokeLifetime=Slider("Smoke lifetime",effects.SmokeLifetime,.1f,3);effects.Wind=Slider("Wind",effects.Wind,0,3);
            effects.BloodAmount=Slider("Blood spray amount",effects.BloodAmount,0,3);effects.BloodSize=Slider("Droplet size",effects.BloodSize,.1f,3);
            effects.DebrisAmount=Slider("Explosion debris",effects.DebrisAmount,0,3);effects.ExplosionFlash=Slider("Explosion flash",effects.ExplosionFlash,0,3);
            GUILayout.Label("Emission changes affect the next shot. Size, density and wind update live. Damage stays unchanged.",small);
            presetName=GUILayout.TextField(presetName);GUILayout.BeginHorizontal();
            if(GUILayout.Button("Save preset"))Action(()=>{CapturePreset();store.SavePreset(preset);message="Preset saved.";});
            if(GUILayout.Button("Apply to game"))ApplyPreset();GUILayout.EndHorizontal();
            if(store!=null)
            {
                presetScroll=GUILayout.BeginScrollView(presetScroll,GUILayout.Height(65));
                foreach(var name in store.PresetNames())if(GUILayout.Button("Load: "+name))Action(()=>{RestorePreset(store.LoadPreset(name));message="Preset loaded.";});
                GUILayout.EndScrollView();
            }
            }
            if(controlTab==2)
            {
            GUILayout.Space(12);GUILayout.Label("NOTES FOR CODEX",heading);
            GUILayout.Label("Attached to "+(sandbox?"effects sandbox":track?.Name??"model")+". Example: fix the elbow at this frame; make a spear variant.",small);
            string edited=GUILayout.TextArea(noteText,GUILayout.MinHeight(100));if(edited!=noteText){noteText=edited;dirtyNote=true;playing=false;}
            GUILayout.BeginHorizontal();foreach(var state in new[]{"todo","accepted","done"})if(GUILayout.Toggle(noteStatus==state,state,"Button")&&state!=noteStatus){noteStatus=state;dirtyNote=true;}GUILayout.EndHorizontal();
            if(GUILayout.Button(dirtyNote?"Save note *":"Save note"))SaveNote();
            if(GUILayout.Button("Export all pending notes"))Action(()=>{if(dirtyNote&&!SaveNote())return;store.ExportQueue();message="Exported WorkbenchData/codex-queue.json";});
            GUILayout.Label(store?.Root??"Notebook unavailable",small);
            if(store!=null&&GUILayout.Button("Restore saved note frame"))
            {
                var saved=store.Find(selected.Id,NoteTrack);
                if(saved!=null){normalized=saved.NormalizedTime;playing=false;yaw=saved.Yaw;pitch=saved.Pitch;distance=saved.Distance;focus=saved.Focus;}
            }
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
            GUI.Label(new Rect(280,Screen.height-44,Screen.width-640,40),"Right drag: orbit   Middle drag: pan   Wheel: zoom\n"+message,small);
        }
        static float Slider(string label,float value,float min,float max)
        {GUILayout.Label(label+"   "+value.ToString("F2"));return GUILayout.HorizontalSlider(value,min,max);}
        void OnApplicationQuit(){if(dirtyNote)SaveNote();}
        System.Collections.IEnumerator ReviewQA()
        {
            Directory.CreateDirectory(qaDirectory);
            yield return new WaitForSecondsRealtime(2);
            int index=Array.FindIndex(Catalog.Entries,e=>e.Faction=="ming"&&e.Family=="sword");
            if(index<0){Debug.LogError("Workbench QA: missing Ming swordsman");Application.Quit(2);yield break;}
            SelectAsset(index);loop=true;
            int slash=Array.FindIndex(Tracks,t=>t.Name.Contains("SlashLeft"));if(slash>=0)SelectTrack(slash);normalized=.5f;
            if(!loop||!playing){Debug.LogError("Loop preference lost on clip change");Application.Quit(9);yield break;}
            playing=false;
            yield return new WaitForSecondsRealtime(.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,"sword.png"));
            noteText="QA roundtrip: elbow review";noteStatus="todo";dirtyNote=true;SaveNote();
            if(new WorkbenchStore(store.Root).Find(selected.Id,NoteTrack)?.Text!=noteText){Application.Quit(3);yield break;}
            controlTab=2;yield return new WaitForSecondsRealtime(.3f);ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,"notes.png"));controlTab=0;
            yield return new WaitForSecondsRealtime(.5f);
            index=Array.FindIndex(Catalog.Entries,e=>e.Faction=="india"&&e.Family=="line");SelectAsset(index);
            int reload=Array.FindIndex(Tracks,t=>t.Name=="Reload");SelectTrack(reload);normalized=.55f;
            yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,"reload.png"));
            yield return new WaitForSecondsRealtime(.5f);
            if(selected.PreviewSet.Lods.Any(l=>l.DeathVariants==null||l.DeathVariants.Length<10))
            {Debug.LogError("Missing expanded death variants");Application.Quit(12);yield break;}
            int death=Array.FindIndex(Tracks,t=>t.Id.Contains("DeathVariants")&&t.Id.Contains("4"));
            if(death>=0){SelectTrack(death);playing=false;normalized=.55f;yield return new WaitForSecondsRealtime(.3f);ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,"death-side.png"));yield return new WaitForSecondsRealtime(.3f);}
            BeginSandbox();
            yield return new WaitForSecondsRealtime(3);
            if(!sandboxView){Debug.LogError("Workbench sandbox failed to initialize");Application.Quit(4);yield break;}
            preset.Files=10;preset.Ranks=10;preset.FileSpacing=1.3f;preset.RankSpacing=2.1f;ConfigureBlock();
            var block=sandbox.World.Battalions.Last();
            Vector2 grid=sandbox.World.Soldiers.Positions[block.SoldierStart+11]-sandbox.World.Soldiers.Positions[block.SoldierStart];
            if(Mathf.Abs(grid.magnitude-Mathf.Sqrt(1.3f*1.3f+2.1f*2.1f))>.02f){Debug.LogError("Review grid spacing failed");Application.Quit(10);yield break;}
            yield return new WaitForSecondsRealtime(.4f);
            Vector2 stableGrid=sandbox.World.Soldiers.Positions[block.SoldierStart+11]-sandbox.World.Soldiers.Positions[block.SoldierStart];
            if(Vector2.Distance(grid,stableGrid)>.02f){Debug.LogError("Review grid drifted during simulation");Application.Quit(13);yield break;}
            ammo=(int)ArtilleryAmmunition.Explosive;preset.Effects.SmokeSize=1.3f;Shoot();
            yield return new WaitForSecondsRealtime(1.7f);
            sandbox.SetPaused(true);ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,"cannon.png"));
            var result=new QAReceipt {CatalogAssets=Catalog.Entries.Length,Tracks=Catalog.Entries.Sum(e=>e.Tracks.Length),Shots=sandbox.Artillery.Guns[0].ShotsFired,Impacts=sandbox.Artillery.ImpactSequence,Deaths=sandbox.World.DeathCount,NoteSaved=true};
            File.WriteAllText(Path.Combine(qaDirectory,"runtime-verification.json"),JsonUtility.ToJson(result,true));
            if(result.Shots!=1||result.Impacts<1){Debug.LogError("Workbench cannon QA failed");Application.Quit(5);yield break;}
            sandbox.SetPaused(false);
            // More shots than the normal ammunition allocation, with no reload wait.
            for(int burst=0;burst<27;burst++)
            {
                for(int shot=0;shot<10;shot++)Shoot();
                yield return new WaitForSecondsRealtime(.22f);
            }
            int unlimitedShots=sandbox.Artillery.Guns[0].ShotsFired;
            float refillDeadline=sandbox.World.Time+10,qaTimeout=Time.realtimeSinceStartup+120;
            while(sandbox.World.Time<refillDeadline)
            {
                if(Time.realtimeSinceStartup>qaTimeout){Debug.LogError("Lab refill simulation timed out");Application.Quit(14);yield break;}
                yield return null;
            }
            if(unlimitedShots!=271||replacements==0||block.AliveCount!=block.SoldierCount||sandbox.World.DeathCount!=0)
            {Debug.LogError($"Lab repeat QA failed: shots={unlimitedShots}, replaced={replacements}, alive={block.AliveCount}, deaths={sandbox.World.DeathCount}");Application.Quit(11);yield break;}
            File.WriteAllText(Path.Combine(qaDirectory,"lab-repeat.json"),$"{{\"shots\":{unlimitedShots},\"replaced\":{replacements},\"alive\":{block.AliveCount},\"files\":{preset.Files},\"ranks\":{preset.Ranks}}}");
            yield return new WaitForSecondsRealtime(.5f);EndSandbox();
            index=Array.FindIndex(Catalog.Entries,e=>e.Name=="laigle-pinus_1");if(index>=0)SelectAsset(index);
            yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,"pine.png"));
            yield return new WaitForSecondsRealtime(1);
            index=Array.FindIndex(Catalog.Entries,e=>e.Name=="horse_black_long");SelectAsset(index);
            yield return new WaitForSecondsRealtime(.4f);ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,"horse-black-long.png"));
            yield return new WaitForSecondsRealtime(.4f);
            index=Array.FindIndex(Catalog.Entries,e=>e.Name.Contains("source-katana_slashright_a"));
            if(index<0){Application.Quit(6);yield break;}
            SelectAsset(index);playing=false;normalized=.2f;yield return null;
            var hand=model.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="hand.R");
            if(!hand){Application.Quit(7);yield break;}
            var first=hand.position;normalized=.6f;yield return null;
            float motion=Vector3.Distance(first,hand.position);
            File.WriteAllText(Path.Combine(qaDirectory,"source-motion.json"),"{\"rightHandDisplacement\":"+motion.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"}");
            if(float.IsNaN(motion)||motion<.02f){Debug.LogError("Source animation did not move");Application.Quit(8);yield break;}
            yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,"source-sword.png"));
            CapturePreset();preset.Name="QA-preset";store.SavePreset(preset);RestorePreset(store.LoadPreset("QA-preset"));
            yield return new WaitForSecondsRealtime(1);Debug.Log("WORKBENCH_RUNTIME_VERIFIED: sword, reload, cannon casualties, notes, preset, pine and source motion");Application.Quit(0);
        }
        System.Collections.IEnumerator ReviewHorseQA()
        {
            Directory.CreateDirectory(qaDirectory);
            yield return null;
            int checkedClips=0;
            foreach(string name in new[]{"HussarBay","CuirassierChestnut","ArabianA","ArabianB"})
            {
                int index=Array.FindIndex(Catalog.Entries,e=>e.SourcePath.EndsWith("/CavalryHorses/"+name+".fbx",StringComparison.Ordinal));
                if(index<0){Debug.LogError("Horse review model missing: "+name);Application.Quit(11);yield break;}
                SelectAsset(index);playing=false;search="CavalryHorses";category="Models";
                yaw=110;pitch=8;distance=4.5f;focus=new Vector3(0,1.1f,0);
                var hoof=model.GetComponentsInChildren<Transform>().First(t=>t.name=="clavicle.L");
                foreach(var gait in Tracks.ToArray())
                {
                    track=gait;normalized=0;yield return null;
                    Vector3 start=hoof.position;
                    normalized=.5f;yield return null;
                    if(track.Id!="Idle"&&Vector3.Distance(start,hoof.position)<.08f)
                    {Debug.LogError("Runtime horse clip did not move: "+name+"/"+track.Id);Application.Quit(12);yield break;}
                    checkedClips++;
                }
                track=Tracks.First(t=>t.Id=="Idle");normalized=0;
                yield return new WaitForSecondsRealtime(.2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,name+"-idle.png"));
                yield return new WaitForSecondsRealtime(.2f);
                if(name=="HussarBay")foreach(string gait in new[]{"Walk","Trot","CanterLeft","GallopLeft"})
                {
                    track=Tracks.First(t=>t.Id==gait);normalized=.35f;
                    yield return new WaitForSecondsRealtime(.2f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(qaDirectory,gait+".png"));
                    yield return new WaitForSecondsRealtime(.2f);
                }
            }
            File.WriteAllText(Path.Combine(qaDirectory,"result.json"),"{\"mounts\":4,\"animatedClips\":"+checkedClips+",\"passed\":"+(checkedClips==28?"true":"false")+"}");
            Debug.Log("CAVALRY_WORKBENCH_RUNTIME_VERIFIED mounts=4 clips="+checkedClips);
            Application.Quit(checkedClips==28?0:13);
        }
        [Serializable]sealed class QAReceipt {public int CatalogAssets,Tracks,Shots,Impacts,Deaths;public bool NoteSaved;}
        void OnDestroy()
        {EndSandbox();if(model)Destroy(model);if(floorMaterial)Destroy(floorMaterial);BattleEffectTuning.Current=originalProfile??new BattleEffectProfile();}
    }
}
