using System;
using EmpireBattle;
using UnityEngine;

public static class LargeBattleOptimizationVerification
{
    public static void VerifyNativeOptimizations()
    {
        bool previous=BattleOptimizationSettings.CrowdFlow;
        try
        {
            BattleOptimizationSettings.CrowdFlow=false;
            VerifyFormationGates();VerifyIsolatedRegiments();VerifyContacts();VerifyTargeting();
            VerifyArtilleryBroadphase();VerifyRenderMath();VerifyRenderWork();SoldierContactVerification.Verify();
            InfantryPerformanceVerification.VerifyMovement();
            BattleOptimizationSettings.CrowdFlow=true;
            CrowdFlowVerification.Verify();InfantryPerformanceVerification.VerifyMovement();
            InfantryContactVerification.Verify();CavalryCombatVerification.VerifySimulation();
        }
        finally {BattleOptimizationSettings.CrowdFlow=previous;}
        Debug.Log("NATIVE_BATTLE_OPTIMIZATIONS_VERIFIED hard contact parity, crowd flow, melee/cavalry, targeting, artillery, render math and scalar/Burst movement");
    }
    public static void VerifyContactMath()
    {
        bool previous=BattleOptimizationSettings.ContactMathOptimizations;
        try
        {
            foreach(bool fleeing in new[]{false,true})for(int seed=0;seed<8;seed++)
            {
                BattleWorld Create()=>new BattleWorld(new BattleConfig {BattalionsPerSide=2,SoldiersPerBattalion=80,EnableAI=false,EnableLandscape=seed%2==0,FrenchArtillery=0,BritishArtillery=0,Seed=1815+seed});
                var reference=Create();var actual=Create();var random=new System.Random(seed+811);
                for(int id=0;id<actual.Soldiers.Alive.Length;id++)
                {
                    var position=new Vector2((float)random.NextDouble()*16-8,(float)random.NextDouble()*16-8);
                    reference.Soldiers.Positions[id]=actual.Soldiers.Positions[id]=position;
                }
                foreach(var world in new[]{reference,actual})foreach(var unit in world.Battalions)
                {unit.InMelee=!fleeing;unit.Cohesion=.2f;unit.Routing=fleeing;unit.Destination=new Vector2(0,unit.Side==ArmySide.French?-200:200);}
                for(int tick=0;tick<30;tick++)
                {
                    BattleOptimizationSettings.ContactMathOptimizations=false;reference.StepMovement(.05f);
                    BattleOptimizationSettings.ContactMathOptimizations=true;actual.StepMovement(.05f);
                    for(int id=0;id<actual.Soldiers.Alive.Length;id++)
                    {Same(reference.Soldiers.Positions[id],actual.Soldiers.Positions[id],"Contact math preserves movement");Require(reference.Soldiers.Facings[id]==actual.Soldiers.Facings[id],"Contact math preserves heading");}
                }
            }
            Debug.Log("CONTACT_MATH_PARITY_VERIFIED 480 dense melee/routing ticks, mixed sides and terrain");
        }
        finally {BattleOptimizationSettings.ContactMathOptimizations=previous;}
    }

    public static void VerifyRenderUploadAndCapture(){VerifyRenderUploadWork();LargeBattlePerformanceVerification.Capture();}

