using System;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleWorld
    {
        public bool DeploymentActive { get; private set; }
        public string DeploymentMessage { get; private set; } = "";
        public BattleSiege Siege { get; private set; }

        void InitializeRegionalDeployment()
        {
            DeploymentActive=Config.RequireDeployment;
            if(Terrain.Profile!=null&&Terrain.Profile.Settlement)Siege=new BattleSiege(this);
            if(!Config.RegionalMap)return;
            if(Config.RegimentalDeployment){InitializeCompactDeployment();return;}
            var original=new Vector2[Battalions.Length];
            for(int i=0;i<Battalions.Length;i++){original[i]=Battalions[i].Position;Battalions[i].Position=new Vector2(100000,100000);}
            for(int i=0;i<Battalions.Length;i++)
            {
                var b=Battalions[i];if(b.InReserve)continue;
                if(DeploymentAllowed(b,original[i],b.Facing,out _)){SnapFormation(b,original[i],b.Facing);continue;}
                Bounds area=DeploymentArea(b.Side);Vector2 half=FormationMath.HalfExtents(b);
                float dx=half.x*2+12,dz=Mathf.Max(24,half.y*2+12);bool placed=false;
                int columns=Mathf.Max(1,Mathf.FloorToInt((area.size.x-6)/dx));
                int rows=Mathf.Max(1,Mathf.FloorToInt((area.size.z-6)/dz));
                for(int row=0;row<rows&&!placed;row++)for(int col=0;col<columns&&!placed;col++)
                {
                    var point=new Vector2((col-(columns-1)*.5f)*dx+area.center.x,area.center.z+(row-(rows-1)*.5f)*dz);
                    if(!DeploymentAllowed(b,point,b.Facing,out _))continue;
                    SnapFormation(b,point,b.Facing);placed=true;
                }
                if(!placed)throw new ArgumentException("Army does not fit its deployment area: "+b.Name);
            }
        }
        void InitializeCompactDeployment()
        {
            // Hide unplaced units from overlap checks, then pack each army from its
            // front backwards. Actual footprints include loose skirmishers and horses.
            foreach(var b in Battalions)b.Position=new Vector2(100000,100000);
            foreach(ArmySide side in new[]{ArmySide.French,ArmySide.British})
            {
                Bounds area=DeploymentArea(side);float dz=14,totalWidth=0;
                foreach(var b in Battalions)if(b.Side==side)
                {var half=FormationMath.HalfExtents(b);dz=Mathf.Max(dz,half.y*2+6);totalWidth+=half.x*2+6;}
                float left=area.min.x+3,right=area.max.x-3;
                float x=totalWidth<right-left?area.center.x-totalWidth*.5f:left;
                int row=0,rows=Mathf.Max(1,Mathf.FloorToInt((area.size.z-6)/dz));
                foreach(var b in Battalions)if(b.Side==side)
                {
                    float width=FormationMath.HalfExtents(b).x*2+6;bool placed=false;
                    while(row<rows)
                    {
                        if(x+width>right){row++;x=left;continue;}
                        float z=side==ArmySide.French?area.max.z-3-dz*(row+.5f):area.min.z+3+dz*(row+.5f);
                        var point=new Vector2(x+width*.5f,z);x+=width;
                        if(!DeploymentAllowed(b,point,b.Facing,out _))continue;
                        SnapFormation(b,point,b.Facing);placed=true;break;
                    }
                    if(!placed)throw new ArgumentException("Army does not fit its compact deployment area: "+b.Name);
                }
            }
        }
        public Bounds DeploymentArea(ArmySide side)
        {
            float half=Config.FieldHalfSize;var map=Terrain.Profile;
            if(map!=null&&map.Settlement&&side==map.DefenderSide)
                return new Bounds(new Vector3(0,0,map.SettlementCenter.y),new Vector3(map.SettlementRadius*1.60f,1,map.SettlementRadius*1.60f));
            float left=map!=null&&map.Coast?-half*.70f:-half+20;
            float right=half-20,front=Config.RegimentalDeployment?Config.InitialSeparation*.5f:100;
            if(map!=null&&map.Settlement)front=Mathf.Max(front,Mathf.Abs(map.Gate.y)+90);
            front=Mathf.Min(front,half*.65f);
            float near=side==ArmySide.French?-front:front,far=side==ArmySide.French?-half+20:half-20;
            return new Bounds(new Vector3((left+right)*.5f,0,(near+far)*.5f),new Vector3(right-left,1,Mathf.Abs(far-near)));
        }
        bool DeploymentAllowed(BattalionState b,Vector2 position,float facing,out string reason)
        {
            reason="Position is outside the deployment area or overlaps an obstacle.";
            if(!Finite(position.x)||!Finite(position.y)||!Finite(facing))return false;
            Bounds area=DeploymentArea(b.Side);
            for(int slot=0;slot<b.AliveCount;slot++)
            {
                Vector2 point=position+FormationMath.SlotOffset(b,slot,b.Files,facing);
                if(point.x<area.min.x||point.x>area.max.x||point.y<area.min.z||point.y>area.max.z||Terrain.IsBlocked(point,.8f))return false;
            }
            foreach(var other in Battalions)
            {
                if(other==b||other.AliveCount==0||other.Position.x>90000)continue;
                if(Overlap(b,position,facing,other,other.Position,other.Facing)>0)
                {reason="This formation overlaps another regiment.";return false;}
            }
            reason="Formation deployed.";return true;
        }
        void SnapFormation(BattalionState b,Vector2 position,float facing)
        {
            b.Position=b.Destination=position;b.Facing=b.DesiredFacing=facing;b.Moving=b.Reforming=false;
            b.FormationReadyTime=0;b.FormationError=0;
            for(int slot=0;slot<b.AliveCount;slot++)
            {
                int soldier=livingSlots[b.Id][slot];
                Soldiers.Positions[soldier]=position+FormationMath.SlotOffset(b,slot);
                Soldiers.Facings[soldier]=facing;Soldiers.Velocities[soldier]=Vector2.zero;Soldiers.MovementSpeeds[soldier]=0;
            }
        }
        public bool TryDeploy(int id,Vector2 position,float facing,out string reason)
        {
            reason="Deployment has ended or this regiment is not yours.";
            if(!DeploymentActive||!Valid(id)||Battalions[id].InReserve||Battalions[id].Side!=ArmySide.French)return false;
            var b=Battalions[id];
            if(!DeploymentAllowed(b,position,facing,out reason)){DeploymentMessage=reason;return false;}
            SnapFormation(b,position,facing);DeploymentMessage=reason;return true;
        }
        public bool BeginBattle(out string reason)
        {
            reason="Battle has already begun.";if(!DeploymentActive)return false;
            foreach(var b in Battalions)if(!b.InReserve&&!DeploymentAllowed(b,b.Position,b.Facing,out reason))
            {DeploymentMessage=b.Name+": "+reason;return false;}
            foreach(var b in Battalions)if(!b.InReserve)SnapFormation(b,b.Position,b.Facing);
            DeploymentActive=false;DeploymentMessage="Battle begun.";reason=DeploymentMessage;return true;
        }
    }
}
