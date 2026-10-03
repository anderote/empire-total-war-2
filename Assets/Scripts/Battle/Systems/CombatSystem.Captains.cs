using UnityEngine;
namespace EmpireBattle
{
    public sealed partial class CombatSystem
    {
        public const float RevolverRange=18f, RevolverCycle=1.4f, RevolverReload=12f;
        readonly int[] captainFireTargets;
        void StepCaptains()
        {
            var s=world.Soldiers;
            foreach(var u in world.Battalions)
            {
                if(u.Kind!=UnitKind.Infantry||u.Routing||(!u.FireAtWill&&u.AttackMode!=InfantryAttackMode.BayonetCharge)||u.Withdrawing||u.AliveCount<=0)continue;
                bool nearby=false;
                foreach(var enemy in world.Battalions)if(enemy.Side!=u.Side&&enemy.AliveCount>0&&MeleeSystem.EdgeGap(u,u.Position,enemy,enemy.Position)<RevolverRange){nearby=true;break;}
                if(!nearby)continue;
                for(int soldier=u.SoldierStart;soldier<u.SoldierStart+u.SoldierCount;soldier++)
                {
                    if(!s.Captains[soldier]||!s.Alive[soldier]||s.IsKnockedDown(soldier,world.Time)||s.SidearmReadyTimes[soldier]>world.Time)continue;
                    if(s.MovementSpeeds[soldier]>.08f||MeleeSystem.IsStrike(s.MeleeActions[soldier])&&s.MeleeEnds[soldier]>world.Time)continue;
                    if(s.SidearmRounds[soldier]==0)
                    {
                        if(s.SidearmReserves[soldier]<=0)continue;
                        int load=Mathf.Min(6,s.SidearmReserves[soldier]);s.SidearmRounds[soldier]=load;s.SidearmReserves[soldier]-=load;
                    }
                    int target=RevolverTarget(soldier);if(target<0)continue;
                    Vector2 delta=s.Positions[target]-s.Positions[soldier];
                    if(delta.sqrMagnitude<1.6f*1.6f||Vector2.Dot(FormationMath.Forward(s.Facings[soldier]),delta.normalized)<.75f||(!ExplicitFireOrder(u)&&!ClearRevolverLane(soldier,target)))continue;
                    s.Facings[soldier]=FormationMath.FacingOf(delta);
                    Vector3 origin=world.Terrain.SurfacePoint(s.Positions[soldier])+Quaternion.Euler(0,s.Facings[soldier]*Mathf.Rad2Deg,0)*new Vector3(-.29f,1.3f,.75f);
                    Vector3 end=world.Terrain.SurfacePoint(s.Positions[target],1.2f);
                    if(!WeaponBallistics.TrySolve(origin,end,out var velocity,out float duration,RevolverRange)||WeaponBallistics.TraceGround(world.Terrain,origin,velocity,duration,out _))continue;
                    s.SidearmRounds[soldier]--;s.SidearmReadyTimes[soldier]=world.Time+(s.SidearmRounds[soldier]==0&&s.SidearmReserves[soldier]>0?RevolverReload:RevolverCycle);
                    s.FireTimes[soldier]=s.FireVolleys[soldier]=world.Time;s.FireOrigins[soldier]=s.FireLockOrigins[soldier]=origin;
                    s.FireDirections[soldier]=new Vector3(delta.normalized.x,0,delta.normalized.y);
                    u.LastVolleySize=u.LastVolleyTime==world.Time?u.LastVolleySize+1:1;u.LastVolleyTime=world.Time;
                    u.ShotsFired++;var defender=world.Battalions[s.BattalionIds[target]];defender.LastIncomingFireTime=world.Time;threatened[defender.Id]=true;
                    float accuracy=Mathf.Lerp(.9f,.42f,delta.magnitude/RevolverRange)*Mathf.Lerp(.65f,1,u.Cohesion);
                    if(random.NextDouble()<accuracy)
                    {
                        int before=pendingCount;QueueHit(defender.Id,u.Id,45,true,soldier);
                        if(pendingCount>before)captainFireTargets[soldier]=target;
                    }
                    else LaunchBullet(origin,end+Vector3.right*((float)random.NextDouble()<.5f?-1f:1f),45,u.Id,soldier,world.Time,true);
                }
            }
        }
        int RevolverTarget(int soldier)
        {
            var s=world.Soldiers;var u=world.Battalions[s.BattalionIds[soldier]];Vector2 start=s.Positions[soldier];
            int target=-1;float nearest=RevolverRange*RevolverRange;
            foreach(var enemy in world.Battalions)
            {
                if(enemy.Side==u.Side||enemy.AliveCount<=0)continue;
                float radius=RevolverRange+FormationMath.HalfExtents(enemy).magnitude+8;
                if((enemy.Position-start).sqrMagnitude>radius*radius)continue;
                for(int i=enemy.SoldierStart;i<enemy.SoldierStart+enemy.SoldierCount;i++)
                {
                    if(!s.Alive[i]||s.IsKnockedDown(i,world.Time))continue;
                    Vector2 delta=s.Positions[i]-start;float distance=delta.sqrMagnitude;
                    if(distance>=nearest||Vector2.Dot(FormationMath.Forward(s.Facings[soldier]),delta.normalized)<.75f)continue;
                    nearest=distance;target=i;
                }
            }
            return target;
        }
        bool ClearRevolverLane(int soldier,int target)
        {
            var s=world.Soldiers;var u=world.Battalions[s.BattalionIds[soldier]];Vector2 start=s.Positions[soldier],path=s.Positions[target]-start;
            float length=path.magnitude;Vector2 direction=path/Mathf.Max(.001f,length);
            foreach(var ally in world.Battalions)
            {
                if(ally.Side!=u.Side||ally.AliveCount<=0)continue;
                float radius=length+FormationMath.HalfExtents(ally).magnitude+8;
                if((ally.Position-start).sqrMagnitude>radius*radius)continue;
                for(int i=ally.SoldierStart;i<ally.SoldierStart+ally.SoldierCount;i++)
                {
                    if(i==soldier||!s.Alive[i]||s.IsKnockedDown(i,world.Time))continue;
                    Vector2 delta=s.Positions[i]-start;float along=Vector2.Dot(delta,direction);
                    if(along>.25f&&along<length-.2f&&Mathf.Abs(delta.x*direction.y-delta.y*direction.x)<.32f)return false;
                }
            }
            return true;
        }
    }
}
