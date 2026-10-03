using System;
using EmpireBattle;
using UnityEngine;

public static class FormationTacticsVerification
{
    static void Require(bool condition,string message)
    { if(!condition)throw new Exception("Formation tactics: "+message); }
    static BattleWorld World(int soldiers=250,int enemies=1) => new BattleWorld(new BattleConfig {
        BattalionsPerSide=1,BritishBattalions=enemies,SoldiersPerBattalion=soldiers,
        EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false });
    static void Step(BattleWorld world,float seconds)
    { for(int i=0;i<Mathf.CeilToInt(seconds/.05f);i++)world.StepMovement(.05f); }
    static void Place(BattleWorld world,BattalionState unit,Vector2 center)
    {
        Vector2 offset=center-unit.Position;unit.Position=unit.Destination=center;unit.FireAtWill=false;
        for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)world.Soldiers.Positions[i]+=offset;
    }
    public static void Verify()
    {
        VerifyDragging();VerifyAssignment();VerifyMarching();VerifySquare();VerifyRankControls();VerifySkirmish();VerifyCavalry();
        Debug.Log("FORMATION_TACTICS_VERIFIED: optimal walking assignment, fixed marching slots, hollow outward squares, independent four-sided fire, individual skirmisher stance cycles, exposure and cavalry combat.");
    }
    // Exercise the actual drag preview and order commit without constructing a rendered battle.
    public static void VerifyDragging()
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=2,SoldiersPerBattalion=60,FrenchCavalry=2,BritishCavalry=1,EnableAI=false,EnableLandscape=false});
        var host=new GameObject("Formation drag verification");var view=host.AddComponent<BattleView>();
        var type=typeof(BattleView);int count=world.Battalions.Length;
        Action<string,object> set=(name,value)=>type.GetField(name,flags).SetValue(view,value);
        set("world",world);var selection=new bool[count];set("selected",selection);
        set("previewActive",new bool[count]);set("previewDestinations",new Vector2[count]);
        set("previewFacings",new float[count]);set("previewFiles",new int[count]);set("previewOrder",new int[count]);
        try
        {
            foreach(var plan in new[]{(FormationKind.Line,false),(FormationKind.Column,false),(FormationKind.Skirmish,false),(FormationKind.Square,false),(FormationKind.Line,true),(FormationKind.Column,true),(FormationKind.Skirmish,true),(FormationKind.Diamond,true)})
            {
                var kind=plan.Item1;
                var unit=Array.Find(world.Battalions,u=>u.Side==ArmySide.French&&u.Kind==(plan.Item2?UnitKind.Cavalry:UnitKind.Infantry));
                world.SetFormation(unit.Id,kind);Require(unit.Formation==kind,"Test fixture can form "+kind);
                float spacing=unit.Spacing;int depth=unit.SquareDepth;
                Array.Clear(selection,0,count);selection[unit.Id]=true;
                foreach(var endpoints in new[]{new[]{new Vector2(-30,20),new Vector2(30,20)},new[]{new Vector2(30,20),new Vector2(-30,20)},new[]{new Vector2(1400,1400),new Vector2(1450,1400)}})
                {
                    type.GetMethod("BuildPreview",flags).Invoke(view,new object[]{endpoints[0],endpoints[1],true});
                    int files=((int[])type.GetField("previewFiles",flags).GetValue(view))[unit.Id];
                    float facing=((float[])type.GetField("previewFacings",flags).GetValue(view))[unit.Id];
                    Vector2 destination=((Vector2[])type.GetField("previewDestinations",flags).GetValue(view))[unit.Id];
                    var offsets=new Vector2[unit.AliveCount];for(int slot=0;slot<offsets.Length;slot++)offsets[slot]=FormationMath.SlotOffset(unit,slot,files,facing);
                    type.GetMethod("ApplyPreview",flags).Invoke(view,new object[]{false,true});
                    Require(unit.Formation==kind&&unit.Spacing==spacing&&unit.SquareDepth==depth,"Drag preserves drill, spacing and square depth: "+kind);
                    Require(unit.Files==files&&Vector2.Distance(unit.Destination,destination)<.001f,"Preview matches committed files and destination: "+kind);
                    for(int slot=0;slot<offsets.Length;slot++)Require(Vector2.Distance(offsets[slot],FormationMath.SlotOffset(unit,slot,unit.Files,unit.DesiredFacing))<.001f,"Preview matches committed slot: "+kind);
                }
            }
            // A frontage drag applies to every selected regiment, including mixed drills.
            // Keep this separate from single-unit coverage: the reported failure was a group order.
            foreach(var groupKind in new[]{UnitKind.Infantry,UnitKind.Cavalry})
            {
                var units=Array.FindAll(world.Battalions,u=>u.Side==ArmySide.French&&u.Kind==groupKind);
                Require(units.Length==2,"Two friendly regiments for group drag: "+groupKind);
                foreach(var secondKind in new[]{FormationKind.Skirmish,FormationKind.Line,FormationKind.Column,groupKind==UnitKind.Cavalry?FormationKind.Diamond:FormationKind.Square})
                {
                    world.SetFormation(units[0].Id,FormationKind.Skirmish);
                    world.SetFormation(units[1].Id,secondKind);
                    Array.Clear(selection,0,count);foreach(var unit in units)selection[unit.Id]=true;
                    foreach(bool frontage in new[]{true,false})
                    foreach(bool reverse in new[]{false,true})
                    {
                        Place(world,units[0],new Vector2(-35,-60));Place(world,units[1],new Vector2(35,-60));
                        var start=new Vector2(reverse?90:-90,25);var end=new Vector2(reverse?-90:90,25);
                        type.GetMethod("BuildPreview",flags).Invoke(view,new object[]{start,end,frontage});
                        var files=(int[])type.GetField("previewFiles",flags).GetValue(view);
                        var facings=(float[])type.GetField("previewFacings",flags).GetValue(view);
                        var destinations=(Vector2[])type.GetField("previewDestinations",flags).GetValue(view);
                        var active=(bool[])type.GetField("previewActive",flags).GetValue(view);
                        type.GetMethod("ApplyPreview",flags).Invoke(view,new object[]{false,frontage});
                        foreach(var unit in units)
                        {
                            var expected=unit==units[0]?FormationKind.Skirmish:secondKind;
                            Require(active[unit.Id]&&unit.Formation==expected,"Group order preserves each selected drill: "+expected);
                            Require(unit.Spacing==(expected==FormationKind.Skirmish?(groupKind==UnitKind.Cavalry?3.2f:2.6f):groupKind==UnitKind.Cavalry?1.65f:.62f),"Group order preserves open spacing");
                            Require(unit.Files==files[unit.Id]&&Vector2.Distance(unit.Destination,destinations[unit.Id])<.001f&&Mathf.Abs(FormationMath.AngleDifference(unit.DesiredFacing,facings[unit.Id]))<.001f,"Group preview matches committed order");
                        }
                        Step(world,1);
                        Require(units[0].Formation==FormationKind.Skirmish&&units[1].Formation==secondKind,"Group movement retains drills");
                    }
                }
            }
            var cavalry=Array.Find(world.Battalions,u=>u.Side==ArmySide.French&&u.Kind==UnitKind.Cavalry);
            var enemy=Array.Find(world.Battalions,u=>u.Side!=cavalry.Side&&u.Kind==UnitKind.Infantry);
            world.SetFormation(cavalry.Id,FormationKind.Line);
            Require(world.IssueCharge(cavalry.Id,enemy.Id)&&cavalry.Formation==FormationKind.Diamond,"Mounted charge selects diamond");
            Require(!world.CanForm(enemy,FormationKind.Diamond),"Infantry cannot select mounted diamond");
            for(int soldiers=1;soldiers<=250;soldiers++)
            {
                var positions=new System.Collections.Generic.HashSet<Vector2>();
                for(int slot=0;slot<soldiers;slot++)Require(positions.Add(FormationMath.DiamondSlotOffset(slot,soldiers,1.65f,0)),"Unique diamond slots at "+soldiers);
            }
            // The native Burst path must agree with managed cavalry movement.
            foreach(var kind in new[]{FormationKind.Diamond,FormationKind.Skirmish})
            {
                var parallel=new BattleWorld(world.Config);var mounted=Array.Find(parallel.Battalions,u=>u.Id==cavalry.Id);
                parallel.SetFormation(mounted.Id,kind);
                // Use fresh identical worlds so the preceding drag scenarios cannot bias parity.
                var scalar=new BattleWorld(world.Config);scalar.SetFormation(cavalry.Id,kind);
                using(var jobs=new SoldierMovementJobs(parallel))
                {
                    parallel.MovementJobs=jobs;
                    foreach(var w in new[]{scalar,parallel}){var u=w.Battalions[cavalry.Id];w.IssueMove(u.Id,u.Position+new Vector2(20,40),.6f);}
                    for(int tick=0;tick<300;tick++)
                    {
                        scalar.StepMovement(.05f);parallel.StepMovement(.05f);
                        for(int soldier=mounted.SoldierStart;soldier<mounted.SoldierStart+mounted.SoldierCount;soldier++)
                            Require(Vector2.Distance(scalar.Soldiers.Positions[soldier],parallel.Soldiers.Positions[soldier])<.01f,kind+" Burst movement parity");
                    }
                    parallel.MovementJobs=null;
                }
            }
            Debug.Log("FORMATION_DRAG_VERIFIED line, column, skirmish, square and diamond previews/orders, infantry and cavalry group drags, reversed drags, field edges, charge selection, unique slots and mounted Burst parity");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    static void VerifyAssignment()
    {
        var positions=new[]{new Vector2(-3,1),new Vector2(4,3),new Vector2(0,-2),new Vector2(2,0),new Vector2(-2,-4),new Vector2(5,-1)};
        var targets=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(2,0),new Vector2(0,1),new Vector2(1,1),new Vector2(2,1)};
        int[] soldiers={0,1,2,3,4,5},result=new int[6];
        new FormationAssignment(6).Assign(positions,soldiers,targets,6,result);
        float assigned=0,best=float.PositiveInfinity;
        var used=new bool[6];
        void Search(int slot,float cost)
        {
            if(slot==6){best=Mathf.Min(best,cost);return;}
            for(int soldier=0;soldier<6;soldier++)if(!used[soldier])
            {used[soldier]=true;Search(slot+1,cost+(positions[soldier]-targets[slot]).sqrMagnitude);used[soldier]=false;}
        }
        Search(0,0);
        for(int slot=0;slot<6;slot++)
        {Require(!used[result[slot]],"Assignment has no duplicate soldiers");used[result[slot]]=true;assigned+=(positions[result[slot]]-targets[slot]).sqrMagnitude;}
        Require(Mathf.Abs(assigned-best)<.0001f,"Assignment matches exhaustive minimum squared travel");
        var benchmark=World();var timer=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<10;i++){benchmark.SetFormation(0,FormationKind.Column);benchmark.SetFormation(0,FormationKind.Line);}
        Debug.Log("FORMATION_ASSIGNMENT_BENCHMARK: 20 x 250-man orders "+timer.ElapsedMilliseconds+" ms");
    }
    static void VerifyMarching()
    {
        foreach(bool running in new[]{false,true})
        {
            var world=World(100);var unit=world.Battalions[0];unit.Running=running;
            Vector2 goal=unit.Position+Vector2.up*30;
            var slots=(int[])world.Soldiers.FormationSlots.Clone();world.IssueMarch(0,goal);
            for(int tick=0;tick<200;tick++)
            {
                world.StepMovement(.05f);
                for(int soldier=0;soldier<100;soldier++)
                {
                    Require(slots[soldier]==world.Soldiers.FormationSlots[soldier],"Straight marching preserves assigned places");
                    Vector2 expected=unit.Position+FormationMath.SlotOffset(unit,slots[soldier]);
                    Require(Vector2.Distance(expected,world.Soldiers.Positions[soldier])<.001f,"Walking and running keep rank/file positions");
                }
            }
            world.IssueMarch(0,unit.Position+new Vector2(10,10));Step(world,25);
            Require(!unit.Moving&&!unit.Reforming&&Mathf.Abs(FormationMath.AngleDifference(unit.Facing,Mathf.PI*.25f))<.001f,"Click march ends facing travel");
        }
        var block=World(100);var b=block.Battalions[0];block.SetFrontage(0,5);Step(block,30);
        Require(b.Files==5&&!b.Reforming,"Custom frontage supports deeper blocks");
        block.SetFrontage(0,40);Step(block,30);Require(!b.Reforming,"Deep block efficiently expands into wide block");
    }
    public static void VerifySquare()
    {
        foreach(int count in new[]{1,7,32,96,250,251})
        {
            var world=World(count);var unit=world.Battalions[0];world.SetFormation(0,FormationKind.Square);Step(world,40);
            Require(!unit.Reforming,"Square settles with partial sides: "+count);
            Vector2 half=FormationMath.HalfExtents(unit);
            for(int slot=0;slot<count;slot++)
            {
                int soldier=world.SoldierAtSlot(unit,slot);Vector2 offset=world.Soldiers.Positions[soldier]-unit.Position;
                Require(Mathf.Abs(offset.x)<=half.x+.001f&&Mathf.Abs(offset.y)<=half.y+.001f,"Square footprint contains its soldiers");
                Require(Mathf.Abs(FormationMath.AngleDifference(world.Soldiers.Facings[soldier],FormationMath.SlotFacing(unit,slot)))<.08f,"Square soldiers face their own outward side");
                if(count>=96)Require(Mathf.Max(Mathf.Abs(offset.x),Mathf.Abs(offset.y))>2f,"Square has a hollow center");
                for(int other=slot+1;other<count;other++)
                    Require(Vector2.Distance(world.Soldiers.Positions[soldier],world.Soldiers.Positions[world.SoldierAtSlot(unit,other)])>.3f,"Square corners have no stacked soldiers");
            }
        }
        var surround=World(250,4);var square=surround.Battalions[0];surround.SetFormation(0,FormationKind.Square);Step(surround,40);
        for(int side=0;side<4;side++)Place(surround,surround.Battalions[side+1],square.Position+FormationMath.Forward(side*Mathf.PI*.5f)*100);
        var combat=new CombatSystem(surround);combat.Step(.05f);
        int[] fired=new int[4];float first=float.PositiveInfinity,last=0;
        for(int slot=0;slot<square.AliveCount;slot++)
        {
            int soldier=surround.SoldierAtSlot(square,slot);
            if(surround.Soldiers.FireTimes[soldier]<0)continue;
            fired[FormationMath.SquareSide(slot,square.AliveCount)]++;
            if(FormationMath.IsBayonetWall(square,slot))Require(RifleFireTiming.IsKneeling(surround,square,soldier,surround.Time),"Outer rank fires from its kneeling bayonet posture on the approach");
            Require(Vector3.Dot(surround.Soldiers.FireDirections[soldier],new Vector3(FormationMath.Forward(FormationMath.SlotFacing(square,slot)).x,0,FormationMath.Forward(FormationMath.SlotFacing(square,slot)).y))>.8f,"Square shots go outward");
            first=Mathf.Min(first,surround.Soldiers.FireTimes[soldier]);last=Mathf.Max(last,surround.Soldiers.FireTimes[soldier]);
        }
        foreach(int count in fired)Require(count>0,"Square fires independently on all four sides");
        Require(last-first>.1f,"Independent shots have staggered timing");
        // Diagonal enemies must be covered too, with no gaps between adjacent wall sectors.
        for(int side=0;side<4;side++)Place(surround,surround.Battalions[side+1],square.Position+FormationMath.Forward((side+.5f)*Mathf.PI*.5f)*100);
        int previousShots=square.ShotsFired;Step(surround,20);combat.Step(.05f);
        Require(square.ShotsFired>previousShots,"Squares cover diagonal approaches between sides");
        surround.ApplyCasualties(0,17);Step(surround,10);Require(!square.Reforming,"Damaged square closes ranks");
    }
    static void VerifyRankControls()
    {
        var world=World();var unit=world.Battalions[0];world.SetFormation(0,FormationKind.Square);
        world.AdjustRanks(0,2);Require(FormationMath.RankCount(unit)==5,"Square supports five ranks");
        world.AdjustRanks(0,1);Require(FormationMath.RankCount(unit)==6,"Square supports six ranks");Step(world,35);
        Require(!unit.Reforming,"Six-rank square forms without teleportation");
        for(int slot=0;slot<unit.AliveCount;slot++)
        {
            Vector2 p=world.Soldiers.Positions[world.SoldierAtSlot(unit,slot)]-unit.Position;
            Require(Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.y))>1,"Deep square retains a hollow center");
            for(int other=slot+1;other<unit.AliveCount;other++)
                Require(Vector2.Distance(p,world.Soldiers.Positions[world.SoldierAtSlot(unit,other)]-unit.Position)>.3f,"Deep square corners do not stack soldiers");
        }
        world.AdjustRanks(0,-1);Require(FormationMath.RankCount(unit)==5,"Square can decrease its depth");
        world.AdjustRanks(0,100);Require(FormationMath.RankCount(unit)==FormationMath.MaximumSquareRanks(unit.AliveCount),"Square depth has a safe geometric limit");
        world.ApplyCasualties(0,130);Step(world,40);
        Require(!unit.Reforming&&FormationMath.RankCount(unit)<=FormationMath.MaximumSquareRanks(unit.AliveCount),"Losses reduce excessive square depth safely");
        foreach(var kind in new[]{FormationKind.Line,FormationKind.Column,FormationKind.Skirmish})
        {
            world.SetFormation(0,kind);int ranks=FormationMath.RankCount(unit);
            world.AdjustRanks(0,1);Require(FormationMath.RankCount(unit)>ranks,"Bracket increases ranks in "+kind);
            world.AdjustRanks(0,-1);Require(FormationMath.RankCount(unit)<=ranks,"Bracket decreases ranks in "+kind);
        }
    }

    static void VerifySkirmish()
    {
        var world=World(60);var unit=world.Battalions[0];world.SetFormation(0,FormationKind.Skirmish);Step(world,35);
        Require(!unit.Reforming&&unit.Spacing>2,"Skirmishers spread into open order");
        Place(world,world.Battalions[1],unit.Position+Vector2.up*100);
        var combat=new CombatSystem(world);combat.Step(.05f);Require(unit.ShotsFired>0,"Skirmishers fire independently");
        int[] stances=new int[2];float first=float.PositiveInfinity,last=0;
        int reloader=-1,ready=-1;
        for(int slot=0;slot<unit.AliveCount;slot++)
        {
            int soldier=world.SoldierAtSlot(unit,slot);
            if(world.Soldiers.Captains[soldier]||world.Soldiers.PikeSergeants[soldier])continue;
            var stance=RifleFireTiming.SkirmishFiringStance(soldier);stances[(int)stance]++;
            if(world.Soldiers.FireTimes[soldier]>=0)
            {
                float shot=world.Soldiers.FireTimes[soldier];
                Require(RifleFireTiming.Stance(world,unit,soldier,shot)==stance,"Individual firing stance agrees with preference");
                float height=world.Soldiers.FireOrigins[soldier].y-world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier]).y;
                Require(Mathf.Abs(height-RifleFireTiming.MuzzleHeight(stance))<.02f,"Shot origin agrees with firing posture");
                Require(RifleFireTiming.IsKneeling(world,unit,soldier,shot+1f),"Each shooter kneels to reload");
                Require(RifleFireTiming.IsKneeling(world,unit,soldier,world.Soldiers.ReloadReadyTimes[soldier]+1f),"Loaded skirmishers default to kneeling");
                first=Mathf.Min(first,shot);last=Mathf.Max(last,shot);
                reloader=soldier;
            }
            else {ready=soldier;Require(RifleFireTiming.IsKneeling(world,unit,soldier,world.Time),"Waiting skirmishers default to kneeling");}
        }
        foreach(int count in stances)Require(count>0,"Skirmish order mixes standing and kneeling fire");
        Require(last-first>.7f,"Opening fire is spread across individual aiming windows");
        // One reload does not prevent a different loaded soldier from scheduling his shot.
        if(ready>=0&&reloader>=0)
        {
            world.Soldiers.ReloadReadyTimes[reloader]=world.Time+30;
            Require(RifleFireTiming.ScheduleIndependent(world,unit,ready,unit.Position+Vector2.up*100),"A loaded man fires without waiting for a comrade's reload");
        }
        unit.FireAtWill=false;
        for(int slot=0;slot<unit.AliveCount;slot++)
            Require(!RifleFireTiming.IsKneeling(world,unit,world.SoldierAtSlot(unit,slot),world.Time),"Hold fire releases low postures");
        unit.FireAtWill=true;
        Require(FormationMath.FireExposure(unit)<1,"Open order reduces exposure to enemy fire");
        world.IssueMarch(0,unit.Position+Vector2.up*10);world.StepMovement(.05f);
        Require(!RifleFireTiming.IsKneeling(world,unit,world.SoldierAtSlot(unit,0),world.Time),"Skirmishers stand for movement");
        world.Halt(0);world.SetFormation(0,FormationKind.Line);Step(world,35);Require(!unit.Reforming&&unit.Spacing<1,"Return to close order settles");
    }
    static void VerifyCavalry()
    {
        var world=World(120);var defender=world.Battalions[0];world.SetBayonets(defender.Id,true);world.SetFormation(0,FormationKind.Square);Step(world,35);
        int man=world.SoldierAtSlot(defender,0);Vector2 incoming=world.Soldiers.Positions[man]+FormationMath.Forward(world.Soldiers.Facings[man])*2;
        Require(MeleeSystem.BraceStrength(world,man,incoming)>.7f,"Actual formed bayonet wall braces against cavalry");
        defender.Moving=true;Require(MeleeSystem.BraceStrength(world,man,incoming)==0,"Moving square loses bracing");defender.Moving=false;
        Require(MeleeSystem.BraceStrength(world,man,world.Soldiers.Positions[man]-FormationMath.Forward(world.Soldiers.Facings[man])*2)==0,"Bayonet protection is directional");
    }
    public static void VerifyAll()
    { Verify();InfantryManeuverVerification.Verify();InfantryFireVerification.Verify();InfantryPresentationVerification.Verify(); }
    public static void VerifyBehavior()
    {
        Verify();InfantryFireVerification.VerifyDrills();InfantryPresentationVerification.Verify();
        Debug.Log("FORMATION_BEHAVIOR_VERIFIED: mixed skirmisher firing stances, kneeling reload/idle, independent aim/reload timing, square bracing and existing line drills.");
    }
    public static void BuildReview()
    {
        InfantryAssetBuild.BakeFormationPostures();
        VerifyAll();
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
        var runtime=new GameObject("Formation review runtime").AddComponent<BattleRuntime>();
        runtime.Config=new BattleConfig { BattalionsPerSide=1,SoldiersPerBattalion=250,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0 };
        const string path="Assets/Scenes/FormationReview.unity";
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,path);
        System.IO.Directory.CreateDirectory("Builds");
        var report=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions {
            scenes=new[]{path},locationPathName="Builds/Formation Review.app",target=UnityEditor.BuildTarget.StandaloneOSX,options=UnityEditor.BuildOptions.None });
        Require(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded,"Formation review player builds");
        Debug.Log("FORMATION_REVIEW_BUILD_PASSED");
        InfantryBattleBuild.BuildMac();
    }
    public static void BuildMac() { VerifyAll();InfantryBattleBuild.BuildMac(); }
}
