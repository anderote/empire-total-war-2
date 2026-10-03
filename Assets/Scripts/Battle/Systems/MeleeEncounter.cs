using UnityEngine;
namespace EmpireBattle
{
    // Repeatable encounters also used by native visual fixtures; never enabled in ordinary battles.
    public static class MeleeEncounter
    {
        public static bool IsInfantryCase(string scenario)=>scenario=="infantry"||scenario=="withdrawal";
        public static void Advance(BattleWorld w,string scenario)
        {if(scenario=="withdrawal"&&w.Time>=4&&!w.Battalions[1].Withdrawing)w.Withdraw(1);}
        public static BattleConfig Config(string scenario)=>new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=120,InitialStrengths=IsInfantryCase(scenario)?new[]{80,80}:new[]{60,120},EnableAI=false,EnableLandscape=false,FrenchArtillery=0,BritishArtillery=0,InitialSeparation=70};
        public static void Place(BattleWorld w,BattalionState u,Vector2 position,float facing)
        {
            u.Position=u.Destination=position;u.Facing=u.DesiredFacing=facing;u.Moving=false;u.Reforming=false;
            for(int slot=0;slot<u.AliveCount;slot++)
            {
                int i=w.SoldierAtSlot(u,slot);w.Soldiers.Positions[i]=position+FormationMath.SlotOffset(u,slot);w.Soldiers.Facings[i]=FormationMath.SlotFacing(u,slot);
                w.Soldiers.Velocities[i]=Vector2.zero;w.Soldiers.MovementSpeeds[i]=0;
            }
        }
        public static void Setup(BattleWorld w,string scenario)
        {
            var attacker=w.Battalions[0];var defender=w.Battalions[1];
            foreach(var u in w.Battalions){u.FireAtWill=false;u.Morale=100;u.Cohesion=1;}
            if(!IsInfantryCase(scenario)){attacker.Kind=UnitKind.Cavalry;attacker.Name="French Cavalry";attacker.Spacing=1.65f;attacker.Files=30;}
            // These contact fixtures exercise prepared bayonet combat.
            foreach(var u in w.Battalions)w.SetBayonets(u.Id,true);
            if(scenario=="square")w.SetFormation(defender.Id,FormationKind.Square);
            if(scenario=="skirmish")w.SetFormation(defender.Id,FormationKind.Skirmish);
            Place(w,defender,Vector2.zero,Mathf.PI);
            Place(w,attacker,scenario=="flank"?Vector2.left*35:Vector2.down*(IsInfantryCase(scenario)?9:35),scenario=="flank"?Mathf.PI*.5f:0);
            if(scenario=="routing"){defender.Routing=true;w.Withdraw(defender.Id);}
            w.IssueCharge(attacker.Id,defender.Id);
            if(IsInfantryCase(scenario))w.IssueCharge(defender.Id,attacker.Id);
        }
    }
}
