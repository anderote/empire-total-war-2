using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace Empire.UI
{
    public sealed class RegimentRecord
    {
        public string Id,Name,Role,Status,Weapon="musket",StrengthLabel="troops",Faction="france";
        public EquipmentUsers Users;
        public UnitRole BattleRole=UnitRole.Line;
        public int Strength,Capacity,Quality,Tier,CurrentKills;
        public long TotalKills=>Progression.totalKills>long.MaxValue-Math.Max(0,CurrentKills)?long.MaxValue:Progression.totalKills+Math.Max(0,CurrentKills);
        public UnitProgression Progression;
        public UnitLoadout Loadout;
        public double Power=>BattlePower.Total(Strength,BattleRole,Tier,Quality,Progression,Loadout,Weapon,Users==EquipmentUsers.Artillery);
    }
    public sealed class RegimentAction
    {
        public string Title,Cost="",Description="",Reason="";
        public bool Enabled;
        public Func<string> Execute;
        public KeyCode Hotkey;
    }
    public interface IRegimentSource
    {
        string Context {get;}
        string Resources {get;}
        string Guidance {get;}
        bool MultiSelect {get;}
        bool CanArchive {get;}
        RegimentAction Import(string targetId,SavedRegiment saved);
        IReadOnlyList<RegimentRecord> Units {get;}
        RegimentAction Equipment(string[] ids,EquipmentItem item);
        RegimentAction Skill(string[] ids,UnitSkill skill);
        RegimentAction Ability(string[] ids,RegimentAbilityDefinition ability);
        IEnumerable<RegimentAction> Services(string[] ids);
    }
    public interface IRegimentAppearanceSource
    {
        bool CanDress {get;}
        string Dress(string id,string appearance);
    }
    public interface IRegimentOrganizationSource
    {
        bool CanOrganize {get;}
        string Rename(string id,string name);
        string Move(string id,string beforeId);
    }
    public static class RegimentOrganization
    {
        // Insert before a stable ID; null means the end. Hidden units retain their relative order.
        public static bool Move<T>(List<T> units,Func<T,string> id,string moving,string before)
        {
            int from=units.FindIndex(u=>id(u)==moving);
            if(from<0||moving==before||(before!=null&&!units.Any(u=>id(u)==before)))return false;
            var unit=units[from];units.RemoveAt(from);
            int to=before==null?units.Count:units.FindIndex(u=>id(u)==before);
            units.Insert(to,unit);return true;
        }
    }
    public interface IRegimentCompanySource
    {
        IEnumerable<RegimentAction> CompanyActions();
    }
    public readonly struct RegimentStat
    {
        public readonly string Name,Format,Help;
        public readonly float Value;
        public readonly bool LowerIsBetter;
        public RegimentStat(string name,float value,string format,string help,bool lower=false){Name=name;Value=value;Format=format;Help=help;LowerIsBetter=lower;}
        public string Display=>Value.ToString(Format,System.Globalization.CultureInfo.InvariantCulture);
    }
    public static class RegimentPreview
    {
        public static string SlotName(EquipmentSlot slot)=>slot==EquipmentSlot.Weapon?"Primary weapon":slot==EquipmentSlot.Secondary?"Sidearm / bayonet":slot==EquipmentSlot.Throwables?"Throwables":slot==EquipmentSlot.Mobility?"Boots / transport":slot==EquipmentSlot.Standard?"Colours":slot.ToString();
        public static string Current(RegimentRecord r,EquipmentSlot slot)
        {
            var item=r.Loadout.items.Select(UnitEquipment.Find).FirstOrDefault(i=>i!=null&&i.Slot==slot);
            return (item==null?null:UnitEquipment.Label(item))??(slot==EquipmentSlot.Weapon?MilitaryEquipment.Weapon(r.Weapon)?.Name??"Standard issue":slot==EquipmentSlot.Secondary?"Standard issue":"No additional kit");
        }
        public static UnitLoadout WithItem(RegimentRecord r,EquipmentItem item)
        {var loadout=r.Loadout.Copy();if(item!=null&&item.Supports(r.Users))loadout.Equip(item.Id,r.Users);return loadout;}
        public static RegimentStat[] Stats(RegimentRecord r,EquipmentItem replacement=null)
        {
            var kit=WithItem(r,replacement);var mods=kit.Modifiers;var p=r.Progression;var basis=RegimentBaseStats.For(r.Tier,r.BattleRole);
            var issued=kit.items.Select(UnitEquipment.Find).FirstOrDefault(i=>i.Weapon!="");
            var weapon=MilitaryEquipment.Weapon(issued?.Weapon??r.Weapon)??MilitaryEquipment.Weapon("musket");
            bool artillery=r.Users==EquipmentUsers.Artillery,shoots=artillery||UnitRoster.Shoots(r.BattleRole)||r.BattleRole==UnitRole.Cavalry&&(EmpireBattle.Art.NapoleonicOutfits.MountedWeapon(kit)=="carbine"||EmpireBattle.Art.NapoleonicOutfits.MountedWeapon(kit)=="pistol");
            var stats=new List<RegimentStat> {new RegimentStat("BP",(float)BattlePower.Total(r.Strength,r.BattleRole,r.Tier,r.Quality,p,kit,r.Weapon,artillery),"N0",BattlePower.Help),
                new RegimentStat("BP per "+(artillery?"gun":"troop"),(float)BattlePower.PerTroop(r.BattleRole,r.Tier,r.Quality,p,kit,r.Weapon,artillery),"0.00",BattlePower.Help)};
            if(!artillery)stats.Add(new RegimentStat("Health",basis.Health+p.MaxHealth-100,"#,0","Maximum health per soldier. Living wounded retain damage; replacements begin at full health."));
            if(shoots)
            {
                stats.Add(new RegimentStat("Accuracy",(artillery?basis.Accuracy:weapon.Precision*basis.Accuracy)*p.AccuracyMultiplier*(1+r.Quality*.04f)*(1+mods.accuracy),"0.00'×'","Relative precision. Terrain, distance, fatigue and formation affect the final chance to hit."));
                if(!artillery)stats.Add(new RegimentStat("Shot damage",WeaponBallistics.SmallArmsDamage(weapon.EffectiveRange,weapon.EffectiveRange,.5f,weapon.Id,UnitKind.Infantry),"#,0","Average damage per hit against infantry or cavalry outside close range. Close-range hits deal more; individual damage rolls vary."));
                stats.Add(new RegimentStat("Reload",(artillery?1:weapon.Reload)*Mathf.Clamp(basis.Reload*p.ReloadMultiplier*(1-r.Quality*.02f)*(1+mods.reload),.2f,1.5f),artillery?"0.00'×'":"0.0' s'","Reload time for issued weapons; lower is faster. Artillery shows a service-time multiplier.",true));
                if(!artillery)
                {
                    stats.Add(new RegimentStat("Shot interval",(weapon.Magazine>1?weapon.Cycle:weapon.Reload)*Mathf.Clamp(basis.Reload*p.ReloadMultiplier*(1-r.Quality*.02f)*(1+mods.reload),.2f,1.5f),"0.0' s'","Time between shots while ammunition is loaded. Magazine rifles need a full reload when empty. Firing drills, fatigue and fixed bayonets affect combat timing.",true));
                    stats.Add(new RegimentStat("Capacity",weapon.Magazine,"0","Shots before a full reload. Magazine capacity is separate from total carried ammunition."));
                }
                stats.Add(new RegimentStat("Effective range",(artillery?900:weapon.EffectiveRange)*(1+mods.range),"#,0' m'","Nominal firing range; line of sight and ammunition still apply."));
                stats.Add(new RegimentStat("Ammunition",(artillery?ArtillerySystem.StartingAmmunition:30)+mods.ammunition+(artillery?p.ArtilleryAmmunitionBonus:p.AmmunitionBonus(weapon)),"0","Rounds per soldier or cannon at the start of battle."));
            }
            stats.Add(new RegimentStat("Melee",basis.Melee*p.MeleeMultiplier*(1+r.Quality*.04f)*(1+mods.melee),"0.00'×'","Damage multiplier from training, quality and kit. Weapon role, bayonets and charge state are applied in combat."));
            stats.Add(new RegimentStat("Morale reserve",basis.Morale+p.MaxMorale-100+mods.moraleReserve,"0","Starting morale before combat pressure."));
            stats.Add(new RegimentStat("Resolve",basis.Resolve*p.ResolveMultiplier*(1+mods.resolve),"0.00'×'","Resistance to morale damage; higher is better."));
            stats.Add(new RegimentStat("Fatigue gain",Mathf.Clamp(p.FatigueMultiplier*(1+mods.fatigue),.15f,1.6f),"0.00'×'","Fatigue gained in temperate conditions. Uniforms add separate heat and cold effects.",true));
            stats.Add(new RegimentStat("March speed",basis.Speed*(1+mods.speed),"0.00'×'","Movement multiplier from carried equipment. Formation, fatigue and terrain also affect speed."));
            stats.Add(new RegimentStat("Grenades",p.Grenades(mods.grenades),"0","Grenades per regiment. Automatically thrown at close enemies while fire at will is enabled. Limited to one battle."));
            return stats.ToArray();
        }
    }
    // One stateful view. Adapters own economy/rules and save through their existing game models.
    public sealed partial class RegimentManagementPanel
    {
        readonly HashSet<string> selection=new HashSet<string>();
        readonly UITooltip tips=new UITooltip();
        RegimentPortrait equipmentPortrait;
        string focus="",search="",equipmentSearch="",message="",chosenItem="";
        int tab,filter,slot=-1;
        string renameId="",renameText="",dragId="";
        bool focusRename,dragging;
        Vector2 dragStart;
        double lastDragRepaint;
        void BeginRename(RegimentRecord unit,IRegimentSource source)
        {
            if(source is not IRegimentOrganizationSource organization||!organization.CanOrganize)return;
            Inspect(unit.Id);renameId=unit.Id;renameText=unit.Name;focusRename=true;
        }
        void DrawName(Rect r,RegimentRecord unit,IRegimentSource source)
        {
            if(renameId!=""&&renameId!=unit.Id){renameId="";GUI.FocusControl(null);}
            if(renameId==unit.Id)
            {
                var organization=source as IRegimentOrganizationSource;
                if(organization?.CanOrganize!=true){renameId="";GUI.FocusControl(null);return;}
                bool valid=SavedRegiment.SafeName(renameText.Trim(),60);
                var e=Event.current;bool save=false,cancel=false;
                if(e.type==EventType.KeyDown&&(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter||e.keyCode==KeyCode.Escape))
                {save=e.keyCode!=KeyCode.Escape&&valid;cancel=e.keyCode==KeyCode.Escape;e.Use();}
                GUI.SetNextControlName("regiment-rename");renameText=GUI.TextField(new Rect(r.x,r.y,Mathf.Max(50,r.width-144),r.height),renameText,60);
                if(focusRename&&e.type==EventType.Repaint){GUI.FocusControl("regiment-rename");focusRename=false;}
                save|=CommandUI.Button(new Rect(r.xMax-136,r.y,62,r.height),"Save",valid);
                cancel|=CommandUI.Button(new Rect(r.xMax-70,r.y,70,r.height),"Cancel");
                if(save){message=organization.Rename(renameId,renameText);renameId="";GUI.FocusControl(null);}
                if(cancel){renameId="";GUI.FocusControl(null);}
                tips.Add(r,"Enter to save · Escape to cancel · up to 60 characters.");
                return;
            }
            var name=new Rect(r.x,r.y,r.width-76,r.height);
            if(Event.current.type==EventType.MouseDown&&Event.current.button==0&&Event.current.clickCount==2&&name.Contains(Event.current.mousePosition))
            {BeginRename(unit,source);Event.current.Use();}
            CommandUI.Label(name,unit.Name,22,false,true);
            if(source is IRegimentOrganizationSource editable&&editable.CanOrganize)
            {
                if(CommandUI.Button(new Rect(r.xMax-72,r.y,72,r.height),"Rename"))BeginRename(unit,source);
                tips.Add(name,"Double-click to rename this regiment.");
            }
        }
        int HandleRosterPointer(Rect viewport,RegimentRecord[] visible,float rowHeight,IRegimentSource source,int control)
        {
            var e=Event.current;var mouse=e.mousePosition;
            bool inside=viewport.Contains(mouse)&&mouse.x<viewport.xMax-16;
            float y=mouse.y-viewport.y+rosterScroll.y;
            int row=Mathf.FloorToInt(y/rowHeight);
            int insertion=Mathf.Clamp(Mathf.FloorToInt((y+rowHeight*.5f)/rowHeight),0,visible.Length);
            var organization=source as IRegimentOrganizationSource;
            if(dragId!=""&&organization?.CanOrganize!=true){dragId="";dragging=false;if(GUIUtility.hotControl==control)GUIUtility.hotControl=0;}
            if(e.type==EventType.MouseDown&&inside&&row>=0&&row<visible.Length)
            {
                if(e.button==0&&e.clickCount==2){BeginRename(visible[row],source);e.Use();}
                else if(e.button==1&&organization?.CanOrganize==true&&renameId=="")
                {dragId=visible[row].Id;dragStart=mouse;dragging=false;GUIUtility.hotControl=control;lastDragRepaint=Time.realtimeSinceStartupAsDouble;e.Use();}
            }
            if(dragId=="")return -1;
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape)
            {dragId="";dragging=false;GUIUtility.hotControl=0;e.Use();return -1;}
            if(e.type==EventType.MouseDrag&&e.button==1)
            {dragging|=(mouse-dragStart).sqrMagnitude>=36;e.Use();}
            if(e.type==EventType.MouseUp&&e.button==1)
            {
                if(dragging&&inside)
                {
                    string before=insertion<visible.Length?visible[insertion].Id:null;
                    // Dropping just before/after the source card leaves it in place.
                    int from=Array.FindIndex(visible,u=>u.Id==dragId);
                    if(insertion!=from&&insertion!=from+1)message=organization.Move(dragId,before);
                }
                dragId="";dragging=false;GUIUtility.hotControl=0;e.Use();return -1;
            }
            if(dragging&&e.type==EventType.Repaint)
            {
                double now=Time.realtimeSinceStartupAsDouble;float dt=(float)Math.Min(.05,now-lastDragRepaint);lastDragRepaint=now;
                if(mouse.x>=viewport.x&&mouse.x<=viewport.xMax)
                {
                    float speed=mouse.y<viewport.y+28?-300:mouse.y>viewport.yMax-28?300:0;
                    rosterScroll.y=Mathf.Clamp(rosterScroll.y+speed*dt,0,Mathf.Max(0,visible.Length*rowHeight-viewport.height));
                }
            }
            return dragging&&inside?insertion:-1;
        }
        EquipmentItem statsPreview;
        RegimentRecord trainingPreview;
        bool groupEquipment,allEquipmentLevels,readyEquipmentOnly;
        string[] GearTargets()=>groupEquipment?selection.ToArray():new[]{focus};
        Vector2 rosterScroll,contentScroll,equipmentScroll,previewScroll,abilityScroll,statsScroll;
        static readonly string[] Tabs={"Equipment","Skill points","Abilities","Quartermaster"};
        static readonly int[] TabPages={0,2,3,4};
        public string FocusedId=>focus;
        public int ActiveTab=>tab;
        public string[] EquipmentRecipientIds=>GearTargets();
        public void Inspect(string id){if(groupEquipment){focus=id;contentScroll=previewScroll=Vector2.zero;}else Select(id);}
        public void SetEquipmentRecipients(IEnumerable<string> ids){groupEquipment=true;selection.Clear();foreach(string id in ids)selection.Add(id);}
        public void Select(string id){focus=id;selection.Clear();selection.Add(id);contentScroll=previewScroll=Vector2.zero;}
        public void ShowTab(int index){tab=Mathf.Clamp(index,0,4);contentScroll=Vector2.zero;}
        public void InspectSlot(EquipmentSlot value){slot=(int)value;tab=1;equipmentScroll=previewScroll=Vector2.zero;chosenItem="";}
        void Run(RegimentAction action)
        {if(action?.Enabled==true&&action.Execute!=null)message=action.Execute();}
        public void Draw(Rect r,IRegimentSource source)
        {
            int rosterControl=GUIUtility.GetControlID(FocusType.Passive);
            tips.Begin();statsPreview=null;trainingPreview=null;EmpireTheme.Panel(r,EmpireTheme.Surface);
            var units=source.Units;if(!source.MultiSelect)groupEquipment=false;
            selection.RemoveWhere(id=>!units.Any(u=>u.Id==id));
            if(!units.Any(u=>u.Id==focus)&&units.Count>0)Select(units[0].Id);
            float left=r.width>=1450?300:244;
            CommandUI.Eyebrow(new Rect(r.x+18,r.y+12,r.width-36,18),source.Context);
            CommandUI.Label(new Rect(r.x+18,r.y+34,r.width-36,28),source.Resources,18,false,true);
            CommandUI.Rule(r.x+16,r.y+72,r.width-32);
            float commands=source is IRegimentCompanySource?58:0;
            if(source is IRegimentCompanySource companySource)DrawCompanyActions(new Rect(r.x+16,r.y+79,r.width-32,50),companySource);
            var roster=new Rect(r.x+14,r.y+86+commands,left,r.height-146-commands);
            DrawRoster(roster,units,source,rosterControl);
            bool separateStats=r.width>=1080;
            float statsWidth=separateStats?Mathf.Clamp(r.width*.25f,320,420):0;
            var statsPane=new Rect(roster.xMax+14,roster.y,statsWidth,roster.height);
            var detail=new Rect(separateStats?statsPane.xMax+18:roster.xMax+18,roster.y,r.xMax-(separateStats?statsPane.xMax:roster.xMax)-36,roster.height);
            var unit=units.FirstOrDefault(u=>u.Id==focus);
            if(unit!=null)
            {
                MilitaryInsignia.Rank(new Rect(detail.x,detail.y,48,57),unit.Progression.Rank);
                tips.Add(new Rect(detail.x,detail.y,48,57),unit.Progression.RankName+" · experience rank\nLevel "+unit.Progression.UnitLevel+" / "+UnitProgression.MaxLevel+". Rank and troop tier are separate.");
                DrawName(new Rect(detail.x+60,detail.y,detail.width-118,30),unit,source);
                if(unit.Tier>0){MilitaryInsignia.Tier(new Rect(detail.xMax-46,detail.y,40,48),unit.Tier);tips.Add(new Rect(detail.xMax-46,detail.y,40,48),"Troop tier "+unit.Tier+" · "+unit.Role+"\nPromote through Quartermaster; experience rank is retained.");}
                string label=unit.Role+"  ·  "+CommandUI.Number(unit.Strength)+" / "+CommandUI.Number(unit.Capacity)+" "+unit.StrengthLabel+"  ·  Level "+unit.Progression.UnitLevel+" / "+UnitProgression.MaxLevel+"  ·  "+unit.Progression.RankName;
                CommandUI.Label(new Rect(detail.x+60,detail.y+34,detail.width-112,24),label,13,true);
                var xp=unit.Progression;int floor=xp.RankStartXP;
                CommandUI.Meter(new Rect(detail.x,detail.y+68,detail.width,5),xp.UnitLevel==UnitProgression.MaxLevel?1:(xp.xp-floor)/(float)(xp.NextRankXP-floor));
                CommandUI.Label(new Rect(detail.x,detail.y+77,detail.width,23),CommandUI.Number(xp.xp)+" XP"+(xp.UnitLevel==UnitProgression.MaxLevel?" · maximum level":" / "+CommandUI.Number(xp.NextRankXP)+" for next level")+"   ·   "+xp.AvailablePoints+" stat points  ·  "+xp.AvailableSkillPoints+" ability points"+(groupEquipment?"   ·   Gear group: "+selection.Count+" checked":""),12,true);
                tips.Add(new Rect(detail.x,detail.y+64,detail.width,37),UnitProgression.MaxLevel+" levels: each level grants 2 stat points. Every odd level grants an ability point. Equipment costs money and does not consume either type of point.");
                for(int i=0;i<Tabs.Length;i++)if(CommandUI.Button(new Rect(detail.x+i*detail.width/Tabs.Length,detail.y+112,detail.width/Tabs.Length-4,34),(i==1?Tabs[i]+" ("+xp.AvailablePoints+")":i==2?Tabs[i]+" ("+xp.AvailableSkillPoints+")":Tabs[i]),selected:tab==TabPages[i]||(i==0&&tab==1))){tab=TabPages[i];contentScroll=Vector2.zero;GUI.FocusControl(null);}
                var body=new Rect(detail.x,detail.y+162,detail.width,detail.height-162);
                if(tab==0)DrawOverview(body,unit,source);
                if(tab==1)DrawEquipment(body,unit,source);
                if(tab==2)DrawSkills(body,unit,source);
                if(tab==3)DrawAbilities(body,unit,source);
                if(tab==4)DrawServices(body,unit,source);
                if(separateStats){CampaignArt.PaperPanel(statsPane);DrawStats(CommandUI.Inset(statsPane,12),unit,statsPreview);}
            }
            else CommandUI.Label(detail,"No regiments available.",18,true);
            CommandUI.Rule(r.x+16,r.yMax-48,r.width-32);
            CommandUI.Label(new Rect(r.x+18,r.yMax-43,r.width-36,36),dragging?"Moving "+(units.FirstOrDefault(u=>u.Id==dragId)?.Name??"regiment")+" · release right mouse to place · Escape to cancel":string.IsNullOrEmpty(message)?source.Guidance:message,12,true,false,true);
            tips.Draw(r);
        }
        void DrawCompanyActions(Rect r,IRegimentCompanySource source)
        {
            var actions=source.CompanyActions().ToArray();float width=(r.width-8*(actions.Length-1))/actions.Length;
            for(int i=0;i<actions.Length;i++)
            {
                var action=actions[i];string shortcut="Ctrl/⌘ + Shift + "+action.Hotkey;
                if(CommandUI.Button(new Rect(r.x+i*(width+8),r.y,width,r.height),action.Title+"\n"+action.Cost,action.Enabled,hint:action.Description+"\n"+shortcut+(action.Enabled?"":"\n"+action.Reason),tips:tips))Run(action);
                var e=Event.current;
                if(e.type==EventType.KeyDown&&(e.control||e.command)&&e.shift&&e.keyCode==action.Hotkey&&GUI.GetNameOfFocusedControl()=="")
                {Run(action);e.Use();}
            }
        }
        string[] Selected()=>GearTargets();
        void DrawRoster(Rect r,IReadOnlyList<RegimentRecord> units,IRegimentSource source,int control)
        {
            bool bulk=source.MultiSelect;
            GUI.SetNextControlName("regiment-search");search=GUI.TextField(new Rect(r.x,r.y,r.width,30),search);
            tips.Add(new Rect(r.x,r.y,r.width,30),"Search by regiment name or troop type.");
            if(search==""&&GUI.GetNameOfFocusedControl()!="regiment-search")CommandUI.Label(new Rect(r.x+10,r.y,r.width-20,30),"Search regiments…",12,true);
            string[] filters={"All","Wounded","Points"};
            for(int i=0;i<3;i++)if(CommandUI.Button(new Rect(r.x+i*r.width/3,r.y+37,r.width/3-3,27),filters[i],selected:filter==i)){filter=i;rosterScroll=Vector2.zero;}
            bool checkedMode=bulk&&groupEquipment;
            float rowHeight=r.width>=280?100:78;
            float top=72;
            if(bulk)
            {
                if(CommandUI.Button(new Rect(r.x,r.y+72,r.width/2-3,30),"This regiment",selected:!groupEquipment)){groupEquipment=false;selection.Clear();selection.Add(focus);}
                if(CommandUI.Button(new Rect(r.x+r.width/2,r.y+72,r.width/2,30),"Checked group",selected:groupEquipment)){if(!groupEquipment){selection.Clear();selection.Add(focus);}groupEquipment=true;}
                top=110;
                if(groupEquipment)
                {
                    if(CommandUI.Button(new Rect(r.x,r.y+109,r.width/2-3,27),"Check visible")){selection.Clear();foreach(var u in Visible())selection.Add(u.Id);}
                    if(CommandUI.Button(new Rect(r.x+r.width/2,r.y+109,r.width/2,27),"Clear checks"))selection.Clear();
                    CommandUI.Label(new Rect(r.x,r.y+139,r.width,20),selection.Count+" checked · gear orders only",11,true);top=163;
                }
                checkedMode=groupEquipment;
            }
            var visible=Visible().ToArray();
            var viewport=new Rect(r.x,r.y+top,r.width,r.height-top);
            int insertion=HandleRosterPointer(viewport,visible,rowHeight,source,control);
            rosterScroll=GUI.BeginScrollView(viewport,rosterScroll,new Rect(0,0,r.width-14,Mathf.Max(viewport.height-1,visible.Length*rowHeight)));
            for(int i=0;i<visible.Length;i++)
            {
                var u=visible[i];var row=new Rect(0,i*rowHeight,r.width-16,rowHeight-6);
                float inset=checkedMode?28:0;
                if(checkedMode)
                {
                    bool check=GUI.Toggle(new Rect(4,row.y+18,24,24),selection.Contains(u.Id),GUIContent.none);
                    if(check)CommandUI.Label(new Rect(8,row.y+18,20,24),"✓",14,false,true);
                    if(check!=selection.Contains(u.Id)){if(check)selection.Add(u.Id);else selection.Remove(u.Id);}
                }
                if(CommandUI.Button(new Rect(inset,row.y,row.width-inset,row.height),"",selected:focus==u.Id,hint:u.Name+"\n"+u.Status+(source is IRegimentOrganizationSource editable&&editable.CanOrganize?"\nDouble-click to rename · hold right mouse and drag to reorder.":"")+(checkedMode?"\nChecked group receives gear. Skill points, abilities and Quartermaster apply only to the inspected regiment.":""),tips:tips))
                {Inspect(u.Id);}
                var paper=new Rect(inset,row.y,row.width-inset,row.height);
                CampaignArt.PaperPanel(paper,focus==u.Id,paper.Contains(Event.current.mousePosition));
                CampaignArt.Portrait(new Rect(inset+7,row.y+7,36,45),u,false);
                CampaignArt.Label(new Rect(inset+51,row.y+6,row.width-inset-136,21),u.Name,13,false,true);
                CampaignArt.Label(new Rect(inset+51,row.y+28,row.width-inset-136,19),CommandUI.Number(u.Strength)+" / "+CommandUI.Number(u.Capacity)+" · Lv "+u.Progression.UnitLevel+(u.Progression.AvailablePoints>0?" +"+u.Progression.AvailablePoints:""),11,true);
                var powerRect=new Rect(row.xMax-79,row.y+12,70,29);
                CampaignArt.Label(powerRect,u.Power.ToString("N0",System.Globalization.CultureInfo.InvariantCulture),18,false,true);
                CampaignArt.Label(new Rect(powerRect.x,powerRect.yMax,70,16),"BP",9,true);
                tips.Add(new Rect(powerRect.x,powerRect.y,70,45),BattlePower.Help+"\n"+(u.Strength>0?u.Power/u.Strength:0).ToString("0.00")+" power per "+(u.Users==EquipmentUsers.Artillery?"gun":"troop"));
                if(r.width>=280)
                {
                    CampaignArt.Label(new Rect(inset+51,row.y+50,row.width-inset-61,18),u.Role+" · "+u.Loadout.items.Count+" kit items",11,true);
                    CampaignArt.Label(new Rect(inset+10,row.y+68,row.width-inset-20,18),u.Progression.abilities.Distinct().Count()+" abilities · "+u.Progression.AvailablePoints+" stat points",11,true);
                }
                EmpireTheme.Fill(new Rect(inset+10,row.yMax-8,row.width-inset-20,3),CampaignArt.RuleColor);
                EmpireTheme.Fill(new Rect(inset+10,row.yMax-8,(row.width-inset-20)*Mathf.Clamp01(u.Strength/(float)Math.Max(1,u.Capacity)),3),u.Strength<u.Capacity*.5f?CampaignArt.Reduction:CampaignArt.Muted);
            }
            if(insertion>=0)EmpireTheme.Fill(new Rect(0,Mathf.Clamp(insertion*rowHeight-3,0,Mathf.Max(0,visible.Length*rowHeight-4)),r.width-16,3),EmpireTheme.Brass);
            if(visible.Length==0)CommandUI.Label(new Rect(8,8,r.width-30,65),"No matching regiments.\nTry another search or filter.",13,true,false,true);
            GUI.EndScrollView();
            if(Event.current.type==EventType.KeyDown&&GUI.GetNameOfFocusedControl()==""&&(Event.current.keyCode==KeyCode.UpArrow||Event.current.keyCode==KeyCode.DownArrow)&&visible.Length>0)
            {
                int n=Array.FindIndex(visible,u=>u.Id==focus);n=Mathf.Clamp(n+(Event.current.keyCode==KeyCode.DownArrow?1:-1),0,visible.Length-1);Inspect(visible[n].Id);rosterScroll.y=Mathf.Clamp(n*rowHeight-viewport.height/2,0,Mathf.Max(0,visible.Length*rowHeight-viewport.height));Event.current.Use();
            }
            IEnumerable<RegimentRecord> Visible()=>units.Where(u=>(filter!=1||u.Strength<u.Capacity)&&(filter!=2||u.Progression.AvailablePoints>0||u.Progression.AvailableSkillPoints>0)&&(string.IsNullOrWhiteSpace(search)||(u.Name+" "+u.Role).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0));
        }
        void DrawStats(Rect r,RegimentRecord unit,EquipmentItem preview)
        {
            bool comparing=preview!=null||trainingPreview!=null;
            CampaignArt.Label(new Rect(r.x,r.y,r.width,24),comparing?"Upgrade comparison":"Regiment record",20,false,false,true);
            float portraitHeight=Mathf.Clamp(r.height*.25f,132,195),portraitWidth=portraitHeight*.8f;
            var portrait=new Rect(r.x,r.y+34,portraitWidth,portraitHeight);
            CampaignArt.Portrait(portrait,unit);
            tips.Add(portrait,"Curated portrait of this unit's base uniform and role. Inspect current equipment in the live model on the Equipment tab.");
            float summaryX=portrait.xMax+13,summaryWidth=r.xMax-summaryX;
            CampaignArt.Label(new Rect(summaryX,r.y+37,summaryWidth,48),unit.Name,18,false,false,true,true);
            CampaignArt.Label(new Rect(summaryX,r.y+89,summaryWidth,35),unit.Role+" · Tier "+unit.Tier,11,true,false,false,true);
            CampaignArt.Label(new Rect(summaryX,r.y+128,summaryWidth,20),"Kit condition  "+unit.Loadout.Condition+"%",11,true);
            CampaignArt.Label(new Rect(summaryX,r.y+151,summaryWidth,20),"Quality  "+unit.Quality+" / "+UnitProgression.MaxQuality,11,true);
            var current=RegimentPreview.Stats(unit);var next=RegimentPreview.Stats(trainingPreview??unit,preview);
            float statsTop=portraitHeight+48;
            var viewport=new Rect(r.x,r.y+statsTop,r.width,Mathf.Max(1,r.height-statsTop));
            statsScroll=GUI.BeginScrollView(viewport,statsScroll,new Rect(0,0,r.width-16,Mathf.Max(viewport.height-1,(current.Length+1)*27+60)));
            float width=r.width-20;
            CampaignArt.Label(new Rect(0,0,width*.51f,26),"Total kills",12,true);
            CampaignArt.Label(new Rect(width*.51f,0,width*.49f,26),CommandUI.Number(unit.TotalKills),12,false,true);
            tips.Add(new Rect(0,0,width,26),"Lifetime credited kills, including the current battle. Retained through replenishment. Older saves begin tracking from this update.");
            CampaignArt.Rule(0,26,width);
            for(int i=0;i<current.Length;i++)
            {
                var rect=new Rect(0,(i+1)*27,width,26);tips.Add(rect,current[i].Help);
                CampaignArt.Label(new Rect(rect.x,rect.y,rect.width*.51f,rect.height),current[i].Name,12,true);
                float delta=next[i].Value-current[i].Value;
                string value=comparing?RegimentUpgradeChoices.Comparison(current[i],next[i]):current[i].Display;
                Color? change=Mathf.Abs(delta)>.001f?(delta>0!=current[i].LowerIsBetter?CampaignArt.Improvement:CampaignArt.Reduction):(Color?)null;
                CampaignArt.Label(new Rect(rect.x+rect.width*.51f,rect.y,rect.width*.49f,rect.height),value,12,false,change.HasValue,tint:change);
                CampaignArt.Rule(rect.x,rect.yMax-1,rect.width);
            }
            CampaignArt.Label(new Rect(0,(current.Length+1)*27+8,width,44),comparing?"Green improves · red reduces. Before conditional abilities.":"Base stats + kit, before conditional abilities. Hover for details.",11,true,false,false,true);
            GUI.EndScrollView();
        }
    }
}
