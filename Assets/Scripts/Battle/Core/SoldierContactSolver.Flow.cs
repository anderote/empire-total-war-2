using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class SoldierContactSolver
    {
        // A soft crowd field, not an incompressible fluid or a rigid-body solver.
        // Four weighted samples make density and passing velocity continuous
        // across the 4 m grid. Own ranks are subtracted before steering: ordinary
        // formation spacing and melee reach remain owned by their movement systems.
        public const float FlowCellSize=4f;
        struct FlowCell
        {
            public float Count, FrenchCount;
            public Vector2 Position, Velocity, FrenchVelocity;
        }
        struct FlowGroup
        {
            public int Cell, Unit;
            public float Count;
            public Vector2 Position, Velocity, Steering;
        }
        readonly Dictionary<long,int> flowCells=new Dictionary<long,int>(SpatialCellComparer.Instance);
        readonly Dictionary<long,int> flowGroups=new Dictionary<long,int>(SpatialCellComparer.Instance);
        FlowCell[] flowCellData;
        FlowGroup[] flowGroupData;
        int[] flowSamples;
        Vector2[] flowFractions, flowVelocities;
        int flowCellCount, flowGroupCount;

        void ResolveFlow(float dt)
        {
            var s=world.Soldiers;
            LastGridBuilds=LastContactRegiments=0;LastPassOverlap=0;
            ResetUnitBounds();Array.Clear(contactUnits,0,contactUnits.Length);
            for(int i=0;i<before.Length;i++)if(active[i])
            {
                int unit=s.BattalionIds[i];var p=s.Positions[i];proposed[i]=p;
                unitMin[unit]=Vector2.Min(unitMin[unit],Vector2.Min(before[i],p));
                unitMax[unit]=Vector2.Max(unitMax[unit],Vector2.Max(before[i],p));
            }
            WakeNearbyUnits();
            foreach(var u in world.Battalions)if(contactUnits[u.Id]&&u.AliveCount>0)LastContactRegiments++;
            if(LastContactRegiments==0)
            {if(flowVelocities!=null)Array.Clear(flowVelocities,0,flowVelocities.Length);return;}
            if(flowCellData==null)
            {
                int capacity=checked(before.Length*4);
                flowCellData=new FlowCell[capacity];flowGroupData=new FlowGroup[capacity];
                flowSamples=new int[capacity];flowFractions=new Vector2[before.Length];flowVelocities=new Vector2[before.Length];
            }
            using(BattleMetrics.Measure(BattleMetrics.Stage.ContactIndex))
            {
                flowCells.Clear();flowGroups.Clear();flowCellCount=flowGroupCount=0;LastGridBuilds=1;
                for(int i=0;i<before.Length;i++)
                {
                    if(!active[i]||!contactUnits[s.BattalionIds[i]]){flowVelocities[i]=Vector2.zero;continue;}
                    int unit=s.BattalionIds[i];Vector2 p=s.Positions[i],v=(p-before[i])/dt;
                    float gx=p.x/FlowCellSize,gy=p.y/FlowCellSize;
                    int x=Mathf.FloorToInt(gx),y=Mathf.FloorToInt(gy);
                    Vector2 fraction=new Vector2(gx-x,gy-y);flowFractions[i]=fraction;
                    for(int corner=0;corner<4;corner++)
                    {
                        float weight=FlowWeight(fraction,corner);
                        int group=FlowSample(x+(corner&1),y+(corner>>1),unit);
                        flowSamples[i*4+corner]=group;
                        ref var g=ref flowGroupData[group];ref var c=ref flowCellData[g.Cell];
                        g.Count+=weight;g.Position+=p*weight;g.Velocity+=v*weight;
                        c.Count+=weight;c.Position+=p*weight;c.Velocity+=v*weight;
                        if(world.Battalions[unit].Side==ArmySide.French)
                        {c.FrenchCount+=weight;c.FrenchVelocity+=v*weight;}
                    }
                }
            }
            using(BattleMetrics.Measure(BattleMetrics.Stage.ContactPairs))
            {
                // Work is bounded by occupied regiment/cell samples, independent
                // of the number of body pairs packed into an overlapping cell.
                for(int group=0;group<flowGroupCount;group++)
                {
                    ref var g=ref flowGroupData[group];ref var c=ref flowCellData[g.Cell];
                    float others=c.Count-g.Count;
                    if(g.Count<.0001f||others<.001f)continue;
                    var unit=world.Battalions[g.Unit];
                    Vector2 delta=g.Position/g.Count-(c.Position-g.Position)/others;
                    float pressure=Mathf.Clamp01(others/8f);
                    // Weak pressure leaves opposing lines inside weapon reach.
                    Vector2 steering=delta/(delta.magnitude+.8f)*(.3f*pressure);
                    float allies=(unit.Side==ArmySide.French?c.FrenchCount:c.Count-c.FrenchCount)-g.Count;
                    if(allies>.001f)
                    {
                        Vector2 allyVelocity=(unit.Side==ArmySide.French?c.FrenchVelocity:c.Velocity-c.FrenchVelocity)-g.Velocity;
                        Vector2 relative=g.Velocity/g.Count-allyVelocity/allies;
                        float speed=relative.magnitude;
                        if(speed>.05f)
                        {
                            float crossing=Mathf.Clamp01(allies/8f)*Mathf.Clamp01(speed/1.5f);
                            // Each stream keeps right relative to the other. The
                            // opposite stream receives the opposite lateral bias.
                            steering+=(new Vector2(relative.y,-relative.x)/speed*.65f-relative*.08f)*crossing;
                        }
                    }
                    g.Steering=Vector2.ClampMagnitude(steering,unit.Kind==UnitKind.Cavalry?.95f:.7f)*(unit.Bracing?.1f:1f);
                }
            }
            float blend=1f-Mathf.Exp(-dt*12f);
            Array.Clear(proposedError,0,proposedError.Length);
            foreach(var u in world.Battalions)expandedGaits[u.Id]=FormationMath.ExpandedInfantryGaits(u);
            for(int i=0;i<before.Length;i++)if(active[i]&&contactUnits[s.BattalionIds[i]])
            {
                int id=s.BattalionIds[i];var u=world.Battalions[id];Vector2 desired=Vector2.zero;
                for(int corner=0;corner<4;corner++)desired+=flowGroupData[flowSamples[i*4+corner]].Steering*FlowWeight(flowFractions[i],corner);
                Vector2 velocity=Vector2.Lerp(flowVelocities[i],desired,blend);flowVelocities[i]=velocity;
                if(velocity.sqrMagnitude<.000001f)continue;
                radius[i]=BodyRadius(u.Kind);
                Vector2 push=Allowed(i,velocity*dt);
                if(push.sqrMagnitude<.00000001f)continue;
                s.Positions[i]+=push;touched[i]=true;unitTouched[id]=true;centrePush[id]+=push;
                proposedError[id]=Mathf.Max(proposedError[id],push.magnitude);
                velocity=(s.Positions[i]-before[i])/dt;float speed=velocity.magnitude;
                s.Velocities[i]=velocity;s.MovementSpeeds[i]=speed;
                bool running=u.Running||InfantryContactMovement.Fleeing(u);
                float stride=u.Kind==UnitKind.Cavalry?(speed>5?4f:speed>2.5f?2.6f:1.9f)
                    :FormationMath.SoldierStride(i,running,speed,u.AttackMode==InfantryAttackMode.BayonetCharge,s.Captains[i]||s.PikeSergeants[i],expandedGaits[id]);
                s.GaitPhases[i]=Mathf.Repeat(gait[i]+speed*dt/stride,1f);
                if(speed>.08f&&s.MeleeEnds[i]<=world.Time)
                    s.Facings[i]=FormationMath.TurnTowards(facing[i],FormationMath.FacingOf(velocity),dt*(u.Kind==UnitKind.Cavalry?2.8f:RifleFireTiming.SoldierTurnRate));
            }
            foreach(var u in world.Battalions)if(unitTouched[u.Id])
            {
                Vector2 push=centrePush[u.Id]/Mathf.Max(1,u.AliveCount);
                u.Position+=push;if(!u.Moving)u.Destination+=push;
                u.ActualSpeed=Vector2.Distance(unitBefore[u.Id],u.Position)/dt;
                // Conservative update of the movement job's existing error avoids
                // recalculating every formation slot just for a small correction.
                if(!world.ContactMovement.Loose[u.Id])u.FormationError+=proposedError[u.Id]+push.magnitude;
                u.Reforming|=u.FormationError>.22f;
            }
        }
        int FlowSample(int x,int y,int unit)
        {
            long key=Cell(x,y);
            if(!flowCells.TryGetValue(key,out int cell))
            {cell=flowCellCount++;flowCells.Add(key,cell);flowCellData[cell]=default;}
            key=Cell(cell,unit);
            if(!flowGroups.TryGetValue(key,out int group))
            {group=flowGroupCount++;flowGroups.Add(key,group);flowGroupData[group]=new FlowGroup{Cell=cell,Unit=unit};}
            return group;
        }
        static float FlowWeight(Vector2 fraction,int corner)=>(corner&1)==0
            ?(1f-fraction.x)*((corner&2)==0?1f-fraction.y:fraction.y)
            :fraction.x*((corner&2)==0?1f-fraction.y:fraction.y);
    }
}
