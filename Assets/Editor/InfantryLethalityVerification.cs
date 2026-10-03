using System;
using EmpireBattle;
using UnityEngine;

/// <summary>Headless combat measurements; no scene, player build or saved playtest is changed.</summary>
public static class InfantryLethalityVerification
{
    const int Trials = 64;
    struct Result
    {
        public int Shots, Hits, Kills;
        public float KillRate => Kills / (float)Mathf.Max(1, Shots);
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Infantry lethality: " + message); }

    static BattleWorld Fixture(float distance, InfantryFireMode mode, int seed, UnitKind targetKind = UnitKind.Infantry)
    {
        var world = new BattleWorld(new BattleConfig {
            BattalionsPerSide = 1, SoldiersPerBattalion = 128, InitialSeparation = distance,
            ExplicitArmyCounts=true,FrenchBattalions=1,BritishBattalions=targetKind==UnitKind.Cavalry?0:1,BritishCavalry=targetKind==UnitKind.Cavalry?1:0,RidersPerSquadron=128,
            FrenchArtillery = 0, BritishArtillery = 0, EnableLandscape = false,
            EnableAI = false, UseDetailedInfantry = false, Seed = seed
        });
        foreach (var unit in world.Battalions)
        {
            unit.Files = 64;
            unit.FireMode = mode;
            for (int slot = 0; slot < unit.AliveCount; slot++)
            {
                int soldier = world.SoldierAtSlot(unit, slot);
                world.Soldiers.Positions[soldier] = unit.Position + FormationMath.SlotOffset(unit, slot);
                world.Soldiers.Facings[soldier] = FormationMath.SlotFacing(unit, slot);
            }
        }
        MeleeEncounter.Place(world,world.Battalions[0],Vector2.zero,0);
        MeleeEncounter.Place(world,world.Battalions[1],new Vector2(0,distance),Mathf.PI);
        world.Battalions[1].FireAtWill = false;
        return world;
    }

    static Result Measure(float distance, InfantryFireMode mode, UnitKind targetKind = UnitKind.Infantry)
    {
        var total = new Result();
        for (int trial = 0; trial < Trials; trial++)
        {
            var world = Fixture(distance, mode, 1815 + trial * 37,targetKind);
            var unit = world.Battalions[0];
            var combat = new CombatSystem(world);
            combat.Step(.05f);
            Require(unit.ShotsFired > 0, "Ready infantry must fire at " + distance + "m in " + mode);
            Require(world.Battalions[1].AliveCount == 128 - unit.Kills, "Kill credit matches casualties");
            Require(unit.Hits <= unit.ShotsFired && unit.Kills <= unit.Hits, "Shots, hits and kills remain consistent");
            int shots = unit.ShotsFired;
            // Without advancing time, a second step cannot bypass individual reloads.
            combat.Step(.05f);
            Require(unit.ShotsFired == shots, "Close-range lethality cannot bypass reloads");
            total.Shots += unit.ShotsFired; total.Hits += unit.Hits; total.Kills += unit.Kills;
        }
        Debug.Log($"MUSKET_LETHALITY target={targetKind} mode={mode} range={distance:F0}m trials={Trials} shots={total.Shots} hits={total.Hits} kills={total.Kills} killsPer100Shots={total.KillRate * 100:F2}");
        return total;
    }

    public static void MeasureBaseline()
    {
        foreach (var mode in new[] { InfantryFireMode.Volley, InfantryFireMode.FireAtWill })
            foreach (float range in new[] { 30f, 50f, 75f, 150f }) Measure(range, mode);
    }

