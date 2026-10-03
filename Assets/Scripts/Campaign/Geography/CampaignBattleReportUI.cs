using System;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        void CampaignBattleReturned()
        {
            countryPage="Battles";campaignPageScroll=Vector2.zero;selectedRegimentId="";
            bool saved=SaveCountry(false);
            countryMessage=saved?"Battle saved. Review casualties and XP in Battles, then select a surviving regiment to improve it.":"Battle complete, but saving failed. Review Battles and save again from the menu.";
        }
        void DrawCampaignBattleReport()
        {
            var report=countryCampaign.Save.lastBattle;
            if(report==null){GUILayout.Label("No battle has been resolved yet.",heading);GUILayout.Label("Select an army and right-click an enemy city. Fight the battle or autoresolve, then review survivors and XP here.",text);return;}
            var region=Array.Find(catalog.regions,r=>r.id==report.regionId);
            GUILayout.Label(report.result.ToUpperInvariant()+" / "+region.capital.name,heading);
            GUILayout.Label(report.method+" / turn "+report.turn+" / latest resolved battle",small);
            GUILayout.Space(12);
            GUILayout.Label("Your army: "+report.playerBefore.ToString("N0")+" deployed / "+report.playerSurvivors.ToString("N0")+" survivors / "+(report.playerBefore-report.playerSurvivors).ToString("N0")+" casualties",text);
            GUILayout.Label("Enemy: "+report.enemyBefore.ToString("N0")+" deployed / "+report.enemySurvivors.ToString("N0")+" survivors / "+(report.enemyBefore-report.enemySurvivors).ToString("N0")+" casualties",text);
            GUILayout.Label("Survivors can retreat or disperse. Local militia garrisons do not become persistent field regiments.",small);
            GUILayout.Space(16);GUILayout.Label("REGIMENT EXPERIENCE AND UPGRADES",campaignCaption);
            foreach(var row in report.regiments)
            {
                var army=countryCampaign.Save.armies.FirstOrDefault(a=>a.owner==countryCampaign.Save.playerFaction&&a.units.Any(u=>u.id==row.unitId));
                var unit=army?.units.Find(u=>u.id==row.unitId);
                GUILayout.Label((string.IsNullOrEmpty(row.name)?unit?.name??row.unitId:row.name)+" / "+row.role+" / "+row.survivors+" of "+row.before+" survived / +"+(row.xpAfter-row.xpBefore)+" XP",heading);
                if(row.survivors==0)GUILayout.Label("Regiment destroyed. Its soldiers and veterancy cannot be recovered through upgrades.",small);
                else
                {
                    GUILayout.Label("After battle: "+row.xpAfter+" XP / "+row.pointsAfter+" unspent stat points"+(row.pointsAfter>row.pointsBefore?" / NEW STAT POINTS":""),text);
                    if(unit!=null)
                    {
                        GUILayout.Label("Now: "+unit.progression.RankName+" / "+unit.progression.AvailablePoints+" stat points / "+unit.progression.AvailableSkillPoints+" ability points / "+unit.soldiers+" soldiers",small);
                        if(GUILayout.Button("Select regiment / stats, abilities, equipment and replacements",campaignAction))
                        {SelectCampaignArmy(army);selectedRegimentId=unit.id;detailScroll=Vector2.zero;}
                    }
                    else GUILayout.Label("This regiment has since been lost or demobilized.",small);
                }
                GUILayout.Space(12);
            }
            GUILayout.Label("Battle XP advances unit level and grants stat and ability points. Spend them on the regiment upgrade screen; kit, weapons and supplies cost treasury and materials. Paid deliveries require a supplied force at home and complete over campaign turns. New recruits dilute veteran XP, while learned skills remain with the regiment.",text);
            if(GUILayout.Button("Open armies",campaignAction)){countryPage="Armies";campaignPageScroll=Vector2.zero;}
        }
    }
}
