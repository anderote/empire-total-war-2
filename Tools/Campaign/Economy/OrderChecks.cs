using System;
using System.Linq;
using EmpireCampaign.Geography;

public static class OrderChecks
{
    // Run only when campaign validation is explicitly resumed; source-compilable independently.
    static void QuietTurn(CountryCampaign c)
    {
        foreach(var p in c.Save.countries)p.lastPlanningTurn=c.Save.world.turn;
        foreach(var army in c.Save.armies)army.lastAIActionTurn=c.Save.world.turn;
        if(!c.EndTurn(out string reason)||c.Save.pending!=null)throw new Exception("Order fixture turn: "+reason);
    }
    public static void Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        {
            var c=fresh();var state=c.Owned("france").First(r=>r.buildings.Length>=3);var funds=c.World.FactionState("france");
            funds.treasury=5000;
            state.buildings[0]=new RegionBuilding();state.buildings[1]=new RegionBuilding{kind="Market"};
            int slot=state.buildings.Length-1;state.buildings[slot]=new RegionBuilding();
            int quote=c.ConstructionCost("france",state.regionId,"Farm"),before=funds.treasury;
            check(quote>RegionCampaign.Cost("Farm"),"Quote includes existing-development surcharge");
            check(c.CanBuild("france",state.regionId,"Farm",slot,out _)&&c.Build("france",state.regionId,"Farm",slot,out _),"Selected plot accepts construction");
            check(state.buildings[0].kind=="Empty"&&state.buildings[slot].kind=="Farm","Building uses chosen plot even when an earlier plot is empty");
            check(funds.treasury==before-quote&&state.buildings[slot].paidCost==quote,"Construction charges exactly the displayed quote");
            check(!c.Build("france",state.regionId,"Farm",slot,out _)&&funds.treasury==before-quote,"Occupied plot cannot charge again");
            check(c.CancelConstruction("france",state.regionId,slot,out _)&&funds.treasury==before,"Immediate cancellation refunds the entire quoted price");
            funds.treasury=quote-1;
            check(!c.CanBuild("france",state.regionId,"Farm",slot,out _)&&!c.Build("france",state.regionId,"Farm",slot,out _),"Unaffordable displayed action and actual order both reject");
        }
        foreach(bool leave in new[]{false,true})
        {
            var c=fresh();foreach(var treaty in c.Save.treaties)treaty.war=false;
            var army=c.Save.armies.First(a=>a.owner=="france");var home=c.World.State(army.regionId);
            home.buildings[0]=new RegionBuilding{kind="Barracks",level=2};home.economy.materials=200;c.World.FactionState("france").treasury=5000;
            var other=c.Save.armies.First(a=>a.owner=="france"&&a.id!=army.id);other.regionId=army.regionId;
            int previous=army.units.Count,otherPrevious=other.units.Count;
            check(c.Recruit("france",home.regionId,"Regular",army.id,out _),"Recruitment can name its receiving army");
            string id=army.id;var order=c.Save.recruitment.Last();
            check(c.Adopt(clone(c),out string reason),"Recruitment target survives save validation: "+reason);
            army=c.Army(id);other=c.Army(other.id);order=c.Save.recruitment.Single(q=>q.id==order.id);
            check(order.targetArmyId==army.id,"Saved recruitment retains selected army");
            if(leave)army.regionId=c.Owned("france").First(r=>r.regionId!=home.regionId).regionId;
            for(int i=0;i<c.Balance.trainingTurns;i++)QuietTurn(c);
            check(army.units.Count==previous+(leave?0:1),"Recruits join only when the selected army stays in the city");
            check(other.units.Count==otherPrevious,"Explicit army assignment never silently selects a different local army");
            if(leave)check(c.Save.armies.Any(a=>a.owner=="france"&&a.id!=army.id&&a.id!=other.id&&a.regionId==home.regionId&&a.units.Count==1),"Departed target leaves recruits in a local detachment");
            check(c.Save.recruitment.All(q=>q.id!=order.id)&&c.Adopt(clone(c),out reason),"Completed targeted recruitment conserves valid saved manpower: "+reason);
        }
        {
            var c=fresh();var army=c.Save.armies.First(a=>a.owner=="france");var home=c.World.State(army.regionId);
            home.buildings[0]=new RegionBuilding{kind="Barracks",level=2};home.economy.materials=200;home.economy.population=1000000;
            c.World.FactionState("france").treasury=5000;
            while(army.units.Count<19)
            {army.units.Add(new CampaignRegiment{id="capacity-unit-"+army.units.Count,homeRegionId=home.regionId,soldiers=c.Balance.unitSize});home.economy.enlisted+=c.Balance.unitSize;}
            check(c.Recruit("france",home.regionId,"Regular",army.id,out _),"An army can reserve its final regiment place");
            var order=c.Save.recruitment.Last();int funds=c.World.FactionState("france").treasury;
            check(!c.Recruit("france",home.regionId,"Regular",army.id,out _)&&c.World.FactionState("france").treasury==funds,"Queued recruits count toward the 20-regiment limit without charging rejected orders");
            check(c.CancelRecruitment("france",order.id,out _)&&c.CanRecruit("france",home.regionId,"Regular",army.id,out _),"Cancelling releases both the army reservation and training place");
        }
        {
            var c=fresh();var army=c.Save.armies.First(a=>a.owner=="france");var unit=army.units[0];
            unit.soldiers-=80;c.World.State(unit.homeRegionId).economy.enlisted-=80;
            c.World.FactionState("france").treasury=10;c.World.State(unit.homeRegionId).economy.materials=80;
            int men=army.Soldiers,funds=c.World.FactionState("france").treasury,enlisted=c.World.State(unit.homeRegionId).economy.enlisted;
            check(c.CanRecover("france",army.id,out int count,out int cost,out _),"Replacement quote permits a partial affordable reinforcement");
            check(count>0&&count<=50&&cost<=funds&&army.Soldiers==men&&c.World.State(unit.homeRegionId).economy.enlisted==enlisted,"Preview reserves no money or manpower");
            check(c.Recover("france",army.id,out _)&&army.Soldiers==men+count&&c.World.FactionState("france").treasury==funds-cost,"Replacement order matches the quoted count and price");
            check(!c.Recover("france",army.id,out _),"Recovery cannot be charged twice in the same turn");
        }
        {
            var c=fresh();var treaty=c.Treaty("france","prussia");treaty.war=false;treaty.peaceUntil=c.Save.world.turn+2;
            check(!c.CanDeclareWar("france","prussia",out _)&&!c.Diplomacy("france","prussia","War",out _),"Both war preview and action honor a truce");
            treaty.peaceUntil=0;treaty.trade=true;treaty.alliance=true;treaty.access=true;
            var guarantee=c.Treaty("prussia","britain");guarantee.war=false;guarantee.alliance=true;
            var joined=c.Treaty("france","britain");joined.war=false;joined.peaceUntil=0;
            check(c.WarAllies("france","prussia").Contains("britain"),"Declaration preview lists the defensive ally");
            check(c.Diplomacy("france","prussia","War",out _)&&joined.war&&!treaty.trade&&!treaty.access&&!treaty.alliance,"Confirmed declaration applies the displayed treaty consequences");
        }
    }
}
