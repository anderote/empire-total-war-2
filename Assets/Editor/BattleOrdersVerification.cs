using System;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class BattleOrdersVerification
{
    static void Require(bool value,string message)
    {if(!value)throw new Exception("Battle order verification: "+message);}
    static void Place(BattleWorld world,int id,Vector2 point)
    {
        var unit=world.Battalions[id];Vector2 delta=point-unit.Position;
        unit.Position=unit.Destination=point;unit.Moving=false;
        for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)world.Soldiers.Positions[i]+=delta;
    }
    [MenuItem("Empire Battle/Verify camera cannon and ranged orders")]
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=2,SoldiersPerBattalion=200,
            FrenchArtillery=1,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        Place(world,0,Vector2.zero);Place(world,1,new Vector2(-400,-400));
        Place(world,2,new Vector2(0,90));Place(world,3,new Vector2(0,70));
        foreach(var unit in world.Battalions)unit.FireAtWill=false;
        Require(world.IssueRangedAttack(0,2),"Ranged target order accepted");
        var shooter=world.Battalions[0];
        Require(!shooter.PursueTargets&&!shooter.Moving&&shooter.AttackTargetId==2,"Ranged attacks default to holding their current position");
        var combat=new CombatSystem(world);
        for(int i=0;i<120;i++){world.StepMovement(.05f);combat.Step(.05f);}
        Require(shooter.Position==Vector2.zero&&shooter.ShotsFired>0,"Stationary ordered infantry fire without advancing");
        // The nearer enemy physically stands in the bullet path and may intercept shots.
        // Check the intended target rather than requiring bullets to pass through it.
        Require(shooter.AttackTargetId==2&&world.Battalions[2].LastIncomingFireTime>0,
            "Explicit attack fires at the assigned formation over a closer enemy");
        Place(world,2,new Vector2(0,600));world.StepMovement(.05f);
        Require(!shooter.Moving&&shooter.Position==Vector2.zero,"Out-of-range target is not chased by default");
        world.SetPursuit(0,true);Require(shooter.Moving,"Pursuit advances toward an out-of-range target");
        for(int i=0;i<40;i++)world.StepMovement(.05f);
        Require(shooter.Position.y>0,"Enabled pursuit moves the regiment");
        world.SetPursuit(0,false);Vector2 anchor=shooter.Position;
        for(int i=0;i<20;i++)world.StepMovement(.05f);
        Require(shooter.Position==anchor&&!shooter.Moving,"Turning pursuit off stops an existing chase");
        Place(world,2,new Vector2(0,90));world.Battalions[2].Routing=true;world.StepMovement(.05f);
        Require(shooter.AttackTargetId==2&&!shooter.Moving,"Routing target remains assigned while holding position");
        int before=shooter.ShotsFired;
        for(int i=0;i<500;i++){world.StepMovement(.05f);combat.Step(.05f);}
        Require(shooter.ShotsFired>before,"Regiment continues firing at fugitives after reload");
        world.IssueMove(0,new Vector2(50,0),0);
        Require(shooter.AttackTargetId<0&&shooter.AttackMode==InfantryAttackMode.None,"Movement replaces an attack order");
        world.IssueRangedAttack(0,3);world.Halt(0);
        Require(shooter.AttackTargetId<0&&!shooter.Moving,"Halt cancels pursuit and attack orders");

        var artillery=new ArtillerySystem(world);var gun=artillery.Guns[0];gun.FireAtWill=false;
        int ammo=gun.Ammunition;var origin=new Vector3(400,80,0);var target=new Vector3(400,.08f,250);
        Require(artillery.FireCameraRoundshot(origin,target),"Camera shot spawns in the real artillery pool");
        var ball=artillery.Projectiles[0];
        float flight=Vector3.Distance(origin,target)/220f;
        Require(Vector3.Distance(WeaponBallistics.PositionAt(origin,ball.Velocity,flight),target)<.01f,"Camera shot reaches the mouse aim point under gravity");
        Require(gun.Ammunition==ammo&&gun.ShotsFired==0,"Camera shot does not consume a battery's ammunition or reload");
        for(int i=0;i<700&&ball.Active;i++)artillery.Step(.02f);
        Require(artillery.ImpactSequence>0&&ball.BounceCount<=1&&!ball.Active,"Camera shot creates terrain impacts and stops after at most one skip");
        int impacts=artillery.ImpactSequence;
        Require(artillery.FireCameraRoundshot(new Vector3(400,900,-2000),new Vector3(400,.08f,0)),"Zoomed-out camera can fire from beyond the battlefield boundary");
        artillery.Step(.02f);Require(ball.Active,"Camera shot outside the map survives until it enters the battlefield");
        for(int i=0;i<800&&ball.Active;i++)artillery.Step(.02f);
        Require(artillery.ImpactSequence>impacts,"Zoomed-out camera shot reaches terrain and creates impacts");
        Require(!artillery.FireCameraRoundshot(origin,new Vector3(float.NaN,0,0)),"Invalid camera aim rejected");
        foreach(var shot in artillery.Projectiles)shot.Active=true;
        Require(!artillery.FireCameraRoundshot(origin,target),"Full projectile pool rejects a new shot safely");
        foreach(var shot in artillery.Projectiles)shot.Active=false;
        // Artillery can retain and fire at routed assigned targets too.
        gun.TargetBattalion=2;gun.Position=new Vector2(0,-100);gun.Facing=0;gun.ReadyDelay=0;gun.FireAtWill=true;
        artillery.Step(.01f);
        Require(gun.TargetBattalion==2&&gun.ShotsFired==1,"Cannons continue firing at a routed formation");
        var root=new GameObject("Camera firing control verification");
        try
        {
            var view=root.AddComponent<BattleView>();view.Initialize(world);view.InitializeArtilleryCommands(artillery);
            view.SelectUnit(0,false);Require(view.HasCommandSelection,"Selected infantry retain right-click command ownership");
            view.SelectUnit(-1,false);view.SelectGun(0,false);Require(view.HasCommandSelection,"Selected cannon retains right-click command ownership");
            view.SelectUnit(-1,false);Require(!view.HasCommandSelection,"Cleared selection enables camera firing");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        Debug.Log("BATTLE_ORDERS_VERIFIED: hold-position ranged attacks, preferred targets, optional pursuit, routing retarget, movement/halt cancellation, camera trajectories/skips, bounded pool and right-click selection ownership.");
    }
    public static void BuildPlaytests()
    {
        Verify();InfantryBattleBuild.BuildMatchedPlaytests();
    }
}
