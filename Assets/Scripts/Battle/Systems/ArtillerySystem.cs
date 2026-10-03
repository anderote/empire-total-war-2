using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{

    public enum ArtilleryPhase { Ready, Firing, Recoil, Sponging, Loading, Ramming, RunUp, Aiming, Exhausted }

    public sealed class ArtilleryGun
    {
        public int Id;
        public float Health = 180f;
        public float LimeExposureRemaining;
        [System.NonSerialized] public float BattleGasExposure, BattleLimeExposure;
        public float GasMovementMultiplier=>1f-.4f*Mathf.Clamp01(BattleGasExposure);
        public readonly ArtilleryCrewState[] Crew = ArtillerySystem.CreateCrew();
        public int LivingCrew { get { int count=0; foreach(var member in Crew) if(member.Alive) count++; return count; } }
        public bool Destroyed => Health <= 0f;
        public bool Operational => !Destroyed && LivingCrew > 0;
        public bool Suppressed => LimeExposureRemaining > 0f;
        public int UnitId;
        public UnitProgression Progression=new UnitProgression();
        public UnitLoadout Loadout=new UnitLoadout();
        public EquipmentModifiers Kit;
        public int Equipment,Hits,Kills,TroopTier;
        public RegimentBaseStats BaseStats=>RegimentBaseStats.For(TroopTier,UnitRole.Line);
        public float MaxMorale=>BaseStats.Morale+Progression.MaxMorale-100;
        public float Fatigue,Morale=100,Cohesion=1;
        public float ReloadScale=>Mathf.Clamp(BaseStats.Reload*Progression.ReloadMultiplier*(1-Equipment*.02f)*(1+Kit.reload)*(1+Fatigue*.002f)*(Progression.Has(RegimentAbility.QuickHands)&&Ammunition<=(ArtillerySystem.StartingAmmunition+Kit.ammunition+Progression.ArtilleryAmmunitionBonus)*.25f?Mathf.Max(.2f,1-(.15f+.10f*Progression.AbilityRank(RegimentAbility.QuickHands))):1),.2f,1.5f);
        public float Precision=>(1f-.6f*Mathf.Clamp01(BattleLimeExposure))*BaseStats.Accuracy*Progression.AccuracyMultiplier*(1+Equipment*.04f)*(1+Kit.accuracy)*Cohesion*(Progression.Has(RegimentAbility.SteadyAim)?1.10f+.05f*Progression.AbilityRank(RegimentAbility.SteadyAim):1);
        public ArmySide Side;
        public Vector2 Position;
        public float Facing;
        public Vector2 Destination;
        public float DestinationFacing, MovePreparation, SetupRemaining;
        public bool Moving, MovementBlocked;
        public bool Relocating => Moving || SetupRemaining>0f;
        internal readonly List<Vector2> MoveRoute = new List<Vector2>();
        internal int MoveWaypoint;
        public float ReloadRemaining;
        public float LastShotTime = -100f;
        public int Ammunition = ArtillerySystem.StartingAmmunition;
        public ArtilleryAmmunition AmmunitionType;
        public bool ExplosiveShell { get => AmmunitionType >= ArtilleryAmmunition.Explosive;
            set => AmmunitionType = value ? ArtilleryAmmunition.Explosive : ArtilleryAmmunition.Roundshot; }
        public bool FireAtWill = true;
        public int TargetBattalion = -1;
        public int TargetBattery = -1;
        public bool HasGroundTarget;
        public Vector2 GroundTarget;
        public ArtilleryPhase Phase = ArtilleryPhase.Ready;
        public float PhaseElapsed;
        public float ReadyDelay;
        public int ShotsFired;
        public float Elevation = 5f;
        public float PhaseDuration(ArtilleryPhase phase)=>ArtillerySystem.Duration(phase)*ReloadScale*(4f/Mathf.Max(1,LivingCrew))/(phase==ArtilleryPhase.RunUp?Mathf.Max(.5f,1+Kit.speed):1);
        public float PhaseProgress => Mathf.Clamp01(PhaseElapsed / PhaseDuration(Phase));
        // Wheels stay back while the muzzle is serviced, then the crew runs the carriage forward.
        public float RecoilOffset => Phase == ArtilleryPhase.Firing ? .65f * PhaseProgress
            : Phase == ArtilleryPhase.Recoil || Phase == ArtilleryPhase.Sponging || Phase == ArtilleryPhase.Loading || Phase == ArtilleryPhase.Ramming ? .65f
            : Phase == ArtilleryPhase.RunUp ? .65f * (1f - Mathf.SmoothStep(0, 1, PhaseProgress)) : 0;
    }

    public sealed class CannonProjectile
    {
        public int GunId=-1;
        public bool Active;
        public bool FromCamera;
        public bool Shrapnel;
        public float FragmentRangeRemaining, FragmentStreakTime;
        public int Id;
        public ArmySide Side;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Age;
        public float SegmentAge;
        public ArtilleryAmmunition AmmunitionType;
        public bool ExplosiveShell { get => AmmunitionType >= ArtilleryAmmunition.Explosive;
            set => AmmunitionType = value ? ArtilleryAmmunition.Explosive : ArtilleryAmmunition.Roundshot; }
        public int BounceCount;
        public int HitCount;
        public int LastHitBattalion = -1;
        public readonly HashSet<int> StruckSoldiers = new HashSet<int>();
        public readonly HashSet<int> StruckArtillery = new HashSet<int>();
    }

    public sealed class ArtilleryImpact
    {
        public ArtilleryAmmunition AmmunitionType;
        public int Sequence;
        public Vector3 Position;
        public bool ExplosiveShell;
        public bool Detonates => ExplosiveShell && !ArtillerySystem.IsCloudShell(AmmunitionType);
        public float Time;
        public bool TerrainStrike;
        public Vector3 Direction;
        public float Speed;
    }

    /// <summary>Bounded artillery simulation: swept shot, local blasts, fire and obscuring clouds.</summary>
    public sealed partial class ArtillerySystem
    {
        public ArtilleryGun[] Guns { get; }
        public CannonProjectile[] Projectiles { get; }
        public ArtilleryImpact[] Impacts { get; }
        public int ImpactSequence { get; private set; }
        public sealed class Hazard
        {
            public int GunId=-1;
            public Vector3 Position;
            // Incoming trajectory retained for the incendiary splash presentation.
            public Vector3 Direction;
            public ArtilleryAmmunition Type;
            public ArmySide Side;
            // Keep countdowns precise across thousands of small simulation steps.
            public double RemainingSeconds, UntilPulseSeconds;
            public float Remaining {get=>(float)RemainingSeconds;set=>RemainingSeconds=value;}
            public float UntilPulse {get=>(float)UntilPulseSeconds;set=>UntilPulseSeconds=value;}
        }
        public readonly Hazard[] Hazards = new Hazard[128];
        // Presentation notification only; burn damage remains in the existing hazard pulses.
        public event System.Action<int,float> SoldierIgnited;
        int hazardCursor;
        readonly BattleWorld world;
        readonly System.Random random;
        int projectileSequence;
        struct SoldierIntersection : System.IComparable<SoldierIntersection>
        {
            public int Soldier;
            public bool Horse, Crew, Carriage;
            public int Gun, Role;
            public float Fraction, DamageScale;
            public int CompareTo(SoldierIntersection other)
            {
                int order = Fraction.CompareTo(other.Fraction);
                return order != 0 ? order : Soldier.CompareTo(other.Soldier);
            }
        }
        readonly List<SoldierIntersection> soldierIntersections = new List<SoldierIntersection>(256);
        readonly int[] roundshotLosses;
        readonly Vector4[] shotBounds;
        bool steppingProjectiles,shotBoundsReady;
        const float Gravity = 9.81f;
        public const float Range = 900f;
        public const int StartingAmmunition = 250;
        public const int CannonsPerUnit = 4;
        public const int CanisterPellets = 96;
        public const int ShellFragments = 24;
        public const float FragmentMinRange = 8f, FragmentRange = 18f, FragmentLifetime = .35f;
        static int[] BatterySizes(int count,int[] specified)
        {
            if(specified!=null)
            {
                int total=0;foreach(int size in specified){if(size<1||size>12)throw new System.ArgumentException("Battery size must be between one and twelve guns.");total+=size;}
                if(total!=count)throw new System.ArgumentException("Battery sizes do not match army gun count.");return specified;
            }
            var sizes=new int[Mathf.CeilToInt(count/(float)CannonsPerUnit)];
            for(int i=0;i<sizes.Length;i++)sizes[i]=Mathf.Min(CannonsPerUnit,count-i*CannonsPerUnit);return sizes;
        }

        public ArtillerySystem(BattleWorld battleWorld)
        {
            world = battleWorld ?? throw new System.ArgumentNullException(nameof(battleWorld));
            roundshotLosses = new int[world.Battalions.Length];
            shotBounds = new Vector4[world.Battalions.Length];
            for(int i=0;i<Hazards.Length;i++)Hazards[i]=new Hazard();
            random = new System.Random(world.Config.Seed ^ 31991);
            if (world.Config.FrenchArtillery < 0 || world.Config.BritishArtillery < 0 || world.Config.FrenchArtillery > 128 || world.Config.BritishArtillery > 128)
                throw new System.ArgumentOutOfRangeException(nameof(battleWorld), "Artillery counts must be between zero and 128 per army.");
            Guns = new ArtilleryGun[world.Config.FrenchArtillery + world.Config.BritishArtillery];
            var frenchSizes=BatterySizes(world.Config.FrenchArtillery,world.Config.FrenchBatterySizes);
            var britishSizes=BatterySizes(world.Config.BritishArtillery,world.Config.BritishBatterySizes);
            int unitCount=frenchSizes.Length+britishSizes.Length;
            var profiles=world.Config.ArtilleryProgression;var kits=world.Config.ArtilleryLoadouts;var quality=world.Config.ArtilleryEquipment;var tiers=world.Config.ArtilleryTiers;
            if(tiers?.Length==0)tiers=null;
            if(tiers!=null&&(tiers.Length!=unitCount||System.Array.Exists(tiers,t=>t!=0&&t!=11&&t!=12)))throw new System.ArgumentException("Invalid artillery tier deployment.");
            if(profiles?.Length==0)profiles=null;if(kits?.Length==0)kits=null;if(quality?.Length==0)quality=null;
            if(profiles!=null&&(profiles.Length!=unitCount||System.Array.Exists(profiles,p=>p==null||!p.Valid)))throw new System.ArgumentException("Invalid artillery progression deployment.");
            if(kits!=null&&(kits.Length!=unitCount||System.Array.Exists(kits,k=>k==null||!k.Valid(EquipmentUsers.Artillery))))throw new System.ArgumentException("Invalid artillery loadout deployment.");
            if(quality!=null&&(quality.Length!=unitCount||System.Array.Exists(quality,q=>q<0||q>UnitProgression.MaxQuality)))throw new System.ArgumentException("Invalid artillery quality deployment.");
            // A simultaneous battery volley must not silently lose most of its pellets.
            Projectiles = new CannonProjectile[Mathf.Max(512, Guns.Length * (CanisterPellets + ShellFragments + 8))];
            Impacts = new ArtilleryImpact[Mathf.Max(64, Guns.Length * 4)];
            for (int i = 0; i < Projectiles.Length; i++) Projectiles[i] = new CannonProjectile();
            for (int i = 0; i < Impacts.Length; i++) Impacts[i] = new ArtilleryImpact();
            float flank = Mathf.Min(850f, world.Config.FieldHalfSize * .65f);
            float rear = Mathf.Min(world.Config.InitialSeparation * .5f + 80f, world.Config.FieldHalfSize * .8f);
            for (int i = 0; i < Guns.Length; i++)
            {
                bool french = i < world.Config.FrenchArtillery;
                int count = french ? world.Config.FrenchArtillery : world.Config.BritishArtillery;
                int number = french ? i : i - world.Config.FrenchArtillery;
                var sizes=french?frenchSizes:britishSizes;
                int batteries=sizes.Length,battery=0,slot=number;
                while(slot>=sizes[battery]){slot-=sizes[battery];battery++;}
                int batteryCount=sizes[battery];
                float batteryX = batteries == 1 ? 0 : Mathf.Lerp(-flank, flank, battery / (float)(batteries - 1));
                float x = batteryX + (slot - (batteryCount - 1) * .5f) * 12f;
                // Large batteries deploy behind their own infantry rather than inside its rear ranks.
                int armyCount = french ? world.Config.FrenchBattalionCount : world.Config.BritishBattalionCount;
                int columns = Mathf.Clamp(Mathf.FloorToInt((world.Config.FieldHalfSize * 2 - 80) / (FormationMath.FileCount(world.Config.SoldiersPerBattalion, FormationKind.Line) * 1.1f + 20)), 1, Mathf.Min(20, armyCount));
                float gunRear = count <= 4 ? rear : Mathf.Min(world.Config.InitialSeparation * .5f + (Mathf.CeilToInt(armyCount / (float)columns) - 1) * 65 + 80, world.Config.FieldHalfSize * .8f);
                float delay = count <= 4 ? 3 + number * 1.7f : 3 + battery * .65f + slot * .18f;
                x = Mathf.Clamp(x, -world.Config.FieldHalfSize + 5f, world.Config.FieldHalfSize - 5f);
                Guns[i] = new ArtilleryGun
                {
                    Id = i, UnitId = battery + (french ? 0 : frenchSizes.Length),
                    Side = french ? ArmySide.French : ArmySide.British,
                    Position = new Vector2(x, french ? -gunRear : gunRear),
                    Facing = french ? 0f : Mathf.PI,
                    ExplosiveShell = !world.Config.RegimentalDeployment && number % 2 == 1,
                    // Stagger the initial battery salvo without creating an instantaneous opening wipe.
                    ReadyDelay = delay,
                    ReloadRemaining = delay
                };
                var gun=Guns[i];gun.TroopTier=tiers!=null?tiers[gun.UnitId]:0;gun.Progression=profiles!=null?profiles[gun.UnitId].Copy():new UnitProgression();
                gun.Loadout=kits!=null?kits[gun.UnitId].Copy():new UnitLoadout();gun.Kit=gun.Loadout.Modifiers;
                gun.Equipment=quality!=null?quality[gun.UnitId]:0;gun.Ammunition+=gun.Kit.ammunition+gun.Progression.ArtilleryAmmunitionBonus;gun.Morale=gun.MaxMorale+gun.Kit.moraleReserve;
            }
        }

        public void Step(float dt)
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Artillery);
            if (!world.CanSimulate || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            foreach (var gun in Guns)
            {
                gun.LimeExposureRemaining=Mathf.Max(0,gun.LimeExposureRemaining-dt);
                StepGunMovement(gun,dt);
                StepCrewPositions(gun,dt);
            }
            StepHazards(dt);
            targetMassCache.Clear();aimingBattery=true;
            try
            {
                foreach (var gun in Guns)
                {
                    if (!gun.Operational) continue;
                    StepCrewCondition(gun,dt);
                    if (gun.Suppressed || gun.Relocating) continue;
                    AdvanceCycle(gun, dt);
                    if (gun.Ammunition <= 0 || !gun.FireAtWill || gun.Morale<25 || (gun.Phase != ArtilleryPhase.Ready && gun.Phase != ArtilleryPhase.Aiming) || gun.ReadyDelay > 0f) continue;
                    if (gun.TargetBattalion >= 0 && world.Battalions[gun.TargetBattalion].AliveCount <= 0) gun.TargetBattalion = -1;
                    if (!TryAimPoint(gun, out Vector2 target)) continue;
                    Vector2 aim = target - gun.Position;
                    gun.Facing = FormationMath.TurnTowards(gun.Facing, FormationMath.FacingOf(aim), dt * .45f);
                    if (gun.Phase == ArtilleryPhase.Ready && Vector2.Dot(FormationMath.Forward(gun.Facing), aim.normalized) > .9995f) Fire(gun, target);
                }
            }
            finally{aimingBattery=false;}
            steppingProjectiles=true;shotBoundsReady=false;
            try
            {
            foreach (var shot in Projectiles)
            {
                if (!shot.Active) continue;
                float stepDt=shot.Shrapnel?Mathf.Min(dt,FragmentLifetime-shot.Age):dt;
                if(stepDt<=0||shot.Shrapnel&&shot.FragmentRangeRemaining<=.0001f){shot.Active=false;continue;}
                Vector3 previous=shot.Position;
                Vector3 next=previous+shot.Velocity*stepDt+Vector3.down*(.5f*Gravity*stepDt*stepDt);
                if(shot.Shrapnel)
                {
                    float distance=Vector3.Distance(previous,next);
                    float fraction=Mathf.Min(1f,shot.FragmentRangeRemaining/Mathf.Max(.0001f,distance));
                    next=Vector3.Lerp(previous,next,fraction);stepDt*=fraction;
                    shot.FragmentRangeRemaining=Mathf.Max(0,shot.FragmentRangeRemaining-distance*fraction);
                }
                shot.Age+=stepDt;shot.SegmentAge+=stepDt;
                if(shot.Age>(shot.AmmunitionType==ArtilleryAmmunition.Canister?1.4f:18f)){shot.Active=false;continue;}
                shot.Velocity+=Vector3.down*(Gravity*stepDt);
                // Sample the swept segment against the shared surface, including rising terrain.
                bool ground = ClipToTerrain(previous, next, out Vector3 intersection);
                if (ground) next = intersection;
                if (shot.ExplosiveShell && HitArtilleryShell(shot,previous,next)) continue;
                if (!shot.ExplosiveShell) TestRoundshot(shot, previous, next, stepDt);
                if (!shot.Active) continue;
                shot.Position = next;
                if (ground)
                {
                    Vector3 contactNormal = world.Terrain.NormalAt(new Vector2(next.x, next.z));
                    bool ricochet=ShouldRicochet(shot,contactNormal);
                    // A skipped shell retains its filling. Only its eventual stop detonates it.
                    EmitImpact(next, shot.ExplosiveShell&&!ricochet, true, shot.Velocity, shot.AmmunitionType);
                    if(ricochet)
                    {
                        shot.BounceCount++;
                        shot.SegmentAge=0f;
                        float inward=Vector3.Dot(shot.Velocity,contactNormal);
                        Vector3 tangent=shot.Velocity-contactNormal*inward;
                        float incidence=Mathf.Clamp01(-inward/Mathf.Max(1f,shot.Velocity.magnitude));
                        shot.Velocity=tangent*Mathf.Lerp(.7f,.42f,incidence)
                            +contactNormal*Mathf.Clamp(-inward*.28f,1.5f,7f);
                        shot.Position=next+contactNormal*.14f;
                    }
                    else
                    {
                        if(shot.ExplosiveShell)Explode(shot);
                        shot.Active=false;
                    }
                }
                if(shot.Shrapnel&&(shot.FragmentRangeRemaining<=.0001f||shot.Age>=FragmentLifetime))shot.Active=false;
                if (!shot.FromCamera && (Mathf.Abs(next.x) > world.Config.FieldHalfSize + 20f || Mathf.Abs(next.z) > world.Config.FieldHalfSize + 20f))
                    shot.Active = false;
            }
            }
            finally { steppingProjectiles=false; }
            ReleaseShrapnel();
        }

        public const float ImpactRicochetChance=.075f;
        bool ShouldRicochet(CannonProjectile shot,Vector3 normal)
        {
            // Fragments are already spent casing. Whole rounds/pellets may skip once,
            // provided the impact has enough tangential energy to leave the surface.
            if(shot.Shrapnel||shot.BounceCount>0||shot.Velocity.sqrMagnitude<625f)return false;
            float incidence=Vector3.Dot(-shot.Velocity.normalized,normal);
            if(incidence<0||incidence>.85f)return false;
            uint hash=unchecked((uint)shot.Id*747796405u+(uint)world.Config.Seed*2891336453u+277803737u);
            hash=((hash>>((int)(hash>>28)+4))^hash)*277803737u;
            hash=(hash>>22)^hash;
            return (hash&0xffffffu)/16777216f<ImpactRicochetChance;
        }

        public bool FireCameraRoundshot(Vector3 origin, Vector3 target)
        {
            if (!world.CanSimulate || !Finite(origin) || !Finite(target)) return false;
            Vector3 delta = target - origin;
            if (delta.sqrMagnitude < .25f || origin.y <= world.Terrain.HeightAt(new Vector2(origin.x, origin.z))) return false;
            CannonProjectile shot = null;
            foreach (var candidate in Projectiles) if (!candidate.Active) { shot = candidate; break; }
            if (shot == null) return false;
            float flight = Mathf.Clamp(delta.magnitude / 220f, .1f, 14f);
            shot.Shrapnel = false;
            shot.Id = ++projectileSequence; shot.Side = ArmySide.French;
            shot.GunId=-1;
            shot.FromCamera = true;
            shot.Position = origin; shot.Velocity = delta / flight + Vector3.up * (.5f * Gravity * flight);
            shot.Age = shot.SegmentAge = 0f; shot.BounceCount = shot.HitCount = 0; shot.LastHitBattalion = -1;
            shot.AmmunitionType = ArtilleryAmmunition.Roundshot; shot.StruckSoldiers.Clear(); shot.StruckArtillery.Clear(); shot.Active = true;
            return true;
        }
        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        public static float Duration(ArtilleryPhase phase)
        {
            switch (phase)
            {
                case ArtilleryPhase.Firing: return .35f;
                case ArtilleryPhase.Recoil: return 1.45f;
                case ArtilleryPhase.Sponging: return 4f;
                case ArtilleryPhase.Loading: return 3f;
                case ArtilleryPhase.Ramming: return 4f;
                case ArtilleryPhase.RunUp: return 4f;
                case ArtilleryPhase.Aiming: return 3.2f;
                default: return 1f;
            }
        }

        void AdvanceCycle(ArtilleryGun gun, float dt)
        {
            gun.ReadyDelay = Mathf.Max(0, gun.ReadyDelay - dt);
            if (gun.Phase != ArtilleryPhase.Ready && gun.Phase != ArtilleryPhase.Exhausted)
            {
                gun.PhaseElapsed += dt;
                while (gun.PhaseElapsed >= gun.PhaseDuration(gun.Phase))
                {
                    gun.PhaseElapsed -= gun.PhaseDuration(gun.Phase);
                    gun.Phase = gun.Phase == ArtilleryPhase.Aiming ? ArtilleryPhase.Ready : (ArtilleryPhase)((int)gun.Phase + 1);
                    if (gun.Phase == ArtilleryPhase.Ready) { gun.PhaseElapsed = 0; break; }
                }
            }
            if (gun.Phase == ArtilleryPhase.Ready && gun.Ammunition <= 0) gun.Phase = ArtilleryPhase.Exhausted;
            gun.ReloadRemaining = gun.ReadyDelay;
            if (gun.Phase != ArtilleryPhase.Ready && gun.Phase != ArtilleryPhase.Exhausted)
            {
                gun.ReloadRemaining = gun.PhaseDuration(gun.Phase) - gun.PhaseElapsed;
                for (int phase = (int)gun.Phase + 1; phase <= (int)ArtilleryPhase.Aiming; phase++)
                    gun.ReloadRemaining += gun.PhaseDuration((ArtilleryPhase)phase);
            }
        }

        public static float EffectiveRange(ArtilleryGun gun) => (gun.AmmunitionType == ArtilleryAmmunition.Canister ? 220f : Range)*(1+gun.Kit.range);
        void StepCrewCondition(ArtilleryGun gun,float dt)
        {
            bool pressured=false;
            foreach(var unit in world.Battalions)
            {
                if(unit.AliveCount<=0||(unit.Position-gun.Position).sqrMagnitude>120*120)continue;
                if(unit.Side!=gun.Side||unit.Routing){pressured=true;break;}
            }
            float resolve=gun.BaseStats.Resolve*gun.Progression.ResolveMultiplier*(1+gun.Kit.resolve);
            gun.Morale=Mathf.Clamp(gun.Morale+(pressured?-4/resolve:.5f*resolve)*dt,0,gun.MaxMorale+gun.Kit.moraleReserve);
            gun.Cohesion=Mathf.Clamp(gun.Cohesion+(pressured?-.003f:.012f*(1+gun.Kit.cohesion))*dt,.5f,1);
            // Equipment controls crew strain; servicing speed changes through ReloadScale.
            float strain=Mathf.Clamp(gun.Progression.FatigueMultiplier*(1+gun.Kit.fatigue+(world.Config.Map?.Aridity??0)*gun.Kit.heatFatigue+(world.Config.Map?.Snow??0)*gun.Kit.coldFatigue),.15f,1.6f);
            gun.Fatigue=Mathf.Clamp(gun.Fatigue+(world.Time-gun.LastShotTime<5?.12f*strain:-.08f/strain)*dt,0,100);
        }
        void CreditHit(int gunId,ArmySide target,bool killed)
        {
            if(gunId<0||gunId>=Guns.Length||Guns[gunId].Side==target)return;
            Guns[gunId].Hits++;if(killed)Guns[gunId].Kills++;
        }
        public bool CanUseAmmunition(ArtilleryGun gun,ArtilleryAmmunition type)=>gun!=null&&System.Enum.IsDefined(typeof(ArtilleryAmmunition),type)&&(!world.Config.RegimentalDeployment||gun.Progression.CanUseArtilleryAmmunition(type));
        public void SetAmmunition(int gunId, ArtilleryAmmunition type)
        {
            if(gunId<0||gunId>=Guns.Length||!System.Enum.IsDefined(typeof(ArtilleryAmmunition),type))return;
            var gun=Guns[gunId];if(!gun.Operational||!CanUseAmmunition(gun,type)||gun.AmmunitionType==type)return;
            gun.AmmunitionType=type;
            // Switching a loaded gun requires servicing it; it cannot bypass the reload cycle.
            if(gun.Phase==ArtilleryPhase.Ready){gun.Phase=ArtilleryPhase.Sponging;gun.PhaseElapsed=0;gun.ReloadRemaining=18.2f;}
        }
        public bool OrderTarget(int gunId, int battalionId)
        {
            if (gunId < 0 || gunId >= Guns.Length || battalionId < 0 || battalionId >= world.Battalions.Length) return false;
            var gun = Guns[gunId]; var target = world.Battalions[battalionId];
            if (!gun.Operational || target.Side == gun.Side || target.AliveCount <= 0) return false;
            StopGunMovement(gun);
            gun.TargetBattalion = battalionId; gun.TargetBattery=-1; gun.HasGroundTarget = false; gun.FireAtWill = true;
            return true;
        }
        public bool OrderGround(int gunId, Vector2 point)
        {
            if (gunId < 0 || gunId >= Guns.Length || float.IsNaN(point.x) || float.IsNaN(point.y)
                || float.IsInfinity(point.x) || float.IsInfinity(point.y)) return false;
            var gun = Guns[gunId]; if(!gun.Operational)return false;
            int battery=BatteryAt(gun.Side,point);
            if(battery>=0)return OrderBattery(gunId,battery);
            StopGunMovement(gun);
            gun.TargetBattalion = -1; gun.TargetBattery=-1; gun.HasGroundTarget = true;
            float half = world.Config.FieldHalfSize;
            gun.GroundTarget = new Vector2(Mathf.Clamp(point.x,-half,half),Mathf.Clamp(point.y,-half,half));
            gun.FireAtWill = true; return true;
        }
        public void ClearTarget(int gunId, bool fireAtWill)
        {
            if (gunId < 0 || gunId >= Guns.Length) return;
            var gun = Guns[gunId]; StopGunMovement(gun); gun.TargetBattalion = -1; gun.TargetBattery=-1; gun.HasGroundTarget = false; gun.FireAtWill = fireAtWill&&gun.Operational;
        }
        public bool TryAimPoint(ArtilleryGun gun, out Vector2 point)
        {
            point = gun.GroundTarget;
            if(!gun.Operational)return false;
            if(gun.TargetBattery>=0)
            {
                if(!BatteryAimPoint(gun,gun.TargetBattery,out point))return false;
            }
            else if (!gun.HasGroundTarget)
            {
                BattalionState target = gun.TargetBattalion >= 0 ? world.Battalions[gun.TargetBattalion] : FindTarget(gun);
                if(gun.TargetBattalion<0)
                {
                    int battery=NearestEnemyBattery(gun,target==null?EffectiveRange(gun):(target.Position-gun.Position).magnitude);
                    if(battery>=0)return BatteryAimPoint(gun,battery,out point);
                }
                if (target == null || target.AliveCount <= 0) return false;
                point = RegimentAimPoint(gun,target);
            }
            float distance = Vector2.Distance(gun.Position, point);
            return distance >= 8f && distance <= EffectiveRange(gun);
        }

        BattalionState FindTarget(ArtilleryGun gun)
        {
            BattalionState target = null;
            float best = EffectiveRange(gun) * EffectiveRange(gun);
            foreach (var battalion in world.Battalions)
            {
                if (battalion.Side == gun.Side || battalion.AliveCount <= 0) continue;
                float distance = (battalion.Position - gun.Position).sqrMagnitude;
                if (distance < best) { best = distance; target = battalion; }
            }
            return target;
        }

        public Vector3 GunCenter(ArtilleryGun gun)
        {
            Vector2 point = gun.Position - FormationMath.Forward(gun.Facing) * gun.RecoilOffset;
            return world.Terrain.SurfacePoint(point);
        }

        public Vector3 Muzzle(ArtilleryGun gun)
        {
            Quaternion facing = Quaternion.Euler(0, gun.Facing * Mathf.Rad2Deg, 0);
            Vector3 pivot = new Vector3(0, .9f, -.23f);
            Vector3 tip = new Vector3(0, 0, gun.ExplosiveShell ? .842f : 1.08f);
            return GunCenter(gun) + facing * (pivot + Quaternion.Euler(-gun.Elevation, 0, 0) * tip);
        }

        struct TargetMass
        {
            public Vector2 Center;public float WidthSpread,DepthSpread;
        }
        readonly Dictionary<int,TargetMass> targetMassCache=new Dictionary<int,TargetMass>();
        bool aimingBattery;
        TargetMass MeasureTarget(BattalionState target)
        {
            if(aimingBattery&&targetMassCache.TryGetValue(target.Id,out var cached))return cached;
            Vector2 right=FormationMath.Right(target.Facing),forward=FormationMath.Forward(target.Facing);
            // A local origin avoids cancellation in the variance on large battlefields.
            Vector2 sum=Vector2.zero;float widthSquares=0,depthSquares=0;int count=0;
            for(int soldier=target.SoldierStart;soldier<target.SoldierStart+target.SoldierCount;soldier++)
            {
                if(!world.Soldiers.Alive[soldier])continue;
                Vector2 offset=world.Soldiers.Positions[soldier]-target.Position;
                float width=Vector2.Dot(offset,right),depth=Vector2.Dot(offset,forward);
                sum+=offset;widthSquares+=width*width;depthSquares+=depth*depth;count++;
            }
            Vector2 mean=count>0?sum/count:Vector2.zero;
            float meanWidth=Vector2.Dot(mean,right),meanDepth=Vector2.Dot(mean,forward);
            var result=new TargetMass{Center=target.Position+mean,
                WidthSpread=count>0?Mathf.Sqrt(Mathf.Max(0,widthSquares/count-meanWidth*meanWidth)):0,
                DepthSpread=count>0?Mathf.Sqrt(Mathf.Max(0,depthSquares/count-meanDepth*meanDepth)):0};
            if(aimingBattery)targetMassCache[target.Id]=result;
            return result;
        }
        Vector2 RegimentAimPoint(ArtilleryGun gun,BattalionState target)
        {
            var mass=MeasureTarget(target);int slot=0,count=0;
            foreach(var member in Guns)if(member.Side==gun.Side&&member.UnitId==gun.UnitId)
            {if(member.Id<gun.Id)slot++;count++;}
            float lane=count>1?2f*slot/(count-1)-1f:0f;
            // Reverse lanes on successive salvos so a gun doesn't repeatedly work one strip.
            if((gun.ShotsFired&1)!=0)lane=-lane;
            return mass.Center+FormationMath.Right(target.Facing)*(mass.WidthSpread*1.25f*lane)
                +FormationMath.Forward(target.Facing)*(mass.DepthSpread*.4f*lane);
        }
        public static float ShotDispersionRadius(ArtilleryGun gun,float distance)
        {
            float trained=distance*WeaponBallistics.Dispersion(distance,EffectiveRange(gun),.006f,gun.ExplosiveShell?.045f:.03f)
                /Mathf.Max(.1f,gun.Precision);
            // Even expert crews have range estimation, powder and barrel variation.
            return Mathf.Max(1f+distance*.003f,trained);
        }

        bool ClipToTerrain(Vector3 from, Vector3 to, out Vector3 point)
        {
            int samples = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to) / 4f), 2, 16);
            Vector3 previous = from;
            for (int i = 1; i <= samples; i++)
            {
                Vector3 current = Vector3.Lerp(from, to, i / (float)samples);
                if (current.y <= world.Terrain.HeightAt(new Vector2(current.x, current.z)))
                {
                    Vector3 low = previous, high = current;
                    for (int refine = 0; refine < 6; refine++)
                    {
                        Vector3 mid = Vector3.Lerp(low, high, .5f);
                        if (mid.y <= world.Terrain.HeightAt(new Vector2(mid.x, mid.z))) high = mid;
                        else low = mid;
                    }
                    point = high;
                    point.y = world.Terrain.HeightAt(new Vector2(point.x, point.z));
                    return true;
                }
                previous = current;
            }
            point = to;
            return false;
        }

        // Explicit inspection API: normal battle firing still follows aim/reload/ammunition phases.
        public bool FireReviewShot(int gunId,int targetId,ArtilleryAmmunition type)
        {
            if(!world.Config.AssetReview||gunId<0||gunId>=Guns.Length||targetId<0||targetId>=world.Battalions.Length)return false;
            var gun=Guns[gunId];int before=gun.ShotsFired;
            gun.AmmunitionType=type;gun.Ammunition=StartingAmmunition;gun.ReadyDelay=0;
            gun.FireAtWill=false;gun.PhaseElapsed=gun.ReloadRemaining=0;gun.Phase=ArtilleryPhase.Ready;
            Fire(gun,RegimentAimPoint(gun,world.Battalions[targetId]));
            return gun.ShotsFired>before;
        }
        bool FriendlyArtilleryRisk(ArtilleryGun gun,Vector2 target)
        {
            Vector2 path=target-gun.Position;float length=path.magnitude;
            if(length<1f)return false;
            Vector2 direction=path/length;
            float spread=ShotDispersionRadius(gun,length);
            float blast=gun.ExplosiveShell?ExplosionRadius(gun.AmmunitionType)+spread:0f;
            foreach(var ally in world.Battalions)
            {
                if(ally.Side!=gun.Side||ally.AliveCount<=0)continue;
                for(int i=ally.SoldierStart;i<ally.SoldierStart+ally.SoldierCount;i++)
                {
                    if(!world.Soldiers.Alive[i])continue;
                    Vector2 at=world.Soldiers.Positions[i];
                    if(blast>0&&(at-target).sqrMagnitude<blast*blast)return true;
                    if(gun.ExplosiveShell)continue;
                    Vector2 delta=at-gun.Position;float along=Vector2.Dot(delta,direction);
                    float lane=.8f+spread*Mathf.Clamp01(along/length)+(gun.AmmunitionType==ArtilleryAmmunition.Canister?along*.12f:0f);
                    if(along>2f&&along<length+spread&&Mathf.Abs(delta.x*direction.y-delta.y*direction.x)<lane)return true;
                }
            }
            return false;
        }

        void Fire(ArtilleryGun gun, Vector2 target)
        {
            if(!gun.Operational || gun.Suppressed || gun.Relocating)return;
            if(!world.Config.AssetReview&&gun.TargetBattalion<0&&gun.TargetBattery<0&&!gun.HasGroundTarget&&FriendlyArtilleryRisk(gun,target))return;
            CannonProjectile shot = null;
            foreach (var candidate in Projectiles) if (!candidate.Active) { shot = candidate; break; }
            if (shot == null) return;
            float distance = Vector2.Distance(gun.Position, target);
            float flight = Mathf.Clamp(distance / (gun.ExplosiveShell ? 120f : gun.AmmunitionType == ArtilleryAmmunition.Canister ? 180f : 155f), gun.AmmunitionType == ArtilleryAmmunition.Canister ? .1f : 1.2f, 7.5f);
            flight *= Mathf.Lerp(.95f, 1.05f, (float)random.NextDouble());
            Vector2 forward = (target - gun.Position).normalized;
            Vector2 right = new Vector2(forward.y, -forward.x);
            float error = ShotDispersionRadius(gun,distance);
            Vector2 aim = target + right * ((float)(random.NextDouble() - random.NextDouble()) * error)
                + forward * ((float)(random.NextDouble() - random.NextDouble()) * error * 1.5f);
            Vector3 origin = Muzzle(gun);
            Vector3 end = world.Terrain.SurfacePoint(aim, gun.ExplosiveShell ? 0f : gun.AmmunitionType == ArtilleryAmmunition.Canister ? 1f : .5f);
            Vector3 velocity = (end - origin) / flight + Vector3.up * (.5f * Gravity * flight);
            gun.Elevation = Mathf.Atan2(velocity.y, new Vector2(velocity.x, velocity.z).magnitude) * Mathf.Rad2Deg;
            origin = Muzzle(gun);
            shot.Shrapnel = false;
            shot.Id = ++projectileSequence;
            shot.FromCamera = false;
            shot.GunId=gun.Id;
            shot.Side = gun.Side;
            shot.Position = origin;
            shot.Velocity = (end - origin) / flight + Vector3.up * (.5f * Gravity * flight);
            shot.Age = 0f;
            shot.SegmentAge = 0f;
            shot.AmmunitionType = gun.AmmunitionType;
            shot.BounceCount = 0;
            shot.HitCount = 0;
            shot.LastHitBattalion = -1;
            shot.StruckSoldiers.Clear(); shot.StruckArtillery.Clear(); shot.Active = true;
            if(gun.AmmunitionType==ArtilleryAmmunition.Canister)
            {
                int slot=0;
                for(int pellet=0;pellet<CanisterPellets-1;pellet++)
                {
                    while(slot<Projectiles.Length&&Projectiles[slot].Active)slot++;
                    if(slot==Projectiles.Length)break;
                    var extra=Projectiles[slot++];
                    // Stratify the horizontal fan so every shell covers the frontage. A shallow
                    // vertical cone keeps close-range shot at horse/torso height instead of sky/soil.
                    float sideways=((pellet+(float)random.NextDouble())/(CanisterPellets-1)*2f-1f)*.12f;
                    float vertical=(float)(random.NextDouble()-random.NextDouble())*.009f;
                    extra.Shrapnel=false;extra.Id=++projectileSequence;extra.Side=gun.Side;extra.Position=origin;extra.FromCamera=false;
                    extra.GunId=gun.Id;
                    extra.Velocity=shot.Velocity+new Vector3(right.x,0,right.y)*(sideways*180f)+Vector3.up*(vertical*180f);
                    extra.Age=extra.SegmentAge=0;extra.BounceCount=extra.HitCount=0;extra.LastHitBattalion=-1;
                    extra.AmmunitionType=ArtilleryAmmunition.Canister;extra.StruckSoldiers.Clear();extra.StruckArtillery.Clear();extra.Active=true;
                }
            }
            gun.Ammunition--;
            gun.LastShotTime = world.Time;
            gun.ShotsFired++;
            gun.Phase = ArtilleryPhase.Firing;
            gun.PhaseElapsed = 0;
            gun.ReloadRemaining = 20f;
        }

        void TestRoundshot(CannonProjectile shot, Vector3 from, Vector3 to, float dt)
        {
            if(steppingProjectiles&&!shotBoundsReady)
            {
                // Build from actual living positions once per tick. Formation bounds can lag
                // behind stragglers, and scanning the whole army for every pellet is expensive.
                foreach(var unit in world.Battalions)
                {
                    var b=new Vector4(float.PositiveInfinity,float.PositiveInfinity,float.NegativeInfinity,float.NegativeInfinity);
                    for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)if(world.Soldiers.Alive[i])
                    {
                        var p=world.Soldiers.Positions[i];b.x=Mathf.Min(b.x,p.x);b.y=Mathf.Min(b.y,p.y);b.z=Mathf.Max(b.z,p.x);b.w=Mathf.Max(b.w,p.y);
                    }
                    shotBounds[unit.Id]=b;
                }
                shotBoundsReady=true;
            }
            soldierIntersections.Clear();
            System.Array.Clear(roundshotLosses, 0, roundshotLosses.Length);
            // Use current soldier positions: formation bounds can lag behind men who are reforming.
            float radius = shot.AmmunitionType == ArtilleryAmmunition.Canister ? .24f : .4f; // Body plus projectile radius.
            float minX = Mathf.Min(from.x, to.x) - radius, maxX = Mathf.Max(from.x, to.x) + radius;
            float minZ = Mathf.Min(from.z, to.z) - radius, maxZ = Mathf.Max(from.z, to.z) + radius;
            foreach (var battalion in world.Battalions)
            {
                if (battalion.AliveCount <= 0) continue;
                bool mounted=battalion.Kind==UnitKind.Cavalry;
                float bodyRadius=mounted?radius+.5f:radius;
                float horseMargin=mounted?.5f:0f;
                if(steppingProjectiles)
                {
                    var b=shotBounds[battalion.Id];
                    if(b.z<minX-horseMargin||b.x>maxX+horseMargin||b.w<minZ-horseMargin||b.y>maxZ+horseMargin)continue;
                }
                for (int soldier = battalion.SoldierStart; soldier < battalion.SoldierStart + battalion.SoldierCount; soldier++)
                {
                    if (!world.Soldiers.Alive[soldier] || shot.StruckSoldiers.Contains(soldier)) continue;
                    Vector2 position = world.Soldiers.Positions[soldier];
                    if (position.x < minX-horseMargin || position.x > maxX+horseMargin || position.y < minZ-horseMargin || position.y > maxZ+horseMargin) continue;
                    float height = mounted?2.8f:world.Soldiers.IsKnockedDown(soldier, world.Time) ? .45f : RifleFireTiming.IsKneeling(world, battalion, soldier, world.Time) ? 1.15f : 1.8f;
                    Vector3 feet = world.SoldierPosition(soldier,world.Time);
                    if (SweptSoldier(from, to, feet, height, bodyRadius, out float fraction))
                    {
                        Vector2 segment = new Vector2(to.x-from.x, to.z-from.z);
                        Vector2 relative = position - new Vector2(from.x,from.z);
                        float nearest = segment.sqrMagnitude > .000001f ? Vector2.Dot(relative,segment)/segment.sqrMagnitude : 0f;
                        float missDistance = (relative-segment*nearest).magnitude;
                        float contact = Mathf.Lerp(.15f,1f,Mathf.InverseLerp(bodyRadius,mounted?.6f:.22f,missDistance));
                        soldierIntersections.Add(new SoldierIntersection { Soldier = soldier, Fraction = fraction,
                            DamageScale = shot.AmmunitionType == ArtilleryAmmunition.Canister ? 1f : contact });
                    }
                }
            }
            foreach (var horse in world.LooseHorses.Values)
            {
                if (!horse.Targetable || shot.StruckSoldiers.Contains(horse.Rider)) continue;
                if (SweptSoldier(from, to, world.Terrain.SurfacePoint(horse.Position), 1.8f, radius + .5f, out float fraction))
                    soldierIntersections.Add(new SoldierIntersection { Soldier = horse.Rider, Horse = true, Fraction = fraction, DamageScale = 1f });
            }
            AddArtilleryIntersections(shot,from,to);
            // Resolve in flight order, including crews, carriages and infantry in one tick.
            soldierIntersections.Sort();
            float speed = shot.Velocity.magnitude;
            float damage = Mathf.Min(180f, 100f * speed * speed / (60f * 60f));
            Vector3 impulse = shot.Velocity.normalized * Mathf.Clamp(speed * .1f, 1f, 8f) + Vector3.up * 2f;
            foreach (var hit in soldierIntersections)
            {
                Vector3 impact = Vector3.Lerp(from, to, hit.Fraction);
                if(hit.Crew||hit.Carriage)
                { if(!shot.StruckArtillery.Add(hit.Gun*5+hit.Role))continue; }
                else if (!shot.StruckSoldiers.Add(hit.Soldier)) continue;
                // Resolve pellet energy at contact time, rather than the end of this frame.
                float hitAge = Mathf.Max(0f, shot.Age - dt + dt * hit.Fraction);
                float pelletEnergy = Mathf.Lerp(1f, .15f, Mathf.InverseLerp(90f, 220f, hitAge * 180f));
                float hitDamage = shot.Shrapnel ? Mathf.Lerp(85f,20f,Mathf.Clamp01(hitAge/FragmentLifetime)) : shot.AmmunitionType == ArtilleryAmmunition.Canister ? 140f * pelletEnergy : damage * hit.DamageScale;
                float impulseScale = shot.AmmunitionType == ArtilleryAmmunition.Canister ? Mathf.Sqrt(pelletEnergy) * .55f : Mathf.Sqrt(hit.DamageScale);
                if(hit.Crew) DamageCrew(Guns[hit.Gun],hit.Role,hitDamage,shot.GunId);
                else if(hit.Carriage) DamageGun(Guns[hit.Gun],shot.AmmunitionType==ArtilleryAmmunition.Canister?hitDamage*.025f:hitDamage,shot.GunId);
                else if (hit.Horse) world.ApplyHorseHit(hit.Soldier, hitDamage, shot.Shrapnel ? DeathCause.Shell : DeathCause.Roundshot, impulse * impulseScale);
                else
                {
                    bool killed = world.ApplyArtilleryHit(hit.Soldier, hitDamage, shot.Shrapnel ? DeathCause.Shell : DeathCause.Roundshot, impulse * impulseScale, impact);
                    int battalionId = world.Soldiers.BattalionIds[hit.Soldier];
                    CreditHit(shot.GunId,world.Battalions[battalionId].Side,killed);
                    if (killed) roundshotLosses[battalionId]++;
                    else ApplyDamagePressure(world.Battalions[battalionId], 0, "Under cannon fire");
                    shot.LastHitBattalion = battalionId;
                }
                shot.HitCount++;
                EmitImpact(impact, false, false, shot.Velocity, shot.AmmunitionType);
                if (shot.AmmunitionType == ArtilleryAmmunition.Canister)
                {
                    shot.Position = impact;
                    shot.Active = false;
                    break;
                }
            }
            foreach (var battalion in world.Battalions)
                if (roundshotLosses[battalion.Id] > 0)
                    ApplyDamagePressure(battalion, roundshotLosses[battalion.Id], "Under cannon fire");
        }

        // Sweep against a vertical body cylinder expanded by the ball radius, clipping height
        // and horizontal intersection to the SAME portion of the segment to avoid aerial hits.
        internal static bool SweptSoldier(Vector3 from, Vector3 to, Vector3 feet, float height, float radius, out float fraction)
        {
            Vector3 relative = from - feet, delta = to - from;
            float minimum = 0f, maximum = 1f;
            fraction = 0f;
            if (!ClipAxis(relative.y - height * .5f, delta.y, height * .5f + .08f, ref minimum, ref maximum)) return false;
            float a = delta.x * delta.x + delta.z * delta.z;
            float c = relative.x * relative.x + relative.z * relative.z - radius * radius;
            if (a < .000001f)
            {
                if (c > 0f) return false;
            }
            else
            {
                float b = relative.x * delta.x + relative.z * delta.z;
                float discriminant = b * b - a * c;
                if (discriminant < 0f) return false;
                float root = Mathf.Sqrt(discriminant);
                minimum = Mathf.Max(minimum, (-b - root) / a);
                maximum = Mathf.Min(maximum, (-b + root) / a);
                if (minimum > maximum) return false;
            }
            fraction = minimum;
            return true;
        }

        public const float CarcassBurnDuration = 90f;
        public const float CarcassFireRadius = 18f;
        public const float LimeDamagePerPulse=6f, LimeDuration=60f;
        public const float PoisonGasDamagePerPulse=6f, PoisonGasDuration=45f, PoisonGasRadius=20f;
        public static bool IsCloudShell(ArtilleryAmmunition type)=>type==ArtilleryAmmunition.Lime||type==ArtilleryAmmunition.PoisonGas;
        public static float HazardPulseInterval(ArtilleryAmmunition type)=>type==ArtilleryAmmunition.PoisonGas?2f:3f;
        public static float HazardRadius(ArtilleryAmmunition type) => type==ArtilleryAmmunition.PoisonGas?PoisonGasRadius:type == ArtilleryAmmunition.Lime ? 16f : CarcassFireRadius;
        static float CloudDamage(ArtilleryAmmunition type)=>type==ArtilleryAmmunition.PoisonGas?PoisonGasDamagePerPulse:LimeDamagePerPulse;
        float HazardExposure(Hazard hazard,Vector3 position)
        {
            if(hazard.Type!=ArtilleryAmmunition.PoisonGas)
                return 1f-Mathf.Clamp01(Vector3.Distance(position,hazard.Position)/HazardRadius(hazard.Type));
            var ground=new Vector2(position.x,position.z);
            // The dense cloud follows the terrain, leaving airborne soldiers above it clear.
            if(position.y-world.Terrain.HeightAt(ground)>2.4f)return 0f;
            return 1f-Mathf.Clamp01(Vector2.Distance(ground,new Vector2(hazard.Position.x,hazard.Position.z))/PoisonGasRadius);
        }

        public static float ExplosionRadius(ArtilleryAmmunition type)
            => type == ArtilleryAmmunition.Carcass ? 4f : 5f;

        void Explode(CannonProjectile shot)
        {
            float radius = ExplosionRadius(shot.AmmunitionType);
            if(shot.AmmunitionType==ArtilleryAmmunition.Carcass||IsCloudShell(shot.AmmunitionType))
            {
                var hazard=Hazards[hazardCursor++%Hazards.Length];hazard.Position=shot.Position;hazard.Type=shot.AmmunitionType;
                hazard.Side=shot.Side;hazard.Remaining=shot.AmmunitionType==ArtilleryAmmunition.PoisonGas?PoisonGasDuration:shot.AmmunitionType==ArtilleryAmmunition.Lime?LimeDuration:CarcassBurnDuration;
                hazard.UntilPulse=HazardPulseInterval(shot.AmmunitionType);
                if(shot.AmmunitionType==ArtilleryAmmunition.PoisonGas)
                    hazard.Position=world.Terrain.SurfacePoint(new Vector2(shot.Position.x,shot.Position.z));
                hazard.GunId=shot.GunId;
                hazard.Direction=shot.Velocity;
                if(IsCloudShell(shot.AmmunitionType)){PulseHazard(hazard);return;}
            }
            QueueShrapnel(shot);
            ExplodeArtillery(shot,radius);
            foreach (var battalion in world.Battalions)
            {
                if (battalion.AliveCount <= 0) continue;
                int killed = 0, affected = 0;
                // Use actual positions, including stragglers outside the nominal formation bounds.
                for (int soldier = battalion.SoldierStart; soldier < battalion.SoldierStart + battalion.SoldierCount; soldier++)
                {
                    if (!world.Soldiers.Alive[soldier]) continue;
                    Vector3 feet = world.SoldierPosition(soldier,world.Time);
                    Vector3 away = feet - shot.Position;
                    float distance = away.magnitude;
                    if (distance >= radius) continue;
                    float strength = 1f - distance / radius;
                    float damage = (shot.AmmunitionType == ArtilleryAmmunition.Carcass ? 110f : 160f) * strength * strength;
                    if (damage < 4f) continue;
                    if(shot.AmmunitionType==ArtilleryAmmunition.Carcass)SoldierIgnited?.Invoke(soldier,strength);
                    away.y = 0;
                    if (away.sqrMagnitude < .001f) away = new Vector3(Mathf.Sin(soldier * 2.4f), 0, Mathf.Cos(soldier * 2.4f));
                    float push = strength * strength;
                    Vector3 impulse = (away.normalized * (9f*Mathf.Lerp(.82f,1.18f,RifleFireTiming.Variation(soldier,433)))
                        + Vector3.up * (6f*Mathf.Lerp(.8f,1.25f,RifleFireTiming.Variation(soldier,439)))) * push;
                    bool fatal=world.ApplyArtilleryHit(soldier, damage, DeathCause.Shell, impulse, shot.Position);
                    if(fatal)killed++;CreditHit(shot.GunId,battalion.Side,fatal);
                    affected++;
                }
                if (affected > 0) ApplyDamagePressure(battalion, killed, "Shell explosion");
            }
            foreach (var horse in world.LooseHorses.Values)
            {
                if (!horse.Targetable) continue;
                Vector3 away = world.Terrain.SurfacePoint(horse.Position) - shot.Position;
                float strength = 1f - Mathf.Clamp01(away.magnitude / radius);
                float damage = (shot.AmmunitionType == ArtilleryAmmunition.Carcass ? 110f : 160f) * strength * strength;
                if (damage >= 4f) world.ApplyHorseHit(horse.Rider, damage, DeathCause.Shell, (away.normalized * 9f + Vector3.up * 6f) * strength * strength);
            }
        }

        void StepHazards(float dt)
        {
            foreach (var hazard in Hazards)
            {
                if (hazard.Remaining <= 0f) continue;
                // Clip to its lifetime, preserving every scheduled pulse on long frames.
                double elapsed = System.Math.Min(dt, hazard.RemainingSeconds);
                if (hazard.RemainingSeconds - elapsed < .0001) elapsed = hazard.RemainingSeconds;
                hazard.RemainingSeconds = System.Math.Max(0, hazard.RemainingSeconds - elapsed);
                hazard.UntilPulseSeconds -= elapsed;
                while (hazard.UntilPulseSeconds <= .0001)
                {
                    PulseHazard(hazard);
                    hazard.UntilPulseSeconds += HazardPulseInterval(hazard.Type);
                }
            }
        }

        void PulseHazard(Hazard hazard)
        {
            PulseArtilleryHazard(hazard);
            foreach (var battalion in world.Battalions)
            {
                if (battalion.AliveCount <= 0) continue;
                int exposed = 0, killed = 0, living = battalion.AliveCount;
                float exposure = 0f;
                // Persistent hazards affect either army, only while actual men occupy them.
                for (int soldier = battalion.SoldierStart; soldier < battalion.SoldierStart + battalion.SoldierCount; soldier++)
                {
                    if (!world.Soldiers.Alive[soldier]) continue;
                    float strength=HazardExposure(hazard,world.SoldierPosition(soldier,world.Time));
                    if(strength<=0)continue;
                    exposed++; exposure += strength;
                    if(hazard.Type==ArtilleryAmmunition.Carcass)
                    {SoldierIgnited?.Invoke(soldier,strength);bool fatal=world.ApplyWound(soldier,24f*strength,DeathCause.Fire,hazard.Position);if(fatal)killed++;CreditHit(hazard.GunId,battalion.Side,fatal);}
                    else if(IsCloudShell(hazard.Type))
                    {
                        bool fatal=world.ApplyWound(soldier,CloudDamage(hazard.Type)*strength,hazard.Type==ArtilleryAmmunition.PoisonGas?DeathCause.PoisonGas:DeathCause.Lime,hazard.Position);
                        if(fatal)killed++;
                        CreditHit(hazard.GunId,battalion.Side,fatal);
                    }
                }
                if (exposed == 0) continue;
                // Ongoing injury is combat exposure, including for field-medicine recovery.
                battalion.LastIncomingFireTime=world.Time;
                if (hazard.Type == ArtilleryAmmunition.Carcass)
                    ApplyDamagePressure(battalion, killed, "Under incendiary fire");
                else
                {
                    float share = Mathf.Clamp01(exposure / Mathf.Max(1, living));
                    // Retain the worst exposed share; overlapping clouds do not stack without limit.
                    if(hazard.Type==ArtilleryAmmunition.PoisonGas)battalion.BattleGasExposure=Mathf.Max(battalion.BattleGasExposure,share);
                    else if(hazard.Type==ArtilleryAmmunition.Lime)battalion.BattleLimeExposure=Mathf.Max(battalion.BattleLimeExposure,share);
                    battalion.Cohesion = Mathf.Max(.1f, battalion.Cohesion - .06f * share);
                    battalion.Fatigue = Mathf.Min(100f, battalion.Fatigue + 3f * share);
                    battalion.Morale = Mathf.Max(0f, battalion.Morale - share/world.Resolve(battalion));
                    battalion.LimeObscuration=world.Time<battalion.LimeExposureUntil?Mathf.Max(battalion.LimeObscuration,share):share;
                    battalion.LimeExposureUntil=world.Time+HazardPulseInterval(hazard.Type)+.2f;
                    battalion.MoraleReason = hazard.Type==ArtilleryAmmunition.PoisonGas?"Poison gas: casualties and impaired vision":"Lime cloud: injuries and impaired vision";
                    if(killed>0)ApplyDamagePressure(battalion,killed,hazard.Type==ArtilleryAmmunition.PoisonGas?"Losses from poison gas":"Losses from lime exposure");
                    if (!battalion.Routing && battalion.Morale <= 18f && !world.ResistRout(battalion))
                    { battalion.Routing = true; world.Withdraw(battalion.Id); }
                }
            }
        }

        void ApplyDamagePressure(BattalionState battalion, int killed, string reason)
        {
            battalion.LastIncomingFireTime = world.Time;
            float loss = killed / (float)Mathf.Max(1, battalion.SoldierCount);
            battalion.Morale = Mathf.Max(0f, battalion.Morale - (2f + loss * 90f)/world.Resolve(battalion));
            battalion.Cohesion = Mathf.Max(.1f, battalion.Cohesion - loss * .5f);
            battalion.MoraleReason = reason;
            if (battalion.AliveCount > 0 && !battalion.Routing && battalion.Morale <= 18f && !world.ResistRout(battalion))
            {
                battalion.Routing = true;
                world.Withdraw(battalion.Id);
            }
        }

        void EmitImpact(Vector3 position, bool shell, bool terrainStrike = false, Vector3 velocity = default, ArtilleryAmmunition type = ArtilleryAmmunition.Roundshot)
        {
            int sequence = ++ImpactSequence;
            ArtilleryImpact impact = Impacts[(sequence - 1) % Impacts.Length];
            impact.Sequence = sequence;
            impact.AmmunitionType = type;
            impact.Position = position;
            impact.ExplosiveShell = shell;
            impact.Time = world.Time;
            impact.TerrainStrike = terrainStrike;
            impact.Direction = velocity.sqrMagnitude > .001f ? velocity.normalized : Vector3.forward;
            impact.Speed = velocity.magnitude;
            if (terrainStrike) world.Terrain.Carve(impact);
        }

        static bool ClipAxis(float origin, float direction, float half, ref float minimum, ref float maximum)
        {
            if (Mathf.Abs(direction) < .00001f) return Mathf.Abs(origin) <= half;
            float enter = (-half - origin) / direction, exit = (half - origin) / direction;
            if (enter > exit) { float temporary = enter; enter = exit; exit = temporary; }
            minimum = Mathf.Max(minimum, enter);
            maximum = Mathf.Min(maximum, exit);
            return minimum <= maximum;
        }
    }
}
