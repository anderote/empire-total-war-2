using UnityEngine;
using System.Collections.Generic;

namespace EmpireBattle
{
    /// <summary>Shared deterministic landscape. All simulation and scenery sample the same height grid.</summary>
    public sealed partial class BattlefieldTerrain
    {
        public readonly Vector2[] FarmCenters;
        public readonly Vector2[] FarmHalfExtents;
        public readonly Vector2[] WoodsCenters;
        public readonly float[] WoodsRadii;
        public const int Resolution = 257;
        readonly float[] heights;
        readonly EarthworkHeightIndex heightIndex;
        readonly ArtilleryEarthwork[] cuts = new ArtilleryEarthwork[512];
        readonly Dictionary<Vector2Int, List<ArtilleryEarthwork>> cutCells = new Dictionary<Vector2Int, List<ArtilleryEarthwork>>();
        public readonly Dictionary<Vector2Int, int> DamageCells = new Dictionary<Vector2Int, int>();
        int cutCursor;
        public int DamageRevision { get; private set; }
        public float CellSize => cellSize;
        public float HalfSize => halfSize;
        Vector2Int DamageCell(Vector2 p) => new Vector2Int(Mathf.FloorToInt((p.x + halfSize) / cellSize), Mathf.FloorToInt((p.y + halfSize) / cellSize));
        void IndexCut(ArtilleryEarthwork cut, bool add)
        {
            Vector2Int min = DamageCell(cut.Center - Vector2.one * cut.Extent), max = DamageCell(cut.Center + Vector2.one * cut.Extent);
            for (int z = Mathf.Max(0, min.y); z <= Mathf.Min(Resolution - 2, max.y); z++)
            for (int x = Mathf.Max(0, min.x); x <= Mathf.Min(Resolution - 2, max.x); x++)
            {
                var cell = new Vector2Int(x, z);
                if (!cutCells.TryGetValue(cell, out var list)) { list = new List<ArtilleryEarthwork>(); cutCells[cell] = list; }
                if (add) list.Add(cut); else list.Remove(cut);
                DamageCells[cell] = DamageRevision;
            }
            heightIndex.Index(cut, add);
        }
        public void Carve(ArtilleryImpact impact)
        {
            if (!impact.TerrainStrike || impact.AmmunitionType == ArtilleryAmmunition.Canister || ArtillerySystem.IsCloudShell(impact.AmmunitionType)) return;
            DamageRevision++;
            if (cuts[cutCursor] != null) IndexCut(cuts[cutCursor], false);
            cuts[cutCursor] = ArtilleryEarthwork.FromImpact(impact);
            IndexCut(cuts[cutCursor], true);
            cutCursor = (cutCursor + 1) % cuts.Length;
        }
        public List<ArtilleryEarthwork> EarthworksIn(Bounds bounds)
        {
            var result=new List<ArtilleryEarthwork>();
            EarthworksIn(bounds,result,new HashSet<ArtilleryEarthwork>());return result;
        }
        public bool DamageChangedAt(Vector2 point,int since) => DamageCells.TryGetValue(DamageCell(point),out int revision)&&revision>since;
        public bool DamageChangedIn(Bounds bounds,int since)
        {
            Vector2Int min=DamageCell(new Vector2(bounds.min.x,bounds.min.z)),max=DamageCell(new Vector2(bounds.max.x,bounds.max.z));
            for(int z=Mathf.Max(0,min.y);z<=Mathf.Min(Resolution-2,max.y);z++)
            for(int x=Mathf.Max(0,min.x);x<=Mathf.Min(Resolution-2,max.x);x++)
                if(DamageCells.TryGetValue(new Vector2Int(x,z),out int revision)&&revision>since)return true;
            return false;
        }
        public void EarthworksIn(Bounds bounds,List<ArtilleryEarthwork> result,HashSet<ArtilleryEarthwork> unique)
        {
            result.Clear();unique.Clear();
            Vector2Int min=DamageCell(new Vector2(bounds.min.x,bounds.min.z)),max=DamageCell(new Vector2(bounds.max.x,bounds.max.z));
            for(int z=min.y;z<=max.y;z++)for(int x=min.x;x<=max.x;x++)
                if(cutCells.TryGetValue(new Vector2Int(x,z),out var list))foreach(var cut in list)if(unique.Add(cut))result.Add(cut);
            result.Sort((a,b)=>b.Seed.CompareTo(a.Seed));
        }
        public bool HasDamage(Vector2Int cell) => cutCells.TryGetValue(cell, out var list) && list.Count > 0;
        sealed class SharedDamageMesh { public Mesh Mesh; public int References, Revision; }
        readonly Dictionary<Vector2Int, SharedDamageMesh> sharedDamageMeshes = new Dictionary<Vector2Int, SharedDamageMesh>();
        // Renderer and physics own references to the same geometry. Each cell is sampled
        // once per revision; collider consumers still reassign sharedMesh to recook physics.
        public Mesh AcquireDamageMesh(Vector2Int key)
        {
            if(!sharedDamageMeshes.TryGetValue(key,out var entry))
            {
                entry=new SharedDamageMesh {Revision=-1};sharedDamageMeshes.Add(key,entry);
            }
            entry.References++;return RefreshDamageMesh(key);
        }
        public Mesh RefreshDamageMesh(Vector2Int key)
        {
            var entry=sharedDamageMeshes[key];
            int revision=DamageCells.TryGetValue(key,out int changed)?changed:0;
            if(entry.Revision!=revision){entry.Mesh=BuildDamageMesh(key,entry.Mesh);entry.Revision=revision;}
            return entry.Mesh;
        }
        public void ReleaseDamageMesh(Vector2Int key)
        {
            var entry=sharedDamageMeshes[key];
            if(--entry.References>0)return;
            if(Application.isPlaying)Object.Destroy(entry.Mesh);else Object.DestroyImmediate(entry.Mesh);
            sharedDamageMeshes.Remove(key);
        }
        Vector3[] damageVertices;
        Color[] damageColors;
        int[] damageTriangles;
        public Mesh BuildDamageMesh(Vector2Int key,Mesh mesh=null)
        {
            int n=Mathf.Clamp(Mathf.CeilToInt(cellSize/.24f),4,64)+1;
            if(damageVertices==null)
            {
                damageVertices=new Vector3[n*n];damageColors=new Color[n*n];damageTriangles=new int[(n-1)*(n-1)*6];int t=0;
                for(int z=0;z<n;z++)for(int x=0;x<n;x++)
                {
                    int i=z*n+x;damageColors[i]=Color.white;if(x==n-1||z==n-1)continue;
                    damageTriangles[t++]=i;damageTriangles[t++]=i+n;damageTriangles[t++]=i+1;
                    damageTriangles[t++]=i+1;damageTriangles[t++]=i+n;damageTriangles[t++]=i+n+1;
                }
            }
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                Vector2 point=new Vector2(-halfSize+(key.x+x/(float)(n-1))*cellSize,-halfSize+(key.y+z/(float)(n-1))*cellSize);
                damageVertices[z*n+x]=SurfacePoint(point);
            }
            bool created=!mesh;
            if(created){mesh=new Mesh{name="Excavated terrain cell"};mesh.MarkDynamic();}
            mesh.vertices=damageVertices;
            if(created){mesh.colors=damageColors;mesh.triangles=damageTriangles;}
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public Vector3 NormalAt(Vector2 p)
        {
            const float step = .12f;
            return new Vector3(HeightAt(p - Vector2.right * step) - HeightAt(p + Vector2.right * step), 2f * step,
                HeightAt(p - Vector2.up * step) - HeightAt(p + Vector2.up * step)).normalized;
        }
        public float HeightAt(Vector2 point)
        {
            float height = BaseHeightAt(point);
            if (BattleOptimizationSettings.FastForwardOptimizations)
                return height + Mathf.Clamp(heightIndex.Displacement(point), -2.5f, 1f);
            if (cutCells.TryGetValue(DamageCell(point), out var list))
            {
                float displacement = 0;
                foreach (var cut in list) displacement += cut.Displacement(point);
                height += Mathf.Clamp(displacement, -2.5f, 1f);
            }
            return height;
        }
        readonly float halfSize;
        readonly float gridScale;
        readonly float cellSize;
        readonly bool enabled;

