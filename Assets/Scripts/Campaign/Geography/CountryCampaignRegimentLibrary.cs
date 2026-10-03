using System;
using System.Linq;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        public static string SavedRole(SavedRegiment saved)=>saved.role==UnitRole.Militia?"Militia":saved.role==UnitRole.Skirmisher?"Light":saved.role==UnitRole.Rifleman?"Rifle":saved.role==UnitRole.Guard?"Guard":"Regular";
        public int SavedRecruitCost(SavedRegiment saved)=>(saved.role==UnitRole.Militia?120:saved.role==UnitRole.Rifleman?270:saved.role==UnitRole.Guard?300:saved.role==UnitRole.Skirmisher?210:Balance.recruitCost)+saved.KitCost+saved.VeteranCost;
        public int SavedRecruitMaterials(SavedRegiment saved)=>(saved.role==UnitRole.Militia?10:saved.role==UnitRole.Rifleman?25:15)+saved.KitMaterials;
        bool SavedResearch(string actor,SavedRegiment saved,out string reason)
        {
            reason="";
            string roleTech=saved.role==UnitRole.Rifleman?"rifles":saved.role==UnitRole.Skirmisher?"light-infantry":saved.role==UnitRole.Guard?"drill":"";
            string qualityTech=saved.quality>=2?"arsenals":saved.quality==1?"drill":"";
            foreach(string tech in new[]{roleTech,qualityTech,MilitaryEquipment.Weapon(saved.weapon).Research}.Concat(saved.loadout.items.Select(i=>UnitEquipment.Find(i).Research)).Where(t=>t!="").Distinct())
                if(!Knows(actor,tech)){reason="Requires "+(Technology(tech)?.name??tech)+" research.";return false;}
            return true;
        }
        public bool CanRecruitSaved(string actor,string region,SavedRegiment saved,out string reason)
            =>CanRecruitSaved(actor,region,saved,"",out reason);
        public bool CanRecruitSaved(string actor,string region,SavedRegiment saved,string targetArmyId,out string reason)
        {
            reason="Recruit saved regiments during your turn at an owned barracks.";
            if(!CanOrder(actor)||!Owns(actor,region)||Besieged(region)||InRevolt(region)||Buildings(region,"Barracks")==0)return false;
            if(!CanAssignRecruits(actor,region,targetArmyId,out reason))return false;
            if(saved==null||!saved.Valid){reason="Invalid saved infantry regiment.";return false;}
            if(saved.RequiredYear>Year){reason="This equipment requires year "+saved.RequiredYear+"; current campaign year is "+Year+".";return false;}
            if(!SavedResearch(actor,saved,out reason))return false;
            if((saved.quality>0||saved.role==UnitRole.Rifleman)&&Buildings(region,"Arsenal")==0){reason="This cadre's equipment requires a local arsenal.";return false;}
            if(!RecruitmentFacilities(region,SavedRole(saved),out reason)||!SavedEquipmentFacilities(region,saved,out reason))return false;
            if(TrainingPlacesUsed(region)>=TrainingCapacity(region)){reason="Barracks training capacity is occupied.";return false;}
            var e=World.State(region).economy;
            if(e.enlisted+Balance.unitSize>e.population*(Balance.manpowerFraction+(Progress(actor).policy=="Mobilization"?.02f:0))){reason="Local manpower reserve exhausted.";return false;}
            if(World.FactionState(actor).treasury<SavedRecruitCost(saved)||e.materials<SavedRecruitMaterials(saved)){reason="Requires "+SavedRecruitCost(saved)+" treasury and "+SavedRecruitMaterials(saved)+" local materials.";return false;}
            reason="";return true;
        }
        public bool RecruitSaved(string actor,string region,SavedRegiment saved,out string reason)
            =>RecruitSaved(actor,region,saved,"",out reason);
        public bool RecruitSaved(string actor,string region,SavedRegiment saved,string targetArmyId,out string reason)
        {
            if(!CanRecruitSaved(actor,region,saved,targetArmyId,out reason))return false;
            int cost=SavedRecruitCost(saved),materials=SavedRecruitMaterials(saved);var e=World.State(region).economy;
            World.FactionState(actor).treasury-=cost;e.materials-=materials;e.enlisted+=Balance.unitSize;
            Save.recruitment.Add(new RecruitmentOrder {id=Id("recruit-"),owner=actor,regionId=region,role=SavedRole(saved),weapon=saved.weapon,
                doctrine=(int)MilitaryEquipment.AllDoctrine,soldiers=Balance.unitSize,paidCost=cost,paidMaterials=materials,totalTurns=Balance.trainingTurns,turnsRemaining=Balance.trainingTurns,
                savedRegiment=saved.Copy(),libraryRecruit=true,targetArmyId=targetArmyId??""});
            Save.openingRecruitment=true;reason="Recruiting "+saved.name+" · "+Balance.trainingTurns+" turns. Veteran cadre, abilities and equipment retained.";return true;
        }
        bool ValidSavedRecruitment(RecruitmentOrder queue,int year,CountryCampaignSave candidate)
        {
            if(!queue.libraryRecruit)return queue.paidCost<=Math.Max(300,Balance.recruitCost)&&queue.paidMaterials<=25&&string.IsNullOrEmpty(queue.savedRegiment?.id);
            var s=queue.savedRegiment;
            if(s==null||!s.Valid||s.RequiredYear>year||queue.role!=SavedRole(s)||queue.weapon!=s.weapon||queue.doctrine!=(int)MilitaryEquipment.AllDoctrine||queue.paidCost!=SavedRecruitCost(s)||queue.paidMaterials!=SavedRecruitMaterials(s))return false;
            var country=candidate.countries.Find(c=>c.factionId==queue.owner);
            string roleTech=s.role==UnitRole.Rifleman?"rifles":s.role==UnitRole.Skirmisher?"light-infantry":s.role==UnitRole.Guard?"drill":"";
            string qualityTech=s.quality>=2?"arsenals":s.quality==1?"drill":"";
            return new[]{roleTech,qualityTech,MilitaryEquipment.Weapon(s.weapon).Research}.Concat(s.loadout.items.Select(i=>UnitEquipment.Find(i).Research)).Where(t=>t!="").All(t=>country.technologies.Contains(t));
        }
        static void ApplySavedRecruitment(RecruitmentOrder queue,CampaignRegiment unit)
        {
            if(!queue.libraryRecruit)return;var s=queue.savedRegiment;
            unit.name=s.name;unit.progression=s.progression.Copy();unit.loadout=s.loadout.Copy();unit.equipment=s.quality;unit.experience=unit.progression.Rank;
        }
    }
}
