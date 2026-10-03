using UnityEngine;

namespace EmpireBattle
{
    public sealed class ArtilleryCrewState
    {
        public float Health=100f, DeathTime;
        public Vector2 Local;
        public Vector3 DeathPosition;
        public float DeathFacing;
        public bool Alive => Health>0f;
    }

    public sealed partial class ArtillerySystem
    {
        public static ArtilleryCrewState[] CreateCrew() => new[] {
            new ArtilleryCrewState { Local=new Vector2(-1.45f,.75f) },
            new ArtilleryCrewState { Local=new Vector2(1.45f,.75f) },
            new ArtilleryCrewState { Local=new Vector2(-1.2f,-1.8f) },
            new ArtilleryCrewState { Local=new Vector2(1.2f,-1.8f) }
        };

        public static Vector2 CrewStation(ArtilleryGun gun,int role)
        {
            Vector2 station=role<2?new Vector2(role==0?-1.45f:1.45f,.75f):new Vector2(role==2?-1.2f:1.2f,-1.8f);
            if(gun.Operational&&!gun.Suppressed&&!gun.Relocating)
            {
                if(role==0&&(gun.Phase==ArtilleryPhase.Sponging||gun.Phase==ArtilleryPhase.Ramming)||role==1&&gun.Phase==ArtilleryPhase.Loading)
                    station=new Vector2(role==0?-.38f:.38f,gun.ExplosiveShell?1.65f:1.9f);
                if(gun.Phase==ArtilleryPhase.RunUp&&role>=2)station=new Vector2(role==2?-.9f:.9f,-1.35f);
            }
            if(gun.Moving)station=new Vector2(role%2==0?-.95f:.95f,role<2?-.8f:-2.2f);
            station.y-=gun.RecoilOffset;
            return station;
        }

        public void StepCrewPositions(ArtilleryGun gun,float dt)
        {
            for(int role=0;role<gun.Crew.Length;role++)
                if(gun.Crew[role].Alive)gun.Crew[role].Local=Vector2.MoveTowards(gun.Crew[role].Local,CrewStation(gun,role),dt*1.5f*gun.GasMovementMultiplier);
        }

        public Vector3 CrewPosition(ArtilleryGun gun,int role)
        {
            var member=gun.Crew[role];
            if(!member.Alive)return member.DeathPosition;
            return world.Terrain.SurfacePoint(gun.Position+FormationMath.Right(gun.Facing)*member.Local.x+FormationMath.Forward(gun.Facing)*member.Local.y);
        }

        void DamageCrew(ArtilleryGun gun,int role,float damage,int attacker)
        {
            var member=gun.Crew[role];
            if(!member.Alive||damage<=0)return;
            Vector3 position=CrewPosition(gun,role);
            member.Health=Mathf.Max(0,member.Health-damage);
            if(!member.Alive){member.DeathTime=world.Time;member.DeathPosition=position;member.DeathFacing=gun.Facing;}
            CreditHit(attacker,gun.Side,!member.Alive);
            gun.Morale=Mathf.Max(0,gun.Morale-(!member.Alive?18f:4f));
            gun.Cohesion=Mathf.Max(.5f,gun.Cohesion-(!member.Alive?.12f:.03f));
            DisableIfLost(gun);
        }

        void DamageGun(ArtilleryGun gun,float damage,int attacker)
        {
            if(gun.Destroyed||damage<=0)return;
            gun.Health=Mathf.Max(0,gun.Health-damage);
            // Kills count people; carriage hits still earn hit credit.
            CreditHit(attacker,gun.Side,false);
            gun.Morale=Mathf.Max(0,gun.Morale-8f);
            DisableIfLost(gun);
        }

        static void DisableIfLost(ArtilleryGun gun)
        {
            if(gun.Operational)return;
            gun.Moving=false;gun.SetupRemaining=gun.MovePreparation=0;gun.MoveRoute.Clear();
            gun.FireAtWill=false;gun.TargetBattalion=-1;gun.TargetBattery=-1;gun.HasGroundTarget=false;
            gun.Phase=ArtilleryPhase.Exhausted;gun.PhaseElapsed=0;gun.ReloadRemaining=0;
        }

