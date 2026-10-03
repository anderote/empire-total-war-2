using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace Empire.UI
{
    // Paintings contain artwork only. Names, frames, stats and comparisons stay live UI.
    public static class CampaignArt
    {
        public static readonly Color Paper=PaperUI.Paper;
        public static readonly Color PaperLight=PaperUI.PaperLight;
        public static readonly Color Ink=PaperUI.Ink;
        public static readonly Color Muted=PaperUI.Muted;
        public static readonly Color RuleColor=PaperUI.Rule;
        public static readonly Color Improvement=PaperUI.Good;
        public static readonly Color Reduction=PaperUI.Danger;
        static readonly Dictionary<string,Texture2D> paintings=new Dictionary<string,Texture2D>();
        static readonly Dictionary<string,GUIStyle> styles=new Dictionary<string,GUIStyle>();

        static Texture2D Load(string key)
        {
            if(!paintings.TryGetValue(key,out var texture))
                paintings[key]=texture=Resources.Load<Texture2D>("UI/CampaignArt/"+key);
            return texture;
        }
        // This first bounded integration only binds verified base Line portraits.
        // Do not relabel another role, cartridge uniform or tier tint with unverified art.
        public static string PortraitKey(RegimentRecord unit)
        {
            if(unit==null||unit.Users==EquipmentUsers.Artillery||unit.BattleRole!=UnitRole.Line)return null;
            if(RegimentBaseStats.Uniform(unit.Tier)!=Vector4.zero)return null;
            string weapon=unit.Loadout?.items.Select(UnitEquipment.Find).FirstOrDefault(i=>i!=null&&!string.IsNullOrEmpty(i.Weapon))?.Weapon??unit.Weapon;
            if(weapon=="breechloader"||weapon=="magazine"||weapon=="smokeless")return null;
            string faction=UnitRoster.ArtFaction(unit.Faction);
            return faction=="france"||faction=="britain"||faction=="ming"||faction=="india"?"Portraits/"+faction+"-line-base":null;
        }
        public static bool HasPortrait(RegimentRecord unit)=>PortraitKey(unit) is string key&&Load(key);
        // Stable art slots. A matching role/weapon image is never borrowed from another unit type.
        public static string UnitCardKey(RegimentRecord unit)=>unit==null?null:
            "Units/"+UnitRoster.ArtFaction(unit.Faction)+"/"+unit.BattleRole.ToString().ToLowerInvariant()+"-"+(unit.Weapon??"musket");
        public static bool Image(Rect r,string key,bool framed=false)
        {
            var texture=string.IsNullOrEmpty(key)?null:Load(key);if(!texture)return false;
            if(framed)Painting(r,texture);
            else {var previous=GUI.color;GUI.color=Color.white;GUI.DrawTexture(r,texture,ScaleMode.ScaleToFit,true);GUI.color=previous;}
            return true;
        }
        public static bool UnitCard(Rect r,RegimentRecord unit)
        {
            if(unit==null||!string.IsNullOrEmpty(unit.Loadout?.appearance)||RegimentBaseStats.Uniform(unit.Tier)!=Vector4.zero)return false;
            return Image(r,UnitCardKey(unit),true);
        }
        public static void PaperPanel(Rect r,bool selected=false,bool hover=false)
        {
            PaperUI.Panel(r,selected,hover);
        }
        public static void Rule(float x,float y,float width)=>EmpireTheme.Fill(new Rect(x,y,width,1),RuleColor);
        static GUIStyle Style(int size,bool muted,bool bold,bool display,bool wrap)
        {
            string key=size+"/"+muted+"/"+bold+"/"+display+"/"+wrap;
            if(!styles.TryGetValue(key,out var style))
            {
                style=display?EmpireTheme.Display(size,muted?Muted:Ink):EmpireTheme.Text(size,muted?Muted:Ink,bold?FontStyle.Bold:FontStyle.Normal);
                style.wordWrap=wrap;styles[key]=style;
            }
            return style;
        }
        public static void Label(Rect r,string text,int size=12,bool muted=false,bool bold=false,bool display=false,bool wrap=false,Color? tint=null)
        {
            var old=GUI.contentColor;GUI.contentColor=Color.white;
            var style=Style(size,muted,bold,display,wrap);
            if(tint.HasValue){style=new GUIStyle(style);style.normal.textColor=tint.Value;}
            GUI.Label(r,text??"",style);GUI.contentColor=old;
        }
        static void Painting(Rect r,Texture2D texture)
        {
            EmpireTheme.Fill(r,new Color(.20f,.16f,.10f));
            EmpireTheme.Fill(CommandUI.Inset(r,1),EmpireTheme.Brass);
            EmpireTheme.Fill(CommandUI.Inset(r,3),new Color(.34f,.26f,.14f));
            EmpireTheme.Fill(CommandUI.Inset(r,5),new Color(.81f,.67f,.41f));
            var image=CommandUI.Inset(r,7);
            var old=GUI.color;GUI.color=Color.white;
            GUI.DrawTexture(image,texture,ScaleMode.ScaleAndCrop,false);GUI.color=old;
        }
        public static void Portrait(Rect r,RegimentRecord unit,bool caption=true)
        {
            string key=PortraitKey(unit);var texture=key==null?null:Load(key);
            if(texture){Painting(r,texture);return;}
            PaperPanel(r);
            if(caption)
            {
                Label(new Rect(r.x+8,r.center.y-32,r.width-16,32),unit?.Role??"Regiment",15,false,false,true,true);
                Label(new Rect(r.x+8,r.center.y+4,r.width-16,35),"Portrait awaiting commission",10,true,false,false,true);
            }
            else MilitaryInsignia.Rank(CommandUI.Inset(r,6),unit.Progression.Rank);
        }
        public static bool Building(Rect r,string kind,int level)
        {
            if(string.IsNullOrEmpty(kind)||level<1)return false;
            var texture=Load("Buildings/"+kind+"-level-"+level);
            if(!texture)return false;
            Painting(r,texture);return true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){paintings.Clear();styles.Clear();}
    }
}
