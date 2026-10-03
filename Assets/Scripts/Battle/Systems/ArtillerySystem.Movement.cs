using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class ArtillerySystem
    {
        const float GunClearance=2.5f, PrepareMoveSeconds=2f, SetUpSeconds=3f;

        public bool OrderMove(int gunId,Vector2 destination,float facing)
        {
            if(gunId<0||gunId>=Guns.Length||!Finite(new Vector3(destination.x,facing,destination.y)))return false;
            var gun=Guns[gunId];
            if(!gun.Operational||!GunPositionAllowed(destination))return false;
            if(world.DeploymentActive)
            {
                var area=world.DeploymentArea(gun.Side);
                if(gun.Side!=ArmySide.French||destination.x<area.min.x+GunClearance||destination.x>area.max.x-GunClearance||
                    destination.y<area.min.z+GunClearance||destination.y>area.max.z-GunClearance)return false;
                gun.Position=gun.Destination=destination;gun.Facing=gun.DestinationFacing=facing;
                gun.Moving=false;gun.SetupRemaining=gun.MovePreparation=0;gun.MoveRoute.Clear();gun.MovementBlocked=false;
            }
            else
            {
                var route=new List<Vector2>();
                if(!PlanGunRoute(gun.Position,destination,route))return false;
                bool alreadyMoving=gun.Moving;
                gun.MoveRoute.Clear();gun.MoveRoute.AddRange(route);gun.MoveWaypoint=0;
                gun.Destination=destination;gun.DestinationFacing=facing;gun.Moving=true;gun.MovementBlocked=false;
                if(!alreadyMoving)gun.MovePreparation=PrepareMoveSeconds;
                gun.SetupRemaining=0;
            }
            gun.TargetBattalion=gun.TargetBattery=-1;gun.HasGroundTarget=false;
            // Keep hold/fire preference and the interrupted reload phase. Moving cannot reload for free.
            return true;
        }

        static void StopGunMovement(ArtilleryGun gun)
        {
            if(!gun.Moving)return;
            gun.Moving=false;gun.MovePreparation=0;gun.MoveRoute.Clear();
            gun.Destination=gun.Position;gun.DestinationFacing=gun.Facing;
            gun.SetupRemaining=SetUpSeconds;
        }

        bool GunPositionAllowed(Vector2 point)
        {
            float edge=world.Config.FieldHalfSize-GunClearance;
            return Mathf.Abs(point.x)<=edge&&Mathf.Abs(point.y)<=edge&&!world.Terrain.IsBlocked(point,GunClearance);
        }

        bool GunSegmentClear(Vector2 from,Vector2 to)
        {
            int samples=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(from,to)/2f));
            for(int i=1;i<=samples;i++)if(!GunPositionAllowed(Vector2.Lerp(from,to,i/(float)samples)))return false;
            return true;
        }

        bool PlanGunRoute(Vector2 from,Vector2 to,List<Vector2> route)
        {
            if(GunSegmentClear(from,to)){route.Add(to);return true;}
            bool Via(Vector2 first,Vector2 second)
            {
                if(!GunSegmentClear(from,first)||!GunSegmentClear(first,second)||!GunSegmentClear(second,to))return false;
                route.Add(first);route.Add(second);route.Add(to);return true;
            }
            if(world.Terrain.GatePath(from,to,GunClearance,out var entry,out var exit)&&Via(entry,exit))return true;
            var direction=(to-from).normalized;var tangent=new Vector2(-direction.y,direction.x);
            // Bounded local detours around farms and other obstacles. Reject routes we cannot clear.
            for(float distance=8;distance<=256;distance*=2)
                foreach(float side in new[]{1f,-1f})
                    if(Via(from+tangent*distance*side,to+tangent*distance*side))return true;
            return false;
        }

        void StepGunMovement(ArtilleryGun gun,float dt)
        {
            if(!gun.Operational){gun.Moving=false;gun.SetupRemaining=0;return;}
            // Lime interrupts serving the muzzle, but surviving crew can haul a gun out of the cloud.
            if(!gun.Moving){gun.SetupRemaining=Mathf.Max(0,gun.SetupRemaining-dt);return;}
            if(gun.MovePreparation>0)
            {
                float used=Mathf.Min(dt,gun.MovePreparation);gun.MovePreparation-=used;dt-=used;
                if(dt<=0)return;
            }
            float speed=1.4f*Mathf.Lerp(.4f,1,gun.LivingCrew/4f)*Mathf.Clamp(1+gun.Kit.speed,.5f,1.5f)*
                Mathf.Lerp(1,.65f,gun.Fatigue/100f)*world.Terrain.SpeedMultiplier(gun.Position)*gun.GasMovementMultiplier;
            float remaining=dt*speed;
            while(gun.MoveWaypoint<gun.MoveRoute.Count&&remaining>0)
            {
                Vector2 target=gun.MoveRoute[gun.MoveWaypoint],delta=target-gun.Position;
                float travel=Mathf.Min(remaining,delta.magnitude);
                Vector2 next=Vector2.MoveTowards(gun.Position,target,travel);
                if(!GunSegmentClear(gun.Position,next)){StopGunMovement(gun);gun.MovementBlocked=true;return;}
                if(delta.sqrMagnitude>.0001f)gun.Facing=FormationMath.TurnTowards(gun.Facing,FormationMath.FacingOf(delta),dt*1.2f);
                gun.Position=next;remaining-=travel;
                if(delta.magnitude<=travel+.001f)gun.MoveWaypoint++;else break;
            }
            if(gun.MoveWaypoint<gun.MoveRoute.Count)return;
            gun.Facing=FormationMath.TurnTowards(gun.Facing,gun.DestinationFacing,dt*1.2f);
            if(Mathf.Abs(Mathf.DeltaAngle(gun.Facing*Mathf.Rad2Deg,gun.DestinationFacing*Mathf.Rad2Deg))>.5f)return;
            gun.Facing=gun.DestinationFacing;gun.Moving=false;gun.MoveRoute.Clear();gun.SetupRemaining=SetUpSeconds;
        }
    }
}