        void AddArtilleryIntersections(CannonProjectile shot,Vector3 from,Vector3 to)
        {
            foreach(var gun in Guns)
            {
                if(gun.Id==shot.GunId&&!shot.Shrapnel&&shot.Age<.1f)continue;
                if(BattleOptimizationSettings.ArtilleryBroadphase)
                {
                    // Reject remote batteries before terrain samples and exact
                    // swept tests for each crewman. L1 distance conservatively
                    // covers every crew station at any gun facing, plus recoil.
                    float reach=Mathf.Abs(gun.RecoilOffset)+1f;
                    foreach(var member in gun.Crew)if(member.Alive)
                        reach=Mathf.Max(reach,Mathf.Abs(member.Local.x)+Mathf.Abs(member.Local.y)+.4f);
                    if(gun.Position.x<Mathf.Min(from.x,to.x)-reach||gun.Position.x>Mathf.Max(from.x,to.x)+reach
                        ||gun.Position.y<Mathf.Min(from.z,to.z)-reach||gun.Position.y>Mathf.Max(from.z,to.z)+reach)continue;
                }
                for(int role=0;role<gun.Crew.Length;role++)
                    if(gun.Crew[role].Alive&&!shot.StruckArtillery.Contains(gun.Id*5+role)&&
                        SweptSoldier(from,to,CrewPosition(gun,role),1.8f,shot.AmmunitionType==ArtilleryAmmunition.Canister?.24f:.4f,out float fraction))
                        soldierIntersections.Add(new SoldierIntersection { Crew=true,Gun=gun.Id,Role=role,Fraction=fraction,DamageScale=1f });
                if(!gun.Destroyed&&!shot.StruckArtillery.Contains(gun.Id*5+4)&&
                    SweptSoldier(from,to,GunCenter(gun),1.25f,1f,out float carriageFraction))
                    soldierIntersections.Add(new SoldierIntersection { Carriage=true,Gun=gun.Id,Role=4,Fraction=carriageFraction,DamageScale=1f });
            }
        }

        bool HitArtilleryShell(CannonProjectile shot,Vector3 from,Vector3 to)
        {
            soldierIntersections.Clear();AddArtilleryIntersections(shot,from,to);
            if(soldierIntersections.Count==0)return false;
            soldierIntersections.Sort();var hit=soldierIntersections[0];
            shot.Position=Vector3.Lerp(from,to,hit.Fraction);shot.HitCount++;
            EmitImpact(shot.Position,true,false,shot.Velocity,shot.AmmunitionType);
            Explode(shot);shot.Active=false;return true;
        }

        struct FragmentBurst
        {
            public Vector3 Position;
            public ArmySide Side;
            public int GunId;
            public CannonProjectile Parent;
        }
        readonly System.Collections.Generic.List<FragmentBurst> fragmentBursts = new System.Collections.Generic.List<FragmentBurst>();
        void QueueShrapnel(CannonProjectile shell)
        {
            fragmentBursts.Add(new FragmentBurst { Position=shell.Position,Side=shell.Side,GunId=shell.GunId,Parent=shell });
            if(!steppingProjectiles)ReleaseShrapnel();
        }
        void ReleaseShrapnel()
        {
            // Defer until the flight loop finishes: fragments all start on the next tick.
            int slot=0;
            foreach(var burst in fragmentBursts)
            for(int i=0;i<ShellFragments;i++)
            {
                while(slot<Projectiles.Length&&(Projectiles[slot].Active||fragmentBursts.Exists(b=>b.Parent==Projectiles[slot])))slot++;
                if(slot==Projectiles.Length)break;
                var fragment=Projectiles[slot++];
                float angle=(i+(float)random.NextDouble())*Mathf.PI*2f/ShellFragments;
                float lift=Mathf.Lerp(-.08f,.7f,(float)random.NextDouble());
                Vector3 direction=new Vector3(Mathf.Cos(angle),lift,Mathf.Sin(angle)).normalized;
                fragment.Id=++projectileSequence;fragment.GunId=burst.GunId;fragment.Side=burst.Side;
                fragment.FromCamera=false;fragment.Shrapnel=true;fragment.FragmentRangeRemaining=Mathf.Lerp(FragmentMinRange,FragmentRange,(float)random.NextDouble());
                fragment.FragmentStreakTime=Mathf.Lerp(.003f,.007f,(float)random.NextDouble());
                fragment.Position=burst.Position+Vector3.up*.35f;
                fragment.Velocity=direction*Mathf.Lerp(65f,115f,(float)random.NextDouble());
                fragment.Age=fragment.SegmentAge=0f;fragment.BounceCount=fragment.HitCount=0;
                fragment.LastHitBattalion=-1;fragment.AmmunitionType=ArtilleryAmmunition.Canister;
                fragment.StruckSoldiers.Clear();fragment.StruckArtillery.Clear();fragment.Active=true;
            }
            fragmentBursts.Clear();
        }

