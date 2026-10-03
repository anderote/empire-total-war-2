using UnityEngine;
namespace EmpireBattle
{
    public sealed partial class BattleWorld
    {
        public bool SetMeleeMode(int id, bool enabled)
        {
            if(!Valid(id))return false;
            var unit=Battalions[id];
            if(unit.Routing||unit.AliveCount<=0)return false;
            unit.MeleeMode=enabled;
            if(enabled)
            {
                RifleFireTiming.Interrupt(this,unit);unit.FireAtWill=false;
                if(Valid(unit.AttackTargetId))IssueCharge(id,unit.AttackTargetId);
            }
            else
            {
                if(unit.AttackMode==InfantryAttackMode.BayonetCharge)Halt(id);
                unit.FireAtWill=UnitRoster.HasFirearm(unit);
            }
            return true;
        }

        public bool IsPursuingFugitives(BattalionState unit) =>
            unit.AttackMode==InfantryAttackMode.BayonetCharge && !InfantryContactMovement.Fleeing(unit)
            && unit.AliveCount>0 && Valid(unit.AttackTargetId) && Battalions[unit.AttackTargetId].AliveCount>0
            && InfantryContactMovement.Fleeing(Battalions[unit.AttackTargetId]);

        public bool IssueCharge(int id,int targetId)
        {
            if(!Valid(id)||!Valid(targetId))return false;
            var u=Battalions[id];var target=Battalions[targetId];
            if(u.Routing||u.AliveCount<=0||target.AliveCount<=0||u.Side==target.Side)return false;
            if(u.Kind==UnitKind.Cavalry&&u.ChargePhase==ChargePhase.Recovering&&Time<u.ChargeRecoveryUntil)return false;
            ClearPendingOrders(id);
            ContactMovement.CancelGathering(id);
            if(u.AttackMode==InfantryAttackMode.BayonetCharge&&u.AttackTargetId==targetId&&!u.ChargeSpent)return true;
            if(u.Kind==UnitKind.Cavalry)SetFormation(id,FormationKind.Diamond);
            RifleFireTiming.Interrupt(this,u);u.FireAtWill=false;u.Withdrawing=false;u.Running=true;
            u.AttackMode=InfantryAttackMode.BayonetCharge;u.AttackTargetId=targetId;
            u.ChargePhase=ChargePhase.Approach;u.ChargeSpent=false;u.ImpactSpeed=0;u.ChargeElapsed=0;u.ChargeCycle++;
            u.ChargeDirection=(target.Position-u.Position).normalized;
            UpdateChargeOrders(0);return true;
        }
        void UpdateChargeOrders(float dt)
        {
            foreach(var u in Battalions)
            {
                if(u.AttackMode!=InfantryAttackMode.BayonetCharge||u.Routing||u.AliveCount<=0)continue;
                u.ChargeElapsed+=dt;
                if(!Valid(u.AttackTargetId)){Halt(u.Id);continue;}
                if(Battalions[u.AttackTargetId].AliveCount<=0)
                {
                    int next=InfantryContactMovement.Fleeing(Battalions[u.AttackTargetId])?NextFleeingTarget(u):-1;
                    if(next<0)
                    {
                        bool hunted=InfantryContactMovement.Fleeing(Battalions[u.AttackTargetId]);
                        Halt(u.Id);
                        if(hunted)ContactMovement.GatherAfterPursuit(u);
                        continue;
                    }
                    u.AttackTargetId=next;
                }
                var target=Battalions[u.AttackTargetId];
                bool pursuing=IsPursuingFugitives(u);
                if(pursuing){u.PursueTargets=true;if(CanFixBayonets(u)&&!u.BayonetsFixed)SetBayonets(u.Id,true);}
                if(pursuing&&u.ChargePhase==ChargePhase.Recovering)u.ChargePhase=ChargePhase.Approach;
                if(u.Kind==UnitKind.Cavalry&&u.ChargePhase==ChargePhase.Recovering)
                {
                    if(Time>=u.ChargeRecoveryUntil&&!u.Moving){u.AttackMode=InfantryAttackMode.None;u.ChargePhase=ChargePhase.None;u.ChargeSpent=false;}
                    continue;
                }
                Vector2 delta=target.Position-u.Position;
                if(u.Kind==UnitKind.Cavalry&&u.ChargeSpent&&!pursuing)
                {
                    if(Time-u.LastImpactTime>.7f)
                    {
                        u.ChargePhase=ChargePhase.Recovering;u.ChargeRecoveryUntil=Time+4f;
                        IssueMove(u.Id,u.Position+u.ChargeDirection*25,FormationMath.FacingOf(u.ChargeDirection),preserveAttack:true);
                    }
                    continue;
                }
                if(delta.sqrMagnitude<.001f)continue;
                float facing=FormationMath.FacingOf(delta);
                float standoff=u.Kind==UnitKind.Cavalry||IsPursuingFugitives(u)?0:FormationMath.HalfExtents(u).y+MeleeSystem.ProjectedRadius(target,delta.normalized)+.45f;
                Vector2 destination=target.Position-delta.normalized*standoff;
                IssueMove(u.Id,destination,facing,preserveRoute:true,preserveAttack:true);
            }
        }
        int NextFleeingTarget(BattalionState unit)
        {
            int best=-1;float score=float.PositiveInfinity;
            foreach(var enemy in Battalions)
            {
                if(enemy.Side==unit.Side||enemy.AliveCount<=0||!InfantryContactMovement.Fleeing(enemy))continue;
                float distance=Vector2.Distance(unit.Position,enemy.Position);if(distance>120)continue;
                int assigned=0;
                foreach(var ally in Battalions)
                    if(ally.Id!=unit.Id&&ally.Side==unit.Side&&IsPursuingFugitives(ally)&&ally.AttackTargetId==enemy.Id)assigned+=ally.AliveCount;
                float candidate=distance+20f*assigned/Mathf.Max(1,enemy.AliveCount);
                if(candidate<score){score=candidate;best=enemy.Id;}
            }
            return best;
        }
        float MountedDesiredSpeed(BattalionState u)
        {
            if(!u.Moving)return 0;
            float speed=u.Routing?8f:IsPursuingFugitives(u)?10.5f:u.ChargePhase==ChargePhase.Approach?9f:u.ChargePhase==ChargePhase.Impact?3.5f:u.Running?7.6f:u.Trotting?4.2f:2.2f;
            return speed*Mathf.Lerp(1,.7f,u.Fatigue/100f);
        }
        public void RepelCavalry(BattalionState u,Vector2 outward)
        {
            if(u.ChargePhase==ChargePhase.Recovering)return;
            u.ChargeSpent=true;u.ChargePhase=ChargePhase.Recovering;u.ChargeRecoveryUntil=Time+5f;u.MountedSpeed=Mathf.Min(u.MountedSpeed,2f);
            u.ChargeDirection=(outward+FormationMath.Right(u.Facing)*.65f).normalized;
            IssueMove(u.Id,u.Position+u.ChargeDirection*23,FormationMath.FacingOf(u.ChargeDirection),preserveAttack:true);
            u.Morale=Mathf.Max(0,u.Morale-7);u.MoraleReason="Charge repelled by bayonets";
        }
        public bool ApplyMeleeHit(int attacker,int target,float damage,Vector3 impulse=default,bool knockdown=false)
        {
            if(target<0||target>=Soldiers.Alive.Length||!Soldiers.Alive[target]||damage<=0||!Finite(damage))return false;
            int attackingUnit=attacker>=0?Soldiers.BattalionIds[attacker]:-1;
            var defender=Battalions[Soldiers.BattalionIds[target]];
            if(Valid(attackingUnit)&&Battalions[attackingUnit].Side==defender.Side)return false;
            if(Valid(attackingUnit)){var attackerUnit=Battalions[attackingUnit];if(attackerUnit.Progression.Has(RegimentAbility.AssaultTraining)&&(attackerUnit.ChargePhase==ChargePhase.Impact||attackerUnit.ChargePhase==ChargePhase.Approach))damage*=1.15f+.10f*attackerUnit.Progression.AbilityRank(RegimentAbility.AssaultTraining);damage*=attackerUnit.Progression.MeleeMultiplier*(1+attackerUnit.EquipmentQuality)*(1+attackerUnit.Kit.melee);if(!Soldiers.Captains[attacker]&&!Soldiers.PikeSergeants[attacker])damage*=BayonetMeleeMultiplier(attackerUnit);}
            // Pursuit is lethal only on real melee contact; ranged wounds are unchanged.
            if(InfantryContactMovement.Fleeing(defender))damage*=Valid(attackingUnit)&&Battalions[attackingUnit].Kind==UnitKind.Cavalry?4f:3f;
            bool killed=knockdown&&defender.Kind==UnitKind.Infantry
                ?ApplyArtilleryHit(target,damage,DeathCause.Melee,impulse,Terrain.SurfacePoint(Soldiers.Positions[target]))
                :ApplyWound(target,damage,DeathCause.Melee,Terrain.SurfacePoint(Soldiers.Positions[target]));
            if(Valid(attackingUnit))
            {
                var u=Battalions[attackingUnit];u.Hits++;u.Experience+=killed?6:1;if(killed)u.Kills++;
                Soldiers.Experience[attacker]+=killed?6:1;if(killed)Soldiers.Kills[attacker]++;
            }
            return killed;
        }
    }
}
