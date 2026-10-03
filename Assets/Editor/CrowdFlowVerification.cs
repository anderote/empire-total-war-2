using System;
using EmpireBattle;
using UnityEngine;

public static class CrowdFlowVerification
{
    static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Crowd flow: "+message);}
    public static void Verify()
    {
        bool previous=BattleOptimizationSettings.CrowdFlow;
        try
        {
            BattleOptimizationSettings.CrowdFlow=true;
            VerifyRest();VerifyCrossing(false);VerifyCrossing(true);VerifyBounds();
        }
        finally {BattleOptimizationSettings.CrowdFlow=previous;}
        Debug.Log("CROWD_FLOW_VERIFIED isolated ranks, reciprocal/perpendicular passing, scalar/Burst parity, bounded corrections and field/terrain limits");
    }
    static BattleWorld World(int count=128)=>new BattleWorld(new BattleConfig{
        BattalionsPerSide=2,SoldiersPerBattalion=count,EnableAI=false,EnableLandscape=false,
        FrenchArtillery=0,BritishArtillery=0,InitialSeparation=600});
    static void VerifyRest()
    {
        var world=World();var start=(Vector2[])world.Soldiers.Positions.Clone();
        for(int tick=0;tick<100;tick++)
        {
            world.StepMovement(.05f);
            Require(world.BodyContacts.LastGridBuilds==0,"Separated ranks do no contact grid work");
        }
        for(int i=0;i<start.Length;i++)Require(Vector2.Distance(start[i],world.Soldiers.Positions[i])<.001f,"Resting troops do not drift");
    }
    static void VerifyCrossing(bool perpendicular)
    {
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var scalar=World();var burst=World();
            foreach(var world in new[]{scalar,burst})
            {
                MeleeEncounter.Place(world,world.Battalions[0],new Vector2(0,-12),0);
                MeleeEncounter.Place(world,world.Battalions[1],perpendicular?new Vector2(-12,0):new Vector2(0,12),perpendicular?Mathf.PI*.5f:Mathf.PI);
                world.IssueMove(0,new Vector2(0,24),0);
                world.IssueMove(1,perpendicular?new Vector2(24,0):new Vector2(0,-24),perpendicular?Mathf.PI*.5f:Mathf.PI);
            }
            float maximumPush=0;int contactTicks=0;
            using(var jobs=new SoldierMovementJobs(burst))
            {
                burst.MovementJobs=jobs;
                for(int tick=0;tick<Mathf.CeilToInt(35/dt);tick++)
                {
                    scalar.StepMovement(dt);burst.StepMovement(dt);
                    if(scalar.BodyContacts.LastGridBuilds>0)contactTicks++;
                    Require(scalar.BodyContacts.LastGridBuilds<=1,"Contact work never repeats a grid build");
                    for(int i=0;i<scalar.Soldiers.Positions.Length;i++)
                    {
                        float push=scalar.BodyContacts.ContactDisplacement(i).magnitude;
                        maximumPush=Mathf.Max(maximumPush,push);
                        Require(push<=dt*.701f,"Flow correction stays below 0.7 m/s");
                        Require(Vector2.Distance(scalar.Soldiers.Positions[i],burst.Soldiers.Positions[i])<.035f,"Crossing scalar/Burst parity");
                    }
                }
                burst.MovementJobs=null;
            }
            Require(contactTicks>0&&maximumPush>dt*.02f,"Crossing activates visible smooth steering");
            Require(scalar.Battalions[0].Position.y>18,"First formation passes through congestion");
            Require(perpendicular?scalar.Battalions[1].Position.x>18:scalar.Battalions[1].Position.y< -18,"Second formation passes through congestion");
            Require(!scalar.Battalions[0].Moving&&!scalar.Battalions[1].Moving,"Both formations reach their destinations");
            Debug.Log($"CROWD_FLOW_PASSING perpendicular={perpendicular} dt={dt} contactTicks={contactTicks} maxPush={maximumPush:F4} final={scalar.Battalions[0].Position}/{scalar.Battalions[1].Position}");
        }
    }
    static void VerifyBounds()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=2,SoldiersPerBattalion=64,
            EnableAI=false,EnableLandscape=true,FrenchArtillery=0,BritishArtillery=0});
        float edge=world.Config.FieldHalfSize-.3f;
        for(int i=0;i<128;i++)world.Soldiers.Positions[i]=new Vector2(edge-i%8*.08f,i%3*.08f);
        world.BodyContacts.Capture();world.BodyContacts.Resolve(.1f);
        for(int i=0;i<128;i++)
        {
            var p=world.Soldiers.Positions[i];
            Require(!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&p.x<=edge+.011f,"Dense pressure remains finite and inside the field");
            if(world.BodyContacts.ContactDisplacement(i).sqrMagnitude>0)
                Require(!world.Terrain.IsBlocked(p,.29f),"Flow never pushes a soldier into blocked terrain");
        }
    }
}
