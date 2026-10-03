using UnityEngine;
namespace EmpireBattle
{
    public enum MeleeContactOutcome { Swing, Miss, Hit, Parry, Charge }
    public struct MeleeContact
    {
        public int Sequence, Attacker, Target;
        public float Time;
        public MeleeAction Action;
        public MeleeContactOutcome Outcome;
        public Vector2 Position, Direction;
    }
    public sealed partial class BattleWorld
    {
        // A bounded history survives multiple simulation ticks per rendered frame.
        public const int MeleeContactCapacity=4096;
        readonly MeleeContact[] meleeContacts=new MeleeContact[MeleeContactCapacity];
        public int MeleeContactSequence {get;private set;}
        public void RecordMeleeContact(int attacker,int target,MeleeAction action,MeleeContactOutcome outcome)
        {
            Vector2 position=Soldiers.Positions[attacker];
            Vector2 direction=target>=0?(Soldiers.Positions[target]-position).normalized:FormationMath.Forward(Soldiers.Facings[attacker]);
            if(outcome!=MeleeContactOutcome.Swing&&target>=0)position=Soldiers.Positions[target];
            int sequence=++MeleeContactSequence;
            meleeContacts[(sequence-1)%MeleeContactCapacity]=new MeleeContact{Sequence=sequence,Attacker=attacker,Target=target,Action=action,Outcome=outcome,Time=Time,Position=position,Direction=direction};
        }
        public bool TryGetMeleeContact(int sequence,out MeleeContact contact)
        {
            contact=default;
            if(sequence<=0||sequence>MeleeContactSequence||sequence<=MeleeContactSequence-MeleeContactCapacity)return false;
            contact=meleeContacts[(sequence-1)%MeleeContactCapacity];return contact.Sequence==sequence;
        }
    }
}
