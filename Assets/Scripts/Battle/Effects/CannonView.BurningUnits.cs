using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class CannonView
    {
        public const int BurningUnitCapacity=128;
        struct BurningUnit {public int Soldier;public float Until,Strength,NextSmoke;}
        readonly BurningUnit[] burningUnits=new BurningUnit[BurningUnitCapacity];
        readonly int[] fireSmokeSequence=new int[128];
        int[] soldierBurnSlot;
        int burnCursor;
        BattleCasualtyView burningCasualties;
        public int VisibleBurningUnits {get;private set;}
        public int FireCards=>fireSprites?.Cards??0;
        public int FireDrawCalls=>fireSprites?.DrawCalls??0;

        void InitializeBurningUnits()
        {
            soldierBurnSlot=new int[world.Soldiers.Alive.Length];System.Array.Fill(soldierBurnSlot,-1);
            burningCasualties=GetComponent<BattleCasualtyView>();
            artillery.SoldierIgnited+=IgniteSoldier;
        }
        void IgniteSoldier(int soldier,float strength)
        {
            if(soldier<0||soldier>=soldierBurnSlot.Length)return;
            if((world.SoldierPosition(soldier,TimeNow)-cameraView.transform.position).sqrMagnitude>225f*225f)return;
            int slot=soldierBurnSlot[soldier];
            if(slot<0)
            {
                // Reuse expired slots before evicting a live emitter. The budget is fixed.
                slot=burnCursor;
                for(int i=0;i<BurningUnitCapacity;i++)
                {int candidate=(burnCursor+i)%BurningUnitCapacity;if(burningUnits[candidate].Until<=TimeNow){slot=candidate;break;}}
                var old=burningUnits[slot];
                if(old.Until>0)soldierBurnSlot[old.Soldier]=-1;
                burningUnits[slot]=new BurningUnit {Soldier=soldier};
                soldierBurnSlot[soldier]=slot;burnCursor=(slot+1)%BurningUnitCapacity;
            }
            burningUnits[slot].Until=TimeNow+5f;
            burningUnits[slot].Strength=Mathf.Clamp(strength,.35f,1f);
        }
        void DrawBurningUnits()
        {
            VisibleBurningUnits=0;
            for(int i=0;i<burningUnits.Length;i++)
            {
                var burn=burningUnits[i];if(burn.Until<=0)continue;
                if(burn.Until<=TimeNow){soldierBurnSlot[burn.Soldier]=-1;burningUnits[i]=default;continue;}
                int id=burn.Soldier;bool alive=world.Soldiers.Alive[id];
                Vector3 root=world.SoldierPosition(id,TimeNow);
                if(!alive&&burningCasualties&&burningCasualties.HasPhysicalCorpse(id))root=burningCasualties.BloodAnchor(id);
                if((root-cameraView.transform.position).sqrMagnitude>225f*225f)continue;
                bool standing=alive&&!world.Soldiers.IsKnockedDown(id,TimeNow);
                float fade=Mathf.Clamp01((burn.Until-TimeNow)/2f)*burn.Strength;
                float height=standing?.8f:.42f;
                Vector3 right=new Vector3(Mathf.Cos(world.Soldiers.Facings[id]),0,-Mathf.Sin(world.Soldiers.Facings[id]));
                Vector3 nearSide=cameraView.transform.position-root;nearSide.y=0;nearSide=nearSide.normalized*.22f;
                for(int part=0;part<3;part++)
                {
                    Vector3 point=root+nearSide+right*((part-1)*.2f)+Vector3.up*(standing?.25f+part*.35f:.1f);
                    fireSprites.Add(point,standing?.7f:.7f,height,id*17+part,fade);
                }
                if(TimeNow>=burn.NextSmoke)
                {
                    smoke.EmitFireSmoke(root+Vector3.up*(standing?1.25f:.4f),TimeNow,.65f,fade);
                    burn.NextSmoke=TimeNow+.4f;burningUnits[i]=burn;
                }
                VisibleBurningUnits++;
            }
        }
    }
}
