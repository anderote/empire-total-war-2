using System;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class PikeSergeantVerification
{
    public static void BakeAndVerify(){InfantryAssetBuild.BakePikeSergeants();Verify();}
    static void Require(bool pass,string message){if(!pass)throw new Exception("Pike sergeants: "+message);}
    static BattleWorld World()=>new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=250,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
    public static void Verify()
    {
        var world=World();var unit=world.Battalions[0];
        Require(world.LivingSergeants(unit)==8&&unit.AliveCount==250,"Eight actual soldiers within the regiment strength");
        foreach(var formation in new[]{FormationKind.Line,FormationKind.Column,FormationKind.Square,FormationKind.Line})
        {
            world.SetFormation(unit.Id,formation);
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)if(world.Soldiers.PikeSergeants[i])
                Require(BattleWorld.IsSergeantFrontSlot(unit,world.Soldiers.FormationSlots[i]),"Front rank after "+formation);
        }
        int sergeant=Array.FindIndex(world.Soldiers.PikeSergeants,x=>x);
        world.ApplyWound(sergeant,100,DeathCause.Melee,Vector3.zero);world.StepMovement(.05f);
        Require(world.LivingSergeants(unit)==7&&unit.AliveCount==249,"Fallen sergeant is a casualty without promotion");
        world.SetFormation(unit.Id,FormationKind.Square);world.SetFormation(unit.Id,FormationKind.Line);
        Require(world.LivingSergeants(unit)==7,"Orders never regenerate sergeants");
        var firing=World();var shooters=new int[250];var firingUnit=firing.Battalions[0];
        int count=RifleFireTiming.ScheduleVolley(firing,firingUnit,shooters);
        Require(count==238,"Eight pikes and four captains replace twelve muskets");
        for(int n=0;n<count;n++)Require(!firing.Soldiers.PikeSergeants[shooters[n]],"No sergeant in volley");
        int pike=Array.FindIndex(firing.Soldiers.PikeSergeants,x=>x);
        firing.SetFormation(firingUnit.Id,FormationKind.Skirmish);
        Require(!RifleFireTiming.ScheduleIndependent(firing,firingUnit,pike,Vector2.zero),"Pike cannot fire independently");
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            Require(Mathf.Abs(Duel(true,2.7f,dt)-51)<.01f,"Pike deals 51 damage at long reach, dt="+dt);
            Require(Duel(false,2.7f,dt)==0,"Musket bayonet cannot reach distant target");
            Require(Mathf.Abs(Duel(false,1.7f,dt)-34)<.01f,"Baseline bayonet deals 34 damage");
        }
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        {
            var role=lod.Command[3];
            foreach(var poses in new[]{role.PikeReady,role.PikeThrust,role.PikeKnockdown,role.PikeGetUp,role.PikeDeath})
            {
                Require(poses!=null&&poses.Length>=12,"Complete pike combat and casualty poses");
                foreach(var mesh in poses)Require(mesh&&mesh.vertexCount>0,"Pike geometry exists");
            }
            Require(role.PikeThrust[5].bounds.max.z>role.PikeReady[0].bounds.max.z+.2f,"Pike visibly thrusts toward target");
        }
        Debug.Log("PIKE_SERGEANTS_VERIFIED: eight persistent front-rank soldiers, casualties, formations, no firearms, 3m reach and 50% stronger thrusts at three timesteps.");
    }
    static float Duel(bool pike,float distance,float dt)
    {
        var w=World();var s=w.Soldiers;var a=w.Battalions[0];var b=w.Battalions[1];
        a.Position=Vector2.zero;b.Position=new Vector2(0,distance);a.FireAtWill=b.FireAtWill=false;
        for(int i=0;i<s.Alive.Length;i++)s.Positions[i]=new Vector2(1000+i*10,1000);
        int attacker=0,target=b.SoldierStart;
        s.PikeSergeants[attacker]=pike;s.Positions[attacker]=Vector2.zero;s.Positions[target]=new Vector2(0,distance);
        s.Facings[attacker]=s.Facings[target]=0;b.Routing=true;
        var melee=new MeleeSystem(w);var threatened=new bool[2];
        for(int tick=0;tick<=Mathf.CeilToInt(.5f/dt);tick++)
        {
            typeof(BattleWorld).GetProperty("Time").SetValue(w,tick*dt);
            melee.Step(dt,threatened);
        }
        return 100-s.Health[target];
    }
}
