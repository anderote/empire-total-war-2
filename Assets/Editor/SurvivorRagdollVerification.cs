using System;
using System.IO;
using System.Reflection;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class SurvivorRagdollVerification
{
    const string Pending="ETW2.SurvivorRagdollVerification.Pending";
    const string Capture="ETW2.SurvivorRagdollVerification.Capture";
    static void Require(bool value,string message){if(!value)throw new Exception("Survivor ragdolls: "+message);}
    [InitializeOnLoadMethod] static void Resume(){if(SessionState.GetBool(Pending,false))EditorApplication.update+=RunWhenPlaying;}
    public static void VerifyAndCapture(){SessionState.SetBool(Capture,true);Verify();}
    public static void Verify()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
        SessionState.SetBool(Pending,true);EditorApplication.update+=RunWhenPlaying;EditorApplication.EnterPlaymode();
    }
    static void RunWhenPlaying()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        EditorApplication.update-=RunWhenPlaying;SessionState.SetBool(Pending,false);
        try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    static void Run()
    {
        var config=new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=64,AssetReview=true,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0};
        var world=new BattleWorld(config);var control=new BattleWorld(config);
        var root=new GameObject("Survivor ragdoll verification");var cameraRoot=new GameObject("Survivor camera");
        var camera=cameraRoot.AddComponent<Camera>();camera.transform.position=new Vector3(0,7,-14);camera.transform.LookAt(Vector3.up);
        var casualties=root.AddComponent<BattleCasualtyView>();casualties.Initialize(world,camera);
        typeof(BattleCasualtyView).GetField("renderingWarmed",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(casualties,true);
        try
        {
            for(int i=0;i<world.Soldiers.Alive.Length;i++)
            {
                world.Soldiers.Captains[i]=world.Soldiers.PikeSergeants[i]=false;
                control.Soldiers.Captains[i]=control.Soldiers.PikeSergeants[i]=false;
                world.Soldiers.Positions[i]=control.Soldiers.Positions[i]=new Vector2(400+i,400);
            }
            int initial=world.Battalions[0].AliveCount;
            for(int i=0;i<14;i++)
            {
                world.Soldiers.Positions[i]=control.Soldiers.Positions[i]=new Vector2((i%4)*3-5,(i/4)*3);
                world.Soldiers.Facings[i]=control.Soldiers.Facings[i]=0;
                Vector3 impulse=new Vector3(i%2==0?-4:4,3,i%3-1);
                world.ApplyArtilleryHit(i,30,DeathCause.Shell,impulse,Vector3.zero);
                control.ApplyArtilleryHit(i,30,DeathCause.Shell,impulse,Vector3.zero);
            }
            Require(casualties.ActiveSurvivorRagdolls==12,"Survivor physics respects twelve-body cap");
            Require(!casualties.HasSurvivorVisual(12)&&world.Soldiers.IsKnockedDown(12,0),"Overflow keeps animated recovery");
            Require(world.DeathCount==0&&world.Battalions[0].AliveCount==initial,"Ragdoll knockdowns preserve roster and health");
            Require(casualties.ActiveLooseEquipment==0,"Living soldiers retain their equipment");
            world.Soldiers.Positions[20]=new Vector2(1000,1000);world.ApplyArtilleryHit(20,30,DeathCause.Shell,new Vector3(4,3,0),Vector3.zero);
            Require(!casualties.HasSurvivorVisual(20),"Distant troops keep animated reactions");
            for(int tick=0;tick<320;tick++)
            {
                world.StepReview(.025f);control.StepReview(.025f);casualties.Step(.025f);
                for(int i=0;i<14;i++)
                {
                    Require(Vector3.Distance(world.SoldierPosition(i,world.Time),control.SoldierPosition(i,control.Time))<.0001f,"Physics presentation preserves authoritative soldier positions");
                    Require(world.Soldiers.KnockdownEnds[i]==control.Soldiers.KnockdownEnds[i],"Ragdolls cannot alter combat readiness timing");
                }
            }
            Require(casualties.ActiveSurvivorRagdolls==0&&casualties.SurvivorRecoveries==12,"All survivors hand off to animation and release slots");
            Require(world.DeathCount==0&&casualties.FrozenRagdolls==0,"Recovery leaves no corpses");
            for(int i=0;i<14;i++)Require(world.Soldiers.Health[i]==70&&!world.Soldiers.IsKnockedDown(i,world.Time),"Survivors stand with their wounds intact");
            // Kill a second, airborne knockdown: reuse its physical body and never get up.
            world.Soldiers.Positions[0]=Vector2.zero;world.ApplyArtilleryHit(0,10,DeathCause.Shell,new Vector3(4,3,0),Vector3.zero);
            Require(casualties.HasSurvivorVisual(0),"Released pool can start another knockdown");
            world.StepReview(.1f);casualties.Step(.1f);
            world.ApplyArtilleryHit(0,100,DeathCause.Shell,Vector3.right,Vector3.zero);
            Require(!casualties.HasSurvivorVisual(0)&&casualties.IsPhysical(0)&&world.DeathCount==1,"Airborne death converts the same body to a corpse");
            Require(casualties.ActiveLooseEquipment>0,"Equipment detaches only after death");
            for(int i=0;i<240;i++){world.StepReview(.025f);casualties.Step(.025f);}
            Require(!world.Soldiers.Alive[0]&&!world.Soldiers.IsKnockedDown(0,world.Time),"Killed survivor never returns to standing");
            world.Soldiers.Positions[3]=Vector2.zero;world.ApplyArtilleryHit(3,10,DeathCause.Shell,new Vector3(4,3,0),Vector3.zero);
            for(int tick=0;tick<160&&world.Soldiers.KnockdownRecoveryVariants[3]<0;tick++){world.StepReview(.025f);casualties.Step(.025f);}
            Require(casualties.HasSurvivorVisual(3)&&world.Soldiers.KnockdownRecoveryVariants[3]>=0,"Settled survivor enters the pose blend");
            world.ApplyArtilleryHit(3,100,DeathCause.Shell,Vector3.right,Vector3.zero);
            Require(!casualties.HasSurvivorVisual(3)&&casualties.IsPhysical(3),"Death during recovery blend retains a physical corpse");
            Require(casualties.ActiveSurvivorRagdolls==0&&world.DeathCount==2,"A blending survivor cannot leave a duplicate living visual");
            world.Soldiers.Positions[1]=Vector2.zero;world.ApplyArtilleryHit(1,5,DeathCause.Shell,Vector3.up*.5f,Vector3.zero);
            Require(!casualties.HasSurvivorVisual(1),"Light hits retain cheap animated reactions");
            ArtilleryAmmunitionVerification.VerifyFriendlyFireAndShrapnel();
            VerifyFragmentBounds();
            ArtilleryRecoveryVerification.Verify();
            if(SessionState.GetBool(Capture,false)){SessionState.SetBool(Capture,false);CaptureMotion();}
            Debug.Log("SURVIVOR_RAGDOLLS_VERIFIED cap, overflow, distance, stable gameplay, recovery, wounds, lethal follow-up and bounded varied fragments");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraRoot);}
    }
    static void VerifyFragmentBounds()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        var artillery=new ArtillerySystem(world);
        var origin=new Vector3(100,30,0);
        typeof(ArtillerySystem).GetMethod("Explode",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(artillery,
            new object[]{new CannonProjectile {Position=origin,AmmunitionType=ArtilleryAmmunition.Explosive}});
        var ranges=new System.Collections.Generic.HashSet<float>();var streaks=new System.Collections.Generic.HashSet<float>();
        int count=0;foreach(var shot in artillery.Projectiles)if(shot.Active)
        {
            count++;ranges.Add(shot.FragmentRangeRemaining);streaks.Add(shot.FragmentStreakTime);
            Require(shot.FragmentRangeRemaining>=8&&shot.FragmentRangeRemaining<=18,"Fragment travel has 8–18m bounds");
            Require(shot.FragmentStreakTime>=.003f&&shot.FragmentStreakTime<=.007f,"Streak lifetime stays short");
        }
        Require(count==24&&ranges.Count>12&&streaks.Count>12,"Each shell has twenty-four independently varied fragments");
        artillery.Step(.5f);
        foreach(var shot in artillery.Projectiles)if(shot.Shrapnel)
        {
            Require(!shot.Active,"Fragments expire within a single long frame");
            Require(Vector3.Distance(shot.Position,origin+Vector3.up*.35f)<=18.001f,"No fragment overshoots the range cap at coarse timesteps");
        }
    }
    static void CaptureMotion()
    {
        const string output="Artifacts/SurvivorRagdolls";Directory.CreateDirectory(output);
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=8,AssetReview=true,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        var root=new GameObject("Survivor visual capture");var cameraObject=new GameObject("Survivor capture camera");var camera=cameraObject.AddComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=3.3f;camera.backgroundColor=new Color(.22f,.25f,.29f);camera.clearFlags=CameraClearFlags.SolidColor;
        var light=new GameObject("Survivor review light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,-30,0);
        RenderSettings.ambientLight=Color.gray;RenderSettings.fog=false;
        Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.7f,.75f,.85f,1));Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.65f,.67f,.7f,1));Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.45f,.45f,.5f,1));
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.position=new Vector3(0,-.02f,0);floor.transform.localScale=Vector3.one*3;
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.color=new Color(.32f,.34f,.29f);floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
        var target=new RenderTexture(1000,700,24);camera.targetTexture=target;var pixels=new Texture2D(1000,700,TextureFormat.RGB24,false);
        var casualties=root.AddComponent<BattleCasualtyView>();casualties.Initialize(world,camera);
        typeof(BattleCasualtyView).GetField("renderingWarmed",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(casualties,true);
        using(var renderer=new RendererOwner(new CrowdInfantryRenderer(world,camera)))
        try
        {
            renderer.Value.Casualties=casualties;
            foreach(var unit in world.Battalions){unit.Position=Vector2.zero;unit.FireAtWill=false;}
            for(int i=0;i<world.Soldiers.Alive.Length;i++){world.Soldiers.Positions[i]=new Vector2(500,500);world.Soldiers.Captains[i]=world.Soldiers.PikeSergeants[i]=false;}
            world.Soldiers.Positions[0]=new Vector2(-1,0);world.Soldiers.Positions[1]=new Vector2(1,0);
            camera.transform.position=new Vector3(7,6,-10);camera.transform.LookAt(new Vector3(0,1,0));
            world.ApplyArtilleryHit(0,30,DeathCause.Shell,new Vector3(-3,3,0),Vector3.zero);
            world.ApplyArtilleryHit(1,30,DeathCause.Shell,new Vector3(3,3,0),Vector3.zero);
            var frustum=new Plane[6];
            foreach(float moment in new[]{.15f,.45f,.9f,1.5f,2.2f,3.2f,4.5f})
            {
                while(world.Time<moment-.001f){world.StepReview(.025f);casualties.Step(.025f);}
                GeometryUtility.CalculateFrustumPlanes(camera,frustum);renderer.Value.Render(frustum,world.Time);camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1000,700),0,0);pixels.Apply();
                File.WriteAllBytes(output+"/recovery-"+moment.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+".png",pixels.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active=null;camera.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(light.gameObject);UnityEngine.Object.DestroyImmediate(floor);UnityEngine.Object.DestroyImmediate(floorMaterial);
        }
    }
    sealed class RendererOwner:IDisposable
    {
        public readonly CrowdInfantryRenderer Value;public RendererOwner(CrowdInfantryRenderer value){Value=value;}public void Dispose(){Value.Dispose();}
    }
}
