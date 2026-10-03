using System;
using System.Collections.Generic;
using UnityEngine;

namespace Empire.UI
{
    // Original, code-drawn heraldry shared by every mode; no font glyph dependency.
    public static class MilitaryInsignia
    {
        static readonly Dictionary<string,Texture2D> cache=new Dictionary<string,Texture2D>();
        static readonly Color Gold=new Color(.82f,.68f,.39f),Light=new Color(.98f,.88f,.62f),Dark=new Color(.18f,.25f,.28f),Red=new Color(.52f,.22f,.20f);
        public static void Tier(Rect r,int tier,bool earned=true)=>Draw(r,"tier",Mathf.Clamp(tier,1,8),earned);
        public static void Rank(Rect r,int rank,bool earned=true)=>Draw(r,"rank",Mathf.Clamp(rank,0,5),earned);
        public static void Pin(Rect r,int kind,bool earned=true)=>Draw(r,"pin",kind,earned);
        public static void Medal(Rect r,int grade,bool earned=true)=>Draw(r,"medal",Mathf.Clamp(grade,0,4),earned);
        static void Draw(Rect r,string kind,int value,bool earned)
        {
            string key=kind+value;
            if(!cache.TryGetValue(key,out var texture)){texture=Create(kind,value);cache.Add(key,texture);}
            var old=GUI.color;if(!earned)GUI.color=new Color(.55f,.60f,.62f,.48f)*old;
            GUI.DrawTexture(r,texture,ScaleMode.ScaleToFit);GUI.color=old;
        }
        public static void Pips(Rect r,int filled,int total=5)
        {
            float width=Mathf.Min(8,(r.width-(total-1)*4)/total);
            for(int i=0;i<total;i++)EmpireTheme.Fill(new Rect(r.x+i*(width+4),r.y,width,r.height),i<filled?Gold:EmpireTheme.Line);
        }
        static Texture2D Create(string kind,int value)
        {
            var a=new Canvas();
            if(kind=="medal")
            {
                Color metal=value==0?new Color(.75f,.48f,.28f):value==2?new Color(.79f,.85f,.85f):Gold;
                Color ribbon=value==2?new Color(.22f,.38f,.54f):value==4?new Color(.69f,.62f,.42f):Red;
                if(value==1)
                {
                    // Campaign ribbon: a broad service bar, distinct from hanging decorations.
                    a.Box(.09f,.36f,.82f,.28f,Gold);a.Box(.12f,.39f,.76f,.22f,new Color(.28f,.40f,.39f));
                    for(int i=0;i<3;i++)a.Box(.24f+i*.20f,.39f,.07f,.22f,Light);
                    a.Star(.50f,.51f,.11f,Gold);a.Line(.16f,.69f,.84f,.69f,.015f,Gold);
                }
                else
                {
                    a.Polygon(ribbon,.25f,.05f,.75f,.05f,.70f,.38f,.50f,.53f,.30f,.38f);
                    a.Box(.34f,.06f,.08f,.32f,metal);a.Box(.58f,.06f,.08f,.32f,metal);
                    if(value==3)
                    {
                        a.Polygon(metal,.36f,.36f,.64f,.36f,.59f,.53f,.80f,.49f,.80f,.78f,.59f,.73f,.64f,.94f,.36f,.94f,.41f,.73f,.20f,.78f,.20f,.49f,.41f,.53f);
                        a.Circle(.50f,.65f,.18f,Red);a.Star(.50f,.65f,.12f,Light);
                    }
                    else if(value==4)
                    {
                        a.Star(.50f,.65f,.32f,metal,12);a.Circle(.50f,.65f,.21f,Dark);a.Wreath(.50f,.65f,.175f);a.Star(.50f,.65f,.125f,Light);
                    }
                    else
                    {
                        a.Circle(.50f,.65f,.29f,metal);a.Circle(.50f,.65f,.235f,Dark);
                        if(value==2){a.Wreath(.50f,.65f,.20f);a.Box(.47f,.50f,.06f,.30f,metal);a.Box(.35f,.62f,.30f,.06f,metal);}
                        else a.Star(.50f,.65f,.18f,Light);
                    }
                    a.Line(.28f,.03f,.72f,.03f,.025f,metal);
                }
            }
            else if(kind=="tier"||kind=="rank")
            {
                a.Polygon(Gold,.12f,.09f,.50f,.02f,.88f,.09f,.83f,.65f,.50f,.96f,.17f,.65f);
                a.Polygon(Dark,.17f,.13f,.50f,.07f,.83f,.13f,.78f,.62f,.50f,.89f,.22f,.62f);
                if(kind=="tier")
                {
                    if(value>=4){a.Wreath(.50f,.46f,.27f);a.Star(.50f,.43f,.18f,Light);for(int i=0;i<value-4;i++)a.Circle(.28f+i*.145f,.70f,.035f,Light);}
                    else for(int i=0;i<value;i++)a.Chevron(.26f,.26f+i*.16f,.48f,.14f,Gold);
                    a.Box(.42f,.77f,.16f,.027f,Light);
                }
                else
                {
                    if(value==0)a.Circle(.50f,.43f,.055f,Gold);
                    else if(value==5){a.Wreath(.50f,.46f,.28f);a.Star(.50f,.43f,.18f,Light);a.Chevron(.34f,.72f,.32f,.08f,Gold);}
                    else {for(int i=0;i<Math.Min(3,value);i++)a.Chevron(.25f,.27f+i*.15f,.5f,.13f,Gold);if(value==4)a.Star(.5f,.18f,.095f,Light);}
                }
            }
            else
            {
                a.Circle(.50f,.46f,.39f,Gold);a.Circle(.50f,.46f,.345f,Dark);
                switch(value%8)
                {
                    case 0:a.Circle(.5f,.46f,.19f,Gold);a.Circle(.5f,.46f,.14f,Dark);a.Line(.5f,.16f,.5f,.76f,.035f,Light);a.Line(.2f,.46f,.8f,.46f,.035f,Light);break;
                    case 1:a.Box(.29f,.25f,.42f,.42f,Gold);a.Box(.35f,.31f,.30f,.30f,Dark);break;
                    case 2:a.Box(.44f,.23f,.12f,.46f,Light);a.Box(.27f,.4f,.46f,.12f,Light);break;
                    case 3:a.Line(.28f,.7f,.7f,.23f,.055f,Light);a.Line(.28f,.23f,.7f,.7f,.055f,Gold);a.Line(.23f,.56f,.41f,.74f,.04f,Gold);a.Line(.59f,.74f,.77f,.56f,.04f,Gold);break;
                    case 4:for(int i=0;i<3;i++){a.Box(.28f+i*.16f,.34f,.10f,.32f,Gold);a.Polygon(Light,.28f+i*.16f,.32f,.33f+i*.16f,.22f,.38f+i*.16f,.32f);}break;
                    case 5:a.Polygon(Gold,.33f,.23f,.51f,.23f,.51f,.51f,.70f,.58f,.7f,.68f,.3f,.68f);break;
                    case 6:a.Star(.50f,.46f,.23f,Light);break;
                    default:a.Circle(.50f,.51f,.19f,Gold);a.Box(.44f,.24f,.12f,.1f,Light);a.Line(.51f,.23f,.61f,.17f,.03f,Gold);break;
                }
                a.Line(.27f,.87f,.73f,.87f,.025f,Gold);
            }
            return a.Texture(kind+value);
        }
        sealed class Canvas
        {
            const int N=192;readonly Color[] pixels=new Color[N*N];
            public void Box(float x,float y,float w,float h,Color color)=>Polygon(color,x,y,x+w,y,x+w,y+h,x,y+h);
            public void Circle(float x,float y,float radius,Color color)
            {
                Paint(x-radius,y-radius,x+radius,y+radius,(px,py)=>(px-x)*(px-x)+(py-y)*(py-y)<radius*radius,color);
            }
            public void Line(float x,float y,float endX,float endY,float width,Color color)
            {
                float dx=endX-x,dy=endY-y,len=Mathf.Sqrt(dx*dx+dy*dy);if(len<.001f)return;
                float nx=-dy/len*width/2,ny=dx/len*width/2;
                Polygon(color,x+nx,y+ny,endX+nx,endY+ny,endX-nx,endY-ny,x-nx,y-ny);
            }
            public void Chevron(float x,float y,float w,float h,Color color)
            {Line(x,y,x+w/2,y+h,.055f,color);Line(x+w/2,y+h,x+w,y,.055f,color);}
            public void Star(float x,float y,float radius,Color color,int points=5)
            {
                var p=new float[points*4];for(int i=0;i<points*2;i++){float angle=-Mathf.PI/2+i*Mathf.PI/points,r=i%2==0?radius:radius*(points==5?.43f:.7f);p[i*2]=x+Mathf.Cos(angle)*r;p[i*2+1]=y+Mathf.Sin(angle)*r;}Polygon(color,p);
            }
            public void Wreath(float x,float y,float radius)
            {
                for(int side=-1;side<=1;side+=2)for(int i=0;i<6;i++)
                {float angle=(25+i*24)*Mathf.Deg2Rad;float px=x+side*Mathf.Sin(angle)*radius,py=y+Mathf.Cos(angle)*radius;
                    Line(px,py,px+side*.048f,py-.048f,.04f,Gold);}
            }
            public void Polygon(Color color,params float[] vertices)
            {
                float minX=1,minY=1,maxX=0,maxY=0;for(int i=0;i<vertices.Length;i+=2){minX=Math.Min(minX,vertices[i]);maxX=Math.Max(maxX,vertices[i]);minY=Math.Min(minY,vertices[i+1]);maxY=Math.Max(maxY,vertices[i+1]);}
                Paint(minX,minY,maxX,maxY,(x,y)=>{bool inside=false;int n=vertices.Length/2;for(int i=0,j=n-1;i<n;j=i++)
                    {float ix=vertices[i*2],iy=vertices[i*2+1],jx=vertices[j*2],jy=vertices[j*2+1];if((iy>y)!=(jy>y)&&x<(jx-ix)*(y-iy)/(jy-iy)+ix)inside=!inside;}return inside;},color);
            }
            void Paint(float left,float top,float right,float bottom,Func<float,float,bool> contains,Color color)
            {
                for(int y=Mathf.Max(0,(int)(top*N));y<Mathf.Min(N,Mathf.CeilToInt(bottom*N));y++)for(int x=Mathf.Max(0,(int)(left*N));x<Mathf.Min(N,Mathf.CeilToInt(right*N));x++)
                    if(contains((x+.5f)/N,(y+.5f)/N))pixels[(N-1-y)*N+x]=color;
            }
            public Texture2D Texture(string name)
            {var t=new Texture2D(N,N,TextureFormat.RGBA32,true){name="Insignia / "+name,hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};t.SetPixels(pixels);t.Apply(true,true);return t;}
        }
    }
}
