using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EmpireBattle;
using EmpireCampaign.Geography;
using Empire.UI;

public static class ProcurementChecks
{
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
    static string root;static int assertions;
    static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root,path)),Json);
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);assertions++;}
    static string Snapshot(CountryCampaign c)=>JsonSerializer.Serialize(c.Save,Json);
    static CountryCampaignSave Clone(CountryCampaign c)=>JsonSerializer.Deserialize<CountryCampaignSave>(Snapshot(c),Json);
    static CountryCampaign Fresh(int year=1886)
    {
        var balance=Read<CampaignBalance>("Assets/Resources/CountryCampaignBalance.json");balance.startYear=year;
        return new CountryCampaign(Read<RegionCatalog>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/campaign.json"),Read<EconomyProfiles>("Assets/Resources/CampaignEconomyProfiles.json"),Read<TheaterData>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/world.json"),balance,"france");
    }
    static CampaignArmy Fixture(CountryCampaign c,int market=5)
    {
        var a=c.Save.armies.First(x=>x.owner=="france");a.regionId="region-paris";a.supply=1;
        var s=c.World.State(a.regionId);for(int i=0;i<s.buildings.Length;i++)s.buildings[i]=new RegionBuilding();
        s.buildings[0]=new RegionBuilding{kind="Barracks",level=3};
        s.buildings[1]=new RegionBuilding{kind="Workshop",level=5};
        s.buildings[2]=new RegionBuilding{kind="Arsenal",level=5};
        s.buildings[3]=new RegionBuilding{kind="Market",level=market};
        s.economy.materials=240;s.owner="austria";c.World.FactionState(a.owner).treasury=100000;
        c.Progress(a.owner).technologies=c.Balance.technologies.Where(t=>t.minYear<=c.Year&&string.IsNullOrEmpty(t.unavailableReason)).Select(t=>t.id).ToList();
        return a;
    }
    static void Tick(CountryCampaign c,CampaignArmy a)=>typeof(CountryCampaign).GetMethod("AdvanceItems",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{a});
    static CampaignArmy Restore(CountryCampaign c,CampaignArmy a){Check(c.Adopt(Clone(c),out var reason),"Procurement save round-trip: "+reason);return c.Army(a.id);}
    static EquipmentItem Boots(int level)=>UnitEquipment.Find(level==1?"stout-boots":"stout-boots-level-"+level);
    public static void Run(string project)
    {
        root=project;QuotesAndAtomicity();Eligibility();DurabilityRepairCycles();SecuredFirearmDelivery();ActualRoundDelivery();ReceiptValidation();
        Console.WriteLine("PASS: "+assertions+" hostile procurement assertions: actual Buy/Raid adapter, batch transactions, tier durability, secured delivery, persistence, capture and repair economics. No Unity/game launch.");
    }
    static void QuotesAndAtomicity()
    {
        var c=Fresh();var a=Fixture(c);var units=a.units.Take(2).ToArray();var ids=units.Select(u=>u.id).ToArray();var item=Boots(4);var state=c.World.State(a.regionId);
        var quote=c.EquipmentQuote(a,units,item);int expected=units.Sum(u=>(int)Math.Ceiling(c.ItemCost(u,item)*1.5));
        Check(quote.Hostile&&quote.Treasury==expected&&quote.Materials==units.Sum(u=>c.ItemMaterials(u,item)),"Hostile quote applies markup to living-soldier quantities and retains local materials");
        int saves=0;var source=new CampaignRegimentSource(c,a,()=>{saves++;return true;});var before=Snapshot(c);var offer=source.Equipment(ids,item);var raid=source.RaidEquipment(ids,item);
        Check(offer.Enabled&&raid.Enabled&&source.HasHostileProcurement&&offer.Description.Contains("50% markup")&&raid.Description.Contains("repair debt"),"Shared UI source exposes both choices and consequences");
        Check(Snapshot(c)==before&&saves==0,"Buy and raid inspection leave purchased state untouched");
        c.World.FactionState(a.owner).treasury=expected-1;before=Snapshot(c);offer.Execute();
        Check(Snapshot(c)==before&&saves==0,"Stale buy quote with insufficient total funds is atomic");
        Check(!source.Equipment(ids,item).Enabled&&source.RaidEquipment(ids,item).Enabled,"Raid remains eligible when money is insufficient for buy");
        c.World.FactionState(a.owner).treasury=expected;state.economy.materials=quote.Materials-1;before=Snapshot(c);raid.Execute();
        Check(Snapshot(c)==before&&saves==0,"Raid with insufficient shared materials damages nothing");
        state.economy.materials=quote.Materials;state.buildings[3].raidDamage=CampaignBuildingTiers.RaidDurability(5)-1;state.buildings[3].raidRepairLiability=1;before=Snapshot(c);raid.Execute();
        Check(Snapshot(c)==before&&saves==0,"Bulk raid cannot overrun the single remaining producer use");
        state.buildings[3].raidDamage=state.buildings[3].raidRepairLiability=0;before=Snapshot(c);
        Check(!c.RaidEquipmentItems(a.owner,a.id,new[]{ids[0],ids[0]},item.Id,out _)&&!c.RaidEquipmentItems(a.owner,a.id,new[]{ids[0],"unknown"},item.Id,out _)&&Snapshot(c)==before,"Duplicate and unknown raid recipients fail without partial effects");
        raid=source.RaidEquipment(ids,item);state.buildings[3].turnsRemaining=1;before=Snapshot(c);raid.Execute();
        Check(Snapshot(c)==before,"Stale raid quote rechecks operational source");state.buildings[3].turnsRemaining=0;
        var rq=c.EquipmentQuote(a,units,item,true);int cash=c.World.FactionState(a.owner).treasury;raid=source.RaidEquipment(ids,item);raid.Execute();
        Check(saves==1&&units.All(u=>u.pendingProcurement&&u.kitTurns==1)&&state.buildings[3].raidDamage==2&&state.buildings[3].raidRepairLiability==rq.RepairLiability,"Single adapter transaction persists both recipient orders and exact facility damage/debt");
        Check(c.World.FactionState(a.owner).treasury==cash&&state.economy.materials==0&&rq.RepairLiability>expected,"Raid consumes finite materials and incurs more repair liability than purchase savings");
        a=Restore(c,a);Tick(c,a);Check(a.units.Take(2).All(u=>u.loadout.items.Contains(item.Id)&&!u.pendingProcurement&&u.procurementRegionId==""),"Bulk secured ordinary gear delivers after restore and clears receipts");
        // An ordinary hostile buy uses the same receipt, without facility damage.
        c=Fresh();a=Fixture(c);var survivor=a.units[0];c.World.State(survivor.homeRegionId).economy.enlisted-=survivor.soldiers-1;survivor.soldiers=1;item=Boots(1);quote=c.EquipmentQuote(a,new[]{survivor},item);state=c.World.State(a.regionId);
        Check(quote.Treasury==2&&quote.Materials==1,"Small surviving units round normal cost and hostile markup up without a zero-price issue");
        c.World.FactionState(a.owner).treasury=quote.Treasury;state.economy.materials=quote.Materials;
        Check(c.EquipItem(a.owner,a.id,survivor.id,item.Id,out _)&&c.World.FactionState(a.owner).treasury==0&&state.economy.materials==0&&state.buildings[3].raidDamage==0&&survivor.pendingProcurement,"Hostile buy charges exact quote once and leaves source intact");
        a=Restore(c,a);Tick(c,a);Check(a.units[0].loadout.items.Contains(item.Id),"Paid hostile equipment delivers");
    }
    static void Eligibility()
    {
        var c=Fresh();var a=Fixture(c);var u=a.units[0];var item=UnitEquipment.Find("magazine-rifles");var state=c.World.State(a.regionId);
        var exact=c.EquipmentQuote(a,new[]{u},item,true);Check(c.EquipmentPurchaseCost(a,u,item)==1650&&exact.RepairLiability==1815,"Full magazine issue has exact1650 hostile price and1815 repair liability without floating-point rounding drift");
        a.supply=.79f;Check(!c.CanRaidEquipmentItems(a.owner,a,new[]{u},item,out _),"Raid requires80percent supply");a.supply=.8f;
        c.Progress(a.owner).technologies.Remove("magazines");Check(!c.CanRaidEquipmentItems(a.owner,a,new[]{u},item,out var why)&&why.Contains("research"),"Raid does not bypass national technology");c.Progress(a.owner).technologies.Add("magazines");
        state.buildings[1].level=3;Check(!c.CanRaidEquipmentItems(a.owner,a,new[]{u},item,out why)&&why.Contains("Workshop Lv 4"),"Raid requires all advanced firearm facilities, not only the source Arsenal");state.buildings[1].level=5;
        state.buildings[0].kind="Empty";state.buildings[0].level=1;Check(!c.CanRaidEquipmentItems(a.owner,a,new[]{u},item,out why)&&why.Contains("Barracks"),"Raid firearm order requires training capacity");state.buildings[0]=new RegionBuilding{kind="Barracks",level=3};
        Check(!c.CanRaidEquipmentItems(a.owner,a,new[]{u},UnitEquipment.Find("reserve-caissons"),out _),"Raid retains unit role restrictions");
        state.buildings[3]=new RegionBuilding();Check(c.CanEquipItem(a.owner,a,u,Boots(1),out _)&&!c.CanRaidEquipmentItems(a.owner,a,new[]{u},Boots(1),out why)&&why.Contains("operational Market"),"Basic purchases need no factory, but raiding needs a real producer");
        state.owner=a.owner;Check(!c.CanRaidEquipmentItems(a.owner,a,new[]{u},item,out _),"Own province cannot be raided");
        state.owner="austria";var treaty=c.Save.treaties.First(t=>(t.a==a.owner&&t.b==state.owner)||(t.b==a.owner&&t.a==state.owner));treaty.war=false;
        Check(!c.CanEquipItem(a.owner,a,u,item,out _)&&!c.CanRaidEquipmentItems(a.owner,a,new[]{u},item,out _),"Neutral or allied province cannot use hostile procurement");
        var early=Fresh(1800);a=Fixture(early);Check(!early.CanRaidEquipmentItems(a.owner,a,new[]{a.units[0]},item,out why)&&why.Contains("1880"),"Raid cannot acquire weapons before their historical year");
    }
    static void DurabilityRepairCycles()
    {
        int[] durability={4,5,5,6,7};
        for(int tier=1;tier<=5;tier++)
        {
            var c=Fresh();var a=Fixture(c,tier);string region=a.regionId;int totalSavings=0,totalRepair=0;
            Check(c.FacilityRaidUses(region,3)==durability[tier-1],"Tier"+tier+" has the agreed4–7raid durability");
            // Two real recipients repeat the complete raid/capture/repair cycle without resetting their purchased kit.
            for(int cycle=0;cycle<2;cycle++)
            {
                var state=c.World.State(region);state.owner="austria";state.economy.materials=240;int cycleSavings=0;
                for(int n=1;n<=durability[tier-1];n++)
                {
                    var u=a.units[cycle];var item=Boots(n);int saved=c.EquipmentPurchaseCost(a,u,item);cycleSavings+=saved;totalSavings+=saved;
                    Check(c.RaidEquipmentItems(a.owner,a.id,new[]{u.id},item.Id,out var reason),"Tier"+tier+" cycle"+cycle+" raid"+n+": "+reason);
                    Check(c.FacilityRaidUses(region,3)==durability[tier-1]-n,"Each recipient consumes exactly one source use");
                    a=Restore(c,a);Tick(c,a);Check(a.units[cycle].loadout.items.Contains(item.Id),"Secured loot delivers even after the final source-destroying raid");
                }
                state=c.World.State(region);var ruin=state.buildings[3];int damage=ruin.raidDamage,debt=ruin.raidRepairLiability;
                Check(ruin.kind=="Market"&&ruin.level==tier&&c.FacilityLevel(region,"Market")==0,"Destroyed facility remains same tier/slot with no equipment unlock");
                Check(!c.CanEquipItem(a.owner,a,a.units[1-cycle],Boots(10),out _),"Destroyed producer cannot supply new advanced gear");
                state.owner=a.owner;Check(c.RepairCost(region,3)>cycleSavings,"Capture preserves repair cost above the full purchase savings");
                Check(!c.CanUpgrade(a.owner,region,3,out _)&&!c.CancelConstruction(a.owner,region,3,out _)&&!c.CanBuild(a.owner,region,"Market",3,out _),"Upgrade, cancel and rebuild cannot clear a ruined facility debt");
                int repair=c.RepairCost(region,3),materials=c.RepairMaterials(region,3);c.World.FactionState(a.owner).treasury=repair-1;state.economy.materials=materials;string before=Snapshot(c);
                Check(!c.RepairFacility(a.owner,region,3,out _)&&Snapshot(c)==before,"Insufficient repair funds preserve all damage, debt and resources");
                c.World.FactionState(a.owner).treasury=repair;state.economy.materials=materials-1;before=Snapshot(c);
                Check(!c.RepairFacility(a.owner,region,3,out _)&&Snapshot(c)==before,"Insufficient repair materials cannot partially clear liability");
                state.economy.materials=materials;Check(c.RepairFacility(a.owner,region,3,out _)&&c.World.FactionState(a.owner).treasury==0&&state.economy.materials==0,"Full repair charges the exact debt and material bill");totalRepair+=repair;
                Check(ruin.raidDamage==0&&ruin.raidRepairLiability==0&&c.FacilityLevel(region,"Market")==tier&&c.FacilityRaidUses(region,3)==durability[tier-1],"Paid repair restores original tier and bounded durability");
                a=Restore(c,a);
            }
            Check(totalRepair>totalSavings,"Two raid/repair cycles cost more treasury than legitimate hostile purchases at tier"+tier);
        }
    }
    static void SecuredFirearmDelivery()
    {
        var c=Fresh();var a=Fixture(c);var u=a.units[0];var item=UnitEquipment.Find("magazine-rifles");var state=c.World.State(a.regionId);
        state.buildings[2].raidDamage=CampaignBuildingTiers.RaidDurability(5)-1;state.buildings[2].raidRepairLiability=1;
        Check(c.RaidEquipmentItems(a.owner,a.id,new[]{u.id},item.Id,out _)&&c.FacilityLevel(a.regionId,"Arsenal")==0&&u.weapon=="musket"&&u.kitTurns==2,"Final Arsenal raid queues firearm fitting while destroying its production unlock");
        Check(!c.Move(a.owner,a.id,u.homeRegionId,out _),"Secured firearm refit locks army movement");
        a=Restore(c,a);u=a.units[0];a.supply=.79f;Tick(c,a);Check(u.kitTurns==2&&c.EquipmentDeliveryStatus(a,u).Contains("80%"),"Secured fitting pauses with an explicit supply reason");a.supply=1;
        state=c.World.State(a.regionId);state.buildings[0].raidDamage=CampaignBuildingTiers.RaidDurability(3);state.buildings[0].raidRepairLiability=100;Tick(c,a);
        Check(u.kitTurns==2&&c.EquipmentDeliveryStatus(a,u).Contains("Barracks"),"Secured firearm still needs operational fitting capacity");
        state.owner=a.owner;Check(c.RepairFacility(a.owner,a.regionId,0,out _),"Captured damaged Barracks can be paid for and restored");
        Tick(c,a);Check(u.kitTurns==1&&u.weapon=="musket","First fitting turn retains purchased-state weapon");a=Restore(c,a);Tick(c,a);u=a.units[0];
        Check(u.weapon=="magazine"&&u.loadout.items.Contains(item.Id)&&!u.pendingProcurement&&c.FacilityLevel(a.regionId,"Arsenal")==0,"Second turn delivers secured firearm after capture, without reconstructing destroyed source");
        c=Fresh();a=Fixture(c);u=a.units[0];Check(c.RaidEquipmentItems(a.owner,a.id,new[]{u.id},item.Id,out _),"Retreat fixture queues secured firearm");
        string origin=u.homeRegionId;a.regionId=origin;a=Restore(c,a);u=a.units[0];Tick(c,a);
        Check(u.kitTurns==0&&!u.pendingProcurement&&u.procurementRegionId==""&&u.weapon=="musket","Forced departure survives save then cancels receipt cleanly");
    }
    static void ActualRoundDelivery()
    {
        var c=Fresh();var a=Fixture(c);var u=a.units[0];var item=UnitEquipment.Find("magazine-rifles");
        var source=new CampaignRegimentSource(c,a,()=>true);Check(source.RaidEquipment(new[]{u.id},item).Description.Contains("paid local army rations"),"Raid quote explains ongoing ration payment and scarcity");
        Check(c.RaidEquipmentItems(a.owner,a.id,new[]{u.id},item.Id,out _),"Actual round fixture secures firearms in enemy province");
        var finish=typeof(CountryCampaign).GetMethod("FinishRound",BindingFlags.Instance|BindingFlags.NonPublic);
        finish.Invoke(c,null);Check(u.kitTurns==1&&a.supply>=.8f&&u.weapon=="musket","Actual enemy-province round buys available local rations and advances fitting once");
        a=Restore(c,a);u=a.units[0];finish.Invoke(c,null);
        Check(u.kitTurns==0&&u.weapon=="magazine"&&!u.pendingProcurement,"Second actual enemy-province round delivers the secured firearm");
        finish.Invoke(c,null);Check(a.supply<.8f,"Temporary fitting contract does not grant permanent enemy supply access after delivery");
    }
    static void ReceiptValidation()
    {
        var c=Fresh();var a=Fixture(c);var u=a.units[0];Check(c.RaidEquipmentItems(a.owner,a.id,new[]{u.id},Boots(1).Id,out _),"Validation fixture secures ordinary kit");
        var bad=Clone(c);bad.armies.Find(x=>x.id==a.id).units[0].procurementRegionId="missing-region";Check(!c.Adopt(bad,out _),"Save rejects nonexistent receipt source region");
        bad=Clone(c);bad.armies.Find(x=>x.id==a.id).units[0].procurementOwnerId=a.owner;Check(!c.Adopt(bad,out _),"Save rejects own-province hostile receipt");
        bad=Clone(c);bad.armies.Find(x=>x.id==a.id).units[0].pendingProcurement=false;Check(!c.Adopt(bad,out _),"Save rejects orphan source metadata");
        bad=Clone(c);bad.armies.Find(x=>x.id==a.id).units[0].legacyFacilityOrder=true;Check(!c.Adopt(bad,out _),"Save rejects contradictory legacy and secured contract flags");
        Tick(c,a);bad=Clone(c);bad.armies.Find(x=>x.id==a.id).units[0].pendingProcurement=true;Check(!c.Adopt(bad,out _),"Completed order cannot retain active secured receipt");
    }
}
