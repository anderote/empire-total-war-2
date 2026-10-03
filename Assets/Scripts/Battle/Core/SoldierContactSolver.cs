using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    // One ordered, allocation-free body solver for scalar and Burst movement.
    // Regiment contact wakes body physics around melee and crossing formations.
    // Isolated regiments dress their ranks using formation movement alone.
    public sealed partial class SoldierContactSolver
    {
        const float CellSize=1.5f, Tolerance=.0001f;
        readonly BattleWorld world;
        readonly Dictionary<long,int> cells=new Dictionary<long,int>(SpatialCellComparer.Instance);
        readonly int[] cellHeads, neighborHeads;
        readonly int[] soldierCells;
        readonly int[] cellX,cellY;
        readonly bool[] dirtyCells, neighborsReady;
        readonly bool[] queuedCells;
        readonly uint[] candidates;
        bool queueContacts;
        bool[] movedLastPass, movedThisPass;
        readonly int[] emptyCellNeighbors=new int[9];
        int cellCount;
        readonly int[] next;
        readonly int[] friendlyBlockers;
        public int FriendlyBlocker(int unit)=>friendlyBlockers[unit];
        public int LastGridBuilds {get;private set;}
        public int LastContactRegiments {get;private set;}
        public float LastPassOverlap {get;private set;}
#if UNITY_EDITOR
        // Reference traversal for deterministic equivalence checks, absent in players.
        public bool ExhaustiveVerification;
#endif
        readonly Vector2[] before, proposed, centrePush, unitBefore;
        readonly float[] gait, facing, radius, mobility;
        readonly bool[] active, touched, unitTouched, nudged, contactBodies;
        readonly bool[] externalContact;
        readonly Vector2[] unitForward;
        readonly float[] correctionSpeed;
        readonly bool[] expandedGaits;
        readonly bool[] contactUnits;
        readonly Vector2[] unitBeforeForward, unitMin, unitMax;
        readonly float[] unitBeforeFacing, slotErrorSquared, proposedError;
        static long Cell(int x,int y)=>((long)x<<32)|(uint)y;
        public static float BodyRadius(UnitKind kind)=>kind==UnitKind.Cavalry?.65f:.29f;
        public Vector2 ContactDisplacement(int soldier)=>touched[soldier]?world.Soldiers.Positions[soldier]-proposed[soldier]:Vector2.zero;

        public SoldierContactSolver(BattleWorld value)
        {
            world=value;int n=value.Soldiers.Alive.Length;
            next=new int[n];before=new Vector2[n];proposed=new Vector2[n];
            cellHeads=new int[n];neighborHeads=new int[checked(n*9)];
            soldierCells=new int[n];dirtyCells=new bool[n];neighborsReady=new bool[n];movedLastPass=new bool[n];movedThisPass=new bool[n];
            cellX=new int[n];cellY=new int[n];queuedCells=new bool[n];candidates=new uint[(n+31)/32];
            gait=new float[n];facing=new float[n];radius=new float[n];mobility=new float[n];
            active=new bool[n];touched=new bool[n];nudged=new bool[n];contactBodies=new bool[n];
            centrePush=new Vector2[value.Battalions.Length];unitBefore=new Vector2[centrePush.Length];unitTouched=new bool[centrePush.Length];
            friendlyBlockers=new int[centrePush.Length];
            externalContact=new bool[centrePush.Length];
            unitForward=new Vector2[centrePush.Length];correctionSpeed=new float[centrePush.Length];expandedGaits=new bool[centrePush.Length];
            contactUnits=new bool[centrePush.Length];unitBeforeForward=new Vector2[centrePush.Length];
            unitMin=new Vector2[centrePush.Length];unitMax=new Vector2[centrePush.Length];
            unitBeforeFacing=new float[centrePush.Length];slotErrorSquared=new float[centrePush.Length];proposedError=new float[centrePush.Length];
        }
        public void Capture()
        {
            var s=world.Soldiers;
            Array.Clear(touched,0,touched.Length);Array.Clear(unitTouched,0,unitTouched.Length);
            Array.Clear(nudged,0,nudged.Length);
            Array.Clear(contactBodies,0,contactBodies.Length);
            Array.Clear(centrePush,0,centrePush.Length);
            Array.Fill(friendlyBlockers,-1);
            Array.Clear(externalContact,0,externalContact.Length);
            foreach(var u in world.Battalions)
            {unitBefore[u.Id]=u.Position;unitBeforeFacing[u.Id]=u.Facing;unitBeforeForward[u.Id]=FormationMath.Forward(u.Facing);}
            for(int i=0;i<before.Length;i++)
            {
                before[i]=s.Positions[i];gait[i]=s.GaitPhases[i];facing[i]=s.Facings[i];
                active[i]=s.Alive[i]&&!s.IsKnockedDown(i,world.Time);
            }
        }
        void Index(Vector2[] positions,bool regimentContacts)
        {
            using var metric=BattleMetrics.Measure(BattleMetrics.Stage.ContactIndex);
            cells.Clear();cellCount=0;LastGridBuilds++;
            bool sleeping=false;
            foreach(var u in world.Battalions)sleeping|=u.AliveCount>0&&!contactUnits[u.Id];
            if(sleeping)
            {
                // Refresh before indexing so pressure wakes every newly adjacent
                // regiment, including a unit outside the original swept envelope.
                // MarkMoved expands those envelopes as pressure propagates.
                WakeNearbyUnits();
            }
            for(int i=0;i<next.Length;i++)if(active[i])
            {
                if(regimentContacts&&!contactUnits[world.Soldiers.BattalionIds[i]])continue;
                var p=positions[i];int x=Mathf.FloorToInt(p.x/CellSize),y=Mathf.FloorToInt(p.y/CellSize);long key=Cell(x,y);
                if(!cells.TryGetValue(key,out int cell))
                {cell=cellCount++;cells.Add(key,cell);cellHeads[cell]=-1;dirtyCells[cell]=false;neighborsReady[cell]=false;cellX[cell]=x;cellY[cell]=y;}
                next[i]=cellHeads[cell];cellHeads[cell]=i;
                soldierCells[i]=cell;dirtyCells[cell]|=movedLastPass[i];
            }
        }
        void FillNeighbors(int cx,int cy,int[] heads,int start)
        {
            for(int x=cx-1;x<=cx+1;x++)for(int y=cy-1;y<=cy+1;y++)
                heads[start++]=cells.TryGetValue(Cell(x,y),out int cell)?cellHeads[cell]:-1;
        }
        void QueueCell(int cell)
        {
            if(queuedCells[cell])return;queuedCells[cell]=true;
            for(int x=cellX[cell]-1;x<=cellX[cell]+1;x++)for(int y=cellY[cell]-1;y<=cellY[cell]+1;y++)
                if(cells.TryGetValue(Cell(x,y),out int neighbor))
                    for(int soldier=cellHeads[neighbor];soldier>=0;soldier=next[soldier])
                        candidates[soldier>>5]|=1u<<(soldier&31);
        }
        int NextCandidate(int start)
        {
            int word=start>>5;if(word>=candidates.Length)return before.Length;
            uint bits=candidates[word]&(uint.MaxValue<<(start&31));
            while(bits==0){if(++word>=candidates.Length)return before.Length;bits=candidates[word];}
            int bit=0;
            if((bits&0xffff)==0){bits>>=16;bit+=16;}
            if((bits&0xff)==0){bits>>=8;bit+=8;}
            if((bits&0xf)==0){bits>>=4;bit+=4;}
            if((bits&3)==0){bits>>=2;bit+=2;}
            if((bits&1)==0)bit++;
            return word*32+bit;
        }
        public void Resolve(float dt)
        {
            using var metric=BattleMetrics.Measure(BattleMetrics.Stage.BodyContacts);
            if(BattleOptimizationSettings.CrowdFlow){ResolveFlow(dt);return;}
            var s=world.Soldiers;int requiredSteps=1;
            LastGridBuilds=LastContactRegiments=0;
            LastPassOverlap=0;
            ResetUnitBounds();
            bool reuseContacts=true;
#if UNITY_EDITOR
            reuseContacts=!ExhaustiveVerification;
#endif
            bool regimentContacts=BattleOptimizationSettings.RegimentContacts;
            float separationTolerance=regimentContacts?.005f:Tolerance;
            foreach(var u in world.Battalions)
            {
                unitForward[u.Id]=FormationMath.Forward(u.Facing);
                float speed=world.MovementSpeed(u);
                correctionSpeed[u.Id]=Mathf.Max(speed*(u.Moving&&u.Kind==UnitKind.Infantry?1.2f:1f),world.Config.MarchSpeed*1.15f*u.GasMovementMultiplier);
                expandedGaits[u.Id]=FormationMath.ExpandedInfantryGaits(u);
                contactUnits[u.Id]=!reuseContacts||u.InMelee||(!regimentContacts&&(world.ContactMovement.Loose[u.Id]
                    ||u.Formation==FormationKind.Square||u.Formation==FormationKind.Diamond||u.ReviewRankSpacing>0));
                slotErrorSquared[u.Id]=0;
                proposedError[u.Id]=0;
            }
            for(int i=0;i<before.Length;i++)if(active[i])
            {
                var u=world.Battalions[s.BattalionIds[i]];
                // Contact magnifies tiny SIMD rounding differences. Evaluate the
                // Burst position proposal with the same managed arithmetic as scalar
                // movement; animation and heading work still runs in the job.
                Vector2 goal;
                if(world.MovementJobs!=null)proposed[i]=PositionProposal(i,u,dt,out goal);
                else {proposed[i]=s.Positions[i];goal=u.Position+FormationMath.SlotOffset(u,s.FormationSlots[i]);}
                if(!world.ContactMovement.Loose[u.Id])
                {
                    if(u.ReviewRankSpacing>0)goal=u.Position+FormationMath.SlotOffset(u,s.FormationSlots[i]);
                    proposedError[u.Id]=Mathf.Max(proposedError[u.Id],Vector2.Distance(proposed[i],goal));
                }
                radius[i]=BodyRadius(u.Kind);
                // Horses carry more mass; a planted bayonet wall resists a shove.
                mobility[i]=(u.Bracing?.08f:u.Moving||world.ContactMovement.Loose[u.Id]?1f:.35f)/(u.Kind==UnitKind.Cavalry?3f:1f);
                int id=u.Id;
                unitMin[id]=Vector2.Min(unitMin[id],Vector2.Min(before[i],proposed[i]));
                unitMax[id]=Vector2.Max(unitMax[id],Vector2.Max(before[i],proposed[i]));
                if(!regimentContacts&&!contactUnits[id])
                {
                    int slot=s.FormationSlots[i];var f=unitBeforeForward[id];
                    var oldGoal=unitBefore[id]+FormationMath.SlotOffset(slot,u.AliveCount,u.Files,u.Spacing,new Vector2(f.y,-f.x),f);
                    f=unitForward[id];
                    var newGoal=u.Position+FormationMath.SlotOffset(slot,u.AliveCount,u.Files,u.Spacing,new Vector2(f.y,-f.x),f);
                    slotErrorSquared[id]=Mathf.Max(slotErrorSquared[id],Mathf.Max((before[i]-oldGoal).sqrMagnitude,(proposed[i]-newGoal).sqrMagnitude));
                }
            }
            if(!regimentContacts)foreach(var u in world.Battalions)if(!contactUnits[u.Id]&&u.AliveCount>1)
            {
                // Certify internal clearance for the entire swept movement, rather
                // than trusting a possibly stale Reforming flag. Rigid slot paths
                // retain at least spacing*cos(turn/2), less both soldiers' errors.
                float turn=Mathf.Abs(FormationMath.AngleDifference(unitBeforeFacing[u.Id],u.Facing));
                float clearance=Mathf.Min(u.Spacing,FormationMath.RankSpacing(u.Spacing))*Mathf.Cos(turn*.5f)
                    -2f*Mathf.Sqrt(slotErrorSquared[u.Id]);
                contactUnits[u.Id]=clearance<=2f*BodyRadius(u.Kind)+.002f;
            }
            WakeNearbyUnits();
            bool anyContacts=false;
            foreach(var u in world.Battalions)anyContacts|=contactUnits[u.Id]&&u.AliveCount>0;
            for(int i=0;i<before.Length;i++)if(active[i]&&contactUnits[s.BattalionIds[i]])
                requiredSteps=Mathf.Max(requiredSteps,Mathf.CeilToInt(Vector2.Distance(before[i],proposed[i])/(regimentContacts?radius[i]*.9f:.2f)));
            // Advance bodies in short increments. Relative travel is less than
            // the smallest body diameter, so two men cannot exchange sides or
            // transmit a shove through a third man between contact passes.
            int steps=anyContacts?requiredSteps:0;
            for(int i=0;i<before.Length;i++)if(active[i])s.Positions[i]=before[i];
            for(int step=0;step<steps;step++)
            {
                for(int i=0;i<before.Length;i++)if(active[i])
                    s.Positions[i]+=(proposed[i]-before[i])/steps;
                // Rebuild the hash as pressure propagates through adjacent ranks.
                // Isolated/resting formations exit after the first pass.
                for(int pass=0;pass<12;pass++)
                {
                    Array.Clear(movedThisPass,0,movedThisPass.Length);
                    Index(s.Positions,regimentContacts);bool contact=false;float maximumOverlap=0;
                    queueContacts=reuseContacts&&pass>0;
                    if(queueContacts)
                    {
                        Array.Clear(candidates,0,candidates.Length);Array.Clear(queuedCells,0,cellCount);
                        for(int cell=0;cell<cellCount;cell++)if(dirtyCells[cell])QueueCell(cell);
                    }
                    using var pairMetric=BattleMetrics.Measure(BattleMetrics.Stage.ContactPairs);
                    // Ordered bitset traversal visits only disturbed neighborhoods.
                    // Shoves can enqueue later soldiers during this same pass.
                    for(int i=queueContacts?NextCandidate(0):0;i<before.Length;i=queueContacts?NextCandidate(i+1):i+1)if(active[i])
                    {
                        if(reuseContacts&&!contactUnits[s.BattalionIds[i]])continue;
                        Vector2 p=s.Positions[i];int cx=Mathf.FloorToInt(p.x/CellSize),cy=Mathf.FloorToInt(p.y/CellSize);
                        int[] heads=neighborHeads;int start;
                        if(reuseContacts&&cells.TryGetValue(Cell(cx,cy),out int cell))
                        {
                            start=cell*9;
                            // Cache only neighborhoods that actually need contact work.
                            if(!neighborsReady[cell]){FillNeighbors(cx,cy,heads,start);neighborsReady[cell]=true;}
                        }
                        else
                        {
                            // Earlier shoves can carry this man into a cell that
                            // was empty when the pass began. Query that cell too.
                            heads=emptyCellNeighbors;start=0;FillNeighbors(cx,cy,heads,0);
                        }
                        // A pair that did not move in the last pass or this one
                        // has exactly the same contact result. Wake neighborhoods
                        // immediately on every shove so pressure still propagates
                        // in the original soldier order within this pass.
                        if(reuseContacts&&pass>0&&!movedLastPass[i]&&!movedThisPass[i])
                        {
                            bool dirty=false;
                            for(int neighbor=start;neighbor<start+9;neighbor++)
                                if(heads[neighbor]>=0&&dirtyCells[soldierCells[heads[neighbor]]]){dirty=true;break;}
                            if(!dirty)continue;
                        }
                        for(int neighbor=start;neighbor<start+9;neighbor++)
                        {
                            // Cell chains are in descending soldier-ID order.
                            for(int j=heads[neighbor];j>i;j=next[j])
                            {
                                // A broad regiment envelope is only a candidate.
                                // Own ranks need hard-body physics when a real
                                // shove reaches them; casualty gaps elsewhere
                                // in that regiment remain ordinary formation work.
                                // Loose melee movement already separates comrades.
                                if(regimentContacts&&s.BattalionIds[i]==s.BattalionIds[j]
                                    &&!contactBodies[i]&&!contactBodies[j])continue;
                                if(reuseContacts&&pass>0&&!movedLastPass[i]&&!movedThisPass[i]&&!movedLastPass[j]&&!movedThisPass[j])continue;
                                Vector2 delta=s.Positions[i]-s.Positions[j];float size=radius[i]+radius[j],d=delta.sqrMagnitude;
                                if(d>=(size-separationTolerance)*(size-separationTolerance))continue;
                                float distance=Mathf.Sqrt(d);
                                maximumOverlap=Mathf.Max(maximumOverlap,size-distance);
                                // Tiny SIMD rounding differences at coincidence must
                                // not choose opposite shove directions for the pair.
                                Vector2 normal=distance>.01f?delta/distance:(before[i]-before[j]).normalized;
                                if(normal.sqrMagnitude<.5f)normal=FormationMath.Right(facing[i]);
                                contact|=Push(i,j,normal,size-distance);
                                // Returning men take a consistent side around their own
                                // blocked comrades instead of deadlocking nose-to-nose.
                                int unit=s.BattalionIds[i];
                                if(unit==s.BattalionIds[j]&&!world.ContactMovement.Loose[unit]
                                    &&world.Battalions[unit].Reforming
                                    &&(!world.Battalions[unit].Moving||world.Battalions[unit].Kind==UnitKind.Cavalry))
                                {
                                    Vector2 tangent=new Vector2(-normal.y,normal.x);
                                    Sidestep(i,normal,tangent);Sidestep(j,-normal,-tangent);
                                }
                            }
                        }
                    }
                    var swap=movedLastPass;movedLastPass=movedThisPass;movedThisPass=swap;
                    LastPassOverlap=maximumOverlap;
                    // Millimetre-scale settling does not warrant twelve more
                    // army-wide sweeps. Keep iterating while compression is visible.
                    if(!contact||(regimentContacts&&pass>=1&&maximumOverlap<.01f))break;
                }
            }
            for(int i=0;i<before.Length;i++)if(active[i])
            {
                if(!touched[i])s.Positions[i]=proposed[i];
                int id=s.BattalionIds[i];
                if(touched[i]){centrePush[id]+=s.Positions[i]-proposed[i];unitTouched[id]=true;}
                if(!touched[i]&&world.MovementJobs==null)continue;
                if(!touched[i]&&proposed[i].x==before[i].x&&proposed[i].y==before[i].y)
                {s.Velocities[i]=Vector2.zero;s.MovementSpeeds[i]=0;s.GaitPhases[i]=Mathf.Repeat(gait[i],1f);continue;}
                var u=world.Battalions[id];Vector2 velocity=(s.Positions[i]-before[i])/dt;float speed=velocity.magnitude;
                s.Velocities[i]=velocity;s.MovementSpeeds[i]=speed;
                bool running=u.Running||InfantryContactMovement.Fleeing(u);
                float stride=u.Kind==UnitKind.Cavalry?(speed>5?4f:speed>2.5f?2.6f:1.9f)
                    :FormationMath.SoldierStride(i,running,speed,u.AttackMode==InfantryAttackMode.BayonetCharge,s.Captains[i]||s.PikeSergeants[i],expandedGaits[id]);
                s.GaitPhases[i]=Mathf.Repeat(gait[i]+speed*dt/stride,1f);
                if(touched[i]&&speed>.08f&&s.MeleeEnds[i]<=world.Time)
                    s.Facings[i]=FormationMath.TurnTowards(facing[i],FormationMath.FacingOf(velocity),dt*(u.Kind==UnitKind.Cavalry?2.8f:RifleFireTiming.SoldierTurnRate));
            }
            foreach(var u in world.Battalions)
            {
                if(contactUnits[u.Id]&&u.AliveCount>0)LastContactRegiments++;
                // Unpushed regiments retain the error measured from the canonical
                // movement goal. Avoid another trigonometric slot calculation.
                if(reuseContacts&&!unitTouched[u.Id])
                {u.FormationError=proposedError[u.Id];u.Reforming|=u.FormationError>.22f;continue;}
                // Keep the logical regiment with its men when contact blocks or
                // pushes it. A halted unit holds its new position after the shove.
                if(unitTouched[u.Id]&&externalContact[u.Id])
                {
                    Vector2 push=centrePush[u.Id]/Mathf.Max(1,u.AliveCount);
                    u.Position+=push;if(!u.Moving)u.Destination+=push;
                    u.ActualSpeed=Vector2.Distance(unitBefore[u.Id],u.Position)/dt;
                }
                u.FormationError=0;
                if(world.ContactMovement.Loose[u.Id])continue;
                for(int slot=0;slot<u.AliveCount;slot++)
                {
                    int i=world.SoldierAtSlot(u,slot);
                    if(!active[i])continue;
                    u.FormationError=Mathf.Max(u.FormationError,Vector2.Distance(s.Positions[i],u.Position+FormationMath.SlotOffset(u,slot)));
                }
                u.Reforming|=u.FormationError>.22f;
            }
        }
        void ResetUnitBounds()
        {
            for(int i=0;i<unitMin.Length;i++)
            {unitMin[i]=new Vector2(float.PositiveInfinity,float.PositiveInfinity);unitMax[i]=new Vector2(float.NegativeInfinity,float.NegativeInfinity);}
        }
        void WakeNearbyUnits()
        {
            // The two-cell margin covers every candidate in the original nine-cell
            // search, including a body moved by an earlier shove in the same pass.
            // Swept bounds also catch units crossing between simulation ticks.
            float margin=CellSize*2f;
            for(int a=0;a<unitMin.Length;a++)for(int b=a+1;b<unitMin.Length;b++)
                if(unitMin[a].x<=unitMax[b].x+margin&&unitMin[b].x<=unitMax[a].x+margin
                    &&unitMin[a].y<=unitMax[b].y+margin&&unitMin[b].y<=unitMax[a].y+margin)
                    contactUnits[a]=contactUnits[b]=true;
        }
        void Sidestep(int i,Vector2 away,Vector2 tangent)
        {
            if(nudged[i])return;
            Vector2 travel=proposed[i]-before[i];
            float approaching=-Vector2.Dot(travel,away);
            if(approaching<=.001f)return;
            Vector2 move=Allowed(i,tangent*Mathf.Min(.03f,approaching*.4f));
            world.Soldiers.Positions[i]+=move;MarkMoved(i,move);nudged[i]=true;
        }
        Vector2 PositionProposal(int i,BattalionState u,float dt,out Vector2 target)
        {
            if(world.ContactMovement.Loose[u.Id]){target=world.ContactMovement.Targets[i];return target;}
            int slot=world.Soldiers.FormationSlots[i];Vector2 forward=unitForward[u.Id];
            target=u.Position+(u.Formation==FormationKind.Square||u.Formation==FormationKind.Diamond
                ?FormationMath.SlotOffset(u,slot)
                :FormationMath.SlotOffset(slot,u.AliveCount,u.Files,u.Spacing,new Vector2(forward.y,-forward.x),forward));
            float distance=dt*correctionSpeed[u.Id]*InfantryContactMovement.Pace(i,false);
            Vector2 carried=before[i]+(u.Position-unitBefore[u.Id]);
            return Vector2.MoveTowards(before[i],Vector2.MoveTowards(carried,target,distance),distance);
        }
        bool Push(int i,int j,Vector2 normal,float depth)
        {
            var positions=world.Soldiers.Positions;
            int aUnit=world.Soldiers.BattalionIds[i],bUnit=world.Soldiers.BattalionIds[j];
            if(aUnit!=bUnit){externalContact[aUnit]=true;externalContact[bUnit]=true;}
            if(aUnit!=bUnit&&world.Battalions[aUnit].Side==world.Battalions[bUnit].Side)
            {
                if(friendlyBlockers[aUnit]<0)friendlyBlockers[aUnit]=bUnit;
                if(friendlyBlockers[bUnit]<0)friendlyBlockers[bUnit]=aUnit;
            }
            float share=mobility[i]/(mobility[i]+mobility[j]);
            Vector2 wanted=normal*depth;
            Vector2 a=Allowed(i,wanted*share),b=Allowed(j,-wanted*(1-share));
            // A wall or field edge transfers the rejected portion to the other body.
            float missing=depth-Vector2.Dot(a-b,normal);
            if(missing>Tolerance)
            {
                if(a.sqrMagnitude<wanted.sqrMagnitude*share*share*.99f)b=Allowed(j,b-normal*missing);
                else a=Allowed(i,a+normal*missing);
            }
            positions[i]+=a;positions[j]+=b;
            MarkMoved(i,a);MarkMoved(j,b);
            return a.sqrMagnitude+b.sqrMagnitude>Tolerance*Tolerance;
        }
        void MarkMoved(int soldier,Vector2 displacement)
        {
            if(displacement.sqrMagnitude<=0)return;
            contactBodies[soldier]=true;
            touched[soldier]=true;movedThisPass[soldier]=true;dirtyCells[soldierCells[soldier]]=true;
            if(queueContacts){candidates[soldier>>5]|=1u<<(soldier&31);QueueCell(soldierCells[soldier]);}
            int unit=world.Soldiers.BattalionIds[soldier];contactUnits[unit]=true;
            var position=world.Soldiers.Positions[soldier];
            unitMin[unit]=Vector2.Min(unitMin[unit],position);unitMax[unit]=Vector2.Max(unitMax[unit],position);
        }
        Vector2 Allowed(int i,Vector2 displacement)
        {
            Vector2 p=world.Soldiers.Positions[i];float limit=world.Config.FieldHalfSize-radius[i];
            Vector2 target=p+displacement;
            if(!InfantryContactMovement.Fleeing(world.Battalions[world.Soldiers.BattalionIds[i]]))
            {target.x=Mathf.Clamp(target.x,-limit,limit);target.y=Mathf.Clamp(target.y,-limit,limit);}
            return world.Terrain.IsBlocked(target,radius[i])?Vector2.zero:target-p;
        }
    }
}
