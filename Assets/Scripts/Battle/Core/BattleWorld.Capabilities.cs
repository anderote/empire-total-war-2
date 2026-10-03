using UnityEngine;

namespace EmpireBattle
{
    public static partial class UnitEquipment
    {
        public static EquipmentUsers BattleUsers(BattalionState unit)=>unit.Kind==UnitKind.Cavalry?EquipmentUsers.Cavalry:
            unit.Role==UnitRole.Swordsman?EquipmentUsers.Melee:unit.Role==UnitRole.Militia?EquipmentUsers.Militia:
            unit.Role==UnitRole.Rifleman?EquipmentUsers.Rifle:UnitRoster.Light(unit.Role)?EquipmentUsers.Light:EquipmentUsers.Line;
    }
    public sealed partial class BattleWorld
    {
        public bool ResistRout(BattalionState unit)
        {
            if(unit==null||unit.AliveCount<=0||unit.Routing||!unit.Progression.Has(RegimentAbility.LastStand))return false;
            if(!unit.LastStandUsed){unit.LastStandUsed=true;unit.LastStandUntil=Time+8+4*unit.Progression.AbilityRank(RegimentAbility.LastStand);}
            if(unit.LastStandUntil<=Time)return false;unit.MoraleReason="Last stand";return true;
        }
        public float EffectiveRange(BattalionState unit)=>(unit.Weapon?.EffectiveRange??Config.MusketRange)*(1+unit.Kit.range);
        public float AimedRange(BattalionState unit)=>(unit.Weapon?.AimedRange??WeaponBallistics.MaximumAimedRange)*(1+unit.Kit.range);
        public bool CanFixBayonets(BattalionState unit)=>unit!=null&&unit.Kind==UnitKind.Infantry&&UnitRoster.Shoots(unit.Role);
        public bool SetBayonets(int id,bool fixedBayonets)
        {
            if(!Valid(id))return false;
            var unit=Battalions[id];
            if(!CanFixBayonets(unit)||unit.Routing||unit.AliveCount<=0)return false;
            if(unit.BayonetsFixed==fixedBayonets)return true;
            float reloadScale=fixedBayonets?1.25f:.8f;
            // Preserve reload progress when the order changes during a reload.
            for(int soldier=unit.SoldierStart;soldier<unit.SoldierStart+unit.SoldierCount;soldier++)
                if(Soldiers.Alive[soldier]&&Soldiers.ReloadReadyTimes[soldier]>Time)
                {
                    Soldiers.ReloadReadyTimes[soldier]=Time+(Soldiers.ReloadReadyTimes[soldier]-Time)*reloadScale;
                    Soldiers.ReloadDurations[soldier]*=reloadScale;
                }
            unit.ReloadRemaining*=reloadScale;
            if(!unit.FireCycleActive)
            {
                if(unit.NextFireTime>Time)unit.NextFireTime=Time+(unit.NextFireTime-Time)*reloadScale;
                if(unit.FireCycleReadyTime>Time)unit.FireCycleReadyTime=Time+(unit.FireCycleReadyTime-Time)*reloadScale;
            }
            // Reversing a still-playing handling action preserves its current pose.
            float progress=BayonetPresentationTiming.Progress(unit,Time);
            unit.BayonetOrderInitialPhase=Time>=unit.BayonetOrderTime&&progress<1?1-progress:0;
            unit.BayonetsFixed=fixedBayonets;
            unit.BayonetOrderTime=Time;
            if(!fixedBayonets)unit.Bracing=false;
            return true;
        }
        public float BayonetMeleeMultiplier(BattalionState unit)=>CanFixBayonets(unit)?unit.BayonetsFixed?1.5f:.75f:1f;
        public float BaseReload(BattalionState unit)=>(unit.Weapon?.Reload??Config.ReloadSeconds)*ReloadModifier(unit)*(CanFixBayonets(unit)&&unit.BayonetsFixed?1.25f:1f);
        public float ReloadModifier(BattalionState unit)=>Mathf.Clamp(unit.BaseStats.Reload*unit.Progression.ReloadMultiplier*(1-unit.EquipmentQuality*.5f)*(1+unit.Kit.reload)*(unit.Progression.Has(RegimentAbility.QuickHands)&&unit.Ammunition<=unit.StartingAmmunition*.25f?Mathf.Max(.2f,1-(.15f+.10f*unit.Progression.AbilityRank(RegimentAbility.QuickHands))):1),.2f,1.5f);
        public float Precision(BattalionState unit)=>(1f-.6f*Mathf.Clamp01(Mathf.Max(unit.BattleLimeExposure,Time<unit.LimeExposureUntil?unit.LimeObscuration:0f)))*(unit.Weapon?.Precision??1)*(1+unit.EquipmentQuality)*unit.BaseStats.Accuracy*unit.Progression.AccuracyMultiplier*(1+unit.Kit.accuracy)*(unit.Progression.Has(RegimentAbility.SteadyAim)&&!unit.Moving&&!unit.InMelee?1.10f+.05f*unit.Progression.AbilityRank(RegimentAbility.SteadyAim):1);
        public float Resolve(BattalionState unit)=>unit.BaseStats.Resolve*unit.Progression.ResolveMultiplier*(1+unit.Kit.resolve)*(unit.Progression.Has(RegimentAbility.SquareDiscipline)&&unit.Formation==FormationKind.Square?1.20f+.15f*unit.Progression.AbilityRank(RegimentAbility.SquareDiscipline):1);
        public float FatigueRate(BattalionState unit)=>Mathf.Clamp(unit.Progression.FatigueMultiplier*(1+unit.Kit.fatigue
            +(Config.Map?.Aridity??0)*unit.Kit.heatFatigue+(Config.Map?.Snow??0)*unit.Kit.coldFatigue)*(unit.Progression.Has(RegimentAbility.HardyMarch)&&unit.Moving&&!unit.Running&&!unit.Routing?Mathf.Max(.2f,1-(.25f+.10f*unit.Progression.AbilityRank(RegimentAbility.HardyMarch))):1),.15f,1.6f);
        public bool CanForm(BattalionState unit,FormationKind formation)
        {
            if(unit==null||!System.Enum.IsDefined(typeof(FormationKind),formation))return false;
            if(formation==FormationKind.Diamond)return unit.Kind==UnitKind.Cavalry;
            if(formation==FormationKind.Square)return unit.Kind==UnitKind.Infantry&&unit.Role!=UnitRole.Swordsman&&MilitaryEquipment.Has(unit.Doctrine,MilitaryDoctrine.Square);
            if(formation==FormationKind.Skirmish)return unit.Kind==UnitKind.Cavalry||(unit.Kind==UnitKind.Infantry&&MilitaryEquipment.Has(unit.Doctrine,MilitaryDoctrine.LightInfantry));
            return true;
        }
        public bool CanFireMode(BattalionState unit,InfantryFireMode mode)=>UnitRoster.HasFirearm(unit)&&System.Enum.IsDefined(typeof(InfantryFireMode),mode)&&(unit.Kind!=UnitKind.Cavalry||mode==InfantryFireMode.FireAtWill);
        public InfantryFireMode NextFireMode(BattalionState unit)=>unit.Kind==UnitKind.Cavalry?InfantryFireMode.FireAtWill:RifleFireTiming.NormalizeMode(unit.FireMode)==InfantryFireMode.FireAtWill?InfantryFireMode.ByPlatoon:
            RifleFireTiming.NormalizeMode(unit.FireMode)==InfantryFireMode.ByPlatoon?InfantryFireMode.Volley:InfantryFireMode.FireAtWill;
        public bool Ignites(BattalionState unit,int soldier)
        {
            if(unit.Weapon==null)return true;
            float probability=Mathf.Clamp01(MilitaryEquipment.MisfireChance(unit.Weapon.Id,Config.WeaponWetness)*(1-unit.EquipmentQuality)+unit.Kit.misfire*Config.WeaponWetness);
            bool fired=RifleFireTiming.Variation(soldier,Config.Seed^Mathf.RoundToInt(Time*100))>=probability;
            if(!fired)unit.Misfires++;return fired;
        }
        public void RefreshAmmunition(BattalionState unit)
        {
            if(unit.Weapon==null)return;
            int remaining=0;for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                if(Soldiers.Alive[i])remaining=Mathf.Max(remaining,Soldiers.RoundsRemaining[i]);
            unit.Ammunition=remaining;
        }
        public float ReloadAfterShot(BattalionState unit,int soldier)
        {
            if(unit.Weapon==null||unit.Weapon.Magazine<=1)return BaseReload(unit);
            // RoundsRemaining is total reserve. Magazine replenishment never creates ammunition.
            if(Soldiers.MagazineRounds[soldier]<=0)Soldiers.MagazineRounds[soldier]=Mathf.Min(unit.Weapon.Magazine,Soldiers.RoundsRemaining[soldier]+1);
            Soldiers.MagazineRounds[soldier]--;
            if(Soldiers.MagazineRounds[soldier]>0)return unit.Weapon.Cycle*ReloadModifier(unit);
            Soldiers.MagazineRounds[soldier]=Mathf.Min(unit.Weapon.Magazine,Soldiers.RoundsRemaining[soldier]);
            return BaseReload(unit);
        }
    }
}
