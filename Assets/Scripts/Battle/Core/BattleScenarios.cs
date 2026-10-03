using System;
using UnityEngine;

namespace EmpireBattle
{
    public static class BattleScenarios
    {
        public sealed class Scenario
        {
            public readonly string Id, Name, Description;
            public Scenario(string id,string name,string description){Id=id;Name=name;Description=description;}
        }
        public static readonly Scenario[] All = {
            new Scenario("infantry-skirmish","Infantry Skirmish","5 infantry regiments and 2 cavalry units per side. Rolling farmland and woodland edges test volleys, movement and flanking."),
            new Scenario("hammer-anvil","Hammer and Anvil","35 cannons and 5 defending regiments face 10,000 infantry in columns. Flat, dry ground and aligned firing lanes test roundshot penetration and artillery defence."),
            new Scenario("grand-battle","Grand Battle","The existing full battle: 7,500 infantry, 2 cavalry units and 60 cannons against 15,000 infantry, 2 cavalry units and 4 cannons. Original rolling battlefield."),
            new Scenario("cavalry-charge","Cavalry Charge","6 cavalry units face 4 infantry regiments in line. Open grassland tests charge impact, melee and mounted recovery."),
            new Scenario("square-defence","Square Defence","5 infantry regiments start in square against 10 cavalry units. A wooded valley tests bracing, cavalry avoidance and formation defence."),
            new Scenario("fortified-assault","Fortified Assault","8 infantry regiments and 8 cannons attack 5 regiments and 2 cannons in a fortified town. Tests deployment, breaches and capture objectives."),
            new Scenario("winter-volley","Winter Volley","2 infantry regiments per side on snowy uplands, without cavalry or artillery. Tests close infantry combat, volley timing and terrain movement.")
        };
        public static Scenario Find(string id) => Array.Find(All,s=>s.Id==id) ?? All[2];
        public static BattleConfig Create(string id)
        {
            var scenario=Find(id);
            if(scenario.Id=="grand-battle"){var grand=BattleConfig.CreatePlaytest();grand.ScenarioId=scenario.Id;return grand;}
            var c=new BattleConfig {ScenarioId=scenario.Id,ExplicitArmyCounts=true,BattalionsPerSide=1,
                SoldiersPerBattalion=250,FrenchArtillery=0,BritishArtillery=0,FieldHalfSize=600,
                InitialSeparation=260,RegionalMap=true};
            var map=new BattleMapProfile {RegionId=scenario.Id,RegionName=scenario.Name,Biome=BattleBiome.Temperate,Relief=.8f,Forest=.3f};
            c.Map=map;
            switch(scenario.Id)
            {
                case "infantry-skirmish": c.FrenchBattalions=c.BritishBattalions=5;c.FrenchCavalry=c.BritishCavalry=2;c.Seed=1821;break;
                case "hammer-anvil":
                    c.FrenchBattalions=5;c.BritishBattalions=40;c.FrenchArtillery=35;c.InitialSeparation=300;c.Seed=1835;
                    c.PreserveInitialFormations=true;map.Biome=BattleBiome.Arid;map.Relief=.15f;map.Forest=0;map.Aridity=.65f;break;
                case "cavalry-charge":
                    c.FrenchCavalry=6;c.BritishBattalions=4;c.FieldHalfSize=500;c.Seed=1841;c.PreserveInitialFormations=true;
                    map.Relief=.35f;map.Forest=.03f;break;
                case "square-defence":
                    c.FrenchBattalions=5;c.BritishCavalry=10;c.FieldHalfSize=550;c.Seed=1848;
                    map.Biome=BattleBiome.Woodland;map.Relief=1.2f;map.Forest=.4f;break;
                case "fortified-assault":
                    c.FrenchBattalions=8;c.BritishBattalions=5;c.FrenchArtillery=8;c.BritishArtillery=2;
                    c.FieldHalfSize=700;c.InitialSeparation=500;c.RequireDeployment=true;c.Seed=1852;
                    map.Settlement=true;map.FortLevel=2;map.SettlementRadius=260;map.Forest=.22f;map.Relief=.8f;break;
                case "winter-volley":
                    c.FrenchBattalions=c.BritishBattalions=2;c.FieldHalfSize=400;c.InitialSeparation=220;c.Seed=1860;
                    map.Biome=BattleBiome.Snow;map.Snow=.85f;map.Forest=.12f;map.Relief=1.4f;break;
            }
            c.InitialFormations=new FormationKind[c.FrenchBattalionCount+c.BritishBattalionCount];
            if(scenario.Id=="hammer-anvil")for(int i=c.FrenchBattalionCount;i<c.InitialFormations.Length;i++)c.InitialFormations[i]=FormationKind.Column;
            if(scenario.Id=="square-defence")for(int i=0;i<c.FrenchInfantryCount;i++)c.InitialFormations[i]=FormationKind.Square;
            return c;
        }
        public static void Deploy(BattleWorld world)
        {
            if(world.Config.ScenarioId=="hammer-anvil")
            {
                for(int i=0;i<5;i++)Place(world,world.Battalions[i],new Vector2((i-2)*90,-120),0);
                for(int i=0;i<40;i++)Place(world,world.Battalions[5+i],new Vector2((i%35-17)*12,210+(i/35)*55),Mathf.PI);
            }
            else if(world.Config.ScenarioId=="cavalry-charge"||world.Config.ScenarioId=="square-defence")
            {
                foreach(var unit in world.Battalions)
                {
                    bool player=unit.Side==ArmySide.French;int count=player?world.Config.FrenchBattalionCount:world.Config.BritishBattalionCount;
                    int index=player?unit.Id:unit.Id-world.Config.FrenchBattalionCount;
                    Place(world,unit,new Vector2((index-(count-1)*.5f)*80,player?-180:180),player?0:Mathf.PI);
                }
            }
        }
        static void Place(BattleWorld world,BattalionState unit,Vector2 position,float facing)
        {
            unit.Position=unit.Destination=position;unit.Facing=unit.DesiredFacing=facing;
            for(int slot=0;slot<unit.SoldierCount;slot++)
            {
                int soldier=unit.SoldierStart+slot;
                world.Soldiers.Positions[soldier]=position+FormationMath.SlotOffset(unit,slot);
                world.Soldiers.Facings[soldier]=FormationMath.SlotFacing(unit,slot);
            }
        }
        public static void AlignArtillery(BattleWorld world,ArtillerySystem artillery)
        {
            if(world.Config.ScenarioId!="hammer-anvil")return;
            for(int i=0;i<artillery.Guns.Length;i++)
            {
                var gun=artillery.Guns[i];gun.Position=new Vector2((i-17)*12,-210);gun.Facing=0;
                gun.AmmunitionType=ArtilleryAmmunition.Roundshot;gun.TargetBattalion=5+i;gun.FireAtWill=true;
            }
        }
    }
}
