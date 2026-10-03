using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class ArtilleryRecoveryVerification
{
    static void Require(bool value, string message)
    { if (!value) throw new Exception("Artillery recovery: " + message); }
    static BattleWorld World() => new BattleWorld(new BattleConfig { BattalionsPerSide=1,
        SoldiersPerBattalion=8, EnableAI=false, EnableLandscape=false, FrenchArtillery=0, BritishArtillery=0 });

    public static void VerifyDirectionalMotion()
    {
        var world=World();var artillery=new ArtillerySystem(world);
        Vector2 center=new Vector2(100,0);
        for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(400+i,400);
        var directions=new[]{Vector2.left,Vector2.right,Vector2.up,Vector2.down};
        float radius=ArtillerySystem.ExplosionRadius(ArtilleryAmmunition.Explosive);
        for(int i=0;i<4;i++){world.Soldiers.Positions[i]=center+directions[i]*radius*.5f;world.Soldiers.Facings[i]=0;}
        typeof(ArtillerySystem).GetMethod("Explode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(artillery,
            new object[]{new CannonProjectile { Position=world.Terrain.SurfacePoint(center),Side=world.Battalions[1].Side,AmmunitionType=ArtilleryAmmunition.Explosive }});
        var endings=new System.Collections.Generic.HashSet<float>();
        for(int i=0;i<4;i++)
        {
            var impulse=world.Soldiers.KnockdownImpulses[i];Vector2 horizontal=new Vector2(impulse.x,impulse.z);
            Require(world.Soldiers.IsKnockedDown(i,0)&&Vector2.Dot(horizontal.normalized,directions[i])>.999f,"Blast pushes survivors radially in all four directions");
            Vector3 landing=world.Soldiers.KnockdownLandingPositions[i];Vector3 origin=world.Soldiers.KnockdownOrigins[i];
            Require(Vector2.Dot(new Vector2(landing.x-origin.x,landing.z-origin.z),directions[i])>0,"Landing follows outward throw");
            int variant=InfantryRecoveryTiming.Variant(i,impulse,0,4);
            var facing=InfantryRecoveryTiming.Facing(impulse,0,variant);
            var fallDirection=facing*(variant%2==1?Vector3.forward:Vector3.back);
            Require(Vector3.Dot(fallDirection,new Vector3(directions[i].x,0,directions[i].y))>.999f,"Fall animation matches throw direction");
            float end=world.Soldiers.KnockdownEnds[i];endings.Add(end);
            InfantryRecoveryTiming.Phase(world,i,end-InfantryRecoveryTiming.RiseDuration(i)*.5f,out bool rising);
            Require(rising,"Matched recovery begins after settling");
            var rotation=InfantryRecoveryTiming.Rotation(world,i,end,variant);
            Require(Quaternion.Angle(rotation,Quaternion.identity)<.01f,"Recovery returns continuously to standing facing");
        }
        Require(endings.Count>1,"Recovery times vary across the same blast");
        var tumbleA=InfantryRecoveryTiming.AirborneRotation(0,new Vector3(-6,4,0),.3f,.6f);
        var tumbleB=InfantryRecoveryTiming.AirborneRotation(1,new Vector3(6,4,0),.3f,.6f);
        Require(Quaternion.Angle(tumbleA,tumbleB)>10,"Opposite throws have distinct directional tumble");
        int small=0,large=0;float largest=0;
        for(int i=0;i<80;i++)
        {
            float size=ArtilleryEffectShape.ClodRadius(97+i,i,80);
            Require(size>=.035f&&size<=.55f,"Clod sizes remain bounded");
            if(size<.1f)small++;if(size>.25f)large++;largest=Mathf.Max(largest,size);
        }
        Require(small>50&&large>=3&&large<12&&largest>.4f,"Power distribution gives many small clods and rare large chunks");
        Debug.Log("DIRECTIONAL_BLAST_RECOVERY_VERIFIED");
    }

    public static void Verify()
    {
        // Actual soldier distances matter even when all men are outside the formation's bounds.
        var blastWorld=World();var artillery=new ArtillerySystem(blastWorld);
        int start=blastWorld.Battalions[1].SoldierStart;
        for(int i=0;i<8;i++)blastWorld.Soldiers.Positions[start+i]=new Vector2(140+i,0);
        blastWorld.Soldiers.Positions[start]=new Vector2(100,0);
        float blastRadius=ArtillerySystem.ExplosionRadius(ArtilleryAmmunition.Explosive);
        blastWorld.Soldiers.Positions[start+1]=new Vector2(100+blastRadius*.45f,0);
        blastWorld.Soldiers.Positions[start+2]=new Vector2(100+blastRadius*.7f,0);
        blastWorld.Soldiers.Positions[start+3]=new Vector2(101+blastRadius,0);
        typeof(ArtillerySystem).GetMethod("Explode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(artillery,
            new object[]{new CannonProjectile{Position=new Vector3(100,0,0),Side=ArmySide.French,AmmunitionType=ArtilleryAmmunition.Explosive}});
        Require(!blastWorld.Soldiers.Alive[start]&&blastWorld.DeathCount==1,"Blast core is lethal at the actual victim");
        Require(blastWorld.Soldiers.IsKnockedDown(start+1,0)&&blastWorld.Soldiers.IsKnockedDown(start+2,0),"Survivors in the blast fall down");
        Require(blastWorld.Soldiers.Health[start+1]<blastWorld.Soldiers.Health[start+2],"Damage falls with individual distance");
        Require(blastWorld.Soldiers.Health[start+3]==100&&!blastWorld.Soldiers.IsKnockedDown(start+3,0),"Outside blast is unaffected");
        Require(blastWorld.Battalions[1].AliveCount==7,"Knockdowns do not become casualties");

        foreach(bool jobs in new[]{false,true})foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var world=World();var unit=world.Battalions[1];int soldier=unit.SoldierStart;
            int slot=world.Soldiers.FormationSlots[soldier],events=0;world.SoldierDied+=_=>events++;
            using var movement=jobs?new SoldierMovementJobs(world):null;world.MovementJobs=movement;
            Vector2 original=world.Soldiers.Positions[soldier];
            world.ApplyArtilleryHit(soldier,25,DeathCause.Shell,new Vector3(3,2,0),world.Terrain.SurfacePoint(original));
            Require(world.Soldiers.Health[soldier]==75&&world.Soldiers.Alive[soldier]&&events==0,"Wounds persist without death events");
            Require(!RifleFireTiming.SoldierReady(world,unit,soldier),"Downed soldier cannot fire even at his slot");
            var shooters=new int[8];int count=RifleFireTiming.ScheduleVolley(world,unit,shooters);
            for(int i=0;i<count;i++)Require(shooters[i]!=soldier,"Volley excludes downed soldiers");
            world.StepMovement(dt);
            Require(world.Soldiers.MovementSpeeds[soldier]==0,"Normal marching is suspended during the throw");
            for(int i=0;i<Mathf.CeilToInt(18f/dt);i++)world.StepMovement(dt);
            Vector2 target=unit.Position+FormationMath.SlotOffset(unit,world.Soldiers.FormationSlots[soldier]);
            Require(!world.Soldiers.IsKnockedDown(soldier,world.Time)&&Vector2.Distance(world.Soldiers.Positions[soldier],target)<.05f,"Survivor recovers and rejoins formation");
            Require(world.Soldiers.FormationSlots[soldier]==slot&&unit.AliveCount==8&&world.DeathCount==0&&events==0,"Recovery preserves slot and roster");
            Require(world.Soldiers.Health[soldier]==75&&RifleFireTiming.SoldierReady(world,unit,soldier),"Recovered soldier remains wounded and ready");
            world.ApplyArtilleryHit(soldier,80,DeathCause.Shell,Vector3.up,Vector3.zero);
            Require(!world.Soldiers.Alive[soldier]&&world.DeathCount==1&&events==1,"Further damage can kill a survivor exactly once");
        }
        // A slow ball wounds once even if successive sweeps overlap the same man.
        var slow=World();var guns=new ArtillerySystem(slow);int victim=slow.Battalions[1].SoldierStart;
        for(int i=0;i<8;i++)slow.Soldiers.Positions[victim+i]=new Vector2(130+i,0);
        slow.Soldiers.Positions[victim]=new Vector2(100,0);
        var ball=guns.Projectiles[0];ball.Active=true;ball.FromCamera=true;ball.Side=ArmySide.French;
        ball.Position=new Vector3(100,1,-.5f);ball.Velocity=new Vector3(0,0,25);
        guns.Step(.025f);float wounded=slow.Soldiers.Health[victim];guns.Step(.025f);
        Require(wounded>0&&wounded<100&&slow.Soldiers.Health[victim]==wounded&&ball.HitCount==1,"Slow roundshot knocks down without duplicate hits");
        Require(slow.Soldiers.IsKnockedDown(victim,0)&&slow.DeathCount==0,"Low energy contact is survivable");
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var glance=World();var gun=new ArtillerySystem(glance);int man=glance.Battalions[1].SoldierStart;
            for(int i=0;i<8;i++)glance.Soldiers.Positions[man+i]=new Vector2(130+i,0);
            glance.Soldiers.Positions[man]=new Vector2(100.36f,0);
            var shot=gun.Projectiles[0];shot.Active=true;shot.FromCamera=true;shot.Side=ArmySide.French;
            shot.Position=new Vector3(100,1,-1);shot.Velocity=new Vector3(0,0,80);
            for(int i=0;i<Mathf.CeilToInt(.2f/dt);i++)gun.Step(dt);
            Require(glance.Soldiers.IsKnockedDown(man,0)&&glance.DeathCount==0&&shot.HitCount==1,"A fast glancing ball is survivable at every timestep");
        }
        foreach(bool jobs in new[]{false,true})
        {
            var marching=World();var unit=marching.Battalions[1];int man=unit.SoldierStart;
            using var movement=jobs?new SoldierMovementJobs(marching):null;marching.MovementJobs=movement;
            marching.ApplyArtilleryHit(man,20,DeathCause.Shell,new Vector3(3,2,0),Vector3.zero);
            marching.IssueMove(unit.Id,unit.Position+new Vector2(0,-12),unit.Facing);
            for(int i=0;i<600;i++)marching.StepMovement(.05f);
            Vector2 target=unit.Position+FormationMath.SlotOffset(unit,marching.Soldiers.FormationSlots[man]);
            Require(Vector2.Distance(marching.Soldiers.Positions[man],target)<.05f,"Recovery catches up with a formation that marched away");
            marching.ApplyArtilleryHit(man,10,DeathCause.Shell,Vector3.up*2,Vector3.zero);
            marching.ApplyArtilleryHit(man,100,DeathCause.Shell,Vector3.up*2,Vector3.zero);
            for(int i=0;i<120;i++)marching.StepMovement(.05f);
            Require(!marching.Soldiers.Alive[man]&&!marching.Soldiers.IsKnockedDown(man,marching.Time)&&marching.DeathCount==1,"A killed downed man never gets back up");
        }
        var melee=World();var fighters=melee.Battalions[0];var defenders=melee.Battalions[1];
        MeleeEncounter.Place(melee,fighters,Vector2.zero,0);MeleeEncounter.Place(melee,defenders,new Vector2(0,3.6f),Mathf.PI);
        for(int i=0;i<fighters.SoldierCount;i++)
            melee.ApplyArtilleryHit(fighters.SoldierStart+i,5,DeathCause.Shell,Vector3.up,Vector3.zero);
        var combat=new CombatSystem(melee);
        // Hold recovery time fixed, and suppress the defenders' attacks to isolate downed attackers.
        for(int i=0;i<800;i++){defenders.Cohesion=0;combat.Step(.05f);}
        Require(defenders.AliveCount==8,"Downed soldiers contribute no melee attacks");
        for(int i=defenders.SoldierStart;i<defenders.SoldierStart+8;i++)
            Require(melee.Soldiers.Health[i]==100,"No melee wounds from downed men");
        melee.StepMovement(12f);
        MeleeEncounter.Place(melee,fighters,Vector2.zero,0);MeleeEncounter.Place(melee,defenders,new Vector2(0,3.6f),Mathf.PI);
        for(int i=0;i<800;i++){defenders.Cohesion=0;melee.StepMovement(.05f);combat.Step(.05f);}
        Require(defenders.AliveCount<8,"Recovered soldiers resume melee contribution");
        RoundshotHitVerification.Verify();CasualtyVerification.Verify();
        Debug.Log("ARTILLERY_RECOVERY_VERIFIED distance damage, blast core, survivors, fire exclusion, slow balls, repeated wounds, roster and recovery at three timesteps with both movement paths");
    }
    public static void BuildMatchedPlaytests()
    {
        RegimentPresentationVerification.Verify();
        InfantryBehaviorVerification.Verify();
        BattleAudioBuild.Verify();
        Verify();
        BallisticsVerification.Verify();
        InfantryBattleBuild.BuildMatchedPlaytests();
    }
}
