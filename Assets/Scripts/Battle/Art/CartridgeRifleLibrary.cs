using UnityEngine;
namespace EmpireBattle.Art
{
    public sealed class CartridgeRifleLibrary : ScriptableObject
    {
        public InfantryCrowdSet French, British, SingleFrench, SingleBritish;
        public int WeaponBakeVersion;
        public static bool UsesCartridges(InfantryWeapon weapon)=>weapon!=null&&(weapon.Id=="breechloader"||weapon.Id=="magazine"||weapon.Id=="smokeless");
    }
}
