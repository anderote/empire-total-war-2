using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class InfantryContactMovement
    {
        // Shared across regiments and troop types, so nearby hunters divide the fleeing
        // soldiers between them. Stable assignments avoid a nearest-target stampede.
        public int[] PursuitTargets { get; private set; }
        public bool[] Pursuing { get; private set; }
        readonly Dictionary<long,int> pursuitCells=new Dictionary<long,int>(SpatialCellComparer.Instance);
        int[] pursuitNext, pursuitOwners;
        float nextPursuitAssignment;
        const float PursuitCellSize=12f, PursuitReach=60f;

        void InitializePursuit(int count)
        {
            PursuitTargets=new int[count];Array.Fill(PursuitTargets,-1);
            Pursuing=new bool[world.Battalions.Length];
            pursuitNext=new int[count];pursuitOwners=new int[count];
            InitializeGathering(count);
        }

        bool ValidPursuitTarget(int soldier,int target)
        {
            var s=world.Soldiers;
            if(target<0||target>=s.Alive.Length||!s.Alive[target]||s.IsKnockedDown(target,world.Time))return false;
            var hunter=world.Battalions[s.BattalionIds[soldier]];
            var victim=world.Battalions[s.BattalionIds[target]];
            return victim.Side!=hunter.Side&&Fleeing(victim)&&
                (s.Positions[target]-s.Positions[soldier]).sqrMagnitude<=PursuitReach*PursuitReach;
        }

        void PreparePursuit()
        {
            var s=world.Soldiers;bool any=false,changed=false;
            foreach(var unit in world.Battalions)
            {
                // Once dispersed, keep hunting until the order ends. Regiment centres
                // can separate even while individual men are still catching stragglers.
                bool pursuing=world.IsPursuingFugitives(unit)&&(Pursuing[unit.Id]||MeleeSystem.EdgeGap(unit,unit.Position,
                    world.Battalions[unit.AttackTargetId],world.Battalions[unit.AttackTargetId].Position)<PursuitReach);
                changed|=pursuing!=Pursuing[unit.Id];Pursuing[unit.Id]=pursuing;any|=pursuing;
            }
            if(!any&&!changed)return;
            for(int i=0;i<s.Alive.Length;i++)
            {
                if(!s.Alive[i]||s.IsKnockedDown(i,world.Time)||!Pursuing[s.BattalionIds[i]])
                {changed|=PursuitTargets[i]>=0;PursuitTargets[i]=-1;}
                else if(PursuitTargets[i]>=0&&!ValidPursuitTarget(i,PursuitTargets[i]))
                {PursuitTargets[i]=-1;changed=true;}
            }
            if(!any||(!changed&&world.Time<nextPursuitAssignment))return;
            nextPursuitAssignment=world.Time+.4f;
            pursuitCells.Clear();Array.Fill(pursuitOwners,-1);
            for(int i=0;i<s.Alive.Length;i++)
            {
                if(!s.Alive[i]||s.IsKnockedDown(i,world.Time)||!Fleeing(world.Battalions[s.BattalionIds[i]]))continue;
                var p=s.Positions[i];long cell=Cell(Mathf.FloorToInt(p.x/PursuitCellSize),Mathf.FloorToInt(p.y/PursuitCellSize));
                pursuitNext[i]=pursuitCells.TryGetValue(cell,out int head)?head:-1;pursuitCells[cell]=i;
            }
            // Keep one existing hunter on each victim before assigning unclaimed targets.
            for(int i=0;i<PursuitTargets.Length;i++)
            {
                int target=PursuitTargets[i];if(target<0)continue;
                if(world.Terrain.BlocksLine(s.Positions[i],s.Positions[target])){PursuitTargets[i]=-1;continue;}
                if(pursuitOwners[target]<0)pursuitOwners[target]=i;
                else PursuitTargets[i]=-1;
            }
            for(int i=0;i<s.Alive.Length;i++)
            {
                if(!s.Alive[i]||s.IsKnockedDown(i,world.Time)||!Pursuing[s.BattalionIds[i]]||PursuitTargets[i]>=0)continue;
                Vector2 p=s.Positions[i];int cx=Mathf.FloorToInt(p.x/PursuitCellSize),cz=Mathf.FloorToInt(p.y/PursuitCellSize);
                int best=-1;float bestScore=float.PositiveInfinity;
                for(int x=cx-5;x<=cx+5;x++)for(int z=cz-5;z<=cz+5;z++)
                {
                    if(!pursuitCells.TryGetValue(Cell(x,z),out int target))continue;
                    for(;target>=0;target=pursuitNext[target])
                    {
                        if(!ValidPursuitTarget(i,target))continue;
                        int owner=pursuitOwners[target];float distance=Vector2.Distance(s.Positions[target],p);
                        // A close interceptor may replace a distant hunter, but small
                        // distance changes must not make two men swap jobs every tick.
                        if(owner>=0)
                        {
                            float incumbent=Vector2.Distance(s.Positions[target],s.Positions[owner]);
                            if(distance+Mathf.Max(3f,incumbent*.2f)>=incumbent)continue;
                        }
                        // Cover reachable unclaimed fugitives before relieving an
                        // existing hunter. This also avoids leaving holes in a full hunt.
                        float score=distance+(owner>=0?PursuitReach:0f);
                        if(score>bestScore+.0001f||(Mathf.Abs(score-bestScore)<=.0001f&&target>best))continue;
                        if(world.Terrain.BlocksLine(p,s.Positions[target]))continue;
                        best=target;bestScore=score;
                    }
                }
                PursuitTargets[i]=best;
                if(best>=0)
                {
                    int displaced=pursuitOwners[best];
                    if(displaced>=0)PursuitTargets[displaced]=-1;
                    pursuitOwners[best]=i;
                }
            }
        }
    }
}
