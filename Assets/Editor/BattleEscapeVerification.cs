using System;
using EmpireBattle;
using UnityEngine;

public static class BattleEscapeVerification
{
    static void Require(bool value,string message)
    {if(!value)throw new Exception("Battle escape: "+message);}

    public static void Verify()
    {
        foreach(bool jobs in new[]{false,true})
        foreach(bool cavalry in new[]{false,true})
        foreach(ArmySide side in new[]{ArmySide.French,ArmySide.British})
        foreach(bool routing in new[]{false,true})
        {
            var world=new BattleWorld(new BattleConfig {
                BattalionsPerSide=1,SoldiersPerBattalion=12,
                FrenchCavalry=cavalry?1:0,BritishCavalry=cavalry?1:0,RidersPerSquadron=12,
                FieldHalfSize=80,InitialSeparation=30,EnableAI=false,EnableLandscape=false,
                FrenchArtillery=0,BritishArtillery=0});
            var unit=Array.Find(world.Battalions,u=>u.Side==side&&u.Kind==(cavalry?UnitKind.Cavalry:UnitKind.Infantry));
            float direction=side==ArmySide.French?-1:1;
            MeleeEncounter.Place(world,unit,new Vector2(0,direction*76),direction>0?0:Mathf.PI);
            foreach(var u in world.Battalions)u.FireAtWill=false;
            world.ApplyCasualties(unit.Id,1);
            int deaths=world.DeathCount;
            unit.Routing=routing;
            // A routing unit may still have an old movement order when morale breaks.
            unit.Moving=routing;
            if(!routing)world.Withdraw(unit.Id);
            using(var movement=jobs?new SoldierMovementJobs(world):null)
            {
                world.MovementJobs=movement;
                world.StepMovement(.05f);
                Require(Mathf.Abs(unit.Destination.y)>world.Config.FieldHalfSize,"escape destination clears map edge");
                Require(unit.AliveCount>0,"soldiers remain targetable before crossing");
                Require(world.IssueCharge(Array.Find(world.Battalions,u=>u.Side!=side).Id,unit.Id),"fugitives remain valid attack targets on the field");
                bool partial=false;
                for(int tick=0;tick<1000&&unit.AliveCount>0;tick++)
                {
                    world.StepMovement(.05f);
                    partial|=unit.AliveCount>0&&unit.EscapedCount>0;
                }
                Require(partial,"soldiers leave individually rather than removing the whole regiment early");
                Require(unit.AliveCount==0&&unit.EscapedCount==11&&unit.SurvivorCount==11,"all survivors escape without becoming casualties");
                Require(world.DeathCount==deaths&&unit.Kills==0,"escape creates no corpses or kills");
                Require(!unit.Moving&&!unit.InMelee&&!unit.Reforming,"empty regiment stops simulating");
                Require(!world.IssueCharge(Array.Find(world.Battalions,u=>u.Side!=side).Id,unit.Id),"escaped regiment cannot be targeted");
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                    Require(!world.Soldiers.Alive[i],"escaped soldiers leave render and combat roster");
            }
        }
        Debug.Log("BATTLE_ESCAPE_VERIFIED infantry/cavalry, both sides, routing/withdrawal, scalar/Burst, individual departures, survivors, corpses and targeting.");
    }
}
