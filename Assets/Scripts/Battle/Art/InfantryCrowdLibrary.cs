using System;
using UnityEngine;
namespace EmpireBattle.Art
{
    // Baked poses are in soldier root space: Y up, forward +Z, feet on Y=0.
    // Each pose is one combined mesh using the same faction atlas/material.
    public sealed class InfantryCrowdLibrary : ScriptableObject
    {
        public InfantryCrowdSet French;
        public InfantryCrowdSet British;
        [NonSerialized]public Func<BattalionState,InfantryCrowdSet> ReviewResolver;
        [NonSerialized] CartridgeRifleLibrary cartridgeLibrary;
        [NonSerialized] readonly System.Collections.Generic.Dictionary<string,InfantryCrowdSet> roster = new System.Collections.Generic.Dictionary<string,InfantryCrowdSet>();
        public InfantryCrowdSet Resolve(BattalionState unit)
        {
            var review=ReviewResolver?.Invoke(unit);if(review!=null)return review;
            var outfit=NapoleonicOutfits.Infantry(unit);if(outfit!=null)return NapoleonicOutfits.WithGrenade(outfit,unit);
            if(CartridgeRifleLibrary.UsesCartridges(unit.Weapon))
            {
                if(!cartridgeLibrary)cartridgeLibrary=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
                if(cartridgeLibrary){var single=unit.Weapon.Id=="breechloader";var upgraded=unit.Side==ArmySide.French?(single?cartridgeLibrary.SingleFrench:cartridgeLibrary.French):(single?cartridgeLibrary.SingleBritish:cartridgeLibrary.British);if(upgraded!=null)return NapoleonicOutfits.WithGrenade(upgraded,unit);}
            }
            return NapoleonicOutfits.WithGrenade(ResolveRoster(unit),unit);
        }
        // Command equipment is independent of the rank-and-file weapon upgrade.
        public InfantryCrowdSet ResolveCommand(BattalionState unit)=>NapoleonicOutfits.Command(unit)??ResolveRoster(unit,true);
        InfantryCrowdSet ResolveRoster(BattalionState unit,bool command=false)
        {
            if(!command){var outfit=NapoleonicOutfits.Infantry(unit);if(outfit!=null)return outfit;}
            string faction=UnitRoster.ArtFaction(unit.Faction);
            string family=command?"line":unit.Role==UnitRole.Swordsman?"sword":(UnitRoster.Light(unit.Role)||(RegionalArmyRoster.Supports(faction)&&unit.Role==UnitRole.Militia))?"light":"line";
            if(family=="line"&&faction=="france")return French;
            if(family=="line"&&faction=="britain")return British;
            string key=faction+"-"+family;
            if(!roster.TryGetValue(key,out var set))
            {
                string path=(RegionalArmyRoster.Supports(faction)?"RegionalRoster/":"UnitRoster/")+key;
                var asset=Resources.Load<UnitVisualLibrary>(path+"/Library");
                if(!asset)asset=Resources.Load<UnitVisualReference>(path+"/Reference")?.Library;
                set=asset&&asset.BakeVersion>0?asset.Set:unit.Side==ArmySide.French?French:British;roster[key]=set;
            }
            return set;
        }
        public float IdleDuration = 3.333333f;
        public float FireDuration = 3.333333f;
        public float ReloadDuration = 5f;
    }
    [Serializable]
    public sealed class InfantryCrowdSet
    {
        public Vector3 MuzzleOffset = new Vector3(0f,1.45f,1.2f);
        public Vector3 LockOffset = new Vector3(.08f,1.45f,.35f);
        public Material Material;
        public Material GrenadeMaterial;
        public InfantryCrowdLod[] Lods;
    }
    [Serializable]
    public sealed class InfantryDeathVariant { public Mesh[] Poses; }
    [Serializable]
    public sealed class InfantryRecoveryVariant { public Mesh[] Knockdown, GetUp; }
    [Serializable]
    public sealed class InfantryLocomotionVariant
    {
        public Mesh[] Walk, March, Jog, Run, Charge;
        public System.Collections.Generic.IEnumerable<Mesh[]> Loops()
        { yield return Walk; yield return March; yield return Jog; yield return Run; yield return Charge; }
    }
    [Serializable]
    public sealed class InfantryCommandVariant { public Mesh[] Idle, March, PikeReady, PikeThrust, PikeKnockdown, PikeGetUp, PikeDeath, OfficerReady, OfficerSlash, OfficerFire, OfficerKnockdown, OfficerGetUp, OfficerDeath, KneelingIdle, KneelingStrike; }
    [Serializable]
    public sealed class InfantryMeleeClip { public MeleeAction Action; public Mesh[] Poses, Kneeling; }
    [Serializable]
    public sealed class InfantryCrowdLod
    {
        public InfantryCrowdLod Copy()=>(InfantryCrowdLod)MemberwiseClone();
        public Mesh[] Idle;
        public InfantryDeathVariant[] IdleVariants, UnderFireVariants;
        // Decorative command party, outside the simulated musket files.
        public InfantryCommandVariant[] Command;
        public Mesh[] GrenadeThrow;
        public Mesh[] Fire;
        public Mesh[] Reload;
        public Mesh[] KneelingReload, KneelingFire;
        // Shared standing-to-kneeling morphs; _PoseBlend is the eased kneel amount.
        public Mesh LowerToKneel, RiseToAim, RiseToIdle, KneelingAimTransition;
        public Mesh[] RiseToReload;
        public Mesh[] March, Run, Charge, Hit, Death;
        public Mesh[] Knockdown, GetUp;
        public InfantryRecoveryVariant[] RecoveryVariants;
        public InfantryMeleeClip[] Melee;
        // Stable per-soldier variants; legacy loops remain a fallback for older libraries.
        public InfantryLocomotionVariant[] Locomotion;
        public InfantryDeathVariant[] DeathVariants;
        public float MaxDistance;
    }
}
