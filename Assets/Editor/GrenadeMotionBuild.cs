using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static partial class InfantryAssetBuild
{
    public static void BakeGrenadeMotions()
    {
        PrepareOutfitImports();var report=new StringBuilder();
        foreach(string faction in UnitRoster.Factions.Concat(new[]{"generic"}))foreach(string family in new[]{"line","light"})
        {
            string output="Assets/Resources/GrenadeMotions/"+faction+"-"+family;Directory.CreateDirectory(output);
            var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(output+"/Library.asset");if(asset&&asset.BakeVersion>=1)continue;
            if(!asset){asset=ScriptableObject.CreateInstance<UnitVisualLibrary>();AssetDatabase.CreateAsset(asset,output+"/Library.asset");}
            asset.Faction=faction;asset.Family="grenade / "+family;asset.Set=new InfantryCrowdSet{Lods=new InfantryCrowdLod[2]};asset.hideFlags=HideFlags.DontUnloadUnusedAsset;
            Dictionary<string,Rect> maps=null;
            for(int level=0;level<2;level++)
            {
                int f=Array.IndexOf(UnitRoster.Factions,faction);if(f<0||f>=8)f=0;
                var recipe=RosterParts[f];var parts=new[]{recipe[family=="light"?1:0],recipe[2],recipe[3],recipe[4]};
                if(family=="light")
                {
                    if(f==0){parts[1]="gap_infantry_pants_french_voltigeur.fbx";parts[2]="gap_french_shako_voltigeur.FBX";}
                    if(f==1){parts[1]="gap_british_rifleman_pants.FBX";parts[2]="gap_british_stovepipe_rifle.FBX";parts[3]="gap_british_equipment_rifle.FBX";}
                    if(f==2){parts[1]="gap_infantry_pants_austrian_jaeger.FBX";parts[2]="gap_austrian_jaeger_hat.FBX";parts[3]="gap_austrian_equipment_light.FBX";}
                    if(f==3){parts[1]="gap_infantry_pants_prussian_jaeger.fbx";parts[3]="gap_prussian_equipment_light.FBX";}
                    if(f==4){parts[1]="gap_russian_jaeger_pants.FBX";parts[2]="gap_russian_shako_jaeger.FBX";parts[3]="gap_infantry_equipment_russian_rifle.FBX";}
                    if(f==5)parts[2]="gap_italian_shako_voltigeur.FBX";
                }
                var rig=OutfitRig(new NapoleonicOutfit{Id=faction+"-"+family,Parts=parts},level,report);
                if(RegionalArmyRoster.Supports(faction))PrepareRegionalRig(rig,faction,family,level);
                if(level==0)maps=PackRosterAtlas(rig,output,asset.Set);
                AssetDatabase.StartAssetEditing();try
                {
                    var poses=BakeGrenade(rig,OutfitClip("musket_idle"),maps,output,level,report);asset.Set.Lods[level]=new InfantryCrowdLod{Idle=new[]{poses[0]},GrenadeThrow=poses};
                    for(int i=0;i<poses.Length;i++){var next=poses[Math.Min(i+1,poses.Length-1)];poses[i].SetUVs(2,new List<Vector3>(next.vertices));poses[i].SetUVs(3,new List<Vector3>(next.normals));var bounds=poses[i].bounds;bounds.Encapsulate(next.bounds);poses[i].bounds=bounds;EditorUtility.SetDirty(poses[i]);}
                }
                finally{AssetDatabase.StopAssetEditing();}
            }
            asset.BakeVersion=1;asset.hideFlags=HideFlags.None;EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();CompactOutfitData(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();Debug.Log("GRENADE_MOTION_BAKED "+faction+" "+family);
        }
        File.WriteAllText("Artifacts/NapoleonicOutfits/grenade-bake.txt",report.ToString());
    }
}
