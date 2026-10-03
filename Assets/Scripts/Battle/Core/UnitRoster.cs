using System;
using UnityEngine;

namespace EmpireBattle
{
    public enum UnitRole { Line, Skirmisher, Rifleman, Guard, Militia, Swordsman, Cavalry }

    // Faction-neutral prototype tuning. Equipment, training and command determine advantages.
    public static class UnitRoster
    {
        public static readonly string[] Factions={"france","britain","austria","prussia","russia","italy","hungary","ottoman","ming","india"};
        public static readonly string[] FactionNames={"France","Britain","Austria","Prussia","Russia","Italy","Hungary","Ottoman Empire","Ming China","Indian States"};
        // Formed guards and regular militia retain the same command structure as line battalions.
        // Regional levies use the light roster and do not carry a European colour party.
        public static bool HasCommandParty(BattalionState unit)=>unit.Kind==UnitKind.Infantry&&
            (unit.Role==UnitRole.Line||unit.Role==UnitRole.Guard||
             unit.Role==UnitRole.Militia&&!RegionalArmyRoster.Supports(ArtFaction(unit.Faction)));
        public static bool HasFirearm(UnitRole role,UnitLoadout kit)=>Shoots(role)||role==UnitRole.Cavalry&&(EmpireBattle.Art.NapoleonicOutfits.MountedWeapon(kit)=="carbine"||EmpireBattle.Art.NapoleonicOutfits.MountedWeapon(kit)=="pistol");
        public static bool HasFirearm(BattalionState unit)=>HasFirearm(unit.Role,unit.Loadout);
        public static bool Shoots(UnitRole role)=>role!=UnitRole.Swordsman&&role!=UnitRole.Cavalry;
        public static bool Light(UnitRole role)=>role==UnitRole.Skirmisher||role==UnitRole.Rifleman;
        public static InfantryFireMode DefaultFireMode(UnitRole role)=>role==UnitRole.Cavalry?InfantryFireMode.FireAtWill:role==UnitRole.Militia?InfantryFireMode.Volley:
            Light(role)?InfantryFireMode.FireAtWill:InfantryFireMode.ByPlatoon;
        public static float Speed(UnitRole role)=>Light(role)?1.12f:role==UnitRole.Swordsman?1.08f:1;
        public static float Melee(UnitRole role)=>role==UnitRole.Swordsman?1.4f:role==UnitRole.Guard?1.15f:role==UnitRole.Militia?.72f:Light(role)?.8f:1;
        public static float Resolve(UnitRole role)=>role==UnitRole.Guard?1.18f:role==UnitRole.Militia?.75f:Light(role)?.9f:1;
        public static float Accuracy(UnitRole role)=>role==UnitRole.Skirmisher?1.08f:role==UnitRole.Guard?1.08f:role==UnitRole.Militia?.8f:1;
        public static float Cost(UnitRole role)=>role==UnitRole.Rifleman?1.5f:role==UnitRole.Guard?1.45f:role==UnitRole.Skirmisher?1.15f:role==UnitRole.Militia?.67f:role==UnitRole.Swordsman?.9f:role==UnitRole.Cavalry?2.1f:1;
        public static string Label(UnitRole role)=>role==UnitRole.Rifleman?"Riflemen":role==UnitRole.Swordsman?"Swordsmen":role==UnitRole.Skirmisher?"Skirmishers":role==UnitRole.Line?"Line infantry":role.ToString();
        public static UnitRole CampaignRole(string role,string weapon)=>role=="Militia"?UnitRole.Militia:role=="Guard"?UnitRole.Guard:role=="Light"?UnitRole.Skirmisher:role=="Rifle"||weapon=="rifle"?UnitRole.Rifleman:UnitRole.Line;
        public static string ArtFaction(string faction)
        {
            if(Array.IndexOf(Factions,faction)>=0)return faction;
            if(faction=="cisalpine"||faction=="naples"||faction=="papal"||faction=="sardinia")return "italy";
            return "generic";
        }
        public static BattleConfig CreateBattle(string player,string enemy)
        {
            int a=Array.IndexOf(Factions,player),b=Array.IndexOf(Factions,enemy);
            if(a<0||b<0)throw new ArgumentException("Unknown roster faction");
            var roles=new[]{UnitRole.Line,UnitRole.Line,UnitRole.Skirmisher,UnitRole.Rifleman,UnitRole.Guard,UnitRole.Militia,UnitRole.Swordsman,UnitRole.Cavalry};
            var config=new BattleConfig {FrenchBattalions=7,BritishBattalions=7,FrenchCavalry=1,BritishCavalry=1,
                SoldiersPerBattalion=120,RidersPerSquadron=40,FrenchArtillery=4,BritishArtillery=4,InitialSeparation=320,
                PlayerFaction=player,EnemyFaction=enemy,PlayerCountryName=FactionNames[a],EnemyCountryName=FactionNames[b],
                FormationRoles=new UnitRole[16],FormationNames=new string[16],FormationWeapons=new string[16],FormationDoctrine=new int[16],InitialStrengths=new int[16]};
            for(int i=0;i<16;i++)
            {
                string faction=i<8?player:enemy;
                var role=RegionalArmyRoster.Supports(faction)?RegionalArmyRoster.Role(i%8):roles[i%8];config.FormationRoles[i]=role;
                config.FormationNames[i]=RegionalArmyRoster.Supports(faction)?RegionalArmyRoster.Label(faction,i%8):(i<8?config.PlayerCountryName:config.EnemyCountryName)+" / "+Label(role);
                config.FormationWeapons[i]=role==UnitRole.Rifleman?"rifle":"musket";
                config.FormationDoctrine[i]=Light(role)?(int)MilitaryDoctrine.LightInfantry:role==UnitRole.Line||role==UnitRole.Guard?(int)(MilitaryDoctrine.Square|MilitaryDoctrine.Maneuver|MilitaryDoctrine.ControlledFire):0;
                config.InitialStrengths[i]=role==UnitRole.Cavalry?40:role==UnitRole.Rifleman?80:role==UnitRole.Guard?100:120;
            }
            return config;
        }
    }
}
