using System;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class BallisticsVerification
{
    static void Require(bool value,string message)
    {if(!value)throw new Exception("Ballistics verification: "+message);}

    [MenuItem("Empire Battle/Verify ballistics and blast flight")]
    public static void Verify()
    {
        VerifyOrdersAndAmmunition();
        BattleBuild.Verify();
        ArtilleryBuild.Verify();
        MusketSmokeVerification.Verify();
        float previous=1f;
        for(int range=20;range<=240;range+=20)
        {
            float chance=WeaponBallistics.MusketHitChance(range,120,1,0,0);
            Require(chance>0&&chance<previous,"Musket accuracy decreases across and beyond effective range");previous=chance;
            Require(WeaponBallistics.TrySolve(new Vector3(0,1.5f,0),new Vector3(0,1.3f,range),out var velocity,out float duration),"Slower rounds preserve engagement range");
            Require(Vector3.Distance(WeaponBallistics.PositionAt(new Vector3(0,1.5f,0),velocity,duration),new Vector3(0,1.3f,range))<.01f,"Slower ballistic solution reaches aim");
        }
        var config=new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=100,EnableAI=false,EnableLandscape=false,FrenchArtillery=1,BritishArtillery=0};
        var world=new BattleWorld(config);var guns=new ArtillerySystem(world);
        guns.Guns[0].FireAtWill=false;
        var shot=guns.Projectiles[0];shot.Active=true;shot.Position=new Vector3(400,1,0);shot.Velocity=new Vector3(0,-2,100);
        float lastSpeed=shot.Velocity.magnitude;int bounces=0;
        for(int i=0;i<300&&shot.Active;i++)
        {
            guns.Step(.02f);
            if(shot.BounceCount>bounces)
            {Require(shot.Velocity.magnitude<lastSpeed,"Every skip loses energy");lastSpeed=shot.Velocity.magnitude;bounces=shot.BounceCount;}
        }
        Require(bounces<=1&&!shot.Active,"Roundshot stops after at most one chance-based skip");
        Vector3 blast=world.Terrain.SurfacePoint(world.Soldiers.Positions[0]);
        world.ApplyCasualties(0,1,DeathCause.Shell,Vector3.zero,blast);
        var death=world.Deaths[world.DeadSoldiers[0]];
        Require(Vector3.Distance(death.Position,blast)<.01f,"Blast chooses the nearest living casualty");
        Require(death.Impulse.y>7f,"Close blast launches casualties upward");
        var mid=BattleCasualtyView.CannonFlightPosition(death,world.Terrain,.5f,out float landing);
        var end=BattleCasualtyView.CannonFlightPosition(death,world.Terrain,5f,out _);
        Require(mid.y>2f&&landing>1f,"Distant fallback has visible airborne flight");
        Require(Mathf.Abs(end.y)<.001f&&Vector3.Distance(end,death.Position)>3f,"Thrown casualty lands on terrain away from blast");
        // Fire both shell and roundshot into the same stationary target on repeated reloads.
        foreach(bool shell in new[]{false,true})
        {
            var a=new BattleWorld(config);var b=new BattleWorld(config);
            var ga=new ArtillerySystem(a);var gb=new ArtillerySystem(b);
            Vector3 last=Vector3.zero;bool varied=false;
            for(int i=0;i<8;i++)
            {
                foreach(var system in new[]{ga,gb})
                {
                    var gun=system.Guns[0];gun.ExplosiveShell=shell;gun.Phase=ArtilleryPhase.Ready;gun.ReadyDelay=0;
                    gun.Facing=FormationMath.FacingOf((system==ga?a:b).Battalions[1].Position-gun.Position);
                    foreach(var ball in system.Projectiles)ball.Active=false;
                    system.Step(.01f);
                }
                Require(ga.Projectiles[0].Active&&ga.Projectiles[0].Velocity==gb.Projectiles[0].Velocity,"Seeded artillery dispersion reproduces exactly");
                if(i>0&&Vector3.Distance(last,ga.Projectiles[0].Velocity)>.01f)varied=true;
                last=ga.Projectiles[0].Velocity;
            }
            Require(varied,"Shells and roundshot vary between shots");
        }
        Debug.Log("BALLISTICS_VERIFIED: range penalties, preserved musket range, seeded artillery dispersion, bounded chance-based ricochets, local blast victims and airborne/landed casualty fallback.");
    }
    static void VerifyOrdersAndAmmunition()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=100,
            InitialSeparation=120,EnableAI=false,EnableLandscape=false,FrenchArtillery=8,BritishArtillery=4,UseDetailedInfantry=false});
        var guns=new ArtillerySystem(world);
        Require(!guns.OrderTarget(0,0),"Friendly formation cannot be targeted");
        Require(guns.OrderTarget(0,1),"Enemy formation accepts a tracking order");
        Require(guns.TryAimPoint(guns.Guns[0],out var beforeAim),"Ordered cannon can aim at regiment");
        world.Battalions[1].Position+=new Vector2(8,0);
        for(int i=world.Battalions[1].SoldierStart;i<world.Battalions[1].SoldierStart+world.Battalions[1].SoldierCount;i++)world.Soldiers.Positions[i]+=new Vector2(8,0);
        Require(guns.TryAimPoint(guns.Guns[0],out var aim)&&Vector2.Distance(aim,beforeAim+new Vector2(8,0))<.001f,"Ordered cannon tracks moving target mass");
        Require(guns.OrderGround(0,new Vector2(0,1400))&&!guns.TryAimPoint(guns.Guns[0],out _),"Out-of-range ground order waits instead of switching targets");
        Require(!guns.OrderGround(0,new Vector2(float.NaN,0)),"Invalid ground coordinates rejected");
        var owner=new GameObject("Artillery command verification");
        try
        {
            var view=owner.AddComponent<BattleView>();view.Initialize(world);view.InitializeArtilleryCommands(guns);
            view.SelectGun(0,false);
            Require(view.SelectedGunCount==4&&view.SelectedArtilleryUnitCount==1,"Clicking a cannon selects its four-piece battery");
            view.SelectGun(4,true);
            Require(view.SelectedGunCount==8&&view.SelectedArtilleryUnitCount==2,"Additive selection adds an entire second battery");
            view.SelectGun(5,true);
            Require(view.SelectedGunCount==4&&view.SelectedArtilleryUnitCount==1,"Additive click deselects an entire battery");
            view.SelectGun(8,true);Require(view.SelectedGunCount==4,"Enemy battery cannot enter player selection");
            view.OrderSelectedGuns(1,Vector2.zero);
            for(int i=0;i<guns.Guns.Length;i++)
                Require(i<4?guns.Guns[i].TargetBattalion==1:guns.Guns[i].TargetBattalion<0,"Attack order affects the selected battery only");
            view.SetSelectedAmmunition(ArtilleryAmmunition.Canister);
            for(int i=0;i<guns.Guns.Length;i++)
                Require(i<4?guns.Guns[i].AmmunitionType==ArtilleryAmmunition.Canister:guns.Guns[i].AmmunitionType!=ArtilleryAmmunition.Canister,"Ammo choice applies to the selected battery only");
            view.OrderHalt();
            for(int i=0;i<guns.Guns.Length;i++)Require(guns.Guns[i].FireAtWill==(i>=4),"Halt holds selected battery fire only");
            view.SelectGun(1,true);Require(view.SelectedGunCount==0,"Additive click deselects the battery");
            view.SelectGun(0,false);
            view.SelectUnit(0,false);Require(view.SelectedGunCount==0,"Exclusive infantry selection clears cannon selection");
        }
        finally{UnityEngine.Object.DestroyImmediate(owner);}
        foreach(ArtilleryAmmunition type in Enum.GetValues(typeof(ArtilleryAmmunition)))
        {
            var gun=guns.Guns[0];guns.SetAmmunition(0,type);gun.Position=new Vector2(0,-100);gun.Facing=0;
            gun.ReadyDelay=0;gun.Phase=ArtilleryPhase.Ready;gun.PhaseElapsed=0;
            foreach(var other in guns.Guns)other.FireAtWill=false;
            foreach(var ball in guns.Projectiles)ball.Active=false;
            int ammo=gun.Ammunition;
            guns.OrderGround(0,new Vector2(0,20));guns.Step(.01f);
            int count=0;foreach(var ball in guns.Projectiles)if(ball.Active){count++;Require(ball.AmmunitionType==type,"Projectile snapshots its fired ammunition");}
            Require(count==(type==ArtilleryAmmunition.Canister?ArtillerySystem.CanisterPellets:1)&&gun.Ammunition==ammo-1,"Each discharge consumes one round, including canister pellets");
            gun.FireAtWill=false;
            for(int i=0;i<600;i++)guns.Step(.02f);
            if(type==ArtilleryAmmunition.Carcass||ArtillerySystem.IsCloudShell(type))
            {
                bool active=false;foreach(var hazard in guns.Hazards)if(hazard.Remaining>0&&hazard.Type==type)active=true;
                Require(active,"Incendiary and lime shells leave distinct persistent areas");
            }
        }
        Debug.Log("ARTILLERY_COMMANDS_VERIFIED: additive selection, exclusive selection, battery orders, target tracking, range limits, hold fire and all six ammunition types.");
    }

}
