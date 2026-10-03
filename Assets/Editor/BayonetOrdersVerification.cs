using System;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class BayonetOrdersVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Bayonet orders: "+message);}
    static int Trooper(BattleWorld world,BattalionState unit)
    {
        for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
            if(!world.Soldiers.Captains[i]&&!world.Soldiers.PikeSergeants[i])return i;
        throw new Exception("No regular trooper");
    }
    [MenuItem("Empire Battle/Verify bayonet orders")]
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=32,EnableAI=false,EnableLandscape=false});
        var unit=world.Battalions[0];var enemy=world.Battalions[1];
        int attacker=Trooper(world,unit),target=Trooper(world,enemy);
        Require(!unit.BayonetsFixed&&!enemy.BayonetsFixed,"infantry on both sides start with bayonets unfixed");
        float reload=world.BaseReload(unit);
        float health=world.Soldiers.Health[target];
        world.ApplyMeleeHit(attacker,target,10,Vector3.zero);
        float unfixedDamage=health-world.Soldiers.Health[target];
        world.Soldiers.Health[target]=health;
        world.Soldiers.ReloadReadyTimes[attacker]=world.Time+8;
        world.Soldiers.ReloadDurations[attacker]=10;
        Require(world.SetBayonets(unit.Id,true),"eligible infantry can fix bayonets");
        Require(Mathf.Abs(world.Soldiers.ReloadReadyTimes[attacker]-world.Time-10)<.001f,"fixing during reload preserves progress with the penalty");
        Require(Mathf.Abs(world.BaseReload(unit)/reload-1.25f)<.001f,"fixed bayonets increase reload time by 25 percent");
        world.ApplyMeleeHit(attacker,target,10,Vector3.zero);
        float fixedDamage=health-world.Soldiers.Health[target];
        Require(Mathf.Abs(fixedDamage/unfixedDamage-2)<.001f,"bayonet state doubles actual melee damage");
        for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
        {world.Soldiers.Positions[i]=new Vector2((i%5)*.2f,(i%3)*.2f);world.Soldiers.Facings[i]=0;}
        unit.Moving=unit.Reforming=false;unit.Cohesion=1;
        Require(MeleeSystem.BraceStrength(world,attacker,Vector2.up*10)>0,"fixed infantry can defend against cavalry");
        Require(world.SetBayonets(unit.Id,false),"eligible infantry can unfix bayonets");
        Require(MeleeSystem.BraceStrength(world,attacker,Vector2.up*10)==0,"unfixed muskets cannot present a bayonet wall");
        Require(Mathf.Abs(world.BaseReload(unit)-reload)<.001f,"unfixing restores normal reload speed");
        unit.Role=UnitRole.Swordsman;Require(!world.SetBayonets(unit.Id,true),"swordsmen do not get bayonet orders");
        unit.Role=UnitRole.Cavalry;unit.Kind=UnitKind.Cavalry;Require(!world.SetBayonets(unit.Id,true),"cavalry do not get bayonet orders");
        Debug.Log($"BAYONET_ORDERS_VERIFIED fixedDamage={fixedDamage:F1} unfixedDamage={unfixedDamage:F1}; reload penalty, cavalry defence and eligibility verified.");
    }
}
