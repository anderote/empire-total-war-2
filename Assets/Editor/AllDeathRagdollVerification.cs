using System;
using System.IO;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static class AllDeathRagdollVerification
{
    const string Pending="ETW2.AllDeathRagdollVerification.Pending";
    const string Capture="ETW2.AllDeathRagdollVerification.Capture";
    public static void VerifyAndCapture(){SessionState.SetBool(Capture,true);Verify();}
    [InitializeOnLoadMethod]
    static void Resume()
    {
        if(SessionState.GetBool(Pending,false))EditorApplication.update+=RunWhenPlaying;
    }
    static void RunWhenPlaying()
    {
        if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        EditorApplication.update-=RunWhenPlaying;
        SessionState.SetBool(Pending,false);
        try{Run();EditorApplication.Exit(0);}
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    static void Require(bool value,string message)
    { if(!value)throw new Exception("All-death ragdolls: "+message); }

    public static void Verify()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        SessionState.SetBool(Pending,true);
        EditorApplication.update+=RunWhenPlaying;
        EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        using(var equipment=new RagdollEquipment())
        using(var appearances=new InfantryRagdollAppearance())
        {
            var rigs=Resources.Load<InfantryRagdollLibrary>("BakedInfantry/InfantryRagdollLibrary");
            var rifles=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
            void CheckEquipment(InfantryRagdollSet original,bool standard=false)
            {
                var split=equipment.Resolve(original);
                int triangles=split.Mesh.triangles.Length;bool hat=false,gun=false,pack=false,flag=false;
                foreach(var part in split.DetachedParts)
                {
                    triangles+=part.Mesh.triangles.Length;hat|=part.Hat;gun|=part.Kind==DetachedEquipmentKind.Weapon;
                    pack|=part.Kind==DetachedEquipmentKind.Backpack;flag|=part.Kind==DetachedEquipmentKind.Standard;
                    if(part.Kind==DetachedEquipmentKind.Backpack)
                        Require(part.Mesh.bounds.size.magnitude>.3f,"Backpack extraction includes the pack body, not just its buckles");
                }
                Require(hat&&(standard?flag:gun)&&pack,"Hat, backpack and weapon/standard split: "+original.Mesh.name);
                Require(triangles==original.Mesh.triangles.Length,"Equipment split preserves every triangle exactly once");
                Require(ReferenceEquals(split,equipment.Resolve(original)),"Equipment extraction is cached per outfit");
            }
            CheckEquipment(rigs.French);CheckEquipment(rigs.British);
            foreach(var appearance in new[]{rifles.French,rifles.British,rifles.SingleFrench,rifles.SingleBritish})
                CheckEquipment(appearances.Resolve(appearance,rigs.French));
            var crowd=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
            CheckEquipment(appearances.Resolve(crowd.French,rigs.French,2),true);
            CheckEquipment(appearances.Resolve(crowd.British,rigs.British,2),true);
        }
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=64,
            EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        var root=new GameObject("All-death ragdoll verification");
        var cameraRoot=new GameObject("All-death ragdoll camera");
        try
        {
            var camera=cameraRoot.AddComponent<Camera>();
            camera.transform.position=new Vector3(0,3,-12);
            var casualties=root.AddComponent<BattleCasualtyView>();casualties.Initialize(world,camera);
            var causes=new[]{DeathCause.Musket,DeathCause.Melee,DeathCause.Fire};
            for(int i=0;i<32;i++)
            {
                world.Soldiers.Positions[i]=new Vector2((i%8)*4-14,(i/8)*4);
                Require(world.KillSoldier(i,causes[i%causes.Length],Vector3.zero,Vector3.zero),"Death recorded");
                Require(casualties.IsPhysical(i),"Nearby ordinary death uses physics: "+causes[i%causes.Length]);
            }
            Require(casualties.ActiveRagdolls==32,"Ordinary physics budget reached");
            Require(casualties.ActiveLooseEquipment==96,"Each ragdoll releases a gun, hat and backpack");
            world.KillSoldier(32,DeathCause.Melee,Vector3.zero,Vector3.zero);
            Require(!casualties.HasPhysicalCorpse(32),"Burst overflow retains animated fallback");
            world.Soldiers.Positions[33]=new Vector2(1000,1000);
            world.KillSoldier(33,DeathCause.Musket,Vector3.zero,Vector3.zero);
            Require(!casualties.HasPhysicalCorpse(33),"Distant death retains animated fallback");
            world.Soldiers.Positions[34]=new Vector2(-20,0);
            world.KillSoldier(34,DeathCause.Roundshot,Vector3.zero,Vector3.zero);
            Require(casualties.IsPhysical(34)&&casualties.ActiveRagdolls==33,"Artillery retains reserved capacity");
            for(int tick=0;tick<2400&&(casualties.ActiveRagdolls>0||casualties.ActiveLooseEquipment>0);tick++)casualties.Step(.025f);
            Require(casualties.ActiveRagdolls==0&&casualties.FrozenRagdolls==33,"Bodies freeze and release physics slots");
            Require(casualties.ActiveLooseEquipment==0&&casualties.RestingLooseEquipment==99,"Detached equipment settles and releases physics slots");
            Require(casualties.ValidateCorpses(out string issue),issue);
            world.Soldiers.Positions[35]=new Vector2(-24,0);
            world.KillSoldier(35,DeathCause.Melee,Vector3.zero,Vector3.zero);
            Require(casualties.IsPhysical(35)&&casualties.ActiveRagdolls==1,"Settled pool is reusable");
            Require(world.DeathCount==36,"Physics fallback loses no casualties");
            int bearer=CommandPartyLayout.StandardBearer(world,world.Battalions[0],0);
            Require(bearer>=0&&world.Soldiers.Alive[bearer],"Standard has a stable living soldier identity");
            world.Soldiers.Positions[bearer]=new Vector2(20,0);
            world.KillSoldier(bearer,DeathCause.Musket,Vector3.zero,Vector3.zero);
            Require(casualties.IsPhysical(bearer)&&casualties.ActiveLooseEquipment>=6,"Bearer casualty drops physical standard, hat and backpack");
            Require(CommandPartyLayout.StandardBearer(world,world.Battalions[0],0)==bearer,"Dead bearer identity is not reassigned to a survivor");
            if(SessionState.GetBool(Capture,false))
            {
                SessionState.SetBool(Capture,false);
                CaptureEquipment(camera,casualties);
            }
            Debug.Log("ALL_DEATH_RAGDOLLS_VERIFIED ordinary causes, zero-impulse collapse, overflow, distance, artillery reserve, freezing and pool reuse");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(cameraRoot);
        }
    }
    static void CaptureEquipment(Camera camera,BattleCasualtyView casualties)
    {
        const string output="Artifacts/RagdollEquipment";Directory.CreateDirectory(output);
        var light=new GameObject("Equipment review light").AddComponent<Light>();
        light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,-30,0);
        RenderSettings.ambientLight=Color.gray;RenderSettings.fog=false;
        Shader.SetGlobalVector("_BattleAmbientSky",new Vector4(.7f,.75f,.85f,1));
        Shader.SetGlobalVector("_BattleAmbientEquator",new Vector4(.65f,.67f,.7f,1));
        Shader.SetGlobalVector("_BattleAmbientGround",new Vector4(.45f,.45f,.5f,1));
        camera.orthographic=true;camera.orthographicSize=4f;camera.backgroundColor=new Color(.16f,.19f,.22f);
        camera.clearFlags=CameraClearFlags.SolidColor;
        var target=new RenderTexture(1000,800,24);camera.targetTexture=target;
        var pixels=new Texture2D(1000,800,TextureFormat.RGB24,false);
        var late=typeof(BattleCasualtyView).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
        typeof(BattleCasualtyView).GetField("renderingWarmed",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(casualties,true);
        try
        {
            float previous=0;
            foreach(float moment in new[]{0f,.3f,.7f,1.5f,8.5f})
            {
                for(float time=previous;time<moment-.001f;time+=.025f)casualties.Step(.025f);
                previous=moment;
                foreach(var review in new[]{(Name:"standard",X:20f),(Name:"infantry",X:-24f)})
                {
                    var center=new Vector3(review.X,1,0);
                    camera.transform.position=center+new Vector3(7,6,-9);camera.transform.LookAt(center);
                    late.Invoke(casualties,null);camera.Render();RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,1000,800),0,0);pixels.Apply();
                    File.WriteAllBytes(output+"/"+review.Name+"-"+moment.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+".png",pixels.EncodeToPNG());
                }
            }
        }
        finally
        {
            RenderTexture.active=null;camera.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(light.gameObject);
        }
    }
}
