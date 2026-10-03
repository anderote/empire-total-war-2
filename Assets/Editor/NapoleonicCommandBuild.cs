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
    public static void BakeNapoleonicCommands()
    {
        PrepareOutfitImports();var catalog=JsonUtility.FromJson<NapoleonicOutfitCatalog>(Resources.Load<TextAsset>("NapoleonicOutfits/Catalog").text);var report=new StringBuilder();
        foreach(var dress in catalog.Commands)
        {
            string output="Assets/Resources/NapoleonicCommands/"+dress.Faction;Directory.CreateDirectory(output);
            var asset=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(output+"/Library.asset");if(asset&&asset.BakeVersion>=1)continue;
            if(!asset){asset=ScriptableObject.CreateInstance<UnitVisualLibrary>();AssetDatabase.CreateAsset(asset,output+"/Library.asset");}
            asset.hideFlags=HideFlags.DontUnloadUnusedAsset;asset.Faction=dress.Faction;asset.Family="command attire";asset.Set=new InfantryCrowdSet{Lods=new InfantryCrowdLod[2]};Dictionary<string,Rect> atlas=null;
            for(int level=0;level<2;level++)
            {
                var captain=OutfitRig(new NapoleonicOutfit{Id=dress.Faction+"-captain",Faction=dress.Faction,Parts=dress.CaptainParts},level,report);
                var colonel=OutfitRig(new NapoleonicOutfit{Id=dress.Faction+"-colonel",Faction=dress.Faction,Parts=dress.ColonelParts},level,report,false);
                int faction=Array.IndexOf(UnitRoster.Factions,dress.Faction);var recipe=RosterParts[faction];
                var regular=OutfitRig(new NapoleonicOutfit{Id=dress.Faction+"-command-party",Faction=dress.Faction,Parts=new[]{recipe[0],recipe[2],recipe[3],recipe[4]}},level,report,false);
                if(level==0){colonel.transform.SetParent(captain.transform,false);regular.transform.SetParent(captain.transform,false);atlas=PackRosterAtlas(captain,output,asset.Set);colonel.transform.SetParent(null);regular.transform.SetParent(null);}
                var data=new InfantryCrowdLod{MaxDistance=level==0?40:100000,Command=new InfantryCommandVariant[5]};asset.Set.Lods[level]=data;
                var idle=OutfitClip("musket_idle");
                // A small regular-body fallback keeps the command library independently inspectable.
                data.Idle=BakePoses(regular,idle,4,atlas,output,level,report);
                data.March=BakePoses(regular,idle,8,atlas,output,level,report,InfantryMotionKind.March);
                data.Death=BakePoses(regular,idle,12,atlas,output,level,report,InfantryMotionKind.Death);
                data.Knockdown=BakePoses(regular,idle,12,atlas,output,level,report,InfantryMotionKind.Knockdown);
                data.GetUp=BakePoses(regular,idle,12,atlas,output,level,report,InfantryMotionKind.GetUp);
                AssetDatabase.StartAssetEditing();try
                {
                    for(int role=0;role<5;role++)
                    {
                        var rig=role==0?captain:role==4?colonel:regular;
                        var kind=role==0?InfantryMotionKind.Officer:role==1?InfantryMotionKind.Drummer:role==2?InfantryMotionKind.Standard:role==3?InfantryMotionKind.Sergeant:InfantryMotionKind.Colonel;
                        var command=new InfantryCommandVariant();data.Command[role]=command;
                        foreach(int sequence in Enumerable.Range(0,role==0?10:role==3?9:2))
                        {
                            idle.SampleAnimation(rig,0);var cloth=rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.enabled).sharedMaterial;
                            var gun=rig.GetComponentsInChildren<MeshRenderer>().First(r=>r.enabled&&r.name.StartsWith("newmusket.lod"));var rod=rig.transform.Find("Authored ramrod");
                            var equipment=InfantryCommandAuthoring.Create(rig,kind,cloth,rod.GetComponent<Renderer>().sharedMaterial,gun.sharedMaterial,dress.Faction=="britain"?1:0,sequence==1);
                            bool kneel=role==0?sequence>=8:role==3&&sequence>=7;
                            int variant=kneel?(role==0?(sequence==8?0:3):(sequence==7?2:3)):sequence;
                            var poses=BakePoses(rig,idle,12,atlas,output,level,report,kind,kneel,variant);
                            if(sequence==0)command.Idle=poses;else if(sequence==1)command.March=poses;
                            else if(kneel){if((role==0&&sequence==8)||(role==3&&sequence==7))command.KneelingIdle=poses;else command.KneelingStrike=poses;}
                            else if(role==0){if(sequence==2)command.OfficerReady=poses;else if(sequence==3)command.OfficerSlash=poses;else if(sequence==4)command.OfficerKnockdown=poses;else if(sequence==5)command.OfficerGetUp=poses;else if(sequence==6)command.OfficerDeath=poses;else command.OfficerFire=poses;}
                            else if(sequence==2)command.PikeReady=poses;else if(sequence==3)command.PikeThrust=poses;else if(sequence==4)command.PikeKnockdown=poses;else if(sequence==5)command.PikeGetUp=poses;else command.PikeDeath=poses;
                            InfantryCommandAuthoring.Destroy(equipment,rig);
                        }
                    }
                    data.Melee=Array.Empty<InfantryMeleeClip>();PrepareRosterBlends(data);
                }
                finally{AssetDatabase.StopAssetEditing();}
            }
            asset.BakeVersion=1;asset.hideFlags=HideFlags.None;EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();CompactOutfitData(output);
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();Debug.Log("NAPOLEONIC_COMMAND_BAKED "+dress.Faction);
        }
        File.WriteAllText("Artifacts/NapoleonicOutfits/command-bake.txt",report.ToString());
    }
}
