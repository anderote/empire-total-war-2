using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EmpireBattle;
using EmpireCampaign.Geography;
using Empire.UI;

public static class EquipmentAccessChecks
{
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
    static string root;static int assertions;
    static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root,path)),Json);
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);assertions++;}
    static CountryCampaign Fresh(int year=1886)
    {
        var balance=Read<CampaignBalance>("Assets/Resources/CountryCampaignBalance.json");balance.startYear=year;
        return new CountryCampaign(Read<RegionCatalog>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/campaign.json"),Read<EconomyProfiles>("Assets/Resources/CampaignEconomyProfiles.json"),Read<TheaterData>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/world.json"),balance,"france");
    }
    static CountryCampaignSave Clone(CountryCampaign c)=>JsonSerializer.Deserialize<CountryCampaignSave>(JsonSerializer.Serialize(c.Save,Json),Json);
    static void Restore(CountryCampaign c){Check(c.Adopt(Clone(c),out var reason),"Save round-trip: "+reason);}
    static CampaignArmy Fixture(CountryCampaign c,int workshop=0,int arsenal=0,int barracks=2)
    {
        var a=c.Save.armies.First(x=>x.owner=="france");a.regionId="region-paris";a.supply=1;
        var s=c.World.State(a.regionId);foreach(var b in s.buildings){b.kind="Empty";b.level=1;b.turnsRemaining=0;b.upgrading=false;}
        s.buildings[0]=new RegionBuilding{kind="Barracks",level=barracks};
        if(workshop>0)s.buildings[1]=new RegionBuilding{kind="Workshop",level=workshop};
        if(arsenal>0)s.buildings[2]=new RegionBuilding{kind="Arsenal",level=arsenal};
        s.economy.materials=240;c.World.FactionState(a.owner).treasury=100000;
        c.Progress(a.owner).technologies=c.Balance.technologies.Where(t=>t.minYear<=c.Year&&string.IsNullOrEmpty(t.unavailableReason)).Select(t=>t.id).ToList();
        return a;
    }
    static void Tick(CountryCampaign c,CampaignArmy a,string method="AdvanceItems")=>typeof(CountryCampaign).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{a});
    static SavedRegiment Saved(CampaignRegiment u)=>new SavedRegiment{id="saved-test",name="Saved cadre",role=UnitRole.Line,users=EquipmentUsers.Line,sourceCapacity=500,sourceStrength=500,weapon=u.weapon,quality=u.equipment,loadout=u.loadout.Copy(),progression=u.progression.Copy()};
    public static void Run(string project)
    {
        root=project;
        BasicAndTieredKit();Firearms();BatchAndStaleQuotes();LegacyRoutesAndQuality();Restrictions();TurnIntegrationAndTransfer();
        Console.WriteLine("PASS: "+assertions+" bounded campaign equipment assertions. Actual model, shared source adapter, purchase/delivery, serialized save restore and full stat previews; no Unity/game launch.");
    }
    static void BasicAndTieredKit()
    {
        var c=Fresh(1800);var a=Fixture(c);var u=a.units[0];var current=c.World.State(a.regionId);var origin=c.World.State(u.homeRegionId);
        origin.buildings[1]=new RegionBuilding{kind="Market",level=5};origin.buildings[2]=new RegionBuilding{kind="Arsenal",level=2};
        var source=new CampaignRegimentSource(c,a,()=>true);string[] ids={u.id};var basic=UnitEquipment.Find("stout-boots-level-3");
        Check(source.Context.Contains("Current province: Paris")&&source.Units[0].Status.Contains("Recruitment origin:"),"Current province and recruitment origin are explicit");
        Check(source.Equipment(ids,basic).Enabled,"Basic level-three kit is available without a workshop");
        var advanced=source.Equipment(ids,UnitEquipment.Find("stout-boots-level-7"));
        Check(!advanced.Enabled&&advanced.Reason.Contains("Market Lv 3")&&advanced.Reason.Contains("Paris"),"Origin facilities cannot unlock current-province gear");
        Check(advanced.Cost=="315 treasury + 28 materials"&&advanced.Description.Contains("Market Lv 3"),"Locked offers retain exact costs and requirements");
        current.buildings[1]=new RegionBuilding{kind="Market"};
        Check(c.CanEquipItem(a.owner,a,u,UnitEquipment.Find("stout-boots-level-4"),out _),"Market one supports kit level four");
        current.buildings[2]=new RegionBuilding{kind="Market"};
        Check(c.Buildings(a.regionId,"Market")==2&&c.FacilityLevel(a.regionId,"Market")==1&&!c.CanEquipItem(a.owner,a,u,UnitEquipment.Find("stout-boots-level-7"),out _),"Two basic markets cannot impersonate an advanced market");
        current.buildings[1].level=3;current.buildings[1].upgrading=true;current.buildings[1].turnsRemaining=1;
        Check(!c.CanEquipItem(a.owner,a,u,UnitEquipment.Find("stout-boots-level-7"),out _),"An upgrading market provides no advanced access");
        current.buildings[1].upgrading=false;current.buildings[1].turnsRemaining=0;current.buildings[1].level=5;
        var item=UnitEquipment.Find("stout-boots-level-10");int treasury=c.World.FactionState(a.owner).treasury;float materials=current.economy.materials;
        Check(c.EquipItem(a.owner,a.id,u.id,item.Id,out _)&&u.kitTurns==1&&u.loadout.items.Count==0,"Purchase reserves advanced kit without immediate issue");
        Check(c.World.FactionState(a.owner).treasury==treasury-c.ItemCost(u,item)&&current.economy.materials==materials-c.ItemMaterials(u,item),"Exact advanced-kit cost charged once");
        Restore(c);a=c.Army(a.id);u=a.units[0];current=c.World.State(a.regionId);
        current.buildings[1].turnsRemaining=1;Tick(c,a);
        Check(u.kitTurns==1&&c.EquipmentDeliveryStatus(a,u).Contains("Paused:"),"Facility interruption preserves and explains paid pending delivery");
        Restore(c);a=c.Army(a.id);u=a.units[0];current=c.World.State(a.regionId);current.buildings[1].turnsRemaining=0;Tick(c,a);
        Check(u.kitTurns==0&&u.pendingItem==""&&u.loadout.items.Contains(item.Id),"Restored delivery completes after facility returns");
        a.regionId=u.homeRegionId;c.World.State(a.regionId).buildings[1]=new RegionBuilding();c.World.State(a.regionId).buildings[2]=new RegionBuilding();Restore(c);
        Check(c.Army(a.id).units[0].loadout.items.Contains(item.Id),"Issued kit persists when army leaves manufacturing province");
        c=Fresh();a=Fixture(c);u=a.units[0];
        Check(!c.CanEquipItem(a.owner,a,u,UnitEquipment.Find("selected-powder"),out var why)&&why.Contains("Arsenal Lv 1"),"Precision ordnance needs a local arsenal even at level one");
        c.Progress(a.owner).technologies.Remove("industry");c.World.State(a.regionId).buildings[1]=new RegionBuilding{kind="Workshop"};
        Check(!c.CanUpgrade(a.owner,a.regionId,1,out why)&&why.Contains("Workshop standardization"),"Workshop tier two is linked to canonical industry progression");
    }
    static void Firearms()
    {
        string[] ids={"flintlock-rifles","percussion-muskets","percussion-rifles","breechloaders","magazine-rifles","smokeless-rifles"};
        foreach(string id in ids)
        {
            var c=Fresh();var item=UnitEquipment.Find(id);var required=CountryCampaign.ItemFacilities(item);var a=Fixture(c,required.Workshop,required.Arsenal,Math.Max(2,required.RequiredLevel("Barracks")));var u=a.units[0];
            int saves=0;var source=new CampaignRegimentSource(c,a,()=>{saves++;return true;});var before=JsonSerializer.Serialize(c.Save,Json);
            var offer=source.Equipment(new[]{u.id},item);var record=source.Units[0];
            var stats=RegimentPreview.Stats(record);var preview=RegimentPreview.Stats(record,item);
            Check(stats.Length==16&&preview.Length==stats.Length&&stats.All(s=>!string.IsNullOrEmpty(s.Help))&&record.TotalKills==u.progression.totalKills,"All 17 shooting-infantry rows (16 comparisons plus lifetime kills) retained for "+id);
            Check(preview.Single(s=>s.Name=="Effective range").Value==MilitaryEquipment.Weapon(item.Weapon).EffectiveRange,"Actual weapon profile feeds comparison for "+id);
            Check(JsonSerializer.Serialize(c.Save,Json)==before&&saves==0,"Candidate preview is read-only for "+id);
            Check(offer.Enabled,"Correct current-province facilities unlock "+id+": "+offer.Reason);int cash=c.World.FactionState(a.owner).treasury;
            offer.Execute();Check(saves==1&&u.kitTurns==2&&u.weapon=="musket"&&c.World.FactionState(a.owner).treasury==cash-c.ItemCost(u,item),"Adapter queues and persists exact firearm order "+id);
            Check(!c.Move(a.owner,a.id,u.homeRegionId,out _),"Refitting army cannot leave for "+id);
            Restore(c);a=c.Army(a.id);u=a.units[0];Tick(c,a);Check(u.kitTurns==1&&u.weapon=="musket","First delivery turn retains old gun for "+id);Tick(c,a);
            Check(u.weapon==item.Weapon&&u.loadout.items.Contains(id)&&u.kitTurns==0&&u.pendingItem=="","Second delivery turn commits canonical item and weapon for "+id);Restore(c);
            a=c.Army(a.id);u=a.units[0];a.regionId=u.homeRegionId;Restore(c);
            Check(c.Army(a.id).units[0].weapon==item.Weapon,"Issued firearm persists outside production province for "+id);
        }
        var young=Fresh(1800);var army=Fixture(young,4,4);var magazine=UnitEquipment.Find("magazine-rifles");
        Check(!young.CanEquipItem(army.owner,army,army.units[0],magazine,out var why)&&why.Contains("1880"),"Advanced buildings do not bypass historical year");
        var late=Fresh();army=Fixture(late,4,4);late.Progress(army.owner).technologies.Remove("magazines");
        Check(!late.CanEquipItem(army.owner,army,army.units[0],magazine,out why)&&why.Contains("Magazine mechanisms"),"Advanced buildings do not bypass national research");
        late.Progress(army.owner).technologies.Add("magazines");var home=late.World.State(army.regionId);home.buildings[2].level=1;home.buildings[3]=new RegionBuilding{kind="Arsenal"};
        Check(!late.CanEquipItem(army.owner,army,army.units[0],magazine,out why)&&why.Contains("Arsenal Lv 4"),"Two basic arsenals cannot impersonate advanced metallurgy/proofing");
    }
    static void BatchAndStaleQuotes()
    {
        var c=Fresh();var a=Fixture(c,4,4);var units=a.units.Take(2).ToArray();var ids=units.Select(u=>u.id).ToArray();var item=UnitEquipment.Find("magazine-rifles");
        int total=units.Sum(u=>c.ItemCost(u,item)),materials=units.Sum(u=>c.ItemMaterials(u,item));
        var source=new CampaignRegimentSource(c,a,()=>true);var offer=source.Equipment(ids,item);
        c.World.FactionState(a.owner).treasury=total-1;int cash=c.World.FactionState(a.owner).treasury;
        offer.Execute();Check(units.All(u=>u.kitTurns==0)&&c.World.FactionState(a.owner).treasury==cash,"Stale quote rechecks total funds without partial purchase");
        Check(source.Equipment(ids,item).Reason.Contains(total+" treasury"),"Unaffordable batch reports the entire cost");
        c.World.FactionState(a.owner).treasury=total;c.World.State(a.regionId).economy.materials=materials-1;
        Check(!c.EquipItems(a.owner,a.id,ids,item.Id,out _)&&units.All(u=>u.kitTurns==0),"Combined materials cannot be overspent");
        c.World.State(a.regionId).economy.materials=materials;c.World.State(a.regionId).buildings[0].level=1;
        Check(!c.EquipItems(a.owner,a.id,ids,item.Id,out var why)&&why.Contains("2 free Barracks places"),"One training slot cannot accept two firearm recipients");
        c.World.State(a.regionId).buildings[0].level=2;
        Check(!c.EquipItems(a.owner,a.id,new[]{ids[0],"missing-unit"},item.Id,out _)&&!c.EquipItems(a.owner,a.id,new[]{ids[0],ids[0]},item.Id,out _),"Unknown and duplicate batch IDs are rejected atomically");
        offer=source.Equipment(ids,item);c.World.State(a.regionId).buildings[1].level=1;offer.Execute();
        Check(units.All(u=>u.kitTurns==0)&&c.World.FactionState(a.owner).treasury==total,"Stale quote rechecks province facilities");
        c.World.State(a.regionId).buildings[1].level=4;
        Check(c.EquipItems(a.owner,a.id,ids,item.Id,out _)&&units.All(u=>u.kitTurns==2)&&c.World.FactionState(a.owner).treasury==0&&c.World.State(a.regionId).economy.materials==0,"Exact budget and materials buy entire batch once");Restore(c);
    }
    static void LegacyRoutesAndQuality()
    {
        var c=Fresh();var a=Fixture(c,1,1);var u=a.units[0];
        Check(!c.CanAdopt(a.owner,a,u,CountryCampaign.Course("magazine"),out var why)&&why.Contains("Lv 4"),"Legacy weapon service cannot bypass advanced facilities");
        var saved=Saved(u);saved.weapon="magazine";saved.loadout.Equip("magazine-rifles",saved.users);
        Check(!c.CanRecruitSaved(a.owner,a.regionId,saved,out why)&&why.Contains("Lv 4"),"Saved cadre recruitment cannot bypass advanced facilities");
        var state=c.World.State(a.regionId);state.buildings[1].level=state.buildings[2].level=5;
        Check(c.CanRecruitSaved(a.owner,a.regionId,saved,out _),"Saved advanced cadre is purchasable with matching current facilities");
        Check(c.CanAdopt(a.owner,a,u,CountryCampaign.Course("magazine"),out _),"Legacy adoption honors matching facility gate");
        var source=new CampaignRegimentSource(c,a,()=>true);
        Check(source.Services(new[]{u.id}).All(x=>!x.Title.StartsWith("Issue ")),"Visible firearm purchases use Equipment and its full comparison");
        for(int grade=1;grade<=UnitProgression.MaxQuality;grade++)
        {
            Check(c.UpgradeEquipment(a.owner,a.id,u.id,out why),"Quality grade "+grade+" can be ordered: "+why);Restore(c);a=c.Army(a.id);u=a.units[0];
            Tick(c,a,"AdvanceEquipment");Tick(c,a,"AdvanceEquipment");Check(u.equipment==grade&&u.upgradeTurns==0,"Quality grade "+grade+" completes");
        }
        Check(!c.UpgradeEquipment(a.owner,a.id,u.id,out _),"Maximum quality cannot be exceeded");Restore(c);
        c=Fresh();a=Fixture(c,1,1);u=a.units[0];u.equipment=3;
        Check(!c.CanUpgradeEquipment(a.owner,a,u,out why)&&why.Contains("Lv 4"),"Advanced quality cannot bypass higher-tier facilities");
        // A paid legacy course remains loadable; completion still requires the current facilities.
        a.units[0].course="magazine";a.units[0].trainingTurns=2;Restore(c);a=c.Army(a.id);u=a.units[0];Tick(c,a,"AdvanceTraining");
        Check(u.trainingTurns==2&&u.weapon=="musket","Legacy paid conversion pauses without required facilities");
        state=c.World.State(a.regionId);state.buildings[1].level=state.buildings[2].level=4;Tick(c,a,"AdvanceTraining");Tick(c,a,"AdvanceTraining");
        Check(u.weapon=="magazine"&&u.trainingTurns==0,"Legacy conversion resumes with current facilities");Restore(c);
    }
    static void TurnIntegrationAndTransfer()
    {
        var c=Fresh();var a=Fixture(c,4,4);var u=a.units[0];string id=u.id,name=u.name,origin=u.homeRegionId;
        var source=new CampaignRegimentSource(c,a,()=>true);var offer=source.Equipment(new[]{u.id},UnitEquipment.Find("magazine-rifles"));
        Check(offer.Enabled,"Round fixture can quote magazine rifles");offer.Execute();Restore(c);a=c.Army(a.id);
        var finish=typeof(CountryCampaign).GetMethod("FinishRound",BindingFlags.Instance|BindingFlags.NonPublic);
        finish.Invoke(c,null);u=a.units.First(x=>x.id==id);Check(c.Save.world.turn==2&&u.kitTurns==1&&u.weapon=="musket","Actual round integration advances one delivery step");Restore(c);a=c.Army(a.id);
        finish.Invoke(c,null);u=a.units.First(x=>x.id==id);Check(c.Save.world.turn==3&&u.kitTurns==0&&u.weapon=="magazine","Second actual round issues purchased rifle");
        Check(c.Detach(a.owner,a.id,new[]{id},out var detached,out var why),"Issued regiment can detach: "+why);
        var target=c.Army(detached);u=target.units[0];
        Check(u.name==name&&u.homeRegionId==origin&&u.weapon=="magazine"&&u.loadout.items.Contains("magazine-rifles"),"Detachment preserves purchased gear and original identity");
        var destination=c.Owned(a.owner).Where(r=>r.regionId!=target.regionId&&c.FacilityLevel(r.regionId,"Workshop")<2).Select(r=>new{state=r,route=c.Route(a.owner,target.regionId,r.regionId)}).Where(x=>x.route!=null&&x.route.distance<2000).OrderBy(x=>x.route.distance).First().state;
        target.movement=2000;Check(c.Move(target.owner,target.id,destination.regionId,out why),"Issued regiment can move to a province without advanced facilities: "+why);
        source=new CampaignRegimentSource(c,target,()=>true);offer=source.Equipment(new[]{id},UnitEquipment.Find("smokeless-rifles"));
        Check(!offer.Enabled&&offer.Reason.Contains("current province")&&target.units[0].weapon=="magazine","Moved regiment keeps gun but new purchases use destination facilities");Restore(c);
    }
    static void Restrictions()
    {
        var c=Fresh();var a=Fixture(c,4,4);var u=a.units[0];var item=UnitEquipment.Find("stout-boots");
        Check(!c.CanEquipItem("austria",a,u,item,out _),"Foreign actor cannot buy for player army");
        string current=a.regionId;a.regionId="region-cairo";Check(!c.CanEquipItem(a.owner,a,u,item,out _),"Equipment purchases stay inside the active campaign");a.regionId=current;
        a.supply=.79f;Check(!c.CanEquipItem(a.owner,a,u,item,out _),"Supply below 80 percent blocks issue");a.supply=.8f;
        Check(c.CanEquipItem(a.owner,a,u,item,out _),"80 percent supply boundary permits issue");
        a.marchTarget=u.homeRegionId;Check(!c.CanEquipItem(a.owner,a,u,item,out _),"Pending march must be halted before purchasing");a.marchTarget="";
        c.Save.politics.First(p=>p.regionId==a.regionId).rebels=100;Check(!c.CanEquipItem(a.owner,a,u,item,out _),"Province revolt pauses equipment work");c.Save.politics.First(p=>p.regionId==a.regionId).rebels=0;
        Check(!c.CanEquipItem(a.owner,a,u,UnitEquipment.Find("reserve-caissons"),out _),"Role restrictions survive facility integration");
        var state=c.World.State(a.regionId);state.owner="austria";Check(c.CanEquipItem(a.owner,a,u,item,out _),"Enemy current province allows marked-up purchases");var treaty=c.Save.treaties.First(t=>(t.a==a.owner&&t.b=="austria")||(t.b==a.owner&&t.a=="austria"));treaty.war=false;Check(!c.CanEquipItem(a.owner,a,u,item,out _),"Neutral province does not allow hostile procurement");treaty.war=true;state.owner=a.owner;
        Check(c.EquipItem(a.owner,a.id,u.id,item.Id,out _),"Fixture queues purchase before occupation changes");state.owner="austria";Tick(c,a);
        Check(u.kitTurns==0&&u.pendingItem==""&&!u.loadout.items.Contains(item.Id),"Loss of province cancels pending issue without manufacturing gear");
    }
}
