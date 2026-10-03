using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace EmpireGame
{
    [Serializable] public sealed class GloryRegiment
    {
        public int id, tier, soldiers=120,quality, sizeUpgrades, sizeCapacityBonus;
        public string name="",weapon="";
        public UnitLoadout loadout=new UnitLoadout();
        public UnitProgression progression=new UnitProgression();
        public string Name=>(string.IsNullOrEmpty(name)?"Regiment "+id:name)+" · "+GloryCompany.TierNames[tier];
    }
    [Serializable] public sealed class GlorySave
    {
        public int version=4, coins=240, victories, commanderXP, nextId=2, encounterRerolls;
        public string serviceSequenceNamespace=Guid.NewGuid().ToString("N");
        public long serviceBattleSequence;
        public bool inBattle, defeated, summaryPending, recoveryBonusClaimed;
        public GloryBattleReport lastBattle;
        public string report="Your first regiment awaits orders. Replacements cost coins; casualties persist.";
        public List<GloryRegiment> regiments=new List<GloryRegiment>{new GloryRegiment{id=1}};
    }
    public sealed class GloryScenario
    {
        public int choice, stage, seed, coins, xp, terrainStyle, bonusPercent, enemyGuns;
        public string name, terrain, composition, archetype, brief, enemyFaction, enemyNation, objective;
        public ArmyTactics tactics;
        public float separation;
        public Color enemyColor;
        public UnitRole[] roles;
        public int[] strengths, enemyXP;
        public float[] enemyQuality;
        public BattleMapProfile map;
        public int headcountOdds;
        public int[] enemyTiers;
        public string[] enemyWeapons;
        public UnitLoadout[] enemyLoadouts;
        public UnitProgression[] enemyProgression;
        public UnitProgression enemyArtilleryProgression;
        public UnitLoadout enemyArtilleryLoadout;
        public int enemyArtilleryTier,enemyArtilleryQuality;
        public double playerPower,enemyPower;
        public bool powerBalanced;
    }
    public enum GloryCompanyOrder { Replenish, Equipment, Repair, Quality, Training }
    public sealed class GloryCompany
    {
        public const int Capacity=120, MaxRegiments=6, InfantryTierCount=8, MaxPersonnelCapacity=BattleConfig.MaxPersonnelPerRegiment;
        public static readonly string[] TierNames={"Line infantry","Light infantry","Riflemen","Guard infantry","Grenadiers","Elite riflemen","Guard grenadiers","Imperial guard","Cavalry squadron","Heavy cavalry","Field artillery","Veteran artillery"};
        public static readonly UnitRole[] Roles={UnitRole.Line,UnitRole.Skirmisher,UnitRole.Rifleman,UnitRole.Guard,UnitRole.Guard,UnitRole.Rifleman,UnitRole.Guard,UnitRole.Guard,UnitRole.Cavalry,UnitRole.Cavalry,UnitRole.Line,UnitRole.Line};
        public static readonly int[] UnlockXP={0,200,500,900,2000,4000,5200,8000,500,1800,900,4000};
        public static readonly int[] RecruitCosts={300,390,510,630,780,940,1120,1360,600,900,850,1250};
        public static readonly int[] ReplacementCosts={2,3,4,5,6,7,8,10,8,12,100,150};
        public GlorySave Save {get;}
        public static readonly int[] PromotionLevels={1,2,3,4,10,18,26,40,1,12,1,20};
        public static readonly string[] TierWeapons={"","","flintlock-rifles","","percussion-muskets","percussion-rifles","breechloaders","smokeless-rifles","","","",""};
        public static string TierIssue(int tier)
        {
            if(tier>=InfantryTierCount)return tier==8?"60 riders · balanced sabres":tier==9?"60 riders · quality 1 · steel cuirasses":tier==10?"4 guns · crew tools":"4 guns · quality 2 · gun sights";
            string gear=tier<4?"Standard kit":tier==4?"Quality 1 · grenade pouches":tier==5?"Quality 2":tier==6?"Quality 3 · level 3 grenades":"Quality 4 · level 3 colours";
            return gear+(TierWeapons[tier]==""?"":"\n"+UnitEquipment.Find(TierWeapons[tier]).Name);
        }
        public int UnlockedTier=>Array.FindLastIndex(UnlockXP.Take(InfantryTierCount).ToArray(),xp=>Save.commanderXP>=xp);
        public int EquipmentYear=>1918;
        public static int EraYear(int commandXP)=>1918;
        public static int EraUnlockXP(int year)=>0;
        public static bool IsArtillery(GloryRegiment r)=>r.tier>=10;
        public static int SizeStep(GloryRegiment r)=>IsArtillery(r)?1:20;
        public static int MaxSizeUpgrades(GloryRegiment r)=>IsArtillery(r)?8:(MaxPersonnelCapacity-BaseCapacity(r))/SizeStep(r);
        public static int BaseCapacity(GloryRegiment r)=>IsArtillery(r)?ArtillerySystem.CannonsPerUnit:r.tier>=InfantryTierCount?60:Capacity;
        public static int MinSizeUpgrades(GloryRegiment r)=>r.tier<InfantryTierCount?-3:0;
        public static int UnitCapacity(GloryRegiment r)=>BaseCapacity(r)+r.sizeCapacityBonus;
        // Price the full authorized roster, so casualties cannot discount a permanent issue.
        public int ScaledCost(GloryRegiment r,int baseCost)=>(int)Math.Ceiling((double)baseCost*UnitCapacity(r)/BaseCapacity(r));
        public int EquipmentCost(GloryRegiment r,EquipmentItem item)=>ScaledCost(r,item.Cost);
        public int QualityCost(GloryRegiment r)=>ScaledCost(r,60);
        public int SizeIssueCost(GloryRegiment r)=>(int)Math.Ceiling((double)(r.loadout.items.Sum(id=>UnitEquipment.Find(id).Cost)+r.quality*60)*SizeStep(r)/BaseCapacity(r));
        public bool DecreaseSize(GloryRegiment r)
        {
            if(!CanManage||!Own(r)||r.sizeUpgrades<=MinSizeUpgrades(r))return false;
            r.sizeUpgrades--;r.sizeCapacityBonus-=SizeStep(r);r.soldiers=Math.Min(r.soldiers,UnitCapacity(r));return true;
        }
        public int SizeUpgradeCost(GloryRegiment r)=>SizeStep(r)*ReplacementCosts[r.tier]+60+Math.Max(0,r.sizeUpgrades)*20+SizeIssueCost(r);
        public bool IncreaseSize(GloryRegiment r)
        {
            if(!CanManage||!Own(r)||(IsArtillery(r)&&r.soldiers!=UnitCapacity(r))||r.sizeUpgrades>=MaxSizeUpgrades(r)||Save.coins<SizeUpgradeCost(r))return false;
            Save.coins-=SizeUpgradeCost(r);r.sizeUpgrades++;r.sizeCapacityBonus+=SizeStep(r);r.soldiers+=SizeStep(r);return true;
        }
        public static string StrengthLabel(GloryRegiment r)=>IsArtillery(r)?"guns":r.tier>=InfantryTierCount?"riders":"troops";
        public static int InitialQuality(int tier)=>tier<InfantryTierCount?Math.Max(0,tier-3):tier==9?1:tier==11?2:0;
        public bool TierUnlocked(int tier)=>tier>=0&&tier<TierNames.Length&&Save.commanderXP>=UnlockXP[tier];
        public int ReadyTroops=>Save.regiments.Where(r=>!IsArtillery(r)).Sum(r=>r.soldiers);
        public bool CanDeploy=>CanManage&&ReadyTroops>0;
        // Defeat records the last result; it does not disband the surviving company.
        // This also lets companies from older, terminal-defeat saves regroup.
        public bool CanManage=>!Save.inBattle;
        public string RecoveryMessage=>Save.recoveryBonusClaimed
            ?"Recovery bonus: all regiments replenished and +250 coins. XP, skills, equipment and honours are retained."
            :ReadyTroops>0
            ?"Regroup your survivors, replenish casualties with coins, and fight again. XP, skills, equipment and honours are retained."
            :Save.regiments.Any(r=>!IsArtillery(r)&&AffordableReplacements(r)>0)
                ?"No troops remain ready. Buy replacements in Company before fighting again; your regiment records and honours are retained."
                :"No troops remain ready and replacements are unaffordable. Review your company or start a new company.";
        public bool TryRecoveryBonus()
        {
            if(!CanManage||Save.recoveryBonusClaimed||!Save.regiments.Any(r=>Missing(r)>0)
                ||Save.regiments.Any(r=>AffordableReplacements(r)>0))return false;
            foreach(var r in Save.regiments)r.soldiers=UnitCapacity(r);
            Save.coins=Math.Min(10000000,Save.coins+250);Save.recoveryBonusClaimed=true;
            Save.report=RecoveryMessage;
            return true;
        }
        public GloryCompany(GlorySave save=null)
        {
            Save=save??new GlorySave();
            if(Save.version==1&&Save.regiments!=null){foreach(var r in Save.regiments)if(r!=null){r.loadout=new UnitLoadout();r.quality=0;}Save.version=2;}
            if(Save.version==2&&Save.regiments!=null)
            {
                foreach(var r in Save.regiments)if(r!=null&&r.sizeCapacityBonus==0)
                    r.sizeCapacityBonus=r.sizeUpgrades*(r.tier>=10?1:r.tier>=InfantryTierCount?10:20);
                // Mounted units used ten-rider size steps in schema 2. Normalize
                // their upgrade count to the new twenty-person steps without
                // changing the migrated capacity.
                foreach(var r in Save.regiments)if(r!=null&&r.tier>=InfantryTierCount&&r.tier<10)
                    r.sizeUpgrades=r.sizeCapacityBonus/SizeStep(r);
                Save.version=3;
            }
            if(Save.version==3){Save.serviceSequenceNamespace=NewSequenceNamespace();Save.serviceBattleSequence=Save.victories;Save.version=4;}
            if(string.IsNullOrEmpty(Save.serviceSequenceNamespace))Save.serviceSequenceNamespace=NewSequenceNamespace();
            if(!Valid(Save))throw new ArgumentException("Invalid Regimental Glory save.");
        }
        public static bool Valid(GlorySave s)=>s!=null&&s.version==4&&s.coins>=0&&s.coins<=10000000&&s.victories>=0&&s.victories<=10000&&s.encounterRerolls>=0&&s.encounterRerolls<=1000000
            &&ValidServiceSequence(s.serviceSequenceNamespace,s.serviceBattleSequence)
            &&(!s.summaryPending||s.lastBattle!=null)&&(s.lastBattle==null||s.lastBattle.Valid)
            &&s.commanderXP>=0&&s.commanderXP<=1000000&&s.nextId>0&&s.regiments!=null&&s.regiments.Count>0&&s.regiments.Count<=MaxRegiments
            &&s.regiments.All(r=>r!=null&&r.id>0&&r.id<s.nextId&&(string.IsNullOrEmpty(r.name)||SavedRegiment.SafeName(r.name,60))&&r.tier>=0&&r.tier<TierNames.Length&&r.sizeUpgrades>=MinSizeUpgrades(r)&&r.sizeUpgrades<=MaxSizeUpgrades(r)&&UnitCapacity(r)>=(r.tier<InfantryTierCount?60:BaseCapacity(r))&&UnitCapacity(r)<=MaxPersonnelCapacity&&r.soldiers>=0&&r.soldiers<=UnitCapacity(r)&&r.progression!=null&&r.progression.Valid&&(string.IsNullOrEmpty(r.weapon)||MilitaryEquipment.Weapon(r.weapon)!=null&&MilitaryEquipment.Weapon(r.weapon).EarliestYear<=EraYear(s.commanderXP))&&r.quality>=0&&r.quality<=UnitProgression.MaxQuality&&r.loadout!=null&&r.loadout.Valid(UnitEquipment.RetainedUsers(Users(r),r.tier+1),EraYear(s.commanderXP)))
            &&s.regiments.Select(r=>r.id).Distinct().Count()==s.regiments.Count;
        bool Own(GloryRegiment r)=>r!=null&&Save.regiments.Contains(r);
        public int Missing(GloryRegiment r)=>UnitCapacity(r)-r.soldiers;
        public int ReplenishCost(GloryRegiment r)=>Missing(r)*ReplacementCosts[r.tier];
        public int AffordableReplacements(GloryRegiment r)=>Math.Min(Missing(r),Save.coins/ReplacementCosts[r.tier]);
        public bool Replenish(GloryRegiment r)
        {
            if(!CanManage||!Own(r))return false;
            int recruits=AffordableReplacements(r);if(recruits<=0)return false;
            Save.coins-=recruits*ReplacementCosts[r.tier];r.soldiers+=recruits;
            // The regiment retains its accumulated XP and learned skills.
            return true;
        }
        public bool Recruit(int tier)
        {
            if(!CanManage||tier<0||!TierUnlocked(tier)||Save.regiments.Count>=MaxRegiments||Save.coins<RecruitCosts[tier])return false;
            Save.coins-=RecruitCosts[tier];var recruit=new GloryRegiment{id=Save.nextId++,tier=tier,soldiers=tier>=10?ArtillerySystem.CannonsPerUnit:tier>=InfantryTierCount?60:Capacity};ApplyTierIssue(recruit);Save.regiments.Add(recruit);return true;
        }
        public int SavedTier(SavedRegiment saved)=>saved.tier>0&&saved.tier<=InfantryTierCount&&Roles[saved.tier-1]==saved.role?saved.tier-1:Array.IndexOf(Roles,saved.role);
        public int SavedRecruitCost(SavedRegiment saved)=>RecruitCosts[Math.Max(0,SavedTier(saved))]+saved.KitCost+saved.VeteranCost;
        public bool CanRecruitSaved(SavedRegiment saved,out string reason)
        {
            reason="Recruit saved cadres between battles in an active expedition.";if(!CanManage)return false;
            if(saved==null||!saved.Valid){reason="Invalid saved infantry regiment.";return false;}
            int tier=SavedTier(saved);if(tier<0){reason="This expedition supports line, light, rifle and guard infantry.";return false;}
            if(!TierUnlocked(tier)){reason="Requires company tier "+(tier+1)+" ("+UnlockXP[tier]+" command XP).";return false;}
            if(Save.regiments.Count>=MaxRegiments){reason="Company has reached its six-regiment limit.";return false;}
            if(Save.coins<SavedRecruitCost(saved)){reason="Requires "+SavedRecruitCost(saved)+" coins.";return false;}reason="";return true;
        }
        public bool RecruitSaved(SavedRegiment saved)
        {
            if(!CanRecruitSaved(saved,out _))return false;Save.coins-=SavedRecruitCost(saved);
            Save.regiments.Add(new GloryRegiment {id=Save.nextId++,name=saved.name,tier=SavedTier(saved),soldiers=Capacity,quality=saved.quality,weapon=saved.weapon,progression=saved.progression.Copy(),loadout=saved.loadout.Copy()});return true;
        }
        public static int NextTier(GloryRegiment r)=>r.tier==7||r.tier==9||r.tier==11?-1:r.tier+1;
        public int UpgradeCost(GloryRegiment r)=>NextTier(r)<0?0:ScaledCost(r,RecruitCosts[NextTier(r)]-RecruitCosts[r.tier]+120);
        public bool Upgrade(GloryRegiment r)
        {
            if(!CanManage||!Own(r))return false;int next=NextTier(r);
            if(next<0||!TierUnlocked(next)||r.progression.UnitLevel<PromotionLevels[next]||Save.coins<UpgradeCost(r))return false;
            // Freeze legacy standard issue before changing role. Promotion changes the
            // troop class, never its purchased equipment, quality, wear or progression.
            if(string.IsNullOrEmpty(r.weapon))r.weapon=Roles[r.tier]==UnitRole.Rifleman?"rifle":"musket";
            Save.coins-=UpgradeCost(r);r.tier=next;return true;
        }
        static void ApplyTierIssue(GloryRegiment r)
        {
            r.quality=Math.Max(r.quality,InitialQuality(r.tier));
            if(r.tier>=InfantryTierCount)
            {
                string kit=r.tier==8?"balanced-sabres":r.tier==9?"cuirasses":r.tier==10?"crew-tools":"gun-sights";
                var item=UnitEquipment.Find(kit);if(UnitEquipment.Current(r.loadout,item.Slot)==null)r.loadout.Equip(item.Id,Users(r));
                if(r.tier==9&&UnitEquipment.Current(r.loadout,EquipmentSlot.Secondary)==null)r.loadout.Equip("balanced-sabres",Users(r));return;
            }
            var gun=UnitEquipment.Find(TierWeapons[r.tier]);var current=UnitEquipment.Current(r.loadout,EquipmentSlot.Weapon);
            if(gun!=null&&(current==null||current.Cost<gun.Cost))r.loadout.Equip(gun.Id,Users(r));
            string issue=r.tier==4?"grenade-pouches":r.tier==6?"grenade-pouches-level-3":r.tier==7?"regimental-colours-level-3":"";
            if(issue!=""&&UnitEquipment.Current(r.loadout,UnitEquipment.Find(issue).Slot)==null)r.loadout.Equip(issue,Users(r));
        }
        public static EquipmentUsers Users(GloryRegiment r)=>IsArtillery(r)?EquipmentUsers.Artillery:Roles[r.tier]==UnitRole.Cavalry?EquipmentUsers.Cavalry:Roles[r.tier]==UnitRole.Skirmisher?EquipmentUsers.Light:Roles[r.tier]==UnitRole.Rifleman?EquipmentUsers.Rifle:EquipmentUsers.Line;
        public bool CanEquip(GloryRegiment r,EquipmentItem item,out string reason,bool checkFunds=true)
        {
            reason="Equipment is available between battles in an active expedition.";
            if(!CanManage||!Own(r)||item==null)return false;
            if(!item.Supports(Users(r))){reason="This regiment cannot carry that item.";return false;}
            if(!UnitEquipment.CanReplace(r.loadout,item)){reason="Already equipped at this level or higher.";return false;}
            int cost=EquipmentCost(r,item);
            if(checkFunds&&Save.coins<cost){reason="Requires "+cost+" coins.";return false;}reason="";return true;
        }
        public bool Equip(GloryRegiment r,string id)
        {var item=UnitEquipment.Find(id);if(!CanEquip(r,item,out _))return false;if(!r.loadout.Equip(id,Users(r),EquipmentYear))return false;Save.coins-=EquipmentCost(r,item);return true;}
        public bool ImproveQuality(GloryRegiment r)
        {if(!CanManage||!Own(r)||r.quality>=UnitProgression.MaxQuality||Save.coins<QualityCost(r))return false;Save.coins-=QualityCost(r);r.quality++;return true;}
        public bool Learn(GloryRegiment r,RegimentAbility ability)=>CanManage&&Own(r)&&RegimentAbilities.Find(ability)!=null&&RegimentAbilities.Find(ability).Supports(Users(r))&&r.progression.Learn(ability);
        public static bool UsefulSkill(GloryRegiment r,UnitSkill skill)=>IsArtillery(r)?skill!=UnitSkill.Health&&skill!=UnitSkill.Melee:Roles[r.tier]==UnitRole.Cavalry&&!UnitRoster.HasFirearm(UnitRole.Cavalry,r.loadout)?skill!=UnitSkill.Accuracy&&skill!=UnitSkill.Drill&&skill!=UnitSkill.Ammunition:true;
        public bool Train(GloryRegiment r,UnitSkill skill)=>CanManage&&Own(r)&&UsefulSkill(r,skill)&&r.progression.Train(skill);
        public int RepairCost(GloryRegiment r)=>r.loadout.wear==0?0:Math.Max(1,(int)Math.Ceiling(ScaledCost(r,10+r.quality*6+r.loadout.items.Sum(id=>UnitEquipment.Find(id).Materials))*r.loadout.wear/100.0));
        public bool Repair(GloryRegiment r)
        {int cost=r==null?0:RepairCost(r);if(!CanManage||!Own(r)||cost==0||Save.coins<cost)return false;Save.coins-=cost;r.loadout.wear=0;return true;}
        public bool CompanyOrder(GloryCompanyOrder order,bool execute,out int cost,out int affected,out string reason)
        {
            cost=0;affected=0;reason="Requires an active company between battles.";
            if(!CanManage)return false;
            var targets=Save.regiments;
            var kit=targets.SelectMany(r=>r.loadout.items.Select(UnitEquipment.Find).Select(item=>new {regiment=r,item=UnitEquipment.Next(r.loadout,item.Slot,Users(r))}))
                .Where(p=>p.item!=null&&CanEquip(p.regiment,p.item,out _,false)).ToArray();
            var train=targets.Select(r=>new {regiment=r,skill=Enum.GetValues(typeof(UnitSkill)).Cast<UnitSkill>().Where(s=>UsefulSkill(r,s)).OrderBy(s=>r.progression.Level(s)).First()})
                .Where(p=>p.regiment.progression.AvailablePoints>0&&p.regiment.progression.Level(p.skill)<UnitProgression.MaxSkill).ToArray();
            switch(order)
            {
                case GloryCompanyOrder.Replenish:cost=targets.Sum(ReplenishCost);affected=targets.Sum(Missing);break;
                case GloryCompanyOrder.Equipment:cost=kit.Sum(p=>EquipmentCost(p.regiment,p.item));affected=kit.Length;break;
                case GloryCompanyOrder.Repair:cost=targets.Sum(RepairCost);affected=targets.Count(r=>r.loadout.wear>0);break;
                case GloryCompanyOrder.Quality:affected=targets.Count(r=>r.quality<UnitProgression.MaxQuality);cost=targets.Where(r=>r.quality<UnitProgression.MaxQuality).Sum(QualityCost);break;
                case GloryCompanyOrder.Training:affected=train.Length;break;
                default:reason="Unknown company order.";return false;
            }
            if(affected==0){reason=order==GloryCompanyOrder.Equipment?"All issued kit is at its maximum level or locked.":"No regiments need this order.";return false;}
            if(Save.coins<cost){reason="Requires "+cost+" coins for the complete order.";return false;}
            reason="";if(!execute)return true;
            // The complete order is checked before any money or progression is changed.
            switch(order)
            {
                case GloryCompanyOrder.Replenish:foreach(var r in targets)r.soldiers=UnitCapacity(r);break;
                case GloryCompanyOrder.Equipment:foreach(var p in kit)p.regiment.loadout.Equip(p.item.Id,Users(p.regiment),EquipmentYear);break;
                case GloryCompanyOrder.Repair:foreach(var r in targets)r.loadout.wear=0;break;
                case GloryCompanyOrder.Quality:foreach(var r in targets.Where(r=>r.quality<UnitProgression.MaxQuality))r.quality++;break;
                case GloryCompanyOrder.Training:foreach(var p in train)p.regiment.progression.Train(p.skill);break;
            }
            Save.coins-=cost;reason=order+" completed for "+affected+(order==GloryCompanyOrder.Replenish?" replacements.":order==GloryCompanyOrder.Equipment?" kit items.":" regiments.");return true;
        }
        public static double Power(GloryRegiment r)=>BattlePower.Total(r.soldiers,Roles[r.tier],r.tier+1,r.quality,r.progression,r.loadout,!string.IsNullOrEmpty(r.weapon)?r.weapon:Roles[r.tier]==UnitRole.Rifleman?"rifle":"musket",IsArtillery(r));
        public double ReadyPower=>Save.regiments.Sum(Power);
        string offerSignature="";
        GloryScenario[] cachedOffers;
        public GloryScenario[] Choices()
        {
            // Save data is edited in-place by all company services; include every
            // matching input so a purchase or casualty invalidates the cached cards.
            string signature=Save.victories+"|"+Save.encounterRerolls+"|"+string.Join("|",Save.regiments.Select(r=>r.tier+","+r.soldiers+","+r.quality+","+r.weapon+","+r.loadout.wear+","+string.Join("/",r.loadout.items)+","+
                r.progression.xp+","+r.progression.HighestLevel+","+r.progression.accuracy+","+r.progression.drill+","+r.progression.discipline+","+r.progression.melee+","+r.progression.endurance+","+r.progression.health+","+r.progression.ammunition+","+string.Join("/",r.progression.abilities??new List<RegimentAbility>())));
            if(cachedOffers==null||signature!=offerSignature){cachedOffers=GloryEncounters.ForCompany(this);offerSignature=signature;}
            return cachedOffers;
        }
        public bool RerollEncounter()
        {
            if(!CanManage||Save.encounterRerolls>=1000000)return false;
            Save.encounterRerolls++;cachedOffers=null;return true;
        }
        public BattleConfig Deployment(GloryScenario scenario)
        {
            if(!CanDeploy||scenario==null||scenario.stage!=Save.victories+1||scenario.choice<0||scenario.choice>=GloryEncounters.ChoiceCount)throw new InvalidOperationException("No available scenario.");
            // Resolve from the current stage so callers cannot supply altered rewards or enemies.
            scenario=GloryEncounters.ForCompany(this,scenario.choice)[0];
            var troops=Save.regiments.Where(r=>r.soldiers>0&&!IsArtillery(r)).OrderBy(r=>Roles[r.tier]==UnitRole.Cavalry?1:0).ToArray();
            var batteries=Save.regiments.Where(r=>r.soldiers>0&&IsArtillery(r)).ToArray();if(troops.Length==0)throw new InvalidOperationException("Replenish a regiment first.");
            var roles=troops.Select(r=>Roles[r.tier]).Concat(scenario.roles).ToArray();
            var progress=troops.Select(r=>r.progression.Copy()).Concat(scenario.enemyProgression.Select(p=>p.Copy())).ToArray();
            int enemyBatteries=scenario.enemyGuns/ArtillerySystem.CannonsPerUnit;
            return new BattleConfig {CampaignDeployment=true,RegimentalDeployment=true,RequireDeployment=true,RegionalMap=true,ExplicitArmyCounts=true,
                FrenchBattalions=troops.Count(r=>Roles[r.tier]!=UnitRole.Cavalry),FrenchCavalry=troops.Count(r=>Roles[r.tier]==UnitRole.Cavalry),BritishBattalions=scenario.roles.Count(r=>r!=UnitRole.Cavalry),BritishCavalry=scenario.roles.Count(r=>r==UnitRole.Cavalry),RidersPerSquadron=MaxPersonnelCapacity,FrenchArtillery=batteries.Sum(r=>r.soldiers),BritishArtillery=scenario.enemyGuns,SoldiersPerBattalion=MaxPersonnelCapacity,
                FieldHalfSize=GloryEncounters.FieldHalfSize(roles,troops.Select(r=>r.soldiers).Concat(scenario.strengths).ToArray(),troops.Length,scenario.map,scenario.powerBalanced?0:scenario.choice,scenario.headcountOdds),
                InitialSeparation=scenario.separation,Seed=scenario.seed,Map=scenario.map,PlayerCountryName="Your company",EnemyCountryName=scenario.enemyNation,
                EnemyFaction=scenario.enemyFaction,EnemyCountryColor=scenario.enemyColor,EnemyTactics=scenario.tactics,
                InitialStrengths=troops.Select(r=>r.soldiers).Concat(scenario.strengths).ToArray(),FormationRoles=roles,
                FormationTiers=troops.Select(r=>r.tier+1).Concat(scenario.enemyTiers).ToArray(),
                FormationNames=troops.Select(r=>r.Name).Concat(scenario.roles.Select((r,i)=>GloryEncounters.UnitName(scenario.enemyFaction,r)+" "+(i+1))).ToArray(),
                FormationWeapons=troops.Select(r=>r.loadout.items.Select(UnitEquipment.Find).FirstOrDefault(i=>i.Weapon!="")?.Weapon??(!string.IsNullOrEmpty(r.weapon)?r.weapon:Roles[r.tier]==UnitRole.Rifleman?"rifle":"musket")).Concat(scenario.enemyWeapons).ToArray(),
                FormationQuality=troops.Select(r=>r.quality*.04f).Concat(scenario.enemyQuality).ToArray(),
                FormationLoadouts=troops.Select(r=>r.loadout.Copy()).Concat(scenario.enemyLoadouts.Select(l=>l.Copy())).ToArray(),
                FrenchBatterySizes=batteries.Select(r=>r.soldiers).ToArray(),
                ArtilleryProgression=batteries.Select(r=>r.progression.Copy()).Concat(Enumerable.Range(0,enemyBatteries).Select(_=>scenario.enemyArtilleryProgression.Copy())).ToArray(),
                ArtilleryLoadouts=batteries.Select(r=>r.loadout.Copy()).Concat(Enumerable.Range(0,enemyBatteries).Select(_=>scenario.enemyArtilleryLoadout.Copy())).ToArray(),
                ArtilleryEquipment=batteries.Select(r=>r.quality).Concat(Enumerable.Repeat(scenario.enemyArtilleryQuality,enemyBatteries)).ToArray(),
                ArtilleryTiers=batteries.Select(r=>r.tier+1).Concat(Enumerable.Repeat(scenario.enemyArtilleryTier,enemyBatteries)).ToArray(),
                FormationDoctrine=roles.Select(r=>(int)MilitaryEquipment.AllDoctrine).ToArray(),FormationProgression=progress};
        }
        public bool Complete(GloryScenario scenario,BattleWorld world,bool concede=false,ArtillerySystem artillery=null)
        {
            if(!Save.inBattle||scenario==null||world==null||scenario.stage!=Save.victories+1||scenario.choice<0||scenario.choice>=GloryEncounters.ChoiceCount||(!concede&&world.Outcome==BattleOutcome.Ongoing))return false;
            scenario=GloryEncounters.ForCompany(this,scenario.choice)[0];
            var deployed=Save.regiments.Where(r=>r.soldiers>0&&!IsArtillery(r)).OrderBy(r=>Roles[r.tier]==UnitRole.Cavalry?1:0).ToArray();
            var batteries=Save.regiments.Where(r=>r.soldiers>0&&IsArtillery(r)).ToArray();
            if(batteries.Length>0&&(artillery==null||world.Config.FrenchArtillery!=batteries.Sum(r=>r.soldiers)))return false;
            if(world.Config.Seed!=scenario.seed||world.Config.FrenchBattalionCount!=deployed.Length)return false;
            bool won=!concede&&world.Outcome==BattleOutcome.FrenchVictory;
            long sequence=Save.serviceBattleSequence<RegimentServiceLedger.MaxCounter?++Save.serviceBattleSequence:Save.serviceBattleSequence;
            string battleId=sequence>0&&sequence<=RegimentServiceLedger.MaxCounter?Save.serviceSequenceNamespace+":"+sequence.ToString(System.Globalization.CultureInfo.InvariantCulture):"";
            string date="Stage "+scenario.stage.ToString(System.Globalization.CultureInfo.InvariantCulture)+" · Engagement "+sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string label=string.IsNullOrWhiteSpace(scenario.name)?"Battle":scenario.name;
            var serviceOutcome=world.Outcome==BattleOutcome.Draw&&!concede?ServiceBattleOutcome.Draw:concede?ServiceBattleOutcome.Retreat:won?ServiceBattleOutcome.Victory:ServiceBattleOutcome.Defeat;
            var report=GloryBattleReport.Capture(Save,scenario);
            for(int i=0;i<deployed.Length;i++)
            {
                var unit=world.Battalions[i];var r=deployed[i];
                report.RecordCombat(r.id,unit.Kills,unit.Hits,unit.ShotsFired);
                RegimentServiceCommit.Record(r.progression,battleId,label,date,serviceOutcome,unit.SoldierCount,unit.SurvivorCount,unit.Kills,unit.Hits,unit.ShotsFired);
                r.progression.CreditKills(unit.Kills);
                r.loadout.wear=Math.Min(100,r.loadout.wear+5+20*(r.soldiers-unit.SurvivorCount)/Math.Max(1,r.soldiers));r.soldiers=unit.SurvivorCount;
                if(r.soldiers>0)r.progression.Award(GloryEncounters.BattleXP(unit.SoldierCount,r.soldiers,unit.Kills,unit.Hits,unit.ShotsFired,won,Roles[r.tier])+(won?scenario.xp:0));
            }
            for(int i=0;i<batteries.Length;i++)
            {
                var r=batteries[i];var guns=artillery.Guns.Where(g=>g.Side==ArmySide.French&&g.UnitId==i).ToArray();
                report.RecordCombat(r.id,guns.Sum(g=>g.Kills),guns.Sum(g=>g.Hits),guns.Sum(g=>g.ShotsFired));
                RegimentServiceCommit.Record(r.progression,battleId,label,date,serviceOutcome,r.soldiers,guns.Count(g=>g.Operational),guns.Sum(g=>g.Kills),guns.Sum(g=>g.Hits),guns.Sum(g=>g.ShotsFired));
                r.progression.CreditKills(guns.Sum(g=>g.Kills));
                int deployedGuns=r.soldiers;r.soldiers=guns.Count(g=>g.Operational);
                r.loadout.wear=Math.Min(100,r.loadout.wear+5+20*(deployedGuns-r.soldiers)/Math.Max(1,deployedGuns));
                if(r.soldiers>0)r.progression.Award(GloryEncounters.BattleXP(deployedGuns,r.soldiers,guns.Sum(g=>g.Kills),guns.Sum(g=>g.Hits),guns.Sum(g=>g.ShotsFired),won,UnitRole.Line,true)+(won?scenario.xp:0));
            }
            Save.inBattle=false;Save.recoveryBonusClaimed=false;
            if(won&&Save.regiments.Any(r=>r.soldiers>0))
            {
                Save.defeated=false;
                Save.victories++;Save.coins=Math.Min(10000000,Save.coins+scenario.coins);Save.commanderXP=Math.Min(1000000,Save.commanderXP+scenario.xp);
                Save.report="Victory! +"+scenario.coins+" coins, +"+scenario.xp+" command XP. Surviving regiments earned XP. Pay to replenish casualties before the next battle.";
            }
            else {Save.defeated=true;Save.report=(concede?"Battle conceded. ":"Defeat. ")+RecoveryMessage;}
            report.Finish(Save,won,concede);Save.lastBattle=report;Save.summaryPending=true;
            // Keep the battle report's real casualties and rewards before issuing relief.
            TryRecoveryBonus();
            return true;
        }
        static string NewSequenceNamespace()=>Guid.NewGuid().ToString("N");
        static bool ValidServiceSequence(string value,long sequence)=>!string.IsNullOrEmpty(value)&&value.Length==32&&value.All(Uri.IsHexDigit)
            &&sequence>=0&&sequence<=RegimentServiceLedger.MaxCounter;
    }
}
