using System;
using System.Linq;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class UnifiedReleaseChecks
{
    static void Require(bool value,string description){if(!value)throw new Exception("Unified release: "+description);}
    public static void Verify()
    {
        foreach(var scenario in BattleScenarios.All)
        {
            var config=BattleScenarios.Create(scenario.Id);config.EnableLandscape=false;
            var world=new BattleWorld(config);BattleScenarios.Deploy(world);
            var guns=new ArtillerySystem(world);BattleScenarios.AlignArtillery(world,guns);
            Require(world.CountAlive(ArmySide.French)>0&&world.CountAlive(ArmySide.British)>0,scenario.Name+" armies populated");
            Require(world.Battalions.Count(u=>u.Kind==UnitKind.Infantry&&u.Side==ArmySide.French)==config.FrenchInfantryCount,scenario.Name+" exact infantry count");
            if(scenario.Id=="infantry-skirmish")Require(config.FrenchInfantryCount==5&&config.BritishInfantryCount==5&&config.FrenchCavalry==2&&config.BritishCavalry==2,"skirmish allocation");
            if(scenario.Id=="hammer-anvil")
            {
                Require(config.BritishInfantryCount*config.SoldiersPerBattalion==10000&&config.FrenchInfantryCount==5&&guns.Guns.Length==35,"hammer allocation");
                foreach(var gun in guns.Guns)Require(Mathf.Abs(gun.Position.x-world.Battalions[gun.TargetBattalion].Position.x)<.01f,"gun aligned down column");
                new TacticalAI(world).Step(1);
                Require(world.Battalions.Where(u=>u.Side==ArmySide.British).All(u=>u.Formation==FormationKind.Column),"AI preserves attacking columns");
            }
            Debug.Log("BATTLE_SCENARIO_READY "+scenario.Id+" French="+world.CountAlive(ArmySide.French)+" British="+world.CountAlive(ArmySide.British));
        }
        var w=new BattleWorld(new BattleConfig {FrenchBattalions=2,BritishBattalions=1,FrenchCavalry=1,FrenchArtillery=4,BritishArtillery=0,SoldiersPerBattalion=20,EnableLandscape=false});
        var session=new BattleArmorySession(w,new ArtillerySystem(w),null);session.Save.year=1886;
        Require(session.Save.treasury==100000,"larger starting funds");
        Require(session.EquipMany(session.Entries,"magazine-rifles")&&session.Save.treasury==100000-430,"bulk purchase charges only eligible infantry");
        Require(w.Battalions.Take(2).All(u=>u.Weapon.Id=="magazine"&&u.FireMode==InfantryFireMode.FireAtWill),"magazine rifles default to independent fire");
        int funds=session.Save.treasury;Require(!session.EquipMany(session.Entries,"magazine-rifles")&&session.Save.treasury==funds,"already equipped units are not charged again");
        session.Save.treasury=1;Require(!session.EquipMany(session.Entries,"smokeless-rifles")&&w.Battalions[0].Weapon.Id=="magazine","insufficient funds cannot partially upgrade units");
        var unit=w.Battalions[0];
        for(int shot=0;shot<8;shot++)
        {
            w.Soldiers.RoundsRemaining[0]--;float duration=w.ReloadAfterShot(unit,0);
            Require(Mathf.Abs(duration-(shot<7?2.5f:20f))<.01f,"eight-round magazine has seven short cycles and one full refill");
        }
        w.SetFireMode(unit.Id,InfantryFireMode.ByRank);Require(!RifleFireTiming.IndependentFire(unit),"rank mode available on magazine troops");
        w.SetFireMode(unit.Id,InfantryFireMode.FireAtWill);Require(RifleFireTiming.IndependentFire(unit),"independent mode available on line infantry");
        var library=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
        Require(library&&library.French.Lods.Length==2&&library.SingleFrench.Lods.Length==2,"rifle libraries packaged at both LODs");
        Require(library.French.Lods[0].Idle[0].vertexCount>library.SingleFrench.Lods[0].Idle[0].vertexCount,"single-shot model omits magazine geometry");
        foreach(var set in new[]{library.French,library.British,library.SingleFrench,library.SingleBritish})
            Require(set.Material&&set.Lods.All(l=>l.Reload.Length==24&&l.KneelingReload.Length==24&&l.Fire.Length==8),"both factions have standing/kneeling rifle animations");
        Require(library.French.Lods[0].Reload[0].HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2),"rifle poses include GPU interpolation targets");
        Debug.Log("UNIFIED_SCENARIOS_ARMORY_RIFLES_VERIFIED seven scenarios, bulk purchase, funds, selectable firing modes, eight-round cycling and separate rifle models");
    }
}
