using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Only cleared recordings belong here. Empty sets retain procedural fallbacks.</summary>
    [CreateAssetMenu(menuName = "Empire Battle/Audio bank")]
    public sealed class BattleAudioBank : ScriptableObject
    {
        public AudioClip MarchingLoop, MarchMusic;
        public AudioClip HoofWalk, Gallop, Nature, Rain;
        public AudioClip[] Tack, Wheel, Effort, HorseCall;
        public AudioClip[] Musket, Cannon, Dirt, Shell, Volley, Echo;
        public AudioClip[] Equipment, Cartridge, Ramrod, Lock;
        public AudioClip[] DrumCall, CommandReady, CommandFire, CommandReload;
        public TextAsset Credits;

        public AudioClip[] Clips(string kind)
        {
            switch(kind)
            {
                case "DrumCall": return DrumCall;
                case "CommandReady": return CommandReady;
                case "CommandFire": return CommandFire;
                case "CommandReload": return CommandReload;
                case "Tack": return Tack;
                case "Wheel": return Wheel;
                case "Effort": return Effort;
                case "HorseCall": return HorseCall;
                case "Musket": return Musket;
                case "Cannon": return Cannon;
                case "Dirt": return Dirt;
                case "Shell": return Shell;
                case "Volley": return Volley;
                case "Echo": return Echo;
                case "Equipment": return Equipment;
                case "Cartridge": return Cartridge;
                case "Ramrod": return Ramrod;
                case "Lock": return Lock;
                default: return null;
            }
        }
    }
}
