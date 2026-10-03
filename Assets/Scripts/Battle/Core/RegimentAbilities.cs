using System;

namespace EmpireBattle
{
    public enum RegimentAbility { SteadyAim, SquareDiscipline, FieldMedicine, AssaultTraining, QuickHands, HardyMarch, LastStand, GrenadierCadre, IronConstitution, AmmunitionReserve, RallyingSpirit, WeaponMastery, ArtilleryMunitions }
    public sealed class RegimentAbilityDefinition
    {
        public readonly RegimentAbility Id;public readonly string Name,Description;public readonly int Level;public readonly EquipmentUsers Users;
        public RegimentAbilityDefinition(RegimentAbility id,string name,int level,EquipmentUsers users,string description){Id=id;Name=name;Level=level;Users=users;Description=description;}
        public int RequiredLevel(int rank)=>1;
        public bool Supports(EquipmentUsers users)=>(Users&users)!=0;
    }
    public static class RegimentAbilities
    {
        public const int MaxRank=10;
        public static readonly RegimentAbilityDefinition[] All={
            new RegimentAbilityDefinition(RegimentAbility.SteadyAim,"Steady aim",1,EquipmentUsers.Infantry|EquipmentUsers.Artillery,"15% more precision (+5% per additional rank) while stationary and outside melee. Artillery receives the precision bonus while serving its guns."),
            new RegimentAbilityDefinition(RegimentAbility.SquareDiscipline,"Square discipline",1,EquipmentUsers.Infantry,"35% more morale resistance (+15% per additional rank) while formed in square. Requires the unit to know square formation."),
            new RegimentAbilityDefinition(RegimentAbility.FieldMedicine,"Field medicine",3,EquipmentUsers.Troops,"Surviving wounded recover 1 health per second per ability rank while stationary, outside melee and free from incoming fire for 20 seconds. Cannot revive casualties."),
            new RegimentAbilityDefinition(RegimentAbility.AssaultTraining,"Shock assault",3,EquipmentUsers.Troops,"25% more melee damage (+10% per additional rank) during a charge impact or approach. Requires an ordered charge."),
            new RegimentAbilityDefinition(RegimentAbility.QuickHands,"Last cartridges",5,EquipmentUsers.Infantry|EquipmentUsers.Artillery,"Reload 25% faster (+10% per additional rank) when remaining ammunition is at or below one quarter of starting reserves. Ability reduction caps at 80%."),
            new RegimentAbilityDefinition(RegimentAbility.HardyMarch,"Route march",5,EquipmentUsers.Troops,"Gain 35% less fatigue (+10% per additional rank) while marching at walking pace. Does not apply to running or routing. Ability reduction caps at 80%."),
            new RegimentAbilityDefinition(RegimentAbility.LastStand,"Last stand",7,EquipmentUsers.Troops,"Once per battle, resist a morale or casualty rout for 12 seconds (+4 seconds per additional rank). Survivors may still rout when the stand expires."),
            new RegimentAbilityDefinition(RegimentAbility.GrenadierCadre,"Grenadier cadre",9,EquipmentUsers.Infantry,"Carry four additional grenades per regiment per ability rank when grenade pouches are equipped. Does not provide grenades without pouches."),
            new RegimentAbilityDefinition(RegimentAbility.IronConstitution,"Iron constitution",1,EquipmentUsers.Troops,"+5 maximum health per soldier or rider per rank."),
            new RegimentAbilityDefinition(RegimentAbility.AmmunitionReserve,"Reserve ammunition",1,EquipmentUsers.Infantry|EquipmentUsers.Artillery,"+2 reserve shots or full magazines per rank; artillery receives two extra rounds per gun."),
            new RegimentAbilityDefinition(RegimentAbility.RallyingSpirit,"Rallying spirit",1,EquipmentUsers.All,"+3 starting and maximum morale per rank."),
            new RegimentAbilityDefinition(RegimentAbility.WeaponMastery,"Weapon mastery",1,EquipmentUsers.Troops,"+2% melee damage per rank."),
            new RegimentAbilityDefinition(RegimentAbility.ArtilleryMunitions,"Artillery munitions",1,EquipmentUsers.Artillery,"Rank 1 unlocks canister; rank 2 explosive shells; rank 3 incendiary carcass shells; rank 4 lime shells; rank 5 poison gas shells. Roundshot is always available. Each rank also adds two reserve rounds per gun. Spend one ability point per rank, with no regiment level requirement.")
        };
        public static RegimentAbilityDefinition Find(RegimentAbility id)=>Array.Find(All,a=>a.Id==id);
    }
}