    public static void VerifyBallDamage()
    {
        int oldLethal=0,newLethal=0;
        foreach(float range in new[]{0f,30f,50f,75f,150f,247f})foreach(float sample in new[]{0f,.5f,1f})
        {
            float baseline=WeaponBallistics.MusketDamage(range,150,sample);
            foreach(string weapon in new[]{null,"","musket"})
            {
                Require(Mathf.Abs(WeaponBallistics.SmallArmsDamage(range,150,sample,weapon,UnitKind.Infantry)-(baseline+35f))<.001f,"all round-ball weapons receive the infantry damage increase");
                Require(Mathf.Abs(WeaponBallistics.SmallArmsDamage(range,150,sample,weapon,UnitKind.Cavalry)-(baseline+35f))<.001f,"riders receive the same lethal round-ball damage as infantry");
            }
            foreach(string weapon in new[]{"percussion","rifle","rifle-musket","breechloader","magazine","smokeless"})
                foreach(var target in new[]{UnitKind.Infantry,UnitKind.Cavalry})
                    Require(WeaponBallistics.SmallArmsDamage(range,150,sample,weapon,target)>baseline+35f,"every upgraded firearm improves on musket hit damage");
        }
        for(int i=0;i<1000;i++)
        {
            float sample=(i+.5f)/1000;
            if(WeaponBallistics.MusketDamage(150,150,sample)>=100)oldLethal++;
            if(WeaponBallistics.SmallArmsDamage(150,150,sample,"musket",UnitKind.Infantry)>=100)newLethal++;
        }
        Require(newLethal>=900&&newLethal<1000,"normal-health infantry are much more vulnerable while nonfatal hits remain possible");
        Require(BattleEffectTuning.BloodCount(100)==70&&Mathf.Abs(BattleEffectTuning.BloodPoolScale*BattleEffectTuning.BloodPoolScale-.7f)<.0001f,"blood spray count and pool coverage reduce by thirty percent");
    }

    public static void Verify()
    {
        VerifyBallDamage();
        foreach (float nominal in new[] { 120f, 150f, 225f, 360f })
        {
            float previous = 1f;
            for (int range = 0; range <= 500; range++)
            {
                float chance = WeaponBallistics.MusketHitChance(range, nominal, 1, 0, 0);
                Require(chance > 0 && chance <= previous, "Accuracy falls with distance");
                previous = chance;
                if (range < 75) continue;
                float fraction = range / nominal;
                float original = Mathf.Lerp(.20f, .025f, Mathf.Clamp01(fraction)) * Mathf.Exp(-Mathf.Max(0, fraction - 1) * 3);
                Require(Mathf.Abs(chance - original) < .000001f, "Long-range accuracy is unchanged for every weapon");
                Require(WeaponBallistics.MusketDamage(range, nominal, 0) == 60 && WeaponBallistics.MusketDamage(range, nominal, 1) == 120,
                    "Long-range damage is unchanged");
            }
        }
        float fresh = WeaponBallistics.MusketHitChance(30, 150, 1, 0, 0);
        Require(fresh > .6f && fresh < .75f, "Fresh infantry are highly lethal, but not infallible, at 30m");
        Require(WeaponBallistics.MusketHitChance(30, 150, .6f, 80, 0) < fresh * .5f, "Fatigue and disorder still matter");
        foreach (var mode in new[] { InfantryFireMode.Volley, InfantryFireMode.FireAtWill })
        {
            var close = Measure(30, mode); var middle = Measure(50, mode);
            var far = Measure(75, mode); var longRange = Measure(150, mode);
            Require(close.KillRate > .55f && close.KillRate < .8f, "Close fire rapidly kills exposed infantry in " + mode);
            Require(close.KillRate > middle.KillRate && middle.KillRate > far.KillRate * 1.5f, "Closing distance matters in " + mode);
            Require(longRange.KillRate < .04f, "Long range remains ineffective");
            var cavalry=Measure(30,mode,UnitKind.Cavalry);
            Require(cavalry.KillRate>close.KillRate&&cavalry.KillRate>.6f,"Mounted targets die rapidly to close musket fire in "+mode);
        }
        VerifySquareOpeningFire();VerifySettledSquareWalls();CavalryCombatVerification.VerifySquareContactFire();
        InfantryFireVerification.VerifyDrills();
        Debug.Log("INFANTRY_LETHALITY_VERIFIED close volleys, independent fire, casualties, reloads, range and condition modifiers");
    }

