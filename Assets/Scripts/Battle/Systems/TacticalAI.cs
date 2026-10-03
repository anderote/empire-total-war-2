using Unity.Profiling;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Opposing army orders, with optional encounter-specific tactical plans.</summary>
    public sealed class TacticalAI
    {
        static readonly ProfilerMarker PerformanceMarker = new ProfilerMarker("Battle.AI");
        private readonly BattleWorld world;
        private float thinkRemaining;
        private bool reservesCommitted;
        private readonly bool[] flanksReached;

        public TacticalAI(BattleWorld world) { this.world = world; flanksReached=new bool[world.Battalions.Length]; }

        public void Step(float dt)
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.AI);
            using var performanceScope = PerformanceMarker.Auto();
            if (!world.Config.EnableAI || !world.CanSimulate || dt <= 0f) return;
            thinkRemaining -= dt;
            if (thinkRemaining > 0f) return;
            thinkRemaining = 1f;
            int engaged = 0;
            int active = 0;
            foreach (BattalionState unit in world.Battalions)
            {
                if (unit.Side != ArmySide.British || unit.Routing || unit.AliveCount <= 0) continue;
                active++;
                if (world.Time - unit.LastVolleyTime < 25f || unit.Morale < 60f) engaged++;
            }
            var tactics=world.Config.EnemyTactics;
            if (tactics==ArmyTactics.Assault||tactics==ArmyTactics.Envelop||world.Time > 180f || engaged >= Mathf.Max(1, active / 2)) reservesCommitted = true;
            int ordinal = 0;
            foreach (BattalionState unit in world.Battalions)
            {
                if (unit.Side != ArmySide.British) continue;
                bool isReserve = ordinal++ >= Mathf.CeilToInt(world.Config.BritishInfantryCount * 0.75f);
                if (unit.Routing || unit.AliveCount <= 0 || unit.Withdrawing) continue;
                // Do not cancel a committed Glory assault with a fresh firing-line order at contact.
                if(world.Config.RegimentalDeployment&&unit.InMelee&&unit.AttackMode==InfantryAttackMode.BayonetCharge)continue;
                if(world.Config.ScenarioId=="hammer-anvil")
                {
                    unit.FireAtWill=true;
                    if(unit.Position.y>-80)world.IssueMove(unit.Id,new Vector2(unit.Position.x,Mathf.Max(-80,unit.Position.y-80)),Mathf.PI,preserveRoute:true);
                    else world.Halt(unit.Id);
                    continue;
                }
                BattalionState enemy = NearestEnemy(unit);
                if(world.Config.RegimentalDeployment&&GloryTownOrders(unit,enemy))continue;
                if(unit.Kind==UnitKind.Cavalry)
                {
                    BattalionState victim=null;float score=float.NegativeInfinity;
                    foreach(var candidate in world.Battalions)
                    {
                        if(candidate.Side==unit.Side||candidate.AliveCount<=0)continue;
                        float value=-(candidate.Position-unit.Position).magnitude+(candidate.Routing?100:candidate.Formation==FormationKind.Skirmish?60:0)-(candidate.Formation==FormationKind.Square&&!candidate.Reforming?100:0);
                        if(value>score){score=value;victim=candidate;}
                    }
                    if(victim!=null&&UnitRoster.HasFirearm(unit)&&unit.Ammunition>0&&!unit.InMelee&&!victim.Routing&&(victim.Position-unit.Position).sqrMagnitude>14*14)
                    {unit.MeleeMode=false;unit.PursueTargets=true;world.IssueRangedAttack(unit.Id,victim.Id);continue;}
                    if(victim!=null&&!FlankOrders(unit,victim))world.IssueCharge(unit.Id,victim.Id);
                    continue;
                }
                if(world.Siege!=null)
                {
                    var map=world.Terrain.Profile;
                    if(unit.Side==world.Siege.Defender)
                    {
                        world.Halt(unit.Id);unit.FireAtWill=true;
                        if(enemy!=null)unit.DesiredFacing=FormationMath.FacingOf(enemy.Position-unit.Position);
                        continue;
                    }
                    if(enemy==null||(unit.Position-map.Gate).sqrMagnitude<280*280||world.CountEffective(world.Siege.Defender)==0)
                    {
                        unit.FireAtWill=true;
                        world.IssueMove(unit.Id,map.SettlementCenter,FormationMath.FacingOf(map.SettlementCenter-unit.Position),preserveRoute:true);
                        continue;
                    }
                }
                if (enemy == null) continue;
                Vector2 delta = enemy.Position - unit.Position;
                float distance = delta.magnitude;
                float facing = Mathf.Atan2(delta.x, delta.y);
                if(tactics==ArmyTactics.HoldGround&&distance>world.EffectiveRange(unit)*.95f&&world.Time-unit.LastIncomingFireTime>10)
                {world.Halt(unit.Id);unit.DesiredFacing=facing;unit.FireAtWill=UnitRoster.Shoots(unit.Role);continue;}
                if(!UnitRoster.Shoots(unit.Role)){world.IssueCharge(unit.Id,enemy.Id);continue;}
                float desiredRange = Mathf.Max(15f, world.EffectiveRange(unit) * (tactics==ArmyTactics.Skirmish?.92f:tactics==ArmyTactics.Assault?.58f:UnitRoster.Light(unit.Role)?.85f:.72f));
                if (isReserve && !reservesCommitted && distance > world.EffectiveRange(unit) * 0.9f) continue;
                bool mountedThreat=false;foreach(var threat in world.Battalions)if(threat.Side!=unit.Side&&threat.Kind==UnitKind.Cavalry&&threat.AliveCount>0&&!threat.Routing&&(threat.Position-unit.Position).sqrMagnitude<70*70){mountedThreat=true;break;}
                if(!world.Config.PreserveInitialFormations&&mountedThreat&&unit.Morale>40&&unit.AliveCount>=48&&world.CanForm(unit,FormationKind.Square)){world.Halt(unit.Id);world.SetFormation(unit.Id,FormationKind.Square);continue;}
                var formation=world.Config.PreserveInitialFormations?unit.Formation:UnitRoster.Light(unit.Role)&&world.CanForm(unit,FormationKind.Skirmish)?FormationKind.Skirmish:FormationKind.Line;
                if (unit.Formation != formation) world.SetFormation(unit.Id, formation);
                unit.FireAtWill = true;
                if(tactics==ArmyTactics.Assault&&!mountedThreat&&!unit.InMelee&&!UnitRoster.Light(unit.Role)&&distance<45&&unit.Morale>55)
                {world.IssueCharge(unit.Id,enemy.Id);continue;}
                if(UnitRoster.Light(unit.Role)&&!mountedThreat&&!unit.InMelee&&distance<(tactics==ArmyTactics.Skirmish?world.EffectiveRange(unit)*.65f:70f))
                {world.IssueMove(unit.Id,unit.Position-delta.normalized*25,facing,preserveRoute:true);continue;}
                if (distance <= desiredRange + 8f)
                {
                    world.Halt(unit.Id);
                    unit.DesiredFacing = facing;
                }
                else
                {
                    // Preserve each unit's lane instead of converging all formations onto one point.
                    Vector2 direction = delta.normalized;
                    Vector2 destination = unit.Position + direction * Mathf.Min(80f, distance - desiredRange);
                    world.IssueMove(unit.Id, destination, facing, preserveRoute:true);
                }
            }
        }

        // Town missions use the existing contested capture rule, including for mounted attackers.
        bool GloryTownOrders(BattalionState unit,BattalionState enemy)
        {
            if(world.Siege==null)return false;
            var map=world.Terrain.Profile;
            if(unit.Side==world.Siege.Defender)
            {
                if(enemy!=null&&(enemy.Position-map.SettlementCenter).sqrMagnitude<150*150&&(unit.Position-map.SettlementCenter).sqrMagnitude>50*50)
                    world.IssueMove(unit.Id,map.SettlementCenter,FormationMath.FacingOf(map.SettlementCenter-unit.Position),preserveRoute:true);
                else if(enemy!=null&&!UnitRoster.Shoots(unit.Role)&&(enemy.Position-unit.Position).sqrMagnitude<80*80)
                    world.IssueCharge(unit.Id,enemy.Id);
                else {world.Halt(unit.Id);unit.FireAtWill=UnitRoster.Shoots(unit.Role);if(enemy!=null)unit.DesiredFacing=FormationMath.FacingOf(enemy.Position-unit.Position);}
                return true;
            }
            if(enemy==null||(unit.Position-map.Gate).sqrMagnitude<280*280)
            {world.IssueMove(unit.Id,map.SettlementCenter,FormationMath.FacingOf(map.SettlementCenter-unit.Position),preserveRoute:true);return true;}
            return false;
        }
        bool FlankOrders(BattalionState unit,BattalionState enemy)
        {
            if(world.Config.EnemyTactics!=ArmyTactics.Envelop||flanksReached[unit.Id]||unit.InMelee)return false;
            // A limited approach phase prevents cavalry orbiting indefinitely when targets move.
            if(world.Time>120||(enemy.Position-unit.Position).sqrMagnitude<95*95){flanksReached[unit.Id]=true;return false;}
            int index=unit.Id-world.Config.FrenchBattalionCount-world.Config.BritishInfantryCount;
            float wing=index%2==0?-1:1;
            float half=world.Config.FieldHalfSize;
            var approach=new Vector2(Mathf.Clamp(enemy.Position.x+wing*(180+index%3*25),world.Config.Map?.Coast==true?-half*.65f:-half+100,half-100),enemy.Position.y+70);
            if((unit.Position-approach).sqrMagnitude<35*35){flanksReached[unit.Id]=true;return false;}
            world.IssueMove(unit.Id,approach,FormationMath.FacingOf(approach-unit.Position),preserveRoute:true);return true;
        }

        private BattalionState NearestEnemy(BattalionState unit)
        {
            BattalionState nearest = null;
            float distanceSquared = float.PositiveInfinity;
            foreach (BattalionState enemy in world.Battalions)
            {
                if (enemy.Side == unit.Side || enemy.Routing || enemy.AliveCount <= 0) continue;
                float candidate = (enemy.Position - unit.Position).sqrMagnitude;
                if (candidate >= distanceSquared) continue;
                nearest = enemy;
                distanceSquared = candidate;
            }
            return nearest;
        }
    }
}
