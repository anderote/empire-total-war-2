using System;
using System.IO;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

// Exercise the real frame loop without a player, campaign, rendering or physics scene.
public static class DeploymentDeathVerification
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    static void Require(bool value, string message)
    { if (!value) throw new Exception("Deployment deaths: " + message); }

    public static void Verify()
    {
        foreach (float speed in BattleRuntime.PlaybackSpeeds)
        {
            var config = new BattleConfig { BattalionsPerSide=1, SoldiersPerBattalion=16,
                FrenchArtillery=0, BritishArtillery=0, EnableAI=false, EnableLandscape=false,
                RequireDeployment=true, EnemyFaction="ming" };
            var world = new BattleWorld(config);
            var root = new GameObject("Offline deployment death check");
            try
            {
                var camera = root.AddComponent<Camera>(); camera.enabled=false;
                var runtime = root.AddComponent<BattleRuntime>(); runtime.enabled=false; runtime.Config=config;
                Set(runtime, "<World>k__BackingField", world);
                Set(runtime, "combat", new CombatSystem(world));
                Set(runtime, "artillery", new ArtillerySystem(world));
                var casualties = root.AddComponent<BattleCasualtyView>(); casualties.enabled=false;
                Set(runtime, "casualties", casualties);
                Set(casualties, "world", world); Set(casualties, "cameraView", camera);
                Set(casualties, "anchors", new Vector3[world.Soldiers.Alive.Length]);
                Set(casualties, "physical", new bool[world.Soldiers.Alive.Length]);
                Set(casualties, "frozen", new Mesh[world.Soldiers.Alive.Length]);
                var blood = root.AddComponent<BattleBlood>(); blood.enabled=false;
                blood.Initialize(world, camera, casualties); Set(casualties, "blood", blood);
                var handler = typeof(BattleCasualtyView).GetMethod("OnDeath", Hidden);
                world.SoldierDied += death => handler.Invoke(casualties, new object[] { death });
                var advance = typeof(BattleRuntime).GetMethod("AdvanceSimulation", Hidden);
                void Frame(float delta) => advance.Invoke(runtime, new object[] { delta });
                runtime.SetSpeed(speed);
                for (int i=0; i<120; i++) Frame(.25f);
                Require(world.Time==0 && casualties.VisualTime==0,
                    "Thirty seconds of deployment must not age casualty animations at speed " + speed);
                Require(world.BeginBattle(out string reason), reason);
                foreach (var unit in world.Battalions) unit.FireAtWill=false;
                for (int i=0; i<30; i++) Frame(1f/60f);
                Require(Mathf.Abs(casualties.VisualTime-world.Time)<.051f, "Clocks agree within one combat tick");
                var victimUnit = world.Battalions[1];
                int victim = victimUnit.SoldierStart;
                while (world.Soldiers.Captains[victim] || world.Soldiers.PikeSergeants[victim]) victim++;
                Require(world.ApplyWound(victim,1000,DeathCause.Musket,world.SoldierPosition(victim,world.Time),Vector3.forward), "Fatal rifle hit");
                var death = world.Deaths[victim];
                var library = Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
                var set = library.Resolve(victimUnit);
                var ming = Resources.Load<UnitVisualLibrary>("RegionalRoster/ming-line/Library");
                Require(set==ming.Set, "Casualty uses Ming outfit");
                float age = casualties.VisualTime-death.Born;
                Require(age>=-.001f && age<.051f, "New casualty starts at impact, not the resting pose");
                foreach (var lod in set.Lods)
                {
                    int variant = InfantryDeathMotion.Variant(death,lod.DeathVariants.Length);
                    var poses = lod.DeathVariants[variant].Poses;
                    int frame = AnimationPlayback.Frame(poses.Length,age/InfantryDeathMotion.Duration(death,variant),false,out _);
                    Require(frame<poses.Length/4, "Both Ming LODs start near the beginning of the fall");
                }
                float before = casualties.VisualTime;
                runtime.SetPaused(true); Frame(.25f);
                Require(casualties.VisualTime==before, "Pause freezes the fall");
                runtime.SetPaused(false);
                for (int i=0; i<480; i++) Frame(1f/60f);
                Require(casualties.VisualTime-death.Born>1.7f && world.DeathCount==1, "Fall finishes with one persistent casualty");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        Directory.CreateDirectory("Artifacts/MusketDeaths");
        const string report = "Passed: actual battle frame loop at 0.25x, 1x, 2x and 4x; 30-second deployment; fatal Ming musket casualty starts at impact at both LODs; pause/resume; completed fall and persistent death. No player or campaign launched.";
        File.WriteAllText("Artifacts/MusketDeaths/deployment-verification.txt",report);
        Debug.Log("DEPLOYMENT_DEATHS_VERIFIED " + report);
    }
}
