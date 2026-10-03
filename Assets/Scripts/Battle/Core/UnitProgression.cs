using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireBattle
{
    public enum UnitSkill { Accuracy, Drill, Discipline, Melee, Endurance, Health, Ammunition }

    // Shared by regiments, cavalry squadrons and artillery batteries. No national research here.
    [Serializable]
    public sealed class UnitProgression
    {
        public int xp, earnedRanks, accuracy, drill, discipline, melee, endurance, health, highestLevel, ammunition;
        public RegimentServiceLedger service=new RegimentServiceLedger();
        public long totalKills;
        public void CreditKills(int kills){if(kills>0)totalKills=totalKills>long.MaxValue-kills?long.MaxValue:totalKills+kills;}
        public List<RegimentAbility> abilities=new List<RegimentAbility>();
        public const int MaxXP=1000000, MaxSkill=100, MaxQuality=5, XPPerKill=6;
        static readonly int[] Thresholds=BuildThresholds();
        static int[] BuildThresholds()
        {
            var values=new int[MaxLevel];int[] opening={0,100,250,500,850,1300,1850,2500,3250,4100};Array.Copy(opening,values,opening.Length);
            for(int i=10;i<values.Length;i++)values[i]=values[i-1]+950+(i-10)*25;return values;
        }
        static readonly string[] Names={"Recruit","Trained","Seasoned","Veteran","Elite","Guard"};
        public int UnitLevel {get {int level=1;while(level<Thresholds.Length&&xp>=Thresholds[level])level++;return level;}}
        public int HighestLevel=>Math.Max(1,Math.Max(highestLevel,Math.Max(earnedRanks+1,earnedRanks==5?UnitLevel:1)));
        public int Rank=>Math.Min(5,UnitLevel-1);
        public const int MaxLevel=100;
        public int StatPointsEarned=>HighestLevel*2;
        public int SkillPointsEarned=>(HighestLevel+1)/2;
        public int AvailableSkillPoints=>Math.Max(0,SkillPointsEarned-(abilities?.Count??0));
        public int AbilityRank(RegimentAbility ability)=>abilities?.Count(a=>a==ability)??0;
        public bool Has(RegimentAbility ability)=>AbilityRank(ability)>0;
        public bool Learn(RegimentAbility ability)
        {
            var definition=RegimentAbilities.Find(ability);
            if(definition==null||AbilityRank(ability)>=RegimentAbilities.MaxRank||AvailableSkillPoints<=0)return false;
            if(abilities==null)abilities=new List<RegimentAbility>();abilities.Add(ability);return true;
        }
        public string RankName=>Names[Rank];
        public int RankStartXP=>Thresholds[UnitLevel-1];
        public int NextRankXP=>UnitLevel==MaxLevel?Thresholds[MaxLevel-1]:Thresholds[UnitLevel];
        public int Spent=>accuracy+drill+discipline+melee+endurance+health+ammunition;
        public int AvailablePoints=>Math.Max(0,StatPointsEarned-Spent);
        public int Level(UnitSkill skill)
        {
            switch(skill){case UnitSkill.Accuracy:return accuracy;case UnitSkill.Drill:return drill;
                case UnitSkill.Discipline:return discipline;case UnitSkill.Melee:return melee;case UnitSkill.Endurance:return endurance;case UnitSkill.Health:return health;case UnitSkill.Ammunition:return ammunition;default:return 0;}
        }
        public bool Train(UnitSkill skill)
        {
            if(!Enum.IsDefined(typeof(UnitSkill),skill)||AvailablePoints==0||Level(skill)>=MaxSkill)return false;
            switch(skill){case UnitSkill.Accuracy:accuracy++;break;case UnitSkill.Drill:drill++;break;
                case UnitSkill.Discipline:discipline++;break;case UnitSkill.Melee:melee++;break;case UnitSkill.Endurance:endurance++;break;case UnitSkill.Health:health++;break;case UnitSkill.Ammunition:ammunition++;break;}
            return true;
        }
        public void Award(int amount)
        {
            if(amount<=0)return;xp=(int)Math.Min(MaxXP,(long)xp+amount);earnedRanks=Math.Max(earnedRanks,Rank);highestLevel=Math.Max(HighestLevel,UnitLevel);
        }
        public void Reinforce(int veterans,int recruits)
        {
            if(veterans<0||recruits<=0)return;
            highestLevel=HighestLevel;
            xp=(int)((long)xp*veterans/((long)veterans+recruits));
            // Institutional skills survive replacements; diluted experience must be earned again.
        }
        public bool Valid=>totalKills>=0&&xp>=0&&xp<=MaxXP&&earnedRanks>=Rank&&earnedRanks<=5&&HighestLevel>=UnitLevel&&highestLevel>=0&&highestLevel<=MaxLevel&&Spent<=StatPointsEarned
            &&health>=0&&health<=MaxSkill&&ammunition>=0&&ammunition<=MaxSkill&&(abilities==null||(abilities.Count<=SkillPointsEarned&&abilities.GroupBy(a=>a).All(g=>g.Count()<=RegimentAbilities.MaxRank&&RegimentAbilities.Find(g.Key)!=null)))
            &&(service==null||service.Valid)&&accuracy>=0&&accuracy<=MaxSkill&&drill>=0&&drill<=MaxSkill&&discipline>=0&&discipline<=MaxSkill
            &&melee>=0&&melee<=MaxSkill&&endurance>=0&&endurance<=MaxSkill;
        public UnitProgression Copy()=>new UnitProgression {totalKills=totalKills,xp=xp,earnedRanks=earnedRanks,accuracy=accuracy,drill=drill,discipline=discipline,melee=melee,endurance=endurance,health=health,ammunition=ammunition,highestLevel=HighestLevel,abilities=abilities==null?new List<RegimentAbility>():new List<RegimentAbility>(abilities),service=service==null?new RegimentServiceLedger():service.Copy()};
        public float MaxMorale=>100+discipline*2+AbilityRank(RegimentAbility.RallyingSpirit)*3+(service?.MoraleBonus??0);
        public float MaxHealth=>100+health*5+AbilityRank(RegimentAbility.IronConstitution)*5;
        public int Grenades(int issued)=>issued>0?issued+AbilityRank(RegimentAbility.GrenadierCadre)*4:0;
        public int AmmunitionBonus(InfantryWeapon weapon)=>(ammunition+AbilityRank(RegimentAbility.AmmunitionReserve)*2)*Math.Max(1,weapon?.Magazine??1);
        public int ArtilleryAmmunitionBonus=>ammunition+(AbilityRank(RegimentAbility.AmmunitionReserve)+AbilityRank(RegimentAbility.ArtilleryMunitions))*2;
        public bool CanUseArtilleryAmmunition(ArtilleryAmmunition type)=>Enum.IsDefined(typeof(ArtilleryAmmunition),type)&&(type==ArtilleryAmmunition.Roundshot||AbilityRank(RegimentAbility.ArtilleryMunitions)>=(int)type);
        public float AccuracyMultiplier=>1+Rank*.025f+accuracy*.02f;
        public float ReloadMultiplier=>Math.Max(.2f,1-Rank*.02f-Math.Min(50,drill)*.01f-Math.Max(0,drill-50)*.004f);
        public float ResolveMultiplier=>1+Rank*.04f+discipline*.03f;
        public float MeleeMultiplier=>1+Rank*.025f+melee*.03f+AbilityRank(RegimentAbility.WeaponMastery)*.02f;
        public float FatigueMultiplier=>Math.Max(.2f,1-Rank*.02f-Math.Min(50,endurance)*.01f-Math.Max(0,endurance-50)*.004f);
        public float PowerMultiplier=>1+Rank*.04f+accuracy*.025f+drill*.025f+discipline*.02f+melee*.025f+endurance*.015f+health*.025f;
        public static string EquipmentName(int level)=>level>=3?"Masterwork grade "+level:level==2?"Arsenal grade":level==1?"Improved":"Standard";
        public static string StatName(UnitSkill skill)=>skill==UnitSkill.Drill?"Reload":skill==UnitSkill.Discipline?"Morale":skill.ToString();
        public static string SkillEffect(UnitSkill skill)=>skill==UnitSkill.Ammunition?"+1 reserve shot, or +1 full magazine, per point":skill==UnitSkill.Health?"+5 health per soldier per point":skill==UnitSkill.Accuracy?"+2% accuracy per point":skill==UnitSkill.Drill?"−1% reload time per point through 50; −0.4% thereafter":
            skill==UnitSkill.Discipline?"+2 morale and +3% resistance per point":skill==UnitSkill.Melee?"+3% melee damage per point":"1% less fatigue per point through 50; 0.4% thereafter";
        public static int BattleXP(int strength,int survivors,int kills,int hits,int shots,bool won)
        {
            if(strength<=0||survivors<=0||(kills<=0&&hits<=0&&shots<=0&&survivors>=strength))return 0;
            // Every credited kill has the same value, regardless of regiment size.
            // Large pursuits can earn multiple levels; only the lifetime XP limit applies.
            return (int)Math.Min(MaxXP,40+(won?20:0)+(long)XPPerKill*Math.Max(0,kills)+20L*Math.Max(0,hits)/strength);
        }
    }
}
