using System;
using System.Linq;
using EmpireBattle;
using EmpireGame;
using UnityEngine;

public static class RegimentalGloryVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Regimental Glory: "+message);}
    static void VerifyEncounters()
    {
        var c=new GloryCompany();var first=c.Choices();
        Require(first[1].roles.Contains(UnitRole.Cavalry)&&first[1].enemyPower>first[0].enemyPower,"opening levy force has a larger power budget");
        Require(first[4].enemyPower>first[0].enemyPower&&first[2].enemyQuality.Min()>first[0].enemyQuality.Max(),"elite challenge combines greater power with better equipment");
        Require(first[2].coins>first[1].coins&&first[1].coins>first[0].coins&&first[1].xp>first[0].xp,"optional harder opposition pays explicit bonus rewards");
        c.Save.regiments[0].soldiers=20;c.Save.coins=9999;c.Recruit(0);
        Require(c.Choices()[1].playerPower==c.ReadyPower&&c.Choices()[1].seed==first[1].seed,"casualties and recruitment preserve seed while maintaining current company power");
        var restored=new GloryCompany(JsonUtility.FromJson<GlorySave>(JsonUtility.ToJson(c.Save)));
        Require(restored.Choices()[2].enemyXP.SequenceEqual(c.Choices()[2].enemyXP),"reload preserves enemy veterancy");
        var names=new System.Collections.Generic.HashSet<string>();var terrains=new System.Collections.Generic.HashSet<int>();
        foreach(int victories in Enumerable.Range(0,64).Concat(new[]{9999}))
        {
            c.Save.victories=victories;var offers=c.Choices();
            Require(offers.Length==GloryEncounters.ChoiceCount,"each stage offers all generated armies");
            Require(offers[1].enemyPower>offers[0].enemyPower,"levy missions retain their larger power budget");
            foreach(var option in offers.Take(GloryEncounters.BalancedChoiceCount))
            {
                names.Add(option.name);if(option.choice==1)terrains.Add(option.terrainStyle);
                Require(option.roles.Length==option.strengths.Length&&option.roles.Length==option.enemyXP.Length&&option.roles.Length==option.enemyQuality.Length,"all deployment arrays align");
                Require(option.roles.Length<=4096&&option.strengths.All(n=>n>0&&n<=120)&&option.coins>0,"late-stage offers remain bounded");
                var deployment=c.Deployment(option);int offset=deployment.FrenchBattalionCount;
                Require(deployment.FormationProgression.Skip(offset).Select(p=>p.xp).SequenceEqual(option.enemyXP)&&deployment.FormationQuality.Skip(offset).SequenceEqual(option.enemyQuality),"advertised enemy training and quality reach deployment");
            }
        }
        Require(names.Count==18&&terrains.Count==6,"eighteen named encounters rotate across six terrains");
        c.Save.victories=17;var horde=c.Choices()[1];var world=new BattleWorld(c.Deployment(horde));
        Require(world.Battalions.Skip(c.Save.regiments.Count).Sum(b=>b.AliveCount)==horde.strengths.Sum(),"full advertised combined force actually deploys");
        var forged=new GloryScenario{stage=18,choice=1,coins=9999999,roles=new[]{UnitRole.Militia},strengths=new[]{1}};
        Require(c.Deployment(forged).BritishBattalionCount==horde.roles.Length,"callers cannot replace the advertised host with a weak army");
        Require(GloryHonours.Victories.SequenceEqual(new[]{1,5,10,25,50})&&GloryHonours.Names.Length==5,"honours track concrete expedition victory milestones");
        Debug.Log("GLORY_ENCOUNTERS_VERIFIED: eighteen deterministic faction encounters; varied objectives and tactics; honest rewards; bounded combined-arms deployment; stable seeds and comparative battle power.");
    }
    public static void VerifyBattleReports()
    {
        var c=new GloryCompany();c.Save.coins=10000;c.Save.commanderXP=4000;c.Recruit(8);c.Recruit(10);
        var infantry=c.Save.regiments[0];var cavalry=c.Save.regiments[1];var battery=c.Save.regiments[2];
        infantry.progression.Learn(RegimentAbility.SteadyAim);cavalry.progression.Learn(RegimentAbility.AssaultTraining);battery.progression.Learn(RegimentAbility.ArtilleryMunitions);
        var report=GloryBattleReport.Capture(c.Save,c.Choices()[0]);
        report.RecordCombat(infantry.id,20,30,80);report.RecordCombat(cavalry.id,10,15,0);report.RecordCombat(battery.id,40,60,12);
        infantry.soldiers=100;cavalry.soldiers=50;infantry.progression.Award(250);cavalry.progression.Award(100);battery.progression.Award(500);
        c.Save.coins+=300;c.Save.commanderXP+=200;report.Finish(c.Save,true,false);c.Save.lastBattle=report;c.Save.summaryPending=true;
        var foot=report.regiments[0];var horse=report.regiments[1];var guns=report.regiments[2];
        Require(report.Valid&&GloryCompany.Valid(c.Save)&&report.CoinsGained==300&&report.CommandXPGained==200&&report.Casualties==30&&report.Kills==70,"persisted report totals reflect actual changes");
        Require(foot.XPGained==250&&foot.LevelsGained==2&&foot.StatPointsGained==4&&foot.AbilityPointsGained==1&&foot.Casualties==20,"multi level infantry gains and casualties are exact");
        Require(horse.LevelsGained==1&&horse.StatPointsGained==2&&horse.AbilityPointsGained==0&&horse.strengthLabel=="riders","cavalry uses its own before and after progression");
        Require(guns.XPGained==500&&guns.LevelsGained==3&&guns.AbilityPointsGained==1&&guns.strengthLabel=="guns"&&guns.kills==40,"battery result tracks experience, gun labels and kills");
        Require(foot.after.Has(RegimentAbility.SteadyAim)&&guns.after.Has(RegimentAbility.ArtilleryMunitions),"report includes learned ability ranks");
        infantry.progression.Award(1000);infantry.progression.Learn(RegimentAbility.SteadyAim);Require(foot.after.xp==250&&foot.after.AbilityRank(RegimentAbility.SteadyAim)==1,"later management cannot mutate historical report snapshots");
        c.Save.summaryPending=false;Require(c.Save.lastBattle==report&&GloryCompany.Valid(c.Save),"dismissing report preserves history");
        var defeat=GloryBattleReport.Capture(c.Save,c.Choices()[0]);cavalry.soldiers=0;defeat.Finish(c.Save,false,true);
        Require(defeat.Valid&&defeat.conceded&&!defeat.won&&defeat.CoinsGained==0&&defeat.CommandXPGained==0&&defeat.regiments[1].XPGained==0,"conceded battle reports losses without invented rewards");
    }
    public static void VerifyBattlePower()
    {
        var maxPersonnel=new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=500,RidersPerSquadron=500,FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false,InitialStrengths=new[]{500,500}};
        var maxWorld=new BattleWorld(maxPersonnel);
        Require(maxWorld.Battalions.Sum(b=>b.AliveCount)==1000,"battle formations accept the 500-person maximum");
        bool rejected=false;try{new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=501,FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false});}catch(ArgumentException){rejected=true;}
        Require(rejected,"battle configuration rejects a 501-person infantry formation");
        rejected=false;try{new BattleWorld(new BattleConfig {FrenchBattalions=0,BritishBattalions=0,FrenchCavalry=1,BritishCavalry=1,RidersPerSquadron=501,FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false});}catch(ArgumentException){rejected=true;}
        Require(rejected,"battle configuration rejects a 501-rider cavalry formation");
        rejected=false;try{new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=500,FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false,InitialStrengths=new[]{501,100}});}catch(ArgumentException){rejected=true;}
        Require(rejected,"explicit campaign formation strengths reject 501 personnel");
        var fresh=new UnitProgression();var kit=new UnitLoadout();
        double baseline=BattlePower.Total(120,UnitRole.Line,1,0,fresh,kit);
        Require(Math.Abs(baseline-120)<.001,"one fresh line musketeer equals one BP");
        Require(BattlePower.Total(0,UnitRole.Line,1,0,fresh,kit)==0,"empty regiments have zero BP");
        Require(BattlePower.Total(60,UnitRole.Line,1,0,fresh,kit)==baseline/2,"casualties reduce starting power proportionally");
        var trained=new UnitProgression();trained.Award(50000);
        foreach(var skill in new[]{UnitSkill.Accuracy,UnitSkill.Drill,UnitSkill.Health,UnitSkill.Discipline})
            for(int i=0;i<10;i++)trained.Train(skill);
        double elite=BattlePower.Total(60,UnitRole.Guard,8,5,trained,kit,"smokeless");
        Require(elite>baseline,"a small elite modern regiment can outweigh many standard troops");
        kit.Equip("regimental-colours",EquipmentUsers.Line);
        double maintained=BattlePower.Total(120,UnitRole.Line,1,0,fresh,kit);kit.wear=100;
        Require(maintained>BattlePower.Total(120,UnitRole.Line,1,0,fresh,kit),"worn equipment reduces useful kit bonuses");
        foreach(int tier in new[]{0,2,7,9})foreach(int strength in new[]{1,120,360})
        {
            var company=new GloryCompany();var unit=company.Save.regiments[0];
            unit.tier=tier;unit.sizeUpgrades=tier>=8?6:12;unit.sizeCapacityBonus=unit.sizeUpgrades*GloryCompany.SizeStep(unit);unit.soldiers=Math.Min(strength,GloryCompany.UnitCapacity(unit));
            unit.quality=5;unit.weapon="smokeless";unit.progression=trained.Copy();
            var offers=company.Choices();
            foreach(var offer in offers.Take(GloryEncounters.BalancedChoiceCount))
            {
                var config=company.Deployment(offer);int offset=config.FrenchBattalionCount;
                Require(config.FormationTiers.Skip(offset).SequenceEqual(offer.enemyTiers)&&config.FormationWeapons.Skip(offset).SequenceEqual(offer.enemyWeapons),"enemy class and actual weapons reach deployment");
                Require(config.FormationProgression.All(p=>p.Valid),"scaled enemy training stays within earned point budgets");
                for(int i=0;i<offer.roles.Length;i++)
                {
                    var users=offer.roles[i]==UnitRole.Cavalry?EquipmentUsers.Cavalry:offer.roles[i]==UnitRole.Swordsman?EquipmentUsers.Melee:offer.roles[i]==UnitRole.Militia?EquipmentUsers.Militia:offer.roles[i]==UnitRole.Rifleman?EquipmentUsers.Rifle:offer.roles[i]==UnitRole.Skirmisher?EquipmentUsers.Light:EquipmentUsers.Line;
                    Require(offer.enemyLoadouts[i].Valid(UnitEquipment.RetainedUsers(users,offer.enemyTiers[i])),"enemy equipment fits its troop class");
                }
                double deployed=config.FormationRoles.Skip(offset).Select((role,i)=>BattlePower.Total(config.InitialStrengths[offset+i],role,config.FormationTiers[offset+i],(int)Math.Round(config.FormationQuality[offset+i]/.04f),config.FormationProgression[offset+i],config.FormationLoadouts[offset+i],config.FormationWeapons[offset+i])).Sum();
                deployed+=BattlePower.Total(config.BritishArtillery,UnitRole.Line,offer.enemyArtilleryTier,offer.enemyArtilleryQuality,offer.enemyArtilleryProgression,offer.enemyArtilleryLoadout,artillery:true);
                Require(Math.Abs(deployed-offer.enemyPower)<.001,"advertised BP equals deployed troops plus artillery");
                double tolerance=offer.roles.Select((role,i)=>BattlePower.PerTroop(role,offer.enemyTiers[i],(int)Math.Round(offer.enemyQuality[i]/.04f),offer.enemyProgression[i],offer.enemyLoadouts[i],offer.enemyWeapons[i])).Max();
                Require(Math.Abs(offer.enemyPower-company.ReadyPower*GloryEncounters.PowerBudgets[offer.choice])<=Math.Max(tolerance*2,company.ReadyPower*.06),"generated BP tracks the chosen difficulty budget: tier "+tier+", troops "+unit.soldiers+", choice "+offer.choice+", actual "+offer.enemyPower+", target "+company.ReadyPower*GloryEncounters.PowerBudgets[offer.choice]+", troop tolerance "+tolerance);
                Require(offer.strengths.SequenceEqual(company.Choices()[offer.choice].strengths),"power matching is deterministic");
            }
            unit.soldiers=Math.Max(0,unit.soldiers-1);
            Require(company.Choices()[0].playerPower==company.ReadyPower,"casualties invalidate cached cards");
            unit.soldiers++;unit.progression.Train(UnitSkill.Endurance);
            Require(company.Choices()[0].playerPower==company.ReadyPower,"training invalidates cached cards");
            // Even a returned card altered by a caller is resolved from canonical save data.
            var altered=company.Choices()[0];int original=altered.strengths.Sum();altered.strengths[0]=9999;
            Require(company.Deployment(altered).InitialStrengths.Skip(company.Save.regiments.Count).Sum()==original,"cached card edits cannot alter the deployed opposition");
        }
    }
    public static void VerifyMassEncounters()
    {
        foreach(int strength in new[]{1,120,3000})
        {
            var c=new GloryCompany();
            if(strength>360)
            {
                c.Save.coins=100000;
                while(c.Save.regiments.Count<GloryCompany.MaxRegiments)c.Recruit(0);
                foreach(var r in c.Save.regiments){r.sizeUpgrades=19;r.sizeCapacityBonus=380;r.soldiers=500;}
            }
            else c.Save.regiments[0].soldiers=strength;
            for(int choice=GloryEncounters.BalancedChoiceCount;choice<GloryEncounters.ChoiceCount;choice++)
            {
                var offer=GloryEncounters.ForCompany(c,choice)[0];
                int odds=choice==GloryEncounters.VariableChoice?GloryEncounters.VariableOdds(1,0):GloryEncounters.Odds(choice);
                Require(offer.headcountOdds==odds&&offer.strengths.Sum()==c.ReadyTroops*odds,"mass hosts retain exact advertised headcount odds: "+odds);
                Require(offer.strengths.Select(n=>n>0&&n<=GloryCompany.MaxPersonnelCapacity).All(ok=>ok),"mass formations respect the 500-person cap");
                var config=c.Deployment(offer);int offset=config.FrenchBattalionCount;
                Require(config.InitialStrengths.Skip(offset).Sum()==c.ReadyTroops*odds&&config.TotalSoldiers==c.ReadyTroops*(odds+1),"the whole advertised mass host reaches deployment metadata");
                Require(config.FieldHalfSize>=4000&&config.InitialSeparation>=1600&&config.InitialSeparation<config.FieldHalfSize,"mass maps provide long approaches and ample army space");
                Require(!config.Map.Settlement&&config.Map.Valid,"large hosts use open ground rather than overcrowded village garrisons");
                double points=config.FormationRoles.Skip(offset).Select((role,i)=>BattlePower.Total(config.InitialStrengths[offset+i],role,config.FormationTiers[offset+i],(int)Math.Round(config.FormationQuality[offset+i]/.04f),config.FormationProgression[offset+i],config.FormationLoadouts[offset+i],config.FormationWeapons[offset+i])).Sum();
                Require(Math.Abs(points-offer.enemyPower)<.001,"mass host BP remains honest");
                // A conservative bound on packed rear ranks, including coastal land loss.
                double width=0,depth=14,maxWidth=0;
                for(int i=0;i<offer.roles.Length;i++)
                {
                    bool mounted=offer.roles[i]==UnitRole.Cavalry,light=UnitRoster.Light(offer.roles[i]);
                    int files=(int)Math.Ceiling(offer.strengths[i]/(mounted?2.0:light?3.0:4.0));
                    double w=Math.Max(2,files*(mounted?1.65:light?2.6:.62))+6;
                    width+=w;maxWidth=Math.Max(maxWidth,w);
                    depth=Math.Max(depth,Math.Ceiling(offer.strengths[i]/(double)files)*(mounted?3.2:light?2.6:.9)+6);
                }
                double usable=(offer.map.Coast?1.7:2)*config.FieldHalfSize-46;
                double rows=Math.Floor((config.FieldHalfSize-26-config.InitialSeparation*.5)/depth);
                Require(rows*(usable-maxWidth)>width,"the mass host fits on the larger battlefield");
            }
        }
        var company=new GloryCompany();var first=company.Choices();var fixedSignatures=first.Take(GloryEncounters.VariableChoice).Select(o=>o.seed+":"+o.strengths.Sum()+":"+o.enemyPower).ToArray();
        int previousSeed=first[GloryEncounters.VariableChoice].seed;
        var ratios=new System.Collections.Generic.HashSet<int>();
        for(int roll=0;roll<12;roll++)
        {
            Require(company.RerollEncounter(),"variable encounter rerolls between battles");
            var offers=company.Choices();var top=offers[GloryEncounters.VariableChoice];
            Require(top.seed!=previousSeed&&top.headcountOdds>=1000&&top.headcountOdds<=2500,"reroll generates a fresh reproducible variable host");
            ratios.Add(top.headcountOdds);previousSeed=top.seed;
            Require(offers.Take(GloryEncounters.VariableChoice).Select(o=>o.seed+":"+o.strengths.Sum()+":"+o.enemyPower).SequenceEqual(fixedSignatures),"reroll preserves every other offer");
            var restored=new GloryCompany(company.Save);
            Require(restored.Choices()[GloryEncounters.VariableChoice].seed==top.seed,"saved reroll counter reproduces the selected army");
        }
        Require(ratios.Count>1,"variable slot produces different headcount odds");
        company.Save.inBattle=true;int old=company.Save.encounterRerolls;
        Require(!company.RerollEncounter()&&company.Save.encounterRerolls==old,"reroll is locked during battle");
    }
    public static void VerifyEncounterOffers()
    {
        foreach(int ready in new[]{1,20,120,720,2160})foreach(int victories in new[]{0,1,2,3,4,5,17,9999})
        {
            var offers=GloryEncounters.ForStage(victories,ready);
            Require(offers.Length==5&&offers.Select(o=>GloryEncounters.Odds(o.choice)).SequenceEqual(new[]{1,5,10,25,100}),"offers retain all five odds choices");
            for(int choice=0;choice<5;choice++)
            {
                var offer=offers[choice];int odds=GloryEncounters.Odds(choice);
                Require(offer.strengths.Sum()==ready*odds,"each advertised headcount ratio is exact");
                Require(offer.roles.Length==offer.strengths.Length&&offer.roles.Length==offer.enemyXP.Length&&offer.roles.Length==offer.enemyQuality.Length,"mixed formation metadata remains aligned");
                Require(offer.roles.Length<=4096&&offer.strengths.Select((n,i)=>n>0&&n<=(offer.roles[i]==UnitRole.Cavalry?60:120)).All(ok=>ok),"all infantry and cavalry formations fit their capacities");
                Require(choice==0||offer.roles.Contains(UnitRole.Cavalry),"outnumbered engagements always include cavalry");
                Require(offer.roles.SkipWhile(r=>r!=UnitRole.Cavalry).All(r=>r==UnitRole.Cavalry),"mounted formations follow infantry for engine allocation");
            }
            Require(offers.Zip(offers.Skip(1),(a,b)=>b.coins>a.coins).All(ok=>ok),"rewards increase with odds");
        }
        var company=new GloryCompany();
        foreach(var offer in company.Choices())
        {
            var config=company.Deployment(offer);
            Require(config.BritishCavalry==offer.roles.Count(r=>r==UnitRole.Cavalry)&&config.BritishInfantryCount==offer.roles.Count(r=>r!=UnitRole.Cavalry),"scenario allocates real mounted squadrons separately");
            Require(config.InitialStrengths.Length==config.FrenchBattalionCount+config.BritishBattalionCount&&config.TotalSoldiers==120+offer.strengths.Sum(),"actual deployment matches advertised totals");
            Require(config.FieldHalfSize>=360,"power-matched encounter has enough deployment space");
        }
    }
    public static void VerifyFieldSizing()
    {
        foreach(int ready in new[]{1,20,120,720,2160})foreach(int victories in Enumerable.Range(0,64))
        foreach(var offer in GloryEncounters.ForStage(victories,ready))
        {
            var half=GloryEncounters.FieldHalfSize(offer.roles,offer.strengths,0,offer.map,offer.choice);
            var compact=GloryEncounters.FieldHalfSize(offer.roles,offer.strengths,0,offer.map,0);
            Require(half>=360&&half<4800,"fields remain bounded even for maximum 100:1 hosts: "+ready+" / "+victories+" / "+offer.choice+" / "+half);
            if(offer.choice==3)Require(half>=1000&&half>=compact*2,"25:1 hosts have at least two kilometres across and double the compact width");
            else if(offer.choice==4)Require(half>=2000&&half>=compact*3,"100:1 hosts have at least four kilometres across and triple the compact width");
            else Require(half==compact,"lower odds retain their compact field sizing");
            if(ready<=120&&offer.choice<3)Require(half<=520,"small companies use fields at most about one kilometre across");
            if(offer.map.Settlement&&offer.map.DefenderSide==ArmySide.British)continue;
            // Conservative capacity bound includes lost space at the end of each row,
            // loose light troops, mounted footprints, coastal land and the approach gap.
            double width=0,maxWidth=0,depth=14;
            for(int i=0;i<offer.roles.Length;i++)
            {
                bool cavalry=offer.roles[i]==UnitRole.Cavalry,light=UnitRoster.Light(offer.roles[i]);
                int files=(int)Math.Ceiling(offer.strengths[i]/(cavalry?2.0:light?3.0:4.0));
                double w=Math.Max(2,files*(cavalry?1.65:light?2.6:.62))+6;
                width+=w;maxWidth=Math.Max(maxWidth,w);
                depth=Math.Max(depth,Math.Ceiling(offer.strengths[i]/(double)files)*(cavalry?3.2:light?2.6:.9)+6);
            }
            double usableWidth=(offer.map.Coast?1.7:2)*half-(offer.map.Coast?26:46);
            double front=offer.map.Settlement?offer.map.SettlementRadius+90:offer.separation*.5;
            double rows=Math.Floor((half-20-front-6)/depth);
            Require(rows*(usableWidth-maxWidth)>width,"full enemy host fits rear ranks with a margin for obstacles: "+ready+" / "+victories+" / "+offer.choice+" / "+half);
        }
    }
    // This contract suite also runs as managed C#, without opening Unity or a game.
    public static void VerifyEncounterVariety()
    {
        var nations=new System.Collections.Generic.HashSet<string>();var names=new System.Collections.Generic.HashSet<string>();
        var biomes=new System.Collections.Generic.HashSet<BattleBiome>();var tactics=new System.Collections.Generic.HashSet<ArmyTactics>();
        var roles=new System.Collections.Generic.HashSet<UnitRole>();bool attack=false,defend=false,guns=false;
        for(int victories=0;victories<64;victories++)
        {
            var c=new GloryCompany();c.Save.victories=victories;c.Save.regiments[0].sizeUpgrades=12;c.Save.regiments[0].sizeCapacityBonus=240;c.Save.regiments[0].soldiers=360;
            var offers=c.Choices();
            Require(offers.Length==GloryEncounters.ChoiceCount,"each stage offers balanced and mass encounters");
            foreach(var offer in offers.Take(GloryEncounters.BalancedChoiceCount))
            {
                nations.Add(offer.enemyFaction);names.Add(offer.name);biomes.Add(offer.map.Biome);tactics.Add(offer.tactics);
                foreach(var role in offer.roles)roles.Add(role);
                Require(offer.map.Valid&&UnitRoster.Factions.Contains(offer.enemyFaction),"every mission has supported art and a valid battlefield");
                Require(!string.IsNullOrWhiteSpace(offer.objective)&&!string.IsNullOrWhiteSpace(offer.brief),"missions advertise their objective and tactical situation");
                var config=c.Deployment(offer);int offset=config.FrenchBattalionCount;
                Require(config.EnemyFaction==offer.enemyFaction&&config.EnemyCountryName==offer.enemyNation&&config.EnemyTactics==offer.tactics,"faction, name and tactical behavior reach the runtime");
                Require(config.FormationRoles.Skip(offset).SequenceEqual(offer.roles)&&config.InitialStrengths.Skip(offset).SequenceEqual(offer.strengths),"advertised composition is exactly the deployed army");
                Require(config.FormationNames.Skip(offset).All(n=>!n.StartsWith("Enemy ")),"enemy formations carry their national roster names");
                Require(config.FormationWeapons.All(w=>MilitaryEquipment.Weapon(w)!=null),"every formation has supported issued equipment");
                Require(config.BritishArtillery==offer.enemyGuns&&config.ArtilleryProgression.Length==offer.enemyGuns/4&&config.ArtilleryLoadouts.Length==config.ArtilleryProgression.Length&&config.ArtilleryEquipment.Length==config.ArtilleryProgression.Length,"advertised enemy guns have aligned battery metadata");
                if(offer.map.Settlement){if(offer.map.DefenderSide==ArmySide.French)defend=true;else attack=true;}
                guns|=offer.enemyGuns>0;
                var repeat=c.Choices()[offer.choice];
                Require(repeat.name==offer.name&&repeat.enemyFaction==offer.enemyFaction&&repeat.seed==offer.seed&&repeat.roles.SequenceEqual(offer.roles)&&repeat.strengths.SequenceEqual(offer.strengths)&&repeat.enemyXP.SequenceEqual(offer.enemyXP),"reopening an offer cannot reroll it");
                var forged=new GloryScenario{stage=offer.stage,choice=offer.choice,enemyFaction="unknown",enemyGuns=99,tactics=ArmyTactics.HoldGround};
                var resolved=c.Deployment(forged);
                Require(resolved.EnemyFaction==config.EnemyFaction&&resolved.BritishArtillery==config.BritishArtillery&&resolved.EnemyTactics==config.EnemyTactics,"altered caller metadata cannot replace a canonical encounter");
            }
        }
        Require(names.Count==18&&nations.SetEquals(new[]{"france","britain","austria","prussia","russia","italy","ming","india"})&&biomes.Count==6&&tactics.Count==5,"rotation uses eighteen missions, eight ready factions, six biomes and five tactical behaviors");
        Require(roles.SetEquals(Enum.GetValues(typeof(UnitRole)).Cast<UnitRole>())&&attack&&defend&&guns,"roster covers every unit role, town attacks, town defenses and enemy artillery");
        var large=new GloryCompany();large.Save.coins=100000;
        while(large.Save.regiments.Count<GloryCompany.MaxRegiments)Require(large.Recruit(0),"large-company setup");
        foreach(var regiment in large.Save.regiments){regiment.sizeUpgrades=19;regiment.sizeCapacityBonus=380;regiment.soldiers=GloryCompany.UnitCapacity(regiment);}
        Require(large.ReadyTroops==3000&&Math.Abs(large.Choices()[4].enemyPower/large.ReadyPower-GloryEncounters.PowerBudgets[4])<.05,"six maximum-size regiments retain the elite challenge power budget");
        large.Save.commanderXP=4000;large.Save.regiments.RemoveAt(5);Require(large.Recruit(10),"mixed battery setup");
        large.Save.regiments[5].progression.Award(850);
        var combined=large.Deployment(large.Choices()[4]);
        Require(combined.FrenchArtillery==4&&combined.BritishArtillery==large.Choices()[4].enemyGuns&&combined.ArtilleryProgression.Length==1+large.Choices()[4].enemyGuns/4&&combined.ArtilleryProgression[0].xp==850,"player batteries precede enemy batteries without losing their saved training");
    }
    public static void VerifyCombinedRecruitment()
    {
        var c=new GloryCompany();c.Save.coins=10000;c.Save.commanderXP=4000;
        Require(c.Recruit(8)&&c.Recruit(0)&&c.Recruit(9)&&c.Recruit(10)&&c.Recruit(11),"cavalry and batteries recruit alongside infantry within the company cap");
        Require(GloryCompany.Valid(c.Save)&&!c.Recruit(0),"mixed company save and roster cap remain valid");
        var cavalry=c.Save.regiments[1];var heavy=c.Save.regiments[3];var battery=c.Save.regiments[4];var veteran=c.Save.regiments[5];
        Require(cavalry.soldiers==60&&GloryCompany.Users(cavalry)==EquipmentUsers.Cavalry&&battery.soldiers==4&&GloryCompany.Users(battery)==EquipmentUsers.Artillery,"mounted and gun capacities and carry slots are correct");
        cavalry.progression.Award(250);battery.progression.Award(850);veteran.quality=5;
        var offer=c.Choices()[1];var config=c.Deployment(offer);
        Require(config.FrenchInfantryCount==2&&config.FrenchCavalry==2&&config.FrenchArtillery==8,"mixed recruitment allocates infantry, mounted squadrons and real guns separately");
        Require(config.FormationNames[0].Contains("Regiment 1")&&config.FormationNames[1].Contains("Regiment 3")&&config.FormationNames[2].Contains("Regiment 2")&&config.FormationNames[3].Contains("Regiment 4"),"formation metadata follows infantry-then-cavalry engine ordering rather than recruitment ordering");
        Require(config.InitialStrengths.Take(4).SequenceEqual(new[]{120,120,60,60})&&config.FormationProgression[2].xp==250,"mounted strength and XP follow the correct formation");
        Require(config.ArtilleryProgression.Length==2&&config.ArtilleryProgression[0].xp==850&&config.ArtilleryLoadouts[0].items.Contains("crew-tools")&&config.ArtilleryEquipment[1]==5,"each battery receives its own saved training, gear and quality");
        Require(c.ReadyTroops==360&&offer.strengths.Sum()==1800,"advertised odds count infantry and riders and exclude gun pieces");
        Require(!c.Train(cavalry,UnitSkill.Accuracy)&&!c.Train(battery,UnitSkill.Health)&&c.Train(cavalry,UnitSkill.Melee)&&c.Train(battery,UnitSkill.Drill),"training actions respect mounted and gun capabilities");
        cavalry.soldiers=45;int coins=c.Save.coins;Require(c.Replenish(cavalry)&&cavalry.soldiers==60&&c.Save.coins==coins-15*GloryCompany.ReplacementCosts[8],"cavalry replenishes riders at its own replacement price");
        battery.soldiers=0;c.Save.coins=99;Require(!c.Replenish(battery)&&battery.soldiers==0&&c.Save.coins==99,"battery replenishment requires the price of at least one gun");
        c.Save.coins=100;Require(c.Replenish(battery)&&battery.soldiers==1&&c.Save.coins==0&&GloryCompany.Valid(c.Save),"one affordable gun can replenish a depleted battery");
        c.Save.coins=10000;Require(c.Replenish(battery)&&battery.soldiers==4&&GloryCompany.Valid(c.Save),"battery replacement restores four gun pieces");
        cavalry.progression.Award(20000);Require(c.Upgrade(cavalry)&&cavalry.tier==9&&!c.Upgrade(cavalry),"mounted promotion stops at heavy cavalry");
        battery.progression.Award(40000);Require(c.Upgrade(battery)&&battery.tier==11&&!c.Upgrade(battery),"battery promotion stops at veteran artillery");
        var fresh=new GloryCompany();Require(!fresh.Recruit(8)&&!fresh.Recruit(10),"advanced branch unlocks cannot be bypassed");
        fresh.Save.commanderXP=900;fresh.Save.coins=5000;Require(fresh.Recruit(10)&&fresh.UnlockedTier==3&&!fresh.Recruit(7),"artillery unlock does not accidentally unlock imperial infantry");
    }

    // Managed regression checks: old defeated saves must remain playable without
    // resetting the roster, granting money, or bringing casualties back for free.
    public static void VerifyDefeatRecovery()
    {
        var save=new GlorySave {defeated=true,coins=51,victories=5,commanderXP=900};
        var r=save.regiments[0];r.soldiers=20;r.name="The Survivors";
        r.progression.Award(850);r.progression.Train(UnitSkill.Drill);r.progression.Learn(RegimentAbility.SteadyAim);
        r.loadout.Equip("regimental-colours",GloryCompany.Users(r));r.loadout.wear=30;
        var c=new GloryCompany(save);var offer=c.Choices()[0];
        Require(c.CanManage&&c.CanDeploy&&r.soldiers==20&&save.coins==51,"legacy defeated saves reopen with exact surviving strength and funds");
        Require(c.Deployment(offer).InitialStrengths[0]==20&&offer.stage==6&&save.victories==5&&save.commanderXP==900,"survivors can retry the same stage without losing command XP or honours");
        Require(c.Replenish(r)&&r.soldiers==45&&save.coins==1&&r.id==1&&r.name=="The Survivors"&&r.progression.xp==850&&r.progression.drill==1&&r.progression.Has(RegimentAbility.SteadyAim)&&r.loadout.items.Contains("regimental-colours")&&r.loadout.wear==30,"paid recovery retains identity, progression and worn kit");
        Require(!c.Replenish(r)&&GloryCompany.Valid(save),"recovery cannot overspend");
        save.inBattle=true;Require(!c.CanManage&&!c.CanDeploy&&!c.Replenish(r),"regrouping cannot run during battle");save.inBattle=false;
        r.soldiers=0;save.coins=0;
        Require(c.CanManage&&!c.CanDeploy&&!c.Replenish(r)&&r.soldiers==0,"a penniless wipeout does not resurrect soldiers or deploy an empty army");
        save.coins=2;Require(c.Replenish(r)&&r.soldiers==1&&c.CanDeploy,"a wiped out regiment can rebuild with affordable paid replacements");
        var restored=new GloryCompany(save);
        Require(restored.CanDeploy&&restored.Save.defeated&&restored.Save.victories==5&&restored.Save.regiments[0].soldiers==1,"reopening a regrouped defeated company remains playable");
    }
    public static void VerifyRecoveryBonus()
    {
        var c=new GloryCompany();c.Save.coins=10000;c.Save.commanderXP=4000;c.Save.victories=5;
        Require(c.Recruit(8)&&c.Recruit(10),"recovery mixed company setup");
        var r=c.Save.regiments[0];r.soldiers=15;r.sizeUpgrades=2;r.progression.Award(850);
        r.progression.Train(UnitSkill.Drill);r.progression.Learn(RegimentAbility.SteadyAim);
        r.loadout.Equip("regimental-colours",GloryCompany.Users(r));r.loadout.wear=30;
        c.Save.regiments[1].soldiers=0;c.Save.regiments[2].soldiers=0;c.Save.coins=1;c.Save.defeated=true;
        var report=GloryBattleReport.Capture(c.Save,c.Choices()[0]);report.Finish(c.Save,false,false);c.Save.lastBattle=report;
        Require(c.TryRecoveryBonus()&&c.Save.coins==251&&c.Save.regiments.All(u=>u.soldiers==GloryCompany.UnitCapacity(u)),"unable to afford any replacements grants full infantry, rider and gun replenishment plus 250 coins");
        Require(r.soldiers==160&&r.progression.xp==850&&r.progression.drill==1&&r.progression.Has(RegimentAbility.SteadyAim)&&r.loadout.items.Contains("regimental-colours")&&r.loadout.wear==30&&c.Save.victories==5&&c.Save.commanderXP==4000,"relief respects expanded capacity and preserves progression, kit and honours");
        Require(report.CoinsGained==0&&report.regiments[0].strengthAfter==15&&report.Valid&&GloryCompany.Valid(c.Save),"relief cannot rewrite historical losses or become a victory bounty");
        c.Save.coins=0;r.soldiers=0;
        Require(!c.TryRecoveryBonus()&&!new GloryCompany(c.Save).TryRecoveryBonus(),"bonus cannot be repeated or reclaimed by reopening the same save");
        c.Save.recoveryBonusClaimed=false;c.Save.inBattle=true;
        Require(!c.TryRecoveryBonus(),"no mid battle relief");c.Save.inBattle=false;
        c.Save.coins=2;Require(!c.TryRecoveryBonus(),"affordable partial replacements do not trigger relief");
        c.Save.coins=0;Require(c.TryRecoveryBonus()&&c.Save.coins==250&&c.CanDeploy,"penniless wipeout can recover once eligibility is renewed after battle");
        Require(!c.TryRecoveryBonus(),"a full company cannot repeatedly receive credits");
    }
    public static void Verify()
    {
        VerifyBattlePower();VerifyRecoveryBonus();VerifyDefeatRecovery();VerifyBattleReports();VerifyCombinedRecruitment();VerifyEncounterOffers();VerifyEncounterVariety();VerifyEncounters();
        var c=new GloryCompany();
        Require(c.Save.regiments.Count==1&&c.Save.regiments[0].soldiers==120,"starts with one full regiment");
        var r=c.Save.regiments[0];r.progression.Award(250);r.progression.Train(UnitSkill.Drill);r.soldiers=20;c.Save.coins=51;
        Require(c.Replenish(r)&&r.soldiers==45&&c.Save.coins==1&&r.progression.xp==250&&r.progression.drill==1,"partial affordable replenishment preserves XP and skills");
        Require(!c.Replenish(r)&&!c.Recruit(0)&&!c.Recruit(-1)&&!c.Recruit(3),"cannot overspend or recruit locked tiers");
        c.Save.coins=5000;
        var choices=c.Choices();Require(choices.Length==5,"five generated engagements");
        foreach(var scenario in choices){var w=new BattleWorld(c.Deployment(scenario));Require(w.Battalions[0].AliveCount==45&&w.Battalions[0].Progression.xp==250,"exact strength and progression deploy");}
        var selected=choices[0];var world=new BattleWorld(c.Deployment(selected));c.Save.inBattle=true;
        Require(!c.Recruit(0)&&!c.Replenish(r)&&!c.Train(r,UnitSkill.Accuracy),"management unavailable during battle");
        Require(!c.Complete(selected,world),"ongoing battle cannot earn rewards");
        world.ApplyCasualties(0,15);world.Outcome=BattleOutcome.FrenchVictory;
        int coins=c.Save.coins;
        Require(c.Complete(selected,world)&&r.soldiers==30&&c.Save.coins==coins+selected.coins&&c.Save.victories==1,"victory commits exact casualties and rewards");
        Require(c.Save.summaryPending&&c.Save.lastBattle.won&&c.Save.lastBattle.regiments[0].strengthBefore==45&&c.Save.lastBattle.regiments[0].strengthAfter==30,"completion opens report with exact battle casualties");
        var committedReport=c.Save.lastBattle;int xp=r.progression.xp;
        Require(!c.Complete(selected,world)&&r.progression.xp==xp&&c.Save.coins==coins+selected.coins,"repeated return cannot duplicate rewards");
        Require(c.Save.lastBattle==committedReport,"repeated return cannot replace the committed report");
        var restored=new GloryCompany(JsonUtility.FromJson<GlorySave>(JsonUtility.ToJson(c.Save)));
        Require(restored.Save.regiments[0].soldiers==30&&restored.Save.regiments[0].progression.xp==xp,"persistent company JSON round trip");
        c.Save.commanderXP=900;
        Require(c.UnlockedTier==3&&c.Upgrade(r)&&r.soldiers==30&&r.progression.drill==1,"tier upgrade preserves strength and training");
        Require(c.Upgrade(r)&&!c.Upgrade(r),"higher upgrade requires regiment rank");
        while(c.Save.regiments.Count<GloryCompany.MaxRegiments)Require(c.Recruit(0),"recruit capacity");
        Require(!c.Recruit(0),"roster capped at six");
        for(int stage=0;stage<12;stage++)
        {
            c.Save.victories=stage;
            foreach(var option in c.Choices())
            {
                var w=new BattleWorld(c.Deployment(option));
                Require(w.Battalions.Length==c.Save.regiments.Count+option.roles.Length,"later stages deploy mixed enemy rosters");
            }
        }
        selected=c.Choices()[1];world=new BattleWorld(c.Deployment(selected));c.Save.inBattle=true;coins=c.Save.coins;
        world.Outcome=BattleOutcome.BritishVictory;
        int victories=c.Save.victories,commandXP=c.Save.commanderXP;
        world.ApplyCasualties(0,10);int survivors=world.Battalions[0].AliveCount;
        Require(c.Complete(selected,world)&&c.Save.defeated&&c.Save.coins==coins&&c.CanManage&&c.CanDeploy&&r.soldiers==survivors,"defeat retains actual survivors and permits regrouping without rewards");
        Require(c.Save.victories==victories&&c.Save.commanderXP==commandXP&&c.Save.lastBattle.Valid&&!c.Save.lastBattle.won&&c.Save.lastBattle.CoinsGained==0,"defeat preserves honours and records casualties without a bounty");
        restored=new GloryCompany(JsonUtility.FromJson<GlorySave>(JsonUtility.ToJson(c.Save)));
        Require(restored.CanDeploy&&restored.Save.regiments[0].soldiers==survivors,"defeated survivors remain deployable after saving and reloading");
        selected=c.Choices()[0];world=new BattleWorld(c.Deployment(selected));c.Save.inBattle=true;world.ApplyCasualties(0,5);
        Require(c.Complete(selected,world,true)&&r.soldiers==survivors-5&&c.CanDeploy&&c.Save.lastBattle.conceded&&c.Save.coins==coins,"retreat preserves exact survivors and permits another battle without rewards");
        Require(!c.Complete(selected,world,true),"retreat results cannot be applied twice");
        selected=c.Choices()[0];world=new BattleWorld(c.Deployment(selected));c.Save.inBattle=true;world.Outcome=BattleOutcome.FrenchVictory;
        Require(c.Complete(selected,world)&&!c.Save.defeated&&c.Save.victories==victories+1&&c.Save.coins==coins+selected.coins,"victory after regrouping clears defeat and advances normally");
        var invalid=JsonUtility.FromJson<GlorySave>(JsonUtility.ToJson(c.Save));invalid.regiments[0].soldiers=121;
        Require(!GloryCompany.Valid(invalid),"malformed strength rejected");
        Debug.Log("REGIMENTAL_GLORY_VERIFIED: five scenarios, real terrain deployment, persisted casualties, affordable paid replacements, retained XP and skills, locked tiers, upgrades, roster cap, exactly-once rewards and defeat.");
    }
}