    public static void VerifyRenderUploadWork()
    {
        VerifyRenderWork();
        MusketSmokeVerification.Verify();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var type=typeof(CrowdInfantryRenderer).GetNestedType("Batch",System.Reflection.BindingFlags.NonPublic);
        bool previous=BattleOptimizationSettings.RenderWorkOptimizations;
        try
        {
            foreach(string shaderName in new[]{"CrowdInfantry","CrowdSword"})
            {
                var material=new Material(Resources.Load<Shader>(shaderName));
                try
                {
                    var batch=Activator.CreateInstance(type,new object[]{511});
                    type.GetField("Material",flags).SetValue(batch,material);
                    type.GetMethod("EnsureBuffers",flags).Invoke(batch,null);
                    var blends=(float[])type.GetField("Blends",flags).GetValue(batch);
                    var uniforms=(Vector4[])type.GetField("Uniforms",flags).GetValue(batch);
                    var melee=(float[])type.GetField("MeleeBlends",flags).GetValue(batch);
                    for(int pass=0;pass<2;pass++)foreach(int count in new[]{1,3,65,2,255,256,257,511,17,1})
                    {
                        for(int i=0;i<count;i++){blends[i]=i*.001f+pass;uniforms[i]=new Vector4(i,pass,count,1);if(melee!=null)melee[i]=i*.002f+pass;}
                        type.GetField("Count",flags).SetValue(batch,count);
                        BattleOptimizationSettings.RenderWorkOptimizations=pass==0;
                        var props=(MaterialPropertyBlock)type.GetMethod("UploadProperties",flags).Invoke(batch,null);
                        var got=props.GetFloatArray("_PoseBlend");var tint=props.GetVectorArray("_RegimentUniform");
                        Require(got.Length>=count&&tint.Length>=count,"Uploads contain every instance across bucket boundaries");
                        for(int i=0;i<count;i++)Require(got[i]==blends[i]&&tint[i]==uniforms[i],"Instance properties preserve order and values on bucket reuse");
                        if(melee!=null){var carry=props.GetFloatArray("_MeleeBlend");for(int i=0;i<count;i++)Require(carry[i]==melee[i],"Sword readiness survives right-sized uploads");}
                    }
                }
                finally {UnityEngine.Object.DestroyImmediate(material);}
            }
        }
        finally {BattleOptimizationSettings.RenderWorkOptimizations=previous;}
        Debug.Log("RENDER_UPLOAD_WORK_VERIFIED infantry/sword payloads, bucket growth/reuse, reference uploads and smoke");
    }

    public static void VerifyRenderWork()
    {
        bool previous=BattleOptimizationSettings.RenderWorkOptimizations;
        try
        {
            var world=new BattleWorld(new BattleConfig {BattalionsPerSide=2,SoldiersPerBattalion=500,EnableLandscape=true,EnableAI=false});
            var random=new System.Random(317);
            var positions=new Vector3[world.Soldiers.Alive.Length];
            for(int tick=0;tick<100;tick++)
            {
                foreach(var unit in world.Battalions)world.IssueMove(unit.Id,unit.Position+new Vector2(2,3),unit.Facing);
                world.StepMovement(.05f);
                foreach(var unit in world.Battalions)
                {
                    BattleOptimizationSettings.RenderWorkOptimizations=false;
                    var reference=BattleRenderBounds.Regiment(world,unit,world.Time+.025f);
                    BattleOptimizationSettings.RenderWorkOptimizations=true;
                    var actual=BattleRenderBounds.Regiment(world,unit,world.Time+.025f,positions);
                    Require(Vector3.Distance(reference.center,actual.center)<.01f&&Vector3.Distance(reference.size,actual.size)<.02f,"Direct bounds retain reference silhouette");
                    for(int id=unit.SoldierStart;id<unit.SoldierStart+unit.SoldierCount;id++)
                        Require(actual.Contains(positions[id]+new Vector3(3.99f,5.99f,3.99f))&&actual.Contains(positions[id]-new Vector3(3.99f,5.99f,3.99f)),"Bounds contain padded living bodies");
                }
            }
            var terrain=world.Terrain;
            var cache=new InfantryGroundContact.RestingPoseCache();
            var surface=new BattleRenderBounds.SurfaceCache();
            var point=Vector3.zero;var facing=Quaternion.identity;
            for(int sample=0;sample<1000;sample++)
            {
                if(sample%7==0)point=new Vector3((float)random.NextDouble()*10-5,0,(float)random.NextDouble()*10-5);
                if(sample%11==0)facing=Quaternion.Euler(0,sample*.7f,0);
                if(sample%13==0)terrain.Carve(new ArtilleryImpact {Position=point,Direction=new Vector3(.3f,-1,.2f).normalized,Speed=120,Sequence=sample+1,TerrainStrike=true,ExplosiveShell=true,AmmunitionType=ArtilleryAmmunition.Explosive});
                var surfacePoint=new Vector2(point.x,point.z);
                Require(surface.Sample(terrain,surfacePoint)==terrain.SurfacePoint(surfacePoint),"Surface cache tracks stationary, moving and crater samples");
                float amount=sample%5==0?.5f:1f;
                var expected=InfantryGroundContact.Rotation(terrain,point,facing,amount);
                var actual=cache.Sample(terrain,point,facing,amount);
                Require(Mathf.Abs(expected.x-actual.x)<.000001f&&Mathf.Abs(expected.y-actual.y)<.000001f&&Mathf.Abs(expected.z-actual.z)<.000001f&&Mathf.Abs(expected.w-actual.w)<.000001f,"Resting pose cache tracks movement, rotation, fall transitions and craters");
            }
            Debug.Log("RENDER_WORK_VERIFIED 400 regiment bounds and 1000 cached terrain fits");
        }
        finally {BattleOptimizationSettings.RenderWorkOptimizations=previous;}
    }

