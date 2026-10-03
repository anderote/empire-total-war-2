using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    // Built once from the cheapest authored pose. Atlas regions stay separate;
    // the same collapse map is applied to both endpoints of the GPU pose morph.
    public static class DistantUnitMesh
    {
        public const float FarDistance = 180f;
        public const float VeryFarDistance = 350f;
        public static int Tier(float squaredDistance) => squaredDistance > VeryFarDistance * VeryFarDistance ? 2
            : squaredDistance > FarDistance * FarDistance ? 1 : 0;

        public static Mesh Build(Mesh source, float cellSize)
        {
            var points = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            var next = new List<Vector3>(); source.GetUVs(2, next);
            var nextNormals = new List<Vector3>(); source.GetUVs(3, nextNormals);
            // Sword carry has a second pair of pose endpoints. Preserve them
            // with the same collapse map so distant LODs keep readiness blending.
            var optional=new List<Vector4>[4];
            for(int channel=0;channel<4;channel++){
                optional[channel]=new List<Vector4>();source.GetUVs(channel+4,optional[channel]);
            }
            var groups = new Dictionary<(int,int,int,int,int),int>();
            var remap = new int[points.Length];
            var counts = new List<int>();
            var positions = new List<Vector3>(); var ns = new List<Vector3>();
            var texcoords = new List<Vector2>();
            var targets = new List<Vector3>(); var targetNormals = new List<Vector3>();
            for(int i=0;i<points.Length;i++)
            {
                Vector3 p=points[i]; Vector2 t=uv.Length==points.Length?uv[i]:Vector2.zero;
                var key=(Mathf.FloorToInt(p.x/cellSize),Mathf.FloorToInt(p.y/cellSize),Mathf.FloorToInt(p.z/cellSize),
                    Mathf.FloorToInt(t.x*16),Mathf.FloorToInt(t.y*16));
                if(!groups.TryGetValue(key,out int group))
                {
                    group=positions.Count;groups.Add(key,group);counts.Add(0);
                    positions.Add(Vector3.zero);ns.Add(Vector3.zero);texcoords.Add(Vector2.zero);
                    targets.Add(Vector3.zero);targetNormals.Add(Vector3.zero);
                }
                remap[i]=group;counts[group]++;
                Vector3 n=normals.Length==points.Length?normals[i]:Vector3.up;
                positions[group]+=p;ns[group]+=n;texcoords[group]+=t;
                targets[group]+=next.Count==points.Length?next[i]:p;
                targetNormals[group]+=nextNormals.Count==points.Length?nextNormals[i]:n;
            }
            var triangles = source.triangles; var kept=new List<int>();
            var unique=new HashSet<(int,int,int)>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=remap[triangles[i]],b=remap[triangles[i+1]],c=remap[triangles[i+2]];
                if(a==b||b==c||a==c)continue;
                // Canonical cyclic order preserves opposite winding faces.
                var key=a<b&&a<c?(a,b,c):b<c?(b,c,a):(c,a,b);
                if(unique.Add(key)){kept.Add(a);kept.Add(b);kept.Add(c);}
            }
            if(kept.Count==0||kept.Count>=triangles.Length)return source;
            for(int i=0;i<positions.Count;i++)
            {
                positions[i]/=counts[i];targets[i]/=counts[i];texcoords[i]/=counts[i];
                ns[i]=ns[i].sqrMagnitude>.000001f?ns[i].normalized:Vector3.up;
                targetNormals[i]=targetNormals[i].sqrMagnitude>.000001f?targetNormals[i].normalized:ns[i];
            }
            var mesh=new Mesh{name=source.name+" distant "+cellSize,hideFlags=HideFlags.DontSave};
            mesh.indexFormat=source.indexFormat;
            mesh.SetVertices(positions);mesh.SetNormals(ns);mesh.SetUVs(0,texcoords);
            mesh.SetUVs(2,targets);mesh.SetUVs(3,targetNormals);mesh.SetTriangles(kept,0);
            for(int channel=0;channel<4;channel++)if(optional[channel].Count==points.Length)
            {
                var collapsed=new Vector4[positions.Count];
                for(int i=0;i<points.Length;i++)collapsed[remap[i]]+=optional[channel][i];
                for(int i=0;i<collapsed.Length;i++){
                    collapsed[i]/=counts[i];
                    if(channel>=2){Vector3 n=collapsed[i];n.Normalize();collapsed[i]=new Vector4(n.x,n.y,n.z,0);}
                }
                mesh.SetUVs(channel+4,new List<Vector4>(collapsed));
            }
            // Keep the authored conservative bounds, including the morph endpoint.
            mesh.bounds=source.bounds;return mesh;
        }
    }
}
