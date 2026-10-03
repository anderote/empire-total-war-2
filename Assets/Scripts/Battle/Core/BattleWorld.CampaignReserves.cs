using System;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleWorld
    {
        float nextCampaignWave;
        ArmySide nextReinforcementSide;
        readonly float[] blockedEntrySince={-1,-1};
        public int ReserveTroops(ArmySide side)
        {int count=0;foreach(var b in Battalions)if(b.Side==side&&b.InReserve)count+=b.SoldierCount;return count;}
        void InitializeCampaignReserves()
        {
            if(!Config.CampaignDeployment)return;
            if(Config.MaxActiveCampaignTroops<BattleConfig.MaxPersonnelPerRegiment*2||Config.MaxActiveCampaignTroops>20000)
                throw new ArgumentException("Campaign battlefield capacity must be between 1,000 and 20,000 troops.");
            if(Config.FormationArrivalSeconds!=null&&Config.FormationArrivalSeconds.Length!=Battalions.Length)
                throw new ArgumentException("Campaign reinforcement schedule does not match the roster.");
            int french=0,british=0,half=Config.MaxActiveCampaignTroops/2;
            foreach(var b in Battalions)
            {
                b.ArrivalTime=Config.FormationArrivalSeconds==null?0:Config.FormationArrivalSeconds[b.Id];
                if(!Finite(b.ArrivalTime)||b.ArrivalTime<0)throw new ArgumentException("Invalid reinforcement arrival time.");
                int current=b.Side==ArmySide.French?french:british;
                if(current+b.SoldierCount<=half&&b.ArrivalTime==0)
                {if(b.Side==ArmySide.French)french+=b.SoldierCount;else british+=b.SoldierCount;continue;}
                b.InReserve=true;b.HasEnteredBattle=false;b.AliveCount=0;b.ArrivalTime=Mathf.Max(20,b.ArrivalTime);b.Position=b.Destination=new Vector2(100000,100000);
                for(int i=b.SoldierStart;i<b.SoldierStart+b.SoldierCount;i++)Soldiers.Alive[i]=false;
            }
            nextCampaignWave=20;
        }
        void AdvanceCampaignReinforcements()
        {
            if(!Config.CampaignDeployment||Time<nextCampaignWave||Outcome!=BattleOutcome.Ongoing)return;
            nextCampaignWave=Time+8;
            int total=CountAlive(ArmySide.French)+CountAlive(ArmySide.British),arrived=0;
            // Alternate entry priority and reserve half the active capacity for each side while both have reserves.
            for(int pass=0;pass<2;pass++)
            {
                var side=pass==0?nextReinforcementSide:nextReinforcementSide==ArmySide.French?ArmySide.British:ArmySide.French;
                var other=side==ArmySide.French?ArmySide.British:ArmySide.French;
                int sideLimit=ReserveTroops(other)>0?Config.MaxActiveCampaignTroops/2:Config.MaxActiveCampaignTroops;
                bool eligible=false,entered=false;
                foreach(var b in Battalions)
                {
                    if(arrived>=4)break;
                    if(b.Side!=side||!b.InReserve||b.ArrivalTime>Time||total+b.SoldierCount>Config.MaxActiveCampaignTroops||CountAlive(side)+b.SoldierCount>sideLimit)continue;
                    eligible=true;if(!TryAdmitReserve(b))continue;entered=true;
                    total+=b.SoldierCount;arrived++;
                }
                int sideIndex=(int)side;
                if(CountAlive(side)>0||entered||!eligible)blockedEntrySince[sideIndex]=-1;
                else if(blockedEntrySince[sideIndex]<0)blockedEntrySince[sideIndex]=Time;
                else if(Time-blockedEntrySince[sideIndex]>=60)
                {
                    // A force with no troops on the field cannot hold a battle open indefinitely behind a blocked entrance.
                    foreach(var b in Battalions)if(b.Side==side&&b.InReserve){b.InReserve=false;b.EscapedCount=b.SoldierCount;b.Withdrawing=true;}
                }
            }
            nextReinforcementSide=nextReinforcementSide==ArmySide.French?ArmySide.British:ArmySide.French;
        }
        bool ReinforcementEntryAllowed(BattalionState b,Vector2 position)
        {
            for(int slot=0;slot<b.SoldierCount;slot++)
            {var p=position+FormationMath.SlotOffset(b,slot,b.Files,b.Facing);if(Mathf.Abs(p.x)>Config.FieldHalfSize-2||Mathf.Abs(p.y)>Config.FieldHalfSize-2||Terrain.IsBlocked(p,.8f))return false;}
            foreach(var other in Battalions)if(other!=b&&other.AliveCount>0&&Overlap(b,position,b.Facing,other,other.Position,other.Facing)>0)return false;
            return true;
        }
        bool TryAdmitReserve(BattalionState b)
        {
            float field=Config.FieldHalfSize;
            float left=Terrain.Profile!=null&&Terrain.Profile.Coast?-field*.7f:-field+20;
            var area=new Bounds(new Vector3((left+field-20)/2,0,0),new Vector3(field-20-left,1,field*2-40));bool placed=false;
            b.AliveCount=b.SoldierCount;var half=FormationMath.HalfExtents(b);
            // Enter from the rear deployment boundary with a clear physical footprint.
            for(int row=0;row<5&&!placed;row++)for(int column=0;column<24&&!placed;column++)
            {
                float x=Mathf.Lerp(area.min.x+half.x+4,area.max.x-half.x-4,column/23f);
                float z=b.Side==ArmySide.French?area.min.z+half.y+8+row*(half.y*2+8):area.max.z-half.y-8-row*(half.y*2+8);
                var p=new Vector2(x,z);if(!ReinforcementEntryAllowed(b,p))continue;
                bool enemyNear=false;foreach(var enemy in Battalions)if(enemy.Side!=b.Side&&enemy.AliveCount>0&&(enemy.Position-p).sqrMagnitude<180*180){enemyNear=true;break;}
                if(enemyNear)continue;SnapFormation(b,p,b.Facing);placed=true;
            }
            if(!placed){b.AliveCount=0;return false;}
            b.InReserve=false;b.HasEnteredBattle=true;
            for(int i=b.SoldierStart;i<b.SoldierStart+b.SoldierCount;i++)Soldiers.Alive[i]=true;
            return true;
        }
    }
}
