using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using EmpireBattle;
using EmpireBattle.Art;
using EmpireBattle.AnimationAuthoring;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using HorseArt=EmpireBattle.SupportingArt.ProceduralSupportingArt;
public static partial class InfantryAssetBuild
{
    static Action<GameObject,float> supplementalPose;
    static string supplementalPoseName;
    static bool cartridgeOutfitSingle;
    public static void BakeNapoleonicOutfits()
    {
        PrepareOutfitImports();NapoleonicOutfits.ClearCache();
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-outfit");string selected=at>=0?args[at+1]:null;
        var report=new StringBuilder();
        foreach(var outfit in NapoleonicOutfits.All.Where(o=>selected==null||selected==o.Id))
        {
            string output="Assets/Resources/NapoleonicOutfits/"+outfit.Id;Directory.CreateDirectory(output);
            if(outfit.Mounted)BakeOutfitMounted(outfit,output,report);else BakeOutfitInfantry(outfit,output,report);
            AssetDatabase.SaveAssets();CompactOutfitData(output);File.WriteAllText("Artifacts/NapoleonicOutfits/bake.txt",report.ToString());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
            Debug.Log("NAPOLEONIC_OUTFIT_BAKED "+outfit.Id);
        }
    }
    static void PrepareOutfitImports()
    {
        var catalog=JsonUtility.FromJson<NapoleonicOutfitCatalog>(Resources.Load<TextAsset>("NapoleonicOutfits/Catalog").text);
        foreach(string file in NapoleonicOutfits.All.SelectMany(o=>o.Parts).Concat(catalog.Commands.SelectMany(c=>c.CaptainParts.Concat(c.ColonelParts))).Distinct())
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath(Root+"/RosterModels/"+file);
            if(importer.animationType==ModelImporterAnimationType.Generic&&!importer.importAnimation)continue;
            importer.animationType=ModelImporterAnimationType.Generic;importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.SaveAndReimport();
        }
        var maps=new Dictionary<string,string>{
            {"bl_british_coldstream_shako_ranker","british_coldstream_shako_ranker"},{"bl_british_shako_plume","british_coldstream_shako_plume"},
            {"gap_british_coldstream","gap_british_guard"},{"gap_austrian_bearskin","gap_austrian_grenadier_bearskin"},{"gap_austrian_bearskin_fur","gap_bearskin_fur"},
            {"gap_austrian_hussar_cords","gap_austrian_hussar_shako"},{"gap_french_epaulette_red","gap_french_84e_epaulette_ranker"},
            {"gap_french_grenadier_old","gap_french_grenadier"},{"gap_french_infantry_vistula_old","gap_french_oldinfantry_vistula"},
            {"gap_french_guard_bearskin_plume","gap_plumes_red"},{"gap_french_natguard","gap_french_reworked_natguard"},
            {"gap_hungarian_hussar_boots","gap_hungarian_hussar_pants"},{"gap_hussar_accessoire_austrian","gap_hussar_accessoire_austria"},
            {"gap_hussar_accessoire_france","gap_hussar_accessoire_french"},{"gap_hussar_accessoire_russia","gap_hussar_accessoire_russian"},
            {"gap_russian_musketeer_pants_2","gap_russian_musketeer_pants2"},{"body_male_a","skin"}};
        foreach(var color in new Dictionary<string,int>{{"brown",2},{"dark",4},{"green",5},{"orange",3},{"white",1}})maps["gap_ottoman_greatcoat_"+color.Key]="gap_ottoman_greatcoat_"+color.Value;
        foreach(var pair in maps)RosterMaterialNames[pair.Key]=pair.Value;
    }
    static GameObject OutfitRig(NapoleonicOutfit outfit,int level,StringBuilder report,bool clearScene=true)
    {
        if(clearScene)EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var rig=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/FrenchInspection.prefab"));rig.name=outfit.Id;rig.transform.position=Vector3.zero;
        foreach(var child in rig.transform.Cast<Transform>().ToArray())if(child.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0)UnityEngine.Object.DestroyImmediate(child.gameObject);
        var bones=rig.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        foreach(string file in outfit.Parts)AttachRosterPart(rig,bones,file,level==0?4:5,report);
        foreach(var renderer in rig.GetComponentsInChildren<MeshRenderer>())if(renderer.name.StartsWith("newmusket.lod"))
        {renderer.enabled=renderer.name=="newmusket.lod"+(level==0?3:5);if(renderer.enabled){renderer.sharedMaterial=MaterialFor(level==1?"CoarseWeapon":"newmusket",outfit.Faction=="britain"?1:0);if(level==1)renderer.GetComponent<MeshFilter>().sharedMesh=CoarseWeapon();}}
        // The saved inspection prefab keeps disabled LODs in their import pose. Fit the selected weapon just as BuildScene does.
        OutfitClip("gp_musket_aimfire").SampleAnimation(rig,OutfitClip("gp_musket_aimfire").length*.4f);
        var selectedGun=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));
        selectedGun.transform.rotation=Quaternion.LookRotation((bones["l_hand"].position-bones["r_hand"].position).normalized,Vector3.up);
        selectedGun.transform.position=bones["r_hand"].position-selectedGun.transform.TransformVector(new Vector3(0,0,-25));selectedGun.transform.SetParent(bones["r_hand"],true);
        MusketReloadAuthoring.StowRod(selectedGun.transform,rig.transform.Find("Authored ramrod"));
        return rig;
    }
    static AnimationClip OutfitClip(string name)
    {
        var importer=(ModelImporter)AssetImporter.GetAtPath(Root+"/Animations/"+name+".FBX");
        if(importer.animationType!=ModelImporterAnimationType.Generic){importer.animationType=ModelImporterAnimationType.Generic;importer.SaveAndReimport();}
        return AssetDatabase.LoadAllAssetsAtPath(Root+"/Animations/"+name+".FBX").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
    }
    static void BakeOutfitInfantry(NapoleonicOutfit outfit,string output,StringBuilder report)
    {
        int version=outfit.Faction=="ottoman"?3:2;
        var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(output+"/Library.asset");if(asset&&asset.BakeVersion>=version){CopyOutfitFireAnchors(asset.Set);EditorUtility.SetDirty(asset);return;}
        if(!asset){asset=ScriptableObject.CreateInstance<UnitVisualLibrary>();AssetDatabase.CreateAsset(asset,output+"/Library.asset");}
        asset.Faction=outfit.Faction;asset.Family=outfit.Id;asset.Set=new InfantryCrowdSet{Lods=new InfantryCrowdLod[2]};CopyOutfitFireAnchors(asset.Set);asset.hideFlags=HideFlags.DontUnloadUnusedAsset;
        var idle=OutfitClip("musket_idle");var fire=OutfitClip("gp_musket_aimfire");var reload=OutfitClip("gp_reload_musket_reworked");Dictionary<string,Rect> atlas=null;
        for(int level=0;level<2;level++)
        {
            var rig=OutfitRig(outfit,level,report);
            if(cartridgeBake){var weapon=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));weapon.GetComponent<MeshFilter>().sharedMesh=CartridgeWeaponAuthoring.Prepare(cartridgeOutfitSingle,level);weapon.sharedMaterial=CartridgeWeaponAuthoring.PrepareMaterial();}
            if(level==0)atlas=PackRosterAtlas(rig,output,asset.Set);
            var data=new InfantryCrowdLod{MaxDistance=level==0?40:100000};asset.Set.Lods[level]=data;
            AssetDatabase.StartAssetEditing();
            try
            {
                data.Idle=BakePoses(rig,idle,8,atlas,output,level,report);
                data.March=BakePoses(rig,idle,8,atlas,output,level,report,InfantryMotionKind.March);
                data.Run=BakePoses(rig,idle,8,atlas,output,level,report,InfantryMotionKind.Run);
                data.Charge=BakePoses(rig,idle,8,atlas,output,level,report,InfantryMotionKind.Charge);
                data.Hit=BakePoses(rig,idle,6,atlas,output,level,report,InfantryMotionKind.Hit);
                data.Death=BakePoses(rig,idle,12,atlas,output,level,report,InfantryMotionKind.Death);
                data.Knockdown=BakePoses(rig,idle,12,atlas,output,level,report,InfantryMotionKind.Knockdown);
                data.GetUp=BakePoses(rig,idle,12,atlas,output,level,report,InfantryMotionKind.GetUp);
                data.Fire=BakePoses(rig,fire,8,atlas,output,level,report);
                data.Reload=BakePoses(rig,reload,24,atlas,output,level,report);
                data.KneelingFire=BakePoses(rig,fire,8,atlas,output,level,report,null,true);
                data.KneelingReload=BakePoses(rig,reload,24,atlas,output,level,report,null,true);
                data.Melee=new InfantryMeleeClip[6];
                var kinds=new[]{InfantryMotionKind.MeleeReady,InfantryMotionKind.Thrust,InfantryMotionKind.Parry,InfantryMotionKind.ButtStrike,InfantryMotionKind.Stagger,InfantryMotionKind.Retreat};
                var actions=new[]{MeleeAction.Ready,MeleeAction.Thrust,MeleeAction.Parry,MeleeAction.ButtStrike,MeleeAction.Stagger,MeleeAction.Retreat};
                for(int m=0;m<6;m++)data.Melee[m]=new InfantryMeleeClip{Action=actions[m],Poses=BakePoses(rig,idle,8,atlas,output,level,report,kinds[m]),Kneeling=BakePoses(rig,idle,8,atlas,output,level,report,kinds[m],true)};
                data.GrenadeThrow=BakeGrenade(rig,idle,atlas,output,level,report);
                PrepareRosterBlends(data);
            }
            finally{AssetDatabase.StopAssetEditing();}
        }
        var temporary=ScriptableObject.CreateInstance<InfantryCrowdLibrary>();temporary.French=asset.Set;temporary.British=asset.Set;InfantryPostureBuild.Prepare(temporary);UnityEngine.Object.DestroyImmediate(temporary);
        asset.BakeVersion=version;asset.hideFlags=HideFlags.None;EditorUtility.SetDirty(asset);
    }
    static void CopyOutfitFireAnchors(InfantryCrowdSet set)
    {
        var original=cartridgeBake?(cartridgeOutfitSingle?Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library").SingleFrench:Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library").French):Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary").French;
        set.MuzzleOffset=original.MuzzleOffset;set.LockOffset=original.LockOffset;
    }
    public static void BakeCartridgeOutfits()
    {
        PrepareOutfitImports();var report=new StringBuilder();
        try
        {
            cartridgeBake=true;
            foreach(var outfit in NapoleonicOutfits.All.Where(o=>!o.Mounted))foreach(bool single in new[]{true,false})
            {
                cartridgeOutfitSingle=single;string output="Assets/Resources/NapoleonicOutfits/"+outfit.Id+"/"+(single?"CartridgeSingle":"CartridgeMagazine");Directory.CreateDirectory(output);
                BakeOutfitInfantry(outfit,output,report);AssetDatabase.SaveAssets();CompactOutfitData(output);UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();Debug.Log("NAPOLEONIC_CARTRIDGE_BAKED "+outfit.Id+" single="+single);
                File.WriteAllText("Artifacts/NapoleonicOutfits/cartridge-bake.txt",report.ToString());
            }
        }
        finally{cartridgeBake=false;}
    }
    static Mesh[] BakeGrenade(GameObject rig,AnimationClip idle,Dictionary<string,Rect> atlas,string output,int level,StringBuilder report)
    {
        idle.SampleAnimation(rig,0);
        var gun=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));var rod=rig.transform.Find("Authored ramrod");
        var ball=GameObject.CreatePrimitive(PrimitiveType.Sphere);ball.name="Throwing grenade";ball.transform.SetParent(rig.transform,false);ball.GetComponent<Renderer>().sharedMaterial=MaterialFor("Ramrod",0);UnityEngine.Object.DestroyImmediate(ball.GetComponent<Collider>());
        var grip=new ClosedGripAuthoring(rig,gun.transform,rod);
        supplementalPoseName="GrenadeThrow";supplementalPose=(r,phase)=>{ball.transform.localScale=Vector3.one*(phase*GrenadeSystem.ThrowDuration<GrenadeSystem.ReleaseDelay?.065f:0);grip.ApplyGrenade(phase,ball.transform);ball.GetComponent<Renderer>().enabled=true;};
        try{return BakePoses(rig,idle,23,atlas,output,level,report,InfantryMotionKind.Idle);}
        finally{supplementalPose=null;supplementalPoseName=null;UnityEngine.Object.DestroyImmediate(ball);}
    }
    static GameObject WeaponProp(Transform parent,string name,bool lance,bool pistol=false)
    {
        var prop=new GameObject(name);prop.transform.SetParent(parent,false);
        void Part(string label,Vector3 position,Vector3 size)
        {bool round=label.Contains("shaft")||label.Contains("barrel")||label.Contains("ramrod");var item=GameObject.CreatePrimitive(round?PrimitiveType.Cylinder:PrimitiveType.Cube);item.name=label;item.transform.SetParent(prop.transform,false);item.transform.localPosition=position;item.transform.localScale=round?(label.Contains("shaft")?new Vector3(size.x,size.y*.5f,size.z):new Vector3(size.x,size.z*.5f,size.y)):size;if(round&&!label.Contains("shaft"))item.transform.localRotation=Quaternion.Euler(90,0,0);if(label=="Lance point")
            {var tip=new Mesh{name="Authored lance point"};tip.vertices=new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(0,.5f,0)};tip.triangles=new[]{0,2,1,0,3,2,0,1,4,1,2,4,2,3,4,3,0,4};tip.uv=new Vector2[5];tip.RecalculateNormals();tip.RecalculateBounds();item.GetComponent<MeshFilter>().sharedMesh=tip;}
            item.GetComponent<Renderer>().sharedMaterial=MaterialFor(label.Contains("wood")?"CoarseWeapon":"Ramrod",0);UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());}
        if(lance){Part("Lance wood shaft",new Vector3(0,1.45f,0),new Vector3(.028f,2.9f,.028f));Part("Lance point",new Vector3(0,2.94f,0),new Vector3(.045f,.18f,.025f));}
        else{if(!pistol){Part("Firearm wood butt",new Vector3(0,-.06f,-.25f),new Vector3(.065f,.12f,.28f));Part("Firearm butt plate",new Vector3(0,-.06f,-.394f),new Vector3(.066f,.122f,.014f));}Part("Firearm wood stock",new Vector3(0,-.04f,pistol?-.04f:.12f),new Vector3(.055f,.08f,pistol?.12f:.45f));Part("Firearm barrel",new Vector3(0,.025f,pistol?.16f:.44f),new Vector3(.025f,.025f,pistol?.26f:.65f));Part("Firearm grip",new Vector3(0,-.10f,0),new Vector3(.038f,.14f,.035f));Part("Loading ramrod",new Vector3(0,-.025f,pistol?.10f:.40f),new Vector3(.007f,.007f,pistol?.22f:.50f));}
        return prop;
    }
    static void BakeOutfitMounted(NapoleonicOutfit outfit,string output,StringBuilder report)
    {
        var library=AssetDatabase.LoadAssetAtPath<CavalryCrowdLibrary>(output+"/CavalryCrowdLibrary.asset");if(library&&library.OutfitBakeVersion>=4)return;
        if(!library){library=ScriptableObject.CreateInstance<CavalryCrowdLibrary>();AssetDatabase.CreateAsset(library,output+"/CavalryCrowdLibrary.asset");}library.hideFlags=HideFlags.DontUnloadUnusedAsset;
        library.French=new CavalryCrowdSet{Lods=new CavalryCrowdLod[2]};library.British=library.French;Dictionary<string,Rect> atlas=null;
        for(int level=0;level<2;level++)
        {
            var rig=OutfitRig(outfit,level,report);OutfitClip("musket_idle").SampleAnimation(rig,0);
            var gun=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));var rod=rig.transform.Find("Authored ramrod");
            var cloth=rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.enabled).sharedMaterial;
            var equipment=InfantryCommandAuthoring.Create(rig,InfantryMotionKind.Colonel,cloth,MaterialFor("Ramrod",0),gun.sharedMaterial,outfit.Faction=="britain"?1:0);
            foreach(var r in equipment.GetComponentsInChildren<Renderer>())if(r.name.StartsWith("Revolver")||r.name.Contains("epaulette")||r.name.Contains("Colonel"))r.enabled=false;
            gun.enabled=false;rod.GetComponent<Renderer>().enabled=false;
            var horse=equipment.GetComponent<CommandHorseMarker>().Rig;
            foreach(var part in equipment.GetComponentsInChildren<Renderer>())if(part.name.StartsWith("Saddle")||part.name.StartsWith("Stirrup")||part.name.StartsWith("Bridle")||part.name.StartsWith("Rein"))part.transform.SetParent(horse.Root.transform,true);
            FitSourceSword(rig,outfit.Id.EndsWith("-cuirassier"),level);
            var lance=WeaponProp(rig.transform,"Mounted lance",true);var firearm=WeaponProp(rig.transform,"Mounted firearm",false);var pistol=WeaponProp(rig.transform,"Mounted pistol",false,true);
            foreach(var r in pistol.GetComponentsInChildren<Renderer>())if(r.name!="Loading ramrod")UnityEngine.Object.DestroyImmediate(r.gameObject);SourceWeapon(pistol.transform,"new_flintlock_pistol.FBX",level,true);
            // Atlas includes each weapon before visibility changes; motion variants keep topology within each track.
            var dummy=new InfantryCrowdSet();if(level==0){atlas=PackRosterAtlas(rig,output,dummy);library.French.Material=dummy.Material;}
            var motion=new ProceduralInfantryRig(rig);var grip=new ClosedGripAuthoring(rig,gun.transform,rod,-.65f);var sword=rig.GetComponentsInChildren<Transform>().First(t=>t.name=="Sabre");
            var clips=new List<MountedClip>();var lod=new CavalryCrowdLod{MaxDistance=level==0?50:100000};library.French.Lods[level]=lod;
            var rootPosition=horse.Root.transform.localPosition;var rootRotation=horse.Root.transform.localRotation;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(MountedMotion kind in Enum.GetValues(typeof(MountedMotion)))
                {
                    bool carry=kind>=MountedMotion.CarbineCarryWalk;
                    bool firing=kind==MountedMotion.MountedAim||kind==MountedMotion.MountedFire||kind==MountedMotion.MountedReload||kind>=MountedMotion.CarbineCarryWalk&&kind<=MountedMotion.CarbineCarryGallop;
                    bool small=kind==MountedMotion.PistolAim||kind==MountedMotion.PistolFire||kind==MountedMotion.PistolReload||kind>=MountedMotion.PistolCarryWalk;
                    bool thrust=kind==MountedMotion.LanceThrust||kind==MountedMotion.LanceCharge;bool lancePose=kind>=MountedMotion.LanceWalk&&kind<=MountedMotion.LanceCharge||thrust||kind==MountedMotion.LanceReady||outfit.Weapon=="lance"&&(kind<=MountedMotion.Charge||kind==MountedMotion.TurnLeft||kind==MountedMotion.TurnRight);
                    bool loop=kind<=MountedMotion.Charge||kind==MountedMotion.TurnLeft||kind==MountedMotion.TurnRight||kind>=MountedMotion.HorseWalk&&kind<=MountedMotion.HorseIdle||kind==MountedMotion.LanceReady||kind==MountedMotion.MountedAim||kind==MountedMotion.PistolAim||kind>=MountedMotion.LanceWalk;
                    int count=kind==MountedMotion.MountedReload||kind==MountedMotion.PistolReload?32:loop?12:16;var poses=new Mesh[count];var muzzleSockets=firing||small?new Vector3[count]:null;var lockSockets=firing||small?new Vector3[count]:null;
                    for(int frame=0;frame<count;frame++)
                    {
                        float phase=frame/(float)(loop?count:count-1);
                        horse.Root.transform.localPosition=rootPosition;horse.Root.transform.localRotation=rootRotation;
                        var gait=kind==MountedMotion.Walk||kind==MountedMotion.LanceWalk||kind==MountedMotion.CarbineCarryWalk||kind==MountedMotion.PistolCarryWalk||kind==MountedMotion.HorseWalk||kind==MountedMotion.TurnLeft||kind==MountedMotion.TurnRight?HorseArt.HorseGait.Walk:kind==MountedMotion.Trot||kind==MountedMotion.LanceTrot||kind==MountedMotion.CarbineCarryTrot||kind==MountedMotion.PistolCarryTrot||kind==MountedMotion.HorseTrot?HorseArt.HorseGait.Trot:kind==MountedMotion.Gallop||kind==MountedMotion.LanceGallop||kind==MountedMotion.LanceCharge||kind==MountedMotion.CarbineCarryGallop||kind==MountedMotion.PistolCarryGallop||kind==MountedMotion.Charge||kind==MountedMotion.HorseGallop?HorseArt.HorseGait.Gallop:HorseArt.HorseGait.Idle;
                        HorseArt.SampleGait(horse,gait,phase);
                        var action=kind==MountedMotion.SlashLeft?MeleeAction.SlashLeft:kind==MountedMotion.SlashRight?MeleeAction.SlashRight:kind==MountedMotion.Brake?MeleeAction.Brake:kind==MountedMotion.Stumble?MeleeAction.Stumble:MeleeAction.None;
                        motion.SampleMounted(action,phase,Mathf.Sin(phase*Mathf.PI*4)*.018f);grip.ApplyMountedCombat(action,phase);
                        foreach(var r in lance.GetComponentsInChildren<Renderer>())r.enabled=lancePose;
                        foreach(var r in firearm.GetComponentsInChildren<Renderer>())r.enabled=firing;
                        foreach(var r in pistol.GetComponentsInChildren<Renderer>())r.enabled=small;
                        foreach(var r in sword.GetComponentsInChildren<Renderer>())r.enabled=!lancePose&&!firing&&!small;
                        if(lancePose)grip.ApplyLance(lance.transform,phase,thrust||kind==MountedMotion.Charge);
                        if(carry)grip.ApplyMountedCarry((small?pistol:firearm).transform,small);
                        else if(firing||small)grip.ApplyMountedFirearm((small?pistol:firearm).transform,phase,kind==MountedMotion.MountedReload||kind==MountedMotion.PistolReload,small);
                        if(kind==MountedMotion.TurnLeft||kind==MountedMotion.TurnRight){float sign=kind==MountedMotion.TurnLeft?-1:1;horse.Neck.localRotation*=Quaternion.Euler(0,sign*25,0);horse.Torso.localRotation*=Quaternion.Euler(0,0,sign*5);}
                        if(kind==MountedMotion.HorseFall)horse.Root.transform.localRotation=rootRotation*Quaternion.Euler(0,0,88*Mathf.SmoothStep(0,1,phase));
                        if(IsRiderFall(kind))motion.SampleMountedFall(phase,kind==MountedMotion.RiderFallBack?0:kind==MountedMotion.RiderFallSide?2:kind==MountedMotion.RiderTumble?3:1);
                        if(firing||small)
                        {
                            var prop=(small?pistol:firearm).transform;var points=prop.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.name!="Loading ramrod").SelectMany(r=>r.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v=>r.transform.TransformPoint(v))).ToArray();
                            float furthest=points.Max(v=>Vector3.Dot(v,prop.forward));var end=points.Where(v=>furthest-Vector3.Dot(v,prop.forward)<.003f).ToArray();Vector3 tip=Vector3.zero;foreach(var point in end)tip+=point;tip/=end.Length;
                            var rotate=Quaternion.Euler(0,180,0);muzzleSockets[frame]=rotate*rig.transform.InverseTransformPoint(tip);lockSockets[frame]=rotate*rig.transform.InverseTransformPoint(prop.TransformPoint(new Vector3(0,.025f,0)));
                        }
                        poses[frame]=CombineMounted(rig,horse,kind,phase,atlas,outfit.Id+"_L"+level+"_"+kind+"_"+frame);
                        string path=output+"/"+poses[frame].name+".asset";AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(poses[frame],path);
                    }
                    for(int i=0;i<count;i++){var next=poses[i+1<count?i+1:loop?0:i];if(next.vertexCount!=poses[i].vertexCount)throw new Exception("Outfit mounted topology mismatch");poses[i].SetUVs(2,new List<Vector3>(next.vertices));poses[i].SetUVs(3,new List<Vector3>(next.normals));var bounds=poses[i].bounds;bounds.Encapsulate(next.bounds);poses[i].bounds=bounds;}
                    clips.Add(new MountedClip{Motion=kind,Loop=loop,Poses=poses,MuzzleSockets=muzzleSockets,LockSockets=lockSockets});
                }
                lod.Clips=clips.ToArray();
            }
            finally{AssetDatabase.StopAssetEditing();}
        }
        library.OutfitBakeVersion=4;library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);
    }
}
