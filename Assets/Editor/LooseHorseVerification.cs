using System;
using System.Collections.Generic;
using System.Reflection;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class LooseHorseVerification
{
    static void Require(bool value,string message){if(!value)throw new Exception("Loose horse: "+message);}
    static BattleWorld World(float chance=0,float field=300)
    {
        var w=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=40,BritishCavalry=1,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false,
            ShotHorseDeathChance=chance,FieldHalfSize=field,InitialSeparation=60});
        foreach(var u in w.Battalions)u.FireAtWill=false;
        return w;
    }
    static LooseHorse Release(BattleWorld w)
    {
        var cavalry=w.Battalions[2];int rider=cavalry.SoldierStart;
        w.Soldiers.Positions[rider]=new Vector2(0,40);
        for(int i=rider+1;i<rider+cavalry.SoldierCount;i++)w.KillSoldier(i,DeathCause.Melee,Vector3.zero,Vector3.zero);
        Require(w.KillSoldier(rider,DeathCause.Musket,Vector3.forward,Vector3.zero),"Rider killed");
        return w.LooseHorses[rider];
    }
    public static void Verify()
    {
        foreach(float dt in new[]{.025f,.05f,.1f})
        {
            var w=World(0,80);var cavalry=w.Battalions[2];
            MeleeEncounter.Place(w,cavalry,Vector2.zero,0);
            int alive=w.CountAlive(ArmySide.British);
            for(int i=cavalry.SoldierStart;i<cavalry.SoldierStart+cavalry.SoldierCount;i++)w.KillSoldier(i,DeathCause.Musket,Vector3.forward,Vector3.zero);
            Require(w.LooseHorses.Count==60&&w.DeathCount==60&&w.CountAlive(ArmySide.British)==alive-60,"Independent horse and rider bookkeeping");
            var exits=new HashSet<Vector2>();var edges=new HashSet<int>();
            foreach(var h in w.LooseHorses.Values)
            {
                exits.Add(h.Exit);edges.Add(h.Exit.x < -80?0:h.Exit.x > 80?1:h.Exit.y < -80?2:3);
                Require(h.Targetable&&w.Deaths[h.Rider].HorseSurvived,"Survivors remain targets");
            }
            Require(exits.Count>50&&edges.Count==4,"Random exits distributed around all four field edges");
            for(int tick=0;tick<Mathf.CeilToInt(40/dt);tick++)w.StepMovement(dt);
            foreach(var h in w.LooseHorses.Values)
            {
                Require(h.Escaped&&!h.Targetable,"Every horse leaves the field at timestep "+dt);
                Require(!w.ApplyHorseHit(h.Rider,200,DeathCause.Musket,Vector3.zero),"Escaped horse cannot be hit");
            }
            Require(w.DeathCount==60&&w.CountAlive(ArmySide.British)==alive-60,"Escape does not create extra soldier losses");
        }
        var certain=World(1);int victim=certain.Battalions[2].SoldierStart;
        certain.KillSoldier(victim,DeathCause.Musket,Vector3.forward,Vector3.zero);
        Require(!certain.Deaths[victim].HorseSurvived&&certain.LooseHorses.Count==0,"Horse can die with its rider");
        Require(CavalryCasualtyMotion.Sample(certain.Deaths[victim],5).HorseMotion==MountedMotion.HorseFall,"Fatal shot renders horse fall");
        var mixed=World(.25f);var repeat=World(.25f);
        for(int i=mixed.Battalions[2].SoldierStart;i<mixed.Soldiers.Alive.Length;i++)
        {
            mixed.KillSoldier(i,DeathCause.Musket,Vector3.zero,Vector3.zero);
            repeat.KillSoldier(i,DeathCause.Musket,Vector3.zero,Vector3.zero);
            Require(mixed.Deaths[i].HorseSurvived==repeat.Deaths[i].HorseSurvived,"Seeded survival is repeatable");
            if(mixed.LooseHorses.TryGetValue(i,out var horse))Require(horse.Exit==repeat.LooseHorses[i].Exit,"Seeded exit is repeatable");
        }
        Require(mixed.LooseHorses.Count>20&&mixed.LooseHorses.Count<59,"Default probability produces both outcomes");
        foreach(var formation in new[]{FormationKind.Line,FormationKind.Skirmish,FormationKind.Square})VerifyShooting(formation);
        VerifyDamage();VerifyArtillery();
        var review=World();review.Config.AssetReview=true;Release(review);
        review.ResetReviewBlock(2,10,1.65f,2f);
        Require(review.LooseHorses.Count==0,"Workbench roster reset also retires horses");
        Release(review);
        Require(review.LooseHorses.Count==1,"Recycled rider can release a fresh horse");
        CavalryCombatVerification.VerifySimulation();
        ArtilleryAmmunitionVerification.Verify();
        Debug.Log("LOOSE_HORSES_VERIFIED random exits at three timesteps, seeded joint deaths, musket targeting in line/skirmish/square, cannon hits, escape and casualty accounting");
    }
    static void VerifyShooting(FormationKind formation)
    {
        var w=World();var horse=Release(w);var shooters=w.Battalions[0];
        w.SetFormation(shooters.Id,formation);MeleeEncounter.Place(w,shooters,Vector2.zero,0);
        shooters.FormationReadyTime=0;shooters.FireAtWill=true;shooters.FireMode=InfantryFireMode.Volley;
        MeleeEncounter.Place(w,w.Battalions[1],new Vector2(-200,-200),0);
        var combat=new CombatSystem(w);int deaths=w.DeathCount;
        for(int tick=0;tick<80;tick++){w.StepMovement(.05f);combat.Step(.05f);}
        Require(shooters.ShotsFired>0&&horse.Health<100,"Actual "+formation+" fire targets surviving horse of destroyed squadron");
        Require(w.DeathCount==deaths&&shooters.Kills==0,"Horse hits do not duplicate rider kill credit");
        bool emitted=false;foreach(var shot in combat.Shots)if(shot.Sequence>0&&shot.TargetHorse==horse.Rider&&shot.Hit)emitted=true;
        Require(emitted,"Horse hit has a visible projectile event");
        var find=typeof(CombatSystem).GetMethod("FindLooseHorse",BindingFlags.Instance|BindingFlags.NonPublic);
        horse.Health=100;horse.Position=new Vector2(0,-40);
        Require(find.Invoke(combat,new object[]{shooters,0f})==null,"Horses behind firing arc are ignored");
        horse.Position=new Vector2(0,400);
        Require(find.Invoke(combat,new object[]{shooters,0f})==null,"Out-of-range horses are ignored");
        horse.Position=new Vector2(0,40);horse.Side=shooters.Side;
        Require(find.Invoke(combat,new object[]{shooters,0f})==null,"Own horses are ignored");
    }
    static void VerifyDamage()
    {
        var w=World();var horse=Release(w);int deaths=w.DeathCount;
        w.StepMovement(.5f);Vector2 hitPosition=horse.Position;
        Require(w.ApplyHorseHit(horse.Rider,40,DeathCause.Musket,Vector3.forward)&&horse.Health==60&&horse.Targetable,"Nonfatal horse wound");
        Require(w.ApplyHorseHit(horse.Rider,80,DeathCause.Musket,Vector3.forward)&&!horse.Targetable,"Fatal horse wound");
        Require(!w.ApplyHorseHit(horse.Rider,80,DeathCause.Musket,Vector3.forward),"Horse cannot die twice");
        w.StepMovement(2);
        Require(horse.Position==hitPosition&&w.DeathCount==deaths,"Dead horse stops without another rider death");
        var end=CavalryCasualtyMotion.Sample(horse.Death,w.Time);var later=CavalryCasualtyMotion.Sample(horse.Death,w.Time+100);
        Require(end.HorsePosition==later.HorsePosition&&end.HorseMotion==MountedMotion.HorseFall,"Horse corpse settles at its own death position");
    }
    static void VerifyArtillery()
    {
        foreach(var type in new[]{ArtilleryAmmunition.Roundshot,ArtilleryAmmunition.Canister,ArtilleryAmmunition.Explosive})
        {
            var w=World();var horse=Release(w);var artillery=new ArtillerySystem(w);int deaths=w.DeathCount;
            var shot=new CannonProjectile{Side=ArmySide.French,Active=true,AmmunitionType=type,Velocity=Vector3.forward*180};
            Vector3 feet=w.Terrain.SurfacePoint(horse.Position);
            if(type==ArtilleryAmmunition.Explosive)
            {
                shot.Position=feet;
                typeof(ArtillerySystem).GetMethod("Explode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(artillery,new object[]{shot});
            }
            else typeof(ArtillerySystem).GetMethod("TestRoundshot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(artillery,
                new object[]{shot,feet+Vector3.up-Vector3.forward*3,feet+Vector3.up+Vector3.forward*3,.05f});
            Require(horse.Health<=0&&w.DeathCount==deaths,type+" kills horse without duplicate soldier casualty");
            if(type==ArtilleryAmmunition.Canister)Require(!shot.Active&&shot.HitCount==1,"Pellet stops on horse");
        }
    }
}
