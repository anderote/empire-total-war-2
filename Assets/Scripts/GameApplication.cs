using System;
using System.IO;
using Empire.UI;
using EmpireBattle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmpireGame
{
    public sealed class GameApplication : MonoBehaviour
    {
        public static bool Enabled {get;private set;}
        public static string ScenarioId {get;private set;}="grand-battle";
        public static bool Available=>SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/MainMenu.unity")>=0;
        static bool customBattle;static int playerFaction,enemyFaction=1;
        string page="Home",selected="grand-battle";
        bool glory,quitPrompt,skirmish;
        Vector2 scenariosScroll;
        readonly OptionsPanel options=new OptionsPanel();readonly UITooltip tips=new UITooltip();
        GUIStyle navigation, navigationIndex, heroTitle, heroSubtitle;
        public static BattleConfig BattleConfiguration()=>customBattle?UnitRoster.CreateBattle(UnitRoster.Factions[playerFaction],UnitRoster.Factions[enemyFaction]):BattleScenarios.Create(ScenarioId);
        void Start()
        {
            Enabled=true;GamePreferences.Apply();
            var camera=gameObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=EmpireTheme.Ink;camera.cullingMask=0;
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++)if(args[i]=="--scenario")selected=BattleScenarios.Find(args[i+1]).Id;
            if(Array.IndexOf(args,"--campaign")>=0){SceneManager.LoadScene("Campaign");return;}
            if(Array.IndexOf(args,"--regimental-glory")>=0){OpenGlory();return;}
            if(Array.IndexOf(args,"--battle")>=0||Array.IndexOf(args,"--scenario")>=0||Array.IndexOf(args,"--benchmark-seconds")>=0)StartBattle();
        }
        public static void ReturnToMenu(){if(Available)SceneManager.LoadScene("MainMenu");}
        void StartBattle(){ScenarioId=selected;customBattle=skirmish;SceneManager.LoadScene("LineBattle");}
        public void OpenGlory(){glory=true;var mode=gameObject.AddComponent<RegimentalGloryMode>();mode.OnClose=()=>glory=false;}
        public void ReviewPage(string value){page=value;}
        void OnGUI()
        {
            if(glory)return;
            using var theme=new EmpireTheme.Scope(true);
            float scale=CommandUI.Scale(Screen.width,Screen.height);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float w=Screen.width/scale,h=Screen.height/scale;CommandUI.Backdrop(w,h);tips.Begin();
            EmpireTheme.Frame(new Rect(0,0,268,h),EmpireTheme.Surface);
            CommandUI.Eyebrow(new Rect(28,34,210,24),"The art of command");
            CommandUI.Label(new Rect(26,70,226,65),"EMPIRE",49,false,true);
            CommandUI.Label(new Rect(30,137,215,27),"T O T A L   W A R   2",13,true);
            EmpireTheme.Ornament(28,193,210);
            string[] pages={"Home","Campaign","Regimental Glory","Battles","Options"};
            if(navigation==null){navigation=EmpireTheme.Display(23,EmpireTheme.Ivory);navigationIndex=EmpireTheme.Text(11,EmpireTheme.Brass);}
            string[] folios={"I","II","III","IV","V"};
            for(int i=0;i<pages.Length;i++)
            {
                var row=new Rect(20,232+i*58,228,48);
                if(CommandUI.Button(row,"",selected:page==pages[i])){page=pages[i];GUI.FocusControl(null);}
                if(page==pages[i])EmpireTheme.Fill(new Rect(row.x,row.y,3,row.height),EmpireTheme.Brass);
                GUI.Label(new Rect(row.x+13,row.y,25,row.height),folios[i],navigationIndex);
                GUI.Label(new Rect(row.x+39,row.y,row.width-44,row.height),pages[i],navigation);
            }
            if(CommandUI.Button(new Rect(28,h-108,208,38),"Quit game"))quitPrompt=true;
            CommandUI.Label(new Rect(28,h-53,208,23),"NATIONS  ·  ARMIES  ·  AMBITION",10,true);
            var content=new Rect(302,36,w-338,h-72);
            if(quitPrompt)
            {
                var r=new Rect((w-500)/2,(h-210)/2,500,210);EmpireTheme.Panel(r,EmpireTheme.Ink);
                CommandUI.Label(new Rect(r.x+24,r.y+24,452,40),"Leave the game?",26,false,true);
                CommandUI.Label(new Rect(r.x+24,r.y+72,452,35),"Saved campaigns and companies will be here next time.",14,true);
                if(CommandUI.Button(new Rect(r.x+24,r.y+132,220,42),"Quit"))Application.Quit();
                if(CommandUI.Button(new Rect(r.x+256,r.y+132,220,42),"Keep playing"))quitPrompt=false;
            }
            else if(page=="Options"){if(options.Draw(new Rect(content.x,content.y,content.width,Mathf.Min(700,content.height))))page="Home";}
            else if(page=="Battles")DrawBattles(content);
            else if(page=="Home")DrawHome(content);
            else DrawMode(content,page=="Campaign");
            tips.Draw(new Rect(0,0,w,h));
            if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){if(quitPrompt)quitPrompt=false;else page="Home";Event.current.Use();}
        }
        void DrawHome(Rect r)
        {
            CommandUI.Eyebrow(new Rect(r.x,r.y,r.width,25),"Headquarters  /  The war room");
            EmpireTheme.Ornament(r.x,r.y+39,r.width);
            var atlas=new Rect(r.x,r.y+60,r.width,r.height-274);
            StrategicAtlasArt.Draw(atlas,false);
            // A solid ink cartouche protects the title from the map's varying relief.
            var cartouche=new Rect(atlas.x+26,atlas.y+28,Mathf.Min(480,atlas.width-140),atlas.height-56);
            EmpireTheme.Frame(cartouche,EmpireTheme.Ink);
            CommandUI.Eyebrow(new Rect(cartouche.x+26,cartouche.y+22,cartouche.width-52,22),"An age of revolution");
            if(heroTitle==null){heroTitle=EmpireTheme.Display(52,EmpireTheme.Ivory);heroTitle.wordWrap=true;heroTitle.alignment=TextAnchor.UpperLeft;heroSubtitle=EmpireTheme.Display(23,EmpireTheme.Muted,true);heroSubtitle.wordWrap=true;}
            GUI.Label(new Rect(cartouche.x+24,cartouche.y+65,cartouche.width-48,155),"The world at\nyour command.",heroTitle);
            EmpireTheme.Ornament(cartouche.x+26,cartouche.y+228,cartouche.width-52);
            GUI.Label(new Rect(cartouche.x+26,cartouche.y+250,cartouche.width-52,82),"Lead a nation. Forge an army.\nWrite your own history.",heroSubtitle);
            CommandUI.Eyebrow(new Rect(r.x,r.yMax-196,r.width,20),"Choose your command");
            string[] titles={"Campaign","Regimental Glory","Battles"};string[] captions={"Govern a nation. Lead its armies.","Earn your reputation, one battle at a time.","Scenarios and custom skirmishes."};
            float cw=(r.width-20)/3;
            for(int i=0;i<3;i++)
            {
                var card=new Rect(r.x+i*(cw+10),r.yMax-174,cw,156);
                if(CommandUI.Button(card,"",hint:captions[i],tips:tips))page=titles[i];
                CommandUI.Eyebrow(new Rect(card.x+16,card.y+14,cw-32,20),new[]{"I  /  THE STATE","II  /  THE REGIMENT","III  /  THE FIELD"}[i]);
                CommandUI.Label(new Rect(card.x+16,card.y+46,cw-32,51),titles[i],29,false,true,true);
                CommandUI.Label(new Rect(card.x+16,card.y+104,cw-32,39),captions[i],12,true,false,true);
            }
        }
        void DrawMode(Rect r,bool campaign)
        {
            CommandUI.Eyebrow(new Rect(r.x,r.y,r.width,25),campaign?"Grand strategy":"Persistent company");
            CommandUI.Label(new Rect(r.x,r.y+44,r.width,90),campaign?"The fate of a nation.":"From recruits to legends.",38,false,true,true);
            TacticalIllustration.Draw(new Rect(r.x,r.y+153,r.width,200),campaign?2:1);
            CommandUI.Label(new Rect(r.x,r.y+382,r.width,100),campaign?"Command Revolutionary France. Manage provinces, research new doctrine and equip your regiments before leading them into tactical battles.":"Begin with one regiment. Choose an enemy and terrain, win coins and XP, then replenish, recruit and specialize your company.",19,true,false,true);
            CommandUI.Label(new Rect(r.x,r.y+496,r.width,69),campaign?"Army management uses the same gear slots, stat allocation and ability screen as every other mode.":"Allocate base stats at every level. Grow through 50 levels, with ability points at every odd level. Gear is bought separately; casualties need paid replacements.",14,true,false,true);
            string label=campaign?"Open campaign":File.Exists(Path.Combine(Application.persistentDataPath,"regimental-glory.json"))?"Continue company":"Begin expedition";
            if(CommandUI.Button(new Rect(r.x,r.yMax-66,Mathf.Min(420,r.width),48),label,selected:true)){if(campaign)SceneManager.LoadScene("Campaign");else OpenGlory();}
        }
        void DrawBattles(Rect r)
        {
            CommandUI.Eyebrow(new Rect(r.x,r.y,r.width,25),"Tactical command");
            CommandUI.Label(new Rect(r.x,r.y+35,r.width,48),"Choose your battlefield",30,false,true);
            if(CommandUI.Button(new Rect(r.x,r.y+101,190,36),"Battle scenarios",selected:!skirmish))skirmish=false;
            if(CommandUI.Button(new Rect(r.x+200,r.y+101,190,36),"Custom skirmish",selected:skirmish))skirmish=true;
            if(skirmish)
            {
                TacticalIllustration.Draw(new Rect(r.x,r.y+158,r.width,175),0);
                CommandUI.Label(new Rect(r.x,r.y+359,r.width,44),"Choose armies",23,false,true);
                float cw=(r.width-20)/2;
                CommandUI.Eyebrow(new Rect(r.x,r.y+416,cw,22),"Your army");CommandUI.Eyebrow(new Rect(r.x+cw+20,r.y+416,cw,22),"Enemy army");
                for(int i=0;i<UnitRoster.Factions.Length;i++)
                {
                    float x=(i%2)*cw/2,y=r.y+447+(i/2)*36;
                    if(CommandUI.Button(new Rect(r.x+x,y,cw/2-6,30),UnitRoster.FactionNames[i],selected:playerFaction==i))playerFaction=i;
                    if(CommandUI.Button(new Rect(r.x+cw+20+x,y,cw/2-6,30),UnitRoster.FactionNames[i],selected:enemyFaction==i))enemyFaction=i;
                }
                if(CommandUI.Button(new Rect(r.x,r.yMax-51,r.width,43),"Prepare armies & equipment",selected:true))StartBattle();return;
            }
            float lw=230;
            var list=new Rect(r.x,r.y+162,lw,r.height-230);
            scenariosScroll=GUI.BeginScrollView(list,scenariosScroll,new Rect(0,0,lw-14,Mathf.Max(list.height-1,BattleScenarios.All.Length*66)));
            for(int i=0;i<BattleScenarios.All.Length;i++)
            {
                var scenario=BattleScenarios.All[i];if(CommandUI.Button(new Rect(0,i*66,lw-16,58),scenario.Name,selected:selected==scenario.Id))selected=scenario.Id;
            }
            GUI.EndScrollView();
            var chosen=BattleScenarios.Find(selected);float xdetail=r.x+lw+24,dw=r.width-lw-24;
            TacticalIllustration.Draw(new Rect(xdetail,r.y+162,dw,175),Array.FindIndex(BattleScenarios.All,s=>s.Id==selected)%3);
            CommandUI.Label(new Rect(xdetail,r.y+363,dw,84),chosen.Name,28,false,true,true);
            CommandUI.Label(new Rect(xdetail,r.y+464,dw,135),chosen.Description,16,true,false,true);
            if(CommandUI.Button(new Rect(xdetail,r.yMax-51,dw,43),"Prepare regiments & equipment",selected:true))StartBattle();
        }
    }
}
