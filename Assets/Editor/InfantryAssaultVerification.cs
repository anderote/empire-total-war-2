using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class InfantryAssaultVerification
{
    static void Require(bool value,string message)
    {if(!value)throw new Exception("Infantry assault: "+message);}

    static BattleWorld World() => new BattleWorld(new BattleConfig {
        BattalionsPerSide=1,SoldiersPerBattalion=64,InitialSeparation=180,
        FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false
    });

    public static void Verify()
    {
        VerifyChargeSpeed();VerifyMeleeMode();VerifyPursuitOrders();VerifyInfantryPursuit();
        foreach(float dt in new[]{.025f,.05f,.1f})VerifyPursuit(dt);
        VerifyRetreatFire();VerifyCavalryRewards();
        BattleOrdersVerification.Verify();
        Debug.Log("INFANTRY_ASSAULT_VERIFIED faster charges, melee mode/sword cursor, mixed infantry/cavalry pursuit, unique targets, retargeting, halt and scalar/Burst parity");
    }

    public static void VerifyHunting()
    {
        VerifyPursuitOrders();VerifyInfantryPursuit();
        foreach(float dt in new[]{.025f,.05f,.1f})VerifyPursuit(dt);
        InfantryPursuitVerification.Verify();
        InfantryContactVerification.VerifyReforming();
        Debug.Log("INFANTRY_HUNTING_VERIFIED pursuit, gathering, fatigue, contact and scalar/Burst parity");
    }

    static void VerifyRetreatFire()
    {
        foreach(bool routing in new[]{false,true})
        foreach(bool independent in new[]{false,true})
        foreach(bool explicitOrder in new[]{false,true})
        {
            var world=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=2,SoldiersPerBattalion=64,
                FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
            var shooter=world.Battalions[0];var target=world.Battalions[1];
            MeleeEncounter.Place(world,shooter,Vector2.zero,0);
            MeleeEncounter.Place(world,target,new Vector2(0,45),0);
            MeleeEncounter.Place(world,world.Battalions[2],new Vector2(500,500),0);
            foreach(var unit in world.Battalions)unit.FireAtWill=false;
            if(independent)world.SetFormation(0,FormationKind.Skirmish);
            else world.SetFireMode(0,InfantryFireMode.Volley);
            target.Routing=routing;world.Withdraw(target.Id);
            if(explicitOrder)Require(world.IssueRangedAttack(0,1),"Ranged order accepts retreating and routed targets");
            else shooter.FireAtWill=true;
            var combat=new CombatSystem(world);
            for(int i=0;i<140;i++){world.StepMovement(.05f);combat.Step(.05f);}
            Require(world.Outcome==BattleOutcome.Ongoing&&shooter.ShotsFired>0,"Automatic and assigned fire target fugitives during an ongoing battle");
            Require(shooter.AttackMode!=InfantryAttackMode.BayonetCharge&&!shooter.Moving,"Firing at fugitives never silently starts a melee pursuit");
            if(explicitOrder)Require(shooter.AttackTargetId==1,"Ranged order retains its retreating target");
        }
        Debug.Log("RETREAT_FIRE_VERIFIED assigned/automatic, volley/skirmish and withdrawing/routing targets during ongoing battles");
    }

    static void VerifyCavalryRewards()
    {
        int a=UnitProgression.BattleXP(500,400,100,100,100,true);
        int b=UnitProgression.BattleXP(500,400,200,100,100,true);
        Require(b-a==100*UnitProgression.XPPerKill,"Kill XP grows proportionally without the old battle cap");
        Require(UnitProgression.BattleXP(50,50,100,0,0,true)==UnitProgression.BattleXP(500,500,100,0,0,true),"Each kill has the same XP value for cavalry and large infantry units");
        Require(UnitProgression.BattleXP(1,1,int.MaxValue,int.MaxValue,1,true)==UnitProgression.MaxXP,"Kill rewards cannot overflow");
        Require(UnitProgression.BattleXP(100,100,0,0,0,true)==0&&UnitProgression.BattleXP(100,0,100,100,100,true)==0,"Idle and destroyed units receive no veterans' XP");
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var world=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,FrenchCavalry=1,
                RidersPerSquadron=32,SoldiersPerBattalion=96,FrenchArtillery=0,BritishArtillery=0,
                EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
            var session=new BattleArmorySession(world,new ArtillerySystem(world),null);
            var cavalry=Array.Find(world.Battalions,u=>u.Side==ArmySide.French&&u.Kind==UnitKind.Cavalry);
            var fugitives=Array.Find(world.Battalions,u=>u.Side==ArmySide.British);
            MeleeEncounter.Place(world,world.Battalions[0],new Vector2(-500,-500),0);
            MeleeEncounter.Place(world,cavalry,Vector2.zero,0);
            MeleeEncounter.Place(world,fugitives,new Vector2(0,25),0);
            foreach(var unit in world.Battalions)unit.FireAtWill=false;
            fugitives.Routing=true;world.Withdraw(fugitives.Id);world.IssueCharge(cavalry.Id,fugitives.Id);
            var combat=new CombatSystem(world);
            for(int tick=0;tick<Mathf.CeilToInt(30/dt);tick++){world.StepMovement(dt);combat.Step(dt);}
            Require(cavalry.Kills>=48&&cavalry.AliveCount==32,"Cavalry kills at least half a larger routing regiment without counterattack: "+dt+" kills="+cavalry.Kills);
            Require(cavalry.Kills==96-fugitives.AliveCount,"Only real cavalry kills earn reward credit");
            var entry=session.Entries.Find(e=>e.users==EquipmentUsers.Cavalry);
            session.Complete();int xp=entry.progression.xp;
            Require(xp==UnitProgression.BattleXP(32,32,cavalry.Kills,cavalry.Hits,cavalry.ShotsFired,true)&&xp>240&&entry.progression.UnitLevel>=3,"Successful cavalry pursuit persists uncapped rewards and multiple levels");
            session.Complete();Require(entry.progression.xp==xp,"Completing pursuit rewards twice cannot duplicate XP");
            Debug.Log($"CAVALRY_PURSUIT_REWARDS_VERIFIED dt={dt} kills={cavalry.Kills} xp={xp} level={entry.progression.UnitLevel}");
        }
    }

    public static void VerifyCombatChanges()
    {
        InfantryLethalityVerification.Verify();
        Verify();
    }

    public static void VerifyRetreatAndRewards()
    {
        VerifyMeleeMode();VerifyPursuitOrders();VerifyRetreatFire();VerifyCavalryRewards();
        foreach(float dt in new[]{.025f,.05f,.1f})VerifyPursuit(dt);
        BattleOrdersVerification.Verify();
        Debug.Log("RETREAT_AND_REWARDS_VERIFIED ranged fire, explicit melee pursuit, cavalry hunting, proportional XP and persisted level gains");
    }

    static void VerifyChargeSpeed()
    {
        var charge=World();var run=World();var tired=World();
        Vector2 start=charge.Battalions[0].Position;
        Require(charge.IssueCharge(0,1)&&tired.IssueCharge(0,1),"Infantry accepts charge orders");
        tired.Battalions[0].Fatigue=100;
        run.IssueMove(0,new Vector2(0,80),0);run.Battalions[0].Running=true;
        for(int i=0;i<100;i++){charge.StepMovement(.05f);run.StepMovement(.05f);tired.StepMovement(.05f);}
        float charging=Vector2.Distance(start,charge.Battalions[0].Position);
        float running=Vector2.Distance(start,run.Battalions[0].Position);
        Require(charging>running*1.55f&&charging<running*1.75f,"Charge is materially faster than ordinary running");
        Require(Vector2.Distance(start,tired.Battalions[0].Position)<charging*.75f,"Fatigue still slows charges");
        Debug.Log($"INFANTRY_CHARGE_SPEED charge={charging/5:F2}m/s run={running/5:F2}m/s ratio={charging/running:F2}");
    }

    static void VerifyMeleeMode()
    {
        var world=World();var unit=world.Battalions[0];
        var root=new GameObject("Melee command verification");
        try
        {
            var view=root.AddComponent<BattleView>();view.Initialize(world);view.SelectUnit(0,false);
            Require(view.AttackCursorFor(1)==BattleView.CommandCursorKind.Attack,"Ranged infantry have the ranged cursor");
            view.OrderToggleMelee();
            Require(unit.MeleeMode&&!unit.FireAtWill&&view.SelectedMeleeMode,"Melee toggle visibly arms selected infantry");
            Require(view.AttackCursorFor(1)==BattleView.CommandCursorKind.Melee&&view.UsesMeleeAttack(unit,1),"Cursor and attack both select melee");
            Require(world.IssueRangedAttack(0,1)&&unit.AttackMode==InfantryAttackMode.BayonetCharge&&unit.Running,"Enemy click charges in melee mode");
            world.Soldiers.ReloadReadyTimes[0]=world.Time+20;
            view.OrderToggleMelee();
            Require(!unit.MeleeMode&&unit.FireAtWill&&!unit.Moving&&unit.AttackMode==InfantryAttackMode.None,"Switching back cancels the charge and restores fire");
            Require(world.Soldiers.ReloadReadyTimes[0]==world.Time+20,"Mode changes never refill a musket");
            world.Battalions[1].Routing=true;
            Require(view.AttackCursorFor(1)==BattleView.CommandCursorKind.Attack,"Routing targets retain ranged cursor outside melee mode");
            var create=typeof(BattleView).GetMethod("CreateCommandCursor",BindingFlags.Static|BindingFlags.NonPublic);
            var sword=(Texture2D)create.Invoke(null,new object[]{BattleView.CommandCursorKind.Melee});
            try
            {
                int visible=0;foreach(var pixel in sword.GetPixels32())if(pixel.a>0)visible++;
                Require(visible>80&&visible<450,"Sword cursor has visible, bounded artwork");
                Directory.CreateDirectory("Artifacts/InfantryAssault");
                File.WriteAllBytes("Artifacts/InfantryAssault/sword-cursor.png",sword.EncodeToPNG());
            }
            finally{UnityEngine.Object.DestroyImmediate(sword);}
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }

    static void VerifyPursuitOrders()
    {
        var world=World();
        Require(world.IssueRangedAttack(0,1),"Ranged order accepted");
        world.SetPursuit(0,true);world.Battalions[1].Routing=true;world.StepMovement(.05f);
        Require(!world.IsPursuingFugitives(world.Battalions[0])&&world.Battalions[0].AttackMode==InfantryAttackMode.FirePosition&&world.Battalions[0].AttackTargetId==1,"Enabled ranged pursuit keeps shooting a target that breaks");
        world.SetPursuit(0,false);
        Require(!world.Battalions[0].Moving&&world.Battalions[0].AttackTargetId==1,"Disabling ranged pursuit holds position and retains the firing target");
        Require(world.IssueRangedAttack(0,1)&&!world.IsPursuingFugitives(world.Battalions[0]),"Explicit ranged attack on fugitives stays ranged");
        Require(world.SetMeleeMode(0,true)&&world.IsPursuingFugitives(world.Battalions[0]),"Switching to melee begins hunting the assigned fugitives");
        Require(world.Battalions[0].PursueTargets,"Explicit pursuit lights the pursuit toggle");
        world.Halt(0);world.ContactMovement.Prepare();
        Require(!world.ContactMovement.Pursuing[0],"Halt cancels individual pursuit");
        for(int i=0;i<64;i++)Require(world.ContactMovement.PursuitTargets[i]<0,"Halt clears personal targets");
    }

    static void VerifyInfantryPursuit()
    {
        var world=World();var attacker=world.Battalions[0];var defender=world.Battalions[1];
        MeleeEncounter.Place(world,attacker,Vector2.zero,0);
        MeleeEncounter.Place(world,defender,new Vector2(0,14),0);
        defender.Routing=true;world.Withdraw(1);world.IssueCharge(0,1);
        var combat=new CombatSystem(world);
        for(int tick=0;tick<600;tick++){world.StepMovement(.05f);combat.Step(.05f);}
        Require(attacker.Kills>15,"Infantry can catch fleeing infantry without cavalry support");
        Require(attacker.AliveCount==64,"Fugitives cannot counterattack their pursuers");
        Debug.Log($"INFANTRY_PURSUIT_VERIFIED kills={attacker.Kills} remaining={defender.AliveCount}");
    }

    static BattleWorld PursuitFixture()
    {
        var world=new BattleWorld(new BattleConfig {
            FrenchBattalions=1,BritishBattalions=2,FrenchCavalry=1,RidersPerSquadron=32,SoldiersPerBattalion=48,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false
        });
        for(int id=0;id<4;id++)
        {
            var unit=world.Battalions[id];
            MeleeEncounter.Place(world,unit,id<2?new Vector2(id==0?-5:5,0):new Vector2(id==2?-12:12,24),0);
            unit.FireAtWill=false;
            if(id>=2){unit.Routing=true;world.Withdraw(id);}
        }
        Require(world.IssueCharge(0,2)&&world.IssueCharge(1,2),"Both infantry and cavalry can pursue the same broken regiment");
        return world;
    }

    static void VerifyPursuit(float dt)
    {
        var world=PursuitFixture();var parallel=PursuitFixture();
        world.ContactMovement.Prepare(dt);parallel.ContactMovement.Prepare(dt);
        var unique=new HashSet<int>();var formations=new HashSet<int>();
        for(int i=0;i<80;i++)
        {
            int target=world.ContactMovement.PursuitTargets[i];
            Require(target>=80,"Each hunter chooses a fleeing enemy");
            unique.Add(target);formations.Add(world.Soldiers.BattalionIds[target]);
        }
        Require(unique.Count==80&&formations.Count==2,"Eighty hunters divide across eighty fugitives in both nearby formations");
        Require(world.ContactMovement.Loose[0]&&world.ContactMovement.Loose[1],"Infantry and cavalry both leave formation for pursuit");
        int victim=world.ContactMovement.PursuitTargets[0];
        world.KillSoldier(victim,DeathCause.Musket,Vector3.zero,Vector3.zero);
        parallel.KillSoldier(victim,DeathCause.Musket,Vector3.zero,Vector3.zero);
        world.ContactMovement.Prepare(dt);parallel.ContactMovement.Prepare(dt);
        Require(world.ContactMovement.PursuitTargets[0]!=victim,"A dead fugitive is promptly replaced");
        var combat=new CombatSystem(world);var otherCombat=new CombatSystem(parallel);
        using(var jobs=new SoldierMovementJobs(parallel))
        {
            parallel.MovementJobs=jobs;
            for(int tick=0;tick<Mathf.CeilToInt(18/dt);tick++)
            {
                world.StepMovement(dt);parallel.StepMovement(dt);combat.Step(dt);otherCombat.Step(dt);
                Require(world.DeathCount==parallel.DeathCount,"Scalar/Burst pursuit casualties agree");
                for(int i=0;i<world.Soldiers.Alive.Length;i++)if(world.Soldiers.Alive[i])
                    Require(Vector2.Distance(world.Soldiers.Positions[i],parallel.Soldiers.Positions[i])<.035f,"Scalar/Burst pursuit positions agree");
                if(tick==0)Require(world.DeathCount==1,"Distant pursuit causes no remote melee damage");
            }
            parallel.MovementJobs=null;
        }
        int kills=world.Battalions[0].Kills+world.Battalions[1].Kills;
        Require(kills>15&&world.Battalions[1].Kills>0,"Pursuers reach and kill actual fugitives");
        Require(world.Battalions[2].AliveCount<47&&world.Battalions[3].AliveCount<48,"Both fleeing formations are hunted");
        // Destroy the currently ordered group; surviving hunters seek nearby fugitives.
        var retarget=PursuitFixture();retarget.ApplyCasualties(2,48);retarget.StepMovement(dt);
        Require(retarget.Battalions[0].AttackTargetId==3&&retarget.Battalions[1].AttackTargetId==3,"A destroyed fleeing group does not stop the hunt");
        Debug.Log($"PURSUIT_VERIFIED dt={dt} uniqueInitialTargets={unique.Count} enemyFormations={formations.Count} infantryKills={world.Battalions[0].Kills} cavalryKills={world.Battalions[1].Kills}");
    }
}
