using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleAudio
    {
        public const int HorseVoiceLimit=4, AmbienceVoiceLimit=3;
        public const int TotalVoiceLimit=VoiceCount+MarchVoiceLimit+1+HorseVoiceLimit+AmbienceVoiceLimit;
        public int ActiveHorseVoices { get; private set; }
        public int ActiveAmbienceVoices { get; private set; }
        public int HorseEventsPlayed { get; private set; }
        public int CrewEventsPlayed { get; private set; }
        public int MeleeEventsPlayed { get; private set; }
        readonly Dictionary<SoundKind,AudioClip[]> atmosphericClips=new Dictionary<SoundKind,AudioClip[]>();
        AudioSource[] horseSources, ambienceSources;
        AudioLowPassFilter[] horseFilters;
        AudioClip trotLoop;
        BattleAtmosphere atmosphere;
        int[] horseUnits;
        float[] nextHorseCall, nextTack, nextFlag, nextEffort;
        ArtilleryPhase[] crewPhases;
        float[] crewElapsed;
        float previousAtmosphereTime, nextThunder, battlePressure;
        int observedMeleeContact;

        void InitializeAtmosphericAudio()
        {
            AddAtmosphereSet(SoundKind.Hoof,4,.22f,8121);
            AddAtmosphereSet(SoundKind.Tack,3,.5f,8211);
            AddAtmosphereSet(SoundKind.CrewStep,3,.25f,8311);
            AddAtmosphereSet(SoundKind.Sponge,3,.65f,8411);
            AddAtmosphereSet(SoundKind.ShotLoad,3,.3f,8511);
            AddAtmosphereSet(SoundKind.Wheel,3,1.2f,8611);
            AddAtmosphereSet(SoundKind.Flag,3,.8f,8711);
            AddAtmosphereSet(SoundKind.MeleeHit,4,.3f,8811);
            AddAtmosphereSet(SoundKind.Parry,3,.22f,8861);
            AddAtmosphereSet(SoundKind.Swish,3,.35f,8911);
            AddAtmosphereSet(SoundKind.Thunder,2,6f,9011);
            AddAtmosphereSet(SoundKind.Wind,1,8f,9111);
            trotLoop=CreateHoofLoop();
            horseSources=new AudioSource[HorseVoiceLimit];horseFilters=new AudioLowPassFilter[HorseVoiceLimit];
            horseUnits=new int[HorseVoiceLimit];
            nextHorseCall=new float[world.Battalions.Length];nextTack=new float[world.Battalions.Length];
            nextFlag=new float[world.Battalions.Length];nextEffort=new float[world.Battalions.Length];
            for(int i=0;i<HorseVoiceLimit;i++)
            {horseUnits[i]=-1;horseSources[i]=CreateLoop("Cavalry hooves",null,180,out horseFilters[i]);}
            ambienceSources=new AudioSource[AmbienceVoiceLimit];
            var wind=atmosphericClips[SoundKind.Wind][0];
            CrossfadeProceduralLoop(wind);
            AudioClip[] beds={wind,recordedBank?recordedBank.Nature:null,recordedBank?recordedBank.Rain:null};
            for(int i=0;i<AmbienceVoiceLimit;i++)
            {
                ambienceSources[i]=CreateLoop("Landscape bed "+i,beds[i],1,out var filter);
                ambienceSources[i].spatialBlend=0;ambienceSources[i].priority=240;
                filter.cutoffFrequency=i==0?1400:10000;
            }
            crewPhases=new ArtilleryPhase[artillery.Guns.Length];crewElapsed=new float[artillery.Guns.Length];
            for(int i=0;i<crewPhases.Length;i++){crewPhases[i]=artillery.Guns[i].Phase;crewElapsed[i]=artillery.Guns[i].PhaseElapsed;}
            for(int i=0;i<nextHorseCall.Length;i++)nextHorseCall[i]=world.Time+12+RifleFireTiming.Variation(i,151)*25;
            atmosphere=FindAnyObjectByType<BattleAtmosphere>();
            observedMeleeContact=world.MeleeContactSequence;
            previousAtmosphereTime=world.Time;nextThunder=world.Time+35;battlePressure=0;
            HorseEventsPlayed=CrewEventsPlayed=MeleeEventsPlayed=ActiveHorseVoices=ActiveAmbienceVoices=0;
        }

        void AddAtmosphereSet(SoundKind kind,int count,float duration,int seed) =>
            atmosphericClips[kind]=BuildSet(kind,count,duration,seed);

        // Two diagonal-pair beats per stride, with three quiet offset horses for a squadron body.
        AudioClip CreateHoofLoop()
        {
            const float duration=2.4f;var data=new float[Mathf.RoundToInt(duration*SampleRate)];
            for(int horse=0;horse<3;horse++)for(int beat=0;beat<8;beat++)
            {
                var tap=GenerateSamples(SoundKind.Hoof,.22f,SampleRate,9211+horse*101+beat*17);
                int start=Mathf.RoundToInt((beat*.3f+horse*.067f)*SampleRate);
                for(int j=0;j<tap.Length;j++)data[(start+j)%data.Length]+=tap[j]*(horse==0?.6f:.22f);
            }
            var clip=AudioClip.Create("Original squadron trot foley",data.Length,1,SampleRate,false);clip.SetData(data,0);return clip;
        }

        static void CrossfadeProceduralLoop(AudioClip clip)
        {
            var data=new float[clip.samples];clip.GetData(data,0);int n=Mathf.Min(2205,data.Length/8);
            for(int i=0;i<n;i++)data[i]=Mathf.Lerp(data[data.Length-n+i],data[i],i/(float)n);
            clip.SetData(data,0);
        }

        public static int HorseGait(float speed) => speed>5f?2:speed>2.5f?1:0;
        public static float NatureMix(bool rain,float hour,float pressure,float woods) =>
            (rain?.10f:1f)*Mathf.Clamp01((hour-5)/2)*Mathf.Clamp01((21-hour)/3)
            *Mathf.Lerp(1,.22f,Mathf.Clamp01(pressure))*Mathf.Lerp(.45f,1,Mathf.Clamp01(woods));

        void UpdateAtmosphericAudio(bool paused)
        {
            if(horseSources==null)return;
            ActiveHorseVoices=ActiveAmbienceVoices=0;
            float now=PresentationTime!=null?PresentationTime():world.Time;
            if(!AudioEnabled||paused)
            {
                foreach(var source in horseSources){if(!AudioEnabled)source.Stop();else if(source.isPlaying)source.Pause();}
                foreach(var source in ambienceSources){if(!AudioEnabled)source.Stop();else if(source.isPlaying)source.Pause();}
                // Muting must not create a backlog of service/strike cues on unmute.
                if(!AudioEnabled){ObserveCrewClock();previousAtmosphereTime=now;observedMeleeContact=world.MeleeContactSequence;}
                return;
            }
            if(wasPaused)
            {foreach(var source in horseSources)source.UnPause();foreach(var source in ambienceSources)source.UnPause();}
            UpdateHorses(now);
            UpdateCrew(now);
            UpdateNearbyActivity(now);
            UpdateLandscape(now);
            previousAtmosphereTime=now;
        }

        void UpdateHorses(float now)
        {
            for(int slot=0;slot<HorseVoiceLimit;slot++)
            {
                float best=180*180;int chosen=-1;
                foreach(var u in world.Battalions)
                {
                    if(u.Kind!=UnitKind.Cavalry||u.AliveCount<=0)continue;
                    bool used=false;for(int j=0;j<HorseVoiceLimit;j++)if(j!=slot&&horseUnits[j]==u.Id)used=true;
                    if(used)continue;
                    float d=(world.Terrain.SurfacePoint(u.Position)-battleCamera.transform.position).sqrMagnitude;
                    if(d<best){best=d;chosen=u.Id;}
                }
                var source=horseSources[slot];
                // Fade the old owner before moving its sound to a different formation.
                if(horseUnits[slot]!=chosen&&source.volume>.001f){FadeLoop(source,0);continue;}
                if(horseUnits[slot]!=chosen){source.Stop();horseUnits[slot]=chosen;}
                float gain=0;
                if(chosen>=0)
                {
                    var u=world.Battalions[chosen];int soldier=ClosestSoldier(u,-1);
                    if(soldier>=0)
                    {
                        var position=world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier],.35f);
                        float speed=world.Soldiers.MovementSpeeds[soldier];int gait=HorseGait(speed);
                        var desired=gait==2&&recordedBank&&recordedBank.Gallop?recordedBank.Gallop:
                            gait==0&&recordedBank&&recordedBank.HoofWalk?recordedBank.HoofWalk:trotLoop;
                        if(source.clip!=desired){source.Stop();source.clip=desired;}
                        source.transform.position=position;
                        source.pitch=gait==2?Mathf.Clamp(speed/7,.85f,1.18f):gait==1?Mathf.Clamp(speed/4,.85f,1.2f):Mathf.Clamp(speed/2,.75f,1.15f);
                        float road=RuralFieldLayout.RoadDistance(world.Soldiers.Positions[soldier]);
                        bool wet=atmosphere&&atmosphere.Weather==WeatherKind.Rain;
                        horseFilters[slot].cutoffFrequency=Mathf.Lerp(wet?2800:road<7?9500:4200,1200,Mathf.Clamp01(Mathf.Sqrt(best)/180));
                        if(speed>.1f)gain=Mathf.Clamp01(MasterVolume)*(gait==2?.20f:.13f)*Mathf.Clamp01(u.AliveCount/30f);
                        if(speed>.1f&&now>=nextTack[chosen])
                        {nextTack[chosen]=now+.9f+RifleFireTiming.Variation(chosen,(int)now)*.6f;
                            if(Play(SoundKind.Tack,position+Vector3.up,.055f,0,70,chosen+(int)(now*10)))HorseEventsPlayed++;}
                        if(now>=nextHorseCall[chosen])
                        {nextHorseCall[chosen]=now+22+RifleFireTiming.Variation(chosen,(int)(now*3))*30;
                            if(recordedBank&&recordedBank.HorseCall!=null&&recordedBank.HorseCall.Length>0&&Play(SoundKind.HorseCall,position+Vector3.up,.10f,0,150,chosen))HorseEventsPlayed++;}
                    }
                }
                FadeLoop(source,gain);if(source.isPlaying)ActiveHorseVoices++;
            }
        }

        void ObserveCrewClock()
        {foreach(var gun in artillery.Guns){crewPhases[gun.Id]=gun.Phase;crewElapsed[gun.Id]=gun.PhaseElapsed;}}

        public static float CrewCueFraction(ArtilleryPhase phase,int stroke) =>
            phase==ArtilleryPhase.Sponging||phase==ArtilleryPhase.Ramming?(stroke==0?.5f:5f/6):
            phase==ArtilleryPhase.Loading?.5f:stroke==0?.24f:.65f;

        void UpdateCrew(float now)
        {
            int played=0;
            foreach(var gun in artillery.Guns)
            {
                float previous=crewPhases[gun.Id]==gun.Phase?crewElapsed[gun.Id]:0;
                var position=world.Terrain.SurfacePoint(gun.Position-FormationMath.Forward(gun.Facing)*gun.RecoilOffset,.7f);
                if(played<4&&(position-battleCamera.transform.position).sqrMagnitude<95*95)
                {
                    float duration=ArtillerySystem.Duration(gun.Phase);
                    for(int stroke=0;stroke<2;stroke++)
                    {
                        // The visible rod reaches full travel at phase .5 and 5/6, after the crew approaches.
                        if(gun.Phase==ArtilleryPhase.Loading&&stroke>0)continue;
                        float cue=duration*CrewCueFraction(gun.Phase,stroke);
                        if(!CrossedFoleyCue(previous,gun.PhaseElapsed,cue))continue;
                        SoundKind kind;
                        switch(gun.Phase)
                        {
                            case ArtilleryPhase.Sponging:kind=SoundKind.Sponge;break;
                            case ArtilleryPhase.Loading:kind=SoundKind.ShotLoad;break;
                            case ArtilleryPhase.Ramming:kind=SoundKind.Ramrod;break;
                            case ArtilleryPhase.RunUp:case ArtilleryPhase.Recoil:kind=SoundKind.Wheel;break;
                            case ArtilleryPhase.Aiming:kind=SoundKind.Wheel;break;
                            default:continue;
                        }
                        if(Play(kind,position,kind==SoundKind.Wheel?.10f:.12f,0,95,gun.Id+(int)(now*10)))
                        {
                            CrewEventsPlayed++;played++;
                            Play(SoundKind.CrewStep,position,.055f,0,60,gun.Id+stroke);
                            if(gun.Phase==ArtilleryPhase.RunUp&&stroke==0&&recordedBank&&recordedBank.Effort!=null&&recordedBank.Effort.Length>0)
                                Play(SoundKind.Effort,position+Vector3.up,.055f,0,55,gun.Id);
                        }
                    }
                }
                crewPhases[gun.Id]=gun.Phase;crewElapsed[gun.Id]=gun.PhaseElapsed;
            }
        }

        void UpdateNearbyActivity(float now)
        {
            int contacts=0,swings=0;
            foreach(var u in world.Battalions)
            {
                if(u.AliveCount<=0||(world.Terrain.SurfacePoint(u.Position)-battleCamera.transform.position).sqrMagnitude>150*150)continue;
                if(u.Kind==UnitKind.Infantry&&now>=nextFlag[u.Id])
                {
                    nextFlag[u.Id]=now+5+RifleFireTiming.Variation(u.Id,(int)now)*6;
                    Play(SoundKind.Flag,world.Terrain.SurfacePoint(u.Position,2.5f),.035f,0,50,u.Id);
                }
            }
            observedMeleeContact=Mathf.Max(observedMeleeContact,world.MeleeContactSequence-BattleWorld.MeleeContactCapacity);
            while(world.TryGetMeleeContact(observedMeleeContact+1,out var contact))
            {
                if(contact.Time>now)break;
                observedMeleeContact++;
                if(contacts>=8||now-contact.Time>.25f||contact.Outcome==MeleeContactOutcome.Miss)continue;
                Vector3 p=world.Terrain.SurfacePoint(contact.Position,1.3f);
                if((p-battleCamera.transform.position).sqrMagnitude>65*65)continue;
                if(contact.Outcome==MeleeContactOutcome.Swing)
                {if(swings++<8)Play(SoundKind.Swish,p,.065f,0,65,contact.Attacker);continue;}
                bool parry=contact.Outcome==MeleeContactOutcome.Parry;
                if(Play(parry?SoundKind.Parry:SoundKind.MeleeHit,p,parry?.10f:.12f,1,80,contact.Attacker))
                {MeleeEventsPlayed++;contacts++;}
                int unit=world.Soldiers.BattalionIds[contact.Attacker];
                if(!parry&&now>=nextEffort[unit]&&recordedBank&&recordedBank.Effort!=null&&recordedBank.Effort.Length>0)
                {nextEffort[unit]=now+2.4f;Play(SoundKind.Effort,p,.075f,0,65,contact.Attacker);}
            }
        }

        void UpdateLandscape(float now)
        {
            bool raining=atmosphere&&atmosphere.Weather==WeatherKind.Rain;
            float pressure=0;
            foreach(var u in world.Battalions)
                if(now-u.LastVolleyTime<4||u.InMelee)pressure=Mathf.Max(pressure,1-Mathf.Clamp01(Vector3.Distance(world.Terrain.SurfacePoint(u.Position),battleCamera.transform.position)/700));
            foreach(var gun in artillery.Guns)
                if(now-gun.LastShotTime<5)pressure=Mathf.Max(pressure,1-Mathf.Clamp01(Vector3.Distance(world.Terrain.SurfacePoint(gun.Position),battleCamera.transform.position)/1000));
            battlePressure=Mathf.MoveTowards(battlePressure,pressure,Time.unscaledDeltaTime*.25f);
            float woods=0;Vector2 cameraPosition=new Vector2(battleCamera.transform.position.x,battleCamera.transform.position.z);
            for(int i=0;i<world.Terrain.WoodsCenters.Length;i++)
                woods=Mathf.Max(woods,1-Mathf.Clamp01((Vector2.Distance(cameraPosition,world.Terrain.WoodsCenters[i])-world.Terrain.WoodsRadii[i])/100));
            float master=Mathf.Clamp01(MasterVolume);
            float gust=.8f+.2f*Mathf.Sin(now*.11f)*Mathf.Sin(now*.047f);
            FadeLoop(ambienceSources[0],master*(raining?.14f:.075f)*gust*Mathf.Lerp(1,.65f,battlePressure));
            FadeLoop(ambienceSources[1],master*.07f*Mathf.Lerp(1,.15f,Mathf.Clamp01((battleCamera.transform.position.y-50)/250))*NatureMix(raining,atmosphere?atmosphere.Hour:16.5f,battlePressure,woods));
            FadeLoop(ambienceSources[2],raining?master*.20f:0);
            foreach(var source in ambienceSources)if(source.isPlaying)ActiveAmbienceVoices++;
            // The rain preset is occasional thunder weather; no thunder under clear/cloudy/fog.
            if(raining&&now>=nextThunder)
            {
                nextThunder=now+40+RifleFireTiming.Variation((int)now,193)*35;
                Vector3 p=battleCamera.transform.position+new Vector3(180,80,240);
                Schedule(SoundKind.Thunder,p,now,.18f,0,1600,(int)now);
            }
            if(!raining)nextThunder=now+35;
        }

        void CleanupAtmosphericAudio()
        {
            if(horseSources!=null)foreach(var source in horseSources)if(source)Destroy(source.gameObject);
            if(ambienceSources!=null)foreach(var source in ambienceSources)if(source)Destroy(source.gameObject);
            foreach(var clips in atmosphericClips.Values)DestroyClips(clips);atmosphericClips.Clear();
            if(trotLoop)Destroy(trotLoop);
            horseSources=ambienceSources=null;trotLoop=null;atmosphere=null;
        }
    }
}
