using System;
using System.Linq;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    public readonly struct EquipmentProcurementQuote
    {
        public readonly int Treasury,Materials,Recipients,FacilitySlot,RaidUses,RepairLiability;
        public readonly bool Hostile,Raid;
        public EquipmentProcurementQuote(int treasury,int materials,int recipients,bool hostile,bool raid,int slot,int uses,int liability)
        {Treasury=treasury;Materials=materials;Recipients=recipients;Hostile=hostile;Raid=raid;FacilitySlot=slot;RaidUses=uses;RepairLiability=liability;}
    }

    public sealed partial class CountryCampaign
    {
        public bool HostileEquipmentProvince(CampaignArmy army)=>army!=null&&Active(army.regionId)&&AtWar(army.owner,World.State(army.regionId).owner);
        bool HasSecuredProcurement(CampaignArmy army)=>army.units.Any(u=>u.pendingProcurement&&u.kitTurns>0&&u.procurementRegionId==army.regionId);
        public int ProcurementRationCost(CampaignArmy army)=>(int)Math.Ceiling(army.Soldiers/1000f*Math.Max(.5f,1-army.units.Average(u=>u.loadout.Modifiers.supply)));
        public string ProcurementRations(CampaignArmy army)=>"Fitting uses paid local army rations (up to "+ProcurementRationCost(army)+" treasury per turn). Food shortages or insufficient treasury pause delivery.";
        public int EquipmentPurchaseCost(CampaignArmy army,CampaignRegiment unit,EquipmentItem item)
            =>HostileEquipmentProvince(army)?(int)Math.Ceiling(ItemCost(unit,item)*1.5):ItemCost(unit,item);

        int RaidProducerSlot(CampaignArmy army,EquipmentItem item)
        {
            if(army==null||item==null||!Active(army.regionId))return -1;
            string producer=CampaignBuildingTiers.EquipmentProducer(item);var required=ItemFacilities(item);
            int level=Math.Max(1,required.RequiredLevel(producer));
            return World.State(army.regionId).buildings.Select((b,i)=>new{building=b,slot=i})
                .Where(x=>x.building.kind==producer&&x.building.level>=level&&CampaignBuildingTiers.IsOperational(x.building))
                .OrderByDescending(x=>FacilityRaidUses(army.regionId,x.slot)).ThenBy(x=>x.building.level).ThenBy(x=>x.slot)
                .Select(x=>x.slot).DefaultIfEmpty(-1).First();
        }
        public EquipmentProcurementQuote EquipmentQuote(CampaignArmy army,CampaignRegiment[] units,EquipmentItem item,bool raid=false)
        {
            units=units??Array.Empty<CampaignRegiment>();
            if(army==null||item==null||units.Any(u=>u==null))return new EquipmentProcurementQuote(0,0,0,false,raid,-1,0,0);
            int price=units.Sum(u=>EquipmentPurchaseCost(army,u,item)),materials=units.Sum(u=>ItemMaterials(u,item));
            int liability=raid?units.Sum(u=>{int saved=EquipmentPurchaseCost(army,u,item);return Math.Max(saved+1,(int)(((long)saved*11+9)/10));}):0;
            return new EquipmentProcurementQuote(raid?0:price,materials,units.Length,HostileEquipmentProvince(army),raid,raid?RaidProducerSlot(army,item):-1,raid?units.Length:0,liability);
        }
        bool CanProcureEquipment(string actor,CampaignArmy army,CampaignRegiment[] units,EquipmentItem item,bool raid,out EquipmentProcurementQuote quote,out string reason)
        {
            quote=EquipmentQuote(army,units,item,raid);reason="Select existing regiments from this army, once each.";
            if(!CanOrder(actor)||army==null||army.owner!=actor||item==null||units==null||units.Length==0||units.Distinct().Count()!=units.Length||units.Any(u=>u==null||!army.units.Contains(u)||u.soldiers<1))return false;
            if(!EquipmentProvinceReady(army,true,out reason))return false;
            if(World.State(army.regionId).owner!=actor&&!quote.Hostile){reason="Equipment orders require your own province or an enemy province at war.";return false;}
            if(raid&&!quote.Hostile){reason="Raids require an enemy current province at war.";return false;}
            if(Committed(army)){reason="Finish the army's current training or refit first.";return false;}
            if(Year<item.EarliestYear){reason="Available from "+item.EarliestYear+".";return false;}
            if(item.Research!=""&&!Knows(actor,item.Research)){reason="Requires "+(Technology(item.Research)?.name??item.Research)+" research.";return false;}
            foreach(var unit in units)
            {
                if(!item.Supports(UnitEquipment.CampaignUsers(unit.role))){reason="This unit cannot use that item.";return false;}
                if(!UnitEquipment.CanReplace(unit.loadout,item)){reason="Already equipped at this level or higher.";return false;}
            }
            if(!HasEquipmentFacilities(army.regionId,ItemFacilities(item),out reason))return false;
            if(item.Weapon!=""&&FreeTrainingPlaces(army.regionId)<units.Length)
            {reason="Requires "+units.Length+" free Barracks places in "+Place(army.regionId)+"; "+FreeTrainingPlaces(army.regionId)+" available.";return false;}
            if(World.FactionState(actor).treasury<quote.Treasury||World.State(army.regionId).economy.materials<quote.Materials)
            {reason="Requires "+quote.Treasury+" treasury and "+quote.Materials+" materials for the entire selection.";return false;}
            if(raid)
            {
                if(quote.FacilitySlot<0){reason="Raiding needs an operational "+CampaignBuildingTiers.EquipmentProducer(item)+" producing this kit in the current province.";return false;}
                int remaining=FacilityRaidUses(army.regionId,quote.FacilitySlot);
                if(remaining<quote.RaidUses){reason="Requires "+quote.RaidUses+" raid uses from one "+CampaignBuildingTiers.EquipmentProducer(item)+"; "+remaining+" remain. Select fewer recipients.";return false;}
                if(!CanDamageFacility(army.regionId,quote.FacilitySlot,quote.RaidUses,quote.RepairLiability,out reason))return false;
            }
            reason="";return true;
        }
        public bool CanRaidEquipmentItems(string actor,CampaignArmy army,CampaignRegiment[] units,EquipmentItem item,out string reason)
            =>CanProcureEquipment(actor,army,units,item,true,out _,out reason);
        public bool RaidEquipmentItems(string actor,string armyId,string[] unitIds,string itemId,out string reason)
            =>ProcureEquipment(actor,armyId,unitIds,itemId,true,out reason);
        bool ProcureEquipment(string actor,string armyId,string[] unitIds,string itemId,bool raid,out string reason)
        {
            var army=Army(armyId);var item=UnitEquipment.Find(itemId);reason="Select existing regiments from this army, once each.";
            if(army==null||unitIds==null||unitIds.Length==0||unitIds.Distinct().Count()!=unitIds.Length||unitIds.Any(id=>!army.units.Any(u=>u.id==id)))return false;
            var units=army.units.Where(u=>unitIds.Contains(u.id)).ToArray();
            if(!CanProcureEquipment(actor,army,units,item,raid,out var quote,out reason))return false;
            // A synchronous, fully validated batch commits once. No recipient is issued before all gates pass.
            if(raid&&!DamageFacility(army.regionId,quote.FacilitySlot,quote.RaidUses,quote.RepairLiability,out reason))return false;
            World.FactionState(actor).treasury-=quote.Treasury;
            World.State(army.regionId).economy.materials-=quote.Materials;
            foreach(var unit in units)
            {
                unit.legacyFacilityOrder=false;unit.pendingItem=item.Id;unit.kitTurns=item.Weapon==""?1:2;
                unit.pendingProcurement=quote.Hostile;
                unit.procurementRegionId=quote.Hostile?army.regionId:"";
                unit.procurementOwnerId=quote.Hostile?World.State(army.regionId).owner:"";
            }
            army.movement=0;
            reason=UnitEquipment.Label(item)+(raid?" secured by raid":" ordered")+" for "+units.Length+" regiments; "+(item.Weapon==""?1:2)+" turn(s) to fit.";
            if(raid)reason+=" Facility loses "+quote.RaidUses+" use(s); repair liability rises by "+quote.RepairLiability+" treasury.";
            return true;
        }
        public string EquipmentRaidDescription(CampaignArmy army,CampaignRegiment[] units,EquipmentItem item)
        {
            var quote=EquipmentQuote(army,units,item,true);
            if(quote.FacilitySlot<0)return "Requires an operational "+CampaignBuildingTiers.EquipmentProducer(item)+" producing this kit. Each recipient consumes one facility use and the normal local materials.";
            var building=World.State(army.regionId).buildings[quote.FacilitySlot];int remaining=FacilityRaidUses(army.regionId,quote.FacilitySlot);
            return "Raid "+building.kind+" Lv "+building.level+" (slot "+(quote.FacilitySlot+1)+"): "+quote.RaidUses+" of "+remaining+" remaining uses. "+
                (quote.RaidUses>=remaining?"Exhausting its uses destroys the facility and removes its unlocks. ":"")+
                "Adds "+quote.RepairLiability+" treasury repair debt; total repair costs at least "+(building.raidRepairLiability+quote.RepairLiability)+" treasury plus materials. Secured gear remains deliverable after source destruction.";
        }
        string ProcurementDeliveryStatus(CampaignArmy army,CampaignRegiment unit)
        {
            if(army.regionId!=unit.procurementRegionId)return "Interrupted: the army left the province where its gear was secured.";
            if(InTransit(army)||army.marchTarget!=""||UnderSiegeOrders(army))return "Paused: halt movement and finish siege orders before fitting secured gear.";
            if(Besieged(army.regionId)||InRevolt(army.regionId))return "Paused: the current province is besieged or in revolt.";
            if(army.supply<.8f)return "Paused: fitting secured gear needs at least 80% supply.";
            var item=UnitEquipment.Find(unit.pendingItem);
            if(item!=null&&item.Weapon!=""&&FacilityLevel(army.regionId,"Barracks")==0)return "Paused: fitting secured firearms requires an operational Barracks.";
            return "";
        }
        static void ClearProcurementReceipt(CampaignRegiment unit)
        {unit.pendingProcurement=false;unit.procurementRegionId=unit.procurementOwnerId="";}
        static bool ValidProcurementReceipt(CountryCampaignSave candidate,CampaignArmy army,CampaignRegiment unit)
        {
            if(!unit.pendingProcurement)return string.IsNullOrEmpty(unit.procurementRegionId)&&string.IsNullOrEmpty(unit.procurementOwnerId);
            return unit.kitTurns>0&&!unit.legacyFacilityOrder&&candidate.buildingProgressionVersion==CampaignBuildingTiers.Version&&candidate.activeRegions.Contains(unit.procurementRegionId)&&unit.procurementOwnerId!=army.owner&&candidate.countries.Any(c=>c.factionId==unit.procurementOwnerId);
        }
    }
}
