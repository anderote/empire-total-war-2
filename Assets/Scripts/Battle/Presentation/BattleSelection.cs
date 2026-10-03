using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleView
    {
        readonly Vector2[] selectionCorners = new Vector2[8];
        readonly Vector3[] selectionGroundCorners = new Vector3[4];
        int selectionCornerCount = 4;

        // Project the ground footprint itself, preserving perspective and rotation.
        bool FormationOverlaps(BattalionState unit, Rect rect)
        {
            Vector2 half=AtlasBlend>=.15f?AtlasHalfExtents(unit):FormationMath.HalfExtents(unit);
            Vector2 right=FormationMath.Right(unit.Facing)*half.x,forward=FormationMath.Forward(unit.Facing)*half.y;
            for(int i=0;i<4;i++)
            {
                Vector2 ground=unit.Position+(i==0||i==3?-right:right)+(i<2?-forward:forward);
                selectionGroundCorners[i]=world.Terrain.SurfacePoint(ground,AtlasBlend>=.15f?.12f:.9f);
            }
            // At close zoom the camera can cut through a formation. Clip its footprint
            // before projection so a visible edge still participates in drag selection.
            selectionCornerCount=0;
            float near=battleCamera.nearClipPlane+.001f;
            for(int i=0;i<4;i++)
            {
                Vector3 a=selectionGroundCorners[(i+3)%4],b=selectionGroundCorners[i];
                float az=Vector3.Dot(a-battleCamera.transform.position,battleCamera.transform.forward);
                float bz=Vector3.Dot(b-battleCamera.transform.position,battleCamera.transform.forward);
                if((az>=near)!=(bz>=near))
                    selectionCorners[selectionCornerCount++]=battleCamera.WorldToScreenPoint(Vector3.Lerp(a,b,(near-az)/(bz-az)));
                if(bz>=near)selectionCorners[selectionCornerCount++]=battleCamera.WorldToScreenPoint(b);
            }
            return SelectionOutlineOverlaps(rect);
        }

        bool SelectionOutlineOverlaps(Rect rect)
        {
            // Separating-axis test for a projected convex formation and the selection box.
            if(selectionCornerCount<3||Separated(Vector2.right,rect)||Separated(Vector2.up,rect))return false;
            for(int i=0;i<selectionCornerCount;i++)
            {
                Vector2 edge=selectionCorners[(i+1)%selectionCornerCount]-selectionCorners[i];
                if(Separated(new Vector2(-edge.y,edge.x),rect))return false;
            }
            return true;
        }
        bool Separated(Vector2 axis,Rect rect)
        {
            float minimum=float.MaxValue,maximum=float.MinValue;
            for(int i=0;i<selectionCornerCount;i++)
            {
                float dot=Vector2.Dot(selectionCorners[i],axis);
                minimum=Mathf.Min(minimum,dot);maximum=Mathf.Max(maximum,dot);
            }
            float center=Vector2.Dot(rect.center,axis);
            float radius=(Mathf.Abs(axis.x)*rect.width+Mathf.Abs(axis.y)*rect.height)*.5f;
            return maximum<center-radius||minimum>center+radius;
        }

        bool SoldierOverlaps(int soldier,Rect rect)
        {
            Vector3 feet=world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier]);
            Vector3 bottom=battleCamera.WorldToScreenPoint(feet),top=battleCamera.WorldToScreenPoint(feet+Vector3.up*1.8f);
            if(bottom.z<=battleCamera.nearClipPlane||top.z<=battleCamera.nearClipPlane)return false;
            return Rect.MinMaxRect(Mathf.Min(bottom.x,top.x)-3,Mathf.Min(bottom.y,top.y)-3,
                Mathf.Max(bottom.x,top.x)+3,Mathf.Max(bottom.y,top.y)+3).Overlaps(rect);
        }

        int PickFormation(Vector2 mouse,ArmySide side,bool includeRouting)
        {
            var rect=new Rect(mouse.x-8,mouse.y-8,16,16);
            int closest=-1;float best=float.MaxValue;
            foreach(var unit in world.Battalions)
            {
                if(unit.Side!=side||unit.AliveCount<=0||(!includeRouting&&unit.Routing)||!FormationOverlaps(unit,rect))continue;
                Vector3 center=battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(unit.Position));
                float score=((Vector2)center-mouse).sqrMagnitude;
                if(score<best){closest=unit.Id;best=score;}
            }
            if(closest>=0)return closest;
            // Dispersed/maneuvering soldiers can be outside their nominal formation.
            // Ground-distance rejection avoids projecting the entire army on hover.
            if(!GroundPoint(mouse,out var ground))return -1;
            float radius=ProjectileVisibility.Width(battleCamera,world.Terrain.SurfacePoint(ground),12,3,24)+7;
            best=float.MaxValue;
            foreach(var unit in world.Battalions)
            {
                if(unit.Side!=side||unit.AliveCount<=0||(!includeRouting&&unit.Routing))continue;
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                {
                    float score=(world.Soldiers.Positions[i]-ground).sqrMagnitude;
                    if(!world.Soldiers.Alive[i]||score>radius*radius||score>=best||!SoldierOverlaps(i,rect))continue;
                    closest=unit.Id;best=score;
                }
            }
            return closest;
        }

        public void SelectUnitsInRect(Rect rect,bool additive=false)
        {
            if(!additive)ClearSelection();
            var padded=Rect.MinMaxRect(rect.xMin-4,rect.yMin-4,rect.xMax+4,rect.yMax+4);
            foreach(var unit in world.Battalions)
            {
                if(unit.Side!=ArmySide.French||unit.Routing||unit.AliveCount<=0)continue;
                bool hit=FormationOverlaps(unit,padded);
                for(int i=unit.SoldierStart;!hit&&i<unit.SoldierStart+unit.SoldierCount;i++)
                    if(world.Soldiers.Alive[i])hit=SoldierOverlaps(i,padded);
                if(hit)selected[unit.Id]=true;
            }
            SelectGunsInRect(padded);
        }
    }
}
