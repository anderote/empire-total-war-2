using System;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;
public static class RegimentCombatVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Regiment combat: "+message);}
    public static void BakeAndVerify(){InfantryAssetBuild.VerifyCommandGrips();InfantryAssetBuild.BakeCombatCommand();Verify();}
    public static void Verify()
    {
        PikeSergeantVerification.Verify();
        var w=World();var a=w.Battalions[0];
        Require(w.LivingCaptains(a)==4&&w.LivingSergeants(a)==8&&a.AliveCount==250,"Four captains and eight sergeants in the 250-man roster");
        foreach(var formation in new[]{FormationKind.Square,FormationKind.Column,FormationKind.Line})
        {
            w.SetFormation(a.Id,formation);
            var seen=new bool[a.AliveCount];
            for(int i=a.SoldierStart;i<a.SoldierStart+a.SoldierCount;i++)if(w.Soldiers.Alive[i])
            {
                int slot=w.Soldiers.FormationSlots[i];Require(slot>=0&&slot<a.AliveCount&&!seen[slot],"Unique formation occupancy after "+formation);seen[slot]=true;
                Require(!(w.Soldiers.Captains[i]&&w.Soldiers.PikeSergeants[i]),"Roles never overlap");
            }
        }
        int captain=Array.FindIndex(w.Soldiers.Captains,x=>x);
        w.ApplyWound(captain,100,DeathCause.Melee,Vector3.zero);w.StepMovement(.05f);w.SetFormation(a.Id,FormationKind.Square);
        Require(w.LivingCaptains(a)==3&&a.AliveCount==249,"Captain dies without decorative replacement");
        foreach(float dt in new[]{.025f,.05f,.1f})VerifySidearm(dt);
        VerifyPlatoonRanks();
        VerifySabre();
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        {
            var role=lod.Command[0];
            foreach(var poses in new[]{role.OfficerReady,role.OfficerSlash,role.OfficerFire,role.OfficerKnockdown,role.OfficerGetUp,role.OfficerDeath})
            {Require(poses!=null&&poses.Length>=12,"Officer combat poses exist");foreach(var mesh in poses){var next=new System.Collections.Generic.List<Vector3>();mesh.GetUVs(2,next);Require(next.Count==mesh.vertexCount,"Interpolated officer geometry");}}
        }
        Debug.Log("REGIMENT_COMBAT_VERIFIED captains: stable roster/casualties, short range, six-shot cylinder, finite reserves, reload, no fire through allies/downed/routing and sabre combat; pike verification passed.");
    }
    static BattleWorld World()=>new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=250,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
    static (BattleWorld world,CombatSystem combat,int captain,int target) Fixture(float range)
    {
        var w=World();var s=w.Soldiers;var a=w.Battalions[0];var b=w.Battalions[1];
        a.Position=Vector2.zero;b.Position=new Vector2(0,range);a.Ammunition=b.Ammunition=0;b.FireAtWill=false;
        for(int i=0;i<s.Alive.Length;i++){s.Positions[i]=new Vector2(1000+i*10,1000);s.Captains[i]=false;s.PikeSergeants[i]=false;}
        int captain=0,target=b.SoldierStart;s.Captains[captain]=true;s.SidearmRounds[captain]=6;s.SidearmReserves[captain]=12;
        s.Positions[captain]=Vector2.zero;s.Positions[target]=new Vector2(0,range);s.Facings[captain]=s.Facings[target]=0;s.Health[target]=10000;
        return (w,new CombatSystem(w),captain,target);
    }
    static void Time(BattleWorld w,float value)=>typeof(BattleWorld).GetProperty("Time").SetValue(w,value);
    static void VerifySidearm(float dt)
    {
        var f=Fixture(10);var w=f.world;var u=w.Battalions[0];
        for(int tick=0;tick<Mathf.FloorToInt(10/dt);tick++){Time(w,tick*dt);f.combat.Step(dt);}
        Require(u.ShotsFired==6&&w.Soldiers.SidearmRounds[f.captain]==0&&w.Soldiers.SidearmReserves[f.captain]==12,"Six-shot cylinder then reload pause at "+dt);
        Time(w,20);f.combat.Step(dt);Require(u.ShotsFired==7&&w.Soldiers.SidearmRounds[f.captain]==5&&w.Soldiers.SidearmReserves[f.captain]==6,"Reload consumes actual reserve");
        for(int tick=1;tick<Mathf.CeilToInt(55/dt);tick++){Time(w,20+tick*dt);f.combat.Step(dt);}
        Require(u.ShotsFired==18&&w.Soldiers.SidearmRounds[f.captain]==0&&w.Soldiers.SidearmReserves[f.captain]==0,"Sidearm ammunition finite");
        var distant=Fixture(25);distant.combat.Step(dt);Require(distant.world.Battalions[0].ShotsFired==0,"Revolver cannot shoot beyond close range");
        var blocked=Fixture(10);blocked.world.Soldiers.Positions[1]=new Vector2(0,4);blocked.combat.Step(dt);Require(blocked.world.Battalions[0].ShotsFired==0,"Ally blocks sidearm lane");
        var down=Fixture(10);down.world.Soldiers.KnockdownEnds[down.captain]=5;down.combat.Step(dt);Require(down.world.Battalions[0].ShotsFired==0,"Downed captain cannot shoot");
        var routed=Fixture(10);routed.world.Battalions[0].Routing=true;routed.combat.Step(dt);Require(routed.world.Battalions[0].ShotsFired==0,"Routing captain cannot shoot");
        Require(w.Soldiers.Health[f.target]<10000,"Shots wound the actual selected target");
    }
    static void VerifySabre()
    {
        var f=Fixture(1.2f);f.world.Battalions[0].FireAtWill=false;var melee=new MeleeSystem(f.world);
        var threatened=new bool[2];for(int tick=0;tick<=10;tick++){Time(f.world,tick*.05f);melee.Step(.05f,threatened);}
        Require(Mathf.Abs(10000-f.world.Soldiers.Health[f.target]-40)<.01f,"Captain deals 40 damage with timed sabre strike");
        Require(f.world.Soldiers.MeleeActions[f.captain]==MeleeAction.SlashLeft||f.world.Soldiers.MeleeActions[f.captain]==MeleeAction.SlashRight,"Captain uses sabre action");
        Require(f.world.Battalions[0].ShotsFired==0,"Sabre strike is separate from sidearm shot");
    }
    static void VerifyPlatoonRanks()
    {
        var w=World();var unit=w.Battalions[0];unit.Ammunition=1;unit.FireMode=InfantryFireMode.ByPlatoon;
        var shooters=new int[250];int total=0,groups=0;
        do{
            while(w.Time<unit.NextFireTime+.001f)w.StepMovement(Mathf.Min(.05f,unit.NextFireTime+.001f-w.Time));
            int count=RifleFireTiming.ScheduleNextGroup(w,unit,shooters);total+=count;groups++;
            Require(count>0&&groups<=16,"Platoons progress through the ranks with active command roles");
            for(int n=0;n<count;n++)Require(!w.Soldiers.PikeSergeants[shooters[n]]&&!w.Soldiers.Captains[shooters[n]],"Only muskets participate in rank drill");
        }while(unit.FireCycleActive);
        Require(total==238,"All 238 muskets can fire over kneeling command soldiers");
    }
}
