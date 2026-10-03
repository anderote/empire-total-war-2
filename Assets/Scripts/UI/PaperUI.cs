using System;
using UnityEngine;

namespace Empire.UI
{
    /// <summary>Atlas Paper: shared palette and controls. Design contract: Docs/PAPER_UI_STYLE.md.</summary>
    public static class PaperUI
    {
        public const string StyleName="Atlas Paper";
        public static readonly Color Paper=Hex(0xF0ECDF),PaperLight=Hex(0xFAF7EF),Inset=Hex(0xE5E0D2);
        public static readonly Color Ink=Hex(0x34372F),Muted=Hex(0x696C60),Rule=Hex(0xABA99A);
        public static readonly Color Accent=Hex(0x967B4F),Selection=Hex(0xE3EAF0),BlueInk=Hex(0x0E4591),RedInk=Hex(0xC21F1A);
        public static readonly Color Good=Hex(0x386447),Warning=Hex(0x8A632F),Danger=Hex(0xA13B32);
        public const int Caption=12,Body=14,Heading=24;
        public const float RuleWidth=1,Spacing=8,PanelPadding=20,ControlHeight=36;
        static GUISkin skin;
        static Color Hex(uint rgb)=>new Color(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f);
        public static GUIStyle Text(int size=Body,bool muted=false,bool bold=false)=>EmpireTheme.Text(size,muted?Muted:Ink,bold?FontStyle.Bold:FontStyle.Normal);
        public static GUIStyle Title(int size=Heading)=>EmpireTheme.Display(size,Ink);
        static Texture2D Tile(Color fill,Color border)
        {
            var image=new Texture2D(3,3,TextureFormat.RGBA32,false){name="Atlas Paper control",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point};
            var pixels=new Color[9];for(int i=0;i<9;i++)pixels[i]=EmpireTheme.RenderColor(i==4?fill:border);
            image.SetPixels(pixels);image.Apply(false,true);return image;
        }
        static void State(GUIStyleState state,Texture2D image,Color color){state.background=image;state.textColor=color;}
        static GUISkin Skin
        {
            get
            {
                if(skin)return skin;
                skin=UnityEngine.Object.Instantiate(GUI.skin);skin.name=StyleName;skin.hideFlags=HideFlags.HideAndDontSave;
                skin.label=Text();skin.label.wordWrap=true;
                Texture2D normal=Tile(PaperLight,Rule),hover=Tile(Selection,Rule),active=Tile(Selection,BlueInk),disabled=Tile(Inset,Rule);
                var button=Text();button.alignment=TextAnchor.MiddleCenter;button.padding=new RectOffset(12,12,6,6);button.margin=new RectOffset(0,6,3,3);button.border=new RectOffset(1,1,1,1);
                State(button.normal,normal,Ink);State(button.hover,hover,Ink);State(button.active,active,Ink);State(button.focused,active,Ink);
                State(button.onNormal,active,Ink);State(button.onHover,active,Ink);State(button.onActive,active,Ink);State(button.onFocused,active,Ink);
                skin.button=button;skin.toggle=new GUIStyle(button){alignment=TextAnchor.MiddleLeft};
                skin.textField=new GUIStyle(button){alignment=TextAnchor.MiddleLeft};skin.textArea=new GUIStyle(skin.textField){wordWrap=true};
                skin.box=new GUIStyle(button);State(skin.box.normal,disabled,Ink);
                Texture2D track=Tile(Inset,Inset),thumb=Tile(Rule,Rule);
                foreach(var bar in new[]{skin.verticalScrollbar,skin.horizontalScrollbar})State(bar.normal,track,Ink);
                foreach(var bar in new[]{skin.verticalScrollbarThumb,skin.horizontalScrollbarThumb}){State(bar.normal,thumb,Ink);State(bar.hover,active,Ink);State(bar.active,active,Ink);}
                skin.verticalScrollbar.fixedWidth=7;skin.horizontalScrollbar.fixedHeight=7;
                skin.settings.cursorColor=EmpireTheme.RenderColor(Ink);skin.settings.selectionColor=EmpireTheme.RenderColor(Selection);
                return skin;
            }
        }
        public static GUIStyle Button(int size=Body,TextAnchor alignment=TextAnchor.MiddleCenter)=>new GUIStyle(Skin.button){fontSize=size,alignment=alignment};
        public static bool Toggle(bool value,string label)=>GUILayout.Toggle(value,(value?"✓  ":"–  ")+label,Skin.toggle);
        public static void Panel(Rect rect,bool selected=false,bool hover=false)
        {
            EmpireTheme.Fill(new Rect(rect.x+2,rect.y+3,rect.width,rect.height),new Color(0,0,0,.13f));
            EmpireTheme.Fill(rect,selected?Selection:hover?PaperLight:Paper);
            EmpireTheme.Fill(new Rect(rect.x,rect.y,rect.width,1),Rule);EmpireTheme.Fill(new Rect(rect.x,rect.yMax-1,rect.width,1),Rule);
            EmpireTheme.Fill(new Rect(rect.x,rect.y,1,rect.height),Rule);EmpireTheme.Fill(new Rect(rect.xMax-1,rect.y,1,rect.height),Rule);
            if(selected)EmpireTheme.Fill(new Rect(rect.x,rect.y,3,rect.height),BlueInk);
        }
        public readonly struct Scope:IDisposable
        {
            readonly GUISkin previous;
            public Scope(bool apply){previous=GUI.skin;if(apply)GUI.skin=Skin;}
            public void Dispose(){GUI.skin=previous;}
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            if(!skin)return;
            var textures=new System.Collections.Generic.HashSet<Texture2D>();
            foreach(var style in new[]{skin.button,skin.box,skin.verticalScrollbar,skin.verticalScrollbarThumb})
                foreach(var state in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal})if(state.background)textures.Add(state.background);
            foreach(var texture in textures)UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(skin);skin=null;
        }
    }
}
