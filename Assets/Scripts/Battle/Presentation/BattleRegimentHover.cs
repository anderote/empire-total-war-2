using System;
using Empire.UI;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleView
    {
        GUIStyle hoverTitle,hoverDetails;

        internal float RegimentAmmunitionFraction(BattalionState unit)
        {
            if(unit.StartingAmmunition<=0||unit.AliveCount<=0)return 0;
            float remaining=unit.Ammunition;
            if(unit.Weapon!=null&&RifleFireTiming.IndependentFire(unit))
            {
                long rounds=0;int alive=0;
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                    if(world.Soldiers.Alive[i]){rounds+=world.Soldiers.RoundsRemaining[i];alive++;}
                remaining=alive>0?rounds/(float)alive:0;
            }
            return Mathf.Clamp01(remaining/unit.StartingAmmunition);
        }

        void DrawRegimentHoverTip()
        {
            if(!EnableInput||Event.current.type!=EventType.Repaint||selecting||ordering||Input.GetMouseButton(1)||Input.GetMouseButton(2))return;
            Vector2 mouse=Input.mousePosition,guiMouse=new Vector2(mouse.x,Screen.height-mouse.y);
            int id=hud!=null?hud.HoveredRegimentAt(guiMouse):-1;
            if(id<0)
            {
                if(PointerOverUI(guiMouse))return;
                id=PickBattalion(mouse,ArmySide.French,true);
                if(id<0)id=PickBattalion(mouse,ArmySide.British,true);
            }
            if(id<0)return;
            var unit=world.Battalions[id];
            int factionIndex=Array.IndexOf(UnitRoster.Factions,unit.Faction);
            string faction=factionIndex>=0?UnitRoster.FactionNames[factionIndex]:
                unit.Side==ArmySide.French?world.Config.PlayerCountryName:world.Config.EnemyCountryName;
            string ammo=UnitRoster.Shoots(unit.Role)?$"{RegimentAmmunitionFraction(unit)*100:0}%":"N/A";
            string morale=unit.Routing?"Routing":unit.MoraleReason;
            string details=$"Faction: {faction}\nType: {UnitRoster.Label(unit.Role)}\nTroops: {unit.AliveCount:N0} / {unit.SoldierCount:N0}\nAmmunition: {ammo}\nFatigue: {unit.Fatigue:0}%\nMorale: {unit.Morale:0} / 100 · {morale}";
            if(world.CanFixBayonets(unit))details+=unit.BayonetsFixed?"\nBayonets: Fixed · reload +25%":"\nBayonets: Unfixed · weaker melee";
            hoverTitle??=EmpireTheme.Text(14,EmpireTheme.Ivory,FontStyle.Bold);
            hoverDetails??=EmpireTheme.Text(12,EmpireTheme.Ivory);
            hoverTitle.wordWrap=true;hoverDetails.wordWrap=true;
            float scale=BattleUILayout.Scale(Screen.width,Screen.height);
            float width=Mathf.Min(300,Screen.width/scale-16);
            float titleHeight=hoverTitle.CalcHeight(new GUIContent(unit.Name),width-24);
            float height=titleHeight+hoverDetails.CalcHeight(new GUIContent(details),width-24)+32;
            Vector2 pointer=guiMouse/scale;
            float x=pointer.x+18,y=pointer.y+20;
            if(x+width>Screen.width/scale-8)x=pointer.x-width-18;
            if(y+height>Screen.height/scale-8)y=pointer.y-height-16;
            var rect=new Rect(Mathf.Clamp(x,8,Mathf.Max(8,Screen.width/scale-width-8)),Mathf.Clamp(y,8,Mathf.Max(8,Screen.height/scale-height-8)),width,height);
            var matrix=GUI.matrix;int depth=GUI.depth;
            try
            {
                GUI.depth=-100;
                GUI.matrix=matrix*Matrix4x4.Scale(new Vector3(scale,scale,1));
                EmpireTheme.Panel(rect,EmpireTheme.Ink);
                EmpireTheme.Fill(new Rect(rect.x,rect.y,rect.width,2),EmpireTheme.Brass);
                GUI.Label(new Rect(rect.x+12,rect.y+10,width-24,titleHeight),unit.Name,hoverTitle);
                GUI.Label(new Rect(rect.x+12,rect.y+titleHeight+18,width-24,height-titleHeight-22),details,hoverDetails);
            }
            finally{GUI.matrix=matrix;GUI.depth=depth;}
        }
    }
}
