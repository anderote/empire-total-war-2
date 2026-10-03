using System;
using System.Linq;
using Empire.UI;
using EmpireBattle;
using EmpireGame;
using UnityEngine;

public static class RegimentManagementVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Regiment management: "+message);}
    public static void VerifySizedEquipment()
    {
        var c=new GloryCompany();var r=c.Save.regiments[0];
        r.progression.Award(250);r.progression.Learn(RegimentAbility.SteadyAim);int xp=r.progression.xp;
        for(int n=0;n<3;n++)Require(c.DecreaseSize(r),"infantry can shrink in twenty troop steps");
        Require(GloryCompany.UnitCapacity(r)==60&&r.soldiers==60&&!c.DecreaseSize(r)&&r.progression.xp==xp&&r.progression.Has(RegimentAbility.SteadyAim),"sixty troop minimum preserves progression");
        var gun=UnitEquipment.Find("smokeless-rifles");c.Save.coins=10000;
        Require(c.Save.commanderXP==0&&c.EquipmentCost(r,gun)==120&&c.CanEquip(r,gun,out _),"best rifles available at zero command XP for size scaled price");
        int coins=c.Save.coins;Require(c.Equip(r,gun.Id)&&c.Save.coins==coins-120&&GloryCompany.Valid(c.Save),"small regiment buys and saves advanced rifles");
        foreach(var item in UnitEquipment.Items.Where(i=>i.Supports(GloryCompany.Users(r))&&UnitEquipment.CanReplace(r.loadout,i)))Require(c.CanEquip(r,item,out _),"all compatible gear levels are independent of XP");
        Require(c.QualityCost(r)==30&&c.ImproveQuality(r),"quality price scales to sixty troops");
        Require(c.SizeIssueCost(r)==50,"growth issues twenty recruits the existing rifle and quality grade");
        int growth=c.SizeUpgradeCost(r);coins=c.Save.coins;Require(c.IncreaseSize(r)&&GloryCompany.UnitCapacity(r)==80&&r.soldiers==80&&c.Save.coins==coins-growth&&r.loadout.items.Contains(gun.Id),"expansion pays to outfit additional recruits");
        Require(c.DecreaseSize(r)&&c.Recruit(0),"mixed sized company created");var big=c.Save.regiments[1];
        for(int n=0;n<12;n++)Require(c.IncreaseSize(big),"large regiment grows to maximum");
        Require(c.EquipmentCost(big,gun)==720&&c.QualityCost(big)==180,"360 troop upgrades cost three times baseline");
        r.soldiers=1;Require(c.EquipmentCost(r,gun)==120,"casualties do not discount permanent equipment");r.soldiers=60;
        int saves=0;var source=new GloryRegimentSource(c,()=>{saves++;return true;});var ids=source.Units.Select(u=>u.Id).ToArray();var boots=UnitEquipment.Find("stout-boots-level-2");
        int expected=c.EquipmentCost(r,boots)+c.EquipmentCost(big,boots);var order=source.Equipment(ids,boots);
        Require(order.Enabled&&order.Cost==expected+" coins","batch quote sums actual recipient sizes");
        coins=c.Save.coins;order.Execute();Require(c.Save.coins==coins-expected&&saves==1&&c.Save.regiments.All(u=>u.loadout.items.Contains(boots.Id)),"mixed size batch charges exact total and persists once");
        var tools=UnitEquipment.Find("cleaning-kits-level-2");int full=c.EquipmentCost(r,tools)+c.EquipmentCost(big,tools);c.Save.coins=full-1;
        Require(!source.Equipment(ids,tools).Enabled&&c.Save.regiments.All(u=>!u.loadout.items.Contains(tools.Id)),"mixed size unaffordable batch is atomic");
        c.Save.coins=10000;var deployment=c.Deployment(c.Choices()[0]);Require(deployment.InitialStrengths[0]==60&&deployment.FormationWeapons[0]=="smokeless","small modern rifle unit deploys at exact saved strength");
        Require(c.CompanyOrder(GloryCompanyOrder.Quality,false,out int qualityCost,out _,out _)&&qualityCost==210,"quality all sums scaled prices");
        c.Save.inBattle=true;Require(!c.DecreaseSize(r)&&!c.Equip(big,gun.Id),"downsizing and gear purchase unavailable while deployed");
    }
    public static void VerifyArtilleryAmmunitionUnlocks()
    {
        var p=new UnitProgression();
        Require(p.CanUseArtilleryAmmunition(ArtilleryAmmunition.Roundshot)&&!p.CanUseArtilleryAmmunition(ArtilleryAmmunition.Canister),"fresh battery has roundshot only");
        Require(RegimentAbilities.Find(RegimentAbility.ArtilleryMunitions).Supports(EquipmentUsers.Artillery)&&!RegimentAbilities.Find(RegimentAbility.ArtilleryMunitions).Supports(EquipmentUsers.Cavalry),"ammunition specialization is battery only");
        p.Award(1850);
        for(int rank=1;rank<=4;rank++)
        {
            Require(p.Learn(RegimentAbility.ArtilleryMunitions)&&p.ArtilleryAmmunitionBonus==rank*2,"munition ranks cost points and add ammunition");
            foreach(ArtilleryAmmunition type in Enum.GetValues(typeof(ArtilleryAmmunition)))Require(p.CanUseArtilleryAmmunition(type)==((int)type<=rank),"successive rank unlocks the matching type");
        }
        Require(!p.Learn(RegimentAbility.ArtilleryMunitions)&&p.Valid&&p.Copy().CanUseArtilleryAmmunition(ArtilleryAmmunition.Lime),"spent points and copied unlocks persist");
        Require(!p.CanUseArtilleryAmmunition((ArtilleryAmmunition)99),"unknown ammunition rejected");
        var company=new GloryCompany();company.Save.commanderXP=8000;company.Save.coins=10000;company.Recruit(8);company.Recruit(10);
        var cavalry=company.Save.regiments[1];var battery=company.Save.regiments[2];
        Require(!company.Learn(cavalry,RegimentAbility.ArtilleryMunitions)&&company.Learn(battery,RegimentAbility.ArtilleryMunitions),"company enforces unit compatibility");
        var config=company.Deployment(company.Choices()[0]);Require(config.ArtilleryProgression[0].CanUseArtilleryAmmunition(ArtilleryAmmunition.Canister),"learned ammunition reaches deployed guns");
    }
    public static void VerifyAbilitiesAndSize()
    {
        var p=new UnitProgression();Require(p.Learn(RegimentAbility.GrenadierCadre),"former level nine ability available using a point");
        p.Award(250);Require(p.Learn(RegimentAbility.GrenadierCadre)&&p.AbilityRank(RegimentAbility.GrenadierCadre)==2&&p.Valid,"second rank needs only another ability point");
        Require(!p.Learn(RegimentAbility.SteadyAim),"cannot overspend ability points");
        var passive=new UnitProgression();passive.Award(850);
        Require(passive.Learn(RegimentAbility.IronConstitution)&&passive.Learn(RegimentAbility.AmmunitionReserve)&&passive.Learn(RegimentAbility.RallyingSpirit),"new abilities can be learned");
        Require(passive.MaxHealth==105&&passive.MaxMorale==103&&passive.ArtilleryAmmunitionBonus==2&&passive.AmmunitionBonus(MilitaryEquipment.Weapon("magazine"))==16,"passive health, morale and ammunition effects");
        passive.Award(1000);Require(passive.Learn(RegimentAbility.WeaponMastery)&&passive.MeleeMultiplier>1+passive.Rank*.025f,"melee ability changes damage");
        var c=new GloryCompany();c.Save.commanderXP=8000;c.Save.coins=100000;c.Recruit(8);c.Recruit(10);
        foreach(var r in c.Save.regiments)
        {
            int start=GloryCompany.UnitCapacity(r),count=GloryCompany.MaxSizeUpgrades(r),step=GloryCompany.SizeStep(r);
            for(int n=0;n<count;n++){int cost=c.SizeUpgradeCost(r),coins=c.Save.coins;Require(c.IncreaseSize(r)&&c.Save.coins==coins-cost,"size purchase charges exact cost");}
            Require(GloryCompany.UnitCapacity(r)==start+count*step&&r.soldiers==GloryCompany.UnitCapacity(r)&&!c.IncreaseSize(r),"size cap and immediate recruits");
        }
        Require(c.Save.regiments.Select(GloryCompany.UnitCapacity).SequenceEqual(new[]{500,500,12})&&GloryCompany.Valid(c.Save),"personnel caps reach 500 while artillery remains a twelve-gun battery");
        var emptyBattery=c.Save.regiments[2];emptyBattery.soldiers=0;Require(!c.IncreaseSize(emptyBattery),"empty battery cannot form an invalid partial unit");emptyBattery.soldiers=12;
        var config=c.Deployment(c.Choices()[0]);Require(config.InitialStrengths[0]==500&&config.InitialStrengths[1]==500&&config.FrenchBatterySizes.SequenceEqual(new[]{12})&&config.FrenchArtillery==12,"500-person regiments deploy while artillery keeps its twelve-gun grouping");
        var overCap=JsonUtility.FromJson<GlorySave>(JsonUtility.ToJson(c.Save));overCap.regiments[0].sizeCapacityBonus++;
        Require(!GloryCompany.Valid(overCap),"save validation rejects personnel capacity above 500");
        var poor=new GloryCompany();poor.Save.coins=0;Require(!poor.IncreaseSize(poor.Save.regiments[0])&&poor.Save.regiments[0].sizeUpgrades==0,"unaffordable size purchase is atomic");
    }
    public static void VerifySizeMigration()
    {
        var legacy=JsonUtility.FromJson<GlorySave>("{\"version\":1,\"coins\":240,\"nextId\":2,\"regiments\":[{\"id\":1,\"soldiers\":120,\"tier\":0,\"progression\":{\"xp\":0,\"earnedRanks\":0}}]}");
        Require(new GloryCompany(legacy).Save.version==3,"original company saves migrate without discarding regiments");
        var legacySized=new GloryCompany().Save;legacySized.version=2;legacySized.nextId=4;
        legacySized.regiments[0].sizeUpgrades=12;legacySized.regiments[0].soldiers=360;
        legacySized.regiments.Add(new GloryRegiment{id=2,tier=8,sizeUpgrades=12,soldiers=180});
        legacySized.regiments.Add(new GloryRegiment{id=3,tier=10,sizeUpgrades=8,soldiers=12});
        var migrated=new GloryCompany(legacySized);
        Require(migrated.Save.version==3&&migrated.Save.regiments.Select(GloryCompany.UnitCapacity).SequenceEqual(new[]{360,180,12})&&migrated.Save.regiments[1].sizeUpgrades==6,"schema-2 migration preserves old infantry, rider and gun capacities");
        migrated.Save.coins=100000;var migratedCavalry=migrated.Save.regiments[1];while(migrated.IncreaseSize(migratedCavalry)){}
        Require(GloryCompany.UnitCapacity(migratedCavalry)==500&&migratedCavalry.soldiers==500,"migrated cavalry can continue growing to the new cap");
    }
    public static void Verify()
    {
        var p=new UnitProgression();Require(p.UnitLevel==1&&p.AvailablePoints==2&&p.AvailableSkillPoints==1,"level one has separate stat and ability pools");
        Require(p.Train(UnitSkill.Health)&&p.MaxHealth==105&&p.AvailableSkillPoints==1,"health spends only stat points");
        Require(p.Learn(RegimentAbility.SteadyAim)&&p.AvailablePoints==1&&p.AvailableSkillPoints==0,"ability spends only ability points");
        Require(!p.Learn(RegimentAbility.FieldMedicine)&&!p.Learn(RegimentAbility.SteadyAim),"ability points cannot be overspent");
        p.Award(100);Require(p.UnitLevel==2&&p.AvailableSkillPoints==0&&p.AvailablePoints==3,"even level awards stats only");
        p.Award(150);Require(p.UnitLevel==3&&p.AvailableSkillPoints==1,"odd level awards ability point");
        Require(p.Learn(RegimentAbility.FieldMedicine),"distinct ability spends earned point");
        p.Award(UnitProgression.MaxXP);Require(p.UnitLevel==UnitProgression.MaxLevel&&p.StatPointsEarned==UnitProgression.MaxLevel*2&&p.SkillPointsEarned==(UnitProgression.MaxLevel+1)/2&&p.Valid,"fifty levels, one hundred stat points and twenty-five ability points");
        for(int i=0;i<UnitProgression.MaxSkill;i++)Require(p.Train(UnitSkill.Ammunition),"ammunition stat can specialize to fifty");
        Require(!p.Train(UnitSkill.Ammunition)&&p.AmmunitionBonus(MilitaryEquipment.Weapon("musket"))==UnitProgression.MaxSkill&&p.AmmunitionBonus(MilitaryEquipment.Weapon("magazine"))==UnitProgression.MaxSkill*8,"single shots versus complete reserve magazines, with stat cap");
        for(int i=1;i<RegimentAbilities.MaxRank;i++)Require(p.Learn(RegimentAbility.SteadyAim),"ability ranks can improve at higher levels");
        Require(!p.Learn(RegimentAbility.SteadyAim)&&p.Valid,"ability rank cap and save validation");
        int earned=p.StatPointsEarned;p.Reinforce(10,110);p.Award(1);Require(p.StatPointsEarned==earned,"dilution and re-earning XP cannot farm points");
        var copy=JsonUtility.FromJson<UnitProgression>(JsonUtility.ToJson(p));Require(copy.Valid&&copy.ammunition==UnitProgression.MaxSkill&&copy.AbilityRank(RegimentAbility.SteadyAim)==5,"new progression save round trip");
        var legacy=JsonUtility.FromJson<UnitProgression>("{\"xp\":8000,\"earnedRanks\":5,\"accuracy\":3,\"drill\":2}");Require(legacy.Valid&&legacy.UnitLevel>6&&legacy.AvailablePoints>0,"legacy veteran saves receive level-appropriate point budgets");
        var config=new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=20,FrenchArtillery=4,BritishArtillery=0,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false};
        var world=new BattleWorld(config);var guns=new ArtillerySystem(world);var session=new BattleArmorySession(world,guns,null);var entry=session.Entries[0];
        entry.progression.Award(850);Require(session.Train(entry,UnitSkill.Ammunition),"prebattle ammunition allocation");
        Require(world.Battalions[0].Ammunition==31&&world.Soldiers.RoundsRemaining[0]==31,"musket point reaches every soldier reserve");
        session.Save.year=1918;Require(session.Equip(entry,"magazine-rifles"),"magazine conversion");
        Require(world.Battalions[0].Ammunition==38&&world.Soldiers.RoundsRemaining[0]==38&&world.Soldiers.MagazineRounds[0]==8,"weapon change recalculates reserve without enlarging magazine");
        Require(session.Train(entry,UnitSkill.Health)&&world.Soldiers.Health[0]==105,"health allocation reaches soldier health");
        var source=new BattleRegimentSource(session);var record=source.Units[0];
        Require(RegimentPreview.Stats(record).First(s=>s.Name=="Ammunition").Value==world.Battalions[0].Ammunition,"displayed ammunition matches simulation");
        session.Equip(entry,"summer-uniforms");var winter=UnitEquipment.Find("winter-greatcoats");
        var replacement=RegimentPreview.WithItem(source.Units[0],winter);
        Require(!replacement.items.Contains("summer-uniforms")&&replacement.items.Contains("winter-greatcoats"),"preview replaces slot instead of stacking old and new modifiers");
        Require(entry.loadout.items.Contains("summer-uniforms")&&!entry.loadout.items.Contains("winter-greatcoats"),"hover preview never mutates equipment");
        Require(session.Equip(entry,"sharpened-bayonets")&&session.Equip(entry,"cleaning-kits")&&entry.loadout.items.Contains("sharpened-bayonets"),"sidearm and tools coexist in different slots");
        var preview=RegimentPreview.Stats(source.Units[0],winter);Require(preview.First(s=>s.Name=="March speed").Value<RegimentPreview.Stats(source.Units[0]).First(s=>s.Name=="March speed").Value,"comparison exposes equipment tradeoffs");
        Require(session.Learn(entry,RegimentAbility.SteadyAim),"learn distinct battle ability");world.Battalions[0].Moving=false;
        float stationary=world.Precision(world.Battalions[0]);world.Battalions[0].Moving=true;Require(stationary>world.Precision(world.Battalions[0])*1.14f,"steady aim condition reaches combat precision");
        var company=new GloryCompany();company.Save.coins=1000;company.Save.commanderXP=900;var regiment=company.Save.regiments[0];regiment.progression.Award(850);company.Train(regiment,UnitSkill.Ammunition);company.Train(regiment,UnitSkill.Health);
        Require(company.Equip(regiment,"grenade-pouches")&&company.Equip(regiment,"sharpened-bayonets"),"company equipment uses shared carry slots");
        var deployed=new BattleWorld(company.Deployment(company.Choices()[0]));Require(deployed.Battalions[0].GrenadesRemaining==4&&deployed.Battalions[0].Ammunition==31&&deployed.Soldiers.Health[0]==105,"company health, reserve ammunition and throwables deploy together");
        VerifyCompanyOrders();VerifyKitUpgrades();VerifyGrenades();VerifySizeMigration();VerifyGlorySharedSource();VerifyAbilities();
        RegimentalGloryVerification.Verify();UnitProgressionVerification.Verify();
        Debug.Log("REGIMENT_MANAGEMENT_VERIFIED: 50 levels; separate stat/ability pools; ranked ability gates; per-shot and per-magazine reserves; legacy save budgets; nonmutating replacement preview; real deployment; grenade damage, stock and hold-fire.");
    }
    public static void VerifyKitUpgrades()
    {
        Require(UnitEquipment.Items.Select(i=>i.Id).Distinct().Count()==UnitEquipment.Items.Length,"every kit level has a unique save ID");
        foreach(var item in UnitEquipment.Items.Where(i=>i.Level==1&&i.Slot!=EquipmentSlot.Weapon))
        {
            var loadout=new UnitLoadout();Require(loadout.Equip(item.Id,item.Users),"legacy kit remains valid");
            var next=UnitEquipment.Next(loadout,item.Slot,item.Users);Require(next!=null&&next.Level==2&&next.Family==item.Id,"each kit item has a second level");
            loadout.Equip(next.Id,item.Users);next=UnitEquipment.Next(loadout,item.Slot,item.Users);
            Require(next!=null&&next.Level==3&&next.Cost==item.Cost*3,"each kit item has a third level with explicit cost");
            loadout.Equip(next.Id,item.Users);
            for(int level=4;level<=UnitEquipment.MaxEquipmentLevel;level++){next=UnitEquipment.Next(loadout,item.Slot,item.Users);Require(next!=null&&next.Level==level,"all ten kit levels can be purchased in order");loadout.Equip(next.Id,item.Users);}
            Require(loadout.items.Count==1&&loadout.Valid(item.Users)&&UnitEquipment.Next(loadout,item.Slot,item.Users)==null,"levels replace rather than stack and stop at ten");
            Require(!UnitEquipment.CanReplace(loadout,item),"bulk issue cannot downgrade a higher level of the same item");
        }
        var company=new GloryCompany();company.Save.coins=2000;Require(company.Recruit(0),"second regiment recruited for batch check");
        int saves=0;var source=new GloryRegimentSource(company,()=>{saves++;return true;});var ids=source.Units.Select(u=>u.Id).ToArray();
        var boots=UnitEquipment.Find("stout-boots-level-2");int coins=company.Save.coins;
        var order=source.Equipment(ids,boots);Require(source.MultiSelect&&order.Enabled,"company supports checked batch gear purchases");order.Execute();
        Require(company.Save.coins==coins-boots.Cost*2&&company.Save.regiments.All(r=>r.loadout.items.Contains(boots.Id))&&saves==1,"batch charges full total and persists once");
        Require(!source.Equipment(ids,boots).Enabled,"repeat purchase is disabled");
        var top=UnitEquipment.Find("stout-boots-level-3");company.Equip(company.Save.regiments[0],top.Id);coins=company.Save.coins;
        order=source.Equipment(ids,top);Require(order.Enabled,"mixed levels can catch up");order.Execute();
        Require(company.Save.coins==coins-top.Cost&&company.Save.regiments.All(r=>r.loadout.items.Contains(top.Id)),"higher levels skipped without duplicate charges");
        company.Save.coins=UnitEquipment.Find("cleaning-kits").Cost;
        Require(!source.Equipment(ids,UnitEquipment.Find("cleaning-kits")).Enabled&&company.Save.regiments.All(r=>!r.loadout.items.Contains("cleaning-kits")),"unaffordable batch cannot partially buy");
        company.Save.inBattle=true;Require(!source.Equipment(ids,UnitEquipment.Find("sharpened-bayonets")).Enabled,"deployed company gear stays locked");
    }
    public static void VerifyCompanyOrders()
    {
        var c=new GloryCompany();c.Save.coins=10000;c.Recruit(0);var troops=c.Save.regiments;troops[0].soldiers=50;troops[1].soldiers=100;
        int before=c.Save.coins;Require(c.CompanyOrder(GloryCompanyOrder.Replenish,false,out int cost,out int count,out _)&&cost==180&&count==90&&c.Save.coins==before,"replenish quote is read only and charges exact missing soldiers");
        c.Save.coins=cost-1;Require(!c.CompanyOrder(GloryCompanyOrder.Replenish,true,out _,out _,out _)&&troops[0].soldiers==50&&troops[1].soldiers==100,"unaffordable replenish does not partially spend");
        c.Save.coins=10000;Require(c.CompanyOrder(GloryCompanyOrder.Replenish,true,out cost,out _,out _)&&troops.All(r=>r.soldiers==120)&&c.Save.coins==10000-cost,"replenish all restores every regiment");
        foreach(var r in troops){c.Equip(r,"stout-boots");r.loadout.wear=50;}
        float worn=troops[0].loadout.Modifiers.speed;var copy=troops[0].loadout.Copy();Require(copy.wear==50&&copy.Condition==50,"loadout copies preserve condition");
        before=c.Save.coins;Require(c.CompanyOrder(GloryCompanyOrder.Repair,true,out cost,out _,out _)&&c.Save.coins==before-cost&&troops.All(r=>r.loadout.wear==0)&&troops[0].loadout.Modifiers.speed>worn,"repair all restores actual kit bonuses and charges once");
        Require(!c.CompanyOrder(GloryCompanyOrder.Repair,true,out _,out _,out _),"fully repaired gear cannot be charged again");
        before=c.Save.coins;Require(c.CompanyOrder(GloryCompanyOrder.Equipment,true,out cost,out count,out _)&&count==2&&cost==180&&c.Save.coins==before-cost&&troops.All(r=>r.loadout.items.Contains("stout-boots-level-2")),"upgrade all advances each issued item once");
        c.Save.coins=120;Require(!c.CompanyOrder(GloryCompanyOrder.Equipment,true,out _,out _,out _)&&troops.All(r=>r.loadout.items.Contains("stout-boots-level-2")),"full upgrade cost includes individually unaffordable items");
        Require(c.CompanyOrder(GloryCompanyOrder.Training,true,out cost,out count,out _)&&cost==0&&count==2&&troops.All(r=>r.progression.accuracy==1&&r.progression.AvailableSkillPoints==1),"train all spends one stat point each and preserves ability points");
        c.Save.coins=10000;for(int i=0;i<UnitProgression.MaxQuality;i++)Require(c.CompanyOrder(GloryCompanyOrder.Quality,true,out _,out _,out _),"all quality grades can be purchased");
        Require(!c.CompanyOrder(GloryCompanyOrder.Quality,true,out _,out _,out _)&&GloryCompany.Valid(c.Save),"quality cap remains save valid");
        c.Save.inBattle=true;foreach(GloryCompanyOrder order in Enum.GetValues(typeof(GloryCompanyOrder)))Require(!c.CompanyOrder(order,true,out _,out _,out _),"company commands lock during battles");
        foreach(int tier in Enumerable.Range(0,GloryCompany.TierNames.Length))
        {
            var company=new GloryCompany();company.Save.coins=10000;company.Save.commanderXP=GloryCompany.UnlockXP[tier];Require(company.Recruit(tier),"every infantry tier can be recruited at its advertised unlock");
            var r=company.Save.regiments.Last();Require(GloryCompany.Valid(company.Save)&&r.quality==GloryCompany.InitialQuality(tier),"advanced troop quality and issued kit remain save valid");
            string gun=GloryCompany.TierWeapons[tier];Require(gun==""||r.loadout.items.Contains(gun),"advanced troops receive their advertised guns");
            var deployment=company.Deployment(company.Choices()[0]);Require(gun==""||deployment.FormationWeapons[1]==UnitEquipment.Find(gun).Weapon,"issued guns reach the tactical deployment");
        }
        var p=new UnitProgression();p.Award(UnitProgression.MaxXP);for(int i=0;i<UnitProgression.MaxSkill;i++)Require(p.Train(UnitSkill.Drill),"one hundred training levels reachable");
        for(int i=0;i<RegimentAbilities.MaxRank;i++)Require(p.Learn(RegimentAbility.QuickHands),"ten ability ranks reachable");
        Require(p.UnitLevel==100&&p.Valid&&p.ReloadMultiplier>=.2f&&!p.Train(UnitSkill.Drill)&&!p.Learn(RegimentAbility.QuickHands),"expanded progression caps remain safe");
    }
    static void VerifyGrenades()
    {
        var kit=new UnitLoadout();kit.Equip("grenade-pouches",EquipmentUsers.Line);
        var w=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=20,FrenchArtillery=0,BritishArtillery=0,InitialSeparation=20,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false,FormationLoadouts=new[]{kit,new UnitLoadout()}});
        var a=w.Battalions[0];var b=w.Battalions[1];a.Position=new Vector2(0,-10);b.Position=new Vector2(0,10);
        for(int i=0;i<20;i++){w.Soldiers.Positions[i]=new Vector2(i*.2f,-10);w.Soldiers.Positions[i+20]=new Vector2(i*.2f,10);}
        var grenades=new GrenadeSystem(w);a.FireAtWill=false;grenades.Step(.05f);Require(grenades.Thrown==0&&a.GrenadesRemaining==4,"hold fire prevents grenade use");
        a.FireAtWill=true;grenades.Step(.05f);Require(grenades.Thrown==1&&a.GrenadesRemaining==3,"throw consumes a finite charge");
        for(int i=0;i<40;i++)grenades.Step(.05f);
        Require(grenades.Detonated==1&&b.AliveCount<20&&a.Kills>0&&a.AliveCount==20,"grenade detonates with actual casualties and attribution");
        for(int i=0;i<1200;i++)grenades.Step(.05f);Require(grenades.Thrown<=4&&a.GrenadesRemaining>=0,"grenades never replenish during battle");
    }
    static void VerifyGlorySharedSource()
    {
        var company=new GloryCompany();company.Save.coins=2000;company.Save.commanderXP=6500;var r=company.Save.regiments[0];r.progression.Award(8000);
        int saves=0;var source=new GloryRegimentSource(company,()=>{saves++;return true;});string[] ids={r.id.ToString()};
        var allocate=source.Skill(ids,UnitSkill.Ammunition);Require(allocate.Enabled,"Glory exposes the shared ammunition allocation action");allocate.Execute();
        var learn=source.Ability(ids,RegimentAbilities.Find(RegimentAbility.SteadyAim));Require(learn.Enabled,"Glory exposes the shared ability action");learn.Execute();
        var equip=source.Equipment(ids,UnitEquipment.Find("magazine-rifles"));Require(equip.Enabled,"later guns are available without equipment XP gates");equip.Execute();
        Require(saves==3&&r.progression.ammunition==1&&r.progression.Has(RegimentAbility.SteadyAim)&&r.loadout.items.Contains("magazine-rifles"),"all shared company actions persist through the company adapter");
        var restored=new GloryCompany(JsonUtility.FromJson<GlorySave>(JsonUtility.ToJson(company.Save)));
        var scenario=restored.Choices()[0];var world=new BattleWorld(restored.Deployment(scenario));var deployed=world.Battalions[0];
        Require(deployed.Weapon.Id=="magazine"&&deployed.Ammunition==38&&deployed.Progression.Has(RegimentAbility.SteadyAim),"saved Glory points, skills and gear reach tactical battle");
        restored.Save.inBattle=true;world.ApplyCasualties(0,10);world.Outcome=BattleOutcome.FrenchVictory;Require(restored.Complete(scenario,world),"Glory battle returns with shared progression");
        var returned=restored.Save.regiments[0];int xp=returned.progression.xp;Require(restored.Replenish(returned)&&returned.progression.xp==xp&&returned.progression.ammunition==1&&returned.progression.Has(RegimentAbility.SteadyAim)&&returned.loadout.items.Contains("magazine-rifles"),"paid replacements preserve all three progression layers");
    }
    static void VerifyAbilities()
    {
        var p=new UnitProgression();p.Award(1850);p.Learn(RegimentAbility.LastStand);p.Learn(RegimentAbility.FieldMedicine);p.Learn(RegimentAbility.QuickHands);p.Learn(RegimentAbility.SquareDiscipline);
        var world=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=20,FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false,FormationProgression=new[]{p,new UnitProgression()}});
        var unit=world.Battalions[0];float normal=world.ReloadModifier(unit);unit.Ammunition=1;Require(world.ReloadModifier(unit)<normal*.76f,"low-ammunition ability affects reload");
        unit.Formation=FormationKind.Line;float resolve=world.Resolve(unit);unit.Formation=FormationKind.Square;Require(world.Resolve(unit)>resolve*1.34f,"square discipline has a conditional battlefield effect");
        unit.Morale=0;var artillery=new ArtillerySystem(world);var pressure=typeof(ArtillerySystem).GetMethod("ApplyDamagePressure",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        pressure.Invoke(artillery,new object[]{unit,0,"Verification"});Require(!unit.Routing&&unit.LastStandUsed,"last stand protects against artillery morale collapse");
        world.StepMovement(13);pressure.Invoke(artillery,new object[]{unit,0,"Verification"});Require(unit.Routing,"last stand expires and cannot reactivate");
        var medic=new UnitProgression();medic.Award(250);medic.Learn(RegimentAbility.FieldMedicine);
        var recovery=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=20,FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false,FormationProgression=new[]{medic,new UnitProgression()}});
        foreach(var b in recovery.Battalions)b.FireAtWill=false;
        recovery.Soldiers.Health[0]=50;recovery.StepMovement(21);new CombatSystem(recovery).Step(1);
        Require(recovery.Soldiers.Health[0]==51&&recovery.Battalions[0].AliveCount==20,"field medicine heals surviving wounded without replacements");
    }
    public static void BuildReview()
    {
        RegimentLibraryVerification.Verify();Verify();
        var result=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/MainMenu.unity","Assets/Scenes/LineBattle.unity"},
            locationPathName="Artifacts/RegimentUIReview/Interface Review.app",target=UnityEditor.BuildTarget.StandaloneOSX,options=UnityEditor.BuildOptions.None});
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Interface review build failed.");
    }
}
