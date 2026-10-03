using System;
using EmpireBattle;
using UnityEngine;

public static class InfantryContactVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Infantry contact: "+message);}
    static BattleWorld World(int count=80)=>new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=count,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
    public static void Verify()
    {
        VerifyMelee();VerifyEscape(false,FormationKind.Line);VerifyEscape(true,FormationKind.Line);VerifyEscape(false,FormationKind.Square);VerifyDamage();VerifyDispersedContact();VerifyBracing();
        Debug.Log("INFANTRY_CONTACT_VERIFIED formed bayonet lines, supporting ranks, retreat dispersion, escape facing, reforming, scalar/Burst parity and triple pursuit damage");
    }
    public static void VerifyReforming(){VerifyEscape(false,FormationKind.Line);VerifyEscape(false,FormationKind.Square);}
    public static void VerifyBayonetLines(){Verify();MeleePresentationVerification.Verify();}
    static void VerifyMelee()
    {
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var a=new BattleWorld(MeleeEncounter.Config("infantry"));var b=new BattleWorld(MeleeEncounter.Config("infantry"));
            MeleeEncounter.Setup(a,"infantry");MeleeEncounter.Setup(b,"infantry");var ca=new CombatSystem(a);var cb=new CombatSystem(b);
            int lineSamples=0,dressedSamples=0,reserveSamples=0;var rear=new bool[a.Soldiers.Alive.Length];
            using(var jobs=new SoldierMovementJobs(b))
            {
                b.MovementJobs=jobs;
                for(int tick=0;tick<Mathf.CeilToInt(10/dt);tick++)
                {
                    var before=(Vector2[])a.Soldiers.Positions.Clone();a.StepMovement(dt);b.StepMovement(dt);
                    for(int i=0;i<rear.Length;i++)if(a.Soldiers.Alive[i])
                    {
                        Require(Vector2.Distance(a.Soldiers.Positions[i],b.Soldiers.Positions[i])<.035f,"Melee scalar/Burst positions dt="+dt+" tick="+tick+" man="+i+" scalar="+a.Soldiers.Positions[i]+" burst="+b.Soldiers.Positions[i]);
                        var u=a.Battalions[a.Soldiers.BattalionIds[i]];
                        // Walking remains speed-limited; physical shoves add external motion.
                        bool looseMove=a.ContactMovement.Loose[u.Id];
                        float limit=a.Config.MarchSpeed*2.8f*(looseMove?1f:1.2f)*InfantryContactMovement.Pace(i,looseMove&&InfantryContactMovement.Fleeing(u));
                        float walked=Vector2.Distance(before[i],a.Soldiers.Positions[i]-a.BodyContacts.ContactDisplacement(i));
                        Require(walked<=dt*limit+.001f,$"Bounded self-propelled charge/melee/escape speed dt={dt} tick={tick} man={i} loose={a.ContactMovement.Loose[u.Id]} walked={walked} limit={dt*limit} push={a.BodyContacts.ContactDisplacement(i)}");
                        if(a.ContactMovement.BattleLines[u.Id]&&a.Time>3)
                        {
                            lineSamples++;
                            Vector2 offset=a.Soldiers.Positions[i]-u.Position-FormationMath.SlotOffset(u,a.Soldiers.FormationSlots[i]);
                            // Promotion and command-role placement can change a slot
                            // after a casualty. Allow those men time to dress the new file.
                            if(Mathf.Abs(Vector2.Dot(offset,FormationMath.Right(u.Facing)))<.65f)dressedSamples++;
                            if(a.Soldiers.FormationSlots[i]>=FormationMath.EffectiveFiles(u))
                            {
                                reserveSamples++;
                                Require(!MeleeSystem.IsStrike(a.Soldiers.MeleeActions[i]),"Supporting ranks cannot strike through the front line");
                            }
                        }
                    }
                    ca.Step(dt);cb.Step(dt);
                    Require(a.DeathCount==b.DeathCount,"Melee scalar/Burst casualties dt="+dt+" tick="+tick);
                }
                b.MovementJobs=null;
            }
            Require(lineSamples>0&&reserveSamples>0,"Formed lines retain supporting ranks dt="+dt);
            Require(dressedSamples>lineSamples*.9f,"At least ninety percent of the fighting line stays in its files dt="+dt+" dressed="+dressedSamples+" samples="+lineSamples);
            Require(ca.Melee.ResolvedStrikes>0,"Real timed melee contacts");
            Require(a.DeathCount<60,"An equal-strength bayonet encounter cannot wipe out its frontage in ten seconds deaths="+a.DeathCount);
            Debug.Log($"INFANTRY_BAYONET_LINES_PASSED dt={dt} lineSamples={lineSamples} reserveSamples={reserveSamples} strikes={ca.Melee.ResolvedStrikes} parries={ca.Melee.ParriedStrikes} deaths={a.DeathCount}");
        }
    }
    static void VerifyEscape(bool routing,FormationKind formation)
    {
        var a=World();var b=World();
        foreach(var w in new[]{a,b})
        {
            w.SetFormation(0,formation);for(int t=0;t<800;t++)w.StepMovement(.05f);
            w.Battalions[0].Routing=routing;w.Withdraw(0);
        }
        var start=(Vector2[])a.Soldiers.Positions.Clone();
        using(var jobs=new SoldierMovementJobs(b))
        {
            b.MovementJobs=jobs;
            for(int t=0;t<80;t++)
            {
                a.StepMovement(.05f);b.StepMovement(.05f);
                for(int i=0;i<80;i++)Require(Vector2.Distance(a.Soldiers.Positions[i],b.Soldiers.Positions[i])<.01f,"Escape scalar/Burst parity");
            }
            b.MovementJobs=null;
        }
        int dispersed=0;
        for(int i=0;i<80;i++)
        {
            var s=a.Soldiers;Require(Vector2.Distance(s.Positions[i],start[i])>3,"Retreat actually escapes");
            Require(Vector2.Dot(FormationMath.Forward(s.Facings[i]),s.Velocities[i].normalized)>.95f,"Escape faces travel instead of backing away");
            Require(!MeleeSystem.IsStrike(s.MeleeActions[i]),"Retreat cancels attacks");
            if(Vector2.Distance(s.Positions[i],a.Battalions[0].Position+FormationMath.SlotOffset(a.Battalions[0],s.FormationSlots[i]))>.5f)dispersed++;
        }
        Require(dispersed>50,"Withdrawal breaks ranks including squares");
        if(!routing)
        {
            a.Halt(0);for(int t=0;t<800;t++)a.StepMovement(.05f);
            if(a.Battalions[0].Reforming)for(int i=0;i<80;i++)
            {
                var s=a.Soldiers;var u=a.Battalions[0];var target=u.Position+FormationMath.SlotOffset(u,s.FormationSlots[i]);
                if(Vector2.Distance(s.Positions[i],target)>.22f)Debug.Log($"REFORM_BLOCKED man={i} slot={s.FormationSlots[i]} command={s.Captains[i]||s.PikeSergeants[i]} pos={s.Positions[i]} target={target} velocity={s.Velocities[i]}");
            }
            Require(!a.Battalions[0].Reforming&&!a.ContactMovement.Loose[0],"Halted survivors reform formation="+formation+" error="+a.Battalions[0].FormationError);
            for(int i=0;i<80;i++)Require(Vector2.Distance(a.Soldiers.Positions[i],a.Battalions[0].Position+FormationMath.SlotOffset(a.Battalions[0],a.Soldiers.FormationSlots[i]))<.01f,"Return to assigned formation");
        }
        Debug.Log($"INFANTRY_ESCAPE_PASSED routing={routing} formation={formation} dispersed={dispersed}");
    }
    static void VerifyDamage()
    {
        foreach(bool routing in new[]{false,true})
        {
            var w=World(8);w.Battalions[1].Routing=routing;w.Withdraw(1);
            w.ApplyMeleeHit(0,8,22);Require(Mathf.Abs(w.Soldiers.Health[8]-34)<.001f,"Triple butt-strike damage vs fleeing");
            w.ApplyMeleeHit(0,9,34);Require(!w.Soldiers.Alive[9],"Bayonet strike can kill fleeing unwounded soldier");
            w.ApplyWound(10,10,DeathCause.Musket,w.Terrain.SurfacePoint(w.Soldiers.Positions[10]));Require(w.Soldiers.Health[10]==90,"No pursuit bonus on musket wounds");
            w.Soldiers.MeleeActions[8]=MeleeAction.Thrust;w.Soldiers.MeleeStarts[8]=-1;w.Soldiers.MeleeEnds[8]=1;w.Soldiers.MeleeTargets[8]=0;w.Soldiers.StrikeResolved[8]=false;
            w.Soldiers.Positions[8]=w.Soldiers.Positions[0]+Vector2.up;new MeleeSystem(w).Step(.05f,new bool[2]);
            Require(w.Soldiers.Health[0]==100&&!MeleeSystem.IsStrike(w.Soldiers.MeleeActions[8]),"Fleeing unit cannot resolve a queued counterattack");
        }
        var normal=World(8);normal.ApplyMeleeHit(0,8,34);Require(normal.Soldiers.Health[8]==66,"Normal melee damage preserved");
    }
    static void VerifyDispersedContact()
    {
        var w=World(8);var u=w.Battalions[0];var v=w.Battalions[1];w.SetBayonets(u.Id,true);
        MeleeEncounter.Place(w,u,new Vector2(0,-200),0);MeleeEncounter.Place(w,v,new Vector2(0,200),Mathf.PI);
        u.InMelee=true;w.Withdraw(v.Id);w.Soldiers.Positions[0]=Vector2.zero;w.Soldiers.Positions[8]=Vector2.up*1.3f;
        w.ContactMovement.Prepare();var melee=new MeleeSystem(w);
        w.Soldiers.MeleeActions[0]=MeleeAction.Thrust;w.Soldiers.MeleeStarts[0]=-1;w.Soldiers.MeleeEnds[0]=1;w.Soldiers.MeleeTargets[0]=8;w.Soldiers.StrikeResolved[0]=false;
        melee.Step(.05f,new bool[2]);Require(!w.Soldiers.Alive[8],"Real dispersed tail contact survives separation of regiment centres");
        w.Soldiers.MeleeActions[1]=MeleeAction.Thrust;w.Soldiers.MeleeStarts[1]=-1;w.Soldiers.MeleeEnds[1]=1;w.Soldiers.MeleeTargets[1]=9;w.Soldiers.StrikeResolved[1]=false;
        melee.Step(.05f,new bool[2]);Require(w.Soldiers.Health[9]==100,"No melee damage across a distant gap");
    }
    static void VerifyBracing()
    {
        foreach(string scenario in new[]{"frontal","flank","square","forming"})
        {
            var w=new BattleWorld(MeleeEncounter.Config(scenario));MeleeEncounter.Setup(w,scenario);
            var combat=new CombatSystem(w);var defender=w.Battalions[1];bool braced=false;float strength=0;
            for(int tick=0;tick<280;tick++)
            {
                if(scenario=="forming"&&tick==35)w.SetFormation(1,FormationKind.Square);
                w.StepMovement(.05f);combat.Step(.05f);
                if(defender.Bracing)
                {
                    braced=true;Require(!w.ContactMovement.Loose[1],"Prepared bayonet wall holds its slots through the charge");
                    int man=w.SoldierAtSlot(defender,FormationMath.EffectiveFiles(defender)/2);
                    var incoming=w.Soldiers.Positions[man]+FormationMath.Forward(w.Soldiers.Facings[man])*2;
                    strength=Mathf.Max(strength,MeleeSystem.BraceStrength(w,man,incoming));
                }
            }
            if(scenario=="frontal")Require(braced&&strength>.72f&&combat.Melee.RepelledCharges>0,"Prepared front line visibly braces and resists momentum");
            if(scenario=="flank")Require(!braced,"A frontal wall cannot defend an exposed flank");
            w.Withdraw(defender.Id);w.ContactMovement.Prepare();Require(!defender.Bracing&&w.ContactMovement.Loose[1],"Retreat abandons the defensive wall");
            Debug.Log($"INFANTRY_BRACE_PASSED {scenario} braced={braced} strength={strength:F2} impacts={combat.Melee.ChargeImpacts}");
        }
    }
    public static void VerifyAll()
    {
        Verify();MeleePresentationVerification.Verify();CavalryCombatVerification.VerifySimulation();
        CavalryCombatVerification.VerifyAssets();CavalryCombatVerification.VerifySquareContactFire();
        CavalryCombatVerification.VerifyScale();FormationTacticsVerification.Verify();
        InfantryPerformanceVerification.VerifyMovement();ArtilleryRecoveryVerification.Verify();
    }
    public static void BuildAll(){VerifyAll();BattlePlaytestBuild.BuildAll();}
}
