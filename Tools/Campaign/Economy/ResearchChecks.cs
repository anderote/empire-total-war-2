using System;
using System.Linq;
using EmpireBattle;
using EmpireCampaign.Geography;

public static class ResearchChecks
{
    public static int Run(Func<CountryCampaign> fresh,Action<bool,string> check,Func<CountryCampaign,CountryCampaignSave> clone)
    {
        int count=0;Action<bool,string> test=(ok,name)=>{check(ok,name);count++;};string why;
        var c=fresh();var a=c.Save.armies.First(x=>x.owner=="france");
        a.regionId="region-paris";var home=c.World.State(a.regionId);home.buildings[0]=new RegionBuilding{kind="Barracks",level=2};
        c.World.FactionState("france").treasury=10000;home.economy.materials=200;
        var u=a.units.First();var nation=c.Progress("france");
        nation.technologies.Add("industry");nation.technologies.Add("drill");
        test(u.weapon=="musket"&&MilitaryEquipment.Has(u.doctrine,MilitaryDoctrine.Square),"Regular baseline has musket and square");
        test(!MilitaryEquipment.Has(u.doctrine,MilitaryDoctrine.LightInfantry),"Regular baseline does not receive skirmish drill");
        test(!c.Research("france","rifles",out why),"Rifle research requires both branches");
        nation.technologies.Add("arsenals");test(!c.Research("france","rifles",out why),"Arsenal knowledge alone cannot unlock rifle corps");
        nation.technologies.Add("light-infantry");test(c.Research("france","rifles",out why),"Rifle research accepts both prerequisites");
        test(!c.ResearchAvailable("france",c.Technology("breechloading"),out why)&&why.Contains("1840"),"Future research gated by scenario year");
        test(!c.ResearchAvailable("france",c.Technology("engineering"),out why),"Unsupported research does not advertise a working capability");
        nation.research="";nation.technologies.Add("rifles");
        test(!c.AdoptRegiment("france",a.id,u.id,"rifle",out why)&&u.weapon=="musket","Knowledge cannot issue rifles without arsenal");
        home.buildings[1]=new RegionBuilding{kind="Arsenal"};
        int budget=c.World.FactionState("france").treasury,experience=u.experience,men=u.soldiers;
        test(c.AdoptRegiment("france",a.id,u.id,"rifle",out why),"Rifle adoption accepted with facilities and money");
        test(c.World.FactionState("france").treasury==budget-100&&u.weapon=="musket"&&u.trainingTurns==2,"Weapon reservation paid with delayed effects");
        test(!c.Move("france",a.id,"region-lyon",out why),"Training army cannot march away");
        test(!c.AdoptRegiment("france",a.id,u.id,"rifle",out why),"Duplicate adoption rejected");
        test(!c.AdoptRegiment("austria",a.id,u.id,"rifle",out why),"Foreign adoption orders rejected");
        test(c.Adopt(clone(c),out why),"Pending adoption save round trip");
        a=c.Army(a.id);u=a.units.First();
        c.EndTurn(out why);while(c.Save.pending!=null)c.Resolve(c.Save.pending.id,out why);
        test(u.weapon=="musket"&&u.trainingTurns==1,"No premature weapon upgrade on first training turn");
        c.EndTurn(out why);while(c.Save.pending!=null)c.Resolve(c.Save.pending.id,out why);
        test(u.weapon=="rifle"&&u.trainingTurns==0&&u.course==""&&u.role=="Rifle","Completed adoption changes actual kit and role");
        test(u.soldiers==men&&u.experience==experience,"Adoption creates neither soldiers nor combat experience");
        test(MilitaryEquipment.Has(u.doctrine,MilitaryDoctrine.LightInfantry),"Rifle conversion includes required specialist instruction");
        nation=c.Progress("france");nation.technologies.Add("maneuver");
        var militia=a.units.Last();militia.role="Militia";militia.doctrine=0;
        test(c.AdoptRegiment("france",a.id,militia.id,"maneuver",out why),"Militia can purchase square training");
        c.EndTurn(out why);while(c.Save.pending!=null)c.Resolve(c.Save.pending.id,out why);
        test(militia.role=="Regular"&&MilitaryEquipment.Has(militia.doctrine,MilitaryDoctrine.Square),"Militia gains square through completed training");
        var bad=clone(c);bad.armies[0].units[0].weapon="magic-rifle";test(!c.Adopt(bad,out why),"Unknown weapon save rejected");
        bad=clone(c);bad.armies[0].units[0].doctrine=65536;test(!c.Adopt(bad,out why),"Unknown doctrine bit rejected");
        bad=clone(c);bad.armies[0].units[0].weapon="magazine";test(!c.Adopt(bad,out why),"Anachronistic equipment save rejected");
        bad=clone(c);bad.armies[0].units[0].trainingTurns=1;bad.armies[0].units[0].course="";test(!c.Adopt(bad,out why),"Malformed pending training rejected");
        bad=clone(c);bad.countries.First(x=>x.factionId=="france").technologies.Add("magazines");test(!c.Adopt(bad,out why),"Future national knowledge rejected");
        bad=clone(c);bad.countries.First(x=>x.factionId=="france").technologies.Add("engineering");test(!c.Adopt(bad,out why),"Unsupported national capability rejected");
        bad=clone(c);bad.countries.First(x=>x.factionId=="france").technologies.Remove("industry");test(!c.Adopt(bad,out why),"Save research requires all prerequisite knowledge");
        bad=clone(c);bad.countries.First(x=>x.factionId=="france").research="magazines";test(!c.Adopt(bad,out why),"Future in-progress research rejected");
        bad=clone(c);bad.armies.First(x=>x.owner=="france").units[0].course="volleys";bad.armies.First(x=>x.owner=="france").units[0].trainingTurns=1;test(!c.Adopt(bad,out why),"Pending course requires national knowledge");
        var legacy=clone(c);legacy.schemaVersion=1;legacy.armies[0].units[0].equipment=2;
        test(c.Adopt(legacy,out why)&&c.Save.schemaVersion==5&&c.Save.armies[0].units[0].equipment==2&&c.Save.armies[0].units[0].weapon=="musket","Legacy quality migrates without becoming a rifle");
        var small=new CampaignRegiment{soldiers=267};
        test(c.AdoptionCost(small,CountryCampaign.Course("rifle"))==54&&c.AdoptionMaterials(small,CountryCampaign.Course("rifle"))==6,"Partial-strength refit charges actual personnel");
        var other=fresh();a=other.Save.armies.First(x=>x.owner=="france");a.regionId="region-paris";
        home=other.World.State(a.regionId);home.buildings[0]=new RegionBuilding{kind="Barracks"};other.World.FactionState("france").treasury=10000;
        test(other.Recruit("france",a.regionId,"Militia",out why)&&other.Save.recruitment.Last().doctrine==0,"Militia recruitment has restricted doctrine");
        test(!other.Recruit("france",a.regionId,"Rifle",out why),"Specialist recruiting cannot bypass research");
        test(MilitaryEquipment.Weapon("rifle").EffectiveRange>MilitaryEquipment.Weapon("musket").EffectiveRange&&MilitaryEquipment.Weapon("rifle").Reload>MilitaryEquipment.Weapon("musket").Reload,"Rifle profile preserves range versus cadence tradeoff");
        test(MilitaryEquipment.Weapon("magazine").Magazine==8&&MilitaryEquipment.Weapon("magazine").Reload>MilitaryEquipment.Weapon("magazine").Cycle,"Magazine reload and cycling are distinct");
        test(MilitaryEquipment.MisfireChance("musket",1)>MilitaryEquipment.MisfireChance("musket",0),"Rain increases flintlock ignition failures");
        test(MilitaryEquipment.MisfireChance("percussion",1)<MilitaryEquipment.MisfireChance("musket",1),"Percussion has a concrete wet-weather advantage");
        var technology=other.Technology("drill");var previous=technology.prerequisites;
        try
        {
            technology.prerequisites=new[]{"maneuver"};bool rejected=false;
            try{fresh();}catch(ArgumentException){rejected=true;}
            test(rejected,"Cyclic research definition rejected at initialization");
        }
        finally{technology.prerequisites=previous;}
        return count;
    }
}
