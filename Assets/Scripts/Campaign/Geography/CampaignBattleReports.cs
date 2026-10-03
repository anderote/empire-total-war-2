using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    [Serializable] public sealed class CampaignRegimentResult
    {
        public string unitId,role,name="",originArmyId="",originTownId="";
        public int before,survivors,xpBefore,xpAfter,pointsBefore,pointsAfter;
    }
    [Serializable] public sealed class CampaignBattleReport
    {
        public string encounterId,regionId,result,method;
        public int turn,playerBefore,playerSurvivors,enemyBefore,enemySurvivors;
        public List<CampaignRegimentResult> regiments=new List<CampaignRegimentResult>();
    }
    public sealed partial class CountryCampaign
    {
        sealed class BattleReportCapture
        {
            public CampaignBattleReport report;
            public List<CampaignRegiment> player,enemy,persistent;
        }
        BattleReportCapture CaptureBattleReport(CampaignEncounter e,string method)
        {
            if(e.attacker!=Save.playerFaction&&e.defender!=Save.playerFaction)return null;
            bool attacking=e.attacker==Save.playerFaction;
            var player=BattleUnits(e,attacking).ToList();var enemy=BattleUnits(e,!attacking).ToList();
            var persistent=player.Where(u=>!e.garrison.Contains(u)).ToList();
            return new BattleReportCapture {player=player,enemy=enemy,persistent=persistent,
                report=new CampaignBattleReport {encounterId=e.id,regionId=e.regionId,turn=Save.world.turn,method=method,
                    playerBefore=player.Sum(u=>u.soldiers),enemyBefore=enemy.Sum(u=>u.soldiers),
                    regiments=persistent.Select(u=>new CampaignRegimentResult {unitId=u.id,role=u.role,name=u.name,originArmyId=u.originArmyId,originTownId=u.originTownId,before=u.soldiers,
                        xpBefore=u.progression.xp,pointsBefore=u.progression.AvailablePoints}).ToList()}};
        }
        void CompleteBattleReport(BattleReportCapture capture,CampaignEncounter e,bool attackerWon)
        {
            if(capture==null)return;
            var report=capture.report;report.result=(e.attacker==Save.playerFaction)==attackerWon?"Victory":"Defeat";
            report.playerSurvivors=capture.player.Sum(u=>u.soldiers);report.enemySurvivors=capture.enemy.Sum(u=>u.soldiers);
            for(int i=0;i<capture.persistent.Count;i++)
            {
                var unit=capture.persistent[i];var row=report.regiments[i];row.survivors=unit.soldiers;
                row.xpAfter=unit.soldiers>0?unit.progression.xp:row.xpBefore;
                row.pointsAfter=unit.soldiers>0?unit.progression.AvailablePoints:0;
            }
            Save.lastBattle=report;
        }
        bool ValidBattleReport(CountryCampaignSave candidate)
        {
            var r=candidate.lastBattle;if(r==null)return true;
            // Unity writes optional inline classes as empty objects.
            if(string.IsNullOrEmpty(r.encounterId)&&string.IsNullOrEmpty(r.regionId)&&r.turn==0&&r.playerBefore==0&&r.enemyBefore==0&&r.playerSurvivors==0&&r.enemySurvivors==0&&string.IsNullOrEmpty(r.result)&&string.IsNullOrEmpty(r.method)&&(r.regiments==null||r.regiments.Count==0))
            {candidate.lastBattle=null;return true;}
            if(!candidate.resolvedEncounters.Contains(r.encounterId)||!Active(r.regionId)||r.turn<1||r.turn>candidate.world.turn||
                r.result!="Victory"&&r.result!="Defeat"||r.method!="Tactical"&&r.method!="Autoresolve"||r.regiments==null||
                r.playerBefore<0||r.playerSurvivors<0||r.playerSurvivors>r.playerBefore||r.enemyBefore<0||r.enemySurvivors<0||r.enemySurvivors>r.enemyBefore)return false;
            if(r.regiments.Any(u=>u==null||string.IsNullOrEmpty(u.unitId)||!string.IsNullOrEmpty(u.name)&&!SavedRegiment.SafeName(u.name,60)||!ValidRole(u.role)||u.before<1||u.before>Balance.unitSize||u.survivors<0||u.survivors>u.before||u.xpBefore<0||u.xpAfter<u.xpBefore||u.xpAfter>UnitProgression.MaxXP||u.pointsBefore<0||u.pointsBefore>UnitProgression.MaxLevel*2||u.pointsAfter<0||u.pointsAfter>UnitProgression.MaxLevel*2))return false;
            return r.regiments.Select(u=>u.unitId).Distinct().Count()==r.regiments.Count&&r.regiments.Sum(u=>(long)u.before)<=r.playerBefore&&r.regiments.Sum(u=>(long)u.survivors)<=r.playerSurvivors;
        }
    }
}
