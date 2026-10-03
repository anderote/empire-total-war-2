using UnityEngine;
namespace EmpireBattle
{
    public sealed partial class BattleBlood
    {
        int observedMeleeContact,meleeFrame=-1,meleeContactsThisFrame;
        public int MeleeSpraysEmitted {get;private set;}
        void EmitMeleeContacts(float time)
        {
            if(!material||!cameraView)return;
            if(meleeFrame!=Time.frameCount){meleeFrame=Time.frameCount;meleeContactsThisFrame=0;}
            observedMeleeContact=Mathf.Max(observedMeleeContact,world.MeleeContactSequence-BattleWorld.MeleeContactCapacity);
            while(world.TryGetMeleeContact(observedMeleeContact+1,out var contact))
            {
                if(contact.Time>time)break;
                observedMeleeContact++;
                // Corpses already emit their death spray. A parry or stock blow has
                // no cutting effect, and no old contact is replayed after a hitch.
                if(meleeContactsThisFrame>=8||time-contact.Time>.25f||contact.Outcome!=MeleeContactOutcome.Hit
                    ||contact.Action==MeleeAction.ButtStrike||contact.Target<0||!world.Soldiers.Alive[contact.Target])continue;
                bool rider=world.Battalions[world.Soldiers.BattalionIds[contact.Target]].Kind==UnitKind.Cavalry;
                Vector3 position=world.Terrain.SurfacePoint(contact.Position,rider?1.8f:1.1f);
                if((position-cameraView.transform.position).sqrMagnitude>SprayDrawDistance*SprayDrawDistance)continue;
                Vector3 direction=new Vector3(contact.Direction.x,.1f,contact.Direction.y);
                for(int i=0;i<BattleEffectTuning.BloodCount(12);i++)
                {
                    drops[cursor]=new Drop{Active=true,Position=position,
                        Velocity=direction*Range(1f,2.6f)+new Vector3(Range(-.45f,.45f),Range(.3f,1f),Range(-.45f,.45f)),
                        Born=time,Size=Range(.025f,.05f),Seed=Range(0,100)};
                    cursor=(cursor+1)%DropCapacity;
                }
                meleeContactsThisFrame++;MeleeSpraysEmitted++;
            }
        }
    }
}