        public BattlefieldTerrain(BattleConfig config)
        {
            halfSize = config.FieldHalfSize;
            gridScale = (Resolution - 1) / (2f * halfSize);
            cellSize = 1f / gridScale;
            heightIndex = new EarthworkHeightIndex(halfSize, cellSize);
            enabled = config.EnableLandscape;
            SetupRegion(config);
            heights = new float[Resolution * Resolution];
            if (!enabled)
            {
                FarmCenters = new Vector2[0]; FarmHalfExtents = new Vector2[0];
                WoodsCenters = new Vector2[0]; WoodsRadii = new float[0];
                return;
            }
            float scale = halfSize / 1500f;
            FarmCenters = new[] { new Vector2(-1050f, -650f) * scale, new Vector2(1080f, 610f) * scale };
            FarmHalfExtents = new[] { new Vector2(12f, 9f) * scale, new Vector2(14f, 10f) * scale };
            WoodsCenters = new[]
            {
                new Vector2(-1260f, -170f) * scale, new Vector2(-1020f, 960f) * scale,
                new Vector2(1260f, -850f) * scale, new Vector2(1210f, 120f) * scale,
                new Vector2(-180f, 1270f) * scale, new Vector2(610f, -1260f) * scale
            };
            WoodsRadii = new[] { 155f * scale, 190f * scale, 170f * scale, 130f * scale, 175f * scale, 150f * scale };
            if(Profile!=null)
            {RegionalLandmarks(config,out var farms,out var half,out var woods,out var radii);FarmCenters=farms;FarmHalfExtents=half;WoodsCenters=woods;WoodsRadii=radii;}
            float phase = (config.Seed % 997) * .0063f;
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                float worldX = -halfSize + x * cellSize, worldZ = -halfSize + z * cellSize;
                // Normalize landscape landmarks to the default 3-km map, preserving a broad playable lane.
                float nx = worldX / scale, nz = worldZ / scale;
                float ridge = 5.5f * Mathf.Exp(-(nx * nx / 950000f + (nz - 65f) * (nz - 65f) / 70000f));
                float hillWest = 16f * Mathf.Exp(-((nx + 1100f) * (nx + 1100f) / 180000f + (nz - 800f) * (nz - 800f) / 200000f));
                float hillEast = 21f * Mathf.Exp(-((nx - 1100f) * (nx - 1100f) / 230000f + (nz + 850f) * (nz + 850f) / 230000f));
                float lane = Mathf.Exp(-(nx * nx / 1250000f + nz * nz / 180000f));
                float rolling = (Mathf.Sin(nx * .0031f + phase) * Mathf.Cos(nz * .0027f - phase) + 1f) * (1.3f + 2.5f * (1f - lane));
                float height=(ridge + hillWest + hillEast + rolling) * Mathf.Min(1f, scale);
                heights[z * Resolution + x] = Profile==null?height:RegionalHeight(worldX,worldZ,height,phase);
            }
        }

