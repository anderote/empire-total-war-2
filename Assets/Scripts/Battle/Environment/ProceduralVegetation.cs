using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    // Branch skeletons and individual folded leaves preserve gaps in the canopy.
    // Alpha in vertex color distinguishes foliage from bark, not transparency.
    public static class ProceduralVegetation
    {
        sealed class Builder
        {
            public readonly List<Vector3> Positions=new List<Vector3>();
            readonly List<Vector3> normals=new List<Vector3>();
            readonly List<Color> colors=new List<Color>();
            readonly List<Vector2> uvs=new List<Vector2>();
            readonly List<int> triangles=new List<int>();
            public readonly System.Random Random;
            readonly int seed;readonly bool distant;int leafGroup;
            public Builder(int seed,bool distant=false){this.seed=seed;this.distant=distant;Random=new System.Random(seed);}
            public float Range(float min,float max)=>min+(float)Random.NextDouble()*(max-min);
            public Vector3 Sphere()
            {
                float y=Range(-1,1),a=Range(0,Mathf.PI*2),r=Mathf.Sqrt(1-y*y);
                return new Vector3(r*Mathf.Cos(a),y,r*Mathf.Sin(a))*Mathf.Pow(Range(0,1),1f/3);
            }
            int Vertex(Vector3 p,Vector3 normal,Color color,Vector2 uv)
            {int i=Positions.Count;Positions.Add(p);normals.Add(normal);colors.Add(color);uvs.Add(uv);return i;}
            public void Stem(Vector3 from,Vector3 to,float radius,float tip,Color tint)
            {
                Vector3 direction=(to-from).normalized;
                Vector3 right=Vector3.Cross(direction,Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up).normalized;
                Vector3 forward=Vector3.Cross(right,direction);
                int start=Positions.Count;
                int sides=distant?4:8;
                tint.a=0;
                for(int ring=0;ring<2;ring++)for(int side=0;side<=sides;side++)
                {
                    float angle=side*Mathf.PI*2/sides;
                    Vector3 normal=right*Mathf.Cos(angle)+forward*Mathf.Sin(angle);
                    Vertex((ring==0?from:to)+normal*(ring==0?radius:tip),normal,tint,
                        new Vector2(side/(float)sides*Mathf.Max(.25f,radius*6),ring*(to-from).magnitude*.55f));
                }
                for(int side=0;side<sides;side++)
                {
                    int a=start+side,b=a+sides+1;
                    triangles.Add(a);triangles.Add(b);triangles.Add(a+1);
                    triangles.Add(a+1);triangles.Add(b);triangles.Add(b+1);
                }
            }
            public void Leaves(Vector3 center,Vector3 radius,int count,float size,int species)
            {
                var leafRandom=new System.Random(seed+(leafGroup++)*971);
                float LeafRange(float min,float max)=>min+(float)leafRandom.NextDouble()*(max-min);
                for(int i=0;i<count;i++)
                {
                    float y=LeafRange(-1,1),a=LeafRange(0,Mathf.PI*2),r=Mathf.Sqrt(1-y*y);
                    Vector3 sample=new Vector3(r*Mathf.Cos(a),y,r*Mathf.Sin(a))*Mathf.Pow(LeafRange(0,1),1f/3);
                    Vector3 offset=Vector3.Scale(sample,radius),p=center+offset;
                    float angle=LeafRange(0,Mathf.PI*2);
                    Vector3 along=new Vector3(Mathf.Cos(angle),LeafRange(-1.6f,1.6f),Mathf.Sin(angle)).normalized;
                    Vector3 across=Vector3.Cross(along,Vector3.up).normalized;
                    float length=LeafRange(.65f,1.3f)*size,width=length*(species==1?.34f:.48f);
                    Vector3 normal=Vector3.Cross(across,along).normalized;
                    if(normal.y<0)normal=-normal;
                    Color dark=species==1?new Color(.15f,.27f,.07f):species==2?new Color(.12f,.22f,.055f):new Color(.17f,.25f,.065f);
                    Color light=species==1?new Color(.40f,.49f,.18f):new Color(.34f,.40f,.12f);
                    Color tint=Color.Lerp(dark,light,LeafRange(.08f,.88f));tint.a=1;
                    int start=Positions.Count;
                    Vertex(p-along*length,normal,tint,new Vector2(.5f,0));
                    Vertex(p-across*width,normal,tint,new Vector2(0,.5f));
                    if(!distant)Vertex(p+normal*length*.13f,normal,tint,new Vector2(.5f,.5f));
                    Vertex(p+across*width,normal,tint,new Vector2(1,.5f));
                    Vertex(p+along*length,normal,tint,new Vector2(.5f,1));
                    int[] indices=distant?new[]{0,3,1,0,2,3}:new[]{0,2,1,0,3,2,1,2,4,2,3,4};
                    foreach(int index in indices)triangles.Add(start+index);
                }
            }
            public Mesh Finish(string name)
            {
                var mesh=new Mesh{name=name,indexFormat=Positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                mesh.SetVertices(Positions);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetUVs(0,uvs);
                mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(.5f);mesh.bounds=bounds;return mesh;
            }
        }

        public static Mesh Tree(int species,int seed,bool distant=false)
        {
            var b=new Builder(seed,distant);
            Color bark=species==1?new Color(.68f,.66f,.54f):new Color(.36f,.30f,.22f);
            float height=species==1?14f:species==2?11f:12f;
            Vector3 trunk=Vector3.zero;
            for(int level=0;level<6;level++)
            {
                Vector3 next=new Vector3(b.Range(-.28f,.28f),(level+1)*height/6,b.Range(-.28f,.28f));
                b.Stem(trunk,next,Mathf.Lerp(.42f,.08f,level/6f),Mathf.Lerp(.42f,.04f,(level+1)/6f),bark);
                if(level>=1)
                {
                    int branches=level==1?3:4;
                    for(int branch=0;branch<branches;branch++)
                    {
                        float angle=branch*Mathf.PI*2/branches+level*2.4f+b.Range(-.45f,.45f);
                        float spread=(species==1?2.8f:species==2?4.8f:4.2f)*Mathf.Sin((level+.2f)/6*Mathf.PI);
                        Vector3 end=next+new Vector3(Mathf.Cos(angle)*spread,b.Range(.5f,1.6f),Mathf.Sin(angle)*spread);
                        b.Stem(next,end,.10f*(1-level/8f),.025f,bark);
                        for(int twig=0;twig<3;twig++)
                        {
                            Vector3 tip=end+new Vector3(b.Range(-1.1f,1.1f),b.Range(.1f,1.3f),b.Range(-1.1f,1.1f));
                            b.Stem(end,tip,.026f,.008f,bark);
                            b.Leaves(tip,new Vector3(species==1?1.25f:1.7f,1.25f,species==1?1.25f:1.7f),
                                distant?8:240,distant?.9f:.22f,species);
                        }
                    }
                }
                trunk=next;
            }
            return b.Finish("Branching tree "+species+" seed "+seed+(distant?" distant":""));
        }

        public static Mesh Hedge(int seed,bool distant=false)
        {
            var b=new Builder(seed,distant);
            for(int stem=0;stem<8;stem++)
            {
                // Author in meters so leaves are not stretched by a normalized 4:2:1.8 hedge scale.
                Vector3 root=new Vector3(((stem+.5f)/8-.5f)*4,0,b.Range(-.20f,.20f));
                Vector3 top=root+new Vector3(b.Range(-.3f,.3f),b.Range(1.3f,1.9f),b.Range(-.3f,.3f));
                var bark=new Color(.29f,.24f,.16f);
                b.Stem(root,top,.032f,.008f,bark);
                Vector3 center=new Vector3(top.x,top.y*.6f,top.z);
                for(int twig=0;twig<2;twig++)
                    b.Stem(Vector3.Lerp(root,top,.4f),center+new Vector3(b.Range(-.5f,.5f),b.Range(-.15f,.4f),b.Range(-.5f,.5f)),.013f,.004f,bark);
                b.Leaves(center,new Vector3(.92f,.84f,.75f),distant?16:400,distant?.38f:.10f,stem%3);
            }
            return b.Finish("Leafy hedgerow "+seed+(distant?" distant":""));
        }
    }
}
