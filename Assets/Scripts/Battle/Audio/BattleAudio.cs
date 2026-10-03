using System;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>
    /// Bounded, procedural battlefield audio. It observes simulation timestamps and keeps
    /// its own fixed set of spatial voices; it does not add audio state to battle systems.
    /// </summary>
    public sealed partial class BattleAudio : MonoBehaviour
    {
        const int VoiceCount = 48;
        const int SampleRate = 22050;
        const int MusketVoices = 3;
        const int CannonVoices = 3;
        const int DirtVoices = 2;
        const int ShellVoices = 2;
        const float MaxDistanceMusket = 420f;
        const float MaxDistanceHeavy = 1500f;

        enum SoundKind { Musket, Cannon, Dirt, Shell, Volley, Echo, Equipment, Cartridge, Ramrod, Lock, Hoof, Tack, CrewStep, Sponge, ShotLoad, Wheel, Flag, MeleeHit, Parry, Swish, Effort, HorseCall, Thunder, Wind, DrumCall, CommandReady, CommandFire, CommandReload }

        sealed class Voice
        {
            public int Priority;
            public int Serial;
            public float Gain;
        }

        struct ScheduledSound
        {
            public bool Active;
            public SoundKind Kind;
            public Vector3 Position;
            public float Time, Gain, MaxDistance;
            public int Priority, Seed;
        }
        readonly ScheduledSound[] scheduled = new ScheduledSound[1024];
        int soundCursor;
        AudioLowPassFilter[] filters;
        AudioClip[] volleyClips, echoClips;
        public Func<float> PresentationTime;
        BattleWorld world;
        ArtillerySystem artillery;
        Camera battleCamera;
        Func<bool> isPaused;
        AudioClip[] musketClips, cannonClips, dirtClips, shellClips;
        AudioSource[] sources;
        Voice[] voices;
        float[] observedVolleys, observedShots;
        int observedImpactSequence;
        int nextVoice, serial;
        int[] clipCursors = new int[Enum.GetValues(typeof(SoundKind)).Length];
        bool wasPaused;
        bool initialized;

        [Range(0f, 1f)] public float MasterVolume = .5f;
        public bool AudioEnabled = true;

        /// <summary>Number of fixed voices currently playing.</summary>
        public int ActiveVoices
        {
            get
            {
                if (sources == null) return 0;
                int count = 0;
                for (int i = 0; i < sources.Length; i++) if (sources[i] && sources[i].isPlaying) count++;
                return count;
            }
        }

        /// <summary>Initialize once battle simulation, artillery and listener camera are ready.</summary>
        public void Initialize(BattleWorld value, ArtillerySystem guns, Camera camera, Func<bool> pauseState)
        {
            MarchMusicEnabled=Empire.UI.GamePreferences.Music;
            Cleanup();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (guns == null) throw new ArgumentNullException(nameof(guns));
            if (!camera) throw new ArgumentNullException(nameof(camera));
            world = value;
            artillery = guns;
            battleCamera = camera;
            isPaused = pauseState;

            observedVolleys = new float[world.Battalions.Length];
            for (int i = 0; i < observedVolleys.Length; i++) observedVolleys[i] = world.Battalions[i].LastVolleyTime;
            observedShots = new float[artillery.Guns.Length];
            for (int i = 0; i < observedShots.Length; i++) observedShots[i] = artillery.Guns[i].LastShotTime;
            observedImpactSequence = artillery.ImpactSequence;

            musketClips = BuildSet(SoundKind.Musket, MusketVoices, .48f, 1101);
            cannonClips = BuildSet(SoundKind.Cannon, CannonVoices, 1.12f, 2407);
            dirtClips = BuildSet(SoundKind.Dirt, DirtVoices, .62f, 3791);
            shellClips = BuildSet(SoundKind.Shell, ShellVoices, 2.05f, 5179);
            volleyClips = BuildSet(SoundKind.Volley, 3, 1.4f, 6349);
            echoClips = BuildSet(SoundKind.Echo, 3, 2.5f, 7193);
            InitializeInfantryAudio();
            InitializeAtmosphericAudio();
            Array.Clear(scheduled,0,scheduled.Length); soundCursor=0;
            sources = new AudioSource[VoiceCount];
            filters = new AudioLowPassFilter[VoiceCount];
            voices = new Voice[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                GameObject sourceObject = new GameObject("Battle audio voice " + (i + 1));
                sourceObject.transform.SetParent(null);
                AudioSource source = sourceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 8f;
                source.maxDistance = MaxDistanceHeavy;
                source.priority = 128;
                sources[i] = source;
                filters[i] = sourceObject.AddComponent<AudioLowPassFilter>();
                filters[i].lowpassResonanceQ = 1f;
                voices[i] = new Voice { Priority = 0, Serial = 0 };
            }
            wasPaused = isPaused != null && isPaused();
            if (wasPaused) PauseVoices();
            initialized = true;
            enabled = true;
            if(!audioRuntimeVerificationStarted&&Array.IndexOf(Environment.GetCommandLineArgs(),"--audio-verify")>=0)
            {audioRuntimeVerificationStarted=true;StartCoroutine(VerifyRuntimeAudio());}
        }

        void LateUpdate()
        {
            if (!initialized || world == null || artillery == null || !battleCamera) return;
            bool paused = isPaused != null && isPaused();
            UpdateInfantryAudio(paused);
            UpdateAtmosphericAudio(paused);
            if (paused != wasPaused)
            {
                if (paused) PauseVoices();
                else ResumeVoices();
                wasPaused = paused;
            }
            if (!AudioEnabled)
            {
                StopVoices();
                Array.Clear(scheduled,0,scheduled.Length);
                for (int i = 0; i < world.Battalions.Length; i++) observedVolleys[i] = world.Battalions[i].LastVolleyTime;
                for (int i = 0; i < artillery.Guns.Length; i++) observedShots[i] = artillery.Guns[i].LastShotTime;
                observedImpactSequence = artillery.ImpactSequence;
                return;
            }
            if (paused) return;
            float master = Mathf.Clamp01(MasterVolume);
            for (int i = 0; i < voices.Length; i++)
                if (sources[i] && sources[i].isPlaying) sources[i].volume = master * voices[i].Gain;

            float now=PresentationTime!=null?PresentationTime():world.Time;
            // Sample individual rifles across the frontage, preserving the same fire times.
            // The body and echo layers carry the size of the remaining formation.
            foreach (BattalionState battalion in world.Battalions)
            {
                if (battalion.LastVolleyTime <= observedVolleys[battalion.Id]) continue;
                observedVolleys[battalion.Id] = battalion.LastVolleyTime;
                ScheduleFormationCalls(battalion,now);
                int sampled=0;
                int stride=Mathf.Max(1,battalion.LastVolleySize/6);
                int ordinal=0;
                for(int soldier=battalion.SoldierStart;soldier<battalion.SoldierStart+battalion.SoldierCount;soldier++)
                {
                    if(world.Soldiers.FireVolleys[soldier]!=battalion.LastVolleyTime)continue;
                    if(ordinal++%stride!=0||sampled>=6)continue;
                    sampled++;
                    Vector3 position=world.Soldiers.FireOrigins[soldier];
                    Schedule(SoundKind.Musket,position,world.Soldiers.FireTimes[soldier],.10f,2,850f,soldier);
                }
                Vector3 center=world.Terrain.SurfacePoint(battalion.Position,1.4f);
                float strength=Mathf.Sqrt(Mathf.Clamp01(battalion.LastVolleySize/250f));
                Schedule(SoundKind.Volley,center,battalion.LastVolleyTime+.10f,.09f+.10f*strength,1,1200f,battalion.Id);
                Schedule(SoundKind.Echo,center,battalion.LastVolleyTime+.65f,.035f+.04f*strength,0,1600f,battalion.Id);
            }
            for(int i=0;i<scheduled.Length;i++)
            {
                if(!scheduled[i].Active||scheduled[i].Time>now)continue;
                var sound=scheduled[i];scheduled[i].Active=false;
                if(now-sound.Time<.35f)
                    Play(sound.Kind,sound.Position,sound.Gain,sound.Priority,sound.MaxDistance,sound.Seed);
            }

            foreach (ArtilleryGun gun in artillery.Guns)
            {
                if (gun.LastShotTime <= observedShots[gun.Id]) continue;
                observedShots[gun.Id] = gun.LastShotTime;
                Vector3 position = world.Terrain.SurfacePoint(gun.Position, 1.35f);
                Play(SoundKind.Cannon, position, .28f, 3, MaxDistanceHeavy,
                    gun.Id * 19 + Mathf.RoundToInt(gun.LastShotTime * 10f));
            }

            // The simulation stores impacts in a fixed ring. Skip overwritten entries if a
            // long frame catches more events than the ring can retain.
            int first = Mathf.Max(observedImpactSequence + 1, artillery.ImpactSequence - artillery.Impacts.Length + 1);
            for (int sequence = first; sequence <= artillery.ImpactSequence; sequence++)
            {
                ArtilleryImpact impact = artillery.Impacts[(sequence - 1) % artillery.Impacts.Length];
                if (impact == null || impact.Sequence != sequence) continue;
                if (impact.Detonates)
                    Play(SoundKind.Shell, impact.Position, .34f, 4, MaxDistanceHeavy, sequence * 31);
                else
                    Play(SoundKind.Dirt, impact.Position, .19f, 2, MaxDistanceHeavy, sequence * 31);
            }
            observedImpactSequence = artillery.ImpactSequence;
        }

        void Schedule(SoundKind kind,Vector3 position,float time,float gain,int priority,float maxDistance,int seed)
        {
            float distance=Vector3.Distance(position,battleCamera.transform.position);
            if(distance>maxDistance)return;
            scheduled[soundCursor]=new ScheduledSound { Active=true,Kind=kind,Position=position,
                Time=time+distance/343f,Gain=gain,Priority=priority,MaxDistance=maxDistance,Seed=seed };
            soundCursor=(soundCursor+1)%scheduled.Length;
        }
        public void SmallArmsImpact(Vector3 position,int seed)
        {
            if(initialized&&AudioEnabled)Schedule(SoundKind.Dirt,position,world.Time,.035f,0,220f,seed);
        }
        public void CameraCannonFire(Vector3 position)
        {
            if(initialized)Play(SoundKind.Cannon,position,.28f,3,MaxDistanceHeavy,Mathf.RoundToInt(world.Time*100f));
        }

        bool Play(SoundKind kind, Vector3 position, float volume, int priority, float maxDistance, int variationSeed)
        {
            if (sources == null || sources.Length == 0 || !AudioEnabled) return false;
            float distance=Vector3.Distance(position,battleCamera.transform.position);
            if(distance>maxDistance)return false;
            // Near cracks win over distant reports when the voice pool is full.
            if(distance<120f)priority++;
            int index = SelectVoice(priority);
            if (index < 0) return false;
            AudioClip clip = NextClip(kind);
            AudioSource source = sources[index];
            if (!source || !clip) return false;
            source.Stop();
            source.transform.position = position;
            source.clip = clip;
            source.volume = Mathf.Clamp01(MasterVolume) * volume;
            // Broad weather rumble needs a larger reference distance than a close material tap.
            source.minDistance = kind==SoundKind.Thunder?80f:8f;
            source.maxDistance = maxDistance;
            source.spread = Mathf.Lerp(5f,65f,Mathf.Clamp01(distance/700f));
            filters[index].cutoffFrequency = Mathf.Lerp(16000f,1600f,Mathf.Clamp01(distance/950f));
            // Tiny deterministic pitch spread keeps a massed volley from sounding identical.
            source.pitch = 1f + ((Hash(variationSeed) & 1023) / 1023f - .5f) * .055f;
            source.Play();
            voices[index].Priority = priority;
            voices[index].Serial = ++serial;
            voices[index].Gain = volume;
            return true;
        }

        int SelectVoice(int priority)
        {
            for (int step = 0; step < sources.Length; step++)
            {
                int index = (nextVoice + step) % sources.Length;
                if (!sources[index].isPlaying)
                {
                    nextVoice = (index + 1) % sources.Length;
                    return index;
                }
            }
            // When saturated, discard low-value dirt first and preserve louder shell impacts.
            int candidate = -1;
            int lowest = int.MaxValue;
            int oldest = int.MaxValue;
            for (int i = 0; i < voices.Length; i++)
            {
                Voice voice = voices[i];
                if (voice.Priority >= priority) continue;
                if (voice.Priority < lowest || (voice.Priority == lowest && voice.Serial < oldest))
                {
                    candidate = i;
                    lowest = voice.Priority;
                    oldest = voice.Serial;
                }
            }
            if (candidate >= 0) nextVoice = (candidate + 1) % sources.Length;
            return candidate;
        }

        AudioClip NextClip(SoundKind kind)
        {
            var recorded = recordedBank ? recordedBank.Clips(kind.ToString()) : null;
            if (recorded != null && recorded.Length > 0)
                return recorded[(clipCursors[(int)kind]++ & int.MaxValue) % recorded.Length];
            if(atmosphericClips!=null&&atmosphericClips.TryGetValue(kind,out var atmosphereSet))
                return atmosphereSet[(clipCursors[(int)kind]++ & int.MaxValue)%atmosphereSet.Length];
            AudioClip[] set;
            int slot = (int)kind;
            switch (kind)
            {
                case SoundKind.Musket: set = musketClips; break;
                case SoundKind.Cannon: set = cannonClips; break;
                case SoundKind.Dirt: set = dirtClips; break;
                case SoundKind.Shell: set = shellClips; break;
                case SoundKind.Volley: set = volleyClips; break;
                case SoundKind.Equipment: set = equipmentClips; break;
                case SoundKind.Cartridge: set = cartridgeClips; break;
                case SoundKind.Ramrod: set = ramrodClips; break;
                case SoundKind.Lock: set = lockClips; break;
                case SoundKind.DrumCall: set = drumCallClips; break;
                // Spoken calls require a recording; never substitute a weapon sound.
                case SoundKind.CommandReady:
                case SoundKind.CommandFire:
                case SoundKind.CommandReload: return null;
                default: set = echoClips; break;
            }
            int index = (clipCursors[slot]++ & int.MaxValue) % set.Length;
            return set[index];
        }

        void PauseVoices()
        {
            if (sources == null) return;
            foreach (AudioSource source in sources) if (source && source.isPlaying) source.Pause();
        }

        void ResumeVoices()
        {
            if (sources == null) return;
            foreach (AudioSource source in sources) if (source && source.clip) source.UnPause();
        }

        void StopVoices()
        {
            if (sources == null) return;
            foreach (AudioSource source in sources) if (source) source.Stop();
        }

        static AudioClip[] BuildSet(SoundKind kind, int count, float duration, int seed)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++)
            {
                float[] samples = GenerateSamples(kind, duration, SampleRate, seed + i * 997);
                AudioClip clip = AudioClip.Create("Procedural " + kind + " " + (i + 1), samples.Length, 1, SampleRate, false);
                if (!clip.SetData(samples, 0))
                {
                    Destroy(clip);
                    throw new InvalidOperationException("Could not fill procedural battle audio clip: " + kind);
                }
                clips[i] = clip;
            }
            return clips;
        }

        /// <summary>
        /// Deterministic mono PCM generator exposed for lightweight bounds checks in tooling.
        /// Every result is finite and constrained to [-1, 1].
        /// </summary>
        public static float[] GenerateSamples(string soundName, float durationSeconds, int sampleRate, int seed)
        {
            SoundKind kind;
            if (!Enum.TryParse(soundName, true, out kind) || !Enum.IsDefined(typeof(SoundKind), kind))
                throw new ArgumentException("Unknown sound kind.", nameof(soundName));
            if(kind==SoundKind.CommandReady||kind==SoundKind.CommandFire||kind==SoundKind.CommandReload)
                throw new ArgumentException("Spoken commands require recorded clips.",nameof(soundName));
            return GenerateSamples(kind, durationSeconds, sampleRate, seed);
        }

        static float[] GenerateSamples(SoundKind kind, float duration, int sampleRate, int seed)
        {
            if (duration <= 0f || float.IsNaN(duration) || float.IsInfinity(duration)) throw new ArgumentOutOfRangeException(nameof(duration));
            if (duration > 10f) throw new ArgumentOutOfRangeException(nameof(duration), "Procedural clips are capped at ten seconds.");
            if (sampleRate < 8000 || sampleRate > 48000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            int count = Mathf.CeilToInt(duration * sampleRate);
            var samples = new float[count];
            uint state = unchecked((uint)seed) + 0x9e3779b9u;
            if (state == 0u) state = 0x6d2b79f5u;
            float previousNoise = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)sampleRate;
                float noise = NextNoise(ref state);
                previousNoise = previousNoise * .72f + noise * .28f;
                float dry = noise;
                float lowNoise = previousNoise;
                float value;
                switch (kind)
                {
                    case SoundKind.DrumCall:
                    {
                        // Original snare signal: double strokes followed by an accented beat.
                        float hit=time<.10f?time:time<.22f?time-.10f:time<.32f?time-.22f:time<.48f?time-.32f:time-.48f;
                        float accent=time>=.48f?1f:.65f;
                        value=accent*Mathf.Min(1f,hit*1800f)*(dry*.43f*Mathf.Exp(-hit*32f)
                            +Mathf.Sin(2f*Mathf.PI*185f*hit)*.30f*Mathf.Exp(-hit*22f)
                            +Mathf.Sin(2f*Mathf.PI*330f*hit)*.12f*Mathf.Exp(-hit*28f));
                        break;
                    }
                    case SoundKind.Musket:
                    {
                        float crack = Mathf.Exp(-time * 24f) * Mathf.Min(1f, time * 900f);
                        float body = Mathf.Exp(-time * 10.5f);
                        value = dry * (.48f * crack + .13f * body)
                            + Mathf.Sin(2f * Mathf.PI * 112f * time) * .19f * body
                            + Mathf.Sin(2f * Mathf.PI * 286f * time) * .07f * Mathf.Exp(-time * 17f);
                        break;
                    }
                    case SoundKind.Volley:
                    {
                        // Overlapping reports create a rolling body without requiring hundreds of voices.
                        float body=Mathf.Exp(-time*3.6f);
                        float pulses=0f;
                        for(int report=0;report<7;report++)
                        {
                            float t=time-report*.065f;
                            if(t>=0)pulses+=Mathf.Exp(-t*25f);
                        }
                        value=dry*.20f*pulses+lowNoise*.25f*body
                            +Mathf.Sin(2f*Mathf.PI*94f*time)*.12f*body;
                        break;
                    }
                    case SoundKind.Echo:
                    {
                        float tail=Mathf.Exp(-time*1.6f)*Mathf.Min(1f,time*18f);
                        float flutter=.65f+.35f*Mathf.Sin(time*19f)*Mathf.Sin(time*11f);
                        value=lowNoise*.38f*tail*flutter+Mathf.Sin(2f*Mathf.PI*72f*time)*.08f*tail;
                        break;
                    }
                    case SoundKind.Cannon:
                    {
                        float attack = Mathf.Min(1f, time * 180f);
                        float body = Mathf.Exp(-time * 3.7f);
                        float tail = Mathf.Exp(-time * 2.25f);
                        value = attack * (dry * (.31f * Mathf.Exp(-time * 15f) + .11f * body) + lowNoise * .12f * tail)
                            + Mathf.Sin(2f * Mathf.PI * 69f * time) * .30f * body
                            + Mathf.Sin(2f * Mathf.PI * 137f * time) * .11f * Mathf.Exp(-time * 5.2f);
                        break;
                    }
                    case SoundKind.Dirt:
                    {
                        float thud = Mathf.Exp(-time * 8.5f);
                        float grit = Mathf.Exp(-time * 5.6f) * Mathf.Min(1f, time * 220f);
                        value = lowNoise * .24f * grit + dry * .12f * grit
                            + Mathf.Sin(2f * Mathf.PI * 82f * time) * .25f * thud
                            + Mathf.Sin(2f * Mathf.PI * 164f * time) * .07f * Mathf.Exp(-time * 13f);
                        break;
                    }
                    case SoundKind.Hoof:
                        value = lowNoise*.31f*Mathf.Exp(-time*26f)
                            +Mathf.Sin(2*Mathf.PI*92*time)*.32f*Mathf.Exp(-time*34)
                            +dry*.12f*Mathf.Exp(-time*80);break;
                    case SoundKind.Wheel:
                        value=lowNoise*.22f*Mathf.Exp(-time*2.4f)*Mathf.Min(1,time*16)
                            +Mathf.Sin(2*Mathf.PI*(280+35*Mathf.Sin(time*18))*time)*.045f*Mathf.Exp(-time*3.5f);break;
                    case SoundKind.ShotLoad:
                    case SoundKind.MeleeHit:
                        value=lowNoise*.3f*Mathf.Exp(-time*24)+Mathf.Sin(2*Mathf.PI*135*time)*.24f*Mathf.Exp(-time*32)
                            +dry*.08f*Mathf.Exp(-time*55);break;
                    case SoundKind.Parry:
                        value=dry*.19f*Mathf.Exp(-time*65)+Mathf.Sin(2*Mathf.PI*1700*time)*.13f*Mathf.Exp(-time*30)
                            +Mathf.Sin(2*Mathf.PI*2450*time)*.08f*Mathf.Exp(-time*42);break;
                    case SoundKind.Sponge:
                    case SoundKind.Tack:
                    case SoundKind.Flag:
                        value=lowNoise*.3f*Mathf.Exp(-time*4)*Mathf.Min(1,time*35)
                            *(.65f+.35f*Mathf.Sin(time*23))+dry*.035f*Mathf.Exp(-time*7);break;
                    case SoundKind.CrewStep:
                        value=lowNoise*.25f*Mathf.Exp(-time*22)+Mathf.Sin(2*Mathf.PI*78*time)*.2f*Mathf.Exp(-time*35);break;
                    case SoundKind.Swish:
                        value=lowNoise*.25f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(time/.35f))*Mathf.Exp(-time*5);break;
                    case SoundKind.Thunder:
                        value=lowNoise*.45f*Mathf.Min(1,time*2)*Mathf.Exp(-time*.5f)
                            +Mathf.Sin(2*Mathf.PI*39*time)*.14f*Mathf.Min(1,time*3)*Mathf.Exp(-time*.6f);break;
                    case SoundKind.Wind:
                        value=lowNoise*.2f*(.75f+.12f*Mathf.Sin(2*Mathf.PI*time/duration)
                            +.13f*Mathf.Sin(4*Mathf.PI*time/duration));break;
                    case SoundKind.Equipment:
                    {
                        // Quiet cloth/leather scrape with brief buckle and wooden-kit taps.
                        float rustle = Mathf.Exp(-time * 11f) * Mathf.Min(1f,time*65f);
                        float tap = Mathf.Max(0f,time-.08f);
                        value = lowNoise*.32f*rustle + dry*.04f*rustle;
                        if(time>=.08f) value += Mathf.Sin(2f*Mathf.PI*760f*tap)*.065f*Mathf.Exp(-tap*65f);
                        break;
                    }
                    case SoundKind.Cartridge:
                        value = dry*.22f*Mathf.Exp(-time*18f)*Mathf.Min(1f,time*100f)
                            +lowNoise*.12f*Mathf.Exp(-time*10f);
                        break;
                    case SoundKind.Ramrod:
                        value = lowNoise*.24f*Mathf.Exp(-time*15f)*Mathf.Min(1f,time*70f)
                            +Mathf.Sin(2f*Mathf.PI*1280f*time)*.07f*Mathf.Exp(-time*32f)
                            +Mathf.Sin(2f*Mathf.PI*190f*time)*.11f*Mathf.Exp(-time*40f);
                        break;
                    case SoundKind.Lock:
                        value = dry*.24f*Mathf.Exp(-time*80f)
                            +Mathf.Sin(2f*Mathf.PI*2200f*time)*.12f*Mathf.Exp(-time*95f);
                        break;
                    default: // Shell: low resonant blast with a short crack and rough, decaying tail.
                    {
                        float attack = Mathf.Min(1f, time * 55f);
                        float boom = Mathf.Exp(-time * 2.15f);
                        float rumble = Mathf.Exp(-time * 1.72f);
                        value = attack * (dry * (.20f * Mathf.Exp(-time * 7f) + .10f * rumble) + lowNoise * .14f * rumble)
                            + Mathf.Sin(2f * Mathf.PI * 48f * time) * .30f * boom
                            + Mathf.Sin(2f * Mathf.PI * 91f * time) * .17f * Mathf.Exp(-time * 2.6f)
                            + Mathf.Sin(2f * Mathf.PI * 176f * time) * .06f * Mathf.Exp(-time * 5f);
                        break;
                    }
                }
                // Headroom is deliberate: simultaneous distant reports should remain clean.
                if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
                samples[i] = Mathf.Clamp(value * .82f, -1f, 1f);
            }
            return samples;
        }

        static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return ((state & 0x00ffffffu) / 8388607.5f) - 1f;
        }

        static int Hash(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15;
                x *= 0x846ca68bu; x ^= x >> 16;
                return (int)x;
            }
        }

        void OnDestroy() { Cleanup(); }

        void Cleanup()
        {
            CleanupAtmosphericAudio();
            CleanupInfantryAudio();
            if (sources != null)
            {
                foreach (AudioSource source in sources)
                    if (source) Destroy(source.gameObject);
            }
            DestroyClips(musketClips); DestroyClips(cannonClips);
            DestroyClips(dirtClips); DestroyClips(shellClips);
            DestroyClips(volleyClips); DestroyClips(echoClips);
            volleyClips=echoClips=null; filters=null;
            sources = null; voices = null;
            musketClips = cannonClips = dirtClips = shellClips = null;
            world = null; artillery = null; battleCamera = null; isPaused = null;
            initialized = false;
        }

        static void DestroyClips(AudioClip[] clips)
        {
            if (clips == null) return;
            foreach (AudioClip clip in clips) if (clip) Destroy(clip);
        }
    }
}
