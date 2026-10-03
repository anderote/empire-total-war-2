using System;
using System.IO;
using System.Text;
using EmpireBattle;
using UnityEngine;

// A repeatable scenario report, not a claim that prototype price targets are tuned.
public static class UnitRosterBalanceReview
{
    public static void Review()
    {
        var report=new StringBuilder("role,start_distance,seed,role_strength,line_strength,role_survivors,line_survivors,role_shots,line_shots,strikes,outcome\n");
        foreach(var role in new[]{UnitRole.Line,UnitRole.Skirmisher,UnitRole.Rifleman,UnitRole.Guard,UnitRole.Militia,UnitRole.Swordsman})
        foreach(float distance in new[]{60f,200f})foreach(int seed in new[]{1815,42,701})
        {
            int strength=Mathf.RoundToInt(120/UnitRoster.Cost(role));
            var config=new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=Mathf.Max(120,strength),
                InitialStrengths=new[]{strength,120},FormationRoles=new[]{role,UnitRole.Line},
                FormationWeapons=new[]{role==UnitRole.Rifleman?"rifle":"musket","musket"},
                FormationDoctrine=new[]{UnitRoster.Light(role)?(int)MilitaryDoctrine.LightInfantry:(int)MilitaryEquipment.AllDoctrine,(int)MilitaryEquipment.AllDoctrine},
                FrenchArtillery=0,BritishArtillery=0,InitialSeparation=distance,EnableLandscape=false,EnableAI=false,Seed=seed};
            var world=new BattleWorld(config);var combat=new CombatSystem(world);
            try
            {
                if(role==UnitRole.Swordsman)world.IssueCharge(0,1);
                else {world.IssueRangedAttack(0,1);world.IssueRangedAttack(1,0);}
                for(int tick=0;tick<2400&&world.Outcome==BattleOutcome.Ongoing;tick++)
                {world.StepMovement(.05f);combat.Step(.05f);}
                var a=world.Battalions[0];var b=world.Battalions[1];
                if(role==UnitRole.Swordsman&&(a.ShotsFired!=0||world.LivingSergeants(a)!=0))throw new Exception("Sword role used a ranged or pike weapon");
                foreach(var p in world.Soldiers.Positions)if(float.IsNaN(p.x)||float.IsNaN(p.y))throw new Exception("Nonfinite balance scenario position");
                report.AppendLine($"{role},{distance},{seed},{strength},120,{a.AliveCount},{b.AliveCount},{a.ShotsFired},{b.ShotsFired},{combat.Melee.ResolvedStrikes},{world.Outcome}");
            }
            finally {world.MovementJobs?.Dispose();}
        }
        Directory.CreateDirectory("Artifacts/UnitRoster");File.WriteAllText("Artifacts/UnitRoster/balance-scenarios.csv",report.ToString());
        Debug.Log("UNIT_ROSTER_BALANCE_REVIEWED scenarios=36");
    }
}
