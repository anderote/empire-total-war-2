using System;
using EmpireBattle;
using UnityEngine;

public static class InfantryPerformanceVerification
{
    static void Require(bool value,string message) { if(!value)throw new Exception("Performance verification: "+message); }
    public static void VerifyMovement()
    {
        foreach(FormationKind formation in Enum.GetValues(typeof(FormationKind)))
        {
            var config=new BattleConfig {BattalionsPerSide=2,SoldiersPerBattalion=250,EnableAI=false,EnableLandscape=true};
            var scalar=new BattleWorld(config);var parallel=new BattleWorld(config);
            using(var jobs=new SoldierMovementJobs(parallel))
            {
                parallel.MovementJobs=jobs;
                foreach(var world in new[]{scalar,parallel})
                foreach(var unit in world.Battalions)
                {
                    world.SetFormation(unit.Id,formation);
                    world.IssueMove(unit.Id,unit.Position+new Vector2(20,unit.Side==ArmySide.French?40:-40),unit.Facing);
                }
                for(int tick=0;tick<900;tick++)
                {
                    scalar.StepMovement(.05f);parallel.StepMovement(.05f);
                    for(int i=0;i<scalar.Soldiers.Alive.Length;i++)
                    {
                        if(!scalar.Soldiers.Alive[i])continue;
                        Require(Vector2.Distance(scalar.Soldiers.Positions[i],parallel.Soldiers.Positions[i])<.01f,$"{formation} tick {tick} soldier {i} position parity");
                        // During an about-face either turn direction is valid; floating point
                        // noise can select opposite sides. Compare settled headings instead.
                        var a=scalar.Battalions[scalar.Soldiers.BattalionIds[i]];var b=parallel.Battalions[parallel.Soldiers.BattalionIds[i]];
                        if(!a.Reforming&&!b.Reforming&&scalar.Soldiers.MovementSpeeds[i]<.04f&&parallel.Soldiers.MovementSpeeds[i]<.04f)
                            Require(Mathf.Abs(FormationMath.AngleDifference(scalar.Soldiers.Facings[i],parallel.Soldiers.Facings[i]))<.01f,"Settled facing parity");
                        float gait=Mathf.Abs(scalar.Soldiers.GaitPhases[i]-parallel.Soldiers.GaitPhases[i]);
                        Require(Mathf.Min(gait,1-gait)<.01f,$"{formation} tick {tick} soldier {i} gait parity");
                    }
                }
                parallel.MovementJobs=null;
            }
        }
        Debug.Log("BURST_MOVEMENT_PARITY_PASSED line/column/skirmish/square marching, 45 seconds each");
    }
    public static void VerifyTerrain()
    {
        VerifyTerrainIndices();
        VerifyHeightIndex();
        EarthworkSamplingVerification.Verify();
        var terrain=new BattlefieldTerrain(new BattleConfig{EnableLandscape=false});
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
        try
        {
            using(var ground=new RagdollGround(terrain,scene))
            {
                var point=new Vector2(37,51);
                terrain.Carve(new ArtilleryImpact{Position=terrain.SurfacePoint(point),Direction=new Vector3(.4f,-.9f,.2f).normalized,Speed=120,Sequence=1,ExplosiveShell=true,TerrainStrike=true,AmmunitionType=ArtilleryAmmunition.Explosive});
                ground.Update();
                Require(ground.LastRebuiltTiles>0&&ground.LastRebuiltTiles<=4,"Crater only recooks local terrain tiles");
                UnityEngine.Physics.SyncTransforms();var physics=scene.GetPhysicsScene();
                Require(physics.Raycast(new Vector3(point.x,20,point.y),Vector3.down,out var hit,40),"Crater collision surface exists");
                Require(Mathf.Abs(hit.point.y-terrain.HeightAt(point))<.05f,"Ragdolls collide with excavated height, not old flat surface");
                foreach(var cell in terrain.DamageCells.Keys)
                {
                    if(!terrain.HasDamage(cell))continue;
                    var first=terrain.AcquireDamageMesh(cell);var second=terrain.AcquireDamageMesh(cell);
                    Require(ReferenceEquals(first,second),"Renderer and physics share damage geometry");
                    terrain.ReleaseDamageMesh(cell);
                    Require(first && ReferenceEquals(first,terrain.RefreshDamageMesh(cell)),"Mesh survives while another consumer owns it");
                    terrain.ReleaseDamageMesh(cell);
                    Require(first,"Physics still owns the shared mesh");
                    break;
                }
                int colliders=ground.FineColliderCount;ground.Update();
                Require(ground.LastRebuiltTiles==0&&ground.FineColliderCount==colliders,"Unchanged terrain does no collision rebuilding");
                var regions=new[]{new Bounds(new Vector3(point.x,0,point.y),Vector3.one*30)};
                ground.Update(regions,1);int localCount=ground.FineColliderCount;
                Require(localCount>0,"Active body region installs fine crater collision");
                terrain.Carve(new ArtilleryImpact{Position=new Vector3(900,0,900),Direction=Vector3.down,Speed=120,Sequence=2,ExplosiveShell=true,TerrainStrike=true,AmmunitionType=ArtilleryAmmunition.Explosive});
                ground.Update(regions,1);
                Require(ground.FineColliderCount==localCount&&ground.LastRebuiltTiles==0,"Remote impacts do not recook active body collision geometry");
                regions[0]=new Bounds(new Vector3(-900,0,-900),Vector3.one*30);ground.Update(regions,1);
                Require(ground.FineColliderCount==0,"Leaving a region releases fine collision mesh references");
                UnityEngine.Physics.SyncTransforms();
                Require(physics.Raycast(new Vector3(point.x,20,point.y),Vector3.down,out hit,40)&&Mathf.Abs(hit.point.y-terrain.BaseHeightAt(point))<.001f,"Leaving crater restores coarse collision without a hole");
                regions[0]=new Bounds(new Vector3(point.x,0,point.y),Vector3.one*30);ground.Update(regions,1);
                UnityEngine.Physics.SyncTransforms();
                Require(physics.Raycast(new Vector3(point.x,20,point.y),Vector3.down,out hit,40)&&Mathf.Abs(hit.point.y-terrain.HeightAt(point))<.05f,"Re-entering old crater restores exact collision without a new terrain revision");
                for(int sequence=3;sequence<=513;sequence++)terrain.Carve(new ArtilleryImpact{Position=new Vector3(900,0,900),Direction=Vector3.down,Speed=120,Sequence=sequence,ExplosiveShell=true,TerrainStrike=true,AmmunitionType=ArtilleryAmmunition.Explosive});
                ground.Update(regions,1);UnityEngine.Physics.SyncTransforms();
                Require(ground.FineColliderCount==0&&physics.Raycast(new Vector3(point.x,20,point.y),Vector3.down,out hit,40)&&Mathf.Abs(hit.point.y-terrain.HeightAt(point))<.001f,"Expired crater restores collision and releases meshes in active region");
                var remote=new Bounds(new Vector3(-1000,0,-1000),Vector3.one*32);
                Require(!terrain.DamageChangedIn(remote,0),"Remote grass batches are not invalidated by a local crater");
            }
        }
        finally {UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);}
        Debug.Log("LOCAL_TERRAIN_DAMAGE_VERIFIED");
    }
    static void VerifyHeightIndex()
    {
        bool previous = BattleOptimizationSettings.FastForwardOptimizations;
        var terrain = new BattlefieldTerrain(new BattleConfig { EnableLandscape = true });
        var random = new System.Random(731);
        try
        {
            for (int sequence = 1; sequence <= 700; sequence++)
            {
                // Dense overlaps, rotated furrows, shell rims, cell boundaries and map edges.
                float x = sequence % 7 == 0 ? terrain.HalfSize - .05f : (float)(random.NextDouble() * 24 - 12);
                float z = sequence % 11 == 0 ? -terrain.HalfSize + .05f : (float)(random.NextDouble() * 24 - 12);
                float angle = (float)(random.NextDouble() * Mathf.PI * 2);
                terrain.Carve(new ArtilleryImpact { Position = new Vector3(x, 0, z),
                    Direction = new Vector3(Mathf.Sin(angle), -.15f - (float)random.NextDouble(), Mathf.Cos(angle)).normalized,
                    Speed = 40 + (float)random.NextDouble() * 230, Sequence = sequence,
                    ExplosiveShell = sequence % 3 == 0, TerrainStrike = true,
                    AmmunitionType = sequence % 3 == 0 ? ArtilleryAmmunition.Explosive : ArtilleryAmmunition.Roundshot });
                for (int sample = 0; sample < 80; sample++)
                {
                    var point = new Vector2(x + (float)(random.NextDouble() * 28 - 14), z + (float)(random.NextDouble() * 28 - 14));
                    BattleOptimizationSettings.FastForwardOptimizations = false;
                    float expected = terrain.HeightAt(point);
                    BattleOptimizationSettings.FastForwardOptimizations = true;
                    Require(Mathf.Abs(terrain.HeightAt(point) - expected) < .000001f,
                        $"Fine height index parity after cut {sequence}, sample {sample}");
                }
            }
        }
        finally { BattleOptimizationSettings.FastForwardOptimizations = previous; }
        Debug.Log("FINE_EARTHWORK_HEIGHT_INDEX_PARITY_PASSED 56000 samples, rotated overlaps, map edges and ring expiry");
    }
    static void VerifyTerrainIndices()
    {
        const int n=BattlefieldTerrain.Resolution;
        var original=new int[(n-1)*(n-1)*6];
        for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++)
        {
            int i=z*n+x,k=(z*(n-1)+x)*6;
            original[k]=i;original[k+1]=i+n;original[k+2]=i+1;
            original[k+3]=i+1;original[k+4]=i+n;original[k+5]=i+n+1;
        }
        var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
        try
        {
            mesh.vertices=new Vector3[n*n];mesh.triangles=original;
            var bounds=new Bounds(Vector3.one,Vector3.one*100);mesh.bounds=bounds;
            var patches=new TerrainIndexPatches(mesh);
            var cell=new Vector2Int(128,127);int start=(cell.y*(n-1)+cell.x)*6;
            patches.SetDamaged(cell,true);var changed=mesh.triangles;
            for(int i=0;i<original.Length;i++)
                Require(changed[i]==(i>=start&&i<start+6?cell.y*n+cell.x:original[i]),"Only the crater cell's indices change");
            Require(mesh.bounds==bounds,"Index patch preserves full terrain bounds");
            patches.SetDamaged(cell,false);changed=mesh.triangles;
            for(int i=0;i<original.Length;i++)Require(changed[i]==original[i],"Expired crater restores both original triangles");
        }
        finally {UnityEngine.Object.DestroyImmediate(mesh);}
        Debug.Log("LOCAL_TERRAIN_INDEX_PATCHES_VERIFIED");
    }
    public static void Verify()
    {
        var config=new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=8,
            InitialStrengths=Array.Empty<int>(),FormationNames=Array.Empty<string>(),
            FormationExperience=Array.Empty<float>(),FormationSupply=Array.Empty<float>()};
        var serializedDefaults=new BattleWorld(config);
        Require(serializedDefaults.Soldiers.Alive.Length==16,"Empty serialized campaign arrays use normal battle defaults");
        bool rejected=false;
        try {new BattleWorld(new BattleConfig{InitialStrengths=new[]{5}});}catch(ArgumentException){rejected=true;}
        Require(rejected,"Populated campaign arrays still require matching formation counts");
        VerifyMovement();
        var jobs=new System.Collections.Generic.List<SoldierMovementJobs>();
        BattleWorld.VerificationMovementFactory=world=>{var job=new SoldierMovementJobs(world);jobs.Add(job);return job;};
        try
        {
            InfantryBehaviorVerification.Verify();
            InfantryFireVerification.Verify();
            CasualtyVerification.Verify();
        }
        finally {BattleWorld.VerificationMovementFactory=null;foreach(var job in jobs)job.Dispose();}
        MusketSmokeVerification.Verify();
        VerifyTerrain();
        Debug.Log("INFANTRY_PERFORMANCE_VERIFIED");
    }
}
