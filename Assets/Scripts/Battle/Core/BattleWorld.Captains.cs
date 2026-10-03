using UnityEngine;
namespace EmpireBattle
{
    public sealed partial class BattleWorld
    {
        readonly int[] captainIds=new int[4];
        void InitializeCaptains(BattalionState unit)
        {
            if(!UnitRoster.HasCommandParty(unit))return;
            int count=Mathf.Min(4,Mathf.CeilToInt(unit.SoldierCount/64f));
            int available=0;
            for(int slot=0;slot<unit.AliveCount;slot++)if(!Soldiers.PikeSergeants[SoldierAtSlot(unit,slot)])available++;
            count=Mathf.Min(count,available);
            // Prefer the first rank. Tiny formations may have every front slot occupied by pikes.
            int front=0;
            for(int slot=0;slot<FormationMath.EffectiveFiles(unit);slot++)if(!Soldiers.PikeSergeants[SoldierAtSlot(unit,slot)])front++;
            int limit=front>=count?FormationMath.EffectiveFiles(unit):unit.AliveCount;
            available=front>=count?front:available;
            int ordinal=0,next=0;
            for(int slot=0;slot<limit&&next<count;slot++)
            {
                int soldier=SoldierAtSlot(unit,slot);if(Soldiers.PikeSergeants[soldier])continue;
                if(ordinal++!=Mathf.FloorToInt((next+.5f)*available/count))continue;
                next++;Soldiers.Captains[soldier]=true;
                Soldiers.RoundsRemaining[soldier]=Soldiers.MagazineRounds[soldier]=0;
                Soldiers.SidearmRounds[soldier]=6;Soldiers.SidearmReserves[soldier]=12;
            }
        }
        void PlaceCaptainsNearFront(BattalionState unit)
        {
            int count=0;
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)if(Soldiers.Alive[i]&&Soldiers.Captains[i])captainIds[count++]=i;
            if(count==0)return;
            SortCommandSlots(captainIds,count);
            int available=0;
            for(int slot=0;slot<unit.AliveCount;slot++)if(CaptainSlot(unit,slot,true))available++;
            bool front=available>=count;
            if(!front){available=0;for(int slot=0;slot<unit.AliveCount;slot++)if(CaptainSlot(unit,slot,false))available++;}
            int ordinal=0,next=0;
            for(int slot=0;slot<unit.AliveCount&&next<count;slot++)
            {
                if(!CaptainSlot(unit,slot,front))continue;
                if(ordinal++!=Mathf.FloorToInt((next+.5f)*available/count))continue;
                int soldier=captainIds[next++],old=Soldiers.FormationSlots[soldier],displaced=SoldierAtSlot(unit,slot);
                formationOccupants[unit.SoldierStart+old]=displaced;if(displaced>=0)Soldiers.FormationSlots[displaced]=old;
                formationOccupants[unit.SoldierStart+slot]=soldier;Soldiers.FormationSlots[soldier]=slot;
            }
        }
        bool CaptainSlot(BattalionState unit,int slot,bool front)
        {
            int soldier=SoldierAtSlot(unit,slot);
            return soldier>=0&&!Soldiers.PikeSergeants[soldier]&&(!front||IsSergeantFrontSlot(unit,slot));
        }
        public int LivingCaptains(BattalionState unit)
        {
            int count=0;for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)if(Soldiers.Alive[i]&&Soldiers.Captains[i])count++;
            return count;
        }
        public int ApplyCaptainHit(int soldier,int target,float damage)
        {
            if(target<0||!Soldiers.Alive[target])return -1;
            var attacker=Battalions[Soldiers.BattalionIds[soldier]];var defender=Battalions[Soldiers.BattalionIds[target]];
            if(attacker.Side==defender.Side)return -1;
            bool killed=ApplyWound(target,damage,DeathCause.Musket,Terrain.SurfacePoint(Soldiers.Positions[target],1.2f),
                Terrain.SurfacePoint(Soldiers.Positions[target])-Terrain.SurfacePoint(Soldiers.Positions[soldier]));
            attacker.Hits++;attacker.Experience+=killed?6:1;Soldiers.Experience[soldier]+=killed?6:1;
            if(killed){attacker.Kills++;Soldiers.Kills[soldier]++;}
            return target;
        }
    }
}
