using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace EmpireBattle
{
    // Each grid cell keeps its six index slots. A cut replaces only those slots
    // with degenerate triangles; replacing an old crater restores the originals.
    // Vertices, normals, submesh range and bounds remain unchanged.
    public sealed class TerrainIndexPatches
    {
        readonly Mesh mesh;
        readonly int[] indices=new int[6];
        readonly HashSet<Vector2Int> legacyCuts;
        readonly List<int> legacyIndices;
        public TerrainIndexPatches(Mesh value)
        {
            mesh=value;
            if(!BattleOptimizationSettings.HitchOptimizations)
            {
                legacyCuts=new HashSet<Vector2Int>();
                legacyIndices=new List<int>((BattlefieldTerrain.Resolution-1)*(BattlefieldTerrain.Resolution-1)*6);
            }
        }
        public void SetDamaged(Vector2Int cell,bool damaged)
        {
            if(legacyCuts!=null) { if(damaged)legacyCuts.Add(cell);else legacyCuts.Remove(cell);return; }
            const int n=BattlefieldTerrain.Resolution;
            int i=cell.y*n+cell.x;
            indices[0]=i;indices[1]=damaged?i:i+n;indices[2]=damaged?i:i+1;
            indices[3]=damaged?i:i+1;indices[4]=damaged?i:i+n;indices[5]=damaged?i:i+n+1;
            mesh.SetIndexBufferData(indices,0,(cell.y*(n-1)+cell.x)*6,6,MeshUpdateFlags.DontRecalculateBounds);
        }
        public void Flush()
        {
            if(legacyCuts==null)return;
            const int n=BattlefieldTerrain.Resolution;legacyIndices.Clear();
            for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++)
            {
                if(legacyCuts.Contains(new Vector2Int(x,z)))continue;
                int i=z*n+x;
                legacyIndices.Add(i);legacyIndices.Add(i+n);legacyIndices.Add(i+1);
                legacyIndices.Add(i+1);legacyIndices.Add(i+n);legacyIndices.Add(i+n+1);
            }
            mesh.SetTriangles(legacyIndices,0);
        }
    }
}
