using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Empire.UI;
using EmpireBattle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Asset-level offscreen checks: no campaign, battle simulation or playtest app is launched.
public static class RegimentPortraitVerification
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool condition,string label){if(!condition)throw new Exception("Regiment portrait: "+label);}
    static object Field(RegimentPortrait portrait,string name)=>typeof(RegimentPortrait).GetField(name,Private).GetValue(portrait);
    static void Initialize(RegimentPortrait portrait)
    {if(Field(portrait,"portraitCamera")==null)typeof(RegimentPortrait).GetMethod("Awake",Private).Invoke(portrait,null);}
    static RegimentRecord Record(string faction="france",UnitRole role=UnitRole.Line,EquipmentUsers users=EquipmentUsers.Line,string weapon="musket")=>new RegimentRecord
    {Id="preview-verification",Name="Portrait verification",Faction=faction,BattleRole=role,Users=users,Tier=1,Weapon=weapon,Loadout=new UnitLoadout(),Progression=new UnitProgression(),Strength=120,Capacity=120};
    public static void VerifyCounters()
    {
        var progression=new UnitProgression();progression.CreditKills(1234567);progression.CreditKills(-10);
        Require(progression.totalKills==1234567,"negative kills cannot reduce lifetime history");
        var copy=progression.Copy();copy.CreditKills(2000);
        Require(copy.totalKills==1236567&&progression.totalKills==1234567,"copies retain independent lifetime history");
        progression.Reinforce(60,60);Require(progression.totalKills==1234567,"replacements preserve lifetime kills");
        var restored=JsonUtility.FromJson<UnitProgression>(JsonUtility.ToJson(progression));
        Require(restored.totalKills==1234567&&restored.Valid,"existing save serializer preserves kill totals");
        var legacy=JsonUtility.FromJson<UnitProgression>("{\"xp\":0}");Require(legacy.totalKills==0&&legacy.Valid,"older saves default to zero kills");
        var record=Record();record.Progression=restored;record.CurrentKills=123;
        Require(record.TotalKills==1234690,"inspection includes current battle kills");
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("de-DE");
            Require(CommandUI.Number(1000)=="1,000"&&CommandUI.Number(1000000)=="1,000,000","large numbers use comma separators independently of system locale");
            Require(new RegimentStat("Range",12345,"#,0' m'","").Display=="12,345 m","numerical stats group large values");
        }
        finally {System.Globalization.CultureInfo.CurrentCulture=culture;}
        var capped=new UnitProgression {totalKills=long.MaxValue-1};capped.CreditKills(10);
        Require(capped.totalKills==long.MaxValue,"lifetime totals cannot overflow");
        Debug.Log("REGIMENT_COUNTERS_VERIFIED: lifetime kills, copy/save/legacy compatibility, replenishment, live totals and comma formatting.");
    }
    public static void Verify()
    {
        VerifyCounters();
        var portrait=RegimentPortrait.Create();Initialize(portrait);
        try
        {
            var record=Record();portrait.Configure(record);
            var steps=(IList)Field(portrait,"steps");
            Require(steps.Count>=10,"infantry has a complete standing, kneeling, firing and reload sequence");
            string[] expected={"Standing idle","Standing aim","Firing","Reloading","Kneeling down","Kneeling idle","Kneeling aim","Kneeling fire","Kneeling reload","Standing up"};
            foreach(var name in expected)Require(steps.Cast<object>().Any(step=>(string)step.GetType().GetField("Name").GetValue(step)==name),"sequence includes "+name);
            var sample=typeof(RegimentPortrait).GetMethod("Sample",BindingFlags.Static|BindingFlags.NonPublic);
            for(int i=0;i<steps.Count;i++)
            {
                var step=steps[i];float duration=(float)step.GetType().GetField("Duration").GetValue(step);
                for(int j=0;j<=4;j++)
                {
                    object[] arguments={step,duration*j/4f,0f};var mesh=(Mesh)sample.Invoke(null,arguments);
                    Require(mesh&&mesh.vertexCount>0,"every sequence state has geometry");
                    float blend=(float)arguments[2];Require(!float.IsNaN(blend)&&blend>=0&&blend<=1,"morph phases are bounded");
                }
                typeof(RegimentPortrait).GetMethod("BuildBridge",Private).Invoke(portrait,new[]{step,steps[(i+1)%steps.Count]});
                Require(Field(portrait,"bridge") is Mesh,"compatible baked meshes blend across every sequence boundary");
            }
            Render(portrait,steps[0],0,"standing.png");
            Render(portrait,steps[5],0,"kneeling.png");
            Render(portrait,steps[6],1,"aiming.png");
            record.Loadout.Equip("magazine-rifles",EquipmentUsers.Line);portrait.Configure(record);
            Require(((IList)Field(portrait,"steps")).Count>=10,"modern rifle kit resolves animated poses");
            foreach(string faction in new[]{"britain","austria","ming","india"})
            {portrait.Configure(Record(faction));Require(((IList)Field(portrait,"steps")).Count>0,"national unit resolves "+faction);}
            portrait.Configure(Record("france",UnitRole.Swordsman,EquipmentUsers.Melee));
            Require(((IList)Field(portrait,"steps")).Count==1,"swordsmen do not perform musket firing drills");
            portrait.Configure(Record("france",UnitRole.Cavalry,EquipmentUsers.Cavalry));
            Require(((IList)Field(portrait,"steps")).Count>0,"cavalry uses mounted idle poses");
            portrait.Configure(Record(users:EquipmentUsers.Artillery));Require(Field(portrait,"cannon")!=null,"battery preview has a field piece");
            Require(record.Loadout.items.Contains("magazine-rifles")&&record.Strength==120,"presentation preserves loadout and strength");
            var tabs=(string[])typeof(RegimentManagementPanel).GetField("Tabs",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            Require(tabs.SequenceEqual(new[]{"Equipment","Skill points","Abilities","Quartermaster"}),"only the four requested tabs remain");
            Debug.Log("REGIMENT_PORTRAIT_VERIFIED: animation coverage, pose handoffs, issued weapons, national models, mounted/artillery previews, simplified tabs, offscreen renders.");
        }
        finally {UnityEngine.Object.DestroyImmediate(portrait.gameObject);}
    }
    static void Render(RegimentPortrait portrait,object step,float time,string name)
    {
        var camera=(Camera)Field(portrait,"portraitCamera");
        var target=new RenderTexture(480,320,24,RenderTextureFormat.ARGB32);target.Create();
        var actor=new GameObject("Offscreen portrait verification"){layer=31};
        var previous=RenderTexture.active;Texture2D pixels=null;
        try
        {
            var sample=typeof(RegimentPortrait).GetMethod("Sample",BindingFlags.Static|BindingFlags.NonPublic);
            object[] arguments={step,time,0f};var mesh=(Mesh)sample.Invoke(null,arguments);
            actor.AddComponent<MeshFilter>().sharedMesh=mesh;
            actor.AddComponent<MeshRenderer>().sharedMaterial=(Material)Field(portrait,"material");
            var properties=new MaterialPropertyBlock();properties.SetFloat("_PoseBlend",(float)arguments[2]);properties.SetFloat("_Morph",1);
            properties.SetVector("_RegimentUniform",RegimentBaseStats.Uniform(1));actor.GetComponent<MeshRenderer>().SetPropertyBlock(properties);
            camera.aspect=1.5f;camera.transform.rotation=Quaternion.Euler(10,155,0);
            camera.transform.position=new Vector3(0,1,0)-camera.transform.forward*4.5f;
            var request=new UniversalRenderPipeline.SingleCameraRequest {destination=target};
            RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=target;pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
            var colors=pixels.GetPixels32();var background=colors[0];int foreground=colors.Count(c=>Math.Abs(c.r-background.r)+Math.Abs(c.g-background.g)+Math.Abs(c.b-background.b)>24);
            Require(foreground>2000,"render has visible textured unit pixels: "+name);
            string directory="Artifacts/RegimentPortraitVerification";Directory.CreateDirectory(directory);File.WriteAllBytes(Path.Combine(directory,name),pixels.EncodeToPNG());
        }
        finally
        {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(actor);target.Release();UnityEngine.Object.DestroyImmediate(target);if(pixels)UnityEngine.Object.DestroyImmediate(pixels);}
    }
}
