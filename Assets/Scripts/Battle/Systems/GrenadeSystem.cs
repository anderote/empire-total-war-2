using UnityEngine;

namespace EmpireBattle
{
    public sealed class GrenadeCharge
    {
        public bool Active,Exploded;
        public int Unit,Thrower;
        public bool Released;
        public float Age;
        public Vector3 Start,End;
        public Vector3 Position=>Vector3.Lerp(Start,End,Mathf.Clamp01(Age/1.2f))+Vector3.up*(Mathf.Sin(Mathf.Clamp01(Age/1.2f)*Mathf.PI)*4);
    }
    // A finite regimental consumable. Fire-at-will controls throws; no mid-battle refills.
    public sealed class GrenadeSystem
    {
        public const float Range=28,BlastRadius=4,ReleaseDelay=.55f,ThrowDuration=1.1f;
        readonly BattleWorld world;
        readonly float[] cooldown;
        public readonly GrenadeCharge[] Charges=new GrenadeCharge[128];
        public int Thrown {get;private set;}
        public int Detonated {get;private set;}
        public GrenadeSystem(BattleWorld value)
        {world=value;cooldown=new float[world.Battalions.Length];for(int i=0;i<Charges.Length;i++)Charges[i]=new GrenadeCharge();}
        public void Step(float dt)
        {
            if(dt<=0||!world.CanSimulate)return;
            foreach(var charge in Charges)
            {
                if(!charge.Active)continue;charge.Age+=dt;
                if(!charge.Released)
                {
                    var owner=world.Battalions[charge.Unit];
                    if(!world.Soldiers.Alive[charge.Thrower]||world.Soldiers.IsKnockedDown(charge.Thrower,world.Time)||owner.Moving||owner.Routing||owner.Withdrawing||owner.InMelee)
                    {charge.Active=false;owner.GrenadesRemaining++;world.Soldiers.GrenadeThrowStarts[charge.Thrower]=-100;continue;}
                    if(charge.Age<0)continue;
                    charge.Released=true;charge.Start=ReleaseOrigin(charge.Thrower);owner.ShotsFired++;Thrown++;
                }
                if(!charge.Exploded&&charge.Age>=1.8f){charge.Exploded=true;Detonate(charge);}
                if(charge.Age>=2.4f)charge.Active=false;
            }
            foreach(var unit in world.Battalions)
            {
                cooldown[unit.Id]=Mathf.Max(0,cooldown[unit.Id]-dt);
                if(unit.Kind!=UnitKind.Infantry||!UnitRoster.Shoots(unit.Role)||unit.GrenadesRemaining<=0||unit.AliveCount<=0||unit.Routing||unit.Moving||unit.Withdrawing||unit.InMelee||!unit.FireAtWill||cooldown[unit.Id]>0)continue;
                BattalionState target=null;float nearest=Range*Range;
                foreach(var enemy in world.Battalions)
                {
                    float distance=(enemy.Position-unit.Position).sqrMagnitude;
                    if(enemy.Side==unit.Side||enemy.AliveCount==0||distance>nearest||distance<64)continue;
                    if(Vector2.Dot(FormationMath.Forward(unit.Facing),(enemy.Position-unit.Position).normalized)<.45f)continue;
                    target=enemy;nearest=distance;
                }
                if(target==null)continue;
                int soldier=world.SoldierAtSlot(target,0);if(soldier<0)continue;
                Vector2 landing=world.Soldiers.Positions[soldier];
                if(Vector2.Distance(unit.Position,landing)>Range)continue;
                bool safe=true;
                for(int i=0;i<world.Soldiers.Alive.Length;i++)
                    if(world.Soldiers.Alive[i]&&world.Battalions[world.Soldiers.BattalionIds[i]].Side==unit.Side&&(world.Soldiers.Positions[i]-landing).sqrMagnitude<36){safe=false;break;}
                if(!safe&&!(unit.AttackMode==InfantryAttackMode.FirePosition&&unit.AttackTargetId>=0))continue;
                Vector3 start=world.Terrain.SurfacePoint(unit.Position,1.5f),end=world.Terrain.SurfacePoint(landing,.15f);
                bool clear=true;for(int n=1;n<12;n++){float t=n/12f;var p=Vector3.Lerp(start,end,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*4);if(p.y<world.Terrain.HeightAt(new Vector2(p.x,p.z))+.1f){clear=false;break;}}
                if(!clear)continue;
                int thrower=-1;
                for(int slot=0;slot<unit.AliveCount;slot++)
                {int candidate=world.SoldierAtSlot(unit,slot);if(candidate>=0&&!world.Soldiers.Captains[candidate]&&!world.Soldiers.PikeSergeants[candidate]&&!world.Soldiers.IsKnockedDown(candidate,world.Time)&&world.Soldiers.ReloadReadyTimes[candidate]<=world.Time&&world.Time-world.Soldiers.FireTimes[candidate]>1){thrower=candidate;break;}}
                if(thrower<0||!ClearArc(ReleaseOrigin(thrower),end))continue;
                foreach(var charge in Charges)if(!charge.Active)
                {charge.Active=true;charge.Released=false;charge.Thrower=thrower;charge.Exploded=false;charge.Unit=unit.Id;charge.Age=-ReleaseDelay;charge.Start=ReleaseOrigin(thrower);charge.End=end;unit.GrenadesRemaining--;world.Soldiers.GrenadeThrowStarts[thrower]=world.Time;cooldown[unit.Id]=6;break;}
            }
        }
        bool ClearArc(Vector3 start,Vector3 end)
        {
            for(int n=1;n<12;n++){float t=n/12f;var p=Vector3.Lerp(start,end,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*4);if(p.y<world.Terrain.HeightAt(new Vector2(p.x,p.z))+.1f)return false;}
            return true;
        }
        public Vector3 ReleaseOrigin(int soldier)
        {
            Vector2 forward=FormationMath.Forward(world.Soldiers.Facings[soldier]);
            Vector2 right=new Vector2(forward.y,-forward.x);
            Vector2 point=world.Soldiers.Positions[soldier]+forward*.52f+right*.22f;
            return world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier],1.72f)+new Vector3(point.x-world.Soldiers.Positions[soldier].x,0,point.y-world.Soldiers.Positions[soldier].y);
        }
        void Detonate(GrenadeCharge charge)
        {
            Detonated++;var owner=world.Battalions[charge.Unit];var at=new Vector2(charge.End.x,charge.End.z);
            for(int i=0;i<world.Soldiers.Alive.Length;i++)
            {
                if(!world.Soldiers.Alive[i])continue;
                float distance=Vector2.Distance(world.Soldiers.Positions[i],at);if(distance>BlastRadius)continue;
                var victim=world.Battalions[world.Soldiers.BattalionIds[i]];
                Vector3 delta=world.Terrain.SurfacePoint(world.Soldiers.Positions[i],1)-charge.End;
                bool killed=world.ApplyArtilleryHit(i,Mathf.Lerp(105,15,distance/BlastRadius),DeathCause.Shell,delta.normalized*2+Vector3.up,charge.End);
                if(victim.Side!=owner.Side){owner.Hits++;if(killed)owner.Kills++;}
                victim.Morale=Mathf.Max(0,victim.Morale-3/world.Resolve(victim));victim.LastIncomingFireTime=world.Time;
            }
        }
    }
    public sealed class GrenadeView : MonoBehaviour
    {
        GrenadeSystem system;GameObject[] balls;Material shell,burst;
        public void Initialize(GrenadeSystem value)
        {
            system=value;balls=new GameObject[system.Charges.Length];
            var shader=Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Color");
            shell=new Material(shader);shell.color=new Color(.19f,.16f,.11f);burst=new Material(shader);burst.color=new Color(1,.55f,.12f);
        }
        void LateUpdate()
        {
            if(system==null)return;
            for(int i=0;i<balls.Length;i++)
            {
                var c=system.Charges[i];if(!c.Active||!c.Released){if(balls[i])balls[i].SetActive(false);continue;}
                if(!balls[i]){balls[i]=GameObject.CreatePrimitive(PrimitiveType.Sphere);balls[i].name="Grenade";balls[i].transform.SetParent(transform);Destroy(balls[i].GetComponent<Collider>());}
                balls[i].SetActive(true);balls[i].transform.position=c.Exploded?c.End+Vector3.up*.5f:c.Position;
                balls[i].transform.localScale=Vector3.one*(c.Exploded?Mathf.Max(.1f,2.2f*(1-(c.Age-1.8f)/.6f)):.065f);
                balls[i].GetComponent<Renderer>().sharedMaterial=c.Exploded?burst:shell;
            }
        }
        void OnDestroy(){if(shell)Destroy(shell);if(burst)Destroy(burst);}
    }
}
