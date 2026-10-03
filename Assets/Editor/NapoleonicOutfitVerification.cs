using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;
using UnityEditor;
public static class NapoleonicOutfitVerification
{
    static int checkedMeshes,checkedVertices;
    static void Require(bool value,string message){if(!value)throw new Exception("Napoleonic outfit verification: "+message);}
    public static void Verify()
    {
        Directory.CreateDirectory("Artifacts/NapoleonicOutfits");NapoleonicOutfits.ClearCache();checkedMeshes=checkedVertices=0;
        Require(NapoleonicOutfits.All.Length>=59,"curated library is incomplete");
        Require(NapoleonicOutfits.All.Select(o=>o.Id).Distinct().Count()==NapoleonicOutfits.All.Length,"duplicate identity");
        foreach(var outfit in NapoleonicOutfits.All)
        {
            string root="Assets/Resources/NapoleonicOutfits/"+outfit.Id;
            if(outfit.Mounted)
            {
                var library=AssetDatabase.LoadAssetAtPath<CavalryCrowdLibrary>(root+"/CavalryCrowdLibrary.asset");Require(library&&library.OutfitBakeVersion>=4,"incomplete mounted bake "+outfit.Id);
                Require(library.French?.Lods?.Length==2,"mounted LODs "+outfit.Id);
                foreach(var lod in library.French.Lods)
                foreach(MountedMotion motion in Enum.GetValues(typeof(MountedMotion)))
                {var clip=lod.Clip(motion);Require(clip?.Poses?.Length>0,"missing "+outfit.Id+" / "+motion);CheckTrack(clip.Poses,clip.Loop);}
            }
            else
            {
                var library=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>(root+"/Library.asset");Require(library&&library.BakeVersion>=2,"incomplete infantry bake "+outfit.Id);
                Require(library.Set?.Lods?.Length==2,"infantry LODs "+outfit.Id);
                foreach(var lod in library.Set.Lods)
                {
                    Require(lod.Idle?.Length>0&&lod.Fire?.Length>0&&lod.Reload?.Length>0&&lod.KneelingReload?.Length>0&&lod.GrenadeThrow?.Length==23,"missing core motion "+outfit.Id);
                    foreach(var field in typeof(InfantryCrowdLod).GetFields())CheckObject(field.GetValue(lod));
                    Require(lod.Idle[0].triangles.Length/3<10000,"duplicate weapon LODs: "+outfit.Id);
                }
            }
            EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
        }
        VerifyAuxiliaryLibraries();VerifySelection();VerifyGrenades();VerifyMountedFire();
        File.WriteAllText("Artifacts/NapoleonicOutfits/verification.json",JsonUtility.ToJson(new Receipt{Outfits=NapoleonicOutfits.All.Length,Meshes=checkedMeshes,Vertices=checkedVertices,Simulation="selection, save round trip, grenade release/depletion/cancellation, mounted fire/reload pause/weapon eligibility"},true));
        Debug.Log($"NAPOLEONIC_OUTFITS_VERIFIED {NapoleonicOutfits.All.Length} outfits; {checkedMeshes} meshes; {checkedVertices} vertices");
    }
    static void VerifyAuxiliaryLibraries()
    {
        foreach(var outfit in NapoleonicOutfits.All.Where(o=>!o.Mounted))foreach(string variant in new[]{"CartridgeSingle","CartridgeMagazine"})
        {
            var library=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>("Assets/Resources/NapoleonicOutfits/"+outfit.Id+"/"+variant+"/Library.asset");
            Require(library&&library.BakeVersion>=2,"missing advanced rifle dress "+outfit.Id+" / "+variant);
            foreach(var lod in library.Set.Lods){Require(lod.Fire.Length>0&&lod.Reload.Length>0&&lod.GrenadeThrow.Length==23,"incomplete advanced rifle motions");foreach(var field in typeof(InfantryCrowdLod).GetFields())CheckObject(field.GetValue(lod));}
            EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
        }
        foreach(string faction in UnitRoster.Factions.Concat(new[]{"generic"}))foreach(string family in new[]{"line","light"})
        {
            var library=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>("Assets/Resources/GrenadeMotions/"+faction+"-"+family+"/Library.asset");Require(library&&library.BakeVersion>=1,"missing baseline grenade "+faction+" / "+family);
            foreach(var lod in library.Set.Lods){Require(lod.GrenadeThrow?.Length==23,"baseline grenade frames");CheckTrack(lod.GrenadeThrow,false);}
        }
        var catalog=JsonUtility.FromJson<NapoleonicOutfitCatalog>(Resources.Load<TextAsset>("NapoleonicOutfits/Catalog").text);
        foreach(var dress in catalog.Commands)
        {
            var library=AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>("Assets/Resources/NapoleonicCommands/"+dress.Faction+"/Library.asset");Require(library&&library.BakeVersion>=1,"missing command dress "+dress.Faction);
            foreach(var lod in library.Set.Lods){Require(lod.Command?.Length==5,"missing command roles");foreach(var command in lod.Command){Require(command.Idle?.Length>0&&command.March?.Length>0,"command carry motions");CheckObject(command);}}
            EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
        }
    }
    [Serializable]sealed class Receipt{public int Outfits,Meshes,Vertices;public string Simulation;}
    static void CheckObject(object value)
    {
        if(value==null)return;if(value is Mesh[] poses){CheckTrack(poses,false);return;}
        if(value is Mesh mesh){CheckMesh(mesh);return;}
        if(value is IEnumerable collection){foreach(var element in collection)CheckObject(element);return;}
        if(value is UnityEngine.Object||value.GetType().IsPrimitive||value.GetType().IsEnum)return;
        foreach(var field in value.GetType().GetFields())CheckObject(field.GetValue(value));
    }
    static void CheckTrack(Mesh[] poses,bool loop)
    {
        Require(poses.Length>0,"empty pose sequence");
        for(int i=0;i<poses.Length;i++)
        {
            CheckMesh(poses[i]);var next=poses[i+1<poses.Length?i+1:loop?0:i];
            Require(poses[i].vertexCount==next.vertexCount,"pose topology "+poses[i].name);
            var targets=new List<Vector3>();poses[i].GetUVs(2,targets);Require(targets.Count==poses[i].vertexCount,"missing GPU interpolation "+poses[i].name);
            foreach(var p in targets)Require(Finite(p),"nonfinite interpolation "+poses[i].name);
        }
    }
    static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
    static void CheckMesh(Mesh mesh)
    {
        Require(mesh&&mesh.vertexCount>0,"missing mesh");checkedMeshes++;checkedVertices+=mesh.vertexCount;
        Require(Finite(mesh.bounds.center)&&Finite(mesh.bounds.size)&&mesh.bounds.size.magnitude<10,"invalid bounds "+mesh.name);
        foreach(var p in mesh.vertices)Require(Finite(p),"nonfinite vertex "+mesh.name);
    }
    public static void VerifySimulation(){VerifySelection();VerifyGrenades();VerifyMountedFire();Debug.Log("NAPOLEONIC_SIMULATION_VERIFIED");}
    static void VerifySelection()
    {
        var kit=new UnitLoadout();Require(NapoleonicOutfits.Select("france",UnitRole.Guard,kit).Id=="france-guard","guard dress");
        Require(NapoleonicOutfits.Select("prussia",UnitRole.Militia,kit).Id=="prussia-landwehr","militia dress");
        Require(NapoleonicOutfits.Select("britain",UnitRole.Line,kit,"Highland regiment").Id=="britain-highlander","regional identity");
        kit.appearance="france-vistula";Require(NapoleonicOutfits.Select("france",UnitRole.Line,kit).Id==kit.appearance,"chosen identity");
        var saved=JsonUtility.FromJson<UnitLoadout>(JsonUtility.ToJson(kit));Require(saved.appearance==kit.appearance&&saved.Copy().appearance==kit.appearance,"saved identity");
        Require(NapoleonicOutfits.Select("france",UnitRole.Guard,kit).Id=="france-guard","promotion replaces incompatible dress");
        Require(kit.Equip("heavy-lances",EquipmentUsers.Cavalry)&&NapoleonicOutfits.MountedWeapon(kit)=="lance","issued lance");
        Require(kit.Equip("cavalry-carbines",EquipmentUsers.Cavalry)&&NapoleonicOutfits.MountedWeapon(kit)=="carbine","issued carbine replaces lance");
    }
    static void Place(BattleWorld world,BattalionState unit,Vector2 point,float facing)
    {
        unit.Position=unit.Destination=point;unit.Facing=unit.DesiredFacing=facing;unit.FireAtWill=true;unit.Moving=false;unit.Routing=false;
        for(int slot=0;slot<unit.SoldierCount;slot++){int i=unit.SoldierStart+slot;world.Soldiers.Positions[i]=point+FormationMath.SlotOffset(unit,slot);world.Soldiers.Facings[i]=facing;world.Soldiers.MovementSpeeds[i]=0;}
    }
    static BattleWorld SmallWorld(bool mounted=false,string weapon="")
    {
        var config=new BattleConfig{FrenchBattalions=1,BritishBattalions=1,FrenchCavalry=mounted?1:0,BritishCavalry=0,SoldiersPerBattalion=12,RidersPerSquadron=8,EnableAI=false,EnableLandscape=false,FieldHalfSize=100,InitialSeparation=40};
        if(mounted){config.FormationLoadouts=new[]{new UnitLoadout(),new UnitLoadout(),new UnitLoadout()};Require(config.FormationLoadouts[1].Equip(weapon=="pistol"?"cavalry-pistols":"cavalry-carbines",EquipmentUsers.Cavalry),"mounted deployment kit");}
        return new BattleWorld(config);
    }
    static void VerifyGrenades()
    {
        var world=SmallWorld();var owner=world.Battalions[0];var target=world.Battalions[1];Place(world,owner,new Vector2(0,-10),0);Place(world,target,new Vector2(0,10),Mathf.PI);
        owner.GrenadesRemaining=1;var grenades=new GrenadeSystem(world);grenades.Step(.02f);
        var charge=grenades.Charges.FirstOrDefault(c=>c.Active);Require(charge!=null&&!charge.Released&&grenades.Thrown==0,"wind-up precedes release");
        Require(world.Soldiers.GrenadeThrowStarts[charge.Thrower]==world.Time,"thrower selected");
        grenades.Step(GrenadeSystem.ReleaseDelay+.01f);Require(charge.Released&&grenades.Thrown==1&&owner.GrenadesRemaining==0,"finite grenade at release");
        Require(Vector3.Distance(charge.Start,grenades.ReleaseOrigin(charge.Thrower))<.001f,"release from actual thrower");
        grenades.Step(3);Require(grenades.Detonated==1,"grenade detonates once");grenades.Step(8);Require(grenades.Thrown==1,"no infinite grenades");
        world=SmallWorld();owner=world.Battalions[0];target=world.Battalions[1];Place(world,owner,new Vector2(0,-10),0);Place(world,target,new Vector2(0,10),Mathf.PI);owner.GrenadesRemaining=1;grenades=new GrenadeSystem(world);grenades.Step(.02f);charge=grenades.Charges.FirstOrDefault(c=>c.Active);Require(charge!=null,"second wind-up");owner.Moving=true;grenades.Step(.05f);Require(!charge.Active&&owner.GrenadesRemaining==1,"interrupted wind-up returns unthrown grenade");
    }
    static void VerifyMountedFire()
    {
        foreach(string weapon in new[]{"carbine","pistol"})
        {
            var world=SmallWorld(true,weapon);var rider=world.Battalions.First(u=>u.Kind==UnitKind.Cavalry);var enemy=world.Battalions.First(u=>u.Side!=rider.Side);var ally=world.Battalions.First(u=>u.Side==rider.Side&&u.Kind==UnitKind.Infantry);
            Place(world,rider,new Vector2(0,-10),0);Place(world,enemy,new Vector2(0,15),Mathf.PI);Place(world,ally,new Vector2(-50,-50),0);ally.FireAtWill=false;enemy.FireAtWill=false;
            Require(rider.Loadout.Equip(weapon=="pistol"?"cavalry-pistols":"cavalry-carbines",EquipmentUsers.Cavalry),"mounted weapon equip");rider.Kit=rider.Loadout.Modifiers;
            Require(world.CanFireMode(rider,InfantryFireMode.FireAtWill)&&!world.CanFireMode(rider,InfantryFireMode.Volley),"mounted fire modes");
            Require(world.IssueRangedAttack(rider.Id,enemy.Id)&&rider.AttackMode==InfantryAttackMode.FirePosition,"mounted ranged attack order");
            Require(world.SetMeleeMode(rider.Id,true)&&world.SetMeleeMode(rider.Id,false)&&rider.FireAtWill,"mounted melee toggle resumes fire");
            var combat=new CombatSystem(world);combat.Step(.05f);Require(rider.ShotsFired>0,"mounted "+weapon+" fires while halted");
            int soldier=rider.SoldierStart;float ready=world.Soldiers.ReloadReadyTimes[soldier];int shots=rider.ShotsFired;
            rider.Moving=true;combat.Step(.5f);Require(rider.ShotsFired==shots,"mounted moving fire prohibited");Require(world.Soldiers.ReloadReadyTimes[soldier]>=ready+.49f,"mounted reload pauses during movement");
            rider.Moving=false;rider.FireAtWill=false;ready=world.Soldiers.ReloadReadyTimes[soldier];combat.Step(.5f);Require(world.Soldiers.ReloadReadyTimes[soldier]==ready,"hold-fire does not pause mounted reload");rider.FireAtWill=true;rider.InMelee=true;combat.Step(.5f);Require(rider.ShotsFired==shots,"mounted melee fire prohibited");
            rider.Loadout.Equip("heavy-lances",EquipmentUsers.Cavalry);rider.InMelee=false;combat.Step(.5f);Require(rider.ShotsFired==shots,"lancers do not shoot");
        }
    }
}
