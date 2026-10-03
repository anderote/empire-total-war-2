using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using EmpireBattle;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEditor;
using UnityEngine;

public static partial class InfantryAssetBuild
{
    // Reuses the inspected common rig and bind-pose correction. Source assets stay intact.
    static readonly string[][] RosterParts={
        new[]{"gap_french_infantry.FBX","gap_french_voltigeur.FBX","gap_infantry_pants_french_infantry.fbx","gap_french_shako_line.FBX","gap_french_equipment_beige.FBX"},
        new[]{"gap_british_infantry.FBX","gap_british_rifleman.FBX","gap_british_infantry_pants.FBX","gap_british_stovepipe_line.FBX","gap_british_equipment_infantry.FBX"},
        new[]{"gap_austrian_infantry.FBX","gap_austrian_jaeger.FBX","gap_infantry_pants_austrian.FBX","gap_austrian_shako.FBX","gap_austrian_equipment_infantry.FBX"},
        new[]{"gap_prussian_infantry.FBX","gap_prussian_rifleman.FBX","gap_infantry_pants_prussian.fbx","gap_prussian_shako_line.FBX","gap_prussian_equipment_infantry.FBX"},
        new[]{"gap_russian_infantry.FBX","gap_russian_rifleman.FBX","gap_russian_musketeer_pants.FBX","gap_russian_shako.FBX","gap_infantry_equipment_russian.FBX"},
        new[]{"gap_italian_line.FBX","gap_italian_voltigeur.FBX","gap_infantry_pants_french_infantry.fbx","gap_italian_shako.FBX","gap_french_equipment_beige.FBX"},
        new[]{"gap_hungarian_infantry_19.FBX","gap_austrian_grenzer.FBX","gap_austrian_hungarian_pants.FBX","gap_austrian_shako.FBX","gap_austrian_equipment_infantry.FBX"},
        new[]{"gap_ottoman_shortcoat_blue.FBX","gap_ottoman_shortcoat_green.FBX","gap_ottoman_trousers_white.FBX","gap_janissary_hat.FBX","gap_french_equipment_beige.FBX"}
    };
    static bool groundedRosterOnly;
    public static void BakeUnitRoster()
    {
        if(!groundedRosterOnly)VerifyCommandGrips();
        prepared=true;Directory.CreateDirectory("Logs");var report=new StringBuilder();
        foreach(string path in groundedRosterOnly||commandCoverageOnly?Array.Empty<string>():Directory.GetFiles(Root+"/RosterModels"))
        {
            if(!path.EndsWith(".FBX",StringComparison.OrdinalIgnoreCase))continue;
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType=ModelImporterAnimationType.Generic;importer.importAnimation=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.SaveAndReimport();
        }
        for(int faction=0;faction<9;faction++)foreach(string family in commandCoverageOnly?new[]{"line"}:new[]{"line","light","sword"})
        {
            string id=faction==8?"generic":UnitRoster.Factions[faction];
            if(family=="line"&&faction<2)continue;
            string output="Assets/Generated/UnitRoster/"+id+"-"+family;Directory.CreateDirectory(output);
            string path=output+"/Library.asset";var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(path);
            if(groundedRosterOnly&&(!asset||asset.BakeVersion==0))continue;
            int requiredVersion=family=="line"?3:2;
            if(!commandCoverageOnly&&asset&&asset.BakeVersion>=requiredVersion)continue;
            if(commandCoverageOnly&&(!asset||asset.BakeVersion==0))continue;
            if(!asset){asset=ScriptableObject.CreateInstance<UnitVisualLibrary>();AssetDatabase.CreateAsset(asset,path);}
            asset.hideFlags=HideFlags.DontUnloadUnusedAsset;asset.Faction=id;asset.Family=family;
            if(!groundedRosterOnly&&!commandCoverageOnly)asset.Set=new InfantryCrowdSet {Lods=new InfantryCrowdLod[2]};
            var common=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
            if(common){asset.Set.MuzzleOffset=common.French.MuzzleOffset;asset.Set.LockOffset=common.French.LockOffset;}
            Dictionary<string,Rect> atlasRects=null;
            for(int level=0;level<2;level++)
            {
                int lod=level==0?4:5;BuildScene(false,lod);
                var inspection=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
                var rig=inspection.Soldiers[0];rig.transform.position=Vector3.zero;
                rig.name=id+"_"+family;
                foreach(var child in rig.transform.Cast<Transform>().ToArray())
                    if(child.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0)UnityEngine.Object.DestroyImmediate(child.gameObject);
                var bones=rig.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
                var recipe=RosterParts[faction==8?0:faction];
                var parts=new[]{recipe[family=="light"?1:0],recipe[2],recipe[3],recipe[4]};
                // Rifles/light units use their supplied matching trousers, hats and equipment where present.
                if(family=="light")
                {
                    if(faction==0){parts[1]="gap_infantry_pants_french_voltigeur.fbx";parts[2]="gap_french_shako_voltigeur.FBX";}
                    if(faction==1){parts[1]="gap_british_rifleman_pants.FBX";parts[2]="gap_british_stovepipe_rifle.FBX";parts[3]="gap_british_equipment_rifle.FBX";}
                    if(faction==2){parts[1]="gap_infantry_pants_austrian_jaeger.FBX";parts[2]="gap_austrian_jaeger_hat.FBX";parts[3]="gap_austrian_equipment_light.FBX";}
                    if(faction==3){parts[1]="gap_infantry_pants_prussian_jaeger.fbx";parts[3]="gap_prussian_equipment_light.FBX";}
                    if(faction==4){parts[1]="gap_russian_jaeger_pants.FBX";parts[2]="gap_russian_shako_jaeger.FBX";parts[3]="gap_infantry_equipment_russian_rifle.FBX";}
                    if(faction==5)parts[2]="gap_italian_shako_voltigeur.FBX";
                }
                foreach(string part in parts)AttachRosterPart(rig,bones,part,lod,report);
                if(family=="sword")
                {
                    var sword=new GameObject("Roster sword");sword.transform.SetParent(rig.transform,false);
                    var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var source=cube.GetComponent<MeshFilter>().sharedMesh;
                    var mesh=new Mesh {name="Prototype sword"};mesh.CombineMeshes(new[]{
                        new CombineInstance {mesh=source,transform=Matrix4x4.TRS(new Vector3(0,.47f,0),Quaternion.identity,new Vector3(.035f,.82f,.012f))},
                        new CombineInstance {mesh=source,transform=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(.025f,.16f,.025f))},
                        new CombineInstance {mesh=source,transform=Matrix4x4.TRS(new Vector3(0,.07f,0),Quaternion.identity,new Vector3(.18f,.025f,.035f))}});
                    UnityEngine.Object.DestroyImmediate(cube);sword.AddComponent<MeshFilter>().sharedMesh=mesh;
                    sword.AddComponent<MeshRenderer>().sharedMaterial=MaterialFor("Ramrod",0);
                }
                if(level==0)atlasRects=PackRosterAtlas(rig,output,asset.Set);
                var clips=inspection.Clips;var data=groundedRosterOnly||commandCoverageOnly?asset.Set.Lods[level]:new InfantryCrowdLod {MaxDistance=level==0?40:100000};asset.Set.Lods[level]=data;
                rosterSword=family=="sword";
                try
                {
                    if(!groundedRosterOnly&&!commandCoverageOnly){
                    data.Idle=BakePoses(rig,clips[0],4,atlasRects,output,level,report);
                    data.March=BakePoses(rig,clips[0],8,atlasRects,output,level,report,InfantryMotionKind.March);
                    data.Run=BakePoses(rig,clips[0],8,atlasRects,output,level,report,InfantryMotionKind.Run);
                    data.Charge=BakePoses(rig,clips[0],8,atlasRects,output,level,report,InfantryMotionKind.Charge);
                    data.Hit=BakePoses(rig,clips[0],4,atlasRects,output,level,report,InfantryMotionKind.Hit);
                    }
                    if(!commandCoverageOnly)BakeGroundedLod(rig,clips[0],data,atlasRects,output,level,report,true);
                    if(!groundedRosterOnly&&!commandCoverageOnly){
                    var motions=new[]{InfantryMotionKind.MeleeReady,InfantryMotionKind.Thrust,InfantryMotionKind.Parry,InfantryMotionKind.ButtStrike,InfantryMotionKind.Stagger,InfantryMotionKind.Retreat};
                    var actions=new[]{MeleeAction.Ready,MeleeAction.Thrust,MeleeAction.Parry,MeleeAction.ButtStrike,MeleeAction.Stagger,MeleeAction.Retreat};
                    data.Melee=new InfantryMeleeClip[6];
                    for(int m=0;m<6;m++)
                    {
                        var action=rosterSword&&m==1?MeleeAction.SlashRight:rosterSword&&m==3?MeleeAction.SlashLeft:actions[m];
                        data.Melee[m]=new InfantryMeleeClip {Action=action,Poses=BakePoses(rig,clips[0],8,atlasRects,output,level,report,motions[m])};
                        if(!rosterSword)data.Melee[m].Kneeling=BakePoses(rig,clips[0],8,atlasRects,output,level,report,motions[m],true);
                    }
                    if(!rosterSword)
                    {
                        data.Fire=BakePoses(rig,clips[1],8,atlasRects,output,level,report);
                        data.Reload=BakePoses(rig,clips[2],24,atlasRects,output,level,report);
                        data.KneelingFire=BakePoses(rig,clips[1],8,atlasRects,output,level,report,null,true);
                        data.KneelingReload=BakePoses(rig,clips[2],24,atlasRects,output,level,report,null,true);
                    }
                    else {data.Fire=data.Idle;data.Reload=data.Idle;}
                    }
                    if(!rosterSword&&family=="line")
                    {
                        if(!groundedRosterOnly){data.Command=new InfantryCommandVariant[5];
                        for(int role=0;role<5;role++)data.Command[role]=new InfantryCommandVariant();}
                        var cloth=rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.enabled).sharedMaterial;
                        var gun=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
                        var rod=rig.GetComponentsInChildren<Renderer>().First(r=>r.name=="Authored ramrod");
                        foreach(int role in new[]{0,1,2,3,4})for(int sequence=0;sequence<(role==0?10:role==3?9:2);sequence++)
                        {
                            if(groundedRosterOnly&&(sequence<4||sequence>6))continue;
                            var command=data.Command[role];
                            var kind=role==0?InfantryMotionKind.Officer:role==1?InfantryMotionKind.Drummer:role==2?InfantryMotionKind.Standard:role==3?InfantryMotionKind.Sergeant:InfantryMotionKind.Colonel;
                            clips[0].SampleAnimation(rig,0);
                            var equipment=InfantryCommandAuthoring.Create(rig,kind,cloth,rod.sharedMaterial,gun.sharedMaterial,0,sequence==1);
                            bool kneel=role==0?sequence>=8:role==3&&sequence>=7;
                            int variant=kneel?(role==0?(sequence==8?0:3):(sequence==7?2:3)):sequence;
                            var poses=BakePoses(rig,clips[0],12,atlasRects,output,level,report,kind,kneel,variant);
                            if(sequence==0)command.Idle=poses;else if(sequence==1)command.March=poses;
                            else if(kneel){if((role==0&&sequence==8)||(role==3&&sequence==7))command.KneelingIdle=poses;else command.KneelingStrike=poses;}
                            else if(role==0){if(sequence==2)command.OfficerReady=poses;else if(sequence==3)command.OfficerSlash=poses;else if(sequence==4)command.OfficerKnockdown=poses;else if(sequence==5)command.OfficerGetUp=poses;else if(sequence==6)command.OfficerDeath=poses;else command.OfficerFire=poses;}
                            else if(sequence==2)command.PikeReady=poses;else if(sequence==3)command.PikeThrust=poses;else if(sequence==4)command.PikeKnockdown=poses;else if(sequence==5)command.PikeGetUp=poses;else command.PikeDeath=poses;
                            InfantryCommandAuthoring.Destroy(equipment,rig);
                        }
                    }
                    SyncCommandRecovery(data);
                    PrepareRosterBlends(data);
                    if(rosterSword&&!groundedRosterOnly&&!commandCoverageOnly){RefineMeleeLod(rig,clips[0],data,output,level,report,true);UseSwordCarryMaterial(asset.Set);EnsureSwordResourceReference(asset);}
                }
                finally {rosterSword=false;}
            }
            if(!commandCoverageOnly&&family!="sword")
            {
                var temporary=ScriptableObject.CreateInstance<InfantryCrowdLibrary>();temporary.French=asset.Set;temporary.British=asset.Set;
                InfantryPostureBuild.Prepare(temporary);UnityEngine.Object.DestroyImmediate(temporary);
            }
            if(!commandCoverageOnly)asset.BakeVersion=requiredVersion;asset.hideFlags=HideFlags.None;EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/unit-roster-bake.txt",report.ToString());Debug.Log("UNIT_ROSTER_BAKED "+id+" "+family);
        }
        if(!groundedRosterOnly&&!commandCoverageOnly)UnitRosterVerification.Verify();
    }
    static void AttachRosterPart(GameObject rig,Dictionary<string,Transform> bones,string file,int lod,StringBuilder report)
    {
        string path=Root+"/RosterModels/"+file;var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(!source)throw new Exception("Missing roster part "+path);
        var obj=UnityEngine.Object.Instantiate(source);obj.transform.SetParent(rig.transform,false);
        foreach(var a in obj.GetComponentsInChildren<Animator>())UnityEngine.Object.DestroyImmediate(a);
        foreach(var g in obj.GetComponentsInChildren<LODGroup>())UnityEngine.Object.DestroyImmediate(g);
        int enabled=0;
        foreach(var r in obj.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            r.enabled=r.name.ToLowerInvariant().Contains(".lod"+lod);if(!r.enabled)continue;enabled++;
            var mesh=UnityEngine.Object.Instantiate(r.sharedMesh);var original=r.bones;var bind=mesh.bindposes;
            for(int i=0;i<bind.Length;i++)
            {
                if(!bones.ContainsKey(original[i].name))throw new Exception("Unknown roster bone "+original[i].name);
                var from=original[i].lossyScale;var to=bones[original[i].name].lossyScale;
                bind[i]=Matrix4x4.Scale(new Vector3(from.x/to.x,from.y/to.y,from.z/to.z))*bind[i];
            }
            mesh.bindposes=bind;r.sharedMesh=mesh;r.bones=Array.ConvertAll(original,b=>bones[b.name]);r.rootBone=bones["pelvis"];r.updateWhenOffscreen=true;
            r.sharedMaterials=Array.ConvertAll(r.sharedMaterials,m=>MaterialFor(RosterMaterial(m.name),0));
        }
        if(enabled==0)throw new Exception("No weighted LOD"+lod+" in "+file);
        report.AppendLine(rig.name+" LOD"+lod+" "+file+" parts="+enabled);
    }
    static readonly Dictionary<string,string> RosterMaterialNames=new Dictionary<string,string>
        {
            {"gap_french_voltigeur_old","gap_french_oldinfantry_voltigeur"},
            {"gap_french_epaulette_voltigeur","gap_french_voltigeur_epaulette_ranker"},
            {"gap_austrian_equipment_backpack_black","gap_austrian_equipment_backpack_jaeger"},
            {"gap_prussian_equipment_backpack_black","gap_prussian_equipment_backpack_jaeger"},
            {"gap_prussian_infantry","gap_prussian_fusilier"},
            {"gap_prussian_rifleman","gap_prussian_jaeger"},
            {"gap_russian_infantry","gap_russian_line"},
            {"gap_ottoman_shortcoat_blue","gap_ottoman_opencoat_lightblue"},
            {"gap_ottoman_shortcoat_green","gap_ottoman_opencoat_green"},
            {"gap_ottoman_trousers_white","gap_ottoman_trousers_white_red"},
            {"gap_british_stovepipe_rifle_cords","gap_british_stovepipe_rifle"}
        };
    static string RosterMaterial(string name)=>RosterMaterialNames.TryGetValue(name,out var mapped)?mapped:name;
    static Dictionary<string,Rect> PackRosterAtlas(GameObject rig,string output,InfantryCrowdSet set)
    {
        var materials=rig.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).SelectMany(r=>r.sharedMaterials).Concat(new[]{MaterialFor("CoarseWeapon",0)}).Concat(InfantryCommandAuthoring.Colours(0)).GroupBy(m=>m.name).Select(g=>g.First()).ToArray();
        var textures=new List<Texture2D>();var rects=new Dictionary<string,Rect>();
        foreach(var material in materials)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);var source=material.GetTexture("_BaseMap");
            if(source)texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));
            else {texture.SetPixels(new[]{material.color,material.color,material.color,material.color});texture.Apply();}
            textures.Add(texture);
        }
        var atlas=new Texture2D(2048,2048,TextureFormat.RGBA32,false);var packed=atlas.PackTextures(textures.ToArray(),4,2048);
        for(int i=0;i<materials.Length;i++)rects[materials[i].name]=packed[i];
        string atlasPath=output+"/Atlas.png";File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());AssetDatabase.ImportAsset(atlasPath);
        var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.SaveAndReimport();
        string materialPath=output+"/Crowd.mat";set.Material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!set.Material){set.Material=new Material(Shader.Find("EmpireBattle/CrowdInfantry"));AssetDatabase.CreateAsset(set.Material,materialPath);}
        set.Material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));set.Material.enableInstancing=true;EditorUtility.SetDirty(set.Material);
        foreach(var texture in textures)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(atlas);
        return rects;
    }
    static void PrepareRosterBlends(InfantryCrowdLod lod)
    {
        var loops=new[]{lod.Idle,lod.March,lod.Run,lod.Charge};var sequences=new List<Mesh[]>(loops);
        if(lod.RecoveryVariants!=null)foreach(var recovery in lod.RecoveryVariants)sequences.AddRange(new[]{recovery.Knockdown,recovery.GetUp});
        if(lod.DeathVariants!=null)foreach(var death in lod.DeathVariants)sequences.Add(death.Poses);
        sequences.AddRange(new[]{lod.GrenadeThrow,lod.Fire,lod.Reload,lod.KneelingFire,lod.KneelingReload,lod.Death,lod.Hit,lod.Knockdown,lod.GetUp});
        foreach(var clip in lod.Melee){sequences.Add(clip.Poses);sequences.Add(clip.Kneeling);}
        if(lod.Command!=null)foreach(var command in lod.Command){sequences.AddRange(new[]{command.Idle,command.March,command.PikeReady,command.PikeThrust,command.PikeKnockdown,command.PikeGetUp,command.PikeDeath,command.OfficerReady,command.OfficerSlash,command.OfficerFire,command.OfficerKnockdown,command.OfficerGetUp,command.OfficerDeath,command.KneelingIdle,command.KneelingStrike});}
        foreach(var poses in sequences.Distinct())
        {
            if(poses==null)continue;bool loop=loops.Contains(poses)||lod.Command!=null&&lod.Command.Any(c=>poses==c.Idle||poses==c.March||poses==c.PikeReady||poses==c.OfficerReady||poses==c.KneelingIdle);
            for(int i=0;i<poses.Length;i++)
            {
                var mesh=poses[i];var next=poses[i+1<poses.Length?i+1:loop?0:i];
                if(mesh.vertexCount!=next.vertexCount)throw new Exception("Roster topology mismatch "+mesh.name);
                mesh.SetUVs(2,new List<Vector3>(next.vertices));mesh.SetUVs(3,new List<Vector3>(next.normals));var b=mesh.bounds;b.Encapsulate(next.bounds);mesh.bounds=b;EditorUtility.SetDirty(mesh);
            }
        }
    }
    public static void BuildRosterPlaytests()
    {
        if(!groundedRosterOnly)UnitRosterVerification.Verify();UnitRosterBalanceReview.Review();
        BattleBuild.BuildMac();InfantryBattleBuild.BuildMac();CampaignBuild.BuildMac();
    }
}
