using System;
using System.Collections.Generic;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class VegetationVerification
{
    [MenuItem("Empire Battle/Verify vegetation geometry")]
    public static void Verify()
    {
        VerifyMeadowLod();
        for(int species=0;species<3;species++)
        {
            Mesh near=null,far=null;
            try
            {
                near=ProceduralVegetation.Tree(species,8103+species*73);
                far=ProceduralVegetation.Tree(species,8103+species*73,true);
                Validate(near);Validate(far);
                var nearSkeleton=Skeleton(near,8);var farSkeleton=Skeleton(far,4);
                Require(nearSkeleton.Count==farSkeleton.Count,"LOD keeps every branch");
                for(int i=0;i<nearSkeleton.Count;i++)
                    Require((nearSkeleton[i]-farSkeleton[i]).sqrMagnitude<.000001f,"LOD preserves branch positions");
                Require(far.vertexCount<near.vertexCount/3,"Distant canopy reduces geometry substantially");
            }
            finally { if(near)UnityEngine.Object.DestroyImmediate(near);if(far)UnityEngine.Object.DestroyImmediate(far); }
        }
        for(int variant=0;variant<3;variant++)
        {
            var hedge=ProceduralVegetation.Hedge(7103+variant*131);
            try {Validate(hedge);}
            finally {UnityEngine.Object.DestroyImmediate(hedge);}
        }
        Debug.Log("VEGETATION_GEOMETRY_VERIFICATION_PASSED");
    }
    static void VerifyMeadowLod()
    {
            // Distant triangles preserve each retained blade's roots and tip.
        var build=typeof(RuralLandscape).GetMethod("BuildGrassMesh",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        var owner=new GameObject("Meadow LOD verification");
        Mesh near=null,far=null;
        try
        {
            var landscape=owner.AddComponent<RuralLandscape>();
            near=(Mesh)build.Invoke(landscape,new object[]{16f,false});
            far=(Mesh)build.Invoke(landscape,new object[]{16f,true});
            Validate(near);Validate(far);
            Require(far.vertexCount<near.vertexCount/10,"Distant meadow removes at least 90% of near geometry");
            var vertices=near.vertices;var colors=near.colors;var normals=near.normals;var roots=near.uv;
            var farVertices=far.vertices;var farNormals=far.normals;var farRoots=far.uv;
            int index=0;
            for(int blade=0;blade<vertices.Length;blade+=5)
            {
                if(colors[blade].a<.5f)continue;
                foreach(int offset in new[]{0,1,4})
                {
                    int i=blade+offset;
                    Require(index<farVertices.Length,"Retained blades exist in far mesh");
                    Require(vertices[i]==farVertices[index] && normals[i]==farNormals[index] && roots[i]==farRoots[index],
                        "LOD preserves silhouette positions, lighting and terrain roots");
                    index++;
                }
            }
            Require(index==farVertices.Length,"Far mesh contains only retained silhouettes");
        }
        finally
        {
            if(near)UnityEngine.Object.DestroyImmediate(near);
            if(far)UnityEngine.Object.DestroyImmediate(far);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }
    static List<Vector3> Skeleton(Mesh mesh,int sides)
    {
        var result=new List<Vector3>();var colors=mesh.colors;var vertices=mesh.vertices;
        var bark=new List<Vector3>();
        for(int i=0;i<vertices.Length;i++)if(colors[i].a<.5f)bark.Add(vertices[i]);
        for(int ring=0;ring<bark.Count;ring+=sides+1)
        {
            Vector3 center=Vector3.zero;
            for(int side=0;side<sides;side++)center+=bark[ring+side];
            result.Add(center/sides);
        }
        return result;
    }
    static void Validate(Mesh mesh)
    {
        var vertices=mesh.vertices;var normals=mesh.normals;var colors=mesh.colors;var uv=mesh.uv;var indices=mesh.triangles;
        Require(vertices.Length==normals.Length && vertices.Length==colors.Length && vertices.Length==uv.Length,"All geometry has lighting and material attributes");
        Require(vertices.Length<65536 || mesh.indexFormat==UnityEngine.Rendering.IndexFormat.UInt32,"Dense foliage supports all vertex indices");
        for(int i=0;i<vertices.Length;i++)
        {
            Require(float.IsFinite(vertices[i].x)&&float.IsFinite(vertices[i].y)&&float.IsFinite(vertices[i].z),"Finite geometry");
            Require(mesh.bounds.Contains(vertices[i]),"Culling bounds contain geometry");
            Require(Mathf.Abs(normals[i].sqrMagnitude-1)<.01f,"Unit lighting normals");
        }
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2];
            Require(a>=0&&a<vertices.Length&&b>=0&&b<vertices.Length&&c>=0&&c<vertices.Length,"Valid triangle indices");
            Vector3 face=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
            Require(face.sqrMagnitude>.000000000001f,"Nondegenerate vegetation triangles");
            Require(Vector3.Dot(face,normals[a]+normals[b]+normals[c])>0,"Triangle winding matches lighting normals");
        }
    }
    static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Vegetation verification failed: "+message);}
}
