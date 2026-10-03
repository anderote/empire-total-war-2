using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.AnimationAuthoring;
using EmpireBattle.Art;
using EmpireWorkbench;
using UnityEditor;
using UnityEngine;

public static class AnimationQualityVerification
{
    static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Animation quality: "+message);}

    public static void Verify()
    {
        Directory.CreateDirectory("Artifacts/AnimationQuality");
        File.Delete("Artifacts/AnimationQuality/verification.txt");
        var results=new List<string>();var failures=new List<string>();
        void Check(string name,Action action)
        {
            try{action();results.Add("PASS "+name);}
            catch(Exception e){failures.Add(name+": "+e.Message);results.Add("FAIL "+name+": "+e);}
            File.WriteAllLines("Artifacts/AnimationQuality/checks.txt",results);
        }
        Check("Playback and workbench",VerifyPlayback);Check("Command cadence",VerifyCommandCadence);
        Check("Foot contacts",VerifyFeet);Check("Artillery crew",VerifyCrew);Check("Baked meshes",VerifyMeshes);
        Check("Authored skeleton",InfantryMotionVerification.Verify);
        Check("Reload anatomy",InfantryAssetBuild.VerifyReload);
        Check("Infantry presentation",InfantryPresentationVerification.Verify);
        Check("Melee contact timing",MeleePresentationVerification.Verify);
        Check("Cavalry motion",CavalryMotionVerification.Verify);
        Check("Scalar/Burst movement parity",InfantryPerformanceVerification.VerifyMovement);
        if(failures.Count>0)throw new Exception(string.Join("\n",failures));
        File.WriteAllText("Artifacts/AnimationQuality/verification.txt",
            "PASS: loop seams, endpoints, command cadence, gait stride, retreat footwork, artillery loading/pause, mesh interpolation, reload anatomy, kneel/fire presentation, melee impact timing, cavalry motions and scalar/Burst movement parity.\n");
        Debug.Log("ANIMATION_QUALITY_VERIFIED");
    }

