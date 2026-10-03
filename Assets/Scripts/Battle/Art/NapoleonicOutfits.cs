using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace EmpireBattle.Art
{
    [Serializable] public sealed class NapoleonicOutfit
    {
        public string Id,Faction,Name,Role,Weapon;
        public bool Automatic;
        public string[] Parts;
        public bool Mounted=>Role=="Cavalry";
        public bool Supports(string faction,UnitRole role)=>Faction==UnitRoster.ArtFaction(faction)&&Role==role.ToString();
    }
    [Serializable] public sealed class NapoleonicCommandDress { public string Faction;public string[] CaptainParts,ColonelParts; }
    [Serializable] public sealed class NapoleonicOutfitCatalog { public NapoleonicOutfit[] Outfits;public NapoleonicCommandDress[] Commands; }
    // Appearance has no combat modifiers. A named regiment retains its identity through XP gains.
    public static class NapoleonicOutfits
    {
        static NapoleonicOutfit[] outfits;
        static readonly Dictionary<string,UnitVisualLibrary> infantry=new Dictionary<string,UnitVisualLibrary>();
        static readonly Dictionary<(InfantryCrowdSet,string),InfantryCrowdSet> grenadeSets=new Dictionary<(InfantryCrowdSet,string),InfantryCrowdSet>();
        static readonly Dictionary<string,CavalryCrowdLibrary> cavalry=new Dictionary<string,CavalryCrowdLibrary>();
        public static NapoleonicOutfit[] All=>outfits??(outfits=JsonUtility.FromJson<NapoleonicOutfitCatalog>(Resources.Load<TextAsset>("NapoleonicOutfits/Catalog").text).Outfits);
        public static string MountedWeapon(UnitLoadout loadout)
        {
            if(loadout?.items==null)return "sabre";
            if(loadout.items.Any(i=>UnitEquipment.Find(i)?.Family=="heavy-lances"))return "lance";
            if(loadout.items.Any(i=>UnitEquipment.Find(i)?.Weapon=="carbine"))return "carbine";
            if(loadout.items.Any(i=>UnitEquipment.Find(i)?.Weapon=="pistol"))return "pistol";
            return "sabre";
        }
        public static bool HasCuirass(UnitLoadout kit)=>kit?.items?.Any(i=>UnitEquipment.Find(i)?.Family=="cuirasses")==true;
        public static bool Compatible(NapoleonicOutfit dress,string faction,UnitRole role,UnitLoadout kit)=>dress.Supports(faction,role)&&(!dress.Mounted||dress.Id.EndsWith("-cuirassier")==HasCuirass(kit));
        public static NapoleonicOutfit Select(string faction,UnitRole role,UnitLoadout kit,string name="")
        {
            faction=UnitRoster.ArtFaction(faction);
            var explicitDress=All.FirstOrDefault(o=>o.Id==kit?.appearance&&Compatible(o,faction,role,kit));
            string weapon=MountedWeapon(kit);
            if(explicitDress!=null)return explicitDress;
            if(role==UnitRole.Cavalry)
            {
                string key=HasCuirass(kit)?"cuirassier":weapon=="lance"?"lancer":weapon=="carbine"||weapon=="pistol"?"dragoon":"hussar";
                return All.FirstOrDefault(o=>o.Id==faction+"-"+key);
            }
            // Historical regiment names can select a regional outfit without inventing a strength tier.
            var named=All.FirstOrDefault(o=>o.Supports(faction,role)&&!o.Automatic&&
                !string.IsNullOrEmpty(name)&&(name.IndexOf(o.Name,StringComparison.OrdinalIgnoreCase)>=0||
                o.Id.EndsWith("-highlander")&&name.IndexOf("highland",StringComparison.OrdinalIgnoreCase)>=0||
                o.Id.EndsWith("-vistula")&&name.IndexOf("vistula",StringComparison.OrdinalIgnoreCase)>=0));
            return named??All.FirstOrDefault(o=>o.Supports(faction,role)&&o.Automatic);
        }
        public static InfantryCrowdSet Infantry(BattalionState unit)
        {
            var dress=Select(unit.Faction,unit.Role,unit.Loadout,unit.Name);if(dress==null||dress.Mounted)return null;
            string key=dress.Id+(CartridgeRifleLibrary.UsesCartridges(unit.Weapon)?unit.Weapon.Id=="breechloader"?"/CartridgeSingle":"/CartridgeMagazine":"");
            if(!infantry.TryGetValue(key,out var asset))infantry[key]=asset=Resources.Load<UnitVisualLibrary>("NapoleonicOutfits/"+key+"/Library");
            return asset&&asset.BakeVersion>=2?asset.Set:null;
        }
        public static CavalryCrowdSet Mounted(BattalionState unit)
        {
            var dress=Select(unit.Faction,UnitRole.Cavalry,unit.Loadout,unit.Name);if(dress==null)return null;
            if(!cavalry.TryGetValue(dress.Id,out var asset))cavalry[dress.Id]=asset=Resources.Load<CavalryCrowdLibrary>("NapoleonicOutfits/"+dress.Id+"/CavalryCrowdLibrary");
            return asset&&asset.OutfitBakeVersion>=4?asset.French:null;
        }
        public static InfantryCrowdSet Command(BattalionState unit)
        {
            string key=UnitRoster.ArtFaction(unit.Faction);
            if(!infantry.TryGetValue("command/"+key,out var asset))infantry["command/"+key]=asset=Resources.Load<UnitVisualLibrary>("NapoleonicCommands/"+key+"/Library");
            return asset&&asset.BakeVersion>0?asset.Set:null;
        }
        public static InfantryCrowdSet WithGrenade(InfantryCrowdSet set,BattalionState unit)
        {
            if(set?.Lods==null||set.Lods[0].GrenadeThrow?.Length>0||!UnitRoster.Shoots(unit.Role))return set;
            string key=UnitRoster.ArtFaction(unit.Faction)+"-"+(UnitRoster.Light(unit.Role)?"light":"line");
            if(grenadeSets.TryGetValue((set,key),out var dressed))return dressed;
            if(!infantry.TryGetValue("grenade/"+key,out var asset))infantry["grenade/"+key]=asset=Resources.Load<UnitVisualLibrary>("GrenadeMotions/"+key+"/Library");
            if(asset?.Set?.Lods!=null)
            {
                var original=set;
                set=new InfantryCrowdSet{Material=original.Material,MuzzleOffset=original.MuzzleOffset,LockOffset=original.LockOffset,Lods=original.Lods.Select(l=>l.Copy()).ToArray(),GrenadeMaterial=asset.Set.Material};
                grenadeSets[(original,key)]=set;
                for(int i=0;i<set.Lods.Length;i++)set.Lods[i].GrenadeThrow=asset.Set.Lods[Math.Min(i,asset.Set.Lods.Length-1)].GrenadeThrow;
            }
            return set;
        }
        public static void ClearCache(){outfits=null;infantry.Clear();cavalry.Clear();grenadeSets.Clear();}
    }
}
