using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public static class ArtilleryTreeFracture
    {
        struct Vertex { public Vector3 P, N; public Color C; public Vector2 UV; }
        public static Mesh Clip(Mesh source, bool upper)
        {
            float plane = source.bounds.min.y + source.bounds.size.y * .08f;
            var positions = source.vertices; var normals = source.normals; var colors = source.colors; var uv = source.uv; var indices = source.triangles;
            var result = new List<Vector3>(); var ns = new List<Vector3>(); var cs = new List<Color>(); var ts = new List<int>(); var uvs = new List<Vector2>();
            var polygon = new List<Vertex>(4);
            for (int i = 0; i < indices.Length; i += 3)
            {
                polygon.Clear();
                for (int edge = 0; edge < 3; edge++)
                {
                    int a = indices[i + edge], b = indices[i + (edge + 1) % 3];
                    var va = new Vertex { P = positions[a], N = normals.Length > a ? normals[a] : Vector3.up, C = colors.Length > a ? colors[a] : Color.white, UV = uv.Length > a ? uv[a] : Vector2.zero };
                    var vb = new Vertex { P = positions[b], N = normals.Length > b ? normals[b] : Vector3.up, C = colors.Length > b ? colors[b] : Color.white, UV = uv.Length > b ? uv[b] : Vector2.zero };
                    bool insideA = upper ? va.P.y >= plane : va.P.y <= plane;
                    bool insideB = upper ? vb.P.y >= plane : vb.P.y <= plane;
                    if (insideA) polygon.Add(va);
                    if (insideA != insideB)
                    {
                        float t = (plane - va.P.y) / (vb.P.y - va.P.y);
                        polygon.Add(new Vertex { P = Vector3.Lerp(va.P, vb.P, t), N = Vector3.Lerp(va.N, vb.N, t).normalized, C = Color.Lerp(va.C, vb.C, t), UV = Vector2.Lerp(va.UV,vb.UV,t) });
                    }
                }
                if (polygon.Count < 3) continue;
                int start = result.Count;
                foreach (var v in polygon) { result.Add(v.P); ns.Add(v.N); cs.Add(v.C); uvs.Add(v.UV); }
                for (int j = 1; j < polygon.Count - 1; j++) { ts.Add(start); ts.Add(start + j); ts.Add(start + j + 1); }
            }
            var mesh = new Mesh { name = upper ? "Snapped tree crown" : "Broken tree stump", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(result); mesh.SetNormals(ns); mesh.SetColors(cs); mesh.SetUVs(0,uvs); mesh.SetTriangles(ts, 0); mesh.RecalculateBounds(); return mesh;
        }
    }
}
