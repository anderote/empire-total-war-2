using System;
using System.Collections.Generic;
using UnityEngine;
namespace EmpireBattle
{
    /// <summary>Deterministic contact combat, identical at every camera distance. No runtime skeletons or duel objects.</summary>
    public sealed class MeleeSystem
    {
        readonly BattleWorld world;
        readonly Dictionary<long,int> cells=new Dictionary<long,int>(SpatialCellComparer.Instance);
        readonly int[] next, reservations, hitAttackers, hitTargets;
        readonly float[] hitDamage;readonly Vector3[] hitImpulse;readonly bool[] hitKnockdown,active;
        readonly Vector2[] boundsMin,boundsMax;
        int hitCount;public int ResolvedStrikes {get;private set;} public int ChargeImpacts {get;private set;} public int ParriedStrikes {get;private set;}
        const float CellSize=3f;
        public int RepelledCharges { get; private set; }
        public MeleeSystem(BattleWorld value)
        {
            world=value;int n=world.Soldiers.Alive.Length;
            next=new int[n];reservations=new int[n];hitAttackers=new int[n];hitTargets=new int[n];hitDamage=new float[n];hitImpulse=new Vector3[n];hitKnockdown=new bool[n];active=new bool[world.Battalions.Length];boundsMin=new Vector2[active.Length];boundsMax=new Vector2[active.Length];
        }
        static long Cell(int x,int z)=>((long)x<<32)|(uint)z;
        public static float ProjectedRadius(BattalionState u,Vector2 axis)
        {
            var h=FormationMath.HalfExtents(u);return Mathf.Abs(Vector2.Dot(axis,FormationMath.Right(u.Facing)))*h.x+Mathf.Abs(Vector2.Dot(axis,FormationMath.Forward(u.Facing)))*h.y;
        }
        public static float EdgeGap(BattalionState a,Vector2 ap,BattalionState b,Vector2 bp)
        {
            Vector2 delta=bp-ap;if(delta.sqrMagnitude<.001f)return -1;
            return delta.magnitude-ProjectedRadius(a,delta.normalized)-ProjectedRadius(b,delta.normalized);
        }
        public static float BraceStrength(BattleWorld w,int defender,Vector2 incoming)
        {
            var s=w.Soldiers;var u=w.Battalions[s.BattalionIds[defender]];
            if(!u.BayonetsFixed&&!s.PikeSergeants[defender])return 0;
            if(u.Kind!=UnitKind.Infantry||s.Captains[defender]||u.Role==UnitRole.Swordsman||InfantryContactMovement.Fleeing(u)||u.Moving||u.Reforming||u.Cohesion<.55f||s.IsKnockedDown(defender,w.Time)||u.Formation==FormationKind.Skirmish)return 0;
            int slot=s.FormationSlots[defender];
            if(u.Formation==FormationKind.Square&&!FormationMath.IsBayonetWall(u,slot))return 0;
            Vector2 toward=incoming-s.Positions[defender];
            float facing=toward.sqrMagnitude<.001f?1:Vector2.Dot(FormationMath.Forward(s.Facings[defender]),toward.normalized);
            if(facing<.65f)return 0;
            // Count real supporting neighbours on this wall; gaps, missing ranks and exposed corners weaken it.
            int support=0;
            for(int i=u.SoldierStart;i<u.SoldierStart+u.SoldierCount;i++)
            {
                if(i==defender||!s.Alive[i]||s.IsKnockedDown(i,w.Time)||(s.Positions[i]-s.Positions[defender]).sqrMagnitude>2.1f*2.1f)continue;
                if(u.Formation==FormationKind.Square&&FormationMath.SquareSide(s.FormationSlots[i],u.AliveCount)!=FormationMath.SquareSide(slot,u.AliveCount))continue;
                if(Vector2.Dot(FormationMath.Forward(s.Facings[i]),toward.normalized)>.65f)support++;
            }
            float wall=u.Formation==FormationKind.Square?1f:u.Bracing
                ?Mathf.Lerp(.62f,.82f,Mathf.Clamp01((w.Time-u.BraceStarted)/.65f)):.62f;
            return wall*u.Cohesion*Mathf.Clamp01(support/3f);
        }
        public void Step(float dt,bool[] threatened)
        {
            var s=world.Soldiers;var units=world.Battalions;float now=world.Time;
            Array.Clear(active,0,active.Length);Array.Clear(reservations,0,reservations.Length);hitCount=0;
            foreach(var u in units)
            {u.InMelee=false;boundsMin[u.Id]=new Vector2(float.MaxValue,float.MaxValue);boundsMax[u.Id]=new Vector2(float.MinValue,float.MinValue);}
            for(int i=0;i<s.Alive.Length;i++)if(s.Alive[i])
            {int id=s.BattalionIds[i];boundsMin[id]=Vector2.Min(boundsMin[id],s.Positions[i]);boundsMax[id]=Vector2.Max(boundsMax[id],s.Positions[i]);}
            for(int a=0;a<units.Length;a++)for(int b=a+1;b<units.Length;b++)
            {
                var u=units[a];var v=units[b];if(u.Side==v.Side||u.AliveCount<=0||v.AliveCount<=0)continue;
                // Dispersed soldiers can still be caught after their regiment centres separate.
                float gap=world.ContactMovement.Loose[a]||world.ContactMovement.Loose[b]
                    ?BoundsGap(a,b):EdgeGap(u,u.Position,v,v.Position);
                if(gap<4f){active[a]=active[b]=true;}
                if(gap<1.8f){u.InMelee=v.InMelee=true;threatened[a]=threatened[b]=true;u.LastMeleeTime=v.LastMeleeTime=now;}
            }
            cells.Clear();
            for(int i=0;i<s.Alive.Length;i++)
            {
                if(!s.Alive[i]||!active[s.BattalionIds[i]])continue;
                int x=Mathf.FloorToInt(s.Positions[i].x/CellSize),z=Mathf.FloorToInt(s.Positions[i].y/CellSize);
                long key=Cell(x,z);next[i]=cells.TryGetValue(key,out int head)?head:-1;cells[key]=i;
            }
            for(int i=0;i<s.Alive.Length;i++)
            {
                if(!s.Alive[i]||s.IsKnockedDown(i,now))continue;
                var u=units[s.BattalionIds[i]];
                bool mounted=u.Kind==UnitKind.Cavalry;
                if(InfantryContactMovement.Fleeing(u))
                {s.MeleeActions[i]=MeleeAction.Retreat;s.MeleeTargets[i]=-1;s.MeleeEnds[i]=now;s.StrikeResolved[i]=true;continue;}
                if(world.ContactMovement.BattleLines[u.Id]&&s.FormationSlots[i]>=FormationMath.EffectiveFiles(u))
                {s.MeleeActions[i]=MeleeAction.Ready;s.MeleeTargets[i]=-1;s.MeleeEnds[i]=now;s.StrikeResolved[i]=true;continue;}
                // An unfix order may arrive during a committed bayonet thrust.
                // Cancel that blade attack before its contact event; the next
                // attack uses the stock and keeps its own wind-up/contact timing.
                if(!mounted&&!s.PikeSergeants[i]&&!s.Captains[i]&&u.Role!=UnitRole.Swordsman
                    &&!u.BayonetsFixed&&s.MeleeActions[i]==MeleeAction.Thrust)
                {s.MeleeActions[i]=MeleeAction.Ready;s.MeleeEnds[i]=now;s.StrikeResolved[i]=true;}
                if(IsStrike(s.MeleeActions[i])&&!s.StrikeResolved[i]&&now>=s.MeleeStarts[i]+ImpactDelay(s.MeleeActions[i]))
                {
                    s.StrikeResolved[i]=true;int target=s.MeleeTargets[i];
                    float reach=s.PikeSergeants[i]?3.15f:mounted?3f:s.Captains[i]||u.Role==UnitRole.Swordsman?1.55f:u.BayonetsFixed?2.05f:1.25f;
                    if(target>=0&&s.Alive[target]&&!s.IsKnockedDown(target,now)&&CanResolveStrike(i,target,reach))
                    {
                        bool facing=Vector2.Dot(FormationMath.Forward(s.Facings[target]),(s.Positions[i]-s.Positions[target]).normalized)>.4f;
                        bool infantryExchange=!mounted&&units[s.BattalionIds[target]].Kind==UnitKind.Infantry
                            &&!InfantryContactMovement.Fleeing(units[s.BattalionIds[target]]);
                        float blockChance=facing&&CanParry(target)?infantryExchange?.50f:.24f:0;
                        bool block=RifleFireTiming.Variation(i+(int)(s.MeleeStarts[i]*31),347)<blockChance;
                        float damage=s.PikeSergeants[i]?51:s.Captains[i]||mounted?40: s.MeleeActions[i]==MeleeAction.ButtStrike?22:34;
                        if(units[s.BattalionIds[target]].Kind==UnitKind.Cavalry&&!mounted&&!s.Captains[i]&&u.Role!=UnitRole.Swordsman&&(u.BayonetsFixed||s.PikeSergeants[i]))damage*=1.5f;
                        if(!block)Queue(i,target,damage*u.BaseStats.Melee*(infantryExchange?.72f:1),Vector3.zero,false);
                        if(block){ParriedStrikes++;React(target,MeleeAction.Parry,i,infantryExchange?.95f:.65f);}else React(target,MeleeAction.Recoil,i,infantryExchange?.85f:.55f);
                        world.RecordMeleeContact(i,target,s.MeleeActions[i],block?MeleeContactOutcome.Parry:MeleeContactOutcome.Hit);
                        if(block&&infantryExchange)React(i,MeleeAction.Recoil,target,.65f);
                        ResolvedStrikes++;
                    }
                    else world.RecordMeleeContact(i,target,s.MeleeActions[i],MeleeContactOutcome.Miss);
                }
                if(s.MeleeEnds[i]>now)continue;
                if(!active[u.Id]||u.Routing||u.Cohesion<=.01f||(mounted&&u.ChargePhase==ChargePhase.Recovering)){s.MeleeActions[i]=u.Routing?MeleeAction.Retreat:MeleeAction.None;continue;}
                int previousTarget=s.MeleeTargets[i];
                int enemy=Nearest(i,s.PikeSergeants[i]?3f:mounted?3f:s.Captains[i]?1.4f:u.Role==UnitRole.Swordsman?1.25f:u.BayonetsFixed?1.85f:1.1f);
                if(enemy<0){s.MeleeTargets[i]=-1;s.MeleeActions[i]=MeleeAction.Ready;continue;}
                s.MeleeTargets[i]=enemy;
                // Each rider delivers at most one momentum hit in a charge cycle, before sustained sword combat.
                if(mounted&&!u.Routing&&(u.ChargePhase==ChargePhase.Approach||u.ChargePhase==ChargePhase.Impact)
                    &&s.ImpactCycles[i]!=u.ChargeCycle&&(u.ActualSpeed>3.5f||u.ImpactSpeed>3.5f))
                {
                    s.ImpactCycles[i]=u.ChargeCycle;
                    float brace=BraceStrength(world,enemy,s.Positions[i]);
                    float impactSpeed=Mathf.Max(u.ActualSpeed,u.ImpactSpeed);
                    if(!u.ChargeSpent){u.ImpactSpeed=impactSpeed;u.ChargeSpent=true;u.ChargePhase=ChargePhase.Impact;u.LastImpactTime=now;u.MountedSpeed*=.55f;var v=units[s.BattalionIds[enemy]];v.Morale-=Mathf.Lerp(12,2,brace);v.Cohesion=Mathf.Max(.3f,v.Cohesion-Mathf.Lerp(.12f,.01f,brace));v.MoraleReason="Cavalry impact";}
                    if(brace>.72f)
                    {
                        world.RecordMeleeContact(enemy,i,MeleeAction.Thrust,MeleeContactOutcome.Parry);
                        Queue(enemy,i,35,Vector3.zero,false);React(i,MeleeAction.Stumble,enemy,.9f);
                        world.RepelCavalry(u,(u.Position-units[s.BattalionIds[enemy]].Position).normalized);RepelledCharges++;continue;
                    }
                    float flank=Vector2.Dot(FormationMath.Forward(s.Facings[enemy]),(s.Positions[i]-s.Positions[enemy]).normalized)<.35f?1.35f:1;
                    float exposure=units[s.BattalionIds[enemy]].Formation==FormationKind.Skirmish?1.3f:1;
                    float damage=Mathf.Clamp(impactSpeed*impactSpeed*.85f,12,75)*(1-brace*.8f)*flank*exposure;
                    Vector2 direction=FormationMath.Forward(u.Facing);
                    Queue(i,enemy,Mathf.Min(95,damage),new Vector3(direction.x,1.4f,direction.y)*Mathf.Lerp(1,3.5f,1-brace),brace<.6f);
                    var impactAction=EmpireBattle.Art.NapoleonicOutfits.MountedWeapon(u.Loadout)=="lance"?MeleeAction.Thrust:MeleeAction.SlashRight;
                    world.RecordMeleeContact(i,enemy,impactAction,MeleeContactOutcome.Charge);
                    React(i,impactAction,enemy,.75f);ChargeImpacts++;continue;
                }
                // A standing infantry opponent answers a committed attack before
                // counterattacking, rather than both weapons passing through each other.
                bool exchange=!mounted&&units[s.BattalionIds[enemy]].Kind==UnitKind.Infantry
                    &&!InfantryContactMovement.Fleeing(units[s.BattalionIds[enemy]]);
                if(exchange&&previousTarget!=enemy)
                {
                    s.MeleeActions[i]=MeleeAction.Ready;
                    s.MeleeReadyTimes[i]=Mathf.Max(s.MeleeReadyTimes[i],now+Mathf.Lerp(.35f,.9f,RifleFireTiming.Variation(i,941)));
                }
                if(now<s.MeleeReadyTimes[i])continue;
                if(exchange&&IsStrike(s.MeleeActions[enemy])&&s.MeleeTargets[enemy]==i
                    &&s.MeleeEnds[enemy]>now&&!s.StrikeResolved[enemy])continue;
                Vector2 delta=s.Positions[enemy]-s.Positions[i];
                // Waiting front-rank attackers turn to a reachable opponent, while rear ranks do not attack through them.
                if(Vector2.Dot(FormationMath.Forward(s.Facings[i]),delta.normalized)<.45f){s.Facings[i]=FormationMath.TurnTowards(s.Facings[i],FormationMath.FacingOf(delta),dt*(mounted?.9f:2.8f));continue;}
                reservations[enemy]++;
                MeleeAction action=SelectAttack(s.PikeSergeants[i]||mounted&&EmpireBattle.Art.NapoleonicOutfits.MountedWeapon(u.Loadout)=="lance",mounted||s.Captains[i]||u.Role==UnitRole.Swordsman,
                    u.BayonetsFixed,Vector2.Dot(delta,FormationMath.Right(s.Facings[i])));
                s.MeleeActions[i]=action;s.MeleeStarts[i]=now;s.MeleeEnds[i]=now+AttackDuration(action);
                s.MeleeReadyTimes[i]=now+(exchange?Mathf.Lerp(2.8f,4.2f,RifleFireTiming.Variation(i,(int)(now*17)+91))
                    :Mathf.Lerp(1.3f,1.9f,RifleFireTiming.Variation(i,(int)(now*17)+91)));s.StrikeResolved[i]=false;
                if(exchange&&CanParry(enemy)&&(!IsStrike(s.MeleeActions[enemy])||s.MeleeEnds[enemy]<=now))
                {
                    s.MeleeTargets[enemy]=i;
                    s.MeleeActions[enemy]=MeleeAction.Ready;
                    s.MeleeReadyTimes[enemy]=Mathf.Max(s.MeleeReadyTimes[enemy],now+ImpactDelay(action)+.15f);
                }
                world.RecordMeleeContact(i,enemy,action,MeleeContactOutcome.Swing);
            }
            for(int h=0;h<hitCount;h++)
                world.ApplyMeleeHit(hitAttackers[h],hitTargets[h],hitDamage[h],hitImpulse[h],hitKnockdown[h]);
        }
        bool CanResolveStrike(int soldier,int target,float reach)
        {
            var s=world.Soldiers;Vector2 p=s.Positions[soldier],delta=s.Positions[target]-p;
            float length=delta.magnitude;if(length>reach)return false;
            Vector2 direction=delta/Mathf.Max(.001f,length);
            if(world.Battalions[s.BattalionIds[soldier]].Kind==UnitKind.Cavalry)return true;
            return !BlockedByAlly(soldier,Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.y/CellSize),p,direction,length);
        }
        float BoundsGap(int a,int b)
        {
            float x=Mathf.Max(0,Mathf.Max(boundsMin[a].x-boundsMax[b].x,boundsMin[b].x-boundsMax[a].x));
            float z=Mathf.Max(0,Mathf.Max(boundsMin[a].y-boundsMax[b].y,boundsMin[b].y-boundsMax[a].y));
            return Mathf.Sqrt(x*x+z*z);
        }
        int Nearest(int soldier,float reach)
        {
            var s=world.Soldiers;var u=world.Battalions[s.BattalionIds[soldier]];Vector2 p=s.Positions[soldier];int cx=Mathf.FloorToInt(p.x/CellSize),cz=Mathf.FloorToInt(p.y/CellSize);int best=-1;float distance=reach*reach;
            if(world.ContactMovement.BattleLines[u.Id]&&s.FormationSlots[soldier]>=FormationMath.EffectiveFiles(u))return -1;
            int pursued=world.ContactMovement.PursuitTargets[soldier];
            if(pursued>=0&&s.Alive[pursued]&&!s.IsKnockedDown(pursued,world.Time)&&reservations[pursued]<2&&CanResolveStrike(soldier,pursued,reach))return pursued;
            int opponent=s.MeleeTargets[soldier];
            if(opponent>=0&&s.Alive[opponent]&&!s.IsKnockedDown(opponent,world.Time)
                &&world.Battalions[s.BattalionIds[opponent]].Side!=u.Side&&reservations[opponent]<2
                &&CanResolveStrike(soldier,opponent,reach))return opponent;
            for(int x=cx-1;x<=cx+1;x++)for(int z=cz-1;z<=cz+1;z++)
            {
                if(!cells.TryGetValue(Cell(x,z),out int i))continue;
                for(;i>=0;i=next[i])
                {
                    if(!s.Alive[i]||s.IsKnockedDown(i,world.Time)||world.Battalions[s.BattalionIds[i]].Side==u.Side||reservations[i]>=2)continue;
                    float d=(s.Positions[i]-p).sqrMagnitude;
                    if(d>distance+.0001f||(Mathf.Abs(d-distance)<=.0001f&&best>=0&&i>best))continue;
                    // Reject a thrust through a closer ally in the same lane.
                    bool blocked=false;Vector2 path=s.Positions[i]-p;float length=path.magnitude;Vector2 direction=path/Mathf.Max(.001f,length);
                    if(u.Kind==UnitKind.Infantry)blocked=BlockedByAlly(soldier,cx,cz,p,direction,length);
                    if(!blocked){best=i;distance=d;}
                }
            }
            return best;
        }
        bool BlockedByAlly(int soldier,int cx,int cz,Vector2 p,Vector2 direction,float length)
        {
            var s=world.Soldiers;int unit=s.BattalionIds[soldier];
            // All possible blockers lie within the same three-metre contact neighbourhood.
            for(int x=cx-1;x<=cx+1;x++)for(int z=cz-1;z<=cz+1;z++)
            {
                if(!cells.TryGetValue(Cell(x,z),out int j))continue;
                for(;j>=0;j=next[j])
                {
                    if(j==soldier||s.BattalionIds[j]!=unit||!s.Alive[j]||s.IsKnockedDown(j,world.Time))continue;
                    Vector2 delta=s.Positions[j]-p;float along=Vector2.Dot(delta,direction);
                    if(along>.18f&&along<length-.25f&&Mathf.Abs(delta.x*direction.y-delta.y*direction.x)<.26f)return true;
                }
            }
            return false;
        }
        bool CanParry(int soldier)
        {
            var s=world.Soldiers;
            if(InfantryContactMovement.Fleeing(world.Battalions[s.BattalionIds[soldier]])||s.IsKnockedDown(soldier,world.Time))return false;
            if(s.MeleeEnds[soldier]<=world.Time)return true;
            var action=s.MeleeActions[soldier];
            if(action==MeleeAction.Stagger||action==MeleeAction.Recoil||action==MeleeAction.Stumble)return false;
            return !IsStrike(action)||s.StrikeResolved[soldier];
        }
        void React(int soldier,MeleeAction action,int target,float duration)
        {
            var s=world.Soldiers;
            if(InfantryContactMovement.Fleeing(world.Battalions[s.BattalionIds[soldier]]))return;
            if(IsStrike(s.MeleeActions[soldier])&&s.MeleeEnds[soldier]>world.Time&&!s.StrikeResolved[soldier])return;
            s.MeleeTargets[soldier]=target;s.MeleeActions[soldier]=action;s.MeleeStarts[soldier]=world.Time;s.MeleeEnds[soldier]=world.Time+duration;s.StrikeResolved[soldier]=true;
        }
        void Queue(int attacker,int target,float damage,Vector3 impulse,bool knock)
        {
            if(hitCount>=hitTargets.Length)return;hitAttackers[hitCount]=attacker;hitTargets[hitCount]=target;hitDamage[hitCount]=damage;hitImpulse[hitCount]=impulse;hitKnockdown[hitCount++]=knock;
        }
        public static MeleeAction SelectAttack(bool pike,bool sword,bool bayonetFixed,float targetSide)
        {
            if(pike)return MeleeAction.Thrust;
            if(sword)return targetSide<0?MeleeAction.SlashLeft:MeleeAction.SlashRight;
            return bayonetFixed?MeleeAction.Thrust:MeleeAction.ButtStrike;
        }
        public static bool IsStrike(MeleeAction action)=>action==MeleeAction.Thrust||action==MeleeAction.ButtStrike||action==MeleeAction.SlashLeft||action==MeleeAction.SlashRight;
        public static float AttackDuration(MeleeAction action)=>action==MeleeAction.Thrust?1.65f:action==MeleeAction.ButtStrike?1.35f:.95f;
        public static float ImpactDelay(MeleeAction action)=>action==MeleeAction.Thrust?.78f:action==MeleeAction.ButtStrike?.58f:.43f;
    }
}
