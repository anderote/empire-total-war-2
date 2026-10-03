using System;
using System.Linq;
using Empire.UI;
using EmpireBattle;
using EmpireGame;
using UnityEngine;

// In-memory battle checks only; no player session, campaign save or build is opened.
public static class RifleUpgradeVerification
{
    static readonly string[] Tiers={"musket","percussion","rifle","rifle-musket","breechloader","magazine","smokeless"};
    static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Rifle upgrades: "+message);}

    public static void Verify()
    {
        foreach(float distance in new[]{0f,30f,75f,150f})foreach(float sample in new[]{0f,.5f,1f})
            Require(WeaponBallistics.SmallArmsDamage(distance,150,sample,"musket",UnitKind.Infantry)==WeaponBallistics.MusketDamage(distance,150,sample)+35,"musket damage baseline is unchanged");
        VerifyTierBenefits();
        VerifyEquipmentAndPreview();
        VerifyPromotions();
        foreach(var mode in new[]{InfantryFireMode.Volley,InfantryFireMode.FireAtWill})
        {
            var musket=Measure("musket",mode);
            foreach(string id in Tiers.Skip(1))
            {
                var upgraded=Measure(id,mode);
                Require(upgraded.hitRate>musket.hitRate*(id=="percussion"?1.01f:1.4f),id+" increases actual hit rate in "+mode);
                Require(upgraded.damage>musket.damage*1.04f,id+" increases actual damage per hit in "+mode);
                if(id=="magazine"||id=="smokeless")
                    Require(upgraded.hitRate>musket.hitRate*5&&upgraded.damage>musket.damage*1.5f,"magazine fire is substantially more accurate and damaging in "+mode);
            }
        }
        Debug.Log("RIFLE_UPGRADES_VERIFIED tier accuracy and damage, actual volley/independent hits, upgrade previews, paid issue, finite magazine cycling and musket baseline");
    }

    static void VerifyTierBenefits()
    {
        for(int tier=1;tier<Tiers.Length;tier++)
        {
            var previous=MilitaryEquipment.Weapon(Tiers[tier-1]);var weapon=MilitaryEquipment.Weapon(Tiers[tier]);
            Require(weapon.Precision>previous.Precision&&weapon.DamageMultiplier>previous.DamageMultiplier,"each tier improves precision and damage: "+weapon.Id);
            Require(weapon.EffectiveRange>=previous.EffectiveRange&&weapon.AimedRange>=previous.AimedRange,"upgrades preserve or extend range");
            foreach(float distance in new[]{0f,30f,75f,150f,300f})foreach(float sample in new[]{0f,.5f,1f})
                foreach(var target in new[]{UnitKind.Infantry,UnitKind.Cavalry})
                    Require(WeaponBallistics.SmallArmsDamage(distance,weapon.EffectiveRange,sample,weapon.Id,target)
                        >WeaponBallistics.SmallArmsDamage(distance,previous.EffectiveRange,sample,previous.Id,target),"higher tiers never lose per-hit damage at any tested range");
        }
    }

