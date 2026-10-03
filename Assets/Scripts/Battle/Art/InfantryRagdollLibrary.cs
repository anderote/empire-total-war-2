using System;
using UnityEngine;
namespace EmpireBattle.Art
{
    public sealed class InfantryRagdollLibrary : ScriptableObject
    {
        public InfantryRagdollSet French, British;
    }
    [Serializable] public sealed class InfantryRagdollSet
    {
        public Mesh Mesh;
        public Material Material;
        public string[] Names;
        public int[] Parents;
        public Vector3[] Positions;
        public Quaternion[] Rotations;
        public InfantryBonePose[] Idle, Fire, Reload, March, Run, Charge, Mounted;
        [NonSerialized] public InfantryRagdollProp[] Props;
        [NonSerialized] public DetachedRagdollPart[] DetachedParts;
    }
    public sealed class DetachedRagdollPart
    {
        public Mesh Mesh;
        public int Bone;
        public DetachedEquipmentKind Kind;
        public bool Hat=>Kind==DetachedEquipmentKind.Hat;
    }
    public enum DetachedEquipmentKind { Weapon,Hat,Backpack,Standard }
    public struct InfantryRagdollProp
    {
        public int Bone;
        public Vector3 Center, Axis;
        public float Length, Radius;
    }
    [Serializable] public sealed class InfantryBonePose
    {
        public Vector3[] Positions;
        public Quaternion[] Rotations;
    }
}
