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
using HorseArt=EmpireBattle.SupportingArt.ProceduralSupportingArt;
public static partial class InfantryAssetBuild
{
    public static void BakeRegionalArmies()
    {
        BakeRegionalInfantry();
        BakeRegionalMountedFaction("ming");BakeRegionalMountedFaction("india");
        RegionalArmyVerification.Verify();
    }
    public static void RefineRegionalArmies(){BakeRegionalInfantry();RegionalArmyVerification.Verify();RegionalArmyPreview.Export();}
    static void BakeRegionalInfantry()
    {

        prepared=true;Directory.CreateDirectory("Logs");var report=new StringBuilder();
        for(int faction=0;faction<2;faction++)foreach(string family in commandCoverageOnly?new[]{"line"}:new[]{"line","light","sword"})
        {
            string id=faction==0?"ming":"india";

            string output="Assets/Resources/RegionalRoster/"+id+"-"+family;Directory.CreateDirectory(output);
            string path=output+"/Library.asset";var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(path);
            if(groundedRosterOnly&&(!asset||asset.BakeVersion==0))continue;
            int requiredVersion=4;
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
                PrepareRegionalRig(rig,id,family,level);
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
                AssetDatabase.StartAssetEditing();rosterSword=family=="sword";
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
                    if(rosterSword&&!groundedRosterOnly&&!commandCoverageOnly){RefineMeleeLod(rig,clips[0],data,output,level,report,true);UseSwordCarryMaterial(asset.Set);}
                }
                finally {rosterSword=false;AssetDatabase.StopAssetEditing();}
            }
            if(!commandCoverageOnly&&family!="sword")
            {
                var temporary=ScriptableObject.CreateInstance<InfantryCrowdLibrary>();temporary.French=asset.Set;temporary.British=asset.Set;
                InfantryPostureBuild.Prepare(temporary);UnityEngine.Object.DestroyImmediate(temporary);
            }
            if(!commandCoverageOnly)asset.BakeVersion=requiredVersion;asset.hideFlags=HideFlags.None;EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/unit-roster-bake.txt",report.ToString());Debug.Log("UNIT_ROSTER_BAKED "+id+" "+family);
        }

    }
    static void BakeRegionalMountedFaction(string faction)
    {
        prepared=true;string output="Assets/Resources/RegionalCavalry/"+faction;Directory.CreateDirectory(output);
        var library=AssetDatabase.LoadAssetAtPath<CavalryCrowdLibrary>(output+"/CavalryCrowdLibrary.asset");
        if(!library){library=ScriptableObject.CreateInstance<CavalryCrowdLibrary>();AssetDatabase.CreateAsset(library,output+"/CavalryCrowdLibrary.asset");}
        library.hideFlags=HideFlags.DontUnloadUnusedAsset;if(!mountedTurnsOnly&&!mountedCasualtiesOnly){library.French=new CavalryCrowdSet{Lods=new CavalryCrowdLod[2]};library.British=new CavalryCrowdSet{Lods=new CavalryCrowdLod[2]};}
        var report=new StringBuilder();var factionMaps=new Dictionary<string,Rect>[2];
        for(int level=0;level<2;level++)
        {
            BuildScene(false,level==0?3:5);var inspect=UnityEngine.Object.FindFirstObjectByType<InfantryInspection>();
            for(int side=0;side<1;side++)
            {
                var rig=inspect.Soldiers[side];PrepareRegionalRig(rig,faction,"sword",level);rig.name=faction;inspect.Clips[0].SampleAnimation(rig,0);rig.transform.position=Vector3.zero;
                var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));var rod=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");
                var all=rig.GetComponentsInChildren<Renderer>();var cloth=Array.Find(rig.GetComponentsInChildren<SkinnedMeshRenderer>(),r=>r.enabled).sharedMaterial;
                var equipment=InfantryCommandAuthoring.Create(rig,InfantryMotionKind.Colonel,cloth,rod.GetComponent<Renderer>().sharedMaterial,gun.sharedMaterial,side);
                foreach(var r in equipment.GetComponentsInChildren<Renderer>())if(r.name.StartsWith("Revolver")||r.name.Contains("epaulette")||r.name.Contains("Colonel"))r.enabled=false;
                gun.enabled=false;rod.GetComponent<Renderer>().enabled=false;
                var horse=equipment.GetComponent<CommandHorseMarker>().Rig;
                foreach(var part in equipment.GetComponentsInChildren<Renderer>())
                    if(part.name.StartsWith("Saddle")||part.name.StartsWith("Stirrup")||part.name.StartsWith("Bridle")||part.name.StartsWith("Rein"))part.transform.SetParent(horse.Root.transform,true);
                var motion=new ProceduralInfantryRig(rig);var grip=new ClosedGripAuthoring(rig,gun.transform,rod,-.65f);
                var set=side==0?library.French:library.British;
                var maps=new Dictionary<string,Rect>();var textures=new List<Texture2D>();var names=new List<string>();
                foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)if(!names.Contains(m.name))
                {
                    names.Add(m.name);var t=new Texture2D(2,2,TextureFormat.RGBA32,false);var source=m.GetTexture("_BaseMap");
                    if(source)t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));else{t.SetPixels(new[]{m.color,m.color,m.color,m.color});t.Apply();}textures.Add(t);
                }
                if(level==0&&!names.Contains("CoarseWeapon")){names.Add("CoarseWeapon");var t=new Texture2D(2,2,TextureFormat.RGBA32,false);var c=new Color(.2f,.12f,.07f);t.SetPixels(new[]{c,c,c,c});t.Apply();textures.Add(t);}
                var atlas=new Texture2D(2048,2048,TextureFormat.RGBA32,false);var packed=atlas.PackTextures(textures.ToArray(),8,2048);for(int i=0;i<names.Count;i++)maps[names[i]]=packed[i];
                string atlasPath=output+"/"+rig.name+"Atlas.png";
                if(level==0){File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());AssetDatabase.ImportAsset(atlasPath);var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);importer.mipmapEnabled=true;importer.maxTextureSize=2048;importer.SaveAndReimport();}
                if(level==0)factionMaps[side]=maps;else maps=factionMaps[side];
                foreach(var t in textures)UnityEngine.Object.DestroyImmediate(t);UnityEngine.Object.DestroyImmediate(atlas);
                if(level==0)
                {
                    string path=output+"/"+rig.name+".mat";set.Material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!set.Material){set.Material=new Material(Shader.Find("EmpireBattle/CrowdInfantry"));AssetDatabase.CreateAsset(set.Material,path);}
                    set.Material.shader=Shader.Find("EmpireBattle/CrowdInfantry");set.Material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));set.Material.enableInstancing=true;EditorUtility.SetDirty(set.Material);
                }
                var lod=mountedTurnsOnly||mountedCasualtiesOnly?set.Lods[level]:new CavalryCrowdLod{MaxDistance=level==0?50:100000};set.Lods[level]=lod;var clips=mountedTurnsOnly||mountedCasualtiesOnly?new List<MountedClip>(lod.Clips):new List<MountedClip>();
                var baseRoot=horse.Root.transform.localPosition;var baseRotation=horse.Root.transform.localRotation;
                foreach(MountedMotion kind in Enum.GetValues(typeof(MountedMotion)))
                {
                    if(kind>MountedMotion.HorseIdle)continue;
                    bool turn=kind==MountedMotion.TurnLeft||kind==MountedMotion.TurnRight;
                    if(mountedTurnsOnly&&!turn)continue;
                    if(mountedCasualtiesOnly&&!IsRiderFall(kind)&&kind!=MountedMotion.HorseFall&&kind<MountedMotion.HorseWalk)continue;
                    clips.RemoveAll(c=>c.Motion==kind);
                    bool loop=kind<=MountedMotion.Charge||turn||kind>=MountedMotion.HorseWalk;int count=loop?16:20;var poses=new Mesh[count];
                    AssetDatabase.StartAssetEditing();
                    try { for(int frame=0;frame<count;frame++)
                    {
                        float phase=frame/(float)(loop?count:count-1);
                        var gait=kind==MountedMotion.Walk||kind==MountedMotion.HorseWalk||turn?HorseArt.HorseGait.Walk:kind==MountedMotion.Trot||kind==MountedMotion.HorseTrot?HorseArt.HorseGait.Trot:kind==MountedMotion.Gallop||kind==MountedMotion.HorseGallop||kind==MountedMotion.Charge?HorseArt.HorseGait.Gallop:HorseArt.HorseGait.Idle;
                        horse.Root.transform.localPosition=baseRoot;horse.Root.transform.localRotation=baseRotation;HorseArt.SampleGait(horse,gait,phase);
                        MeleeAction action=kind==MountedMotion.SlashLeft?MeleeAction.SlashLeft:kind==MountedMotion.SlashRight?MeleeAction.SlashRight:kind==MountedMotion.Brake?MeleeAction.Brake:kind==MountedMotion.Stumble?MeleeAction.Stumble:MeleeAction.None;
                        float bob=gait==HorseArt.HorseGait.Gallop?.027f:gait==HorseArt.HorseGait.Trot?.018f:gait==HorseArt.HorseGait.Walk?.008f:0;
                        motion.SampleMounted(action,phase,Mathf.Sin(phase*Mathf.PI*4)*bob);grip.ApplyMountedCombat(action,phase);
                        if(kind==MountedMotion.Charge){var spine=Array.Find(motion.Bones,t=>t.name=="spine");spine.localRotation=Quaternion.AngleAxis(-10,Vector3.right)*spine.localRotation;}
                        if(kind==MountedMotion.Brake||kind==MountedMotion.Stumble){horse.Neck.localRotation*=Quaternion.Euler(-15*Mathf.Sin(phase*Mathf.PI),0,0);horse.Torso.localRotation*=Quaternion.Euler(12*Mathf.Sin(phase*Mathf.PI),0,0);}
                        if(kind==MountedMotion.TurnLeft||kind==MountedMotion.TurnRight){float sign=kind==MountedMotion.TurnLeft?-1:1;horse.Neck.localRotation*=Quaternion.Euler(0,sign*25,0);horse.Torso.localRotation*=Quaternion.Euler(0,0,sign*5);}
                        if(kind==MountedMotion.HorseFall)horse.Root.transform.localRotation=baseRotation*Quaternion.Euler(0,0,88*Mathf.SmoothStep(0,1,phase));
                        if(IsRiderFall(kind))motion.SampleMountedFall(phase,kind==MountedMotion.RiderFallBack?0:kind==MountedMotion.RiderFallSide?2:kind==MountedMotion.RiderTumble?3:1);
                        poses[frame]=CombineMounted(rig,horse,kind,phase,maps,rig.name+"_L"+level+"_"+kind+"_"+frame);
                        string path=output+"/"+poses[frame].name+".asset";AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(poses[frame],path);
                    }
                    for(int i=0;i<poses.Length;i++){var mesh=poses[i];var next=poses[i+1<poses.Length?i+1:loop?0:i];if(mesh.vertexCount!=next.vertexCount)throw new Exception("Mounted pose topology differs");mesh.SetUVs(2,new List<Vector3>(next.vertices));mesh.SetUVs(3,new List<Vector3>(next.normals));var bounds=mesh.bounds;bounds.Encapsulate(next.bounds);mesh.bounds=bounds;EditorUtility.SetDirty(mesh);}
                    } finally {AssetDatabase.StopAssetEditing();}
                    if(mountedTurnsOnly)for(int old=count;old<20;old++)AssetDatabase.DeleteAsset(output+"/"+rig.name+"_L"+level+"_"+kind+"_"+old+".asset");
                    clips.Add(new MountedClip{Motion=kind,Loop=loop,Poses=poses});report.AppendLine($"{rig.name} L{level} {kind} poses={poses.Length} vertices={poses[0].vertexCount}");
                }
                lod.Clips=clips.ToArray();InfantryCommandAuthoring.Destroy(equipment,rig);
            }
        }
        BuildScene(false,3);library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();File.WriteAllText("Logs/cavalry-pose-bake.txt",report.ToString());
        Debug.Log("MELEE_CAVALRY_POSES_BAKED infantry standing/kneeling and mounted gait and varied casualty sequences at both LODs/factions");
    }
    [Serializable] class RegionalRecipes { public RegionalRecipe[] recipes; }
    [Serializable] class RegionalRecipe { public string name; public TroopSourceBuild.Mapping[] materials; }
    static void PrepareRegionalRig(GameObject rig,string faction,string family,int level)
    {
        foreach(var child in rig.transform.Cast<Transform>().ToArray())
            if(child.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0&&!child.name.StartsWith("gap_infantry_pants"))UnityEngine.Object.DestroyImmediate(child.gameObject);
        var bones=rig.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        string id=faction+"-"+family;const string sourceRoot="Assets/Art/TroopSources/RegionalArmies";
        var recipes=JsonUtility.FromJson<RegionalRecipes>(File.ReadAllText(sourceRoot+"/recipes.json"));
        var recipe=recipes.recipes.First(r=>r.name==id);
        var part=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourceRoot+"/"+id+".fbx"));part.transform.SetParent(rig.transform,false);
        foreach(var animator in part.GetComponentsInChildren<Animator>())UnityEngine.Object.DestroyImmediate(animator);
        int enabled=0;
        foreach(var skin in part.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.enabled=skin.name.Contains(".LOD"+level);if(!skin.enabled)continue;enabled++;
            var mesh=UnityEngine.Object.Instantiate(skin.sharedMesh);var original=skin.bones;var bind=mesh.bindposes;
            for(int i=0;i<bind.Length;i++)
            {
                if(!bones.ContainsKey(original[i].name))throw new Exception("Regional rig bone missing: "+original[i].name);
                var from=original[i].lossyScale;var to=bones[original[i].name].lossyScale;
                bind[i]=Matrix4x4.Scale(new Vector3(from.x/to.x,from.y/to.y,from.z/to.z))*bind[i];
            }
            mesh.bindposes=bind;skin.sharedMesh=mesh;skin.bones=Array.ConvertAll(original,b=>bones[b.name]);skin.rootBone=bones["pelvis"];skin.updateWhenOffscreen=true;
            skin.sharedMaterials=Array.ConvertAll(skin.sharedMaterials,m=>RegionalMaterial(id,m.name,recipe.materials));
        }
        if(enabled!=2)throw new Exception("Regional outfit needs one body and one headgear at LOD"+level+": "+id+" got "+enabled);
        if(faction=="ming"&&family=="light")
        {
            var neck=GameObject.CreatePrimitive(PrimitiveType.Cylinder);neck.name="Regional neck";
            UnityEngine.Object.DestroyImmediate(neck.GetComponent<Collider>());
            neck.transform.position=bones["head"].position-Vector3.up*.045f;
            neck.transform.localScale=new Vector3(.068f,.075f,.068f);neck.transform.SetParent(bones["head"],true);
            neck.GetComponent<Renderer>().sharedMaterial=MaterialFor("skin",0);
        }
        // Shared trousers supply the legs missing from modular Warband robes.
        foreach(var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            if(skin.enabled&&skin.name.Contains("gap_infantry_pants"))
            {
                const string path=sourceRoot+"/Materials/SharedTrousers.mat";Directory.CreateDirectory(sourceRoot+"/Materials");
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.name="RegionalTrousers";material.color=new Color(.42f,.36f,.25f);AssetDatabase.CreateAsset(material,path);}
                skin.sharedMaterials=Array.ConvertAll(skin.sharedMaterials,m=>material);
            }
    }
    static Material RegionalMaterial(string id,string name,TroopSourceBuild.Mapping[] mappings)
    {
        const string root="Assets/Art/TroopSources/RegionalArmies/Materials";Directory.CreateDirectory(root);
        string path=root+"/"+id+"-"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
        var mapping=mappings.FirstOrDefault(m=>m.name==name||name.StartsWith(m.name+"."));
        if(mapping==null)throw new Exception("Regional texture mapping missing: "+id+" "+name);
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(mapping.texture);if(!texture)throw new Exception("Regional texture missing: "+mapping.texture);
        material.name=id+"-"+name;material.SetTexture("_BaseMap",texture);material.SetFloat("_Smoothness",.15f);EditorUtility.SetDirty(material);return material;
    }
}