    static void VerifyPromotions()
    {
        foreach(string weapon in Tiers.Skip(1))
        {
            var company=new GloryCompany();company.Save.coins=1000000;company.Save.commanderXP=100000;
            var regiment=company.Save.regiments[0];regiment.progression.Award(1000000);
            regiment.progression.Train(UnitSkill.Health);regiment.progression.Train(UnitSkill.Accuracy);regiment.progression.Train(UnitSkill.Drill);
            regiment.quality=2;regiment.loadout.wear=35;
            var item=UnitEquipment.Items.Single(i=>i.Weapon==weapon);
            Require(company.Equip(regiment,item.Id),"purchase rifle before promotion");
            string progression=JsonUtility.ToJson(regiment.progression);
            for(int tier=1;tier<8;tier++)
            {
                var before=RegimentBaseStats.For(tier,GloryCompany.Roles[tier-1]);
                Require(company.Upgrade(regiment),"promotion succeeds for "+weapon+" to tier "+(tier+1));
                Require(regiment.loadout.items.Contains(item.Id)&&regiment.quality==2&&regiment.loadout.wear==35
                    &&JsonUtility.ToJson(regiment.progression)==progression,"promotion preserves paid rifle, quality, wear and earned progression");
                var after=RegimentBaseStats.For(tier+1,GloryCompany.Roles[tier]);
                Require(after.Health>before.Health&&after.Accuracy>before.Accuracy&&after.Melee>before.Melee&&after.Resolve>before.Resolve&&after.Reload<before.Reload,"promotion improves base stats independently of equipment");
                Require(RegimentBaseStats.Uniform(tier+1)!=RegimentBaseStats.Uniform(tier),"promotion changes uniform independently of rifle");
                if(tier==1)Require(company.Equip(regiment,"range-sights"),"buy specialist kit before later role promotions");
                Require(GloryCompany.Valid(company.Save),"promoted regiment remains saveable with retained specialist kit");
            }
            company.Save.lastBattle=GloryBattleReport.Capture(company.Save,company.Choices()[0]);
            var restored=new GloryCompany(JsonUtility.FromJson<GlorySave>(JsonUtility.ToJson(company.Save)));
            var world=new BattleWorld(restored.Deployment(restored.Choices()[0]));var unit=world.Battalions[0];
            var record=new GloryRegimentSource(restored,()=>true).Units[0];var stats=RegimentPreview.Stats(record);
            Require(unit.Weapon.Id==weapon&&unit.TroopTier==8&&unit.Loadout.items.Contains("range-sights"),"save/reload and deployment preserve purchased rifle, specialist kit and promoted tier");
            Require(unit.MaxHealth==205&&world.Soldiers.Health[unit.SoldierStart]==205,"trained health adds to the promoted 200-health base");
            Require(Mathf.Abs(stats.Single(s=>s.Name=="Accuracy").Value-world.Precision(unit))<.001f,"tier, rifle, XP, quality and kit accuracy stack identically in preview and combat");
            Require(Mathf.Abs(stats.Single(s=>s.Name=="Reload").Value-world.BaseReload(unit))<.001f,"promoted reload base stacks with drill and equipment");
        }
        Debug.Log("REGIMENT_PROMOTIONS_VERIFIED purchased rifles and specialist kit retained across all seven infantry promotions, base stats, uniforms, saves and deployed modifiers");
    }

