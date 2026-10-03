using System;
using EmpireBattle;
using UnityEngine;
public static class MeleePresentationVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Melee presentation: "+message);}
    public static void Verify()
    {
        foreach(var action in new[]{MeleeAction.Thrust,MeleeAction.ButtStrike,MeleeAction.SlashLeft,MeleeAction.SlashRight})
        {
            float start=3,end=start+MeleeSystem.AttackDuration(action),impact=start+MeleeSystem.ImpactDelay(action);
            Require(Mathf.Abs(MeleePresentationTiming.Phase(action,start,end,impact)-.5f)<.0001f,"Weapon reaches contact pose at damage time: "+action);
            float last=-1;
            for(int sample=0;sample<=100;sample++)
            {float phase=MeleePresentationTiming.Phase(action,start,end,start+sample*(end-start)/100);Require(phase>=last&&phase>=0&&phase<=1,"Continuous ordered attack phase");last=phase;}
        }
        Require(MeleePresentationTiming.Phase(MeleeAction.Parry,0,.65f,0)==.5f,"Parry begins deflected at contact");
        VerifyContactEffects();
        VerifyDefensiveReadiness();
        foreach(bool miss in new[]{false,true})
        {
            var w=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
            var s=w.Soldiers;int attacker=w.Battalions[0].SoldierStart,target=w.Battalions[1].SoldierStart;
            foreach(var unit in w.Battalions){unit.FireAtWill=false;unit.Cohesion=0;unit.BayonetsFixed=true;}
            s.PikeSergeants[attacker]=false;
            s.Positions[attacker]=Vector2.zero;s.Positions[target]=new Vector2(0,miss?10:1);
            s.Facings[target]=0; // Exposed back makes this a deterministic hit rather than a parry.
            s.MeleeActions[attacker]=MeleeAction.Thrust;s.MeleeTargets[attacker]=target;
            s.MeleeStarts[attacker]=-1;s.MeleeEnds[attacker]=1;s.StrikeResolved[attacker]=false;
            float health=s.Health[target];var melee=new MeleeSystem(w);melee.Step(.05f,new bool[w.Battalions.Length]);
            Require(w.TryGetMeleeContact(1,out var contact),"Resolved attack publishes an outcome");
            Require(contact.Outcome==(miss?MeleeContactOutcome.Miss:MeleeContactOutcome.Hit),"Outcome matches actual reach");
            Require(miss?s.Health[target]==health:s.Health[target]<health,"Sound outcome agrees with damage");
            int sequence=w.MeleeContactSequence;melee.Step(.05f,new bool[w.Battalions.Length]);
            Require(w.MeleeContactSequence==sequence,"Committed strike resolves exactly once");
            for(int i=0;i<BattleWorld.MeleeContactCapacity+3;i++)w.RecordMeleeContact(attacker,target,MeleeAction.Thrust,MeleeContactOutcome.Swing);
            Require(!w.TryGetMeleeContact(1,out _)&&w.TryGetMeleeContact(w.MeleeContactSequence,out _),"Bounded contact history expires old events and retains new ones");
        }
        Debug.Log("MELEE_PRESENTATION_VERIFIED contact-aligned poses, immediate parry, real hit/miss damage parity, exactly-once and bounded event history");
    }
    static void VerifyContactEffects()
    {
        var w=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
        int a=w.Battalions[0].SoldierStart,t=w.Battalions[1].SoldierStart;
        w.Soldiers.Positions[a]=Vector2.zero;w.Soldiers.Positions[t]=Vector2.up;
        var cameraObject=new GameObject("Melee effect verification camera");var effectObject=new GameObject("Melee effect verification");
        try
        {
            var camera=cameraObject.AddComponent<Camera>();camera.transform.position=Vector3.up*5;
            var blood=effectObject.AddComponent<BattleBlood>();blood.Initialize(w,camera,null);
            w.RecordMeleeContact(a,t,MeleeAction.Thrust,MeleeContactOutcome.Miss);
            w.RecordMeleeContact(a,t,MeleeAction.Thrust,MeleeContactOutcome.Parry);
            w.RecordMeleeContact(a,t,MeleeAction.ButtStrike,MeleeContactOutcome.Hit);
            blood.Step(0,w.Time);Require(blood.ActiveDroplets==0,"Misses, parries and stock blows emit no blade blood");
            w.RecordMeleeContact(a,t,MeleeAction.Thrust,MeleeContactOutcome.Hit);
            int sprayCount=BattleEffectTuning.BloodCount(12);
            blood.Step(0,w.Time);Require(blood.ActiveDroplets==sprayCount&&blood.MeleeSpraysEmitted==1,"Nonfatal blade hit has a tuned localized spray");
            blood.Step(0,w.Time);Require(blood.ActiveDroplets==sprayCount,"Contact effect emitted once");
            for(int i=0;i<100;i++)w.RecordMeleeContact(a,t,MeleeAction.Thrust,MeleeContactOutcome.Hit);
            blood.Step(0,w.Time);Require(blood.ActiveDroplets<=sprayCount*8&&blood.MeleeSpraysEmitted<=8&&blood.PermanentMarks==0,"Contact effects stay within frame budget and add no permanent marks");
        }
        finally{UnityEngine.Object.DestroyImmediate(effectObject);UnityEngine.Object.DestroyImmediate(cameraObject);}
    }
    static void VerifyDefensiveReadiness()
    {
        foreach(var action in new[]{MeleeAction.Ready,MeleeAction.Thrust,MeleeAction.Stagger})
        {
            var w=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=20,EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0});
            var s=w.Soldiers;int a=w.Battalions[0].SoldierStart,t=w.Battalions[1].SoldierStart;
            foreach(var u in w.Battalions){u.Cohesion=0;u.FireAtWill=false;u.BayonetsFixed=true;}
            s.PikeSergeants[a]=false;s.Positions[a]=Vector2.zero;s.Positions[t]=Vector2.up;s.Facings[t]=Mathf.PI;
            float start=-1;
            for(int seed=0;seed<200&&RifleFireTiming.Variation(a+(int)(start*31),347)>=.24f;seed++)start-=.04f;
            Require(RifleFireTiming.Variation(a+(int)(start*31),347)<.24f,"Defense fixture has favorable parry roll");
            s.MeleeActions[a]=MeleeAction.Thrust;s.MeleeStarts[a]=start;s.MeleeEnds[a]=1;s.MeleeTargets[a]=t;s.StrikeResolved[a]=false;
            s.MeleeActions[t]=action;s.MeleeStarts[t]=0;s.MeleeEnds[t]=1;s.StrikeResolved[t]=false;
            var melee=new MeleeSystem(w);melee.Step(.05f,new bool[w.Battalions.Length]);
            Require(w.TryGetMeleeContact(1,out var contact),"Defense outcome published");
            bool ready=action==MeleeAction.Ready;
            Require(contact.Outcome==(ready?MeleeContactOutcome.Parry:MeleeContactOutcome.Hit),"Only a ready defender can parry a committed strike");
            if(ready)
            {
                Require(s.MeleeActions[t]==MeleeAction.Parry&&s.MeleeActions[a]==MeleeAction.Recoil,"Parried thrust deflects both combatants");
                Require(s.Health[t]==100,"Successful parry does not chip away health");
            }
        }
    }
}