        public float BaseHeightAt(Vector2 point)
        {
            if (!enabled) return 0f;
            float gx = Mathf.Clamp((point.x + halfSize) * gridScale, 0f, Resolution - 1f);
            float gz = Mathf.Clamp((point.y + halfSize) * gridScale, 0f, Resolution - 1f);
            int x = Mathf.Min(Resolution - 2, Mathf.FloorToInt(gx));
            int z = Mathf.Min(Resolution - 2, Mathf.FloorToInt(gz));
            float tx = gx - x, tz = gz - z;
            int i = z * Resolution + x;
            float near = Mathf.Lerp(heights[i], heights[i + 1], tx);
            float far = Mathf.Lerp(heights[i + Resolution], heights[i + Resolution + 1], tx);
            return Mathf.Lerp(near, far, tz);
        }
        public Vector3 SurfacePoint(Vector2 point, float offset = 0f) => new Vector3(point.x, HeightAt(point) + offset, point.y);

        public float SpeedMultiplier(Vector2 point)
        {
            if (!enabled) return 1f;
            float forest = InWoods(point) ? .7f : 1f;
            float crops = 1f;
            for (int i = 0; i < FarmCenters.Length; i++)
            {
                Vector2 d = point - FarmCenters[i];
                if (Mathf.Abs(d.x) < 150f * halfSize / 1500f && Mathf.Abs(d.y) < 110f * halfSize / 1500f) { crops = .9f; break; }
            }
            float h = HeightAt(point);
            float dx = (HeightAt(point + Vector2.right * 8f) - h) / 8f;
            float dz = (HeightAt(point + Vector2.up * 8f) - h) / 8f;
            float slope = Mathf.Sqrt(dx * dx + dz * dz);
            return forest * crops * Mathf.Clamp(1f - slope * 2.5f, .65f, 1f);
        }
        public bool IsBlocked(Vector2 point, float margin = 0f)
        {
            if(RegionalBlocked(point,margin))return true;
            for (int i = 0; i < FarmCenters.Length; i++)
            {
                Vector2 d = point - FarmCenters[i], half = FarmHalfExtents[i];
                if (Mathf.Abs(d.x) <= half.x + margin && Mathf.Abs(d.y) <= half.y + margin) return true;
            }
            return false;
        }
        public bool BlocksLine(Vector2 from, Vector2 to, float height = 1.5f)
        {
            if (RegionalBlocksLine(from,to)) return true;
            if (!enabled) return false;
            float distance = Vector2.Distance(from, to);
            if (distance < .01f) return false;
            int steps = Mathf.Clamp(Mathf.CeilToInt(distance / 15f), 2, 96);
            float startHeight = HeightAt(from) + height, endHeight = HeightAt(to) + height;
            float woodedLength = 0f;
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                Vector2 p = Vector2.Lerp(from, to, t);
                if (HeightAt(p) > Mathf.Lerp(startHeight, endHeight, t) + .15f || IsBlocked(p)) return true;
                if (InWoods(p)) woodedLength += distance / steps;
                if (woodedLength > 35f) return true;
            }
            return false;
        }
        bool InWoods(Vector2 point)
        {
            for (int i = 0; i < WoodsCenters.Length; i++)
                if ((point - WoodsCenters[i]).sqrMagnitude < WoodsRadii[i] * WoodsRadii[i]) return true;
            return false;
        }
    }
}
