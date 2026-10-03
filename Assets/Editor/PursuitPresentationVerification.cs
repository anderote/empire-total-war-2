using System;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class PursuitPresentationVerification
{
    static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Pursuit presentation: "+message);}
    static BattleWorld World()=>new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=64,
        EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false,FrenchArtillery=0,BritishArtillery=0});
    public static void Verify()
    {
        VerifySpeed();VerifyBayonetAnimation();VerifyVisibility();VerifyDistantBlood();
        Debug.Log("PURSUIT_PRESENTATION_VERIFIED speed bonus, moving bayonet strikes, scattered-body culling and distant melee blood");
    }
    static void VerifySpeed()
    {
        var normal=World();var hunt=World();
        foreach(var world in new[]{normal,hunt})
        {
            MeleeEncounter.Place(world,world.Battalions[0],Vector2.zero,0);
            MeleeEncounter.Place(world,world.Battalions[1],new Vector2(0,180),0);
            world.Battalions[1].Routing=world==hunt;
            Require(world.IssueCharge(0,1),"Charge/pursuit accepted");
        }
        for(int tick=0;tick<100;tick++){normal.StepMovement(.05f);hunt.StepMovement(.05f);}
        float ratio=hunt.Battalions[0].Position.magnitude/normal.Battalions[0].Position.magnitude;
        Require(ratio>1.10f&&ratio<1.14f,"Infantry hunting routed infantry has a small speed bonus: "+ratio);
        var speed=typeof(BattleWorld).GetMethod("MovementSpeed",BindingFlags.Instance|BindingFlags.NonPublic);
        float fresh=(float)speed.Invoke(hunt,new object[]{hunt.Battalions[0]});hunt.Battalions[0].Fatigue=100;
        Require((float)speed.Invoke(hunt,new object[]{hunt.Battalions[0]})<fresh*.7f,"Pursuit still respects fatigue");
        hunt.Halt(0);Require(!hunt.IsPursuingFugitives(hunt.Battalions[0]),"Halt ends the pursuit bonus");
        Debug.Log("INFANTRY_PURSUIT_SPEED_VERIFIED ratio="+ratio);
    }
    static void VerifyBayonetAnimation()
    {
        var world=World();var unit=world.Battalions[0];var target=world.Battalions[1];
        MeleeEncounter.Place(world,unit,Vector2.zero,0);MeleeEncounter.Place(world,target,new Vector2(0,14),0);
        foreach(var u in world.Battalions)u.FireAtWill=false;
        target.Routing=true;world.Withdraw(1);world.IssueCharge(0,1);
        Require(unit.BayonetsFixed,"Eligible infantry fix bayonets when hunting fugitives");
        var renderer=new CrowdInfantryRenderer(world,null);
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        var lod=library.French.Lods[0];var clip=Array.Find(lod.Melee,c=>c.Action==MeleeAction.Thrust);
        Require(clip!=null&&clip.Poses.Length>=16,"Baked bayonet thrust animation exists");
        var pose=typeof(CrowdInfantryRenderer).GetMethod("Pose",BindingFlags.Instance|BindingFlags.NonPublic);
        var clock=typeof(CrowdInfantryRenderer).GetField("clock",BindingFlags.Instance|BindingFlags.NonPublic);
        bool animated=false;var combat=new CombatSystem(world);
        for(int tick=0;tick<600;tick++)
        {
            world.StepMovement(.05f);combat.Step(.05f);
            for(int soldier=unit.SoldierStart;soldier<unit.SoldierStart+unit.SoldierCount&&!animated;soldier++)
            {
                var s=world.Soldiers;
                if(s.Captains[soldier]||s.PikeSergeants[soldier]||s.MeleeActions[soldier]!=MeleeAction.Thrust||s.MeleeEnds[soldier]<=world.Time||s.MovementSpeeds[soldier]<.08f)continue;
                clock.SetValue(renderer,s.MeleeStarts[soldier]);
                object[] args={lod,unit,true,soldier,0f};var start=(Mesh)pose.Invoke(renderer,args);
                clock.SetValue(renderer,s.MeleeStarts[soldier]+MeleeSystem.ImpactDelay(MeleeAction.Thrust));
                var contact=(Mesh)pose.Invoke(renderer,args);
                Require(Array.IndexOf(clip.Poses,start)>=0&&Array.IndexOf(clip.Poses,contact)>=0&&start!=contact,"A moving hunter plays the bayonet lunge through the contact pose");
                animated=true;
            }
        }
        renderer.Dispose();
        Require(animated&&unit.Kills>15&&target.AliveCount==64-unit.Kills,"Animated bayonet hunters catch and kill actual fugitives");
        Debug.Log("INFANTRY_PURSUIT_ANIMATION_VERIFIED kills="+unit.Kills);
    }
    static void VerifyVisibility()
    {
        var host=new GameObject("Scattered infantry culling verification");
        try
        {
            var camera=host.AddComponent<Camera>();camera.transform.position=new Vector3(0,12,-20);
            camera.transform.LookAt(Vector3.zero);camera.aspect=1;camera.farClipPlane=500;
            var planes=GeometryUtility.CalculateFrustumPlanes(camera);
            foreach(bool routing in new[]{false,true})
            {
                var world=World();var unit=world.Battalions[0];unit.Position=unit.Destination=new Vector2(1000,0);
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)world.Soldiers.Positions[i]=unit.Position;
                int visible=unit.SoldierStart+5;world.Soldiers.Positions[visible]=Vector2.zero;
                unit.Routing=routing;if(!routing){world.Battalions[1].Routing=true;world.IssueCharge(0,1);}
                Require(!GeometryUtility.TestPlanesAABB(planes,new Bounds(world.Terrain.SurfacePoint(unit.Position,5),new Vector3(100,60,100))),"Regiment center is outside the camera");
                Require(GeometryUtility.TestPlanesAABB(planes,BattleRenderBounds.Regiment(world,unit,world.Time)),"A visible hunter or fugitive keeps the scattered regiment drawable");
                world.Halt(0);Require(GeometryUtility.TestPlanesAABB(planes,BattleRenderBounds.Regiment(world,unit,world.Time)),"Scattered soldiers remain visible when pursuit stops");
                world.KillSoldier(visible,DeathCause.Musket,Vector3.zero,Vector3.zero);
                Require(!GeometryUtility.TestPlanesAABB(planes,BattleRenderBounds.Regiment(world,unit,world.Time)),"An entirely offscreen living regiment is still culled");
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(host);}
        Debug.Log("SCATTERED_INFANTRY_VISIBILITY_VERIFIED pursuit, routing, halt and offscreen exclusion");
    }
    static void VerifyDistantBlood()
    {
        foreach(float distance in new[]{700f,900f})
        {
            var world=World();var host=new GameObject("Distant blood verification");var eye=new GameObject("Blood camera");
            try
            {
                var camera=eye.AddComponent<Camera>();camera.transform.position=new Vector3(0,5,-distance);
                var blood=host.AddComponent<BattleBlood>();blood.Initialize(world,camera,null);
                int attacker=world.Battalions[0].SoldierStart+5,target=world.Battalions[1].SoldierStart+5;
                world.Soldiers.Positions[attacker]=world.Soldiers.Positions[target]=Vector2.zero;
                world.RecordMeleeContact(attacker,target,MeleeAction.Thrust,MeleeContactOutcome.Hit);
                blood.Step(0,world.Time);
                Require(distance<BattleBlood.SprayDrawDistance?blood.MeleeSpraysEmitted==1&&blood.ActiveDroplets>0:blood.MeleeSpraysEmitted==0,"Distant melee blood is emitted within the expanded spray range and bounded beyond it");
            }
            finally{UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(eye);}
        }
        Debug.Log("DISTANT_MELEE_BLOOD_VERIFIED sprays visible at 700m with bounded emission outside 800m");
    }
}
