using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        public int Year=>Balance.startYear+(Save.world.turn-1+Balance.startSeason)/2;
        public static bool Busy(CampaignArmy army)=>army!=null&&(army.marchProgress>0||AtSea(army)||TrainingBusy(army));
        public static bool TrainingBusy(CampaignArmy army)=>army!=null&&(army.refitTurns>0||army.units.Any(u=>u.trainingTurns>0||u.upgradeTurns>0||u.kitTurns>0));
        public IEnumerable<string> Prerequisites(CampaignTechnology tech)=>(tech.prerequisites??Array.Empty<string>())
            .Concat(string.IsNullOrEmpty(tech.prerequisite)?Array.Empty<string>():new[]{tech.prerequisite}).Distinct();
        public bool ResearchAvailable(string actor,CampaignTechnology tech,out string reason)
        {
            reason="";if(tech==null||Progress(actor)==null){reason="Unknown research.";return false;}
            if(!string.IsNullOrEmpty(tech.unavailableReason)){reason=tech.unavailableReason;return false;}
            if(Year<tech.minYear){reason="Scenario eligibility begins in "+tech.minYear+".";return false;}
            var missing=Prerequisites(tech).Where(id=>!Knows(actor,id)).Select(id=>Technology(id)?.name??id).ToArray();
            if(missing.Length>0){reason="Requires "+string.Join(" and ",missing)+".";return false;}
            return true;
        }
        void ValidateResearchTree()
        {
            if(Balance.startYear<1500||Balance.startYear>1918)throw new ArgumentException("Invalid scenario year.");
            var ids=new HashSet<string>();
            foreach(var t in Balance.technologies)
                if(t==null||string.IsNullOrEmpty(t.id)||!ids.Add(t.id)||t.cost<1||t.cost>10000)throw new ArgumentException("Invalid research definition.");
            var visiting=new HashSet<string>();var visited=new HashSet<string>();
            Action<string> walk=null;walk=id=>{
                if(visited.Contains(id))return;if(!ids.Contains(id)||!visiting.Add(id))throw new ArgumentException("Missing prerequisite or cyclic research tree.");
                foreach(var next in Prerequisites(Technology(id)))walk(next);
                visiting.Remove(id);visited.Add(id);
            };
            foreach(var id in ids)walk(id);
        }
        bool ValidResearchProgress(CountryProgress country,int year)
        {
            bool eligible(CampaignTechnology technology)=>technology!=null&&year>=technology.minYear
                &&string.IsNullOrEmpty(technology.unavailableReason)
                &&Prerequisites(technology).All(country.technologies.Contains);
            if(country.technologies.Any(id=>!eligible(Technology(id))))return false;
            if(country.research=="")return country.researchProgress==0;
            var current=Technology(country.research);
            return eligible(current)&&!country.technologies.Contains(country.research)&&country.researchProgress<current.cost;
        }
        public sealed class AdoptionCourse
        {
            public string id,name,research,weapon;
            public int cost,materials,turns,doctrine;
            public AdoptionCourse(string id,string name,string research,int cost,int materials,int turns,int doctrine=0,string weapon="")
            {this.id=id;this.name=name;this.research=research;this.cost=cost;this.materials=materials;this.turns=turns;this.doctrine=doctrine;this.weapon=weapon;}
        }
        public static readonly AdoptionCourse[] Courses={
            new AdoptionCourse("maneuver","Battalion maneuver","maneuver",50,2,1,(int)(MilitaryDoctrine.Square|MilitaryDoctrine.Maneuver)),
            new AdoptionCourse("volleys","Controlled volleys","volleys",50,2,1,(int)MilitaryDoctrine.ControlledFire),
            new AdoptionCourse("light-infantry","Skirmish instruction","light-infantry",60,3,1,(int)MilitaryDoctrine.LightInfantry),
            new AdoptionCourse("rifle","Issue flintlock rifles","rifles",100,10,2,(int)(MilitaryDoctrine.Square|MilitaryDoctrine.LightInfantry),"rifle"),
            new AdoptionCourse("percussion","Issue percussion muskets","percussion",90,8,2,0,"percussion"),
            new AdoptionCourse("rifle-musket","Issue rifle muskets","rifle-muskets",140,12,2,0,"rifle-musket"),
            new AdoptionCourse("breechloader","Issue breechloaders","breechloading",175,15,2,0,"breechloader"),
            new AdoptionCourse("magazine","Issue magazine rifles","magazines",215,20,2,0,"magazine"),
            new AdoptionCourse("smokeless","Issue smokeless rifles","smokeless",240,23,2,0,"smokeless")
        };
        public static AdoptionCourse Course(string id)=>Array.Find(Courses,c=>c.id==id);
        public int AdoptionCost(CampaignRegiment unit,AdoptionCourse course)=>Math.Max(1,(int)Math.Ceiling(course.cost*unit.soldiers/(double)Balance.unitSize));
        public int AdoptionMaterials(CampaignRegiment unit,AdoptionCourse course)=>Math.Max(1,(int)Math.Ceiling(course.materials*unit.soldiers/(double)Balance.unitSize));
        public bool CanAdopt(string actor,CampaignArmy army,CampaignRegiment unit,AdoptionCourse course,out string reason)
        {
            reason="Adoption unavailable.";
            if(!CanOrder(actor)||army==null||army.owner!=actor||unit==null||!army.units.Contains(unit)||course==null)return false;
            if(!Owns(actor,army.regionId)||InTransit(army)||army.refitTurns>0||unit.trainingTurns>0||unit.upgradeTurns>0||unit.kitTurns>0||army.supply<.8f){reason="Needs a supplied army at home, with no overlapping refit.";return false;}
            if(!Knows(actor,course.research)){reason="Requires research: "+(Technology(course.research)?.name??course.research)+".";return false;}
            if(!ResearchAvailableForAdoption(course,out reason))return false;
            if(course.weapon!=""&&unit.weapon==course.weapon||course.weapon==""&&(unit.doctrine&course.doctrine)==course.doctrine){reason="Already adopted.";return false;}
            int barracks=TrainingCapacity(army.regionId);
            int occupied=Save.armies.Where(a=>a.regionId==army.regionId).Sum(a=>a.units.Count(u=>u.trainingTurns>0||(u.kitTurns>0&&UnitEquipment.Find(u.pendingItem)?.Weapon!="")))+Save.recruitment.Count(q=>q.regionId==army.regionId);
            if(barracks<=occupied){reason="No free barracks training place.";return false;}
            if(!EquipmentProvinceReady(army,out reason))return false;
            if(course.weapon!=""&&!HasEquipmentFacilities(army.regionId,WeaponFacilities(course.weapon),out reason))return false;
            if(World.FactionState(actor).treasury<AdoptionCost(unit,course)||World.State(army.regionId).economy.materials<AdoptionMaterials(unit,course))
            {reason="Insufficient treasury or materials.";return false;}
            reason="";return true;
        }
        bool ResearchAvailableForAdoption(AdoptionCourse course,out string reason)
        {
            reason="";var weapon=MilitaryEquipment.Weapon(course.weapon);
            if(weapon!=null&&Year<weapon.EarliestYear){reason="Weapon is outside this scenario's eligibility.";return false;}
            return true;
        }
        public bool AdoptRegiment(string actor,string armyId,string unitId,string courseId,out string reason)
        {
            var army=Army(armyId);var unit=army?.units.Find(u=>u.id==unitId);var course=Course(courseId);
            if(!CanAdopt(actor,army,unit,course,out reason))return false;
            World.FactionState(actor).treasury-=AdoptionCost(unit,course);World.State(army.regionId).economy.materials-=AdoptionMaterials(unit,course);
            unit.legacyFacilityOrder=false;unit.course=course.id;unit.trainingTurns=course.turns;army.movement=0;
            reason=course.name+" ordered; existing equipment retained until completion.";return true;
        }
        void AdvanceTraining(CampaignArmy army)
        {
            foreach(var unit in army.units)
            {
                if(unit.trainingTurns<=0)continue;
                if(World.State(army.regionId).owner!=army.owner)
                {unit.trainingTurns=0;unit.course="";unit.legacyFacilityOrder=false;Event("Regimental adoption interrupted away from home.");continue;}
                if(!EquipmentProvinceReady(army,out _)||Buildings(army.regionId,"Barracks")==0||!HasEquipmentFacilities(army.regionId,PendingWeaponFacilities(unit,Course(unit.course).weapon),out _))continue;
                if(--unit.trainingTurns>0)continue;
                var course=Course(unit.course);unit.doctrine|=course.doctrine;
                if(course.weapon!=""){unit.weapon=course.weapon;unit.loadout.items.RemoveAll(id=>UnitEquipment.Find(id)?.Slot==EquipmentSlot.Weapon);}
                if(course.weapon=="rifle")unit.role="Rifle";
                else if(unit.role=="Militia"&&MilitaryEquipment.Has(unit.doctrine,MilitaryDoctrine.Square))unit.role="Regular";
                Event(army.owner+" adopted "+course.name+" in "+unit.id+".");unit.course="";unit.legacyFacilityOrder=false;
            }
        }
        public bool Recruit(string actor,string region,string role,out string reason)=>Recruit(actor,region,role,"",out reason);
        int TrainingPlacesUsed(string region)=>Save.recruitment.Count(q=>q.regionId==region)+Save.armies.Where(a=>a.regionId==region).Sum(a=>a.units.Count(u=>u.trainingTurns>0||(u.kitTurns>0&&UnitEquipment.Find(u.pendingItem)?.Weapon!="")));
        static bool ValidRole(string role)=>role=="Regular"||role=="Militia"||role=="Rifle"||role=="Light"||role=="Guard";
        static bool ValidMilitary(CampaignRegiment unit,int year)=>ValidRole(unit.role)&&MilitaryEquipment.Weapon(unit.weapon)!=null&&MilitaryEquipment.Weapon(unit.weapon).EarliestYear<=year&&MilitaryEquipment.ValidDoctrine(unit.doctrine)
            &&unit.trainingTurns>=0&&unit.trainingTurns<=2&&(unit.trainingTurns==0?unit.course=="":Course(unit.course)!=null);
        static bool ValidRecruitment(RecruitmentOrder queue,int year)=>ValidRole(queue.role)&&MilitaryEquipment.Weapon(queue.weapon)!=null&&MilitaryEquipment.Weapon(queue.weapon).EarliestYear<=year&&MilitaryEquipment.ValidDoctrine(queue.doctrine);
        static void MigrateMilitary(CountryCampaignSave candidate)
        {
            // Keep paid quality and existing formation access; never turn generic quality into a rifle.
            foreach(var army in candidate.armies)if(army?.units!=null)foreach(var unit in army.units)if(unit!=null)
            {unit.weapon="musket";unit.role="Regular";unit.doctrine=(int)(MilitaryDoctrine.Square|MilitaryDoctrine.ControlledFire|MilitaryDoctrine.LightInfantry);unit.course="";unit.trainingTurns=0;}
            foreach(var queue in candidate.recruitment)if(queue!=null){queue.weapon="musket";queue.role="Regular";queue.doctrine=1;}
            candidate.schemaVersion=2;
        }
        public float RegimentPower(CampaignRegiment unit)
        {
            var weapon=MilitaryEquipment.Weapon(unit.weapon);var kit=unit.loadout.Modifiers;
            // An estimate for mixed field encounters, not an extra tactical damage bonus.
            float reach=weapon==null?1:(float)Math.Sqrt(weapon.EffectiveRange/150f);
            float cycle=weapon==null?15:(weapon.Reload+(weapon.Magazine-1)*weapon.Cycle)/weapon.Magazine;
            float cadence=(float)Math.Sqrt(15f/cycle);
            return (unit.role=="Militia"?.8f:1f)*(.6f+.4f*reach*cadence*(weapon?.Precision??1))*(1+unit.equipment*.08f)*unit.progression.PowerMultiplier
                *(1+kit.accuracy*.3f-kit.reload*.3f+kit.melee*.2f+kit.resolve*.15f+kit.supply*.1f+kit.range*.1f)
                *(MilitaryEquipment.Has(unit.doctrine,MilitaryDoctrine.Maneuver)?1.03f:1)
                *(MilitaryEquipment.Has(unit.doctrine,MilitaryDoctrine.ControlledFire)?1.06f:1)
                *(MilitaryEquipment.Has(unit.doctrine,MilitaryDoctrine.LightInfantry)&&unit.role=="Rifle"?1.04f:1);
        }
        void ThinkAdoption(string actor)
        {
            foreach(var force in Save.armies.Where(a=>a.owner==actor&&!Committed(a)))foreach(var regiment in force.units)
            {
                foreach(var skill in new[]{UnitSkill.Accuracy,UnitSkill.Discipline,UnitSkill.Drill,UnitSkill.Endurance,UnitSkill.Melee})
                    if(regiment.progression.AvailablePoints>0&&regiment.progression.Level(skill)<3)TrainSkill(actor,force.id,regiment.id,skill,out _);
            }
            var funds=World.FactionState(actor);if(funds.treasury<Balance.reserveBudget+150)return;
            foreach(var army in Save.armies.Where(a=>a.owner==actor&&!Committed(a)&&World.State(a.regionId).owner==actor))
            {
                if(Save.armies.Any(a=>AtWar(actor,a.owner)&&CanObserve(a,actor)&&(a.regionId==army.regionId||graph[army.regionId].ContainsKey(a.regionId))))continue;
                foreach(var unit in army.units)
                {
                    foreach(string id in new[]{"maneuver","volleys","light-infantry","rifle"})
                        if(AdoptRegiment(actor,army.id,unit.id,id,out _))return;
                    foreach(string item in new[]{"regimental-colours","stout-boots","field-rations","extra-cartridges"})
                        if(funds.treasury>Balance.reserveBudget+250&&EquipItem(actor,army.id,unit.id,item,out _))return;
                }
            }
        }
    }
}
