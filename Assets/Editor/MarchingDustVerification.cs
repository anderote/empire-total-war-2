using System;
using System.Reflection;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class MarchingDustVerification
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception("Marching dust: "+message);}
    struct Sample { public int Count;public float Opacity,Width; }

    static Sample Review(float speed,Vector2 position,bool checkPause=false)
    {
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=64,EnableAI=false});
        var root=new GameObject("March dust verification");
        var cameraObject=new GameObject("March dust camera");
        try
        {
            var camera=cameraObject.AddComponent<Camera>();
            camera.transform.position=world.Terrain.SurfacePoint(position)+Vector3.up*40;
            var smoke=root.AddComponent<MusketSmoke>();smoke.Initialize(world,camera);
            var unit=world.Battalions[0];unit.Position=position;unit.ActualSpeed=speed;
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
            {world.Soldiers.Positions[i]=position;world.Soldiers.MovementSpeeds[i]=speed;world.Soldiers.Velocities[i]=Vector2.up*speed;}
            var emit=typeof(MusketSmoke).GetMethod("EmitMarchingDust",Private);
            for(int tick=1;tick<=20;tick++)emit.Invoke(smoke,new object[]{tick*.05f});
            int count=smoke.MarchDustEmissions;
            if(checkPause)
            {
                emit.Invoke(smoke,new object[]{1f});
                Require(smoke.MarchDustEmissions==count,"paused presentation emits no dust");
                for(int i=0;i<world.Soldiers.MovementSpeeds.Length;i++)world.Soldiers.MovementSpeeds[i]=0;
                unit.ActualSpeed=0;
                for(int tick=21;tick<=40;tick++)emit.Invoke(smoke,new object[]{tick*.05f});
                Require(smoke.MarchDustEmissions==count,"halted troops emit no dust");
            }
            var puffs=(Array)typeof(MusketSmoke).GetField("puffs",Private).GetValue(smoke);
            float opacity=0,width=0;int sampled=0;
            foreach(var puff in puffs)
            {
                var type=puff.GetType();
                if(!(bool)type.GetField("MarchDust").GetValue(puff))continue;
                opacity+=(float)type.GetField("Opacity").GetValue(puff);
                width+=(float)type.GetField("Width").GetValue(puff);sampled++;
            }
            return new Sample {Count=count,Opacity=sampled>0?opacity/sampled:0,Width=sampled>0?width/sampled:0};
        }
        finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);}
    }

    [MenuItem("Empire Battle/Verify marching dust")]
    public static void Verify()
    {
        var grass=new Vector2(-500,0);var dirt=new Vector2(RuralFieldLayout.LaneX(0),0);
        var idle=Review(0,grass);var walk=Review(1.2f,grass,true);
        var run=Review(3f,grass);var road=Review(1.2f,dirt);
        Require(idle.Count==0,"stationary troops emit no dust");
        Require(walk.Count>0,"marching troops emit dust");
        Require(run.Count>walk.Count&&run.Opacity>walk.Opacity&&run.Width>walk.Width,"running increases frequency, density and size");
        Require(road.Opacity>walk.Opacity&&road.Width>walk.Width,"bare dirt produces stronger dust than grass");
        Require(run.Count<=20*64,"emissions stay bounded");
        Debug.Log($"MARCHING_DUST_VERIFIED idle={idle.Count} walk={walk.Count} run={run.Count} grassOpacity={walk.Opacity:F3} dirtOpacity={road.Opacity:F3}; pause and halt verified.");
    }
}
