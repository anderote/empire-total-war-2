using System;
using System.Collections;
using System.IO;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CannonImpactVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    const string Output="Artifacts/CannonImpactReview";
    static void Require(bool value,string message) { if(!value)throw new Exception("Cannon impact: "+message); }
    public static void Verify()
    {
        Directory.CreateDirectory(Output);
        VerifyCanister();
        RoundshotHitVerification.Verify();
        ArtilleryAmmunitionVerification.Verify();
        VerifyRagdolls();
        ArtilleryBloodVerification.Verify();
        Debug.Log("CANNON_IMPACTS_VERIFIED");
    }
    static void VerifyCanister()
    {
        var report=new System.Text.StringBuilder("kind,range,timestep,killed,wounded,pellets\n");
        var middleRange=new System.Collections.Generic.Dictionary<(bool,float),int>();
        foreach(bool mounted in new[]{false,true})foreach(float distance in new[]{60f,110f,210f})foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var world=new BattleWorld(new BattleConfig { AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=120,
                BritishCavalry=mounted?1:0,RidersPerSquadron=120,EnableLandscape=false,EnableAI=false,FrenchArtillery=4,BritishArtillery=0 });
            var artillery=new ArtillerySystem(world);
            foreach(var unit in world.Battalions)unit.FireAtWill=false;
            for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(-300-i,-300);
            var target=world.Battalions[mounted?2:1];target.Position=new Vector2(100,distance);
            for(int i=0;i<target.SoldierCount;i++)world.Soldiers.Positions[target.SoldierStart+i]=new Vector2(100+(i%30-14.5f)*(mounted?1.7f:1.1f),distance+i/30*(mounted?3:1.5f));
            foreach(var gun in artillery.Guns)
            {
                gun.Position=new Vector2(100+(gun.Id-1.5f)*9,0);gun.Facing=0;
                Require(artillery.FireReviewShot(gun.Id,target.Id,ArtilleryAmmunition.Canister),"Review gun fires");
            }
            int pellets=0;foreach(var p in artillery.Projectiles)if(p.Active)pellets++;
            Require(pellets==4*ArtillerySystem.CanisterPellets,"Every gun in a simultaneous battery gets its full pellet load");
            for(int tick=0;tick<Mathf.CeilToInt(1.6f/dt);tick++)artillery.Step(dt);
            int wounded=0;for(int i=target.SoldierStart;i<target.SoldierStart+target.SoldierCount;i++)if(world.Soldiers.Alive[i]&&world.Soldiers.Health[i]<100)wounded++;
            report.AppendLine($"{(mounted?"cavalry":"infantry")},{distance},{dt},{world.DeathCount},{wounded},{pellets}");
            Debug.Log($"CANISTER_RESULT mounted={mounted} range={distance} dt={dt} killed={world.DeathCount} wounded={wounded}");
            Require(world.DeathCount<target.SoldierCount,"Volley hits actual contacts, not an automatic unit wipe");
            if(distance==60)Require(world.DeathCount>=(mounted?40:60),"Close battery canister devastates a dense formation");
            if(distance==110)middleRange[(mounted,dt)]=world.DeathCount;
            if(distance==210)Require(world.DeathCount<middleRange[(mounted,dt)]&&wounded>0,"Long-range spread and energy reduce lethality");
            Require(world.CountAlive(ArmySide.French)==120,"Friendly soldiers remain unharmed");
        }
        File.WriteAllText(Output+"/canister.csv",report.ToString());
        // A cannonball/pellet can hit the flank of the horse, or the rider above infantry height.
        foreach(float height in new[]{1f,2.5f})
        {
            var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=8,BritishCavalry=1,
                EnableLandscape=false,EnableAI=false,FrenchArtillery=0,BritishArtillery=0});
            for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(-300-i,-300);
            int victim=world.Battalions[2].SoldierStart;world.Soldiers.Positions[victim]=new Vector2(100,0);
            var artillery=new ArtillerySystem(world);var p=artillery.Projectiles[0];p.Active=true;p.Side=ArmySide.French;p.FromCamera=true;
            p.Position=new Vector3(100.5f,height,-2);p.Velocity=Vector3.forward*180;p.AmmunitionType=ArtilleryAmmunition.Canister;
            artillery.Step(.025f);Require(!world.Soldiers.Alive[victim],"Mounted body is hit outside narrow infantry cylinder");
        }
    }
    static void VerifyRagdolls()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var mode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
        var root=new GameObject("Impact review");var camera=new GameObject("Review camera").AddComponent<Camera>();
        camera.transform.position=new Vector3(12,12,-18);camera.transform.LookAt(new Vector3(0,0,3));camera.orthographic=true;camera.orthographicSize=9;
        camera.backgroundColor=new Color(.15f,.18f,.23f);camera.clearFlags=CameraClearFlags.SolidColor;
        var light=new GameObject("Review light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,-30,0);
        RenderSettings.ambientLight=Color.gray;RenderSettings.fog=false;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(60,1,60);
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.26f,.29f,.21f);floor.GetComponent<Renderer>().sharedMaterial=material;
        var target=new RenderTexture(1200,800,24);camera.targetTexture=target;var image=new Texture2D(1200,800,TextureFormat.RGB24,false);
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=4,SoldiersPerBattalion=20,EnableLandscape=false,EnableAI=false,FrenchArtillery=0,BritishArtillery=0});
        var casualties=root.AddComponent<BattleCasualtyView>();
        void Set(string name,object value)=>typeof(BattleCasualtyView).GetField(name,Hidden).SetValue(casualties,value);
        var crowd=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        Set("world",world);Set("cameraView",camera);Set("crowd",crowd);Set("rigs",Resources.Load<InfantryRagdollLibrary>("BakedInfantry/InfantryRagdollLibrary"));
        Set("physicsScene",scene);
        int capacity=world.Soldiers.Alive.Length;
        Set("frozen",new Mesh[capacity]);Set("physical",new bool[capacity]);Set("anchors",new Vector3[capacity]);
        Set("flightEnds",new Vector3[capacity]);Set("flightTimes",new float[capacity]);
        var blood=root.AddComponent<BattleBlood>();blood.Initialize(world,camera,casualties);Set("blood",blood);
        var handler=typeof(BattleCasualtyView).GetMethod("OnDeath",Hidden);
        world.SoldierDied+=d=>handler.Invoke(casualties,new object[]{d});
        try
        {
            string[] factions={"france","britain","prussia","ming","france","britain","prussia","ming"};
            for(int i=0;i<8;i++)
            {
                var unit=world.Battalions[i];unit.Faction=factions[i];unit.Role=i==3?UnitRole.Swordsman:i==4?UnitRole.Skirmisher:UnitRole.Line;
                int id=unit.SoldierStart+4;world.Soldiers.Positions[id]=new Vector2((i%4-1.5f)*4,i/4*6);
                world.Soldiers.Facings[id]=0;world.Soldiers.Captains[id]=i==5||i==7;world.Soldiers.PikeSergeants[id]=i==6;
                world.ApplyArtilleryHit(id,500,DeathCause.Roundshot,new Vector3(i%2==0?3:-3,2,5),Vector3.zero);
                Require(casualties.IsPhysical(id),"Every outfit/command role becomes a physical casualty: "+i);
            }
            var pool=(IList)typeof(BattleCasualtyView).GetField("pool",Hidden).GetValue(casualties);
            float elapsed=0;
            foreach(float time in new[]{0f,.2f,.5f,1f,3f,8f})
            {
                while(elapsed+1e-5f<time){Physics.Simulate(1f/120f);elapsed+=1f/120f;}
                camera.Render();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1200,800),0,0);image.Apply();
                File.WriteAllBytes(Output+$"/infantry-{time:F1}.png",image.EncodeToPNG());
            }
            foreach(var rag in pool)
            {
                var skin=(SkinnedMeshRenderer)rag.GetType().GetField("Skin").GetValue(rag);
                var mesh=new Mesh();skin.BakeMesh(mesh);mesh.RecalculateBounds();
                Debug.Log("RAGDOLL_SETTLED "+skin.sharedMesh.name+" bounds="+mesh.bounds);
                float bodyTop=0;foreach(var body in skin.GetComponentsInChildren<Rigidbody>())bodyTop=Mathf.Max(bodyTop,body.position.y);
                Require(bodyTop<1f&&mesh.bounds.size.y<3.5f&&mesh.bounds.min.y>-.45f,"Body settles without stretched limbs or sinking: "+skin.sharedMesh.name);
                UnityEngine.Object.DestroyImmediate(mesh);
                typeof(BattleCasualtyView).GetMethod("Freeze",Hidden).Invoke(casualties,new[]{rag});
            }
            Require(casualties.FrozenRagdolls==8&&casualties.ActiveRagdolls==0,"All bodies persist and free their slots");
            var cavalry=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
            var horses=new GameObject[4];var props=new MaterialPropertyBlock();
            for(int i=0;i<4;i++){horses[i]=new GameObject("Horse impact "+i);horses[i].AddComponent<MeshFilter>();horses[i].AddComponent<MeshRenderer>();}
            foreach(float time in new[]{0f,.25f,.6f,1f,2.5f})
            {
                for(int i=0;i<4;i++)
                {
                    var death=new SoldierDeath{Soldier=i,Kind=UnitKind.Cavalry,Cause=DeathCause.Roundshot,Position=new Vector3((i-1.5f)*4,0,0),
                        Impulse=(i%2==0?Vector3.right:Vector3.back)*8,Velocity=Vector3.forward*(i<2?0:8)};
                    var pose=CavalryCasualtyMotion.Sample(death,time);var set=i%2==0?cavalry.French:cavalry.British;
                    var clip=set.Lods[0].Clip(pose.HorseMotion);
                    int frame=AnimationPlayback.Frame(clip.Poses.Length,pose.HorsePhase,false,out float blend);
                    horses[i].GetComponent<MeshFilter>().sharedMesh=clip.Poses[frame];
                    var renderer=horses[i].GetComponent<MeshRenderer>();renderer.sharedMaterial=set.Material;props.SetFloat("_PoseBlend",blend);renderer.SetPropertyBlock(props);
                    horses[i].transform.SetPositionAndRotation(pose.HorsePosition,Quaternion.Euler(0,pose.HorseYaw,0));
                }
                camera.Render();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1200,800),0,0);image.Apply();
                File.WriteAllBytes(Output+$"/horses-{time:F2}.png",image.EncodeToPNG());
            }
            foreach(var horse in horses)UnityEngine.Object.DestroyImmediate(horse);
            // Physics remains bounded under dense canister casualties, while overflow remains visible.
            for(int i=0;i<capacity;i++)if(world.Soldiers.Alive[i])
            {
                world.Soldiers.Positions[i]=new Vector2(i%10,i/10);world.Soldiers.Captains[i]=world.Soldiers.PikeSergeants[i]=false;
                world.ApplyArtilleryHit(i,500,DeathCause.Roundshot,new Vector3(1,2,3),Vector3.zero);
            }
            Require(casualties.ActiveRagdolls==BattleCasualtyView.MaxRagdolls&&world.DeathCount==capacity,"96 physical slots with persistent overflow deaths");
            foreach(var rag in pool)((GameObject)rag.GetType().GetField("Root").GetValue(rag)).transform.SetParent(root.transform);
            foreach(float sign in new[]{-1f,1f})
            {
                var death=new SoldierDeath{Kind=UnitKind.Cavalry,Cause=DeathCause.Roundshot,Impulse=Vector3.right*sign*8,Velocity=Vector3.forward*8};
                var start=CavalryCasualtyMotion.Sample(death,0);var end=CavalryCasualtyMotion.Sample(death,2);var late=CavalryCasualtyMotion.Sample(death,20);
                Require(start.HorsePosition==Vector3.zero&&start.HorseYaw==0,"Horse starts at struck pose");
                Require(Mathf.Sign(end.HorseYaw)==sign&&end.HorsePosition.magnitude<4,"Horse reacts in impact direction with bounded skid");
                Require(end.HorsePosition==late.HorsePosition&&end.HorseYaw==late.HorseYaw&&end.HorsePhase==1,"Horse corpse remains settled");
            }
        }
        finally
        {
            Set("physicsScene",default(Scene));UnityEngine.Object.DestroyImmediate(root);
            RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(material);
            Physics.simulationMode=mode;
            if(Array.Exists(setup,s=>s.isLoaded&&s.isActive&&!string.IsNullOrEmpty(s.path)))EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
    }
}
