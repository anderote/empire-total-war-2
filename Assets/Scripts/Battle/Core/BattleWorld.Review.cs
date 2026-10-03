using System;
using UnityEngine;
namespace EmpireBattle
{
    public sealed partial class BattleWorld
    {
        public void StepReview(float dt)
        {
            if(!Config.AssetReview)throw new InvalidOperationException("Review stepping requires a workbench world.");
            if(!Finite(dt)||dt<=0)return;
            Time+=dt;StepLooseHorses(dt);StepKnockdowns();
        }
        // Recycles a fixed, bounded roster only in the asset workbench. Call after retiring its visuals.
        public void ResetReviewBlock(int id,int files,float fileSpacing,float rankSpacing)
        {
            if(!Config.AssetReview)throw new InvalidOperationException("Review reset requires an asset workbench world.");
            var b=Battalions[id];
            int kept=0;
            for(int j=0;j<DeathCount;j++)if(Soldiers.BattalionIds[DeadSoldiers[j]]!=id)DeadSoldiers[kept++]=DeadSoldiers[j];
            Array.Clear(DeadSoldiers,kept,DeathCount-kept);DeathCount=kept;
            b.EscapedCount=0;b.AliveCount=b.SoldierCount;b.Files=Mathf.Clamp(files,1,b.SoldierCount);b.Formation=FormationKind.Line;
            b.Spacing=fileSpacing;b.ReviewRankSpacing=rankSpacing;b.Morale=100;b.Cohesion=1;
            b.Routing=b.Moving=b.Reforming=b.InMelee=b.Withdrawing=false;b.AttackTargetId=-1;
            b.FireAtWill=false;b.Destination=b.Position;b.DesiredFacing=b.Facing;
            slotsDirty[id]=false;
            for(int slot=0;slot<b.SoldierCount;slot++)
            {
                int i=b.SoldierStart+slot;livingSlots[id][slot]=i;formationOccupants[i]=i;
                Soldiers.FormationSlots[i]=slot;Soldiers.Alive[i]=true;Soldiers.Health[i]=b.MaxHealth;
                Soldiers.Positions[i]=b.Position+FormationMath.SlotOffset(b,slot);Soldiers.Facings[i]=b.Facing;
                Soldiers.Velocities[i]=Vector2.zero;Soldiers.MovementSpeeds[i]=0;
                Soldiers.KnockdownRecoveryVariants[i]=-1;
                Soldiers.KnockdownStarts[i]=Soldiers.KnockdownEnds[i]=Soldiers.KnockdownLandings[i]=0;
                Soldiers.MeleeTargets[i]=-1;Soldiers.MeleeStarts[i]=Soldiers.MeleeEnds[i]=Soldiers.MeleeReadyTimes[i]=0;
                Soldiers.MeleeActions[i]=MeleeAction.None;Soldiers.StrikeResolved[i]=false;
                Soldiers.FireTimes[i]=Soldiers.FireVolleys[i]=-100;Soldiers.ReloadReadyTimes[i]=0;
                Soldiers.FireTargetSoldiers[i]=-1;Soldiers.FireAimPoints[i]=Vector3.zero;
                Soldiers.KneelTimes[i]=float.PositiveInfinity;Deaths[i]=default;LooseHorses.Remove(i);
            }
            Outcome=BattleOutcome.Ongoing;
        }
    }
}
