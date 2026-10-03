using System;
using System.Collections.Generic;
using System.Linq;
using Empire.UI;
using EmpireBattle;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    // Both the printed values and the comparison use the same additive BattlePower metric.
    // This presentation never samples the campaign PRNG or changes autoresolve rules.
    public readonly struct CampaignPowerComparison
    {
        public readonly double Left,Right;
        public readonly bool Known;
        public readonly float LeftShare;
        public CampaignPowerComparison(double left,double right)
        {
            Known=Valid(left)&&Valid(right);Left=Known?left:0;Right=Known?right:0;
            double scale=Math.Max(Left,Right);
            LeftShare=Known&&scale>0?(float)((Left/scale)/(Left/scale+Right/scale)):.5f;
        }
        static bool Valid(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n)&&n>=0;
        public bool Empty=>Known&&Left==0&&Right==0;
    }

    public sealed partial class EuropeMapView
    {
        Vector2 attackCardsScroll,defenceCardsScroll,recruitCardsScroll;
        string rosterEncounter="",recruitCardRole="Regular";
        readonly Dictionary<string,RegimentPortrait> campaignCardActors=new Dictionary<string,RegimentPortrait>();
        RegimentPortrait attackRepresentative,defenceRepresentative;
        static readonly string[] recruitCardRoles={"Regular","Militia","Light","Rifle","Guard"};
        static readonly string[] recruitCardNames={"Line Infantry","Militia","Light Infantry","Rifles","Guards"};
        static readonly string[] recruitCardDescriptions={"Musket infantry trained to form square.","Lower-cost infantry; formation courses available.","Musket skirmishers with dispersed formation drill.","Long-range flintlock specialists; slower loading.","Elite infantry cadre."};

        RegimentRecord CampaignCardRecord(CampaignRegiment unit,string faction)=>new RegimentRecord
        {
            Id=unit.id,Name=string.IsNullOrEmpty(unit.name)?unit.id:unit.name,Faction=faction,Role=UnitRoster.Label(UnitRoster.CampaignRole(unit.role,unit.weapon)),
            BattleRole=UnitRoster.CampaignRole(unit.role,unit.weapon),Users=UnitEquipment.CampaignUsers(unit.role),
            Strength=unit.soldiers,Capacity=countryCampaign.Balance.unitSize,Quality=unit.equipment,
            Progression=unit.progression,Loadout=unit.loadout,Weapon=unit.weapon
        };
        RegimentRecord CampaignRecruitRecord(string role)=>new RegimentRecord
        {
            Id="recruit-preview/"+role,Faction=countryCampaign.Save.playerFaction,Name=recruitCardNames[Array.IndexOf(recruitCardRoles,role)],
            Role=recruitCardNames[Array.IndexOf(recruitCardRoles,role)],BattleRole=UnitRoster.CampaignRole(role,role=="Rifle"?"rifle":"musket"),
            Users=UnitEquipment.CampaignUsers(role),Strength=countryCampaign.Balance.unitSize,Capacity=countryCampaign.Balance.unitSize,
            Progression=new UnitProgression(),Loadout=new UnitLoadout(),Weapon=role=="Rifle"?"rifle":"musket"
        };
        RegimentRecord CampaignLineRepresentative(string faction,string id)=>new RegimentRecord
        {
            Id=id,Faction=faction,Name="Line infantry",Role="Line infantry",BattleRole=UnitRole.Line,Users=EquipmentUsers.Infantry,
            Strength=1,Capacity=1,Progression=new UnitProgression(),Loadout=new UnitLoadout(),Weapon="musket"
        };
        void DrawCampaignUnitArt(Rect art,RegimentRecord unit,string key)
        {
            if(CampaignArt.UnitCard(art,unit))return;
            if(string.IsNullOrEmpty(unit.Loadout?.appearance)&&CampaignArt.HasPortrait(unit)){CampaignArt.Portrait(art,unit);return;}
            // Uncommissioned artwork uses the matching production model, never another role's painting.
            // Draw only on repaint so the portrait's drag controls do not steal card selection or scrolling.
            if(Event.current.type!=EventType.Repaint)return;
            if(!campaignCardActors.TryGetValue(key,out var portrait)||!portrait)campaignCardActors[key]=portrait=RegimentPortrait.Create();
            portrait.Draw(art,unit);
            CampaignArt.Label(new Rect(art.x+5,art.yMax-19,art.width-10,17),"MODEL STUDY",9,false,true,false,false,EmpireTheme.Ivory);
        }
        void DrawCampaignUnitCard(Rect card,RegimentRecord unit,string key,bool selected=false,bool clickable=false)
        {
            CampaignArt.PaperPanel(card,selected,clickable&&card.Contains(Event.current.mousePosition));
            DrawCampaignUnitArt(new Rect(card.x+5,card.y+5,card.width-10,card.height-77),unit,key);
            CampaignArt.Label(new Rect(card.x+9,card.yMax-68,card.width-18,27),unit.Name,12,false,true,false,true);
            CampaignArt.Label(new Rect(card.x+9,card.yMax-42,card.width-18,24),unit.Strength.ToString("N0"),24,false,false,true);
            CampaignArt.Label(new Rect(card.x+9,card.yMax-18,card.width-18,15),unit.Power.ToString("N0")+" BP",10,true);
        }
        bool CampaignPresentationAction(Rect rect,string title,string help,bool allowed=true)
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&allowed;
            bool pressed=GUI.Button(rect,new GUIContent(title,help),campaignAction);GUI.enabled=enabled;return pressed;
        }
        void DrawCampaignBattle(float w,float h)
        {
            var r=CampaignWindow(w,h);CampaignFrame(r);var e=countryCampaign.Save.pending;if(e==null)return;
            if(rosterEncounter!=e.id){rosterEncounter=e.id;attackCardsScroll=defenceCardsScroll=Vector2.zero;}
            bool attacking=e.attacker==countryCampaign.Save.playerFaction;
            var attackers=countryCampaign.BattleUnits(e,true).Select(u=>CampaignCardRecord(u,e.attacker)).ToArray();
            var defenders=countryCampaign.BattleUnits(e,false).Select(u=>CampaignCardRecord(u,e.defender)).ToArray();
            var comparison=new CampaignPowerComparison(attackers.Sum(u=>u.Power),defenders.Sum(u=>u.Power));
            var city=Array.Find(catalog.regions,t=>t.id==e.regionId)?.capital.name??e.regionId;
            GUI.Label(new Rect(r.x+24,r.y+16,r.width-48,35),(attacking?"ATTACK / ":"DEFEND / ")+city.ToUpperInvariant(),campaignTitle);
            float gap=14,paneWidth=(r.width-48-gap)/2;
            DrawCampaignForceSummary(new Rect(r.x+24,r.y+62,paneWidth,184),e,true,attackers,comparison.Known?comparison.Left:double.NaN);
            DrawCampaignForceSummary(new Rect(r.x+24+paneWidth+gap,r.y+62,paneWidth,184),e,false,defenders,comparison.Known?comparison.Right:double.NaN);
            var bar=new Rect(r.x+24,r.y+263,r.width-48,18);
            EmpireTheme.Fill(bar,EmpireTheme.Line);
            if(comparison.Known&&!comparison.Empty)
            {
                float split=bar.width*comparison.LeftShare;
                EmpireTheme.Fill(new Rect(bar.x,bar.y,split,bar.height),catalog.Faction(e.attacker).MapColor);
                EmpireTheme.Fill(new Rect(bar.x+split,bar.y,bar.width-split,bar.height),catalog.Faction(e.defender).MapColor);
            }
            GUI.Label(new Rect(bar.x,bar.yMax+5,bar.width,20),new GUIContent(!comparison.Known?"Battle Power unavailable":comparison.Empty?"No Battle Power on either side":
                "RELATIVE BATTLE POWER  /  "+(comparison.LeftShare*100).ToString("F0")+"% : "+((1-comparison.LeftShare)*100).ToString("F0")+"%",BattlePower.Help),campaignCaption);
            bool manual=countryCampaign.CanFightManually(),withdraw=countryCampaign.CanWithdraw(out string retreatReason);
            bool siegeAllowed=countryCampaign.CanBesiege(out string siegeReason);
            float actionWidth=(r.width-48-24)/4;float actionY=r.y+313;
            if(CampaignPresentationAction(new Rect(r.x+24,actionY,actionWidth,36),"FIGHT BATTLE",manual?"Deploy both armies in a tactical battle.":"This encounter cannot be deployed manually; autoresolve remains available.",manual))
            {if(SaveCountry(false))CampaignBattleBridge.Launch(countryCampaign,CampaignBattleReturned);return;}
            if(CampaignPresentationAction(new Rect(r.x+32+actionWidth,actionY,actionWidth,36),"AUTORESOLVE","Resolve exchanges and morale from troop stats, supply, command and forts. Reloading preserves the same result.")&&countryCampaign.Resolve(e.id,out countryMessage)){CampaignBattleReturned();return;}
            if(CampaignPresentationAction(new Rect(r.x+40+actionWidth*2,actionY,actionWidth,36),attacking?"CANCEL ATTACK":"RETREAT",retreatReason,withdraw)&&countryCampaign.Withdraw(e.id,out countryMessage)){SaveCountry(false);return;}
            if(CampaignPresentationAction(new Rect(r.x+48+actionWidth*3,actionY,actionWidth,36),"BESIEGE",siegeReason,siegeAllowed)&&countryCampaign.Besiege(e.id,out countryMessage)){SaveCountry(false);return;}
            float rosterY=r.y+368,rosterHeight=Math.Max(105,r.height-452);
            DrawCampaignEncounterCards(new Rect(r.x+24,rosterY,paneWidth,rosterHeight),attackers,true);
            DrawCampaignEncounterCards(new Rect(r.x+24+paneWidth+gap,rosterY,paneWidth,rosterHeight),defenders,false);
            var note=new Rect(r.x+24,r.yMax-72,r.width-48,56);EmpireTheme.Panel(note,PaperUI.Inset);
            string tactical=!manual?"Manual battles support the main army and up to four reinforcing armies.":!e.fieldOnly&&countryCampaign.Buildings(e.regionId,"Fortress")>0?"Assault / breach the gate and hold the square for 20 seconds.":e.garrison.Count>0?"Town battle / hold the square for 20 seconds.":"Field battle / break the opposing army. Deployment can be cancelled.";
            GUI.Label(new Rect(note.x+10,note.y+5,note.width-20,20),tactical,small);
            string order=e.fieldOnly?(e.ambush?"Ambush":"Interception")+" / no settlement capture. ":"";
            GUI.Label(new Rect(note.x+10,note.y+28,note.width-20,24),order+(!withdraw?retreatReason:!siegeAllowed?"Battle Power compares starting troops; tactics, supply, command and forts affect resolution.":"Besiege to weaken the defenders. Battle Power is a troop comparison, not a win probability."),small);
        }
        void DrawCampaignForceSummary(Rect panel,CampaignEncounter e,bool attacker,RegimentRecord[] units,double power)
        {
            string faction=attacker?e.attacker:e.defender;CampaignArt.PaperPanel(panel);
            EmpireTheme.Fill(new Rect(panel.x,panel.y,3,panel.height),catalog.Faction(faction).MapColor);
            float artWidth=126;var art=new Rect(attacker?panel.x+6:panel.xMax-artWidth-6,panel.y+6,artWidth,panel.height-12);
            if(Event.current.type==EventType.Repaint)
            {
                if(attacker){if(!attackRepresentative)attackRepresentative=RegimentPortrait.Create();attackRepresentative.Draw(art,CampaignLineRepresentative(faction,"attack-line/"+faction));}
                else {if(!defenceRepresentative)defenceRepresentative=RegimentPortrait.Create();defenceRepresentative.Draw(art,CampaignLineRepresentative(faction,"defence-line/"+faction));}
            }
            float x=attacker?art.xMax+14:panel.x+16,width=panel.width-artWidth-36;
            CampaignArt.Label(new Rect(x,panel.y+12,width,35),CampaignFactionName(faction),21,false,false,true,true);
            CampaignArt.Label(new Rect(x,panel.y+50,width,41),units.Sum(u=>u.Strength).ToString("N0"),43,false,false,true);
            CampaignArt.Label(new Rect(x,panel.y+90,width,18),"MEN  /  "+units.Length+" REGIMENTS",10,true,true);
            CampaignArt.Label(new Rect(x,panel.y+114,width,25),double.IsNaN(power)?"BP unavailable":power.ToString("N0")+" BP",22,false,false,true);
            var armies=countryCampaign.EncounterArmies(e,attacker).Where(a=>a!=null).ToArray();
            string command=armies.Length==0?"Local garrison":string.Join(" · ",armies.Select(a=>string.IsNullOrEmpty(a.generalName)?a.name:a.generalName));
            CampaignArt.Label(new Rect(x,panel.y+145,width,30),command,11,true,false,false,true);
        }
        void DrawCampaignEncounterCards(Rect pane,RegimentRecord[] units,bool attacker)
        {
            GUI.Label(new Rect(pane.x,pane.y-17,pane.width,17),attacker?"ATTACKING ROSTER":"DEFENDING ROSTER",campaignCaption);
            const float cardWidth=132;float cardHeight=pane.height-18;
            Vector2 scroll=attacker?attackCardsScroll:defenceCardsScroll;
            scroll=GUI.BeginScrollView(pane,scroll,new Rect(0,0,Math.Max(pane.width-12,units.Length*(cardWidth+8)),cardHeight));
            for(int i=0;i<units.Length;i++)
            {
                var card=new Rect(i*(cardWidth+8),0,cardWidth,cardHeight);
                if(card.xMax>=scroll.x&&card.x<=scroll.x+pane.width)DrawCampaignUnitCard(card,units[i],"encounter/"+units[i].Id);
            }
            GUI.EndScrollView();if(attacker)attackCardsScroll=scroll;else defenceCardsScroll=scroll;
        }

        void DrawCampaignArmyTray(float w,float h)
        {
            var army=SelectedCampaignArmy();if(army==null)return;var r=CampaignTray(w,h);CampaignFrame(r);var actor=countryCampaign.Save.playerFaction;
            GUI.Label(new Rect(r.x+22,r.y+12,r.width-75,28),(army.name+" / "+(army.generalName==""?"No general":army.generalName)).ToUpperInvariant(),campaignTitle);
            if(GUI.Button(new Rect(r.xMax-44,r.y+12,28,27),"X",campaignAction)){selectedArmyId="";selectedRegimentId="";return;}
            var units=army.units.Select(u=>CampaignCardRecord(u,army.owner)).ToArray();
            GUI.Label(new Rect(r.x+22,r.y+45,r.width-44,23),army.Soldiers.ToString("N0")+" MEN  /  "+units.Sum(u=>u.Power).ToString("N0")+" BP  /  "+units.Length+" REGIMENTS",campaignCaption);
            var deck=new Rect(r.x+20,r.y+73,r.width-40,162);const float cardWidth=124;const float cardHeight=143;
            regimentScroll=GUI.BeginScrollView(deck,regimentScroll,new Rect(0,0,Math.Max(deck.width-12,units.Length*(cardWidth+8)),cardHeight));
            for(int i=0;i<units.Length;i++)
            {
                var unit=units[i];var card=new Rect(i*(cardWidth+8),0,cardWidth,cardHeight);bool focused=campaignRegimentSelection.Contains(unit.Id);
                if(card.xMax>=regimentScroll.x&&card.x<=regimentScroll.x+deck.width)DrawCampaignUnitCard(card,unit,"army/"+unit.Id,focused,true);
                if(GUI.Button(card,new GUIContent("",unit.Name+" / "+unit.Role+" / "+unit.Progression.RankName+" / Ctrl-click to select several; right-click land to detach and move."),GUIStyle.none))
                {
                    bool multiple=Event.current.control||Event.current.command||Event.current.shift;
                    if(!multiple)campaignRegimentSelection.Clear();
                    if(!campaignRegimentSelection.Add(unit.Id))campaignRegimentSelection.Remove(unit.Id);
                    if(Event.current.clickCount>1)selectedRegimentId=unit.Id;
                }
            }
            GUI.EndScrollView();
            GUILayout.BeginArea(new Rect(r.x+22,r.y+240,r.width-44,36));GUILayout.BeginHorizontal();
            var ground=countryCampaign.ArmyGround(army);
            GUILayout.Label(army.movement+" MP  ·  "+(army.supply*100).ToString("F0")+"% supply"+(ground.attrition>0&&!countryCampaign.AtSettlement(army)?"  ·  Attrition":""),small);
            if(campaignRegimentSelection.Count==1&&CampaignOrderButton("Inspect unit"))selectedRegimentId=campaignRegimentSelection.First();
            if(CampaignOrderButton(choosingCampaignDestination?"Choose destination…":"March / attack"))
            {choosingCampaignDestination=!choosingCampaignDestination;countryMessage=choosingCampaignDestination?"Click land, a city, or an army to march, attack or merge. Escape cancels.":"Destination selection cancelled.";}
            if(army.marchTarget!=""&&CampaignOrderButton("Halt")){if(countryCampaign.Halt(actor,army.id,out countryMessage))SaveCountry(false);}
            if(GUILayout.Button("Army actions",campaignAction)){campaignPopup="Army actions";campaignDialogScroll=Vector2.zero;}
            GUILayout.EndHorizontal();GUILayout.EndArea();
        }

        void DrawCampaignRecruitment(RegionState state,float width)
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;var receiving=c.Army(recruitTargetArmy);
            if(state.owner==actor&&c.Active(state.regionId))DrawCampaignRecruitSelection(state,width);
            if(receiving!=null)
            {
                bool recover=c.CanRecover(actor,receiving.id,out int soldiers,out int cost,out string why);
                GUILayout.Label(receiving.name+" / "+receiving.units.Count+" of 80 regiments",heading);
                GUILayout.Label("REPLACE LOSSES",campaignCaption);GUILayout.Label(why,small);
                if(CampaignOrderButton("Reinforce existing regiments / +"+soldiers+" soldiers / "+cost+" treasury",recover)&&c.Recover(actor,receiving.id,out countryMessage))SaveCountry(false);
                if(receiving.marchTarget!=""&&CampaignOrderButton("Halt march to receive reinforcements")&&c.Halt(actor,receiving.id,out countryMessage))SaveCountry(false);
                GUILayout.Space(14);
            }
            if(state.owner!=actor||!c.Active(state.regionId))
            {
                GUILayout.Label("Recruitment requires an owned city. Recruit a detachment at a barracks and march it to this army, then use Transfer / merge when they meet.",text);
                if(GUILayout.Button("Find a recruiting city",campaignAction))OpenRecruitingCities();
                return;
            }
            GUILayout.Label("RECRUIT NEW REGIMENTS",campaignCaption);
            GUILayout.Label(c.AvailableManpower(state.regionId).ToString("N0")+" available recruits  ·  "+state.economy.materials.ToString("F0")+" materials  ·  "+c.FreeTrainingPlaces(state.regionId)+" free training places",text);
            GUILayout.Label(string.IsNullOrEmpty(recruitTargetArmy)?"New regiments keep this town’s name and a separate number for each type.":"New regiments keep the selected army’s number block; transfers preserve their names.",small);
            GUILayout.Label("Join: "+(receiving!=null?receiving.name:string.IsNullOrEmpty(recruitTargetArmy)?"an available local army, or a new detachment":"unavailable army — choose another destination"),text);
            if(GUILayout.Button(chooseRecruitArmy?"Done choosing army":"Change receiving army",campaignAction)){chooseRecruitArmy=!chooseRecruitArmy;GUIUtility.ExitGUI();}
            if(chooseRecruitArmy)
            {
                if(GUILayout.Button("Assign to an available local force",campaignAction)){recruitTargetArmy="";chooseRecruitArmy=false;}
                foreach(var army in c.Save.armies.Where(a=>c.CanReceiveRecruits(a,actor,state.regionId)).ToArray())
                    if(GUILayout.Button(army.name+" / "+army.units.Count+" regiments",campaignAction)){recruitTargetArmy=army.id;chooseRecruitArmy=false;}
            }
            GUILayout.Label("Recruits train here. If the chosen army leaves or cannot receive them, they form a detachment in this city.",small);
            if(c.Buildings(state.regionId,"Barracks")==0)
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Build or upgrade barracks",campaignAction))OpenCampaignBuilding(state.regionId);
                if(GUILayout.Button("Find another recruiting city",campaignAction))OpenRecruitingCities();
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(12);
            GUILayout.Label("TRAINING QUEUE",campaignCaption);
            var queue=c.Save.recruitment.Where(q=>q.regionId==state.regionId&&q.owner==actor).ToArray();
            if(queue.Length==0)GUILayout.Label("No regiments training here.",small);
            foreach(var q in queue)
            {
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label(q.role+" / "+q.turnsRemaining+" turns"+(c.Besieged(q.regionId)||c.InRevolt(q.regionId)?" / PAUSED":""),text);
                var target=c.Army(q.targetArmyId);
                GUILayout.Label(string.IsNullOrEmpty(q.targetArmyId)?"Joins an available local force":target!=null&&c.CanReceiveRecruits(target,q.owner,q.regionId)?"For "+target.name:"Will form a local detachment",small);
                GUILayout.EndVertical();
                int refund=(q.paidCost>0?q.paidCost:c.RecruitCost(q.role))*q.turnsRemaining/Math.Max(1,q.totalTurns>0?q.totalTurns:c.Balance.trainingTurns);
                if(CampaignOrderButton("Cancel / +"+refund)&&c.CancelRecruitment(actor,q.id,out countryMessage))SaveCountry(false);
                GUILayout.EndHorizontal();GUILayout.Space(8);
            }
        }

        void DrawCampaignRecruitSelection(RegionState state,float width)
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;
            GUILayout.Label("CHOOSE A REGIMENT",campaignCaption);
            const float stride=160,cardHeight=174;
            recruitCardsScroll=GUILayout.BeginScrollView(recruitCardsScroll,GUILayout.Height(cardHeight+22));
            var deck=GUILayoutUtility.GetRect(Math.Max(width-12,recruitCardRoles.Length*stride),cardHeight);
            for(int i=0;i<recruitCardRoles.Length;i++)
            {
                string role=recruitCardRoles[i];var unit=CampaignRecruitRecord(role);
                var card=new Rect(deck.x+i*stride,deck.y,150,cardHeight);
                bool selected=recruitCardRole==role;
                DrawCampaignUnitCard(card,unit,"recruit/"+actor+"/"+role,selected,true);
                if(GUI.Button(card,new GUIContent("",recruitCardDescriptions[i]),GUIStyle.none))recruitCardRole=role;
            }
            GUILayout.EndScrollView();
            int index=Array.IndexOf(recruitCardRoles,recruitCardRole);if(index<0){index=0;recruitCardRole=recruitCardRoles[0];}
            bool allowed=c.CanRecruit(actor,state.regionId,recruitCardRole,recruitTargetArmy,out string reason);
            var detail=GUILayoutUtility.GetRect(width,152);CampaignArt.PaperPanel(detail);
            CampaignArt.Label(new Rect(detail.x+16,detail.y+10,detail.width-32,28),recruitCardNames[index],24,false,false,true);
            CampaignArt.Label(new Rect(detail.x+16,detail.y+43,detail.width-32,23),recruitCardDescriptions[index],14);
            CampaignArt.Label(new Rect(detail.x+16,detail.y+72,detail.width-32,21),c.Balance.unitSize+" men  /  "+CampaignRecruitRecord(recruitCardRole).Power.ToString("N0")+" BP  /  "+c.RecruitTurns(recruitCardRole,state.regionId)+" turns  /  "+c.RecruitMaterials(recruitCardRole)+" materials  /  "+c.Balance.armyUnitUpkeep+" upkeep per turn",12,true);
            float buttonWidth=Math.Min(280,detail.width*.42f);
            CampaignArt.Label(new Rect(detail.x+16,detail.y+105,detail.width-buttonWidth-44,38),reason,12,true,false,false,true);
            if(CampaignRectButton(new Rect(detail.xMax-buttonWidth-16,detail.y+104,buttonWidth,36),"Recruit / "+c.RecruitCost(recruitCardRole)+" treasury",allowed)&&c.Recruit(actor,state.regionId,recruitCardRole,recruitTargetArmy,out countryMessage))SaveCountry(false);
            GUILayout.Space(12);
        }
        void DrawCampaignRegimentDetails(float w,float h,CampaignArmy army,CampaignRegiment unit)
        {
            if(managementArmy!=army.id){managementArmy=army.id;campaignManagement.Select(unit.id);}
            else if(campaignManagement.FocusedId!=selectedRegimentId)campaignManagement.Select(selectedRegimentId);
            EmpireTheme.Fill(new Rect(0,0,w,h),new Color(.02f,.03f,.035f,.94f));
            CommandUI.Label(new Rect(30,20,w-300,44),"REGIMENT MANAGEMENT",26,false,true);
            if(CommandUI.Button(new Rect(w-588,24,160,36),"Detach regiment",army.units.Count>1&&!countryCampaign.Committed(army)))
            {if(countryCampaign.Detach(army.owner,army.id,new[]{unit.id},out string detached,out countryMessage)){selectedArmyId=detached;selectedRegimentId="";SaveCountry(false);}return;}
            if(CommandUI.Button(new Rect(w-416,24,162,36),"Transfer regiment",!countryCampaign.Committed(army))){campaignPopup="Transfers";return;}
            if(CommandUI.Button(new Rect(w-242,24,212,36),"Return to campaign")){selectedRegimentId="";return;}
            campaignManagement.Draw(new Rect(30,82,w-60,h-112),new CampaignRegimentSource(countryCampaign,army,()=>SaveCountry(false)));
            selectedRegimentId=campaignManagement.FocusedId;
        }
    }
}
