using System;
using System.IO;
using System.Linq;
using Empire.UI;
using EmpireBattle;
using EmpireGame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class RegimentUpgradeMenuVerification
{
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Verify()
    {
        var company=new GloryCompany();company.Save.coins=10000;company.Save.commanderXP=8000;Require(company.Recruit(0),"Fixture recruits second regiment");
        var first=company.Save.regiments[0];first.progression.Award(8000);company.Equip(first,"stout-boots-level-3");
        int saves=0;var source=new GloryRegimentSource(company,()=>{saves++;return true;});var record=source.Units[0];
        string before=JsonUtility.ToJson(company.Save);
        var choices=RegimentUpgradeChoices.Equipment(record,false);
        Require(choices.Count(i=>i.Family=="stout-boots")==1&&choices.Any(i=>i.Id=="stout-boots-level-4"),"Suggested list exposes only the next boot grade");
        Require(RegimentUpgradeChoices.Equipment(record,true).Count(i=>i.Family=="stout-boots")==UnitEquipment.MaxEquipmentLevel,"Full catalogue retains all upgrade levels");
        var trained=RegimentUpgradeChoices.WithSkill(record,UnitSkill.Health);
        Require(RegimentPreview.Stats(trained).First(s=>s.Name=="Health").Value==RegimentPreview.Stats(record).First(s=>s.Name=="Health").Value+5,"Training preview uses actual next health value");
        Require(trained.Progression.AvailablePoints==record.Progression.AvailablePoints-1,"Preview spends only its copied point");
        Require(RegimentUpgradeChoices.Comparison(new RegimentStat("Speed",1.028f,"0.00'×'",""),new RegimentStat("Speed",1.032f,"0.00'×'","")).Contains("1.028× → 1.032×"),"Small improvements remain visible when ordinary rounding would hide them");
        Require(!RegimentUpgradeChoices.Skills(new RegimentRecord{Users=EquipmentUsers.Artillery}).Contains(UnitSkill.Health),"Artillery does not offer soldier health training");
        Require(!RegimentUpgradeChoices.Skills(new RegimentRecord{Users=EquipmentUsers.Cavalry,BattleRole=UnitRole.Cavalry}).Contains(UnitSkill.Ammunition),"Melee cavalry does not offer ammunition training");
        var panel=new RegimentManagementPanel();panel.Select(record.Id);panel.Inspect(source.Units[1].Id);
        Require(panel.EquipmentRecipientIds.SequenceEqual(new[]{source.Units[1].Id}),"Normal inspection keeps gear recipients on the inspected regiment");
        panel.SetEquipmentRecipients(source.Units.Select(u=>u.Id));panel.Inspect(record.Id);
        Require(panel.EquipmentRecipientIds.Length==2,"Group inspection preserves explicit checks");
        panel.UseCheckedGroup(false);Require(panel.EquipmentRecipientIds.SequenceEqual(new[]{record.Id}),"Leaving group mode returns to the inspected regiment");
        panel.InspectEquipment("magazine-rifles");source.Equipment(new[]{record.Id},UnitEquipment.Find("magazine-rifles"));
        Require(JsonUtility.ToJson(company.Save)==before&&saves==0,"Inspecting, quoting and stat comparisons mutate neither funds nor regiments");
        var boots=UnitEquipment.Find("stout-boots-level-3");var order=source.Equipment(source.Units.Select(u=>u.Id).ToArray(),boots);int coins=company.Save.coins;
        Require(order.Enabled,"Mixed group accepts a catch-up order");order.Execute();
        Require(company.Save.coins==coins-boots.Cost&&saves==1,"Explicit group order skips issued gear, charges eligible recipients and saves once");
        company.Save.coins=0;Require(!source.Equipment(new[]{record.Id},UnitEquipment.Find("stout-boots-level-4")).Enabled,"Unaffordable gear stays inspectable with ordering disabled");
        var groupQuote=source.Equipment(source.Units.Select(u=>u.Id).ToArray(),UnitEquipment.Find("stout-boots-level-4"));
        Require(!groupQuote.Enabled&&groupQuote.Reason.Contains(groupQuote.Cost),"Unaffordable group explains the complete order cost, even when one item is already unaffordable");
        Require(RegimentUpgradeChoices.AbilityEffect(RegimentAbility.QuickHands,RegimentAbilities.MaxRank).StartsWith("80%"),"Rank preview respects the combat cap");
        Debug.Log("REGIMENT_UPGRADE_MENU_CHECKS_PASSED");
    }
    public static void Build()
    {
        Verify();const string scenePath="Assets/Scenes/__RegimentUpgradeReview.unity";
        if(File.Exists(scenePath))throw new Exception("Review scene path is already occupied; preserve it.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Regiment Upgrade Review");var camera=root.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=EmpireTheme.Ink;camera.cullingMask=0;
            root.AddComponent<RegimentUpgradeReview>();EditorSceneManager.SaveScene(scene,scenePath);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scenePath},
                locationPathName="Artifacts/RegimentUpgradeReview/Regiment Upgrade Review.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Regiment upgrade review build failed.");
            Debug.Log("REGIMENT_UPGRADE_REVIEW_BUILD_PASSED");
        }
        finally
        {
            if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);
            AssetDatabase.DeleteAsset(scenePath);
        }
    }
}