    static void VerifyPlayback()
    {
        int frame=AnimationPlayback.Frame(12,1,true,out float blend);
        Require(frame==0&&blend==0,"Loop endpoint returns exactly to frame zero");
        frame=AnimationPlayback.Frame(12,.99f,true,out blend);
        Require(frame==11&&blend>.8f,"Final loop interval interpolates to the first pose");
        frame=AnimationPlayback.Frame(12,1,false,out blend);
        Require(frame==11&&blend==0,"One shot holds its final pose");
        Require(AnimationPlayback.MountedGait(.2f,0,1,1)!=AnimationPlayback.MountedGait(.2f,0,1.5f,1.5f),"Stationary mounted idle continues breathing");
        Require(AnimationPlayback.MountedGait(.2f,0,1,1)==AnimationPlayback.MountedGait(.2f,0,1,1),"Paused mounted idle is deterministic");
        Require(Mathf.Abs(AnimationPlayback.MountedGait(.2f,4,1.025f,1)-(.2f+.025f*4/2.6f))<.0001f,"Mounted gait interpolates between simulation ticks");
        var loop=typeof(AssetWorkbenchBuild).GetMethod("IsLoop",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(string path in new[]{"Melee/Ready/Poses","Melee/Parry/Poses","Melee/Retreat/Poses","Fire","Reload","DeathVariants/0/Poses"})
            Require(!(bool)loop.Invoke(null,new object[]{path}),"Workbench one-shot metadata: "+path);
        Require((bool)loop.Invoke(null,new object[]{"UnderFireVariants/0/Poses"}),"Under-fire idle remains a loop");
        var lod=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary").French.Lods[0];
        var tracks=new List<ReviewTrack>();
        typeof(AssetWorkbenchBuild).GetMethod("Collect",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[]{lod,"",0,tracks,null});
        Require(tracks.Single(t=>t.Id=="LowerToKneel").Morph,"Kneeling bridge scrubs its morph");
        Require(tracks.Single(t=>t.Id=="RiseToIdle").ReverseMorph,"Rising starts on the knee and finishes upright");
        Require(tracks.Count(t=>t.Id.StartsWith("RiseToReload/"))==lod.Reload.Length&&tracks.Where(t=>t.Id.StartsWith("RiseToReload/")).All(t=>t.Morph),"Reload posture bridges are independently reviewable");
    }

    static void VerifyCommandCadence()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,EnableLandscape=false,EnableAI=false});
        var unit=world.Battalions[0];unit.Moving=true;
        var renderer=new CrowdInfantryRenderer(world,null);
        var lod=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary").French.Lods[0];
        var method=typeof(CrowdInfantryRenderer).GetMethod("Pose",BindingFlags.Instance|BindingFlags.NonPublic);
        var clock=typeof(CrowdInfantryRenderer).GetField("clock",BindingFlags.Instance|BindingFlags.NonPublic);
        int soldier=unit.SoldierStart;
        for(int role=0;role<2;role++)
        {
            world.Soldiers.Captains[soldier]=role==0;world.Soldiers.PikeSergeants[soldier]=role==1;
            world.Soldiers.MovementSpeeds[soldier]=1.2f;world.Soldiers.GaitPhases[soldier]=.27f;
            // Large presentation time used to add an unrelated second walking clock.
            clock.SetValue(renderer,7.31f);
            object[] args={lod,unit,false,soldier,0f};var actual=(Mesh)method.Invoke(renderer,args);
            var poses=lod.Command[role==0?0:3].March;
            int index=AnimationPlayback.Frame(poses.Length,AnimationPlayback.InfantryGait(world,soldier,7.31f),true,out float expectedBlend);
            Require(actual==poses[index]&&Mathf.Abs((float)args[4]-expectedBlend)<.0001f,"Command feet advance once per travelled stride");
        }
        // No flag meshes are drawn by this pose-only fixture; release its generated meshes.
        var meshes=(IEnumerable<Mesh>)typeof(CrowdInfantryRenderer).GetField("standardMeshes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
        foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
    }

    static void VerifyFeet()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/NapoleonicInfantry/Prefabs/FrenchInspection.prefab");
        var root=UnityEngine.Object.Instantiate(prefab);
        try
        {
            root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var idle=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/NapoleonicInfantry/Animations/musket_idle.FBX").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
            idle.SampleAnimation(root,0);var rig=new ProceduralInfantryRig(root);
            var foot=rig.Bones.First(b=>b.name=="l_foot");
            float plantedHeight=foot.position.y;
            rig.Sample(InfantryMotionKind.Retreat,.1f);var start=foot.position;
            rig.Sample(InfantryMotionKind.Retreat,.4f);
            Require(Vector3.Distance(start,foot.position)>.08f,"Retreat preserves actual stepping footwork");
            var report=new List<string>();
            for(int soldier=0;soldier<12;soldier++)
            foreach(var kind in new[]{InfantryMotionKind.Walk,InfantryMotionKind.March,InfantryMotionKind.Jog,InfantryMotionKind.Run,InfantryMotionKind.Charge})
            {
                int variant=Mathf.Min(2,(int)(RifleFireTiming.Variation(soldier,71)*3));
                bool running=kind==InfantryMotionKind.Run||kind==InfantryMotionKind.Jog;
                float speed=kind==InfantryMotionKind.Walk?.5f:kind==InfantryMotionKind.Jog?1.3f:2;
                float stride=FormationMath.SoldierStride(soldier,running,speed,kind==InfantryMotionKind.Charge);
                rig.Sample(kind,.12f,variant);var first=foot.position;
                rig.Sample(kind,.20f,variant);var second=foot.position;
                float slip=Mathf.Abs((second.z-first.z)-stride*.08f);
                report.Add($"{kind} soldier={soldier} stride={stride:F4} stance-slip={slip:F5}m");
                File.WriteAllLines("Artifacts/AnimationQuality/foot-contact.txt",report);
                Require(slip<.005f,"Planted foot speed matches world travel: "+report.Last());
                float stance=running||kind==InfantryMotionKind.Charge?.48f:.62f;
                for(int sample=0;sample<20;sample++)
                {
                    rig.Sample(kind,stance*sample/20f,variant);
                    Require(Mathf.Abs(foot.position.y-plantedHeight)<.005f,"Planted sole stays grounded through the stance: "+kind+" variant "+variant);
                }
            }
            File.WriteAllLines("Artifacts/AnimationQuality/foot-contact.txt",report);
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }

    static void VerifyCrew()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,FrenchArtillery=1,BritishArtillery=0,EnableLandscape=false,EnableAI=false});
        var guns=new ArtillerySystem(world);var gun=guns.Guns[0];gun.Position=Vector2.zero;gun.Facing=0;
        var owner=new GameObject("Crew verification");var crew=owner.AddComponent<ArtilleryCrewView>();
        try
        {
            crew.Initialize(world,guns,null);crew.Sample();
            var shot=owner.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Carried round");
            Require(!shot.gameObject.activeSelf,"Round hidden before reload begins");
            gun.Phase=ArtilleryPhase.Loading;
            var loader=shot.parent;Vector3 previous=loader.position;
            for(int frame=0;frame<100;frame++)
            {
                world.StepMovement(.02f);gun.PhaseElapsed=.2f;guns.StepCrewPositions(gun,.02f);crew.Sample();
                Vector3 travel=loader.position-previous;travel.y=0;
                if(frame>25&&travel.sqrMagnitude>.00001f)
                    Require(Vector3.Dot(-loader.forward,travel.normalized)>.9f,"Loader faces the approach direction");
                previous=loader.position;
            }
            Require(shot.gameObject.activeSelf,"Loader restores carried round after an idle period");
            Require(crew.ServiceSamples>0,"Crew reaches the working station");
            var before=owner.GetComponentsInChildren<Transform>(true).Select(t=>t.localToWorldMatrix).ToArray();
            crew.Sample();var after=owner.GetComponentsInChildren<Transform>(true).Select(t=>t.localToWorldMatrix).ToArray();
            for(int i=0;i<before.Length;i++)for(int k=0;k<16;k++)Require(Mathf.Abs(before[i][k]-after[i][k])<.00001f,"Paused crew pose does not advance");
            gun.PhaseElapsed=gun.PhaseDuration(gun.Phase)*.95f;crew.Sample();
            Require(!shot.gameObject.activeSelf,"Round disappears into the bore after loading");
            gun.Phase=ArtilleryPhase.Ramming;gun.PhaseElapsed=0;crew.Sample();
            Require(!shot.gameObject.activeSelf,"Loader carries no second shot during ramming");
        }
        finally{UnityEngine.Object.DestroyImmediate(owner);}
    }

    static void VerifyMeshes()
    {
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        int tracks=0,frames=0;
        void Check(Mesh[] poses,bool loop)
        {
            if(poses==null||poses.Length==0)return;
            tracks++;
            for(int frame=0;frame<poses.Length;frame++)
            {
                var mesh=poses[frame];Require(mesh,"Missing baked mesh");
                var next=poses[frame+1<poses.Length?frame+1:loop?0:frame];var points=mesh.vertices;var target=new List<Vector3>();mesh.GetUVs(2,target);
                Require(next.vertexCount==points.Length&&target.Count==points.Length,"Consistent interpolated topology: "+mesh.name);
                var expected=next.vertices;
                for(int v=0;v<points.Length;v++)
                {
                    Require(float.IsFinite(points[v].x)&&float.IsFinite(points[v].y)&&float.IsFinite(points[v].z),"Finite vertices: "+mesh.name);
                    Require((target[v]-expected[v]).sqrMagnitude<1e-8f,"Correct next pose or loop seam: "+mesh.name);
                }
                frames++;
            }
        }
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        {
            foreach(var poses in new[]{lod.Fire,lod.KneelingFire,lod.Reload,lod.KneelingReload,lod.Hit,lod.Death})Check(poses,false);
            foreach(var v in lod.Locomotion)foreach(var poses in v.Loops())Check(poses,true);
            foreach(var v in lod.DeathVariants)Check(v.Poses,false);
            foreach(var role in lod.Command)
            {
                foreach(var poses in new[]{role.Idle,role.March,role.PikeReady,role.OfficerReady,role.KneelingIdle})Check(poses,true);
                foreach(var poses in new[]{role.PikeThrust,role.OfficerSlash,role.OfficerFire,role.PikeDeath,role.OfficerDeath,role.KneelingStrike})Check(poses,false);
            }
            foreach(var clip in lod.Melee){Check(clip.Poses,false);Check(clip.Kneeling,false);}
        }
        var cavalry=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
        foreach(var set in new[]{cavalry.French,cavalry.British})foreach(var lod in set.Lods)foreach(var clip in lod.Clips)Check(clip.Poses,clip.Loop);
        File.WriteAllText("Artifacts/AnimationQuality/mesh-audit.txt",$"PASS {tracks} tracks, {frames} meshes across French/British infantry, command roles, cavalry and melee, both LODs.\n");
    }
}
