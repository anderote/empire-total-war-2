using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EmpireBattle;
using EmpireGame;
using Empire.UI;
using EmpireCampaign.Geography;
using UnityEngine;

public static class RegimentLibraryVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Regiment library: "+message);}
    static SavedRegiment Fixture()
    {
        var company=new GloryCompany();var unit=company.Save.regiments[0];unit.progression.Award(1850);unit.progression.Train(UnitSkill.Health);unit.progression.Train(UnitSkill.Ammunition);unit.progression.Learn(RegimentAbility.SteadyAim);
        var record=new GloryRegimentSource(company,()=>true).Units[0];return RegimentLibrary.Capture(record,"The Iron First","Regimental Glory / Company");
    }
    public static void Verify()
    {
        string dir=Path.GetFullPath("Artifacts/RegimentLibraryVerification/temporary-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var saved=Fixture();Require(saved.Valid,"valid snapshot with both point pools");
            var library=new RegimentLibrary(Path.Combine(dir,"library.json"));Require(library.Store(saved,"",out var why),why);
            saved.progression.Train(UnitSkill.Health);var archived=library.Entries.Single();Require(archived.progression.health==1,"saved snapshots are independent copies");
            var reread=new RegimentLibrary(Path.Combine(dir,"library.json"));Require(reread.Entries.Single().progression.Has(RegimentAbility.SteadyAim),"atomic library JSON round trip preserves abilities");
            var changed=archived.Copy();changed.name="The Iron First, veterans";Require(reread.Store(changed,archived.id,out why)&&reread.Entries.Count==1&&reread.Entries[0].id==archived.id,"explicit update retains library identity");
            var alien=changed.Copy();alien.users=EquipmentUsers.Artillery;Require(!reread.Store(alien,"",out why),"reject incompatible unit masks");
            string malformed=Path.Combine(dir,"broken.json");File.WriteAllText(malformed,"{broken");var broken=new RegimentLibrary(malformed);Require(!broken.Writable&&!broken.Store(archived,"",out why)&&File.ReadAllText(malformed)=="{broken","corrupt library cannot be silently overwritten");
            var world=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=40,FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
            string armoryPath=Path.Combine(dir,"armory.json");var session=new BattleArmorySession(world,new ArtillerySystem(world),armoryPath);var entry=session.Entries[0];int money=session.Save.treasury;
            Require(session.Import(entry,archived)&&world.Battalions[0].Name==archived.name&&world.Battalions[0].AliveCount==40&&world.Soldiers.Health[0]==105&&world.Battalions[0].Ammunition==31,"saved Glory regiment loads actual battle stats without changing scenario troop strength");
            Require(session.Save.treasury==money-archived.KitCost-archived.VeteranCost&&world.Battalions[0].Progression.Has(RegimentAbility.SteadyAim),"import is paid and preserves abilities");
            var rifle=archived.Copy();rifle.role=UnitRole.Rifleman;rifle.users=EquipmentUsers.Rifle;rifle.weapon="rifle";
            Require(session.Import(entry,rifle)&&world.Battalions[0].Role==UnitRole.Rifleman&&world.Battalions[0].Weapon.Id=="rifle","saved role and default weapon replace destination role");
            var reloadWorld=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=1,SoldiersPerBattalion=40,FrenchArtillery=0,BritishArtillery=0,EnableLandscape=false});
            var reloaded=new BattleArmorySession(reloadWorld,new ArtillerySystem(reloadWorld),armoryPath);Require(reloaded.CanChange&&reloadWorld.Battalions[0].Role==UnitRole.Rifleman&&reloadWorld.Battalions[0].Progression.ammunition==1,"imported role survives standalone armory reload without a type-mismatch lock");
            var modern=archived.Copy();modern.loadout.Equip("magazine-rifles",modern.users);Require(!session.CanImport(entry,modern,out why),"battle imports respect equipment era");
            session.Save.year=1918;Require(session.Import(entry,modern)&&world.Battalions[0].Ammunition==38&&world.Soldiers.MagazineRounds[0]==8,"reserve magazines retain correct ammunition semantics");
            Require(session.Import(entry,archived)&&world.Battalions[0].Weapon.Id=="musket","loading standard issue removes a previous magazine weapon");
            world.StepMovement(.1f);Require(!session.Import(entry,archived),"cannot overwrite a regiment midbattle");
            VerifyCampaign(archived,modern);
            var company=new GloryCompany();company.Save.coins=3000;Require(company.RecruitSaved(archived)&&company.Save.regiments.Count==2&&company.Save.regiments[1].progression.ammunition==1,"library can recruit paid full-strength Glory copies");
            company.Save.regiments[1].progression.Train(UnitSkill.Accuracy);Require(archived.progression.accuracy==0,"imported instances never mutate archived progression");
            Require(reread.Delete(archived.id,out why)&&reread.Entries.Count==0&&world.Battalions[0].Progression.ammunition==1,"deleting archive leaves active regiments intact");
            Debug.Log("REGIMENT_LIBRARY_VERIFIED: archive/update/delete and atomic saves; corrupt-file preservation; copied progression; paid role/weapon/health/ammunition battle imports; campaign training and native save restoration; independent Glory recruitment.");
        }
        finally {Directory.Delete(dir,true);}
    }
    static void VerifyCampaign(SavedRegiment saved,SavedRegiment modern)
    {
        var campaign=new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),JsonUtility.FromJson<TheaterData>(Resources.Load<TextAsset>("WorldGeography/world").text),CampaignBalance.Load(),"france");
        var region=campaign.World.State("region-paris");int treasury=campaign.World.FactionState("france").treasury,enlisted=region.economy.enlisted;float materials=region.economy.materials;
        Require(!campaign.CanRecruitSaved("france",region.regionId,modern,out var why),"campaign blocks future equipment before payment");
        Require(campaign.RecruitSaved("france",region.regionId,saved,out why),why);
        Require(campaign.World.FactionState("france").treasury==treasury-campaign.SavedRecruitCost(saved)&&region.economy.materials==materials-campaign.SavedRecruitMaterials(saved)&&region.economy.enlisted==enlisted+campaign.Balance.unitSize,"campaign import reserves real treasury, material and manpower");
        var needsResearch=saved.Copy();needsResearch.weapon="rifle";
        Require(!campaign.CanRecruitSaved("france",region.regionId,needsResearch,out why),"base weapons also require research even without an equipment item");
        var candidate=JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(campaign.Save));Require(campaign.Adopt(candidate,out why),"queued library recruitment survives native save: "+why);
        Require(!campaign.RecruitSaved("france",region.regionId,saved,out why),"training capacity prevents duplicate queued imports");
        var forged=JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(campaign.Save));forged.recruitment[0].paidCost=1;Require(!campaign.Adopt(forged,out why),"tampered library payments are rejected");
        var finish=typeof(CountryCampaign).GetMethod("FinishRound",BindingFlags.Instance|BindingFlags.NonPublic);for(int i=0;i<campaign.Balance.trainingTurns;i++)finish.Invoke(campaign,null);
        var recruited=campaign.Save.armies.SelectMany(a=>a.units).Single(u=>u.name==saved.name);
        Require(recruited.soldiers==campaign.Balance.unitSize&&recruited.progression.ammunition==1&&recruited.progression.health==1&&recruited.progression.Has(RegimentAbility.SteadyAim),"normal training completion creates full campaign regiment with saved upgrades");
        candidate=JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(campaign.Save));Require(campaign.Adopt(candidate,out why),"completed library regiment survives native save: "+why);
        Require(UnitRoster.CampaignRole("Guard","musket")==UnitRole.Guard&&UnitRoster.CampaignRole("Light","musket")==UnitRole.Skirmisher,"campaign preserves imported infantry specializations");
    }
}
