using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class ArtilleryImpactRevisionVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception("Artillery revision: "+message);}
    static BattleWorld World(out ArtillerySystem guns)
    {
        var w=new BattleWorld(new BattleConfig {AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=8,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false});
        guns=new ArtillerySystem(w);
        for(int i=0;i<w.Soldiers.Alive.Length;i++)w.Soldiers.Positions[i]=new Vector2(400+i,400);
        return w;
    }
    public static void Verify()
    {
        var world=World(out var artillery);
        var method=typeof(ArtillerySystem).GetMethod("ShouldRicochet",Hidden);
        bool Roll(CannonProjectile shot)=>(bool)method.Invoke(artillery,new object[]{shot,Vector3.up});
        int selected=-1,ordinary=-1;
        foreach(ArtilleryAmmunition type in Enum.GetValues(typeof(ArtilleryAmmunition)))
        {
            int skipped=0;
            for(int i=1;i<=10000;i++)
            {
                var shot=new CannonProjectile {Id=i,AmmunitionType=type,Velocity=new Vector3(80,-12,0)};
                if(Roll(shot)){skipped++;selected=i;}else ordinary=i;
            }
            Require(skipped>=650&&skipped<=850,type+" ricochet frequency within 6.5–8.5% over 10000 rounds: "+skipped);
            Debug.Log($"RICOCHET_RATE {type}: {skipped}/10000");
        }
        foreach(ArtilleryAmmunition type in Enum.GetValues(typeof(ArtilleryAmmunition)))
        {
            foreach(bool bounce in new[]{false,true})
            {
                var w=World(out var guns);var shot=guns.Projectiles[0];
                shot.Active=true;shot.FromCamera=true;shot.Id=bounce?selected:ordinary;shot.AmmunitionType=type;
                shot.Position=new Vector3(100,.1f,0);shot.Velocity=new Vector3(80,-12,0);
                guns.Step(.02f);
                Require(shot.Active==bounce&&shot.BounceCount==(bounce?1:0),type+" obeys seeded terrain impact decision");
                if(!bounce)continue;
                Require(!guns.Impacts[0].Detonates&&guns.Hazards[0].Remaining==0,"Skip cannot detonate or leave filling on the ground");
                Require(shot.SegmentAge==0&&shot.Velocity.y>0&&shot.Velocity.magnitude<81,"Bounce restarts trail, rises and loses energy");
                foreach(var fragment in guns.Projectiles)Require(!fragment.Shrapnel||!fragment.Active,"No shrapnel before discharge");
                for(int i=0;i<500&&shot.Active;i++)guns.Step(.02f);
                Require(!shot.Active&&shot.BounceCount==1,"Round stops after its single skip");
                Require((guns.Hazards[0].Remaining>0)==(type==ArtilleryAmmunition.Carcass||ArtillerySystem.IsCloudShell(type)),"Filling discharges at the final impact");
                if(type==ArtilleryAmmunition.Explosive||type==ArtilleryAmmunition.Carcass)
                    Require(guns.Impacts[1].Detonates,"Explosive round detonates on second impact");
            }
        }
        Require(!Roll(new CannonProjectile{Id=selected,Shrapnel=true,Velocity=new Vector3(80,-12,0)}),"Fragments cannot ricochet");
        Require(!Roll(new CannonProjectile{Id=selected,Velocity=Vector3.down*100}),"Vertical impacts do not launch backward");
        Require(!Roll(new CannonProjectile{Id=selected,Velocity=new Vector3(10,-2,0)}),"Spent shots cannot skip");
        var unit=world.Battalions[0];world.Soldiers.Positions[0]=new Vector2(100,0);
        float precision=world.Precision(unit),morale=unit.Morale;
        typeof(ArtillerySystem).GetMethod("Explode",Hidden).Invoke(artillery,new object[]{new CannonProjectile {
            Position=new Vector3(100,0,0),AmmunitionType=ArtilleryAmmunition.Lime }});
        Require(unit.Morale<morale&&world.Precision(unit)<precision,"Lime immediately disrupts and impairs aim");
        Require(Mathf.Abs(world.Precision(unit)/precision-.925f)<.0001f,"One of eight exposed men causes proportional 7.5% accuracy penalty");
        Require(world.Soldiers.Health[0]==94&&world.DeathCount==0&&!world.Soldiers.IsKnockedDown(0,world.Time),"Lime wounds immediately without a blast knockdown");
        world.Soldiers.Positions[0]=new Vector2(400,400);
        typeof(BattleWorld).GetProperty("Time").SetValue(world,3.3f);
        Require(Mathf.Abs(world.Precision(unit)-precision*.925f)<.0001f,"Lime accuracy injury persists after leaving exposure");
        VerifyLimeDamage();
        ArtilleryAmmunitionVerification.Verify();
        Debug.Log("ARTILLERY_IMPACT_REVISION_VERIFIED: all-ammunition probability, single skip, delayed discharge, no duplicate hazards, lime immediate/proportional/expiry");
    }
    static void VerifyLimeDamage()
    {
        foreach(float dt in new[]{.05f,3f,18f,60f})
        {
            var w=World(out var guns);
            // Same cloud catches both sides. Half-radius exposure survives the full duration.
            w.Soldiers.Positions[0]=new Vector2(100,0);
            int enemy=w.Battalions[1].SoldierStart;
            w.Soldiers.Positions[enemy]=new Vector2(100,0);
            w.Soldiers.Positions[1]=new Vector2(108,0);
            w.Soldiers.Positions[2]=new Vector2(117,0);
            typeof(ArtillerySystem).GetMethod("Explode",Hidden).Invoke(guns,new object[]{new CannonProjectile {
                Position=new Vector3(100,0,0),AmmunitionType=ArtilleryAmmunition.Lime }});
            Require(w.Soldiers.Health[0]==94&&w.Soldiers.Health[1]==97,"Initial exposure damages with radial falloff");
            for(int i=0;i<Mathf.CeilToInt(60/dt);i++)
            {
                typeof(BattleWorld).GetProperty("Time").SetValue(w,w.Time+dt);
                guns.Step(dt);
            }
            Require(!w.Soldiers.Alive[0]&&!w.Soldiers.Alive[enemy],"Sustained central exposure kills on both sides");
            Require(Mathf.Abs(w.Soldiers.Health[1]-37)<.001f,"Twenty-one half-strength pulses are independent of timestep: "+dt);
            Require(w.Soldiers.Health[2]==100,"Lime cannot damage outside its 16 m radius");
            Require(!w.Soldiers.IsKnockedDown(1,w.Time),"Lime injury does not repeatedly throw soldiers");
            Require(w.Deaths[w.DeadSoldiers[0]].Cause==DeathCause.Lime&&w.Deaths[w.DeadSoldiers[0]].Impulse==Vector3.zero,"Lime casualty records a distinct cause without explosive force");
            guns.Step(60);Require(Mathf.Abs(w.Soldiers.Health[1]-37)<.001f,"Expired clouds cannot keep wounding");
        }
        {
            var w=World(out var guns);w.Soldiers.Positions[0]=new Vector2(100,0);
            var hazard=guns.Hazards[0];hazard.Position=new Vector3(100,0,0);hazard.Type=ArtilleryAmmunition.Lime;hazard.Remaining=30;hazard.UntilPulse=3;
            guns.Step(3);Require(w.Soldiers.Health[0]==94,"Exposure wounds inside an active cloud");
            w.Soldiers.Positions[0]=new Vector2(130,0);guns.Step(15);
            Require(w.Soldiers.Health[0]==94&&hazard.Remaining>0,"Leaving a live cloud immediately stops damage");
        }
        {
            var w=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=1,BritishArtillery=1});
            var guns=new ArtillerySystem(w);
            for(int i=0;i<w.Soldiers.Alive.Length;i++)w.Soldiers.Positions[i]=new Vector2(400+i,400);
            foreach(var gun in guns.Guns){gun.FireAtWill=false;gun.Position=new Vector2(100+gun.Id*200,0);}
            var target=guns.Guns[0];float carriage=target.Health;
            typeof(ArtillerySystem).GetMethod("Explode",Hidden).Invoke(guns,new object[]{new CannonProjectile {
                GunId=1,Side=guns.Guns[1].Side,Position=new Vector3(100,0,0),AmmunitionType=ArtilleryAmmunition.Lime }});
            Require(target.Crew[0].Health<100&&target.Suppressed,"Lime immediately wounds and suppresses gun crews");
            guns.Step(60);
            Require(target.LivingCrew==0&&!target.Operational&&target.Health==carriage,"Sustained lime can disable a battery through crew loss, leaving its carriage intact");
            Require(guns.Guns[1].Kills==4&&guns.Guns[1].LivingCrew==4,"Crew casualties credit the firing battery without affecting distant crews");
        }
        Debug.Log("LIME_DAMAGE_VERIFIED: immediate 6-point core pulse, falloff, timestep-independent duration, friendly fire, lethal prolonged exposure, exit/expiry, no knockback, crew wounds and kill credit");
    }

}
