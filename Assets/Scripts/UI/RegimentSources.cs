using System;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;
using EmpireGame;
using UnityEngine;
using EmpireCampaign.Geography;

namespace Empire.UI
{
    public sealed class BattleRegimentSource : IRegimentSource,IRegimentOrganizationSource,IRegimentAppearanceSource,IRegimentAutoProgressionSource
    {
        readonly BattleArmorySession session;
        public BattleRegimentSource(BattleArmorySession value){session=value;}
        public string Context=>"Battle / Regiment management";
        public string Resources=>session.Save.treasury.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" funds  ·  equipment era "+session.Save.year;
        public string Guidance=>session.Message!=""?session.Message:session.CanChange?"Click a gear slot to compare upgrades. Check regiments for bulk gear orders.":"Battle in progress. Inspect kit and skills; purchases become available before the next battle.";
        public bool MultiSelect=>true;
        public bool CanArchive=>session.CanChange;
        public RegimentAction Import(string targetId,SavedRegiment saved)
        {
            var entry=session.Entries.Find(e=>e.id==targetId);bool enabled=session.CanImport(entry,saved,out var reason);
            return new RegimentAction {Title="Load into selected regiment",Cost=CommandUI.Number(saved.KitCost+saved.VeteranCost)+" funds",Enabled=enabled,Reason=reason,
                Description="Replace the selected regiment's name, role, XP, stats, abilities and equipment. Keep scenario troop strength. Saved copy and original are unchanged.",
                Execute=()=>session.Import(entry,saved)?session.Message:session.Message};
        }
        public IReadOnlyList<RegimentRecord> Units=>session.Entries.Select((e,i)=>
        {
            var b=i<session.World.Config.FrenchBattalionCount?session.World.Battalions[i]:null;
            return new RegimentRecord {Id=e.id,Faction=b?.Faction??session.World.Config.PlayerFaction,Name=session.Names[i],Role=e.users.ToString(),Users=e.users,BattleRole=b?.Role??UnitRole.Line,
                Strength=b?.AliveCount??session.Artillery.Guns.Count(g=>g.Side==ArmySide.French&&g.UnitId==i-session.World.Config.FrenchBattalionCount),
                Capacity=b?.SoldierCount??session.Artillery.Guns.Count(g=>g.Side==ArmySide.French&&g.UnitId==i-session.World.Config.FrenchBattalionCount),
                Tier=e.tier,Quality=e.quality,CurrentKills=session.CurrentKills(e),Progression=e.progression,Loadout=e.loadout,Weapon=b?.Weapon?.Id??"musket",
                Status=session.CanChange?"Pre-battle preparation. Equipment is issued immediately; purchases and training are saved.":"Deployed. Upgrades are locked until a fresh battle."};
        }).OrderBy(u=>session.Entries.Find(e=>e.id==u.Id).rosterOrder).ToArray();
        public string EntryId(int index)=>session.Entries[Mathf.Clamp(index,0,session.Entries.Count-1)].id;
        public bool CanOrganize=>session.CanChange;
        public bool CanDress=>session.CanChange;
        public string Dress(string id,string appearance)=>session.Dress(id,appearance);
        public string Rename(string id,string name)=>session.Rename(id,name);
        public string Move(string id,string beforeId)=>session.Move(id,beforeId);
        ArmoryEntry[] Entries(string[] ids)=>session.Entries.Where(e=>ids.Contains(e.id)).ToArray();
        public RegimentAction Equipment(string[] ids,EquipmentItem item)
        {
            var entries=Entries(ids);var targets=session.EquipmentTargets(entries,item);int cost=targets.Count*item.Cost;
            string reason=!session.CanChange?"Upgrades are available before battle.":item.EarliestYear>session.Save.year?"Available from "+item.EarliestYear+". Advance the equipment era in Quartermaster.":targets.Count==0?"Already equipped or incompatible with this selection.":session.Save.treasury<cost?"Requires "+CommandUI.Number(cost)+" funds for this selection.":"";
            return new RegimentAction {Title=entries.Length>1?"Equip "+targets.Count+" units":"Equip",Cost=CommandUI.Number(cost)+" funds",Enabled=reason=="",Reason=reason,
                Description="Immediate issue. Replaces this slot on "+targets.Count+" eligible units; no refund for replaced kit.",Execute=()=>session.EquipMany(Entries(ids),item.Id)?session.Message:"Order could not be completed."};
        }
        public RegimentAction Skill(string[] ids,UnitSkill skill)
        {
            var eligible=Entries(ids).Where(e=>BattleArmorySession.UsefulSkill(e,skill)&&e.progression.AvailablePoints>0&&e.progression.Level(skill)<UnitProgression.MaxSkill).ToArray();
            return new RegimentAction {Title="Allocate · 1 point",Enabled=session.CanChange&&eligible.Length>0,Reason=!session.CanChange?"Training is available before battle.":"Needs an unspent point, an applicable skill and a stat below its maximum.",
                Description=UnitProgression.SkillEffect(skill)+". Spends one stat point on each eligible selected regiment ("+eligible.Length+").",
                Execute=()=>{int trained=0;foreach(var e in eligible)if(session.Train(e,skill))trained++;return "Trained "+skill+" for "+trained+" regiments.";}};
        }
        public RegimentAction Ability(string[] ids,RegimentAbilityDefinition ability)
        {
            var targets=Entries(ids).Where(e=>ability.Supports(e.users)&&e.progression.AbilityRank(ability.Id)<RegimentAbilities.MaxRank&&e.progression.AvailableSkillPoints>0).ToArray();
            return new RegimentAction {Enabled=session.CanChange&&targets.Length>0,Reason=!session.CanChange?"Learn abilities before battle.":"Requires an unspent ability point and a compatible regiment.",Execute=()=>{int count=0;foreach(var e in targets)if(session.Learn(e,ability.Id))count++;return ability.Name+" learned by "+count+" regiments.";}};
        }
        public RegimentAction AutoApply(string id)
        {
            var entry=session.Entries.Find(e=>e.id==id);var unit=Units.FirstOrDefault(u=>u.Id==id);
            string reason=!session.CanChange?"Training is available before battle.":entry==null||unit==null?"Choose a regiment.":"No eligible stat or ability points are available.";
            var preview=entry==null||unit==null?null:RegimentAutoAllocation.Plan(entry.progression,unit.BattleRole,entry.users,s=>BattleArmorySession.UsefulSkill(entry,s));
            return new RegimentAction {Title="Auto apply available progression",Enabled=session.CanChange&&preview!=null&&preview.HasWork,Reason=session.CanChange&&preview!=null&&preview.HasWork?"":reason,
                Description="Spends eligible stat and ability points by troop role. Existing allocations are preserved; no points or progression are created.",
                Execute=()=>
                {
                    var current=session.Entries.Find(e=>e.id==id);var record=Units.FirstOrDefault(u=>u.Id==id);
                    if(!session.CanChange||current==null||record==null)return "Training is unavailable for this regiment.";
                    if(!session.ApplyAllocation(current,record.BattleRole))return "No eligible stat or ability points are available.";
                    return session.Message;
                }};
        }
        public IEnumerable<RegimentAction> Services(string[] ids)
        {
            int count=Entries(ids).Count(e=>e.quality<UnitProgression.MaxQuality),cost=count*60;
            yield return new RegimentAction {Title="Improve equipment quality",Cost=CommandUI.Number(cost)+" funds",Description="+4% quality accuracy / melee and 2% faster reload per grade. Improves "+count+" eligible units.",Enabled=session.CanChange&&count>0&&session.Save.treasury>=cost,Reason=count==0?"All selected units are already maximum equipment quality.":!session.CanChange?"Available before battle.":"Requires "+CommandUI.Number(cost)+" funds.",Execute=()=>session.ImproveQualityMany(Entries(ids))?session.Message:"Quality order unavailable."};
            yield return new RegimentAction {Title="Advance equipment era",Cost="Advance era",Description="Unlock later equipment for all standalone battles. The next era is permanent for this armory; existing gear is retained.",Enabled=session.CanChange&&session.Save.year<1918,Reason="Era changes require pre-battle preparation; 1918 is the final era.",Execute=()=>{session.AdvanceEra();return "Equipment era advanced to "+session.Save.year+".";}};
        }
    }
    public sealed class GloryRegimentSource : IRegimentSource,IRegimentOrganizationSource,IRegimentCompanySource,IRegimentAppearanceSource,IRegimentAutoProgressionSource
    {
        readonly GloryCompany company;readonly Func<bool> persist;
        public GloryRegimentSource(GloryCompany value,Func<bool> save){company=value;persist=save;}
        public string Context=>"Regimental Glory / Company";
        public string Resources=>company.Save.coins.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" coins  ·  "+CommandUI.Number(company.Save.commanderXP)+" command XP  ·  tier "+(company.UnlockedTier+1)+"  ·  all equipment available";
        public string Guidance=>"Company shortcuts: Ctrl / ⌘ + Shift + R replenish · U upgrade kit · E repair · Q quality · T train. Orders apply to all regiments.";
        public bool MultiSelect=>true;
        public bool CanArchive=>!company.Save.inBattle;
        public IEnumerable<RegimentAction> CompanyActions()
        {
            string[] names={"Replenish all","Upgrade all kit","Repair all","Quality all","Train all (+1)"};
            string[] descriptions={"Restore every regiment to full strength. Requires the full replacement cost.","Raise every issued kit item one level. Keeps existing equipment choices; skips maximum items. Issued guns advance to their next technology. Cost scales with each regiment’s capacity.","Restore all equipment condition to 100%. Battle wear reduces kit bonuses; repairs preserve upgrades and troop experience.","Raise equipment quality one grade on every eligible regiment. Cost scales with each regiment’s capacity.","Spend one available stat point on each regiment's lowest trained stat. Requires no coins; preserves ability points."};
            KeyCode[] keys={KeyCode.R,KeyCode.U,KeyCode.E,KeyCode.Q,KeyCode.T};
            for(int i=0;i<names.Length;i++)
            {
                var order=(GloryCompanyOrder)i;bool allowed=company.CompanyOrder(order,false,out int cost,out int count,out string reason);
                yield return new RegimentAction {Title=names[i],Cost=order==GloryCompanyOrder.Training?count+" stat points":CommandUI.Number(cost)+" coins",Description=descriptions[i],Enabled=allowed,Reason=reason,Hotkey=keys[i],
                    Execute=()=>{bool ok=company.CompanyOrder(order,true,out _,out _,out string result);return ok?Done(true,result):result;}};
            }
        }
        public RegimentAction Import(string targetId,SavedRegiment saved)
        {
            bool enabled=company.CanRecruitSaved(saved,out var reason);
            return new RegimentAction {Title="Recruit saved cadre",Cost=CommandUI.Number(company.SavedRecruitCost(saved))+" coins",Enabled=enabled,Reason=reason,
                Description="Adds a new full-strength 120-soldier regiment with the saved name, role, progression and equipment. Pays recruitment, equipment and veteran costs. Original remains unchanged.",
                Execute=()=>Done(company.RecruitSaved(saved),saved.name+" joined the company.")};
        }
        public IReadOnlyList<RegimentRecord> Units=>company.Save.regiments.Select(r=>new RegimentRecord {Id=r.id.ToString(),Name=string.IsNullOrEmpty(r.name)?"Regiment "+r.id:r.name,Tier=r.tier+1,Role=GloryCompany.TierNames[r.tier],Users=GloryCompany.Users(r),BattleRole=GloryCompany.Roles[r.tier],Strength=r.soldiers,Capacity=GloryCompany.UnitCapacity(r),StrengthLabel=GloryCompany.StrengthLabel(r),Quality=r.quality,Progression=r.progression,Loadout=r.loadout,Weapon=!string.IsNullOrEmpty(r.weapon)?r.weapon:GloryCompany.Roles[r.tier]==UnitRole.Rifleman?"rifle":"musket",Status=(company.Save.defeated?"Regrouping · ":"")+"Tier "+(r.tier+1)+" · Kit condition "+r.loadout.Condition+"% · "+company.Missing(r)+" replacements needed · "+GloryCompany.ReplacementCosts[r.tier]+" coins per replacement. Skills and XP survive replenishment."}).ToArray();
        public bool CanOrganize=>company.CanManage;
        public bool CanDress=>CanOrganize;
        public string Dress(string id,string appearance){var unit=company.Save.regiments.Find(u=>u.id.ToString()==id);if(!CanDress||unit==null)return "Change uniforms between battles.";unit.loadout.appearance=appearance;return Done(true,"Regiment uniform saved.");}
        public string Rename(string id,string name)
        {
            var unit=company.Save.regiments.Find(u=>u.id.ToString()==id);name=name?.Trim();
            if(!CanOrganize||unit==null||!SavedRegiment.SafeName(name,60))return "Enter a regiment name of 1–60 characters between battles.";
            unit.name=name;return Done(true,"Regiment renamed to “"+name+"”.");
        }
        public string Move(string id,string beforeId)=>Done(CanOrganize&&RegimentOrganization.Move(company.Save.regiments,u=>u.id.ToString(),id,beforeId),"Regiment order saved.");
        GloryRegiment Find(string[] ids)=>company.Save.regiments.Find(r=>ids.Contains(r.id.ToString()));
        string Done(bool success,string message)=>!success?"Order unavailable.":persist()?message:"Change applied, but saving failed. Please retry saving before leaving.";
        public RegimentAction Equipment(string[] ids,EquipmentItem item)
        {
            var candidates=company.Save.regiments.Where(r=>ids.Contains(r.id.ToString())&&item.Supports(GloryCompany.Users(r))&&UnitEquipment.CanReplace(r.loadout,item)).ToArray();
            string why="No selected regiment needs this item at this level.";
            bool allowed=candidates.Length>0;
            foreach(var r in candidates)if(!company.CanEquip(r,item,out why,false)){allowed=false;break;}
            int cost=candidates.Sum(r=>company.EquipmentCost(r,item));
            if(allowed&&company.Save.coins<cost){allowed=false;why="Requires "+CommandUI.Number(cost)+" coins for all eligible recipients.";}
            return new RegimentAction {Title="Equip "+candidates.Length+" regiments",Cost=CommandUI.Number(cost)+" coins",Enabled=allowed,Reason=why,
                Description="Issued to "+candidates.Length+" eligible recipients. Already equipped, higher levels and incompatible regiments are skipped. Cost scales with authorized regiment size, including empty places. Replaces this slot; no refund.",
                Execute=()=>
                {
                    // Revalidate the complete purchase before spending anything.
                    var fresh=Equipment(ids,item);if(!fresh.Enabled)return fresh.Reason;
                    foreach(var r in candidates)if(!company.Equip(r,item.Id))return "Equipment order failed.";
                    return Done(true,UnitEquipment.Label(item)+" issued to "+candidates.Length+" regiments.");
                }};
        }
        public RegimentAction Skill(string[] ids,UnitSkill skill)
        {
            var r=Find(ids);
            string reason=!company.CanManage?"Training requires an active company between battles.":r==null?"Choose a regiment.":!GloryCompany.UsefulSkill(r,skill)?"This training does not apply to this troop type.":r.progression.Level(skill)>=UnitProgression.MaxSkill?"Maximum training reached.":r.progression.AvailablePoints==0?"No stat points available. Gain experience to earn more.":"";
            return new RegimentAction {Title="Allocate · 1 point",Enabled=reason=="",Reason=reason,Description=UnitProgression.SkillEffect(skill)+". Uses one stat point, no coins.",Execute=()=>Done(company.Train(r,skill),skill+" trained.")};
        }
        public RegimentAction Ability(string[] ids,RegimentAbilityDefinition ability)
        {
            var r=Find(ids);
            string reason=!company.CanManage?"Learn abilities between battles in an active company.":r==null?"Choose a regiment.":!ability.Supports(GloryCompany.Users(r))?"This ability does not apply to this troop type.":r.progression.AbilityRank(ability.Id)>=RegimentAbilities.MaxRank?"Ability mastered.":r.progression.AvailableSkillPoints==0?"No ability points available. Earn another at an odd regiment level.":"";
            return new RegimentAction {Enabled=reason=="",Reason=reason,Execute=()=>Done(company.Learn(r,ability.Id),ability.Name+" learned.")};
        }
        public RegimentAction AutoApply(string id)
        {
            var r=company.Save.regiments.Find(u=>u.id.ToString()==id);
            string reason=!company.CanManage?"Learn or train between battles in an active company.":r==null?"Choose a regiment.":"No eligible stat or ability points are available.";
            var users=r==null?EquipmentUsers.All:GloryCompany.Users(r);var role=r==null?UnitRole.Line:GloryCompany.Roles[r.tier];
            var preview=r==null?null:RegimentAutoAllocation.Plan(r.progression,role,users,s=>GloryCompany.UsefulSkill(r,s));
            return new RegimentAction {Title="Auto apply available progression",Enabled=company.CanManage&&preview!=null&&preview.HasWork,Reason=company.CanManage&&preview!=null&&preview.HasWork?"":reason,
                Description="Spends eligible stat and ability points by troop role. Existing allocations are preserved; no coins or points are created.",
                Execute=()=>
                {
                    var current=company.Save.regiments.Find(u=>u.id.ToString()==id);
                    if(!company.CanManage||current==null)return "Progression allocation is unavailable.";
                    var plan=RegimentAutoAllocation.Plan(current.progression,GloryCompany.Roles[current.tier],GloryCompany.Users(current),s=>GloryCompany.UsefulSkill(current,s));
                    int stats=0,abilities=0;foreach(var skill in plan.Stats)if(company.Train(current,skill))stats++;
                    foreach(var ability in plan.Abilities)if(company.Learn(current,ability))abilities++;
                    return Done(true,stats+" stat point(s) and "+abilities+" ability point(s) allocated.");
                }};
        }
        public IEnumerable<RegimentAction> Services(string[] ids)
        {
            var r=Find(ids);if(r==null)yield break;
            int add=company.AffordableReplacements(r),price=add*GloryCompany.ReplacementCosts[r.tier];
            yield return new RegimentAction {Title="Replenish "+add+" "+GloryCompany.StrengthLabel(r),Cost=CommandUI.Number(price)+" coins",Enabled=company.CanManage&&add>0,Description="Restore up to "+GloryCompany.UnitCapacity(r)+" "+GloryCompany.StrengthLabel(r)+". Buys as many replacements as you can afford; keeps all XP and skills.",Reason=!company.CanManage?"Expedition is not active.":r.soldiers==GloryCompany.UnitCapacity(r)?"Regiment is at full strength.":"Not enough coins for a replacement.",Execute=()=>Done(company.Replenish(r),add+" replacements joined the regiment.")};
            int sizeCost=company.SizeUpgradeCost(r),step=GloryCompany.SizeStep(r),capacity=GloryCompany.UnitCapacity(r);
            bool sizeMax=r.sizeUpgrades>=GloryCompany.MaxSizeUpgrades(r),sizeReady=!GloryCompany.IsArtillery(r)||r.soldiers==capacity;
            yield return new RegimentAction {Title="Increase regiment size · "+capacity+" → "+(sizeMax?capacity:capacity+step)+" "+GloryCompany.StrengthLabel(r),Cost=CommandUI.Number(sizeCost)+" coins",Enabled=company.CanManage&&sizeReady&&!sizeMax&&company.Save.coins>=sizeCost,Reason=sizeMax?"Maximum regiment size reached.":!sizeReady?"Replenish this battery before adding guns.":"Requires an active company and "+CommandUI.Number(sizeCost)+" coins.",Description="Permanently adds "+step+" places and recruits "+step+" "+GloryCompany.StrengthLabel(r)+" immediately. Price includes issuing existing kit and quality to the new recruits. Preserves XP, abilities and kit. Maximum "+(capacity+(GloryCompany.MaxSizeUpgrades(r)-r.sizeUpgrades)*step)+".",Execute=()=>Done(company.IncreaseSize(r),"Regiment size increased.")};
            if(r.tier<GloryCompany.InfantryTierCount)
            {
                bool canShrink=r.sizeUpgrades>GloryCompany.MinSizeUpgrades(r);
                yield return new RegimentAction {Title="Reduce regiment size · "+capacity+" → "+(canShrink?capacity-step:capacity)+" troops",Cost="Free · no refund",Enabled=company.CanManage&&canShrink,Reason=canShrink?"Available between battles.":"Minimum infantry size is 60 troops.",Description="Reduce capacity by 20, down to 60 troops. Excess soldiers leave; retain XP, abilities, quality and kit. Future equipment purchases cost less. Growing again includes equipping new recruits.",Execute=()=>Done(company.DecreaseSize(r),"Regiment size reduced.")};
            }
            int repair=company.RepairCost(r);
            yield return new RegimentAction {Title="Repair equipment · "+r.loadout.Condition+"% condition",Cost=CommandUI.Number(repair)+" coins",Enabled=company.CanManage&&repair>0&&company.Save.coins>=repair,Reason=repair==0?"Equipment is in full condition.":"Requires an active company and "+CommandUI.Number(repair)+" coins.",Description="Restores kit bonuses to full condition. Retains every item, level and training point.",Execute=()=>Done(company.Repair(r),"Equipment repaired.")};
            int next=GloryCompany.NextTier(r),cost=company.UpgradeCost(r);
            string why=!company.CanManage?"Requires an active company.":next<0?"This unit is already the highest tier in its branch.":!company.TierUnlocked(next)?"Requires "+CommandUI.Number(GloryCompany.UnlockXP[next])+" command XP.":r.progression.UnitLevel<GloryCompany.PromotionLevels[next]?"Requires regiment level "+GloryCompany.PromotionLevels[next]+".":company.Save.coins<cost?"Requires "+CommandUI.Number(cost)+" coins.":"";
            yield return new RegimentAction {Title=next<0?"Highest troop tier":"Promote to "+GloryCompany.TierNames[next],Cost=CommandUI.Number(cost)+" coins",Enabled=why=="",Reason=why,Description="Improves base troop stats and changes the unit's appearance. Keeps purchased rifles, all equipment, quality, strength, XP and trained stats; their bonuses apply on top of the new base stats.",Execute=()=>Done(company.Upgrade(r),"Unit promoted; equipment and progression retained.")};
            yield return new RegimentAction {Title="Improve equipment quality",Cost=CommandUI.Number(company.QualityCost(r))+" coins",Enabled=company.CanManage&&r.quality<UnitProgression.MaxQuality&&company.Save.coins>=company.QualityCost(r),Reason=r.quality>=UnitProgression.MaxQuality?"Already maximum equipment quality.":"Requires an active expedition and "+CommandUI.Number(company.QualityCost(r))+" coins.",Description="+4% quality accuracy / melee and 2% faster reload per grade. Does not replace carried equipment.",Execute=()=>Done(company.ImproveQuality(r),"Quality improved.")};
        }
    }
    public sealed class CampaignRegimentSource : IRegimentSource,IRegimentOrganizationSource,IRegimentProcurementSource,IRegimentAppearanceSource,IRegimentAutoProgressionSource
    {
        readonly CountryCampaign campaign;readonly CampaignArmy army;readonly Func<bool> save;
        public CampaignRegimentSource(CountryCampaign c,CampaignArmy a,Func<bool> persist){campaign=c;army=a;save=persist;}
        public string Context=>"Campaign / "+army.name+" / Current province: "+campaign.EquipmentProvince(army.regionId);
        public string Resources=>campaign.World.FactionState(army.owner).treasury.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" treasury  ·  "+campaign.World.State(army.regionId).economy.materials.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)+" local materials";
        public string Guidance=>campaign.EquipmentFacilities(army.regionId)+". Purchases use this province. Original recruitment identity and custom names stay through transfers.";
        public bool MultiSelect=>true;
        public bool CanArchive=>campaign.CanOrder(army.owner)&&!campaign.Committed(army);
        public RegimentAction Import(string targetId,SavedRegiment saved)
        {
            bool enabled=campaign.CanRecruitSaved(army.owner,army.regionId,saved,army.id,out var reason);
            return new RegimentAction {Title="Recruit cadre for this army",Cost=CommandUI.Number(campaign.SavedRecruitCost(saved))+" treasury + "+CommandUI.Number(campaign.SavedRecruitMaterials(saved))+" materials",Enabled=enabled,Reason=reason,
                Description="Trains a new "+campaign.Balance.unitSize+"-soldier regiment for "+army.name+" in "+campaign.Balance.trainingTurns+" turns. Preserves saved progression and equipment; reserves local manpower and a training place. If this army leaves, recruits form a detachment in the recruiting city.",
                Execute=()=>{bool ok=campaign.RecruitSaved(army.owner,army.regionId,saved,army.id,out var msg);return Result(ok,msg);}};
        }
        public IReadOnlyList<RegimentRecord> Units=>army.units.Select(u=>new RegimentRecord {Id=u.id,Faction=army.owner,Name=string.IsNullOrEmpty(u.name)?u.id:u.name,Role=u.role,Users=UnitEquipment.CampaignUsers(u.role),BattleRole=UnitRoster.CampaignRole(u.role,u.weapon),Strength=u.soldiers,Capacity=campaign.Balance.unitSize,Quality=u.equipment,Progression=u.progression,Loadout=u.loadout,Weapon=u.weapon,Status="Current province: "+campaign.EquipmentProvince(army.regionId)+" · "+(u.originProvenanceKnown?"Recruitment origin: ":"Legacy recorded origin: ")+(string.IsNullOrEmpty(u.originTownName)?campaign.EquipmentProvince(u.homeRegionId):u.originTownName)+(string.IsNullOrEmpty(u.originArmyName)?"":" / "+u.originArmyName)+"\n"+(u.kitTurns>0?"Delivery: "+UnitEquipment.Label(UnitEquipment.Find(u.pendingItem))+" · "+u.kitTurns+" turns remaining":u.upgradeTurns>0?"Quality refit · "+u.upgradeTurns+" turns remaining":u.trainingTurns>0?"Training · "+u.trainingTurns+" turns remaining":"Supply "+(army.supply*100).ToString("0")+"% · "+MilitaryEquipment.TrainingName(u.doctrine))+"\n"+campaign.EquipmentDeliveryStatus(army,u)}).ToArray();
        public bool CanOrganize=>campaign.CanOrder(army.owner)&&!campaign.Committed(army);
        public bool CanDress=>CanOrganize;
        public string Dress(string id,string appearance){var unit=army.units.Find(u=>u.id==id);if(!CanDress||unit==null)return "Change uniforms while the army is available.";unit.loadout.appearance=appearance;return Result(true,"Regiment uniform saved.");}
        public string Rename(string id,string name)
        {
            bool ok=campaign.RenameRegiment(army.owner,army.id,id,name,out string reason);
            return Result(ok,reason);
        }
        public string Move(string id,string beforeId)=>Result(CanOrganize&&RegimentOrganization.Move(army.units,u=>u.id,id,beforeId),"Regiment order saved.");
        CampaignRegiment Find(string[] ids)=>army.units.Find(u=>ids.Contains(u.id));
        string Result(bool result,string reason)=>!result?reason:save()?reason:reason+" Campaign save failed.";
        public bool HasHostileProcurement=>campaign.HostileEquipmentProvince(army);
        CampaignRegiment[] EquipmentTargets(string[] ids,EquipmentItem item)=>army.units.Where(u=>ids.Contains(u.id)&&item.Supports(UnitEquipment.CampaignUsers(u.role))&&UnitEquipment.CanReplace(u.loadout,item)).ToArray();
        public RegimentAction Equipment(string[] ids,EquipmentItem item)
        {
            var targets=EquipmentTargets(ids,item);
            bool allowed=campaign.CanEquipItems(army.owner,army,targets,item,out string why);
            var quote=campaign.EquipmentQuote(army,targets,item);
            return new RegimentAction {Title="Buy for "+targets.Length+" regiments",Cost=CommandUI.Number(quote.Treasury)+" treasury + "+CommandUI.Number(quote.Materials)+" materials",Enabled=allowed,Reason=why,
                Description=campaign.EquipmentRequirements(item)+"\n"+(quote.Hostile?"Enemy province purchase: 50% markup. "+campaign.ProcurementRations(army)+" ":"")+(item.Weapon==""?"1 turn":"2 turns")+" to deliver to "+targets.Length+" eligible recipients in "+campaign.EquipmentProvince(army.regionId)+". Already equipped, higher levels and incompatible regiments are skipped. Army cannot march while refitting.",
                Execute=()=>{bool ok=campaign.EquipItems(army.owner,army.id,targets.Select(u=>u.id).ToArray(),item.Id,out string msg);return Result(ok,msg);}};
        }
        public RegimentAction RaidEquipment(string[] ids,EquipmentItem item)
        {
            var targets=EquipmentTargets(ids,item);
            bool allowed=campaign.CanRaidEquipmentItems(army.owner,army,targets,item,out string why);
            var quote=campaign.EquipmentQuote(army,targets,item,true);
            return new RegimentAction {Title="Raid for "+targets.Length+" regiments",Cost="0 treasury + "+CommandUI.Number(quote.Materials)+" materials · "+quote.RaidUses+" facility uses",Enabled=allowed,Reason=why,
                Description=campaign.EquipmentRaidDescription(army,targets,item)+" "+campaign.ProcurementRations(army)+" "+(item.Weapon==""?"1 turn":"2 turns")+" to fit secured gear; army cannot march while refitting.",
                Execute=()=>{bool ok=campaign.RaidEquipmentItems(army.owner,army.id,targets.Select(u=>u.id).ToArray(),item.Id,out string msg);return Result(ok,msg);}};
        }
        public RegimentAction Skill(string[] ids,UnitSkill skill)
        {
            var u=Find(ids);return new RegimentAction {Title="Allocate · 1 point",Enabled=campaign.CanOrder(army.owner)&&!campaign.Committed(army)&&u.progression.AvailablePoints>0&&u.progression.Level(skill)<UnitProgression.MaxSkill,Reason=!campaign.CanOrder(army.owner)?"Allocate points during your campaign turn.":campaign.Committed(army)?"Finish the army’s march, training, refit or siege orders first.":u.progression.Level(skill)>=UnitProgression.MaxSkill?"This stat is at its maximum.":"No stat points available. Gain experience to earn more.",Description=UnitProgression.SkillEffect(skill)+". Applied immediately; no treasury or materials cost.",Execute=()=>{bool ok=campaign.TrainSkill(army.owner,army.id,u.id,skill,out string msg);return Result(ok,msg);}};
        }
        public RegimentAction Ability(string[] ids,RegimentAbilityDefinition ability)
        {
            var u=Find(ids);return new RegimentAction {Enabled=campaign.CanOrder(army.owner)&&!campaign.Committed(army)&&ability.Supports(UnitEquipment.CampaignUsers(u.role))&&u.progression.AbilityRank(ability.Id)<RegimentAbilities.MaxRank&&u.progression.AvailableSkillPoints>0,Reason=!campaign.CanOrder(army.owner)?"Learn abilities during your campaign turn.":campaign.Committed(army)?"Finish the army’s march, training, refit or siege orders first.":u.progression.AbilityRank(ability.Id)>=RegimentAbilities.MaxRank?"Ability mastered.":"No ability points available. Earn another at an odd regiment level.",Execute=()=>{bool ok=campaign.LearnAbility(army.owner,army.id,u.id,ability.Id,out string reason);return Result(ok,reason);}};
        }
        public RegimentAction AutoApply(string id)
        {
            var unit=army.units.Find(u=>u.id==id);
            string reason=!campaign.CanOrder(army.owner)?"Allocate points during your campaign turn.":campaign.Committed(army)?"Finish the army’s march, training, refit or siege orders first.":unit==null?"Choose a regiment.":"No eligible stat or ability points are available.";
            var role=unit==null?UnitRole.Line:UnitRoster.CampaignRole(unit.role,unit.weapon);var users=unit==null?EquipmentUsers.All:UnitEquipment.CampaignUsers(unit.role);
            var preview=unit==null?null:RegimentAutoAllocation.Plan(unit.progression,role,users);
            return new RegimentAction {Title="Auto apply available progression",Enabled=campaign.CanOrder(army.owner)&&!campaign.Committed(army)&&preview!=null&&preview.HasWork,Reason=campaign.CanOrder(army.owner)&&!campaign.Committed(army)&&preview!=null&&preview.HasWork?"":reason,
                Description="Spends eligible stat and ability points by troop role. Existing allocations are preserved; no treasury or materials are used.",
                Execute=()=>
                {
                    var current=army.units.Find(u=>u.id==id);
                    if(current==null||!campaign.CanOrder(army.owner)||campaign.Committed(army))return "Progression allocation is unavailable.";
                    var currentRole=UnitRoster.CampaignRole(current.role,current.weapon);var currentUsers=UnitEquipment.CampaignUsers(current.role);
                    var plan=RegimentAutoAllocation.Plan(current.progression,currentRole,currentUsers);int stats=0,abilities=0;
                    foreach(var skill in plan.Stats)if(campaign.TrainSkill(army.owner,army.id,current.id,skill,out _))stats++;
                    foreach(var ability in plan.Abilities)if(campaign.LearnAbility(army.owner,army.id,current.id,ability,out _))abilities++;
                    return Result(stats+abilities>0,stats+" stat point(s) and "+abilities+" ability point(s) allocated.");
                }};
        }
        public IEnumerable<RegimentAction> Services(string[] ids)
        {
            var u=Find(ids);bool allowed=campaign.CanUpgradeEquipment(army.owner,army,u,out string why);
            yield return new RegimentAction {Title="Quality refit / "+u.equipment+" → "+Math.Min(UnitProgression.MaxQuality,u.equipment+1),Cost=CommandUI.Number(campaign.EquipmentUpgradeCost(u))+" treasury + "+CommandUI.Number(campaign.EquipmentUpgradeMaterials(u))+" materials",Enabled=allowed,Reason=why,Description=(CountryCampaign.QualityFacilities(u.equipment+1).Description==""?"No production building required. ":"Current province: "+CountryCampaign.QualityFacilities(u.equipment+1).Description+". ")+"Two turns; the army cannot march while refitting. Each grade adds 4% quality accuracy / melee and reduces reload time by 2%.",Execute=()=>{bool ok=campaign.UpgradeEquipment(army.owner,army.id,u.id,out string msg);return Result(ok,msg);}};
            // Firearms are purchased in Equipment, with one visible quote and the full stat comparison.
            foreach(var course in CountryCampaign.Courses.Where(course=>course.weapon==""))
            {
                bool can=campaign.CanAdopt(army.owner,army,u,course,out string reason);
                yield return new RegimentAction {Title=course.name,Cost=CommandUI.Number(campaign.AdoptionCost(u,course))+" treasury + "+CommandUI.Number(campaign.AdoptionMaterials(u,course))+" materials",Enabled=can,Reason=reason,Description=course.turns+" turn(s); army movement pauses. "+(course.weapon!=""?"Issue "+MilitaryEquipment.Weapon(course.weapon).Name+" / "+MilitaryEquipment.Weapon(course.weapon).EffectiveRange+" m range / "+MilitaryEquipment.Weapon(course.weapon).Reload+" s reload.":"Teaches "+MilitaryEquipment.TrainingName(course.doctrine)+"."),Execute=()=>{bool ok=campaign.AdoptRegiment(army.owner,army.id,u.id,course.id,out string msg);return Result(ok,msg);}};
            }
        }
    }
    public sealed class DeployedRegimentSource : IRegimentSource
    {
        readonly BattleWorld world;
        public DeployedRegimentSource(BattleWorld value){world=value;}
        public string Context=>"Battle / Deployed regiments";
        public string Resources=>world.CountAlive(ArmySide.French)+" surviving troops";
        public string Guidance=>"Inspect deployed equipment and training. Return to your company or campaign to make changes.";
        public bool MultiSelect=>false;
        public bool CanArchive=>false;
        public RegimentAction Import(string targetId,SavedRegiment saved)=>new RegimentAction {Title="Load saved regiment",Enabled=false,Reason="Return to preparation or campaign management before importing a saved regiment."};
        public IReadOnlyList<RegimentRecord> Units=>world.Battalions.Where(b=>b.Side==ArmySide.French).Select(b=>new RegimentRecord {Id=b.Id.ToString(),Faction=b.Faction,Name=b.Name,CurrentKills=b.Kills,Role=UnitRoster.Label(b.Role),BattleRole=b.Role,Tier=b.TroopTier,Users=UnitEquipment.BattleUsers(b),Strength=b.AliveCount,Capacity=b.SoldierCount,Quality=(int)Math.Round(b.EquipmentQuality/.04f),Progression=b.Progression,Loadout=b.Loadout,Weapon=b.Weapon?.Id??"musket",Status="Deployed · morale "+b.Morale.ToString("0")+" · fatigue "+b.Fatigue.ToString("0")+"%"}).ToArray();
        public RegimentAction Equipment(string[] ids,EquipmentItem item)=>new RegimentAction {Title="Deployed",Cost="",Reason=Guidance};
        public RegimentAction Skill(string[] ids,UnitSkill skill)=>new RegimentAction {Title="Deployed",Reason=Guidance};
        public RegimentAction Ability(string[] ids,RegimentAbilityDefinition ability)=>new RegimentAction {Reason=Guidance};
        public IEnumerable<RegimentAction> Services(string[] ids){yield return new RegimentAction {Title="Return to headquarters to refit",Description=Guidance,Reason=Guidance};}
    }
}
