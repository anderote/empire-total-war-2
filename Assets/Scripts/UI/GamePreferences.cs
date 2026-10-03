using System;
using UnityEngine;

namespace Empire.UI
{
    public static class GamePreferences
    {
        const string Prefix="ETW2.Options.";
        public static float MasterVolume=>PlayerPrefs.GetFloat(Prefix+"Volume",.85f);
        public static bool Music=>PlayerPrefs.GetInt(Prefix+"Music",1)==1;
        public static bool Tooltips=>PlayerPrefs.GetInt(Prefix+"Tooltips",1)==1;
        public static float InterfaceScale=>PlayerPrefs.GetFloat(Prefix+"Scale",1f);
        public static int FrameLimit=>PlayerPrefs.GetInt(Prefix+"Frames",60);
        public static bool VSync=>PlayerPrefs.GetInt(Prefix+"VSync",0)==1;
        public static void Set(string key,float value){PlayerPrefs.SetFloat(Prefix+key,value);PlayerPrefs.Save();Apply();}
        public static void Set(string key,int value){PlayerPrefs.SetInt(Prefix+key,value);PlayerPrefs.Save();Apply();}
        public static void Apply()
        {
            AudioListener.volume=MasterVolume;Application.targetFrameRate=FrameLimit;QualitySettings.vSyncCount=VSync?1:0;
            foreach(var audio in UnityEngine.Object.FindObjectsByType<EmpireBattle.BattleAudio>(FindObjectsSortMode.None))audio.MarchMusicEnabled=Music;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Initialize()
        {
            Apply();var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-screen-fullscreen")<0&&PlayerPrefs.HasKey(Prefix+"Fullscreen"))Screen.fullScreenMode=PlayerPrefs.GetInt(Prefix+"Fullscreen")==1?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed;
        }
    }
    public sealed class OptionsPanel
    {
        readonly UITooltip tips=new UITooltip();
        public bool Draw(Rect r)
        {
            tips.Begin();EmpireTheme.Panel(r,EmpireTheme.Surface);
            CommandUI.Eyebrow(new Rect(r.x+24,r.y+18,r.width-48,20),"Game settings");
            CommandUI.Label(new Rect(r.x+24,r.y+42,r.width-170,38),"Options",28,false,true);
            bool close=CommandUI.Button(new Rect(r.xMax-124,r.y+26,100,34),"Done",hint:"Return to the previous screen. Escape also closes options.",tips:tips);
            float y=r.y+110,x=r.x+24,right=r.x+r.width*.52f,rw=r.width*.48f-24;
            Row("Master volume","Overall game audio",0);
            float volume=GUI.HorizontalSlider(new Rect(right,y+16,rw-55,22),GamePreferences.MasterVolume,0,1);
            CommandUI.Label(new Rect(r.xMax-65,y+8,45,28),Mathf.RoundToInt(volume*100)+"%",13);
            if(Mathf.Abs(volume-GamePreferences.MasterVolume)>.005f)GamePreferences.Set("Volume",volume);
            y+=78;Row("March music","Regimental marching music",0);
            if(CommandUI.Button(new Rect(right,y+6,rw,34),GamePreferences.Music?"On":"Off",selected:GamePreferences.Music))GamePreferences.Set("Music",GamePreferences.Music?0:1);
            y+=78;Row("Tooltips","Descriptions, tradeoffs and locked requirements",0);
            if(CommandUI.Button(new Rect(right,y+6,rw,34),GamePreferences.Tooltips?"On":"Off",selected:GamePreferences.Tooltips))GamePreferences.Set("Tooltips",GamePreferences.Tooltips?0:1);
            y+=78;Row("Interface size","Automatically fits smaller windows",0);
            float[] sizes={.85f,1f,1.15f};string[] labels={"Compact","Standard","Large"};
            for(int i=0;i<3;i++)if(CommandUI.Button(new Rect(right+i*rw/3,y+6,rw/3-4,34),labels[i],selected:Mathf.Abs(GamePreferences.InterfaceScale-sizes[i])<.01f))GamePreferences.Set("Scale",sizes[i]);
            y+=78;Row("Display","Window or borderless fullscreen",0);
            if(CommandUI.Button(new Rect(right,y+6,rw,34),Screen.fullScreen?"Fullscreen":"Windowed"))
            {bool full=!Screen.fullScreen;Screen.fullScreenMode=full?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed;PlayerPrefs.SetInt("ETW2.Options.Fullscreen",full?1:0);PlayerPrefs.Save();}
            y+=78;Row("Frame limit / VSync","VSync follows your display refresh rate",0);
            int[] caps={30,60,120,-1};string[] capLabels={"30","60","120","Unlimited"};
            for(int i=0;i<4;i++)if(CommandUI.Button(new Rect(right+i*rw/5,y+6,rw/5-4,34),capLabels[i],selected:GamePreferences.FrameLimit==caps[i]))GamePreferences.Set("Frames",caps[i]);
            if(CommandUI.Button(new Rect(right+4*rw/5,y+6,rw/5,34),"VSync",selected:GamePreferences.VSync))GamePreferences.Set("VSync",GamePreferences.VSync?0:1);
            CommandUI.Label(new Rect(x,r.yMax-48,r.width-48,30),"Audio, interface and performance preferences are saved automatically.",12,true);
            tips.Draw(r);
            if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){Event.current.Use();close=true;}
            return close;
            void Row(string title,string caption,int unused){CommandUI.Label(new Rect(x,y,r.width*.48f-40,24),title,16,false,true);CommandUI.Label(new Rect(x,y+28,r.width*.48f-40,34),caption,12,true,false,true);CommandUI.Rule(x,y+68,r.width-48);}
        }
    }
}
