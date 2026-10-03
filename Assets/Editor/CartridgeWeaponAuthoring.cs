using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Original, offline-authored presentation meshes. Dimensions retain the infantry
// rig's existing grip at (0,0,-25) and bore/muzzle at (0,1.8,18).
public static class CartridgeWeaponAuthoring
{
    public const int Version = 2;
    public const int CloseBudget = 1500, CrowdBudget = 300, DistantBudget = 80;
    public const string Root = "Assets/Art/CartridgeWeapons";
    public static readonly Vector3 Muzzle = new Vector3(0, 1.8f, 18);
    public static readonly Vector3 Receiver = new Vector3(-1.8f, 1, -25);

    public static Mesh Prepare(bool single, int level)
    {
        if (level < 0 || level > 2) throw new ArgumentOutOfRangeException(nameof(level));
        Directory.CreateDirectory(Root);
        string name = (single ? "SingleShot" : "Magazine") + "_L" + level;
        string path = Root + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing && existing.name == name + "_v" + Version) return existing;
        var mesh = Create(single, level);
        mesh.name = name + "_v" + Version;
        if (existing) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    public static Mesh Create(bool single, int level)
    {
        bool close = level == 0;
        var b = new Builder();
        if(level==2)
        {
            b.Rings(new[]{new Vector3(0,-1.1f,-40),new Vector3(0,-1.1f,-30),new Vector3(0,.35f,-25),new Vector3(0,.35f,8)},new[]{new Vector2(.9f,2.5f),new Vector2(.9f,2.5f),new Vector2(.65f,.95f),new Vector2(.65f,.6f)},4,true);
            b.Cylinder(new Vector3(0,1.8f,-22),Muzzle,.42f,4);
            b.Cylinder(new Vector3(0,1.65f,-28),new Vector3(0,1.65f,-20),.88f,4);
            if(!single)b.Box(new Vector3(0,-1.6f,-22.6f),new Vector3(1.6f,2.4f,3));
            var distant=b.Mesh();if(distant.triangles.Length/3>DistantBudget)throw new Exception("Distant weapon exceeds budget");return distant;
        }
        int sides = close ? 8 : 4;
        // A dropped butt, narrow wrist and tapered fore-end, rather than boxes.
        float[] z = close ? new[] {-40f,-39,-34,-30,-27,-25,-22,-17,-5,8} : new[] {-40f,-34,-27,-25,-17,8};
        var stock = new List<Vector3>(); var radii = new List<Vector2>();
        foreach (float at in z)
        {
            float centre = at < -30 ? -1.1f : at < -25 ? Mathf.Lerp(-1.1f,.35f,(at+30)/5) : .35f;
            float height = at < -30 ? 2.5f : at < -25 ? Mathf.Lerp(2.5f,.95f,(at+30)/5) : Mathf.Lerp(.95f,.6f,(at+25)/33);
            float width = at < -30 ? .9f : at < -25 ? Mathf.Lerp(.9f,.65f,(at+30)/5) : .65f;
            stock.Add(new Vector3(0,centre,at)); radii.Add(new Vector2(width,height));
        }
        b.Rings(stock.ToArray(),radii.ToArray(),sides,true);
        b.Cylinder(new Vector3(0,1.8f,-22),Muzzle,.42f,close?12:6,close?6:2);
        if(close)b.Cylinder(new Vector3(0,1.8f,17.2f),Muzzle,.48f,12);
        b.Cylinder(new Vector3(0,1.65f,-28),new Vector3(0,1.65f,-20),.88f,sides,close?5:2);
        b.Cylinder(new Vector3(0,2.15f,-28),new Vector3(0,2.15f,-21),.38f,sides);
        b.Cylinder(new Vector3(-.3f,2.15f,-25),new Vector3(-1.8f,1,-25),.22f,sides);
        if(close)b.Cylinder(new Vector3(-1.8f,1,-25.4f),new Vector3(-1.8f,1,-24.6f),.48f,8,3);
        foreach(float at in close?new[]{-14f,-3,7}:new[]{-3f,7})
            b.Cylinder(new Vector3(0,.65f,at-.25f),new Vector3(0,.65f,at+.25f),1.05f,sides,close?4:2);
        if(close)b.Cylinder(new Vector3(0,-1.1f,-40),new Vector3(0,-1.1f,-39.7f),1,sides,2,2.5f);
        var guard = new List<Vector3>();
        int guardSteps=close?8:4;
        for(int i=0;i<=guardSteps;i++)
        {
            float a=Mathf.PI*i/guardSteps;
            guard.Add(new Vector3(0,-.7f-1.65f*Mathf.Sin(a),-25+2.1f*Mathf.Cos(a)));
        }
        b.Rings(guard.ToArray(),Builder.Uniform(guard.Count,new Vector2(.17f,.17f)),close?4:3,false);
        if(close)b.Cylinder(new Vector3(0,-.65f,-25.5f),new Vector3(0,-1.7f,-24.9f),.17f,6);
        b.Box(new Vector3(0,2.55f,-16),new Vector3(.65f,.65f,1.3f));
        b.Box(new Vector3(0,2.6f,16.1f),new Vector3(.24f,.9f,.55f));
        if(close)b.Box(new Vector3(0,2.3f,-17),new Vector3(.9f,.25f,2.1f));
        if(!single)b.Cylinder(new Vector3(0,-2.8f,-22.6f),new Vector3(0,-.4f,-22.6f),1,sides,close?3:2,2);
        if(close)
        {
            foreach(float at in new[]{-35f,5f})
            {
                var loop=new List<Vector3>();
                for(int i=0;i<=4;i++){float a=Mathf.PI*i/4;loop.Add(new Vector3(.65f*Mathf.Cos(a),-1.2f-.6f*Mathf.Sin(a),at));}
                b.Rings(loop.ToArray(),Builder.Uniform(loop.Count,new Vector2(.11f,.11f)),4,false);
            }
            foreach(float at in new[]{-29f,-24,-15})b.Cylinder(new Vector3(-.9f,1.4f,at),new Vector3(-1,1.4f,at),.2f,6);
        }
        Mesh result=b.Mesh();
        int tris=result.triangles.Length/3, budget=close?CloseBudget:CrowdBudget;
        if(tris>budget)throw new Exception($"Weapon exceeds LOD {level} budget: {tris}/{budget}");
        return result;
    }