    static void VerifySquareOpeningFire()
    {
        int shots=0,kills=0;
        for(int trial=0;trial<32;trial++)
        {
            var world=new BattleWorld(new BattleConfig {ExplicitArmyCounts=true,FrenchBattalions=1,BritishBattalions=0,BritishCavalry=1,
                SoldiersPerBattalion=250,RidersPerSquadron=96,FrenchArtillery=0,BritishArtillery=0,
                EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false,Seed=1815+trial*37});
            var square=world.Battalions[0];var cavalry=world.Battalions[1];
            world.SetFormation(square.Id,FormationKind.Square);
            MeleeEncounter.Place(world,square,Vector2.zero,0);
            MeleeEncounter.Place(world,cavalry,new Vector2(0,30),Mathf.PI);
            cavalry.FireAtWill=false;var combat=new CombatSystem(world);combat.Step(.05f);
            int outerShots=0,innerShots=0;
            for(int slot=0;slot<square.AliveCount;slot++)
            {
                int soldier=world.SoldierAtSlot(square,slot);
                if(world.Soldiers.FireTimes[soldier]<0)continue;
                if(FormationMath.IsBayonetWall(square,slot))outerShots++;else innerShots++;
            }
            Require(outerShots>0&&innerShots>0,"Kneeling outer and standing inner ranks both fire at approaching cavalry");
            var fireTimes=(float[])world.Soldiers.FireTimes.Clone();var rounds=(int[])world.Soldiers.RoundsRemaining.Clone();
            // Measure the opening discharge before later sides can acquire newly released loose horses.
            shots+=square.ShotsFired;kills+=square.Kills;
            combat.Step(.05f);
            for(int soldier=square.SoldierStart;soldier<square.SoldierStart+square.SoldierCount;soldier++)
                if(fireTimes[soldier]>=0)Require(world.Soldiers.FireTimes[soldier]==fireTimes[soldier]&&world.Soldiers.RoundsRemaining[soldier]==rounds[soldier],"Square shooters cannot fire their loaded round twice before reloading");
            Require(cavalry.AliveCount==96-square.Kills,"Opening musket kill credit matches mounted casualties");
        }
        Debug.Log($"SQUARE_OPENING_LETHALITY_VERIFIED trials=32 averageShots={shots/32f:F1} averageKills={kills/32f:F1}");
        Require(kills/32f>=24&&kills/(float)shots>.6f,"A square's first close-range discharge rapidly removes approaching cavalry: shots="+shots/32f+" kills="+kills/32f);
    }

    public static void VerifySettledSquareWalls()
    {
        var world=new BattleWorld(new BattleConfig {FrenchBattalions=1,BritishBattalions=4,SoldiersPerBattalion=96,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        var square=world.Battalions[0];world.SetFormation(0,FormationKind.Square);
        MeleeEncounter.Place(world,square,Vector2.zero,0);
        // One inner soldier is still arriving. All settled wall slots must remain usable.
        int straggler=-1;
        for(int slot=0;slot<square.AliveCount;slot++)
        {
            int soldier=world.SoldierAtSlot(square,slot);
            if(FormationMath.SquareRank(square,slot)==1&&!world.Soldiers.Captains[soldier]&&!world.Soldiers.PikeSergeants[soldier]){straggler=soldier;break;}
        }
        Require(straggler>=0,"Fixture includes a normal musketman in the standing firing rank");
        world.Soldiers.Positions[straggler]+=new Vector2(2,2);square.Reforming=true;
        for(int side=0;side<4;side++)
        {
            var enemy=world.Battalions[side+1];enemy.FireAtWill=false;
            MeleeEncounter.Place(world,enemy,FormationMath.Forward(side*Mathf.PI*.5f)*30,side*Mathf.PI*.5f+Mathf.PI);
        }
        var combat=new CombatSystem(world);combat.Step(.05f);var fired=new int[4];
        for(int slot=0;slot<square.AliveCount;slot++)
        {
            int soldier=world.SoldierAtSlot(square,slot);if(world.Soldiers.FireTimes[soldier]<0)continue;
            Require(soldier!=straggler,"An unsettled square soldier cannot shoot");
            fired[FormationMath.SquareSide(slot,square.AliveCount)]++;
            if(FormationMath.IsBayonetWall(square,slot))Require(RifleFireTiming.IsKneeling(world,square,soldier,world.Time),"Settled outer wall fires from a kneeling posture while others form");
            var outward=FormationMath.Forward(FormationMath.SlotFacing(square,slot));
            Require(Vector3.Dot(world.Soldiers.FireDirections[soldier],new Vector3(outward.x,0,outward.y))>.8f,"Square fires outward from each wall");
        }
        foreach(int count in fired)Require(count>0,"All four settled square walls fire despite a straggler");
        Require(world.Soldiers.FireTimes[straggler]<0,"The unsettled firing-rank musketman waits until he reaches his slot");
        Debug.Log("SETTLED_SQUARE_WALLS_VERIFIED all four sides fire outward while unfinished slots remain blocked");
    }
}
