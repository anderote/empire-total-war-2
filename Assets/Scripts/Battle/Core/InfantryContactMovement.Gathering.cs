using System;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class InfantryContactMovement
    {
        public bool[] Gathering { get; private set; }
        public bool[] PursuitReserves { get; private set; }
        bool[] gatheringActive;
        Vector2[] gatheringCenters;
        float[] gatheringFacings;
        int[] gatheringSlots, gatheringOccupants;
        const float GatheringSpacing=2.2f;

        void InitializeGathering(int count)
        {
            Gathering=new bool[world.Battalions.Length];PursuitReserves=new bool[count];
            gatheringActive=new bool[Gathering.Length];gatheringCenters=new Vector2[Gathering.Length];
            gatheringFacings=new float[Gathering.Length];
            gatheringSlots=new int[count];gatheringOccupants=new int[count];
            Array.Fill(gatheringSlots,-1);Array.Fill(gatheringOccupants,-1);
        }

        public void CancelGathering(int unit) => Gathering[unit]=false;
        public void GatherAfterPursuit(BattalionState unit)
        {
            if(unit.Kind==UnitKind.Infantry&&!Fleeing(unit)&&unit.AliveCount>0)Gathering[unit.Id]=true;
        }

        Vector2 PhysicalCenter(BattalionState unit)
        {
            Vector2 sum=Vector2.zero;int count=0;
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                if(world.Soldiers.Alive[i]){sum+=world.Soldiers.Positions[i];count++;}
            return count>0?sum/count:unit.Position;
        }

        Vector2 GatheringSlot(BattalionState unit,int slot)
        {
            // Stable, open files with small personal offsets; hunters leave holes
            // rather than making the whole reserve shuffle whenever someone returns.
            int files=Mathf.CeilToInt(Mathf.Sqrt(unit.SoldierCount*3f));
            Vector2 forward=FormationMath.Forward(gatheringFacings[unit.Id]);
            Vector2 right=FormationMath.Right(gatheringFacings[unit.Id]);
            Vector2 offset=FormationMath.SlotOffset(slot,unit.SoldierCount,files,GatheringSpacing,right,forward);
            offset+=right*Mathf.Lerp(-.25f,.25f,RifleFireTiming.Variation(unit.SoldierStart+slot,829));
            offset+=forward*Mathf.Lerp(-.25f,.25f,RifleFireTiming.Variation(unit.SoldierStart+slot,831));
            return gatheringCenters[unit.Id]+offset;
        }

        void PrepareGathering()
        {
            var s=world.Soldiers;
            foreach(var unit in world.Battalions)
            {
                if(Fleeing(unit)||unit.AliveCount<=0)Gathering[unit.Id]=false;
                bool active=Pursuing[unit.Id]||Gathering[unit.Id];
                if(!active)gatheringActive[unit.Id]=false;
                bool anyReserve=false;
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                {
                    PursuitReserves[i]=active&&s.Alive[i]&&!s.IsKnockedDown(i,world.Time)&&PursuitTargets[i]<0;
                    if(PursuitReserves[i]){anyReserve=true;continue;}
                    int slot=gatheringSlots[i];
                    if(slot>=0)gatheringOccupants[unit.SoldierStart+slot]=-1;
                    gatheringSlots[i]=-1;
                }
                if(!anyReserve){gatheringActive[unit.Id]=false;continue;}
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                {
                    if(!PursuitReserves[i]||gatheringSlots[i]>=0)continue;
                    if(!gatheringActive[unit.Id])
                    {
                        gatheringCenters[unit.Id]=PhysicalCenter(unit);
                        gatheringFacings[unit.Id]=unit.Facing;gatheringActive[unit.Id]=true;
                    }
                    int best=-1;float distance=float.PositiveInfinity;
                    for(int slot=0;slot<unit.SoldierCount;slot++)
                    {
                        if(gatheringOccupants[unit.SoldierStart+slot]>=0)continue;
                        Vector2 target=GatheringSlot(unit,slot);
                        float candidate=(target-s.Positions[i]).sqrMagnitude;
                        if(Mathf.Abs(target.x)>world.Config.FieldHalfSize-.5f||Mathf.Abs(target.y)>world.Config.FieldHalfSize-.5f)continue;
                        if(candidate>=distance||world.Terrain.IsBlocked(target,.35f))continue;
                        distance=candidate;best=slot;
                    }
                    gatheringSlots[i]=best;
                    if(best>=0)gatheringOccupants[unit.SoldierStart+best]=i;
                }
            }
        }

        Vector2 GatheringTravel(int soldier,BattalionState unit)
        {
            int slot=gatheringSlots[soldier];if(slot<0)return Vector2.zero;
            Vector2 delta=GatheringSlot(unit,slot)-world.Soldiers.Positions[soldier];
            // Leave room to stand naturally; do not continually dress an exact rank.
            return delta.normalized*Mathf.Max(0,delta.magnitude-.55f);
        }

        public void UpdatePursuitCenters()
        {
            // The regiment marker and future orders follow its men, not a phantom
            // formation centre that keeps charging ahead after most men stop hunting.
            foreach(var unit in world.Battalions)
            {
                if((!Pursuing[unit.Id]&&!Gathering[unit.Id])||unit.AliveCount<=0)continue;
                unit.Position=PhysicalCenter(unit);
                if(Gathering[unit.Id])unit.Destination=unit.Position;
            }
        }

        public float PursuitFatigueRate(BattalionState unit)
        {
            var s=world.Soldiers;float strain=world.FatigueRate(unit),sum=0;int count=0;
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
            {
                if(!s.Alive[i])continue;
                bool fighting=s.MeleeTargets[i]>=0&&s.MeleeEnds[i]>world.Time;
                float speed=s.MovementSpeeds[i];
                sum+=fighting||!PursuitReserves[i]||speed>GatheringSpeed(unit)*1.1f
                    ?.45f*strain:(speed>.1f?-.12f:-.25f)/strain;
                count++;
            }
            return sum/Mathf.Max(1,count);
        }

        float GatheringSpeed(BattalionState unit) => Mathf.Max(.1f,world.Config.MarchSpeed)
            *unit.BaseStats.Speed*(1+unit.Kit.speed)
            *Mathf.Lerp(1f,.65f,Mathf.Clamp01(unit.Fatigue/100f))*world.Terrain.SpeedMultiplier(unit.Position)*unit.GasMovementMultiplier;
    }
}
