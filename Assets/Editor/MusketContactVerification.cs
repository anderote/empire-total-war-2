using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class MusketContactVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception("Musket contact: "+message);}
    static BattleWorld Fixture(out CombatSystem combat,out int front,out int rear)
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=16,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(50+i,50);
        front=world.Battalions[1].SoldierStart;rear=front+1;
        world.Soldiers.Positions[front]=new Vector2(0,20);world.Soldiers.Positions[rear]=new Vector2(0,30);
        world.Soldiers.Health[front]=world.Soldiers.Health[rear]=100;
        combat=new CombatSystem(world);return world;
    }
    static void Launch(CombatSystem combat,float damage=200,float born=.5f,float x=0)
        =>typeof(CombatSystem).GetMethod("LaunchBullet",Hidden).Invoke(combat,new object[]{
            new Vector3(x,1,0),new Vector3(x,1,40),damage,0,0,born,true,1f,false});
    static void Advance(BattleWorld world,CombatSystem combat,float time)
    {
        typeof(BattleWorld).GetProperty("Time").SetValue(world,time);
        typeof(CombatSystem).GetMethod("StepBullets",Hidden).Invoke(combat,null);
    }
    public static void Verify()
    {
        var w=Fixture(out var c,out int front,out int rear);Launch(c);
        Advance(w,c,.49f);Require(w.DeathCount==0,"No damage before scheduled muzzle flash");
        Advance(w,c,.55f);Require(w.DeathCount==0,"No damage before bullet arrival");
        Advance(w,c,.7f);
        Require(!w.Soldiers.Alive[front]&&w.Soldiers.Alive[rear]&&w.DeathCount==1,"First contacted soldier dies; rear soldier is protected");
        Require(c.Shots[0].TargetSoldier==front&&c.Shots[0].Killed&&c.Shots[0].End.z<21,"Visible impact identifies the same casualty");
        Require(w.Battalions[0].Hits==1&&w.Battalions[0].Kills==1&&w.Soldiers.Kills[0]==1,"Exact shooter receives one hit and kill");
        Require(w.Deaths[front].Cause==DeathCause.Musket&&w.Deaths[front].Born==w.Time,"Death animation begins on impact");
        Advance(w,c,1f);Require(w.DeathCount==1&&w.Battalions[0].Hits==1,"Spent bullet cannot cause duplicate damage");

        w=Fixture(out c,out front,out rear);Launch(c);w.Soldiers.Positions[front]=new Vector2(4,20);Advance(w,c,.7f);
        Require(w.Soldiers.Alive[front]&&!w.Soldiers.Alive[rear]&&c.Shots[0].TargetSoldier==rear,"Moving out of the lane avoids the bullet; next actual contact dies");
        w=Fixture(out c,out front,out rear);Launch(c,x:3);Advance(w,c,.7f);
        Require(w.DeathCount==0&&w.Battalions[0].Hits==0,"Empty lane cannot damage a random regiment member");
        w=Fixture(out c,out front,out rear);Launch(c,damage:20);Advance(w,c,.7f);
        Require(w.Soldiers.Health[front]==80&&w.Soldiers.Health[rear]==100&&w.DeathCount==0,"Nonlethal contact wounds only the struck soldier");
        w=Fixture(out c,out front,out rear);Launch(c);Launch(c);Advance(w,c,.7f);
        Require(w.DeathCount==2&&w.Battalions[0].Kills==2,"Separate bullets can strike the rear after the front falls");
        w=Fixture(out c,out front,out rear);Launch(c);
        c.BulletScenery=new SceneryImpactMap();
        c.BulletScenery.Register(new Bounds(new Vector3(0,1,10),new Vector3(2,2,.2f)),SmallArmsSurface.Masonry);
        Advance(w,c,.7f);Require(w.DeathCount==0&&c.Shots[0].Grounded&&c.Shots[0].End.z<11,"Scenery stops the bullet before soldiers behind it");
        w=Fixture(out c,out front,out rear);Launch(c);w.Soldiers.Positions[front]=new Vector2(4,20);
        w.Soldiers.Positions[rear]=new Vector2(0,20);Advance(w,c,.7f);
        Require(!w.Soldiers.Alive[rear]&&w.Soldiers.Alive[front],"A different soldier entering the path receives the hit");
        w=Fixture(out c,out front,out rear);Launch(c);
        w.KillSoldier(front,DeathCause.Melee,Vector3.zero,Vector3.zero);Advance(w,c,.7f);
        Require(w.Deaths[front].Cause==DeathCause.Melee&&!w.Soldiers.Alive[rear]&&w.Battalions[0].Kills==1,"Already fallen soldiers cannot be hit again or replaced randomly");
        w=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=32,InitialSeparation=30,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        MeleeEncounter.Place(w,w.Battalions[0],Vector2.zero,0);
        MeleeEncounter.Place(w,w.Battalions[1],new Vector2(0,30),Mathf.PI);
        w.Battalions[1].FireAtWill=false;c=new CombatSystem(w);c.Step(.05f);
        Require(w.Battalions[0].ShotsFired>0&&w.DeathCount==0,"Normal volley schedules bullets without instant regiment damage");
        w.Battalions[0].FireAtWill=false;
        for(int tick=0;tick<40;tick++){w.StepMovement(.05f);c.Step(.05f);}
        Require(w.DeathCount>0&&w.Battalions[0].Kills==w.DeathCount,"Normal firing pipeline produces credited ballistic casualties");
        for(int i=0;i<w.DeathCount;i++)
        {
            int dead=w.DeadSoldiers[i];bool found=false;
            for(int shot=0;shot<c.ShotSequence;shot++)if(c.Shots[shot].TargetSoldier==dead&&c.Shots[shot].Killed)found=true;
            Require(found,"Every visible volley casualty is the soldier struck by its bullet");
        }
        w=Fixture(out c,out front,out rear);
        w.Battalions[1].Routing=true;w.Battalions[1].Running=true;
        w.Soldiers.Velocities[front]=new Vector2(0,5);w.Soldiers.MovementSpeeds[front]=5;
        Launch(c);Advance(w,c,.7f);
        Require(!w.Soldiers.Alive[front]&&w.Soldiers.Alive[rear]&&w.Deaths[front].Running&&w.Deaths[front].Moving,
            "A routing runner dies at the actual bullet contact with movement captured");
        var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        var set=library.Resolve(w.Battalions[1]);
        foreach(var lod in set.Lods)
        {
            int variant=InfantryDeathMotion.Variant(w.Deaths[front],lod.DeathVariants?.Length??0);
            var poses=variant>=0?lod.DeathVariants[variant].Poses:lod.Death;
            Require(poses!=null&&poses.Length>1,"Routing casualties have animated fall tracks at every LOD");
            float duration=InfantryDeathMotion.Duration(w.Deaths[front],variant);
            int start=AnimationPlayback.Frame(poses.Length,0,false,out _);
            int end=AnimationPlayback.Frame(poses.Length,2/duration,false,out _);
            Require(start==0&&end==poses.Length-1,"Routing death advances from the first fall pose to the settled body");
        }
        InfantryDeathVerification.Verify();
        Debug.Log("MUSKET_CONTACT_VERIFIED scheduled fire, flight, first body contact, moving targets, misses, wounds, credit and death tracks");
    }
}
