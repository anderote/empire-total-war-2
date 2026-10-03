using System;
using System.Collections;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class ArtilleryBloodVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception("Artillery/blood: "+message);}
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        var root=new GameObject("Blood verification");var cameraRoot=new GameObject("Blood camera");
        ArtilleryGore gore=null;
        try
        {
            var camera=cameraRoot.AddComponent<Camera>();
            var blood=root.AddComponent<BattleBlood>();blood.Initialize(world,camera,null);
            world.ApplyHit(1,10,0);blood.Step(0,0);
            Require(blood.MusketWoundSprays==1&&blood.ActiveDroplets>0,"Nonfatal line musket hit emits spurt");
            Require(blood.PermanentMarks==0,"Living wound has no corpse pool");
            int target=world.Battalions[1].SoldierStart;
            world.ApplyWound(target,10,DeathCause.Musket,world.SoldierPosition(target,0),Vector3.right);
            Require(blood.MusketWoundSprays==2,"Targeted musket wound emits spurt");
            int deaths=0;world.SoldierDied+=d=>{deaths++;blood.Emit(d,0);};
            world.ApplyWound(target,1000,DeathCause.Musket,world.SoldierPosition(target,0),Vector3.right);
            Require(deaths==1&&blood.MusketWoundSprays==2&&blood.PermanentMarks==1,"Fatal bullet emits one corpse pool without duplicate wound spray");
            blood.Step(0,2);
            var marks=(IList)typeof(BattleBlood).GetField("marks",Hidden).GetValue(blood);
            object pool=marks[0];
            Require(!(bool)pool.GetType().GetField("Waiting").GetValue(pool)&&(bool)pool.GetType().GetField("Pool").GetValue(pool),"Bullet pool activates after fall");
            for(int i=0;i<180;i++)blood.Step(1f/60f,2+i/60f);
            Require(blood.PermanentMarks>1,"Bullet droplets deposit permanent spatter");
            var rigs=Resources.Load<InfantryRagdollLibrary>("BakedInfantry/InfantryRagdollLibrary");
            Require(rigs&&rigs.French.Mesh&&rigs.British.Mesh,"Physical infantry assets present");
            gore=new ArtilleryGore(world,camera,rigs);
            foreach(var set in new[]{rigs.French,rigs.British})
            {
                var skinRoot=new GameObject("Fragment source");skinRoot.transform.SetParent(root.transform);
                var bones=new Transform[set.Names.Length];
                for(int i=0;i<bones.Length;i++)
                {
                    bones[i]=new GameObject(set.Names[i]).transform;bones[i].SetParent(set.Parents[i]>=0?bones[set.Parents[i]]:skinRoot.transform);
                    bones[i].SetPositionAndRotation(set.Positions[i],set.Rotations[i]);
                }
                var skin=skinRoot.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=set.Mesh;skin.bones=bones;skin.rootBone=bones[0];
                int before=gore.SeveredLimbs;
                var death=new SoldierDeath{Soldier=target,Cause=DeathCause.Shell,Impulse=new Vector3(9,6,0)};
                Require(gore.Sever(death,skin,set)&&skin.sharedMesh.vertexCount==0,"Strong shell completely removes original body");
                Require(gore.SeveredLimbs-before==6,"Shell creates head, four limbs and torso");
                var pieces=(IList)typeof(ArtilleryGore).GetField("pieces",Hidden).GetValue(gore);
                int count=0;
                for(int i=before;i<pieces.Count;i++)
                {
                    var piece=pieces[i];var mesh=(Mesh)piece.GetType().GetField("Mesh").GetValue(piece);
                    count+=mesh.GetTriangles(0).Length;Require(mesh.GetTriangles(1).Length>0,"Each fragment has wound cap");
                }
                Require(count==set.Mesh.triangles.Length,"All original triangles retained exactly once across fragments");
                skin.sharedMesh=set.Mesh;
                Require(!gore.Sever(new SoldierDeath{Soldier=target,Cause=DeathCause.Musket,Impulse=death.Impulse},skin,set)&&skin.sharedMesh==set.Mesh,"Bullet death keeps complete mesh");
            }
            int trails=blood.FragmentTrailDrops,stains=blood.PermanentMarks;
            gore.Step(0,blood,5);Require(blood.FragmentTrailDrops==trails,"Pause stops trails");
            for(int i=0;i<420;i++){gore.Step(1f/60f,blood,5+i/60f);blood.Step(1f/60f,5+i/60f);}
            Require(blood.FragmentTrailDrops>trails&&blood.PermanentMarks>stains,"Flying fragments shed blood and leave landing stains");
            trails=blood.FragmentTrailDrops;gore.Step(1,blood,13);
            Require(blood.FragmentTrailDrops==trails,"Settled fragments stop bleeding trails");
            var casualtyRoot=new GameObject("Full fragment casualty integration");
            try
            {
                var integrationWorld=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
                var casualties=casualtyRoot.AddComponent<BattleCasualtyView>();
                // Exercise the real death handler in edit mode; local physics scenes require play mode.
                void Set(string name,object value)=>typeof(BattleCasualtyView).GetField(name,Hidden).SetValue(casualties,value);
                int capacity=integrationWorld.Soldiers.Alive.Length;
                Set("world",integrationWorld);Set("cameraView",camera);Set("rigs",rigs);
                Set("crowd",Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary"));
                Set("physicsScene",UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                Set("frozen",new Mesh[capacity]);Set("physical",new bool[capacity]);Set("anchors",new Vector3[capacity]);
                Set("flightEnds",new Vector3[capacity]);Set("flightTimes",new float[capacity]);
                Set("gore",new ArtilleryGore(integrationWorld,camera,rigs));
                var integratedBlood=casualtyRoot.AddComponent<BattleBlood>();integratedBlood.Initialize(integrationWorld,camera,casualties);Set("blood",integratedBlood);
                var handler=typeof(BattleCasualtyView).GetMethod("OnDeath",Hidden);
                Vector3 recordedImpulse=Vector3.zero;
                integrationWorld.SoldierDied+=d=>{recordedImpulse=d.Impulse;handler.Invoke(casualties,new object[]{d});};
                int victim=integrationWorld.Battalions[1].SoldierStart;
                while(integrationWorld.Soldiers.Captains[victim]||integrationWorld.Soldiers.PikeSergeants[victim])victim++;
                camera.transform.position=integrationWorld.SoldierPosition(victim,0)+Vector3.up*10;
                Require(integrationWorld.ApplyArtilleryHit(victim,1000,DeathCause.Shell,new Vector3(9,6,0),Vector3.zero),"Integrated shell kills victim");
                Require((recordedImpulse-new Vector3(9,6,0)).sqrMagnitude<.0001f,"Shell death preserves supplied blast impulse");
                Require(casualties.FragmentedBodies==1&&casualties.SeveredLimbs==6&&casualties.ActiveRagdolls==0&&casualties.FrozenRagdolls==1,"Full fragmentation suppresses intact corpse and frees physics slot immediately");
                victim++;
                while(integrationWorld.Soldiers.Captains[victim]||integrationWorld.Soldiers.PikeSergeants[victim])victim++;
                integrationWorld.ApplyArtilleryHit(victim,1000,DeathCause.Roundshot,new Vector3(2,1,0),Vector3.zero);
                Require(casualties.ActiveRagdolls==1&&casualties.FragmentedBodies==1,"Reused slot restores intact mesh for ordinary cannon death");
                var ragPool=(IList)typeof(BattleCasualtyView).GetField("pool",Hidden).GetValue(casualties);
                foreach(var rag in ragPool)
                {
                    var rigRoot=(GameObject)rag.GetType().GetField("Root").GetValue(rag);rigRoot.transform.SetParent(casualtyRoot.transform);
                    var skin=(SkinnedMeshRenderer)rag.GetType().GetField("Skin").GetValue(rag);
                    Require(skin.sharedMesh.vertexCount>0,"Physics slot mesh restored after full breakup");
                }
                Set("physicsScene",default(UnityEngine.SceneManagement.Scene));
                Require(casualties.Blood.PermanentMarks==2,"Both integrated casualties retain death pools");
            }
            finally{UnityEngine.Object.DestroyImmediate(casualtyRoot);}
            Debug.Log("ARTILLERY_BLOOD_VERIFIED: both faction meshes fully partitioned, six capped fragments each, ballistic blood trails and deposits, nonfatal/fatal bullets and settled pools, pause and settling");
        }
        finally{gore?.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraRoot);}
    }
}
