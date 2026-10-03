using UnityEngine;

namespace EmpireBattle
{
    public static class BattleRenderBounds
    {
        public struct SurfaceCache
        {
            bool ready;
            Vector3 position;
            int revision;
            public Vector3 Sample(BattlefieldTerrain terrain,Vector2 point)
            {
                if(!BattleOptimizationSettings.RenderWorkOptimizations)return terrain.SurfacePoint(point);
                bool same=ready&&position.x==point.x&&position.z==point.y;
                if(same&&terrain.DamageRevision!=revision)same=!terrain.DamageChangedAt(point,revision);
                if(!same){position=terrain.SurfacePoint(point);ready=true;}
                revision=terrain.DamageRevision;
                return position;
            }
        }
        // Pursuit, routing and recovery can leave soldiers far outside their formation footprint.
        // Bounds follow living bodies, including interpolation and airborne knockdowns.
        public static Bounds Regiment(BattleWorld world,BattalionState unit,float presentationTime,Vector3[] positions=null,SurfaceCache[] surfaceCache=null)
        {
            using var metric=BattleMetrics.Measure(BattleMetrics.Stage.InfantryBounds);
            var bounds=new Bounds();
            var min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);
            var max=new Vector3(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
            bool found=false;
            float interpolation=Mathf.Clamp(presentationTime-world.Time,0,.05f);
            for(int soldier=unit.SoldierStart;soldier<unit.SoldierStart+unit.SoldierCount;soldier++)
            {
                if(!world.Soldiers.Alive[soldier])continue;
                Vector3 position;
                if(world.Soldiers.IsKnockedDown(soldier,presentationTime))position=world.SoldierPosition(soldier,presentationTime);
                else
                {
                    var point=world.Soldiers.Positions[soldier]+world.Soldiers.Velocities[soldier]*interpolation;
                    position=surfaceCache!=null?surfaceCache[soldier].Sample(world.Terrain,point):world.Terrain.SurfacePoint(point);
                }
                if(positions!=null)positions[soldier]=position;
                if(BattleOptimizationSettings.RenderWorkOptimizations)
                {
                    // Accumulate endpoints directly; Bounds.Encapsulate repeatedly
                    // converts center/extents back to endpoints for every soldier.
                    min.x=Mathf.Min(min.x,position.x);min.y=Mathf.Min(min.y,position.y);min.z=Mathf.Min(min.z,position.z);
                    max.x=Mathf.Max(max.x,position.x);max.y=Mathf.Max(max.y,position.y);max.z=Mathf.Max(max.z,position.z);
                    found=true;
                }
                else if(!found){bounds=new Bounds(position,Vector3.zero);found=true;}else bounds.Encapsulate(position);
            }
            if(!found)bounds=new Bounds(world.Terrain.SurfacePoint(unit.Position),Vector3.zero);
            else if(BattleOptimizationSettings.RenderWorkOptimizations)bounds.SetMinMax(min,max);
            bounds.Expand(new Vector3(8,12,8));
            return bounds;
        }
        public static bool FullyInside(Plane[] planes,Bounds bounds)
        {
            var size=bounds.extents;var center=bounds.center;
            foreach(var plane in planes)
            {
                var normal=plane.normal;
                float radius=Mathf.Abs(normal.x)*size.x+Mathf.Abs(normal.y)*size.y+Mathf.Abs(normal.z)*size.z;
                if(Vector3.Dot(normal,center)+plane.distance<radius)return false;
            }
            return true;
        }
        // Same conservative pose padding as regiment bounds: rifles, standards,
        // recovery and mounted silhouettes stay visible at the screen edges.
        public static bool PointVisible(Plane[] planes,Vector3 position)
        {
            foreach(var plane in planes)
            {
                var normal=plane.normal;
                float radius=Mathf.Abs(normal.x)*4f+Mathf.Abs(normal.y)*6f+Mathf.Abs(normal.z)*4f;
                if(Vector3.Dot(normal,position)+plane.distance < -radius)return false;
            }
            return true;
        }
    }
}
