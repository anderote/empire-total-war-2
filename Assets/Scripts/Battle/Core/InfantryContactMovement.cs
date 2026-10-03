using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    // Individual destinations are computed from one immutable position snapshot for
    // both scalar and Burst movement. Formation slots remain owned for later reforming.
    public sealed partial class InfantryContactMovement
    {
        readonly BattleWorld world;
        readonly Dictionary<long,int> cells = new Dictionary<long,int>(SpatialCellComparer.Instance);
        readonly int[] next;
        readonly float[] speeds;
        readonly Vector2[] separationRights,escapeDirections,escapeRights;
        public readonly Vector2[] Targets;
        public readonly float[] MeleeFacings, Facings;
        public readonly bool[] Loose, WasLoose;
        public readonly bool[] BattleLines;
        const float CellSize = 3f;
        static long Cell(int x,int z) => ((long)x << 32) | (uint)z;
        public InfantryContactMovement(BattleWorld value)
        {
            world=value; int n=value.Soldiers.Alive.Length;
            next=new int[n];Targets=new Vector2[n];MeleeFacings=new float[n];Facings=new float[n];
            Loose=new bool[value.Battalions.Length];WasLoose=new bool[Loose.Length];speeds=new float[Loose.Length];
            BattleLines=new bool[Loose.Length];
            separationRights=new Vector2[Loose.Length];escapeDirections=new Vector2[Loose.Length];escapeRights=new Vector2[Loose.Length];
            InitializePursuit(n);
        }
        public static bool Fleeing(BattalionState u) => u.Routing || u.Withdrawing;
        public static bool IsBraceRank(BattalionState u,int slot) => u.Formation==FormationKind.Square
            ?FormationMath.IsBayonetWall(u,slot):slot<FormationMath.EffectiveFiles(u)*2;
        void UpdateBracing()
        {
            foreach(var u in world.Battalions)
            {
                bool brace=false;
                if(u.Kind==UnitKind.Infantry && u.BayonetsFixed && !Fleeing(u) && !u.Moving && !u.Reforming
                    && u.Cohesion>.55f && !UnitRoster.Light(u.Role) && u.Role!=UnitRole.Swordsman
                    && (u.Formation==FormationKind.Line||u.Formation==FormationKind.Square))
                {
                    foreach(var v in world.Battalions)
                    {
                        if(v.Side==u.Side||v.Kind!=UnitKind.Cavalry||v.AliveCount<=0||Fleeing(v)
                            ||(v.ChargePhase!=ChargePhase.Approach&&v.ChargePhase!=ChargePhase.Impact))continue;
                        Vector2 incoming=u.Position-v.Position;
                        if(incoming.sqrMagnitude<.001f)continue;
                        float gap=MeleeSystem.EdgeGap(u,u.Position,v,v.Position);
                        if(gap>18f||Vector2.Dot(FormationMath.Forward(v.Facing),incoming.normalized)<.55f)continue;
                        if(u.Formation!=FormationKind.Square && Vector2.Dot(FormationMath.Forward(u.Facing),-incoming.normalized)<.65f)continue;
                        brace=true;break;
                    }
                }
                if(brace&&!u.Bracing)u.BraceStarted=world.Time;
                u.Bracing=brace;
            }
        }
        public void Prepare(float dt=.05f)
        {
            UpdateBracing();
            PreparePursuit();
            PrepareGathering();
            var s=world.Soldiers;
            for(int i=0;i<s.Alive.Length;i++)
                MeleeFacings[i]=s.MeleeTargets[i]>=0
                    ?FormationMath.FacingOf(s.Positions[s.MeleeTargets[i]]-s.Positions[i]):s.Facings[i];
            bool needed=false;
            foreach(var u in world.Battalions)
            {
                WasLoose[u.Id]=Loose[u.Id];
                BattleLines[u.Id]=HoldsBattleLine(u);
                Loose[u.Id]=Fleeing(u) || Pursuing[u.Id] || Gathering[u.Id] || (u.Kind==UnitKind.Infantry && u.InMelee && !u.Bracing && u.Formation!=FormationKind.Square);
                needed |= Loose[u.Id] && u.AliveCount>0;
                if(Loose[u.Id])
                {
                    speeds[u.Id]=Mathf.Max(world.MovementSpeed(u),world.Config.MarchSpeed*1.15f*u.GasMovementMultiplier);
                    if(BattleOptimizationSettings.ContactMathOptimizations)
                    {
                        separationRights[u.Id]=FormationMath.Right(u.Facing);
                        Vector2 escape=u.Destination-u.Position;
                        if(escape.sqrMagnitude<1)escape=new Vector2(0,u.Side==ArmySide.French?-1:1);
                        escape.Normalize();escapeDirections[u.Id]=escape;escapeRights[u.Id]=new Vector2(escape.y,-escape.x);
                    }
                }
            }
            if(!needed)return;
            cells.Clear();
            for(int i=0;i<s.Alive.Length;i++)
            {
                if(!s.Alive[i]||s.IsKnockedDown(i,world.Time))continue;
                long key=Cell(Mathf.FloorToInt(s.Positions[i].x/CellSize),Mathf.FloorToInt(s.Positions[i].y/CellSize));
                next[i]=cells.TryGetValue(key,out int head)?head:-1;cells[key]=i;
            }
            for(int i=0;i<s.Alive.Length;i++)
            {
                var u=world.Battalions[s.BattalionIds[i]];
                if(!Loose[u.Id]||!s.Alive[i]||s.IsKnockedDown(i,world.Time))continue;
                Vector2 p=s.Positions[i],separate=Vector2.zero;int enemy=-1;float best=36f;
                int cx=Mathf.FloorToInt(p.x/CellSize),cz=Mathf.FloorToInt(p.y/CellSize);
                bool fleeing=Fleeing(u);
                int radius=fleeing?1:2;
                Vector2 separationRight=separationRights[u.Id];
                float clearance=u.Kind==UnitKind.Cavalry||PursuitReserves[i]?1.5f:.72f;
                for(int x=cx-radius;x<=cx+radius;x++)for(int z=cz-radius;z<=cz+radius;z++)
                {
                    if(!cells.TryGetValue(Cell(x,z),out int j))continue;
                    for(;j>=0;j=next[j])
                    {
                        if(j==i)continue;
                        bool ally=world.Battalions[s.BattalionIds[j]].Side==u.Side;
                        Vector2 delta=p-s.Positions[j];float d=delta.sqrMagnitude;
                        if(ally && d<clearance*clearance)
                        {
                            // A soft core avoids a discontinuous shove when neighbours coincide.
                            float distance=Mathf.Sqrt(d);
                            var right=BattleOptimizationSettings.ContactMathOptimizations?separationRight:FormationMath.Right(u.Facing);
                            Vector2 direction=(delta+right*(i<j?-.005f:.005f))/(distance+.08f);
                            separate+=direction*(clearance-(BattleOptimizationSettings.ContactMathOptimizations?distance:Mathf.Sqrt(d)))*2f;
                        }
                        // Spare hunters must not bypass reservations through the local
                        // melee fallback. They can still defend against standing enemies.
                        if(!ally && !(PursuitReserves[i]&&Fleeing(world.Battalions[s.BattalionIds[j]]))
                            && (d<best-.0001f || (d<=best+.0001f&&(enemy<0||j<enemy)))){enemy=j;best=d;}
                    }
                }
                Vector2 travel;
                if(BattleLines[u.Id])
                {
                    // Keep files and supporting ranks intact. Only the exposed rank
                    // adjusts its reach; casualties promote reserves through slot compaction.
                    Vector2 anchor=u.Position+FormationMath.SlotOffset(u,s.FormationSlots[i]);
                    Vector2 forward=FormationMath.Forward(u.Facing),right=FormationMath.Right(u.Facing);
                    if(enemy>=0&&s.FormationSlots[i]<FormationMath.EffectiveFiles(u))
                    {
                        Vector2 delta=s.Positions[enemy]-anchor;
                        float reach=s.PikeSergeants[i]?2.5f:s.Captains[i]||u.Role==UnitRole.Swordsman?.95f:u.BayonetsFixed?1.55f:.95f;
                        anchor+=forward*Mathf.Clamp(Vector2.Dot(delta,forward)-reach,-.25f,.35f)
                            +right*Mathf.Clamp(Vector2.Dot(delta,right)*.25f,-.15f,.15f);
                    }
                    travel=anchor-p;
                    if(s.MeleeEnds[i]>world.Time&&s.MeleeTargets[i]>=0&&travel.sqrMagnitude<.5f*.5f)travel=Vector2.zero;
                }
                else if(Fleeing(u))
                {
                    Vector2 escape,right;
                    if(BattleOptimizationSettings.ContactMathOptimizations){escape=escapeDirections[u.Id];right=escapeRights[u.Id];}
                    else
                    {
                        escape=u.Destination-u.Position;
                        if(escape.sqrMagnitude<1)escape=new Vector2(0,u.Side==ArmySide.French?-1:1);
                        escape.Normalize();right=new Vector2(escape.y,-escape.x);
                    }
                    // Stable personal escape lanes and pace variation break ranks without jitter.
                    travel=(escape+right*Mathf.Lerp(-.32f,.32f,RifleFireTiming.Variation(i,811))).normalized*8f;
                }
                else if(Pursuing[u.Id]&&PursuitTargets[i]>=0)
                {
                    enemy=PursuitTargets[i];
                    Vector2 intercept=s.Positions[enemy]+Vector2.ClampMagnitude(s.Velocities[enemy]*(u.Kind==UnitKind.Cavalry?.6f:.35f),u.Kind==UnitKind.Cavalry?4f:2f);
                    Vector2 delta=intercept-p;
                    travel=delta.normalized*Mathf.Max(0,delta.magnitude-(u.Kind==UnitKind.Cavalry?.9f:.55f));
                }
                else if(enemy>=0)
                {
                    Vector2 delta=s.Positions[enemy]-p;float distance=Mathf.Sqrt(best);
                    float standOff=s.PikeSergeants[i]?Mathf.Lerp(2.3f,2.7f,RifleFireTiming.Variation(i,813))
                        :s.Captains[i]||u.Role==UnitRole.Swordsman?Mathf.Lerp(.85f,1.1f,RifleFireTiming.Variation(i,813))
                        :Mathf.Lerp(1.15f,1.55f,RifleFireTiming.Variation(i,813));
                    travel=delta.normalized*(distance-standOff);
                    if(distance>standOff+.2f)
                        travel+=new Vector2(delta.y,-delta.x).normalized*Mathf.Lerp(-.4f,.4f,RifleFireTiming.Variation(i,817));
                    // Keep the planted portion of a timed attack stable.
                    if(MeleeSystem.IsStrike(s.MeleeActions[i])&&s.MeleeEnds[i]>world.Time&&distance<2.05f)travel=Vector2.zero;
                }
                else if(PursuitReserves[i])travel=GatheringTravel(i,u);
                else
                {
                    BattalionState closest=null;float distance=float.MaxValue;
                    foreach(var v in world.Battalions)
                        if(v.Side!=u.Side&&v.AliveCount>0&&(v.Position-p).sqrMagnitude<distance)
                        {closest=v;distance=(v.Position-p).sqrMagnitude;}
                    travel=closest!=null?(closest.Position-p).normalized*4f:Vector2.zero;
                }
                float speed=speeds[u.Id];
                if(BattleLines[u.Id])speed=Mathf.Min(speed,1.05f*u.GasMovementMultiplier);
                if(PursuitReserves[i]&&enemy<0)speed=Mathf.Min(speed,GatheringSpeed(u));
                Vector2 target=p+Vector2.ClampMagnitude(travel+separate,1f)*dt*speed*Pace(i,Fleeing(u));
                if(!Fleeing(u))
                {
                    target.x=Mathf.Clamp(target.x,-world.Config.FieldHalfSize+.5f,world.Config.FieldHalfSize-.5f);
                    target.y=Mathf.Clamp(target.y,-world.Config.FieldHalfSize+.5f,world.Config.FieldHalfSize-.5f);
                }
                Targets[i]=world.Terrain.IsBlocked(target,.35f)?p:target;
                Vector2 motion=Targets[i]-p;
                float facing=!Fleeing(u)&&s.MeleeEnds[i]>world.Time&&s.MeleeTargets[i]>=0?MeleeFacings[i]
                    :motion.sqrMagnitude>.00001f?FormationMath.FacingOf(motion)
                    :BattleLines[u.Id]?u.Facing
                    :enemy>=0?FormationMath.FacingOf(s.Positions[enemy]-p)
                    :PursuitReserves[i]?gatheringFacings[u.Id]:s.Facings[i];
                Facings[i]=FormationMath.TurnTowards(s.Facings[i],facing,dt*(u.Kind==UnitKind.Cavalry?2.8f:RifleFireTiming.SoldierTurnRate));
            }
        }
        bool HoldsBattleLine(BattalionState u)
        {
            if(u.Kind!=UnitKind.Infantry||u.Formation!=FormationKind.Line||!u.InMelee
                ||Fleeing(u)||Pursuing[u.Id]||Gathering[u.Id]||u.Cohesion<.35f)return false;
            foreach(var enemy in world.Battalions)
                if(enemy.Side!=u.Side&&enemy.Kind==UnitKind.Infantry&&enemy.AliveCount>0&&!Fleeing(enemy)
                    &&MeleeSystem.EdgeGap(u,u.Position,enemy,enemy.Position)<4f
                    &&Vector2.Dot(FormationMath.Forward(u.Facing),(enemy.Position-u.Position).normalized)>.45f)return true;
            return false;
        }
        public static float Pace(int soldier,bool fleeing) => fleeing
            ?Mathf.Lerp(.8f,1.2f,RifleFireTiming.Variation(soldier,821))
            :Mathf.Lerp(.94f,1.06f,RifleFireTiming.Variation(soldier,79));
    }
}
