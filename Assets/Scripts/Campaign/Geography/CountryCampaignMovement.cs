using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCampaign.Geography
{
    public sealed partial class CountryCampaign
    {
        public const int MaxArmyRegiments=80,MaxReinforcingArmies=4;
        public const float InterceptionRangeKm=40,SettlementRangeKm=25,MergeRangeKm=5;
        public readonly CampaignMovementTerrain Terrain;
        public CampaignMapPoint Position(CampaignArmy a)
        {
            if(a.mapPositionKnown)return new CampaignMapPoint(a.longitude,a.latitude);
            var p=World.Catalog.regions.First(r=>r.id==a.regionId).capital;return new CampaignMapPoint(p.lon,p.lat);
        }
        public CampaignMapPoint CityPosition(string region)
        {var p=World.Catalog.regions.First(r=>r.id==region).capital;return new CampaignMapPoint(p.lon,p.lat);}
        public CampaignMapPoint EncounterPosition(CampaignEncounter e)=>e.mapPositionKnown?new CampaignMapPoint(e.longitude,e.latitude):CityPosition(e.regionId);
        public bool AtSettlement(CampaignArmy a,string region=null)=>a!=null&&!AtSea(a)&&CampaignMovementTerrain.Distance(Position(a),CityPosition(region??a.regionId))<=MergeRangeKm;
        public bool ForcesCanMeet(CampaignArmy a,CampaignArmy b)=>a!=null&&b!=null&&!AtSea(a)&&!AtSea(b)&&CampaignMovementTerrain.Distance(Position(a),Position(b))<=MergeRangeKm;
        public CampaignGround ArmyGround(CampaignArmy a)=>Terrain.Ground(Position(a));
        void PlaceAtCity(CampaignArmy a,string region){a.regionId=region;var p=CityPosition(region);a.longitude=p.lon;a.latitude=p.lat;a.mapPositionKnown=true;}
        void Place(CampaignArmy a,GeoPoint p)
        {a.longitude=p.lon;a.latitude=p.lat;a.mapPositionKnown=true;var region=Terrain.Ground(p).regionId;if(Active(region))a.regionId=region;}
        static void ClearMarch(CampaignArmy a)
        {a.marchTarget=a.marchNext="";a.marchProgress=0;if(a.marchPath==null)a.marchPath=new List<CampaignMapPoint>();else a.marchPath.Clear();a.marchArmyId="";a.marchToSettlement=false;}
        bool CanTraverse(string actor,string region)=>Active(region)&&Passable(actor,region,false);
        public CampaignLandPath LandPath(CampaignArmy army,GeoPoint destination)=>Terrain.Find(Position(army),destination,r=>CanTraverse(army.owner,r));
        public List<CampaignMapPoint[]> MovementBoundary(CampaignArmy army)=>Terrain.ReachBoundary(Position(army),Math.Max(0,army.movement-army.movementDebt),
            r=>CanTraverse(army.owner,r),p=>InterceptionAt(army.owner,p)!=null||FortAt(army.owner,p)!=null);
        CampaignArmy InterceptionAt(string actor,GeoPoint p)=>Save.armies.Where(a=>AtWar(actor,a.owner)&&!AtSea(a)&&a.Soldiers>0&&
            CampaignMovementTerrain.Distance(Position(a),p)<=InterceptionRangeKm&&!float.IsInfinity(Terrain.SegmentCost(Position(a),p,r=>CanTraverse(actor,r),out _)))
            .OrderBy(a=>CampaignMovementTerrain.Distance(Position(a),p)).ThenBy(a=>a.id,StringComparer.Ordinal).FirstOrDefault();
        string FortAt(string actor,GeoPoint p)=>Save.activeRegions.FirstOrDefault(r=>AtWar(actor,World.State(r).owner)&&Buildings(r,"Fortress")>0&&
            CampaignMovementTerrain.Distance(CityPosition(r),p)<=SettlementRangeKm&&!float.IsInfinity(Terrain.SegmentCost(CityPosition(r),p,id=>CanTraverse(actor,id),out _)));
        bool SiegeInRange(CampaignArmy a,string region)=>CampaignMovementTerrain.Distance(Position(a),CityPosition(region))<=InterceptionRangeKm+4;
        public bool CanMarchTo(string actor,string armyId,float longitude,float latitude,out string reason,string targetArmyId="",bool settlement=false)
        {
            var a=Army(armyId);reason="Select your army during your turn.";
            if(!CanOrder(actor)||a==null||a.owner!=actor)return false;
            if(AtSea(a)||UnderSiegeOrders(a)||TrainingBusy(a)){reason="Finish the voyage, training or siege before marching.";return false;}
            var destination=new CampaignMapPoint(longitude,latitude);var ground=Terrain.Ground(destination);
            if(ground.blocked||!Active(ground.regionId)){reason=ground.blocked?"Impassable terrain or water; use a mountain pass or sea transport.":"Outside the active campaign.";return false;}
            if(!CanTraverse(actor,ground.regionId)){reason="Declare war or obtain military access first.";return false;}
            var target=targetArmyId==""?null:Army(targetArmyId);
            if(targetArmyId!=""&&(target==null||target==a||AtSea(target)||target.owner!=actor&&!AtWar(actor,target.owner)))
            {reason="Choose a friendly army to join or an enemy army to attack.";return false;}
            if(CampaignMovementTerrain.Distance(Position(a),destination)<.05f&&target==null&&!settlement){reason="The army is already here.";return false;}
            if(AtSettlement(a)&&Besieged(a.regionId)&&!Save.sieges.Any(s=>s.regionId==a.regionId&&Army(s.armyId)?.id==targetArmyId))
            {reason="Attack the besieging army to break out.";return false;}
            var path=LandPath(a,destination);
            if(path==null){reason="No traversable land path. Check mountain passes, access rights or sea transport.";return false;}
            reason=MovementPreview(a,path,destination);return true;
        }
        string MovementPreview(CampaignArmy a,CampaignLandPath path,GeoPoint destination)
        {
            int turns=path.cost<=a.movement-a.movementDebt?0:(int)Math.Ceiling((path.cost-a.movement+a.movementDebt)/Math.Max(1,MovementAllowance(a)));
            bool interception=path.points.Any(p=>{var enemy=InterceptionAt(a.owner,p);return enemy!=null&&CanObserve(enemy,a.owner)||FortAt(a.owner,p)!=null;});
            return (int)Math.Ceiling(path.distance)+" km / "+(int)Math.Ceiling(path.cost)+" movement points / "+
                (turns==0?"this turn":turns+" more turn(s)")+" / "+Terrain.Ground(destination).name+
                (path.hazardous?" / attrition risk":"")+(interception?" / enemy interception zone":"");
        }
        public bool MarchTo(string actor,string armyId,float longitude,float latitude,out string reason,string targetArmyId="",bool settlement=false)
        {
            if(!CanMarchTo(actor,armyId,longitude,latitude,out reason,targetArmyId,settlement))return false;
            var a=Army(armyId);var p=new CampaignMapPoint(longitude,latitude);var path=LandPath(a,p);
            var joining=Army(targetArmyId);
            if(joining!=null&&joining.owner==actor&&ForcesCanMeet(a,joining)&&a.units.Count+joining.units.Count<=MaxArmyRegiments&&
                Transfer(actor,a.id,joining.id,a.units.Select(u=>u.id).ToArray(),out reason))return true;
            ClearMarch(a);a.stance="March";a.marchTarget=Terrain.Ground(p).regionId;a.marchPath=path.points;a.marchToSettlement=settlement;a.marchArmyId=targetArmyId;
            if(actor==Save.playerFaction)Save.openingMarch=true;return AdvanceLandMarch(a,out reason);
        }
        bool AdvanceLandMarch(CampaignArmy a,out string reason)
        {
            reason="March queued for the next turn.";
            if(a.marchTarget==""||a.movement<=0)return true;
            if(a.marchArmyId!="")
            {
                var target=Army(a.marchArmyId);
                if(target==null||AtSea(target)){ClearMarch(a);reason="Target army is no longer available.";return false;}
                var path=LandPath(a,Position(target));if(path==null){ClearMarch(a);reason="Target army can no longer be reached.";return false;}
                a.marchPath=path.points;a.marchTarget=target.regionId;
            }
            while(a.marchPath.Count>0&&a.movement>0&&Save.pending==null&&a.Soldiers>0)
            {
                var from=Position(a);var next=a.marchPath[0];float distance=CampaignMovementTerrain.Distance(from,next);
                // Execute in short segments: no path can skip coastlines, access borders or interception circles.
                var step=distance<=3?next:CampaignMovementTerrain.Lerp(from,next,3/distance);
                float cost=Terrain.SegmentCost(from,step,r=>CanTraverse(a.owner,r),out _);
                if(float.IsInfinity(cost)){ClearMarch(a);reason="March halted: terrain or military access changed.";return false;}
                float fraction=Math.Min(1,Math.Max(0,a.movement-a.movementDebt)/Math.Max(.001f,cost));var at=CampaignMovementTerrain.Lerp(from,step,fraction);
                float spent=cost*fraction+a.movementDebt;int whole=(int)Math.Floor(spent+.00001f);a.movement=Math.Max(0,a.movement-whole);a.movementDebt=Math.Max(0,spent-whole);Place(a,at);
                if(CampaignMovementTerrain.Distance(at,next)<.01f)a.marchPath.RemoveAt(0);
                var enemy=InterceptionAt(a.owner,at);
                if(enemy!=null&&CampaignMovementTerrain.Distance(Position(enemy),from)<=InterceptionRangeKm&&
                    CampaignMovementTerrain.Distance(Position(enemy),at)>CampaignMovementTerrain.Distance(Position(enemy),from)+.001f)enemy=null;
                if(enemy!=null)
                {
                    enemy.lastInterceptTurn=Save.world.turn;
                    bool cityAssault=a.marchToSettlement&&AtSettlement(enemy,a.marchTarget);
                    string city=a.marchTarget;ClearMarch(a);
                    if(cityAssault)CreateEncounter(a,city);else CreateEncounter(a,a.regionId,interceptor:enemy);
                    reason="Enemy zone of control: resolve the field battle before continuing.";return true;
                }
                string fort=FortAt(a.owner,at);
                if(fort!=null&&CampaignMovementTerrain.Distance(CityPosition(fort),from)<=SettlementRangeKm&&CampaignMovementTerrain.Distance(CityPosition(fort),at)>CampaignMovementTerrain.Distance(CityPosition(fort),from)+.001f)fort=null;
                if(fort!=null){ClearMarch(a);CreateEncounter(a,fort);reason="Fortified settlement blocks the approach.";return true;}
                if(a.marchToSettlement&&a.regionId==a.marchTarget&&AtWar(a.owner,World.State(a.marchTarget).owner)&&SiegeInRange(a,a.marchTarget))
                {string city=a.marchTarget;ClearMarch(a);CreateEncounter(a,city);reason="Settlement assault awaits resolution.";return true;}
            }
            if(a.marchPath.Count==0)
            {
                string targetId=a.marchArmyId;ClearMarch(a);var target=Army(targetId);
                if(target!=null&&target.owner==a.owner&&ForcesCanMeet(a,target))
                {
                    if(target.units.Count+a.units.Count<=MaxArmyRegiments&&Transfer(a.owner,a.id,target.id,a.units.Select(u=>u.id).ToArray(),out reason))return true;
                    reason="Armies have met. Select regiments to transfer; maximum 80 per army.";return true;
                }
                reason="Army arrived in the field.";
            }
            else reason="March paused at its current position; remaining route continues next turn.";
            return true;
        }
        public int MovementAllowance(CampaignArmy a)=>(int)(ArmyMovement(a)*Math.Max(.5f,a.supply)*
            (a.units.Count==0?1:a.units.Min(u=>Math.Max(.25f,1+u.loadout.Modifiers.speed))));
        void ApplyTerrainAttrition(CampaignArmy a)
        {
            if(AtSettlement(a))return;
            var ground=ArmyGround(a);float loss=ground.attrition;
            bool winter=(Save.world.turn-1+Balance.startSeason)%2==1;
            if(winter&&a.latitude>55)loss=Math.Max(loss,.025f);
            a.supply=Math.Max(0,a.supply-ground.supplyLoss);
            if(loss<=0)return;
            int before=a.Soldiers;Casualties(a,loss*(a.stance=="Fortify"?.65f:1));
            Event(a.name+" lost "+(before-a.Soldiers)+" troops to "+ground.name.ToLowerInvariant()+" attrition.");
        }
        bool ValidMapMarch(CampaignArmy a)
        {
            if(!a.mapPositionKnown||!CampaignMovementTerrain.Valid(Position(a))||float.IsNaN(a.movementDebt)||float.IsInfinity(a.movementDebt)||a.movementDebt<0||a.movementDebt>=1||a.marchPath==null||a.marchPath.Count>10000||a.marchArmyId==null||a.marchArmyId.Length>100||a.marchTarget==null||a.marchNext!=""||a.marchProgress!=0)return false;
            var ground=Terrain.Ground(Position(a));if(!AtSea(a)&&(ground.blocked||ground.regionId!=a.regionId))return false;
            if(a.marchTarget=="")return a.marchPath.Count==0&&a.marchArmyId==""&&!a.marchToSettlement;
            if(!Active(a.marchTarget)||AtSea(a)||a.marchPath.Count==0)return false;
            return a.marchPath.All(p=>CampaignMovementTerrain.Valid(p)&&!Terrain.Ground(p).blocked&&Active(Terrain.Ground(p).regionId))&&Terrain.Ground(a.marchPath.Last()).regionId==a.marchTarget;
        }
        bool MigrateMapMovement(CountryCampaignSave c)
        {
            if(c.world.regions==null||c.pending!=null&&!Active(c.pending.regionId))return false;
            foreach(var a in c.armies)
            {
                if(a==null||!Active(a.regionId)||!ValidMarch(a))return false;
                var p=CityPosition(a.regionId);
                if(a.marchNext!=""&&a.marchProgress>0)p=CampaignMovementTerrain.Lerp(p,CityPosition(a.marchNext),a.marchProgress/(float)Math.Ceiling(graph[a.regionId][a.marchNext]));
                if(!AtSea(a)&&Terrain.Ground(p).blocked)p=CityPosition(a.regionId);
                string destination=a.marchTarget;Place(a,p);a.marchPath=new List<CampaignMapPoint>();ClearMarch(a);
                if(destination!=""&&!AtSea(a))
                {
                    bool Access(string r){if(!Active(r))return false;string owner=c.world.regions.First(x=>x.regionId==r).owner;var t=c.treaties.FirstOrDefault(x=>x.a==a.owner&&x.b==owner||x.b==a.owner&&x.a==owner);return owner==a.owner||t!=null&&(t.war||t.access);}
                    var path=Terrain.Find(p,CityPosition(destination),Access);
                    if(path!=null){a.marchTarget=destination;a.marchToSettlement=true;a.marchPath=path.points;}
                }
            }
            if(c.pending!=null){var p=CityPosition(c.pending.regionId);c.pending.mapPositionKnown=true;c.pending.longitude=p.lon;c.pending.latitude=p.lat;}
            c.schemaVersion=6;return true;
        }
    }
}
