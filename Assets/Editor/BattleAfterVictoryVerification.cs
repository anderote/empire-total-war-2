using System;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class BattleAfterVictoryVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Battle after victory: "+message);}
    [MenuItem("Empire Battle/Verify battle after victory")]
    public static void Verify()
    {
        var config=new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=32,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false,InitialSeparation=80,FrenchArtillery=4,BritishArtillery=0};
        var world=new BattleWorld(config);var artillery=new ArtillerySystem(world);
        world.Outcome=BattleOutcome.FrenchVictory;world.PursuitUntil=20;
        for(int i=0;i<250;i++)world.StepMovement(.1f);
        Require(world.Time>24.9f&&world.CanSimulate,"clock continues beyond the old pursuit deadline");
        var unit=world.Battalions[0];var enemy=world.Battalions[1];enemy.Routing=true;
        var combat=new CombatSystem(world);
        for(int i=0;i<80;i++){world.StepMovement(.05f);combat.Step(.05f);}
        Require(combat.ShotSequence>0&&unit.ShotsFired>0,"infantry keep firing at retreating enemies after victory");
        Require(world.Outcome==BattleOutcome.FrenchVictory,"continued combat does not replace the declared result");
        var origin=world.Terrain.SurfacePoint(Vector2.zero,30);
        Require(artillery.FireCameraRoundshot(origin,world.Terrain.SurfacePoint(new Vector2(0,30))),"artillery accepts firing orders after victory");
        foreach(var gun in artillery.Guns)gun.FireAtWill=false;
        Require(artillery.OrderGround(0,new Vector2(0,30)),"ground artillery orders remain available");
        int shots=artillery.Guns[0].ShotsFired;
        for(int i=0;i<400;i++){world.StepMovement(.05f);artillery.Step(.05f);}
        Require(artillery.Guns[0].ShotsFired>shots,"artillery keeps advancing and firing after victory");
        var runtimeObject=new GameObject("After-victory exit verification");
        try
        {
            var runtime=runtimeObject.AddComponent<BattleRuntime>();runtime.Config=config;config.CampaignDeployment=true;
            typeof(BattleRuntime).GetProperty("World").SetValue(runtime,world);
            int returns=0;runtime.ReturnToCampaign=()=>{returns++;return true;};runtime.ExitBattle();
            Require(returns==1,"campaign exit uses the campaign return callback");
            world.Outcome=BattleOutcome.Ongoing;runtime.ExitBattle();
            Require(returns==1,"result exit is unavailable before an outcome");
        }
        finally{UnityEngine.Object.DestroyImmediate(runtimeObject);}
        UnitProgressionVerification.Verify();
        CampaignBattleIntegrationVerification.Verify();
        Debug.Log("BATTLE_AFTER_VICTORY_VERIFIED continuous clock, infantry and artillery fire, locked result, campaign exit callback, rewards and campaign result checks.");
    }
}
