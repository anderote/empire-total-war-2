using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattlefieldTerrain
    {
        public BattleMapProfile Profile { get; private set; }
        public Vector2[] TownCenters { get; private set; } = Array.Empty<Vector2>();
        public Vector2[] TownHalfExtents { get; private set; } = Array.Empty<Vector2>();
        public struct Wall { public Vector2 Center, Half; }
        public readonly List<Wall> Walls = new List<Wall>();
        public float GateHealth { get; private set; }
        public bool GateOpen => GateHealth<=0;
        public void DamageGate(float damage) { if(!float.IsNaN(damage)&&!float.IsInfinity(damage)&&damage>0)GateHealth=Mathf.Max(0,GateHealth-damage); }

        void SetupRegion(BattleConfig config)
        {
            if(!config.RegionalMap)return;
            if(config.Map==null||!config.Map.Valid)throw new ArgumentException("Invalid regional battle profile.");
            Profile=config.Map;
            if(!Profile.Settlement)return;
            var centers=new List<Vector2>();var sizes=new List<Vector2>();
            for(int side=-1;side<=1;side+=2)for(int row=-1;row<=1;row++)
            {
                centers.Add(Profile.SettlementCenter+new Vector2(side*Profile.SettlementRadius*.68f,row*Profile.SettlementRadius*.40f));
                sizes.Add(new Vector2(36,28));
            }
            TownCenters=centers.ToArray();TownHalfExtents=sizes.ToArray();
            if(Profile.FortLevel<=0)return;
            float r=Profile.SettlementRadius;Vector2 c=Profile.SettlementCenter;
            Walls.Add(new Wall{Center=c+Vector2.left*r,Half=new Vector2(3,r)});
            Walls.Add(new Wall{Center=c+Vector2.right*r,Half=new Vector2(3,r)});
            Walls.Add(new Wall{Center=c-Vector2.up*Profile.GateDirection*r,Half=new Vector2(r,3)});
            const float gap=70;
            for(int side=-1;side<=1;side+=2)
                Walls.Add(new Wall{Center=Profile.Gate+Vector2.right*side*(r+gap)*.5f,Half=new Vector2((r-gap)*.5f,3)});
            GateHealth=250*Profile.FortLevel;
        }
        void RegionalLandmarks(BattleConfig config,out Vector2[] farms,out Vector2[] farmHalf,out Vector2[] woods,out float[] radii)
        {
            var random=new System.Random(config.Seed^6143);float scale=config.FieldHalfSize/1500f;
            int farmCount=Profile.Aridity>.65f||Profile.Snow>.6f?0:2;
            farms=new Vector2[farmCount];farmHalf=new Vector2[farmCount];
            for(int i=0;i<farmCount;i++)
            {farms[i]=new Vector2((i==0?-1:1)*(1000+(float)random.NextDouble()*200),(float)(random.NextDouble()-.5)*1900)*scale;farmHalf[i]=new Vector2(12,9)*scale;}
            int woodCount=Mathf.RoundToInt(2+Profile.Forest*8)*(Profile.Aridity>.8f?0:1);
            woods=new Vector2[woodCount];radii=new float[woodCount];
            for(int i=0;i<woodCount;i++)
            {
                float angle=(i+.2f+(float)random.NextDouble()*.4f)/Mathf.Max(1,woodCount)*Mathf.PI*2;
                woods[i]=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*config.FieldHalfSize*.83f;
                radii[i]=(90+Profile.Forest*100+(float)random.NextDouble()*65)*scale;
                if(Profile.Coast)woods[i].x=Mathf.Max(-config.FieldHalfSize*.62f,woods[i].x);
            }
        }
        float RegionalHeight(float x,float z,float baseHeight,float phase)
        {
            float relief=baseHeight*Profile.Relief;
            float variation=Mathf.PerlinNoise(x*.0017f+phase+30,z*.0017f+phase+60)*10*Profile.Relief;
            float lane=1-Mathf.Exp(-(x*x/(halfSize*halfSize*.35f)+z*z/(halfSize*halfSize*.08f)));
            float h=relief+variation*lane;
            if(Profile.Settlement)
            {
                Vector2 d=new Vector2(x,z)-Profile.SettlementCenter;
                float outside=Mathf.Max(Mathf.Abs(d.x),Mathf.Abs(d.y))-Profile.SettlementRadius-30;
                h=Mathf.Lerp(4,h,Mathf.SmoothStep(0,1,Mathf.Clamp01(outside/100)));
            }
            if(Profile.Coast)h=Mathf.Lerp(-1,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-halfSize*.82f,-halfSize*.70f,x)));
            return h;
        }
        public bool RegionalBlocked(Vector2 point,float margin)
        {
            if(Profile==null)return false;
            if(Profile.Coast&&point.x<-halfSize*.82f+margin)return true;
            for(int i=0;i<TownCenters.Length;i++)if(InBox(point,TownCenters[i],TownHalfExtents[i],margin))return true;
            foreach(var wall in Walls)if(InBox(point,wall.Center,wall.Half,margin))return true;
            return Profile.FortLevel>0&&!GateOpen&&InBox(point,Profile.Gate,new Vector2(70,3),margin);
        }
        static bool InBox(Vector2 p,Vector2 center,Vector2 half,float margin)
        {Vector2 d=p-center;return Mathf.Abs(d.x)<=half.x+margin&&Mathf.Abs(d.y)<=half.y+margin;}
        bool RegionalBlocksLine(Vector2 from,Vector2 to)
        {
            if(Profile==null)return false;
            for(int i=0;i<TownCenters.Length;i++)if(SegmentBox(from,to,TownCenters[i],TownHalfExtents[i]))return true;
            foreach(var wall in Walls)if(SegmentBox(from,to,wall.Center,wall.Half))return true;
            return Profile.FortLevel>0&&!GateOpen&&SegmentBox(from,to,Profile.Gate,new Vector2(70,3));
        }
        static bool SegmentBox(Vector2 from,Vector2 to,Vector2 center,Vector2 half)
        {
            Vector2 start=from-center,delta=to-from;float enter=0,leave=1;
            for(int axis=0;axis<2;axis++)
            {
                float s=start[axis],d=delta[axis],h=half[axis];
                if(Mathf.Abs(d)<.000001f){if(Mathf.Abs(s)>h)return false;continue;}
                float a=(-h-s)/d,b=(h-s)/d;
                enter=Mathf.Max(enter,Mathf.Min(a,b));leave=Mathf.Min(leave,Mathf.Max(a,b));
                if(enter>leave)return false;
            }
            return leave>0&&enter<1;
        }
        public bool InsideTown(Vector2 p,float margin=0)
        {
            if(Profile==null||!Profile.Settlement)return false;
            Vector2 d=p-Profile.SettlementCenter;float r=Profile.SettlementRadius-margin;
            return r>0&&Mathf.Abs(d.x)<r&&Mathf.Abs(d.y)<r;
        }
        public bool GatePath(Vector2 from,Vector2 to,float margin,out Vector2 first,out Vector2 second)
        {
            first=second=Vector2.zero;
            if(Profile==null||Profile.FortLevel<=0||InsideTown(from)==InsideTown(to))return false;
            float sign=Profile.GateDirection;bool enters=InsideTown(to);
            first=Profile.Gate+Vector2.up*sign*(margin+12)*(enters?1:-1);
            second=Profile.Gate-Vector2.up*sign*(margin+12)*(enters?1:-1);
            return true;
        }
    }
}
