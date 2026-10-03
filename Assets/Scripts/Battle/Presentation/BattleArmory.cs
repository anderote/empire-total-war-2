using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Empire.UI;
using UnityEngine;

namespace EmpireBattle
{
    [Serializable] public sealed class ArmoryEntry
    {
        public string id,name="",baseWeapon="";
        public bool savedProfile;
        public UnitRole profileRole;
        public EquipmentUsers users;
        public int quality,tier,rosterOrder;
        public UnitProgression progression=new UnitProgression();
        public UnitLoadout loadout=new UnitLoadout();
    }
    [Serializable] public sealed class ArmorySave
    {
        public int version=3,treasury=100000,year=1800;
        public string serviceSequenceNamespace=Guid.NewGuid().ToString("N");
        public long serviceBattleSequence;
        static bool ValidSequence(string value,long sequence)=>!string.IsNullOrEmpty(value)&&value.Length==32&&value.All(Uri.IsHexDigit)
            &&sequence>=0&&sequence<=RegimentServiceLedger.MaxCounter;
        public List<ArmoryEntry> units=new List<ArmoryEntry>();
        public bool Valid=>(version==1||version==2||version==3)&&treasury>=0&&treasury<=1000000&&year>=1800&&year<=1918&&units!=null&&units.Count<=1000
            &&(version<3||ValidSequence(serviceSequenceNamespace,serviceBattleSequence))
            &&units.All(u=>u!=null&&!string.IsNullOrEmpty(u.id)&&(string.IsNullOrEmpty(u.name)||SavedRegiment.SafeName(u.name,60))&&(string.IsNullOrEmpty(u.baseWeapon)||MilitaryEquipment.Weapon(u.baseWeapon)!=null&&MilitaryEquipment.Weapon(u.baseWeapon).EarliestYear<=year)&&(!u.savedProfile||UnitRoster.Shoots(u.profileRole)&&u.users==SavedRegiment.Users(u.profileRole))&&u.quality>=0&&u.quality<=UnitProgression.MaxQuality&&u.progression!=null&&u.progression.Valid
                &&(u.users==EquipmentUsers.Line||u.users==EquipmentUsers.Militia||u.users==EquipmentUsers.Light||u.users==EquipmentUsers.Rifle||u.users==EquipmentUsers.Melee||u.users==EquipmentUsers.Cavalry||u.users==EquipmentUsers.Artillery)&&u.loadout!=null&&u.tier>=0&&u.tier<=12&&u.loadout.Valid(UnitEquipment.RetainedUsers(u.users,u.tier),year))
            &&units.Select(u=>u.id).Distinct().Count()==units.Count;
    }
    public sealed class BattleArmorySession
    {
        public readonly BattleWorld World;
        public readonly ArtillerySystem Artillery;
        public ArmorySave Save { get; private set; }=new ArmorySave();
        public readonly List<ArmoryEntry> Entries=new List<ArmoryEntry>();
        public readonly List<string> Names=new List<string>();
        readonly Dictionary<ArmoryEntry,BattalionState> regiments=new Dictionary<ArmoryEntry,BattalionState>();
        readonly Dictionary<ArmoryEntry,ArtilleryGun[]> batteries=new Dictionary<ArmoryEntry,ArtilleryGun[]>();
        readonly string path;
        bool committed,storageFailed;
        public string Message="";
        public bool CanChange=>!World.Config.CampaignDeployment&&World.Time==0&&!storageFailed;
        public BattleArmorySession(BattleWorld world,ArtillerySystem artillery,string savePath)
        {
            World=world;Artillery=artillery;path=savePath;
            if(path!=null&&File.Exists(path))
            {
                try {var saved=JsonUtility.FromJson<ArmorySave>(File.ReadAllText(path));if(saved==null||!saved.Valid)throw new InvalidDataException("Invalid armory save.");Save=saved;if(Save.version==1){Save.treasury=Math.Min(1000000,Save.treasury+98500);Save.version=2;}if(Save.version<3){Save.serviceSequenceNamespace=NewSequenceNamespace();Save.serviceBattleSequence=0;Save.version=3;}}
                catch(Exception ex){storageFailed=true;Message="Armory save could not be loaded; upgrades disabled. "+ex.Message;Debug.LogWarning(Message);}
            }
            if(string.IsNullOrEmpty(Save.serviceSequenceNamespace))Save.serviceSequenceNamespace=NewSequenceNamespace();
            foreach(var unit in World.Battalions.Where(u=>u.Side==ArmySide.French))
            {
                var entry=Entry(World.Config.PlayerFaction+"/"+unit.Role+"/"+unit.Id,UnitEquipment.BattleUsers(unit));
                if(string.IsNullOrEmpty(entry.baseWeapon)){entry.baseWeapon=unit.Weapon?.Id??"musket";entry.tier=unit.TroopTier;}
                Entries.Add(entry);Names.Add(string.IsNullOrEmpty(entry.name)?unit.Name:entry.name);regiments.Add(entry,unit);Apply(entry);
            }
            foreach(var group in artillery.Guns.Where(g=>g.Side==ArmySide.French).GroupBy(g=>g.UnitId))
            {
                var entry=Entry(World.Config.PlayerFaction+"/artillery/"+group.Key,EquipmentUsers.Artillery);
                Entries.Add(entry);Names.Add(string.IsNullOrEmpty(entry.name)?"Battery "+(group.Key+1)+" · "+group.Count()+" cannons":entry.name);batteries.Add(entry,group.ToArray());Apply(entry);
            }
        }
        public string Dress(string id,string appearance)
        {
            var entry=Entries.Find(e=>e.id==id);if(!CanChange||entry==null)return "Change uniforms before battle.";
            entry.loadout.appearance=appearance;Apply(entry);Persist();return storageFailed?Message:"Regiment uniform saved.";
        }
        public string Rename(string id,string name)
        {
            var entry=Entries.Find(e=>e.id==id);name=name?.Trim();
            if(!CanChange||entry==null||!SavedRegiment.SafeName(name,60))return "Enter a regiment name of 1–60 characters before battle.";
            entry.name=name;Names[Entries.IndexOf(entry)]=name;
            if(regiments.TryGetValue(entry,out var unit))unit.Name=name;
            Persist();return storageFailed?Message:"Regiment renamed to “"+name+"”.";
        }
        public string Move(string id,string beforeId)
        {
            var ordered=Entries.OrderBy(e=>e.rosterOrder).ToList();
            if(!CanChange||!RegimentOrganization.Move(ordered,e=>e.id,id,beforeId))return "Regiment order unchanged.";
            for(int i=0;i<ordered.Count;i++)ordered[i].rosterOrder=i;
            Persist();return storageFailed?Message:"Regiment order saved.";
        }
        ArmoryEntry Entry(string id,EquipmentUsers users)
        {
            var entry=Save.units.Find(u=>u.id==id);
            if(entry!=null&&entry.users!=users&&!(entry.savedProfile&&users!=EquipmentUsers.Artillery&&users!=EquipmentUsers.Cavalry&&users!=EquipmentUsers.Melee)){storageFailed=true;Message="Armory unit type mismatch; upgrades disabled.";entry=null;}
            if(entry==null){entry=new ArmoryEntry {id=id,users=users};Save.units.Add(entry);}return entry;
        }
        void Apply(ArmoryEntry entry)
        {
            if(regiments.TryGetValue(entry,out var unit))
            {
                if(entry.savedProfile){unit.Role=entry.profileRole;unit.FireMode=UnitRoster.DefaultFireMode(unit.Role);}
                if(!string.IsNullOrEmpty(entry.name))unit.Name=entry.name;
                unit.TroopTier=entry.tier;unit.Progression=entry.progression.Copy();unit.Loadout=entry.loadout.Copy();unit.Kit=unit.Loadout.Modifiers;unit.GrenadesRemaining=unit.Progression.Grenades(unit.Kit.grenades);unit.EquipmentQuality=entry.quality*.04f;
                var issued=unit.Loadout.items.Select(UnitEquipment.Find).FirstOrDefault(i=>i.Weapon!="");
                if(issued!=null||!string.IsNullOrEmpty(entry.baseWeapon))
                {
                    string weaponId=issued?.Weapon??entry.baseWeapon;bool changed=unit.Weapon?.Id!=weaponId;unit.Weapon=MilitaryEquipment.Weapon(weaponId);
                    if(changed&&EmpireBattle.Art.CartridgeRifleLibrary.UsesCartridges(unit.Weapon))
                    {
                        World.SetFireMode(unit.Id,InfantryFireMode.FireAtWill);
                        if(unit.Formation==FormationKind.Line)
                        {
                            unit.Files=Mathf.CeilToInt(unit.AliveCount/2f);
                            for(int slot=0;slot<unit.SoldierCount;slot++)World.Soldiers.Positions[unit.SoldierStart+slot]=unit.Position+FormationMath.SlotOffset(unit,slot);
                        }
                    }
                }
                if(unit.Kind==UnitKind.Cavalry){unit.FireAtWill=UnitRoster.HasFirearm(unit);unit.MeleeMode=false;if(!unit.FireAtWill)unit.Weapon=null;}
                unit.Ammunition=UnitRoster.HasFirearm(unit)?30+unit.Kit.ammunition+unit.Progression.AmmunitionBonus(unit.Weapon):0;unit.Morale=unit.MaxMorale+unit.Kit.moraleReserve;
                unit.StartingAmmunition=unit.Ammunition;
                for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)
                {World.Soldiers.Health[i]=unit.MaxHealth;World.Soldiers.RoundsRemaining[i]=unit.Ammunition;World.Soldiers.MagazineRounds[i]=Math.Min(unit.Weapon?.Magazine??1,unit.Ammunition);}
            }
            if(batteries.TryGetValue(entry,out var guns))foreach(var gun in guns)
            {
                int previous=gun.Kit.ammunition;gun.Progression=entry.progression.Copy();gun.Loadout=entry.loadout.Copy();gun.Kit=gun.Loadout.Modifiers;gun.Equipment=entry.quality;
                gun.Ammunition=ArtillerySystem.StartingAmmunition+gun.Kit.ammunition+gun.Progression.ArtilleryAmmunitionBonus;gun.Morale=gun.MaxMorale+gun.Kit.moraleReserve;
            }
        }
        public bool CanImport(ArmoryEntry entry,SavedRegiment saved,out string reason)
        {
            reason="Load saved regiments during battle preparation.";if(!CanChange||!Entries.Contains(entry))return false;
            if(saved==null||!saved.Valid){reason="Invalid saved regiment.";return false;}
            if(!regiments.TryGetValue(entry,out var unit)||!UnitRoster.Shoots(unit.Role)){reason="Choose an infantry regiment to receive this saved cadre.";return false;}
            if(saved.RequiredYear>Save.year){reason="Requires equipment era "+saved.RequiredYear+". Advance the era in Service.";return false;}
            int cost=saved.KitCost+saved.VeteranCost;
            if(Save.treasury<cost){reason="Requires "+cost+" preparation funds.";return false;}
            reason="";return true;
        }
        public bool Import(ArmoryEntry entry,SavedRegiment saved)
        {
            if(!CanImport(entry,saved,out var reason)){Message=reason;return false;}
            entry.tier=saved.tier;entry.name=saved.name;entry.baseWeapon=saved.weapon;entry.savedProfile=true;entry.profileRole=saved.role;entry.users=saved.users;
            entry.progression=saved.progression.Copy();entry.loadout=saved.loadout.Copy();entry.quality=saved.quality;
            Save.treasury-=saved.KitCost+saved.VeteranCost;Names[Entries.IndexOf(entry)]=entry.name;Apply(entry);Persist();if(storageFailed)return false;
            Message="Loaded "+saved.name+". Scenario troop strength retained; the saved copy and Glory regiment are unchanged.";return true;
        }
        public bool CanEquip(ArmoryEntry entry,EquipmentItem item)=>CanChange&&Entries.Contains(entry)&&item!=null&&item.Supports(entry.users)
            &&item.EarliestYear<=Save.year&&UnitEquipment.CanReplace(entry.loadout,item)&&Save.treasury>=item.Cost;
        public List<ArmoryEntry> EquipmentTargets(IEnumerable<ArmoryEntry> selection,EquipmentItem item)=>selection.Distinct().Where(e=>Entries.Contains(e)&&item!=null&&item.Supports(e.users)&&item.EarliestYear<=Save.year&&UnitEquipment.CanReplace(e.loadout,item)).ToList();
        public bool EquipMany(IEnumerable<ArmoryEntry> selection,string itemId)
        {
            var item=UnitEquipment.Find(itemId);var targets=EquipmentTargets(selection,item);
            if(!CanChange||targets.Count==0||Save.treasury<targets.Count*item.Cost)return false;
            foreach(var entry in targets){entry.loadout.Equip(itemId,entry.users,Save.year);if(World.Time==0)Apply(entry);}
            Save.treasury-=targets.Count*item.Cost;Persist();Message=UnitEquipment.Label(item)+" issued to "+targets.Count+" units.";return true;
        }
        public bool ImproveQualityMany(IEnumerable<ArmoryEntry> selection)
        {
            var targets=selection.Distinct().Where(e=>Entries.Contains(e)&&e.quality<UnitProgression.MaxQuality).ToList();
            if(!CanChange||targets.Count==0||Save.treasury<targets.Count*60)return false;
            foreach(var entry in targets){entry.quality++;if(World.Time==0)Apply(entry);}Save.treasury-=targets.Count*60;Persist();Message="Equipment quality improved for "+targets.Count+" units.";return true;
        }
        public bool Equip(ArmoryEntry entry,string itemId)
        {
            var item=UnitEquipment.Find(itemId);if(!CanEquip(entry,item))return false;
            entry.loadout.Equip(itemId,entry.users,Save.year);Save.treasury-=item.Cost;
            if(World.Time==0)Apply(entry);Persist();Message=UnitEquipment.Label(item)+" issued.";return true;
        }
        public bool Train(ArmoryEntry entry,UnitSkill skill)
        {
            if(!CanChange||!Entries.Contains(entry)||!UsefulSkill(entry,skill)||!entry.progression.Train(skill))return false;
            if(World.Time==0)Apply(entry);Persist();Message=skill+" improved.";return true;
        }
        public bool Learn(ArmoryEntry entry,RegimentAbility ability)
        {
            var definition=RegimentAbilities.Find(ability);
            if(!CanChange||!Entries.Contains(entry)||definition==null||!definition.Supports(entry.users)||!entry.progression.Learn(ability))return false;
            Apply(entry);Persist();Message=definition.Name+" learned.";return true;
        }
        public bool ApplyAllocation(ArmoryEntry entry,UnitRole role)
        {
            if(!CanChange||entry==null||!Entries.Contains(entry))return false;
            var plan=RegimentAutoAllocation.Plan(entry.progression,role,entry.users,s=>UsefulSkill(entry,s));
            if(!plan.HasWork)return false;
            bool changed=false;
            foreach(var skill in plan.Stats)if(UsefulSkill(entry,skill))changed|=entry.progression.Train(skill);
            foreach(var ability in plan.Abilities)
            {
                var definition=RegimentAbilities.Find(ability);
                if(definition!=null&&definition.Supports(entry.users))changed|=entry.progression.Learn(ability);
            }
            if(!changed)return false;
            Apply(entry);Persist();Message="Automatic training allocated "+plan.Stats.Length+" stat and "+plan.Abilities.Length+" ability point(s).";return true;
        }
        public static bool UsefulSkill(ArmoryEntry entry,UnitSkill skill)=>entry.users==EquipmentUsers.Artillery?skill!=UnitSkill.Melee&&skill!=UnitSkill.Health:
            entry.users==EquipmentUsers.Cavalry&&!UnitRoster.HasFirearm(UnitRole.Cavalry,entry.loadout)||entry.users==EquipmentUsers.Melee?skill==UnitSkill.Melee||skill==UnitSkill.Discipline||skill==UnitSkill.Endurance||skill==UnitSkill.Health:true;
        public string StatSummary(ArmoryEntry entry)
        {
            var kit=entry.loadout.Modifiers;var p=entry.progression;
            if(batteries.ContainsKey(entry))return "Kit preview: accuracy ×"+(p.AccuracyMultiplier*(1+entry.quality*.04f)*(1+kit.accuracy)).ToString("0.00")
                +" · reload time ×"+(p.ReloadMultiplier*(1-entry.quality*.02f)*(1+kit.reload)).ToString("0.00")
                +" · "+(ArtillerySystem.StartingAmmunition+kit.ammunition+p.ArtilleryAmmunitionBonus)+" rounds / cannon · morale reserve "+(p.MaxMorale+kit.moraleReserve);
            var unit=regiments[entry];var issued=entry.loadout.items.Select(UnitEquipment.Find).FirstOrDefault(i=>i.Weapon!="");
            var weapon=issued!=null?MilitaryEquipment.Weapon(issued.Weapon):unit.Weapon;
            string fire=UnitRoster.HasFirearm(unit.Role,entry.loadout)?" · "+((weapon?.Reload??World.Config.ReloadSeconds)*p.ReloadMultiplier*(1-entry.quality*.02f)*(1+kit.reload)).ToString("0.0")+"s reload · "+(30+kit.ammunition+p.AmmunitionBonus(weapon))+" rounds":"";
            return (weapon!=null?weapon.Name+" · "+weapon.EffectiveRange+"m range"+(weapon.Magazine>1?" · "+weapon.Magazine+" round magazine · "+weapon.Cycle+"s bolt cycle":"")+"\n":"")+"Kit preview: morale reserve "+(p.MaxMorale+kit.moraleReserve)+" · melee ×"+(p.MeleeMultiplier*(1+entry.quality*.04f)*(1+kit.melee)).ToString("0.00")+fire;
        }
        public bool ImproveQuality(ArmoryEntry entry)
        {
            if(!CanChange||!Entries.Contains(entry)||entry.quality>=UnitProgression.MaxQuality||Save.treasury<60)return false;
            entry.quality++;Save.treasury-=60;if(World.Time==0)Apply(entry);Persist();return true;
        }
        public void AdvanceEra()
        {
            if(!CanChange)return;Save.year=Save.year<1830?1830:Save.year<1850?1850:Save.year<1886?1886:1918;Persist();
        }
        public int CurrentKills(ArmoryEntry entry)
        {
            if(committed)return 0;
            if(regiments.TryGetValue(entry,out var unit))return unit.Kills;
            return batteries.TryGetValue(entry,out var guns)?guns.Sum(g=>g.Kills):0;
        }
        public void Complete()
        {
            if(committed||World.Outcome==BattleOutcome.Ongoing)return;committed=true;
            bool won=World.Outcome==BattleOutcome.FrenchVictory;int earned=0;
            long sequence=Save.serviceBattleSequence<RegimentServiceLedger.MaxCounter?++Save.serviceBattleSequence:Save.serviceBattleSequence;
            string battleId=sequence>0&&sequence<=RegimentServiceLedger.MaxCounter?Save.serviceSequenceNamespace+":"+sequence.ToString(System.Globalization.CultureInfo.InvariantCulture):"";
            var outcome=World.Outcome==BattleOutcome.Draw?ServiceBattleOutcome.Draw:won?ServiceBattleOutcome.Victory:ServiceBattleOutcome.Defeat;
            string date="Engagement "+sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);string place=World.Config.Map?.RegionName;
            string label=RegimentServiceLedger.BattleLabel(place,0,World.Config.Seed);
            foreach(var pair in regiments)
            {
                var unit=pair.Value;int xp=UnitProgression.BattleXP(unit.SoldierCount,unit.SurvivorCount,unit.Kills,unit.Hits,unit.ShotsFired,won);
                pair.Key.progression.CreditKills(unit.Kills);
                RegimentServiceCommit.Record(pair.Key.progression,battleId,label,date,outcome,unit.SoldierCount,unit.SurvivorCount,unit.Kills,unit.Hits,unit.ShotsFired);
                if(unit.SurvivorCount==0){pair.Key.progression=new UnitProgression {totalKills=pair.Key.progression.totalKills,service=pair.Key.progression.service?.Copy()};pair.Key.loadout=new UnitLoadout();pair.Key.quality=0;continue;}
                pair.Key.progression.Award(xp);earned+=xp;
            }
            foreach(var pair in batteries)
            {
                int shots=pair.Value.Sum(g=>g.ShotsFired),kills=pair.Value.Sum(g=>g.Kills),hits=pair.Value.Sum(g=>g.Hits);
                int xp=UnitProgression.BattleXP(pair.Value.Length*50,pair.Value.Length*50,kills,hits,shots,won);
                pair.Key.progression.CreditKills(kills);
                int operational=pair.Value.Count(g=>g.Operational);
                RegimentServiceCommit.Record(pair.Key.progression,battleId,label,date,outcome,pair.Value.Length,operational,kills,hits,shots);
                pair.Key.progression.Award(xp);earned+=xp;
            }
            if(earned>0){Save.treasury=Math.Min(1000000,Save.treasury+(won?500:300));Message=earned+" unit XP earned; "+(won?500:300)+" armory funds awarded.";}
            Persist();
        }
        static string NewSequenceNamespace()=>Guid.NewGuid().ToString("N");
        void Persist()
        {
            if(path==null||storageFailed)return;
            try {Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path+".tmp",JsonUtility.ToJson(Save,true));if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);}
            catch(Exception ex){storageFailed=true;Message="Armory could not save changes: "+ex.Message;Debug.LogWarning(Message);}
        }
    }
    public sealed class BattleArmory : MonoBehaviour
    {
        BattleRuntime runtime;BattleView view;IRegimentSource source;
        readonly RegimentManagementPanel management=new RegimentManagementPanel();
        bool wasPaused;
        public bool IsOpen {get;private set;}
        public RegimentManagementPanel Management=>management;
        public Rect PanelRect=>new Rect(24,74,Screen.width-48,Screen.height-98);
        public void SelectEntry(int index){var units=source.Units;if(units.Count>0)management.Select(source is BattleRegimentSource battle?battle.EntryId(index):units[Mathf.Clamp(index,0,units.Count-1)].Id);}
        public void Initialize(BattleRuntime battle,BattleView battleView,BattleArmorySession session)
        {runtime=battle;view=battleView;source=new BattleRegimentSource(session);Open();}
        public void InitializeDeployed(BattleRuntime battle,BattleView battleView)
        {runtime=battle;view=battleView;source=new DeployedRegimentSource(battle.World);}
        public void Open(){if(IsOpen)return;wasPaused=runtime.Paused;runtime.SetPaused(true);view.EnableInput=false;IsOpen=true;}
        void Close(){IsOpen=false;view.EnableInput=true;runtime.SetPaused(wasPaused);}
        void OnGUI()
        {
            if(!IsOpen||source==null)return;
            using var theme=new EmpireTheme.Scope(true);GUI.depth=-20;
            float scale=CommandUI.Scale(Screen.width,Screen.height);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float w=Screen.width/scale,h=Screen.height/scale;
            EmpireTheme.Fill(new Rect(0,0,w,h),new Color(.015f,.025f,.035f,.96f));
            CommandUI.Label(new Rect(24,15,w-310,37),"REGIMENTS & ARMORY",25,false,true);
            if(CommandUI.Button(new Rect(w-258,18,234,34),runtime.World.Time==0&&!runtime.Config.CampaignDeployment?"Begin battle":"Return to battle"))Close();
            management.Draw(new Rect(24,70,w-48,h-94),source);
            if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){Event.current.Use();Close();}
        }
    }
}
