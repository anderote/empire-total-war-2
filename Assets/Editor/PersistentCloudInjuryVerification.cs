using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class PersistentCloudInjuryVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool condition,string message){if(!condition)throw new Exception("Cloud injuries: "+message);}
    static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Hidden).Invoke(target,args);
    static BattleWorld World(out ArtillerySystem guns,int artillery=0)
    {
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=artillery,BritishArtillery=0});
        guns=new ArtillerySystem(world);
        foreach(var unit in world.Battalions)unit.FireAtWill=false;
        return world;
    }
    static void Clock(BattleWorld world,float time)=>typeof(BattleWorld).GetProperty("Time").SetValue(world,time);
    static float Speed(BattleWorld world,BattalionState unit)=>(float)Call(world,"MovementSpeed",unit);
    static void Pulse(ArtillerySystem guns,ArtilleryAmmunition type,Vector3 center)=>Call(guns,"PulseHazard",new ArtillerySystem.Hazard {Type=type,Position=center});
    public static void Verify()
    {
        foreach(var type in new[]{ArtilleryAmmunition.PoisonGas,ArtilleryAmmunition.Lime})
        {
            var w=World(out var guns);var unit=w.Battalions[0];var enemy=w.Battalions[1];
            for(int i=0;i<w.Soldiers.Alive.Length;i++)w.Soldiers.Positions[i]=new Vector2(400+i,400);
            w.Soldiers.Positions[0]=w.Soldiers.Positions[enemy.SoldierStart]=new Vector2(100,0);
            float aim=w.Precision(unit),speed=Speed(w,unit);
            Pulse(guns,type,new Vector3(100,0,0));
            Require(Mathf.Abs((type==ArtilleryAmmunition.PoisonGas?unit.BattleGasExposure:unit.BattleLimeExposure)-.125f)<.0001f,"One of eight exposed men produces a proportional injury");
            Require((type==ArtilleryAmmunition.PoisonGas?enemy.BattleGasExposure:enemy.BattleLimeExposure)==.125f,"Both armies retain injuries");
            for(int n=0;n<20;n++)
            {
                w.Soldiers.Health[0]=w.Soldiers.Health[enemy.SoldierStart]=100;
                Pulse(guns,type,new Vector3(100,0,0));
            }
            Require((type==ArtilleryAmmunition.PoisonGas?unit.BattleGasExposure:unit.BattleLimeExposure)==.125f,"Repeated equal exposure does not multiply the debuff");
            Clock(w,10000);unit.Fatigue=0;unit.Routing=false;unit.Withdrawing=false;
            if(type==ArtilleryAmmunition.PoisonGas)
            {
                Require(Mathf.Abs(Speed(w,unit)/speed-.95f)<.0001f,"Gas movement injury remains long after leaving");
                Require(Mathf.Abs(w.Precision(unit)-aim)<.0001f,"Gas obscuration clears without becoming a permanent aim injury");
            }
            else
            {
                Require(Mathf.Abs(w.Precision(unit)/aim-.925f)<.0001f,"Lime accuracy injury remains long after leaving");
                Require(Mathf.Abs(Speed(w,unit)-speed)<.0001f,"Lime does not add the permanent gas movement penalty");
            }
            for(int i=0;i<unit.SoldierCount;i++){w.Soldiers.Positions[i]=new Vector2(100,0);w.Soldiers.Health[i]=100;}
            Pulse(guns,type,new Vector3(100,0,0));Clock(w,20000);unit.Fatigue=0;unit.Routing=false;unit.Withdrawing=false;
            Require(Mathf.Abs((type==ArtilleryAmmunition.PoisonGas?Speed(w,unit)/speed:w.Precision(unit)/aim)-(type==ArtilleryAmmunition.PoisonGas?.6f:.4f))<.0001f,"Full exposure reaches the bounded penalty");
            var next=new BattleWorld(w.Config);
            Require(next.Battalions[0].BattleGasExposure==0&&next.Battalions[0].BattleLimeExposure==0,"New battle clears injuries without altering progression");
            Require(typeof(BattalionState).GetField("BattleGasExposure").IsNotSerialized&&typeof(BattalionState).GetField("BattleLimeExposure").IsNotSerialized,"Battle injuries are not serialized to regiment saves");
        }
        VerifyMovement();VerifyCrews();
        ArtilleryImpactRevisionVerification.Verify();
        PoisonGasVerification.VerifyGameplay();
        {
            var w=World(out var guns);for(int i=0;i<w.Soldiers.Alive.Length;i++)w.Soldiers.Positions[i]=new Vector2(400+i,400);
            w.Soldiers.Positions[0]=new Vector2(100,0);
            Call(guns,"Explode",new CannonProjectile {Position=new Vector3(100,0,0),AmmunitionType=ArtilleryAmmunition.Lime});
            Clock(w,45);guns.Step(45);Require(w.Soldiers.Health[0]==4&&w.Soldiers.Alive[0],"Lime center is survivable for 45 seconds");
            Clock(w,48);guns.Step(3);Require(!w.Soldiers.Alive[0],"Lime center becomes lethal after 48 seconds");
        }
        Debug.Log("PERSISTENT_CLOUD_INJURIES_VERIFIED: gas speed/lime accuracy persist, proportional exposure and caps, no repeated stacking, new-battle reset, both armies, infantry/cavalry movement, scalar/Burst parity, hauling and gun precision");
    }
    static void VerifyMovement()
    {
        var healthy=World(out _);var scalar=World(out var gases);var parallel=World(out var parallelGases);
        foreach(var w in new[]{healthy,scalar,parallel})
        {
            MeleeEncounter.Place(w,w.Battalions[0],new Vector2(100,0),0);
            MeleeEncounter.Place(w,w.Battalions[1],new Vector2(400,400),0);
        }
        Pulse(gases,ArtilleryAmmunition.PoisonGas,new Vector3(100,0,0));
        Pulse(parallelGases,ArtilleryAmmunition.PoisonGas,new Vector3(100,0,0));
        foreach(var w in new[]{healthy,scalar,parallel})
        {w.Battalions[0].Fatigue=0;w.Battalions[0].Cohesion=1;w.IssueMove(0,new Vector2(100,80),0);}
        using(var jobs=new SoldierMovementJobs(parallel))
        {
            parallel.MovementJobs=jobs;
            for(int tick=0;tick<100;tick++)
            {
                healthy.StepMovement(.05f);scalar.StepMovement(.05f);parallel.StepMovement(.05f);
                for(int i=0;i<scalar.Soldiers.Alive.Length;i++)Require(Vector2.Distance(scalar.Soldiers.Positions[i],parallel.Soldiers.Positions[i])<.015f,"Injured movement agrees in scalar and Burst paths");
            }
            parallel.MovementJobs=null;
        }
        Require(scalar.Battalions[0].Position.y>1&&scalar.Battalions[0].Position.y<healthy.Battalions[0].Position.y*.8f,"Actual marching progresses but is slower after gas");
        var unit=scalar.Battalions[0];unit.Kind=UnitKind.Cavalry;unit.MountedSpeed=7;float slow=Speed(scalar,unit);
        float saved=unit.BattleGasExposure;unit.BattleGasExposure=0;float full=Speed(scalar,unit);unit.BattleGasExposure=saved;
        Require(Mathf.Abs(slow/full-unit.GasMovementMultiplier)<.0001f,"Cavalry movement uses the same lasting multiplier");
    }
    static void VerifyCrews()
    {
        foreach(var type in new[]{ArtilleryAmmunition.PoisonGas,ArtilleryAmmunition.Lime})
        {
            var w=World(out var guns,2);var target=guns.Guns[0];var control=guns.Guns[1];
            foreach(var gun in guns.Guns){gun.Position=new Vector2(gun.Id*100,-100);gun.FireAtWill=false;gun.Facing=0;}
            float baseline=target.Precision;
            Pulse(guns,type,new Vector3(0,0,-100));
            Clock(w,10000);target.Fatigue=control.Fatigue=0;target.Cohesion=control.Cohesion=1;target.LimeExposureRemaining=0;
            if(type==ArtilleryAmmunition.Lime)
                Require(target.Precision<baseline*.5f&&target.GasMovementMultiplier==1,"Lime permanently impairs gun aim, not hauling");
            else
            {
                Require(target.GasMovementMultiplier<.7f&&target.Precision==baseline,"Gas permanently slows crews without a permanent aim penalty");
                Require(guns.OrderMove(0,new Vector2(0,0),0)&&guns.OrderMove(1,new Vector2(100,0),0),"Both hauling orders accepted");
                guns.Step(7);
                Require(target.Position.y>-100&&target.Position.y+100<(control.Position.y+100)*.8f,"Surviving exposed crew hauls the gun slower after the cloud clears");
            }
            var fresh=new ArtillerySystem(new BattleWorld(w.Config));
            Require(fresh.Guns[0].BattleGasExposure==0&&fresh.Guns[0].BattleLimeExposure==0,"New battle clears crew injuries");
        }
    }
}
