using UnityEngine;

namespace EmpireBattle
{
    // Troop class is independent of issued equipment and earned progression.
    // Tier zero keeps the ordinary battle/campaign role defaults.
    public readonly struct RegimentBaseStats
    {
        public readonly float Health,Morale,Accuracy,Reload,Melee,Resolve,Speed;
        public RegimentBaseStats(float health,float morale,float accuracy,float reload,float melee,float resolve,float speed=1)
        {Health=health;Morale=morale;Accuracy=accuracy;Reload=reload;Melee=melee;Resolve=resolve;Speed=speed;}
        static readonly RegimentBaseStats[] Tiers={
            new RegimentBaseStats(100,100,1,1,1,1),
            new RegimentBaseStats(110,104,1.10f,.97f,1.06f,1.07f,1.12f),
            new RegimentBaseStats(120,108,1.22f,.94f,1.12f,1.15f,1.12f),
            new RegimentBaseStats(135,114,1.35f,.91f,1.25f,1.24f,1.08f),
            new RegimentBaseStats(150,120,1.50f,.88f,1.40f,1.34f,1.08f),
            new RegimentBaseStats(165,126,1.65f,.85f,1.50f,1.45f,1.12f),
            new RegimentBaseStats(180,133,1.82f,.82f,1.65f,1.58f,1.10f),
            new RegimentBaseStats(200,140,2,.78f,1.85f,1.75f,1.12f),
            new RegimentBaseStats(120,100,1,1,1,1),
            new RegimentBaseStats(155,115,1,1,1.30f,1.25f),
            new RegimentBaseStats(100,100,1,1,1,1),
            new RegimentBaseStats(120,115,1.25f,.85f,1,1.25f)
        };
        public static RegimentBaseStats For(int tier,UnitRole role)=>tier>=1&&tier<=Tiers.Length?Tiers[tier-1]:
            new RegimentBaseStats(100,100,UnitRoster.Accuracy(role),1,UnitRoster.Melee(role),UnitRoster.Resolve(role),UnitRoster.Speed(role));
        // Distinct coats remain visible with every rifle family; weapons and skin retain their atlas colours.
        public static Vector4 Uniform(int tier)
        {
            switch(tier)
            {
                case 2:return new Vector4(.16f,.34f,.20f,1);
                case 3:return new Vector4(.07f,.19f,.12f,1);
                case 4:return new Vector4(.07f,.12f,.27f,1);
                case 5:return new Vector4(.16f,.27f,.48f,1);
                case 6:return new Vector4(.27f,.36f,.29f,1);
                case 7:return new Vector4(.38f,.08f,.12f,1);
                case 8:return new Vector4(.72f,.68f,.53f,1);
                default:return Vector4.zero;
            }
        }
    }
}
