using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    // Fine sampling bins retain coarse ownership bounds for identical terrain heights.
    sealed class EarthworkHeightIndex
    {
        const float BinSize = 2f;
        struct Entry { public ArtilleryEarthwork Cut; public Vector2Int Min, Max; }
        readonly Dictionary<Vector2Int, List<Entry>> bins = new Dictionary<Vector2Int, List<Entry>>();
        readonly float halfSize, cellSize;
        public EarthworkHeightIndex(float halfSize, float cellSize)
        { this.halfSize = halfSize; this.cellSize = cellSize; }
        Vector2Int CoarseCell(Vector2 p) => new Vector2Int(Mathf.FloorToInt((p.x + halfSize) / cellSize), Mathf.FloorToInt((p.y + halfSize) / cellSize));
        static Vector2Int Bin(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / BinSize), Mathf.FloorToInt(p.y / BinSize));
        public void Index(ArtilleryEarthwork cut, bool add)
        {
            // Shell support <= 1.145 * 1.25; furrow width <= 1.4 * 1.25
            // plus .4 lateral bend. Include a margin for floating point boundaries.
            float width = cut.Radius * (cut.Shell ? 1.44f : 2.16f);
            float length = cut.HalfLength * (cut.Shell ? 1.44f : 1.001f);
            var extent = new Vector2(Mathf.Abs(cut.Right.x) * width + Mathf.Abs(cut.Forward.x) * length + .001f,
                Mathf.Abs(cut.Right.y) * width + Mathf.Abs(cut.Forward.y) * length + .001f);
            Vector2Int min = Bin(cut.Center - extent), max = Bin(cut.Center + extent);
            var entry = new Entry { Cut = cut, Min = CoarseCell(cut.Center - Vector2.one * cut.Extent), Max = CoarseCell(cut.Center + Vector2.one * cut.Extent) };
            entry.Min = new Vector2Int(Mathf.Max(0, entry.Min.x), Mathf.Max(0, entry.Min.y));
            entry.Max = new Vector2Int(Mathf.Min(BattlefieldTerrain.Resolution - 2, entry.Max.x), Mathf.Min(BattlefieldTerrain.Resolution - 2, entry.Max.y));
            for (int y = min.y; y <= max.y; y++) for (int x = min.x; x <= max.x; x++)
            {
                var key = new Vector2Int(x, y);
                if (add)
                {
                    if (!bins.TryGetValue(key, out var entries)) { entries = new List<Entry>(); bins.Add(key, entries); }
                    entries.Add(entry);
                }
                else if (bins.TryGetValue(key, out var entries))
                {
                    for (int i = entries.Count - 1; i >= 0; i--) if (ReferenceEquals(entries[i].Cut, cut)) entries.RemoveAt(i);
                    if (entries.Count == 0) bins.Remove(key);
                }
            }
        }
        public float Displacement(Vector2 point)
        {
            if (!bins.TryGetValue(Bin(point), out var entries)) return 0f;
            var cell = CoarseCell(point); float sum = 0f;
            foreach (var entry in entries)
                if (cell.x >= entry.Min.x && cell.x <= entry.Max.x && cell.y >= entry.Min.y && cell.y <= entry.Max.y)
                    sum += entry.Cut.Displacement(point);
            return sum;
        }
    }
}
