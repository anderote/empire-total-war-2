using System;
using EmpireBattle;
using UnityEngine;

public static class ArtilleryTargetingVerification
{
    static void Require(bool condition,string message){if(!condition)throw new Exception("Artillery targeting: "+message);}
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,
            EnableLandscape=false,FrenchArtillery=4,BritishArtillery=0,AssetReview=true});
        var artillery=new ArtillerySystem(world);var target=world.Battalions[1];target.Facing=.7f;
        target.Position=new Vector2(-50,0);
        Vector2 center=new Vector2(20,30),right=FormationMath.Right(target.Facing),forward=FormationMath.Forward(target.Facing);
        for(int i=0;i<target.SoldierCount;i++)world.Soldiers.Positions[target.SoldierStart+i]=center+right*((i%4)*6-9)+forward*((i/4)*6-3);
        int dead=target.SoldierStart+7;world.Soldiers.Alive[dead]=false;target.AliveCount--;
        world.Soldiers.Positions[dead]=new Vector2(900,900);
        center=Vector2.zero;for(int i=target.SoldierStart;i<dead;i++)center+=world.Soldiers.Positions[i];center/=7;
        foreach(var gun in artillery.Guns){gun.Position=new Vector2(0,-100);gun.Kit.accuracy=10000;artillery.OrderTarget(gun.Id,target.Id);}
        foreach(ArtilleryAmmunition type in Enum.GetValues(typeof(ArtilleryAmmunition)))
        {
            Vector2 sum=Vector2.zero,previous=Vector2.zero;
            for(int i=0;i<artillery.Guns.Length;i++)
            {
                var gun=artillery.Guns[i];gun.AmmunitionType=type;
                Require(artillery.TryAimPoint(gun,out var point),"Regiment is in range");sum+=point;
                if(i>0)Require(Vector2.Distance(point,previous)>1,"Battery guns have distinct aim points");previous=point;
                Require(ArtillerySystem.ShotDispersionRadius(gun,100)>=1.3f&&ArtillerySystem.ShotDispersionRadius(gun,900)>=3.7f,
                    "Maximum accuracy retains range-dependent scatter for "+type);
            }
            Require(Vector2.Distance(sum/4,center)<.001f,"Battery aim is centred on living troops, ignoring dead men and formation anchor");
        }
        var first=artillery.Guns[0];first.AmmunitionType=ArtilleryAmmunition.Explosive;
        artillery.TryAimPoint(first,out var before);first.ShotsFired=1;artillery.TryAimPoint(first,out var reversed);
        Require(Vector2.Distance((before+reversed)*.5f,center)<.001f,"Successive salvos reverse coverage around centre mass");
        first.ShotsFired=0;Vector2 motion=new Vector2(8,15);
        for(int i=target.SoldierStart;i<dead;i++)world.Soldiers.Positions[i]+=motion;
        artillery.TryAimPoint(first,out var after);
        Require(Vector2.Distance(after,before+motion)<.001f,"Aim follows actual moving troops without relying on formation anchor");
        artillery.OrderGround(first.Id,new Vector2(40,40));
        Require(artillery.TryAimPoint(first,out var ground)&&ground==new Vector2(40,40),"Explicit ground orders keep their chosen point");
        // Reconstruct flat-ground impact endpoints: high accuracy must still produce spatial scatter.
        float minX=float.PositiveInfinity,maxX=float.NegativeInfinity;
        for(int shot=0;shot<20;shot++)
        {
            foreach(var projectile in artillery.Projectiles)projectile.Active=false;
            Require(artillery.FireReviewShot(0,target.Id,ArtilleryAmmunition.Explosive),"Review shot follows normal ballistic firing");
            var ball=artillery.Projectiles[0];float g=9.81f;
            float time=(ball.Velocity.y+Mathf.Sqrt(ball.Velocity.y*ball.Velocity.y+2*g*ball.Position.y))/g;
            float x=ball.Position.x+ball.Velocity.x*time;minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);
        }
        Require(maxX-minX>1f,"Accurate shots still land across an area");
        ArtilleryAmmunitionVerification.Verify();
        Debug.Log("ARTILLERY_TARGETING_VERIFIED: living centre mass, rotated battery coverage, moving targets, salvo variation, ground orders, accuracy floor and spatial scatter; ammunition checks passed.");
    }
}
