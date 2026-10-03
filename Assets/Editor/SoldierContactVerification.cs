using System;
using EmpireBattle;
using UnityEngine;

public static class SoldierContactVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Body contact: "+message);}
    static BattleWorld World(int count=1,int units=1)=>new BattleWorld(new BattleConfig {
        BattalionsPerSide=units,SoldiersPerBattalion=count,EnableAI=false,EnableLandscape=false,
        FrenchArtillery=0,BritishArtillery=0,InitialSeparation=80 });
    public static void Verify()
    {
        bool previous=BattleOptimizationSettings.CrowdFlow;
        try
        {
            BattleOptimizationSettings.CrowdFlow=false;
        VerifyPair(false);VerifyPair(true);VerifySweep();VerifyChainSweep();VerifyCoincidence();VerifyWall();VerifyCrowd();
        VerifyMarch(false);VerifyMarch(true);VerifyParity();VerifyRest();
        VerifyMounted();VerifyBraced();VerifyReform();
        Debug.Log("SOLDIER_CONTACT_VERIFIED friendly/enemy pushing, swept crossings, coincident bodies, wall pressure, dense ranks, marching, scalar/Burst parity and resting formations");
        }
        finally {BattleOptimizationSettings.CrowdFlow=previous;}
    }
    public static void VerifyRegression()
    {
        Verify();InfantryContactVerification.Verify();InfantryPerformanceVerification.VerifyMovement();
        CavalryCombatVerification.VerifySimulation();VerifyScale();
    }
    public static void VerifyScale()
    {
        var config=BattleConfig.CreatePlaytest();config.EnableLandscape=false;config.EnableAI=false;
        var w=new BattleWorld(config);
        foreach(var u in w.Battalions)w.IssueMove(u.Id,u.Position+FormationMath.Forward(u.Facing)*20,u.Facing);
        for(int tick=0;tick<5;tick++)w.StepMovement(.05f);
        var clock=System.Diagnostics.Stopwatch.StartNew();
        for(int tick=0;tick<30;tick++)w.StepMovement(.05f);
        clock.Stop();
        Debug.Log($"BODY_CONTACT_SCALE soldiers={w.Soldiers.Alive.Length} movement_ms={clock.Elapsed.TotalMilliseconds/30:F2}");
    }
    static void VerifyMounted()
    {
        var w=new BattleWorld(new BattleConfig { BattalionsPerSide=1,SoldiersPerBattalion=1,
            FrenchCavalry=1,RidersPerSquadron=1,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0 });
        var s=w.Soldiers;s.Positions[0]=Vector2.zero;s.Positions[1]=Vector2.right*1.1f;
        w.Battalions[1].Moving=true;w.BodyContacts.Capture();s.Positions[1]-=Vector2.right*.3f;w.BodyContacts.Resolve(.05f);
        Require(Vector2.Distance(s.Positions[0],s.Positions[1])>=.939f,"Mounted body has larger clearance");
        Require(s.Positions[0].x<0,"Cavalry pushes infantry");
    }
    static void VerifyBraced()
    {
        var w=World();var s=w.Soldiers;s.Positions[0]=Vector2.zero;s.Positions[1]=Vector2.right*.65f;
        w.Battalions[0].Moving=true;w.Battalions[1].Bracing=true;
        w.BodyContacts.Capture();s.Positions[0]+=Vector2.right*.2f;w.BodyContacts.Resolve(.05f);
        Require(s.Positions[1].x-.65f<.02f,"Braced defender resists pushing");
    }
    static void VerifyPair(bool friendly)
    {
        var w=World(1,friendly?2:1);var s=w.Soldiers;
        s.Positions[0]=Vector2.zero;s.Positions[1]=Vector2.right*.65f;
        w.Battalions[0].Moving=true;w.BodyContacts.Capture();
        s.Positions[0]+=Vector2.right*.2f;w.BodyContacts.Resolve(.05f);
        Require(s.Positions[1].x>.65f,"Contact pushes the stationary "+(friendly?"ally":"enemy"));
        Require(s.Positions[1].x-s.Positions[0].x>=.579f,"Bodies stay separated");
        Require(s.MovementSpeeds[1]>0,"Pushed man has physical motion for animation");
    }
    static void VerifySweep()
    {
        var w=World();var s=w.Soldiers;
        s.Positions[0]=Vector2.left;s.Positions[1]=Vector2.right;
        w.BodyContacts.Capture();s.Positions[0]=Vector2.right;s.Positions[1]=Vector2.left;
        w.BodyContacts.Resolve(.2f);
        Require(s.Positions[0].x<s.Positions[1].x,"Fast opposing moves cannot exchange sides");
        Require(Vector2.Distance(s.Positions[0],s.Positions[1])>=.579f,"Swept body clearance");
    }
    static void VerifyChainSweep()
    {
        var w=World(8);var s=w.Soldiers;
        for(int i=0;i<8;i++)s.Positions[i]=Vector2.right*i*.62f;
        for(int i=9;i<s.Positions.Length;i++)s.Positions[i]=new Vector2(i,50);
        s.Positions[8]=Vector2.left*.62f;
        w.BodyContacts.Capture();s.Positions[8]+=Vector2.right*4f;w.BodyContacts.Resolve(.2f);
        Require(s.Positions[8].x<s.Positions[0].x,"External pusher cannot tunnel through the regiment");
        for(int i=1;i<8;i++)Require(s.Positions[i].x>s.Positions[i-1].x,"Fast external shoves cannot pass through a third body");
    }
    static void VerifyCoincidence()
    {
        var w=World();w.Soldiers.Positions[0]=w.Soldiers.Positions[1]=Vector2.zero;
        w.BodyContacts.Capture();w.BodyContacts.Resolve(.05f);
        Require(Vector2.Distance(w.Soldiers.Positions[0],w.Soldiers.Positions[1])>=.579f,"Exact coincidence resolves deterministically");
        w.Soldiers.Alive[1]=false;w.Soldiers.Positions[1]=w.Soldiers.Positions[0];
        w.BodyContacts.Capture();w.BodyContacts.Resolve(.05f);
        Require(w.Soldiers.Positions[0]==w.Soldiers.Positions[1],"Dead soldiers do not block living men");
    }
    static void VerifyWall()
    {
        var w=World();var s=w.Soldiers;float edge=w.Config.FieldHalfSize-.29f;
        s.Positions[0]=Vector2.right*(edge-.65f);s.Positions[1]=Vector2.right*edge;
        w.BodyContacts.Capture();s.Positions[0]+=Vector2.right*.3f;w.BodyContacts.Resolve(.05f);
        Require(s.Positions[1].x<=edge+.0001f,"Pushing cannot escape the field");
        Require(s.Positions[1].x-s.Positions[0].x>=.579f,"Wall transfers pressure back to pusher");
    }
    static void VerifyCrowd()
    {
        var w=World(8);var s=w.Soldiers;
        for(int i=0;i<8;i++)s.Positions[i]=new Vector2(i*.62f,0);
        for(int i=9;i<s.Positions.Length;i++)s.Positions[i]=new Vector2(i,50);
        s.Positions[8]=Vector2.left*.62f;
        w.BodyContacts.Capture();s.Positions[8]+=Vector2.right*.4f;w.BodyContacts.Resolve(.05f);
        for(int i=1;i<8;i++)Require(s.Positions[i].x>s.Positions[i-1].x,"Dense ranks preserve ordering");
        Require(s.Positions[1].x>.62f,"External pressure reaches comrades behind immediate contact");
    }
    static void Place(BattleWorld w,int id,Vector2 p,float f)
    {
        var u=w.Battalions[id];u.Position=u.Destination=p;u.Facing=u.DesiredFacing=f;
        for(int slot=0;slot<u.AliveCount;slot++)
        {int i=w.SoldierAtSlot(u,slot);w.Soldiers.Positions[i]=p+FormationMath.SlotOffset(u,slot);w.Soldiers.Facings[i]=FormationMath.SlotFacing(u,slot);}
    }
    static void VerifyMarch(bool friendly)
    {
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var w=World(8,friendly?2:1);Place(w,0,new Vector2(0,-3),0);Place(w,1,Vector2.zero,0);
            if(friendly){Place(w,2,new Vector2(50,50),0);Place(w,3,new Vector2(-50,50),0);}
            w.IssueMove(0,new Vector2(0,10),0);
            for(int t=0;t<Mathf.CeilToInt(8/dt);t++)
            {
                w.StepMovement(dt);
                for(int i=0;i<16;i++)for(int j=i+1;j<16;j++)
                    Require(Vector2.Distance(w.Soldiers.Positions[i],w.Soldiers.Positions[j])>.54f,"March body separation dt="+dt);
            }
            Debug.Log($"BODY_MARCH friendly={friendly} dt={dt} mover={w.Battalions[0].Position} blocker={w.Battalions[1].Position} destination={w.Battalions[0].Destination} moving={w.Battalions[0].Moving}");
            Require(w.Battalions[1].Position.y>.1f,"Marching formation pushes blocker dt="+dt);
            Require(w.Battalions[0].Position.y<w.Battalions[1].Position.y,"Regiment centres cannot march through blocker dt="+dt);
        }
    }
    static void VerifyParity()
    {
        var a=World(32,2);var b=World(32,2);
        foreach(var w in new[]{a,b})
        {Place(w,0,new Vector2(0,-5),0);Place(w,1,Vector2.zero,0);w.IssueMove(0,new Vector2(0,10),0);}
        using(var jobs=new SoldierMovementJobs(b))
        {
            b.MovementJobs=jobs;
            for(int t=0;t<200;t++)
            {
                a.StepMovement(.05f);b.StepMovement(.05f);
                for(int i=0;i<a.Soldiers.Positions.Length;i++)
                    Require(Vector2.Distance(a.Soldiers.Positions[i],b.Soldiers.Positions[i])<.01f,"Scalar/Burst pushing parity");
            }
            b.MovementJobs=null;
        }
    }
    static void VerifyRest()
    {
        var w=World(80);var before=(Vector2[])w.Soldiers.Positions.Clone();
        for(int t=0;t<100;t++)w.StepMovement(.05f);
        for(int i=0;i<before.Length;i++)Require(Vector2.Distance(before[i],w.Soldiers.Positions[i])<.001f,"Resting ranks do not drift or jitter");
    }
    static void VerifyReform()
    {
        var w=World(2);var u=w.Battalions[0];
        Vector2 p=w.Soldiers.Positions[0];w.Soldiers.Positions[0]=w.Soldiers.Positions[1];w.Soldiers.Positions[1]=p;
        for(int tick=0;tick<500;tick++)
        {
            w.StepMovement(.05f);
            if(!BattleOptimizationSettings.RegimentContacts)
                Require(Vector2.Distance(w.Soldiers.Positions[0],w.Soldiers.Positions[1])>.54f,"Legacy reforming men cannot cross through each other");
            else Require(w.BodyContacts.LastGridBuilds==0,"Isolated re-forming uses formation movement without body grids");
        }
        Require(!u.Reforming&&u.FormationError<.01f,"Reforming comrades settle into their slots");
    }
}