    public static void VerifyRenderMath()
    {
        var host=new GameObject("Battle culling verification");
        try
        {
            var camera=host.AddComponent<Camera>();camera.transform.position=new Vector3(0,15,-30);
            camera.transform.LookAt(Vector3.zero);camera.aspect=1.6f;camera.farClipPlane=1500;
            var planes=GeometryUtility.CalculateFrustumPlanes(camera);var random=new System.Random(19);
            for(int sample=0;sample<2000;sample++)
            {
                var position=new Vector3((float)random.NextDouble()*1200-600,(float)random.NextDouble()*60-15,(float)random.NextDouble()*1500-300);
                float facing=(float)random.NextDouble()*100-50;
                var expected=Matrix4x4.TRS(position,Quaternion.Euler(0,facing*Mathf.Rad2Deg,0),Vector3.one);
                var actual=CrowdInfantryRenderer.UprightMatrix(position,facing);
                for(int i=0;i<16;i++)Require(Mathf.Abs(expected[i]-actual[i])<.00002f,"Upright soldier matrix matches Unity TRS");
                var rotation=Quaternion.Euler(35,28,-16);float roll=facing*.3f,width=.1f+sample%13,height=.2f+sample%7;
                expected=Matrix4x4.TRS(position,rotation*Quaternion.Euler(0,0,roll*Mathf.Rad2Deg),new Vector3(width,height,1));
                actual=MusketSmoke.BillboardMatrix(position,rotation*Vector3.right,rotation*Vector3.up,rotation*Vector3.forward,width,height,roll);
                for(int i=0;i<16;i++)Require(Mathf.Abs(expected[i]-actual[i])<.00004f,"Billboard matrix matches camera roll and nonuniform scale");
                var bounds=new Bounds(position,new Vector3(8,12,8));
                Require(BattleRenderBounds.PointVisible(planes,position)==GeometryUtility.TestPlanesAABB(planes,bounds),"Individual culling preserves the padded pose bounds");
                if(BattleRenderBounds.FullyInside(planes,bounds))
                    foreach(var plane in planes)for(int corner=0;corner<8;corner++)
                    {var p=position+new Vector3((corner&1)==0?-4:4,(corner&2)==0?-6:6,(corner&4)==0?-4:4);Require(plane.GetDistanceToPoint(p)>=-.0001f,"Fully visible bounds contain every pose corner");}
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(host);}
        Debug.Log("BATTLE_RENDER_MATH_VERIFIED 2000 transforms and camera-edge culling bounds");
    }

    public static void VerifyArtilleryBroadphase()
    {
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=10,
            FrenchArtillery=32,BritishArtillery=8,EnableAI=false,EnableLandscape=true});
        var artillery=new ArtillerySystem(world);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var method=typeof(ArtillerySystem).GetMethod("AddArtilleryIntersections",flags);
        var hits=(System.Collections.IList)typeof(ArtillerySystem).GetField("soldierIntersections",flags).GetValue(artillery);
        var random=new System.Random(6137);bool previous=BattleOptimizationSettings.ArtilleryBroadphase;int contacts=0;
        try
        {
            for(int sample=0;sample<2000;sample++)
            {
                var gun=artillery.Guns[sample%artillery.Guns.Length];
                gun.Facing=(float)random.NextDouble()*Mathf.PI*2;gun.Phase=ArtilleryPhase.Firing;
                gun.PhaseElapsed=(float)random.NextDouble()*gun.PhaseDuration(gun.Phase);
                foreach(var crew in gun.Crew)crew.Local=new Vector2((float)random.NextDouble()*12-6,(float)random.NextDouble()*12-6);
                var feet=sample%5==0?artillery.GunCenter(gun):artillery.CrewPosition(gun,sample%4);
                var from=feet+new Vector3((float)random.NextDouble()*40-20,(float)random.NextDouble()*2,(float)random.NextDouble()*40-20);
                var to=sample%3==0?feet+Vector3.up*.7f:from+new Vector3((float)random.NextDouble()*120-60,0,(float)random.NextDouble()*120-60);
                var shot=new CannonProjectile {GunId=-1,Age=1,AmmunitionType=sample%2==0?ArtilleryAmmunition.Canister:ArtilleryAmmunition.Roundshot};
                var args=new object[]{shot,from,to};
                hits.Clear();BattleOptimizationSettings.ArtilleryBroadphase=false;method.Invoke(artillery,args);
                var expected=new object[hits.Count];hits.CopyTo(expected,0);contacts+=hits.Count;
                hits.Clear();BattleOptimizationSettings.ArtilleryBroadphase=true;method.Invoke(artillery,args);
                Require(hits.Count==expected.Length,"Artillery broadphase contact count");
                for(int i=0;i<expected.Length;i++)Require(expected[i].Equals(hits[i]),"Artillery broadphase preserves ordered crew/carriage contacts and fractions");
            }
            Require(contacts>100,"Artillery parity includes real contacts");
        }
        finally {BattleOptimizationSettings.ArtilleryBroadphase=previous;}
        Debug.Log("ARTILLERY_BROADPHASE_PARITY_VERIFIED 2000 swept rays, rotated/recoiling guns and displaced crew; contacts="+contacts);
    }

    public static void VerifyAndCapture(){Verify();LargeBattlePerformanceVerification.Capture();}
    static void Require(bool condition,string message)
    { if(!condition)throw new Exception("Large battle optimization: "+message); }
    static void Same(Vector2 a,Vector2 b,string message)
    { Require(a.x==b.x&&a.y==b.y,message); }

    public static void Verify()
    {
        bool previous=BattleOptimizationSettings.CrowdFlow;
        try
        {
            BattleOptimizationSettings.CrowdFlow=false;
        VerifyFormationGates();
        VerifyIsolatedRegiments();
        VerifyContacts();
        VerifyTargeting();
        SoldierContactVerification.Verify();
        InfantryContactVerification.Verify();
        CavalryCombatVerification.VerifySimulation();
        InfantryTargetingVerification.Verify();
        InfantryPerformanceVerification.VerifyMovement();
        Debug.Log("LARGE_BATTLE_OPTIMIZATION_VERIFIED exhaustive contact parity, target-selection parity, body/melee/cavalry/fire regressions and scalar/Burst movement");
        }
        finally {BattleOptimizationSettings.CrowdFlow=previous;}
    }

    public static void VerifyIsolatedRegiments()
    {
        foreach(FormationKind formation in Enum.GetValues(typeof(FormationKind)))
        {
            var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=250,
                EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0,InitialSeparation=600});
            foreach(var unit in world.Battalions)
            {
                world.SetFormation(unit.Id,formation);
                world.ApplyCasualties(unit.Id,50);
                // Disordered survivors, including coincident comrades, must not
                // wake expensive physics when no other regiment is nearby.
                world.Soldiers.Positions[unit.SoldierStart+1]=world.Soldiers.Positions[unit.SoldierStart+2];
            }
            for(int tick=0;tick<80;tick++)
            {
                world.StepMovement(.05f);
                Require(world.BodyContacts.LastGridBuilds==0,"Isolated casualties/reformation skip contacts: "+formation);
            }
            var a=world.Battalions[0];var b=world.Battalions[1];
            MeleeEncounter.Place(world,b,a.Position,a.Facing);
            world.StepMovement(.05f);
            Require(world.BodyContacts.LastGridBuilds>0&&world.BodyContacts.LastContactRegiments==2,"Disordered units wake immediately when regiments overlap: "+formation);
        }
        Debug.Log("ISOLATED_REGIMENT_CONTACTS_VERIFIED casualties/reforming in all formations sleep; overlapping regiments wake");
    }

    public static void VerifyFormationGates()
    {
        foreach(var formation in new[]{FormationKind.Line,FormationKind.Column,FormationKind.Skirmish})
        {
            var world=new BattleWorld(new BattleConfig {BattalionsPerSide=4,SoldiersPerBattalion=500,
                EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false,FrenchArtillery=0,BritishArtillery=0});
            foreach(var unit in world.Battalions)
            {
                unit.Formation=formation;unit.Files=FormationMath.FileCount(unit.AliveCount,formation);
                if(formation==FormationKind.Skirmish)unit.Spacing=2.6f;
                MeleeEncounter.Place(world,unit,new Vector2((unit.Id%4-1.5f)*600,unit.Side==ArmySide.French?-400:400),unit.Facing);
            }
            using var jobs=new SoldierMovementJobs(world);world.MovementJobs=jobs;
            world.StepMovement(.05f);
            Require(world.BodyContacts.LastGridBuilds==0&&world.BodyContacts.LastContactRegiments==0,"Separated "+formation+" skips contact grids");
            foreach(var unit in world.Battalions)world.IssueMove(unit.Id,unit.Position+FormationMath.Forward(unit.Facing)*10,unit.Facing);
            for(int tick=0;tick<5;tick++)
            {
                world.StepMovement(.05f);
                Require(world.BodyContacts.LastGridBuilds==0,"Orderly "+formation+" march skips contacts");
            }
            world.MovementJobs=null;
        }
        var crossing=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=1,
            EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        var a=crossing.Battalions[0];var b=crossing.Battalions[1];
        MeleeEncounter.Place(crossing,a,Vector2.left*2,0);MeleeEncounter.Place(crossing,b,Vector2.right*2,0);
        crossing.BodyContacts.Capture();
        a.Position=crossing.Soldiers.Positions[0]=Vector2.right*2;
        b.Position=crossing.Soldiers.Positions[1]=Vector2.left*2;
        crossing.BodyContacts.Resolve(.2f);
        Require(crossing.BodyContacts.LastGridBuilds>0&&crossing.BodyContacts.LastContactRegiments==2,"Swept paths wake crossing regiments even when endpoints are separate");
        Require(crossing.Soldiers.Positions[0].x<crossing.Soldiers.Positions[1].x,"Sleeping regiments cannot tunnel through one another");
        Debug.Log("FORMATION_CONTACT_GATES_VERIFIED 4000-troop idle/march line/column/skirmish skip all grids; swept crossing wakes contact");
    }

    static void VerifyContacts()
    {
        // Reference mode uses the original exhaustive nine-cell/pair traversal.
        // Compare every step, including initially interpenetrating bodies, cell
        // boundary crossings, multi-step fast moves and pressure at the map edge.
        for(int seed=0;seed<16;seed++)
        {
            BattleWorld Create()=>new BattleWorld(new BattleConfig {BattalionsPerSide=2,SoldiersPerBattalion=24,
                FrenchCavalry=1,BritishCavalry=1,RidersPerSquadron=8,FrenchArtillery=0,BritishArtillery=0,
                EnableLandscape=seed%3==0,EnableAI=false,UseDetailedInfantry=false,Seed=1815+seed});
            var actual=Create();var reference=Create();reference.BodyContacts.ExhaustiveVerification=true;
            var random=new System.Random(7919+seed);
            for(int i=0;i<actual.Soldiers.Alive.Length;i++)
            {
                var p=new Vector2((float)random.NextDouble()*8-4,(float)random.NextDouble()*8-4);
                if(seed%4==0)p.x=actual.Config.FieldHalfSize-4+Mathf.Abs(p.x)*.8f;
                if(seed%4==1)p=new Vector2((i%14)*.62f,(i/14)*.9f);
                if(seed%4==2&&i%9==0)p=Vector2.zero;
                actual.Soldiers.Positions[i]=reference.Soldiers.Positions[i]=p;
            }
            foreach(var world in new[]{actual,reference})foreach(var unit in world.Battalions)
            {unit.Moving=unit.Id%2==0;unit.Bracing=unit.Id%3==0;unit.Reforming=unit.Id%2==1;}
            for(int tick=0;tick<16;tick++)
            {
                actual.BodyContacts.Capture();reference.BodyContacts.Capture();
                for(int i=0;i<actual.Soldiers.Alive.Length;i++)
                {
                    var delta=tick%4==0?Vector2.zero:new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*(tick%5==0?2f:.16f);
                    actual.Soldiers.Positions[i]+=delta;reference.Soldiers.Positions[i]+=delta;
                }
                float dt=tick%5==0?.2f:.05f;
                actual.BodyContacts.Resolve(dt);reference.BodyContacts.Resolve(dt);
                for(int i=0;i<actual.Soldiers.Alive.Length;i++)
                {
                    Same(actual.Soldiers.Positions[i],reference.Soldiers.Positions[i],$"Contact position seed={seed} tick={tick} soldier={i}");
                    Same(actual.Soldiers.Velocities[i],reference.Soldiers.Velocities[i],"Contact velocity");
                    Require(actual.Soldiers.Facings[i]==reference.Soldiers.Facings[i]&&actual.Soldiers.GaitPhases[i]==reference.Soldiers.GaitPhases[i],"Contact animation state");
                }
                foreach(var unit in actual.Battalions)
                {
                    Same(unit.Position,reference.Battalions[unit.Id].Position,"Regiment push");
                    Require(unit.FormationError==reference.Battalions[unit.Id].FormationError,"Formation error");
                    Require(actual.BodyContacts.FriendlyBlocker(unit.Id)==reference.BodyContacts.FriendlyBlocker(unit.Id),"Friendly blocker order");
                }
            }
        }
        Debug.Log("LARGE_BATTLE_CONTACT_PARITY_PASSED 256 dense/mixed/wall/fast/rest steps");
    }

    static void VerifyTargeting()
    {
        int checkedTargets=0,blocked=0;
        foreach(bool landscape in new[]{false,true})
        {
            var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=128,
                FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=landscape,UseDetailedInfantry=false});
            var attacker=world.Battalions[0];var target=world.Battalions[1];var random=new System.Random(16421);
            for(int sample=0;sample<80;sample++)
            {
                attacker.Formation=(FormationKind)(sample%4);
                attacker.Position=new Vector2((float)random.NextDouble()*500-250,(float)random.NextDouble()*500-250);
                target.Position=attacker.Position+new Vector2((float)random.NextDouble()*100-50,20+(float)random.NextDouble()*210);
                for(int i=0;i<world.Soldiers.Alive.Length;i++)
                {
                    var unit=world.Battalions[world.Soldiers.BattalionIds[i]];
                    world.Soldiers.Positions[i]=unit.Position+new Vector2((float)random.NextDouble()*70-35,(float)random.NextDouble()*16-8);
                    world.Soldiers.Alive[i]=i%11!=0;
                    if(i%9==0){world.Soldiers.KnockdownStarts[i]=0;world.Soldiers.KnockdownEnds[i]=2;}
                }
                // Identical scores must keep the original lower-soldier-ID tie break.
                world.Soldiers.Positions[target.SoldierStart+1]=world.Soldiers.Positions[target.SoldierStart+2];
                for(int shooter=1;shooter<attacker.SoldierCount;shooter+=7)
                {
                    int expected=ReferenceTarget(world,attacker,shooter,target);
                    int actual=InfantryTargeting.Select(world,attacker,shooter,target);
                    Require(actual==expected,$"Target parity landscape={landscape} sample={sample} shooter={shooter}: {actual}/{expected}");
                    checkedTargets++;if(expected<0)blocked++;
                }
            }
        }
        Require(blocked>0,"Out-of-range/occluded target rejection exercised");
        Debug.Log($"LARGE_BATTLE_TARGET_PARITY_PASSED selections={checkedTargets} rejected={blocked}");
    }

    // Frozen pre-optimization selection order, including range, arc, ties and terrain traces.
    static int ReferenceTarget(BattleWorld world,BattalionState attacker,int shooter,BattalionState target)
    {
        if(target==null||target.Side==attacker.Side||target.AliveCount<=0)return -1;
        Vector2 start=world.Soldiers.Positions[shooter];
        Vector2 facing=FormationMath.Forward(FormationMath.SlotFacing(attacker,world.Soldiers.FormationSlots[shooter]));
        Vector2 lane=(target.Position-attacker.Position).normalized;if(lane.sqrMagnitude<.01f)lane=facing;
        float range=world.AimedRange(attacker),score=float.PositiveInfinity;int best=-1;
        var origin=world.Terrain.SurfacePoint(start,1.45f);
        for(int i=target.SoldierStart;i<target.SoldierStart+target.SoldierCount;i++)
        {
            if(!world.Soldiers.Alive[i])continue;
            Vector2 position=world.Soldiers.Positions[i];
            if(world.Soldiers.IsKnockedDown(i,world.Time))
            {var airborne=world.SoldierPosition(i,world.Time);position=new Vector2(airborne.x,airborne.z);}
            Vector2 delta=position-start;float distance=delta.magnitude;
            if(distance<.1f||distance>range||Vector2.Dot(facing,delta/distance)<(attacker.Formation==FormationKind.Square?.707f:WeaponBallistics.MinimumFacingDot))continue;
            float lateral=Mathf.Abs(delta.x*lane.y-delta.y*lane.x);
            float candidate=lateral*1000f+distance;if(candidate>=score)continue;
            if(!WeaponBallistics.TrySolve(origin,InfantryTargeting.AimPoint(world,i),out var velocity,out float duration,range)
                ||WeaponBallistics.TraceGround(world.Terrain,origin,velocity,duration,out _))continue;
            score=candidate;best=i;
        }
        return best;
    }
}
