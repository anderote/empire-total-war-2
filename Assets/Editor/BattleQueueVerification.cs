using System;
using System.Linq;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class BattleQueueVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message)
    { if(!value)throw new Exception("Battle queue: "+message); }
    static void Place(BattleWorld world,int id,Vector2 point)
    {
        var unit=world.Battalions[id];var delta=point-unit.Position;
        unit.Position=unit.Destination=point;unit.Facing=unit.DesiredFacing=0;unit.Moving=false;
        for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)world.Soldiers.Positions[i]+=delta;
    }
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=2,SoldiersPerBattalion=24,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,MarchSpeed=12,UseDetailedInfantry=false});
        Place(world,0,Vector2.zero);Place(world,1,new Vector2(-100,0));
        Place(world,2,new Vector2(8,100));Place(world,3,new Vector2(100,100));
        var unit=world.Battalions[0];foreach(var b in world.Battalions)b.FireAtWill=false;
        var first=new Vector2(0,12);var second=new Vector2(12,12);
        Require(world.OrderMove(0,first,0,0,false,true),"Shift on idle unit starts movement immediately");
        world.OrderMove(0,second,Mathf.PI/2,4,true,true);
        world.OrderAttack(0,2,false,false,true);
        Require(unit.Destination==first&&unit.PendingOrders.Count==2&&unit.AttackTargetId<0&&!unit.Running&&unit.Files!=4,
            "Queued move/frontage/run and attack do not alter the active move");
        var orders=world.PreviewOrders(unit).ToArray();
        Require(orders.Length==3&&orders[0].Destination==first&&orders[1].Destination==second&&orders[1].Files==4&&orders[2].TargetId==2,
            "Shared terrain/map preview includes both waypoints followed by attack");
        world.Battalions[2].Position+=new Vector2(3,0);
        Require(world.PreviewOrders(unit).Last().Destination==world.Battalions[2].Position,"Queued attack preview follows its target");
        bool reachedFirst=false,reachedSecond=false;
        for(int i=0;i<2400&&unit.AttackTargetId<0;i++)
        {
            world.StepMovement(.05f);
            if(unit.PendingOrders.Count==1)
            {
                reachedFirst=true;
                Require(unit.Destination==second&&unit.Files==4&&unit.Running,"Second move applies saved frontage and pace");
            }
            if((unit.Position-second).sqrMagnitude<.05f)reachedSecond=true;
        }
        Require(reachedFirst&&reachedSecond&&unit.AttackTargetId==2&&unit.AttackMode==InfantryAttackMode.FirePosition&&unit.PendingOrders.Count==0,
            "Simulation executes move, move, then ranged attack in order");

        world.OrderMove(0,new Vector2(20,20),0,0,false,true);
        for(int i=0;i<5;i++)world.StepMovement(.05f);
        Require(unit.AttackTargetId==2&&unit.PendingOrders.Count==1,"An active attack holds subsequent orders until target elimination");
        world.Battalions[2].AliveCount=0;world.StepMovement(.05f);
        Require(unit.AttackTargetId<0&&unit.PendingOrders.Count==0&&unit.Destination==new Vector2(20,20),"Target elimination advances queued movement before automatic retargeting");
        world.OrderAttack(0,3,true,true,true);world.Halt(0);
        Require(unit.PendingOrders.Count==0&&!unit.Moving&&unit.AttackTargetId<0,"Halt clears all orders");
        world.OrderMove(0,new Vector2(0,30),0,0,false,false);
        world.OrderAttack(0,3,true,true,true);
        world.IssueMove(0,new Vector2(0,40),0);
        Require(unit.PendingOrders.Count==0,"Normal movement replaces queue");
        world.OrderMove(0,new Vector2(0,50),0,0,false,true);world.IssueRangedAttack(0,3);
        Require(unit.PendingOrders.Count==0&&unit.AttackTargetId==3,"Normal attack replaces queue");
        world.OrderMove(0,new Vector2(0,60),0,0,false,true);world.Withdraw(0);
        Require(unit.PendingOrders.Count==0,"Withdrawal cancels queue");
        world.IssueMove(0,unit.Position,unit.Facing);world.OrderAttack(0,3,true,true,true);
        Require(unit.AttackMode==InfantryAttackMode.BayonetCharge,"Idle queued melee command starts a charge");
        world.OrderMove(0,new Vector2(0,60),0,0,false,true);unit.Routing=true;world.StepMovement(.05f);
        Require(unit.PendingOrders.Count==0,"Routing cancels queue");unit.Routing=false;world.Halt(0);

        world.OrderMove(0,unit.Position,unit.Facing,0,false,false);
        world.OrderMove(0,new Vector2(0,80),0,0,false,false);
        world.OrderAttack(0,3,false,false,true);
        world.OrderMove(0,new Vector2(0,90),0,0,false,true);
        world.Battalions[3].AliveCount=0;
        Place(world,0,new Vector2(0,80));world.StepMovement(.05f);
        Require(unit.PendingOrders.Count==0&&unit.Destination==new Vector2(0,90),"Dead queued target is skipped without losing next move");

        VerifyView(world);
        FormationTacticsVerification.VerifyDragging();
        BattleOrdersVerification.Verify();
        Debug.Log("BATTLE_QUEUE_VERIFIED: sequential moves/attack, deferred frontage/pace, live target previews, cancellation, routing, dead targets, terrain/map queue inputs and formation previews.");
    }
    static void VerifyView(BattleWorld world)
    {
        world.Halt(0);world.Halt(1);Place(world,0,Vector2.zero);Place(world,1,new Vector2(30,0));
        var root=new GameObject("Queued orders view verification");
        try
        {
            var view=root.AddComponent<BattleView>();view.Initialize(world);view.SelectUnit(0,false);view.SelectUnit(1,true);
            var type=typeof(BattleView);
            type.GetField("queueOrder",Hidden).SetValue(view,true);
            foreach(bool map in new[]{false,true})
            {
                if(map)view.ToggleAtlasView();else view.SetInspectionView(Vector2.zero,100,55);
                world.Halt(0);world.Halt(1);
                world.OrderMove(0,new Vector2(0,40),0,0,false,false);
                world.OrderMove(1,new Vector2(50,40),0,0,false,false);
                type.GetMethod("BuildPreview",Hidden).Invoke(view,new object[]{new Vector2(100,100),new Vector2(100,100),false});
                type.GetMethod("ApplyPreview",Hidden).Invoke(view,new object[]{false,false});
                Require(world.Battalions[0].PendingOrders.Count==1&&world.Battalions[1].PendingOrders.Count==1,"View appends moves in terrain and map modes");
                Require(world.Battalions[1].PendingOrders[0].Destination-world.Battalions[0].PendingOrders[0].Destination==new Vector2(50,0),
                    "Queued group moves preserve planned endpoint spacing");
                view.ReviewAtlasOrders=true;
                var preview=world.PreviewOrders(world.Battalions[0]).ToArray();
                Require(preview.Length==2&&preview[1].Destination==world.Battalions[0].PendingOrders[0].Destination,
                    "Both views receive the complete ordered preview");
                if(SystemInfo.supportsInstancing)
                    type.GetMethod(map?"RenderAtlasFormations":"RenderInfantryOrders",Hidden).Invoke(view,null);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
