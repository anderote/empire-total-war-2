using UnityEngine;

namespace EmpireBattle
{
    // Musicians and the colonel are decorative. Colours use stable existing soldier
    // identities, so a bearer casualty can leave its physical standard on the field.
    public static class CommandPartyLayout
    {
        public static int StandardBearer(BattleWorld world,BattalionState unit,int member)
        {
            if(world.Config.AssetReview||!UnitRoster.HasCommandParty(unit)||member<0||member>=StandardCount(unit))return -1;
            int ordinal=0;
            for(int id=unit.SoldierStart+unit.SoldierCount-1;id>=unit.SoldierStart;id--)
                if(!world.Soldiers.Captains[id]&&!world.Soldiers.PikeSergeants[id]&&ordinal++==member)return id;
            return -1;
        }
        public static int StandardMember(BattleWorld world,BattalionState unit,int soldier)
        {
            for(int member=0;member<StandardCount(unit);member++)if(StandardBearer(world,unit,member)==soldier)return member;
            return -1;
        }
        public static int StandardCount(BattalionState unit)=>UnitRoster.ArtFaction(unit.Faction)=="britain"&&unit.Role!=UnitRole.Militia?2:1;
        public static int Platoons(BattalionState unit,int platoonFiles)
        {
            if(!UnitRoster.HasCommandParty(unit)||unit.AliveCount<8)return 0;
            if(unit.Formation==FormationKind.Skirmish)return 1;
            int count=Mathf.CeilToInt(FormationMath.EffectiveFiles(unit)/(float)Mathf.Max(1,platoonFiles));
            if(unit.Formation==FormationKind.Column)
                count=Mathf.CeilToInt(unit.AliveCount/(float)(Mathf.Max(1,platoonFiles)*4));
            return Mathf.Min(8,Mathf.Min(count,Mathf.Max(1,unit.AliveCount/32)));
        }
        public static int Count(int role,int platoons)=>role==4?(platoons>0?1:0):role==3||role==0?0:role==2?platoons:(platoons+1)/2;
        public static int Count(BattalionState unit,int role,int platoons)
        {
            if(!UnitRoster.HasCommandParty(unit))return 0;
            // Colours belong to the battalion, rather than multiplying with every platoon.
            if(role==2)return StandardCount(unit);
            if(platoons<=0)return 0;
            return Count(role,platoons);
        }
        public static Vector2 Offset(BattalionState unit,int role,int index,int platoons)
        {
            Vector2 half=FormationMath.HalfExtents(unit);
            int count=Mathf.Max(1,Count(unit,role,platoons));
            float interior=Mathf.Max(.3f,half.y-FormationMath.SquareRanks(unit)*unit.Spacing-.7f);
            if(role==4)return unit.Formation==FormationKind.Square?new Vector2(0,Mathf.Min(2.8f,interior)):new Vector2(-half.x-3.5f,half.y-1.5f);
            if(role==3){
                int front=0;
                for(int slot=0;slot<unit.AliveCount;slot++)if(BattleWorld.IsSergeantFrontSlot(unit,slot))front++;
                int target=Mathf.FloorToInt((index+.5f)*front/Mathf.Max(1,Mathf.Min(8,front))),ordinal=0;
                for(int slot=0;slot<unit.AliveCount;slot++)if(BattleWorld.IsSergeantFrontSlot(unit,slot)&&ordinal++==target){
                    Vector2 position=FormationMath.SlotOffset(unit,slot);
                    return new Vector2(Vector2.Dot(position,FormationMath.Right(unit.Facing)),Vector2.Dot(position,FormationMath.Forward(unit.Facing)));
                }
                return Vector2.zero;
            }
            if(unit.Formation==FormationKind.Square){
                // Keep the command party safely inside the bayonet perimeter.
                float width=Mathf.Max(.6f,half.x-FormationMath.SquareRanks(unit)*unit.Spacing-1.2f);
                return new Vector2(((index+.5f)/count*2-1)*width,role==0?Mathf.Min(1.15f,interior):role==1?0:-Mathf.Min(1.15f,interior));
            }
            if(unit.Formation==FormationKind.Column)
                return new Vector2(role==0?-half.x-1.2f:half.x+(role==2?1.2f:2.5f),((index+.5f)/count*2-1)*half.y);
            // Standards mark platoon centres behind the last rank; musicians stand another pace back.
            float x=((index+.5f)/count*2-1)*half.x;
            if(role==0)return new Vector2(x,-half.y-.15f);
            return new Vector2(x,-half.y-(role==2?1.35f:2.65f));
        }
    }
}
