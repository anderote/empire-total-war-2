using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleAudio
    {
        public const int MarchVoiceLimit = 6;
        public bool MarchMusicEnabled = true;
        public bool ShowAudioCredits;
        public int ActiveMarchVoices { get; private set; }
        public int ActiveMusicVoices { get; private set; }
        public int FoleyEventsPlayed { get; private set; }
        public int ReloadEventsPlayed { get; private set; }
        BattleAudioBank recordedBank;
        AudioClip[] equipmentClips, cartridgeClips, ramrodClips, lockClips;
        AudioClip[] drumCallClips;
        float[] nextDrumCall;
        float nextFormationCall;
        AudioSource[] marchSources;
        AudioLowPassFilter[] marchFilters;
        AudioSource musicSource;
        AudioLowPassFilter musicFilter;
        readonly int[] nearbyUnits = new int[MarchVoiceLimit];
        readonly int[] marchUnits = new int[MarchVoiceLimit];
        readonly float[] nextEquipment = new float[MarchVoiceLimit];
        float previousFoleyTime, nextNearbyRefresh;
        int musicUnit = -1;
        GUIStyle creditsStyle;
        Vector2 creditsScroll;
        public Rect AudioCreditsRect => new Rect((Screen.width-Mathf.Min(680,Screen.width-32))*.5f,
            (Screen.height-Mathf.Min(430,Screen.height-32))*.5f,Mathf.Min(680,Screen.width-32),Mathf.Min(430,Screen.height-32));

        void InitializeInfantryAudio()
        {
            recordedBank = Resources.Load<BattleAudioBank>("BattleAudio/Bank");
            equipmentClips = BuildSet(SoundKind.Equipment,4,.35f,4021);
            cartridgeClips = BuildSet(SoundKind.Cartridge,3,.25f,5101);
            ramrodClips = BuildSet(SoundKind.Ramrod,4,.28f,6011);
            lockClips = BuildSet(SoundKind.Lock,3,.12f,7013);
            drumCallClips = BuildSet(SoundKind.DrumCall,3,.9f,8093);
            nextDrumCall=new float[world.Battalions.Length];
            nextFormationCall=world.Time;
            marchSources = new AudioSource[MarchVoiceLimit];
            marchFilters = new AudioLowPassFilter[MarchVoiceLimit];
            for(int i=0;i<MarchVoiceLimit;i++)
            {
                nearbyUnits[i]=marchUnits[i]=-1; nextEquipment[i]=0;
                marchSources[i]=CreateLoop("Formation footsteps",recordedBank?recordedBank.MarchingLoop:null,150f,out marchFilters[i]);
            }
            musicSource=CreateLoop("Regimental fife and drum",recordedBank?recordedBank.MarchMusic:null,230f,out musicFilter);
            musicUnit=-1; previousFoleyTime=world.Time; nextNearbyRefresh=0;
            FoleyEventsPlayed=ReloadEventsPlayed=ActiveMarchVoices=ActiveMusicVoices=0;
        }

        AudioSource CreateLoop(string name,AudioClip clip,float range,out AudioLowPassFilter filter)
        {
            var obj=new GameObject(name); obj.transform.SetParent(transform,false);
            var source=obj.AddComponent<AudioSource>();
            source.clip=clip;source.loop=true;source.playOnAwake=false;source.spatialBlend=1;
            source.dopplerLevel=0;source.rolloffMode=AudioRolloffMode.Logarithmic;
            source.minDistance=10;source.maxDistance=range;source.volume=0;source.priority=200;
            filter=obj.AddComponent<AudioLowPassFilter>();filter.lowpassResonanceQ=1;
            return source;
        }

        void UpdateInfantryAudio(bool paused)
        {
            if(marchSources==null)return;
            float now=PresentationTime!=null?PresentationTime():world.Time;
            ActiveMarchVoices=ActiveMusicVoices=0;
            if(!AudioEnabled)
            {
                foreach(var source in marchSources)source.Stop();
                musicSource.Stop();previousFoleyTime=now;return;
            }
            if(paused)
            {
                foreach(var source in marchSources)if(source.isPlaying)source.Pause();
                if(musicSource.isPlaying)musicSource.Pause();
                return;
            }
            if(wasPaused)
            {
                foreach(var source in marchSources)source.UnPause();musicSource.UnPause();
            }
            if(Time.unscaledTime>=nextNearbyRefresh)
            {
                nextNearbyRefresh=Time.unscaledTime+.25f;
                // Fixed-size nearest selection; no per-soldier sources or frame allocations.
                for(int slot=0;slot<MarchVoiceLimit;slot++)
                {
                    float best=210f*210f;int chosen=-1;
                    foreach(var unit in world.Battalions)
                    {
                        if(unit.Kind!=UnitKind.Infantry||unit.AliveCount<=0)continue;
                        bool used=false;for(int j=0;j<slot;j++)if(nearbyUnits[j]==unit.Id)used=true;
                        if(used)continue;
                        float distance=(world.Terrain.SurfacePoint(unit.Position)-battleCamera.transform.position).sqrMagnitude;
                        if(distance<best){best=distance;chosen=unit.Id;}
                    }
                    nearbyUnits[slot]=chosen;
                }
            }
            for(int slot=0;slot<MarchVoiceLimit;slot++)
            {
                int id=marchUnits[slot];bool retained=false;
                for(int j=0;j<MarchVoiceLimit;j++)if(nearbyUnits[j]==id&&id>=0)retained=true;
                var source=marchSources[slot];
                if(!retained&&source.volume<=.001f)
                {
                    source.Stop();marchUnits[slot]=-1;
                    for(int j=0;j<MarchVoiceLimit;j++)
                    {
                        int candidate=nearbyUnits[j];if(candidate<0)continue;
                        bool used=false;for(int k=0;k<MarchVoiceLimit;k++)if(marchUnits[k]==candidate)used=true;
                        if(!used){marchUnits[slot]=candidate;retained=true;break;}
                    }
                    id=marchUnits[slot];
                }
                float gain=0;
                if(id>=0)
                {
                    var unit=world.Battalions[id];
                    int soldier=ClosestSoldier(unit,-1);
                    if(soldier>=0)
                    {
                        Vector3 position=world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier],.4f);
                        bool moving=world.Soldiers.MovementSpeeds[soldier]>.08f&&!unit.InMelee;
                        float distance=Vector3.Distance(position,battleCamera.transform.position);
                        source.transform.position=position;
                        source.pitch=unit.Running||unit.Routing?1.35f:Mathf.Clamp(unit.ActualSpeed/1.2f,.8f,1.15f);
                        bool wet=atmosphere&&atmosphere.Weather==WeatherKind.Rain;
                        float groundCutoff=wet?3500:RuralFieldLayout.RoadDistance(world.Soldiers.Positions[soldier])<7?12000:6500;
                        marchFilters[slot].cutoffFrequency=Mathf.Lerp(groundCutoff,1500,Mathf.Clamp01(distance/150));
                        if(retained&&moving&&distance<150)
                            gain=Mathf.Clamp01(MasterVolume)*.13f*Mathf.Clamp01(unit.AliveCount/80f);
                        if(retained&&distance<95)
                        {
                            if(moving&&now>=nextEquipment[slot])
                            {
                                nextEquipment[slot]=now+(unit.Running?.38f:.65f)+RifleFireTiming.Variation(id,slot)*.12f;
                                if(Play(SoundKind.Equipment,position,.11f,0,95,id+Mathf.RoundToInt(now*20)))FoleyEventsPlayed++;
                            }
                            ObserveReload(unit,soldier,now);
                            int second=ClosestSoldier(unit,soldier);if(second>=0)ObserveReload(unit,second,now);
                        }
                    }
                }
                FadeLoop(source,gain);
                if(source.isPlaying)ActiveMarchVoices++;
            }
            UpdateMarchMusic();previousFoleyTime=now;
        }

        int ClosestSoldier(BattalionState unit,int exclude)
        {
            int closest=-1;float best=float.MaxValue;
            Vector2 listener=new Vector2(battleCamera.transform.position.x,battleCamera.transform.position.z);
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
            {
                if(i==exclude||!world.Soldiers.Alive[i]||world.Soldiers.IsKnockedDown(i,world.Time))continue;
                float distance=(world.Soldiers.Positions[i]-listener).sqrMagnitude;
                if(distance<best){best=distance;closest=i;}
            }
            return closest;
        }

        public static bool CrossedFoleyCue(float previous,float now,float cue) =>
            now>=previous && cue>previous && cue<=now && now-cue<.35f;

        void ScheduleFormationCalls(BattalionState unit,float now)
        {
            // Signals belong to controlled platoon/volley fire, not each skirmisher's shot.
            if(unit.Kind!=UnitKind.Infantry||RifleFireTiming.IndependentFire(unit)||unit.LastVolleySize<=0
                ||unit.Moving||unit.Routing||unit.InMelee)return;
            Vector3 position=world.Terrain.SurfacePoint(unit.Position,1.5f);
            if((position-battleCamera.transform.position).sqrMagnitude>180f*180f)return;
            float first=float.MaxValue,last=float.MinValue;
            for(int soldier=unit.SoldierStart;soldier<unit.SoldierStart+unit.SoldierCount;soldier++)
            {
                if(world.Soldiers.FireVolleys[soldier]!=unit.LastVolleyTime)continue;
                float fire=world.Soldiers.FireTimes[soldier];first=Mathf.Min(first,fire);last=Mathf.Max(last,fire);
            }
            if(first==float.MaxValue||now-last>.35f)return;
            if(MarchMusicEnabled&&now>=nextDrumCall[unit.Id]&&now>=nextFormationCall)
            {
                Schedule(SoundKind.DrumCall,position,unit.LastVolleyTime,.24f,2,180f,unit.Id);
                nextDrumCall[unit.Id]=now+1.5f;
            }
            if(now<nextFormationCall)return;
            // Limit competing formations globally so nearby commands remain intelligible.
            nextFormationCall=now+2.5f;
            ScheduleRecordedCommand(SoundKind.CommandReady,position,Mathf.Max(unit.LastVolleyTime,first-.65f),unit.Id);
            ScheduleRecordedCommand(SoundKind.CommandFire,position,first,unit.Id);
            ScheduleRecordedCommand(SoundKind.CommandReload,position,last+RifleFireTiming.KneelDelay+.2f,unit.Id);
        }

        void ScheduleRecordedCommand(SoundKind kind,Vector3 position,float time,int seed)
        {
            var clips=recordedBank?recordedBank.Clips(kind.ToString()):null;
            if(clips==null||clips.Length==0)return;
            // A late notification cannot fit a ready call in front of the fire command.
            if(kind==SoundKind.CommandReady&&time<=world.Time)return;
            Schedule(kind,position,time,.32f,3,180f,seed);
        }

        void ObserveReload(BattalionState unit,int soldier,float now)
        {
            if(unit.Moving||unit.Routing||unit.InMelee||world.Soldiers.MovementSpeeds[soldier]>.04f)return;
            float fire=world.Soldiers.FireTimes[soldier];
            if(fire<0||now<fire||world.Soldiers.ReloadReadyTimes[soldier]<=now)return;
            float start=unit.Formation==FormationKind.Skirmish?fire+RifleFireTiming.KneelDelay:
                world.Soldiers.KneelTimes[soldier]+RifleFireTiming.LowerDuration;
            float duration=RifleFireTiming.ReloadActionDuration*Mathf.Lerp(.94f,1.06f,RifleFireTiming.Variation(soldier,23));
            // Match the cartridge and two ramrod strokes in MusketReloadAuthoring.Sample.
            EmitReloadCue(SoundKind.Cartridge,start+duration*.20f,soldier,now);
            EmitReloadCue(SoundKind.Ramrod,start+duration*.5475f,soldier,now);
            EmitReloadCue(SoundKind.Ramrod,start+duration*.6825f,soldier,now);
            EmitReloadCue(SoundKind.Lock,start+duration*.88f,soldier,now);
        }

        void EmitReloadCue(SoundKind kind,float cue,int soldier,float now)
        {
            if(!CrossedFoleyCue(previousFoleyTime,now,cue))return;
            if(Play(kind,world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier],1f),.16f,1,95,soldier))
            {FoleyEventsPlayed++;ReloadEventsPlayed++;}
        }

        // Musicians play at rest as well as at a walk, but stand down during combat.
        bool CanPlayRegimentalMusic(BattalionState unit) => MarchMusicEnabled&&unit.Kind==UnitKind.Infantry&&unit.AliveCount>0
            &&!unit.Running&&!unit.Routing&&!unit.Withdrawing&&!unit.InMelee
            &&!unit.FireCycleActive&&unit.ReloadRemaining<=0
            &&world.Time-unit.LastVolleyTime>=8f&&world.Time-unit.LastIncomingFireTime>=8f
            &&world.Time-unit.LastMeleeTime>=8f;

        void UpdateMarchMusic()
        {
            int chosen=-1;
            for(int i=0;i<MarchVoiceLimit;i++)
                if(nearbyUnits[i]>=0&&CanPlayRegimentalMusic(world.Battalions[nearbyUnits[i]])){chosen=nearbyUnits[i];break;}
            if(musicUnit!=chosen&&musicSource.volume<=.001f)
            {
                musicSource.Stop();musicUnit=chosen;
            }
            float gain=0;
            if(musicUnit>=0)
            {
                var unit=world.Battalions[musicUnit];
                Vector2 musician=unit.Position-FormationMath.Right(unit.Facing)*(unit.Files*unit.Spacing*.4f);
                Vector3 position=world.Terrain.SurfacePoint(musician,1.5f);
                musicSource.transform.position=position;
                float distance=Vector3.Distance(position,battleCamera.transform.position);
                musicFilter.cutoffFrequency=Mathf.Lerp(13000,2400,Mathf.Clamp01(distance/230));
                if(chosen==musicUnit&&CanPlayRegimentalMusic(unit)&&distance<230)gain=Mathf.Clamp01(MasterVolume)*.15f;
            }
            FadeLoop(musicSource,gain);
            if(musicSource.isPlaying)ActiveMusicVoices=1;
        }

        void FadeLoop(AudioSource source,float target)
        {
            source.volume=Mathf.MoveTowards(source.volume,target,Time.unscaledDeltaTime*.18f);
            if(source.clip&&source.volume>.001f&&!source.isPlaying)
            {
                source.time=source==musicSource?0:Mathf.Repeat(Time.unscaledTime*.73f+source.GetEntityId().GetHashCode()*.17f,source.clip.length);
                source.Play();
            }
            if(source.volume<=.001f&&target==0)source.Stop();
        }

        void OnGUI()
        {
            if(!ShowAudioCredits)return;
            var rect=AudioCreditsRect;
            Empire.UI.EmpireTheme.Panel(rect,Empire.UI.EmpireTheme.Ink);
            if(creditsStyle==null)creditsStyle=new GUIStyle(Empire.UI.EmpireTheme.Text(12,Empire.UI.EmpireTheme.Ivory)){wordWrap=true};
            string credits=recordedBank&&recordedBank.Credits?recordedBank.Credits.text:"Audio credits unavailable.";
            var viewport=new Rect(rect.x+16,rect.y+16,rect.width-32,rect.height-68);
            float textWidth=viewport.width-20;
            var content=new Rect(0,0,textWidth,Mathf.Max(viewport.height,creditsStyle.CalcHeight(new GUIContent(credits),textWidth)));
            creditsScroll=GUI.BeginScrollView(viewport,creditsScroll,content);
            GUI.Label(content,credits,creditsStyle);
            GUI.EndScrollView();
            if(GUI.Button(new Rect(rect.xMax-100,rect.yMax-40,84,28),"Close"))ShowAudioCredits=false;
        }

        void CleanupInfantryAudio()
        {
            if(marchSources!=null)foreach(var source in marchSources)if(source)Destroy(source.gameObject);
            if(musicSource)Destroy(musicSource.gameObject);
            DestroyClips(equipmentClips);DestroyClips(cartridgeClips);DestroyClips(ramrodClips);DestroyClips(lockClips);
            DestroyClips(drumCallClips);drumCallClips=null;nextDrumCall=null;
            equipmentClips=cartridgeClips=ramrodClips=lockClips=null;
            marchSources=null;musicSource=null;recordedBank=null;ShowAudioCredits=false;
        }
    }
}
