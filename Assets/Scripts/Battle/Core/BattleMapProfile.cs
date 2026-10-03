using System;
using UnityEngine;

namespace EmpireBattle
{
    public enum BattleBiome { Temperate, Woodland, Highlands, Snow, Arid, Coastal }

    [Serializable]
    public sealed class BattleMapProfile
    {
        public string RegionId, RegionName;
        public BattleBiome Biome;
        public float Relief = 1f, Forest = .5f, Snow, Aridity;
        public bool Coast, Settlement;
        public int FortLevel;
        public ArmySide DefenderSide = ArmySide.British;
        public float SettlementRadius = 260f;
        public Vector2 SettlementCenter => new Vector2(0, DefenderSide == ArmySide.French ? -450f : 450f);
        public float GateDirection => DefenderSide == ArmySide.French ? 1f : -1f;
        public Vector2 Gate => SettlementCenter + Vector2.up * GateDirection * SettlementRadius;
        public Color GroundTint => Color.Lerp(Color.Lerp(new Color(.31f,.38f,.17f), new Color(.64f,.49f,.29f), Aridity), new Color(.84f,.88f,.90f), Snow);
        public bool Valid => Enum.IsDefined(typeof(BattleBiome), Biome) && Finite(Relief) && Relief>=.15f && Relief<=4f &&
            Unit(Forest) && Unit(Snow) && Unit(Aridity) && FortLevel>=0 && FortLevel<=4 &&
            Enum.IsDefined(typeof(ArmySide),DefenderSide) && Finite(SettlementRadius) && SettlementRadius>=160f && SettlementRadius<=650f;
        static bool Unit(float n)=>Finite(n)&&n>=0&&n<=1;
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
    }
}
