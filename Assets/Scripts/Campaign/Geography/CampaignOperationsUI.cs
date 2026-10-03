using System;
using System.Linq;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        string CampaignPlace(string id)=>catalog.regions.FirstOrDefault(r=>r.id==id)?.capital.name??id;
        string CampaignForceStatus(CampaignArmy army)=>CountryCampaign.AtSea(army)?
            "AT SEA / "+CampaignPlace(army.seaFrom)+" → "+CampaignPlace(army.seaTarget)+" / "+(army.seaTurns>0?army.seaTurns+" turn(s) remaining":"awaiting landing"):
            countryCampaign.UnderSiegeOrders(army)?"BESIEGING / "+CampaignPlace(countryCampaign.Save.sieges.First(s=>s.armyId==army.id).regionId):
            army.marchTarget!=""?"MARCHING / "+(army.marchToSettlement?CampaignPlace(army.marchTarget):"open territory"):CountryCampaign.Busy(army)?"Training or refitting":army.stance=="March"?"Ready for orders":army.stance+" / holding approaches";
        void DrawCampaignOperations()
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;
            GUILayout.Label(c.OpeningAdvice,text);GUILayout.Space(12);
            GUILayout.Label("ARMY TRANSPORT",heading);
            GUILayout.Label("Select an army, then a destination port. The charter includes shipping and provisions. One army marker represents the entire force; hostile landings open a normal battle choice.",small);
            foreach(var army in c.Save.armies.Where(a=>a.owner==actor).ToArray())
            {
                if(GUILayout.Button(army.name+" / "+CampaignForceStatus(army),campaignAction))
                {SelectCampaignArmy(army);countryPage="Operations";}
            }
            var selectedForce=SelectedCampaignArmy();
            if(selectedForce!=null&&selectedForce.owner==actor)
            {
                DrawCampaignStances(selectedForce);
                GUILayout.Label(selectedForce.name+" / "+selectedForce.Soldiers.ToString("N0")+" troops",heading);
                if(CountryCampaign.AtSea(selectedForce))
                {
                    GUILayout.Label(CampaignForceStatus(selectedForce),text);
                    if(c.Blockaded(actor,selectedForce.seaTarget))GUILayout.Label("The destination is blockaded. Arrange relief at an owned port or return the transports.",small);
                    if(CampaignOrderButton("Return transports")&&c.ReturnTransport(actor,selectedForce.id,out countryMessage))SaveCountry(false);
                }
                else if(!c.HasPort(selectedForce.regionId))GUILayout.Label("March this army to an owned port to embark. Ports and connected shipping routes are listed below.",small);
                else
                {
                    foreach(var port in c.Ports.Where(p=>c.SeaRoute(selectedForce.regionId,p)!=null))
                    {
                        bool allowed=c.CanSail(actor,selectedForce,port,out string why);
                        string label="Sail to "+CampaignPlace(port)+" / "+CampaignFactionName(c.World.State(port).owner)+" / "+c.TransportCost(selectedForce,port)+" treasury / "+c.TransportTurns(selectedForce.regionId,port)+" turn(s)";
                        if(CampaignOrderButton(label,allowed)&&c.Sail(actor,selectedForce.id,port,out countryMessage))SaveCountry(false);
                        if(!allowed)GUILayout.Label(why,small);
                    }
                }
            }
            GUILayout.Space(18);GUILayout.Label("SIEGES",heading);
            foreach(var siege in c.Save.sieges.ToArray())
            {
                var army=c.Army(siege.armyId);if(army==null||army.owner!=actor&&siege.defender!=actor)continue;
                GUILayout.Label(CampaignPlace(siege.regionId)+" / "+CampaignFactionName(army.owner)+" / "+siege.turns+" turn(s)",text);
                if(army.owner==actor)
                {
                    GUILayout.BeginHorizontal();
                    if(CampaignOrderButton("Assault")&&c.AssaultSiege(actor,siege.id,out countryMessage))SaveCountry(false);
                    if(CampaignOrderButton("Lift siege")&&c.LiftSiege(actor,siege.id,out countryMessage))SaveCountry(false);
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label("Recruitment, construction and land freight are suspended. Defenders lose 6% and besiegers 2% strength each turn. Attack the besieging army to relieve the city.",small);
                    if(GUILayout.Button("Locate besieging army / "+CampaignPlace(army.regionId),campaignAction))
                    {SelectCampaignRegion(army.regionId);countryPage="Map";var town=catalog.regions.First(r=>r.id==army.regionId).capital;Focus(town.lon,town.lat,950);}
                }
            }
            if(!c.Save.sieges.Any(s=>c.Army(s.armyId)?.owner==actor||s.defender==actor))GUILayout.Label("No active sieges involving your country. Choose Besiege on a land battle panel to establish one.",small);
            GUILayout.Space(18);GUILayout.Label("PORTS AND NAVAL BLOCKADES",heading);
            GUILayout.Label("Commission a naval squadron for 250 treasury and 20 upkeep per turn. Its three-turn blockade stops hostile transports and removes half the port's tax income (minimum 10). Relief costs 200. Squadrons are strategic contracts; there is no separate naval battle screen.",small);
            string origin=selectedRegion!=null&&c.HasPort(selectedRegion.id)&&c.World.State(selectedRegion.id).owner==actor?selectedRegion.id:
                c.Ports.FirstOrDefault(p=>c.World.State(p).owner==actor);
            foreach(var port in c.Ports.Where(p=>c.World.State(p).owner==actor))
            {
                if(GUILayout.Button(CampaignPlace(port)+(c.Blockaded(actor,port)?" / BLOCKADED":" / use as naval base"),campaignAction))
                {SelectCampaignRegion(port);countryPage="Operations";}
                if(c.Blockaded(actor,port)&&CampaignOrderButton("Relief squadron / 200")&&c.BreakBlockade(actor,port,out countryMessage))SaveCountry(false);
            }
            if(origin!=null)
            {
                GUILayout.Label("Commission from "+CampaignPlace(origin),text);
                foreach(var target in c.Ports.Where(p=>c.AtWar(actor,c.World.State(p).owner)&&c.SeaRoute(origin,p)!=null))
                    if(CampaignOrderButton("Blockade "+CampaignPlace(target)+" / 250")&&c.CommissionBlockade(actor,origin,target,out countryMessage))SaveCountry(false);
            }
            foreach(var blockade in c.Save.blockades.Where(b=>b.owner==actor||c.World.State(b.regionId).owner==actor))
                GUILayout.Label(CampaignFactionName(blockade.owner)+" squadron at "+CampaignPlace(blockade.regionId)+" / "+blockade.turns+" turn(s) remaining",small);
        }
    }
}
