using UnityEngine;

namespace EmpireBattle
{
    /// <summary>One layout for ground materials, vegetation exclusion and field boundaries.
    /// Coordinates are in metres on the reference 3 km battlefield.</summary>
    public static class RuralFieldLayout
    {
        public struct Field
        {
            public Vector2 Center, Half;
            public int Kind; // pasture, hay meadow, stubble, ploughed soil
            public Field(float x, float z, float width, float depth, int kind)
            { Center = new Vector2(x,z); Half = new Vector2(width,depth); Kind = kind; }
        }
        public static readonly Field[] Fields =
        {
            new Field(-1050,-650,140,95,2), new Field(1080,610,140,95,2),
            new Field(-720,-570,165,120,0), new Field(-710,-880,155,140,3),
            new Field(-1080,-960,160,130,1), new Field(740,550,155,125,0),
            new Field(760,870,170,140,3), new Field(1110,940,145,130,1),
            new Field(-680,490,190,135,1), new Field(-320,740,135,160,2),
            new Field(650,-570,140,120,1), new Field(940,-330,150,125,0)
        };
        public static int FieldAt(Vector2 p)
        {
            for(int i=0;i<Fields.Length;i++)
            {
                var d=p-Fields[i].Center;
                if(Mathf.Abs(d.x)<Fields[i].Half.x && Mathf.Abs(d.y)<Fields[i].Half.y)return i;
            }
            return -1;
        }
        public static float LaneX(float z) => 410f+Mathf.Sin(z*.0021f)*110f;
        public static float RoadDistance(Vector2 p)
        {
            float distance=Mathf.Abs(p.x-LaneX(p.y));
            // Farm access lanes connect yards with the main road.
            for(int i=0;i<2;i++)
            {
                var farm=Fields[i].Center;
                var end=new Vector2(LaneX(farm.y),farm.y+45f);
                Vector2 line=end-farm;
                float t=Mathf.Clamp01(Vector2.Dot(p-farm,line)/line.sqrMagnitude);
                distance=Mathf.Min(distance,Vector2.Distance(p,farm+line*t));
            }
            return distance;
        }
        public static Color Surface(Vector2 p)
        {
            float noise=Mathf.PerlinNoise(p.x*.008f+27f,p.y*.008f+15f);
            Color color=Color.Lerp(new Color(.235f,.295f,.115f),new Color(.37f,.395f,.19f),noise);
            float soil=0;
            int index=FieldAt(p);
            if(index>=0)
            {
                var field=Fields[index];
                var d=p-field.Center;
                float edge=Mathf.Min(field.Half.x-Mathf.Abs(d.x),field.Half.y-Mathf.Abs(d.y));
                float blend=Mathf.SmoothStep(0,1,edge/3f);
                Color tint=field.Kind==0?new Color(.29f,.35f,.15f):field.Kind==1?new Color(.41f,.42f,.22f):
                    field.Kind==2?new Color(.47f,.385f,.205f):new Color(.285f,.22f,.145f);
                // Broad agricultural passes remain visible after fine furrows mip away.
                float rows=field.Kind==3?1f+.075f*Mathf.Cos(d.x*Mathf.PI/3f):
                    field.Kind==2?1f+.055f*Mathf.Cos(d.x*Mathf.PI/5f):1f;
                color=Color.Lerp(color,tint*Mathf.Lerp(.86f,1.08f,noise)*rows,blend);
                soil=(field.Kind==3?1f:field.Kind==2?.45f:.05f)*blend;
            }
            float road=RoadDistance(p);
            float roadBlend=1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(3f,7f,road));
            color=Color.Lerp(color,new Color(.41f,.345f,.245f),roadBlend);
            soil=Mathf.Lerp(soil,.8f,roadBlend);
            // Pale wheel ruts, with a grassy strip between them.
            float rut=1-Mathf.SmoothStep(0,1,Mathf.Abs(road-2.2f)/.65f);
            color=Color.Lerp(color,new Color(.48f,.405f,.29f),rut*.3f);
            for(int i=0;i<2;i++)
            {
                var d=p-Fields[i].Center;
                float yard=1-Mathf.SmoothStep(0,1,Mathf.Max(Mathf.Abs(d.x)/30f,Mathf.Abs(d.y)/25f)-.7f);
                color=Color.Lerp(color,new Color(.37f,.31f,.23f),yard);
                soil=Mathf.Lerp(soil,.9f,yard);
            }
            color.a=soil;
            return color;
        }
    }
}
