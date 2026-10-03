using System;
using System.Linq;
using EmpireBattle;
using EmpireCampaign.Geography;

public static class AutoresolveChecks
{
    public static void Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        CountryCampaign Prepare(uint seed,int attackerStrength=500,int defenderStrength=500,bool veterans=false,int fort=0)
        {
            var c=fresh();c.Save.randomState=seed;
            var route=c.Owned("france").SelectMany(h=>c.Owned("austria").Select(t=>c.Route("france",h.regionId,t.regionId)))
                .Where(r=>r!=null).OrderBy(r=>r.distance).First();
            var a=c.Save.armies.First(x=>x.owner=="france");var d=c.Save.armies.First(x=>x.owner=="austria");
            string target=route.regions.Last();a.regionId=route.regions[route.regions.Count-2];a.movement=2000;
            foreach(var other in c.Save.armies.Where(x=>x.owner=="austria"))other.regionId=c.Owned("austria").First(r=>r.regionId!=target).regionId;
            d.regionId=target;
            foreach(var army in new[]{a,d})
            {
                army.supply=1;
                foreach(var u in army.units)
                {
                    int strength=army==a?attackerStrength:defenderStrength;
                    c.World.State(u.homeRegionId).economy.enlisted+=strength-u.soldiers;u.soldiers=strength;
                    u.role="Regular";u.weapon="musket";u.doctrine=1;u.equipment=0;u.progression=new UnitProgression();u.loadout=new UnitLoadout();
                    if(veterans&&army==a){u.progression.Award(1300);u.progression.Train(UnitSkill.Accuracy);u.progression.Train(UnitSkill.Drill);u.progression.Train(UnitSkill.Discipline);u.equipment=2;}
                }
            }
            foreach(var b in c.World.State(target).buildings)if(b.kind=="Fortress")b.kind="Empty";
            if(fort>0)c.World.State(target).buildings[0]=new RegionBuilding{kind="Fortress",level=fort};
            check(c.Move("france",a.id,target,out _)&&c.Save.pending!=null,"Autoresolve fixture creates a real frontier encounter");return c;
        }
        var c=Prepare(271);var saved=clone(c);var other=fresh();
        uint state=c.Save.randomState;int men=c.Save.armies.Sum(a=>a.Soldiers);
        float estimate=c.AutoresolveAttackerShare(c.Save.pending);
        check(c.AutoresolveAttackerShare(c.Save.pending)==estimate&&state==c.Save.randomState&&men==c.Save.armies.Sum(a=>a.Soldiers),"Autoresolve preview does not roll or alter troops");
        check(other.Adopt(saved,out _),"Pending autoresolve restores");
        string encounter=c.Save.pending.id;
        check(c.Resolve(encounter,out _)&&other.Resolve(encounter,out _),"Both saved copies resolve");
        check(c.Save.lastBattle.result==other.Save.lastBattle.result&&c.Save.randomState==other.Save.randomState&&
            c.Save.armies.SelectMany(a=>a.units).Select(u=>(u.id,u.soldiers,u.progression.xp)).SequenceEqual(other.Save.armies.SelectMany(a=>a.units).Select(u=>(u.id,u.soldiers,u.progression.xp))),"Reload preserves exact winner, regimental casualties, XP and random state");
        check(!c.Resolve(encounter,out _),"Autoresolve cannot commit twice");
        int equalWins=0,strongWins=0,weakWins=0,veteranWins=0,fortWins=0;long strongLosses=0,equalLosses=0;
        for(uint seed=1;seed<=64;seed++)
        {
            foreach(string mode in new[]{"equal","strong","weak","veteran","fort"})
            {
                var game=Prepare(seed,mode=="weak"?125:500,mode=="strong"?125:500,mode=="veteran",mode=="fort"?2:0);
                string id=game.Save.pending.id;
                check(game.Resolve(id,out _),"Stat-driven battle resolves: "+mode);
                var report=game.Save.lastBattle;bool win=report.result=="Victory";
                if(mode=="equal"){if(win)equalWins++;equalLosses+=report.playerBefore-report.playerSurvivors;}
                if(mode=="strong"){if(win)strongWins++;strongLosses+=report.playerBefore-report.playerSurvivors;}
                if(mode=="weak"&&win)weakWins++;
                if(mode=="veteran"&&win)veteranWins++;
                if(mode=="fort"&&win)fortWins++;
                check(report.regiments.All(u=>u.survivors>=0&&u.survivors<=u.before&&u.xpAfter-u.xpBefore>=0&&u.xpAfter-u.xpBefore<=UnitProgression.MaxXP),"Regimental casualties and XP remain bounded");
                check(fresh().Adopt(clone(game),out _),"Resolved statistical fixture preserves save/manpower invariants");
            }
        }
        check(equalWins>8&&equalWins<56,"Even forces have uncertain outcomes");
        check(strongWins>=60&&weakWins<=4&&strongLosses<equalLosses/2,"Overwhelming numbers win reliably and with fewer losses");
        check(veteranWins>equalWins&&fortWins<equalWins,"Veterancy, quality and fortifications change win rates");
        Console.WriteLine($"Autoresolve / 64 seeds per case: equal {equalWins} wins, 4:1 strength {strongWins}, 1:4 strength {weakWins}, veterans {veteranWins}, attacking level-2 fort {fortWins}.");
        var stats=Prepare(17);var e=stats.Save.pending;var force=stats.Army(e.attackerArmyId);var unit=force.units[0];
        float normal=stats.AutoresolveAttackerShare(e);force.supply=.4f;check(stats.AutoresolveAttackerShare(e)<normal,"Low supply weakens autoresolve");force.supply=1;
        string general=force.generalName;force.generalName="";check(stats.AutoresolveAttackerShare(e)<normal,"General improves autoresolve");force.generalName=general;
        float power=stats.RegimentPower(unit);unit.role="Militia";check(stats.RegimentPower(unit)<power,"Militia differs from regulars with identical muskets");unit.role="Regular";
        unit.doctrine|=(int)MilitaryDoctrine.ControlledFire;check(stats.RegimentPower(unit)>power,"Adopted fire doctrine matters");unit.doctrine=1;
        unit.weapon="breechloader";check(stats.RegimentPower(unit)>power,"Issued weapon range and cadence matter");unit.weapon="musket";
        unit.loadout.Equip("drums-fifes",EquipmentUsers.Line,1800);check(stats.RegimentPower(unit)>power,"Purchased morale equipment matters");
    }
}
