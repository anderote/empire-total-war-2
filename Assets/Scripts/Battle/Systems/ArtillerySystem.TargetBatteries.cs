using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class ArtillerySystem
    {
        public int BatteryAt(ArmySide firingSide,Vector2 point)
        {
            int result=-1;float nearest=6f*6f;
            foreach(var target in Guns)
            {
                if(target.Side==firingSide||!target.Operational)continue;
                float distance=(target.Position-point).sqrMagnitude;
                if(distance<=nearest){nearest=distance;result=target.UnitId;}
            }
            return result;
        }

        public bool OrderBattery(int gunId,int battery)
        {
            if(gunId<0||gunId>=Guns.Length||!Guns[gunId].Operational)return false;
            var gun=Guns[gunId];bool valid=false;
            foreach(var target in Guns)if(target.UnitId==battery&&target.Side!=gun.Side&&target.Operational){valid=true;break;}
            if(!valid)return false;
            StopGunMovement(gun);
            gun.TargetBattery=battery;gun.TargetBattalion=-1;gun.HasGroundTarget=false;gun.FireAtWill=true;
            return true;
        }

        int NearestEnemyBattery(ArtilleryGun gun,float distance)
        {
            int battery=-1;float best=distance*distance;
            foreach(var target in Guns)
            {
                if(target.Side==gun.Side||!target.Operational)continue;
                float square=(target.Position-gun.Position).sqrMagnitude;
                if(square>=64f&&square<=best){best=square;battery=target.UnitId;}
            }
            return battery;
        }

        bool BatteryAimPoint(ArtilleryGun gun,int battery,out Vector2 point)
        {
            point=default;int count=0,slot=0;
            float range=EffectiveRange(gun);
            bool Eligible(ArtilleryGun target)
            {
                float distance=(target.Position-gun.Position).sqrMagnitude;
                return target.UnitId==battery&&target.Side!=gun.Side&&target.Operational&&distance>=64f&&distance<=range*range;
            }
            foreach(var target in Guns)if(Eligible(target))count++;
            if(count==0)return false;
            foreach(var member in Guns)
                if(member.Side==gun.Side&&member.UnitId==gun.UnitId&&member.Operational&&member.Id<gun.Id)slot++;
            // Stable coverage within a salvo; subsequent salvos rotate individual targets.
            int chosen=(slot+gun.ShotsFired)%count;
            foreach(var target in Guns)if(Eligible(target)&&chosen--==0)
            {
                Vector3 center=GunCenter(target);point=new Vector2(center.x,center.z);return true;
            }
            return false;
        }
    }
}
