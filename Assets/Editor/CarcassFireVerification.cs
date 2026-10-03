using System;
using System.Reflection;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class CarcassFireVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception("Carcass fire: "+message);}
    static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Hidden).Invoke(target,args);
    public static void PrepareTextures()
    {
        foreach(string name in new[]{"CarcassFireFlipbook","CarcassSmokeFlipbook"})
        {
            string path="Assets/Scripts/Battle/Effects/Resources/"+name+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            Require(importer,"Missing flipbook: "+name);
            if(importer.wrapMode!=TextureWrapMode.Clamp||importer.textureCompression!=TextureImporterCompression.CompressedHQ)
            {
                importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.CompressedHQ;
                importer.mipmapEnabled=true;importer.isReadable=false;importer.maxTextureSize=2048;importer.SaveAndReimport();
            }
        }
    }
    public static void Verify()
    {
        PrepareTextures();
        var world=new BattleWorld(new BattleConfig {AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=128,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        var artillery=new ArtillerySystem(world);
        for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(400+i,400);
        var root=new GameObject("Burning unit verification");var cameraRoot=new GameObject("Fire verification camera");
        try
        {
            var camera=cameraRoot.AddComponent<Camera>();camera.transform.position=new Vector3(100,8,-15);camera.transform.LookAt(new Vector3(100,1,0));
            var smoke=root.AddComponent<MusketSmoke>();smoke.Initialize(world,camera);
            var cannon=root.AddComponent<CannonView>();cannon.Initialize(world,artillery,camera,smoke);
            var hazard=artillery.Hazards[0];hazard.Position=new Vector3(100,0,0);hazard.Type=ArtilleryAmmunition.Carcass;hazard.Remaining=80;
            world.Soldiers.Positions[0]=new Vector2(100,0);float health=world.Soldiers.Health[0];
            Call(artillery,"PulseHazard",hazard);Call(cannon,"Update");
            Require(cannon.VisibleBurningUnits==1,"Actual fire exposure ignites the exposed soldier only");
            Require(Mathf.Abs(world.Soldiers.Health[0]-(health-24))<.001f,"Ignition adds no extra damage pulse");
            Require(world.Soldiers.Health[1]==health,"Unexposed soldier remains unharmed");
            world.Soldiers.Positions[0]=new Vector2(120,0);
            typeof(BattleWorld).GetProperty("Time").SetValue(world,2f);Call(cannon,"Update");
            Require(cannon.VisibleBurningUnits==1,"Fire follows a moving soldier outside the hazard");
            bool smokeFollows=false;
            foreach(object puff in (Array)typeof(MusketSmoke).GetField("puffs",Hidden).GetValue(smoke))
            {
                var t=puff.GetType();if(!(bool)t.GetField("FireSmoke").GetValue(puff))continue;
                var origin=(Vector3)t.GetField("Origin").GetValue(puff);
                if(Mathf.Abs(origin.x-120)<.01f&&origin.y>1)smokeFollows=true;
            }
            Require(smokeFollows,"Smoke emits from moving soldier's body and leaves a trail");
            typeof(BattleWorld).GetProperty("Time").SetValue(world,6f);Call(cannon,"Update");
            Require(cannon.VisibleBurningUnits==0,"Leaving the hazard extinguishes attached effects after five seconds");
            Require(Mathf.Abs(world.Soldiers.Health[0]-(health-24))<.001f,"Visual burn-down cannot damage a soldier after leaving");
            for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(100,0);
            Call(artillery,"PulseHazard",hazard);Call(cannon,"Update");
            Require(cannon.VisibleBurningUnits==128,"Mass ignition respects the attached emitter budget");
            Require(cannon.FireCards<=398&&cannon.FireDrawCalls<=2,"One ground fire plus 128 burning soldiers batches into at most two flame draws");
            for(int i=0;i<5000;i++)smoke.EmitFireSmoke(Vector3.up,6,1,1);
            Require(smoke.ActiveFirePuffs==MusketSmoke.FireSmokeCapacity,"Fire smoke has its own 4096-puff budget");
            typeof(BattleWorld).GetProperty("Time").SetValue(world,50f);Call(smoke,"LateUpdate");
            Require(smoke.ActiveFirePuffs==0,"Expired fire smoke releases its entire emission budget");
            Debug.Log($"CARCASS_FIRE_VERIFIED: exposure, moving smoke, expiry, unchanged damage, 128-unit cap; {cannon.FireCards} cards/{cannon.FireDrawCalls} draws");
        }
        finally {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraRoot);}
    }
}
