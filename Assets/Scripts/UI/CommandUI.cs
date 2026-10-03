using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

namespace Empire.UI
{
    // Small, cached controls shared by front end, company, campaign and tactical overlays.
    public static class CommandUI
    {
        public static string Number(long value)=>value.ToString("N0",CultureInfo.InvariantCulture);
        static readonly Dictionary<string,GUIStyle> styles=new Dictionary<string,GUIStyle>();
        public static GUIStyle Text(int size=14,bool muted=false,bool bold=false,bool wrap=false)
        {
            string key=$"{size}/{muted}/{bold}/{wrap}";
            if(!styles.TryGetValue(key,out var style)){var color=muted?EmpireTheme.Muted:EmpireTheme.Ivory;style=bold&&size>=22?EmpireTheme.Display(size,color):EmpireTheme.Text(size,color,bold?FontStyle.Bold:FontStyle.Normal);style.wordWrap=wrap;styles[key]=style;}
            return style;
        }
        static GUIStyle button,selectedButton;
        public static GUIStyle ButtonStyle
        {
            get
            {
                if(button==null){button=EmpireTheme.ButtonStyle(13);button.padding=new RectOffset(8,8,3,3);button.wordWrap=true;
                    selectedButton=new GUIStyle(button){normal=button.onNormal,hover=button.onHover};}
                return button;
            }
        }
        public static bool Button(Rect r,string label,bool enabled=true,bool selected=false,string hint="",UITooltip tips=null)
        {
            var style=ButtonStyle;tips?.Add(r,hint);
            bool prior=GUI.enabled;GUI.enabled=prior&&enabled;
            // Custom tooltips own their lifecycle; do not leave their text in Unity's global tooltip.
            bool clicked=GUI.Button(r,new GUIContent(label,tips==null?hint:""),selected?selectedButton:style);GUI.enabled=prior;return clicked;
        }
        public static void Label(Rect r,string value,int size=14,bool muted=false,bool bold=false,bool wrap=false)=>GUI.Label(r,value??"",Text(size,muted,bold,wrap));
        public static void Rule(float x,float y,float width)=>EmpireTheme.Fill(new Rect(x,y,width,1),EmpireTheme.Line);
        public static void Meter(Rect r,float fraction,Color? color=null)
        {EmpireTheme.Fill(r,EmpireTheme.Raised);EmpireTheme.Fill(new Rect(r.x,r.y,r.width*Mathf.Clamp01(fraction),r.height),color??EmpireTheme.Brass);}
        public static void Eyebrow(Rect r,string value){var old=GUI.contentColor;GUI.contentColor=EmpireTheme.Brass;Label(r,value.ToUpperInvariant(),11,false,true);GUI.contentColor=old;}
        public static Rect Inset(Rect r,float n)=>new Rect(r.x+n,r.y+n,r.width-n*2,r.height-n*2);
        public static float Scale(float width,float height,float minWidth=1180,float minHeight=800)=>Mathf.Max(.2f,Mathf.Min(Mathf.Min(width/minWidth,height/minHeight),GamePreferences.InterfaceScale));
        public static void Backdrop(float w,float h)
        {
            EmpireTheme.Fill(new Rect(0,0,w,h),EmpireTheme.Ink);
            // Book-cover rules leave the content quiet and give every management page a shared frame.
            EmpireTheme.Fill(new Rect(8,8,w-16,1),EmpireTheme.Line);
            EmpireTheme.Fill(new Rect(8,h-9,w-16,1),EmpireTheme.Line);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){styles.Clear();button=selectedButton=null;}
    }
    public sealed class UITooltip
    {
        string candidate="",last="";float since;
        public void Begin(){candidate="";}
        public void Add(Rect r,string value){if(!string.IsNullOrEmpty(value)&&r.Contains(Event.current.mousePosition))candidate=value;}
        public void Draw(Rect bounds,bool includeUnity=false)
        {
            if(includeUnity&&candidate==""&&!string.IsNullOrEmpty(GUI.tooltip))candidate=GUI.tooltip;
            if(candidate!=last){last=candidate;since=Time.realtimeSinceStartup;}
            if(!GamePreferences.Tooltips||candidate==""||Time.realtimeSinceStartup-since<.28f||Event.current.type!=EventType.Repaint)return;
            float width=Mathf.Min(350,bounds.width-24);var style=CommandUI.Text(13,false,false,true);
            float height=Mathf.Min(bounds.height-24,style.CalcHeight(new GUIContent(candidate),width-28)+24);
            var mouse=Event.current.mousePosition;
            var r=new Rect(Mathf.Clamp(mouse.x+18,bounds.x+12,bounds.xMax-width-12),Mathf.Clamp(mouse.y+24,bounds.y+12,bounds.yMax-height-12),width,height);
            bool enabled=GUI.enabled;GUI.enabled=true;EmpireTheme.Panel(r,EmpireTheme.Ink);
            EmpireTheme.Fill(new Rect(r.x,r.y,3,r.height),EmpireTheme.Brass);GUI.Label(CommandUI.Inset(r,12),candidate,style);GUI.enabled=enabled;
        }
    }
}
