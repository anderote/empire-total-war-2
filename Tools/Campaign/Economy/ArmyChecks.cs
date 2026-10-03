using System;
using System.Linq;
using EmpireCampaign.Geography;

public static class ArmyChecks
{
    public static void Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        var c=fresh();string reason,id;var a=c.Save.armies.First(x=>x.owner=="france");
        check(c.Save.playerFaction=="france"&&a.generalName!="","French opening has a general");
        int men=a.Soldiers,manpower=c.Save.world.regions.Sum(r=>r.economy.enlisted);
        a.movement=73;string regiment=a.units[0].id;
        check(!c.Detach("austria",a.id,new[]{regiment},out id,out reason),"Cannot detach another nation's units");
        check(!c.Detach("france",a.id,new[]{regiment,regiment},out id,out reason),"Duplicate detach selection rejected");
        check(c.Detach("france",a.id,new[]{regiment},out id,out reason),"A regiment becomes an independent detachment");
        var detached=c.Army(id);
        check(detached.generalName==""&&detached.movement==73&&a.Soldiers+detached.Soldiers==men,"Detach conserves strength and spent movement");
        check(c.Save.world.regions.Sum(r=>r.economy.enlisted)==manpower,"Detach does not recruit phantom manpower");
        detached.movement=12;
        check(c.Transfer("france",detached.id,a.id,new[]{regiment},out reason)&&a.movement==12&&a.Soldiers==men&&c.Army(id)==null,"Merge preserves identity and lower movement allowance");
        check(c.Detach("france",a.id,new[]{regiment},out id,out reason),"Detach again for a march");detached=c.Army(id);
        string destination=c.Save.activeRegions.First(r=>r!=detached.regionId&&c.World.State(r).owner=="france"&&c.Route("france",detached.regionId,r)!=null);
        detached.movement=1;
        check(c.March("france",id,destination,out reason)&&detached.marchProgress==1&&detached.movement==0,"One kilometre advances a persistent march instead of teleporting");
        check(!c.Transfer("france",id,a.id,new[]{regiment},out reason),"In-transit regiment cannot transfer");
        check(!c.CommissionGeneral("france",id,out reason),"General appointment requires a stationary force");
        check(!c.Move("france",id,destination,out reason),"Direct movement cannot bypass an active march");
        check(c.Adopt(clone(c),out reason),"Mid-march save round-trips");detached=c.Army(id);
        var invalid=clone(c);invalid.armies.First(x=>x.id==id).marchProgress=float.NaN;
        check(!c.Adopt(invalid,out reason),"Nonfinite march progress rejected");
        invalid=clone(c);invalid.armies.First(x=>x.id==id).marchNext="region-london";
        check(!c.Adopt(invalid,out reason),"Nonadjacent march edge rejected");
        check(c.Halt("france",id,out reason)&&detached.movement==0&&detached.marchProgress==0,"Halt never refunds movement");
        int funds=c.World.FactionState("france").treasury;
        check(c.CommissionGeneral("france",id,out reason)&&c.World.FactionState("france").treasury==funds-200,"General appointment consumes treasury");
        check(!c.CommissionGeneral("france",id,out reason),"An army cannot buy duplicate generals");
        detached.movement=1;check(c.March("france",id,destination,out reason),"March can restart after halting");
        int progress=(int)detached.marchProgress;string previous=detached.regionId;
        c.EndTurn(out reason);while(c.Save.pending!=null)c.Resolve(c.Save.pending.id,out reason);
        check(detached.regionId!=previous||detached.marchProgress>progress,"Queued player march advances when movement refreshes");
        check(c.Adopt(clone(c),out reason),"A turn with persistent marches remains a valid save");
        var supplyGame=fresh();var forces=supplyGame.Save.armies.Where(x=>x.owner=="france").ToArray();
        forces[1].regionId=forces[0].regionId;forces[0].supply=1;forces[1].supply=0;
        int moving=forces[0].units[0].soldiers,receiving=forces[1].Soldiers;
        check(supplyGame.Transfer("france",forces[0].id,forces[1].id,new[]{forces[0].units[0].id},out reason)&&
            Math.Abs(forces[1].supply-moving/(float)(moving+receiving))<.0001f,"A partial transfer carries only the transferred soldiers' supplies");
        var old=clone(fresh());old.schemaVersion=3;
        check(fresh().Adopt(old,out reason)&&old.schemaVersion==5,"Schema-three armies migrate to commanders and march orders");
        var scouting=fresh();var scout=scouting.Save.armies.First(x=>x.owner=="france");var foe=scouting.Save.armies.First(x=>x.owner=="prussia");
        foe.regionId="region-berlin";
        check(!scouting.CanObserve(foe),"Distant enemy armies remain hidden");
        scout.regionId=foe.regionId;
        check(scouting.CanObserve(foe),"Expeditionary armies reveal opponents away from owned territory");
    }
}