    public static Material PrepareMaterial()
    {
        Directory.CreateDirectory(Root);
        string path=Root+"/WoodSteel.png";
        if(!File.Exists(path))
        {
            var texture=new Texture2D(256,128,TextureFormat.RGBA32,false);
            for(int y=0;y<128;y++)for(int x=0;x<256;x++)
            {
                float grain=Mathf.Sin(y*.8f+Mathf.Sin(x*.07f)*2)*.035f+Mathf.Sin(y*2.1f+x*.02f)*.012f;
                texture.SetPixel(x,y,x<192?new Color(.30f+grain,.16f+grain*.6f,.075f+grain*.3f):new Color(.18f+grain*.3f,.20f+grain*.3f,.22f+grain*.3f));
            }
            texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
        }
        string materialPath=Root+"/WoodSteel.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,materialPath);}
        material.name="CartridgeWoodSteel";material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",.25f);EditorUtility.SetDirty(material);
        return material;
    }

    sealed class Builder
    {
        readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> triangles=new List<int>();
        public static Vector2[] Uniform(int count,Vector2 radius){var values=new Vector2[count];for(int i=0;i<count;i++)values[i]=radius;return values;}
        int Vertex(Vector3 point,bool wood){int index=vertices.Count;vertices.Add(point);uv.Add(wood?new Vector2(.03f+Mathf.InverseLerp(-40,18,point.z)*.67f,.1f+Mathf.InverseLerp(-4,4,point.y)*.8f):new Vector2(.85f,.5f));return index;}
        void Tri(int a,int b,int c){triangles.Add(a);triangles.Add(b);triangles.Add(c);}
        public void Rings(Vector3[] centres,Vector2[] radii,int sides,bool wood,Quaternion? rotation=null)
        {
            int start=vertices.Count;
            for(int ring=0;ring<centres.Length;ring++)
            {
                Quaternion basis=rotation??(wood?Quaternion.identity:Quaternion.FromToRotation(Vector3.forward,(centres[Mathf.Min(ring+1,centres.Length-1)]-centres[Mathf.Max(0,ring-1)]).normalized));
                for(int i=0;i<sides;i++){float angle=(i+.5f)*2*Mathf.PI/sides;Vertex(centres[ring]+basis*new Vector3(Mathf.Cos(angle)*radii[ring].x,Mathf.Sin(angle)*radii[ring].y,0),wood);}
            }
            for(int ring=0;ring<centres.Length-1;ring++)for(int i=0;i<sides;i++)
            {int a=start+ring*sides+i,b=start+ring*sides+(i+1)%sides,c=a+sides,d=b+sides;Tri(a,b,c);Tri(b,d,c);}
            int first=Vertex(centres[0],wood),last=Vertex(centres[centres.Length-1],wood),end=start+(centres.Length-1)*sides;
            for(int i=0;i<sides;i++){Tri(first,start+(i+1)%sides,start+i);Tri(last,end+i,end+(i+1)%sides);}
        }
        public void Cylinder(Vector3 from,Vector3 to,float radius,int sides,int rings=2,float verticalScale=1)
        {
            var centres=new Vector3[rings];for(int i=0;i<rings;i++)centres[i]=Vector3.Lerp(from,to,i/(float)(rings-1));
            Rings(centres,Uniform(rings,new Vector2(radius,radius*verticalScale)),sides,false,Quaternion.FromToRotation(Vector3.forward,(to-from).normalized));
        }
        public void Box(Vector3 centre,Vector3 size)
        {
            int start=vertices.Count;
            for(int i=0;i<8;i++)Vertex(centre+Vector3.Scale(size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)),false);
            int[] indices={0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5};
            foreach(int index in indices)triangles.Add(start+index);
        }
        public Mesh Mesh(){var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
    }
}
