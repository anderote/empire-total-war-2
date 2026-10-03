using UnityEngine;

namespace EmpireBattle
{
    // Gate assault and a contested town-square objective. No casualties are invented here.
    public sealed class BattleSiege
    {
        readonly BattleWorld world;
        public const float CaptureDuration=20f, CaptureRadius=70f;
        public float CaptureProgress { get; private set; }
        public bool Contested { get; private set; }
        public ArmySide Defender=>world.Terrain.Profile.DefenderSide;
        public ArmySide Attacker=>Defender==ArmySide.French?ArmySide.British:ArmySide.French;
        public BattleSiege(BattleWorld world) {this.world=world;}
        public void Step(float dt)
        {
            if(dt<=0||float.IsNaN(dt)||float.IsInfinity(dt)||world.DeploymentActive||world.Outcome!=BattleOutcome.Ongoing)return;
            var map=world.Terrain.Profile;int attack=0,defence=0,gateAssault=0;
            foreach(var unit in world.Battalions)
            {
                if(unit.Routing||unit.Withdrawing||unit.AliveCount<=0)continue;
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                {
                    if(!world.Soldiers.Alive[i]||world.Soldiers.IsKnockedDown(i,world.Time))continue;
                    Vector2 p=world.Soldiers.Positions[i];
                    if((p-map.SettlementCenter).sqrMagnitude<CaptureRadius*CaptureRadius)
                    {if(unit.Side==Attacker)attack++;else defence++;}
                    if(unit.Side==Attacker&&(p-map.Gate).sqrMagnitude<55*55)gateAssault++;
                }
            }
            if(!world.Terrain.GateOpen&&gateAssault>0)world.Terrain.DamageGate(dt*Mathf.Min(40,gateAssault)*1.5f);
            Contested=attack>0&&defence>0;
            if(attack>0&&defence==0)CaptureProgress=Mathf.Min(CaptureDuration,CaptureProgress+dt);
            else CaptureProgress=Mathf.Max(0,CaptureProgress-dt*2);
            if(CaptureProgress>=CaptureDuration)
            {world.Outcome=Attacker==ArmySide.French?BattleOutcome.FrenchVictory:BattleOutcome.BritishVictory;world.PursuitUntil=world.Time+20;}
        }
    }
}
