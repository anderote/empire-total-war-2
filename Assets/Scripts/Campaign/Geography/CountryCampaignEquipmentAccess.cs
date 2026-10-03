using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        // Technology is national; manufacturing access belongs to the army's current province.
        // Two level-one buildings provide capacity, not a level-two production facility.
        public int FacilityLevel(string region,string kind)=>World.State(region)?.buildings
            .Where(b=>b.kind==kind&&CampaignBuildingTiers.IsOperational(b))
            .Select(b=>b.level).DefaultIfEmpty(0).Max()??0;
        public string EquipmentProvince(string region)=>Place(region);
        public string EquipmentFacilities(string region)=>"Workshop Lv "+FacilityLevel(region,"Workshop")+
            " · Arsenal Lv "+FacilityLevel(region,"Arsenal")+" · "+FreeTrainingPlaces(region)+" free barracks places";

        public readonly struct EquipmentFacilityRequirement
        {
            public readonly int Workshop,Arsenal,ProducerLevel;
            public readonly string Producer;
            public EquipmentFacilityRequirement(int workshop,int arsenal,string producer="",int producerLevel=0){Workshop=workshop;Arsenal=arsenal;Producer=producer;ProducerLevel=producerLevel;}
            public int RequiredLevel(string kind)=>Math.Max(kind=="Workshop"?Workshop:kind=="Arsenal"?Arsenal:0,kind==Producer?ProducerLevel:0);
            public string Description {get {var requirement=this;return string.Join(" + ",CampaignBuildingTiers.Kinds.Where(k=>requirement.RequiredLevel(k)>0).Select(k=>k+" Lv "+requirement.RequiredLevel(k)));}}
        }
        public static EquipmentFacilityRequirement WeaponFacilities(string weapon)
        {
            switch(weapon)
            {
                case "rifle":return new EquipmentFacilityRequirement(0,1,"Barracks",3);
                case "percussion":return new EquipmentFacilityRequirement(0,2);
                case "rifle-musket":case "breechloader":return new EquipmentFacilityRequirement(3,3);
                case "magazine":return new EquipmentFacilityRequirement(4,4);
                case "smokeless":return new EquipmentFacilityRequirement(5,5);
                default:return new EquipmentFacilityRequirement(0,0);
            }
        }
        public static EquipmentFacilityRequirement ItemFacilities(EquipmentItem item)
        {
            if(item.Weapon!="")return WeaponFacilities(item.Weapon);
            int tier=CampaignBuildingTiers.EquipmentTier(item.Level);
            return new EquipmentFacilityRequirement(item.Family=="field-forges"?1:0,item.Research=="arsenals"||item.Family=="range-sights"?1:0,tier>0?CampaignBuildingTiers.EquipmentProducer(item):"",tier);
        }
        public static EquipmentFacilityRequirement QualityFacilities(int grade)=>grade>=5?new EquipmentFacilityRequirement(5,5):grade==4?new EquipmentFacilityRequirement(3,4):new EquipmentFacilityRequirement(0,grade==3?2:grade==2?1:0);
        static EquipmentFacilityRequirement LegacyWeaponFacilities(string weapon)=>weapon=="magazine"||weapon=="smokeless"?new EquipmentFacilityRequirement(2,2):weapon=="rifle-musket"||weapon=="breechloader"?new EquipmentFacilityRequirement(1,1):new EquipmentFacilityRequirement(0,weapon=="rifle"||weapon=="percussion"?1:0);
        static EquipmentFacilityRequirement LegacyItemFacilities(EquipmentItem item)=>item.Weapon!=""?LegacyWeaponFacilities(item.Weapon):new EquipmentFacilityRequirement(item.Level>=7?2:item.Level>=4||item.Family=="field-forges"?1:0,item.Research=="arsenals"||item.Family=="range-sights"?1:0);
        static EquipmentFacilityRequirement PendingItemFacilities(CampaignRegiment unit,EquipmentItem item)=>unit.legacyFacilityOrder?LegacyItemFacilities(item):ItemFacilities(item);
        static EquipmentFacilityRequirement PendingQualityFacilities(CampaignRegiment unit)=>unit.legacyFacilityOrder?(unit.pendingEquipment>=4?new EquipmentFacilityRequirement(2,2):new EquipmentFacilityRequirement(0,unit.pendingEquipment>=2?1:0)):QualityFacilities(unit.pendingEquipment);
        static EquipmentFacilityRequirement PendingWeaponFacilities(CampaignRegiment unit,string weapon)=>unit.legacyFacilityOrder?LegacyWeaponFacilities(weapon):WeaponFacilities(weapon);
        public bool HasEquipmentFacilities(string region,EquipmentFacilityRequirement required,out string reason)
        {
            var missing=CampaignBuildingTiers.Kinds.Where(k=>FacilityLevel(region,k)<required.RequiredLevel(k)).Select(k=>k+" Lv "+required.RequiredLevel(k)).ToArray();
            reason=missing.Length==0?"":"Requires operational "+string.Join(" + ",missing)+" in "+Place(region)+" (current province). Construction, upgrades and ruined facilities do not count.";
            return missing.Length==0;
        }
        public string EquipmentRequirements(EquipmentItem item)
        {
            var requirements=new List<string>();var facilities=ItemFacilities(item);
            requirements.Add(facilities.Description==""?"Basic kit: no production building required.":"Current province: "+facilities.Description+" (completed).");
            if(item.Weapon!="")requirements.Add("One free Barracks place per regiment.");
            if(item.Research!="")requirements.Add("Research: "+(Technology(item.Research)?.name??item.Research)+".");
            if(item.EarliestYear>1500)requirements.Add("Available from "+item.EarliestYear+".");
            return string.Join(" ",requirements);
        }
        bool EquipmentProvinceReady(CampaignArmy army,out string reason)=>EquipmentProvinceReady(army,false,out reason);
        bool EquipmentProvinceReady(CampaignArmy army,bool allowHostile,out string reason)
        {
            reason="";
            if(InTransit(army)||army.marchTarget!=""||UnderSiegeOrders(army)){reason="Halt movement and finish siege orders before refitting.";return false;}
            if(!Active(army.regionId)){reason="Equipment orders require an active current province.";return false;}
            string owner=World.State(army.regionId).owner;
            if(owner!=army.owner&&(!allowHostile||!AtWar(army.owner,owner))){reason="Equipment orders require an owned current province"+(allowHostile?" or one belonging to a country at war with you.":".");return false;}
            if(Besieged(army.regionId)||InRevolt(army.regionId)){reason="Equipment work pauses while the current province is besieged or in revolt.";return false;}
            if(army.supply<.8f){reason="Equipment work needs at least 80% supply.";return false;}
            return true;
        }
        public string EquipmentDeliveryStatus(CampaignArmy army,CampaignRegiment unit)
        {
            if(unit.pendingProcurement&&unit.kitTurns>0)return ProcurementDeliveryStatus(army,unit);
            if(unit.kitTurns<=0&&unit.upgradeTurns<=0)return "";
            var required=unit.kitTurns>0?PendingItemFacilities(unit,UnitEquipment.Find(unit.pendingItem)):PendingQualityFacilities(unit);
            if(!EquipmentProvinceReady(army,out string reason)||!HasEquipmentFacilities(army.regionId,required,out reason))return "Paused: "+reason;
            if(unit.kitTurns>0&&UnitEquipment.Find(unit.pendingItem).Weapon!=""&&Buildings(army.regionId,"Barracks")==0)return "Paused: requires a completed Barracks in the current province.";
            return "";
        }
        bool SavedEquipmentFacilities(string region,SavedRegiment saved,out string reason)
        {
            if(!HasEquipmentFacilities(region,WeaponFacilities(saved.weapon),out reason)||!HasEquipmentFacilities(region,QualityFacilities(saved.quality),out reason))return false;
            foreach(var id in saved.loadout.items)if(!HasEquipmentFacilities(region,ItemFacilities(UnitEquipment.Find(id)),out reason))return false;
            return true;
        }
    }
}
