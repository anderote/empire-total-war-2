using System;
using EmpireBattle;
using EmpireCampaign.Geography;
using System.Collections.Generic;
using UnityEngine;

public static class CampaignBattleIntegrationVerification
{
    public static void VerifySaveCodec()
    {
        var c=new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),
            JsonUtility.FromJson<TheaterData>(Resources.Load<TextAsset>("WorldGeography/world").text),CampaignBalance.Load(),"france");
        string json=JsonUtility.ToJson(c.Save,true);System.IO.File.WriteAllText("Artifacts/CampaignBattleIntegration/native-save-codec.json",json);
        var candidate=JsonUtility.FromJson<CountryCampaignSave>(json);
        Require(c.Adopt(candidate,out var reason),"fresh native save restores: "+reason+" pending="+(candidate.pending==null?"null":candidate.pending.id));
    }
    static int checks;
    static void Require(bool value,string message)
    {checks++;if(!value)throw new Exception("Campaign battle integration: "+message);}
    static BattleConfig Config(BattleBiome biome=BattleBiome.Temperate,bool town=false,int fort=0,ArmySide defender=ArmySide.British,int units=2,int men=40)
    {
        return new BattleConfig{BattalionsPerSide=units,SoldiersPerBattalion=men,FrenchArtillery=0,BritishArtillery=0,
            RequireDeployment=true,RegionalMap=true,InitialSeparation=900,
            Map=new BattleMapProfile{RegionId="fixture",RegionName="Fixture",Biome=biome,Settlement=town,FortLevel=fort,
                DefenderSide=defender,SettlementRadius=town&&units>10?650:260,
                Forest=biome==BattleBiome.Woodland?.9f:.2f,Relief=biome==BattleBiome.Highlands?3:1,
                Snow=biome==BattleBiome.Snow?.9f:0,Aridity=biome==BattleBiome.Arid?.9f:0,Coast=biome==BattleBiome.Coastal}};
    }
    public static void Verify()
    {
        checks=0;VerifyGeography();VerifyTerrain();VerifyDeployment();VerifySiege();
        Debug.Log("CAMPAIGN_BATTLE_INTEGRATION_VERIFIED checks="+checks);
    }
    public static void VerifyTransition()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Campaign.unity");
        UnityEditor.EditorApplication.EnterPlaymode();
    }
    static void VerifyGeography()
    {
        var c=new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),
            JsonUtility.FromJson<TheaterData>(Resources.Load<TextAsset>("WorldGeography/world").text),CampaignBalance.Load(),"france");
        var atlas=new GeographyAtlas();var biomes=new HashSet<BattleBiome>();var examples=new Dictionary<BattleBiome,string>();
        foreach(var region in c.World.Catalog.regions)
        {
            var e=new CampaignEncounter{id="profile-"+region.id,regionId=region.id,attacker="france",defender=region.owner};
            e.garrison.Add(new CampaignRegiment{id="profile-militia",homeRegionId=region.id,role="Militia",weapon="musket",doctrine=1,soldiers=250});
            var profile=CampaignBattleSetup.Profile(c,e,atlas);biomes.Add(profile.Biome);
            if(!examples.ContainsKey(profile.Biome))examples.Add(profile.Biome,region.id);
            Require(profile.Valid&&profile.RegionId==region.id&&profile.RegionName==region.capital.name,"valid geographic profile: "+region.capital.name);
            Require(CampaignBattleSetup.StableSeed(region.id,e.id)==CampaignBattleSetup.StableSeed(region.id,e.id),"region/encounter seed repeatable");
            Require(CampaignBattleSetup.StableSeed(region.id,e.id)!=CampaignBattleSetup.StableSeed(region.id,e.id+"2"),"different encounter gets fresh terrain");
        }
        Require(biomes.Count>=4,"campaign geography produces multiple distinct map types");
        Debug.Log("CAMPAIGN_BATTLE_GEOGRAPHY_VERIFIED biomes="+string.Join(",",biomes));
        System.IO.Directory.CreateDirectory("Artifacts/CampaignBattleIntegration");
        var lines=new List<string>();foreach(var pair in examples)lines.Add(pair.Key+"\t"+pair.Value);
        System.IO.File.WriteAllLines("Artifacts/CampaignBattleIntegration/geographic-examples.tsv",lines);
    }
    static void VerifyTerrain()
    {
        foreach(BattleBiome biome in Enum.GetValues(typeof(BattleBiome)))
        {
            var config=Config(biome);var a=new BattlefieldTerrain(config);var b=new BattlefieldTerrain(Config(biome));
            for(int i=0;i<20;i++)
            {
                var point=new Vector2(-1200+i*121,-730+i*67);
                Require(a.HeightAt(point)==b.HeightAt(point),"repeatable terrain "+biome);
                Require(!float.IsNaN(a.HeightAt(point)),"finite regional height");
            }
            Require(a.WoodsCenters.Length==b.WoodsCenters.Length,"repeatable landmark counts");
            for(int i=0;i<a.WoodsCenters.Length;i++)Require(a.WoodsCenters[i]==b.WoodsCenters[i],"repeatable woodland placement");
            if(biome==BattleBiome.Arid)Require(a.WoodsCenters.Length==0&&a.FarmCenters.Length==0,"arid cover differs");
            if(biome==BattleBiome.Coastal)Require(a.IsBlocked(new Vector2(-1400,0))&&!a.IsBlocked(Vector2.zero),"coastal water excludes deployment");
        }
        var town=new BattlefieldTerrain(Config(town:true,fort:2));var gate=town.Profile.Gate;
        Require(town.IsBlocked(gate),"closed gate blocks movement");
        Require(town.BlocksLine(gate-Vector2.up*20,gate+Vector2.up*20),"thin gate blocks a ray between terrain samples");
        town.DamageGate(500);
        Require(town.GateOpen&&!town.IsBlocked(gate),"breached gate allows movement");
        Require(!town.BlocksLine(gate-Vector2.up*20,gate+Vector2.up*20),"breach opens line of fire");
        var wall=town.Walls[0];
        Require(town.BlocksLine(wall.Center-Vector2.right*20,wall.Center+Vector2.right*20),"thin wall blocks ray");
        Require(town.IsBlocked(town.TownCenters[0]),"buildings block movement");
        var invalid=Config();invalid.Map.Relief=float.NaN;bool rejected=false;
        try{new BattlefieldTerrain(invalid);}catch(ArgumentException){rejected=true;}
        Require(rejected,"invalid geography rejected");
    }
    static void VerifyDeployment()
    {
        foreach(bool settlement in new[]{false,true})foreach(ArmySide defender in Enum.GetValues(typeof(ArmySide)))
        {
            var w=new BattleWorld(Config(town:settlement,fort:settlement?2:0,defender:defender));
            var before=(Vector2[])w.Soldiers.Positions.Clone();var combat=new CombatSystem(w);var artillery=new ArtillerySystem(w);var ai=new TacticalAI(w);
            for(int i=0;i<20;i++){w.StepMovement(.1f);ai.Step(.1f);combat.Step(.1f);artillery.Step(.1f);w.Siege?.Step(.1f);}
            Require(w.Time==0&&w.DeathCount==0,"deployment freezes simulation");
            for(int i=0;i<before.Length;i++)Require(before[i]==w.Soldiers.Positions[i],"troops do not march during deployment");
            var own=w.Battalions[0];var enemy=w.Battalions[w.Config.FrenchBattalionCount];
            Require(!w.TryDeploy(enemy.Id,enemy.Position,enemy.Facing,out _),"enemy cannot be deployed by player");
            Require(!w.TryDeploy(own.Id,new Vector2(float.NaN,0),0,out _),"nonfinite deployment rejected");
            Require(!w.TryDeploy(own.Id,Vector2.zero,0,out _),"neutral zone deployment rejected");
            Require(!w.TryDeploy(own.Id,w.Battalions[1].Position,w.Battalions[1].Facing,out _),"overlapping deployment rejected");
            Require(w.TryDeploy(own.Id,own.Position,own.Facing,out _),"valid placement accepted");
            Require(w.BeginBattle(out var reason),"valid battle begins: "+reason);
            Require(!w.BeginBattle(out _)&&!w.TryDeploy(own.Id,own.Position,0,out _),"deployment cannot reopen");
            w.StepMovement(.1f);Require(w.Time>0,"simulation advances after start");
        }
        foreach(bool town in new[]{false,true})foreach(ArmySide defender in Enum.GetValues(typeof(ArmySide)))
        {
            var w=new BattleWorld(Config(town:town,fort:town?4:0,defender:defender,units:80,men:500));
            Require(w.Soldiers.Alive.Length==80000,"large armies preserve strength");
            Require(w.BeginBattle(out var reason),"80 regiments per side fit full footprints: "+reason);
        }
    }
    static void Put(BattleWorld w,BattalionState unit,Vector2 position)
    {
        for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)w.Soldiers.Positions[i]=position;
    }
    static void VerifySiege()
    {
        foreach(ArmySide defender in Enum.GetValues(typeof(ArmySide)))
        {
            var w=new BattleWorld(Config(town:true,fort:1,defender:defender,units:1));w.BeginBattle(out _);
            var attack=w.Battalions[defender==ArmySide.British?0:1];var defence=w.Battalions[defender==ArmySide.British?1:0];
            Put(w,attack,w.Terrain.Profile.Gate+Vector2.up*w.Terrain.Profile.GateDirection*15);
            for(int i=0;i<5;i++)w.Siege.Step(1);
            Require(w.Terrain.GateOpen&&w.DeathCount==0,"physical gate assault breaches without invented casualties");
            var center=w.Terrain.Profile.SettlementCenter;
            attack.Position=center;Put(w,attack,center+Vector2.right*200);Put(w,defence,center+Vector2.left*200);
            w.Siege.Step(3);Require(w.Siege.CaptureProgress==0,"formation center alone cannot capture");
            Put(w,attack,center);attack.Routing=true;w.Siege.Step(3);Require(w.Siege.CaptureProgress==0,"routing troops cannot capture");attack.Routing=false;
            for(int i=attack.SoldierStart;i<attack.SoldierStart+attack.SoldierCount;i++)w.Soldiers.KnockdownEnds[i]=100;
            w.Siege.Step(3);Require(w.Siege.CaptureProgress==0,"downed troops cannot capture");
            for(int i=attack.SoldierStart;i<attack.SoldierStart+attack.SoldierCount;i++)w.Soldiers.KnockdownEnds[i]=0;
            w.Siege.Step(5);Require(w.Siege.CaptureProgress==5,"actual troops advance occupation");
            Put(w,defence,center);w.Siege.Step(1);Require(w.Siege.Contested&&w.Siege.CaptureProgress==3,"defenders contest occupation");
            w.ApplyCasualties(defence.Id,defence.AliveCount);new CombatSystem(w).Step(.1f);
            Require(w.Outcome==BattleOutcome.Ongoing,"destroying defenders still requires town capture");
            w.Siege.Step(16);Require(w.Outcome==BattleOutcome.Ongoing,"occupation requires full duration");
            w.Siege.Step(1);Require(w.Outcome==(w.Siege.Attacker==ArmySide.French?BattleOutcome.FrenchVictory:BattleOutcome.BritishVictory),"capture awards attacking side");
            var defeat=new BattleWorld(Config(town:true,fort:1,defender:defender,units:1));defeat.BeginBattle(out _);
            var defeated=defeat.Battalions[defender==ArmySide.British?0:1];defeat.ApplyCasualties(defeated.Id,defeated.AliveCount);new CombatSystem(defeat).Step(.1f);
            Require(defeat.Outcome==(defender==ArmySide.French?BattleOutcome.FrenchVictory:BattleOutcome.BritishVictory),"failed siege awards defender");
        }
    }
}
