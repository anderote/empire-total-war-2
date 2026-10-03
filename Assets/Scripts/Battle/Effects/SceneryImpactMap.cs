using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public enum SmallArmsSurface { Dirt, Masonry, Wood, Foliage }

    // Lightweight visual proxies for instanced scenery; no physics colliders per tree.
    // These affect presentation only, not formation combat/cover calculations.
    public sealed class SceneryImpactMap
    {
        sealed class Surface { public Bounds Bounds; public SmallArmsSurface Kind; public bool Disabled; }
        const float CellSize=32f;
        readonly Dictionary<Vector2Int,List<Surface>> cells=new Dictionary<Vector2Int,List<Surface>>();
        static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.z/CellSize));
        public void Register(Bounds bounds,SmallArmsSurface kind)
        {
            Vector2Int min=Cell(bounds.min),max=Cell(bounds.max);
            var surface = new Surface { Bounds=bounds,Kind=kind };
            for(int z=min.y;z<=max.y;z++)for(int x=min.x;x<=max.x;x++)
            {
                var cell=new Vector2Int(x,z);
                if(!cells.TryGetValue(cell,out var surfaces))
                {surfaces=new List<Surface>();cells.Add(cell,surfaces);}
                surfaces.Add(surface);
            }
        }
        public void DisableFoliageAt(Vector3 center)
        {
            if (!cells.TryGetValue(Cell(center), out var surfaces)) return;
            foreach (var surface in surfaces)
                if (surface.Kind == SmallArmsSurface.Foliage && (surface.Bounds.center - center).sqrMagnitude < 4f) surface.Disabled = true;
        }
        public bool Trace(Vector3 start,Vector3 end,out Vector3 position,out Vector3 normal,out SmallArmsSurface kind)
        {
            Vector3 delta=end-start;
            float length=delta.magnitude,nearest=length;
            Vector3 direction=length>.001f?delta/length:Vector3.forward;
            var ray=new Ray(start,direction);
            kind=SmallArmsSurface.Dirt;normal=Vector3.up;position=end;
            bool found=false;
            int steps=Mathf.Clamp(Mathf.CeilToInt(length/(CellSize*.4f)),1,256);
            var previous=new Vector2Int(int.MinValue,int.MinValue);
            for(int step=0;step<=steps;step++)
            {
                Vector2Int cell=Cell(Vector3.Lerp(start,end,step/(float)steps));
                if(cell==previous)continue;
                previous=cell;
                if(!cells.TryGetValue(cell,out var surfaces))continue;
                foreach(var surface in surfaces)
                {
                    if(surface.Disabled || surface.Bounds.Contains(start))continue;
                    if(!surface.Bounds.IntersectRay(ray,out float distance)||distance<.05f||distance>=nearest)continue;
                    nearest=distance;position=start+direction*distance;kind=surface.Kind;found=true;
                    Vector3 local=position-surface.Bounds.center,extent=surface.Bounds.extents;
                    float x=Mathf.Abs(Mathf.Abs(local.x)-extent.x),y=Mathf.Abs(Mathf.Abs(local.y)-extent.y),z=Mathf.Abs(Mathf.Abs(local.z)-extent.z);
                    normal=x<y&&x<z?Vector3.right*Mathf.Sign(local.x):y<z?Vector3.up*Mathf.Sign(local.y):Vector3.forward*Mathf.Sign(local.z);
                }
            }
            return found;
        }
    }
}
