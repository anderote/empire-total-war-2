using System;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;
public static class CasualtyVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=40,EnableAI=false,EnableLandscape=false});
        int emitted=0;world.SoldierDied+=d=>emitted++;
        int wounded=world.ApplyHit(0,20,1);Require(world.DeathCount==0&&world.Soldiers.Health[wounded]==80,"Nonlethal hits must not create corpses");
        int dead=world.ApplyHit(0,1000,1);var death=world.Deaths[dead];
        Require(world.DeathCount==1&&death.Cause==DeathCause.Musket&&!world.Soldiers.Alive[dead],"Musket death provenance");
        Vector3 saved=death.Position;world.IssueMove(0,new Vector2(100,0),1);
        for(int i=0;i<100;i++)world.StepMovement(.05f);
        Require(world.Deaths[dead].Position==saved,"Corpses stay at death position after regiment moves");
        world.ApplyCasualties(0,1000,DeathCause.Roundshot,new Vector3(0,2,8));
        Require(world.DeathCount==40&&emitted==40&&world.Battalions[0].AliveCount==0,"One retained corpse and event per casualty including destroyed battalions");
        world.ApplyCasualties(0,10);Require(world.DeathCount==40,"Dead soldiers cannot die twice");
        var seen=new System.Collections.Generic.HashSet<int>();for(int i=0;i<world.DeathCount;i++)Require(seen.Add(world.DeadSoldiers[i]),"Death roster contains no duplicates");
        world.ApplyCasualties(1,1,DeathCause.Shell,Vector3.zero,world.Terrain.SurfacePoint(world.Battalions[1].Position));
        var shell=world.Deaths[world.DeadSoldiers[40]];Require(shell.Cause==DeathCause.Shell&&shell.Impulse.y>0,"Shell deaths have outward rising impulse");
        var artilleryWorld=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=100,InitialSeparation=120,EnableAI=false,EnableLandscape=false});
        var artillery=new ArtillerySystem(artilleryWorld);for(int i=0;i<1200;i++){artilleryWorld.StepMovement(.05f);artillery.Step(.05f);}
        Require(artilleryWorld.DeathCount>0,"Actual cannon simulation creates death records");
        for(int i=0;i<artilleryWorld.DeathCount;i++)Require(artilleryWorld.Deaths[artilleryWorld.DeadSoldiers[i]].Cannon,"Artillery deaths retain cannon cause");
        var rigs=Resources.Load<InfantryRagdollLibrary>("BakedInfantry/InfantryRagdollLibrary");Require(rigs&&rigs.French.Mesh&&rigs.British.Mesh,"Both physical infantry assets exist");
        foreach(var set in new[]{rigs.French,rigs.British})
        {
            Require(set.Mesh.bounds.size.y>1&&set.Mesh.bounds.size.y<3,"Normalized ragdoll mesh has human scale");
            Require(set.Names.Length==set.Positions.Length&&set.Names.Length==set.Mesh.bindposes.Length,"Physical mesh skeleton contract");
            for(int i=0;i<set.Parents.Length;i++)Require(set.Parents[i]<i,"Normalized skeleton is ordered parent-first");
        }
        Require(rigs.French.Names.Length==rigs.British.Names.Length,"Faction rigs share a skeleton for pooling");
        for(int i=0;i<rigs.French.Names.Length;i++)Require(rigs.French.Names[i]==rigs.British.Names[i]&&rigs.French.Parents[i]==rigs.British.Parents[i],"Faction skeleton ordering matches");
        Debug.Log("CASUALTY_VERIFICATION_PASSED deaths="+world.DeathCount+" artillery="+artilleryWorld.DeathCount);
    }
    public static void Build()
    {
        InfantryRagdollBuild.Bake();Verify();InfantryBattleBuild.BuildMac();
    }
}
