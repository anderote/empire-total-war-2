using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class ArtilleryAmmunitionVerification
{
    static void Require(bool value, string message)
    { if (!value) throw new Exception("Artillery ammunition: " + message); }

    static BattleWorld World(out ArtillerySystem artillery)
    {
        var world = new BattleWorld(new BattleConfig { BattalionsPerSide=1, SoldiersPerBattalion=8,
            EnableAI=false, EnableLandscape=false, FrenchArtillery=0, BritishArtillery=0 });
        artillery = new ArtillerySystem(world);
        for (int i=0;i<world.Soldiers.Alive.Length;i++) world.Soldiers.Positions[i]=new Vector2(400+i,400);
        return world;
    }

    public static void VerifyFriendlyFireAndShrapnel()
    {
        var world=World(out var artillery);
        var shell=new CannonProjectile { Side=world.Battalions[0].Side,Position=new Vector3(100,0,0),AmmunitionType=ArtilleryAmmunition.Explosive };
        world.Soldiers.Positions[0]=new Vector2(100,0);
        typeof(ArtillerySystem).GetMethod("Explode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(artillery,new object[]{shell});
        Require(world.Soldiers.Health[0]<100,"Shell blast wounds friendly troops");
        int fragments=0;CannonProjectile selected=null;
        foreach(var shot in artillery.Projectiles)if(shot.Active&&shot.Shrapnel){fragments++;selected=shot;}
        Require(fragments==24,"Shell releases twenty-four short-range fragments");
        foreach(var shot in artillery.Projectiles)shot.Active=false;
        selected.Active=true;selected.Position=new Vector3(120,1,-1);selected.Velocity=new Vector3(0,0,80);
        world.Soldiers.Positions[1]=new Vector2(120,0);
        artillery.Step(.05f);
        Require(world.Soldiers.Health[1]<100&&!selected.Active,"Fragment wounds friendly troop and stops on contact");
        var combat=new CombatSystem(world);
        var blocks=typeof(CombatSystem).GetMethod("FriendlyBlocksFire",BindingFlags.Instance|BindingFlags.NonPublic);
        var shooter=world.Battalions[0];var ally=world.Battalions[1];
        ally.Side=shooter.Side;shooter.Position=Vector2.zero;
        world.Soldiers.Positions[ally.SoldierStart]=new Vector2(0,20);
        Require((bool)blocks.Invoke(combat,new object[]{shooter,new Vector2(0,40)}),"Automatic fire holds for friendly troops in lane");
        shooter.AttackMode=InfantryAttackMode.FirePosition;shooter.AttackTargetId=1;
        Require(!(bool)blocks.Invoke(combat,new object[]{shooter,new Vector2(0,40)}),"Explicit attack order overrides friendly-fire risk");
        world.Soldiers.Positions[2]=new Vector2(140,0);
        typeof(CombatSystem).GetMethod("LaunchBullet",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(combat,
            new object[]{new Vector3(140,1,-1),new Vector3(140,1,2),60f,0,1,world.Time,true,1f,false});
        typeof(BattleWorld).GetProperty("Time").SetValue(world,world.Time+.05f);
        typeof(CombatSystem).GetMethod("StepBullets",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(combat,null);
        Require(world.Soldiers.Health[2]<100,"Musket bullet wounds friendly troop");
        Require(shooter.Hits==0&&shooter.Kills==0,"Friendly hits award no combat credit");
        Debug.Log("FRIENDLY_FIRE_SHRAPNEL_VERIFIED");
    }

    public static void Verify()
    {
        foreach (ArtilleryAmmunition type in Enum.GetValues(typeof(ArtilleryAmmunition)))
        {
            var world=World(out var artillery);
            var shot=artillery.Projectiles[0];shot.Active=true;shot.FromCamera=true;shot.Side=ArmySide.French;
            shot.Position=new Vector3(100,1,0);shot.Velocity=new Vector3(0,-20,0);shot.AmmunitionType=type;
            artillery.Step(.1f);
            Require(!shot.Active,"Steep " + type + " impact terminates projectile");
            var impact=artillery.Impacts[0];
            Require(impact.Sequence==1 && impact.TerrainStrike && impact.AmmunitionType==type,"Ground impact preserves " + type);
            Require(impact.Detonates==(type==ArtilleryAmmunition.Explosive || type==ArtilleryAmmunition.Carcass),"Only shell and carcass detonate");
            Require((world.Terrain.DamageRevision>0)==(type!=ArtilleryAmmunition.Canister && !ArtillerySystem.IsCloudShell(type)),"Only earth-moving ammunition excavates");
            Require((artillery.Hazards[0].Remaining>0)==(type==ArtilleryAmmunition.Carcass || ArtillerySystem.IsCloudShell(type)),"Only carcass and cloud shells leave hazards");
            if(type==ArtilleryAmmunition.Carcass)
                Require(artillery.Hazards[0].Remaining>=ArtillerySystem.CarcassBurnDuration-.1f,"Carcass impact creates a ninety-second fire");
        }
        // Same local contact at different pellet ages; near is lethal, distant is recoverable.
        foreach (float age in new[]{0f,1f}) foreach (float dt in new[]{.025f,.05f,.1f})
        {
            var world=World(out var artillery);int victim=world.Battalions[1].SoldierStart;
            world.Soldiers.Positions[victim]=new Vector2(100,0);
            var shot=artillery.Projectiles[0];shot.Active=true;shot.FromCamera=true;shot.Side=ArmySide.French;
            shot.Position=new Vector3(100,1,-1);shot.Velocity=new Vector3(0,0,80);
            shot.AmmunitionType=ArtilleryAmmunition.Canister;shot.Age=age;
            for (int i=0;i<Mathf.CeilToInt(.1f/dt);i++) artillery.Step(dt);
            Require(shot.HitCount==1 && !shot.Active,"Pellet stops at first contact for every timestep");
            Require(world.Soldiers.Alive[victim]==(age>0),"Canister damage falls with range");
            if (age>0) Require(world.Soldiers.Health[victim]>40 && world.Soldiers.IsKnockedDown(victim,world.Time),"Distant pellet wounds and knocks down");
        }
        foreach (float dt in new[]{.05f,3f,18f,60f,120f})
        {
            var world=World(out var artillery);int start=world.Battalions[1].SoldierStart;
            world.Soldiers.Positions[start]=new Vector2(100,0);
            world.Soldiers.Positions[start+1]=new Vector2(100+ArtillerySystem.CarcassFireRadius*.9f,0);
            world.Soldiers.Positions[start+2]=new Vector2(100+ArtillerySystem.CarcassFireRadius+1,0);
            // Fire must affect either army, including a straggler outside formation bounds.
            world.Soldiers.Positions[0]=new Vector2(100,0);
            var hazard=artillery.Hazards[0];hazard.Position=new Vector3(100,0,0);
            hazard.Type=ArtilleryAmmunition.Carcass;hazard.Remaining=ArtillerySystem.CarcassBurnDuration;hazard.UntilPulse=3;
            for (int i=0;i<Mathf.CeilToInt(ArtillerySystem.CarcassBurnDuration/dt);i++) artillery.Step(dt);
            Require(!world.Soldiers.Alive[start] && !world.Soldiers.Alive[0],"Sustained fire kills exposed men on either side");
            Require(Mathf.Abs(world.Soldiers.Health[start+1]-28)<.001f, $"Fire falloff and pulse count are timestep independent (dt={dt}, health={world.Soldiers.Health[start+1]}, remaining={hazard.Remaining}, untilPulse={hazard.UntilPulse})");
            Require(world.Soldiers.Health[start+2]==100,"Fire cannot select a victim outside its radius");
            Require(!world.Soldiers.IsKnockedDown(start+1,world.Time),"Heat damage does not repeatedly throw survivors");
            Require(hazard.Remaining==0,"Hazard lifetime is bounded");
            float health=world.Soldiers.Health[start+1];artillery.Step(60);
            Require(world.Soldiers.Health[start+1]==health,"Expired hazards cannot keep damaging");
        }
        {
            var world=World(out var artillery);var unit=world.Battalions[1];int start=unit.SoldierStart;
            world.Soldiers.Positions[start]=new Vector2(100,0);
            var hazard=artillery.Hazards[0];hazard.Position=new Vector3(100,0,0);
            hazard.Type=ArtilleryAmmunition.Lime;hazard.Remaining=30;hazard.UntilPulse=3;
            float morale=unit.Morale,cohesion=unit.Cohesion,fatigue=unit.Fatigue;
            artillery.Step(3);
            Require(Mathf.Abs(unit.Morale-(morale-.125f))<.0001f && Mathf.Abs(unit.Cohesion-(cohesion-.0075f))<.0001f,
                "Lime disruption scales with the exposed share");
            Require(Mathf.Abs(unit.Fatigue-(fatigue+.375f))<.0001f && world.Soldiers.Health[start]==94 && world.DeathCount==0,
                "Lime disrupts and wounds proportionally without an explosive blast");
            world.Soldiers.Positions[start]=new Vector2(400,400);morale=unit.Morale;
            artillery.Step(30);Require(unit.Morale==morale,"Leaving the cloud stops exposure");
        }
        {
            var world=World(out var artillery);int start=world.Battalions[1].SoldierStart;
            world.Soldiers.Positions[start]=new Vector2(100,0);world.Soldiers.Positions[start+1]=new Vector2(100+ArtillerySystem.ExplosionRadius(ArtilleryAmmunition.Carcass)*.55f,0);
            typeof(ArtillerySystem).GetMethod("Explode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(artillery,
                new object[]{new CannonProjectile { Side=ArmySide.French, Position=new Vector3(100,0,0), AmmunitionType=ArtilleryAmmunition.Carcass }});
            Require(!world.Soldiers.Alive[start] && world.Soldiers.Alive[start+1] && world.Soldiers.IsKnockedDown(start+1,0),
                "Carcass has a lethal core and recoverable blast edge before its fire starts");
        }
        {
            var hit=new ArtilleryImpact{ExplosiveShell=true,AmmunitionType=ArtilleryAmmunition.Explosive,Speed=150,Direction=Vector3.down,Sequence=1};
            var shell=ArtilleryEarthwork.FromImpact(hit);hit.AmmunitionType=ArtilleryAmmunition.Carcass;
            var carcass=ArtilleryEarthwork.FromImpact(hit);
            Require(Mathf.Abs(carcass.Radius/shell.Radius-.55f)<.0001f&&Mathf.Abs(carcass.Depth/shell.Depth-.35f)<.0001f,
                "Carcass makes a narrower and shallower crater than a comparable shell");
            var world=World(out var artillery);int victim=world.Battalions[1].SoldierStart;
            var hazard=artillery.Hazards[0];hazard.Position=new Vector3(100,0,0);hazard.Type=ArtilleryAmmunition.Carcass;
            hazard.Remaining=ArtillerySystem.CarcassBurnDuration;hazard.UntilPulse=3;
            world.Soldiers.Positions[victim]=new Vector2(112,0);artillery.Step(3);
            Require(world.Soldiers.Health[victim]<100,"Widened fire reaches beyond the old ten-metre radius");
            float health=world.Soldiers.Health[victim];world.Soldiers.Positions[victim]=new Vector2(120,0);
            artillery.Step(30);Require(world.Soldiers.Health[victim]==health&&hazard.Remaining>0,"Leaving a long-lived fire stops exposure");
        }
        var sceneryWorld = new BattleWorld(new BattleConfig { BattalionsPerSide=1, SoldiersPerBattalion=8,
            EnableAI=false, EnableLandscape=true, FrenchArtillery=0, BritishArtillery=0 });
        var owner = new GameObject("Ammunition scenery verification");
        try
        {
            var landscape = owner.AddComponent<RuralLandscape>(); landscape.Initialize(sceneryWorld,null);
            var artillery = new ArtillerySystem(sceneryWorld); landscape.Artillery=artillery;
            Vector3 root = landscape.ClosestTreeRoot(Vector2.zero);
            foreach (var type in new[]{ArtilleryAmmunition.Lime,ArtilleryAmmunition.PoisonGas,ArtilleryAmmunition.Canister})
            {
                var ball=artillery.Projectiles[0];ball.Active=true;ball.Id=100+(int)type;
                ball.Position=root+Vector3.up*1.5f;ball.Velocity=Vector3.right*90f;
                ball.SegmentAge=.05f;ball.AmmunitionType=type;
                typeof(ArtillerySystem).GetMethod("EmitImpact",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(artillery,
                    new object[]{root, ArtillerySystem.IsCloudShell(type), true, Vector3.down*100f, type});
                landscape.ConsumeArtilleryDamage();
                Require(landscape.BrokenTreeCount==0,"Lime and canister cannot fell whole trees");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
        Debug.Log("ARTILLERY_AMMUNITION_VERIFIED: all six impacts, terrain distinctions, pellet range, recoverable wounds, local fire, lifespan, timestep consistency and proportional lime exposure.");
    }
}
