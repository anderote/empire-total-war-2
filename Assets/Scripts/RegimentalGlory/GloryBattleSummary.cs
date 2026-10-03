using System;
using System.Linq;
using Empire.UI;
using EmpireBattle;
using UnityEngine;

namespace EmpireGame
{
    public sealed partial class RegimentalGloryMode
    {
        bool showBattleSummary;
        Vector2 summaryScroll;
        void LeaveSummary(int targetPage,bool abilities=false)
        {
            company.Save.summaryPending=false;
            if(!Store()){company.Save.summaryPending=true;return;}
            showBattleSummary=false;page=targetPage;
            if(abilities)
            {
                var r=company.Save.regiments.FirstOrDefault(u=>u.progression.AvailableSkillPoints>0)??company.Save.regiments[0];
                management.Select(r.id.ToString());management.ShowTab(3);
            }
        }
        void DrawBattleSummary(Rect r)
        {
            var report=company.Save.lastBattle;EmpireTheme.Panel(r,EmpireTheme.Surface);
            CommandUI.Eyebrow(new Rect(r.x+24,r.y+16,r.width-48,22),"Battle report / Stage "+report.stage);
            CommandUI.Label(new Rect(r.x+24,r.y+43,r.width-48,42),report.won?"VICTORY":report.conceded?"BATTLE CONCEDED":"DEFEAT",30,false,true);
            CommandUI.Label(new Rect(r.x+24,r.y+92,r.width-48,30),report.engagement,18,false,true);
            CommandUI.Label(new Rect(r.x+24,r.y+130,r.width-48,44),"+"+report.CoinsGained.ToString("N0")+" coins  ·  +"+report.CommandXPGained.ToString("N0")+" command XP  ·  "+report.Kills.ToString("N0")+" enemy casualties  ·  "+report.Casualties.ToString("N0")+" troops lost",15,true,false,true);
            CommandUI.Label(new Rect(r.x+24,r.y+176,r.width-48,32),report.won&&!company.Save.recoveryBonusClaimed?"XP and points have been saved. Spend earned points in company management.":company.RecoveryMessage,13,true,false,true);
            int columns=r.width>=1550?3:r.width>=1050?2:1;float span=(r.width-64-(columns-1)*12)/columns;
            var viewport=new Rect(r.x+20,r.y+221,r.width-40,Mathf.Max(80,r.height-307));
            float cardHeight=320;
            summaryScroll=GUI.BeginScrollView(viewport,summaryScroll,new Rect(0,0,r.width-58,Mathf.Max(viewport.height-1,Mathf.CeilToInt(report.regiments.Count/(float)columns)*cardHeight)));
            for(int i=0;i<report.regiments.Count;i++)
            {
                var result=report.regiments[i];var card=new Rect(i%columns*(span+12),i/columns*cardHeight,span,cardHeight-12);EmpireTheme.Panel(card,EmpireTheme.Ink);
                MilitaryInsignia.Rank(new Rect(card.x+14,card.y+13,32,38),result.after.Rank);
                CommandUI.Label(new Rect(card.x+58,card.y+12,span-72,43),result.name,17,false,true,true);
                CommandUI.Label(new Rect(card.x+14,card.y+62,span-28,25),result.strengthBefore==0?"Not deployed":result.strengthBefore+" → "+result.strengthAfter+" "+result.strengthLabel+"  ·  "+result.Casualties+" lost  ·  "+result.kills+" kills",13,true);
                CommandUI.Label(new Rect(card.x+14,card.y+99,span-28,29),"+"+result.XPGained.ToString("N0")+" XP  ·  Level "+result.before.UnitLevel+" → "+result.after.UnitLevel+(result.LevelsGained>0?"  /  LEVEL UP!":""),16,false,true,true);
                float progress=result.after.UnitLevel==UnitProgression.MaxLevel?1:(result.after.xp-result.after.RankStartXP)/(float)Math.Max(1,result.after.NextRankXP-result.after.RankStartXP);
                CommandUI.Meter(new Rect(card.x+14,card.y+136,span-28,6),Mathf.Clamp01(progress));
                CommandUI.Label(new Rect(card.x+14,card.y+150,span-28,25),result.after.UnitLevel==UnitProgression.MaxLevel?"Maximum level reached":result.after.xp.ToString("N0")+" / "+result.after.NextRankXP.ToString("N0")+" XP to next level",12,true);
                CommandUI.Label(new Rect(card.x+14,card.y+186,span-28,29),"+"+result.StatPointsGained+" stat points  ·  +"+result.AbilityPointsGained+" ability points",14,false,true,true);
                CommandUI.Label(new Rect(card.x+14,card.y+218,span-28,25),"Unspent: "+result.after.AvailablePoints+" stats / "+result.after.AvailableSkillPoints+" abilities",12,true);
                string abilities=string.Join(" · ",(result.after.abilities??new System.Collections.Generic.List<RegimentAbility>()).Distinct().Select(a=>RegimentAbilities.Find(a).Name+" "+result.after.AbilityRank(a)));
                CommandUI.Label(new Rect(card.x+14,card.y+253,span-28,43),abilities==""?"No abilities learned yet":abilities,12,true,false,true);
                tips.Add(card,result.hits+" hits / "+result.shots+" shots\n"+(abilities==""?"Learn abilities using earned points.":"Learned abilities: "+abilities));
            }
            GUI.EndScrollView();
            float footer=r.yMax-64;float bw=Mathf.Min(280,(r.width-72)/3);
            if(CommandUI.Button(new Rect(r.x+24,footer,bw,40),report.won?"Continue to company":"Regroup survivors"))LeaveSummary(0);
            if(CommandUI.Button(new Rect(r.x+36+bw,footer,bw,40),"Review abilities & points"))LeaveSummary(0,true);
            if(CommandUI.Button(new Rect(r.x+48+2*bw,footer,bw,40),"Next battle",company.CanDeploy))LeaveSummary(2);
            if(error!="")CommandUI.Label(new Rect(r.x+24,r.yMax-23,r.width-48,22),error,11,true,false,true);
        }
    }
}
