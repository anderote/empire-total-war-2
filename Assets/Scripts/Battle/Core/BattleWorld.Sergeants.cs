using UnityEngine;
namespace EmpireBattle
{
    public sealed partial class BattleWorld
    {
        readonly int[] sergeantIds=new int[8];
        void InitializeSergeants(BattalionState unit)
        {
            if(!UnitRoster.HasCommandParty(unit))return;
            int count=Mathf.Min(8,Mathf.Min(FormationMath.EffectiveFiles(unit),Mathf.CeilToInt(unit.SoldierCount/32f)));
            for(int n=0;n<count;n++)
            {
                int slot=Mathf.FloorToInt((n+.5f)*FormationMath.EffectiveFiles(unit)/count);
                int soldier=SoldierAtSlot(unit,slot);
                Soldiers.PikeSergeants[soldier]=true;
                Soldiers.RoundsRemaining[soldier]=Soldiers.MagazineRounds[soldier]=0;
            }
        }
        // Reassign identities by swapping occupants; never teleport physical positions or create replacements.
        void PlaceSergeantsInFront(BattalionState unit)
        {
            int count=0;
            for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                if(Soldiers.Alive[i]&&Soldiers.PikeSergeants[i])sergeantIds[count++]=i;
            if(count==0)return;
            SortCommandSlots(sergeantIds,count);
            int front=0;
            for(int slot=0;slot<unit.AliveCount;slot++)
                if(IsSergeantFrontSlot(unit,slot))front++;
            int placed=Mathf.Min(count,front),ordinal=0,next=0;
            for(int slot=0;slot<unit.AliveCount&&next<placed;slot++)
            {
                if(!IsSergeantFrontSlot(unit,slot))continue;
                if(ordinal++!=Mathf.FloorToInt((next+.5f)*front/placed))continue;
                int soldier=sergeantIds[next++],old=Soldiers.FormationSlots[soldier];
                int displaced=formationOccupants[unit.SoldierStart+slot];
                formationOccupants[unit.SoldierStart+old]=displaced;
                if(displaced>=0)Soldiers.FormationSlots[displaced]=old;
                formationOccupants[unit.SoldierStart+slot]=soldier;Soldiers.FormationSlots[soldier]=slot;
            }
        }
        public static bool IsSergeantFrontSlot(BattalionState unit,int slot)=>unit.Formation==FormationKind.Square
            ?FormationMath.IsBayonetWall(unit,slot):slot<FormationMath.EffectiveFiles(unit);
        // The general assignment already follows physical positions. Preserve that
        // order when pinning command figures, especially after the facing reverses.
        void SortCommandSlots(int[] ids,int count)
        {
            for(int i=1;i<count;i++)
            {
                int soldier=ids[i],j=i;
                while(j>0&&Soldiers.FormationSlots[ids[j-1]]>Soldiers.FormationSlots[soldier])
                {ids[j]=ids[j-1];j--;}
                ids[j]=soldier;
            }
        }
        public int LivingSergeants(BattalionState unit)
        {
            int count=0;for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                if(Soldiers.Alive[i]&&Soldiers.PikeSergeants[i])count++;
            return count;
        }
    }
}
