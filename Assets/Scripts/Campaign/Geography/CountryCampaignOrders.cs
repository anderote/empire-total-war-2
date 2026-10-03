using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    // Quotes and order validation share the same rules as the actions they describe.
    public sealed partial class CountryCampaign
    {
        public int ConstructionCost(string actor,string region,string kind)
        {
            int cost=RegionCampaign.Cost(kind);
            return cost+Math.Max(0,Owned(actor).Count-Progress(actor).startingRegions)*Balance.constructionGrowthCost+
                World.State(region).buildings.Count(b=>b.kind!="Empty")*cost/10;
        }
        public float ConstructionCapacity(string region)=>1+CampaignBuildingTiers.Completed(World.State(region)).Sum(t=>t.Construction)+(Progress(World.State(region).owner).policy=="Investment"?.25f:0);
        public int UpgradeCost(string kind,int nextLevel)=>CampaignBuildingTiers.Get(kind,nextLevel)?.Cost??0;
        public int TrainingCapacity(string region)=>CampaignBuildingTiers.Sum(World.State(region),t=>t.Training);
        public int MaterialCapacity(string region)=>240+CampaignBuildingTiers.Sum(World.State(region),t=>t.MaterialStorage);
        public int MaterialProduction(string region)=>6+CampaignBuildingTiers.Sum(World.State(region),t=>t.Materials);
        public int FreightCapacity(string region)=>Balance.freightPerRegion+CampaignBuildingTiers.Sum(World.State(region),t=>t.Freight);
        public bool CanBuild(string actor,string region,string kind,int slot,out string reason)
        {
            reason="Construction requires an owned city during your turn.";
            if(!Owns(actor,region))return false;
            var state=World.State(region);
            if(slot<0||slot>=state.buildings.Length||state.buildings[slot].kind!="Empty"){reason="Choose an empty building slot.";return false;}
            if(RegionCampaign.Cost(kind)<=0){reason="Unknown building.";return false;}
            if(kind=="Arsenal"&&!Knows(actor,"arsenals")){reason="Requires Arsenal proof standards research.";return false;}
            int cost=ConstructionCost(actor,region,kind);
            if(World.FactionState(actor).treasury<cost){reason="Need "+(cost-World.FactionState(actor).treasury)+" more treasury.";return false;}
            reason=Besieged(region)||InRevolt(region)?"Can fund now; work pauses until the city is secure.":"Ready to build.";return true;
        }
        public bool CanUpgrade(string actor,string region,int slot,out string reason)
        {
            reason="Upgrades require an owned city during your turn.";
            if(!Owns(actor,region))return false;
            var state=World.State(region);
            if(slot<0||slot>=state.buildings.Length){reason="Choose a building.";return false;}
            var b=state.buildings[slot];
            if(b.kind=="Empty"){reason="Build in this slot first.";return false;}
            if(b.turnsRemaining>0){reason="Finish or cancel the current project first.";return false;}
            if(b.raidDamage>0||b.raidRepairLiability>0){reason="Fully repair raid damage and liability before upgrading.";return false;}
            var next=CampaignBuildingTiers.Get(b.kind,b.level+1);
            if(next==null){reason="Maximum building tier reached.";return false;}
            if(next.Research!=""&&!Knows(actor,next.Research)){reason="Requires "+Technology(next.Research).name+" research for "+next.Name+" (tier "+next.Level+").";return false;}
            int cost=next.Cost;
            if(World.FactionState(actor).treasury<cost){reason="Need "+(cost-World.FactionState(actor).treasury)+" more treasury.";return false;}
            reason="Production pauses during the upgrade.";return true;
        }
        public int RecruitCost(string role)=>role=="Militia"?120:role=="Rifle"?270:role=="Guard"?300:role=="Light"?210:Balance.recruitCost;
        public int RecruitMaterials(string role)=>role=="Militia"?10:role=="Rifle"?25:15;
        public int RecruitTurns(string role,string region="")=>region!=""&&FacilityLevel(region,"Barracks")>=5&&(role=="Regular"||role=="Militia")?1:role=="Regular"?Balance.trainingTurns:2;
        public bool RecruitmentFacilities(string region,string role,out string reason)
        {
            int tier=role=="Guard"?4:role=="Rifle"?3:role=="Light"?2:1;
            reason="Requires completed Barracks Lv "+tier+" in "+Place(region)+" (current province).";
            if(FacilityLevel(region,"Barracks")<tier)return false;
            return HasEquipmentFacilities(region,role=="Rifle"?WeaponFacilities("rifle"):new EquipmentFacilityRequirement(0,0),out reason);
        }
        public int AvailableManpower(string region)
        {
            var state=World.State(region);
            return Math.Max(0,(int)(state.economy.population*(Balance.manpowerFraction+(Progress(state.owner).policy=="Mobilization"?.02f:0)))-state.economy.enlisted);
        }
        public int FreeTrainingPlaces(string region)=>Math.Max(0,TrainingCapacity(region)-TrainingPlacesUsed(region));
        public bool CanReceiveRecruits(CampaignArmy army,string actor,string region)=>army!=null&&army.owner==actor&&army.regionId==region&&AtSettlement(army,region)&&
            !Committed(army)&&!InTransit(army)&&army.marchTarget==""&&army.units.Count<MaxArmyRegiments;
        public bool CanAssignRecruits(string actor,string region,string targetArmyId,out string reason)
        {
            reason="";if(string.IsNullOrEmpty(targetArmyId))return true;
            var army=Army(targetArmyId);
            if(!CanReceiveRecruits(army,actor,region)){reason="The receiving army must be idle in this city, with room for recruits.";return false;}
            if(army.units.Count+Save.recruitment.Count(q=>q.targetArmyId==targetArmyId)>=MaxArmyRegiments){reason="Army's 80 regiment places are filled or reserved.";return false;}
            return true;
        }
        public bool CanRecruit(string actor,string region,string role,string targetArmyId,out string reason)
        {
            reason="Recruit at an owned city during your turn.";
            if(!Owns(actor,region))return false;
            if(!ValidRole(role)){reason="Unknown regiment type.";return false;}
            if(Besieged(region)||InRevolt(region)){reason="Recruitment is paused by siege or revolt.";return false;}
            if(Buildings(region,"Barracks")==0){reason="Build or finish a barracks here first.";return false;}
            if(!RecruitmentFacilities(region,role,out reason))return false;
            string roleResearch=role=="Rifle"?"rifles":role=="Light"?"light-infantry":role=="Guard"?"drill":"";
            if(roleResearch!=""&&!Knows(actor,roleResearch)){reason="Requires "+Technology(roleResearch).name+" research.";return false;}
            if(role=="Rifle"&&Year<EmpireBattle.MilitaryEquipment.Weapon("rifle").EarliestYear){reason="Flintlock rifles are available from 1800.";return false;}
            if(!CanAssignRecruits(actor,region,targetArmyId,out reason))return false;
            if(FreeTrainingPlaces(region)==0){reason="All training places are in use. Wait or cancel a queued regiment.";return false;}
            if(AvailableManpower(region)<Balance.unitSize){reason="Requires "+Balance.unitSize+" available local recruits.";return false;}
            if(World.FactionState(actor).treasury<RecruitCost(role)){reason="Need "+(RecruitCost(role)-World.FactionState(actor).treasury)+" more treasury.";return false;}
            if(World.State(region).economy.materials<RecruitMaterials(role)){reason="Requires "+RecruitMaterials(role)+" local materials.";return false;}
            reason="Ready to recruit.";return true;
        }
        public bool Recruit(string actor,string region,string role,string targetArmyId,out string reason)
        {
            if(!CanRecruit(actor,region,role,targetArmyId,out reason))return false;
            int cost=RecruitCost(role),materials=RecruitMaterials(role);var economy=World.State(region).economy;
            World.FactionState(actor).treasury-=cost;economy.materials-=materials;economy.enlisted+=Balance.unitSize;
            Save.recruitment.Add(new RecruitmentOrder{id=Id("recruit-"),owner=actor,regionId=region,role=role,
                targetArmyId=targetArmyId??"",weapon=role=="Rifle"?"rifle":"musket",
                doctrine=role=="Rifle"?(int)(EmpireBattle.MilitaryDoctrine.Square|EmpireBattle.MilitaryDoctrine.LightInfantry):role=="Light"?(int)EmpireBattle.MilitaryDoctrine.LightInfantry:role=="Regular"||role=="Guard"?1:0,
                soldiers=Balance.unitSize,paidCost=cost,paidMaterials=materials,totalTurns=RecruitTurns(role,region),turnsRemaining=RecruitTurns(role,region)});
            if(actor==Save.playerFaction)Save.openingRecruitment=true;
            reason=role+" regiment queued at "+Place(region)+" / "+RecruitTurns(role,region)+" turns.";return true;
        }
        CampaignArmy RecruitmentDestination(RecruitmentOrder q)
        {
            if(!string.IsNullOrEmpty(q.targetArmyId))
            {
                var target=Army(q.targetArmyId);
                return CanReceiveRecruits(target,q.owner,q.regionId)?target:NewArmy(q.owner,q.regionId);
            }
            return Save.armies.FirstOrDefault(a=>CanReceiveRecruits(a,q.owner,q.regionId))??NewArmy(q.owner,q.regionId);
        }
        public bool CanDeclareWar(string actor,string other,out string reason)
        {
            reason="Diplomatic orders are available during your turn.";
            if(!CanOrder(actor))return false;
            var treaty=Treaty(actor,other);
            if(actor==other||treaty==null){reason="No diplomatic relationship available.";return false;}
            if(treaty.war){reason="Already at war.";return false;}
            if(Save.world.turn<treaty.peaceUntil){reason="Truce: "+(treaty.peaceUntil-Save.world.turn)+" turns remaining.";return false;}
            reason="War ends trade, military access and your alliance with this country.";return true;
        }
        public IEnumerable<string> WarAllies(string actor,string other)=>Save.treaties.Where(t=>t.alliance&&(t.a==other||t.b==other))
            .Select(t=>t.a==other?t.b:t.a).Where(ally=>ally!=actor&&Treaty(actor,ally)!=null&&!AtWar(actor,ally)&&Save.world.turn>=Treaty(actor,ally).peaceUntil).Distinct();
        public bool CanMarch(string actor,string armyId,string destination,out string reason)
        {
            reason="Choose a settlement in the active campaign.";if(!Active(destination))return false;
            var p=CityPosition(destination);return CanMarchTo(actor,armyId,p.lon,p.lat,out reason,settlement:true);
        }

        sealed class ReplacementDelivery
        {
            public CampaignRegiment unit;public RegionState home,factory;public int count,cost;public float materials;
        }
        List<ReplacementDelivery> ReplacementPlan(string actor,string armyId,out string reason)
        {
            var result=new List<ReplacementDelivery>();var army=Army(armyId);
            reason="Select your army during your turn.";
            if(!CanOrder(actor)||army==null||army.owner!=actor)return result;
            if(army.lastRecoveryTurn==Save.world.turn){reason="Already reinforced this turn.";return result;}
            if(Committed(army)||army.marchTarget!=""){reason="Halt the march and finish training or siege orders first.";return result;}
            if(army.supply<.8f){reason="At least 80% supply is needed for replacements.";return result;}
            if(World.State(army.regionId).owner!=actor||!AtSettlement(army)){reason="Return to an owned city for replacements.";return result;}
            if(army.units.All(u=>u.soldiers>=Balance.unitSize)){reason="All regiments are at full strength.";return result;}
            int treasury=World.FactionState(actor).treasury;
            var enlisted=new Dictionary<string,int>();var stock=new Dictionary<string,float>();
            foreach(var unit in army.units)
            {
                var home=World.State(unit.homeRegionId);
                if(home.owner!=actor||Route(actor,army.regionId,home.regionId,true)==null)continue;
                int reserved=enlisted.TryGetValue(home.regionId,out int used)?used:home.economy.enlisted;
                int count=Math.Min(50,Math.Min(Balance.unitSize-unit.soldiers,Math.Max(0,(int)(home.economy.population*Balance.manpowerFraction)-reserved)));
                var factory=home;var weapon=EmpireBattle.MilitaryEquipment.Weapon(unit.weapon);
                if(unit.weapon!="musket")factory=Owned(actor).Where(r=>Buildings(r.regionId,"Arsenal")>0)
                    .Select(r=>new{state=r,route=Route(actor,army.regionId,r.regionId,true)})
                    .Where(x=>x.route!=null&&x.route.distance<=Balance.freightRangeKm).OrderBy(x=>x.route.distance).Select(x=>x.state).FirstOrDefault();
                if(factory==null)continue;
                float price=.25f*weapon.Cost/180f,material=.01f*weapon.Materials/15f;
                float remaining=stock.TryGetValue(factory.regionId,out float available)?available:factory.economy.materials;
                count=Math.Min(count,Math.Min((int)(Math.Max(0,treasury)/price),(int)(remaining/material)));
                if(count<=0)continue;
                int cost=(int)Math.Ceiling(count*price);float materials=count*material;
                result.Add(new ReplacementDelivery{unit=unit,home=home,factory=factory,count=count,cost=cost,materials=materials});
                treasury-=cost;enlisted[home.regionId]=reserved+count;stock[factory.regionId]=Math.Max(0,remaining-materials);
            }
            reason=result.Count>0?"Up to 50 recruits per regiment; uses remaining movement this turn.":
                "No replacements available: check treasury, home-region manpower, materials and supply routes. Special weapons need a connected arsenal.";
            return result;
        }
        public bool CanRecover(string actor,string armyId,out int soldiers,out int cost,out string reason)
        {
            var plan=ReplacementPlan(actor,armyId,out reason);soldiers=plan.Sum(p=>p.count);cost=plan.Sum(p=>p.cost);return soldiers>0;
        }
    }
}
