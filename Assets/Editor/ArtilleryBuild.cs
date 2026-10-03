using System;
using System.IO;
using EmpireBattle;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ArtilleryBuild
{
    [MenuItem("Empire Battle/Prepare artillery crew assets")]
    public static void PrepareCrewAssets()
    {
        const string output = "Assets/Resources/Artillery";
        Directory.CreateDirectory(output);
        AssetDatabase.Refresh();
        AnimationClip idle = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/NapoleonicInfantry/Animations/musket_idle.FBX"))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview")) idle = clip;
        Require(idle, "Source idle pose exists");
        foreach (string side in new[] { "French", "British" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/NapoleonicInfantry/Prefabs/" + side + "Inspection.prefab");
            Require(prefab, "Source " + side + " rig exists");
            var root = UnityEngine.Object.Instantiate(prefab);
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                idle.SampleAnimation(root, 0);
                foreach (var animator in root.GetComponentsInChildren<Animator>()) UnityEngine.Object.DestroyImmediate(animator);
                foreach (var animation in root.GetComponentsInChildren<Animation>()) UnityEngine.Object.DestroyImmediate(animation);
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.StartsWith("newmusket") || renderer.name.Contains("equipment") || renderer.name == "Authored ramrod")
                        renderer.enabled = false;
                Require(new ProceduralInfantryRig(root).HasRequiredBones, side + " has all 28 authoring bones");
                int enabledSkins = 0;
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>()) if (skin.enabled) enabledSkins++;
                Require(enabledSkins > 0, side + " crew retains weighted clothing");
                PrefabUtility.SaveAsPrefabAsset(root, output + "/" + side + "Crew.prefab");
                Debug.Log("ARTILLERY_CREW_ASSET " + side + " bones=28 enabledSkins=" + enabledSkins);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        var shader = Resources.Load<Shader>("ArtilleryScar"); Require(shader, "Scar shader exists");
        const string variant = "Assets/Scripts/Battle/Effects/Resources/ArtilleryScarVariant.mat";
        if (!AssetDatabase.LoadAssetAtPath<Material>(variant)) AssetDatabase.CreateAsset(new Material(shader), variant);
        AssetDatabase.SaveAssets();
    }

    static BattleWorld World() => new BattleWorld(new BattleConfig { BattalionsPerSide = 1, SoldiersPerBattalion = 500, EnableAI = false, EnableLandscape = false, InitialSeparation = 350 });
    static void Tick(BattleWorld world, ArtillerySystem guns, float seconds)
    {
        for (int i = 0; i < Mathf.RoundToInt(seconds / .05f); i++) { world.StepMovement(.05f); guns.Step(.05f); }
    }
    [MenuItem("Empire Battle/Verify artillery")]
    public static void Verify()
    {
        var world = World(); var guns = new ArtillerySystem(world); var gun = guns.Guns[0];
        gun.Position = new Vector2(0, -100); gun.ReadyDelay = 0;
        for (int i = 1; i < guns.Guns.Length; i++) guns.Guns[i].FireAtWill = false;
        var seen = new bool[9]; int ammo = gun.Ammunition;
        for (int i = 0; i < 410; i++)
        {
            world.StepMovement(.05f); guns.Step(.05f); seen[(int)gun.Phase] = true;
            Require(gun.ReloadRemaining >= 0, "Reload never negative");
            Require(gun.RecoilOffset >= 0 && gun.RecoilOffset <= .65001f, "Recoil stays bounded");
            if (gun.Phase == ArtilleryPhase.Sponging || gun.Phase == ArtilleryPhase.Loading || gun.Phase == ArtilleryPhase.Ramming)
                Require(Mathf.Abs(gun.RecoilOffset - .65f) < .001f, "Gun stays back during muzzle service");
        }
        for (int i = 1; i <= 7; i++) Require(seen[i], "Cycle visits " + (ArtilleryPhase)i);
        Require(gun.ShotsFired == 2 && gun.Ammunition == ammo - 2, "One shot per completed 20-second cycle");
        gun.FireAtWill = false; Tick(world, guns, 30);
        Require(gun.Phase == ArtilleryPhase.Ready && gun.ShotsFired == 2, "Hold fire finishes reload and waits loaded");
        foreach (var unit in world.Battalions) if (unit.Side != gun.Side) unit.Routing = true;
        gun.FireAtWill = true; Tick(world, guns, 2);
        Require(gun.ShotsFired == 2 && gun.Phase == ArtilleryPhase.Ready, "Loaded gun waits when no target exists");
        gun.Ammunition = 0; Tick(world, guns, .05f);
        Require(gun.Phase == ArtilleryPhase.Exhausted, "Empty ammunition stops firing");
        bool terrain = false;
        foreach (var hit in guns.Impacts)
        {
            if (hit.Sequence == 0 || !hit.TerrainStrike) continue;
            terrain = true;
            Require(hit.Speed > 0 && hit.Direction.sqrMagnitude > .9f, "Terrain strike preserves momentum");
        }
        Require(terrain, "Live ballistic shots generate terrain-strike events");
        var owner = new GameObject("Scar verification");
        try
        {
            var scars = owner.AddComponent<ArtilleryGroundMarks>();
            scars.Initialize(world, guns, null, null);
            scars.ConsumeImpacts(world.Time);
            int initialMarks = scars.MarkCount;
            scars.ConsumeImpacts(world.Time);
            Require(scars.MarkCount == initialMarks, "Impact consumption does not duplicate scars");
            // Unit-hit puffs must not manufacture craters.
            for (int i = 0; i < guns.Impacts.Length; i++)
            {
                var hit = guns.Impacts[i]; hit.TerrainStrike = false;
            }
            var surfaceWorld = new BattleWorld(new BattleConfig { BattalionsPerSide = 1, SoldiersPerBattalion = 10, EnableLandscape = true });
            foreach (bool shell in new[] { false, true })
            {
                var hit = new ArtilleryImpact { Position = surfaceWorld.Terrain.SurfacePoint(new Vector2(160, 80)), Direction = new Vector3(1, -.4f, 1).normalized, Speed = 150, ExplosiveShell = shell, Sequence = 12 };
                var mesh = ArtilleryGroundMarks.BuildVerificationMesh(surfaceWorld, hit);
                try
                {
                    foreach (var vertex in mesh.vertices)
                    {
                        Vector3 point = vertex + hit.Position;
                        Require(point.y >= surfaceWorld.Terrain.HeightAt(new Vector2(point.x, point.z)), "Scars conform to elevated landscape");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            var otherWorld = World(); var otherGuns = new ArtillerySystem(otherWorld);
            for (int i = 0; i < otherGuns.Guns.Length; i++) otherGuns.Guns[i].FireAtWill = false;
            otherGuns.Guns[0].Position = new Vector2(0, -100); otherGuns.Guns[0].ReadyDelay = 0;
            otherGuns.Guns[0].Ammunition = 1; otherGuns.Guns[0].FireAtWill = true;
            Tick(otherWorld, otherGuns, 30);
            Require(otherGuns.Guns[0].ShotsFired == 1 && otherGuns.Guns[0].Phase == ArtilleryPhase.Exhausted, "Final round cannot produce a phantom extra shot");
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
        foreach (bool shell in new[] { false, true })
        {
            var impact = new ArtilleryImpact { Position = world.Terrain.SurfacePoint(new Vector2(0, 0)), Direction = new Vector3(.6f, -.2f, .8f).normalized, Speed = 150, ExplosiveShell = shell, Sequence = 11, TerrainStrike = true };
            var mesh = ArtilleryGroundMarks.BuildVerificationMesh(world, impact);
            try
            {
                Require(mesh.vertexCount >= 289 && mesh.vertexCount <= 4225, "Scars use bounded terrain-conforming geometry");
                foreach (var point in mesh.vertices)
                    Require(point.y + impact.Position.y >= world.Terrain.HeightAt(new Vector2(point.x + impact.Position.x, point.z + impact.Position.z)), "Scar stays above terrain");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        VerifyEarthworks();
        ArtilleryAmmunitionVerification.Verify();
        Debug.Log("ARTILLERY_VERIFICATION_PASSED cycle hold-fire target-loss ammunition terrain-scars");
    }
    static void VerifyEarthworks()
    {
        var world = World();
        var shell = new ArtilleryImpact { Position = new Vector3(37,0,51), Direction = new Vector3(.4f,-.9f,.2f).normalized,
            Speed = 160, Sequence = 73, ExplosiveShell = true, TerrainStrike = true, AmmunitionType = ArtilleryAmmunition.Explosive };
        var shape = ArtilleryEarthwork.FromImpact(shell);
        float original = world.Terrain.HeightAt(shape.Center);
        world.Terrain.Carve(shell);
        Require(world.Terrain.HeightAt(shape.Center) < original - .5f, "Shell excavates actual ground");
        Require(world.Terrain.HeightAt(shape.Center + shape.Right * shape.Radius * .88f) > original, "Displaced earth creates spoil rim");
        Require(Mathf.Abs(world.Terrain.HeightAt(shape.Center + Vector2.one * 20f)) < .0001f, "Excavation remains local");
        var replica = World(); replica.Terrain.Carve(shell);
        Require(Mathf.Abs(replica.Terrain.HeightAt(shape.Center) - world.Terrain.HeightAt(shape.Center)) < .00001f, "Impact geometry deterministic");
        shell.Sequence++;
        Require(Mathf.Abs(ArtilleryEarthwork.FromImpact(shell).Radius - shape.Radius) > .001f, "Shots have distinct profiles");
        var furrow = new ArtilleryImpact { Position = new Vector3(-35,0,17), Direction = new Vector3(1,-.08f,0).normalized,
            Speed = 180, Sequence = 77, TerrainStrike = true };
        var cut = ArtilleryEarthwork.FromImpact(furrow); world.Terrain.Carve(furrow);
        Require(cut.HalfLength > cut.Radius * 4f, "Grazing strike carves directional furrow");
        Require(world.Terrain.HeightAt(cut.Center) < -.1f, "Roundshot cuts below surface");
        Require(Mathf.Abs(world.Terrain.HeightAt(cut.Center + cut.Right * 2f)) < .0001f, "Furrow preserves surrounding ground");
        foreach (var cell in world.Terrain.DamageCells.Keys)
        {
            var mesh = world.Terrain.BuildDamageMesh(cell);
            try
            {
                Require(mesh.vertexCount <= 4225, "Fine terrain mesh bounded per cell");
                foreach (var vertex in mesh.vertices)
                    Require(Mathf.Abs(vertex.y - world.Terrain.HeightAt(new Vector2(vertex.x,vertex.z))) < .0001f, "Rendered excavation matches collision heights");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        var buriedWorld = World(); var guns = new ArtillerySystem(buriedWorld);
        foreach (var gun in guns.Guns) gun.FireAtWill = false;
        var ball = guns.Projectiles[0]; ball.Active = true; ball.Position = new Vector3(20,1,20); ball.Velocity = new Vector3(3,-100,0);
        Tick(buriedWorld,guns,.05f);
        Require(!ball.Active && ball.BounceCount == 0, "Steep roundshot buries rather than bouncing");
        // Exercise replacement so old excavations and their mesh cells can be reclaimed.
        for (int i = 0; i < 513; i++)
        {
            var hit = new ArtilleryImpact { Sequence = 100+i, Position = new Vector3(200,0,200), Speed = 120,
                Direction = Vector3.down, ExplosiveShell = true, TerrainStrike = true };
            world.Terrain.Carve(hit);
        }
        Require(Mathf.Abs(world.Terrain.HeightAt(shape.Center)) < .0001f, "Bounded excavation pool retires oldest cuts");
        Require(Resources.Load<Shader>("ArtilleryFlame"), "Procedural flame shader present");
        var tree = ProceduralVegetation.Tree(0, 851);
        var stump = ArtilleryTreeFracture.Clip(tree,false); var crown = ArtilleryTreeFracture.Clip(tree,true);
        try
        {
            float split = tree.bounds.min.y + tree.bounds.size.y * .08f;
            Require(stump.vertexCount > 0 && crown.vertexCount > 0, "Tree splits into stump and crown");
            Require(stump.bounds.max.y <= split + .0001f && crown.bounds.min.y >= split - .0001f, "Tree fractures at the trunk plane");
            Require(crown.uv.Length == crown.vertexCount && stump.colors.Length == stump.vertexCount, "Fractures retain bark UVs and leaf colors");
        }
        finally { UnityEngine.Object.DestroyImmediate(tree); UnityEngine.Object.DestroyImmediate(stump); UnityEngine.Object.DestroyImmediate(crown); }
        var landscapeWorld = new BattleWorld(new BattleConfig { BattalionsPerSide = 1, SoldiersPerBattalion = 10, EnableLandscape = true });
        var sceneryOwner = new GameObject("Artillery scenery verification");
        try
        {
            var landscape = sceneryOwner.AddComponent<RuralLandscape>(); landscape.Initialize(landscapeWorld,null);
            var treeGuns = new ArtillerySystem(landscapeWorld); landscape.Artillery = treeGuns;
            Vector3 root = landscape.ClosestTreeRoot(Vector2.zero);
            var treeBall = treeGuns.Projectiles[0]; treeBall.Active = true; treeBall.Id = 991; treeBall.Position = root + Vector3.up * 1.5f;
            treeBall.Velocity = Vector3.right * 90f; treeBall.SegmentAge = .05f;
            landscape.ConsumeArtilleryDamage();
            Require(landscape.BrokenTreeCount > 0, "Swept cannonballs snap actual scenery trees");
            int broken = landscape.BrokenTreeCount; landscape.ConsumeArtilleryDamage();
            Require(landscape.BrokenTreeCount == broken, "Broken trees are not fractured twice");
        }
        finally { UnityEngine.Object.DestroyImmediate(sceneryOwner); }
        Debug.Log("ARTILLERY_EARTHWORKS_PASSED excavation rim deterministic variation furrows mesh burial capacity");
    }
    [MenuItem("Empire Battle/Build artillery playtest Mac")]
    public static void BuildMac()
    {
        PrepareCrewAssets(); Verify(); BattleBuild.Verify();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var runtime = new GameObject("Artillery playtest runtime").AddComponent<BattleRuntime>();
        runtime.ArtilleryInspectionMode = true;
        const string scenePath = "Assets/Scenes/ArtilleryBattle.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath }, locationPathName = "Builds/Artillery Battle.app", target = BuildTarget.StandaloneOSX, options = BuildOptions.None });
        Require(report.summary.result == BuildResult.Succeeded, "Standalone artillery build succeeds");
        Debug.Log("ARTILLERY_BUILD_PASSED " + report.summary.totalSize);
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception("Artillery verification: " + message); }
}
