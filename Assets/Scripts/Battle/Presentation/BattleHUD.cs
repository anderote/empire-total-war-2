using System;
using Empire.UI;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Battle interface; shared selection belongs to BattleView.</summary>
    public sealed partial class BattleHUD : MonoBehaviour
    {
        public static readonly Color Navy = EmpireTheme.Ink;
        public static readonly Color Charcoal = EmpireTheme.Surface;
        public static readonly Color Brass = EmpireTheme.Brass;
        public static readonly Color Cream = EmpireTheme.Ivory;
        public static readonly Color Muted = EmpireTheme.Muted;
        public static readonly Color FrenchBlue = EmpireTheme.FrenchBlue;
        public static readonly Color BritishRed = EmpireTheme.BritishRed;
        BattleWorld world;
        BattleView view;
        bool[] selected;
        Camera battleCamera;
        int[] frenchIds;
        string[] cardStrength, cardStatus;
        readonly System.Collections.Generic.List<int> infantryIndexes=new System.Collections.Generic.List<int>();
        readonly System.Collections.Generic.List<int> cavalryIndexes=new System.Collections.Generic.List<int>();
        readonly int[] rosterPages=new int[3];
        GUIStyle heading, label, small, number, button, cardTitle, condition, cardCount, enemyHeading, centeredNumber, centeredSmall, rightSmall;
        int playerAlive, enemyAlive;
        bool selectionShoots, selectionDrill;
        Texture2D disc;
        readonly System.Collections.Generic.List<int> batteryIds = new System.Collections.Generic.List<int>();
        int gunCount=-1;
        bool menuOpen,optionsOpen,optionsWasPaused;
        readonly OptionsPanel optionsPanel=new OptionsPanel();
        readonly UITooltip battleTips=new UITooltip();
        BattleAudio audio;
        float lastStatsTime=-100f;
        int lastSelectionHash;
        int rosterPlayer,rosterEnemy=1;
        int selectionCount, selectedAlive, selectedKills, selectedShots, selectedHits;
        int selectedCaptains, selectedSergeants;
        float selectedMorale, selectedHealth, selectedFatigue, selectedXP;
        string selectedName, selectedMetrics, selectedCondition, selectedCommand;
        string frenchStrength, britishStrength, clock;

        public void Initialize(BattleWorld value, BattleView battleView, bool[] selection, Camera camera)
        {
            world=value;view=battleView;selected=selection;battleCamera=camera;
            int count=0;
            foreach(var unit in world.Battalions) if(unit.Side==ArmySide.French) count++;
            frenchIds=new int[count];cardStrength=new string[count];cardStatus=new string[count];
            int index=0;
            foreach(var unit in world.Battalions)if(unit.Side==ArmySide.French)
            {
                frenchIds[index]=unit.Id;
                (unit.Kind==UnitKind.Cavalry?cavalryIndexes:infantryIndexes).Add(index);
                index++;
            }
            RefreshStats();
        }

        float UIScale => BattleUILayout.Scale(Screen.width,Screen.height);
        float Width => Screen.width/UIScale;
        float Height => Screen.height/UIScale;
        Rect HeaderRect => BattleUILayout.Header(Width);
        Rect RosterRect => BattleUILayout.Roster(Width,Height);
        Rect DetailRect => BattleUILayout.Detail(Width,Height);
        bool fireModePicker;
        Rect FireModeRect=>new Rect(DetailRect.x+132,DetailRect.y-112,190,108);
        Rect MenuRect => BattleUILayout.Menu(Width);
        Rect ResultRect => new Rect((Width-520)/2,106,520,104);
        public Rect StatusRect
        {
            get
            {
                float y=selectionCount>0?DetailRect.y:RosterRect.y-42;
                if(view!=null&&view.SelectedGunCount>0)y=Mathf.Min(y,view.BatteryOrdersRect.y/UIScale);
                return BattleUILayout.Physical(new Rect(24,y-27,Width-48,22),UIScale);
            }
        }
        public bool IsPointerOverUI(Vector2 guiMouse)
        {
            if(world==null)return false;
            if(optionsOpen||GetComponent<BattleArmory>()?.IsOpen==true)return true;
            Vector2 logical=guiMouse/UIScale;
            return (world.Outcome!=BattleOutcome.Ongoing&&ResultRect.Contains(logical))||(fireModePicker&&FireModeRect.Contains(logical))||HeaderRect.Contains(logical)||RosterRect.Contains(logical)||BattleUILayout.MenuButton.Contains(logical)
                ||((selectionCount>0||view.SelectedGunCount>0)&&DetailRect.Contains(logical))||(menuOpen&&MenuRect.Contains(logical))
                ||(audio&&audio.ShowAudioCredits&&audio.AudioCreditsRect.Contains(guiMouse));
        }

        public static void Fill(Rect rect,Color color) => EmpireTheme.Fill(rect,color);
        public static void Panel(Rect rect,Color color) => EmpireTheme.Panel(rect,color);
        public static GUIStyle TextStyle(int size,Color color,FontStyle weight=FontStyle.Normal) => EmpireTheme.Text(size,color,weight);
        public static GUIStyle ButtonStyle(int size=12) => new GUIStyle(EmpireTheme.ButtonStyle(size)) { padding = new RectOffset(6,6,4,4) };
        public static bool ThemedButton(Rect rect,string text,GUIStyle style,bool active=false) => EmpireTheme.Button(rect,new GUIContent(text,BattleCommandHints.For(text)),style,active);

        void EnsureStyles()
        {
            if(heading!=null)return;
            heading=EmpireTheme.Display(18,Cream);label=TextStyle(14,Cream);
            small=TextStyle(12,Muted);number=TextStyle(22,Cream,FontStyle.Bold);
            button=ButtonStyle(13);cardTitle=TextStyle(13,Cream,FontStyle.Bold);
            enemyHeading=new GUIStyle(heading){alignment=TextAnchor.MiddleRight};
            centeredNumber=new GUIStyle(number){alignment=TextAnchor.MiddleCenter};
            rightSmall=new GUIStyle(small){alignment=TextAnchor.MiddleRight};
            centeredSmall=new GUIStyle(small){alignment=TextAnchor.MiddleCenter};
            condition=new GUIStyle(small){wordWrap=true};
            cardCount=new GUIStyle(TextStyle(20,Cream,FontStyle.Bold)){alignment=TextAnchor.MiddleRight};
            cardTitle.fontSize=12;
            disc=new Texture2D(32,32,TextureFormat.RGBA32,false){name="Battle insignia",hideFlags=HideFlags.HideAndDontSave};
            var pixels=new Color[1024];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
                pixels[y*32+x]=new Color(1,1,1,Mathf.Clamp01(16-Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(16,16))));
            disc.SetPixels(pixels);disc.Apply(false,true);
        }

        void RefreshStats()
        {
            if(world==null)return;
            selectionCount=selectedAlive=selectedKills=selectedShots=selectedHits=0;
            selectedCaptains=selectedSergeants=0;
            selectedMorale=selectedHealth=selectedFatigue=selectedXP=0;
            float remainingAmmunition=0,startingAmmunition=0;bool hasAmmunition=false;
            string reason="";selectionShoots=selectionDrill=true;
            foreach(var unit in world.Battalions)
            {
                if(!selected[unit.Id])continue;
                selectionShoots&=UnitRoster.HasFirearm(unit);
                selectionDrill&=world.CanFireMode(unit,InfantryFireMode.ByPlatoon);
                selectionCount++;selectedAlive+=unit.AliveCount;selectedKills+=unit.Kills;
                selectedMorale+=unit.Morale;selectedFatigue+=unit.Fatigue;selectedXP+=unit.Experience;
                if(UnitRoster.HasFirearm(unit))
                {
                    hasAmmunition=true;
                    // Weight the selection by surviving soldiers and their initial supply.
                    float capacity=unit.AliveCount*(float)Mathf.Max(0,unit.StartingAmmunition);
                    startingAmmunition+=capacity;
                    remainingAmmunition+=view.RegimentAmmunitionFraction(unit)*capacity;
                }
                selectedShots+=unit.ShotsFired;selectedHits+=unit.Hits;selectedName=unit.Name;reason=UnitRoster.Label(unit.Role)+" · "+(UnitRoster.HasFirearm(unit)?unit.Weapon?.Name??"Musket":unit.Kind==UnitKind.Cavalry?"Sabre":"Sword")+" · "+unit.Formation+" · "+FormationMath.RankCount(unit)+" ranks · "+(RifleFireTiming.IndependentFire(unit)?"Fire at will":RifleFireTiming.EffectiveMode(unit)==InfantryFireMode.ByPlatoon?"Platoon fire by rank":"Volley");
                if(unit.BattleGasExposure>0)reason+=$" · Gas: −{(1-unit.GasMovementMultiplier)*100:0}% speed (battle)";
                if(unit.BattleLimeExposure>0)reason+=$" · Lime: −{Mathf.Clamp01(unit.BattleLimeExposure)*60:0}% aim (battle)";
                for(int soldier=unit.SoldierStart;soldier<unit.SoldierStart+unit.SoldierCount;soldier++)
                    if(world.Soldiers.Alive[soldier]){selectedHealth+=world.Soldiers.Health[soldier]/unit.MaxHealth*100f;if(world.Soldiers.Captains[soldier])selectedCaptains++;if(world.Soldiers.PikeSergeants[soldier])selectedSergeants++;}
            }
            if(selectionCount>0)
            {
                selectedMorale/=selectionCount;selectedFatigue/=selectionCount;selectedXP/=selectionCount;
                selectedHealth=selectedAlive>0?selectedHealth/selectedAlive:0;
                selectedName=selectionCount>1?selectionCount+" REGIMENTS SELECTED":selectedName.ToUpperInvariant();
                string ammunition=hasAmmunition?$"{(startingAmmunition>0?remainingAmmunition/startingAmmunition*100:0):0}%":"N/A";
                selectedMetrics=$"{selectedAlive:N0} men   ·   Health {selectedHealth:0}%   ·   Morale {selectedMorale:0}   ·   Fatigue {selectedFatigue:0}%   ·   Ammo {ammunition}   ·   Kills {selectedKills:N0}";
                selectedCondition=selectionCount==1?reason:$"Fatigue {selectedFatigue:0}%   ·   Experience {selectedXP:0.0}";
                selectedCommand=selectedCaptains+selectedSergeants>0?$"Command: {selectedCaptains} captains · {selectedSergeants} pike sergeants":"";
            }
            playerAlive=world.CountAlive(ArmySide.French);enemyAlive=world.CountAlive(ArmySide.British);
            frenchStrength=playerAlive.ToString("N0");britishStrength=enemyAlive.ToString("N0");
            int seconds=Mathf.FloorToInt(world.Time);clock=$"{seconds/60:00}:{seconds%60:00}";
            for(int index=0;index<frenchIds.Length;index++)
            {
                var unit=world.Battalions[frenchIds[index]];
                cardStrength[index]=(unit.InReserve?unit.SoldierCount:unit.AliveCount).ToString();
                cardStatus[index]=unit.InReserve?"RESERVE / "+Mathf.CeilToInt(Mathf.Max(0,unit.ArrivalTime-world.Time))+"s":unit.AliveCount==0?(unit.EscapedCount>0?"ESCAPED":"LOST"):unit.Routing?"ROUT":world.ContactMovement.Gathering[unit.Id]?"GATHER":world.ContactMovement.Pursuing[unit.Id]?"PURSUE":unit.ChargePhase==ChargePhase.Recovering?"REFORM":unit.ChargePhase==ChargePhase.Approach?"CHARGE":unit.InMelee?"MELEE":unit.Moving?unit.Kind==UnitKind.Cavalry?unit.Running?"GALLOP":unit.Trotting?"TROT":"WALK":unit.Running?"RUN":"MARCH":unit.FireCycleActive?"FIRING":unit.Reforming?"FORMING":unit.ReloadRemaining>0?"RELOAD":unit.FireAtWill||!UnitRoster.HasFirearm(unit)?"READY":"HOLD";
            }
            lastStatsTime=Time.unscaledTime;
        }

        void KeepSelectionVisible()
        {
            for(int group=0;group<3;group++)
            {
                int count=group==0?infantryIndexes.Count:group==1?cavalryIndexes.Count:batteryIds.Count;
                bool visible=false;int candidatePage=-1;
                for(int item=0;item<count;item++)
                {
                    bool active;
                    if(group==0)active=selected[frenchIds[infantryIndexes[item]]];
                    else if(group==1)active=selected[frenchIds[cavalryIndexes[item]]];
                    else active=view.IsGunSelected(batteryIds[item]);
                    if(!active)continue;
                    int page=item/4;
                    if(page==rosterPages[group])visible=true;
                    else if(candidatePage<0)candidatePage=page;
                }
                if(!visible&&candidatePage>=0)rosterPages[group]=candidatePage;
            }
        }

        void OnGUI()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.HUD);
            if(world==null||view==null)return;
            if(GetComponent<BattleArmory>()?.IsOpen==true)return;
            using var theme = new EmpireTheme.Scope(true);
            EnsureStyles();battleTips.Begin();
            if(optionsOpen)
            {
                float scale=CommandUI.Scale(Screen.width,Screen.height);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
                float w=Screen.width/scale,h=Screen.height/scale;EmpireTheme.Fill(new Rect(0,0,w,h),new Color(.02f,.03f,.04f,.94f));
                if(optionsPanel.Draw(new Rect((w-820)/2,(h-700)/2,820,700)))CloseOptions();return;
            }
            if(gunCount!=view.CommandGuns.Length)
            {
                gunCount=view.CommandGuns.Length;batteryIds.Clear();int previous=-1;
                foreach(var gun in view.CommandGuns)
                    if(gun.Side==ArmySide.French&&gun.UnitId!=previous){batteryIds.Add(gun.Id);previous=gun.UnitId;}
            }
            int hash=17;foreach(bool value in selected)hash=unchecked(hash*31+(value?1:0));
            foreach(int id in batteryIds)hash=unchecked(hash*31+(view.IsGunSelected(id)?1:0));
            if(hash!=lastSelectionHash)
            {
                KeepSelectionVisible();
                lastSelectionHash=hash;RefreshStats();
            }
            else if(Time.unscaledTime-lastStatsTime>=.25f)RefreshStats();
            Matrix4x4 previousMatrix=GUI.matrix;
            try
            {
                GUI.matrix=previousMatrix*Matrix4x4.Scale(new Vector3(UIScale,UIScale,1));
                DrawHeader();
            if(world.Config.CampaignDeployment&&(world.ReserveTroops(ArmySide.French)>0||world.ReserveTroops(ArmySide.British)>0))
                GUI.Label(new Rect(20,84,650,22),"Reinforcements waiting: "+world.ReserveTroops(ArmySide.French).ToString("N0")+" friendly / "+world.ReserveTroops(ArmySide.British).ToString("N0")+" enemy · 20,000 troops on the field at once",small);
                DrawRoster();if(selectionCount>0)DrawSelection();if(menuOpen)DrawMenu();DrawBattleResult();battleTips.Draw(new Rect(0,0,Width,Height),true);
            }
            finally{GUI.matrix=previousMatrix;}
        }

        void DrawBattleResult()
        {
            if(world.Outcome==BattleOutcome.Ongoing)return;
            var rect=ResultRect;FramedPanel(rect,Navy);
            string message=world.Outcome==BattleOutcome.FrenchVictory?"You've defeated "+world.Config.EnemyCountryName+".":
                world.Outcome==BattleOutcome.BritishVictory?"Your army has been defeated.":"The battle ended in a draw.";
            GUI.Label(new Rect(rect.x+16,rect.y+10,rect.width-32,24),message,heading);
            GUI.Label(new Rect(rect.x+16,rect.y+37,rect.width-32,20),"The battle continues. Leave when you're ready.",small);
            bool previous=GUI.enabled;GUI.enabled=previous&&view.EnableInput;
            if(ThemedButton(new Rect(rect.x+16,rect.y+66,rect.width-32,28),world.Config.RegimentalDeployment?"BACK TO COMPANY":world.Config.CampaignDeployment?"BACK TO CAMPAIGN":"BACK TO MENU",button))view.OnExitBattle?.Invoke();
            GUI.enabled=previous;
        }

        void FramedPanel(Rect rect,Color color)
        {
            EmpireTheme.Frame(rect,color);
        }

        void DrawHeader()
        {
            Rect rect=HeaderRect;FramedPanel(rect,Navy);
            Dot(new Rect(rect.x+14,rect.y+13,32,32),Brass);
            Dot(new Rect(rect.x+17,rect.y+16,26,26),world.Config.PlayerCountryColor);
            Dot(new Rect(rect.xMax-46,rect.y+13,32,32),Brass);
            Dot(new Rect(rect.xMax-43,rect.y+16,26,26),world.Config.EnemyCountryColor);
            GUI.Label(new Rect(rect.x+56,rect.y+10,280,22),world.Config.PlayerCountryName.ToUpperInvariant()+"  ·  "+frenchStrength,heading);
            GUI.Label(new Rect(rect.xMax-336,rect.y+10,280,22),world.Config.EnemyCountryName.ToUpperInvariant()+"  ·  "+britishStrength,enemyHeading);
            GUI.Label(new Rect(rect.center.x-65,rect.y+6,130,28),clock,centeredNumber);
            Rect balance=new Rect(rect.x+56,rect.y+37,rect.width-112,6);
            Fill(balance,world.Config.EnemyCountryColor);
            float ratio=playerAlive+enemyAlive>0?playerAlive/(float)(playerAlive+enemyAlive):.5f;
            Fill(new Rect(balance.x,balance.y,balance.width*ratio,balance.height),world.Config.PlayerCountryColor);
            Fill(new Rect(balance.x+balance.width*ratio-1,balance.y-2,2,10),Cream);
            bool paused=view.IsPaused!=null&&view.IsPaused();
            bool old=GUI.enabled;GUI.enabled=old&&view.EnableInput;
            float controlX=rect.x+14,y=rect.y+53;
            if(ThemedButton(new Rect(controlX,y,78,25),paused?"RESUME":"PAUSE",button,paused))view.OnTogglePause?.Invoke();
            float speed=view.GetSpeed!=null?view.GetSpeed():1f;controlX+=84;
            foreach(float value in BattleRuntime.PlaybackSpeeds)
            {
                if(ThemedButton(new Rect(controlX,y,44,25),value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"×",button,!paused&&Mathf.Approximately(speed,value)))view.OnSetSpeed?.Invoke(value);
                controlX+=48;
            }
            if(view.Cinematics)
            {
                if(ThemedButton(new Rect(controlX,y,74,25),"CINEMA",button,view.Cinematics.CinemaEnabled))view.Cinematics.CinemaEnabled=!view.Cinematics.CinemaEnabled;
                GUI.enabled=old&&view.EnableInput&&view.Cinematics.CinemaEnabled;
                if(ThemedButton(new Rect(controlX+80,y,65,25),"FOCUS",button,view.Cinematics.FocusEnabled))view.Cinematics.FocusEnabled=!view.Cinematics.FocusEnabled;
                GUI.enabled=old&&view.EnableInput;
            }
            string state=world.Outcome==BattleOutcome.Ongoing?(world.DeploymentActive?"DEPLOYMENT":paused?"PAUSED":"BATTLE IN PROGRESS"):
                world.Outcome==BattleOutcome.FrenchVictory?"VICTORY":world.Outcome==BattleOutcome.BritishVictory?"DEFEAT":"DRAW";
            if(ThemedButton(new Rect(rect.xMax-406,y,120,25),"REGIMENTS",button)){var manager=GetComponent<BattleArmory>();if(manager){for(int i=0;i<selected.Length;i++)if(selected[i]){manager.SelectEntry(i);break;}manager.Open();}}
            GUI.Label(new Rect(rect.xMax-275,y,180,25),state,small);
            if(ThemedButton(BattleUILayout.MenuButton,"MENU",button,menuOpen))menuOpen=!menuOpen;
            GUI.enabled=old;
        }

        public int HoveredRegimentAt(Vector2 guiMouse)
        {
            if(world==null||menuOpen||frenchIds==null)return -1;
            Vector2 logical=guiMouse/UIScale;
            for(int group=0;group<2;group++)
            {
                var indexes=group==0?infantryIndexes:cavalryIndexes;
                int start=rosterPages[group]*4;
                Rect lane=BattleUILayout.RosterColumn(Width,Height,group);
                for(int slot=0;slot<4&&start+slot<indexes.Count;slot++)
                    if(BattleUILayout.RosterGroupCard(lane,slot).Contains(logical))return frenchIds[indexes[start+slot]];
            }
            return -1;
        }

        void DrawRoster()
        {
            Rect rect=RosterRect;CommandPanel(rect);
            GUI.Label(new Rect(rect.x+14,rect.y+6,rect.width-28,23),world.Config.PlayerCountryName.ToUpperInvariant()+"  ·  ORDER OF BATTLE",heading);
            Fill(new Rect(rect.x+14,rect.y+31,rect.width-28,1),EmpireTheme.Line);
            int infantryCount=infantryIndexes.Count,cavalryCount=cavalryIndexes.Count,artilleryCount=batteryIds.Count;
            for(int group=0;group<3;group++)
            {
                Rect lane=BattleUILayout.RosterColumn(Width,Height,group);
                if(group>0)Fill(new Rect(lane.x-8,rect.y+39,1,rect.height-52),EmpireTheme.Line);
                string title=group==0?"INFANTRY":group==1?"CAVALRY":"ARTILLERY";
                int count=group==0?infantryCount:group==1?cavalryCount:artilleryCount;
                DrawRosterGroup(lane,group,title,count);
            }
        }

        void DrawRosterGroup(Rect lane,int group,string title,int count)
        {
            Panel(lane,new Color(.035f,.05f,.07f,.80f));
            Fill(new Rect(lane.x,lane.y,lane.width,1),EmpireTheme.Line);
            int pages=BattleUILayout.RosterGroupPages(count);
            rosterPages[group]=Mathf.Clamp(rosterPages[group],0,pages-1);
            GUI.Label(new Rect(lane.x+6,lane.y+2,lane.width-74,18),title+" · "+count,small);
            GUI.Label(new Rect(lane.xMax-63,lane.y+2,27,18),(rosterPages[group]+1)+"/"+pages,centeredSmall);
            bool old=GUI.enabled;GUI.enabled=old&&view.EnableInput&&rosterPages[group]>0;
            if(ThemedButton(new Rect(lane.xMax-34,lane.y+1,15,19),"‹",button))rosterPages[group]--;
            GUI.enabled=old&&view.EnableInput&&rosterPages[group]<pages-1;
            if(ThemedButton(new Rect(lane.xMax-18,lane.y+1,15,19),"›",button))rosterPages[group]++;
            GUI.enabled=old;
            int start=rosterPages[group]*4;
            for(int slot=0;slot<4&&start+slot<count;slot++)
            {
                Rect card=BattleUILayout.RosterGroupCard(lane,slot);
                if(group==0)DrawRegimentCard(card,frenchIds[infantryIndexes[start+slot]]);
                else if(group==1)DrawRegimentCard(card,frenchIds[cavalryIndexes[start+slot]]);
                else DrawBatteryCard(card,batteryIds[start+slot],start+slot+1);
            }
        }

        void CommandPanel(Rect rect)
        {
            Fill(new Rect(rect.x+3,rect.y+4,rect.width,rect.height),new Color(0,0,0,.3f));
            Panel(rect,Navy);
            Fill(new Rect(rect.x+1,rect.y+1,rect.width-2,1),Brass);
        }
        void CardFrame(Rect card,bool picked,bool danger,bool painted=false)
        {
            bool hover=card.Contains(Event.current.mousePosition);
            if(!painted)Panel(card,picked?EmpireTheme.Selected:hover?EmpireTheme.Hover:Charcoal);
            Color edge=picked?Brass:danger?EmpireTheme.Danger:hover?Cream:EmpireTheme.Line;
            Fill(new Rect(card.x,card.y,card.width,1),edge);
            Fill(new Rect(card.x,card.y,2,card.height),edge);
            Fill(new Rect(card.x+8,card.y+31,card.width-16,1),EmpireTheme.Line);
            if(picked)Fill(new Rect(card.x,card.yMax-2,card.width,2),Brass);
        }
        void DrawUnitSymbol(Rect card,UnitRole role,bool artillery)
        {
            // Standard military map symbols keep every card legible at HUD scale.
            Rect badge=new Rect(card.x+8,card.y+9,22,14);
            Color color=Brass;
            Fill(new Rect(badge.x,badge.y,badge.width,1),color);
            Fill(new Rect(badge.x,badge.yMax,badge.width,1),color);
            Fill(new Rect(badge.x,badge.y,1,badge.height),color);
            Fill(new Rect(badge.xMax,badge.y,1,badge.height+1),color);
            if(artillery)Dot(new Rect(badge.center.x-3,badge.center.y-3,6,6),color);
            else
            {
                Stroke(new Vector2(badge.x+3,badge.y+3),new Vector2(badge.xMax-3,badge.yMax-3),1,color);
                if(role!=UnitRole.Cavalry)Stroke(new Vector2(badge.x+3,badge.yMax-3),new Vector2(badge.xMax-3,badge.y+3),1,color);
            }
        }
        void DrawRegimentCard(Rect card,int index)
        {
            var unit=world.Battalions[frenchIds[index]];bool picked=selected[unit.Id];
            bool painted=BattleCardArt.Draw(card,unit);
            CardFrame(card,picked,unit.Routing,painted);
            if(!painted)DrawUnitSymbol(card,unit.Role,false);
            GUI.Label(new Rect(card.x+6,card.y+3,card.width-12,18),cardStrength[index]+" men",cardCount);
            GUI.Label(new Rect(card.x+7,card.y+30,card.width-14,15),(index+1).ToString("00")+" · "+ShortRole(unit.Role),cardTitle);
            GUI.Label(new Rect(card.x+7,card.y+46,card.width-14,14),cardStatus[index],small);
            Meter(new Rect(card.x+6,card.yMax-10,card.width-12,3),unit.AliveCount/(float)Mathf.Max(1,unit.SoldierCount),world.Config.PlayerCountryColor);
            Meter(new Rect(card.x+6,card.yMax-5,card.width-12,3),unit.Morale/100f,unit.Morale<40?EmpireTheme.Danger:Brass);
            bool old=GUI.enabled;GUI.enabled=old&&view.EnableInput&&unit.AliveCount>0;
            if(GUI.enabled&&Event.current.type==EventType.MouseDown&&Event.current.button==0&&Event.current.clickCount==2&&card.Contains(Event.current.mousePosition))
            {view.SelectUnit(unit.Id,false);view.FocusUnit(unit.Id);Event.current.Use();}
            if(GUI.Button(card,"",GUIStyle.none))view.SelectUnit(unit.Id,Event.current.control||Event.current.command||Event.current.shift);
            GUI.enabled=old;
        }
        void DrawBatteryCard(Rect card,int id,int index)
        {
            var gun=view.CommandGuns[id];bool picked=view.IsGunSelected(id);
            int guns=0,operational=0,ammo=0;float morale=0;
            foreach(var member in view.CommandGuns)if(member.UnitId==gun.UnitId){guns++;if(member.Operational)operational++;ammo+=member.Ammunition;morale+=member.Morale;}
            CardFrame(card,picked,morale/Mathf.Max(1,guns)<25);
            DrawUnitSymbol(card,UnitRole.Line,true);
            GUI.Label(new Rect(card.x+6,card.y+3,card.width-12,18),operational+"/"+guns+" guns",cardCount);
            GUI.Label(new Rect(card.x+7,card.y+30,card.width-14,15),index.ToString("00")+" · Guns",cardTitle);
            GUI.Label(new Rect(card.x+7,card.y+46,card.width-14,14),ammo+" rounds",small);
            Meter(new Rect(card.x+6,card.yMax-5,card.width-12,3),morale/Mathf.Max(1,guns)/100f,Brass);
            bool old=GUI.enabled;GUI.enabled=old&&view.EnableInput;
            if(GUI.enabled&&Event.current.type==EventType.MouseDown&&Event.current.button==0&&Event.current.clickCount==2&&card.Contains(Event.current.mousePosition))
            {view.SelectGun(id,false);view.FocusGun(id);Event.current.Use();}
            if(GUI.Button(card,"",GUIStyle.none))view.SelectGun(id,Event.current.control||Event.current.command||Event.current.shift);
            GUI.enabled=old;
        }

        void DrawSelection()
        {
            Rect rect=DetailRect;CommandPanel(rect);
            float x=rect.x+12,y=rect.y+50;
            GUI.Label(new Rect(x,rect.y+6,rect.width-24,20),selectedName,cardTitle);
            GUI.Label(new Rect(x,rect.y+27,rect.width-24,18),selectedMetrics,small);
            const float step=120,wide=114;
            var closedFormation=view.SelectionCanForm(FormationKind.Diamond)?FormationKind.Diamond:FormationKind.Square;
            OrderButton(new Rect(x,y,wide,24),"LINE",()=>view.OrderFormation(FormationKind.Line),view.SelectionCanForm(FormationKind.Line),AllFormation(FormationKind.Line));
            OrderButton(new Rect(x+step,y,wide,24),"COLUMN",()=>view.OrderFormation(FormationKind.Column),view.SelectionCanForm(FormationKind.Column),AllFormation(FormationKind.Column));
            OrderButton(new Rect(x+step*2,y,wide,24),"SKIRMISH",()=>view.OrderFormation(FormationKind.Skirmish),view.SelectionCanForm(FormationKind.Skirmish),AllFormation(FormationKind.Skirmish));
            OrderButton(new Rect(x+step*3,y,wide,24),closedFormation==FormationKind.Diamond?"DIAMOND":"SQUARE",()=>view.OrderFormation(closedFormation),view.SelectionCanForm(closedFormation),AllFormation(closedFormation));
            OrderButton(new Rect(x+step*4,y,wide,24),"RANK −",()=>view.OrderAdjustRanks(-1));
            OrderButton(new Rect(x+step*5,y,wide,24),"RANK +",()=>view.OrderAdjustRanks(1));
            OrderButton(new Rect(x+step*6,y,wide,24),"WITHDRAW",()=>view.OrderWithdraw());
            y+=28;
            var currentFireMode=view.SelectedFireMode;
            string fireModeLabel=currentFireMode==InfantryFireMode.FireAtWill?"AT WILL ▾":
                currentFireMode==InfantryFireMode.ByPlatoon?"PLATOONS ▾":
                currentFireMode==InfantryFireMode.Volley?"VOLLEY ▾":selectionDrill?"MIXED ▾":"FIRE MODE ▾";
            OrderButton(new Rect(x,y,wide,24),"FIRE",()=>view.OrderToggleFire(),selectionShoots,AllFire());
            OrderButton(new Rect(x+step,y,wide,24),fireModeLabel,()=>fireModePicker=!fireModePicker,selectionDrill,fireModePicker);
            OrderButton(new Rect(x+step*2,y,wide,24),"RUN",()=>view.OrderToggleRun(),true,AllRun());
            OrderButton(new Rect(x+step*3,y,wide,24),"HALT",()=>view.OrderHalt());
            OrderButton(new Rect(x+step*4,y,wide,24),"MELEE · F",()=>view.OrderToggleMelee(),true,view.SelectedMeleeMode);
            OrderButton(new Rect(x+step*5,y,wide,24),"PURSUE",()=>view.OrderTogglePursuit(),true,view.SelectedPursuitEnabled);
            if(view.SelectionCanFixBayonets())
                OrderButton(new Rect(x+step*6,y,wide,24),view.SelectedBayonetsFixed()?"UNFIX BAYONETS":"FIX BAYONETS",()=>view.OrderToggleBayonets(),true,view.SelectedBayonetsFixed());
            else OrderButton(new Rect(x+step*6,y,wide,24),"TROT / WALK · T",()=>view.OrderToggleTrot(),view.SelectionHasCavalry());
            if(fireModePicker)
            {
                var picker=FireModeRect;FramedPanel(picker,Navy);
                var modes=new[]{InfantryFireMode.FireAtWill,InfantryFireMode.ByPlatoon,InfantryFireMode.Volley};
                var labels=new[]{"FIRE AT WILL","FIRE BY PLATOONS","VOLLEY"};
                for(int i=0;i<modes.Length;i++){int choice=i;OrderButton(new Rect(picker.x+8,picker.y+8+i*32,picker.width-16,28),labels[i],()=>{view.OrderFireMode(modes[choice]);fireModePicker=false;},selectionDrill,currentFireMode==modes[i]);}
            }
        }

        bool AllFormation(FormationKind kind){foreach(var unit in world.Battalions)if(selected[unit.Id]&&unit.Formation!=kind)return false;return selectionCount>0;}
        bool AllRun(){foreach(var unit in world.Battalions)if(selected[unit.Id]&&!unit.Running)return false;return selectionCount>0;}
        bool AllFire(){foreach(var unit in world.Battalions)if(selected[unit.Id]&&!unit.FireAtWill)return false;return selectionCount>0;}
        void OrderButton(Rect rect,string text,Action action,bool allowed=true,bool active=false)
        {
            battleTips.Add(rect,BattleCommandHints.For(text));
            bool old=GUI.enabled;GUI.enabled=old&&view.EnableInput&&allowed;
            if(ThemedButton(rect,text,button,active))action();GUI.enabled=old;
        }
        void Meter(Rect rect,float value,Color color)
        {Fill(rect,EmpireTheme.Raised);Fill(new Rect(rect.x,rect.y,rect.width*Mathf.Clamp01(value),rect.height),color);}
        void LabeledMeter(Rect rect,string title,float value,Color color)
        {GUI.Label(new Rect(rect.x,rect.y,rect.width,16),title+"  "+(value*100).ToString("0")+"%",small);Meter(new Rect(rect.x,rect.y+17,rect.width,3),value,color);}
        static string ShortRole(UnitRole role) => role==UnitRole.Line?"Line":role==UnitRole.Skirmisher?"Skirmishers":role==UnitRole.Rifleman?"Riflemen":role.ToString();
        void Dot(Rect rect,Color color)
        {Color previous=GUI.color;GUI.color=EmpireTheme.RenderColor(color);GUI.DrawTexture(rect,disc);GUI.color=previous;}
        void Stroke(Vector2 from,Vector2 to,float width,Color color)
        {
            Matrix4x4 previous=GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y-from.y,to.x-from.x)*Mathf.Rad2Deg,from);
            Fill(new Rect(from.x,from.y-width*.5f,Vector2.Distance(from,to),width),color);GUI.matrix=previous;
        }
        void OnDestroy(){if(disc)Destroy(disc);if(mapTexture)Destroy(mapTexture);}

        void OpenOptions()
        {
            optionsWasPaused=view.IsPaused?.Invoke()??false;if(!optionsWasPaused)view.OnTogglePause?.Invoke();view.EnableInput=false;optionsOpen=true;menuOpen=false;
        }
        void CloseOptions(){optionsOpen=false;view.EnableInput=true;if(!optionsWasPaused)view.OnTogglePause?.Invoke();}
        void DrawMenu()
        {
            Rect rect=MenuRect;FramedPanel(rect,Navy);
            GUI.Label(new Rect(rect.x+14,rect.y+10,rect.width-28,25),"BATTLE MENU",heading);
            float y=rect.y+48;bool old=GUI.enabled;GUI.enabled=view.EnableInput;
            if(ThemedButton(new Rect(rect.x+12,y,196,36),"RETURN TO BATTLE",button))menuOpen=false;y+=44;
            var armory=GetComponent<BattleArmory>();
            if(armory&&ThemedButton(new Rect(rect.x+12,y,196,36),"REGIMENTS",button)){menuOpen=false;armory.Open();}y+=44;
            if(ThemedButton(new Rect(rect.x+12,y,196,36),"GAME OPTIONS",button)){OpenOptions();GUI.enabled=old;return;}y+=44;
            if(ThemedButton(new Rect(rect.x+12,y,196,36),"ARMY OVERVIEW",button)){view.SetOverview();menuOpen=false;}y+=44;
            if(!audio)audio=GetComponent<BattleAudio>();
            if(audio&&ThemedButton(new Rect(rect.x+12,y,196,36),"AUDIO CREDITS",button)){audio.ShowAudioCredits=true;menuOpen=false;}y+=44;
            if(world.Config.CampaignDeployment)
            {
                bool ended=world.Outcome!=BattleOutcome.Ongoing;
                string action=ended?(world.Config.RegimentalDeployment?"RETURN TO COMPANY":"RETURN TO CAMPAIGN"):
                    world.Config.RegimentalDeployment?"RETREAT TO COMPANY":world.DeploymentActive?"CANCEL DEPLOYMENT":"CONCEDE BATTLE";
                if(ThemedButton(new Rect(rect.x+12,y,196,36),action,button))
                {
                    if(ended)view.OnExitBattle?.Invoke();else view.OnConcedeBattle?.Invoke();
                }
            }
            else
            {
                if(ThemedButton(new Rect(rect.x+12,y,196,36),"RESTART BATTLE",button)){menuOpen=false;view.OnRestart?.Invoke();}y+=44;
                if(EmpireGame.GameApplication.Available&&ThemedButton(new Rect(rect.x+12,y,196,36),"MAIN MENU",button))EmpireGame.GameApplication.ReturnToMenu();
            }

            GUI.enabled=old;
        }
    }
}
