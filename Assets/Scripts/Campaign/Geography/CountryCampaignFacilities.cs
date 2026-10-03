using System;
namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        RegionBuilding Facility(string region,int slot)
        {var state=World.State(region);return state!=null&&slot>=0&&slot<state.buildings.Length?state.buildings[slot]:null;}
        public int FacilityRaidUses(string region,int slot)
        {var b=Facility(region,slot);return CampaignBuildingTiers.IsOperational(b)?CampaignBuildingTiers.RaidDurability(b.level)-b.raidDamage:0;}
        public bool CanDamageFacility(string region,int slot,int uses,int liability,out string reason)
        {
            var b=Facility(region,slot);reason="Choose an operational source with sufficient remaining raid uses.";
            if(uses<1||FacilityRaidUses(region,slot)<uses)return false;
            // The procurement transaction computes liability from each saved marked-up purchase price.
            // Reject overflow rather than clamping away a debt that the repair must eventually pay.
            if(liability<uses||(long)b.raidRepairLiability+liability>1000000000){reason="Invalid raid repair liability.";return false;}
            reason="";return true;
        }
        public bool DamageFacility(string region,int slot,int uses,int liability,out string reason)
        {
            if(!CanDamageFacility(region,slot,uses,liability,out reason))return false;
            var b=Facility(region,slot);b.raidDamage+=uses;b.raidRepairLiability+=liability;
            reason=FacilityRaidUses(region,slot)==0?b.kind+" ruined; production and unlocks lost until fully repaired.":b.kind+" damaged; "+FacilityRaidUses(region,slot)+" raid uses remain.";return true;
        }
        public int RepairCost(string region,int slot)
        {
            var b=Facility(region,slot);var tier=b==null?null:CampaignBuildingTiers.Get(b.kind,b.level);
            return tier==null||b.raidDamage==0?0:Math.Max(b.raidRepairLiability,(int)Math.Ceiling(tier.Cost*b.raidDamage/(double)CampaignBuildingTiers.RaidDurability(b.level)));
        }
        public int RepairMaterials(string region,int slot)
        {
            var b=Facility(region,slot);var tier=b==null?null:CampaignBuildingTiers.Get(b.kind,b.level);
            return tier==null||b.raidDamage==0?0:Math.Max(1,(int)Math.Ceiling(tier.Work*Balance.constructionMaterials*b.raidDamage/(double)CampaignBuildingTiers.RaidDurability(b.level)));
        }
        public bool CanRepairFacility(string actor,string region,int slot,out string reason)
        {
            reason="Repairs require an owned province during your turn.";if(!Owns(actor,region))return false;
            var b=Facility(region,slot);if(b==null||CampaignBuildingTiers.Get(b.kind,b.level)==null||b.raidDamage==0){reason="No raid damage to repair.";return false;}
            if(b.turnsRemaining>0||Besieged(region)||InRevolt(region)){reason="Repairs require a secure province and no construction in this slot.";return false;}
            int cost=RepairCost(region,slot),materials=RepairMaterials(region,slot);
            if(World.FactionState(actor).treasury<cost||World.State(region).economy.materials<materials){reason="Full repair needs "+cost+" treasury and "+materials+" local materials.";return false;}
            reason="Full repair restores the facility and settles all raid liability.";return true;
        }
        public bool RepairFacility(string actor,string region,int slot,out string reason)
        {
            if(!CanRepairFacility(actor,region,slot,out reason))return false;
            var b=Facility(region,slot);World.FactionState(actor).treasury-=RepairCost(region,slot);World.State(region).economy.materials-=RepairMaterials(region,slot);
            b.raidDamage=0;b.raidRepairLiability=0;reason=b.kind+" fully repaired; local output and unlocks restored.";return true;
        }
    }
}
