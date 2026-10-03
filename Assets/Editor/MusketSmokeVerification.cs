using System;
using System.Reflection;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class MusketSmokeVerification
{
    [MenuItem("Empire Battle/Verify per-rifle smoke")]
    public static void Verify()
    {
        bool previousOptimization=BattleOptimizationSettings.RenderWorkOptimizations;
        var world = new BattleWorld(new BattleConfig {
            BattalionsPerSide = 40, SoldiersPerBattalion = 500, EnableLandscape = false });
        var root = new GameObject("Smoke verification");
        var cameraRoot = new GameObject("Smoke verification camera");
        try
        {
            var smoke = root.AddComponent<MusketSmoke>();
            smoke.Initialize(world, cameraRoot.AddComponent<Camera>());
            var emit = typeof(MusketSmoke).GetMethod("EmitVolley", BindingFlags.Instance | BindingFlags.NonPublic);
            var buffer = typeof(MusketSmoke).GetField("puffs", BindingFlags.Instance | BindingFlags.NonPublic);
            int scheduled=0;
            // Spread the frontage so every rifle fires, stressing 40,000 simultaneous shots.
            foreach (var unit in world.Battalions)
            {
                unit.Files = unit.SoldierCount;
                scheduled+=RifleFireTiming.ScheduleVolley(world,unit,new int[unit.SoldierCount]);
                unit.LastVolleyTime = world.Time;
                emit.Invoke(smoke, new object[] { unit });
            }
            var initialPuffs=(Array)buffer.GetValue(smoke);
            int persistent=0;
            foreach(object puff in initialPuffs)
            {
                var type=puff.GetType();
                if(!(bool)type.GetField("PersistentPowder").GetValue(puff))continue;
                persistent++;
                if((float)type.GetField("Lifetime").GetValue(puff)<80f)
                    throw new Exception("Merged musket powder must linger for at least eighty seconds.");
            }
            if(persistent==0)throw new Exception("Volley did not produce lingering powder clouds.");
            // More than the entire persistent pool: cannon clouds must not erase gun smoke.
            for (int i = 0; i < 400; i++)
                smoke.EmitCloud(Vector3.zero, Vector3.up, 2f, Color.white, 128);
            var puffs = (Array)buffer.GetValue(smoke);
            int cloudCapacity = puffs.Length - world.Soldiers.Alive.Length * 2;
            int protectedShooters=0;
            for (int soldier = 0; soldier < world.Soldiers.Alive.Length; soldier++)
            {
                // Nonfiring command roles and misfires have no scheduled gun smoke.
                if(world.Soldiers.FireVolleys[soldier]!=world.Time)continue;
                protectedShooters++;
                object muzzle = puffs.GetValue(cloudCapacity + soldier * 2);
                object gun = puffs.GetValue(cloudCapacity + soldier * 2 + 1);
                var type = muzzle.GetType();
                if (!(bool)type.GetField("Alive").GetValue(muzzle) || !(bool)type.GetField("GunPuff").GetValue(muzzle)
                    || (!world.Soldiers.Captains[soldier] && (!(bool)type.GetField("Alive").GetValue(gun) || !(bool)type.GetField("GunPuff").GetValue(gun))))
                    throw new Exception("Missing protected gun smoke for soldier " + soldier);
                // Captains fire sidearms, which have muzzle smoke but no rifle lock puff.
                if(world.Soldiers.Captains[soldier])continue;
                if ((float)type.GetField("Width").GetValue(muzzle) <= (float)type.GetField("Width").GetValue(gun)
                    || (float)type.GetField("Opacity").GetValue(muzzle) <= (float)type.GetField("Opacity").GetValue(gun))
                    throw new Exception("Muzzle must dominate lock smoke for soldier " + soldier);
            }
            if(protectedShooters!=scheduled||scheduled<35000)throw new Exception("Smoke saturation did not cover every scheduled rifle shot.");
            // Rendering clock checks run outside the view, avoiding graphics calls in batch mode.
            cameraRoot.transform.position=new Vector3(0,0,-1000000f);
            float presentation=-.1f;
            smoke.PresentationTime=()=>presentation;
            var tick=typeof(MusketSmoke).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
            tick.Invoke(smoke,null);
            if(smoke.ActivePuffs!=0)throw new Exception("Future-dated shots appeared early.");
            object firstMuzzle=puffs.GetValue(cloudCapacity);
            if(!(bool)firstMuzzle.GetType().GetField("Alive").GetValue(firstMuzzle))
                throw new Exception("Scheduled gun smoke was discarded before its firing time.");
            presentation=6f;tick.Invoke(smoke,null);
            firstMuzzle=puffs.GetValue(cloudCapacity);
            if(!(bool)firstMuzzle.GetType().GetField("Alive").GetValue(firstMuzzle))
                throw new Exception("Musket muzzle smoke should still linger after six seconds.");
            presentation=1f;tick.Invoke(smoke,null);
            int active=smoke.ActivePuffs;
            var centerField=typeof(MusketSmoke).GetField("centers",BindingFlags.Instance|BindingFlags.NonPublic);
            var before=(Vector3[])((Vector3[])centerField.GetValue(smoke)).Clone();
            world.StepMovement(.25f);tick.Invoke(smoke,null);
            BattleOptimizationSettings.RenderWorkOptimizations=false;
            tick.Invoke(smoke,null);
            var reference=(Vector3[])centerField.GetValue(smoke);
            for(int i=0;i<before.Length;i++)if(before[i]!=reference[i])throw new Exception("Cached smoke gust differs from reference wind.");
            BattleOptimizationSettings.RenderWorkOptimizations=true;
            tick.Invoke(smoke,null);
            var after=(Vector3[])centerField.GetValue(smoke);
            if(active!=smoke.ActivePuffs)throw new Exception("A frozen presentation clock changed smoke lifetime.");
            for(int i=0;i<before.Length;i++)if(before[i]!=after[i])throw new Exception("A frozen presentation clock moved smoke.");
            if(smoke.WindDisplacement(1f,1f)!=Vector3.zero)throw new Exception("New smoke has a discontinuous wind offset.");
            presentation=1000f;tick.Invoke(smoke,null);
            if(smoke.TrackedPuffs!=0 || smoke.ActivePuffs!=0)throw new Exception("Expired smoke retained active-list entries.");
            smoke.EmitCloud(Vector3.zero,Vector3.up,2f,Color.white,128);
            if(smoke.TrackedPuffs!=128)throw new Exception("Retired smoke slots did not rejoin the active list exactly once.");
            presentation=world.Time;tick.Invoke(smoke,null);
            if(smoke.ActivePuffs!=128)throw new Exception("Refilled smoke slots were skipped after swap removal.");
            var first = world.Battalions[0];
            first.Formation = FormationKind.Column;
            first.Files = 8;
            if (FormationMath.FiringSoldierCount(first) != 16)
                throw new Exception("Column firing ranks differ from combat.");
            world.ApplyCasualties(first.Id, first.AliveCount - 3);
            if (FormationMath.FiringSoldierCount(first) != 3)
                throw new Exception("Firing count exceeds survivors.");
            Debug.Log($"MUSKET_SMOKE_VERIFIED: {scheduled} scheduled rifles across 40,000 personnel retain protected muzzle/lock puffs; cached gust parity, narrow formations, scheduled shots and paused smoke verified.");
        }
        finally
        {
            BattleOptimizationSettings.RenderWorkOptimizations=previousOptimization;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(cameraRoot);
        }
    }
}