    static BattleWorld Fixture(string weapon,int seed=1815)
    {
        var world=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=128,
            FormationWeapons=new[]{weapon,"musket"},FrenchArtillery=0,BritishArtillery=0,
            EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false,Seed=seed});
        foreach(var unit in world.Battalions)unit.Files=64;
        MeleeEncounter.Place(world,world.Battalions[0],Vector2.zero,0);
        MeleeEncounter.Place(world,world.Battalions[1],new Vector2(0,150),Mathf.PI);
        world.Battalions[1].FireAtWill=false;
        return world;
    }

    static void VerifyEquipmentAndPreview()
    {
        var world=Fixture("musket");var session=new BattleArmorySession(world,new ArtillerySystem(world),null);
        session.Save.year=1918;var entry=session.Entries[0];var unit=world.Battalions[0];
        entry.quality=2;entry.progression.Award(850);entry.progression.Train(UnitSkill.Accuracy);entry.progression.Train(UnitSkill.Drill);
        foreach(string id in Tiers.Skip(1))
        {
            var item=UnitEquipment.Items.Single(i=>i.Weapon==id);
            var record=new RegimentRecord {Weapon="musket",Users=EquipmentUsers.Line,BattleRole=UnitRole.Line,
                Quality=entry.quality,Progression=entry.progression,Loadout=entry.loadout};
            var before=RegimentPreview.Stats(record);var preview=RegimentPreview.Stats(record,item);
            Require(before.Select(s=>s.Name).SequenceEqual(preview.Select(s=>s.Name)),"comparison rows stay aligned across weapons");
            Require(!entry.loadout.items.Contains(item.Id),"preview does not issue equipment");
            Require(RegimentUpgradeChoices.Changes(before,preview).Contains("Shot damage"),"upgrade preview calls out damage");
            int funds=session.Save.treasury;
            Require(session.Equip(entry,item.Id)&&session.Save.treasury==funds-item.Cost,"upgrade is purchased and issued");
            float Stat(string name)=>preview.Single(s=>s.Name==name).Value;
            Require(unit.Weapon.Id==id&&Mathf.Abs(Stat("Accuracy")-world.Precision(unit))<.001f,"preview precision matches deployed weapon and training");
            Require(Mathf.Abs(Stat("Shot damage")-WeaponBallistics.SmallArmsDamage(world.EffectiveRange(unit),world.EffectiveRange(unit),.5f,id,UnitKind.Infantry))<.001f,"preview damage matches combat");
            Require(Mathf.Abs(Stat("Reload")-world.BaseReload(unit))<.001f,"preview reload matches deployed weapon");
            Require(Stat("Capacity")==unit.Weapon.Magazine,"preview distinguishes capacity from reserves");
            Require(UnitEquipment.StatText(item).Contains("precision")&&UnitEquipment.StatText(item).Contains("damage"),"catalogue explains both offensive benefits");
            if(unit.Weapon.Magazine<=1)continue;
            int soldier=unit.SoldierStart,rounds=world.Soldiers.RoundsRemaining[soldier];
            for(int shot=0;shot<8;shot++)
            {
                world.Soldiers.RoundsRemaining[soldier]--;
                float duration=world.ReloadAfterShot(unit,soldier);
                Require(Mathf.Abs(duration-(shot<7?Stat("Shot interval"):Stat("Reload")))<.001f,"seven quick bolt cycles followed by a full refill");
            }
            Require(world.Soldiers.RoundsRemaining[soldier]==rounds-8&&world.Soldiers.MagazineRounds[soldier]==8,"magazine reload conserves ammunition");
        }
        var baseline=RegimentPreview.Stats(new RegimentRecord {Weapon="musket",Users=EquipmentUsers.Line,Progression=new UnitProgression(),Loadout=new UnitLoadout()});
        Require(baseline.Single(s=>s.Name=="Shot damage").Value==125,"baseline musket damage remains unchanged");
    }

    static (float hitRate,float damage) Measure(string weapon,InfantryFireMode mode)
    {
        int shots=0,hits=0;float damage=0;
        for(int trial=0;trial<32;trial++)
        {
            var world=Fixture(weapon,1815+trial*37);var unit=world.Battalions[0];var target=world.Battalions[1];unit.FireMode=mode;
            // Tough targets isolate per-hit damage from lethal overkill and casualty selection.
            for(int i=target.SoldierStart;i<target.SoldierStart+target.SoldierCount;i++)world.Soldiers.Health[i]=10000;
            var combat=new CombatSystem(world);combat.Step(.05f);
            Require(unit.ShotsFired>0&&target.AliveCount==target.SoldierCount,"fixture fires without removing its targets");
            int fired=unit.ShotsFired;combat.Step(.05f);
            Require(unit.ShotsFired==fired,"weapon upgrades cannot bypass reload clocks");
            shots+=unit.ShotsFired;hits+=unit.Hits;
            for(int i=target.SoldierStart;i<target.SoldierStart+target.SoldierCount;i++)damage+=10000-world.Soldiers.Health[i];
        }
        Require(hits>0,"measurement has successful hits");
        float rate=hits/(float)shots,perHit=damage/hits;
        Debug.Log($"RIFLE_TIER weapon={weapon} mode={mode} range=150m shots={shots} hitRate={rate:P1} damagePerHit={perHit:F1}");
        return (rate,perHit);
    }
}
