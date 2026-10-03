using System;
using System.Linq;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        string peaceFaction="",peaceGive="",peaceDemand="",campaignMovePreview="",previewKey="";
        int peacePayment,peaceDuration=4;
        void DrawCampaignDispatches()
        {
            var c=countryCampaign;var s=c.Save.turnSummary;
            GUILayout.Label("TURN REPORT",heading);
            if(s.turn==0)GUILayout.Label("Your first turn report will appear after ending the turn.",text);
            else
            {
                GUILayout.Label("Turn "+s.turn+(s.complete?" completed":" in progress"),text);
                if(s.complete)GUILayout.Label("Treasury "+s.treasuryBefore+" → "+s.treasuryAfter+"  /  cities "+s.citiesBefore+" → "+s.citiesAfter+"  /  soldiers "+s.troopsBefore+" → "+s.troopsAfter,text);
                foreach(var notice in s.notices)GUILayout.Label(notice,small);
            }
            GUILayout.Space(14);GUILayout.Label("ORDERS AND WARNINGS",heading);
            var alerts=c.CampaignAlerts();if(alerts.Count==0)GUILayout.Label("No outstanding army or province warnings.",small);
            foreach(var alert in alerts)
            {
                if(GUILayout.Button(alert.kind+" / "+alert.text,campaignAction))
                {
                    if(alert.kind=="National decision")countryPage="Nation";
                    else if(!string.IsNullOrEmpty(alert.armyId))SelectCampaignArmy(c.Army(alert.armyId));
                    else if(!string.IsNullOrEmpty(alert.regionId)){SelectCampaignRegion(alert.regionId);countryPage="Map";var town=catalog.regions.First(r=>r.id==alert.regionId).capital;Focus(town.lon,town.lat,950);}
                }
            }
            GUILayout.Space(14);GUILayout.Label("CAMPAIGN HISTORY",heading);
            GUILayout.Label("The latest 200 battles, conquests, treaties, promotions and provincial events are retained in your save.",small);
            foreach(var entry in c.Save.history.AsEnumerable().Reverse())GUILayout.Label("Turn "+entry.turn+" / "+entry.kind+" / "+entry.text,text);
        }
        void DrawCampaignStances(CampaignArmy army)
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;
            GUILayout.Label("ORDERS / "+army.name,heading);
            GUILayout.BeginHorizontal();foreach(string stance in new[]{"March","Guard","Fortify","Ambush"})
                if(CampaignOrderButton(stance+(army.stance==stance?" / active":""),army.stance!=stance)&&c.SetStance(actor,army.id,stance,out countryMessage))SaveCountry(false);
            GUILayout.EndHorizontal();
            GUILayout.Label("All armies control approaches within 40 km. Defensive stances spend remaining movement. Fortify grants +12% battle readiness. Ambush conceals the army until an enemy approaches and grants +12% readiness in interception battles. Marching clears a prepared stance.",small);
            if(army.generalName!="")
            {
                GUILayout.Label(army.generalName+" / level "+c.CommanderLevel(army)+" / "+army.commanderXP+" command XP / "+army.commanderWins+" victories"+(army.commanderWounds>0?" / recovering: "+army.commanderWounds+" turns":""),text);
                GUILayout.Label("Command strength +"+((c.CommandBonus(army)-1)*100).ToString("F0")+"%. Specialty: "+(army.commanderTrait==""?"none":army.commanderTrait),small);
                GUILayout.Label("Level two unlocks one specialty: Tactician +5% battle readiness; Logistician +15% campaign movement; Inspiring +10% autoresolve resolve. Specialties pause while wounded.",small);
                if(army.commanderTrait=="")
                {GUILayout.BeginHorizontal();foreach(string trait in new[]{"Tactician","Logistician","Inspiring"})if(CampaignOrderButton(trait+" / 100",c.CommanderLevel(army)>=2&&army.commanderWounds==0)&&c.DevelopCommander(actor,army.id,trait,out countryMessage))SaveCountry(false);GUILayout.EndHorizontal();}
                if(CampaignOrderButton("Replace commander / 150 / resets command experience")&&c.ReplaceCommander(actor,army.id,out countryMessage))SaveCountry(false);
            }
        }
        void DrawProvinceGovernment(string region)
        {
            var c=countryCampaign;var p=c.Politics(region);
            GUILayout.Label("Administration: "+p.policy+" / insurgents: "+p.rebels+" / garrison: "+c.GarrisonStrength(region,c.Save.playerFaction)+(p.rebels>0?" / TRAINING AND CONSTRUCTION SUSPENDED":""),small);
            GUILayout.BeginHorizontal();foreach(string policy in new[]{"Reconcile","Garrison","Exploit"})
                if(CampaignOrderButton(policy,p.policy!=policy)&&c.SetOccupationPolicy(c.Save.playerFaction,region,policy,out countryMessage))SaveCountry(false);
            if(CampaignOrderButton("Relief / 150",p.lastReliefTurn!=c.Save.world.turn)&&c.ProvincialRelief(c.Save.playerFaction,region,out countryMessage))SaveCountry(false);
            GUILayout.EndHorizontal();
        }
        string NextPeaceRegion(string current,string owner)
        {var ids=new[]{""}.Concat(countryCampaign.Owned(owner).Select(r=>r.regionId)).ToArray();int i=Array.IndexOf(ids,current);return ids[(i+1)%ids.Length];}
        void DrawPeaceNegotiation(string other)
        {
            var c=countryCampaign;string actor=c.Save.playerFaction;
            if(GUILayout.Button(peaceFaction==other?"Close peace terms":"Propose peace terms",campaignAction)){peaceFaction=peaceFaction==other?"":other;peaceGive=peaceDemand="";peacePayment=0;}
            if(peaceFaction!=other)return;
            GUILayout.Label("War score "+c.WarScore(actor,other).ToString("+0;-0;0")+". Positive payment offers money; negative payment demands an indemnity.",small);
            GUILayout.BeginHorizontal();if(GUILayout.Button("−100",campaignAction))peacePayment=Math.Max(-10000,peacePayment-100);GUILayout.Label("Treasury payment: "+peacePayment,text);if(GUILayout.Button("+100",campaignAction))peacePayment=Math.Min(10000,peacePayment+100);GUILayout.EndHorizontal();
            if(GUILayout.Button("Cede: "+(peaceGive==""?"none":CampaignPlace(peaceGive))+" / cycle province",campaignAction))peaceGive=NextPeaceRegion(peaceGive,actor);
            if(GUILayout.Button("Demand: "+(peaceDemand==""?"none":CampaignPlace(peaceDemand))+" / cycle province",campaignAction))peaceDemand=NextPeaceRegion(peaceDemand,other);
            GUILayout.BeginHorizontal();foreach(int duration in new[]{2,4,6})if(GUILayout.Button(duration+"-turn truce"+(peaceDuration==duration?" / selected":""),campaignAction))peaceDuration=duration;GUILayout.EndHorizontal();
            bool accepted=c.EvaluatePeace(actor,other,peacePayment,peaceGive,peaceDemand,peaceDuration,out string why);GUILayout.Label(why,small);
            if(CampaignOrderButton("Sign these peace terms",accepted)&&c.NegotiatePeace(actor,other,peacePayment,peaceGive,peaceDemand,peaceDuration,out countryMessage)){peaceFaction="";SaveCountry(false);}
        }
        void UpdateCampaignMovePreview(string region)
        {
            var a=SelectedCampaignArmy();if(a==null||region==null){campaignMovePreview="";previewKey="";return;}
            string key=a.id+"/"+a.regionId+"/"+a.movement+"/"+region+"/"+countryCampaign.Save.world.turn+"/"+countryCampaign.Save.randomState;
            if(key==previewKey)return;previewKey=key;campaignMovePreview=countryCampaign.MovementPreview(a.id,region);
        }
    }
}
