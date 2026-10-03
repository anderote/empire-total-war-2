using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EmpireBattle;
using EmpireGame;
using UnityEngine;

public static class CounterBatteryVerification
{
    static void Require(bool value,string message) { if(!value)throw new Exception("Counter-battery: "+message); }
    static BattleWorld Make(out ArtillerySystem artillery,int count=1)
    {
        var world=new BattleWorld(new BattleConfig { BattalionsPerSide=1,SoldiersPerBattalion=8,
            EnableAI=false,EnableLandscape=false,FrenchArtillery=count,BritishArtillery=count });
        artillery=new ArtillerySystem(world);
        foreach(var unit in world.Battalions){unit.Position=new Vector2(400,400);unit.FireAtWill=false;}
        for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(400+i,400);
        foreach(var gun in artillery.Guns)
        {gun.Position=new Vector2(100+(gun.Id%count)*12,gun.Side==ArmySide.French?-100:0);gun.Facing=0;gun.FireAtWill=false;}
        return world;
    }
    static CannonProjectile Shoot(ArtillerySystem artillery,ArtilleryAmmunition type,Vector3 position,Vector3 velocity)
    {
        var shot=artillery.Projectiles.First(p=>!p.Active);
        shot.Active=true;shot.FromCamera=true;shot.GunId=0;shot.Side=ArmySide.French;
        shot.AmmunitionType=type;shot.Position=position;shot.Velocity=velocity;
        shot.Age=shot.SegmentAge=0;shot.HitCount=shot.BounceCount=0;
        shot.StruckSoldiers.Clear();shot.StruckArtillery.Clear();return shot;
    }
    public static void Verify()
    {
        foreach(var type in new[]{ArtilleryAmmunition.Roundshot,ArtilleryAmmunition.Canister,ArtilleryAmmunition.Explosive,ArtilleryAmmunition.Carcass})
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            Make(out var artillery);var gun=artillery.Guns[1];
            Shoot(artillery,type,new Vector3(100,.7f,-4),Vector3.forward*160);
            for(int i=0;i<Mathf.CeilToInt(.1f/dt);i++)artillery.Step(dt);
            Require(gun.Health<180,"Carriage takes "+type+" at dt="+dt);
            Require(artillery.Guns[0].Health==180,"Firing battery is untouched");
            if(type==ArtilleryAmmunition.Roundshot)
            {
                Require(gun.Destroyed&&!gun.Operational,"Direct solid shot disables the carriage");
                int ammo=gun.Ammunition;gun.FireAtWill=true;gun.ReadyDelay=0;gun.Phase=ArtilleryPhase.Ready;
                artillery.Step(30);Require(gun.Ammunition==ammo&&gun.ShotsFired==0,"Destroyed gun never fires or reloads back to life");
                Require(!artillery.OrderGround(gun.Id,new Vector2(100,-100)),"Destroyed gun rejects firing orders");
            }
        }
        foreach(var type in new[]{ArtilleryAmmunition.Roundshot,ArtilleryAmmunition.Canister})
        {
            var world=Make(out var artillery);var gun=artillery.Guns[1];var crew=artillery.CrewPosition(gun,0);
            Shoot(artillery,type,crew+new Vector3(0,1,3),Vector3.back*160);artillery.Step(.025f);
            Require(!gun.Crew[0].Alive&&gun.LivingCrew==3,"Direct "+type+" kills the intersected crew member");
            Require(gun.Health==180&&gun.Crew[1].Health==100,"Crew hit stays local");
            Require(artillery.Guns[0].Kills==1,"Crew kill credited once");
            float duration=gun.PhaseDuration(ArtilleryPhase.Loading);
            Require(duration>3,"Missing crew slows service");
            artillery.Step(.001f);Require(artillery.Guns[0].Kills==1,"Projectile cannot kill same crew twice");
            var owner=new GameObject("Offline crew casualty");
            try
            {
                var view=owner.AddComponent<ArtilleryCrewView>();view.Initialize(world,artillery,null);view.Sample();
                var corpse=owner.GetComponentsInChildren<Transform>(true).First(t=>t.name.Contains("gun 1 / Sponge"));
                Require(Vector3.Distance(corpse.position,gun.Crew[0].DeathPosition)<.001f,"Corpse uses collision position");
                world.StepMovement(2);view.Sample();Vector3 atRest=corpse.position;
                world.StepMovement(2);view.Sample();Require(corpse.position==atRest,"Crew corpse persists after fall");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
        foreach(var type in new[]{ArtilleryAmmunition.Explosive,ArtilleryAmmunition.Carcass})
        {
            Make(out var artillery);var gun=artillery.Guns[1];
            Shoot(artillery,type,artillery.CrewPosition(gun,0)+Vector3.up*3,Vector3.down*80);artillery.Step(.05f);
            Require(gun.Crew[0].Health<100&&gun.Health<180,"Direct "+type+" burst affects crew and adjacent carriage");
        }
        {
            Make(out var artillery);var gun=artillery.Guns[1];
            for(int role=0;role<4;role++)
            {
                Shoot(artillery,ArtilleryAmmunition.Canister,artillery.CrewPosition(gun,role)+Vector3.up*3,Vector3.down*160);
                artillery.Step(.025f);
            }
            Require(gun.Health==180&&gun.LivingCrew==0&&!gun.Operational,"Canister can silence an intact gun by killing its crew");
        }
        foreach(var type in new[]{ArtilleryAmmunition.Roundshot,ArtilleryAmmunition.Explosive})
        {
            Make(out var artillery);var gun=artillery.Guns[1];
            Shoot(artillery,type,new Vector3(100,5,-4),Vector3.forward*160);artillery.Step(.05f);
            Require(gun.Health==180&&gun.Crew.All(c=>c.Health==100),"High "+type+" miss cannot damage battery");
        }
        foreach(var type in new[]{ArtilleryAmmunition.Carcass,ArtilleryAmmunition.Lime})
        {
            Make(out var artillery);var friendly=artillery.Guns[0];
            var hazard=artillery.Hazards[0];hazard.Position=new Vector3(friendly.Position.x,0,friendly.Position.y);
            hazard.Side=friendly.Side;hazard.GunId=0;hazard.Type=type;hazard.Remaining=30;hazard.UntilPulse=3;
            artillery.Step(3);
            Require(type==ArtilleryAmmunition.Carcass?friendly.Crew.Any(c=>c.Health<100):friendly.Suppressed,
                "Persistent "+type+" also exposes friendly crews");
            Require(artillery.Guns[1].Health==180&&artillery.Guns[1].Crew.All(c=>c.Health==100)&&!artillery.Guns[1].Suppressed,
                "Distant battery outside hazard stays unaffected");
        }
        {
            var world=Make(out var artillery);var gun=artillery.Guns[1];var crew=artillery.CrewPosition(gun,0);
            int infantry=world.Battalions[1].SoldierStart;world.Soldiers.Positions[infantry]=new Vector2(crew.x,-2);
            Shoot(artillery,ArtilleryAmmunition.Canister,crew+new Vector3(0,1,-5),Vector3.forward*160);artillery.Step(.05f);
            Require(!world.Soldiers.Alive[infantry]&&gun.Crew[0].Alive,"Pellet stops on nearer infantry before reaching artillery crew");
        }
        foreach(float dt in new[]{.05f,3f,18f,120f})
        {
            Make(out var artillery);var gun=artillery.Guns[1];
            var hazard=artillery.Hazards[0];hazard.Position=new Vector3(100,0,0);hazard.Type=ArtilleryAmmunition.Carcass;
            hazard.GunId=0;hazard.Remaining=90;hazard.UntilPulse=3;
            for(int i=0;i<Mathf.CeilToInt(90/dt);i++)artillery.Step(dt);
            Require(gun.Destroyed&&gun.LivingCrew==0&&!gun.Operational,"Sustained carcass fire destroys carriage and crew at dt="+dt);
            Require(artillery.Guns[0].Kills==4,"Burning crews credit four kills only");
            float health=artillery.Guns[0].Health;artillery.Step(120);Require(artillery.Guns[0].Health==health,"Expired fire stops damage");
        }
        {
            Make(out var artillery);var gun=artillery.Guns[1];
            Shoot(artillery,ArtilleryAmmunition.Lime,new Vector3(100,3,0),Vector3.down*80);artillery.Step(.05f);
            gun.Phase=ArtilleryPhase.Loading;gun.PhaseElapsed=0;
            artillery.Step(3);float phase=gun.PhaseElapsed;
            Require(gun.Suppressed&&gun.Health==180&&gun.LivingCrew==4&&gun.Crew.All(c=>c.Health<100),"Brief lime exposure wounds and suppresses crews without damaging the carriage");
            artillery.Step(.1f);Require(gun.PhaseElapsed==phase,"Lime stops servicing");
            gun.Phase=ArtilleryPhase.Ready;gun.ReadyDelay=0;gun.Facing=Mathf.PI;
            artillery.OrderBattery(gun.Id,artillery.Guns[0].UnitId);int ammo=gun.Ammunition;
            artillery.Step(.1f);Require(gun.Ammunition==ammo,"Lime prevents firing");
            foreach(var hazard in artillery.Hazards)hazard.Remaining=0;
            artillery.Step(3.1f);Require(!gun.Suppressed&&gun.ShotsFired==1,"Battery resumes firing after exposure clears");
        }
        VerifyTargets();
        VerifySavedLosses();
        RoundshotHitVerification.Verify();ArtilleryTargetingVerification.Verify();
        typeof(AnimationQualityVerification).GetMethod("VerifyCrew",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        Directory.CreateDirectory("Artifacts/CounterBattery");
        const string report="PASS: all five ammunition types affect guns/crew; local swept hits and misses, canister ordering, fire lifespan/timestep checks, hazards on both armies, lime service/fire interruption and recovery, crew loss/reload slowdown/corpses, disabled gun orders; individual battery target distribution/rotation/survivor retargeting/range; saved Regimental Glory losses, report, next deployment and replenishment; existing roundshot, ammunition, targeting and crew-animation checks. Offline only; no player launched.";
        File.WriteAllText("Artifacts/CounterBattery/verification.txt",report);Debug.Log("COUNTER_BATTERY_VERIFIED "+report);
    }
    static void VerifySavedLosses()
    {
        var company=new GloryCompany();company.Save.coins=10000;company.Save.commanderXP=4000;
        Require(company.Recruit(10),"Battery recruitment fixture");
        var battery=company.Save.regiments.Last();var scenario=company.Choices()[0];
        var world=new BattleWorld(company.Deployment(scenario));var artillery=new ArtillerySystem(world);
        var guns=artillery.Guns.Where(g=>g.Side==ArmySide.French).ToArray();
        guns[0].Health=0;foreach(var crew in guns[1].Crew)crew.Health=0;
        company.Save.inBattle=true;
        Require(company.Complete(scenario,world,true,artillery),"Battle records battery result");
        Require(battery.soldiers==2&&GloryCompany.Valid(company.Save),"Disabled guns remain lost in a valid partial battery save");
        Require(new GloryCompany(JsonUtility.FromJson<GlorySave>(JsonUtility.ToJson(company.Save))).Save.regiments.Last().soldiers==2,
            "Partial battery strength survives save serialization");
        Require(company.Save.lastBattle.regiments.Last().Casualties==2,"Battle report lists two gun losses");
        Require(company.Deployment(company.Choices()[0]).FrenchArtillery==2,"Next deployment only includes surviving guns");
        Require(company.Replenish(battery)&&battery.soldiers==4,"Paid replenishment restores lost pieces");
    }
    static void VerifyTargets()
    {
        Make(out var artillery,4);
        foreach(var type in (ArtilleryAmmunition[])Enum.GetValues(typeof(ArtilleryAmmunition)))
        {
            var targets=new HashSet<Vector2>();
            foreach(var gun in artillery.Guns.Take(4))
            {
                gun.AmmunitionType=type;
                Require(artillery.OrderGround(gun.Id,artillery.Guns[4].Position),"Clicking an enemy cannon accepts battery order");
                Require(gun.TargetBattery==artillery.Guns[4].UnitId&&!gun.HasGroundTarget,"Order tracks a battery rather than a fixed centre point");
                Require(artillery.TryAimPoint(gun,out var aim),"Enemy cannon within ammunition range");targets.Add(aim);
                Require(artillery.Guns.Skip(4).Any(g=>g.Position==aim),"Aim point is an actual enemy cannon for "+type);
            }
            Require(targets.Count==4,"Four firing guns cover four distinct enemy cannons for "+type);
        }
        var first=artillery.Guns[0];artillery.TryAimPoint(first,out var before);first.ShotsFired++;
        artillery.TryAimPoint(first,out var after);Require(before!=after,"Next salvo rotates individual targets");
        artillery.Guns[5].Health=0;
        foreach(var gun in artillery.Guns.Take(4))
        {Require(artillery.TryAimPoint(gun,out var aim)&&aim!=artillery.Guns[5].Position,"Salvos skip destroyed guns");}
        foreach(var gun in artillery.Guns.Skip(4))if(!gun.Destroyed)foreach(var member in gun.Crew)member.Health=0;
        Require(!artillery.TryAimPoint(first,out _),"No targeting empty or destroyed enemy battery");
        Make(out artillery,4);first=artillery.Guns[0];
        Require(artillery.TryAimPoint(first,out var auto)&&artillery.Guns.Skip(4).Any(g=>g.Position==auto),"Automatic fire can choose a nearer enemy battery");
        artillery.OrderGround(first.Id,new Vector2(350,100));
        Require(first.HasGroundTarget&&artillery.TryAimPoint(first,out var ground)&&ground==new Vector2(350,100),"Unoccupied ground orders remain exact");
        Require(!artillery.OrderBattery(first.Id,first.UnitId),"Cannot order direct fire on own battery");
        artillery.OrderBattery(first.Id,artillery.Guns[4].UnitId);first.AmmunitionType=ArtilleryAmmunition.Canister;
        foreach(var gun in artillery.Guns.Skip(4))gun.Position+=Vector2.up*1000;
        Require(!artillery.TryAimPoint(first,out _),"Out-of-range battery cannot be fired on");
    }
}
