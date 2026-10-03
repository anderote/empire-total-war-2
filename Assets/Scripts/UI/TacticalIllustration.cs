using UnityEngine;

namespace Empire.UI
{
    public static class TacticalIllustration
    {
        public static void Draw(Rect r,int terrain,int friendly=3,int hostile=3,Color? hostileColor=null)
        {
            EmpireTheme.Fill(r,terrain==3?new Color(.35f,.40f,.43f):terrain==4?new Color(.32f,.25f,.16f):new Color(.10f,.15f,.17f));
            if(terrain==5)EmpireTheme.Fill(new Rect(r.x,r.y,r.width*.17f,r.height),new Color(.13f,.29f,.37f));
            for(int n=0;n<6;n++)
            {
                float y=r.y+r.height*(n+1)/7f;
                for(int x=0;x<32;x++)
                {
                    float wave=Mathf.Sin(x*.32f+n*.6f)*(terrain==2?15:terrain==1?7:3);
                    EmpireTheme.Fill(new Rect(r.x+x*r.width/32f,y+wave,r.width/32f+1,1),new Color(.25f,.33f,.31f,.6f));
                }
            }
            if(terrain==1)for(int i=0;i<27;i++)EmpireTheme.Fill(new Rect(r.x+20+(i*47)%(int)Mathf.Max(21,r.width-45),r.y+14+(i*29)%(int)Mathf.Max(15,r.height-28),5,8),new Color(.27f,.38f,.28f));
            void Formations(int count,bool enemy)
            {
                int columns=Mathf.Min(6,count);float width=Mathf.Min(13,(r.width*.37f)/Mathf.Max(1,columns)-4);
                for(int i=0;i<count;i++)
                {
                    float x=r.x+r.width*(enemy?.56f:.13f)+(i%columns)*(width+4);
                    float y=r.y+r.height*(enemy?.18f:.66f)+(i/columns)*11;
                    EmpireTheme.Fill(new Rect(x,y,width,6),enemy?hostileColor??EmpireTheme.BritishRed:EmpireTheme.FrenchBlue);
                }
            }
            Formations(Mathf.Clamp(friendly,0,18),false);Formations(Mathf.Clamp(hostile,0,18),true);
            EmpireTheme.Fill(new Rect(r.x+r.width*.43f,r.y+r.height*.36f,1,r.height*.30f),EmpireTheme.Brass);
        }
    }
}
