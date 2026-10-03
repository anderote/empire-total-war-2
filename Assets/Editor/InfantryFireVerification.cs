using System;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class InfantryFireVerification
{
    static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Infantry fire verification: " + message); }

    [MenuItem("Empire Battle/Verify rank and platoon fire")]
    public static void Verify()
    {
        VerifyDrills();
        BattleBuild.Verify();
        BattleEffectsVerification.Verify();
        Debug.Log("INFANTRY_FIRE_VERIFIED: 1–4 ranks, balanced platoons, synchronized shots, kneeling, individual reloads, ammunition, interruptions, casualties and combat effects.");
    }

    public static void VerifyDrills()
    {
        foreach (int ranks in new[] { 1, 2, 3, 4 })
            foreach (InfantryFireMode mode in new[]{InfantryFireMode.ByPlatoon,InfantryFireMode.Volley})
                VerifyCycle(ranks, mode);
        VerifyCasualtyGaps();
        VerifyInterruptions();
        VerifyStragglers();
        VerifyCadenceVariation();
        Debug.Log("INFANTRY_DRILLS_VERIFIED: ranks, platoons, volleys, individual reloads, ammunition, interruptions, casualties and stragglers.");
    }

    public static void VerifyAndBuildBattle()
    {
        Verify();
        InfantryBattleBuild.BuildMac();
    }

    static void VerifyCadenceVariation()
    {
        var world=Make(250);var unit=world.Battalions[0];
        float minPlatoon=100,maxPlatoon=0,minRank=100,maxRank=0;
        for(int group=0;group<32;group++)
        {
            unit.FireGroup=group;unit.LastVolleyTime=group*1.1f;
            float platoon=RifleFireTiming.GroupInterval(world,unit,false);
            float rank=RifleFireTiming.GroupInterval(world,unit,true);
            minPlatoon=Mathf.Min(minPlatoon,platoon);maxPlatoon=Mathf.Max(maxPlatoon,platoon);
            minRank=Mathf.Min(minRank,rank);maxRank=Mathf.Max(maxRank,rank);
            Require(platoon>RifleFireTiming.LatestShotDelay,"Platoon bursts remain separated");
            Require(rank>RifleFireTiming.LatestShotDelay+RifleFireTiming.KneelDelay+.45f,"Next rank allows even slower soldiers to kneel");
            Require(rank==RifleFireTiming.GroupInterval(world,unit,true),"Cadence variation is deterministic");
        }
        Require(maxPlatoon-minPlatoon>.4f&&maxRank-minRank>1f,"Handoffs have visible inter-platoon and inter-rank variance");
        var shooters=new int[250];int n=RifleFireTiming.ScheduleVolley(world,unit,shooters);
        float minKneel=100,maxKneel=0,minReload=100,maxReload=0;
        for(int i=0;i<n;i++)
        {
            int soldier=shooters[i];float kneel=world.Soldiers.KneelTimes[soldier];
            minKneel=Mathf.Min(minKneel,kneel);maxKneel=Mathf.Max(maxKneel,kneel);
            minReload=Mathf.Min(minReload,world.Soldiers.ReloadDurations[soldier]);maxReload=Mathf.Max(maxReload,world.Soldiers.ReloadDurations[soldier]);
            Require(kneel>world.Soldiers.FireTimes[soldier],"Kneeling follows each individual shot");
        }
        Require(maxKneel-minKneel>.5f&&maxReload-minReload>1f,"Men do not kneel and reload in lockstep");
        AdvanceTo(world,unit.FireCycleReadyTime+.01f);
        int firstGroup=RifleFireTiming.ScheduleNextGroup(world,unit,shooters);
        Require(firstGroup>0,"Next firing cycle starts after reload");
        Require(!RifleFireTiming.IsKneeling(world,unit,shooters[0],world.Time),"Current platoon rises to fire");
        Require(RifleFireTiming.IsKneeling(world,unit,unit.SoldierStart+unit.AliveCount-1,world.Time),
            "Rear ranks do not rise in lockstep at the start of a new cycle");
        Debug.Log($"FIRE_CADENCE_VERIFIED platoons={minPlatoon:F2}–{maxPlatoon:F2}s ranks={minRank:F2}–{maxRank:F2}s kneelSpread={maxKneel-minKneel:F2}s reloadSpread={maxReload-minReload:F2}s");
    }

    public static void BuildReview()
    {
        Verify();
        var result=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/InfantryInspection.unity"},
            "Builds/Infantry Inspection.app",BuildTarget.StandaloneOSX,BuildOptions.None);
        Require(result.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded,"Inspection player builds");
        InfantryBattleBuild.BuildMac();
    }

    public static void DiagnoseKneeling()
    {
        AnimationClip idle=null;
        foreach(var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/NapoleonicInfantry/Animations/musket_idle.FBX"))
            if(asset is AnimationClip c&&!c.name.StartsWith("__preview"))idle=c;
        foreach(float tilt in new[]{20f,25f,30f})foreach(float distance in new[]{-.65f,-.75f,-.85f})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/NapoleonicInfantry/Prefabs/FrenchInspection.prefab");
            var rig=UnityEngine.Object.Instantiate(prefab);rig.transform.position=Vector3.zero;idle.SampleAnimation(rig,0);
            var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name=="newmusket.lod3").transform;
            var rod=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");gun.SetParent(rig.transform,true);
            var grips=new EmpireBattle.Art.ClosedGripAuthoring(rig,gun,rod,.43f);grips.Apply(false,false,0);
            new EmpireBattle.AnimationAuthoring.ProceduralInfantryRig(rig).Kneel();gun.position+=Vector3.down*.43f;
            EmpireBattle.Art.MusketReloadAuthoring.StowRod(gun,rod);
            var reload=new EmpireBattle.Art.MusketReloadAuthoring(rig,gun,rod,true,tilt,distance);
            float contact=0,hand=0;
            for(int i=0;i<24;i++){float phase=i/23f;reload.Sample(phase);hand=Mathf.Max(hand,Mathf.Max(reload.LeftHandError,reload.RightHandError));grips.Apply(false,true,phase);contact=Mathf.Max(contact,grips.MaxContactError);}
            Debug.Log($"KNEEL_DIAG tilt={tilt} buttZ={distance} contact={contact:F4} hand={hand:F4}");
            UnityEngine.Object.DestroyImmediate(rig);
        }
    }

    static BattleWorld Make(int soldiers)
    {
        var world = new BattleWorld(new BattleConfig {
            BattalionsPerSide = 1, SoldiersPerBattalion = soldiers, EnableLandscape = false, EnableAI = false,
            InitialSeparation = 100, UseDetailedInfantry = false });
        // This fixture isolates the musket drill. Real command-role exclusion and
        // role-specific weapons are covered by their regiment verification fixtures.
        Array.Clear(world.Soldiers.PikeSergeants, 0, world.Soldiers.PikeSergeants.Length);
        Array.Clear(world.Soldiers.Captains, 0, world.Soldiers.Captains.Length);
        for (int i = 0; i < world.Soldiers.RoundsRemaining.Length; i++) world.Soldiers.RoundsRemaining[i] = 30;
        return world;
    }

    static void AdvanceTo(BattleWorld world, float time)
    { while (world.Time + .00001f < time) world.StepMovement(Mathf.Min(.05f, time - world.Time)); }

    static void VerifyCycle(int ranks, InfantryFireMode mode)
    {
        int count = 37 * ranks - (ranks > 1 ? 5 : 0);
        var world = Make(count); var unit = world.Battalions[0];
        unit.Files = 37; unit.FireMode = mode; unit.Ammunition = 1;
        for(int slot=0;slot<count;slot++)world.Soldiers.Positions[slot]=unit.Position+
            FormationMath.SlotOffset(slot,count,unit.Files,unit.Spacing,unit.Facing);
        // Scheduling is tested without enemy damage or movement, so every initial slot must fire once.
        var shooters = new int[count]; var seen = new bool[count];
        float priorRankLastShot = -100; int previousRank = -1, total = 0, groups = 0;
        do
        {
            AdvanceTo(world, unit.NextFireTime + .001f);
            int n = RifleFireTiming.ScheduleNextGroup(world, unit, shooters);
            Require(n > 0, $"Each nonempty platoon emits shots: ranks={ranks} mode={mode} group={unit.FireGroup} time={world.Time} next={unit.NextFireTime}");
            if (mode == InfantryFireMode.ByPlatoon) Require(n <= world.Config.PlatoonFiles, "Platoon stays within its file budget");
            int rank = world.Soldiers.FormationSlots[shooters[0]] / unit.FireCycleFiles;
            if (rank != previousRank)
            {
                Require(rank == previousRank + 1, "Ranks fire front to rear");
                if (previousRank >= 0) Require(world.Time > priorRankLastShot + RifleFireTiming.KneelDelay, "Previous rank has knelt before next rank fires");
                previousRank = rank;
            }
            float earliest = float.PositiveInfinity, latest = 0;
            for (int j = 0; j < n; j++)
            {
                int soldier = shooters[j];
                Require(!seen[soldier], "No duplicate shooter within a drill"); seen[soldier] = true;
                Require(world.Soldiers.FormationSlots[soldier] / unit.FireCycleFiles == rank, "One rank per group");
                float fire = world.Soldiers.FireTimes[soldier]; earliest = Mathf.Min(earliest, fire); latest = Mathf.Max(latest, fire);
                Require(world.Soldiers.ReloadReadyTimes[soldier] >= fire + RifleFireTiming.ReloadDuration(world,unit)-.001f, "Every rifle gets a full reload for its fire mode");
                Require(RifleFireTiming.IsKneeling(world, unit, soldier, world.Soldiers.KneelTimes[soldier] + .01f), "Fired soldier kneels during reload");
                Require(!RifleFireTiming.IsKneeling(world, unit, soldier, fire), "Fired soldier is standing at muzzle discharge");
            }
            Require(latest - earliest <= RifleFireTiming.LatestShotDelay + .001f, "Platoon shots are synchronized");
            priorRankLastShot = latest; total += n; groups++;
            Require(groups <= 12, "Cycle is bounded");
        } while (unit.FireCycleActive);
        int expected=mode==InfantryFireMode.Volley?Mathf.Min(37,count):count;
        Require(total == expected, "Controlled drills fire all ranks; simultaneous volley fires unobstructed front rank ranks="+ranks+" mode="+mode+" total="+total+" expected="+expected);
        Require(unit.Ammunition == 0, "One ammunition round funds the entire drill");
        AdvanceTo(world, Mathf.Max(unit.NextFireTime,unit.FireCycleReadyTime) + 1);
        Require(RifleFireTiming.ScheduleNextGroup(world, unit, shooters) == 0, "Empty ammunition prevents a new drill");
        Require(!RifleFireTiming.IsKneeling(world, unit, shooters[0], world.Time), "Soldiers stand when the drill has reloaded");
    }

    public static void VerifyCasualtyGaps()
    {
        var world=Make(250);var unit=world.Battalions[0];unit.FireMode=InfantryFireMode.ByPlatoon;
        var shooters=new int[250];
        Require(RifleFireTiming.ScheduleNextGroup(world,unit,shooters)>0,"Front rank starts casualty scenario");
        AdvanceTo(world,unit.NextFireTime+.001f);
        // A casualty can arrive after movement, leaving -1 until the next movement tick.
        int gap=-1;
        for(int i=0;i<100 && gap<0;i++)
        {
            int killed=world.ApplyHit(unit.Id,1000,world.Battalions[1].Id);
            int slot=world.Soldiers.FormationSlots[killed];
            if(slot<unit.FireCycleFiles)gap=slot;
        }
        Require(gap>=0 && world.SoldierAtSlot(unit,gap)==-1,"An unfilled front-rank casualty slot exists");
        int count=RifleFireTiming.ScheduleNextGroup(world,unit,shooters);
        Require(count>0,"Surviving rear rank can fire across an empty front slot");
        for(int i=0;i<count;i++)Require(world.Soldiers.Alive[shooters[i]],"No casualty is scheduled to shoot");
        Require(world.SoldierAtSlot(unit,gap)==-1,"Firing does not mutate formation occupancy");
        foreach(var formation in new[]{FormationKind.Skirmish,FormationKind.Square})
        {
            var independent=Make(100);var firing=independent.Battalions[0];
            independent.Battalions[1].FireAtWill=false;
            independent.SetFormation(firing.Id,formation);AdvanceTo(independent,35f);
            int killed=independent.ApplyHit(firing.Id,1000,independent.Battalions[1].Id);
            int emptySlot=independent.Soldiers.FormationSlots[killed];
            Require(independent.SoldierAtSlot(firing,emptySlot)==-1,"Independent fire retains casualty gap until movement");
            new CombatSystem(independent).Step(.05f);
            Require(!independent.Soldiers.Alive[killed] && firing.AliveCount==99,"Independent fire tolerates casualty gap");
        }
        Debug.Log("FIRE_CASUALTY_GAPS_VERIFIED");
    }

    static void VerifyStragglers()
    {
        foreach (InfantryFireMode mode in new[]{InfantryFireMode.ByPlatoon,InfantryFireMode.Volley})
        {
            var world=Make(250);var unit=world.Battalions[0];unit.FireMode=mode;
            var shooters=new int[250];int delayed=world.SoldierAtSlot(unit,2);
            world.Soldiers.Positions[delayed]+=new Vector2(0,-2);
            world.Soldiers.MovementSpeeds[delayed]=1;unit.Reforming=true;
            int n=RifleFireTiming.ScheduleNextGroup(world,unit,shooters);
            int expected=unit.Files/unit.FireCyclePlatoons;
            if(mode==InfantryFireMode.Volley)Require(n>=unit.Files-1&&n<=unit.Files+3,"Volley fires ready front men and only exposed rear lanes");
            else Require(n==expected-1,"One straggler does not delay ready platoon/rank mates");
            for(int j=0;j<n;j++)Require(shooters[j]!=delayed,"Moving soldier does not shoot");
            Require(world.Soldiers.FireTimes[delayed]<0,"Straggler is skipped rather than pre-scheduled");
        }
        var partial=Make(250);var partialUnit=partial.Battalions[0];var partialBuffer=new int[250];
        // Even an entirely unsettled platoon does not block the next platoon in the sequence.
        for(int i=0;i<15;i++){partial.Soldiers.Positions[i]+=new Vector2(0,-10);partial.Soldiers.MovementSpeeds[i]=1;}
        Require(RifleFireTiming.ScheduleNextGroup(partial,partialUnit,partialBuffer)==0,"Unsettled platoon skips its discharge");
        AdvanceTo(partial,partialUnit.NextFireTime+.001f);
        Require(RifleFireTiming.ScheduleNextGroup(partial,partialUnit,partialBuffer)==16,"Next ready platoon fires without waiting for the first");
        var battle=Make(250);var attacker=battle.Battalions[0];battle.Battalions[1].FireAtWill=false;
        attacker.Reforming=true;battle.Soldiers.MovementSpeeds[2]=1;
        var combat=new CombatSystem(battle);combat.Step(.05f);
        Require(attacker.ShotsFired==14,"Combat ignores aggregate reforming flag and fires 14 ready men");
        var empty=Make(250);var waiting=empty.Battalions[0];var buffer=new int[250];
        for(int i=0;i<250;i++)empty.Soldiers.MovementSpeeds[i]=1;
        Require(RifleFireTiming.ScheduleNextGroup(empty,waiting,buffer)==0&&waiting.Ammunition==30,
            "Empty discharge does not consume ammunition");
    }

    static void VerifyInterruptions()
    {
        var world = Make(250); var unit = world.Battalions[0]; var shooters = new int[250];
        int n = RifleFireTiming.ScheduleNextGroup(world, unit, shooters);
        int fired = shooters[0]; float ready = world.Soldiers.ReloadReadyTimes[fired];
        world.SetFireMode(unit.Id, InfantryFireMode.Volley);
        Require(!unit.FireCycleActive && unit.NextFireTime <= world.Time, "Changing drill releases loaded soldiers immediately");
        Require(world.Soldiers.ReloadReadyTimes[fired] == ready, "Changing drill preserves the spent musket's reload cooldown");
        int resumed=RifleFireTiming.ScheduleNextGroup(world, unit, shooters);
        Require(resumed>0,"Loaded men can fire after a drill change");
        for(int j=0;j<resumed;j++)Require(shooters[j]!=fired,"Switching modes cannot reload a spent musket instantly");
        unit.Moving = true;
        Require(!RifleFireTiming.IsKneeling(world, unit, fired, world.Soldiers.KneelTimes[fired] + 1), "Movement overrides kneeling");
        unit.Moving = false;
        world.SetFrontage(unit.Id, 1);
        Require(unit.Files == Mathf.Min(4,unit.AliveCount) && FormationMath.FiringSoldierCount(unit) <= unit.Files*4, "Deep custom blocks retain only four firing ranks");
        world.ApplyCasualties(unit.Id, 40); AdvanceTo(world, ready + 20);
        unit.FireAtWill = false;
        Require(!RifleFireTiming.IsKneeling(world, unit, fired, world.Time), "Hold fire releases kneeling posture");
        unit.FireAtWill = true;
        n = RifleFireTiming.ScheduleNextGroup(world, unit, shooters);
        for (int j = 0; j < n; j++) Require(world.Soldiers.Alive[shooters[j]], "Casualty gaps never schedule dead soldiers");
        // Exercise orders through the actual combat loop too.
        var combatWorld = Make(250); var combat = new CombatSystem(combatWorld);
        combatWorld.Battalions[1].FireAtWill = false;
        combatWorld.StepMovement(.05f); combat.Step(.05f);
        var attacker = combatWorld.Battalions[0]; int initial = attacker.ShotsFired;
        Require(initial > 0 && initial <= 16, "Combat fires only the current platoon");
        attacker.FireAtWill = false;
        for (int i = 0; i < 100; i++) { combatWorld.StepMovement(.05f); combat.Step(.05f); }
        Require(attacker.ShotsFired == initial && !attacker.FireCycleActive, "Hold fire stops the active sequence");
    }
}
