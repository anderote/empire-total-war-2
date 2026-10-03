using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace EmpireBattle
{
    // Owned by the runtime (or a using block in tests), not by transient BattleWorld fixtures.
    // Formation proposals/slot reassignment remain ordered on the main thread.
    public sealed class SoldierMovementJobs : IDisposable
    {
        struct Formation
        {
            public Vector2 Position, Translation, Forward, Right;
            public float Facing, Spacing, CorrectionSpeed;
            public int AliveCount, Files, SquareRanks;
            public bool Square, Diamond, Running, Charging, ExpandedGaits, Backing, IndividualAim, Mounted, Loose, Fleeing;
        }
        struct Soldier
        {
            public Vector2 Position, Velocity, LooseTarget;
            public Vector3 FireDirection;
            public float Facing, Gait, FireTime, Speed, Error, HeadingError, MeleeUntil, MeleeFacing, LooseFacing;
            public int Unit, Slot;
            public bool Alive, Command;
        }
        NativeArray<Formation> formations;
        NativeArray<Soldier> soldiers;
        readonly BattleWorld world;
        public SoldierMovementJobs(BattleWorld value)
        {
            world=value;
            formations=new NativeArray<Formation>(world.Battalions.Length,Allocator.Persistent);
            soldiers=new NativeArray<Soldier>(world.Soldiers.Alive.Length,Allocator.Persistent);
        }
        public void Prepare(BattalionState unit,Vector2 oldCenter,float correctionSpeed)
        {
            Vector2 forward=FormationMath.Forward(unit.Facing);
            formations[unit.Id]=new Formation {
                Position=unit.Position,Translation=unit.Position-oldCenter,Forward=forward,Right=new Vector2(forward.y,-forward.x),
                Facing=unit.Facing,Spacing=unit.Spacing,CorrectionSpeed=correctionSpeed,AliveCount=unit.AliveCount,Files=unit.Files,
                SquareRanks=FormationMath.SquareRanks(unit),Square=unit.Formation==FormationKind.Square,Diamond=unit.Formation==FormationKind.Diamond,Running=unit.Running||InfantryContactMovement.Fleeing(unit),
                Charging=unit.AttackMode==InfantryAttackMode.BayonetCharge,ExpandedGaits=FormationMath.ExpandedInfantryGaits(unit),Loose=world.ContactMovement.Loose[unit.Id],Fleeing=InfantryContactMovement.Fleeing(unit),
                Mounted=unit.Kind==UnitKind.Cavalry,Backing=false,IndividualAim=unit.Kind==UnitKind.Infantry&&!unit.Moving&&!unit.Routing };
        }
        public void Step(float dt)
        {
            var source=world.Soldiers;
            for(int i=0;i<soldiers.Length;i++)
                soldiers[i]=new Soldier { Alive=source.Alive[i] && !source.IsKnockedDown(i,world.Time),Unit=source.BattalionIds[i],Slot=source.FormationSlots[i],Command=source.Captains[i]||source.PikeSergeants[i],
                    LooseFacing=world.ContactMovement.Facings[i],LooseTarget=world.ContactMovement.Targets[i],MeleeUntil=source.MeleeEnds[i],MeleeFacing=world.ContactMovement.MeleeFacings[i],Position=source.Positions[i],Facing=source.Facings[i],Gait=source.GaitPhases[i],FireTime=source.FireTimes[i],FireDirection=source.FireDirections[i] };
            new MovementJob { Formations=formations,Soldiers=soldiers,Delta=dt,Now=world.Time }.Schedule(soldiers.Length,128).Complete();
            Array.Clear(MaxErrors,0,MaxErrors.Length);Array.Clear(MaxHeadingErrors,0,MaxHeadingErrors.Length);
            for(int i=0;i<soldiers.Length;i++)
            {
                var value=soldiers[i];if(!value.Alive)continue;
                source.Positions[i]=value.Position;source.Velocities[i]=value.Velocity;source.MovementSpeeds[i]=value.Speed;
                source.Facings[i]=value.Facing;source.GaitPhases[i]=value.Gait;
                MaxErrors[value.Unit]=Mathf.Max(MaxErrors[value.Unit],value.Error);
                MaxHeadingErrors[value.Unit]=Mathf.Max(MaxHeadingErrors[value.Unit],value.HeadingError);
            }
        }
        float[] errors,headings;
        public float[] MaxErrors => errors??(errors=new float[world.Battalions.Length]);
        public float[] MaxHeadingErrors => headings??(headings=new float[world.Battalions.Length]);
        [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard,CompileSynchronously=true)]
        struct MovementJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Formation> Formations;
            public NativeArray<Soldier> Soldiers;
            public float Delta,Now;
            public void Execute(int index)
            {
                var s=Soldiers[index];if(!s.Alive)return;
                var f=Formations[s.Unit];
                Vector2 target=f.Loose?s.LooseTarget:f.Position+(f.Square
                    ? FormationMath.SquareSlotOffset(s.Slot,f.AliveCount,f.SquareRanks,f.Spacing,f.Facing)
                    : f.Diamond ? FormationMath.DiamondSlotOffset(s.Slot,f.AliveCount,f.Spacing,f.Facing)
                    : FormationMath.SlotOffset(s.Slot,f.AliveCount,f.Files,f.Spacing,f.Right,f.Forward));
                Vector2 before=s.Position;
                float variation=InfantryContactMovement.Pace(index,f.Loose&&f.Fleeing);
                Vector2 next=f.Loose?target:Vector2.MoveTowards(before+f.Translation,target,Delta*f.CorrectionSpeed*variation);
                if(!f.Loose)next=Vector2.MoveTowards(before,next,Delta*f.CorrectionSpeed*variation);
                Vector2 velocity=(next-before)/Delta;
                float actual=velocity.magnitude;
                bool backing=f.Backing&&Vector2.Dot(velocity,f.Forward)<0f;
                float stride=f.Mounted?(actual>5?4f:actual>2.5f?2.6f:1.9f):FormationMath.SoldierStride(index,f.Running,actual,f.Charging,s.Command,f.ExpandedGaits);
                s.Gait=Mathf.Repeat(s.Gait+actual*Delta/stride*(backing?-1f:1f),1f);
                float slotFacing=f.Facing+(f.Square?FormationMath.SquareSide(s.Slot,f.AliveCount)*Mathf.PI*.5f:0f);
                float desired=s.MeleeUntil>Now?s.MeleeFacing:slotFacing;
                if(actual>.08f&&!backing&&s.MeleeUntil<=Now)desired=FormationMath.FacingOf(velocity);
                bool individualAim=f.IndividualAim&&actual<=.04f&&s.MeleeUntil<=Now&&s.FireTime>=0&&Now<s.FireTime+RifleFireTiming.KneelDelay;
                if(individualAim)
                    desired=FormationMath.FacingOf(new Vector2(s.FireDirection.x,s.FireDirection.z));
                s.Facing=f.Loose?s.LooseFacing:FormationMath.TurnTowards(s.Facing,desired,Delta*(f.Mounted?2.8f:RifleFireTiming.SoldierTurnRate));
                s.Error=f.Loose?0f:Vector2.Distance(next,target);
                s.HeadingError=f.Loose||individualAim||s.MeleeUntil>Now?0f:Mathf.Abs(FormationMath.AngleDifference(s.Facing,slotFacing));
                s.Position=next;s.Velocity=velocity;s.Speed=actual;Soldiers[index]=s;
            }
        }
        public void Dispose()
        {
            if(formations.IsCreated)formations.Dispose();if(soldiers.IsCreated)soldiers.Dispose();
        }
    }
}
