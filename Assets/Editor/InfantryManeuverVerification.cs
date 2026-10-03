using System;
using System.Reflection;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class InfantryManeuverVerification
{
    static BattleWorld World(int soldiers=250,int units=1,bool landscape=false) => new BattleWorld(new BattleConfig {
        BattalionsPerSide=units,SoldiersPerBattalion=soldiers,EnableAI=false,EnableLandscape=landscape });
    static void Step(BattleWorld world,float seconds)
    {for(int i=0;i<Mathf.CeilToInt(seconds/.05f);i++)world.StepMovement(.05f);}
    static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Infantry maneuver verification failed: "+message);}
    public static void DiagnoseCrossing()
    {
        var world=World(100,2);world.Config.MarchSpeed=2.5f;var a=world.Battalions[0];var b=world.Battalions[1];
        Vector2 startA=a.Position,startB=b.Position;
        world.IssueMove(a.Id,startB,0);world.IssueMove(b.Id,startA,0);
        var route=typeof(BattleWorld).GetField("detourStage",BindingFlags.Instance|BindingFlags.NonPublic);
        for(int i=0;i<2400;i++)
        {
            world.StepMovement(.05f);
            if(i%400==399)Debug.Log("CROSSING "+world.Time+" A="+a.Position+" face="+a.Facing+" B="+b.Position+" face="+b.Facing+" stages="+string.Join(",",(int[])route.GetValue(world)));
        }
        Require(Vector2.Distance(a.Position,startB)<.3f&&Vector2.Distance(b.Position,startA)<.3f,"Crossing completes in 120 seconds");
        Debug.Log("CROSSING_MANEUVER_VERIFIED");
    }
    [MenuItem("Empire Battle/Verify infantry maneuvering")]
    public static void Verify()
    {
        VerifyMovementOrders();
        BattleEffectsVerification.Verify();
    }
    public static void VerifyMovementOrders()
    {
        var world=World();var unit=world.Battalions[0];
        var bins=new int[8];
        for(int i=0;i<unit.SoldierCount;i++)bins[Mathf.Min(7,(int)(world.Soldiers.GaitPhases[i]*8))]++;
        foreach(int count in bins)Require(count>10,"Marching phases span the full gait rather than a shared step");
        var repeat=World();
        for(int i=0;i<world.Soldiers.GaitPhases.Length;i++)Require(world.Soldiers.GaitPhases[i]==repeat.Soldiers.GaitPhases[i],"Gait variation is stable across restart");
        var phases=(float[])world.Soldiers.GaitPhases.Clone();Step(world,1);
        for(int i=0;i<phases.Length;i++)Require(phases[i]==world.Soldiers.GaitPhases[i],"Stationary feet do not advance the marching clock");
        Vector2 destination=unit.Position+new Vector2(0,30);
        world.IssueMove(unit.Id,destination,0);Step(world,30f/world.Config.MarchSpeed+8f);
        Require(Vector2.Distance(unit.Position,destination)<.2f&&!unit.Reforming,"Straight march settles at destination: position="+unit.Position+" target="+destination+" moving="+unit.Moving+" forming="+unit.Reforming+" error="+unit.FormationError+" face="+unit.Facing+" soldier="+world.Soldiers.Facings[0]);
        world.IssueMove(unit.Id,unit.Position,Mathf.PI*.5f);
        Step(world,1);
        Require(Mathf.Abs(FormationMath.AngleDifference(unit.Facing,Mathf.PI*.5f))<.001f,"Quarter turn adopts new slots directly");
        for(int tick=0;tick<900;tick++)
        {
            world.StepMovement(.05f);
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                Require(world.Soldiers.MovementSpeeds[i]<=world.Config.MarchSpeed*1.15f*1.061f+.001f,"Reforming feet have a bounded physical speed");
        }
        Require(!unit.Reforming&&unit.FormationError<.1f,"Quarter turn reforms and faces correctly");
        VerifyIndividualTurns();
        world.SetFormation(unit.Id,FormationKind.Column);
        Require(unit.Reforming,"Formation change enters the forming state");
        world.StepMovement(.05f);
        bool walking=false;for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)walking|=world.Soldiers.MovementSpeeds[i]>.1f;
        Require(walking&&!unit.Moving,"Men walk while reforming even when their center is stationary");
        Step(world,35);Require(!unit.Reforming&&unit.FormationError<.1f,"Column forms without teleporting");
        world.SetFormation(unit.Id,FormationKind.Line);Step(world,35);Require(!unit.Reforming,"Line forms from column");
        world.ApplyCasualties(unit.Id,90);world.StepMovement(.05f);
        var occupied=new bool[world.Soldiers.Alive.Length];
        for(int slot=0;slot<unit.AliveCount;slot++)
        {
            int soldier=world.SoldierAtSlot(unit,slot);
            Require(soldier>=0&&world.Soldiers.Alive[soldier]&&!occupied[soldier]&&world.Soldiers.FormationSlots[soldier]==slot,"Survivors have unique compact formation slots");
            occupied[soldier]=true;
        }
        Step(world,20);Require(!unit.Reforming,"Rear soldiers fill casualty gaps and settle");
        var shooters=new int[unit.SoldierCount];int fired=RifleFireTiming.ScheduleVolley(world,unit,shooters);
        int shot=0;
        for(int slot=0;slot<FormationMath.FiringSoldierCount(unit);slot++)
        {
            int soldier=world.SoldierAtSlot(unit,slot);
            if(world.Soldiers.PikeSergeants[soldier]||world.Soldiers.Captains[soldier])continue;
            Require(shot<fired&&shooters[shot++]==soldier,"Firing follows physical front ranks after gap filling and excludes command weapons");
        }
        Require(shot==fired,"Every scheduled musketeer belongs to a firing slot");
        DiagnoseCrossing();
        VerifyZoomAndDeployment();
        Debug.Log("INFANTRY_MANEUVER_VERIFIED: varied distance-based gait, individual pivots, nearby slot assignments, bounded reformations, casualty gap filling, front-rank fire, reciprocal crossing, spatial deployments and cursor zoom.");
    }
    static void VerifyIndividualTurns()
    {
        foreach (int count in new[] { 252, 250, 1 })
        {
            var world=World(count);var unit=world.Battalions[0];
            // Isolate the in-place musket pivot. Command figures deliberately walk
            // to the new front; InfantryOrderEdgeVerification checks that reformation.
            Array.Clear(world.Soldiers.PikeSergeants,0,world.Soldiers.PikeSergeants.Length);
            Array.Clear(world.Soldiers.Captains,0,world.Soldiers.Captains.Length);
            var before=(Vector2[])world.Soldiers.Positions.Clone();
            var phases=(float[])world.Soldiers.GaitPhases.Clone();
            world.IssueMove(0,unit.Position,Mathf.PI);Step(world,count==250?5:3);
            Require(!unit.Reforming,"About face settles promptly, including incomplete ranks");
            for(int i=0;i<count;i++)
            {
                Require(Vector2.Distance(before[i],world.Soldiers.Positions[i])<FormationMath.RankSpacing(unit.Spacing)*4,"About face keeps each soldier nearby");
                Require(Mathf.Abs(FormationMath.AngleDifference(world.Soldiers.Facings[i],Mathf.PI))<.08f,"Each soldier turns to the rear");
                if(count==252)Require(Vector2.Distance(before[i],world.Soldiers.Positions[i])<.001f&&Mathf.Abs(phases[i]-world.Soldiers.GaitPhases[i])<.001f,"Full ranks pivot without marching in a circle");
            }
            Vector2 start=unit.Position,destination=start+new Vector2(0, -15);
            world.IssueMove(0,destination,Mathf.PI);Step(world,20);
            Require(Vector2.Distance(unit.Position,destination)<.2f&&!unit.Reforming,"March to rear completes after pivot");
        }
        var marching=World(100);var marchUnit=marching.Battalions[0];
        Vector2 marchGoal=marchUnit.Position+new Vector2(0,40);
        marching.IssueMove(0,marchGoal,0);Step(marching,2);
        marching.SetFormation(0,FormationKind.Column);Step(marching,50);
        Require(Vector2.Distance(marchUnit.Position,marchGoal)<.2f&&!marchUnit.Reforming,"Moving formation change catches up and completes its march");
        marchUnit.Running=true;
        Vector2 reverseGoal=marchUnit.Position+new Vector2(0,-20);
        marching.IssueMove(0,reverseGoal,Mathf.PI);Step(marching,20);
        Require(Vector2.Distance(marchUnit.Position,reverseGoal)<.2f&&!marchUnit.Reforming,"Running column can reverse and resume marching");
        marching.IssueMove(0,marchUnit.Position,0);Step(marching,4);
        Vector2 withdrawStart=marchUnit.Position;
        marching.Withdraw(0);Step(marching,3);
        Require(marching.Soldiers.Positions[0].y<withdrawStart.y-1&&Vector2.Dot(FormationMath.Forward(marching.Soldiers.Facings[0]),marching.Soldiers.Velocities[0].normalized)>.9f,"Withdrawal breaks ranks and faces escape");
        var moving=World(100);var b=moving.Battalions[0];
        Vector2 goal=b.Position+new Vector2(0,20);
        moving.IssueMove(0,goal,Mathf.PI);Step(moving,22);
        Require(Vector2.Distance(b.Position,goal)<.2f&&!b.Reforming,"Arrival turns to deployment facing independently of travel direction");
        moving.ApplyCasualties(0,17);
        moving.SetFormation(0,FormationKind.Column);moving.SetFrontage(0,30);
        moving.IssueMove(0,b.Position,0);Step(moving,25);
        var seen=new bool[moving.Soldiers.Alive.Length];
        for(int slot=0;slot<b.AliveCount;slot++)
        {
            int soldier=moving.SoldierAtSlot(b,slot);
            Require(soldier>=0&&moving.Soldiers.Alive[soldier]&&!seen[soldier]&&moving.Soldiers.FormationSlots[soldier]==slot,"Casualties and interrupted orders preserve unique living slots");
            seen[soldier]=true;
        }
        Require(!b.Reforming,"Interrupted formation changes settle");
    }

    static void VerifyZoomAndDeployment()
    {
        var world=World(250,2,true);var root=new GameObject("Maneuver view verification");
        try
        {
            var view=root.AddComponent<BattleView>();view.Initialize(world);
            var camera=view.BattleCamera;camera.pixelRect=new Rect(0,0,1440,900);camera.aspect=1.6f;
            view.SetInspectionView(new Vector2(0,-60),120,55);
            var ground=typeof(BattleView).GetMethod("GroundPoint",BindingFlags.Instance|BindingFlags.NonPublic);
            Vector2 screen=new Vector2(930,510);
            object[] args={screen,Vector2.zero};Require((bool)ground.Invoke(view,args),"Cursor has a terrain zoom anchor");
            Vector2 anchor=(Vector2)args[1];
            view.ZoomAt(screen,8);
            for(int i=0;i<180;i++)view.StepCameraZoom(1f/60);
            Vector3 projected=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(anchor));
            Require(Vector2.Distance(new Vector2(projected.x,projected.y),screen)<.5f,"Terrain beneath cursor stays fixed through zoom in");
            view.ZoomAt(screen,-5);
            for(int i=0;i<180;i++)view.StepCameraZoom(1f/60);
            projected=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(anchor));
            Require(Vector2.Distance(new Vector2(projected.x,projected.y),screen)<.5f,"Cursor anchor stays fixed through zoom out");
            view.ZoomAt(screen,10000);for(int i=0;i<180;i++)view.StepCameraZoom(1f/60);
            float distance=(float)typeof(BattleView).GetField("distance",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            Require(distance>=BattleView.MinimumCameraDistance-.001f&&distance<3.01f,"Extended close zoom reaches three meters");
            Require(camera.transform.position.y>=world.Terrain.HeightAt(new Vector2(camera.transform.position.x,camera.transform.position.z))+.59f,"Close camera stays above terrain");
            view.SelectUnit(0,false);view.SelectUnit(1,true);
            var preview=typeof(BattleView).GetMethod("BuildPreview",BindingFlags.Instance|BindingFlags.NonPublic);
            preview.Invoke(view,new object[] {new Vector2(100,-80),new Vector2(-100,-80),true});
            var destinations=(Vector2[])typeof(BattleView).GetField("previewDestinations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            Require(destinations[0].x<destinations[1].x,"Reversed deployment drag preserves neighboring physical order");
            Vector2 center=(world.Battalions[0].Position+world.Battalions[1].Position)*.5f;
            preview.Invoke(view,new object[]{center+new Vector2(30,30),center+new Vector2(30,30),false});
            var facings=(float[])typeof(BattleView).GetField("previewFacings",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            Require(Mathf.Abs(FormationMath.AngleDifference(facings[0],Mathf.PI*.25f))<.001f&&Mathf.Abs(FormationMath.AngleDifference(facings[1],Mathf.PI*.25f))<.001f,"Single-click group preview faces direction of travel");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    public static void BuildMac(){Verify();InfantryBattleBuild.BuildMac();}
}
