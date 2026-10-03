using System;
using System.Linq;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class UnitProgressionVerification
{
    static void Require(bool condition,string message){if(!condition)throw new Exception("Unit progression: "+message);}
    public static void Verify()
    {
        var veteran=new UnitProgression();veteran.Award(850);veteran.Train(UnitSkill.Accuracy);veteran.Train(UnitSkill.Drill);veteran.Train(UnitSkill.Discipline);
        var kit=new UnitLoadout();kit.Equip("extra-cartridges",EquipmentUsers.Line);kit.Equip("imperial-standard",EquipmentUsers.Line);kit.Equip("summer-uniforms",EquipmentUsers.Line);
        var config=new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=20,FrenchArtillery=4,BritishArtillery=0,
            EnableAI=false,EnableLandscape=false,FormationProgression=new[]{veteran,new UnitProgression()},FormationLoadouts=new[]{kit,new UnitLoadout()},
            FormationWeapons=new[]{"musket","musket"},FormationDoctrine=new[]{15,15},RegionalMap=true,Map=new BattleMapProfile {Aridity=1}};
        var world=new BattleWorld(config);var player=world.Battalions[0];var enemy=world.Battalions[1];
        Require(player.Progression.Rank==4&&world.Precision(player)>world.Precision(enemy)&&world.BaseReload(player)<world.BaseReload(enemy),"deployed XP and skills affect accuracy and reload");
        Require(player.Ammunition==45&&world.Soldiers.RoundsRemaining[0]==45&&player.Morale==veteran.MaxMorale+25,"extra ammo and standard reach soldier reserves and morale");
        Require(world.Resolve(player)>1.6f&&world.FatigueRate(player)<world.FatigueRate(enemy),"Imperial Standard and heat uniform bonuses are active");
        var artilleryKit=new UnitLoadout();artilleryKit.Equip("reserve-caissons",EquipmentUsers.Artillery);artilleryKit.Equip("gun-sights",EquipmentUsers.Artillery);artilleryKit.Equip("reinforced-carriages",EquipmentUsers.Artillery);
        config.ArtilleryProgression=new[]{veteran};config.ArtilleryLoadouts=new[]{artilleryKit};
        var artillery=new ArtillerySystem(world);var gun=artillery.Guns[0];
        Require(gun.Ammunition==375&&gun.Precision>1.15f&&gun.ReloadScale<.9f&&ArtillerySystem.EffectiveRange(gun)>900,"battery kit affects ammunition, sighting, reload and range");
        foreach(var piece in artillery.Guns)piece.FireAtWill=false;
        var raw=new ArtillerySystem(new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=20,FrenchArtillery=4,BritishArtillery=0,EnableLandscape=false}));
        Require(gun.ReloadScale<raw.Guns[0].ReloadScale,"experienced equipped battery services faster than recruit battery");
        var pressure=typeof(ArtillerySystem).GetMethod("ApplyDamagePressure",BindingFlags.Instance|BindingFlags.NonPublic);
        float playerMorale=player.Morale,enemyMorale=enemy.Morale;pressure.Invoke(artillery,new object[]{player,2,"Verification"});pressure.Invoke(artillery,new object[]{enemy,2,"Verification"});
        Require(playerMorale-player.Morale<enemyMorale-enemy.Morale,"standard and veteran discipline resist actual artillery morale damage");
        for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(400+i,400);
        int victim=enemy.SoldierStart;world.Soldiers.Positions[victim]=new Vector2(100,0);
        var shot=artillery.Projectiles[0];shot.Active=true;shot.GunId=0;shot.Side=ArmySide.French;shot.FromCamera=true;
        shot.Position=new Vector3(100,1,-1);shot.Velocity=new Vector3(0,0,80);shot.AmmunitionType=ArtilleryAmmunition.Canister;
        artillery.Step(.05f);Require(gun.Hits==1&&gun.Kills==1,"projectile kill credited to its actual gun");
        var sessionWorld=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=20,FrenchArtillery=4,BritishArtillery=0,EnableLandscape=false});
        var session=new BattleArmorySession(sessionWorld,new ArtillerySystem(sessionWorld),null);var entry=session.Entries[0];
        Require(session.Equip(entry,"extra-cartridges")&&sessionWorld.Battalions[0].Ammunition==45,"standalone purchase applies live prebattle loadout");
        Require(!session.Equip(entry,"reserve-caissons"),"standalone armory enforces unit applicability");
        var restored=JsonUtility.FromJson<ArmorySave>(JsonUtility.ToJson(session.Save));Require(restored.Valid&&restored.units[0].loadout.items.Contains("extra-cartridges"),"native armory JSON round trip");
        int money=session.Save.treasury;sessionWorld.Battalions[0].Kills=10;sessionWorld.Battalions[0].Hits=12;sessionWorld.Battalions[0].ShotsFired=20;
        sessionWorld.Outcome=BattleOutcome.FrenchVictory;session.Complete();int xp=entry.progression.xp;
        Require(xp>0&&session.Save.treasury==money+500,"completed battle pays XP and armory funds");
        session.Complete();Require(entry.progression.xp==xp&&session.Save.treasury==money+500,"repeated completion cannot duplicate rewards");
        bool rejected=false;try{new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,FormationProgression=new[]{new UnitProgression {xp=-1},new UnitProgression()}});}catch(ArgumentException){rejected=true;}
        Require(rejected,"invalid XP deployment rejected");
        rejected=false;try{var bad=new UnitLoadout();bad.items.Add("reserve-caissons");new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,FormationLoadouts=new[]{bad,new UnitLoadout()}});}catch(ArgumentException){rejected=true;}
        Require(rejected,"infantry cannot deploy battery-only gear");
        CampaignResearchVerification.Verify();ArtilleryAmmunitionVerification.Verify();
        Debug.Log("UNIT_PROGRESSION_VERIFIED: deployed bonuses, weapon integration, standard morale, ammunition, thermal uniforms, battery reload/range, gun kill credit, native saves and exactly-once rewards.");
    }
    public static void BuildAll()
    {
        Verify();BattlePlaytestBuild.BuildAll();
    }
}
