using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        // An abstract infantry engagement, not a replay of the tactical battlefield.
        // All randomness uses the saved campaign PRNG; previewing never consumes it.
        sealed class AutoFormation
        {
            public CampaignRegiment unit;
            public int initial,men,rounds;
            public float power,resolve,breakLoss,kills;
            public bool routed;
            public float FightingPower=>routed?0:men*power;
        }
        sealed class AutoResult
        {
            public bool attackerWon;
            public readonly Dictionary<string,int> survivors=new Dictionary<string,int>(),xp=new Dictionary<string,int>(),kills=new Dictionary<string,int>();
        }
        List<AutoFormation> AutoFormations(CampaignEncounter e,bool attacker)
        {
            return BattleUnits(e,attacker).OrderBy(u=>u.id,StringComparer.Ordinal).Select(u=>
            {
                var army=Save.armies.FirstOrDefault(a=>a.units.Contains(u));
                float supply=army?.supply??1,command=CommandBonus(army);
                var kit=u.loadout.Modifiers;
                return new AutoFormation {unit=u,initial=u.soldiers,men=u.soldiers,
                    power=RegimentPower(u)*Math.Max(.4f,supply)*command*FieldReadiness(e,army),
                    resolve=(u.role=="Militia"?.8f:1)*u.progression.ResolveMultiplier*
                        (1+kit.resolve+kit.moraleReserve*.01f)*(.7f+.3f*supply)*command*(army?.commanderTrait=="Inspiring"&&army.commanderWounds==0?1.1f:1)};
            }).ToList();
        }
        float AutoFortProtection(CampaignEncounter e)=>!e.fieldOnly&&World.State(e.regionId).owner==e.defender?1+Buildings(e.regionId,"Fortress")*.22f*Math.Max(.25f,1-e.siegeTurns*.2f):1;
        public float AutoresolveAttackerShare(CampaignEncounter e)
        {
            if(e==null)return .5f;
            float attack=AutoFormations(e,true).Sum(u=>u.FightingPower);
            float defence=AutoFormations(e,false).Sum(u=>u.FightingPower)*AutoFortProtection(e);
            return attack/Math.Max(1,attack+defence);
        }
        AutoResult SimulateAutoresolve(CampaignEncounter e)
        {
            var attack=AutoFormations(e,true);var defence=AutoFormations(e,false);
            foreach(var unit in attack.Concat(defence))unit.breakLoss=Math.Max(.22f,Math.Min(.72f,.34f*unit.resolve+Random01()*.12f));
            float protection=AutoFortProtection(e);
            for(int round=0;round<40;round++)
            {
                float ap=attack.Sum(u=>u.FightingPower),dp=defence.Sum(u=>u.FightingPower);
                if(ap<=0||dp<=0)break;
                foreach(var u in attack.Concat(defence).Where(u=>!u.routed&&u.men>0))u.rounds++;
                // Compute both volleys before applying either. Numbers, quality and supply
                // determine firepower; fortifications reduce incoming defender losses.
                float damageToDefence=ap*.065f*(.8f+Random01()*.4f)/protection;
                float damageToAttack=dp*.065f*(.8f+Random01()*.4f);
                var attackShares=attack.Select(u=>u.FightingPower/ap).ToArray();
                var defenceShares=defence.Select(u=>u.FightingPower/dp).ToArray();
                int lostDefence=AutoLosses(defence,damageToDefence),lostAttack=AutoLosses(attack,damageToAttack);
                for(int i=0;i<attack.Count;i++)attack[i].kills+=lostDefence*attackShares[i];
                for(int i=0;i<defence.Count;i++)defence[i].kills+=lostAttack*defenceShares[i];
                foreach(var u in attack.Concat(defence))
                    if(u.men==0||1-u.men/(float)u.initial>=u.breakLoss)u.routed=true;
            }
            float remainingAttack=attack.Sum(u=>u.FightingPower),remainingDefence=defence.Sum(u=>u.FightingPower)*protection;
            // At the round limit compare remaining fighting strength. Ties and mutually
            // routed assaults leave the defender in possession.
            bool won=remainingAttack>0&&remainingAttack>remainingDefence;
            var losers=won?defence:attack;
            foreach(var u in losers.Where(u=>u.men>0))
            {
                int lost=(int)Math.Floor(u.men*(.04f+Random01()*.08f)/Math.Max(.8f,u.resolve));
                u.men=Math.Max(0,u.men-lost);
            }
            var result=new AutoResult {attackerWon=won};
            foreach(var u in attack.Concat(defence))
            {
                result.survivors[u.unit.id]=u.men;
                result.kills[u.unit.id]=Math.Max(0,(int)u.kills);
                result.xp[u.unit.id]=u.rounds==0?0:UnitProgression.BattleXP(u.initial,u.men,(int)u.kills,0,u.rounds,attack.Contains(u)==won);
            }
            return result;
        }
        int AutoLosses(List<AutoFormation> targets,float damage)
        {
            // Share casualties across engaged formations, with a small exposure variation.
            // Quality offers limited protection; it cannot make a regiment invulnerable.
            var weights=targets.Select(u=>u.routed?0:u.men/(float)Math.Sqrt(Math.Max(.4f,u.power))*(.85f+Random01()*.3f)).ToArray();
            float total=weights.Sum();if(total<=0)return 0;
            int losses=0;
            for(int i=0;i<targets.Count;i++)
            {
                if(weights[i]<=0)continue;
                float expected=damage*weights[i]/total;
                int lost=Math.Min(targets[i].men,(int)Math.Floor(expected)+(Random01()<expected-Math.Floor(expected)?1:0));
                targets[i].men-=lost;losses+=lost;
            }
            return losses;
        }
    }
}
