using System;
using System.IO;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class CavalryMotionVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Mounted motion: "+message);}
    public static void BuildNativeReview()
    {
        Verify();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Cavalry ragdoll review").AddComponent<CavalryRagdollReview>();
        const string scenePath="Assets/Scenes/CavalryMotionQA.unity";EditorSceneManager.SaveScene(scene,scenePath);
        var report=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions{
            scenes=new[]{scenePath},locationPathName="Builds/CavalryMotionQA/Cavalry Motion QA.app",
            target=BuildTarget.StandaloneOSX,options=UnityEditor.BuildOptions.None});
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Mounted review player build failed");
        Debug.Log("CAVALRY_MOTION_NATIVE_REVIEW_BUILT");
    }
    public static void VerifyCasualtyAppearance()
    {
        var library=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
        var rigs=Resources.Load<InfantryRagdollLibrary>("BakedCavalry/CavalryRagdollLibrary");
        using(var appearances=new InfantryRagdollAppearance())
        using(var equipment=new RagdollEquipment())
        foreach(string faction in new[]{"france","britain","ming","india"})
        foreach(var side in new[]{ArmySide.French,ArmySide.British})
        {
            var unit=new BattalionState{Faction=faction,Side=side,Kind=UnitKind.Cavalry};
            var appearance=library.Resolve(unit);var reference=side==ArmySide.French?rigs.French:rigs.British;
            var source=appearance.Lods[0].Clip(MountedMotion.RiderFall).Poses[0];
            var set=appearances.ResolveMounted(appearance,reference);
            Require(set!=null&&set.Mesh!=reference.Mesh,"Uses actual rider geometry: "+faction);
            Require(set.Material.GetTexture("_BaseMap")==appearance.Material.GetTexture("_BaseMap"),"Keeps rider atlas across army sides: "+faction);
            Require(set.Mesh.vertexCount==source.vertexCount&&Array.TrueForAll(set.Mesh.uv,(uv)=>!float.IsNaN(uv.x)),"Keeps rider topology and valid UVs");
            var vertices=set.Mesh.vertices;var original=source.vertices;var weights=set.Mesh.boneWeights;var binds=set.Mesh.bindposes;
            var uv=set.Mesh.uv;var sourceUv=source.uv;
            var pose=set.Mounted[0];var transforms=new Matrix4x4[binds.Length];
            for(int i=0;i<binds.Length;i++)transforms[i]=Matrix4x4.TRS(pose.Positions[i],pose.Rotations[i],Vector3.one)*binds[i];
            float maxError=0;
            for(int i=0;i<vertices.Length;i++)
            {
                Require(uv[i]==sourceUv[i],"Preserves exact outfit UVs");
                var w=weights[i];var v=vertices[i];
                var reconstructed=transforms[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+transforms[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1
                    +transforms[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+transforms[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                maxError=Mathf.Max(maxError,Vector3.Distance(reconstructed,original[i]));
            }
            Require(maxError<.002f,"Saddle handoff preserves rider shape: "+faction+" error="+maxError);
            var split=equipment.Resolve(set);int triangles=split.Mesh.triangles.Length;
            foreach(var part in split.DetachedParts)triangles+=part.Mesh.triangles.Length;
            Require(triangles==source.triangles.Length,"Body and loose equipment preserve every rider triangle");
            Require(ReferenceEquals(set,appearances.ResolveMounted(appearance,reference)),"Caches rider outfit");
            Debug.Log("CAVALRY_CASUALTY_APPEARANCE_PASSED faction="+faction+" side="+side+" vertices="+vertices.Length+" saddleError="+maxError);
        }
    }
    public static void Verify()
    {
        VerifyCasualtyAppearance();
        CavalryCombatVerification.VerifyAssets();
        InfantryPresentationVerification.Verify();
        VerifyCommands();
        var mountedRigs=Resources.Load<InfantryRagdollLibrary>("BakedCavalry/CavalryRagdollLibrary");
        var infantryRigs=Resources.Load<InfantryRagdollLibrary>("BakedInfantry/InfantryRagdollLibrary");
        Require(mountedRigs&&mountedRigs.French.Mesh&&mountedRigs.British.Mesh,"Physical rider assets for both factions");
        foreach(var set in new[]{mountedRigs.French,mountedRigs.British})
        {
            Require(set.Mounted.Length==16&&set.Names.Length==infantryRigs.French.Names.Length,"Mounted start poses and pooled skeleton contract");
            for(int bone=0;bone<set.Names.Length;bone++)Require(set.Names[bone]==infantryRigs.French.Names[bone]&&set.Parents[bone]==infantryRigs.French.Parents[bone],"Rider pool uses the same skeleton ordering");
            int pelvis=Array.IndexOf(set.Names,"pelvis");Require(set.Mounted[0].Positions[pelvis].y>1.3f,"Ragdoll starts in the saddle");
        }
        Require(BattleCasualtyView.MountedLaunchVelocity(new SoldierDeath{Velocity=Vector3.forward*9,Impulse=Vector3.back*200,Cause=DeathCause.Roundshot}).magnitude<9,"Mounted ragdoll launch clamps projectile impulse");
        foreach(float speed in new[]{0f,2.2f,4.2f,7.6f,9f})foreach(var cause in new[]{DeathCause.Musket,DeathCause.Roundshot,DeathCause.Shell,DeathCause.Melee})
        {
            var variants=new System.Collections.Generic.HashSet<MountedMotion>();
            for(int soldier=0;soldier<60;soldier++)
            {
                var d=new SoldierDeath{Soldier=soldier,Kind=UnitKind.Cavalry,Cause=cause,Position=new Vector3(0,25,0),Velocity=Vector3.forward*speed,Impulse=Vector3.back*200,Born=10};
                var first=CavalryCasualtyMotion.Sample(d,10);Require(first.RiderPosition==new Vector3(0,0,0)&&first.HorsePosition==Vector3.zero,"Starts at original terrain position without a jump");
                for(int frame=0;frame<=100;frame++)
                {
                    var pose=CavalryCasualtyMotion.Sample(d,10+frame*.05f);variants.Add(pose.RiderMotion);
                    Require(pose.RiderPosition.magnitude<6,"Rider travel stays bounded for projectile impulse 200");
                    if(cause!=DeathCause.Musket)Require(pose.HorsePosition.magnitude<4,"Artillery horse moves less than four metres");
                    Require(!float.IsNaN(pose.RiderPosition.x),"Finite trajectories");
                }
                var end=CavalryCasualtyMotion.Sample(d,15);var later=CavalryCasualtyMotion.Sample(d,110);
                Require(end.RiderPosition==later.RiderPosition&&Mathf.Abs(end.RiderPosition.y)<.001f,"Grounded rider remains at rest");
                Require(later.HorseMotion==MountedMotion.HorseFall,"Dead horses settle; survivors use independent simulation");
            }
            Require(variants.Count>=3,"Several deterministic falls at every pace and cause");
        }
        var library=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        foreach(var kind in new[]{MountedMotion.HorseFall,MountedMotion.RiderFall,MountedMotion.RiderFallBack,MountedMotion.RiderFallSide,MountedMotion.RiderTumble})
        {
            var clip=lod.Clip(kind);var end=clip.Poses[clip.Poses.Length-1];
            Require(end.bounds.min.y>=-.002f&&end.bounds.min.y<.03f,"Grounded casualty endpoint: "+end.name);
            foreach(var mesh in clip.Poses)Require(mesh.bounds.min.y>=-.002f,"No ground penetration during the fall: "+mesh.name);
            if(kind!=MountedMotion.HorseFall)Require(end.bounds.size.y<1,"Rider lies down rather than retaining saddle height: "+end.name);
        }
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        foreach(var kind in new[]{MountedMotion.HorseWalk,MountedMotion.HorseTrot,MountedMotion.HorseGallop,MountedMotion.HorseIdle})
            foreach(var mesh in lod.Clip(kind).Poses)Require(mesh.bounds.size.y>1.5f,"Unmounted horse remains upright after baking fall clips: "+mesh.name);
        foreach(bool jobs in new[]{false,true})
        {
            float walk=Travel(false,false,jobs),trot=Travel(false,true,jobs),run=Travel(true,false,jobs);
            Require(trot>walk*1.5f&&run>trot*1.5f,"Walk, trot and gallop have distinct travel speeds");
            Debug.Log($"CAVALRY_PACES_VERIFIED jobs={jobs} walk={walk:F2} trot={trot:F2} gallop={run:F2}");
        }
        var casualtyWorld=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,FrenchCavalry=1,EnableAI=false,EnableLandscape=false});
        int mounted=casualtyWorld.Battalions[1].SoldierStart;
        casualtyWorld.Soldiers.Velocities[mounted]=Vector2.up*7.6f;casualtyWorld.Soldiers.MovementSpeeds[mounted]=7.6f;
        Require(casualtyWorld.ApplyWound(mounted,200,DeathCause.Musket,Vector3.zero),"Musket kills the chosen rider");
        Require(casualtyWorld.Deaths[mounted].Velocity==Vector3.forward*7.6f,"Real casualty records preserve movement velocity");
        Require(InfantryTravel(true)>InfantryTravel(false)*1.5f,"Infantry run orders move faster than walking");
        CavalryCombatVerification.VerifySimulation();
        Debug.Log("CAVALRY_MOTION_VERIFIED bounded artillery casualties, rider separation, multiple falls, grounded endpoints, all three paces and infantry run poses");
    }
    static void VerifyCommands()
    {
        Require(BattleView.IsDoubleClickRun(.2f,Vector2.zero,new Vector2(12,0)),"Double click requests run");
        Require(!BattleView.IsDoubleClickRun(.4f,Vector2.zero,Vector2.zero)&&!BattleView.IsDoubleClickRun(.1f,Vector2.zero,new Vector2(30,0)),"Separate clicks stay ordinary orders");
        var w=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,FrenchCavalry=1,EnableAI=false,EnableLandscape=false});
        var go=new GameObject("Command verification");var view=go.AddComponent<BattleView>();
        void Field(string name,object value)=>typeof(BattleView).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,value);
        try
        {
            int count=w.Battalions.Length;var selected=new bool[count];selected[0]=selected[1]=true;
            Field("world",w);Field("selected",selected);Field("previewActive",selected);
            var destinations=new Vector2[count];var facings=new float[count];var files=new int[count];
            foreach(var b in w.Battalions){destinations[b.Id]=b.Position+FormationMath.Forward(b.Facing)*50;facings[b.Id]=b.Facing;files[b.Id]=b.Files;}
            Field("previewDestinations",destinations);Field("previewFacings",facings);Field("previewFiles",files);
            typeof(BattleView).GetMethod("ApplyPreview",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,new object[]{true,false});
            Require(w.Battalions[0].Running&&w.Battalions[1].Running,"Double-click order runs infantry and cavalry");
            var destination=w.Battalions[1].Destination;view.OrderToggleRun();
            Require(!w.Battalions[0].Running&&!w.Battalions[1].Running,"Run control returns selection to walk");
            view.OrderToggleTrot();Require(w.Battalions[1].Trotting&&!w.Battalions[0].Trotting&&!w.Battalions[1].Running,"Trot affects cavalry only");
            view.OrderToggleRun();Require(w.Battalions[1].Running&&!w.Battalions[1].Trotting&&w.Battalions[1].Destination==destination,"Gallop replaces trot without changing destination");
            Debug.Log("MOVEMENT_COMMANDS_VERIFIED double click, infantry run, mounted gallop, trot, walk");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
    static float InfantryTravel(bool run)
    {
        var w=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        var u=w.Battalions[0];u.Running=run;Vector2 start=u.Position;
        w.IssueMove(u.Id,start+FormationMath.Forward(u.Facing)*100,u.Facing);
        for(int i=0;i<100;i++)w.StepMovement(.05f);
        return Vector2.Distance(start,u.Position);
    }
    static float Travel(bool run,bool trot,bool jobs)
    {
        var w=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,FrenchCavalry=1,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        var u=w.Battalions[1];u.Running=run;u.Trotting=trot;Vector2 start=u.Position;
        w.IssueMove(u.Id,start+FormationMath.Forward(u.Facing)*150,u.Facing);
        using(var movement=new SoldierMovementJobs(w))
        {
            if(jobs)w.MovementJobs=movement;
            for(int i=0;i<200;i++)w.StepMovement(.05f);
            w.MovementJobs=null;
        }
        return Vector2.Distance(start,u.Position);
    }
    // Offline contact sheets use the same authored meshes as the runtime renderer.
    public static void RenderReview()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        Directory.CreateDirectory("Artifacts/CavalryMotionReview");
        var set=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary").French;
        var camera=new GameObject("Review camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2.5f;
        camera.transform.position=new Vector3(4,3,-6);camera.transform.LookAt(new Vector3(0,1,0));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.19f,.22f);
        var light=new GameObject("Review sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);RenderSettings.ambientLight=Color.gray;
        var rt=new RenderTexture(480,400,24);camera.targetTexture=rt;
        var item=new GameObject("Mounted pose");var filter=item.AddComponent<MeshFilter>();var renderer=item.AddComponent<MeshRenderer>();renderer.sharedMaterial=set.Material;
        var sheet=new Texture2D(480*5,400*5,TextureFormat.RGB24,false);
        var kinds=new[]{MountedMotion.HorseFall,MountedMotion.RiderFall,MountedMotion.RiderFallBack,MountedMotion.RiderFallSide,MountedMotion.RiderTumble};
        for(int row=0;row<kinds.Length;row++)for(int column=0;column<5;column++)
        {
            var clip=set.Lods[0].Clip(kinds[row]);filter.sharedMesh=clip.Poses[Mathf.RoundToInt((clip.Poses.Length-1)*column/4f)];
            camera.Render();RenderTexture.active=rt;var tile=new Texture2D(480,400,TextureFormat.RGB24,false);tile.ReadPixels(new Rect(0,0,480,400),0,0);tile.Apply();
            sheet.SetPixels(column*480,(4-row)*400,480,400,tile.GetPixels());UnityEngine.Object.DestroyImmediate(tile);
        }
        sheet.Apply();File.WriteAllBytes("Artifacts/CavalryMotionReview/casualty-contact-sheet.png",sheet.EncodeToPNG());
        RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(sheet);
        Debug.Log("CAVALRY_MOTION_CONTACT_SHEET_RENDERED rows horse/front/back/side/tumble; columns start/quarter/half/three-quarter/end");
    }
}
