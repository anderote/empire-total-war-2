using System;
using System.IO;
using System.Collections.Generic;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;
using UnityEditor;

public static class RegionalArmyVerification
{
    static int poses;
    public static void Verify()
    {
        poses=0;
        foreach(var faction in new[]{"ming","india"})
        {
            foreach(var family in new[]{"line","light","sword"})
            {
                var a=Resources.Load<UnitVisualLibrary>("RegionalRoster/"+faction+"-"+family+"/Library");
                Require(a&&a.BakeVersion>0&&a.Set.Material&&a.Set.Material.GetTexture("_BaseMap"),"infantry atlas "+faction+family);
                Require(a.Set.Lods.Length==2,"two infantry LODs");
                foreach(var l in a.Set.Lods)
                {
                    foreach(var sequence in new[]{l.Idle,l.March,l.Run,l.Charge,l.Hit,l.Death,l.Knockdown,l.GetUp})Check(sequence);
                    Require(l.Melee!=null&&l.Melee.Length==6,"six melee clips");foreach(var c in l.Melee)Check(c.Poses);
                    if(family!="sword")foreach(var sequence in new[]{l.Fire,l.Reload,l.KneelingFire,l.KneelingReload})Check(sequence);
                }
                Require(a.Set.Lods[1].Idle[0].vertexCount<a.Set.Lods[0].Idle[0].vertexCount,"reduced infantry LOD");
            }
            var cavalry=Resources.Load<CavalryCrowdLibrary>("RegionalCavalry/"+faction+"/CavalryCrowdLibrary");
            Require(cavalry&&cavalry.French.Material&&cavalry.French.Lods.Length==2,"cavalry library");
            foreach(var l in cavalry.French.Lods)foreach(MountedMotion motion in Enum.GetValues(typeof(MountedMotion)))
            {var clip=l.Clip(motion);Require(clip!=null,"mounted clip "+motion);Check(clip.Poses);}
        }
        UnitRosterVerification.VerifyRules();
        var config=UnitRoster.CreateBattle("ming","india");config.EnableLandscape=false;config.EnableAI=false;config.InitialSeparation=60;config.FrenchArtillery=config.BritishArtillery=0;
        var world=new BattleWorld(config);var combat=new CombatSystem(world);
        try
        {
            Require(world.CountAlive(ArmySide.French)==world.CountAlive(ArmySide.British),"balanced armies");
            var infantry=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
            var cavalry=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
            foreach(var unit in world.Battalions)
            {
                Require(RegionalArmyRoster.Supports(unit.Faction),"regional faction retained");
                if(unit.Role==UnitRole.Cavalry)Require(cavalry.Resolve(unit)==Resources.Load<CavalryCrowdLibrary>("RegionalCavalry/"+unit.Faction+"/CavalryCrowdLibrary").French,"regional cavalry resolution");
                else
                {
                    var family=unit.Role==UnitRole.Swordsman?"sword":unit.Role==UnitRole.Line?"line":"light";
                    Require(infantry.Resolve(unit)==Resources.Load<UnitVisualLibrary>("RegionalRoster/"+unit.Faction+"-"+family+"/Library").Set,"regional infantry resolution");
                }
            }
            world.IssueRangedAttack(0,8);world.IssueRangedAttack(8,0);
            for(int i=0;i<200;i++){world.StepMovement(.05f);combat.Step(.05f);}
            Require(world.Battalions[0].ShotsFired+world.Battalions[8].ShotsFired>0,"regional musketeers fire in combat");
            Require(world.Battalions[6].ShotsFired==0&&world.Battalions[14].ShotsFired==0,"swordsmen do not shoot");
        }
        finally {world.MovementJobs?.Dispose();}
        Directory.CreateDirectory("Artifacts/RegionalArmies");File.WriteAllText("Artifacts/RegionalArmies/verification.json","{\"infantry_sets\":6,\"cavalry_sets\":2,\"poses_checked\":"+poses+",\"roster_rules_passed\":true,\"runtime_resolution_passed\":true,\"musket_combat_passed\":true}\n");
        Debug.Log("REGIONAL_ARMIES_VERIFIED poses="+poses);
    }
    static void Check(Mesh[] sequence)
    {
        Require(sequence!=null&&sequence.Length>=4,"animation sequence");
        foreach(var mesh in sequence)
        {
            Require(mesh&&mesh.vertexCount>0,"nonempty pose");
            var targets=new List<Vector3>();mesh.GetUVs(2,targets);Require(targets.Count==mesh.vertexCount,"GPU interpolation targets");
            foreach(var v in mesh.vertices)Require(!float.IsNaN(v.sqrMagnitude)&&!float.IsInfinity(v.sqrMagnitude),"finite pose");
            Require(mesh.bounds.size.magnitude<15,"human/horse scale");poses++;
        }
    }
    static void Require(bool value,string label){if(!value)throw new Exception("Regional army verification: "+label);}
}
