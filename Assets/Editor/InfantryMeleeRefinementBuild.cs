using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EmpireBattle;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;

public static partial class InfantryAssetBuild
{
    // Targeted in-place refresh: retain mesh GUIDs, UV atlas, unrelated tracks,
    // command parties, casualties and the user's published application.
    static bool meleePreviewOnly, meleeStrikesOnly;
    public static void BakeSwordStrikeRefinements(){meleeStrikesOnly=true;try{BakeMeleeRefinements();}finally{meleeStrikesOnly=false;}}
    public static void BakeMeleePreview(){meleePreviewOnly=true;try{BakeMeleeRefinements();}finally{meleePreviewOnly=false;}}
    public static void BakeCartridgeMeleeRefinements()
    {
        prepared=true;Directory.CreateDirectory("Artifacts/MeleeRefinement");
        var report=new StringBuilder();RefineCartridgeMelee(report);BuildScene(false,3);AssetDatabase.SaveAssets();
        File.WriteAllText("Artifacts/MeleeRefinement/cartridge-bake.txt",report.ToString());Debug.Log("CARTRIDGE_MELEE_REFINED");
    }
    public static void BakeMeleeRefinements()
    {
        prepared=true;Directory.CreateDirectory("Artifacts/MeleeRefinement");Directory.CreateDirectory("Logs");
        var report=new StringBuilder();
        var common=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        common.hideFlags=HideFlags.DontUnloadUnusedAsset;
        try
        {
            for(int level=0;level<(meleeStrikesOnly?0:2);level++)
            {
                BuildScene(false,level==0?3:5);
                var inspection=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
                for(int side=0;side<2;side++)RefineMeleeLod(inspection.Soldiers[side],inspection.Clips[0],
                    (side==0?common.French:common.British).Lods[level],"Assets/Resources/BakedInfantry",level,report,false);
            }
            EditorUtility.SetDirty(common);AssetDatabase.SaveAssets();
        }
        finally{common.hideFlags=HideFlags.None;}
        string[] paths=AssetDatabase.FindAssets("t:UnitVisualLibrary").Select(AssetDatabase.GUIDToAssetPath)
            .Where(p=>p.StartsWith("Assets/Generated/UnitRoster/")||p.StartsWith("Assets/Resources/RegionalRoster/")).OrderBy(p=>p).ToArray();
        foreach(string path in paths)
        {
            var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(path);
            if(!asset||asset.BakeVersion==0||meleeStrikesOnly&&asset.Family!="sword"||meleePreviewOnly&&(asset.Faction!="ming"||asset.Family!="sword"))continue;
            asset.hideFlags=HideFlags.DontUnloadUnusedAsset;
            try
            {
                for(int level=0;level<asset.Set.Lods.Length;level++)
                {
                    BuildScene(false,level==0?4:5);
                    var inspection=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
                    var rig=inspection.Soldiers[0];rig.transform.position=Vector3.zero;rig.name=asset.Faction+"_"+asset.Family;
                    if(RegionalArmyRoster.Supports(asset.Faction))PrepareRegionalRig(rig,asset.Faction,asset.Family,level);
                    else PrepareMeleeRosterRig(rig,asset.Faction,asset.Family,level==0?4:5,report);
                    rosterSword=asset.Family=="sword";
                    if(rosterSword)CreateMeleeSword(rig);
                    RefineMeleeLod(rig,inspection.Clips[0],asset.Set.Lods[level],Path.GetDirectoryName(path),level,report,rosterSword);
                }
                if(asset.Family=="sword"){
                    UseSwordCarryMaterial(asset.Set);EnsureSwordResourceReference(asset);
                }
                EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
                Debug.Log("MELEE_ROSTER_REFINED "+asset.Faction+"/"+asset.Family);
            }
            finally{rosterSword=false;asset.hideFlags=HideFlags.None;}
        }
        if(!meleeStrikesOnly)RefineCartridgeMelee(report);
        BuildScene(false,3);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Artifacts/MeleeRefinement/bake.txt",report.ToString());
        Debug.Log((meleePreviewOnly?"MELEE_PREVIEW_BAKED ":"MELEE_REFINEMENTS_BAKED ")+"sword carry/readiness/cuts, musket stocks, cartridge melee; no player publication");
    }
    static void RefineMeleeLod(GameObject rig,AnimationClip clip,InfantryCrowdLod lod,string output,int level,StringBuilder report,bool sword)
    {
        AssetDatabase.StartAssetEditing();
        try{RefineMeleeLodImpl(rig,clip,lod,output,level,report,sword);}
        finally{AssetDatabase.StopAssetEditing();}
    }
    static void EnsureSwordResourceReference(UnitVisualLibrary asset)
    {
        if(asset.Family!="sword"||RegionalArmyRoster.Supports(asset.Faction))return;
        string dir="Assets/Resources/UnitRoster/"+asset.Faction+"-sword";Directory.CreateDirectory(dir);
        string path=dir+"/Reference.asset";var reference=AssetDatabase.LoadAssetAtPath<UnitVisualReference>(path);
        if(!reference){reference=ScriptableObject.CreateInstance<UnitVisualReference>();AssetDatabase.CreateAsset(reference,path);}
        reference.Library=asset;EditorUtility.SetDirty(reference);
    }
    static void UseSwordCarryMaterial(InfantryCrowdSet set)
    {
        var shader=Shader.Find("EmpireBattle/CrowdSword");
        if(!shader)throw new Exception("Sword carry shader missing");
        set.Material.shader=shader;set.Material.enableInstancing=true;EditorUtility.SetDirty(set.Material);
    }
    static void RefineMeleeLodImpl(GameObject rig,AnimationClip clip,InfantryCrowdLod lod,string output,int level,StringBuilder report,bool sword)
    {
        rig.transform.position=Vector3.zero;
        var maps=new Dictionary<string,Rect>();
        foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)maps[m.name]=new Rect(0,0,1,1);
        var atlas=(Vector2[])lod.Idle[0].uv.Clone();
        Mesh[] Bake(InfantryMotionKind? kind,int count,bool loop,bool kneel=false,int variant=-1,bool carry=false)
        {
            Mesh[] poses=BakePoses(rig,clip,count,maps,output,level,report,kind,kneel,variant);
            Mesh[] guards=null;
            if(carry){
                swordGuardBake=true;
                try{guards=BakePoses(rig,clip,count,maps,output,level,report,kind,kneel,variant);}
                finally{swordGuardBake=false;}
            }
            for(int frame=0;frame<count;frame++)
            {
                var mesh=poses[frame];int next=frame+1<count?frame+1:loop?0:frame;
                if(mesh.vertexCount!=atlas.Length)throw new Exception("Melee topology/atlas changed: "+mesh.name+" "+mesh.vertexCount+" vs "+atlas.Length);
                mesh.uv=atlas;mesh.SetUVs(2,new List<Vector3>(poses[next].vertices));mesh.SetUVs(3,new List<Vector3>(poses[next].normals));
                var bounds=mesh.bounds;bounds.Encapsulate(poses[next].bounds);
                if(guards!=null){
                    if(guards[frame].vertexCount!=mesh.vertexCount)throw new Exception("Sword guard topology changed");
                    mesh.SetUVs(4,guards[frame].vertices.Select(v=>new Vector4(v.x,v.y,v.z,2)).ToList());
                    mesh.SetUVs(5,new List<Vector3>(guards[next].vertices));mesh.SetUVs(6,new List<Vector3>(guards[frame].normals));mesh.SetUVs(7,new List<Vector3>(guards[next].normals));
                    bounds.Encapsulate(guards[frame].bounds);bounds.Encapsulate(guards[next].bounds);
                }
                mesh.bounds=bounds;EditorUtility.SetDirty(mesh);
            }
            if(guards!=null)foreach(var mesh in guards)UnityEngine.Object.DestroyImmediate(mesh);
            return poses;
        }
        if(sword&&!meleeStrikesOnly)
        {
            lod.Idle=Bake(null,lod.Idle.Length,true,carry:true);lod.Fire=lod.Idle;lod.Reload=lod.Idle;
            lod.March=Bake(InfantryMotionKind.March,lod.March.Length,true,carry:true);
            lod.Run=Bake(InfantryMotionKind.Run,lod.Run.Length,true,carry:true);
            lod.Charge=Bake(InfantryMotionKind.Charge,lod.Charge.Length,true,carry:true);
            if(lod.Locomotion!=null)for(int v=0;v<lod.Locomotion.Length;v++){
                var g=lod.Locomotion[v];
                g.Walk=Bake(InfantryMotionKind.Walk,g.Walk.Length,true,variant:v,carry:true);g.March=Bake(InfantryMotionKind.March,g.March.Length,true,variant:v,carry:true);
                g.Jog=Bake(InfantryMotionKind.Jog,g.Jog.Length,true,variant:v,carry:true);g.Run=Bake(InfantryMotionKind.Run,g.Run.Length,true,variant:v,carry:true);g.Charge=Bake(InfantryMotionKind.Charge,g.Charge.Length,true,variant:v,carry:true);
            }
            if(lod.IdleVariants!=null)for(int v=0;v<lod.IdleVariants.Length;v++)lod.IdleVariants[v].Poses=Bake(InfantryMotionKind.Idle,lod.IdleVariants[v].Poses.Length,true,variant:v,carry:true);
            if(lod.UnderFireVariants!=null)for(int v=0;v<lod.UnderFireVariants.Length;v++)lod.UnderFireVariants[v].Poses=Bake(InfantryMotionKind.UnderFire,lod.UnderFireVariants[v].Poses.Length,true,variant:v,carry:true);
        }
        bool all=sword||lod.Melee==null||lod.Melee.Length==0;
        if(all)lod.Melee=new InfantryMeleeClip[6];
        var motions=new[]{InfantryMotionKind.MeleeReady,InfantryMotionKind.Thrust,InfantryMotionKind.Parry,InfantryMotionKind.ButtStrike,InfantryMotionKind.Stagger,InfantryMotionKind.Retreat};
        var actions=new[]{MeleeAction.Ready,sword?MeleeAction.SlashRight:MeleeAction.Thrust,MeleeAction.Parry,sword?MeleeAction.SlashLeft:MeleeAction.ButtStrike,MeleeAction.Stagger,MeleeAction.Retreat};
        for(int m=0;m<6;m++){
            if(!all&&actions[m]!=MeleeAction.ButtStrike)continue;
            var target=all?new InfantryMeleeClip{Action=actions[m]}:lod.Melee.First(c=>c.Action==actions[m]);
            target.Poses=Bake(motions[m],16,false);
            if(!sword)target.Kneeling=Bake(motions[m],16,false,true);
            if(all)lod.Melee[m]=target;
        }
    }
    static void RefineCartridgeMelee(StringBuilder report)
    {
        const string output="Assets/Resources/CartridgeRifles";
        var library=AssetDatabase.LoadAssetAtPath<CartridgeRifleLibrary>(output+"/Library.asset");
        if(!library)throw new Exception("Cartridge rifle library missing");
        library.hideFlags=HideFlags.DontUnloadUnusedAsset;cartridgeBake=true;
        try{
            for(int family=0;family<2;family++)for(int level=0;level<3;level++){
                BuildScene(false,level==0?3:5);var inspection=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
                for(int side=0;side<2;side++){
                    var rig=inspection.Soldiers[side];var gun=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
                    gun.GetComponent<MeshFilter>().sharedMesh=CartridgeWeaponAuthoring.Prepare(family==1,level);gun.sharedMaterial=CartridgeWeaponAuthoring.PrepareMaterial();
                    var set=side==0?(family==1?library.SingleFrench:library.French):(family==1?library.SingleBritish:library.British);
                    RefineMeleeLod(rig,inspection.Clips[0],set.Lods[level],output+(family==1?"/Single":""),level,report,false);
                }
            }
            EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        }finally{cartridgeBake=false;library.hideFlags=HideFlags.None;}
    }
    static void PrepareMeleeRosterRig(GameObject rig,string faction,string family,int lod,StringBuilder report)
    {
        int index=Array.IndexOf(UnitRoster.Factions,faction);if(index<0)index=8;
        foreach(var child in rig.transform.Cast<Transform>().ToArray())if(child.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0)UnityEngine.Object.DestroyImmediate(child.gameObject);
        var bones=rig.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        var recipe=RosterParts[index==8?0:index];var parts=new[]{recipe[family=="light"?1:0],recipe[2],recipe[3],recipe[4]};
        if(family=="light"){
            if(index==0){parts[1]="gap_infantry_pants_french_voltigeur.fbx";parts[2]="gap_french_shako_voltigeur.FBX";}
            if(index==1){parts[1]="gap_british_rifleman_pants.FBX";parts[2]="gap_british_stovepipe_rifle.FBX";parts[3]="gap_british_equipment_rifle.FBX";}
            if(index==2){parts[1]="gap_infantry_pants_austrian_jaeger.FBX";parts[2]="gap_austrian_jaeger_hat.FBX";parts[3]="gap_austrian_equipment_light.FBX";}
            if(index==3){parts[1]="gap_infantry_pants_prussian_jaeger.fbx";parts[3]="gap_prussian_equipment_light.FBX";}
            if(index==4){parts[1]="gap_russian_jaeger_pants.FBX";parts[2]="gap_russian_shako_jaeger.FBX";parts[3]="gap_infantry_equipment_russian_rifle.FBX";}
            if(index==5)parts[2]="gap_italian_shako_voltigeur.FBX";
        }
        foreach(string part in parts)AttachRosterPart(rig,bones,part,lod,report);
    }
    static void CreateMeleeSword(GameObject rig)
    {
        var sword=new GameObject("Roster sword");sword.transform.SetParent(rig.transform,false);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var source=cube.GetComponent<MeshFilter>().sharedMesh;
        var mesh=new Mesh{name="Prototype sword"};mesh.CombineMeshes(new[]{
            new CombineInstance{mesh=source,transform=Matrix4x4.TRS(new Vector3(0,.47f,0),Quaternion.identity,new Vector3(.035f,.82f,.012f))},
            new CombineInstance{mesh=source,transform=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(.025f,.16f,.025f))},
            new CombineInstance{mesh=source,transform=Matrix4x4.TRS(new Vector3(0,.07f,0),Quaternion.identity,new Vector3(.18f,.025f,.035f))}});
        UnityEngine.Object.DestroyImmediate(cube);sword.AddComponent<MeshFilter>().sharedMesh=mesh;sword.AddComponent<MeshRenderer>().sharedMaterial=MaterialFor("Ramrod",0);
    }
}
