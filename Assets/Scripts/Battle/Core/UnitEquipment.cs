using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireBattle
{
    [Flags] public enum EquipmentUsers { Line=1, Militia=2, Light=4, Rifle=8, Melee=16, Cavalry=32, Artillery=64, Infantry=31, Troops=63, All=127 }
    public enum EquipmentSlot { Weapon, Ammunition, Uniform, Protection, Mobility, Tools, Support, Standard, Secondary, Throwables }

    // Percent modifiers are fractions; ammunition and morale reserve are additive.
    [Serializable] public struct EquipmentModifiers
    {
        public float accuracy,reload,melee,resolve,fatigue,speed,range,cohesion,supply,misfire,smoke;
        public float heatFatigue,coldFatigue;
        public int ammunition,moraleReserve,grenades;
        public static EquipmentModifiers operator +(EquipmentModifiers a,EquipmentModifiers b)=>new EquipmentModifiers {
            accuracy=a.accuracy+b.accuracy,reload=a.reload+b.reload,melee=a.melee+b.melee,resolve=a.resolve+b.resolve,
            fatigue=a.fatigue+b.fatigue,speed=a.speed+b.speed,range=a.range+b.range,cohesion=a.cohesion+b.cohesion,
            supply=a.supply+b.supply,misfire=a.misfire+b.misfire,smoke=a.smoke+b.smoke,
            heatFatigue=a.heatFatigue+b.heatFatigue,coldFatigue=a.coldFatigue+b.coldFatigue,
            ammunition=a.ammunition+b.ammunition,moraleReserve=a.moraleReserve+b.moraleReserve,grenades=a.grenades+b.grenades};
    }
    public sealed class EquipmentItem
    {
        public readonly string Id,Name,Research,Weapon,Description,Family;
        public readonly int Level;
        public readonly EquipmentSlot Slot;
        public readonly EquipmentUsers Users;
        public readonly int Cost,Materials,EarliestYear;
        public readonly EquipmentModifiers Stats;
        public EquipmentItem(string id,string name,EquipmentSlot slot,EquipmentUsers users,int cost,int materials,
            EquipmentModifiers stats,string description,string research="",int year=1500,string weapon="",string family="",int level=1)
        {Family=family==""?id:family;Level=level;Id=id;Name=name;Slot=slot;Users=users;Cost=cost;Materials=materials;Stats=stats;Description=description;Research=research;EarliestYear=year;Weapon=weapon;}
        public bool Supports(EquipmentUsers unit)=>(Users&unit)!=0;
    }
    [Serializable] public sealed class UnitLoadout
    {
        public List<string> items=new List<string>();
        public int wear;
        public string appearance="";
        public int Condition=>100-wear;
        public EquipmentModifiers Modifiers
        {
            get {var result=new EquipmentModifiers();if(items!=null)foreach(var id in items){var item=UnitEquipment.Find(id);if(item!=null)result+=item.Stats;}return UnitEquipment.Conditioned(result,Condition);}
        }
        public bool Valid(EquipmentUsers users,int year=1918)
        {
            if(appearance!=null&&(appearance.Length>80||appearance.Any(char.IsControl)))return false;
            if(wear<0||wear>100||items==null||items.Count>Enum.GetValues(typeof(EquipmentSlot)).Length)return false;var slots=new HashSet<EquipmentSlot>();
            foreach(var id in items){var item=UnitEquipment.Find(id);if(item==null||!item.Supports(users)||item.EarliestYear>year||!slots.Add(item.Slot))return false;}return true;
        }
        public bool Equip(string id,EquipmentUsers users,int year=1918)
        {
            var item=UnitEquipment.Find(id);if(item==null||!item.Supports(users)||item.EarliestYear>year||items==null)return false;
            items.RemoveAll(old=>UnitEquipment.Find(old)?.Slot==item.Slot);items.Add(id);return true;
        }
        public UnitLoadout Copy()=>new UnitLoadout {wear=wear,appearance=appearance,items=items==null?null:new List<string>(items)};
    }
    public static partial class UnitEquipment
    {
        const EquipmentUsers Shooters=EquipmentUsers.Line|EquipmentUsers.Militia|EquipmentUsers.Light|EquipmentUsers.Rifle;
        // A promoted infantry regiment keeps kit bought in an earlier infantry role.
        // New purchases still use the current role's normal eligibility rules.
        public static EquipmentUsers RetainedUsers(EquipmentUsers users,int tier)=>tier>=1&&tier<=8&&(users&Shooters)!=0?users|Shooters:users;
        const EquipmentUsers Professionals=EquipmentUsers.Line|EquipmentUsers.Light|EquipmentUsers.Rifle|EquipmentUsers.Cavalry;
        static EquipmentItem I(string id,string name,EquipmentSlot slot,EquipmentUsers users,int cost,int materials,EquipmentModifiers stats,string description,string research="",int year=1500,string weapon="")
            =>new EquipmentItem(id,name,slot,users,cost,materials,stats,description,research,year,weapon);
        static readonly EquipmentItem[] BaseItems={
            I("cavalry-carbines","Mounted carbines",EquipmentSlot.Weapon,EquipmentUsers.Cavalry,100,10,new EquipmentModifiers(),"Short muzzle-loading firearms. Fire and reload while halted; keep the sabre for melee.","",1800,"carbine"),
            I("cavalry-pistols","Cavalry pistols",EquipmentSlot.Weapon,EquipmentUsers.Cavalry,70,6,new EquipmentModifiers(),"Short-range flintlock pistols. Fire and reload while halted; keep the sabre for melee.","",1800,"pistol"),
            I("infantry-hangers","Infantry hangers",EquipmentSlot.Secondary,Shooters,55,5,new EquipmentModifiers {melee=.08f,fatigue=.02f},"Short sidearms improve close combat at a small carrying-weight cost."),
            I("grenade-pouches","Grenade pouches",EquipmentSlot.Throwables,Shooters,90,8,new EquipmentModifiers {grenades=4,speed=-.03f},"Four grenades per regiment. Automatically thrown at enemies within 28 m while stationary and firing at will; no resupply during battle.","drill"),
            I("flintlock-rifles","Flintlock rifles",EquipmentSlot.Weapon,Shooters,300,10,new EquipmentModifiers(),"Much tighter aim, stronger hits and longer reach; slower muzzle loading.","rifles",1800,"rifle"),
            I("percussion-muskets","Percussion-cap muskets",EquipmentSlot.Weapon,Shooters,90,8,new EquipmentModifiers(),"Improved accuracy and damage with dependable cap ignition in rain; retains musket loading cadence.","percussion",1830,"percussion"),
            I("percussion-rifles","Percussion-cap rifle muskets",EquipmentSlot.Weapon,Shooters,450,12,new EquipmentModifiers(),"Greater precision, heavier hits and longer range; reliable cap ignition and faster loading than flintlock rifles.","rifle-muskets",1850,"rifle-musket"),
            I("breechloaders","Breechloading rifles",EquipmentSlot.Weapon,Shooters,700,15,new EquipmentModifiers(),"More accurate and powerful cartridges, loaded from the breech for much faster fire.","breechloading",1840,"breechloader"),
            I("magazine-rifles","Magazine rifles",EquipmentSlot.Weapon,Shooters,1100,20,new EquipmentModifiers(),"A major increase in precision and damage, with long reach and eight rapidly cycled shots before a full reload.","magazines",1880,"magazine"),
            I("smokeless-rifles","Smokeless rifle conversion",EquipmentSlot.Weapon,Shooters,1500,23,new EquipmentModifiers(),"The highest precision and damage, maximum reach and rapid magazine fire. Smokeless cartridges greatly reduce firing smoke.","smokeless",1886,"smokeless"),
            I("extra-cartridges","Extra cartridge boxes",EquipmentSlot.Ammunition,Shooters,45,4,new EquipmentModifiers {ammunition=15,speed=-.03f,fatigue=.05f},"15 extra rounds per soldier; heavier marching load."),
            I("ready-cartridges","Prepared paper cartridges",EquipmentSlot.Ammunition,Shooters,50,5,new EquipmentModifiers {reload=-.07f,misfire=-.03f},"Quicker cartridge handling; fewer loading failures.","drill"),
            I("waxed-cartridges","Waxed ammunition pouches",EquipmentSlot.Ammunition,Shooters,55,5,new EquipmentModifiers {misfire=-.08f},"Keep powder dry; reduce wet-weather failures."),
            I("selected-powder","Consistent powder charges",EquipmentSlot.Ammunition,Shooters|EquipmentUsers.Artillery,65,7,new EquipmentModifiers {accuracy=.08f},"Measured charges reduce shot dispersion.","arsenals"),
            I("reserve-caissons","Reserve ammunition caissons",EquipmentSlot.Ammunition,EquipmentUsers.Artillery,110,15,new EquipmentModifiers {ammunition=125,speed=-.08f,supply=-.05f},"125 extra rounds per cannon; larger logistical footprint.","arsenals"),
            I("summer-uniforms","Summer uniforms",EquipmentSlot.Uniform,EquipmentUsers.All,45,4,new EquipmentModifiers {heatFatigue=-.25f,coldFatigue=.20f},"Light cloth reduces heat exhaustion but performs poorly in cold weather."),
            I("winter-greatcoats","Winter greatcoats",EquipmentSlot.Uniform,EquipmentUsers.All,60,6,new EquipmentModifiers {coldFatigue=-.30f,heatFatigue=.15f,speed=-.02f},"Cold-weather protection; bulky and uncomfortable in heat."),
            I("waterproof-cloaks","Waterproof capes",EquipmentSlot.Uniform,EquipmentUsers.All,55,6,new EquipmentModifiers {misfire=-.04f,fatigue=-.03f,speed=-.02f},"Protect men and ammunition from exposure."),
            I("light-field-dress","Light field dress",EquipmentSlot.Uniform,EquipmentUsers.Light|EquipmentUsers.Rifle,55,5,new EquipmentModifiers {speed=.06f,fatigue=-.08f},"Unencumbered specialist movement.","light-infantry"),
            I("stout-boots","Stout marching boots",EquipmentSlot.Mobility,EquipmentUsers.Infantry|EquipmentUsers.Artillery,45,4,new EquipmentModifiers {speed=.04f,fatigue=-.10f},"Better march endurance and foot protection."),
            I("light-packs","Light marching packs",EquipmentSlot.Mobility,EquipmentUsers.Infantry,40,3,new EquipmentModifiers {speed=.06f,fatigue=-.06f,supply=-.05f},"Faster marches at the expense of carried supplies."),
            I("field-rations","Preserved field rations",EquipmentSlot.Support,EquipmentUsers.All,55,4,new EquipmentModifiers {supply=.12f,fatigue=-.05f},"Reduce supply shortages and help recovery."),
            I("water-carts","Water carts",EquipmentSlot.Support,EquipmentUsers.All,70,8,new EquipmentModifiers {heatFatigue=-.20f,supply=.06f,speed=-.03f},"Improve endurance in hot terrain; slower convoy."),
            I("medical-chests","Regimental medical chests",EquipmentSlot.Support,EquipmentUsers.All,75,6,new EquipmentModifiers {supply=.08f,fatigue=-.08f},"Better field support and rest recovery."),
            I("drums-fifes","Drums and fifes",EquipmentSlot.Standard,EquipmentUsers.Infantry,60,4,new EquipmentModifiers {resolve=.15f,cohesion=.08f},"March rhythm and audible signals steady the ranks."),
            I("regimental-colours","Regimental colours",EquipmentSlot.Standard,EquipmentUsers.Troops,70,6,new EquipmentModifiers {resolve=.20f,moraleReserve=8},"A visible rallying point for the regiment."),
            I("imperial-standard","Imperial Standard",EquipmentSlot.Standard,EquipmentUsers.All,220,18,new EquipmentModifiers {resolve=.60f,moraleReserve=25},"A major morale reserve and strong resistance to battlefield panic.","drill"),
            I("battle-honours","Battle-honour colours",EquipmentSlot.Standard,Professionals,120,9,new EquipmentModifiers {resolve=.35f,cohesion=.08f,moraleReserve=12},"Honoured colours strengthen discipline and unit identity."),
            I("sharpened-bayonets","Sharpened bayonets",EquipmentSlot.Secondary,Shooters,45,4,new EquipmentModifiers {melee=.12f},"Better close-combat blades."),
            I("balanced-sabres","Balanced cavalry sabres",EquipmentSlot.Secondary,EquipmentUsers.Cavalry|EquipmentUsers.Melee,85,8,new EquipmentModifiers {melee=.18f},"Better handling and more effective cuts."),
            I("heavy-lances","Heavy cavalry lances",EquipmentSlot.Weapon,EquipmentUsers.Cavalry,90,9,new EquipmentModifiers {melee=.22f,speed=-.03f},"More force in mounted contact; heavier handling."),
            I("cuirasses","Steel cuirasses",EquipmentSlot.Protection,EquipmentUsers.Cavalry,110,12,new EquipmentModifiers {resolve=.15f,melee=.08f,fatigue=.12f,speed=-.04f},"Protection improves confidence in close combat but adds weight."),
            I("reinforced-helmets","Reinforced helmets",EquipmentSlot.Protection,EquipmentUsers.Cavalry|EquipmentUsers.Melee,60,7,new EquipmentModifiers {resolve=.10f,melee=.05f,fatigue=.03f},"Close-combat confidence and protection."),
            I("remounts","Reserve remounts",EquipmentSlot.Mobility,EquipmentUsers.Cavalry,120,10,new EquipmentModifiers {fatigue=-.18f,supply=.06f},"Fresh horses improve mounted endurance."),
            I("campaign-saddles","Campaign saddles",EquipmentSlot.Mobility,EquipmentUsers.Cavalry,70,6,new EquipmentModifiers {speed=.05f,fatigue=-.10f},"Comfortable tack for sustained movement."),
            I("horseshoes","Improved horseshoes",EquipmentSlot.Tools,EquipmentUsers.Cavalry,55,6,new EquipmentModifiers {speed=.06f,fatigue=-.06f},"More reliable movement on long marches."),
            I("trumpeters","Mounted trumpeters",EquipmentSlot.Standard,EquipmentUsers.Cavalry,60,4,new EquipmentModifiers {cohesion=.12f,resolve=.12f},"Clear charge and regroup signals."),
            I("range-sights","Adjustable range sights",EquipmentSlot.Tools,EquipmentUsers.Rifle|EquipmentUsers.Light,85,8,new EquipmentModifiers {accuracy=.12f,range=.08f},"Improved sight adjustment for aimed fire.","rifles"),
            I("cleaning-kits","Weapon cleaning kits",EquipmentSlot.Tools,Shooters,40,4,new EquipmentModifiers {misfire=-.04f,accuracy=.04f},"Maintain locks and barrels during a campaign."),
            I("gun-sights","Artillery sighting instruments",EquipmentSlot.Tools,EquipmentUsers.Artillery,95,10,new EquipmentModifiers {accuracy=.15f,range=.05f},"Better gun laying and distance estimation.","arsenals"),
            I("crew-tools","Improved ramrods and sponges",EquipmentSlot.Tools,EquipmentUsers.Artillery,70,8,new EquipmentModifiers {reload=-.10f},"Faster, consistent servicing between shots."),
            I("reinforced-carriages","Reinforced gun carriages",EquipmentSlot.Protection,EquipmentUsers.Artillery,100,12,new EquipmentModifiers {accuracy=.08f,reload=-.04f,speed=-.03f},"Stable firing platform; heavier to haul.","arsenals"),
            I("horse-teams","Additional artillery horse teams",EquipmentSlot.Mobility,EquipmentUsers.Artillery,110,10,new EquipmentModifiers {speed=.12f,supply=-.06f},"Faster campaigning with more mouths to feed."),
            I("crew-harnesses","Gun crew hauling harnesses",EquipmentSlot.Mobility,EquipmentUsers.Artillery,55,6,new EquipmentModifiers {fatigue=-.12f,reload=-.04f},"Reduce crew strain while running guns back up."),
            I("gunner-pennants","Battery pennants",EquipmentSlot.Standard,EquipmentUsers.Artillery,65,5,new EquipmentModifiers {resolve=.18f,moraleReserve=8},"Help crews maintain confidence and battery identity."),
            I("sapper-tools","Sapper tool chests",EquipmentSlot.Tools,EquipmentUsers.Infantry|EquipmentUsers.Artillery,65,7,new EquipmentModifiers {supply=.10f,fatigue=-.04f,speed=-.03f},"Maintain camps and field routes; heavy tool load."),
            I("officer-spyglasses","Officer spyglasses",EquipmentSlot.Support,Professionals|EquipmentUsers.Artillery,85,5,new EquipmentModifiers {accuracy=.06f,cohesion=.08f},"Better observation and fire direction."),
            I("veteran-ncos","Veteran NCO cadre",EquipmentSlot.Support,EquipmentUsers.All,100,4,new EquipmentModifiers {resolve=.12f,cohesion=.10f,reload=-.04f},"Experienced leadership improves drills and discipline.","drill"),
            I("tempered-swords","Tempered infantry swords",EquipmentSlot.Weapon,EquipmentUsers.Melee,80,8,new EquipmentModifiers {melee=.20f},"Durable blades improve close-combat damage."),
            I("parrying-bucklers","Parrying bucklers",EquipmentSlot.Protection,EquipmentUsers.Melee,65,7,new EquipmentModifiers {melee=.08f,resolve=.12f,fatigue=.06f},"Close-combat confidence and handling; extra carried weight."),
            I("leather-jerkins","Padded leather jerkins",EquipmentSlot.Protection,EquipmentUsers.Infantry,60,6,new EquipmentModifiers {resolve=.10f,melee=.05f,heatFatigue=.08f},"Protection and confidence in melee; warmer in summer."),
            I("grip-gloves","Reinforced grip gloves",EquipmentSlot.Tools,EquipmentUsers.Melee|EquipmentUsers.Cavalry,40,4,new EquipmentModifiers {melee=.08f,fatigue=-.04f},"Reduce hand strain and improve weapon handling."),
            I("officer-gorgets","Officer gorgets",EquipmentSlot.Standard,Professionals|EquipmentUsers.Melee,75,5,new EquipmentModifiers {resolve=.18f,cohesion=.06f},"Recognisable officers improve confidence and formation recovery."),
            I("artillery-fuses","Reliable shell fuses",EquipmentSlot.Ammunition,EquipmentUsers.Artillery,85,9,new EquipmentModifiers {accuracy=.10f},"Consistent fusing and shot preparation improve effective precision.","arsenals"),
            I("light-limbers","Light battery limbers",EquipmentSlot.Mobility,EquipmentUsers.Artillery,90,10,new EquipmentModifiers {speed=.10f,fatigue=-.08f},"Lighter hauling equipment reduces crew strain."),
            I("field-forges","Mobile field forges",EquipmentSlot.Support,EquipmentUsers.Artillery|EquipmentUsers.Cavalry,110,12,new EquipmentModifiers {supply=.15f,reload=-.04f,speed=-.04f},"Maintain guns and tack in the field; slow workshop convoy.","industry"),
            I("campaign-maps","Regimental campaign maps",EquipmentSlot.Support,EquipmentUsers.All,70,3,new EquipmentModifiers {speed=.04f,supply=.06f},"Better route planning and organisation during campaign movement."),
            I("pack-mules","Regimental pack mules",EquipmentSlot.Support,EquipmentUsers.Infantry|EquipmentUsers.Artillery,85,6,new EquipmentModifiers {supply=.18f,fatigue=-.05f,speed=-.05f},"Carry reserves and spare kit at a slower convoy pace.")
        };
        // Keep original IDs as level one so existing saves retain their kit.
        public const int MaxEquipmentLevel=10;
        public static readonly EquipmentItem[] Items=BaseItems.SelectMany(item=>item.Slot==EquipmentSlot.Weapon?new[]{item}:Enumerable.Range(1,MaxEquipmentLevel).Select(level=>level==1?item:Upgrade(item,level))).ToArray();
        static EquipmentItem Upgrade(EquipmentItem item,int level)
        {
            var stats=item.Stats;float scale=level<=3?1+(level-1)*.5f:2+(level-3)*.15f;
            // Improve benefits while retaining each item's carrying/weather tradeoffs.
            float Benefit(float value,bool lower=false)=>lower?(value<0?value*scale:value):(value>0?value*scale:value);
            stats.accuracy=Benefit(stats.accuracy);stats.reload=Benefit(stats.reload,true);stats.melee=Benefit(stats.melee);
            stats.resolve=Benefit(stats.resolve);stats.fatigue=Benefit(stats.fatigue,true);stats.speed=Benefit(stats.speed);
            stats.range=Benefit(stats.range);stats.cohesion=Benefit(stats.cohesion);stats.supply=Benefit(stats.supply);
            stats.misfire=Benefit(stats.misfire,true);stats.heatFatigue=Benefit(stats.heatFatigue,true);stats.coldFatigue=Benefit(stats.coldFatigue,true);
            stats.ammunition=(int)Math.Ceiling(stats.ammunition*scale);stats.moraleReserve=(int)Math.Ceiling(stats.moraleReserve*scale);stats.grenades=(int)Math.Ceiling(stats.grenades*scale);
            return new EquipmentItem(item.Id+"-level-"+level,item.Name,item.Slot,item.Users,item.Cost*level,item.Materials*level,stats,item.Name+" improved to level "+level+". Stronger benefits with the original carrying and weather tradeoffs. Replaces the previous item in this slot.",item.Research,item.EarliestYear,item.Weapon,item.Id,level);
        }
        public static EquipmentModifiers Conditioned(EquipmentModifiers stats,int condition)
        {
            float scale=.5f+Math.Max(0,Math.Min(100,condition))*.005f;
            float Benefit(float value,bool lower=false)=>lower?(value<0?value*scale:value):(value>0?value*scale:value);
            stats.accuracy=Benefit(stats.accuracy);stats.reload=Benefit(stats.reload,true);stats.melee=Benefit(stats.melee);
            stats.resolve=Benefit(stats.resolve);stats.fatigue=Benefit(stats.fatigue,true);stats.speed=Benefit(stats.speed);
            stats.range=Benefit(stats.range);stats.cohesion=Benefit(stats.cohesion);stats.supply=Benefit(stats.supply);
            stats.misfire=Benefit(stats.misfire,true);stats.heatFatigue=Benefit(stats.heatFatigue,true);stats.coldFatigue=Benefit(stats.coldFatigue,true);
            // Issued ammunition and grenades stay finite counts; wear affects handling benefits.
            return stats;
        }
        public static string Label(EquipmentItem item)=>item.Name+(item.Slot==EquipmentSlot.Weapon?"":" · Lv "+item.Level+" / "+MaxEquipmentLevel);
        public static EquipmentItem Current(UnitLoadout loadout,EquipmentSlot slot)=>loadout.items.Select(Find).FirstOrDefault(i=>i!=null&&i.Slot==slot);
        public static EquipmentItem Next(UnitLoadout loadout,EquipmentSlot slot,EquipmentUsers users)
        {
            var current=Current(loadout,slot);
            if(slot==EquipmentSlot.Weapon&&current!=null)
            {
                string[] weapons={"percussion-muskets","flintlock-rifles","percussion-rifles","breechloaders","magazine-rifles","smokeless-rifles"};
                int index=Array.IndexOf(weapons,current.Id);
                if(current.Id=="percussion-muskets")index=1; // cap muskets progress to rifle muskets, not older flintlocks.
                return index>=0&&index+1<weapons.Length?Find(weapons[index+1]):null;
            }
            if(current==null&&slot==EquipmentSlot.Secondary&&(users&Shooters)!=0)return Find("sharpened-bayonets");
            if(current==null)return Items.FirstOrDefault(i=>i.Slot==slot&&i.Level==1&&i.Supports(users));
            return Items.FirstOrDefault(i=>i.Family==current.Family&&i.Level==current.Level+1&&i.Supports(users));
        }
        public static bool CanReplace(UnitLoadout loadout,EquipmentItem item)
        {var current=Current(loadout,item.Slot);return current==null||current.Family!=item.Family||current.Level<item.Level;}
        static readonly Dictionary<string,EquipmentItem> ById=Items.ToDictionary(i=>i.Id);
        public static EquipmentItem Find(string id)=>id!=null&&ById.TryGetValue(id,out var item)?item:null;
        public static string StatText(EquipmentItem item)
        {
            if(item.Weapon!="")
            {
                var weapon=MilitaryEquipment.Weapon(item.Weapon);
                return weapon.Precision.ToString("0.##")+"× precision · "+weapon.DamageMultiplier.ToString("0.##")+"× damage vs musket\n"
                    +weapon.EffectiveRange+"m range · "+weapon.Reload+"s reload"
                    +(weapon.Magazine>1?" · "+weapon.Magazine+" rounds · "+weapon.Cycle+"s bolt cycle":" · single shot");
            }
            var s=item.Stats;var parts=new List<string>();
            Action<string,float> add=(name,value)=>{if(value!=0)parts.Add(name+" "+(value>0?"+":"")+(value*100).ToString("0",System.Globalization.CultureInfo.InvariantCulture)+"%");};
            add("Accuracy",s.accuracy);add("Reload time",s.reload);add("Melee",s.melee);add("Morale resistance",s.resolve);
            add("Fatigue gain",s.fatigue);add("Movement",s.speed);add("Range",s.range);add("Cohesion recovery",s.cohesion);
            add("Supply efficiency",s.supply);add("Wet misfire chance",s.misfire);add("Heat fatigue",s.heatFatigue);add("Cold fatigue",s.coldFatigue);
            if(s.grenades!=0)parts.Add("Grenades per regiment +"+s.grenades);
            if(s.ammunition!=0)parts.Add("Ammunition +"+s.ammunition);if(s.moraleReserve!=0)parts.Add("Morale reserve +"+s.moraleReserve);
            return string.Join(" · ",parts);
        }
        public static EquipmentUsers CampaignUsers(string role)=>role=="Militia"||role=="Town militia"?EquipmentUsers.Militia:
            role=="Rifle"?EquipmentUsers.Rifle:role=="Light"?EquipmentUsers.Light:role=="Cavalry"?EquipmentUsers.Cavalry:role=="Artillery"?EquipmentUsers.Artillery:EquipmentUsers.Line;
    }
}
