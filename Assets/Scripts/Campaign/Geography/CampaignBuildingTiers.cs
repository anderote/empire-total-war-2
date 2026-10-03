using System;
using System.Linq;
using System.Collections.Generic;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    // One source of truth for quotes, effects, local unlocks, save bounds and art keys.
    public sealed class BuildingTier
    {
        public readonly string Kind,Name,Research,Special;
        public readonly int Level,Cost,Work,Food,Wealth,Workers,Upkeep,Materials,MaterialStorage,Storage,Training,ResearchPoints,ResearchCeiling,Freight,Defense;
        public readonly float Construction;
        public string ArtKey=>"UI/CampaignArt/Buildings/"+Kind+"-level-"+Level;
        public BuildingTier(string kind,string name,string research,int level,int cost,int work,int food,int wealth,int workers,int upkeep,int materials,int materialStorage,int storage,int training,int researchPoints,int researchCeiling,int freight,int defense,float construction,string special)
        {Kind=kind;Name=name;Research=research;Level=level;Cost=cost;Work=work;Food=food;Wealth=wealth;Workers=workers;Upkeep=upkeep;Materials=materials;MaterialStorage=materialStorage;Storage=storage;Training=training;ResearchPoints=researchPoints;ResearchCeiling=researchCeiling;Freight=freight;Defense=defense;Construction=construction;Special=special;}
        public string Effects
        {
            get {var parts=new List<string>();
                if(Food>0)parts.Add("+"+Food+" food / turn");if(Wealth>0)parts.Add("+"+Wealth+" taxable wealth");
                if(Materials>0)parts.Add("+"+Materials+" materials / turn");if(Construction>0)parts.Add("+"+Construction.ToString("0.##")+" construction work / turn");
                if(MaterialStorage>0)parts.Add("+"+MaterialStorage+" material storage");if(Storage>0)parts.Add("+"+Storage+" civilian food turns (province cap 20)");
                if(Training>0)parts.Add(Training+" shared training places");if(ResearchPoints>0)parts.Add("+"+ResearchPoints+" national research / turn");
                if(ResearchCeiling>0)parts.Add("+"+ResearchCeiling+" national research ceiling (best university)");
                if(Freight>0)parts.Add("+"+Freight+" food freight / turn");if(Defense>0)parts.Add("+"+(Defense*200)+" base militia; +"+(Defense*22)+"% autoresolve protection");
                parts.Add(Workers.ToString("N0")+" jobs; "+Upkeep+" upkeep / turn");return string.Join(" · ",parts);}
        }
    }
    public static class CampaignBuildingTiers
    {
        public const int Version=1,MaxWork=5;
        public static int RaidDurability(int level)=>level==1?4:level==2||level==3?5:level==4?6:level==5?7:0;
        public static bool IsOperational(RegionBuilding b)=>b!=null&&Get(b.kind,b.level)!=null&&b.turnsRemaining==0&&!b.upgrading&&b.raidDamage<RaidDurability(b.level);
        public static readonly BuildingTier[] All={
            new BuildingTier("Farm","Smallholding","",1,200,2,35,40,2000,4,0,0,0,0,0,0,0,0,0f,""),
            new BuildingTier("Farm","Enclosed Farm","",2,300,2,70,80,4000,8,0,0,0,0,0,0,0,0,0f,""),
            new BuildingTier("Farm","Irrigated Estate","agriculture",3,500,3,125,140,5500,13,0,0,0,0,0,0,0,0,0f,""),
            new BuildingTier("Farm","Livestock Estate","irrigation",4,800,4,195,210,6500,20,0,0,0,0,0,0,0,0,0f,""),
            new BuildingTier("Farm","Agricultural Cooperative","administration",5,1200,5,280,300,7500,30,0,0,0,0,0,0,0,0,0f,""),
            new BuildingTier("Workshop","Artisan Workshop","",1,400,2,0,220,6000,12,12,0,0,0,0,0,0,0,0.5f,""),
            new BuildingTier("Workshop","Guild Workshop","industry",2,600,2,0,440,12000,24,24,0,0,0,0,0,0,0,1f,""),
            new BuildingTier("Workshop","Machine Workshop","arsenals",3,1000,3,0,720,15500,38,40,80,0,0,0,0,0,0,1.75f,""),
            new BuildingTier("Workshop","Precision Works","commerce",4,1600,4,0,1060,19000,56,60,160,0,0,0,0,0,0,2.75f,""),
            new BuildingTier("Workshop","Manufacturing Complex","administration",5,2400,5,0,1450,23000,80,84,280,0,0,0,0,0,0,4f,""),
            new BuildingTier("Market","Town Market","",1,300,2,0,100,2000,6,0,0,0,0,0,0,0,0,0f,""),
            new BuildingTier("Market","Covered Market","",2,450,2,0,200,4000,12,0,0,0,0,0,0,10,0,0f,""),
            new BuildingTier("Market","Merchant Exchange","commerce",3,750,3,0,360,5500,20,0,0,0,0,0,0,25,0,0f,""),
            new BuildingTier("Market","Bonded Warehouses","logistics",4,1200,4,0,580,7500,31,0,0,0,0,0,0,45,0,0f,""),
            new BuildingTier("Market","Commercial Exchange","administration",5,1800,5,0,850,9500,45,0,0,0,0,0,0,70,0,0f,""),
            new BuildingTier("University","Parish School","",1,550,3,0,0,2000,20,0,0,0,0,3,0,0,0,0f,""),
            new BuildingTier("University","Provincial College","",2,825,2,0,0,4000,40,0,0,0,0,6,0,0,0,0f,""),
            new BuildingTier("University","University","education",3,1375,3,0,0,5500,60,0,0,0,0,10,2,0,0,0f,""),
            new BuildingTier("University","Royal Academy","administration",4,2200,4,0,0,7000,85,0,0,0,0,15,4,0,0,0f,""),
            new BuildingTier("University","Research Institute","commerce",5,3300,5,0,0,9000,115,0,0,0,0,21,6,0,0,0f,""),
            new BuildingTier("Barracks","Garrison Barracks","",1,300,2,0,0,1000,8,0,0,0,1,0,0,0,0,0f,"Militia and Line Infantry recruitment"),
            new BuildingTier("Barracks","Drill Barracks","drill",2,450,2,0,0,2000,16,0,0,0,2,0,0,0,0,0f,"Light Infantry recruitment (light-infantry research)"),
            new BuildingTier("Barracks","Rifle Depot","light-infantry",3,750,3,0,0,3000,26,0,0,0,3,0,0,0,0,0f,"Rifle recruitment (rifles research and Arsenal 1)"),
            new BuildingTier("Barracks","Guards Barracks","maneuver",4,1200,4,0,0,4000,38,0,0,0,4,0,0,0,0,0f,"Guard recruitment (drill research)"),
            new BuildingTier("Barracks","Army Training Centre","logistics",5,1800,5,0,0,6000,54,0,0,0,6,0,0,0,0,0f,"Line and Militia recruitment shortened to one turn"),
            new BuildingTier("Granary","Village Granary","",1,250,2,0,0,0,5,0,0,2,0,0,0,0,0,0f,""),
            new BuildingTier("Granary","Public Granary","",2,375,2,0,0,0,10,0,40,4,0,0,0,0,0,0f,""),
            new BuildingTier("Granary","Supply Depot","agriculture",3,625,3,0,0,0,17,0,100,6,0,0,0,0,0,0f,""),
            new BuildingTier("Granary","Provisioning Yard","logistics",4,1000,4,0,0,0,27,0,180,9,0,0,0,0,0,0f,""),
            new BuildingTier("Granary","Strategic Reserve","administration",5,1500,5,0,0,0,40,0,280,12,0,0,0,0,0,0f,""),
            new BuildingTier("Fortress","Stockade","",1,450,2,0,0,1000,15,0,0,0,0,0,0,0,1,0f,""),
            new BuildingTier("Fortress","Bastion Fort","",2,675,2,0,0,2000,30,0,0,0,0,0,0,0,2,0f,""),
            new BuildingTier("Fortress","Citadel","industry",3,1125,3,0,0,3000,48,0,0,0,0,0,0,0,3,0f,""),
            new BuildingTier("Fortress","Fortress Complex","logistics",4,1800,4,0,0,4500,70,0,0,0,0,0,0,0,4,0f,""),
            new BuildingTier("Fortress","Entrenched Fortress","administration",5,2700,5,0,0,6500,100,0,0,0,0,0,0,0,5,0f,""),
            new BuildingTier("Arsenal","Proof House","arsenals",1,450,2,0,0,3000,15,0,0,0,0,0,0,0,0,0f,"Flintlock rifles with Barracks 3; quality 2 (research applies)"),
            new BuildingTier("Arsenal","Ordnance Arsenal","arsenals",2,675,2,0,0,6000,30,0,0,0,0,0,0,0,0,0f,"Percussion muskets from 1830; quality 3"),
            new BuildingTier("Arsenal","Machine Arsenal","industry",3,1125,3,0,40,8000,48,4,0,0,0,0,0,0,0,0f,"Breechloaders from 1840 / rifle muskets from 1850 + Workshop 3"),
            new BuildingTier("Arsenal","Precision Arsenal","commerce",4,1800,4,0,90,10500,70,8,0,0,0,0,0,0,0,0f,"Magazine rifles from 1880 + Workshop 4; quality 4 + Workshop 3"),
            new BuildingTier("Arsenal","National Arsenal","administration",5,2700,5,0,160,13500,98,14,0,0,0,0,0,0,0,0f,"Smokeless rifles from 1886 + Workshop 5; quality 5 + Workshop 5"),
        };
        public static readonly string[] Kinds=All.Select(t=>t.Kind).Distinct().ToArray();
        static readonly Dictionary<string,BuildingTier[]> ByKind=All.GroupBy(t=>t.Kind).ToDictionary(g=>g.Key,g=>g.OrderBy(t=>t.Level).ToArray());
        public static BuildingTier Get(string kind,int level)=>kind!=null&&ByKind.TryGetValue(kind,out var tiers)&&level>=1&&level<=tiers.Length?tiers[level-1]:null;
        public static int MaxLevel(string kind)=>kind!=null&&ByKind.TryGetValue(kind,out var tiers)?tiers.Length:0;
        public static IEnumerable<BuildingTier> Completed(RegionState state)=>state.buildings.Where(IsOperational).Select(b=>Get(b.kind,b.level)).Where(t=>t!=null);
        public static int Sum(RegionState state,Func<BuildingTier,int> effect)=>Completed(state).Sum(effect);
        public static int EquipmentTier(int level)=>level<=3?0:level==4?1:level==5?2:level<=7?3:level<=9?4:5;
        public static string EquipmentProducer(EquipmentItem item)
        {
            if(item.Weapon!="")return "Arsenal";
            switch(item.Family)
            {
                case "pack-mules":case "water-carts":return "Farm";
                case "field-rations":return "Granary";
                case "medical-chests":case "officer-spyglasses":case "campaign-maps":return "University";
                case "sapper-tools":case "leather-jerkins":case "grenade-pouches":return "Fortress";
                case "drums-fifes":case "regimental-colours":case "imperial-standard":case "battle-honours":case "veteran-ncos":case "trumpeters":case "gunner-pennants":return "Barracks";
                case "range-sights":case "selected-powder":case "cleaning-kits":case "extra-cartridges":case "ready-cartridges":case "waxed-cartridges":case "reserve-caissons":case "artillery-fuses":case "gun-sights":return "Arsenal";
                case "stout-boots":case "light-packs":case "officer-gorgets":return "Market";
                default:return item.Slot==EquipmentSlot.Uniform?"Market":"Workshop";
            }
        }
        public static string Unlocks(BuildingTier tier)
        {
            if(tier==null)return "";
            string band=tier.Level==1?"4":tier.Level==2?"5":tier.Level==3?"6–7":tier.Level==4?"8–9":"10";
            var families=UnitEquipment.Items.Where(i=>i.Level==1&&i.Weapon==""&&EquipmentProducer(i)==tier.Kind&&i.Supports(EquipmentUsers.Infantry)).Select(i=>i.Name);
            return (tier.Special==""?"":tier.Special+". ")+"Local kit level "+band+": "+string.Join(", ",families)+". Research, role, year and other listed requirements still apply.";
        }
        public static bool ValidBuilding(RegionBuilding b)
        {
            if(b==null||b.level<1||b.turnsRemaining<0||b.turnsRemaining>MaxWork||float.IsNaN(b.workRemaining)||float.IsInfinity(b.workRemaining)||b.workRemaining<0||b.workRemaining>MaxWork||float.IsNaN(b.totalWork)||float.IsInfinity(b.totalWork)||b.totalWork<0||b.totalWork>MaxWork||b.paidCost<0||b.paidCost>100000||b.raidDamage<0||b.raidDamage>RaidDurability(b.level)||b.raidRepairLiability<0||b.raidRepairLiability>1000000000||(b.raidDamage==0)!=(b.raidRepairLiability==0)||(b.raidDamage>0&&(b.turnsRemaining>0||b.upgrading)))return false;
            if(b.kind=="Empty")return b.level==1&&b.turnsRemaining==0&&!b.upgrading&&b.workRemaining==0&&b.paidCost==0&&b.raidDamage==0;
            if(Get(b.kind,b.level)==null||b.upgrading&&(b.level<2||b.turnsRemaining==0))return false;
            // Legacy regional saves may have only integer turns; paid fractional projects must be coherent.
            if(b.turnsRemaining>0&&b.totalWork>0&&b.workRemaining>b.totalWork)return false;
            return true;
        }
    }
}
