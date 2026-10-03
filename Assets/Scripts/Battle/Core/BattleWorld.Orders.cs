using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle
{
    public readonly struct BattleOrder
    {
        public readonly Vector2 Destination;
        public readonly float Facing;
        public readonly int Files, TargetId;
        public readonly bool Running, Trotting;
        public readonly InfantryAttackMode AttackMode;
        public bool IsAttack => AttackMode != InfantryAttackMode.None;
        public BattleOrder(Vector2 destination, float facing, int files, bool running, bool trotting,
            int targetId = -1, InfantryAttackMode attackMode = InfantryAttackMode.None)
        { Destination=destination; Facing=facing; Files=files; Running=running; Trotting=trotting; TargetId=targetId; AttackMode=attackMode; }
    }

    public sealed partial class BattleWorld
    {
        bool activatingQueuedOrder;
        internal void ClearPendingOrders(int id)
        { if(!activatingQueuedOrder)Battalions[id].PendingOrders.Clear(); }

        public bool OrderMove(int id, Vector2 destination, float facing, int files, bool running, bool append)
        {
            if(!Valid(id)||!Finite(destination.x)||!Finite(destination.y)||!Finite(facing))return false;
            var unit=Battalions[id];
            if(unit.Routing||unit.AliveCount<=0)return false;
            if(files>0)files=FormationMath.CanResizeFrontage(unit)?FormationMath.ClampFrontage(unit,files):unit.Files;
            destination=ClampDestination(id,destination,files>0?files:append?PlannedOrderFiles(unit):unit.Files,facing);
            return SubmitOrder(unit,new BattleOrder(destination,facing,files,running,unit.Trotting&&!running),append);
        }

        public bool OrderAttack(int id, int targetId, bool melee, bool running, bool append)
        {
            if(!Valid(id)||!Valid(targetId)||DeploymentActive)return false;
            var unit=Battalions[id];var target=Battalions[targetId];
            if(unit.Routing||unit.AliveCount<=0||target.AliveCount<=0||target.Side==unit.Side)return false;
            melee=melee||unit.MeleeMode||!UnitRoster.HasFirearm(unit);
            return SubmitOrder(unit,new BattleOrder(target.Position,0,0,running||melee,unit.Trotting&&!running&&!melee,
                targetId,melee?InfantryAttackMode.BayonetCharge:InfantryAttackMode.FirePosition),append);
        }

        bool SubmitOrder(BattalionState unit, BattleOrder order, bool append)
        {
            if(append&&!DeploymentActive&&!unit.Withdrawing&&(unit.Moving||unit.AttackMode!=InfantryAttackMode.None||unit.PendingOrders.Count>0
                ||Mathf.Abs(FormationMath.AngleDifference(unit.Facing,unit.DesiredFacing))>.03f))
            { unit.PendingOrders.Add(order);return true; }
            unit.PendingOrders.Clear();
            return ActivateOrder(unit,order);
        }

        bool ActivateOrder(BattalionState unit, BattleOrder order)
        {
            activatingQueuedOrder=true;
            try
            {
                if(order.IsAttack)
                {
                    bool accepted=order.AttackMode==InfantryAttackMode.BayonetCharge
                        ?IssueCharge(unit.Id,order.TargetId):IssueRangedAttack(unit.Id,order.TargetId);
                    if(!accepted)return false;
                }
                else
                {
                    if(order.Files>0)SetFrontage(unit.Id,order.Files);
                    IssueMove(unit.Id,order.Destination,order.Facing);
                }
                unit.Running=order.Running;unit.Trotting=order.Trotting;
                return true;
            }
            finally { activatingQueuedOrder=false; }
        }

        void UpdateQueuedOrders()
        {
            foreach(var unit in Battalions)
            {
                if(unit.Routing||unit.Withdrawing||unit.AliveCount<=0){unit.PendingOrders.Clear();continue;}
                if(unit.PendingOrders.Count==0)continue;
                if(unit.AttackMode!=InfantryAttackMode.None)
                {
                    if(Valid(unit.AttackTargetId)&&Battalions[unit.AttackTargetId].AliveCount>0)continue;
                    // Finish an explicit target before automatic retargeting can steal the queue.
                    activatingQueuedOrder=true;
                    try { Halt(unit.Id); } finally { activatingQueuedOrder=false; }
                }
                if(unit.Moving||Mathf.Abs(FormationMath.AngleDifference(unit.Facing,unit.DesiredFacing))>.03f)continue;
                while(unit.PendingOrders.Count>0)
                {
                    var order=unit.PendingOrders[0];
                    if(order.AttackMode==InfantryAttackMode.BayonetCharge&&unit.Kind==UnitKind.Cavalry
                        &&unit.ChargePhase==ChargePhase.Recovering&&Time<unit.ChargeRecoveryUntil
                        &&Valid(order.TargetId)&&Battalions[order.TargetId].AliveCount>0)break;
                    unit.PendingOrders.RemoveAt(0);
                    // Dead targets are skipped without losing the remaining route.
                    if(ActivateOrder(unit,order))break;
                }
            }
        }

        public int PlannedOrderFiles(BattalionState unit)
        {
            int files=unit.Files;
            foreach(var order in unit.PendingOrders)if(!order.IsAttack&&order.Files>0)files=order.Files;
            return Mathf.Clamp(files,1,Mathf.Max(1,unit.AliveCount));
        }

        public Vector2 PlannedOrderPosition(BattalionState unit)
        {
            Vector2 end=unit.Moving?unit.Destination:unit.Position;
            if(unit.AttackMode!=InfantryAttackMode.None&&Valid(unit.AttackTargetId))end=Battalions[unit.AttackTargetId].Position;
            foreach(var order in unit.PendingOrders)
                if(!order.IsAttack)end=order.Destination;
                else if(Valid(order.TargetId)&&Battalions[order.TargetId].AliveCount>0)end=Battalions[order.TargetId].Position;
            return end;
        }

        // Both terrain and map previews consume the same ordered sequence.
        public IEnumerable<BattleOrder> PreviewOrders(BattalionState unit)
        {
            if(unit.Routing||unit.AliveCount<=0)yield break;
            if(unit.AttackMode!=InfantryAttackMode.None&&Valid(unit.AttackTargetId)&&Battalions[unit.AttackTargetId].AliveCount>0)
                yield return new BattleOrder(Battalions[unit.AttackTargetId].Position,0,0,unit.Running,unit.Trotting,unit.AttackTargetId,unit.AttackMode);
            else if(unit.Moving||Mathf.Abs(FormationMath.AngleDifference(unit.Facing,unit.DesiredFacing))>.03f)
                yield return new BattleOrder(unit.Destination,unit.DesiredFacing,unit.Files,unit.Running,unit.Trotting);
            int files=unit.Files;
            foreach(var order in unit.PendingOrders)
            {
                if(order.IsAttack)
                {
                    if(Valid(order.TargetId)&&Battalions[order.TargetId].AliveCount>0)
                        yield return new BattleOrder(Battalions[order.TargetId].Position,0,0,order.Running,order.Trotting,order.TargetId,order.AttackMode);
                }
                else
                {
                    if(order.Files>0)files=order.Files;
                    yield return new BattleOrder(order.Destination,order.Facing,files,order.Running,order.Trotting);
                }
            }
        }
    }
}
