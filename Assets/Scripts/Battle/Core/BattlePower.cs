using System;
using System.Linq;

namespace EmpireBattle
{
    // Comparative starting strength, not a prediction of the battle result.
    // One fresh standard line musketeer is one point; totals are additive.
    public static class BattlePower
    {
        public const string Help="Estimated starting strength from numbers, troop class, weapons, quality, trained stats, abilities and worn kit. One standard line musketeer = 1 BP. Terrain, tactics, counters, current wounds and morale can change the outcome.";
        static double Clamp(double n,double lo,double hi)=>Math.Max(lo,Math.Min(hi,n));
        public static double PerTroop(UnitRole role,int tier,int quality,UnitProgression progress,UnitLoadout loadout,string weapon="musket",bool artillery=false)
        {
            var p=progress??new UnitProgression();var kit=loadout??new UnitLoadout();var m=kit.Modifiers;
            var b=RegimentBaseStats.For(tier,role);double q=Clamp(quality,0,UnitProgression.MaxQuality);
            var issued=kit.items?.Select(UnitEquipment.Find).FirstOrDefault(i=>i!=null&&i.Weapon!="");
            var gun=MilitaryEquipment.Weapon(issued?.Weapon??weapon)??MilitaryEquipment.Weapon("musket");
            double reload=Clamp(b.Reload*p.ReloadMultiplier*(1-q*.02)*(1+m.reload),.2,1.5);
            // Magazine cycle includes the amortized full reload, rather than treating every shot as a reload.
            double interval=gun.Magazine>1?gun.Cycle+gun.Reload/gun.Magazine:gun.Reload;
            double shooting=b.Accuracy*p.AccuracyMultiplier*(1+q*.04)*(1+m.accuracy)/reload;
            if(!artillery)shooting*=gun.Precision*gun.DamageMultiplier*15/interval*Math.Sqrt(gun.EffectiveRange*(1+m.range)/150);
            double melee=b.Melee*p.MeleeMultiplier*(1+q*.04)*(1+m.melee);
            double attack=artillery?shooting:UnitRoster.Shoots(role)?.8*shooting+.2*melee:melee+(role==UnitRole.Cavalry&&UnitRoster.HasFirearm(role,kit)?.25*shooting:0);
            double health=artillery?1:(b.Health+p.MaxHealth-100)/100;
            double morale=(b.Morale+p.MaxMorale-100+m.moraleReserve)/100;
            double resolve=b.Resolve*p.ResolveMultiplier*(1+m.resolve);
            double endurance=1/Math.Sqrt(Clamp(p.FatigueMultiplier*(1+m.fatigue),.15,1.6));
            double mobility=Math.Sqrt(Math.Max(.1,b.Speed*(1+m.speed)));
            double ammo=1+Math.Min(.3,Math.Max(0,m.ammunition+(artillery?p.ArtilleryAmmunitionBonus:p.AmmunitionBonus(gun)))*.003);
            // Tactical abilities receive a modest allowance; direct health/melee/morale effects are already included.
            int tactical=p.abilities?.Count(a=>a!=RegimentAbility.IronConstitution&&a!=RegimentAbility.RallyingSpirit&&a!=RegimentAbility.WeaponMastery&&a!=RegimentAbility.AmmunitionReserve)??0;
            double utility=ammo*(1+Math.Min(.4,tactical*.025))*(1+Math.Min(.15,p.Grenades(m.grenades)*.005));
            return (artillery?35:role==UnitRole.Cavalry?1.8:1)*Math.Sqrt(Math.Max(.01,attack*health))*Math.Pow(Math.Max(.1,morale*resolve),.25)*Math.Pow(endurance*mobility,.25)*utility;
        }
        public static double Total(int strength,UnitRole role,int tier,int quality,UnitProgression progress,UnitLoadout loadout,string weapon="musket",bool artillery=false)
            =>Math.Max(0,strength)*PerTroop(role,tier,quality,progress,loadout,weapon,artillery);
    }
}
