using System.Collections.Generic;

namespace EmpireBattle
{
    // A packed (x,z) long needs a spatial hash: long.GetHashCode() only XORs the
    // two coordinates, putting entire diagonals into one dictionary bucket.
    // Full key equality and insertion order are unchanged, preserving contacts.
    internal sealed class SpatialCellComparer : IEqualityComparer<long>
    {
        public static readonly SpatialCellComparer Instance = new SpatialCellComparer();
        public bool Equals(long a, long b) => a == b;
        public int GetHashCode(long key)
        {
            unchecked
            {
                ulong value = (ulong)key;
                value ^= value >> 33;
                value *= 0xff51afd7ed558ccdUL;
                value ^= value >> 33;
                value *= 0xc4ceb9fe1a85ec53UL;
                value ^= value >> 33;
                return (int)value;
            }
        }
    }
}
