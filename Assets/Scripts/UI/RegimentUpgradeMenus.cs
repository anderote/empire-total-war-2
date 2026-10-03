using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace Empire.UI
{
    public static class RegimentUpgradeChoices
    {
        public static EquipmentItem[] Equipment(RegimentRecord unit,bool allLevels)
        {
            var compatible=UnitEquipment.Items.Where(i=>i.Supports(unit.Users));
            if(allLevels)return compatible.ToArray();
            return compatible.GroupBy(i=>i.Family).Select(group=>
            {
                var ordered=group.OrderBy(i=>i.Level).ToArray();
                var current=UnitEquipment.Current(unit.Loadout,ordered[0].Slot);
                return current!=null&&current.Family==group.Key?ordered.FirstOrDefault(i=>i.Level>current.Level)??current:ordered[0];
            }).ToArray();
        }
        public static RegimentRecord WithSkill(RegimentRecord unit,UnitSkill skill)
        {
            var progress=unit.Progression.Copy();progress.Train(skill);
            return new RegimentRecord{Id=unit.Id,Name=unit.Name,Role=unit.Role,Status=unit.Status,Weapon=unit.Weapon,
                StrengthLabel=unit.StrengthLabel,Faction=unit.Faction,CurrentKills=unit.CurrentKills,Users=unit.Users,BattleRole=unit.BattleRole,Strength=unit.Strength,Capacity=unit.Capacity,
                Quality=unit.Quality,Tier=unit.Tier,Progression=progress,Loadout=unit.Loadout.Copy()};
        }
        public static string Changes(RegimentRecord before,RegimentRecord after)
        {
            return Changes(RegimentPreview.Stats(before),RegimentPreview.Stats(after));
        }
        public static string Changes(RegimentStat[] before,RegimentStat[] after)=>
            string.Join("  ·  ",before.Zip(after,(x,y)=>Math.Abs(x.Value-y.Value)>.001f?x.Name+" "+Comparison(x,y):null).Where(s=>s!=null));
        public static string Comparison(RegimentStat before,RegimentStat after)
        {
            if(Math.Abs(before.Value-after.Value)<=.001f)return before.Display;
            if(before.Display!=after.Display)return before.Display+" → "+after.Display;
            // Small but real upgrades must not display identical rounded values.
            int suffix=before.Format.IndexOf('\'');string format="0.###"+(suffix<0?"":before.Format.Substring(suffix));
            return before.Value.ToString(format)+" → "+after.Value.ToString(format);
        }
        public static UnitSkill[] Skills(RegimentRecord unit)
        {
            UnitSkill[] order={UnitSkill.Health,UnitSkill.Accuracy,UnitSkill.Drill,UnitSkill.Ammunition,UnitSkill.Melee,UnitSkill.Discipline,UnitSkill.Endurance};
            bool artillery=unit.Users==EquipmentUsers.Artillery,shoots=artillery||UnitRoster.HasFirearm(unit.BattleRole,unit.Loadout);
            return order.Where(s=>!(artillery&&(s==UnitSkill.Health||s==UnitSkill.Melee))&&
                !(!shoots&&(s==UnitSkill.Accuracy||s==UnitSkill.Drill||s==UnitSkill.Ammunition))).ToArray();
        }
        public static string AbilityEffect(RegimentAbility ability,int rank)
        {
            if(rank==0)return "Not learned";
            switch(ability)
            {
                case RegimentAbility.SteadyAim:return "+"+(15+5*(rank-1))+"% precision";
                case RegimentAbility.SquareDiscipline:return "+"+(35+15*(rank-1))+"% morale resistance";
                case RegimentAbility.FieldMedicine:return "+"+rank+" health / second";
                case RegimentAbility.AssaultTraining:return "+"+(25+10*(rank-1))+"% charge damage";
                case RegimentAbility.QuickHands:return Math.Min(80,25+10*(rank-1))+"% faster reload";
                case RegimentAbility.HardyMarch:return Math.Min(80,35+10*(rank-1))+"% less walking fatigue";
                case RegimentAbility.LastStand:return (12+4*(rank-1))+" seconds of resistance";
                case RegimentAbility.GrenadierCadre:return "+"+(4*rank)+" grenades with pouches";
                case RegimentAbility.IronConstitution:return "+"+(5*rank)+" maximum health";
                case RegimentAbility.AmmunitionReserve:return "+"+(2*rank)+" reserve shots / magazines";
                case RegimentAbility.RallyingSpirit:return "+"+(3*rank)+" morale";
                case RegimentAbility.WeaponMastery:return "+"+(2*rank)+"% melee damage";
                case RegimentAbility.ArtilleryMunitions:return (rank==1?"Canister":rank==2?"Canister + explosive":rank==3?"Canister + explosive + carcass":rank==4?"Canister + explosive + carcass + lime":"All ammunition types, including poison gas")+" / +"+(2*rank)+" rounds per gun";
                default:return "Rank "+rank;
            }
        }
    }

    public sealed partial class RegimentManagementPanel
    {
        bool readyAbilitiesOnly,readyServicesOnly;
        public void InspectEquipment(string id)
        {chosenItem=id;tab=1;equipmentScroll=previewScroll=Vector2.zero;}
        public void UseCheckedGroup(bool value){groupEquipment=value;}
        void DrawOverview(Rect r,RegimentRecord unit,IRegimentSource source)
        {
            CommandUI.Eyebrow(new Rect(r.x,r.y,r.width,22),"Issued equipment / "+unit.Name);
            CommandUI.Label(new Rect(r.x,r.y+25,r.width,34),"Select a slot to compare and upgrade its equipment.",12,true,false,true);
            DrawUniformIdentity(new Rect(r.x,r.y+62,r.width,30),unit,source);
            DrawKit(new Rect(r.x,r.y+100,r.width,r.height-100),unit,source);
        }
        void DrawUniformIdentity(Rect r,RegimentRecord unit,IRegimentSource source)
        {
            if(unit.Users==EquipmentUsers.Artillery)return;
            var choices=EmpireBattle.Art.NapoleonicOutfits.All.Where(o=>EmpireBattle.Art.NapoleonicOutfits.Compatible(o,unit.Faction,unit.BattleRole,unit.Loadout)).ToArray();
            if(choices.Length==0)return;
            var selected=Array.FindIndex(choices,o=>o.Id==unit.Loadout.appearance);
            var resolved=EmpireBattle.Art.NapoleonicOutfits.Select(unit.Faction,unit.BattleRole,unit.Loadout,unit.Name);
            string label=selected<0?"Automatic · "+(resolved?.Name??"Standard issue"):choices[selected].Name;
            CommandUI.Label(new Rect(r.x,r.y,90,30),"Uniform",12,true);
            CommandUI.Label(new Rect(r.x+92,r.y,Mathf.Max(20,r.width-195),30),label,12,false,true);
            var editable=source as IRegimentAppearanceSource;
            GUI.enabled=editable?.CanDress==true;
            if(CommandUI.Button(new Rect(r.xMax-96,r.y,45,30),"‹")){int next=selected-1;if(next<-1)next=choices.Length-1;message=editable.Dress(unit.Id,next<0?"":choices[next].Id);}
            if(CommandUI.Button(new Rect(r.xMax-47,r.y,45,30),"›")){int next=selected+1;if(next>=choices.Length)next=-1;message=editable.Dress(unit.Id,next<0?"":choices[next].Id);}
            GUI.enabled=true;
            tips.Add(r,"Uniform identity is cosmetic. Guards use guard dress; named regional regiments keep their identity as they gain experience. Cavalry weapons follow issued kit.");
        }
        static string EquipmentSlotLabel(EquipmentSlot slot,EquipmentUsers users)=>slot switch
        {
            EquipmentSlot.Weapon=>users==EquipmentUsers.Artillery?"Gun":"Weapon",
            EquipmentSlot.Mobility=>users==EquipmentUsers.Cavalry?"Mount / tack":users==EquipmentUsers.Artillery?"Transport":"Boots",
            EquipmentSlot.Secondary=>"Sidearm / bayonet",
            EquipmentSlot.Standard=>"Colours",
            _=>slot.ToString()
        };
        void DrawKit(Rect r,RegimentRecord unit,IRegimentSource source)
        {
            // Equipment belongs to a visible slot around the soldier, rather than a generic card grid.
            var leftSlots=new[]{EquipmentSlot.Protection,EquipmentSlot.Weapon,EquipmentSlot.Secondary,EquipmentSlot.Throwables,EquipmentSlot.Standard};
            var rightSlots=new[]{EquipmentSlot.Uniform,EquipmentSlot.Ammunition,EquipmentSlot.Tools,EquipmentSlot.Support,EquipmentSlot.Mobility};
            bool Supports(EquipmentSlot value)=>UnitEquipment.Items.Any(i=>i.Slot==value&&i.Supports(unit.Users));
            var left=leftSlots.Where(Supports).ToArray();var right=rightSlots.Where(Supports).ToArray();
            float contentHeight=Mathf.Max(445,Mathf.Max(left.Length,right.Length)*88+62),width=r.width-18;
            contentScroll=GUI.BeginScrollView(r,contentScroll,new Rect(0,0,r.width-14,contentHeight));
            float centerWidth=Mathf.Clamp(width*.32f,145,235),cardWidth=(width-centerWidth-28)/2;
            var actor=new Rect(cardWidth+14,0,centerWidth,contentHeight-65);
            if(!equipmentPortrait)equipmentPortrait=RegimentPortrait.Create();
            equipmentPortrait.Draw(actor,unit);
            equipmentPortrait.DrawControls(new Rect(actor.x,actor.yMax+4,actor.width,57));
            for(int side=0;side<2;side++)
            {
                var slots=side==0?left:right;
                for(int i=0;i<slots.Length;i++)
                {
                    var gearSlot=slots[i];float x=side==0?0:actor.xMax+14;
                    var card=new Rect(x,i*88,cardWidth,80);
                    var current=unit.Loadout.items.Select(UnitEquipment.Find).FirstOrDefault(item=>item!=null&&item.Slot==gearSlot);
                    var next=UnitEquipment.Next(unit.Loadout,gearSlot,unit.Users);
                    var offer=next==null?null:source.Equipment(GearTargets(),next);
                    if(CommandUI.Button(card,"",selected:slot==(int)gearSlot,hint:"Inspect "+EquipmentSlotLabel(gearSlot,unit.Users)+". Compare upgrades and alternatives before buying.",tips:tips))InspectSlot(gearSlot);
                    float iconSize=Mathf.Clamp(cardWidth*.2f,25,34);
                    KitIcon.Draw(new Rect(card.x+9,card.y+10,iconSize,iconSize),gearSlot);
                    CommandUI.Label(new Rect(card.x+iconSize+17,card.y+7,card.width-iconSize-24,24),EquipmentSlotLabel(gearSlot,unit.Users),11,false,true,true);
                    string equipped=current==null?RegimentPreview.Current(unit,gearSlot):UnitEquipment.Label(current);
                    CommandUI.Label(new Rect(card.x+9,card.y+38,card.width-18,34),equipped,11,current==null,false,true);
                    if(current!=null)EmpireTheme.Fill(new Rect(card.x+1,card.y+1,3,card.height-2),EmpireTheme.Brass);
                    // Short connector anchors point toward the relevant body zone.
                    float anchor=gearSlot==EquipmentSlot.Protection?.12f:gearSlot==EquipmentSlot.Uniform?.32f:gearSlot==EquipmentSlot.Mobility?.91f:gearSlot==EquipmentSlot.Ammunition?.52f:.46f;
                    float edge=side==0?card.xMax:card.x;
                    float joint=side==0?actor.x-4:actor.xMax+4;
                    CommandUI.Rule(Mathf.Min(edge,joint),card.center.y,Mathf.Abs(joint-edge));
                    EmpireTheme.Fill(new Rect(joint,Mathf.Min(card.center.y,actor.height*anchor),1,Mathf.Abs(card.center.y-actor.height*anchor)),EmpireTheme.Line);
                    tips.Add(card,"Slot: "+EquipmentSlotLabel(gearSlot,unit.Users)+"\nIssued: "+equipped+(current==null?"\nStandard issue / empty additional slot":"\nLevel "+current.Level+" / "+UnitEquipment.MaxEquipmentLevel)+(offer==null?"":offer.Enabled?"\nNext upgrade: "+offer.Cost:"\n"+offer.Reason));
                }
            }
            GUI.EndScrollView();
        }
        string GearRecipients(RegimentRecord unit,IRegimentSource source)
        {
            if(!groupEquipment)return "GEAR FOR  "+unit.Name;
            var recipients=source.Units.Where(u=>selection.Contains(u.Id)).ToArray();
            string names=string.Join(", ",recipients.Take(3).Select(u=>u.Name));
            return recipients.Length==0?"NO RECIPIENTS / Check regiments in the roster.":"GEAR FOR  "+recipients.Length+" checked: "+names+(recipients.Length>3?" +"+(recipients.Length-3)+" more":"");
        }
        void DrawEquipment(Rect r,RegimentRecord unit,IRegimentSource source)
        {
            if(CommandUI.Button(new Rect(r.x,r.y,128,30),"← Equipment")){ShowTab(0);return;}
            CommandUI.Label(new Rect(r.x+140,r.y,r.width-140,30),slot<0?"All slots":EquipmentSlotLabel((EquipmentSlot)slot,unit.Users),14,false,true);
            r=new Rect(r.x,r.y+38,r.width,r.height-38);
            GUI.SetNextControlName("equipment-search");equipmentSearch=GUI.TextField(new Rect(r.x,r.y,Mathf.Max(50,r.width-340),30),equipmentSearch);
            if(equipmentSearch==""&&GUI.GetNameOfFocusedControl()!="equipment-search")CommandUI.Label(new Rect(r.x+8,r.y,Mathf.Max(40,r.width-356),30),"Search equipment…",12,true);
            if(CommandUI.Button(new Rect(r.xMax-330,r.y,106,30),allEquipmentLevels?"All levels":"Next upgrades",selected:!allEquipmentLevels)){allEquipmentLevels=!allEquipmentLevels;equipmentScroll=Vector2.zero;}
            if(CommandUI.Button(new Rect(r.xMax-216,r.y,103,30),"Ready only",selected:readyEquipmentOnly)){readyEquipmentOnly=!readyEquipmentOnly;equipmentScroll=Vector2.zero;}
            if(CommandUI.Button(new Rect(r.xMax-105,r.y,105,30),"All slots",selected:slot<0)){slot=-1;equipmentScroll=Vector2.zero;}
            CommandUI.Label(new Rect(r.x,r.y+38,r.width,38),GearRecipients(unit,source),12,true,false,true);
            var procurement=source as IRegimentProcurementSource;bool hostile=procurement?.HasHostileProcurement==true;
            var choices=RegimentUpgradeChoices.Equipment(unit,allEquipmentLevels).ToList();
            var inspected=UnitEquipment.Find(chosenItem);
            if(inspected!=null&&inspected.Supports(unit.Users)&&!choices.Contains(inspected))choices.Add(inspected);
            var candidates=choices
                .Where(i=>(slot<0||(int)i.Slot==slot)&&(string.IsNullOrWhiteSpace(equipmentSearch)||(i.Name+" "+i.Description).IndexOf(equipmentSearch,StringComparison.OrdinalIgnoreCase)>=0))
                .Select(i=>new{item=i,action=source.Equipment(GearTargets(),i),raid=hostile?procurement.RaidEquipment(GearTargets(),i):null}).Where(x=>!readyEquipmentOnly||x.action.Enabled||x.raid?.Enabled==true).ToArray();
            if(!candidates.Any(x=>x.item.Id==chosenItem))chosenItem=(candidates.FirstOrDefault(x=>x.action.Enabled||x.raid?.Enabled==true)??candidates.FirstOrDefault())?.item.Id??"";
            float listWidth=Mathf.Clamp(r.width*.38f,220,365),detailX=r.x+listWidth+16,footer=hostile?150:112;
            var listRect=new Rect(r.x,r.y+83,listWidth,Mathf.Max(50,r.height-83-footer));
            equipmentScroll=GUI.BeginScrollView(listRect,equipmentScroll,new Rect(0,0,listWidth-14,Math.Max(listRect.height-1,candidates.Length*74)));
            for(int i=0;i<candidates.Length;i++)
            {
                var item=candidates[i].item;var offer=candidates[i].action;var row=new Rect(0,i*74,listWidth-17,67);
                bool issued=unit.Loadout.items.Contains(item.Id);
                // Inspect first; the explicit action below commits the selected quote.
                if(row.Contains(Event.current.mousePosition)){if(chosenItem!=item.Id)previewScroll=Vector2.zero;chosenItem=item.Id;statsPreview=item;}
                if(CommandUI.Button(row,"",selected:chosenItem==item.Id)){chosenItem=item.Id;GUI.FocusControl(null);}
                CommandUI.Label(new Rect(10,row.y+6,row.width-20,35),UnitEquipment.Label(item),12,false,true,true);
                CommandUI.Label(new Rect(10,row.y+43,row.width-20,20),offer.Enabled?"Buy · "+offer.Cost:candidates[i].raid?.Enabled==true?"Raid available · inspect consequences":issued?"ISSUED TO INSPECTED UNIT":"UNAVAILABLE · inspect requirements",11,true);
            }
            if(candidates.Length==0)CommandUI.Label(new Rect(8,8,listWidth-25,100),"No equipment matches. Clear the search or turn off Ready only.",13,true,false,true);
            GUI.EndScrollView();
            var chosen=candidates.FirstOrDefault(x=>x.item.Id==chosenItem);
            if(chosen==null)return;
            var selected=chosen.item;statsPreview=selected;var action=source.Equipment(GearTargets(),selected);var raidAction=hostile?procurement.RaidEquipment(GearTargets(),selected):null;
            float dw=r.xMax-detailX;var view=new Rect(detailX,r.y+83,dw,Mathf.Max(50,r.height-83-footer));
            var current=RegimentPreview.Stats(unit);var after=RegimentPreview.Stats(unit,selected);
            string changes=RegimentUpgradeChoices.Changes(current,after);
            string summary=changes==""?UnitEquipment.StatText(selected):changes;
            string details=action.Description+"\n"+(action.Enabled?"":"Unavailable: "+action.Reason+"\n")+"Cost: "+action.Cost+"\n"+UnitEquipment.StatText(selected)+"\n"+(groupEquipment?"Comparison is for the inspected regiment. Other recipients may have different kit.":"Base stats before conditional abilities.");
            if(raidAction!=null)details+="\n\nRAID: "+raidAction.Cost+"\n"+raidAction.Description+(raidAction.Enabled?"":"\nRaid unavailable: "+raidAction.Reason);
            float summaryHeight=Mathf.Max(70,CommandUI.Text(12,false,true,true).CalcHeight(new GUIContent(summary),dw-22));
            float extra=summaryHeight-70,detailsHeight=Mathf.Max(73,CommandUI.Text(11,true,false,true).CalcHeight(new GUIContent(details),dw-22));
            float contentHeight=302+extra+current.Length*28+detailsHeight;
            previewScroll=GUI.BeginScrollView(view,previewScroll,new Rect(0,0,dw-14,contentHeight));dw-=22;
            CommandUI.Label(new Rect(0,0,dw,48),UnitEquipment.Label(selected),19,false,true,true);
            CommandUI.Label(new Rect(0,51,dw,summaryHeight),summary,12,false,true,true);
            CommandUI.Label(new Rect(0,128+extra,dw,63),selected.Description,12,true,false,true);
            CommandUI.Label(new Rect(0,198+extra,dw,45),"Replaces: "+RegimentPreview.Current(unit,selected.Slot),12,true,false,true);
            CommandUI.Eyebrow(new Rect(0,250+extra,dw,20),"Comparison / "+unit.Name);
            DrawStatComparison(new Rect(0,277+extra,dw,current.Length*28),current,after);
            float bottom=284+extra+current.Length*28;
            CommandUI.Label(new Rect(0,bottom,dw,detailsHeight),details,11,true,false,true);
            GUI.EndScrollView();
            float y=r.yMax-footer+7;CommandUI.Rule(r.x,y,r.width);
            CommandUI.Label(new Rect(r.x,y+9,r.width,30),UnitEquipment.Label(selected)+" · "+(action.Enabled?"Ready to buy":action.Reason),12,true,false,true);
            if(CommandUI.Button(new Rect(r.x,y+43,80,32),"Buy",action.Enabled))Run(source.Equipment(GearTargets(),selected));
            CommandUI.Label(new Rect(r.x+90,y+43,r.width-90,32),action.Cost+(hostile?" · includes 50% markup":""),12,true,false,true);
            if(raidAction!=null)
            {
                if(CommandUI.Button(new Rect(r.x,y+82,80,32),"Raid",raidAction.Enabled))Run(procurement.RaidEquipment(GearTargets(),selected));
                CommandUI.Label(new Rect(r.x+90,y+82,r.width-90,32),raidAction.Cost,12,true,false,true);
                CommandUI.Label(new Rect(r.x,y+116,r.width,28),raidAction.Enabled?"Raid damage and repair debt are shown in the comparison details.":raidAction.Reason,11,true,false,true);
            }
            else CommandUI.Label(new Rect(r.x,y+80,r.width,22),"Inspect cards to compare. Buy commits the order for the recipients above.",11,true,false,true);
        }
        void DrawStatComparison(Rect r,RegimentStat[] current,RegimentStat[] after)
        {
            for(int i=0;i<current.Length;i++)
            {
                float y=r.y+i*28,delta=after[i].Value-current[i].Value;bool changed=Mathf.Abs(delta)>.001f;
                tips.Add(new Rect(r.x,y,r.width,26),current[i].Help);
                CommandUI.Label(new Rect(r.x,y,r.width*.47f,26),current[i].Name,12,true);
                var color=GUI.contentColor;if(changed)GUI.contentColor=delta>0!=current[i].LowerIsBetter?new Color(.55f,.85f,.66f):EmpireTheme.Danger;
                CommandUI.Label(new Rect(r.x+r.width*.47f,y,r.width*.53f,26),RegimentUpgradeChoices.Comparison(current[i],after[i]),12,false,changed);
                GUI.contentColor=color;CommandUI.Rule(r.x,y+27,r.width);
            }
        }
        void DrawSkills(Rect r,RegimentRecord unit,IRegimentSource source)
        {
            int columns=r.width>=850?2:1;float span=(r.width-22-(columns-1)*10)/columns;
            var order=RegimentUpgradeChoices.Skills(unit);
            contentScroll=GUI.BeginScrollView(r,contentScroll,new Rect(0,0,r.width-14,76+Mathf.CeilToInt(order.Length/(float)columns)*135));
            CommandUI.Label(new Rect(0,0,r.width-26,38),"TRAINING / "+unit.Name+" / "+unit.Progression.AvailablePoints+" stat points available",14,false,true,true);
            CommandUI.Label(new Rect(0,40,r.width-26,29),"One point per improvement. Applies to this regiment; gear-group checks do not apply.",12,true,false,true);
            for(int i=0;i<order.Length;i++)
            {
                var skill=order[i];var action=source.Skill(new[]{focus},skill);var next=RegimentUpgradeChoices.WithSkill(unit,skill);
                var card=new Rect(i%columns*(span+10),76+i/columns*135,span,125);if(CommandUI.Button(card,"",action.Enabled))Run(source.Skill(new[]{focus},skill));
                bool max=unit.Progression.Level(skill)>=UnitProgression.MaxSkill;
                CommandUI.Label(new Rect(card.x+12,card.y+8,span-164,25),UnitProgression.StatName(skill)+" / "+unit.Progression.Level(skill)+" of "+UnitProgression.MaxSkill,15,false,true);
                string changes=RegimentUpgradeChoices.Changes(unit,next);
                CommandUI.Label(new Rect(card.x+12,card.y+39,span-164,40),changes==""?UnitProgression.SkillEffect(skill):changes,12,true,false,true);
                CommandUI.Label(new Rect(card.x+12,card.y+86,span-24,31),max?"Maximum training reached.":action.Enabled?"Immediate · no money or materials needed":action.Reason,11,true,false,true);
                CommandUI.Label(new Rect(card.xMax-145,card.y+25,132,42),max?"Maximum":"Train +1\n1 stat point",12,false,true,true);
                if(card.Contains(Event.current.mousePosition)&&action.Enabled)trainingPreview=next;
            }
            GUI.EndScrollView();
        }
        void DrawAbilities(Rect r,RegimentRecord unit,IRegimentSource source)
        {
            if(CommandUI.Button(new Rect(r.x,r.y,142,30),"Learnable now",selected:readyAbilitiesOnly)){readyAbilitiesOnly=!readyAbilitiesOnly;contentScroll=Vector2.zero;}
            CommandUI.Label(new Rect(r.x+156,r.y,r.width-156,36),unit.Progression.AvailableSkillPoints+" ability points / "+unit.Name,13,true,false,true);
            var abilities=RegimentAbilities.All.Where(a=>a.Supports(unit.Users)).Where(a=>!readyAbilitiesOnly||source.Ability(new[]{focus},a).Enabled).ToArray();
            var viewport=new Rect(r.x,r.y+46,r.width,r.height-46);float span=r.width-22;
            contentScroll=GUI.BeginScrollView(viewport,contentScroll,new Rect(0,0,r.width-14,Math.Max(viewport.height-1,abilities.Length*190)));
            for(int i=0;i<abilities.Length;i++)
            {
                var ability=abilities[i];var action=source.Ability(new[]{focus},ability);int rank=unit.Progression.AbilityRank(ability.Id);bool max=rank>=RegimentAbilities.MaxRank;
                var card=new Rect(0,i*190,span,181);if(CommandUI.Button(card,"",action.Enabled))Run(source.Ability(new[]{focus},ability));
                MilitaryInsignia.Pin(new Rect(12,card.y+13,42,45),(int)ability.Id,rank>0);
                MilitaryInsignia.Pips(new Rect(12,card.y+68,42,5),rank,RegimentAbilities.MaxRank);
                CommandUI.Label(new Rect(67,card.y+9,span-232,31),ability.Name+" / rank "+rank+" of "+RegimentAbilities.MaxRank,15,false,true,true);
                string effect=RegimentUpgradeChoices.AbilityEffect(ability.Id,rank)+(max?"":" → "+RegimentUpgradeChoices.AbilityEffect(ability.Id,rank+1));
                CommandUI.Label(new Rect(67,card.y+44,span-232,40),effect,12,false,true,true);
                CommandUI.Label(new Rect(12,card.y+91,span-24,53),ability.Description,12,true,false,true);
                CommandUI.Label(new Rect(12,card.y+151,span-24,25),max?"Mastered":action.Enabled?"Next rank costs 1 ability point":action.Reason,11,true,false,true);
                CommandUI.Label(new Rect(span-153,card.y+26,140,47),max?"Mastered":(rank==0?"Learn":"Rank up")+" / 1 point",12,false,true,true);
            }
            if(abilities.Length==0)CommandUI.Label(new Rect(6,10,span-12,70),"No abilities can be learned now. Turn off Learnable now to see point requirements.",13,true,false,true);
            GUI.EndScrollView();
        }
        void DrawServices(Rect r,RegimentRecord unit,IRegimentSource source)
        {
            if(CommandUI.Button(new Rect(r.x,r.y,126,30),"Ready only",selected:readyServicesOnly)){readyServicesOnly=!readyServicesOnly;contentScroll=Vector2.zero;}
            CommandUI.Label(new Rect(r.x+138,r.y,r.width-138,46),unit.Status,12,true,false,true);
            CommandUI.Label(new Rect(r.x,r.y+50,r.width,28),"TRAINING & SERVICE / "+unit.Name,12,false,true);
            var actions=source.Services(new[]{focus}).Where(a=>!readyServicesOnly||a.Enabled).ToArray();
            var view=new Rect(r.x,r.y+87,r.width,r.height-87);float span=r.width-22;
            contentScroll=GUI.BeginScrollView(view,contentScroll,new Rect(0,0,r.width-14,Math.Max(view.height-1,actions.Length*166)));
            for(int i=0;i<actions.Length;i++)
            {
                var action=actions[i];var card=new Rect(0,i*166,span,156);if(CommandUI.Button(card,"",action.Enabled))Run(action);
                CommandUI.Label(new Rect(12,card.y+8,span-201,32),action.Title,16,false,true,true);
                CommandUI.Label(new Rect(12,card.y+47,span-201,58),action.Description,12,true,false,true);
                CommandUI.Label(new Rect(span-173,card.y+10,160,48),action.Cost,13,false,true,true);
                CommandUI.Label(new Rect(span-173,card.y+70,160,36),action.Enabled?"Click to apply":"Unavailable",12,false,true);
                CommandUI.Label(new Rect(12,card.y+115,span-24,33),action.Enabled?"Ready to order for this regiment.":action.Reason,12,true,false,true);
            }
            if(actions.Length==0)CommandUI.Label(new Rect(8,12,span-16,65),"No service orders are available with this filter. Turn off Ready only to inspect requirements.",13,true,false,true);
            GUI.EndScrollView();
        }
    }
}
