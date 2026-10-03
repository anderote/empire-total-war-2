using System.Collections;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleAudio
    {
        bool audioRuntimeVerificationStarted;

        // Explicit command-line acceptance run; never mutates an ordinary playtest.
        IEnumerator VerifyRuntimeAudio()
        {
            yield return null;
            if(!AudioRequire(recordedBank&&recordedBank.MarchingLoop&&recordedBank.MarchMusic,"recorded bank loaded"))yield break;
            foreach(var unit in world.Battalions){unit.FireAtWill=false;world.Halt(unit.Id);}
            foreach(var gun in artillery.Guns)gun.FireAtWill=false;
            var march=world.Battalions[0];
            world.IssueMarch(march.Id,march.Position+new Vector2(0,30));
            var view=FindAnyObjectByType<BattleView>();
            if(!AudioRequire(view,"battle view available"))yield break;
            view.EnableInput=false;
            view.SetInspectionView(march.Position,20,35);
            yield return new WaitForSecondsRealtime(2);
            if(!AudioRequire(ActiveMarchVoices>0&&ActiveMarchVoices<=MarchVoiceLimit,"bounded formation loops playing"))yield break;
            if(!AudioRequire(ActiveMusicVoices==1&&FoleyEventsPlayed>0,"spatial march music and equipment playing"))yield break;
            if(!AudioRequire(ActiveAmbienceVoices>0&&ActiveAmbienceVoices<=AmbienceVoiceLimit,"bounded landscape beds playing"))yield break;
            bool paused=true;isPaused=()=>paused;
            yield return null;yield return null;
            float musicTime=musicSource.time;
            yield return new WaitForSecondsRealtime(.3f);
            if(!AudioRequire(!musicSource.isPlaying&&Mathf.Abs(musicSource.time-musicTime)<.02f,"pause freezes loop playback"))yield break;
            bool atmospherePaused=true;
            foreach(var source in ambienceSources)atmospherePaused&=!source.isPlaying;
            foreach(var source in horseSources)atmospherePaused&=!source.isPlaying;
            if(!AudioRequire(atmospherePaused,"pause suspends atmosphere and horse loops"))yield break;
            paused=false;
            yield return new WaitForSecondsRealtime(.4f);
            if(!AudioRequire(musicSource.isPlaying,"pause resumes existing loops"))yield break;
            MarchMusicEnabled=false;
            yield return new WaitForSecondsRealtime(1.2f);
            if(!AudioRequire(!musicSource.isPlaying&&ActiveMarchVoices>0,"music mute leaves marching audible"))yield break;
            AudioEnabled=false;
            yield return null;yield return null;
            bool silent=!musicSource.isPlaying;
            foreach(var source in sources)silent&=!source.isPlaying;
            foreach(var source in marchSources)silent&=!source.isPlaying;
            foreach(var source in horseSources)silent&=!source.isPlaying;
            foreach(var source in ambienceSources)silent&=!source.isPlaying;
            if(!AudioRequire(silent,"master mute stops every source"))yield break;
            AudioEnabled=MarchMusicEnabled=true;
            yield return new WaitForSecondsRealtime(1.2f);
            if(!AudioRequire(ActiveMarchVoices>0&&musicSource.isPlaying,"unmute restores marching"))yield break;
            foreach(var unit in world.Battalions)world.Halt(unit.Id);
            yield return new WaitForSecondsRealtime(1.5f);
            if(!AudioRequire(ActiveMarchVoices==0&&ActiveMusicVoices==1,"halt stops footsteps and keeps idle regimental music"))yield break;
            float savedVolley=march.LastVolleyTime, savedIncoming=march.LastIncomingFireTime, savedMelee=march.LastMeleeTime;
            march.LastVolleyTime=world.Time;
            if(!AudioRequire(!CanPlayRegimentalMusic(march),"firing suppresses regimental music"))yield break;
            march.LastVolleyTime=savedVolley;
            march.LastIncomingFireTime=world.Time;
            if(!AudioRequire(!CanPlayRegimentalMusic(march),"incoming fire suppresses regimental music"))yield break;
            march.LastIncomingFireTime=savedIncoming;
            march.LastMeleeTime=world.Time;
            if(!AudioRequire(!CanPlayRegimentalMusic(march),"recent melee suppresses regimental music"))yield break;
            march.LastMeleeTime=savedMelee;
            int soldier=ClosestSoldier(march,-1);
            float settleDeadline=Time.realtimeSinceStartup+5;
            while(soldier>=0&&world.Soldiers.MovementSpeeds[soldier]>.04f&&Time.realtimeSinceStartup<settleDeadline)
            {yield return null;soldier=ClosestSoldier(march,-1);}
            if(!AudioRequire(soldier>=0&&!march.Moving&&!march.Routing&&!march.InMelee&&world.Soldiers.MovementSpeeds[soldier]<=.04f,"reload fixture alive and stationary"))yield break;
            float fire=world.Time;
            world.Soldiers.FireTimes[soldier]=fire;
            world.Soldiers.KneelTimes[soldier]=fire+RifleFireTiming.KneelDelay;
            world.Soldiers.ReloadReadyTimes[soldier]=fire+12;
            world.Soldiers.ReloadDurations[soldier]=12;
            int before=ReloadEventsPlayed;
            float reloadDeadline=Time.realtimeSinceStartup+10;
            while(world.Time<fire+6.5f&&Time.realtimeSinceStartup<reloadDeadline)yield return null;
            Debug.Log($"BATTLE_AUDIO_RELOAD_FIXTURE cues={ReloadEventsPlayed-before} elapsed={world.Time-fire:F2} alive={world.Soldiers.Alive[soldier]} knockedDown={world.Soldiers.IsKnockedDown(soldier,world.Time)} moving={world.Soldiers.MovementSpeeds[soldier]:F3}");
            if(!AudioRequire(ReloadEventsPlayed-before==4,"one cartridge, two ramrod strokes, one lock cue"))yield break;
            // Explicit fixture checks source routing for the actual mounted formations.
            BattalionState horse=null;
            foreach(var unit in world.Battalions)if(unit.Kind==UnitKind.Cavalry){horse=unit;break;}
            if(!AudioRequire(horse!=null,"mounted formation available"))yield break;
            view.SetInspectionView(horse.Position,20,35);
            world.IssueMarch(horse.Id,horse.Position+new Vector2(0,80));
            yield return new WaitForSecondsRealtime(2);
            if(!AudioRequire(ActiveHorseVoices>0&&ActiveHorseVoices<=HorseVoiceLimit,"nearby cavalry uses bounded hoof loops"))yield break;
            foreach(int id in marchUnits)
                if(!AudioRequire(id<0||world.Battalions[id].Kind==UnitKind.Infantry,"boot sources remain infantry only"))yield break;
            int rider=ClosestSoldier(horse,-1);
            if(!AudioRequire(rider>=0,"mounted audio fixture alive"))yield break;
            for(int gait=0;gait<3;gait++)
            {
                world.Soldiers.MovementSpeeds[rider]=gait==0?2:gait==1?4:7;
                UpdateHorses(world.Time);
                bool matched=false;
                for(int slot=0;slot<HorseVoiceLimit;slot++)
                    if(horseUnits[slot]==horse.Id)matched=horseSources[slot].clip==(gait==0?recordedBank.HoofWalk:gait==1?trotLoop:recordedBank.Gallop);
                if(!AudioRequire(matched,"mounted gait routes to distinct clip "+gait))yield break;
            }
            nextHorseCall[horse.Id]=world.Time;
            int horseBefore=HorseEventsPlayed;UpdateHorses(world.Time);
            if(!AudioRequire(HorseEventsPlayed>horseBefore,"sparse horse call playback"))yield break;
            world.Halt(horse.Id);
            yield return new WaitForSecondsRealtime(2);
            if(!AudioRequire(ActiveHorseVoices==0,"halt fades hoof movement"))yield break;
            if(!AudioRequire(atmosphere,"battle atmosphere available"))yield break;
            atmosphere.SetWeather(WeatherKind.Rain);
            atmosphere.SetHour(12f);
            yield return new WaitForSecondsRealtime(1);
            if(!AudioRequire(atmosphere.Weather==WeatherKind.Clear&&atmosphere.Hour==7f&&!ambienceSources[2].isPlaying,
                "battle remains clear dawn without rain audio"))yield break;
            var crewGun=artillery.Guns[0];view.SetInspectionView(crewGun.Position,20,35);
            yield return null;
            int crewBefore=CrewEventsPlayed;
            var savedPhase=crewGun.Phase;float savedElapsed=crewGun.PhaseElapsed;
            foreach(var phase in new[]{ArtilleryPhase.Sponging,ArtilleryPhase.Loading,ArtilleryPhase.Ramming,ArtilleryPhase.RunUp})
            {
                crewGun.Phase=crewPhases[crewGun.Id]=phase;
                float cue=ArtillerySystem.Duration(phase)*CrewCueFraction(phase,0);
                crewElapsed[crewGun.Id]=cue-.02f;crewGun.PhaseElapsed=cue+.02f;
                UpdateCrew(world.Time);
                int after=CrewEventsPlayed;UpdateCrew(world.Time);
                if(!AudioRequire(CrewEventsPlayed==after,"stationary gun phase does not duplicate cues"))yield break;
            }
            if(!AudioRequire(CrewEventsPlayed-crewBefore==4,"four gun service phases play timed foley"))yield break;
            crewGun.Phase=savedPhase;crewGun.PhaseElapsed=savedElapsed;
            ObserveCrewClock();
            view.SetInspectionView(march.Position,20,35);
            yield return null;
            int striker=ClosestSoldier(march,-1), victim=-1;
            foreach(var unit in world.Battalions)
                if(unit.Side!=march.Side&&unit.Kind==UnitKind.Infantry){victim=ClosestSoldier(unit,-1);if(victim>=0)break;}
            if(!AudioRequire(striker>=0&&victim>=0,"melee audio fixture participants alive"))yield break;
            observedMeleeContact=world.MeleeContactSequence;
            int meleeBefore=MeleeEventsPlayed;
            world.RecordMeleeContact(striker,victim,MeleeAction.Thrust,MeleeContactOutcome.Miss);
            UpdateNearbyActivity(world.Time);
            if(!AudioRequire(MeleeEventsPlayed==meleeBefore,"missed melee swing emits no impact"))yield break;
            world.RecordMeleeContact(striker,striker,MeleeAction.Thrust,MeleeContactOutcome.Hit);
            UpdateNearbyActivity(world.Time);
            if(!AudioRequire(MeleeEventsPlayed>meleeBefore,"resolved nearby melee contact emits impact"))yield break;
            int meleeAfter=MeleeEventsPlayed;UpdateNearbyActivity(world.Time);
            if(!AudioRequire(MeleeEventsPlayed==meleeAfter,"stationary strike clock does not repeat impacts"))yield break;
            world.RecordMeleeContact(striker,striker,MeleeAction.Thrust,MeleeContactOutcome.Parry);
            UpdateNearbyActivity(world.Time);
            if(!AudioRequire(MeleeEventsPlayed>meleeAfter,"resolved parry emits weapon contact"))yield break;
            AudioClip recorded=recordedBank.MarchingLoop;
            Initialize(world,artillery,battleCamera,isPaused);
            yield return null;yield return null;
            if(!AudioRequire(recorded&&recordedBank.MarchingLoop==recorded,"restart preserves imported recordings"))yield break;
            if(!AudioRequire(FindObjectsByType<AudioSource>().Length==TotalVoiceLimit,"restart keeps fixed 62-source budget"))yield break;
            Debug.Log("BATTLE_AUDIO_RUNTIME_PASSED: march/equipment/music, pause/resume, music/master mute, idle music, combat suppression, four reload cues, cavalry gaits/calls, service phases, melee contacts, weather, restart and 62-source budget.");
            Application.Quit(0);
        }

        bool AudioRequire(bool condition,string message)
        {
            if(condition){Debug.Log("BATTLE_AUDIO_CHECK: "+message);return true;}
            Debug.LogError("BATTLE_AUDIO_RUNTIME_FAILED: "+message);Application.Quit(1);return false;
        }
    }
}
