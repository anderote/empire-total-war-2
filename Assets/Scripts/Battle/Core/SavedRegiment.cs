using System;
using System.Linq;

namespace EmpireBattle
{
    // A reusable veteran cadre, independent of any expedition, army ID or live battle.
    [Serializable]
    public sealed class SavedRegiment
    {
        public string id="",name="",source="",savedAt="",weapon="musket";
        public UnitRole role;
        public EquipmentUsers users;
        public int quality,tier,sourceStrength,sourceCapacity;
        public UnitProgression progression=new UnitProgression();
        public UnitLoadout loadout=new UnitLoadout();
        public int RequiredYear=>Math.Max(MilitaryEquipment.Weapon(weapon)?.EarliestYear??1800,
            loadout?.items.Select(i=>UnitEquipment.Find(i)?.EarliestYear??1800).DefaultIfEmpty(0).Max()??1800);
        public int KitCost=>loadout.items.Sum(i=>UnitEquipment.Find(i).Cost)+quality*60;
        public int KitMaterials=>loadout.items.Sum(i=>UnitEquipment.Find(i).Materials)+quality*6;
        public int VeteranCost=>Math.Max(0,progression.HighestLevel-1)*20;
        public bool Valid=>SafeName(name,60)&&SafeText(source,128)&&SafeText(savedAt,40)&&SafeText(id,40)
            &&Enum.IsDefined(typeof(UnitRole),role)&&UnitRoster.Shoots(role)&&users==Users(role)&&quality>=0&&quality<=UnitProgression.MaxQuality&&tier>=0&&tier<=8
            &&sourceStrength>=0&&sourceCapacity>0&&sourceCapacity<=10000&&sourceStrength<=sourceCapacity
            &&MilitaryEquipment.Weapon(weapon)!=null&&progression!=null&&progression.Valid&&loadout!=null&&loadout.Valid(UnitEquipment.RetainedUsers(users,tier),1918)
            &&(progression.abilities==null||progression.abilities.All(a=>RegimentAbilities.Find(a).Supports(users)));
        public static EquipmentUsers Users(UnitRole role)=>role==UnitRole.Militia?EquipmentUsers.Militia:role==UnitRole.Skirmisher?EquipmentUsers.Light:role==UnitRole.Rifleman?EquipmentUsers.Rifle:role==UnitRole.Cavalry?EquipmentUsers.Cavalry:role==UnitRole.Swordsman?EquipmentUsers.Melee:EquipmentUsers.Line;
        public static bool SafeName(string value,int limit)=>!string.IsNullOrWhiteSpace(value)&&SafeText(value,limit);
        public static bool SafeText(string value,int limit)=>value!=null&&value.Length<=limit&&!value.Any(char.IsControl);
        public SavedRegiment Copy()=>new SavedRegiment{id=id,name=name,source=source,savedAt=savedAt,weapon=weapon,role=role,users=users,quality=quality,tier=tier,sourceStrength=sourceStrength,sourceCapacity=sourceCapacity,progression=progression.Copy(),loadout=loadout.Copy()};
    }
}
