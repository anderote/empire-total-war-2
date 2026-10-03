using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class PoisonGasVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new Exception("Poison gas: "+message);}
    static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Hidden).Invoke(target,args);
    static void Time(BattleWorld world,float time)=>typeof(BattleWorld).GetProperty("Time").SetValue(world,time);
    static BattleWorld World(out ArtillerySystem guns)
    {
        var world=new BattleWorld(new BattleConfig {AssetReview=true,BattalionsPerSide=1,SoldiersPerBattalion=8,
            FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=false});
        guns=new ArtillerySystem(world);
        for(int i=0;i<world.Soldiers.Alive.Length;i++)world.Soldiers.Positions[i]=new Vector2(400+i,400);
        return world;
    }
    static void Release(ArtillerySystem guns,Vector3 position,int owner=-1)=>Call(guns,"Explode",new CannonProjectile {
        GunId=owner,Position=position,AmmunitionType=ArtilleryAmmunition.PoisonGas });
    public static void Verify(){VerifyGameplay();VerifySmoke();}
    public static void VerifyGameplay()
    {
        var progression=new UnitProgression();progression.Award(3250);
        for(int rank=1;rank<=5;rank++)
        {
            Require(progression.Learn(RegimentAbility.ArtilleryMunitions),"Rank can be learned");
            Require(progression.CanUseArtilleryAmmunition(ArtilleryAmmunition.PoisonGas)==(rank>=5),"Gas unlocks at rank five");
        }
        Require(progression.Valid&&progression.Copy().CanUseArtilleryAmmunition(ArtilleryAmmunition.PoisonGas),"Copied progression retains valid gas unlock");
        foreach(float dt in new[]{.05f,2f,9f,60f})
        {
            var w=World(out var guns);int enemy=w.Battalions[1].SoldierStart;
            w.Soldiers.Positions[0]=w.Soldiers.Positions[enemy]=new Vector2(100,0);
            w.Soldiers.Positions[1]=new Vector2(119,0);w.Soldiers.Positions[2]=new Vector2(121,0);
            Release(guns,new Vector3(100,3,0));
            Require(guns.Hazards[0].Position.y==w.Terrain.HeightAt(new Vector2(100,0)),"Air contact settles gas onto terrain");
            Require(w.Soldiers.Health[0]==94&&Mathf.Abs(w.Soldiers.Health[1]-99.7f)<.001f,"Immediate strong dose with edge falloff");
            Require((float)Call(guns,"HazardExposure",guns.Hazards[0],new Vector3(100,3,0))==0,"Airborne targets above the low cloud are clear");
            foreach(var shot in guns.Projectiles)Require(!shot.Active,"Gas releases no shrapnel");
            for(int i=0;i<Mathf.CeilToInt(45/dt);i++){Time(w,w.Time+dt);guns.Step(dt);}
            Require(!w.Soldiers.Alive[0]&&!w.Soldiers.Alive[enemy],"Prolonged gas kills either army");
            Require(Mathf.Abs(w.Soldiers.Health[1]-93.1f)<.002f,"Twenty-three edge doses, independent of timestep "+dt);
            Require(w.Soldiers.Health[2]==100,"Out-of-range troops remain untouched");
            Require(w.Deaths[w.DeadSoldiers[0]].Cause==DeathCause.PoisonGas&&w.Deaths[w.DeadSoldiers[0]].Impulse==Vector3.zero,"Gas cause recorded without explosive launch");
            guns.Step(60);Require(Mathf.Abs(w.Soldiers.Health[1]-93.1f)<.002f&&guns.Hazards[0].Remaining==0,"Expired gas cannot keep damaging");
        }
        {
            var w=World(out var guns);w.Soldiers.Positions[0]=new Vector2(100,0);w.Soldiers.Positions[1]=new Vector2(100,0);
            Release(guns,new Vector3(100,0,0));w.Soldiers.Positions[1]=new Vector2(130,0);
            Time(w,2);guns.Step(2);Require(w.Soldiers.Health[0]==88&&w.Soldiers.Health[1]==94,"Leaving gas stops damage immediately");
            Time(w,32);guns.Step(30);Require(!w.Soldiers.Alive[0]&&w.Soldiers.Alive[1],"Core kills after 32 seconds, escaping survivor remains alive");
        }
        {
            var w=new BattleWorld(new BattleConfig {BattalionsPerSide=1,SoldiersPerBattalion=8,EnableAI=false,EnableLandscape=false,FrenchArtillery=1,BritishArtillery=1});
            var guns=new ArtillerySystem(w);
            for(int i=0;i<w.Soldiers.Alive.Length;i++)w.Soldiers.Positions[i]=new Vector2(400+i,400);
            foreach(var gun in guns.Guns){gun.FireAtWill=false;gun.Position=new Vector2(gun.Id==0?300:100,0);}
            var target=guns.Guns[1];float health=target.Health;
            Release(guns,guns.CrewPosition(target,0),0);Require(target.Crew[0].Health==94&&target.Suppressed,"Gas harms and suppresses crew immediately");
            Time(w,45);guns.Step(45);
            Require(target.LivingCrew==0&&target.Health==health&&guns.Guns[0].Kills==4,"Prolonged exposure casualties credit firing battery without destroying carriage");
        }
        Debug.Log("POISON_GAS_VERIFIED: rank-5 unlock, 6 damage/2 seconds, 32-second core kill, terrain-height exposure, 45-second life, friendly fire, crew credit, no shrapnel/knockback, bounded yellow ground fog");
    }
    static void VerifySmoke()
    {
        var w=World(out _);var root=new GameObject("Gas smoke budget");var view=new GameObject("Gas smoke camera");
        try
        {
            var camera=view.AddComponent<Camera>();camera.transform.position=new Vector3(100,8,-30);camera.transform.LookAt(new Vector3(100,0,0));
            var smoke=root.AddComponent<MusketSmoke>();smoke.Initialize(w,camera);
            for(int i=0;i<400;i++)smoke.EmitPoisonGas(new Vector3(100,0,0),0,i,false);
            Require(smoke.ActiveGasPuffs==MusketSmoke.PoisonGasCapacity,"Bombardment respects 2048-puff gas limit");
            Time(w,3);Call(smoke,"LateUpdate");
            var puffs=(Array)typeof(MusketSmoke).GetField("puffs",Hidden).GetValue(smoke);
            var centers=(Vector3[])typeof(MusketSmoke).GetField("centers",Hidden).GetValue(smoke);
            for(int i=0;i<puffs.Length;i++)
            {
                object puff=puffs.GetValue(i);var type=puff.GetType();if(!(bool)type.GetField("PoisonGas").GetValue(puff))continue;
                var tint=(Color)type.GetField("Tint").GetValue(puff);
                Require(tint.r>tint.b*4&&tint.g>tint.b*4,"Gas is yellow, separate from lime dust");
                float height=centers[i].y-w.Terrain.HeightAt(new Vector2(centers[i].x,centers[i].z));
                Require(height>.5f&&height<.8f,"Fog centers remain low over actual terrain");
            }
            Time(w,100);Call(smoke,"LateUpdate");Require(smoke.ActiveGasPuffs==0,"Expiry returns the gas particle budget");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(view);}
    }
}
