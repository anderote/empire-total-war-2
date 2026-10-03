using System;
using EmpireBattle;
using UnityEngine;

public static class InfantryFireModeVerification
{
    static void Require(bool value,string message)
    {if(!value)throw new Exception("Infantry fire modes: "+message);}

    public static void Verify()
    {
        VerifyDefaultsAndSelection();
        foreach(var mode in new[]{InfantryFireMode.FireAtWill,InfantryFireMode.ByPlatoon,InfantryFireMode.Volley})VerifyMorale(mode);
        for(int ranks=1;ranks<=4;ranks++)
        {
            int independent=MeasureRate(InfantryFireMode.FireAtWill,ranks);
            int platoon=MeasureRate(InfantryFireMode.ByPlatoon,ranks);
            int volley=MeasureRate(InfantryFireMode.Volley,ranks);
            Require(independent>platoon&&platoon>volley,$"Sustained rate ordering in {ranks} ranks: independent={independent}, platoon={platoon}, volley={volley}");
            Debug.Log($"FIRE_MODE_RATE ranks={ranks} seconds=180 fireAtWill={independent} platoon={platoon} volley={volley}");
        }
        InfantryLethalityVerification.Verify();
        Debug.Log("INFANTRY_FIRE_MODES_VERIFIED role defaults, three-mode cycle, legacy migration, current/mixed selection, independent formation overrides, actual morale shock, sustained rates and ranked platoon drills");
    }

    static void VerifyDefaultsAndSelection()
    {
        var roles=new[]{UnitRole.Line,UnitRole.Skirmisher,UnitRole.Rifleman,UnitRole.Militia,UnitRole.Guard,UnitRole.Line};
        var w=new BattleWorld(new BattleConfig {FrenchBattalions=5,BritishBattalions=1,SoldiersPerBattalion=64,
            FormationRoles=roles,FormationDoctrine=new int[6],FormationWeapons=new[]{"musket","musket","rifle","musket","musket","magazine"},
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        var expected=new[]{InfantryFireMode.ByPlatoon,InfantryFireMode.FireAtWill,InfantryFireMode.FireAtWill,
            InfantryFireMode.Volley,InfantryFireMode.ByPlatoon,InfantryFireMode.ByPlatoon};
        for(int i=0;i<roles.Length;i++)Require(w.Battalions[i].FireMode==expected[i],"Role default for "+roles[i]);
        var unit=w.Battalions[0];
        foreach(var next in new[]{InfantryFireMode.Volley,InfantryFireMode.FireAtWill,InfantryFireMode.ByPlatoon})
        {Require(w.NextFireMode(unit)==next,"Cycle has exactly three modes");w.SetFireMode(0,next);}
        w.SetFireMode(0,InfantryFireMode.ByRank);
        Require(unit.FireMode==InfantryFireMode.ByPlatoon,"Old rank orders migrate to platoon fire");
        unit.FireMode=InfantryFireMode.ByRank;
        var shots=new int[64];int count=RifleFireTiming.ScheduleNextGroup(w,unit,shots);
        Require(unit.FireMode==InfantryFireMode.ByPlatoon&&count>0&&count<=16,"Old saved rank drills use platoon scheduling");
        var root=new GameObject("Fire mode selection verification");
        try
        {
            var view=root.AddComponent<BattleView>();view.Initialize(w);view.SelectUnit(0,false);
            Require(view.SelectedFireMode==InfantryFireMode.ByPlatoon,"Button reports line infantry's current drill");
            view.SelectUnit(3,true);Require(!view.SelectedFireMode.HasValue,"Mixed drills are shown as mixed");
            view.OrderFireMode(InfantryFireMode.Volley);
            Require(view.SelectedFireMode==InfantryFireMode.Volley,"Button immediately follows a new selection-wide order");
            view.SelectUnit(1,false);w.Battalions[1].Formation=FormationKind.Skirmish;w.SetFireMode(1,InfantryFireMode.Volley);
            Require(view.SelectedFireMode==InfantryFireMode.FireAtWill,"Button reports actual independent skirmish fire");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }

    static BattleWorld Fixture(InfantryFireMode mode,int ranks,int attackers=250,int defenders=250)
    {
        var w=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=Mathf.Max(attackers,defenders),
            InitialStrengths=new[]{attackers,defenders},InitialSeparation=100,FrenchArtillery=0,BritishArtillery=0,
            EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        var unit=w.Battalions[0];unit.Files=Mathf.CeilToInt(attackers/(float)ranks);w.SetFireMode(0,mode);
        MeleeEncounter.Place(w,unit,Vector2.zero,0);
        MeleeEncounter.Place(w,w.Battalions[1],new Vector2(0,100),Mathf.PI);
        w.Battalions[1].FireAtWill=false;
        return w;
    }

    static int MeasureRate(InfantryFireMode mode,int ranks)
    {
        var w=Fixture(mode,ranks);var enemy=w.Battalions[1];
        // Immortal target isolates firing cadence from casualties, morale and reformation.
        for(int i=enemy.SoldierStart;i<enemy.SoldierStart+enemy.SoldierCount;i++)w.Soldiers.Health[i]=1000000;
        var combat=new CombatSystem(w);
        for(int step=0;step<3600;step++){w.StepMovement(.05f);combat.Step(.05f);}
        Require(enemy.AliveCount==enemy.SoldierCount&&!enemy.Routing,"Cadence fixture remains available for all 180 seconds");
        return w.Battalions[0].ShotsFired;
    }

    static void VerifyMorale(InfantryFireMode mode)
    {
        var w=Fixture(mode,1,128,500);var target=w.Battalions[1];
        MeleeEncounter.Place(w,target,new Vector2(0,30),Mathf.PI);
        for(int i=target.SoldierStart;i<target.SoldierStart+target.SoldierCount;i++)w.Soldiers.Health[i]=1;
        var combat=new CombatSystem(w);combat.Step(.05f);
        int losses=target.SoldierCount-target.AliveCount;
        Require(losses>0&&!target.Routing,"Morale fixture has casualties without clipping or routing");
        float expected=mode==InfantryFireMode.Volley?2.5f:mode==InfantryFireMode.ByPlatoon?1.5f:1f;
        float measured=(100-target.Morale-.18f*.05f/w.Resolve(target))/(140f*losses/target.SoldierCount/w.Resolve(target));
        Require(Mathf.Abs(measured-expected)<.001f,"Actual casualty morale impact matches "+mode);
        if(mode==InfantryFireMode.Volley)Require(losses>20,"Volley morale covers hits beyond the visual effect cap");
        Debug.Log($"FIRE_MODE_MORALE mode={mode} casualties={losses} moraleMultiplier={measured:F2}");
    }
}
