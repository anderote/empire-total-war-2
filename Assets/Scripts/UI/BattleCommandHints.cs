namespace Empire.UI
{
    public static class BattleCommandHints
    {
        public static string For(string label)
        {
            switch(label)
            {
                case "LINE":return "Spread into a firing line. A wide frontage brings more muskets to bear.";
                case "COLUMN":return "Form a compact marching column. Easier to maneuver, with fewer muskets facing forward.";
                case "SKIRMISH":return "Adopt dispersed formation with wider spacing. Available to cavalry and infantry with light infantry doctrine. Drag to set frontage and facing while keeping open order.";
                case "DIAMOND":return "Form cavalry into a pointed diamond for charges. Charge orders select this formation automatically; dragging preserves its shape.";
                case "SQUARE":return "Form an all-round defence against cavalry. Requires square doctrine; movement and fire are constrained.";
                case "FIRE":return "Allow or hold fire using the selected firing mode. Equipped grenades also require firing to be enabled.";
                case "FIRE MODE ▾":return "Choose between rapid individual fire, balanced platoon fire and volleys with the strongest morale shock. Bullet damage and accuracy are unchanged by the mode.";
                case "MIXED ▾":return "Selected regiments use different fire modes. Choose a mode to apply it to the selection. Squares and skirmishers always fire independently.";
                case "AT WILL ▾":
                case "FIRE AT WILL":return "Fire at will: highest sustained firing rate, lowest morale shock per casualty. Soldiers fire as soon as their weapon and lane are ready, without waiting for a coordinated order. Default for light infantry; also used in skirmish and square formations.";
                case "PLATOONS ▾":
                case "FIRE BY PLATOONS":return "Platoon fire: balances firing rate and morale shock (1.5× morale loss per casualty). Small groups fire across the front rank, kneel, then repeat through the rear ranks. Default for line infantry. Squares and skirmishers still fire independently.";
                case "VOLLEY ▾":
                case "VOLLEY":return "Volley: strongest morale shock (2.5× morale loss per casualty), lowest sustained firing rate. Ready muskets with clear lanes fire together, then reload and reform for the next burst. Default for militia. Squares and skirmishers still fire independently.";
                case "RUN":return "Toggle running for selected regiments. Faster movement consumes more stamina. Shortcut: R.";
                case "HALT":return "Stop selected regiments and artillery, clear queued orders and cancel battery targets.";
                case "CHARGE":return "Order a melee charge at the selected target. Hold Shift to queue it after movement; hold Space to preview the full queue in terrain or map view.";
                case "MELEE · F":return "Toggle melee mode. Right-click an enemy with the sword cursor to charge instead of firing. F toggles mode; C charges the current or nearest enemy.";
                case "PURSUE":return "Follow an assigned enemy into range. Ranged units keep shooting; switch to melee mode or order a charge to hunt fleeing soldiers in close combat.";
                case "WITHDRAW":return "Order selected regiments to retreat from the engagement.";
                case "FIX BAYONETS":return "Fix bayonets for stronger melee attacks at the cost of slower reloads.";
                case "UNFIX BAYONETS":return "Remove fixed bayonets to restore normal reload speed.";
                case "RANK −":return "Reduce formation depth and widen the frontage.";
                case "RANK +":return "Increase formation depth and narrow the frontage.";
                case "REGIMENTS":return "Inspect soldier kit, base stats and abilities. Available purchases depend on the current game mode.";
                case "PAUSE":return "Pause battle simulation; camera and orders remain available.";
                case "RESUME":return "Resume the battle at the selected speed.";
                case "CINEMA":return "Toggle cinematic lighting and post-processing.";
                case "FOCUS":return "Toggle cinematic depth of field.";
                case "MENU":return "Open battle navigation, shared options and regiment management.";
                default:return label.EndsWith("×")?"Set battle playback speed to "+label:"";
            }
        }
    }
}
