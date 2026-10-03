using System;

namespace EmpireBattle
{
    [Flags]
    public enum MilitaryDoctrine { None=0, Square=1, Maneuver=2, ControlledFire=4, LightInfantry=8 }

    // Immutable equipment definitions. These are gameplay tuning, not historic measurements.
    public sealed class InfantryWeapon
    {
        public readonly string Id,Name,Research;
        public readonly int EarliestYear,Magazine;
        public readonly float EffectiveRange,AimedRange,Reload,Cycle,Precision,DamageMultiplier,Cost,Materials;
        public InfantryWeapon(string id,string name,string research,int year,float range,float aimed,float reload,
            float precision,float cost,float materials,int magazine=1,float cycle=0,float damageMultiplier=1)
        {Id=id;Name=name;Research=research;EarliestYear=year;EffectiveRange=range;AimedRange=aimed;Reload=reload;
            Precision=precision;DamageMultiplier=damageMultiplier;Cost=cost;Materials=materials;Magazine=magazine;Cycle=cycle;}
    }
    public static class MilitaryEquipment
    {
        public const MilitaryDoctrine AllDoctrine=MilitaryDoctrine.Square|MilitaryDoctrine.Maneuver|MilitaryDoctrine.ControlledFire|MilitaryDoctrine.LightInfantry;
        public static readonly InfantryWeapon[] Weapons={
            new InfantryWeapon("carbine","Cavalry carbine","",1800,90,180,20,.8f,180,15),
            new InfantryWeapon("pistol","Flintlock pistol","",1800,40,100,18,.55f,180,12),
            new InfantryWeapon("musket","Flintlock musket","",1500,150,247,15,1,180,15),
            new InfantryWeapon("rifle","Specialist flintlock rifle","rifles",1800,225,340,24,1.75f,270,25,damageMultiplier:1.15f),
            new InfantryWeapon("percussion","Percussion musket","percussion",1830,150,247,15,1.15f,210,20,damageMultiplier:1.10f),
            new InfantryWeapon("rifle-musket","Expanding-bullet rifle musket","rifle-muskets",1850,300,430,18,2.2f,290,28,damageMultiplier:1.30f),
            new InfantryWeapon("breechloader","Single-shot breechloader","breechloading",1840,320,460,8,2.7f,350,32,damageMultiplier:1.45f),
            new InfantryWeapon("magazine","Black-powder magazine rifle","magazines",1880,340,480,20,3.5f,430,40,8,2.5f,1.70f),
            new InfantryWeapon("smokeless","Smokeless magazine rifle","smokeless",1886,360,500,20,4.25f,480,45,8,2.5f,2f)
        };
        public static float MisfireChance(string weapon,float wetness)
        {
            bool flintlock=weapon=="musket"||weapon=="rifle";
            float wet=Math.Max(0,Math.Min(1,wetness));
            return flintlock?.025f+wet*.155f:.005f+wet*.035f;
        }
        public static InfantryWeapon Weapon(string id)=>Array.Find(Weapons,w=>w.Id==id);
        public static bool ValidDoctrine(int value)=>value>=0&&(value&~(int)AllDoctrine)==0;
        public static bool Has(int value,MilitaryDoctrine doctrine)=>(value&(int)doctrine)!=0;
        public static string TrainingName(int value)
        {
            string result=Has(value,MilitaryDoctrine.Square)?"square":"basic drill";
            if(Has(value,MilitaryDoctrine.Maneuver))result+=", maneuver";
            if(Has(value,MilitaryDoctrine.ControlledFire))result+=", controlled volleys";
            if(Has(value,MilitaryDoctrine.LightInfantry))result+=", skirmish";
            return result;
        }
    }
}
