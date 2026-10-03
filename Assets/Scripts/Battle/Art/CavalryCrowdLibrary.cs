using System;
using UnityEngine;
namespace EmpireBattle.Art
{
    public enum MountedMotion { Idle, Walk, Trot, Gallop, Charge, SlashLeft, SlashRight, Brake, TurnLeft, TurnRight, Stumble, HorseFall, RiderFall, RiderFallBack, RiderFallSide, RiderTumble, HorseWalk, HorseTrot, HorseGallop, HorseIdle, LanceReady, LanceThrust, MountedAim, MountedFire, MountedReload, PistolAim, PistolFire, PistolReload, LanceWalk, LanceTrot, LanceGallop, LanceCharge, CarbineCarryWalk, CarbineCarryTrot, CarbineCarryGallop, PistolCarryWalk, PistolCarryTrot, PistolCarryGallop }
    public sealed class CavalryCrowdLibrary:ScriptableObject
    {
        public CavalryCrowdSet French,British;
        public int OutfitBakeVersion;
        [NonSerialized] readonly System.Collections.Generic.Dictionary<string,CavalryCrowdSet> regional=new System.Collections.Generic.Dictionary<string,CavalryCrowdSet>();
        public CavalryCrowdSet Resolve(BattalionState unit)
        {
            var outfit=NapoleonicOutfits.Mounted(unit);if(outfit?.Lods!=null)return outfit;
            if(RegionalArmyRoster.Supports(unit.Faction))
            {
                if(!regional.TryGetValue(unit.Faction,out var set))
                {
                    var asset=Resources.Load<CavalryCrowdLibrary>("RegionalCavalry/"+unit.Faction+"/CavalryCrowdLibrary");
                    set=asset?asset.French:null;regional[unit.Faction]=set;
                }
                if(set?.Lods!=null)return set;
            }
            if(unit.Faction=="france")return French;
            if(unit.Faction=="britain")return British;
            return unit.Side==ArmySide.French?French:British;
        }
    }
    [Serializable]public sealed class CavalryCrowdSet
    {
        public Material Material;public CavalryCrowdLod[] Lods;
    }
    [Serializable]public sealed class CavalryCrowdLod
    {
        public MountedClip[] Clips;public float MaxDistance;
        public MountedClip Clip(MountedMotion motion){if(Clips!=null)foreach(var c in Clips)if(c.Motion==motion)return c;return null;}
    }
    [Serializable]public sealed class MountedClip
    {
        public MountedMotion Motion;public Mesh[] Poses;public bool Loop;
        public Vector3[] MuzzleSockets,LockSockets;
        public Vector3 Socket(float phase,bool muzzle=true)
        {var values=muzzle?MuzzleSockets:LockSockets;if(values==null||values.Length==0)return Vector3.zero;float frame=Mathf.Clamp01(phase)*(values.Length-1);int i=Mathf.FloorToInt(frame);return Vector3.Lerp(values[i],values[Math.Min(i+1,values.Length-1)],frame-i);}
    }
}
