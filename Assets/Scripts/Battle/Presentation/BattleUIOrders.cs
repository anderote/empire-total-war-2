using System;
using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleView
    {
        // Presentation access; all orders still pass through the simulation's capability checks.
        public ArtilleryGun[] CommandGuns => commandArtillery?.Guns ?? Array.Empty<ArtilleryGun>();
        public int SelectedRegimentCount { get {int count=0;if(selected!=null)foreach(bool picked in selected)if(picked)count++;return count;} }
        public Rect BatteryOrdersRect => GunPanelRect;
        public bool IsGunSelected(int id) => id>=0&&id<selectedGuns.Length&&selectedGuns[id];
        public void FocusGun(int id)
        {
            if(commandArtillery==null||id<0||id>=commandArtillery.Guns.Length)return;
            SetInspectionView(commandArtillery.Guns[id].Position,160,55);
        }
        public void OrderFireMode(InfantryFireMode mode)=>ForSelected(b=>world.SetFireMode(b.Id,mode));
        public void OrderCycleFireMode() => ForSelected(b=>world.SetFireMode(b.Id,world.NextFireMode(b)));
        public InfantryFireMode? SelectedFireMode
        {
            get
            {
                InfantryFireMode? result=null;
                foreach(var unit in world.Battalions)
                {
                    if(!selected[unit.Id]||unit.Routing||unit.AliveCount<=0||!UnitRoster.HasFirearm(unit))continue;
                    // Squares and skirmishers fire independently regardless of their saved line drill.
                    var mode=RifleFireTiming.EffectiveMode(unit);
                    if(result.HasValue&&result.Value!=mode)return null;
                    result=mode;
                }
                return result;
            }
        }
        public bool SelectedMeleeMode
        {
            get
            {
                bool any=false;
                foreach(var unit in world.Battalions)if(selected[unit.Id]&&!unit.Routing&&unit.AliveCount>0)
                {any=true;if(!unit.MeleeMode)return false;}
                return any;
            }
        }
        public void OrderToggleMelee()
        {
            bool enabled=!SelectedMeleeMode;
            ForSelected(unit=>world.SetMeleeMode(unit.Id,enabled));
            status=enabled?"Melee mode: right-click an enemy to charge. F switches back to fire.":"Melee mode off: firearm troops resume fire.";
        }
        public bool UsesMeleeAttack(BattalionState unit,int targetId,bool forceMelee=false) =>
            forceMelee||unit.MeleeMode||!UnitRoster.HasFirearm(unit);
    }
}
