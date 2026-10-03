using System;
using System.IO;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor;
using UnityEngine;

public static partial class InfantryAssetBuild
{
    static bool commandCoverageOnly;
    public static void BakeCommandCoverage()
    {
        commandCoverageOnly=true;
        try { BakeUnitRoster();BakeRegionalInfantry(); }
        finally { commandCoverageOnly=false; }
        InfantryCommandCoverage.Verify();
    }
}

public static class InfantryCommandCoverage
{
    public static void Verify()
    {
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        int checkedUnits=0;
        foreach(string faction in UnitRoster.Factions)
        {
            var config=UnitRoster.CreateBattle(faction,faction);
            config.EnableLandscape=config.EnableAI=false;
            config.FrenchArtillery=config.BritishArtillery=0;
            // Exercise the upgrade path that previously selected command-less rifle libraries.
            for(int i=0;i<config.FormationWeapons.Length;i++)config.FormationWeapons[i]="breechloader";
            var world=new BattleWorld(config);
            try
            {
                foreach(var unit in world.Battalions)
                {
                    bool eligible=UnitRoster.HasCommandParty(unit);
                    Require((world.LivingCaptains(unit)>0)==eligible,"officer eligibility "+faction+" "+unit.Role);
                    Require((world.LivingSergeants(unit)>0)==eligible,"sergeant eligibility "+faction+" "+unit.Role);
                    if(!eligible){Require(CommandPartyLayout.Platoons(unit,config.PlatoonFiles)==0,"no inappropriate colour party");continue;}
                    Require(unit.Weapon.Id=="breechloader","upgraded weapon retained");
                    var set=library.ResolveCommand(unit);
                    Require(set!=library.Resolve(unit),"command roles use their own art after a weapon upgrade");
                    Require(set!=null&&set.Material&&set.Lods.Length>0,"command set "+faction);
                    foreach(var lod in set.Lods)
                    {
                        Require(lod.Command!=null&&lod.Command.Length==5,"five command roles "+faction);
                        for(int role=0;role<5;role++)
                        {
                            var command=lod.Command[role];
                            Require(command!=null,"command role "+role);
                            foreach(var poses in new[]{command.Idle,command.March})
                            {
                                Require(poses!=null&&poses.Length>=4,"idle/march "+faction+" role "+role);
                                foreach(var mesh in poses)Require(mesh&&mesh.vertexCount>0,"command mesh");
                            }
                        }
                        var officer=lod.Command[0];var sergeant=lod.Command[3];
                        foreach(var poses in new[]{officer.OfficerSlash,officer.OfficerFire,officer.OfficerKnockdown,officer.OfficerGetUp,officer.OfficerDeath,sergeant.PikeThrust,sergeant.PikeKnockdown,sergeant.PikeGetUp,sergeant.PikeDeath})
                            Require(poses!=null&&poses.Length>=4,"combat and casualty command coverage "+faction);
                    }
                    foreach(var formation in new[]{FormationKind.Line,FormationKind.Column,FormationKind.Square,FormationKind.Skirmish})
                    {
                        unit.Formation=formation;
                        int platoons=CommandPartyLayout.Platoons(unit,config.PlatoonFiles);
                        Require(platoons>0,"command group survives formation changes");
                        Require(CommandPartyLayout.Count(unit,0,platoons)==0&&CommandPartyLayout.Count(unit,3,platoons)==0,"no decorative duplicates of simulated officers/NCOs");
                        Require(CommandPartyLayout.Count(unit,2,platoons)<=2,"battalion colour count");
                        for(int role=1;role<5;role++)for(int member=0;member<CommandPartyLayout.Count(unit,role,platoons);member++)
                        {
                            var offset=CommandPartyLayout.Offset(unit,role,member,platoons);
                            Require(!float.IsNaN(offset.sqrMagnitude)&&!float.IsInfinity(offset.sqrMagnitude),"finite command placement");
                        }
                    }
                    checkedUnits++;
                }
            }
            finally { world.MovementJobs?.Dispose(); }
        }
        RegimentPresentationVerification.Verify();
        Directory.CreateDirectory("Artifacts/InfantryCommandCoverage");
        File.WriteAllText("Artifacts/InfantryCommandCoverage/verification.json","{\"factions\":10,\"formations_checked\":"+checkedUnits+",\"passed\":true}\n");
        Debug.Log("INFANTRY_COMMAND_COVERAGE_VERIFIED units="+checkedUnits);
    }
    static void Require(bool value,string message){if(!value)throw new Exception("Infantry command coverage: "+message);}
}
