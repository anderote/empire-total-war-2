using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;

namespace EmpireGame
{
    [Serializable]
    public sealed class GloryRegimentResult
    {
        public int id, strengthBefore, strengthAfter, capacity, kills, hits, shots;
        public string name="", strengthLabel="troops";
        public UnitProgression before=new UnitProgression(), after=new UnitProgression();
        public int XPGained=>after.xp-before.xp;
        public int LevelsGained=>after.UnitLevel-before.UnitLevel;
        public int StatPointsGained=>after.StatPointsEarned-before.StatPointsEarned;
        public int AbilityPointsGained=>after.SkillPointsEarned-before.SkillPointsEarned;
        public int Casualties=>strengthBefore-strengthAfter;
        public bool Valid=>id>0&&SavedRegiment.SafeName(name,128)&&SavedRegiment.SafeName(strengthLabel,16)
            &&capacity>0&&capacity<=GloryCompany.MaxPersonnelCapacity&&strengthBefore>=0&&strengthBefore<=capacity&&strengthAfter>=0&&strengthAfter<=strengthBefore
            &&kills>=0&&hits>=0&&shots>=0&&before!=null&&before.Valid&&after!=null&&after.Valid&&after.xp>=before.xp;
    }
    [Serializable]
    public sealed class GloryBattleReport
    {
        public int stage, coinsBefore, coinsAfter, commandXPBefore, commandXPAfter;
        public string engagement="";
        public bool won, conceded;
        public List<GloryRegimentResult> regiments=new List<GloryRegimentResult>();
        public int CoinsGained=>coinsAfter-coinsBefore;
        public int CommandXPGained=>commandXPAfter-commandXPBefore;
        public int Casualties=>regiments.Sum(r=>r.Casualties);
        public int Kills=>regiments.Sum(r=>r.kills);
        public bool Valid=>stage>0&&SavedRegiment.SafeName(engagement,128)&&coinsBefore>=0&&coinsAfter>=coinsBefore&&coinsAfter<=10000000
            &&commandXPBefore>=0&&commandXPAfter>=commandXPBefore&&commandXPAfter<=UnitProgression.MaxXP&&!(won&&conceded)
            &&regiments!=null&&regiments.Count>0&&regiments.Count<=GloryCompany.MaxRegiments&&regiments.All(r=>r!=null&&r.Valid)
            &&regiments.Select(r=>r.id).Distinct().Count()==regiments.Count;
        public static GloryBattleReport Capture(GlorySave save,GloryScenario scenario)=>new GloryBattleReport {
            stage=scenario.stage,engagement=scenario.name,coinsBefore=save.coins,coinsAfter=save.coins,commandXPBefore=save.commanderXP,commandXPAfter=save.commanderXP,
            regiments=save.regiments.Select(r=>new GloryRegimentResult {id=r.id,name=r.Name,strengthLabel=GloryCompany.StrengthLabel(r),capacity=GloryCompany.UnitCapacity(r),strengthBefore=r.soldiers,strengthAfter=r.soldiers,before=r.progression.Copy(),after=r.progression.Copy()}).ToList()
        };
        public void RecordCombat(int regimentId,int kills,int hits,int shots)
        {var result=regiments.Find(r=>r.id==regimentId);if(result==null)return;result.kills=kills;result.hits=hits;result.shots=shots;}
        public void Finish(GlorySave save,bool victory,bool concede)
        {
            won=victory;conceded=concede;coinsAfter=save.coins;commandXPAfter=save.commanderXP;
            foreach(var result in regiments){var r=save.regiments.Find(u=>u.id==result.id);result.strengthAfter=r.soldiers;result.after=r.progression.Copy();}
        }
    }
}
