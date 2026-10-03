using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    // Small fixed-step Verlet cloth. Only the hoist is pinned; structural, shear and
    // bend links preserve fabric length while allowing gusts to travel through it.
    public sealed class StandardCloth : IDisposable
    {
        const int Columns=16, Rows=10;
        const float Step=1f/60;
        struct Link { public int A,B; public float Length,Stiffness; }
        readonly Vector3[] points,previous,rest;
        readonly Link[] links;
        readonly Mesh mesh;
        readonly Material material;
        readonly Texture2D texture;
        readonly float seed;
        float lastTime=float.NaN,accumulator;
        public Mesh Mesh=>mesh;
        public Material Material=>material;
        public Vector3[] Positions=>points;
        public static StandardCloth[] ForBattalion(BattleWorld world,BattalionState unit,int member)
        {
            int key=unit.Id*8+member;
            bool french=UnitRoster.ArtFaction(unit.Faction)=="france";
            bool national=french||UnitRoster.ArtFaction(unit.Faction)=="britain";
            Color field=world.Config.CampaignDeployment?(unit.Side==ArmySide.French?world.Config.PlayerCountryColor:world.Config.EnemyCountryColor)
                :unit.Side==ArmySide.French?new Color(.12f,.28f,.68f):new Color(.75f,.08f,.12f);
            Color.RGBToHSV(field,out float hue,out float saturation,out _);
            field=Color.HSVToRGB(Mathf.Repeat(hue+member*.055f,1),Mathf.Max(.55f,saturation),.8f);
            return new[]{new StandardCloth(1.40f,1.02f,field,national&&member%2==0,french,key),
                new StandardCloth(.82f,.16f,field,false,french,key+1,3.56f)};
        }
        public StandardCloth(float width,float height,Color field,bool national,bool french,int identity,float top=3.36f)
        {
            seed=identity*1.371f;
            int count=(Columns+1)*(Rows+1);
            points=new Vector3[count];previous=new Vector3[count];rest=new Vector3[count];
            var uv=new Vector2[count];
            var constraints=new System.Collections.Generic.List<Link>();
            void Connect(int a,int b,float stiffness){constraints.Add(new Link{A=a,B=b,Length=Vector3.Distance(rest[a],rest[b]),Stiffness=stiffness});}
            for(int y=0;y<=Rows;y++)for(int x=0;x<=Columns;x++){
                int i=y*(Columns+1)+x;
                rest[i]=points[i]=previous[i]=new Vector3(-.08f-x*width/Columns,top-y*height/Rows,.37f);
                uv[i]=new Vector2(x/(float)Columns,1-y/(float)Rows);
            }
            for(int y=0;y<=Rows;y++)for(int x=0;x<=Columns;x++){
                int i=y*(Columns+1)+x;
                if(x<Columns)Connect(i,i+1,1);
                if(y<Rows)Connect(i,i+Columns+1,1);
                if(x<Columns&&y<Rows){Connect(i,i+Columns+2,.7f);Connect(i+1,i+Columns+1,.7f);}
                if(x<Columns-1)Connect(i,i+2,.18f);
                if(y<Rows-1)Connect(i,i+2*(Columns+1),.18f);
            }
            links=constraints.ToArray();
            var triangles=new int[Columns*Rows*6];int t=0;
            for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++){
                int i=y*(Columns+1)+x;
                triangles[t++]=i;triangles[t++]=i+1;triangles[t++]=i+Columns+1;
                triangles[t++]=i+1;triangles[t++]=i+Columns+2;triangles[t++]=i+Columns+1;
            }
            mesh=new Mesh{name="Living regimental silk"};mesh.MarkDynamic();mesh.vertices=points;mesh.uv=uv;mesh.triangles=triangles;
            texture=CreateColour(field,national,french,identity);
            material=new Material(Resources.Load<Shader>("RegimentalSilk")){name="Regimental colour "+identity};
            material.SetTexture("_BaseMap",texture);
            Upload();
        }
        static Texture2D CreateColour(Color field,bool national,bool french,int identity)
        {
            const int w=256,h=160;var pixels=new Color[w*h];
            Color gold=new Color(.96f,.73f,.24f),ivory=new Color(.96f,.91f,.76f),blue=new Color(.07f,.18f,.53f),red=new Color(.77f,.055f,.085f);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                float u=x/(float)(w-1),v=y/(float)(h-1);Color c=field;
                if(national&&french)c=u<1f/3?blue:u<2f/3?ivory:red;
                else if(national){
                    float diagonal=Mathf.Min(Mathf.Abs(v-u),Mathf.Abs(v-(1-u)));
                    c=blue;if(diagonal<.08f)c=ivory;if(diagonal<.025f)c=red;
                    if(Mathf.Abs(u-.5f)<.10f||Mathf.Abs(v-.5f)<.13f)c=ivory;
                    if(Mathf.Abs(u-.5f)<.045f||Mathf.Abs(v-.5f)<.065f)c=red;
                }
                else{
                    // Contrasting canton, gold border and a laurel medallion give
                    // each platoon a readable identity without tinting its soldiers.
                    if(u<.27f&&v>.63f)c=french?blue:red;
                    if(Mathf.Abs(u-v)<.018f||Mathf.Abs(u-(1-v))<.018f)c=ivory;
                    float dx=(u-.53f)*1.6f,dy=v-.5f,r=Mathf.Sqrt(dx*dx+dy*dy);
                    float angle=Mathf.Atan2(dy,dx);
                    if(r>.19f&&r<.24f&&Mathf.Sin(angle*22+identity)>.0f)c=gold;
                    if(r<.15f)c=ivory;
                    if(r<.11f&&(Mathf.Abs(dx)<.018f||Mathf.Abs(dy)<.018f))c=gold;
                    for(int mark=0;mark<1+identity%5;mark++)if(Mathf.Abs(u-(.4f+mark*.055f))<.013f&&v>.18f&&v<.23f)c=gold;
                }
                float edge=Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v));
                if(edge<.027f)c=gold;
                float weave=1+.035f*Mathf.Sin(x*Mathf.PI*.5f)*Mathf.Sin(y*Mathf.PI*.5f);
                pixels[y*w+x]=new Color(c.r*weave,c.g*weave,c.b*weave,1);
            }
            var result=new Texture2D(w,h,TextureFormat.RGBA32,true){name="Woven regimental heraldry",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=4};
            result.SetPixels(pixels);result.Apply(true,true);return result;
        }
        public void Advance(float time,Quaternion facing,bool moving)
        {
            if(float.IsNaN(lastTime)||time<lastTime||time-lastTime>.5f){
                Array.Copy(rest,points,rest.Length);Array.Copy(rest,previous,rest.Length);accumulator=0;lastTime=time;
                // Settle new/returning flags without simulating a long invisible gap.
                for(int i=0;i<24;i++)Simulate(time-(24-i)*Step,facing,moving);
                Upload();return;
            }
            accumulator+=Mathf.Min(.067f,Mathf.Max(0,time-lastTime));lastTime=time;
            bool changed=false;
            while(accumulator>=Step){Simulate(time-accumulator,facing,moving);accumulator-=Step;changed=true;}
            if(changed)Upload();
        }
        float distantTime=float.NaN;
        public void AdvanceDistant(float time,bool moving)
        {
            // Hundreds of standards can be visible in large battles. Distant
            // cloth uses a 10 Hz traveling-wave LOD rather than constraint solves.
            if(!float.IsNaN(distantTime)&&time>=distantTime&&time-distantTime<.1f)return;
            distantTime=time;lastTime=float.NaN;
            float gust=1+.35f*Mathf.Sin(time*.73f);
            for(int i=0;i<points.Length;i++){
                float free=(i%(Columns+1))/(float)Columns;
                var p=rest[i];
                p.z+=free*(.18f*gust*Mathf.Sin(time*(moving?8:5)-free*6+seed)+.06f*Mathf.Sin(time*9-free*11+p.y*4+seed));
                p.y+=free*(.035f*Mathf.Sin(time*3-free*4+seed)-.10f*free);
                points[i]=p;
            }
            Upload();
        }
        void Simulate(float time,Quaternion facing,bool moving)
        {
            float gust=1+.35f*Mathf.Sin(time*.73f)+.19f*Mathf.Sin(time*1.61f+seed*.12f);
            Vector3 wind=Quaternion.Inverse(facing)*new Vector3(6*gust,1.8f,4*gust);
            if(moving)wind+=new Vector3(0,0,-5);
            for(int i=0;i<points.Length;i++){
                if(i%(Columns+1)==0){points[i]=previous[i]=rest[i];continue;}
                float free=(i%(Columns+1))/(float)Columns;
                float flutter=Mathf.Sin(time*7.1f-free*7+seed)+.4f*Mathf.Sin(time*12.7f-free*13+seed);
                Vector3 force=wind+new Vector3(0,-4,flutter*3.6f*free);
                var p=points[i];points[i]+= (p-previous[i])*.975f+force*(Step*Step);previous[i]=p;
            }
            for(int iteration=0;iteration<6;iteration++){
                foreach(var link in links){
                    Vector3 delta=points[link.B]-points[link.A];float length=delta.magnitude;
                    if(length<.00001f)continue;
                    Vector3 correction=delta*((length-link.Length)/length*link.Stiffness);
                    bool aPinned=link.A%(Columns+1)==0,bPinned=link.B%(Columns+1)==0;
                    if(!aPinned)points[link.A]+=correction*(bPinned?1:.5f);
                    if(!bPinned)points[link.B]-=correction*(aPinned?1:.5f);
                }
            }
        }
        void Upload(){mesh.vertices=points;mesh.RecalculateNormals();mesh.RecalculateBounds();}
        public void Draw(Matrix4x4 transform,Camera camera)
        {Graphics.DrawMesh(mesh,transform,material,0,camera,0,null,ShadowCastingMode.On,true);}
        public void Tint(Color color)=>material.SetColor("_BaseColor",color);
        public void Dispose()
        {
            if(Application.isPlaying){UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(texture);}
            else{UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
