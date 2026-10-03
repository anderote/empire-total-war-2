using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class SlowMotionEffectsVerification
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Require(bool value,string message) { if(!value)throw new Exception(message); }
    static int Drops(BattleBlood blood, bool deposits=false)
    {
        int count=0;
        foreach(object drop in (Array)typeof(BattleBlood).GetField("drops",Private).GetValue(blood))
            if((bool)drop.GetType().GetField(deposits?"Deposit":"Active").GetValue(drop))count++;
        return count;
    }
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig { BattalionsPerSide=1,SoldiersPerBattalion=16,EnableLandscape=false });
        var root=new GameObject("Slow motion effect verification");
        var cameraRoot=new GameObject("Effect camera");
        try
        {
            var view=root.AddComponent<BattleView>();
            var camera=cameraRoot.AddComponent<Camera>();
            typeof(BattleView).GetField("battleCamera",Private).SetValue(view,camera);
            float speed=1f;view.GetSpeed=()=>speed;
            foreach(float value in new[]{1f,2f,4f})Require(SlowMotionEffects.Strength(value)==0,"Normal/fast playback boosted effects");
            Require(SlowMotionEffects.Strength(.5f)<SlowMotionEffects.Strength(.25f)&&SlowMotionEffects.Strength(.25f)<SlowMotionEffects.Strength(.1f),"Boost must increase as playback slows");
            var normal=root.AddComponent<BattleBlood>();normal.Initialize(world,camera,null);
            normal.EmitSpray(Vector3.up,Vector3.forward,false,0);
            Require(Drops(normal)==56&&Drops(normal,true)==8,"Normal blood emission changed");
            var cannon=root.AddComponent<BattleBlood>();cannon.Initialize(world,camera,null);
            cannon.EmitSpray(Vector3.up,Vector3.forward,true,0);
            Require(Drops(cannon)==112&&Drops(cannon,true)==18,"Cannon spray is denser without extra permanent deposits");
            normal.Emit(new SoldierDeath{Soldier=0,Position=Vector3.zero},0);
            var marks=(System.Collections.IList)typeof(BattleBlood).GetField("marks",Private).GetValue(normal);
            float poolSize=(float)marks[0].GetType().GetField("Size").GetValue(marks[0]);
            Require(marks.Count==1&&poolSize>=.49f&&poolSize<=.91f,"Larger corpse pool retains one mark per death");
            speed=.1f;
            var boosted=root.AddComponent<BattleBlood>();boosted.Initialize(world,camera,null);
            boosted.EmitSpray(Vector3.up,Vector3.forward,false,0);
            Require(Drops(boosted)==123&&Drops(boosted,true)==8,"Boost must add droplets without multiplying permanent deposits");
            for(int i=0;i<100;i++)boosted.EmitSpray(Vector3.up,Vector3.forward,true,0);
            Require(Drops(boosted)==4096,"Blood pool must remain bounded under repeated slow-motion hits");
            Require(SlowMotionEffects.Amount(view,Vector3.forward*451)==0,"Distant effects should not receive the boost");
            var impact=new ArtilleryImpact { ExplosiveShell=true,AmmunitionType=ArtilleryAmmunition.Explosive,TerrainStrike=true,Position=Vector3.zero,Direction=Vector3.down,Speed=150,Sequence=1 };
            var debris=new ArtilleryDebris(world,null,null,null,null);debris.Emit(impact,1);
            Require((int)typeof(ArtilleryDebris).GetField("cursor",Private).GetValue(debris)==112,"Slow-motion shell should emit 112 bounded clods");
            Require(world.Time==0&&world.DeathCount==0&&world.CountAlive(ArmySide.French)==16,"Presentation changed combat state");
            speed=1;
            Require(SlowMotionEffects.Amount(view,Vector3.up)==0,"Returning to normal must remove current visual boost");
            Debug.Log("SLOW_MOTION_EFFECTS_VERIFIED: graded playback boost, 56->123 blood droplets, fixed deposits/pools, 64->112 debris, distant falloff, combat unchanged");
        }
        finally { UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraRoot); }
    }
    public static void VerifyAndBuild()
    {
        Verify();
        MusketSmokeVerification.Verify();
        BattlePlaytestBuild.BuildAll();
    }
    public static void VerifyBloodProfile()
    {
        Verify();
        MeleePresentationVerification.Verify();
    }
}
