using System;
using System.Linq;
using EmpireBattle;
using EmpireCampaign.Geography;

public static class ProgressionChecks
{
    public static void Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        var p=new UnitProgression();int startingPoints=p.AvailablePoints;
        p.Award(99);check(p.Rank==0&&p.AvailablePoints==startingPoints,"XP below threshold grants no additional rank or stat points");
        p.Award(1);check(p.Rank==1&&p.AvailablePoints==startingPoints+2,"A new level awards two stat points");
        check(p.Train(UnitSkill.Accuracy),"Earned stat point can be spent");
        while(p.Train(UnitSkill.Drill)){}
        check(p.AvailablePoints==0&&!p.Train(UnitSkill.Endurance),"Stat spending cannot overspend points");
        int spent=p.Spent;p.Award(1200);check(p.Rank==5&&p.AvailablePoints==p.StatPointsEarned-spent,"Multiple levels preserve spent points and award the full new allowance");
        var capped=new UnitProgression();capped.Award(UnitProgression.MaxXP);
        while(capped.Level(UnitSkill.Accuracy)<UnitProgression.MaxSkill)check(capped.Train(UnitSkill.Accuracy),"Stat can advance within its cap");
        check(!capped.Train(UnitSkill.Accuracy),"Stat cannot exceed its cap");
        p.Reinforce(50,450);check(p.xp==130&&p.Rank==1&&p.earnedRanks==5&&p.Valid,"Replacements dilute XP while preserving institutional skills");
        int points=p.AvailablePoints;p.Award(370);check(p.AvailablePoints==points,"Regaining an earlier level cannot create more stat points");
        p.Award(int.MaxValue);check(p.xp==UnitProgression.MaxXP&&p.Valid,"XP overflow is capped safely");
        check(!new UnitProgression {xp=-1}.Valid&&!new UnitProgression {accuracy=startingPoints+1}.Valid,"Corrupt XP and unearned stat points rejected");
        check(UnitProgression.BattleXP(500,500,0,0,0,true)==0,"Idle victorious unit earns no participation XP");
        check(UnitProgression.BattleXP(500,0,500,500,500,true)==0,"Destroyed unit has no surviving veterans");
        check(UnitProgression.BattleXP(500,400,100,150,200,true)>UnitProgression.BattleXP(500,400,0,0,0,false),"Effective combat earns more than surviving losses alone");
        check(UnitProgression.BattleXP(1,1,int.MaxValue,int.MaxValue,1,true)==UnitProgression.MaxXP,"Extreme combat credit cannot overflow or exceed lifetime XP limit");
        check(UnitEquipment.Items.Length>=40&&UnitEquipment.Items.Select(i=>i.Id).Distinct().Count()==UnitEquipment.Items.Length,"Large catalogue has unique item IDs");
        foreach(var item in UnitEquipment.Items)
        {
            check(item.Cost>0&&item.Materials>0&&item.Users!=0&&!string.IsNullOrEmpty(UnitEquipment.StatText(item)),"Item has users, cost and explicit stats: "+item.Id);
            if(item.Weapon!="")check(MilitaryEquipment.Weapon(item.Weapon)!=null&&item.EarliestYear==MilitaryEquipment.Weapon(item.Weapon).EarliestYear,"Weapon item matches supported historical profile: "+item.Id);
        }
        var kit=new UnitLoadout();check(kit.Equip("extra-cartridges",EquipmentUsers.Line)&&kit.Modifiers.ammunition==15,"Extra cartridges add reserve ammunition");
        check(kit.Equip("waxed-cartridges",EquipmentUsers.Line)&&kit.items.Count==1&&kit.Modifiers.ammunition==0,"Replacing a slot removes old bonuses");
        check(!kit.Equip("reserve-caissons",EquipmentUsers.Line)&&!kit.Equip("smokeless-rifles",EquipmentUsers.Line,1800),"Wrong unit type and future kit rejected");
        kit.items.Add("extra-cartridges");check(!kit.Valid(EquipmentUsers.Line),"Two items in one equipment slot rejected");
        kit=new UnitLoadout();kit.Equip("imperial-standard",EquipmentUsers.Line);check(kit.Modifiers.resolve==.60f&&kit.Modifiers.moraleReserve==25,"Imperial Standard grants a major morale boost");
        kit.Equip("summer-uniforms",EquipmentUsers.Line);check(kit.Modifiers.heatFatigue<0&&kit.Modifiers.coldFatigue>0,"Summer uniform has a conditional benefit and cold-weather trade-off");
        var c=fresh();var a=c.Save.armies.First(x=>x.owner=="france");a.regionId="region-paris";var u=a.units[0];
        var home=c.World.State(a.regionId);home.buildings[0]=new RegionBuilding {kind="Barracks",level=2};home.buildings[1]=new RegionBuilding {kind="Arsenal"};
        c.World.FactionState("france").treasury=10000;home.economy.materials=500;string reason;
        c.Progress("france").technologies.Add("drill");
        u.progression.Award(250);
        check(c.TrainSkill("france",a.id,u.id,UnitSkill.Discipline,out reason)&&u.progression.discipline==1,"Veterancy skill order changes persistent regiment");
        check(!c.TrainSkill("austria",a.id,u.id,UnitSkill.Endurance,out reason),"Player cannot train foreign units");
        int treasury=c.World.FactionState("france").treasury;
        check(c.EquipItem("france",a.id,u.id,"extra-cartridges",out reason),"Supplied regiment can order equipment");
        check(u.kitTurns==1&&u.loadout.items.Count==0&&c.World.FactionState("france").treasury==treasury-45,"Kit order pays once and keeps old loadout until delivery");
        check(!c.EquipItem("france",a.id,u.id,"extra-cartridges",out reason)&&!c.UpgradeEquipment("france",a.id,u.id,out reason),"Equipment queues cannot overlap");
        check(c.Adopt(clone(c),out reason),"Pending equipment and learned skills survive save validation");
        a=c.Army(a.id);u=a.units[0];int xp=u.progression.xp;
        c.EndTurn(out reason);while(c.Save.pending!=null)c.Resolve(c.Save.pending.id,out reason);
        check(u.kitTurns==0&&u.pendingItem==""&&u.loadout.items.Contains("extra-cartridges")&&u.progression.xp==xp,"Delivery changes kit without manufacturing combat XP");
        check(!c.EquipItem("france",a.id,u.id,"smokeless-rifles",out reason),"1800 campaign cannot issue smokeless rifles");
        check(!c.EquipItem("france",a.id,u.id,"reserve-caissons",out reason),"Infantry campaign rejects battery-only kit");
        treasury=c.World.FactionState("france").treasury;
        check(c.UpgradeEquipment("france",a.id,u.id,out reason)&&u.upgradeTurns==2&&u.equipment==0,"Quality refit has delayed effects");
        check(c.World.FactionState("france").treasury==treasury-60,"Quality refit charges once");
        for(int i=0;i<2;i++){c.EndTurn(out reason);while(c.Save.pending!=null)c.Resolve(c.Save.pending.id,out reason);}
        check(u.equipment==1&&u.upgradeTurns==0&&u.pendingEquipment==0,"Quality refit commits after two supplied turns");
        var bad=clone(c);bad.armies[0].units[0].progression.xp=-1;check(!c.Adopt(bad,out reason),"Save rejects negative unit XP");
        bad=clone(c);bad.armies[0].units[0].progression=new UnitProgression {accuracy=startingPoints+1};check(!c.Adopt(bad,out reason),"Save rejects unearned skill allocation");
        bad=clone(c);bad.armies[0].units[0].loadout.items.Add("invented-item");check(!c.Adopt(bad,out reason),"Save rejects unknown equipment");
        bad=clone(c);bad.armies[0].units[0].loadout.items.Add("extra-cartridges");bad.armies[0].units[0].loadout.items.Add("waxed-cartridges");check(!c.Adopt(bad,out reason),"Save rejects duplicate equipment slots");
        bad=clone(c);bad.armies[0].units[0].pendingItem="";bad.armies[0].units[0].kitTurns=1;check(!c.Adopt(bad,out reason),"Save rejects malformed equipment delivery");
        var batch=fresh();var force=batch.Save.armies.First(a=>a.owner=="france"&&a.units.Count>=2);force.supply=1;
        var targets=force.units.Take(2).ToArray();var boots=UnitEquipment.Find("stout-boots-level-2");
        batch.World.FactionState("france").treasury=1000;batch.World.State(force.regionId).economy.materials=100;
        int total=targets.Sum(t=>batch.ItemCost(t,boots)),materials=targets.Sum(t=>batch.ItemMaterials(t,boots));
        check(batch.EquipItems("france",force.id,targets.Select(t=>t.id).ToArray(),boots.Id,out reason),"Batch gear queues all checked regiments before army commitment");
        check(targets.All(t=>t.pendingItem==boots.Id&&t.kitTurns==1)&&batch.World.FactionState("france").treasury==1000-total&&batch.World.State(force.regionId).economy.materials==100-materials,"Batch charges exact aggregate treasury and materials");
        check(batch.Adopt(clone(batch),out reason),"Tiered batch orders survive save validation");
        batch=fresh();force=batch.Save.armies.First(a=>a.owner=="france"&&a.units.Count>=2);force.supply=1;targets=force.units.Take(2).ToArray();
        batch.World.FactionState("france").treasury=targets.Sum(t=>batch.ItemCost(t,boots))-1;batch.World.State(force.regionId).economy.materials=100;
        int before=batch.World.FactionState("france").treasury;
        check(!batch.EquipItems("france",force.id,targets.Select(t=>t.id).ToArray(),boots.Id,out reason)&&targets.All(t=>t.kitTurns==0)&&batch.World.FactionState("france").treasury==before,"Insufficient batch funds leave every regiment and treasury unchanged");
        var legacy=clone(c);legacy.schemaVersion=2;legacy.armies[0].units[0].experience=4;legacy.armies[0].units[0].progression=null;legacy.armies[0].units[0].loadout=null;
        check(c.Adopt(legacy,out reason)&&c.Save.schemaVersion==5&&c.Save.armies[0].units[0].progression.xp==400,"Legacy battle-count experience migrates to bounded XP");
    }
}
