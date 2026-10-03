using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireBattle
{
    /// <summary>Deterministic, previewable point spending for the Regiment Manager.</summary>
    public sealed class RegimentAutoAllocationPlan
    {
        public readonly UnitSkill[] Stats;
        public readonly RegimentAbility[] Abilities;
        public bool HasWork => Stats.Length != 0 || Abilities.Length != 0;
        public RegimentAutoAllocationPlan(IEnumerable<UnitSkill> stats,IEnumerable<RegimentAbility> abilities)
        { Stats=stats.ToArray(); Abilities=abilities.ToArray(); }
    }

    public static class RegimentAutoAllocation
    {
        static UnitSkill[] SkillOrder(UnitRole role)
        {
            if(role==UnitRole.Cavalry) return new[]{UnitSkill.Melee,UnitSkill.Endurance,UnitSkill.Discipline,UnitSkill.Health};
            if(role==UnitRole.Swordsman) return new[]{UnitSkill.Melee,UnitSkill.Health,UnitSkill.Discipline,UnitSkill.Endurance};
            if(role==UnitRole.Rifleman||role==UnitRole.Skirmisher) return new[]{UnitSkill.Accuracy,UnitSkill.Drill,UnitSkill.Ammunition,UnitSkill.Endurance,UnitSkill.Discipline,UnitSkill.Health,UnitSkill.Melee};
            if(role==UnitRole.Guard) return new[]{UnitSkill.Discipline,UnitSkill.Accuracy,UnitSkill.Melee,UnitSkill.Health,UnitSkill.Drill,UnitSkill.Endurance,UnitSkill.Ammunition};
            return new[]{UnitSkill.Accuracy,UnitSkill.Drill,UnitSkill.Discipline,UnitSkill.Health,UnitSkill.Endurance,UnitSkill.Ammunition,UnitSkill.Melee};
        }
        static RegimentAbility[] AbilityOrder(UnitRole role,EquipmentUsers users)
        {
            RegimentAbility[] order=users==EquipmentUsers.Artillery
                ?new[]{RegimentAbility.ArtilleryMunitions,RegimentAbility.SteadyAim,RegimentAbility.AmmunitionReserve,RegimentAbility.QuickHands,RegimentAbility.RallyingSpirit,RegimentAbility.IronConstitution,RegimentAbility.LastStand}
                :role==UnitRole.Cavalry
                    ?new[]{RegimentAbility.AssaultTraining,RegimentAbility.HardyMarch,RegimentAbility.FieldMedicine,RegimentAbility.WeaponMastery,RegimentAbility.IronConstitution,RegimentAbility.RallyingSpirit,RegimentAbility.LastStand}
                    :new[]{RegimentAbility.SteadyAim,RegimentAbility.SquareDiscipline,RegimentAbility.AssaultTraining,RegimentAbility.FieldMedicine,RegimentAbility.QuickHands,RegimentAbility.AmmunitionReserve,RegimentAbility.HardyMarch,RegimentAbility.IronConstitution,RegimentAbility.RallyingSpirit,RegimentAbility.LastStand,RegimentAbility.GrenadierCadre,RegimentAbility.WeaponMastery};
            return order.Where(a=>RegimentAbilities.Find(a).Supports(users)).ToArray();
        }

        // Stat and ability points are independent budgets. Existing allocations are
        // never moved; each plan spends only currently available points.
        public static RegimentAutoAllocationPlan Plan(UnitProgression progression,UnitRole role,EquipmentUsers users,Func<UnitSkill,bool> usefulSkill=null)
        {
            if(progression==null) return new RegimentAutoAllocationPlan(Array.Empty<UnitSkill>(),Array.Empty<RegimentAbility>());
            var stats=new List<UnitSkill>();var skillOrder=SkillOrder(role).Where(s=>(usefulSkill==null||usefulSkill(s))&&progression.Level(s)<UnitProgression.MaxSkill).ToArray();
            int points=progression.AvailablePoints;
            while(points>0&&skillOrder.Length>0)
            {
                bool spent=false;
                foreach(var skill in skillOrder)
                {
                    if(points==0)break;
                    int level=progression.Level(skill)+stats.Count(s=>s==skill);
                    if(level>=UnitProgression.MaxSkill)continue;
                    stats.Add(skill);points--;spent=true;
                }
                if(!spent)break;
            }
            var abilities=new List<RegimentAbility>();var abilityOrder=AbilityOrder(role,users);int abilityPoints=progression.AvailableSkillPoints;
            while(abilityPoints>0)
            {
                bool spent=false;
                foreach(var ability in abilityOrder)
                {
                    if(abilityPoints==0)break;
                    var definition=RegimentAbilities.Find(ability);int rank=progression.AbilityRank(ability)+abilities.Count(a=>a==ability);
                    if(rank>=RegimentAbilities.MaxRank||progression.UnitLevel<definition.RequiredLevel(rank+1))continue;
                    abilities.Add(ability);abilityPoints--;spent=true;
                }
                if(!spent)break;
            }
            return new RegimentAutoAllocationPlan(stats,abilities);
        }
    }
}