        void ExplodeArtillery(CannonProjectile shot,float radius)
        {
            float power=shot.AmmunitionType==ArtilleryAmmunition.Carcass?110f:160f;
            foreach(var gun in Guns)
            {
                
                for(int role=0;role<gun.Crew.Length;role++)
                {
                    if(!gun.Crew[role].Alive)continue;
                    float strength=1f-Mathf.Clamp01(Vector3.Distance(CrewPosition(gun,role),shot.Position)/radius);
                    if(power*strength*strength>=4f)DamageCrew(gun,role,power*strength*strength,shot.GunId);
                }
                float distance=Vector3.Distance(GunCenter(gun)+Vector3.up*.6f,shot.Position);
                float gunStrength=1f-Mathf.Clamp01(distance/radius);
                if(power*gunStrength*gunStrength>=4f)DamageGun(gun,power*gunStrength*gunStrength,shot.GunId);
            }
        }

        void PulseArtilleryHazard(Hazard hazard)
        {
            float radius=HazardRadius(hazard.Type);
            foreach(var gun in Guns)
            {
                int living=gun.LivingCrew;float exposure=0;
                for(int role=0;role<gun.Crew.Length;role++)
                {
                    if(!gun.Crew[role].Alive)continue;
                    float strength=HazardExposure(hazard,CrewPosition(gun,role));
                    exposure+=strength;
                    if(hazard.Type==ArtilleryAmmunition.Carcass)DamageCrew(gun,role,24f*strength,hazard.GunId);
                    else if(IsCloudShell(hazard.Type))DamageCrew(gun,role,CloudDamage(hazard.Type)*strength,hazard.GunId);
                }
                if(hazard.Type==ArtilleryAmmunition.Carcass)
                {
                    float strength=1f-Mathf.Clamp01(Vector3.Distance(GunCenter(gun),hazard.Position)/radius);
                    DamageGun(gun,6f*strength,hazard.GunId);
                }
                else if(exposure>0)
                {
                    float share=Mathf.Clamp01(exposure/Mathf.Max(1,living));
                    if(hazard.Type==ArtilleryAmmunition.PoisonGas)gun.BattleGasExposure=Mathf.Max(gun.BattleGasExposure,share);
                    else if(hazard.Type==ArtilleryAmmunition.Lime)gun.BattleLimeExposure=Mathf.Max(gun.BattleLimeExposure,share);
                    gun.Cohesion=Mathf.Max(.5f,gun.Cohesion-.06f*share);
                    gun.Fatigue=Mathf.Min(100,gun.Fatigue+3f*share);
                    gun.Morale=Mathf.Max(0,gun.Morale-share/(gun.BaseStats.Resolve*gun.Progression.ResolveMultiplier*(1+gun.Kit.resolve)));
                    // Exposure interrupts the current service phase; it resumes after the cloud clears.
                    gun.LimeExposureRemaining=Mathf.Max(gun.LimeExposureRemaining,HazardPulseInterval(hazard.Type)+.2f);
                }
            }
        }
    }
}
