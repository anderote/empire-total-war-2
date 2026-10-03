using System;
using System.Collections.Generic;
using EmpireBattle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Empire.UI
{
    /// <summary>Small reusable set-dressing for the live regiment preview camera.</summary>
    public sealed class CampaignPortraitDiorama
    {
        public readonly Mesh Ground, Horizon, Landmark;
        public readonly Color GroundColor, HorizonColor, LandmarkColor, SkyColor;

        CampaignPortraitDiorama(Mesh ground,Mesh horizon,Mesh landmark,Color groundColor,Color horizonColor,Color landmarkColor,Color skyColor)
        {Ground=ground;Horizon=horizon;Landmark=landmark;GroundColor=groundColor;HorizonColor=horizonColor;LandmarkColor=landmarkColor;SkyColor=skyColor;}

        public static CampaignPortraitDiorama Build(string faction,UnitRole role)
        {
            string regional=UnitRoster.ArtFaction(faction);
            Color ground,horizon,landmark,sky;
            if(regional=="ming")
            {ground=new Color(.18f,.25f,.18f);horizon=new Color(.17f,.23f,.20f);landmark=new Color(.42f,.25f,.16f);sky=new Color(.28f,.37f,.39f);}
            else if(regional=="india")
            {ground=new Color(.30f,.23f,.13f);horizon=new Color(.28f,.21f,.15f);landmark=new Color(.50f,.35f,.21f);sky=new Color(.48f,.36f,.25f);}
            else
            {ground=new Color(.18f,.24f,.16f);horizon=new Color(.15f,.20f,.17f);landmark=new Color(.39f,.34f,.25f);sky=new Color(.28f,.36f,.41f);}

            var floor=new MeshBuilder();floor.AddDisk(13,28);
            var ridge=new MeshBuilder();
            for(int i=0;i<14;i++)
            {
                float angle=i*Mathf.PI*2/14f;
                float radius=9.8f+(i%3)*.55f;
                float height=2.1f+(i%4)*.55f;
                ridge.AddPyramid(new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius),2.9f,3.8f,height);
            }
            var props=new MeshBuilder();
            if(role==UnitRole.Cavalry)
            {
                AddFence(props,-3.5f,-4f,3.3f);AddFence(props,3.5f,-4f,3.3f);
                props.AddBox(new Vector3(0,.025f,-4.5f),new Vector3(1.5f,.05f,6.5f));
            }
            else if(role==UnitRole.Guard||role==UnitRole.Swordsman)
            {
                props.AddBox(new Vector3(-3.2f,.86f,-4.1f),new Vector3(.35f,1.72f,.5f));
                props.AddBox(new Vector3(3.2f,.86f,-4.1f),new Vector3(.35f,1.72f,.5f));
                props.AddBox(new Vector3(0,1.68f,-4.1f),new Vector3(6.75f,.26f,.62f));
            }
            else if(role==UnitRole.Rifleman||role==UnitRole.Skirmisher)
            {
                AddTarget(props,-3.3f,-4.0f);AddTarget(props,3.3f,-4.6f);
                AddTree(props,-4.5f,-6.1f,1.9f);
            }
            else if(role==UnitRole.Militia)
            {
                AddFence(props,-3.5f,-4.2f,2.7f);AddFence(props,3.5f,-4.2f,2.7f);
                AddTree(props,4.4f,-5.6f,2.0f);
            }
            else
            {
                // Line infantry gets a clear road, low field boundary and distant trees.
                AddFence(props,-3.6f,-4.8f,2.4f);AddFence(props,3.6f,-4.8f,2.4f);
                AddTree(props,-4.6f,-5.8f,2.0f);AddTree(props,4.6f,-6.4f,2.2f);
            }

            // Regional architecture changes the landscape dressing, not the unit model.
            if(regional=="ming")
            {
                props.AddBox(new Vector3(-4.0f,.10f,-3.4f),new Vector3(1.0f,.20f,4.2f));
                props.AddBox(new Vector3(4.0f,.10f,-3.4f),new Vector3(1.0f,.20f,4.2f));
                AddPavilion(props,-5.6f,-4.8f);
            }
            else if(regional=="india")
            {
                props.AddBox(new Vector3(-4.0f,.42f,-3.8f),new Vector3(.3f,.84f,3.2f));
                props.AddBox(new Vector3(4.0f,.42f,-3.8f),new Vector3(.3f,.84f,3.2f));
                props.AddBox(new Vector3(-4.0f,.88f,-3.8f),new Vector3(1.0f,.20f,3.5f));
                props.AddBox(new Vector3(4.0f,.88f,-3.8f),new Vector3(1.0f,.20f,3.5f));
            }
            else
            {
                props.AddBox(new Vector3(-4.2f,.18f,-3.8f),new Vector3(.22f,.36f,3.4f));
                props.AddBox(new Vector3(4.2f,.18f,-3.8f),new Vector3(.22f,.36f,3.4f));
            }

            return new CampaignPortraitDiorama(floor.ToMesh("Portrait ground"),ridge.ToMesh("Portrait ridge"),props.ToMesh("Portrait landmarks"),ground,horizon,landmark,sky);
        }

        static void AddFence(MeshBuilder mesh,float x,float z,float length)
        {
            mesh.AddBox(new Vector3(x,.42f,z),new Vector3(.08f,.84f,.10f));
            mesh.AddBox(new Vector3(x,.42f,z-length),new Vector3(.08f,.84f,.10f));
            mesh.AddBox(new Vector3(x,.60f,z-length*.5f),new Vector3(.08f,.10f,length));
            mesh.AddBox(new Vector3(x,.28f,z-length*.5f),new Vector3(.08f,.10f,length));
        }
        static void AddTarget(MeshBuilder mesh,float x,float z)
        {
            mesh.AddBox(new Vector3(x,.55f,z),new Vector3(.10f,1.1f,.10f));
            mesh.AddBox(new Vector3(x,.48f,z),new Vector3(.68f,.54f,.12f));
            mesh.AddBox(new Vector3(x,.08f,z),new Vector3(.62f,.10f,.20f));
        }
        static void AddTree(MeshBuilder mesh,float x,float z,float height)
        {
            mesh.AddBox(new Vector3(x,.45f,z),new Vector3(.18f,.9f,.18f));
            mesh.AddPyramid(new Vector3(x, height*.30f,z),1.65f,1.55f,height*.58f);
            mesh.AddPyramid(new Vector3(x,height*.52f,z),1.25f,1.2f,height*.48f);
            mesh.AddPyramid(new Vector3(x,height*.70f,z),.82f,.82f,height*.35f);
        }
        static void AddPavilion(MeshBuilder mesh,float x,float z)
        {
            mesh.AddBox(new Vector3(x,.60f,z),new Vector3(1.45f,1.2f,1.05f));
            mesh.AddBox(new Vector3(x,1.28f,z),new Vector3(1.95f,.16f,1.45f));
            mesh.AddPyramid(new Vector3(x,1.36f,z),2.05f,1.55f,.65f);
        }

        public void Draw(Camera camera,int layer,Material ground,Material horizon,Material landmark,MaterialPropertyBlock properties)
        {
            DrawOne(Ground,ground,camera,layer,properties);
            DrawOne(Horizon,horizon,camera,layer,properties);
            DrawOne(Landmark,landmark,camera,layer,properties);
        }
        static void DrawOne(Mesh mesh,Material material,Camera camera,int layer,MaterialPropertyBlock properties)
        {
            if(!mesh||!material||!camera)return;
            properties.Clear();properties.SetVector("_RegimentUniform",Vector4.zero);properties.SetFloat("_PoseBlend",0);properties.SetFloat("_Morph",0);
            Graphics.DrawMesh(mesh,Matrix4x4.identity,material,layer,camera,0,properties,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
        }

        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices=new List<Vector3>();
            readonly List<int> triangles=new List<int>();
            public void AddDisk(float radius,int segments)
            {
                int center=vertices.Count;vertices.Add(new Vector3(0,-.05f,0));
                for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments;vertices.Add(new Vector3(Mathf.Cos(a)*radius,-.05f,Mathf.Sin(a)*radius));}
                for(int i=0;i<segments;i++){int current=center+1+i,next=center+1+(i+1)%segments;triangles.Add(center);triangles.Add(next);triangles.Add(current);}
            }
            public void AddBox(Vector3 center,Vector3 size)
            {
                Vector3 h=size*.5f;Vector3[] p={center+new Vector3(-h.x,-h.y,-h.z),center+new Vector3(h.x,-h.y,-h.z),center+new Vector3(h.x,h.y,-h.z),center+new Vector3(-h.x,h.y,-h.z),center+new Vector3(-h.x,-h.y,h.z),center+new Vector3(h.x,-h.y,h.z),center+new Vector3(h.x,h.y,h.z),center+new Vector3(-h.x,h.y,h.z)};
                AddQuad(p[4],p[5],p[6],p[7]);AddQuad(p[1],p[0],p[3],p[2]);AddQuad(p[0],p[4],p[7],p[3]);AddQuad(p[5],p[1],p[2],p[6]);AddQuad(p[3],p[7],p[6],p[2]);AddQuad(p[0],p[1],p[5],p[4]);
            }
            public void AddPyramid(Vector3 center,float width,float depth,float height)
            {
                Vector3 a=center+new Vector3(-width*.5f,0,depth*.5f),b=center+new Vector3(width*.5f,0,depth*.5f),c=center+new Vector3(width*.5f,0,-depth*.5f),d=center+new Vector3(-width*.5f,0,-depth*.5f),top=center+Vector3.up*height;
                AddTriangle(a,b,top);AddTriangle(b,c,top);AddTriangle(c,d,top);AddTriangle(d,a,top);
            }
            void AddQuad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){AddTriangle(a,b,c);AddTriangle(a,c,d);}
            void AddTriangle(Vector3 a,Vector3 b,Vector3 c){int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);}
            public Mesh ToMesh(string name)
            {
                var mesh=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.UploadMeshData(true);return mesh;
            }
        }
    }
}
