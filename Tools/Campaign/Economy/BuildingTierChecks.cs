using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EmpireBattle;
using EmpireCampaign.Geography;

public static class BuildingTierChecks
{
    static string root;static int checks;
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
    static T Read<T>(string p)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root,p)),Json);
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    static CountryCampaign Fresh(int year=1886)
    {
        var b=Read<CampaignBalance>("Assets/Resources/CountryCampaignBalance.json");b.startYear=year;
        var c=new CountryCampaign(Read<RegionCatalog>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/campaign.json"),Read<EconomyProfiles>("Assets/Resources/CampaignEconomyProfiles.json"),Read<TheaterData>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/world.json"),b,"france");
        c.Progress("france").technologies=b.technologies.Where(t=>t.minYear<=year&&string.IsNullOrEmpty(t.unavailableReason)).Select(t=>t.id).ToList();
        c.World.FactionState("france").treasury=1000000;c.World.State("region-paris").economy.materials=9000;
        foreach(var s in c.World.State("region-paris").buildings){s.kind="Empty";s.level=1;}
        return c;
    }
    static CountryCampaignSave Clone(CountryCampaign c)=>JsonSerializer.Deserialize<CountryCampaignSave>(JsonSerializer.Serialize(c.Save,Json),Json);
    static CampaignArmy Army(CountryCampaign c){var a=c.Save.armies.First(x=>x.owner=="france");a.regionId="region-paris";a.supply=1;return a;}
    static RegionBuilding Put(CountryCampaign c,int slot,string kind,int level)=>c.World.State("region-paris").buildings[slot]=new RegionBuilding{kind=kind,level=level};
    static void Call(CountryCampaign c,string name,params object[] args)=>typeof(CountryCampaign).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,args);
    static void Restore(CountryCampaign c,string label="round trip")=>Check(c.Adopt(Clone(c),out string why),label+": "+why);
    public static void Run(string project)
    {
        root=project;CatalogAndOutputs();Construction();LocalUnlocksAndRecruitment();Migration();DamageAndRepair();
        Console.WriteLine("PASS: "+checks+" building tier, construction, local unlock, recruitment, migration and facility condition assertions; actual model, no Unity/game launch.");
    }
    static void CatalogAndOutputs()
    {
        Check(CampaignBuildingTiers.Kinds.Length==8&&CampaignBuildingTiers.All.Length==40,"Eight kinds, forty meaningful tiers");
        Check(CampaignBuildingTiers.All.Select(t=>t.ArtKey).Distinct().Count()==40,"Forty distinct canonical artwork keys");
        var c=Fresh(1800);var state=c.World.State("region-paris");
        foreach(string kind in CampaignBuildingTiers.Kinds)
        {
            var tiers=CampaignBuildingTiers.All.Where(t=>t.Kind==kind).ToArray();
            Check(tiers.Select(t=>t.Level).SequenceEqual(new[]{1,2,3,4,5})&&tiers.Select(t=>t.Name).Distinct().Count()==5,kind+" contiguous named tiers");
            Check(tiers.Select(t=>t.Effects).Distinct().Count()==5&&tiers.Select(t=>CampaignBuildingTiers.Unlocks(t)).Distinct().Count()==5,kind+" each tier changes output and unlocks");
            for(int level=1;level<=5;level++)
            {
                var tier=CampaignBuildingTiers.Get(kind,level);var b=Put(c,0,kind,level);
                Check(CampaignBuildingTiers.ValidBuilding(b)&&CampaignBuildingTiers.IsOperational(b),kind+level+" valid operational save state");
                Check(tier.Research==""||c.Technology(tier.Research)!=null&&string.IsNullOrEmpty(c.Technology(tier.Research).unavailableReason),kind+level+" uses implemented research");
                Check(CampaignBuildingTiers.Completed(state).Single()==tier,kind+level+" actual economy consumes the canonical tier");
                Check(CampaignBuildingTiers.Unlocks(tier).Contains("Local kit level"),kind+level+" has explicit purchasable local kit");
                if(level>1)Check(tier.Cost>CampaignBuildingTiers.Get(kind,level-1).Cost,kind+level+" increasing capital cost");
                b.upgrading=true;b.turnsRemaining=1;Check(c.FacilityLevel(state.regionId,kind)==0&&CampaignBuildingTiers.Completed(state).Count()==0,kind+level+" projects grant no unlock/output");
            }
            state.buildings[0]=new RegionBuilding();
        }
        Put(c,0,"Workshop",5);Check(c.MaterialProduction(state.regionId)==90&&c.ConstructionCapacity(state.regionId)==5&&c.MaterialCapacity(state.regionId)==520,"Workshop5 materials, throughput and stores");
        Put(c,0,"Granary",5);Check(c.World.Economy.Forecast(state).foodStorage==state.economy.population/1000f*16&&c.MaterialCapacity(state.regionId)==520,"Granary5 adds meaningful food/material capacity above old cap");
        Put(c,1,"Granary",5);Check(c.World.Economy.Forecast(state).foodStorage==state.economy.population/1000f*20,"Multiple granaries obey finite storage ceiling");state.buildings[1]=new RegionBuilding();
        Put(c,0,"Market",5);Check(c.FreightCapacity(state.regionId)==c.Balance.freightPerRegion+70,"Market5 changes actual freight capacity");
        Put(c,0,"University",5);Check(c.ResearchCapacity("france")==c.Balance.researchCap+6,"University5 raises bounded national research ceiling");
        Put(c,0,"Barracks",5);Check(c.TrainingCapacity(state.regionId)==6,"Barracks5 has six real training places");
        Put(c,0,"Farm",5);var forecast=c.World.Economy.Forecast(state);var p=c.World.Economy.Profile(state.regionId);Check(Math.Abs(forecast.foodProduced-(p.foodCapacity+state.economy.foodBonus+280)*forecast.staffing)<.01,"Farm5 feeds actual forecast");
    }
    static void Construction()
    {
        foreach(string kind in CampaignBuildingTiers.Kinds)
        {
            var c=Fresh();var state=c.World.State("region-paris");int cash=c.World.FactionState("france").treasury;
            Check(c.Build("france",state.regionId,kind,0,out var why),kind+" first tier build: "+why);var b=state.buildings[0];
            Check(b.level==1&&b.workRemaining==CampaignBuildingTiers.Get(kind,1).Work&&c.World.FactionState("france").treasury==cash-b.paidCost,kind+" exact initial quote/work payment");
            Restore(c,kind+" in-flight construction save");state=c.World.State(state.regionId);b=state.buildings[0];b.turnsRemaining=0;b.workRemaining=0;b.paidCost=0;
            for(int next=2;next<=5;next++)
            {
                var tier=CampaignBuildingTiers.Get(kind,next);cash=c.World.FactionState("france").treasury;
                Check(c.Upgrade("france",state.regionId,0,out why),kind+next+" actual upgrade order: "+why);b=state.buildings[0];
                Check(b.level==next&&b.workRemaining==tier.Work&&b.totalWork==tier.Work&&b.paidCost==tier.Cost&&c.World.FactionState("france").treasury==cash-tier.Cost,kind+next+" quote/action agreement");
                Restore(c,kind+next+" paid project save");state=c.World.State(state.regionId);b=state.buildings[0];b.workRemaining=tier.Work-.5f;b.turnsRemaining=(int)Math.Ceiling(b.workRemaining);
                int refund=(int)(tier.Cost*b.workRemaining/tier.Work);cash=c.World.FactionState("france").treasury;
                Check(c.CancelConstruction("france",state.regionId,0,out why)&&b.level==next-1&&!b.upgrading&&c.World.FactionState("france").treasury==cash+refund,kind+next+" cancellation restores previous tier and unused paid fraction");
                Check(c.Upgrade("france",state.regionId,0,out why),kind+next+" re-order permitted");b.turnsRemaining=0;b.workRemaining=0;b.upgrading=false;b.paidCost=0;
            }
            Check(!c.Upgrade("france",state.regionId,0,out why)&&why.Contains("Maximum"),kind+" cannot exceed five");Restore(c,kind+" final tier save");
        }
        var actual=Fresh();var state2=actual.World.State("region-paris");Put(actual,0,"Barracks",4);Check(actual.Upgrade("france",state2.regionId,0,out _),"Queue tier5 for actual completion");
        Put(actual,1,"Workshop",5);Call(actual,"FinishRound");Check(state2.buildings[0].level==5&&!state2.buildings[0].upgrading&&state2.buildings[0].turnsRemaining==0&&actual.TrainingCapacity(state2.regionId)==6,"FinishRound consumes sharedwork and enables tier5 only on completion");Restore(actual);
        var noTech=Fresh();Put(noTech,0,"Workshop",1);noTech.Progress("france").technologies.Clear();Check(!noTech.CanUpgrade("france","region-paris",0,out var message)&&message.Contains("Workshop standardization"),"Research gate uses canonical name");
    }
    static void LocalUnlocksAndRecruitment()
    {
        foreach(string kind in CampaignBuildingTiers.Kinds)
        {
            var baseItem=UnitEquipment.Items.First(i=>i.Level==1&&i.Weapon==""&&CampaignBuildingTiers.EquipmentProducer(i)==kind&&i.Supports(EquipmentUsers.Line)&&i.Research=="");
            foreach(int itemLevel in new[]{4,5,6,8,10})
            {
                var c=Fresh();var a=Army(c);var item=UnitEquipment.Items.Single(i=>i.Family==baseItem.Family&&i.Level==itemLevel);int tier=CampaignBuildingTiers.EquipmentTier(itemLevel);var b=Put(c,0,kind,tier);
                Check(c.CanEquipItem("france",a,a.units[0],item,out var why),kind+tier+" unlocks actual "+item.Id+": "+why);
                if(tier>1){b.level=tier-1;Put(c,1,kind,1);Check(!c.CanEquipItem("france",a,a.units[0],item,out why)&&why.Contains(kind+" Lv "+tier),kind+" duplicate lower tiers do not unlock "+item.Id);}
                b.level=tier;b.turnsRemaining=1;Check(!c.CanEquipItem("france",a,a.units[0],item,out why),kind+tier+" unfinished producer cannot sell premiumkit");
            }
        }
        string[] roles={"Regular","Militia","Light","Rifle","Guard"};int[] levels={1,1,2,3,4};
        for(int i=0;i<roles.Length;i++)
        {
            var c=Fresh();var a=Army(c);Put(c,0,"Barracks",levels[i]);Put(c,1,"Arsenal",1);
            Check(c.Recruit("france",a.regionId,roles[i],a.id,out var why),roles[i]+" recruitable at advertised barracks tier: "+why);Restore(c,roles[i]+" queue save");var q=c.Save.recruitment.Single();q.turnsRemaining=1;Call(c,"FinishRound");
            Check(c.Army(a.id).units.Last().role==roles[i],roles[i]+" recruitment completes with actual persistent role");Restore(c,roles[i]+" born regiment save");
            if(levels[i]>1){c=Fresh();Put(c,0,"Barracks",levels[i]-1);Put(c,1,"Arsenal",5);Check(!c.CanRecruit("france","region-paris",roles[i],"",out why),roles[i]+" locked below advertised tier");}
        }
        var elite=Fresh();var army=Army(elite);Put(elite,0,"Barracks",5);Check(elite.RecruitTurns("Regular",army.regionId)==1&&elite.RecruitTurns("Militia",army.regionId)==1&&elite.RecruitTurns("Guard",army.regionId)==2,"Tier5 speeds basic training only");
        Check(elite.Recruit("france",army.regionId,"Regular",out _)&&elite.FreeTrainingPlaces(army.regionId)==5,"Tier5 six places share recruitment reservations");
        int paid=elite.Save.recruitment.Single().paidCost,cash=elite.World.FactionState("france").treasury;Restore(elite,"One-turn training save");var fast=elite.Save.recruitment.Single();Check(fast.totalTurns==1&&elite.CancelRecruitment("france",fast.id,out _)&&elite.World.FactionState("france").treasury==cash+paid,"One-turn unworked order refunds its full persisted duration quote");
        var young=Fresh(1792);army=Army(young);Put(young,0,"Barracks",5);Put(young,1,"Arsenal",5);Check(!young.CanRecruit("france",army.regionId,"Rifle","",out _),"High tiers do not grant weapons before their year");
    }
    static void Migration()
    {
        foreach(string mode in new[]{"item","quality","course"})
        {
            var c=Fresh();var a=Army(c);var u=a.units[0];Put(c,0,"Barracks",2);Put(c,1,"Workshop",2);Put(c,2,"Arsenal",2);
            if(mode=="item"){u.pendingItem="smokeless-rifles";u.kitTurns=2;}
            if(mode=="quality"){u.equipment=4;u.pendingEquipment=5;u.upgradeTurns=2;}
            if(mode=="course"){u.course="magazine";u.trainingTurns=2;}
            var legacy=Clone(c);legacy.buildingProgressionVersion=0;Check(c.Adopt(legacy,out var why),mode+" old paid order migrates: "+why);a=c.Army(a.id);u=a.units[0];
            Check(u.legacyFacilityOrder&&c.Save.buildingProgressionVersion==1&&c.FacilityLevel(a.regionId,"Workshop")==2,mode+" migration preserves old levels and marks only pending contract");Restore(c,mode+" migration idempotence");a=c.Army(a.id);u=a.units[0];
            string method=mode=="item"?"AdvanceItems":mode=="quality"?"AdvanceEquipment":"AdvanceTraining";Call(c,method,a);Call(c,method,a);
            Check(!u.legacyFacilityOrder&&u.kitTurns==0&&u.upgradeTurns==0&&u.trainingTurns==0,mode+" paid legacy completes under original local requirements");
            Check(mode=="item"?u.weapon=="smokeless":mode=="quality"?u.equipment==5:u.weapon=="magazine",mode+" actual old paid benefit delivered");Restore(c);
        }
        var project=Fresh();Put(project,0,"Workshop",2);var b=project.World.State("region-paris").buildings[0];b.upgrading=true;b.turnsRemaining=2;b.workRemaining=1.5f;b.totalWork=2;b.paidCost=600;
        var old=Clone(project);old.buildingProgressionVersion=0;Check(project.Adopt(old,out var why2),"Old partial upgrade loads: "+why2);b=project.World.State("region-paris").buildings[0];Check(b.level==2&&b.upgrading&&b.workRemaining==1.5f&&b.paidCost==600,"Paid partial construction preserved exactly");
        var invalid=Clone(project);invalid.world.regions.Single(r=>r.regionId=="region-paris").buildings[0].level=6;Check(!project.Adopt(invalid,out _),"Unknown tier rejected");
        invalid=Clone(project);invalid.world.regions.Single(r=>r.regionId=="region-paris").buildings[0].workRemaining=float.NaN;Check(!project.Adopt(invalid,out _),"Non-finite work rejected");
        var region=Fresh();var rb=Put(region,0,"Farm",1);rb.turnsRemaining=1;rb.workRemaining=2;rb.totalWork=2;Check(region.World.RestoreState(Clone(region).world),"Legacy regional integer-turn project with stale work remains loadable");
    }
    static void DamageAndRepair()
    {
        for(int level=1;level<=5;level++)
        {
            var c=Fresh();var b=Put(c,0,"Workshop",level);string region="region-paris";int limit=new[]{4,5,5,6,7}[level-1];Check(c.FacilityRaidUses(region,0)==limit,"Tier"+level+" requested durability");
            for(int i=0;i<limit;i++)Check(c.DamageFacility(region,0,1,301,out _),"Per recipient raid consumes use"+level+"/"+i);
            Check(b.kind=="Workshop"&&b.level==level&&b.raidDamage==limit&&b.raidRepairLiability==301*limit&&!CampaignBuildingTiers.IsOperational(b),"Ruin persists same kind/tier/debt"+level);
            Check(c.FacilityLevel(region,"Workshop")==0&&c.MaterialProduction(region)==6&&c.ConstructionCapacity(region)==1,"Ruin disables access/output/work"+level);
            Check(!c.DamageFacility(region,0,1,301,out _)&&!c.CanBuild("france",region,"Workshop",0,out _)&&!c.CanUpgrade("france",region,0,out _)&&!c.CancelConstruction("france",region,0,out _),"Ruin cannot raid, rebuild, upgrade or cancel debt"+level);
            int debt=b.raidRepairLiability;string city=c.World.Catalog.regions.Single(r=>r.id==region).capital.id;c.World.CaptureCity(city,"austria");c.World.CaptureCity(city,"france");Check(b.raidRepairLiability==debt,"Capture never clears liability"+level);Restore(c,"Ruin round trip"+level);b=c.World.State(region).buildings[0];
            int quote=c.RepairCost(region,0),mat=c.RepairMaterials(region,0);Check(quote>=301*limit&&quote>300*limit,"Repair costs more than accrued saved purchases"+level);
            c.World.FactionState("france").treasury=quote-1;Check(!c.RepairFacility("france",region,0,out _)&&b.raidDamage==limit&&b.raidRepairLiability==debt,"Unaffordable repair cannot clear partial debt"+level);
            c.World.FactionState("france").treasury=quote;c.World.State(region).economy.materials=mat;Check(c.RepairFacility("france",region,0,out _)&&c.World.FactionState("france").treasury==0&&c.World.State(region).economy.materials==0&&b.raidDamage==0&&b.raidRepairLiability==0&&c.FacilityLevel(region,"Workshop")==level,"Exact full payment restores facility"+level);Restore(c);
        }
        var partial=Fresh();var damaged=Put(partial,0,"Market",3);Check(partial.DamageFacility("region-paris",0,1,200,out _)&&partial.FacilityLevel("region-paris","Market")==3&&!partial.CanUpgrade("france","region-paris",0,out _),"Partial damage retains output but upgrade requires liability settlement");
        string before=JsonSerializer.Serialize(partial.Save,Json);Check(!partial.DamageFacility("region-paris",0,9,999,out _)&&JsonSerializer.Serialize(partial.Save,Json)==before,"Oversized raid is atomic no-op");
        var bad=Clone(partial);bad.world.regions.Single(r=>r.regionId=="region-paris").buildings[0].raidDamage=9;Check(!partial.Adopt(bad,out _),"Impossible damage rejected");
        bad=Clone(partial);bad.world.regions.Single(r=>r.regionId=="region-paris").buildings[0].raidRepairLiability=0;Check(!partial.Adopt(bad,out _),"Damage with erased debt rejected");
    }
}
