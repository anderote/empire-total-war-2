using System;
using System.Linq;
using EmpireBattle;

namespace EmpireGame
{
    public static class GloryHonours
    {
        public static readonly int[] Victories={1,5,10,25,50};
        public static readonly string[] Names={"First victory","Campaign ribbon","Distinguished service","Order of valour","Star of glory"};
    }
    // Offers are deterministic: reopening the screen cannot reroll the opposition.
    // Seeded templates and weighted roles generate a fresh, reproducible set after a victory.
    public static class GloryEncounters
    {
        sealed class Encounter
        {
            public string Name,Faction,Ground,Plan;
            public BattleBiome Biome;
            public ArmyTactics Tactics;
            public UnitRole[] Foot;
            public int MountedPercent;
            public bool Town,Defend,Guns;
            public Encounter(string name,string faction,BattleBiome biome,string ground,ArmyTactics tactics,int mounted,
                string plan,UnitRole[] foot,bool town=false,bool defend=false,bool guns=false)
            {Name=name;Faction=faction;Biome=biome;Ground=ground;Tactics=tactics;MountedPercent=mounted;Plan=plan;Foot=foot;Town=town;Defend=defend;Guns=guns;}
        }
        static readonly Encounter[][] Encounters={
            new[]{
                new Encounter("The woodland screen","britain",BattleBiome.Woodland,"Wooded lowlands",ArmyTactics.Skirmish,0,
                    "Light troops yield ground and fire at range. Advance together to pin their screen.",new[]{UnitRole.Skirmisher,UnitRole.Line}),
                new Encounter("The winter redoubt","prussia",BattleBiome.Snow,"Snowbound village",ArmyTactics.HoldGround,0,
                    "Riflemen cover a village garrison. Use the buildings to approach the square.",new[]{UnitRole.Rifleman,UnitRole.Line},town:true),
                new Encounter("Raid on the walled village","ming",BattleBiome.Temperate,"Walled river village",ArmyTactics.HoldGround,0,
                    "Musketeers and armored swordsmen defend the gate. Breach it, then take the square.",new[]{UnitRole.Line,UnitRole.Swordsman},town:true),
                new Encounter("The coastal road","italy",BattleBiome.Coastal,"Coastal farmland",ArmyTactics.Balanced,15,
                    "A mixed patrol blocks the road. The shoreline closes one flank; watch the inland riders.",new[]{UnitRole.Line,UnitRole.Skirmisher}),
                new Encounter("The dry ridge","austria",BattleBiome.Arid,"Dry ridgeline",ArmyTactics.HoldGround,10,
                    "Infantry hold their firing line while horsemen counterattack. Concentrate on one wing.",new[]{UnitRole.Line,UnitRole.Skirmisher}),
                new Encounter("The highland advance","france",BattleBiome.Highlands,"Broken highlands",ArmyTactics.Assault,15,
                    "Guards lead an aggressive advance. Use reverse slopes and reserve a close volley.",new[]{UnitRole.Guard,UnitRole.Line})
            },
            new[]{
                new Encounter("Riders of the dry plain","india",BattleBiome.Arid,"Open dry plain",ArmyTactics.Envelop,40,
                    "A broad levy screen covers mounted wings. Keep squares within supporting fire.",new[]{UnitRole.Militia,UnitRole.Militia,UnitRole.Line}),
                new Encounter("The shoreline host","india",BattleBiome.Coastal,"Coastal lowlands",ArmyTactics.Envelop,30,
                    "Levies and mailed swordsmen pin the front while cavalry works inland. Anchor your flank on the sea.",new[]{UnitRole.Militia,UnitRole.Swordsman,UnitRole.Skirmisher}),
                new Encounter("Hold the winter village","russia",BattleBiome.Snow,"Snowbound crossroads",ArmyTactics.Assault,15,
                    "Massed infantry rush your village. Cover the approaches and keep troops in the square.",new[]{UnitRole.Militia,UnitRole.Militia,UnitRole.Line},town:true,defend:true),
                new Encounter("Blades through the woods","ming",BattleBiome.Woodland,"Wooded frontier",ArmyTactics.Assault,15,
                    "Armored swordsmen advance behind light musketeers. Disrupt the melee troops before they close.",new[]{UnitRole.Swordsman,UnitRole.Skirmisher,UnitRole.Militia}),
                new Encounter("Horsemen of the hills","russia",BattleBiome.Highlands,"Rolling hill country",ArmyTactics.Envelop,45,
                    "Mounted wings dominate this host. Tight infantry support matters more than a long firing line.",new[]{UnitRole.Militia,UnitRole.Skirmisher}),
                new Encounter("The frontier stand","austria",BattleBiome.Temperate,"Frontier market town",ArmyTactics.Assault,20,
                    "A levy army advances on your town. Trade space on the outskirts, but protect the square.",new[]{UnitRole.Militia,UnitRole.Line,UnitRole.Militia},town:true,defend:true)
            },
            new[]{
                new Encounter("The imperial offensive","france",BattleBiome.Temperate,"Open farmland",ArmyTactics.Assault,20,
                    "Veteran guards press the centre with cavalry and guns in support. Break one sector before the line closes.",new[]{UnitRole.Guard,UnitRole.Line,UnitRole.Skirmisher},guns:true),
                new Encounter("The mountain host","russia",BattleBiome.Highlands,"Broken mountain foothills",ArmyTactics.Balanced,15,
                    "Deep infantry reserves follow the first line. Avoid a prolonged exchange with their whole army.",new[]{UnitRole.Line,UnitRole.Guard,UnitRole.Line}),
                new Encounter("The coastal expedition","britain",BattleBiome.Coastal,"Coastal gun line",ArmyTactics.HoldGround,15,
                    "Veteran infantry protect field guns near the coast. Use covered approaches and overwhelm a wing.",new[]{UnitRole.Line,UnitRole.Rifleman,UnitRole.Guard},guns:true),
                new Encounter("The iron advance","prussia",BattleBiome.Arid,"Dry open basin",ArmyTactics.Balanced,20,
                    "Rifle screens precede disciplined lines and a reserve. Isolate the screen before the reserves arrive.",new[]{UnitRole.Rifleman,UnitRole.Line,UnitRole.Guard}),
                new Encounter("The forest encirclement","india",BattleBiome.Woodland,"Forested lowlands",ArmyTactics.Envelop,35,
                    "Light musketeers, mailed swordsmen and mounted wings attack from different angles. Keep an escape lane open.",new[]{UnitRole.Skirmisher,UnitRole.Swordsman,UnitRole.Line}),
                new Encounter("The winter banners","ming",BattleBiome.Snow,"Frozen uplands",ArmyTactics.Assault,25,
                    "Veteran musketeers support armored swordsmen and cavalry. Fire into the assault troops before contact.",new[]{UnitRole.Line,UnitRole.Swordsman,UnitRole.Skirmisher})
            }
        };
        public const int RotationLength=6;
        // Six Battle Power challenges are followed by the four original fixed
        // mass-host cards and the original rerollable host card.
        public const int BalancedChoiceCount=5, PowerRatioChoiceCount=6, LegacyMassChoiceStart=6, ChoiceCount=11, VariableChoice=10;
        public static readonly double[] PowerRatios={1,1.5,2,4,5,10};
        // Preserve the previous reader API while editor checks migrate to PowerRatios.
        public static double[] PowerBudgets=>PowerRatios;
        public static readonly string[] PowerRatioLabels={"1:1 Battle Power","1.5:1 Battle Power","2:1 Battle Power","4:1 Battle Power","5:1 Battle Power","10:1 Battle Power"};
        public static double PowerRatio(int choice)=>choice>=0&&choice<PowerRatioChoiceCount?PowerRatios[choice]:throw new ArgumentOutOfRangeException(nameof(choice));
        public static int Odds(int choice)
        {
            if(choice>=0&&choice<5)return new[]{1,5,10,25,100}[choice];
            if(choice==5)return 100;
            if(choice>=LegacyMassChoiceStart&&choice<VariableChoice)return new[]{100,200,250,500}[choice-LegacyMassChoiceStart];
            if(choice==VariableChoice)return 1000;
            throw new ArgumentOutOfRangeException(nameof(choice));
        }
        public static bool Veteran(int choice)=>choice==2||choice==4;
        public static GloryScenario[] ForStage(int victories,int readySoldiers=120)
        {
            int age=Math.Max(0,Math.Min(10000,victories));
            // Six fully enlarged infantry regiments can field 2,160 troops.
            int maxReady=GloryCompany.MaxRegiments*GloryCompany.MaxPersonnelCapacity;
            readySoldiers=Math.Max(1,Math.Min(maxReady,readySoldiers));
            return Enumerable.Range(0,BalancedChoiceCount).Select(choice=>Create(age+1,readySoldiers,choice)).ToArray();
        }
        // These budgets replace raw headcount odds for company play. The legacy
        // ForStage generator remains available for large-army benchmarks.
        static readonly string[] PowerNames={"Matched force","Outnumbered","Grand army","Overwhelming host","Last stand","Tenfold host"};
        public static GloryScenario[] ForCompany(GloryCompany company,int choiceOnly=-1)
        {
            var ready=company.Save.regiments.Where(r=>r.soldiers>0).ToArray();
            var foot=ready.Where(r=>r.tier<GloryCompany.InfantryTierCount).ToArray();
            var peers=foot.Length>0?foot:ready;
            double averageTier=peers.Length==0?1:peers.Sum(r=>(double)(r.tier+1)*r.soldiers)/peers.Sum(r=>r.soldiers);
            int quality=peers.Length==0?0:(int)Math.Round(peers.Sum(r=>(double)r.quality*r.soldiers)/peers.Sum(r=>r.soldiers));
            var representative=peers.OrderByDescending(GloryCompany.Power).FirstOrDefault();
            string weapon=representative?.loadout.items.Select(UnitEquipment.Find).FirstOrDefault(i=>i!=null&&i.Weapon!="")?.Weapon
                ??(string.IsNullOrEmpty(representative?.weapon)?"musket":representative.weapon);
            return Enumerable.Range(choiceOnly>=0?choiceOnly:0,choiceOnly>=0?1:ChoiceCount).Select(choice=>
            {
                if(choice>=PowerRatioChoiceCount)return MassEncounter(company,choice);
                bool host=choice==1||choice==3;
                double scale=host?.55:choice==2||choice==4||choice==5?1.15:1;
                int total=Math.Max(1,company.ReadyTroops);
                GloryScenario offer=null;
                // Regenerate formation counts after estimating the required headcount.
                // All passes use the same seed and profiles, so reopening never rerolls.
                for(int pass=0;pass<3;pass++)
                {
                    offer=Create(company.Save.victories+1,Math.Max(1,company.ReadyTroops),choice,pass==2?Math.Max(1,(int)Math.Ceiling(total*1.25)):total);
                    offer.powerBalanced=true;offer.playerPower=company.ReadyPower;
                    offer.enemyTiers=offer.roles.Select(role=>role==UnitRole.Cavalry?9:role==UnitRole.Militia||role==UnitRole.Swordsman?0:
                        Math.Max(1,Math.Min(8,(int)Math.Round(averageTier*scale)))).ToArray();
                    offer.enemyWeapons=offer.roles.Select(role=>!UnitRoster.Shoots(role)||host?role==UnitRole.Rifleman?"rifle":"musket":weapon).ToArray();
                    offer.enemyQuality=offer.roles.Select(_=>Math.Max(0,Math.Min(UnitProgression.MaxQuality,quality+(host?-1:choice==2||choice==4?1:0)))*.04f).ToArray();
                    offer.enemyProgression=offer.roles.Select(role=>
                    {
                        var p=new UnitProgression();p.Award((int)((representative?.progression.xp??0)*scale));
                        // Spend actual earned points; XP alone does not grant trained stats.
                        var skills=role==UnitRole.Cavalry||role==UnitRole.Swordsman?new[]{UnitSkill.Melee,UnitSkill.Health,UnitSkill.Discipline,UnitSkill.Endurance}:
                            new[]{UnitSkill.Accuracy,UnitSkill.Drill,UnitSkill.Health,UnitSkill.Discipline,UnitSkill.Endurance,UnitSkill.Ammunition};
                        for(int point=0;p.AvailablePoints>0&&point<800;point++)p.Train(skills[point%skills.Length]);
                        var users=role==UnitRole.Cavalry?EquipmentUsers.Cavalry:role==UnitRole.Swordsman?EquipmentUsers.Melee:EquipmentUsers.Line;
                        if(representative!=null)foreach(var ability in representative.progression.abilities??new System.Collections.Generic.List<RegimentAbility>())
                            if(RegimentAbilities.Find(ability).Supports(users))p.Learn(ability);
                        return p;
                    }).ToArray();
                    offer.enemyXP=offer.enemyProgression.Select(p=>p.xp).ToArray();
                    offer.enemyLoadouts=offer.roles.Select((role,i)=>
                    {
                        var kit=new UnitLoadout();
                        var users=role==UnitRole.Cavalry?EquipmentUsers.Cavalry:role==UnitRole.Swordsman?EquipmentUsers.Melee:role==UnitRole.Militia?EquipmentUsers.Militia:role==UnitRole.Rifleman?EquipmentUsers.Rifle:role==UnitRole.Skirmisher?EquipmentUsers.Light:EquipmentUsers.Line;
                        if(!host&&representative!=null)foreach(var id in representative.loadout.items)
                        {var item=UnitEquipment.Find(id);if(item!=null&&item.Supports(users)&&item.Weapon=="")kit.Equip(id,users);}
                        return kit;
                    }).ToArray();
                    offer.enemyArtilleryTier=Veteran(choice)?12:11;
                    offer.enemyArtilleryQuality=quality;
                    offer.enemyArtilleryProgression=new UnitProgression();
                    offer.enemyArtilleryProgression.Award((int)((representative?.progression.xp??0)*scale));
                    while(offer.enemyArtilleryProgression.AvailablePoints>0)
                    {if(!offer.enemyArtilleryProgression.Train(UnitSkill.Accuracy)&&!offer.enemyArtilleryProgression.Train(UnitSkill.Drill))break;}
                    offer.enemyArtilleryLoadout=new UnitLoadout();
                    double guns=BattlePower.Total(offer.enemyGuns,UnitRole.Line,offer.enemyArtilleryTier,quality,offer.enemyArtilleryProgression,offer.enemyArtilleryLoadout,artillery:true);
                    // Tiny companies must not face a battery larger than the entire power budget.
                    if(guns>0)
                    {
                        double perGun=guns/offer.enemyGuns;
                        int affordableBatteries=(int)(offer.playerPower*PowerRatio(choice)*.3/(perGun*ArtillerySystem.CannonsPerUnit));
                        offer.enemyGuns=Math.Min(offer.enemyGuns,affordableBatteries*ArtillerySystem.CannonsPerUnit);
                        guns=offer.enemyGuns*perGun;
                    }
                    offer.enemyPower=guns+offer.roles.Select((role,i)=>BattlePower.Total(offer.strengths[i],role,offer.enemyTiers[i],(int)Math.Round(offer.enemyQuality[i]/.04f),offer.enemyProgression[i],offer.enemyLoadouts[i],offer.enemyWeapons[i])).Sum();
                    double perTroop=(offer.enemyPower-guns)/offer.strengths.Sum();
                    total=Math.Max(1,Math.Min(GloryCompany.MaxRegiments*GloryCompany.MaxPersonnelCapacity*100,(int)Math.Round((offer.playerPower*PowerRatio(choice)-guns)/perTroop)));
                }
                // Round the final force against the actual role mix. Keep spare
                // formation capacity so integer troop counts can reach the budget.
                double target=offer.playerPower*PowerRatio(choice);
                var points=offer.roles.Select((role,i)=>BattlePower.PerTroop(role,offer.enemyTiers[i],(int)Math.Round(offer.enemyQuality[i]/.04f),offer.enemyProgression[i],offer.enemyLoadouts[i],offer.enemyWeapons[i])).ToArray();
                double artilleryPower=offer.enemyPower-offer.strengths.Select((n,i)=>n*points[i]).Sum();
                double factor=Math.Max(0,(target-artilleryPower)/Math.Max(.001,offer.enemyPower-artilleryPower));
                for(int i=0;i<offer.strengths.Length;i++)offer.strengths[i]=Math.Max(1,Math.Min(GloryCompany.MaxPersonnelCapacity,(int)Math.Floor(offer.strengths[i]*factor)));
                offer.enemyPower=artilleryPower+offer.strengths.Select((n,i)=>n*points[i]).Sum();
                for(int step=0;step<10000;step++)
                {
                    double difference=target-offer.enemyPower,best=Math.Abs(difference);int chosen=-1,sign=difference>0?1:-1;
                    for(int i=0;i<points.Length;i++)
                    {
                        int count=offer.strengths[i]+sign;
                        if(count<1||count>GloryCompany.MaxPersonnelCapacity)continue;
                        double error=Math.Abs(difference-sign*points[i]);
                        if(error<best){best=error;chosen=i;}
                    }
                    if(chosen<0)break;
                    offer.strengths[chosen]+=sign;offer.enemyPower+=sign*points[chosen];
                }
                if(offer.enemyGuns==0)offer.brief=offer.brief.Replace("and guns in support","in support").Replace("protect field guns near the coast","hold the line near the coast");
                offer.composition=string.Join(" · ",offer.roles.Distinct().Select(role=>offer.strengths.Where((_,i)=>offer.roles[i]==role).Sum()+" "+UnitRoster.Label(role)))+(offer.enemyGuns>0?" · "+offer.enemyGuns+" guns":"");
                offer.archetype=PowerRatioLabels[choice];
                offer.bonusPercent=new[]{0,20,40,65,100,150}[choice];
                offer.coins=(260+Math.Min(1500,offer.stage*45))*(100+offer.bonusPercent)/100;
                offer.xp=(100+Math.Min(400,(offer.stage-1)*15))*(100+offer.bonusPercent)/100;
                offer.brief+=(host?" A larger force with cheaper training and equipment.":" Enemy class, training and equipment scale with your company.");
                return offer;
            }).ToArray();
        }
        public static int VariableOdds(int stage,int rerolls)=>1000+new Random(73939+stage*97+rerolls*31).Next(7)*250;
        // Scale only the per-unit participation/victory award by the size that
        // actually entered the battle. Kill XP, hit XP, and the scenario bounty
        // retain their existing meanings, so neither kills nor difficulty are
        // multiplied a second time. The reference sizes are current Glory issues.
        public static int BattleXP(int strength,int survivors,int kills,int hits,int shots,bool won,UnitRole role,bool artillery=false)
        {
            if(strength<=0||survivors<=0||(kills<=0&&hits<=0&&shots<=0&&survivors>=strength))return 0;
            int reference=artillery?ArtillerySystem.CannonsPerUnit:role==UnitRole.Cavalry?60:GloryCompany.Capacity;
            long participation=(40L+(won?20:0))*Math.Max(1,strength)/Math.Max(1,reference);
            long killXP=(long)UnitProgression.XPPerKill*Math.Max(0,kills);
            long hitXP=20L*Math.Max(0,hits)/strength;
            return (int)Math.Min(UnitProgression.MaxXP,participation+killXP+hitXP);
        }
        static GloryScenario MassEncounter(GloryCompany company,int choice)
        {
            int stage=company.Save.victories+1;
            int roll=choice==VariableChoice?company.Save.encounterRerolls:0;
            int odds=choice==VariableChoice?VariableOdds(stage,roll):Odds(choice);
            var offer=Create(stage,Math.Max(1,company.ReadyTroops),choice,checked(Math.Max(1,company.ReadyTroops)*odds),roll*1009,true);
            offer.playerPower=company.ReadyPower;
            // These battles take place on open approaches, rather than trying to
            // squeeze hundreds of thousands of defenders into a village square.
            offer.map.Settlement=false;offer.map.FortLevel=0;
            offer.objective="Rout the enemy army";
            offer.brief="A vast host advances across the open approaches. Use the long battlefield to maneuver, preserve ammunition and keep an escape route.";
            offer.separation=(float)(1600+Math.Sqrt(odds)*100);
            offer.enemyTiers=offer.roles.Select(_=>0).ToArray();
            offer.enemyWeapons=offer.roles.Select(role=>role==UnitRole.Rifleman?"rifle":"musket").ToArray();
            // Mass hosts use inexpensive troops. Their size is an exact headcount
            // challenge and is never reduced to match the company's BP budget.
            offer.enemyProgression=offer.enemyXP.Select(xp=>{var p=new UnitProgression();p.Award(xp);return p;}).ToArray();
            offer.enemyLoadouts=offer.roles.Select(_=>new UnitLoadout()).ToArray();
            offer.enemyArtilleryTier=11;offer.enemyArtilleryQuality=0;
            offer.enemyArtilleryProgression=new UnitProgression();offer.enemyArtilleryLoadout=new UnitLoadout();
            offer.enemyPower=offer.roles.Select((role,i)=>BattlePower.Total(offer.strengths[i],role,0,(int)Math.Round(offer.enemyQuality[i]/.04f),offer.enemyProgression[i],offer.enemyLoadouts[i],offer.enemyWeapons[i])).Sum()
                +BattlePower.Total(offer.enemyGuns,UnitRole.Line,11,0,offer.enemyArtilleryProgression,offer.enemyArtilleryLoadout,artillery:true);
            offer.headcountOdds=odds;
            offer.archetype=odds+":1 · "+(choice==VariableChoice?"Variable host":"Mass host");
            offer.bonusPercent=200+odds/5;
            offer.coins=(260+Math.Min(1500,stage*45))*(100+offer.bonusPercent)/100;
            offer.xp=(100+Math.Min(400,(stage-1)*15))*(100+offer.bonusPercent)/100;
            offer.brief+=" "+odds+" enemies per ready troop; guns are counted separately.";
            return offer;
        }
        static GloryScenario Create(int stage,int readySoldiers,int choice,int totalOverride=0,int seedOffset=0,bool largeFormations=false)
        {
            int age=stage-1,odds=Odds(choice),total=totalOverride>0?totalOverride:readySoldiers*odds;
            int seed=1815+stage*97+choice*31+seedOffset;
            var random=new Random(seed);
            int family=choice==0?0:Veteran(choice)?2:1;
            var encounter=Encounters[family][random.Next(RotationLength)];
            int mountedPercent=encounter.MountedPercent==0?0:Math.Max(5,Math.Min(55,encounter.MountedPercent+random.Next(-5,6)));
            int mounted=mountedPercent==0||total==1?0:Math.Max(1,total*mountedPercent/100);
            int foot=total-mounted;
            // Small patrols still show both specialist roles; large hosts use full regiments.
            int footCapacity=largeFormations?GloryCompany.MaxPersonnelCapacity:120,mountedCapacity=largeFormations?GloryCompany.MaxPersonnelCapacity:60;
            int infantry=Math.Min(foot,Math.Max(Math.Min(encounter.Foot.Length,foot/30),(foot+footCapacity-1)/footCapacity));
            int cavalry=(mounted+mountedCapacity-1)/mountedCapacity,count=infantry+cavalry;
            var roles=Enumerable.Range(0,count).Select(i=>i<infantry?encounter.Foot[i<encounter.Foot.Length?i:random.Next(encounter.Foot.Length)]:UnitRole.Cavalry).ToArray();
            var strengths=Enumerable.Range(0,count).Select(i=>i<infantry?Share(foot,infantry,i):Share(mounted,cavalry,i-infantry)).ToArray();
            int[] xp=roles.Select(role=>Veteran(choice)?Math.Min(1850,250+age*70):role==UnitRole.Militia?Math.Min(250,age*10):Math.Min(850,age*35)).ToArray();
            int bonus=choice<BalancedChoiceCount?new[]{0,100,250,500,1000}[choice]:200+odds/5;
            int guns=encounter.Guns?4*Math.Min(3,Math.Max(1,(readySoldiers+239)/240)):0;
            var map=Map(encounter,stage,choice);
            string objective=!encounter.Town?"Rout the enemy army":encounter.Defend?"Protect the square; rout the attackers":"Capture the square or rout the defenders";
            string capture=!encounter.Town?"":encounter.Defend?" An uncontested enemy occupation of the square for 20 seconds loses the battle.":" Hold the square uncontested for 20 seconds to win.";
            return new GloryScenario {
                choice=choice,stage=stage,seed=seed,terrainStyle=(int)encounter.Biome,bonusPercent=bonus,
                coins=(260+Math.Min(1500,stage*45))*(100+bonus)/100,xp=(100+Math.Min(400,age*15))*(100+bonus)/100,
                name=encounter.Name,terrain=encounter.Ground,brief=encounter.Plan+capture,objective=objective,
                enemyFaction=encounter.Faction,enemyNation=UnitRoster.FactionNames[Array.IndexOf(UnitRoster.Factions,encounter.Faction)],
                enemyColor=FactionColor(encounter.Faction),enemyGuns=guns,tactics=encounter.Tactics,
                separation=encounter.Town?480:encounter.Tactics==ArmyTactics.Skirmish?220:encounter.Tactics==ArmyTactics.Envelop?280:240,
                archetype=odds+":1 · "+(choice==0?"Skirmish":choice==1?"Outnumbered":choice==2?"Grand army":choice==3?"Overwhelming host":"Last stand"),
                composition=string.Join(" · ",roles.Distinct().Select(role=>strengths.Where((_,i)=>roles[i]==role).Sum()+" "+UnitRoster.Label(role)))+(guns>0?" · "+guns+" guns":""),
                roles=roles,strengths=strengths,enemyXP=xp,enemyQuality=roles.Select(_=>Veteran(choice)?.04f:0f).ToArray(),map=map
            };
        }
        // Ordinary companies fight close in. The 25:1 and 100:1 hosts get much
        // wider fields for manoeuvre, while still growing with formation footprints.
        public static float FieldHalfSize(UnitRole[] roles,int[] strengths,int playerCount,BattleMapProfile map,int choice,int oddsOverride=0)
        {
            double required=0;
            for(int side=0;side<2;side++)
            {
                int start=side==0?0:playerCount,end=side==0?playerCount:roles.Length;
                double width=0,depth=0;
                for(int i=start;i<end;i++)
                {
                    bool mounted=roles[i]==UnitRole.Cavalry,light=UnitRoster.Light(roles[i]);
                    // Player cartridge rifles can use two ranks, so reserve their full frontage.
                    int files=(int)Math.Ceiling(strengths[i]/(mounted||side==0?2.0:light?3.0:4.0));
                    double spacing=mounted?1.65:light?2.6:.62;
                    width+=Math.Max(2,files*spacing)+6;
                    depth=Math.Max(depth,Math.Max(14,Math.Ceiling(strengths[i]/(double)files)*(mounted?3.2:light?2.6:.9)+6));
                }
                required=Math.Max(required,width*depth);
            }
            // Allow for rounding, coastal land loss, farms and reserves behind the front.
            double compact=Math.Max(map.Settlement?520:360,Math.Ceiling(Math.Sqrt(required*.9)+220));
            int odds=oddsOverride>0?oddsOverride:Odds(choice);
            if(choice>=BalancedChoiceCount)
            {
                // Reserve a long approach between armies and ample rear space.
                // Actual footprint grows this further for fully enlarged companies.
                return (float)Math.Max(4000,Math.Max(2000+odds*8,compact*4));
            }
            if(odds>=100)return (float)Math.Max(2000,compact*3);
            if(odds>=25)return (float)Math.Max(1000,compact*2);
            return (float)compact;
        }
        static int Share(int total,int count,int index)=>total/count+(index<total%count?1:0);
        static BattleMapProfile Map(Encounter e,int stage,int choice)=>new BattleMapProfile {
            RegionId="glory-"+stage+"-"+choice,RegionName=e.Ground,Biome=e.Biome,
            Forest=e.Biome==BattleBiome.Woodland?.72f:e.Biome==BattleBiome.Arid?.04f:.16f,
            Relief=e.Biome==BattleBiome.Highlands?2.2f:e.Biome==BattleBiome.Snow?1.2f:.45f,
            Aridity=e.Biome==BattleBiome.Arid?.85f:0,Snow=e.Biome==BattleBiome.Snow?.85f:0,Coast=e.Biome==BattleBiome.Coastal,
            Settlement=e.Town,DefenderSide=e.Defend?ArmySide.French:ArmySide.British,
            FortLevel=e.Town&&e.Faction=="ming"?1:0,SettlementRadius=220
        };
        public static string UnitName(string faction,UnitRole role)
        {
            if(RegionalArmyRoster.Supports(faction))
                return RegionalArmyRoster.Label(faction,role==UnitRole.Cavalry?7:role==UnitRole.Swordsman?6:role==UnitRole.Militia?4:UnitRoster.Light(role)?2:0);
            return UnitRoster.FactionNames[Array.IndexOf(UnitRoster.Factions,faction)]+" / "+UnitRoster.Label(role);
        }
        static UnityEngine.Color FactionColor(string faction)
        {
            switch(faction)
            {
                case "france":return new UnityEngine.Color(.20f,.36f,.78f);
                case "prussia":return new UnityEngine.Color(.32f,.36f,.43f);
                case "russia":return new UnityEngine.Color(.22f,.47f,.30f);
                case "austria":return new UnityEngine.Color(.85f,.81f,.66f);
                case "italy":return new UnityEngine.Color(.31f,.62f,.42f);
                case "hungary":return new UnityEngine.Color(.65f,.28f,.40f);
                case "ottoman":return new UnityEngine.Color(.70f,.20f,.22f);
                case "ming":return new UnityEngine.Color(.86f,.63f,.17f);
                case "india":return new UnityEngine.Color(.82f,.43f,.19f);
                default:return new UnityEngine.Color(.78f,.14f,.13f);
            }
        }
    }
}
