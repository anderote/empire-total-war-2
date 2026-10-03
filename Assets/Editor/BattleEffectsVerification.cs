using System;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class BattleEffectsVerification
{
    public static void PrepareResources()
    {
        const string path="Assets/Scripts/Battle/Effects/Resources/MusketFlashVariant.mat";
        var shader=Resources.Load<Shader>("MusketFlash");
        Require(shader,"Musket flash shader is available");
        if(!AssetDatabase.LoadAssetAtPath<Material>(path))
            AssetDatabase.CreateAsset(new Material(shader){enableInstancing=true},path);
        CalibrateMuzzles();
        AssetDatabase.SaveAssets();
    }
    static void CalibrateMuzzles()
    {
        var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        if(!library)return;
        AnimationClip fire=null;
        foreach(var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/NapoleonicInfantry/Animations/gp_musket_aimfire.FBX"))
            if(asset is AnimationClip clip&&!clip.name.StartsWith("__preview"))fire=clip;
        Require(fire,"Firing clip is available for muzzle calibration");
        for(int side=0;side<2;side++)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/NapoleonicInfantry/Prefabs/"+(side==0?"French":"British")+"Inspection.prefab");
            Require(prefab,"Infantry prefab is available for muzzle calibration");
            var rig=UnityEngine.Object.Instantiate(prefab);
            try
            {
                rig.transform.position=Vector3.zero;rig.transform.rotation=Quaternion.identity;
                var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
                var rod=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");
                Require(gun&&rod,"Rifle and ramrod authoring anchors exist");
                var parent=gun.transform.parent;var localPosition=gun.transform.localPosition;var localRotation=gun.transform.localRotation;var localScale=gun.transform.localScale;
                var grips=new EmpireBattle.Art.ClosedGripAuthoring(rig,gun.transform,rod);
                Vector3 muzzle=Vector3.zero,lockPosition=Vector3.zero;
                // Match interpolation between the central two baked firing frames exactly.
                for(int frame=3;frame<=4;frame++)
                {
                    gun.transform.SetParent(parent,false);gun.transform.localPosition=localPosition;gun.transform.localRotation=localRotation;gun.transform.localScale=localScale;
                    float phase=frame/7f;fire.SampleAnimation(rig,fire.length*phase);
                    gun.transform.SetParent(rig.transform,true);grips.Apply(true,false,phase);
                    var conversion=Matrix4x4.Rotate(Quaternion.Euler(0,180,0))*rig.transform.worldToLocalMatrix;
                    float tip=gun.GetComponent<MeshFilter>().sharedMesh.bounds.max.z;
                    muzzle+=conversion.MultiplyPoint3x4(gun.transform.TransformPoint(new Vector3(0,0,tip)))*.5f;
                    lockPosition+=conversion.MultiplyPoint3x4(gun.transform.TransformPoint(new Vector3(1f,1f,-25f)))*.5f;
                }
                Require(muzzle.y>.8f&&muzzle.y<2f&&muzzle.z>.4f&&muzzle.z<3f,"Muzzle anchor has plausible soldier-local coordinates");
                var set=side==0?library.French:library.British;set.MuzzleOffset=muzzle;set.LockOffset=lockPosition;
                Debug.Log("RIFLE_MUZZLE_CALIBRATED "+(side==0?"French":"British")+" muzzle="+muzzle+" lock="+lockPosition);
            }
            finally{UnityEngine.Object.DestroyImmediate(rig);}
        }
        EditorUtility.SetDirty(library);
    }
    [MenuItem("Empire Battle/Verify vivid battle effects")]
    public static void Verify()
    {
        PrepareResources();
        BattleBuild.Verify();
        MusketSmokeVerification.Verify();
        var world=new BattleWorld(new BattleConfig { BattalionsPerSide=1,SoldiersPerBattalion=500,
            InitialSeparation=100f,EnableLandscape=false,EnableAI=false });
        var combat=new CombatSystem(world);
        world.StepMovement(.05f);combat.Step(.05f);
        int late=0,early=0,scheduled=0;
        foreach(var unit in world.Battalions)
        {
            Require(unit.LastVolleyTime==world.Time,"Both facing formations fire");
            int count=0;
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
            {
                if(world.Soldiers.FireVolleys[i]!=unit.LastVolleyTime)continue;
                float delay=world.Soldiers.FireTimes[i]-unit.LastVolleyTime;
                Require(delay>=0&&delay<=RifleFireTiming.LatestShotDelay+.00001f,"Scheduled shot stays inside volley window");
                Require(Mathf.Abs(delay-RifleFireTiming.Delay(i,unit.Id,unit.LastVolleyTime))<.00001f,"Deterministic rifle timing");
                if(delay>RifleFireTiming.LatestShotDelay*.65f)late++;if(delay<RifleFireTiming.LatestShotDelay*.35f)early++;count++;scheduled++;
            }
            // Snapshot must include shooters who subsequently died in simultaneous fire.
            Require(count==unit.LastVolleySize && count<=world.Config.PlatoonFiles,"Volley snapshot captures only the current platoon before casualties");
        }
        Require(early>0&&late>0,"Volley has both early shots and late shooters");
        for(int i=0;i<combat.ShotSequence;i++)
        {
            var shot=combat.Shots[i];bool matched=false;
            for(int soldier=0;soldier<world.Soldiers.FireOrigins.Length;soldier++)
                if((shot.Start-world.Soldiers.FireOrigins[soldier]).sqrMagnitude<.00001f&&Mathf.Abs(shot.Time-world.Soldiers.FireTimes[soldier])<.00001f)
                {matched=true;break;}
            Require(matched,"Sampled projectile shares its rifle's muzzle and firing time");
            Require(shot.Duration>0&&!float.IsNaN(shot.Duration),"Finite positive projectile flight");
        }
        var surfaces=new SceneryImpactMap();
        surfaces.Register(new Bounds(new Vector3(40,1,0),new Vector3(.2f,2f,3f)),SmallArmsSurface.Wood);
        surfaces.Register(new Bounds(new Vector3(20,1,0),new Vector3(.2f,2f,3f)),SmallArmsSurface.Masonry);
        Require(surfaces.Trace(new Vector3(0,1,0),new Vector3(80,1,0),out Vector3 position,out Vector3 normal,out SmallArmsSurface kind)
            &&kind==SmallArmsSurface.Masonry&&Mathf.Abs(position.x-19.9f)<.01f&&normal==Vector3.left,"Nearest thin scenery surface wins");
        Require(!surfaces.Trace(new Vector3(0,1,0),new Vector3(10,1,0),out _,out _,out _),"No impact beyond projectile endpoint");
        foreach(string sound in new[] {"Musket","Volley","Echo","Cannon","Dirt","Shell"})
        {
            float[] samples=BattleAudio.GenerateSamples(sound,1.4f,22050,1815);
            double energy=0;
            foreach(float sample in samples)
            {Require(!float.IsNaN(sample)&&!float.IsInfinity(sample)&&Mathf.Abs(sample)<=1f,"Audio stays finite with headroom");energy+=sample*sample;}
            Require(energy>1,"Audio layer is audible rather than silent");
        }
        Debug.Log("BATTLE_EFFECTS_VERIFIED: "+scheduled+" scheduled rifles; early/late timing, matched projectiles, pre-casualty snapshots, saturated 40k smoke, scenery impacts and six finite audio layers.");
    }
    public static void BuildMac()
    {
        Verify();
        InfantryBattleBuild.BuildMac();
    }
    static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Battle effects verification failed: "+message);}
}
