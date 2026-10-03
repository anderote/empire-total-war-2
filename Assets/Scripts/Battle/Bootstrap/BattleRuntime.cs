using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EmpireBattle
{
    public sealed class BattleRuntime : MonoBehaviour
    {
        public BattleConfig Config = new BattleConfig();
        public bool ArtilleryInspectionMode;
        public bool WorkbenchMode;
        public BattleWorld World { get; private set; }
        public bool Paused { get; private set; }
        public float Speed { get; private set; } = 1f;
        public static readonly float[] PlaybackSpeeds = { .25f, 1f, 2f, 4f };
        private BattleView view;
        private CombatSystem combat;
        private TacticalAI ai;
        private ArtillerySystem artillery;
        private BattleArmorySession armorySession;
        public BattleArmorySession ArmorySession=>armorySession;
        public ArtillerySystem Artillery=>artillery;
        public BattleView ReviewView=>view;
        public BattleCasualtyView ReviewCasualties=>casualties;
        public void SetPaused(bool value)=>Paused=value;
        private MusketSmoke smoke;
        private MusketFlashView musketFlashes;
        private BattleProjectileView projectiles;
        public int ActiveRagdolls => casualties ? casualties.ActiveRagdolls : 0;
        public int FrozenRagdolls => casualties ? casualties.FrozenRagdolls : 0;
        private int maxSmokePuffs, maxMusketFlashes;
        private float PresentationTime => World.Time+accumulator;
        public Func<bool> ReturnToCampaign;
        public Action ConcedeBattle;
        private BattleAudio battleAudio;
        private BattleCasualtyView casualties;
        private BattleAtmosphere atmosphere;
        private float accumulator;
        private const float Step = 0.05f;
        private bool grassEnabled = true;
        private int maxGrassVertices, maxGrassPatches;
        private RuralLandscape landscape;
        private BattlePerformanceCapture performanceCapture;
        private string telemetryPath, profileLog;
        private float benchmarkSpeed=1f;
        private bool useMovementJobs=true,modernCrowd=true;
        private int crowdBatchCapacity=511;
        private bool cinemaEnabled=true, focusEnabled=false, bloomEnabled=true, colorEnabled=true, vignetteEnabled=true, postProcessingEnabled=true;
        private float benchmarkSeconds;
        private string benchmarkPath;
        private string benchmarkPhase = "march";
        private float elapsed;
        private int startupFrames = 3;
        private readonly List<float> frames = new List<float>(8192);
        private readonly List<float> simulationCosts = new List<float>(8192);
        private bool benchmarkOrders;
        private int maneuverStage;
        private string screenshotPath;
        private bool screenshotTaken;
        private bool restartRequested;
        private int maxRenderedSoldiers;
        private float screenshotDelay = 3f;
        private bool inspect;
        private string rosterFactions;
        public void StartRosterBattle(string player,string enemy){Config=UnitRoster.CreateBattle(player,enemy);restartRequested=true;}
        private string meleeCase;private bool meleeFormed,meleeVerified;
        private ArtilleryAmmunition? inspectionAmmunition;
        private Vector2? inspectionGround;
        private bool artilleryInspection;
        private ArtilleryGroundMarks artilleryMarks;
        private bool recoveryInspection, recoveryInspectionStarted, recoveryInspectionVerified;
        private bool casualtyInspection;
        private bool casualtyInspectionStarted;
        private bool casualtyInspectionVerified;
        private float casualtyAge;
        private Vector2 inspectionCenter = new Vector2(80,0);
        private float inspectionDistance = 180;
        private float inspectionPitch = 55;
        private float? inspectionYaw;

        private void Start()
        {
            if(!WorkbenchMode&&EmpireGame.GameApplication.Enabled&&!Config.CampaignDeployment)Config=EmpireGame.GameApplication.BattleConfiguration();
            Application.targetFrameRate = Empire.UI.GamePreferences.FrameLimit;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = Empire.UI.GamePreferences.VSync?1:0;
            string[] args = Environment.GetCommandLineArgs();
            bool infantryOrdersReview = Array.IndexOf(args, "--infantry-orders-review") >= 0;
            recoveryInspection = Array.IndexOf(args, "--recovery-inspect") >= 0;
            artilleryInspection = ArtilleryInspectionMode || Array.IndexOf(args, "--artillery-inspect") >= 0;
            for (int i = 0; i + 1 < args.Length; ++i)
            {
                if(args[i]=="--unit-roster")rosterFactions=args[i+1];
                if(args[i]=="--melee-case")meleeCase=args[i+1];
                if (args[i] == "--casualty-age") casualtyAge=float.Parse(args[i+1],System.Globalization.CultureInfo.InvariantCulture);
                if (args[i] == "--casualty-inspect") { casualtyInspection=true; inspectionDistance=24; }
                if (args[i] == "--landscape") Config.EnableLandscape = args[i+1] != "off";
                if (args[i] == "--crowd-backend") modernCrowd=args[i+1]!="legacy";
                if (args[i] == "--crowd-batch-size") crowdBatchCapacity=Mathf.Clamp(int.Parse(args[i+1]),1,511);
                if (args[i] == "--movement-backend") useMovementJobs=args[i+1]!="scalar";
                if (args[i] == "--fast-forward-backend") BattleOptimizationSettings.FastForwardOptimizations=args[i+1]!="baseline";
                if (args[i] == "--contact-math-backend") BattleOptimizationSettings.ContactMathOptimizations=args[i+1]!="baseline";
                if (args[i] == "--render-work-backend") BattleOptimizationSettings.RenderWorkOptimizations=args[i+1]!="baseline";
                if (args[i] == "--hitch-backend") BattleOptimizationSettings.HitchOptimizations=args[i+1]!="baseline";
                if (args[i] == "--contact-backend")
                {BattleOptimizationSettings.CrowdFlow=args[i+1]=="flow";BattleOptimizationSettings.RegimentContacts=args[i+1]!="baseline";}
                if (args[i] == "--artillery-collision-backend") BattleOptimizationSettings.ArtilleryBroadphase=args[i+1]!="baseline";
                if (args[i] == "--telemetry") telemetryPath=args[i+1];
                if (args[i] == "--profile-log") profileLog=args[i+1];
                if (args[i] == "--benchmark-speed") benchmarkSpeed=float.Parse(args[i+1],System.Globalization.CultureInfo.InvariantCulture);
                if (args[i] == "--cinema") cinemaEnabled=args[i+1]!="off";
                if (args[i] == "--focus") focusEnabled=args[i+1]!="off";
                if (args[i] == "--bloom") bloomEnabled=args[i+1]!="off";
                if (args[i] == "--color") colorEnabled=args[i+1]!="off";
                if (args[i] == "--vignette") vignetteEnabled=args[i+1]!="off";
                if (args[i] == "--post-processing") postProcessingEnabled=args[i+1]!="off";
                if (args[i] == "--grass") grassEnabled = args[i+1] != "off";
                if (args[i] == "--frame-cap") Application.targetFrameRate = Mathf.Clamp(int.Parse(args[i+1]), -1, 240);
                if (args[i] == "--ai") Config.EnableAI = args[i+1] != "off";
                if (args[i] == "--models") Config.UseDetailedInfantry = args[i+1] != "placeholder";
                if (args[i] == "--battalions") { Config.ExplicitArmyCounts=false; Config.InitialFormations=null; Config.ScenarioId=null; Config.BattalionsPerSide = Mathf.Clamp(int.Parse(args[i + 1]), 1, 80); Config.FrenchBattalions = Config.BritishBattalions = Config.FrenchCavalry = Config.BritishCavalry = 0; }
                if (args[i] == "--soldiers")
                {
                    int requested=int.Parse(args[i+1]);
                    if(requested<1||requested>BattleConfig.MaxPersonnelPerRegiment)throw new ArgumentOutOfRangeException("--soldiers","A personnel regiment must contain between 1 and 500 troops.");
                    Config.SoldiersPerBattalion=requested;
                }
                if (args[i] == "--benchmark-seconds") benchmarkSeconds = Mathf.Clamp(float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture), 5, 600);
                if (args[i] == "--benchmark-output") benchmarkPath = args[i + 1];
                if (args[i] == "--benchmark-phase") benchmarkPhase = args[i + 1];
                if (args[i] == "--screenshot") screenshotPath = args[i + 1];
                if (args[i] == "--screenshot-delay") screenshotDelay = float.Parse(args[i+1],System.Globalization.CultureInfo.InvariantCulture);
                if (args[i] == "--yaw") inspectionYaw = float.Parse(args[i+1],System.Globalization.CultureInfo.InvariantCulture);
                if (args[i] == "--pitch") inspectionPitch = float.Parse(args[i+1],System.Globalization.CultureInfo.InvariantCulture);
                if (args[i] == "--center") { var parts=args[i+1].Split(','); inspectionCenter = new Vector2(float.Parse(parts[0],System.Globalization.CultureInfo.InvariantCulture),float.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture)); }
                if (args[i] == "--artillery-ground") { var xy = args[i+1].Split(','); inspectionGround = new Vector2(float.Parse(xy[0],System.Globalization.CultureInfo.InvariantCulture),float.Parse(xy[1],System.Globalization.CultureInfo.InvariantCulture)); }
                if (args[i] == "--artillery-ammo" && Enum.TryParse<ArtilleryAmmunition>(args[i+1], true, out var inspectAmmo)) inspectionAmmunition = inspectAmmo;
                if (args[i] == "--inspect") { inspect = true; inspectionDistance = float.Parse(args[i+1],System.Globalization.CultureInfo.InvariantCulture); }
            }
            Config=EmpireCampaign.Geography.RegionalBattleInspection.Configure(Config,args);
            // Uncapped profiling must not inherit a saved VSync preference. This
            // affects only this benchmark process and never writes PlayerPrefs.
            if(benchmarkSeconds>0&&Application.targetFrameRate<0)QualitySettings.vSyncCount=0;
            if(recoveryInspection){Config.ExplicitArmyCounts=false;Config.InitialFormations=null;Config.ScenarioId=null;Config.FrenchBattalions=Config.BritishBattalions=Config.FrenchCavalry=Config.BritishCavalry=0;Config.BattalionsPerSide=1;Config.SoldiersPerBattalion=80;Config.EnableAI=false;Config.FrenchArtillery=Config.BritishArtillery=0;}
            if(casualtyInspection){Config.ExplicitArmyCounts=false;Config.InitialFormations=null;Config.ScenarioId=null;Config.FrenchBattalions=Config.BritishBattalions=Config.FrenchCavalry=Config.BritishCavalry=0;Config.BattalionsPerSide=1;Config.SoldiersPerBattalion=80;Config.EnableAI=false;}
            if (benchmarkSeconds > 0 && benchmarkPhase == "combat") Config.InitialSeparation = 100;
            if (artilleryInspection) { Config.ExplicitArmyCounts=false;Config.InitialFormations=null;Config.ScenarioId=null;Config.FrenchBattalions = Config.BritishBattalions = Config.FrenchCavalry = Config.BritishCavalry = 0; Config.FrenchArtillery = Config.BritishArtillery = 4; Config.BattalionsPerSide = 1; Config.SoldiersPerBattalion = 500; Config.EnableAI = false; Config.InitialSeparation = 350; Config.EnableLandscape = false; }
            if(!string.IsNullOrEmpty(meleeCase))Config=MeleeEncounter.Config(meleeCase);
            if(!string.IsNullOrEmpty(rosterFactions)){var pair=rosterFactions.Split(",");Config=UnitRoster.CreateBattle(pair[0],pair.Length>1?pair[1]:"britain");}
            if (infantryOrdersReview) Config = new BattleConfig { BattalionsPerSide = 1,
                SoldiersPerBattalion = 250, InitialSeparation = 100, EnableAI = false,
                EnableLandscape = false, FrenchArtillery = 0, BritishArtillery = 0 };
            Restart();
            if (infantryOrdersReview) gameObject.AddComponent<InfantryOrdersReview>().Initialize(this);
            if(benchmarkSeconds>0)SetSpeed(benchmarkSpeed);
            if(!string.IsNullOrEmpty(telemetryPath))
            {
                performanceCapture=gameObject.AddComponent<BattlePerformanceCapture>();
                performanceCapture.Initialize(this,telemetryPath,profileLog);
            }
        }

        public void Restart()
        {
            armorySession?.Complete();
            if (view != null) Destroy(view.gameObject);
            World?.MovementJobs?.Dispose();
            World = new BattleWorld(Config);
            BattleScenarios.Deploy(World);
            if(useMovementJobs)World.MovementJobs=new SoldierMovementJobs(World);
            combat = new CombatSystem(World);
            ai = new TacticalAI(World);
            artillery = new ArtillerySystem(World);
            BattleScenarios.AlignArtillery(World,artillery);
            bool armoryEnabled=!WorkbenchMode&&!Config.CampaignDeployment&&benchmarkSeconds<=0&&!inspect&&!artilleryInspection&&!casualtyInspection&&!recoveryInspection&&string.IsNullOrEmpty(meleeCase);
            bool upgradesReview=Array.IndexOf(Environment.GetCommandLineArgs(),"--unit-upgrades-review")>=0;
            armorySession=armoryEnabled?new BattleArmorySession(World,artillery,upgradesReview||Array.IndexOf(Environment.GetCommandLineArgs(),"--ui-workbench-review")>=0?null:Path.Combine(Application.persistentDataPath,"unit-armory.json")):null;
            if (artilleryInspection)
            {
                // Two demonstration guns with targets safely outside musket range.
                artillery.Guns[0].Position = new Vector2(-5, -100);
                artillery.Guns[1].Position = new Vector2(5, -100);
                if (inspectionAmmunition.HasValue) artillery.Guns[1].AmmunitionType = inspectionAmmunition.Value;
                if (inspectionGround.HasValue)
                {
                    artillery.OrderGround(0, inspectionGround.Value + Vector2.left * 4f);
                    artillery.OrderGround(1, inspectionGround.Value + Vector2.right * 4f);
                }
                for (int i = 2; i < artillery.Guns.Length; i++) artillery.Guns[i].FireAtWill = false;
                foreach (var b in World.Battalions) b.FireAtWill = false;
            }
            if(!string.IsNullOrEmpty(meleeCase))MeleeEncounter.Setup(World,meleeCase);
            accumulator = 0;
            Paused = false;
            view = new GameObject("Battle presentation").AddComponent<BattleView>();
            view.OnRestart = () => restartRequested = true;
            view.OnExitBattle = ExitBattle;
            view.OnConcedeBattle = () => ConcedeBattle?.Invoke();
            view.OnRosterBattle = StartRosterBattle;
            view.OnTogglePause = () => Paused = !Paused;
            view.IsPaused = () => Paused;
            view.OnSetSpeed = SetSpeed;
            view.OnCycleSpeed = () => SetSpeed(PlaybackSpeeds[(Array.IndexOf(PlaybackSpeeds, Speed)+1)%PlaybackSpeeds.Length]);
            view.GetSpeed = () => Speed;
            view.GetPresentationTime = () => PresentationTime;
            view.ModernCrowdInstancing=modernCrowd;view.CrowdBatchCapacity=crowdBatchCapacity;
            view.Initialize(World);
            view.Cinematics = view.gameObject.AddComponent<BattleCinematics>();
            view.Cinematics.CinemaEnabled=cinemaEnabled;view.Cinematics.FocusEnabled=focusEnabled;
            view.Cinematics.BloomEnabled=bloomEnabled;view.Cinematics.ColorEnabled=colorEnabled;view.Cinematics.VignetteEnabled=vignetteEnabled;
            view.Cinematics.Initialize(view);
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData cameraData=view.BattleCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing=postProcessingEnabled;
            view.InitializeArtilleryCommands(artillery);
            casualties=view.gameObject.AddComponent<BattleCasualtyView>();
            casualties.Initialize(World,view.BattleCamera);
            view.SetCasualtyView(casualties);
            view.EnableInput = benchmarkSeconds <= 0 && !WorkbenchMode;
            view.HideReviewUI=WorkbenchMode;
            if(WorkbenchMode)view.GetComponent<BattleHUD>().enabled=false;
            if(recoveryInspection)view.SetInspectionView(World.Battalions[0].Position,24,35);
            if(casualtyInspection)view.SetInspectionView(World.Battalions[0].Position,inspectionDistance,35);
            if (artilleryInspection) view.SetInspectionView(new Vector2(0, -100), 23, 32, -30);
            if(!string.IsNullOrEmpty(meleeCase))view.SetInspectionView(Vector2.down*8,60,45,180);
            if (inspect) view.SetInspectionView(inspectionCenter,inspectionDistance,inspectionPitch,inspectionYaw);
            var sun = new GameObject("Late afternoon sun");
            sun.transform.SetParent(view.transform);
            sun.transform.rotation = Quaternion.Euler(38,-35,0);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f;
            light.color = new Color(1f,.91f,.76f); light.shadows = LightShadows.None;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.5f,.57f,.65f);
            landscape=view.gameObject.AddComponent<RuralLandscape>();
            landscape.GrassEnabled=grassEnabled;
            landscape.Initialize(World, view.BattleCamera);
            atmosphere = view.gameObject.AddComponent<BattleAtmosphere>();
            atmosphere.Initialize(World, view.BattleCamera, light);
            smoke = view.gameObject.AddComponent<MusketSmoke>();
            smoke.Initialize(World, view.BattleCamera,light);
            smoke.SoftIntersections = true;
            smoke.PresentationTime=()=>PresentationTime;
            musketFlashes=view.gameObject.AddComponent<MusketFlashView>();
            musketFlashes.Initialize(World,view.BattleCamera,light);
            musketFlashes.PresentationTime=()=>PresentationTime;
            var cannonView = view.gameObject.AddComponent<CannonView>();
            cannonView.Initialize(World,artillery,view.BattleCamera,smoke);
            cannonView.PresentationTime = () => PresentationTime;
            landscape.TreeBroken = cannonView.EmitTreeSplinters;
            landscape.Artillery = artillery;
            if(Config.RegionalMap||Config.RequireDeployment)
                view.gameObject.AddComponent<RegionalBattleView>().Initialize(World,view.BattleCamera,landscape.ImpactSurfaces);
            casualties.Artillery = artillery;
            landscape.PresentationTime = () => PresentationTime;
            artilleryMarks = view.gameObject.AddComponent<ArtilleryGroundMarks>();
            artilleryMarks.Initialize(World, artillery, view.BattleCamera, smoke);
            artilleryMarks.PresentationTime = () => PresentationTime;
            battleAudio=view.gameObject.AddComponent<BattleAudio>();
            battleAudio.Initialize(World,artillery,view.BattleCamera,()=>Paused);
            battleAudio.PresentationTime=()=>PresentationTime;
            projectiles=view.gameObject.AddComponent<BattleProjectileView>();
            projectiles.Initialize(World,combat,view.BattleCamera,smoke,battleAudio,landscape.ImpactSurfaces);
            projectiles.PresentationTime=()=>PresentationTime;
            if(armorySession!=null)view.gameObject.AddComponent<BattleArmory>().Initialize(this,view,armorySession);
            else if(Config.CampaignDeployment)view.gameObject.AddComponent<BattleArmory>().InitializeDeployed(this,view);
            view.gameObject.AddComponent<GrenadeView>().Initialize(combat.Grenades);
            if(upgradesReview&&!GetComponent<UnitProgressionReview>())gameObject.AddComponent<UnitProgressionReview>();
            Debug.Log("UNIT_PROGRESSION_VERSION 1 · 54 equipment items · XP, skills, issued kit and battery progression");
        }

        void OnDestroy() { armorySession?.Complete();World?.MovementJobs?.Dispose(); }

        public void ExitBattle()
        {
            if(World==null||World.Outcome==BattleOutcome.Ongoing)return;
            if(Config.CampaignDeployment){ReturnToCampaign?.Invoke();return;}
            armorySession?.Complete();
            EmpireGame.GameApplication.ReturnToMenu();
        }

        public void SetSpeed(float value)
        {
            if (Array.IndexOf(PlaybackSpeeds, value) < 0) return;
            Speed = value;
            Paused = false;
        }

        private void Update()
        {
            if(atmosphere!=null)Config.WeaponWetness=atmosphere.Weather==WeatherKind.Rain?1f:0f;
            if (restartRequested) { restartRequested = false; Restart(); }
            // The initial presentation also creates GPU resources on the first draw.
            // Start the gameplay clock after those first rendered frames, then retain the benchmark warm-up.
            if (startupFrames > 0) { startupFrames--; return; }
            if(benchmarkSeconds>0 && Time.unscaledDeltaTime>.1f)
                Debug.Log($"BATTLE_FRAME_HITCH wall={elapsed:F2}s sim={World.Time:F2}s frame={Time.unscaledDeltaTime*1000:F1}ms alive={World.CountAlive(ArmySide.French)+World.CountAlive(ArmySide.British)} contactUnits={World.BodyContacts.LastContactRegiments} grids={World.BodyContacts.LastGridBuilds} overlap={World.BodyContacts.LastPassOverlap:F4}");
            elapsed += Time.unscaledDeltaTime;
            if(casualtyInspection && Time.unscaledDeltaTime>.1f)Debug.Log("CASUALTY_FRAME_MS "+Time.unscaledDeltaTime*1000+" elapsed="+elapsed+" active="+casualties.ActiveRagdolls+" frozen="+casualties.FrozenRagdolls+casualties.ActivePhysicsReport());
            if(smoke)maxSmokePuffs=Mathf.Max(maxSmokePuffs,smoke.ActivePuffs);
            if(musketFlashes)maxMusketFlashes=Mathf.Max(maxMusketFlashes,musketFlashes.VisibleFlashes);
            if (view != null) maxRenderedSoldiers = Mathf.Max(maxRenderedSoldiers, view.VisibleSoldiers);
            if (!screenshotTaken && !string.IsNullOrEmpty(screenshotPath) && elapsed > screenshotDelay)
            {
                screenshotTaken = true;
                ScreenCapture.CaptureScreenshot(screenshotPath);
            }
            if (benchmarkSeconds > 0 && !benchmarkOrders && !casualtyInspection && !recoveryInspection && string.IsNullOrEmpty(meleeCase))
            {
                benchmarkOrders = true;
                foreach (var b in World.Battalions)
                {
                    if (benchmarkPhase == "routing") { b.Routing = true; b.Morale = 0; World.Withdraw(b.Id); }
                    else if (benchmarkPhase == "maneuver" && b.Side==ArmySide.French)
                    {b.FireAtWill=false;World.IssueMove(b.Id,b.Position+new Vector2(10,30),0);}
                    else if (benchmarkPhase == "march")
                        World.IssueMove(b.Id, b.Position + new Vector2(80, b.Side == ArmySide.French ? 120 : -120), b.Facing + 0.25f);
                }
            }
            if(benchmarkPhase=="maneuver"&&benchmarkOrders)
            {
                if(maneuverStage==0&&World.Time>6f)
                {foreach(var unit in World.Battalions)if(unit.Side==ArmySide.French)World.SetFormation(unit.Id,FormationKind.Column);maneuverStage++;}
                if(maneuverStage==1&&World.Time>18f)
                {var unit=World.Battalions[0];World.Halt(unit.Id);World.SetFormation(unit.Id,FormationKind.Line);World.IssueMove(unit.Id,unit.Position,Mathf.PI*.5f);maneuverStage++;}
                if(maneuverStage==2&&World.Time>45f)
                {World.ApplyCasualties(0,40);maneuverStage++;}
                if(inspect)view.SetInspectionView(World.Battalions[0].Position,inspectionDistance,inspectionPitch);
            }
            if(recoveryInspection && !recoveryInspectionStarted && elapsed>1)
            {
                recoveryInspectionStarted=true;
                var unit=World.Battalions[0];
                for(int i=0;i<12;i++)World.ApplyArtilleryHit(unit.SoldierStart+i,30,DeathCause.Shell,new Vector3((i%3-1)*2f,2f,2f),Vector3.zero);
            }
            if(recoveryInspection && !recoveryInspectionVerified && World.Time>10)
            {
                recoveryInspectionVerified=true;
                var unit=World.Battalions[0];bool valid=World.DeathCount==0&&unit.AliveCount==80;
                for(int i=0;i<12;i++)
                {
                    int soldier=unit.SoldierStart+i;
                    valid &= World.Soldiers.Health[soldier]==70f && RifleFireTiming.SoldierReady(World,unit,soldier);
                }
                if(valid)Debug.Log("ARTILLERY_RECOVERY_PLAYTEST_PASSED twelve wounded survivors rejoined with no corpses");
                else Debug.LogError("ARTILLERY_RECOVERY_PLAYTEST_FAILED");
            }
            if(casualtyInspection && !casualtyInspectionStarted && elapsed>1)
            {
                casualtyInspectionStarted=true;
                var burstTimer=System.Diagnostics.Stopwatch.StartNew();
                for(int i=0;i<12;i++)World.ApplyHit(0,1000,1);
                World.ApplyCasualties(0,20,DeathCause.Roundshot,new Vector3(2,3,5));
                World.ApplyCasualties(0,20,DeathCause.Shell,new Vector3(-3,5,5));
                for(int soldier=0;soldier<World.Soldiers.Alive.Length;soldier++)if(World.Soldiers.Alive[soldier])
                {World.ApplyWound(soldier,10,DeathCause.Musket,World.SoldierPosition(soldier,World.Time),Vector3.forward);break;}
                Debug.Log("COMBAT_EFFECTS_VERSION 2026-10-01 complete shell fragmentation and musket wound spurts");
                Debug.Log("CASUALTY_BURST_MS "+burstTimer.Elapsed.TotalMilliseconds);
            }
            if(casualtyInspection && !casualtyInspectionVerified && casualties.VisualTime>8 && (casualties.ActiveRagdolls==0 || casualties.VisualTime>12))
            {
                casualtyInspectionVerified=true;
                if(World.DeathCount!=52 || casualties.ActiveRagdolls!=0 || casualties.FrozenRagdolls!=52 || casualties.Blood.PermanentMarks<52 || casualties.SeveredLimbs<1 || casualties.FragmentedBodies<1 || casualties.Blood.MusketWoundSprays<1 || casualties.Blood.FragmentTrailDrops<1 || !casualties.ValidateCorpses(out _))
                    Debug.LogError("CASUALTY_PLAYTEST_FAILED deaths="+World.DeathCount+" active="+casualties.ActiveRagdolls+" frozen="+casualties.FrozenRagdolls+casualties.ActivePhysicsReport());
                else Debug.Log("CASUALTY_PLAYTEST_PASSED deaths=52 cannon=40 physical="+casualties.FrozenRagdolls+" fragmented="+casualties.FragmentedBodies+" woundSpurts="+casualties.Blood.MusketWoundSprays+" trailDrops="+casualties.Blood.FragmentTrailDrops+" active=0 marks="+casualties.Blood.PermanentMarks+" tallestBody="+casualties.HighestFrozenBody+" snapshotMs="+casualties.SnapshotMilliseconds);
                if(casualtyAge>0)casualties.Step(casualtyAge);
            }
            long simulationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            AdvanceSimulation(Time.unscaledDeltaTime);
            if(!string.IsNullOrEmpty(meleeCase)&&!meleeVerified&&World.Time>=15){meleeVerified=true;
                bool valid=meleeCase=="square"||meleeCase=="frontal"?(World.Battalions[0].ChargeSpent||combat.Melee.RepelledCharges>0):(combat.Melee.ResolvedStrikes>0||combat.Melee.ChargeImpacts>0);
                if(!MeleeEncounter.IsInfantryCase(meleeCase)&&meleeCase!="square"&&meleeCase!="frontal")valid &= combat.Melee.ChargeImpacts>0;
                Debug.Log((valid?"MELEE_NATIVE_PASSED ":"MELEE_NATIVE_FAILED ")+meleeCase+" strikes="+combat.Melee.ResolvedStrikes+" impacts="+combat.Melee.ChargeImpacts+" parries="+combat.Melee.ParriedStrikes+" rendered="+maxRenderedSoldiers);
            }
            if (benchmarkSeconds > 0 && elapsed > 2)
            {
                maxGrassVertices=Mathf.Max(maxGrassVertices,landscape.SubmittedGrassVertices);
                maxGrassPatches=Mathf.Max(maxGrassPatches,landscape.SubmittedGrassPatches);
                frames.Add(Time.unscaledDeltaTime * 1000);
                simulationCosts.Add((float)((System.Diagnostics.Stopwatch.GetTimestamp() - simulationStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
            }
            if (benchmarkSeconds > 0 && elapsed >= benchmarkSeconds)
            {
                if(performanceCapture)performanceCapture.Write();
                WriteBenchmark();
                benchmarkSeconds = 0;
                Application.Quit();
            }
        }

        // Shared frame step keeps simulation and casualty presentation on the same clock.
        private void AdvanceSimulation(float frameDelta)
        {
            // Death.Born uses World.Time. Aging effects while deployment holds that
            // clock at zero makes the first casualties skip straight to resting poses.
            if (!Paused && (WorkbenchMode || World.CanSimulate))
            {
                float visualDelta = Mathf.Min(frameDelta, 0.25f) * Speed;
                accumulator += visualDelta;
                int ticks = 0;
                while (accumulator >= Step && ticks++ < 20)
                {
                    if (World.CanSimulate && Config.EnableAI && (benchmarkSeconds <= 0 || benchmarkPhase == "combat")) ai.Step(Step);
                    if(meleeCase=="forming"&&!meleeFormed&&Vector2.Distance(World.Battalions[0].Position,World.Battalions[1].Position)<17){World.SetFormation(1,FormationKind.Square);meleeFormed=true;}
                    MeleeEncounter.Advance(World,meleeCase);
                    if(WorkbenchMode)World.StepReview(Step);else World.StepMovement(Step);
                    if (!casualtyInspection && !recoveryInspection && (benchmarkSeconds <= 0 || benchmarkPhase == "combat" || artilleryInspection || !string.IsNullOrEmpty(meleeCase)))
                    {
                        artillery.Step(Step);
                        if(!WorkbenchMode)combat.Step(Step); // Lab targets stay in place; cannon damage still uses the normal artillery simulation.
                        World.Siege?.Step(Step);
                    }
                    accumulator -= Step;
                }
                // Local effects advance every rendered frame, including between combat ticks.
                // Bound physics steps at high playback speeds while preserving tiny slow-motion steps.
                while (visualDelta > 0f)
                {
                    float visualStep = Mathf.Min(visualDelta, Step);
                    casualties.Step(visualStep);
                    visualDelta -= visualStep;
                }
            }
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string utc, unity, hardware, phase, caveat, infantry;
            public bool burstMovement,modernCrowdInstancing;
            public int crowdBatchSize;
            public bool meadowGrass, cinema, focus, bloom, color, vignette, postProcessing;
            public float playbackSpeed;
            public int frameCap, maxSubmittedGrassVertices, maxSubmittedGrassPatches;
            public int startingFrenchSoldiers, startingBritishSoldiers, playerArtilleryPieces, enemyArtilleryPieces;
            public int soldiers, width, height, frames, survivors, maxRenderedSoldiers, maxSmokePuffs, maxMusketFlashes, smallArmsImpacts, artilleryCrew, artilleryCraters, artilleryFurrows, craterSmokeEmissions, artilleryServiceSamples;
            public float medianFrameMs, p95FrameMs, meanFrameMs, worstFrameMs, medianSimulationMs, p95SimulationMs;
            public long allocatedMemoryBytes;
        }

        private static float Percentile(List<float> values, float fraction)
        {
            if (values.Count == 0) return 0;
            values.Sort();
            return values[Mathf.Clamp(Mathf.CeilToInt(values.Count * fraction) - 1, 0, values.Count - 1)];
        }

        private void WriteBenchmark()
        {
            var report = new BenchmarkReport
            {
                utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                hardware = SystemInfo.processorType + "; " + SystemInfo.graphicsDeviceName,
                phase = benchmarkPhase, soldiers = Config.TotalSoldiers,
                burstMovement=useMovementJobs,modernCrowdInstancing=modernCrowd,crowdBatchSize=crowdBatchCapacity,
                meadowGrass=grassEnabled,frameCap=Application.targetFrameRate,
                cinema=cinemaEnabled,focus=focusEnabled,bloom=bloomEnabled,color=colorEnabled,vignette=vignetteEnabled,postProcessing=postProcessingEnabled,playbackSpeed=Speed,
                maxSubmittedGrassVertices=maxGrassVertices,maxSubmittedGrassPatches=maxGrassPatches,
                startingFrenchSoldiers = World.Battalions.Where(b=>b.Side==ArmySide.French).Sum(b=>b.SoldierCount),
                startingBritishSoldiers = World.Battalions.Where(b=>b.Side==ArmySide.British).Sum(b=>b.SoldierCount),
                playerArtilleryPieces = Config.FrenchArtillery, enemyArtilleryPieces = Config.BritishArtillery,
                infantry = view.UsesDetailedInfantry ? "Baked textured modular infantry with LODs" : "Placeholder silhouettes",
                width = Screen.width, height = Screen.height, frames = frames.Count,
                maxRenderedSoldiers = maxRenderedSoldiers,
                maxSmokePuffs=maxSmokePuffs,maxMusketFlashes=maxMusketFlashes,smallArmsImpacts=projectiles?projectiles.ImpactsEmitted:0,
                artilleryCrew = view.GetComponent<ArtilleryCrewView>().CrewCount,
                artilleryServiceSamples = view.GetComponent<ArtilleryCrewView>().ServiceSamples,
                artilleryCraters = artilleryMarks.CraterCount, artilleryFurrows = artilleryMarks.FurrowCount,
                craterSmokeEmissions = artilleryMarks.SmokeEmissions,
                survivors = World.CountAlive(ArmySide.French) + World.CountAlive(ArmySide.British),
                medianFrameMs = Percentile(frames, .5f), p95FrameMs = Percentile(frames, .95f),
                meanFrameMs = Mean(frames), worstFrameMs = Percentile(frames,1f),
                medianSimulationMs = Percentile(simulationCosts, .5f), p95SimulationMs = Percentile(simulationCosts, .95f),
                allocatedMemoryBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                caveat = "Infantry LOD rendering with sourced rural scenery and atmosphere. Frame time includes the configured frame cap (negative means uncapped). Meadow grass toggle excludes field crops and woodland props. Simulation timing is CPU update cost per rendered frame, not per tick. GPU frame totals are available only in a companion telemetry CSV when enabled; command latency is not measured."
            };
            string output = string.IsNullOrEmpty(benchmarkPath) ? Path.Combine(Application.persistentDataPath, "benchmark.json") : benchmarkPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("Battle benchmark written to " + output);
        }
        private static float Mean(List<float> values)
        {
            if(values.Count==0)return 0;
            double sum=0; foreach(float value in values)sum+=value;
            return (float)(sum/values.Count);
        }
    }
}
