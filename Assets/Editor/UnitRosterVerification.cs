using System;
using System.IO;
using System.Collections.Generic;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class UnitRosterVerification
{
    public static void Verify()
    {
        int sets=0,poses=0;
        foreach(string faction in new[]{"france","britain","austria","prussia","russia","italy","hungary","ottoman","generic"})
        foreach(string family in new[]{"line","light","sword"})
        {
            if(family=="line"&&(faction=="france"||faction=="britain"))continue;
            var asset=UnityEditor.AssetDatabase.LoadAssetAtPath<UnitVisualLibrary>("Assets/Generated/UnitRoster/"+faction+"-"+family+"/Library.asset");
            Require(asset&&asset.Set!=null&&asset.Set.Material&&asset.Set.Lods.Length==2,"complete roster library "+faction+" "+family);
            Require(asset.BakeVersion>=(family=="line"?2:1),"completed bake version "+faction+" "+family);
            sets++;
            foreach(var lod in asset.Set.Lods)
            {
                Require(lod.Melee!=null&&lod.Melee.Length==6,"melee clips");
                var sequences=new List<Mesh[]> {lod.Idle,lod.March,lod.Run,lod.Charge,lod.Hit,lod.Death,lod.Knockdown,lod.GetUp};
                foreach(var clip in lod.Melee)sequences.Add(clip.Poses);
                if(family!="sword")sequences.AddRange(new[]{lod.Fire,lod.Reload,lod.KneelingFire,lod.KneelingReload});
                if(family=="line")
                {
                    Require(lod.Command!=null&&lod.Command.Length==5,"regional command variants");
                    foreach(int role in new[]{1,2,4})
                    {
                        Require(lod.Command[role]!=null,"musician, colour and colonel");
                        sequences.AddRange(new[]{lod.Command[role].Idle,lod.Command[role].March});
                    }
                    var officer=lod.Command[0];var pike=lod.Command[3];
                    Require(officer!=null&&pike!=null,"regional officer and pike");
                    sequences.AddRange(new[]{officer.Idle,officer.March,officer.OfficerReady,officer.OfficerSlash,officer.OfficerFire,officer.OfficerKnockdown,officer.OfficerGetUp,officer.OfficerDeath,officer.KneelingIdle,officer.KneelingStrike,
                        pike.Idle,pike.March,pike.PikeReady,pike.PikeThrust,pike.PikeKnockdown,pike.PikeGetUp,pike.PikeDeath,pike.KneelingIdle,pike.KneelingStrike});
                }
                foreach(var sequence in sequences)
                {
                    Require(sequence!=null&&sequence.Length>=4,"complete animation sequence");
                    foreach(var mesh in sequence)
                    {
                        Require(mesh&&mesh.vertexCount>0,"nonempty pose");
                        var targets=new List<Vector3>();mesh.GetUVs(2,targets);Require(targets.Count==mesh.vertexCount,"GPU interpolation targets");
                        foreach(var v in mesh.vertices)Require(!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.sqrMagnitude),"finite pose");
                        poses++;
                    }
                }
            }
        }
        VerifyRules();
        Directory.CreateDirectory("Artifacts/UnitRoster");File.WriteAllText("Artifacts/UnitRoster/verification.json","{\"sets\":"+sets+",\"poses\":"+poses+",\"rules_passed\":true}\n");
        Debug.Log("UNIT_ROSTER_VERIFIED sets="+sets+" poses="+poses);
    }
    public static void VerifyRules()
    {
        foreach(string faction in UnitRoster.Factions)
        {
            var config=UnitRoster.CreateBattle(faction,faction);config.EnableLandscape=false;config.EnableAI=false;
            var world=new BattleWorld(config);var combat=new CombatSystem(world);
            Require(world.CountAlive(ArmySide.French)==world.CountAlive(ArmySide.British),"mirrored faction strengths");
            var sword=world.Battalions[6];Require(!sword.FireAtWill&&sword.Ammunition==0&&!world.CanForm(sword,FormationKind.Square),"sword weapon restrictions");
            Require(world.LivingSergeants(sword)==0&&world.LivingCaptains(sword)==0,"sword units have no pike or pistol substitutions");
            Require(world.IssueRangedAttack(6,14)&&sword.AttackMode==InfantryAttackMode.BayonetCharge,"sword attack orders close for melee");
            Require(world.Battalions[2].Formation==FormationKind.Skirmish&&world.Battalions[0].Formation==FormationKind.Line,"role deployment");
            if(!RegionalArmyRoster.Supports(faction))Require(world.EffectiveRange(world.Battalions[3])>world.EffectiveRange(world.Battalions[0])&&world.BaseReload(world.Battalions[3])>world.BaseReload(world.Battalions[0]),"rifle range/reload tradeoff");
            else Require(world.Battalions[3].Role==UnitRole.Skirmisher,"regional light musketeers use musket doctrine");
            for(int i=0;i<20;i++){world.StepMovement(.05f);combat.Step(.05f);}
            Require(sword.ShotsFired==0,"swords never fire");world.MovementJobs?.Dispose();
        }
        Require(UnitRoster.Melee(UnitRole.Swordsman)>UnitRoster.Melee(UnitRole.Line)&&UnitRoster.Melee(UnitRole.Rifleman)<UnitRoster.Melee(UnitRole.Line),"melee role tradeoffs");
        Require(UnitRoster.Cost(UnitRole.Guard)>UnitRoster.Cost(UnitRole.Line)&&UnitRoster.Cost(UnitRole.Militia)<UnitRoster.Cost(UnitRole.Line),"cost tradeoffs");
        Debug.Log("UNIT_ROSTER_RULES_VERIFIED");
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception("Unit roster: "+message);}
}
