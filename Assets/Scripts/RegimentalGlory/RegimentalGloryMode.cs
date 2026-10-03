using System;
using System.Collections;
using System.IO;
using System.Linq;
using Empire.UI;
using EmpireBattle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmpireGame
{
    public sealed partial class RegimentalGloryMode : MonoBehaviour
    {
        GloryCompany company;
        string error="";
        bool reviewMode;
        bool resetPrompt;
        Vector2 scroll;
        public Action OnClose;
        string SavePath=>Path.Combine(Application.persistentDataPath,"regimental-glory.json");
        public void ReviewPage(int value){page=Mathf.Clamp(value,0,3);}
        public void ReviewManagementTab(int tab){if(reviewMode){page=0;management.ShowTab(tab);}}
        public void ReviewStage(int victories){if(reviewMode)company.Save.victories=Mathf.Clamp(victories,0,10000);}
        void Awake()
        {
            reviewMode=Array.IndexOf(Environment.GetCommandLineArgs(),"--ui-workbench-review")>=0;
            if(reviewMode){company=new GloryCompany();company.Save.coins=840;company.Save.commanderXP=250;company.Save.regiments[0].progression.Award(500);company.Save.regiments[0].soldiers=87;return;}
            try
            {
                company=new GloryCompany(File.Exists(SavePath)?JsonUtility.FromJson<GlorySave>(File.ReadAllText(SavePath)):null);
                if(company.Save.inBattle)
                {
                    company.Save.inBattle=false;company.Save.defeated=true;
                    company.Save.report="The previous battle was interrupted. Regroup from your last saved roster; no victory rewards were granted.";
                    Store();
                }
                else if(company.Save.defeated)
                {
                    company.Save.report="Defeat. "+company.RecoveryMessage;
                    Store();
                }
                else if(company.TryRecoveryBonus())Store();
            }
            catch(Exception e){error="Could not load your company: "+e.Message;}
        }
        bool Store()
        {
            if(reviewMode)return true;
            company.TryRecoveryBonus();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                string temp=SavePath+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(company.Save,true));
                if(File.Exists(SavePath))File.Replace(temp,SavePath,SavePath+".bak");else File.Move(temp,SavePath);
                error="";return true;
            }
            catch(Exception e){error="Could not save your company: "+e.Message;return false;}
        }
        void Changed(bool changed){if(changed)Store();}
        void Launch(GloryScenario scenario)
        {
            try
            {
                var config=company.Deployment(scenario);company.Save.inBattle=true;
                if(!Store()){company.Save.inBattle=false;return;}
                GloryBattleBridge.Launch(this,company,scenario,config);
            }
            catch(Exception e){company.Save.inBattle=false;error=e.Message;}
        }
        public bool SaveResult()=>Store();
        readonly RegimentManagementPanel management=new RegimentManagementPanel();
        readonly OptionsPanel options=new OptionsPanel();
        readonly UITooltip tips=new UITooltip();
        Vector2 recruitmentScroll;
        int page;bool showOptions;
        void OnGUI()
        {
            using var theme=new EmpireTheme.Scope(true);
            float scale=CommandUI.Scale(Screen.width,Screen.height);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float w=Screen.width/scale,h=Screen.height/scale;CommandUI.Backdrop(w,h);tips.Begin();
            CommandUI.Eyebrow(new Rect(24,14,w-330,20),"Empire / Expedition");
            CommandUI.Label(new Rect(24,37,w-340,35),"REGIMENTAL GLORY",26,false,true);
            if(CommandUI.Button(new Rect(w-248,25,104,34),"Options"))showOptions=!showOptions;
            if(CommandUI.Button(new Rect(w-136,25,112,34),"Main menu")){OnClose?.Invoke();Destroy(this);return;}
            if(resetPrompt)
            {
                var prompt=new Rect((w-540)/2,(h-210)/2,540,210);EmpireTheme.Panel(prompt,EmpireTheme.Surface);
                CommandUI.Label(new Rect(prompt.x+24,prompt.y+22,492,35),"Start a new company?",24,false,true);
                CommandUI.Label(new Rect(prompt.x+24,prompt.y+66,492,55),"This replaces the current expedition with one full regiment and 240 coins.",15,true,false,true);
                if(CommandUI.Button(new Rect(prompt.x+24,prompt.y+146,238,40),"Start new company")){company=new GloryCompany();Store();resetPrompt=false;page=0;}
                if(CommandUI.Button(new Rect(prompt.x+274,prompt.y+146,242,40),"Keep current company"))resetPrompt=false;
                return;
            }
            if(showOptions){if(options.Draw(new Rect((w-800)/2,(h-680)/2,800,680)))showOptions=false;return;}
            if(company==null)
            {
                CommandUI.Label(new Rect(40,150,w-80,100),error,18,true,false,true);
                if(CommandUI.Button(new Rect(40,280,240,42),"Start a new company"))resetPrompt=true;return;
            }
            if(company.Save.lastBattle!=null&&(company.Save.summaryPending||showBattleSummary)){DrawBattleSummary(new Rect(24,86,w-48,h-110));tips.Draw(new Rect(0,0,w,h));return;}
            string[] tabs={"Company","Recruitment","Next battle","Honours"};
            for(int i=0;i<tabs.Length;i++)
            {
                var nav=new Rect(24+i*170,86,162,36);
                if(CommandUI.Button(nav,tabs[i],selected:page==i))page=i;
                var badge=new Rect(nav.x+8,nav.y+5,24,26);
                if(i==0)MilitaryInsignia.Rank(badge,3);else if(i==1)MilitaryInsignia.Tier(badge,2);else if(i==2)MilitaryInsignia.Pin(badge,3);else MilitaryInsignia.Medal(badge,0);
            }
            CommandUI.Label(new Rect(w-510,87,486,34),company.Save.victories+" victories  ·  "+CommandUI.Number(company.Save.coins)+" coins  ·  "+CommandUI.Number(company.Save.commanderXP)+" command XP",15,true);
            var panel=new Rect(24,136,w-48,h-208);
            if(page==0)management.Draw(panel,new GloryRegimentSource(company,Store));
            if(page==1)DrawRecruitment(panel);
            if(page==2)DrawScenarios(panel);
            if(page==3)DrawHonours(panel);
            if(CommandUI.Button(new Rect(24,h-55,154,32),"New company"))resetPrompt=true;
            CommandUI.Label(new Rect(198,h-59,w-222,44),error!=""?error:company.Save.report,12,true,false,true);
            tips.Draw(new Rect(0,0,w,h));
            if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){showOptions=true;Event.current.Use();}
        }
        void DrawRecruitment(Rect r)
        {
            EmpireTheme.Panel(r,EmpireTheme.Surface);
            CommandUI.Label(new Rect(r.x+24,r.y+16,r.width-48,38),"Recruit infantry, cavalry & artillery",26,false,true);
            CommandUI.Label(new Rect(r.x+24,r.y+58,r.width-48,36),company.Save.regiments.Count+" / "+GloryCompany.MaxRegiments+" regiments · higher tiers include better equipment; promote existing regiments through Quartermaster.",13,true);
            int columns=r.width>=1350?4:3;float cw=(r.width-64-(columns-1)*12)/columns;
            var viewport=new Rect(r.x+20,r.y+105,r.width-40,r.height-125);
            recruitmentScroll=GUI.BeginScrollView(viewport,recruitmentScroll,new Rect(0,0,r.width-56,Mathf.CeilToInt(GloryCompany.TierNames.Length/(float)columns)*246));
            for(int i=0;i<GloryCompany.TierNames.Length;i++)
            {
                int tier=i;var card=new Rect(i%columns*(cw+12),i/columns*246,cw,234);
                bool allowed=company.CanManage&&company.TierUnlocked(i)&&company.Save.coins>=GloryCompany.RecruitCosts[i]&&company.Save.regiments.Count<GloryCompany.MaxRegiments;
                string why=!company.CanManage?"Requires an active company.":!company.TierUnlocked(i)?"Requires "+CommandUI.Number(GloryCompany.UnlockXP[i])+" command XP.":company.Save.regiments.Count>=GloryCompany.MaxRegiments?"Company is full.":"Recruit "+GloryCompany.TierNames[i]+" for "+CommandUI.Number(GloryCompany.RecruitCosts[i])+" coins.";
                if(CommandUI.Button(card,"",allowed,hint:why,tips:tips)){Changed(company.Recruit(tier));management.Select(company.Save.regiments.Last().id.ToString());page=0;GUI.EndScrollView();return;}
                MilitaryInsignia.Tier(new Rect(card.x+14,card.y+12,44,51),i+1,company.TierUnlocked(i));
                CommandUI.Eyebrow(new Rect(card.x+70,card.y+14,cw-84,20),"Tier "+(i+1));
                CommandUI.Label(new Rect(card.x+70,card.y+36,cw-84,43),GloryCompany.TierNames[i],18,false,true,true);
                CommandUI.Label(new Rect(card.x+14,card.y+85,cw-28,24),(i>=10?"Artillery":UnitRoster.Label(GloryCompany.Roles[i]))+" · "+(i>=10?"4 guns":i>=GloryCompany.InfantryTierCount?"60 riders":"120 troops"),13,true);
                CommandUI.Label(new Rect(card.x+14,card.y+115,cw-28,45),GloryCompany.TierIssue(i),13,false,true,true);
                CommandUI.Label(new Rect(card.x+14,card.y+164,cw-28,24),company.TierUnlocked(i)?"Unlocked":CommandUI.Number(GloryCompany.UnlockXP[i])+" command XP to unlock",12,true);
                CommandUI.Label(new Rect(card.x+14,card.y+196,cw-28,28),"Recruit · "+CommandUI.Number(GloryCompany.RecruitCosts[i])+" coins",15,false,true);
            }
            GUI.EndScrollView();
        }
        void DrawScenarios(Rect r)
        {
            EmpireTheme.Panel(r,EmpireTheme.Surface);
            CommandUI.Label(new Rect(r.x+24,r.y+20,r.width-48,38),"Choose your next engagement",26,false,true);
            CommandUI.Label(new Rect(r.x+24,r.y+65,r.width-48,42),"Stage "+(company.Save.victories+1)+" · Company BP "+company.ReadyPower.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" · Battle Power challenges at 1:1, 1.5:1, 2:1, 4:1, 5:1 and 10:1; fixed mass hosts at 100:1, 200:1, 250:1 and 500:1, plus a rerollable 1,000:1–2,500:1 host.",14,true,false,true);
            float cw=Mathf.Max(280,(r.width-80)/5);int own=company.ReadyTroops;
            var viewport=new Rect(r.x+24,r.y+117,r.width-48,r.height-134);
            float cardHeight=viewport.height-20;
            scroll=GUI.BeginScrollView(viewport,scroll,new Rect(0,0,GloryEncounters.ChoiceCount*(cw+8)-8,cardHeight));
            foreach(var scenario in company.Choices())
            {
                var card=new Rect(scenario.choice*(cw+8),0,cw,cardHeight);
                var rerollRect=new Rect(card.x+18,card.yMax-47,cw-36,34);
                bool rerollHover=scenario.choice==GloryEncounters.VariableChoice&&rerollRect.Contains(Event.current.mousePosition);
                if(CommandUI.Button(card,"",company.CanDeploy&&!rerollHover,hint:scenario.enemyNation+" — "+scenario.objective+"\n"+scenario.brief+"\nClick to deploy. Conceding preserves survivors but grants no victory bounty.",tips:tips)){GUI.EndScrollView();Launch(scenario);return;}
                MilitaryInsignia.Pin(new Rect(card.x+12,card.y+10,26,28),scenario.choice+1);
                CommandUI.Eyebrow(new Rect(card.x+46,card.y+12,cw-60,24),scenario.archetype);
                float imageHeight=card.height>610?100:card.height>530?50:0;
                if(imageHeight>0)TacticalIllustration.Draw(new Rect(card.x+12,card.y+46,cw-24,imageHeight),scenario.terrainStyle,company.Save.regiments.Count(u=>u.soldiers>0),scenario.roles.Length,scenario.enemyColor);
                float y=card.y+imageHeight+50;
                CommandUI.Label(new Rect(card.x+18,y,cw-36,46),scenario.name,20,false,true,true);
                CommandUI.Label(new Rect(card.x+18,y+48,cw-36,23),scenario.enemyNation,15,false,true);
                CommandUI.Label(new Rect(card.x+18,y+74,cw-36,21),scenario.terrain,12,true);
                CommandUI.Label(new Rect(card.x+18,y+99,cw-36,38),scenario.objective,13,false,true,true);
                CommandUI.Label(new Rect(card.x+18,y+141,cw-36,43),scenario.composition,12,true,false,true);
                string ratio=own>0?(scenario.strengths.Sum()/(float)own).ToString("0.0")+":1 enemy / you":"No troops ready";
                CommandUI.Label(new Rect(card.x+18,y+210,cw-36,22),"BP "+scenario.enemyPower.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" vs "+scenario.playerPower.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" · "+(scenario.playerPower>0?scenario.enemyPower/scenario.playerPower:0).ToString("0.00")+"×",12,true);
                CommandUI.Label(new Rect(card.x+18,y+187,cw-36,22),scenario.strengths.Sum().ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" vs "+CommandUI.Number(own)+" troops · "+ratio,12,true);
                tips.Add(new Rect(card.x+18,y+141,cw-36,92),scenario.roles.Length+" enemy regiments\n"+scenario.composition+"\nEnemy XP: "+CommandUI.Number(scenario.enemyXP.Min())+"–"+CommandUI.Number(scenario.enemyXP.Max())+" per regiment. "+"Class, trained stats and carried equipment are included in BP."+"\n"+CommandUI.Number(scenario.strengths.Sum())+" vs "+CommandUI.Number(own)+" troops · "+ratio+"\n"+BattlePower.Help);
                float footer=card.yMax-95;
                CommandUI.Label(new Rect(card.x+18,y+237,cw-36,Mathf.Max(0,footer-y-243)),scenario.brief,12,true,false,true);
                CommandUI.Eyebrow(new Rect(card.x+18,footer,cw-36,20),scenario.bonusPercent>0?"Victory bounty · +"+scenario.bonusPercent+"%":"Victory bounty · standard");
                CommandUI.Label(new Rect(card.x+18,footer+23,cw-36,25),"+"+CommandUI.Number(scenario.coins)+" coins · +"+CommandUI.Number(scenario.xp)+" XP",16,false,true);
                tips.Add(new Rect(card.x+18,footer,cw-36,48),"Victory grants "+CommandUI.Number(scenario.coins)+" coins and "+CommandUI.Number(scenario.xp)+" command XP. Each surviving regiment also receives "+CommandUI.Number(scenario.xp)+" XP, plus combat experience.");
                if(scenario.choice==GloryEncounters.VariableChoice)
                {
                    if(CommandUI.Button(rerollRect,"Reroll · variable host",company.CanManage,tips:tips,hint:"Generate fresh odds and a new enemy army in this slot. The other engagements stay unchanged."))
                    {company.RerollEncounter();Store();GUI.EndScrollView();return;}
                }
                else CommandUI.Label(rerollRect,"Click this engagement to deploy",14,false,true);
            }
            GUI.EndScrollView();
        }
        void DrawHonours(Rect r)
        {
            EmpireTheme.Panel(r,EmpireTheme.Surface);
            CommandUI.Label(new Rect(r.x+24,r.y+20,r.width-290,38),"The company's honours",26,false,true);
            if(company.Save.lastBattle!=null&&CommandUI.Button(new Rect(r.xMax-245,r.y+22,221,34),"Last battle report"))showBattleSummary=true;
            CommandUI.Label(new Rect(r.x+24,r.y+65,r.width-48,43),"Earned through victories in this expedition. Decorations record achievement; they grant no combat bonuses.",14,true,false,true);
            float cw=(r.width-80)/5;
            for(int i=0;i<GloryHonours.Names.Length;i++)
            {
                var card=new Rect(r.x+24+i*(cw+8),r.y+124,cw,r.height-151);EmpireTheme.Panel(card,EmpireTheme.Ink);
                bool earned=company.Save.victories>=GloryHonours.Victories[i];
                MilitaryInsignia.Medal(new Rect(card.x+(cw-112)/2,card.y+23,112,128),i,earned);
                CommandUI.Eyebrow(new Rect(card.x+16,card.y+167,cw-32,22),earned?"Awarded":"Not yet earned");
                CommandUI.Label(new Rect(card.x+16,card.y+202,cw-32,68),GloryHonours.Names[i],20,false,true,true);
                CommandUI.Label(new Rect(card.x+16,card.y+276,cw-32,46),GloryHonours.Victories[i]+(GloryHonours.Victories[i]==1?" victory":" victories")+" in one expedition",13,true,false,true);
                CommandUI.Meter(new Rect(card.x+16,card.yMax-53,cw-32,5),Mathf.Clamp01(company.Save.victories/(float)GloryHonours.Victories[i]));
                CommandUI.Label(new Rect(card.x+16,card.yMax-39,cw-32,24),Math.Min(company.Save.victories,GloryHonours.Victories[i])+" / "+GloryHonours.Victories[i]+" victories",12,true);
                tips.Add(card,GloryHonours.Names[i]+"\n"+(earned?"Earned by this company.":"Win "+GloryHonours.Victories[i]+" battles in this expedition to earn this decoration."));
            }
        }
    }
    public sealed class GloryBattleBridge : MonoBehaviour
    {
        RegimentalGloryMode mode;GloryCompany company;GloryScenario scenario;BattleRuntime runtime;
        Scene origin,battle;GameObject[] roots;bool[] active;bool returning,committed;
        string error="";
        public static void Launch(RegimentalGloryMode mode,GloryCompany company,GloryScenario scenario,BattleConfig config)
        {
            var bridge=new GameObject("Regimental Glory battle transition").AddComponent<GloryBattleBridge>();
            bridge.mode=mode;bridge.company=company;bridge.scenario=scenario;bridge.origin=SceneManager.GetActiveScene();DontDestroyOnLoad(bridge.gameObject);
            bridge.roots=bridge.origin.GetRootGameObjects();bridge.active=bridge.roots.Select(r=>r.activeSelf).ToArray();
            foreach(var root in bridge.roots)root.SetActive(false);
            bridge.battle=SceneManager.CreateScene("Regimental Glory stage "+scenario.stage);SceneManager.SetActiveScene(bridge.battle);
            bridge.runtime=new GameObject("Company battle runtime").AddComponent<BattleRuntime>();bridge.runtime.Config=config;
            bridge.runtime.ReturnToCampaign=()=>bridge.Finish(false);
            bridge.runtime.ConcedeBattle=()=>bridge.Finish(true);
        }
        void Update()
        {
            if(!returning&&!committed&&runtime!=null&&runtime.World!=null&&runtime.World.Outcome!=BattleOutcome.Ongoing)Finish(false);
        }
        bool Finish(bool concede)
        {
            if(returning||runtime.World==null)return false;
            if(!committed){if(!company.Complete(scenario,runtime.World,concede,runtime.Artillery))return false;committed=true;runtime.SetPaused(true);}
            if(!mode.SaveResult()){error="Unable to save results. Retry returning to your company.";return false;}
            returning=true;runtime.enabled=false;StartCoroutine(Return());return true;
        }
        IEnumerator Return()
        {
            SceneManager.SetActiveScene(origin);for(int i=0;i<roots.Length;i++)if(roots[i])roots[i].SetActive(active[i]);
            yield return SceneManager.UnloadSceneAsync(battle);Destroy(gameObject);
        }
        void OnGUI()
        {
            if(returning||runtime==null||runtime.World==null)return;
            using var theme=new EmpireTheme.Scope(true);

            if(error!="")GUI.Label(new Rect(Screen.width-400,180,380,65),error);
        }
    }
}
