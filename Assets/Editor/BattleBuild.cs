using System;
using System.IO;
using EmpireBattle;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BattleBuild
{
    [MenuItem("Empire Battle/Create battle scene")]
    public static void CreateScene()
    {
        BattleCinematicBuild.Prepare();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Battle runtime").AddComponent<BattleRuntime>().Config = BattleConfig.CreatePlaytest();
        Directory.CreateDirectory("Assets/Scenes");
        // A referenced instancing material keeps the shader's instanced variant in player builds.
        var soldierShader = Shader.Find("EmpireBattle/InstancedUnlit");
        if (soldierShader != null && !File.Exists("Assets/Scripts/Battle/Presentation/Resources/InstancingVariant.mat"))
        {
            var material = new Material(soldierShader) { enableInstancing = true };
            AssetDatabase.CreateAsset(material, "Assets/Scripts/Battle/Presentation/Resources/InstancingVariant.mat");
        }
        var smokeShader = Shader.Find("EmpireBattle/MusketSmoke");
        if (smokeShader != null && !File.Exists("Assets/Scripts/Battle/Effects/Resources/SmokeVariant.mat"))
            AssetDatabase.CreateAsset(new Material(smokeShader) { enableInstancing = true }, "Assets/Scripts/Battle/Effects/Resources/SmokeVariant.mat");
        if (!File.Exists("Assets/Scripts/Battle/Effects/Resources/CannonLit.mat"))
            AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true }, "Assets/Scripts/Battle/Effects/Resources/CannonLit.mat");
        if (!File.Exists("Assets/Scripts/Battle/Effects/Resources/CannonFlash.mat"))
            AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Unlit")) { enableInstancing = true }, "Assets/Scripts/Battle/Effects/Resources/CannonFlash.mat");
        foreach (var entry in new[] { "Atmosphere/Resources/BattleRain", "Environment/Resources/RuralSurface" })
        {
            var path = "Assets/Scripts/Battle/" + entry + "Variant.mat";
            var shaderPath = "Assets/Scripts/Battle/" + entry + ".shader";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader != null && !File.Exists(path))
                AssetDatabase.CreateAsset(new Material(shader) { enableInstancing = true }, path);
        }
        var pipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/LineBattle.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/LineBattle.unity", true) };
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
        Debug.Log("BATTLE_SCENE_CREATED");
    }

    [MenuItem("Empire Battle/Verify simulation")]
    public static void Verify()
    {
        var playtestConfig = BattleConfig.CreatePlaytest();
        playtestConfig.EnableLandscape = false;
        var asymmetric = new BattleWorld(playtestConfig);
        Require(asymmetric.Config.TotalSoldiers == 22740 && asymmetric.Battalions.Length == 94, "asymmetric battle allocates all soldiers and battalions");
        Require(asymmetric.CountAlive(ArmySide.French) == 7620 && asymmetric.CountAlive(ArmySide.British) == 15120, "playtest has 7500 player and 15000 AI infantry plus 120 cavalry each");
        Require(asymmetric.Battalions[29].Side == ArmySide.French && asymmetric.Battalions[32].Side == ArmySide.British && asymmetric.Soldiers.BattalionIds[22739] == 93, "asymmetric side boundary and final soldier index");
        foreach (var unit in asymmetric.Battalions)
            Require(Mathf.Abs(unit.Position.x) + FormationMath.HalfExtents(unit).x <= playtestConfig.FieldHalfSize, "asymmetric infantry deploys within the field");
        var playtestGuns = new ArtillerySystem(asymmetric);
        int playerGuns = 0, enemyGuns = 0;
        foreach (var gun in playtestGuns.Guns)
        {
            if (gun.Side == ArmySide.French) playerGuns++; else enemyGuns++;
            Require(gun.Ammunition == 250, "every cannon starts with 250 rounds");
            Require(gun.UnitId == gun.Id / 4, "shared scenario groups cannons into four-piece units");
            Require(Mathf.Abs(gun.Position.x) < playtestConfig.FieldHalfSize && Mathf.Abs(gun.Position.y) < playtestConfig.FieldHalfSize, "batteries deploy inside battlefield");
            for (int j = 0; j < gun.Id; j++) Require(Vector2.Distance(gun.Position, playtestGuns.Guns[j].Position) >= 10, "artillery pieces have working crew clearance");
        }
        Require(playerGuns == 60 && enemyGuns == 4, "player receives 60 guns while AI keeps four");
        var asymmetricAI = new TacticalAI(asymmetric); asymmetricAI.Step(.05f);
        int advancing = 0;
        foreach (var unit in asymmetric.Battalions) if (unit.Side == ArmySide.British && unit.Kind==UnitKind.Infantry && unit.Moving) advancing++;
        Require(advancing == 45, "AI commits 75 percent of its own larger army and holds 15 battalions in reserve");
        asymmetric.ApplyCasualties(32, 7);
        Require(asymmetric.CountAlive(ArmySide.French) == 7620 && asymmetric.CountAlive(ArmySide.British) == 15113, "casualties account independently across asymmetric armies");
        var smallUnits = new BattleWorld(new BattleConfig());
        Require(smallUnits.Config.TotalSoldiers == 20000 && smallUnits.Battalions.Length == 80,
            "smaller command units preserve 10000 troops per side");
        Require(smallUnits.Battalions[0].SoldierCount == 250 && smallUnits.Battalions[0].Files == 63,
            "250-soldier line uses half the previous frontage");
        foreach (var unit in smallUnits.Battalions)
            Require(Mathf.Abs(unit.Position.x)+FormationMath.HalfExtents(unit).x <= smallUnits.Config.FieldHalfSize,
                "expanded deployment stays within the battlefield");
        var config = new BattleConfig { BattalionsPerSide = 2, SoldiersPerBattalion = 100, EnableAI = false, EnableLandscape = false };
        var world = new BattleWorld(config);
        Require(world.Soldiers.Positions.Length == 400, "soldier buffer count");
        Require(world.CountAlive(ArmySide.French) == 200, "French count");
        var first = world.Battalions[0];
        var initial = first.Position;
        world.IssueMove(first.Id, initial + new Vector2(0, 30), 0);
        for (int i = 0; i < 800; ++i) world.StepMovement(.05f);
        Require(Vector2.Distance(first.Position, initial + new Vector2(0, 30)) < 3, "movement reaches destination");
        world.SetFormation(first.Id, FormationKind.Column);
        Require(first.Formation == FormationKind.Column, "column order");
        world.ApplyCasualties(first.Id, 17);
        Require(first.AliveCount == 83 && world.CountAlive(ArmySide.French) == 183, "casualty accounting");
        world.ApplyCasualties(first.Id, 1000);
        Require(first.AliveCount == 0, "casualties clamp at survivors");
        var combatWorld = new BattleWorld(new BattleConfig { BattalionsPerSide = 1, SoldiersPerBattalion = 100, InitialSeparation = 100, EnableLandscape = false });
        var combat = new CombatSystem(combatWorld);
        for (int i = 0; i < 800; ++i) { combatWorld.StepMovement(.05f); combat.Step(.05f); }
        Require(combatWorld.CountAlive(ArmySide.French) + combatWorld.CountAlive(ArmySide.British) < 200, "volleys cause casualties");
        foreach (var b in combatWorld.Battalions) Require(b.Ammunition < 30, "volleys consume ammunition");
        var holdWorld = new BattleWorld(new BattleConfig { BattalionsPerSide = 1, SoldiersPerBattalion = 100, InitialSeparation = 100, EnableLandscape = false });
        holdWorld.Battalions[0].FireAtWill = false;
        holdWorld.Battalions[1].Facing = holdWorld.Battalions[1].DesiredFacing = 0;
        var holdCombat = new CombatSystem(holdWorld);
        holdWorld.StepMovement(.05f);
        holdCombat.Step(.05f);
        Require(holdWorld.CountAlive(ArmySide.French) + holdWorld.CountAlive(ArmySide.British) == 200, "hold fire and reversed facing prevent firing");
        holdWorld.Battalions[1].Routing = true;
        holdCombat.Step(.05f);
        Require(holdWorld.Outcome == BattleOutcome.FrenchVictory, "routing determines victory");
        var aiWorld = new BattleWorld(new BattleConfig { BattalionsPerSide = 2, SoldiersPerBattalion = 100, EnableLandscape = false });
        var ai = new TacticalAI(aiWorld);
        Vector2 enemyStart = aiWorld.Battalions[2].Position;
        for (int i = 0; i < 200; i++) { ai.Step(.05f); aiWorld.StepMovement(.05f); }
        Require(Vector2.Distance(enemyStart, aiWorld.Battalions[2].Position) > 5, "AI advances toward enemy");
        var crossing = new BattleWorld(config);
        Vector2 a = crossing.Battalions[0].Position, bPosition = crossing.Battalions[1].Position;
        crossing.IssueMove(0, bPosition, 0);
        crossing.IssueMove(1, a, 0);
        for (int i = 0; i < 4800; i++) crossing.StepMovement(.05f);
        Debug.Log($"FRIENDLY_CROSSING first={crossing.Battalions[0].Position} target={bPosition} second={crossing.Battalions[1].Position} target={a} moving={crossing.Battalions[0].Moving}/{crossing.Battalions[1].Moving}");
        Require(Vector2.Distance(crossing.Battalions[0].Position, bPosition) < .3f && Vector2.Distance(crossing.Battalions[1].Position, a) < .3f,
            "friendly formations detour and complete crossing orders");
        world.SetFrontage(1, 35);
        Require(world.Battalions[1].Files == 35, "dragged frontage preserved");
        var gunWorld = new BattleWorld(new BattleConfig { BattalionsPerSide = 2, SoldiersPerBattalion = 100, EnableAI = false, InitialSeparation = 150, EnableLandscape = false });
        var guns = new ArtillerySystem(gunWorld);
        for (int i = 0; i < 600; i++) { gunWorld.StepMovement(.05f); guns.Step(.05f); }
        Require(guns.ImpactSequence > 0, "ballistic artillery produces impacts");
        Require(gunWorld.CountAlive(ArmySide.French) + gunWorld.CountAlive(ArmySide.British) < 400, "artillery produces casualties");
        var healthWorld = new BattleWorld(new BattleConfig { BattalionsPerSide = 1, SoldiersPerBattalion = 1, EnableLandscape = false });
        Require(healthWorld.Soldiers.Health[1] == 100f, "soldiers start with full health");
        int wounded = healthWorld.ApplyHit(1, 40f, 0);
        Require(wounded == 1 && healthWorld.Soldiers.Health[wounded] == 60f && healthWorld.Soldiers.Alive[wounded], "nonlethal damage produces wounds");
        healthWorld.ApplyHit(1, 70f, 0);
        Require(healthWorld.Battalions[1].AliveCount == 0 && healthWorld.Soldiers.Health[1] == 0f, "lethal hit removes living slot");
        Require(healthWorld.Battalions[0].Kills == 1 && healthWorld.Battalions[0].Hits == 2 && healthWorld.Soldiers.Kills[0] == 1 && healthWorld.Soldiers.Experience[0] > 0f,
            "hit kill and experience attribution counted once");
        Require(healthWorld.ApplyHit(1, 100f, 0) == -1 && healthWorld.Battalions[0].Kills == 1, "dead soldiers cannot be hit again");
        Vector3 muzzle = new Vector3(0f, 1.5f, 0f), aim = new Vector3(0f, 1.3f, 100f);
        Require(WeaponBallistics.TrySolve(muzzle, aim, out Vector3 musketVelocity, out float flight), "musket trajectory has reachable solution");
        Require(Vector3.Distance(WeaponBallistics.PositionAt(muzzle, musketVelocity, flight), aim) < .01f, "gravity trajectory reaches aimed endpoint");
        Require(!WeaponBallistics.TraceGround(healthWorld.Terrain, muzzle, musketVelocity, flight, out _), "clear trajectory remains above flat ground");
        Require(WeaponBallistics.TraceGround(healthWorld.Terrain, muzzle, new Vector3(0f, -5f, 100f), 1f, out Vector3 groundHit) && Mathf.Abs(groundHit.y) < .01f,
            "falling bullet intersects ground");
        var landscape = new BattleWorld(new BattleConfig { BattalionsPerSide = 2, SoldiersPerBattalion = 100, EnableLandscape = true, InitialSeparation = 150f });
        Require(landscape.Terrain.HeightAt(Vector2.zero) > 1f && landscape.Terrain.IsBlocked(landscape.Terrain.FarmCenters[0]), "landscape ridge and farmhouse footprints exist");
        Require(landscape.Terrain.SpeedMultiplier(landscape.Terrain.WoodsCenters[0]) < .8f, "woods reduce shared movement speed");
        Require(landscape.Terrain.BlocksLine(new Vector2(0f, -300f), new Vector2(0f, 300f)), "central ridge blocks low musket line of sight");
        var landscapeGuns = new ArtillerySystem(landscape);
        for (int tick = 0; tick < 400; tick++) { landscape.StepMovement(.05f); landscapeGuns.Step(.05f); }
        Require(landscapeGuns.ImpactSequence > 0, "artillery impacts elevated landscape");
        foreach (var impact in landscapeGuns.Impacts)
            if (impact.Sequence > 0) Require(impact.Position.y >= landscape.Terrain.HeightAt(new Vector2(impact.Position.x, impact.Position.z)) - .05f, "artillery impacts grounded above terrain");
        for (int i = 0; i < world.Soldiers.Positions.Length; ++i)
            Require(!float.IsNaN(world.Soldiers.Positions[i].x), "finite position");
        // Surface and scenery agree on clear access lanes and soil exclusions.
        Require(RuralFieldLayout.FieldAt(new Vector2(-710f,-880f)) >= 0, "cultivated field resolves");
        Require(RuralFieldLayout.Surface(new Vector2(-710f,-880f)).a > .95f, "ploughed field selects soil material");
        Require(RuralFieldLayout.RoadDistance(new Vector2(RuralFieldLayout.LaneX(0),0)) < .01f, "main farm lane is continuous");
        Require(RuralFieldLayout.RoadDistance(RuralFieldLayout.Fields[0].Center) < .01f, "access lane reaches farm yard");
        var sceneryObject = new GameObject("Landscape verification");
        var sceneryCamera = new GameObject("Landscape verification camera").AddComponent<Camera>();
        try
        {
            var rural = sceneryObject.AddComponent<RuralLandscape>();
            rural.Initialize(landscape,sceneryCamera);
            Require(rural.TerrainVertices == BattlefieldTerrain.Resolution * BattlefieldTerrain.Resolution, "rendered and simulated terrain grid agree");
            Require(rural.GrassPatches > 1000 && rural.HedgeSegments > 500, "meadow grass and field boundaries generated");
            Require(rural.NearGrassVertices == 19250 && rural.FarGrassVertices == 1050, "grass LOD geometry stays within its budget");
            var ruralShader=Shader.Find("EmpireBattle/RuralSurface");
            if(ShaderUtil.ShaderHasError(ruralShader))
                foreach(var message in ShaderUtil.GetShaderMessages(ruralShader))Debug.LogError($"{message.file}:{message.line} {message.message}");
            Require(!ShaderUtil.ShaderHasError(ruralShader), "rural surface shader compiles");
            Debug.Log($"LANDSCAPE_VERIFICATION_PASSED grassPatches={rural.GrassPatches} hedgeSegments={rural.HedgeSegments} sceneryInstances={rural.SceneryInstances}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sceneryObject);
            UnityEngine.Object.DestroyImmediate(sceneryCamera.gameObject);
        }
        VegetationVerification.Verify();
        CampaignResearchVerification.Verify();
        Debug.Log("BATTLE_VERIFICATION_PASSED");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Battle verification failed: " + message);
    }

    [MenuItem("Empire Battle/Build Mac prototype")]
    public static void BuildMac()
    {
        ArtilleryBuild.PrepareCrewAssets();
        InfantryPoseBlendBuild.Prepare();
        InfantryMotionVerification.Verify();
        Verify();
        CreateScene();
        Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/LineBattle.unity" },
            locationPathName = "Builds/Empire Battle.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.CleanBuildCache
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Mac build failed: " + report.summary.result);
        Debug.Log("BATTLE_MAC_BUILD_PASSED " + report.summary.totalSize);
    }
}
