using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static class MeleeRefinementVerification
{
    const string Output="Artifacts/MeleeRefinement";
    static void Require(bool value,string message){if(!value)throw new Exception("Melee refinement: "+message);}
    public static void BakeAndVerify(){InfantryAssetBuild.BakeMeleeRefinements();Verify();}
    public static void VerifySimulation(){VerifyState();VerifyReadiness();MeleePresentationVerification.Verify();InfantryContactVerification.Verify();Debug.Log("MELEE_REFINEMENT_SIMULATION_VERIFIED");}
    public static void VerifySwordResources()
    {
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");int count=0;
        foreach(string guid in AssetDatabase.FindAssets("t:UnitVisualLibrary")){
            var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(AssetDatabase.GUIDToAssetPath(guid));
            if(asset.BakeVersion==0||asset.Family!="sword")continue;
            var unit=new BattalionState{Faction=asset.Faction,Role=UnitRole.Swordsman,Side=ArmySide.French,Weapon=MilitaryEquipment.Weapon("musket")};
            Require(library.Resolve(unit).Material==asset.Set.Material,"Runtime resolves sword instead of rifle fallback: "+asset.Faction);count++;
        }
        Require(count>=8,"All sword resource references resolved");Debug.Log("SWORD_RESOURCES_VERIFIED "+count);
    }
    public static void Verify()
    {
        Directory.CreateDirectory(Output);var report=new List<string>();
        VerifyState();VerifyReadiness();VerifySwordResources();
        var shader=Shader.Find("EmpireBattle/CrowdSword");Require(shader&&!ShaderUtil.ShaderHasError(shader),"Sword shader compiles");
        int swords=0,poses=0;
        foreach(string guid in AssetDatabase.FindAssets("t:UnitVisualLibrary")){
            var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(AssetDatabase.GUIDToAssetPath(guid));
            if(asset.BakeVersion==0||asset.Family!="sword")continue;
            Require(asset.Set.Material.shader==shader,"Sword material uses carry blending: "+asset.Faction);
            foreach(var lod in asset.Set.Lods){
                foreach(var mesh in CarryMeshes(lod)){
                    var guard=new List<Vector4>();mesh.GetUVs(4,guard);
                    Require(guard.Count==mesh.vertexCount&&guard.All(v=>v.w==2&&float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Valid marked guard endpoint: "+mesh.name);
                    for(int ch=5;ch<=7;ch++){var values=new List<Vector3>();mesh.GetUVs(ch,values);Require(values.Count==mesh.vertexCount,"Complete guard interpolation: "+mesh.name);}
                    poses++;
                }
                foreach(float cell in new[]{.12f,.24f}){
                    var source=lod.March[0];var distant=DistantUnitMesh.Build(source,cell);
                    for(int ch=4;ch<=7;ch++){var values=new List<Vector4>();distant.GetUVs(ch,values);Require(values.Count==distant.vertexCount,"Distant sword retains guard attributes");if(ch==4)Require(values.All(v=>Mathf.Abs(v.w-2)<.0001f),"Distant sword retains explicit guard marker");}
                    if(distant!=source)UnityEngine.Object.DestroyImmediate(distant);
                }
                foreach(var action in new[]{MeleeAction.SlashLeft,MeleeAction.SlashRight}){
                    var clip=lod.Melee.First(c=>c.Action==action);Require(clip.Poses.Length>=16,"Adequate cutting samples");
                    foreach(var pose in clip.Poses){var optional=new List<Vector4>();pose.GetUVs(4,optional);Require(!optional.Any(v=>v.w>1.5f),"Attacks cannot use carry shader endpoints");}
                    var first=clip.Poses[0].vertices;var last=clip.Poses.Last().vertices;float delta=0;
                    for(int i=0;i<first.Length;i++)delta=Mathf.Max(delta,Vector3.Distance(first[i],last[i]));
                    Require(delta<.005f,"Slash returns to matching guard: "+asset.Faction+" "+action+" "+delta);
                }
            }
            swords++;
        }
        Require(swords>=8,"Every available baked roster sword family verified");
        var cartridge=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");int rifleTracks=0;
        var bayonets=Resources.Load<BayonetPresentationLibrary>("Bayonets/Library");Require(bayonets,"Bayonet library available for end-to-end weapon anchor checks");
        var common=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var set in new[]{common.French,common.British,cartridge.French,cartridge.British,cartridge.SingleFrench,cartridge.SingleBritish})foreach(var lod in set.Lods){
            Require(lod.Melee!=null&&lod.Melee.Length==6,"Rifle has six melee tracks, including cartridge upgrades");
            foreach(var action in new[]{MeleeAction.Thrust,MeleeAction.ButtStrike}){
                var track=lod.Melee.First(c=>c.Action==action);
                foreach(var frames in new[]{track.Poses,track.Kneeling}){
                    Require(frames!=null&&frames.Length>=8,"Standing and kneeling weapon action exists");
                    int index=AnimationPlayback.Frame(frames.Length,.5f,false,out float blend);
                    Require(bayonets.Locate(frames[index],out var socket,out var rot),"Fitted actual rifle at contact");
                    Require(bayonets.Locate(frames[index],out var next,out var nextRot,true),"Fitted interpolated rifle at contact");
                    var axis=Vector3.Lerp(rot*Vector3.forward,nextRot*Vector3.forward,blend).normalized;
                    Require(action==MeleeAction.Thrust?axis.z>.65f:axis.z<-.35f,"Contact leads with "+(action==MeleeAction.Thrust?"muzzle":"wooden stock")+": "+frames[index].name+" axis="+axis);
                    rifleTracks++;
                }
            }
        }
        MeleePresentationVerification.Verify();
        InfantryContactVerification.Verify();
        DistantUnitVerification.Verify();
        report.Add($"PASS {swords} sword families, {poses} carry meshes; GPU guard endpoints across authored/distant LODs; slashes return to guard.");
        report.Add($"PASS {rifleTracks} standing/kneeling contact tracks; fixed bayonets lead muzzle-first, bare rifles lead stock-first; cartridge tracks present.");
        report.Add("PASS real MeleeSystem fixed/unfixed selection, unfix cancellation before damage, readiness pause/reversal/fleeing, contact timing, infantry contact scalar/Burst parity and distant LOD regressions.");
        File.WriteAllLines(Output+"/verification.txt",report);Debug.Log("MELEE_REFINEMENT_VERIFIED "+string.Join(" ",report));
    }
    static IEnumerable<Mesh> CarryMeshes(InfantryCrowdLod lod)
    {
        foreach(var track in new[]{lod.Idle,lod.March,lod.Run,lod.Charge})foreach(var mesh in track)yield return mesh;
        if(lod.Locomotion!=null)foreach(var gait in lod.Locomotion)foreach(var track in new[]{gait.Walk,gait.March,gait.Jog,gait.Run,gait.Charge})foreach(var mesh in track)yield return mesh;
        if(lod.IdleVariants!=null)foreach(var track in lod.IdleVariants)foreach(var mesh in track.Poses)yield return mesh;
        if(lod.UnderFireVariants!=null)foreach(var track in lod.UnderFireVariants)foreach(var mesh in track.Poses)yield return mesh;
    }
    static BattleWorld Duel(bool fixedBayonet,out int attacker,out int target)
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        attacker=0;target=world.Battalions[1].SoldierStart;var s=world.Soldiers;
        for(int i=0;i<s.Alive.Length;i++){s.Alive[i]=i==attacker||i==target;s.Captains[i]=s.PikeSergeants[i]=false;s.Positions[i]=new Vector2(200+i,200);}
        foreach(var u in world.Battalions){u.AliveCount=1;u.Role=UnitRole.Line;u.Cohesion=1;u.FireAtWill=false;u.BayonetsFixed=fixedBayonet;}
        world.Battalions[0].Position=Vector2.zero;world.Battalions[1].Position=Vector2.up*.9f;
        s.Positions[attacker]=Vector2.zero;s.Positions[target]=Vector2.up*.9f;s.Facings[attacker]=0;s.Facings[target]=0;
        s.MeleeTargets[attacker]=target;s.MeleeReadyTimes[attacker]=-1;world.Battalions[1].Cohesion=0;
        return world;
    }
    static void VerifyState()
    {
        foreach(bool fixedBayonet in new[]{false,true}){
            var w=Duel(fixedBayonet,out int a,out int t);new MeleeSystem(w).Step(.05f,new bool[2]);
            Require(w.Soldiers.MeleeActions[a]==(fixedBayonet?MeleeAction.Thrust:MeleeAction.ButtStrike),"Actual engagement selects weapon-correct strike");
        }
        foreach(float when in new[]{.1f,.77f,.79f}){
            var w=Duel(true,out int a,out int t);var melee=new MeleeSystem(w);melee.Step(.05f,new bool[2]);
            Require(w.Soldiers.MeleeActions[a]==MeleeAction.Thrust,"Thrust committed before unfix");
            typeof(BattleWorld).GetProperty("Time").SetValue(w,when);
            Require(w.SetBayonets(0,false),"Unfix command accepted");float health=w.Soldiers.Health[t];melee.Step(.05f,new bool[2]);
            Require(w.Soldiers.MeleeActions[a]!=MeleeAction.Thrust&&w.Soldiers.Health[t]==health,"No queued invisible-blade hit after unfix at "+when);
        }
        Require(MeleeSystem.SelectAttack(true,false,false,0)==MeleeAction.Thrust,"Pike remains a thrust without bayonet flag");
        Require(MeleeSystem.SelectAttack(false,true,false,-1)==MeleeAction.SlashLeft&&MeleeSystem.SelectAttack(false,true,true,1)==MeleeAction.SlashRight,"Swords do not depend on bayonet flag");
    }
    static void VerifyReadiness()
    {
        var w=Duel(false,out int a,out _);var u=w.Battalions[0];u.Role=UnitRole.Swordsman;
        var readiness=new SwordReadiness(w.Soldiers.Alive.Length);
        Require(readiness.Sample(w,u,a,0)==0,"Normal carry starts relaxed");u.AttackMode=InfantryAttackMode.BayonetCharge;
        float half=readiness.Sample(w,u,a,.19f);Require(half>.1f&&half<.9f,"Raising blends through intermediate pose");
        Require(readiness.Sample(w,u,a,.19f)==half,"Pause cannot advance presentation");
        u.AttackMode=InfantryAttackMode.None;
        float reversed=readiness.Sample(w,u,a,.24f);Require(reversed<half&&reversed>0,"Reversal continues from current pose");
        u.InMelee=true;Require(readiness.Sample(w,u,a,1)==1,"Combat reaches full guard");
        u.Routing=true;Require(readiness.Sample(w,u,a,2)==0,"Fleeing lowers carry");
    }
}
