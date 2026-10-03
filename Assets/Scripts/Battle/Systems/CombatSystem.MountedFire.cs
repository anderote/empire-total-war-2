using UnityEngine;
using EmpireBattle.Art;
namespace EmpireBattle
{
    public sealed partial class CombatSystem
    {
        public const float MountedFireDuration=.75f;
        CavalryCrowdLibrary mountedFireLibrary;
        void StepMountedFire(float dt)
        {
            var s=world.Soldiers;
            foreach(var unit in world.Battalions)
            {
                if(unit.Kind!=UnitKind.Cavalry)continue;
                string weapon=NapoleonicOutfits.MountedWeapon(unit.Loadout);
                if(weapon!="carbine"&&weapon!="pistol")continue;
                var profile=MilitaryEquipment.Weapon(weapon);unit.Weapon=profile;
                bool halted=!unit.Moving&&!unit.Routing&&!unit.Withdrawing&&!unit.InMelee&&!unit.MeleeMode&&unit.ChargePhase!=ChargePhase.Approach;
                BattalionState target=halted&&unit.FireAtWill?SelectFireTarget(unit,0,unit.Facing):null;
                for(int slot=0;slot<unit.AliveCount;slot++)
                {
                    int soldier=world.SoldierAtSlot(unit,slot);if(soldier<0)continue;
                    bool ready=halted&&s.MovementSpeeds[soldier]<.08f&&!s.IsKnockedDown(soldier,world.Time)&&s.MeleeEnds[soldier]<=world.Time;
                    if(s.ReloadReadyTimes[soldier]>world.Time)
                    {if(!ready)s.ReloadReadyTimes[soldier]+=dt;continue;}
                    if(!ready||target==null||s.RoundsRemaining[soldier]<=0||FriendlyBlocksFire(unit,target.Position))continue;
                    int victim=InfantryTargeting.Select(world,unit,soldier,target);if(victim<0)continue;
                    Vector2 delta=s.Positions[victim]-s.Positions[soldier];
                    if(delta.sqrMagnitude<9||delta.sqrMagnitude>world.AimedRange(unit)*world.AimedRange(unit)||Vector2.Dot(FormationMath.Forward(s.Facings[soldier]),delta.normalized)<.8f)continue;
                    if(!ClearRevolverLane(soldier,victim))continue;
                    if(!mountedFireLibrary)mountedFireLibrary=Resources.Load<CavalryCrowdLibrary>("BakedCavalry/CavalryCrowdLibrary");
                    var fireClip=mountedFireLibrary?.Resolve(unit)?.Lods?[0].Clip(weapon=="pistol"?MountedMotion.PistolFire:MountedMotion.MountedFire);
                    Vector3 socket=fireClip?.MuzzleSockets?.Length>0?fireClip.Socket(0):new Vector3(.12f,2.145f,weapon=="pistol"?.68f:1.065f);
                    Vector3 origin=world.Terrain.SurfacePoint(s.Positions[soldier])+Quaternion.Euler(0,s.Facings[soldier]*Mathf.Rad2Deg,0)*socket;
                    Vector3 end=InfantryTargeting.AimPoint(world,victim);
                    if(!WeaponBallistics.TrySolve(origin,end,out var velocity,out float duration,world.AimedRange(unit))||WeaponBallistics.TraceGround(world.Terrain,origin,velocity,duration,out _))continue;
                    float distance=delta.magnitude;
                    float accuracy=WeaponBallistics.MusketHitChance(distance,world.EffectiveRange(unit),unit.Cohesion,unit.Fatigue,unit.Experience/Mathf.Max(1,unit.SoldierCount),target.Kind)*world.Precision(unit);
                    bool hit=random.NextDouble()<accuracy;
                    if(!hit){float spread=WeaponBallistics.Dispersion(distance,world.EffectiveRange(unit),.025f,.18f)*distance;end+=new Vector3((float)(random.NextDouble()-.5)*spread,0,(float)(random.NextDouble()-.5)*spread);}
                    s.RoundsRemaining[soldier]--;s.ReloadDurations[soldier]=world.BaseReload(unit)*(1+unit.Fatigue*.004f)*Mathf.Lerp(.94f,1.06f,RifleFireTiming.Variation(soldier,world.Config.Seed));s.ReloadReadyTimes[soldier]=world.Time+MountedFireDuration+s.ReloadDurations[soldier];
                    if(!world.Ignites(unit,soldier))continue;
                    s.FireTimes[soldier]=s.FireVolleys[soldier]=world.Time;s.FireOrigins[soldier]=origin;s.FireLockOrigins[soldier]=origin-Quaternion.Euler(0,s.Facings[soldier]*Mathf.Rad2Deg,0)*Vector3.forward*.35f;
                    s.FireDirections[soldier]=(end-origin).normalized;s.FireAimPoints[soldier]=end;s.FireTargetSoldiers[soldier]=victim;
                    if(unit.LastVolleyTime!=world.Time)unit.LastVolleySize=0;
                    unit.ShotsFired++;unit.LastVolleyTime=world.Time;unit.LastVolleySize++;
                    LaunchBullet(origin,end,WeaponBallistics.SmallArmsDamage(distance,world.EffectiveRange(unit),(float)random.NextDouble(),profile.Id,target.Kind),unit.Id,soldier,world.Time,true);
                }
                int ammunition=0;float reload=0;
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)if(s.Alive[i]){ammunition=Mathf.Max(ammunition,s.RoundsRemaining[i]);reload=Mathf.Max(reload,s.ReloadReadyTimes[i]-world.Time);}
                unit.Ammunition=ammunition;unit.ReloadRemaining=Mathf.Max(0,reload);
            }
        }
    }
}
