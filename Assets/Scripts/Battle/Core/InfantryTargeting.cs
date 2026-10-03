using UnityEngine;

namespace EmpireBattle
{
    public static class InfantryTargeting
    {
        internal struct Candidate { public int Soldier; public float Score; }
        public static Vector3 AimPoint(BattleWorld world,int soldier)
        {
            var unit=world.Battalions[world.Soldiers.BattalionIds[soldier]];
            float height=world.Soldiers.IsKnockedDown(soldier,world.Time)?.22f:unit.Kind==UnitKind.Cavalry?1.5f:
                RifleFireTiming.IsKneeling(world,unit,soldier,world.Time)?.7f:1f;
            return world.SoldierPosition(soldier,world.Time)+Vector3.up*height;
        }

        // Prefer a living man in this shooter's lane, then the nearer rank. Selection is
        // deterministic and shared by aim animation, muzzle effects and hit/miss trajectories.
        public static int Select(BattleWorld world,BattalionState attacker,int shooter,BattalionState target)
        {
            if(target==null||target.Side==attacker.Side||target.AliveCount<=0)return -1;
            Vector2 start=world.Soldiers.Positions[shooter];
            Vector2 facing=FormationMath.Forward(FormationMath.SlotFacing(attacker,world.Soldiers.FormationSlots[shooter]));
            Vector2 lane=(target.Position-attacker.Position).normalized;if(lane.sqrMagnitude<.01f)lane=facing;
            float range=world.AimedRange(attacker);int count=0;
            var candidates=world.TargetingCandidates;
            var origin=world.Terrain.SurfacePoint(start,1.45f);
            for(int i=target.SoldierStart;i<target.SoldierStart+target.SoldierCount;i++)
            {
                if(!world.Soldiers.Alive[i])continue;
                Vector2 position=world.Soldiers.Positions[i];
                if(world.Soldiers.IsKnockedDown(i,world.Time))
                {var airborne=world.SoldierPosition(i,world.Time);position=new Vector2(airborne.x,airborne.z);}
                Vector2 delta=position-start;
                float distance=delta.magnitude;
                if(distance<.1f||distance>range||Vector2.Dot(facing,delta/distance)<(attacker.Formation==FormationKind.Square?.707f:WeaponBallistics.MinimumFacingDot))continue;
                float lateral=Mathf.Abs(delta.x*lane.y-delta.y*lane.x);
                float candidate=lateral*1000f+distance;if(candidate>=float.PositiveInfinity)continue;
                candidates[count++]=new Candidate {Soldier=i,Score=candidate};
            }
            // The old scan traced every improving candidate in soldier-ID order.
            // Rank the same candidates cheaply, then trace only until the best
            // unoccluded one is found. ID breaks equal scores exactly as before.
            for(int i=count/2-1;i>=0;i--)SiftDown(candidates,i,count);
            while(count>0)
            {
                int soldier=candidates[0].Soldier;
                Vector3 point=AimPoint(world,soldier);
                if(!WeaponBallistics.TrySolve(origin,point,out var velocity,out float duration,range)
                    ||WeaponBallistics.TraceGround(world.Terrain,origin,velocity,duration,out _))
                {
                    candidates[0]=candidates[--count];SiftDown(candidates,0,count);continue;
                }
                return soldier;
            }
            return -1;
        }
        static bool Before(Candidate a,Candidate b)=>a.Score<b.Score||(a.Score==b.Score&&a.Soldier<b.Soldier);
        static void SiftDown(Candidate[] candidates,int root,int count)
        {
            if(root>=count)return;
            var value=candidates[root];
            for(int child=root*2+1;child<count;child=root*2+1)
            {
                if(child+1<count&&Before(candidates[child+1],candidates[child]))child++;
                if(!Before(candidates[child],value))break;
                candidates[root]=candidates[child];root=child;
            }
            candidates[root]=value;
        }
    }
}
