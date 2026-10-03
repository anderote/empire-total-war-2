using System;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        // Issuance records outlive their armies. Moving a regiment never calls this allocator.
        public static string Ordinal(int number)
        {
            int last=number%100;
            return number+(last>=11&&last<=13?"th":number%10==1?"st":number%10==2?"nd":number%10==3?"rd":"th");
        }
        public static string ArmyDefaultName(int ordinal)
        {
            string[] words={"First","Second","Third","Fourth","Fifth","Sixth","Seventh","Eighth","Ninth","Tenth","Eleventh","Twelfth","Thirteenth","Fourteenth","Fifteenth","Sixteenth","Seventeenth","Eighteenth","Nineteenth","Twentieth"};
            return (ordinal<=words.Length?words[ordinal-1]:Ordinal(ordinal))+" Army";
        }
        static string DesignationType(CampaignRegiment unit)=>unit.role=="Rifle"?"Rifles":unit.role=="Militia"?"Militia":unit.role=="Light"?"Light Infantry":unit.role=="Guard"?"Guards":"Line Infantry";
        static readonly string[] NamingTypes={"Rifles","Militia","Light Infantry","Guards","Line Infantry"};
        static ArmyNamingRecord RegisterArmyName(CountryCampaignSave save,CampaignArmy army)
        {
            var record=save.armyNaming.Find(n=>n.armyId==army.id);
            if(record!=null)return record;
            int ordinal=save.armyNaming.Where(n=>n.owner==army.owner).Select(n=>n.ordinal).DefaultIfEmpty(0).Max()+1;
            while(save.armies.Any(a=>a!=army&&a.owner==army.owner&&string.Equals(a.name,ArmyDefaultName(ordinal),StringComparison.OrdinalIgnoreCase)))ordinal++;
            record=new ArmyNamingRecord {armyId=army.id,owner=army.owner,ordinal=ordinal};
            save.armyNaming.Add(record);return record;
        }
        static RegimentNamingCounter NamingCounter(System.Collections.Generic.List<RegimentNamingCounter> counters,string type)
        {
            var counter=counters.Find(n=>n.type==type);
            if(counter==null){counter=new RegimentNamingCounter {type=type};counters.Add(counter);}return counter;
        }
        static bool NameIssued(CountryCampaignSave save,string owner,string name)=>
            save.armyNaming.Any(n=>n.owner==owner&&n.issuedNames.Any(s=>string.Equals(s,name,StringComparison.OrdinalIgnoreCase)))||
            save.townNaming.Any(n=>n.owner==owner&&n.issuedNames.Any(s=>string.Equals(s,name,StringComparison.OrdinalIgnoreCase)))||
            save.armies.Where(a=>a.owner==owner).SelectMany(a=>a.units).Any(u=>string.Equals(u.name,name,StringComparison.OrdinalIgnoreCase));
        void AssignRegimentName(CountryCampaignSave save,CampaignArmy army,CampaignRegiment unit,bool townOrigin=false)
        {
            if(unit.designationSequence>0)return;
            var record=RegisterArmyName(save,army);string type=DesignationType(unit);
            var counters=record.counters;var names=record.issuedNames;TownNamingRecord town=null;
            string hometown=Place(unit.homeRegionId);
            if(townOrigin)
            {
                town=save.townNaming.Find(n=>n.owner==army.owner&&n.regionId==unit.homeRegionId);
                if(town==null){town=new TownNamingRecord {owner=army.owner,regionId=unit.homeRegionId,townName=hometown};save.townNaming.Add(town);}
                counters=town.counters;names=town.issuedNames;
            }
            var counter=NamingCounter(counters,type);string designation;
            do
            {
                int sequence=++counter.issued;
                designation=townOrigin?town.townName+" "+Ordinal(sequence)+" "+type:
                    sequence<=20?Ordinal((record.ordinal-1)*20+sequence)+" "+type:
                    Ordinal(sequence-20)+" Reserve "+type+" / "+Ordinal(record.ordinal)+" Army";
            }while(NameIssued(save,army.owner,designation));
            names.Add(designation);
            if(!string.IsNullOrEmpty(unit.name)&&!NameIssued(save,army.owner,unit.name))names.Add(unit.name);
            unit.originProvenanceKnown=true;unit.originArmyId=army.id;unit.originArmyName=army.name;unit.originTownId=unit.homeRegionId;unit.originTownName=hometown;
            unit.designationSource=townOrigin?"Town":"Army";unit.designationType=type;unit.designationSequence=counter.issued;
            if(string.IsNullOrEmpty(unit.name))unit.name=designation;
        }
        static bool ValidNamingCounters(System.Collections.Generic.List<RegimentNamingCounter> counters,System.Collections.Generic.List<string> names)=>
            counters!=null&&names!=null&&counters.All(c=>c!=null&&NamingTypes.Contains(c.type)&&c.issued>=1&&c.issued<=1000000)&&
            counters.Select(c=>c.type).Distinct().Count()==counters.Count&&names.All(s=>EmpireBattle.SavedRegiment.SafeName(s,60))&&
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==names.Count&&names.Count<=1000000;
        bool RestoreNaming(CountryCampaignSave candidate)
        {
            if(candidate.namingVersion==0)
            {
                candidate.armyNaming=new System.Collections.Generic.List<ArmyNamingRecord>();candidate.townNaming=new System.Collections.Generic.List<TownNamingRecord>();
                foreach(var army in candidate.armies)
                {
                    var record=RegisterArmyName(candidate,army);
                    // A nonempty legacy label may have been user-authored: never replace it later.
                    army.customName=!string.IsNullOrEmpty(army.name);
                    if(string.IsNullOrEmpty(army.name))army.name=ArmyDefaultName(record.ordinal);
                    foreach(var unit in army.units){unit.designationSequence=0;AssignRegimentName(candidate,army,unit);unit.originProvenanceKnown=false;}
                }
                candidate.namingVersion=1;
            }
            if(candidate.namingVersion!=1||candidate.armyNaming==null||candidate.townNaming==null)return false;
            if(candidate.armyNaming.Any(n=>n==null||!candidate.countries.Any(c=>c.factionId==n.owner)||string.IsNullOrEmpty(n.armyId)||n.ordinal<1||n.ordinal>1000000||!ValidNamingCounters(n.counters,n.issuedNames))||
                candidate.armyNaming.Select(n=>n.armyId).Distinct().Count()!=candidate.armyNaming.Count||
                candidate.armyNaming.Select(n=>n.owner+"/"+n.ordinal).Distinct().Count()!=candidate.armyNaming.Count)return false;
            if(candidate.townNaming.Any(n=>n==null||!candidate.countries.Any(c=>c.factionId==n.owner)||!Active(n.regionId)||!EmpireBattle.SavedRegiment.SafeName(n.townName,60)||!ValidNamingCounters(n.counters,n.issuedNames))||
                candidate.townNaming.Select(n=>n.owner+"/"+n.regionId).Distinct().Count()!=candidate.townNaming.Count)return false;
            var issued=candidate.armyNaming.SelectMany(n=>n.issuedNames.Select(s=>n.owner+"/"+s)).Concat(candidate.townNaming.SelectMany(n=>n.issuedNames.Select(s=>n.owner+"/"+s))).ToArray();
            if(issued.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=issued.Length)return false;
            var identities=new System.Collections.Generic.HashSet<string>();
            foreach(var army in candidate.armies)
            {
                if(!EmpireBattle.SavedRegiment.SafeName(army.name,60)||!candidate.armyNaming.Any(n=>n.armyId==army.id&&n.owner==army.owner))return false;
                foreach(var unit in army.units)
                {
                    var origin=candidate.armyNaming.Find(n=>n.armyId==unit.originArmyId&&n.owner==army.owner);
                    if(origin==null||!EmpireBattle.SavedRegiment.SafeName(unit.originArmyName,60)||unit.originTownId!=unit.homeRegionId||!EmpireBattle.SavedRegiment.SafeName(unit.originTownName,60)||!EmpireBattle.SavedRegiment.SafeName(unit.name,60))return false;
                    var counters=unit.designationSource=="Army"?origin.counters:unit.designationSource=="Town"?candidate.townNaming.Find(n=>n.owner==army.owner&&n.regionId==unit.originTownId)?.counters:null;
                    if(!identities.Add(army.owner+"/"+unit.designationSource+"/"+(unit.designationSource=="Town"?unit.originTownId:unit.originArmyId)+"/"+unit.designationType+"/"+unit.designationSequence))return false;
                    if(counters==null||unit.designationSequence<1||!counters.Any(c=>c.type==unit.designationType&&c.issued>=unit.designationSequence))return false;
                }
            }
            return true;
        }
        public bool RenameArmy(string actor,string armyId,string name,out string reason)
        {
            name=name?.Trim();var army=Army(armyId);reason="Enter an army name of 1–60 characters during your turn.";
            if(!CanOrder(actor)||army==null||army.owner!=actor||!EmpireBattle.SavedRegiment.SafeName(name,60))return false;
            if(Save.armies.Any(a=>a.owner==actor&&a.id!=armyId&&string.Equals(a.name,name,StringComparison.OrdinalIgnoreCase)))
            {reason="Another army already uses that name.";return false;}
            army.name=name;army.customName=true;reason="Army renamed to “"+name+"”.";return true;
        }
        public bool RenameRegiment(string actor,string armyId,string unitId,string name,out string reason)
        {
            name=name?.Trim();var army=Army(armyId);var unit=army?.units.Find(u=>u.id==unitId);reason="Enter a regiment name of 1–60 characters while this army is idle.";
            if(!CanOrder(actor)||army==null||army.owner!=actor||Committed(army)||unit==null||!EmpireBattle.SavedRegiment.SafeName(name,60))return false;
            if(Save.armies.Where(a=>a.owner==actor).SelectMany(a=>a.units).Any(u=>u.id!=unitId&&string.Equals(u.name,name,StringComparison.OrdinalIgnoreCase)))
            {reason="Another regiment already uses that name.";return false;}
            if(!NameIssued(Save,actor,name))Save.armyNaming.Find(n=>n.armyId==unit.originArmyId).issuedNames.Add(name);
            unit.name=name;reason="Regiment renamed to “"+name+"”.";return true;
        }

        // Authored commanders for this fictional opening; detachments do not require a general.
        static string GeneralName(string owner,int index)=>owner=="france"?(index%2==0?"General Beaumont":"General Moreau"):
            owner=="austria"?(index%2==0?"General Adler":"General Weiss"):"General "+owner;
        public bool InTransit(CampaignArmy army)=>army!=null&&(AtSea(army));
        public float MarchLegDistance(CampaignArmy army)=>army.marchNext!=""?(float)Math.Ceiling(graph[army.regionId][army.marchNext]):0;
        bool ValidMarch(CampaignArmy a)=>a.marchTarget!=null&&a.marchNext!=null&&!float.IsNaN(a.marchProgress)&&!float.IsInfinity(a.marchProgress)&&
            a.marchProgress>=0&&a.marchProgress==(float)Math.Floor(a.marchProgress)&&(a.marchTarget==""||Active(a.marchTarget))&&
            (a.marchNext==""?a.marchProgress==0:Active(a.marchNext)&&graph[a.regionId].ContainsKey(a.marchNext)&&
                a.marchProgress<Math.Ceiling(graph[a.regionId][a.marchNext])&&a.marchTarget!="");
        public bool Detach(string actor,string armyId,string[] regimentIds,out string detachedId,out string reason)
        {
            detachedId="";reason="Select regiments from a stationary force.";var source=Army(armyId);
            if(!CanOrder(actor)||source==null||source.owner!=actor||Committed(source)||InTransit(source)||regimentIds==null||regimentIds.Length==0||
                regimentIds.Distinct().Count()!=regimentIds.Length||regimentIds.Any(id=>!source.units.Any(u=>u.id==id)))return false;
            if(regimentIds.Length==source.units.Count){reason="Keep at least one regiment with the original force.";return false;}
            var detached=NewArmy(actor,source.regionId);
            Place(detached,Position(source));detached.movementDebt=source.movementDebt;ClearMarch(source);detached.movement=source.movement;detached.supply=source.supply;detached.lastRecoveryTurn=source.lastRecoveryTurn;
            detached.units=source.units.Where(u=>regimentIds.Contains(u.id)).ToList();source.units.RemoveAll(u=>regimentIds.Contains(u.id));
            detachedId=detached.id;reason="Detachment formed. It can march and reinforce without a general.";return true;
        }
        public bool Transfer(string actor,string sourceId,string targetId,string[] regimentIds,out string reason)
        {
            reason="Forces must meet in the field; maximum 80 regiments.";var source=Army(sourceId);var target=Army(targetId);
            if(!CanOrder(actor)||source==null||target==null||source==target||source.owner!=actor||target.owner!=actor||!ForcesCanMeet(source,target)||
                Committed(source)||Committed(target)||InTransit(source)||InTransit(target)||regimentIds==null||regimentIds.Length==0||
                regimentIds.Distinct().Count()!=regimentIds.Length||regimentIds.Any(id=>!source.units.Any(u=>u.id==id))||target.units.Count+regimentIds.Length>MaxArmyRegiments)return false;
            int transferring=source.units.Where(u=>regimentIds.Contains(u.id)).Sum(u=>u.soldiers);
            int total=transferring+target.Soldiers;
            target.supply=(source.supply*transferring+target.supply*target.Soldiers)/Math.Max(1,total);
            target.movementDebt=Math.Max(target.movementDebt,source.movementDebt);ClearMarch(source);ClearMarch(target);target.movement=Math.Min(target.movement,source.movement);target.lastRecoveryTurn=Math.Max(target.lastRecoveryTurn,source.lastRecoveryTurn);
            target.units.AddRange(source.units.Where(u=>regimentIds.Contains(u.id)));source.units.RemoveAll(u=>regimentIds.Contains(u.id));
            if(source.units.Count==0){if(target.generalName==""){target.generalName=source.generalName;target.commanderXP=source.commanderXP;target.commanderWounds=source.commanderWounds;target.commanderWins=source.commanderWins;target.commanderTrait=source.commanderTrait;}Save.armies.Remove(source);}
            reason="Regiments transferred with their strength, equipment and experience.";return true;
        }
        public bool CommissionGeneral(string actor,string armyId,out string reason)
        {
            reason="A general costs 200 treasury and requires a stationary force in friendly territory.";var a=Army(armyId);
            if(!CanOrder(actor)||a==null||a.owner!=actor||a.generalName!=""||Committed(a)||InTransit(a)||a.marchTarget!=""||World.State(a.regionId).owner!=actor||World.FactionState(actor).treasury<200)return false;
            World.FactionState(actor).treasury-=200;a.generalName=GeneralName(actor,Save.nextId);
            reason=a.generalName+" appointed.";return true;
        }
        public bool Halt(string actor,string armyId,out string reason)
        {
            reason="No march to cancel.";var a=Army(armyId);
            if(!CanOrder(actor)||a==null||a.owner!=actor||a.marchTarget=="")return false;
            ClearMarch(a);reason="March halted at its current position; spent movement remains spent.";return true;
        }
        public bool March(string actor,string armyId,string destination,out string reason)
        {
            reason="Choose a settlement in the active campaign.";if(!Active(destination))return false;
            var p=CityPosition(destination);return MarchTo(actor,armyId,p.lon,p.lat,out reason,settlement:true);
        }
        bool AdvanceMarch(CampaignArmy a,out string reason)=>AdvanceLandMarch(a,out reason);
        float KnownDefense(string actor,string region)
        {
            float field=Save.armies.Where(a=>!AtSea(a)&&(a.regionId==region||CanSupport(a,region))&&AtWar(actor,a.owner)&&CanObserve(a,actor)).Sum(ArmyPower);
            int fort=Buildings(region,"Fortress");float garrison=field>0&&fort==0?0:(250+fort*200)*.8f;
            return (field+garrison)*(1+fort*.2f)+fort*400;
        }
    }
}
