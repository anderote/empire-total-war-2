using System;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Shared logical geometry for drawing and physical pointer exclusion.</summary>
    public static class BattleUILayout
    {
        public static float Scale(float width,float height) => Math.Max(.25f,Math.Min(1.2f*Empire.UI.GamePreferences.InterfaceScale,Math.Min(width/1280f,height/900f)));
        public static Rect Header(float width) => new Rect((width-860)/2,10,860,86);
        public static Rect Roster(float width,float height) => new Rect(14,height-248,width-28,234);
        public static Rect Detail(float width,float height) => new Rect((width-860)/2,Roster(width,height).y-118,860,108);
        public static Rect MenuButton => new Rect(14,10,100,34);
        public static Rect Menu(float width) => new Rect(14,52,220,390);
        public static Rect Atmosphere(bool open) => new Rect(14,10,176,open?211:28);
        public static Rect Map(float width) => new Rect(width-202,10,188,188);
        public static Rect TerrainMap(float width)
        {Rect panel=Map(width);return new Rect(panel.x+14,panel.y+30,160,132);}
        public static Rect Battery(float width,float height,bool mixed)
        {
            Rect rect=Detail(width,height);if(mixed)rect.y-=118;return rect;
        }
        public static Vector2 MapPoint(Rect map,Vector2 world,float halfSize)
            => new Vector2(map.x+(world.x/halfSize+1)*.5f*map.width,map.y+(1-world.y/halfSize)*.5f*map.height);
        public static Vector2 WorldPoint(Rect map,Vector2 point,float halfSize)
            => new Vector2(((point.x-map.x)/map.width*2-1)*halfSize,(1-(point.y-map.y)/map.height*2)*halfSize);
        public static int Capacity(float width) => Math.Max(1,(int)((width-136)/120));
        public static int Pages(float width,int count) => Math.Max(1,(count+Capacity(width)-1)/Capacity(width));
        public static Rect Card(float width,float height,int slot,int count)
        {
            int visible=Math.Min(Capacity(width),count);
            return new Rect((width-visible*120)/2+slot*120,Roster(width,height).y+40,112,80);
        }
        public static Rect RosterColumn(float width,float height,int group)
        {
            Rect roster=Roster(width,height);float lane=(roster.width-28)/3;
            return new Rect(roster.x+14+group*lane,roster.y+36,lane-8,roster.height-44);
        }
        public static int RosterGroupPages(int count)=>Math.Max(1,(count+3)/4);
        public static Rect RosterGroupCard(Rect lane,int slot)
        {
            float cardWidth=(lane.width-16)/2;
            return new Rect(lane.x+4+(slot%2)*(cardWidth+4),lane.y+22+(slot/2)*76,cardWidth,72);
        }
        public static Rect Physical(Rect logical,float scale) => new Rect(logical.x*scale,logical.y*scale,logical.width*scale,logical.height*scale);
    }
}
