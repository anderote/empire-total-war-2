using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class CombatSystem
    {
        sealed class Bullet
        {
            public Vector3 Origin, Velocity;
            public float Born, Duration, Age, Damage, Morale;
            public int Attacker, Shooter, Sequence;
        }
        public SceneryImpactMap BulletScenery { get; set; }
        readonly List<Bullet> bullets = new List<Bullet>();
        readonly Dictionary<Vector2Int,List<int>> bulletCells = new Dictionary<Vector2Int,List<int>>();
        static Vector2Int BulletCell(Vector2 position)=>new Vector2Int(Mathf.FloorToInt(position.x/4f),Mathf.FloorToInt(position.y/4f));

        void LaunchBullet(Vector3 origin,Vector3 end,float damage,int attacker,int shooter,float born,bool visual,float morale=1f,bool grounded=false)
        {
            // Misses can continue beyond aimed range, but remain bounded ballistic paths.
            if(!WeaponBallistics.TrySolve(origin,end,out var velocity,out float duration,1000f))return;
            int sequence=0;
            if(visual){EmitShot(origin,end,duration,false,-1,false,born,grounded);sequence=ShotSequence;}
            bullets.Add(new Bullet{Origin=origin,Velocity=velocity,Born=born,Duration=duration,
                Damage=damage,Attacker=attacker,Shooter=shooter,Sequence=sequence,Morale=morale});
        }

        void StepBullets()
        {
            if(bullets.Count==0)return;
            foreach(var cell in bulletCells.Values)cell.Clear();
            for(int i=0;i<world.Soldiers.Alive.Length;i++)
            {
                if(!world.Soldiers.Alive[i])continue;
                var key=BulletCell(world.Soldiers.Positions[i]);
                if(!bulletCells.TryGetValue(key,out var cell)){cell=new List<int>();bulletCells.Add(key,cell);}
                cell.Add(i);
            }
            for(int b=bullets.Count-1;b>=0;b--)
            {
                var bullet=bullets[b];
                float age=Mathf.Min(bullet.Duration,world.Time-bullet.Born);
                if(age<=bullet.Age)continue;
                Vector3 from=WeaponBallistics.PositionAt(bullet.Origin,bullet.Velocity,bullet.Age);
                Vector3 to=WeaponBallistics.PositionAt(bullet.Origin,bullet.Velocity,age);
                float nearest=1f;int victim=-1,horse=-1;
                bool ground=WeaponBallistics.TraceGround(world.Terrain,from,
                    bullet.Velocity+Vector3.down*(WeaponBallistics.Gravity*bullet.Age),age-bullet.Age,out var groundEnd);
                if(ground)nearest=Mathf.Clamp01(Vector3.Distance(from,groundEnd)/Mathf.Max(.001f,Vector3.Distance(from,to)));
                if(BulletScenery!=null&&BulletScenery.Trace(from,to,out var sceneryEnd,out _,out _))
                {
                    float fraction=Vector3.Distance(from,sceneryEnd)/Mathf.Max(.001f,Vector3.Distance(from,to));
                    if(fraction<nearest){nearest=fraction;ground=true;}
                }
                float minX=Mathf.Min(from.x,to.x)-.3f,maxX=Mathf.Max(from.x,to.x)+.3f;
                float minZ=Mathf.Min(from.z,to.z)-.3f,maxZ=Mathf.Max(from.z,to.z)+.3f;
                var minimum=BulletCell(new Vector2(minX,minZ));var maximum=BulletCell(new Vector2(maxX,maxZ));
                for(int x=minimum.x;x<=maximum.x;x++)
                for(int z=minimum.y;z<=maximum.y;z++)
                {
                    if(!bulletCells.TryGetValue(new Vector2Int(x,z),out var cell))continue;
                    foreach(int i in cell)
                    {
                        if(!world.Soldiers.Alive[i])continue;
                        var unit=world.Battalions[world.Soldiers.BattalionIds[i]];
                        if(i==bullet.Shooter)continue;
                        var position=world.Soldiers.Positions[i];
                        if(position.x<minX||position.x>maxX||position.y<minZ||position.y>maxZ)continue;
                        float height=world.Soldiers.IsKnockedDown(i,world.Time)?.45f:
                            unit.Kind==UnitKind.Cavalry?2.8f:RifleFireTiming.IsKneeling(world,unit,i,world.Time)?1.15f:1.8f;
                        if(ArtillerySystem.SweptSoldier(from,to,world.SoldierPosition(i,world.Time),height,.3f,out float fraction)&&fraction<=nearest)
                        {nearest=fraction;victim=i;horse=-1;ground=false;}
                    }
                }
                foreach(var mount in world.LooseHorses.Values)
                    if(mount.Targetable&&
                        ArtillerySystem.SweptSoldier(from,to,world.Terrain.SurfacePoint(mount.Position),1.8f,.8f,out float fraction)&&fraction<nearest)
                    {nearest=fraction;horse=mount.Rider;victim=-1;ground=false;}
                bool contact=victim>=0||horse>=0;
                if(contact||ground)
                {
                    Vector3 impact=Vector3.Lerp(from,to,nearest);bool killed=false;
                    if(victim>=0)
                    {
                        killed=world.ApplyWound(victim,bullet.Damage,DeathCause.Musket,impact,bullet.Velocity.normalized);
                        var attacker=world.Battalions[bullet.Attacker];
                        if(world.Battalions[world.Soldiers.BattalionIds[victim]].Side!=attacker.Side)
                        {
                        attacker.Hits++;attacker.Experience+=killed?6:1;
                        world.Soldiers.Experience[bullet.Shooter]+=killed?6:1;
                        if(killed){attacker.Kills++;world.Soldiers.Kills[bullet.Shooter]++;
                            moraleCasualtyBonus[world.Soldiers.BattalionIds[victim]]+=bullet.Morale-1f;}
                        }
                    }
                    else if(horse>=0)
                    {world.ApplyHorseHit(horse,bullet.Damage,DeathCause.Musket,bullet.Velocity.normalized);
                        killed=world.LooseHorses.TryGetValue(horse,out var mount)&&mount.Health<=0;}
                    if(bullet.Sequence>0)
                    {
                        var shot=Shots[(bullet.Sequence-1)%Shots.Length];
                        if(shot.Sequence==bullet.Sequence)
                        {shot.End=impact;shot.Duration=Mathf.Max(.001f,Mathf.Lerp(bullet.Age,age,nearest));
                            shot.Hit=contact;shot.Killed=killed;shot.TargetSoldier=victim;shot.TargetHorse=horse;shot.Grounded=ground;}
                    }
                }
                bullet.Age=age;
                if(contact||ground||age>=bullet.Duration)bullets.RemoveAt(b);
            }
        }
    }
}
