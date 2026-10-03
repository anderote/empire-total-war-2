using System;
using System.Linq;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        public int ItemCost(CampaignRegiment unit,EquipmentItem item)=>Math.Max(1,(int)Math.Ceiling(item.Cost*unit.soldiers/(double)Balance.unitSize));
        public int ItemMaterials(CampaignRegiment unit,EquipmentItem item)=>Math.Max(1,(int)Math.Ceiling(item.Materials*unit.soldiers/(double)Balance.unitSize));
        public bool CanEquipItem(string actor,CampaignArmy army,CampaignRegiment unit,EquipmentItem item,out string reason)
            =>CanProcureEquipment(actor,army,new[]{unit},item,false,out _,out reason);
        public bool EquipItem(string actor,string armyId,string unitId,string itemId,out string reason)
            =>ProcureEquipment(actor,armyId,new[]{unitId},itemId,false,out reason);
        public bool CanEquipItems(string actor,CampaignArmy army,CampaignRegiment[] units,EquipmentItem item,out string reason)
            =>CanProcureEquipment(actor,army,units,item,false,out _,out reason);
        public bool EquipItems(string actor,string armyId,string[] unitIds,string itemId,out string reason)
            =>ProcureEquipment(actor,armyId,unitIds,itemId,false,out reason);
        void AdvanceItems(CampaignArmy army)
        {
            foreach(var unit in army.units)
            {
                if(unit.kitTurns==0)continue;var item=UnitEquipment.Find(unit.pendingItem);
                bool interrupted=unit.pendingProcurement?army.regionId!=unit.procurementRegionId:World.State(army.regionId).owner!=army.owner;
                if(interrupted){unit.kitTurns=0;unit.pendingItem="";unit.legacyFacilityOrder=false;ClearProcurementReceipt(unit);Event("Equipment delivery interrupted after leaving its issue province.");continue;}
                if(unit.pendingProcurement){if(ProcurementDeliveryStatus(army,unit)!="")continue;}
                else if(!EquipmentProvinceReady(army,out _)||!HasEquipmentFacilities(army.regionId,PendingItemFacilities(unit,item),out _)||(item.Weapon!=""&&Buildings(army.regionId,"Barracks")==0))continue;
                if(--unit.kitTurns>0)continue;
                unit.loadout.Equip(item.Id,UnitEquipment.CampaignUsers(unit.role),Year);
                if(item.Weapon!="")
                {
                    unit.weapon=item.Weapon;
                    if(item.Weapon=="rifle"){unit.role="Rifle";unit.doctrine|=(int)(MilitaryDoctrine.Square|MilitaryDoctrine.LightInfantry);}
                }
                unit.pendingItem="";unit.legacyFacilityOrder=false;ClearProcurementReceipt(unit);Event(unit.id+" received "+UnitEquipment.Label(item)+".");
            }
        }
        static bool ValidLoadout(CampaignRegiment unit,int year)
        {
            if(unit.loadout==null||!unit.loadout.Valid(UnitEquipment.CampaignUsers(unit.role),year)||unit.kitTurns<0||unit.kitTurns>2)return false;
            if(unit.loadout.items.Any(id=>UnitEquipment.Find(id).Weapon!=""&&UnitEquipment.Find(id).Weapon!=unit.weapon))return false;
            if(unit.kitTurns==0)return unit.pendingItem==""&&!unit.pendingProcurement&&string.IsNullOrEmpty(unit.procurementRegionId)&&string.IsNullOrEmpty(unit.procurementOwnerId);
            var item=UnitEquipment.Find(unit.pendingItem);
            return item!=null&&item.Supports(UnitEquipment.CampaignUsers(unit.role))&&year>=item.EarliestYear
                &&unit.trainingTurns==0&&unit.upgradeTurns==0&&(item.Weapon!=""||unit.kitTurns==1);
        }
        public bool TrainSkill(string actor,string armyId,string unitId,UnitSkill skill,out string reason)
        {
            var army=Army(armyId);var unit=army?.units.Find(u=>u.id==unitId);reason="Skill upgrade unavailable.";
            if(!CanOrder(actor)||army==null||army.owner!=actor||unit==null||Committed(army))return false;
            if(!unit.progression.Train(skill)){reason="Requires an unspent stat point; maximum "+UnitProgression.MaxSkill+" points per stat.";return false;}
            reason=skill+" upgraded to "+unit.progression.Level(skill)+".";return true;
        }
        public bool LearnAbility(string actor,string armyId,string unitId,RegimentAbility ability,out string reason)
        {
            var army=Army(armyId);var unit=army?.units.Find(u=>u.id==unitId);var definition=RegimentAbilities.Find(ability);reason="Ability unavailable.";
            if(!CanOrder(actor)||army==null||army.owner!=actor||unit==null||Committed(army)||definition==null||!definition.Supports(UnitEquipment.CampaignUsers(unit.role)))return false;
            if(!unit.progression.Learn(ability)){reason="Requires an unspent ability point and the listed regiment level.";return false;}reason=definition.Name+" learned.";return true;
        }
        public int EquipmentUpgradeCost(CampaignRegiment unit)=>Math.Max(1,(int)Math.Ceiling(60.0*unit.soldiers/Balance.unitSize));
        public int EquipmentUpgradeMaterials(CampaignRegiment unit)=>Math.Max(1,(int)Math.Ceiling(5.0*unit.soldiers/Balance.unitSize));
        public bool CanUpgradeEquipment(string actor,CampaignArmy army,CampaignRegiment unit,out string reason)
        {
            reason="Equipment upgrade unavailable.";
            if(!CanOrder(actor)||army==null||army.owner!=actor||unit==null||!army.units.Contains(unit))return false;
            if(!EquipmentProvinceReady(army,out reason))return false;
            if(Committed(army)){reason="Finish the army's current training or refit first.";return false;}
            if(unit.equipment>=UnitProgression.MaxQuality){reason="Maximum equipment quality reached.";return false;}
            string research=unit.equipment==0?"drill":"arsenals";
            if(!Knows(actor,research)){reason="Requires "+(Technology(research)?.name??research)+" research.";return false;}
            if(!HasEquipmentFacilities(army.regionId,QualityFacilities(unit.equipment+1),out reason))return false;
            if(World.FactionState(actor).treasury<EquipmentUpgradeCost(unit)||World.State(army.regionId).economy.materials<EquipmentUpgradeMaterials(unit))
            {reason="Insufficient treasury or materials.";return false;}
            reason="";return true;
        }
        public bool UpgradeEquipment(string actor,string armyId,string unitId,out string reason)
        {
            var army=Army(armyId);var unit=army?.units.Find(u=>u.id==unitId);
            if(!CanUpgradeEquipment(actor,army,unit,out reason))return false;
            World.FactionState(actor).treasury-=EquipmentUpgradeCost(unit);World.State(army.regionId).economy.materials-=EquipmentUpgradeMaterials(unit);
            unit.legacyFacilityOrder=false;unit.pendingEquipment=unit.equipment+1;unit.upgradeTurns=2;army.movement=0;
            reason=UnitProgression.EquipmentName(unit.pendingEquipment)+" ordered; arrives in two turns.";return true;
        }
        void AdvanceEquipment(CampaignArmy army)
        {
            foreach(var unit in army.units)
            {
                if(unit.upgradeTurns==0)continue;
                if(World.State(army.regionId).owner!=army.owner){unit.upgradeTurns=unit.pendingEquipment=0;unit.legacyFacilityOrder=false;Event("Regimental refit interrupted away from home.");continue;}
                if(!EquipmentProvinceReady(army,out _)||!HasEquipmentFacilities(army.regionId,PendingQualityFacilities(unit),out _))continue;
                if(--unit.upgradeTurns==0){unit.equipment=unit.pendingEquipment;unit.pendingEquipment=0;unit.legacyFacilityOrder=false;Event(unit.id+" received "+UnitProgression.EquipmentName(unit.equipment)+" kit.");}
            }
        }
        static bool ValidProgression(CampaignRegiment unit)=>unit.progression!=null&&unit.progression.Valid&&unit.upgradeTurns>=0&&unit.upgradeTurns<=2
            &&(unit.upgradeTurns==0?unit.pendingEquipment==0:unit.pendingEquipment==unit.equipment+1&&unit.pendingEquipment<=UnitProgression.MaxQuality&&unit.trainingTurns==0);
        static void MigrateProgression(CountryCampaignSave candidate)
        {
            foreach(var unit in candidate.armies.Where(a=>a?.units!=null).SelectMany(a=>a.units).Concat(candidate.pending?.garrison??new System.Collections.Generic.List<CampaignRegiment>()))
            {
                if(unit==null)continue;unit.progression=new UnitProgression();unit.progression.Award(Math.Min(5,Math.Max(0,unit.experience))*100);
                unit.upgradeTurns=unit.pendingEquipment=0;
                unit.loadout=new UnitLoadout();unit.kitTurns=0;unit.pendingItem="";ClearProcurementReceipt(unit);
            }
            candidate.schemaVersion=3;
        }
    }
}
