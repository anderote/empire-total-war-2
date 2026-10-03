using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleView
    {
        public bool HasCommandSelection
        {
            get
            {
                if(SelectedGunCount>0)return true;
                foreach(var unit in world.Battalions)
                    if(selected[unit.Id]&&unit.AliveCount>0&&!unit.Routing)return true;
                return false;
            }
        }
        public bool FireCameraRoundshotAtScreenPoint(Vector2 screen)
        {
            if(!EnableInput||HasCommandSelection||commandArtillery==null)return false;
            if(PointerOverUI(new Vector2(screen.x,Screen.height-screen.y))||!GroundPoint(screen,out var point))return false;
            Vector3 target=world.Terrain.SurfacePoint(point,.08f);
            Vector3 direction=(target-battleCamera.transform.position).normalized;
            Vector3 origin=battleCamera.transform.position+direction*Mathf.Max(.75f,battleCamera.nearClipPlane+.4f);
            if(!commandArtillery.FireCameraRoundshot(origin,target))return false;
            GetComponent<BattleAudio>()?.CameraCannonFire(origin);
            status="Camera cannon fired.";
            return true;
        }
        public bool SelectedPursuitEnabled
        {
            get
            {
                bool any=false;
                foreach(var unit in world.Battalions)
                {
                    if(!selected[unit.Id]||unit.AliveCount<=0||unit.Routing)continue;
                    any=true;if(!unit.PursueTargets)return false;
                }
                return any;
            }
        }
        public void OrderTogglePursuit()
        {
            bool enabled=!SelectedPursuitEnabled;
            ForSelected(unit=>world.SetPursuit(unit.Id,enabled));
            status=enabled?"Pursuit enabled.":"Pursuit disabled.";
        }
        void RenderInfantryOrders()
        {
            foreach(var unit in world.Battalions)
            {
                if(unit.Side!=ArmySide.French||unit.AliveCount<=0||unit.Routing)continue;
                Vector2 start=unit.Position;
                bool first=true;
                foreach(var order in world.PreviewOrders(unit))
                {
                    if(first){OutlineOrderFormation(unit,unit.Position,unit.Facing,order.IsAttack?attackMarker:marker);first=false;}
                    OrderArrow(start,order.Destination,order.IsAttack?attackMarker:marker);
                    if(order.IsAttack)
                    {
                        var target=world.Battalions[order.TargetId];
                        OutlineOrderFormation(target,target.Position,target.Facing,attackMarker);
                    }
                    else RenderSlots(unit,order.Destination,order.Facing,order.Files);
                    start=order.Destination;
                }
            }
        }
        void OutlineOrderFormation(BattalionState unit,Vector2 center,float facing,Material color)
        {
            Vector2 half=FormationMath.HalfExtents(unit)+Vector2.one*1.8f;
            Vector2 right=FormationMath.Right(facing)*half.x,forward=FormationMath.Forward(facing)*half.y;
            GroundOrderLine(center-right-forward,center+right-forward,color);
            GroundOrderLine(center+right-forward,center+right+forward,color);
            GroundOrderLine(center+right+forward,center-right+forward,color);
            GroundOrderLine(center-right+forward,center-right-forward,color);
        }
        void OrderArrow(Vector2 start,Vector2 end,Material color)
        {
            float length=Vector2.Distance(start,end);if(length<.1f)return;
            int segments=Mathf.Clamp(Mathf.CeilToInt(length/12f),1,100);
            for(int i=0;i<segments;i++)
                GroundOrderLine(Vector2.Lerp(start,end,i/(float)segments),Vector2.Lerp(start,end,(i+.7f)/segments),color);
            Vector2 direction=(end-start).normalized,right=new Vector2(direction.y,-direction.x);
            float size=Mathf.Clamp(length*.05f,3f,12f);
            GroundOrderLine(end,end-direction*size+right*size*.5f,color);
            GroundOrderLine(end,end-direction*size-right*size*.5f,color);
        }
    }
}
