using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EmpireCampaign.Geography;

public static class NamingChecks
{
    static int checks;
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions {IncludeFields=true};
    static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json);
    static CountryCampaign Fresh()=>new CountryCampaign(Read<RegionCatalog>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/campaign.json"),Read<EconomyProfiles>("Assets/Resources/CampaignEconomyProfiles.json"),Read<TheaterData>("Assets/Scripts/Campaign/Geography/Resources/WorldGeography/world.json"),Read<CampaignBalance>("Assets/Resources/CountryCampaignBalance.json"),"france",1);
    static CountryCampaignSave Clone(CountryCampaign c)=>JsonSerializer.Deserialize<CountryCampaignSave>(JsonSerializer.Serialize(c.Save,Json),Json);
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
    static object Call(CountryCampaign c,string method,params object[] args)=>typeof(CountryCampaign).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,args);
    static CampaignRegiment Issue(CountryCampaign c,CampaignArmy army,string role,bool town=false,string custom="")
    {
        var u=(CampaignRegiment)Call(c,"NewRegiment",town?"region-paris":army.regionId,c.Balance.unitSize);u.role=role;u.name=custom;
        Call(c,"AssignRegimentName",c.Save,army,u,town);return u;
    }
    static void Finish(CountryCampaign c)=>Call(c,"FinishRound");
    public static void Main()
    {
        var c=Fresh();var armies=c.Save.armies.Where(a=>a.owner=="france").ToArray();var first=armies[0];var second=armies[1];string why;
        Check(first.name=="First Army"&&second.name=="Second Army","Army names per faction");
        Check(first.units[0].name=="1st Line Infantry"&&second.units[0].name=="21st Line Infantry","Initial independent army blocks");
        for(int i=1;i<=3;i++){Check(Issue(c,first,"Rifle").name==CountryCampaign.Ordinal(i)+" Rifles","First army rifles "+i);Check(Issue(c,second,"Rifle").name==CountryCampaign.Ordinal(20+i)+" Rifles","Second army rifles "+i);}
        foreach(int n in new[]{1,2,3,4,11,12,13,21,22,23,111,112,113})Check(CountryCampaign.Ordinal(n)==n+(new[]{1,21}.Contains(n)?"st":new[]{2,22}.Contains(n)?"nd":new[]{3,23}.Contains(n)?"rd":"th"),"Ordinal "+n);
        Check(CountryCampaign.ArmyDefaultName(3)=="Third Army"&&CountryCampaign.ArmyDefaultName(21)=="21st Army","Army ordinal labels");
        for(int i=4;i<=20;i++)Issue(c,first,"Rifle");
        Check(Issue(c,first,"Rifle").name=="1st Reserve Rifles / 1st Army","Overflow never enters second army block");
        Check(Issue(c,first,"Rifle").name=="2nd Reserve Rifles / 1st Army","Reserve counter persistent");
        Check(Issue(c,first,"Militia").name=="1st Militia"&&Issue(c,first,"Guard").name=="1st Guards"&&Issue(c,first,"Light").name=="1st Light Infantry","Independent type counters");
        Check(Issue(c,first,"Regular",true).name=="Paris 1st Line Infantry","Town infantry counter");
        Check(Issue(c,first,"Rifle",true).name=="Paris 1st Rifles"&&Issue(c,second,"Rifle",true).name=="Paris 2nd Rifles","Town rifle counter independent of army");
        var named=Issue(c,first,"Rifle",true,"Horse Guards");Check(named.name=="Horse Guards"&&named.designationSource=="Town"&&named.originArmyId==first.id&&named.originArmyName==first.name&&named.originProvenanceKnown&&named.originTownId=="region-paris","Library/custom birth name and provenance preserved");
        var counter=c.Save.townNaming.Single().counters.Single(x=>x.type=="Rifles").issued;
        Call(c,"AssignRegimentName",c.Save,second,named,false);Check(named.originArmyId==first.id&&c.Save.townNaming.Single().counters.Single(x=>x.type=="Rifles").issued==counter,"Issuance is idempotent after move");
        c=Fresh();first=c.Save.armies.First(a=>a.owner=="france");var veteran=first.units[0];string oldName=veteran.name,origin=veteran.originArmyId;
        Check(c.RenameArmy("france",first.id,"North Guards",out why),"Custom army naming");
        Check(c.RenameRegiment("france",first.id,veteran.id,"Horse Guards",out why),"Custom regiment naming");
        Check(!c.RenameArmy("austria",first.id,"Wrong",out why)&&!c.RenameRegiment("france",first.id,veteran.id,"bad\nname",out why),"Rename ownership/control validation");
        string detachedId;Check(c.Detach("france",first.id,new[]{veteran.id},out detachedId,out why),"Detach veteran");var detached=c.Army(detachedId);
        Check(veteran.name=="Horse Guards"&&veteran.originArmyId==origin&&detached.name=="Third Army","Detach preserves birth identity");
        Check(c.RenameArmy("france",detached.id,"Southern Guards",out why)&&c.CommissionGeneral("france",detached.id,out why)&&detached.name=="Southern Guards","General preserves custom army name");
        Check(c.Transfer("france",detached.id,first.id,new[]{veteran.id},out why)&&veteran.name=="Horse Guards"&&veteran.originArmyId==origin&&veteran.originArmyName=="First Army","Merge preserves custom veteran identity");
        Check(c.Save.armyNaming.Any(n=>n.armyId==detachedId),"Retired army record retained");
        Check(c.Adopt(Clone(c),out why),"Naming save round trip: "+why);first=c.Army(first.id);veteran=first.units.Find(u=>u.id==veteran.id);
        Check(first.name=="North Guards"&&veteran.name=="Horse Guards"&&veteran.originArmyId==origin,"Custom names survive save load");
        var loss=first.units[1];string lost=loss.name;first.units.Remove(loss);c.World.State(loss.homeRegionId).economy.enlisted-=loss.soldiers;
        Check(c.Adopt(Clone(c),out why),"Loss save round trip");first=c.Army(first.id);
        var afterLoss=Issue(c,first,"Regular");Check(afterLoss.name!=lost&&afterLoss.designationSequence==7,"Loss does not reuse sequence");
        Check(c.Detach("france",first.id,new[]{first.units[0].id},out detachedId,out why)&&c.Army(detachedId).name=="Fourth Army","Retired army ordinals not reused");
        var invalid=Clone(c);invalid.armyNaming[0].ordinal=0;Check(!c.Adopt(invalid,out why),"Invalid naming record rejected");
        invalid=Clone(c);invalid.armies.First(a=>a.owner=="france").units[0].originArmyId="missing";Check(!c.Adopt(invalid,out why),"Unknown origin rejected");
        c=Fresh();var legacy=Clone(c);legacy.namingVersion=0;legacy.armyNaming=null;legacy.townNaming=null;legacy.armies[0].name="Historical Custom Army";legacy.armies[0].units[0].name="Old Guards";legacy.armies[0].units[1].name="";
        Check(c.Adopt(legacy,out why)&&legacy.armies[0].name=="Historical Custom Army"&&legacy.armies[0].units[0].name=="Old Guards"&&!string.IsNullOrEmpty(legacy.armies[0].units[1].name),"Legacy names kept and unnamed units assigned once");
        Check(!legacy.armies[0].units[0].originProvenanceKnown,"Legacy attribution honestly marked inferred");
        Check(c.Adopt(Clone(c),out why),"Migration idempotent");
        // Exercise public queue creation and real completion, without AI or full campaign playthrough.
        c=Fresh();first=c.Save.armies.First(a=>a.owner=="france");string city=first.regionId;
        Check(c.Recruit("france",city,"Regular",first.id,out why),"Explicit army recruitment queued");
        var q=c.Save.recruitment.Single();q.turnsRemaining=1;Finish(c);var recruit=first.units.Last();
        Check(recruit.name=="7th Line Infantry"&&recruit.designationSource=="Army"&&recruit.originArmyId==first.id,"Army queue completion names at birth");
        Check(c.Adopt(Clone(c),out why),"Completed army recruit save round trip: "+why);
        Check(c.Recruit("france","region-paris","Regular","",out why),"Town recruitment queued");q=c.Save.recruitment.Single();q.turnsRemaining=1;Finish(c);
        var townUnit=c.Save.armies.SelectMany(a=>a.units).Single(u=>u.designationSource=="Town");
        Check(townUnit.name=="Paris 1st Line Infantry"&&townUnit.originTownName=="Paris","Town queue completion names at birth");
        Check(c.Adopt(Clone(c),out why),"Completed town recruit save round trip: "+why);
        c=Fresh();first=c.Save.armies.First(a=>a.owner=="france");
        Check(c.RenameRegiment("france",first.id,first.units[0].id,"1st Rifles",out why),"Custom name can reserve future designation");
        Check(Issue(c,first,"Rifle").name=="2nd Rifles","Auto allocator skips live custom collision");
        Check(c.RenameArmy("france",first.id,"Third Army",out why),"Custom army reserves future default");
        Check(c.Detach("france",first.id,new[]{first.units[0].id},out detachedId,out why)&&c.Army(detachedId).name=="Fourth Army","New army skips occupied custom name");
        var marseille=(CampaignRegiment)Call(c,"NewRegiment","region-marseille",c.Balance.unitSize);marseille.role="Rifle";
        Call(c,"AssignRegimentName",c.Save,first,marseille,true);
        Check(marseille.name=="Marseille 1st Rifles","Different towns have independent counters");
        c=Fresh();first=c.Save.armies.First(a=>a.owner=="france");veteran=first.units[0];
        Check(c.RenameRegiment("france",first.id,veteran.id,"1st Rifles",out why),"Reserve fallen custom label");
        first.units.Remove(veteran);c.World.State(veteran.homeRegionId).economy.enlisted-=veteran.soldiers;
        Check(c.Adopt(Clone(c),out why),"Custom tombstone persists through loss/save");first=c.Army(first.id);
        Check(Issue(c,first,"Rifle").name=="2nd Rifles","Automatic naming never steals fallen custom name");
        c=Fresh();first=c.Save.armies.First(a=>a.owner=="france");veteran=first.units[0];
        Check(c.RenameRegiment("france",first.id,veteran.id,"Fallen Guards",out why),"History snapshot custom label");
        var enemy=c.Save.armies.First(a=>a.owner=="austria");
        var e=new CampaignEncounter {id=(string)Call(c,"Id","encounter-"),attacker="france",defender="austria",attackerArmyId=first.id,defenderArmyId=enemy.id,regionId=first.regionId};
        e.attackerArmyIds.Add(first.id);e.defenderArmyIds.Add(enemy.id);
        var capture=Call(c,"CaptureBattleReport",e,"Autoresolve");veteran.soldiers=0;first.units.Remove(veteran);c.World.State(veteran.homeRegionId).economy.enlisted-=c.Balance.unitSize;
        Call(c,"CompleteBattleReport",capture,e,true);c.Save.resolvedEncounters.Add(e.id);
        Check(c.Save.lastBattle.regiments[0].name=="Fallen Guards"&&c.Save.lastBattle.regiments[0].originArmyId==first.id&&c.Save.lastBattle.regiments[0].survivors==0,"Destroyed regiment keeps name and origin in report");
        Check(c.Adopt(Clone(c),out why),"Destroyed name snapshot save round trip: "+why);
        c=Fresh();first=c.Save.armies.First(a=>a.owner=="france");
        var cadre=new EmpireBattle.SavedRegiment {id="saved-cadre",name="Horse Guards",role=EmpireBattle.UnitRole.Line,users=EmpireBattle.EquipmentUsers.Line,sourceCapacity=c.Balance.unitSize};
        Check(c.RecruitSaved("france",first.regionId,cadre,first.id,out why),"Named library cadre queued: "+why);
        c.Save.recruitment.Single().turnsRemaining=1;Finish(c);recruit=first.units.Last();
        Check(recruit.name=="Horse Guards"&&recruit.originArmyId==first.id&&recruit.designationSource=="Army","Real library completion preserves name and records birth");
        Check(c.Adopt(Clone(c),out why),"Named library birth save round trip: "+why);
        c=Fresh();first=c.Save.armies.First(a=>a.owner=="france");
        Check(c.Recruit("france",first.regionId,"Regular",first.id,out why),"Queue before capacity change");
        while(first.units.Count<20){var added=Issue(c,first,"Regular");first.units.Add(added);c.World.State(added.homeRegionId).economy.enlisted+=added.soldiers;}
        c.Save.recruitment.Single().turnsRemaining=1;Finish(c);
        var fallback=c.Save.armies.Single(a=>a.owner=="france"&&a.name=="Third Army");
        Check(first.units.Count==20&&fallback.units.Count==1&&fallback.units[0].name=="41st Line Infantry"&&fallback.units[0].originArmyId==fallback.id,"Capacity fallback names final birth army without exceeding20");
        Check(!c.CanAssignRecruits("france",first.regionId,first.id,out why),"Full army cannot reserve another recruit");
        Check(c.Adopt(Clone(c),out why),"Capacity fallback naming save round trip: "+why);
        Console.WriteLine("PASS: "+checks+" naming, birth recruitment, transfer/loss and managed save checks. No Unity/game launches.");
    }
}
