using System;
using System.Linq;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed partial class EuropeMapView
    {
        string CampaignFactionName(string id)=>countryCampaign!=null?countryCampaign.FactionName(id):catalog.Faction(id).name;
        string CampaignArmyOwnerLabel(string id)=>countryCampaign.IsRepublic&&id=="batavian"?"Dutch":countryCampaign.IsRepublic&&id=="helvetic"?"Swiss":countryCampaign.IsRepublic&&id=="ligurian"?"Genoa":char.ToUpperInvariant(id[0])+id.Substring(1).Replace('-',' ');
        void DrawRepublicStory()
        {
            var c=countryCampaign;var s=c.Save.story;
            GUILayout.Space(18);GUILayout.Label(c.StoryChapter,campaignCaption);
            GUILayout.Label(s.government+" / stability "+s.stability+" / 100",heading);
            GUILayout.Label(c.NapoleonStatus,text);
            GUILayout.Label("At war: "+(c.CoalitionLabel==""?"No current enemies":c.CoalitionLabel),text);
            GUILayout.Label("Food shortages, emergency taxation, losing Paris and a broad coalition erode confidence. Stability below 35 raises regional unrest; you can rebuild and keep campaigning. Naval blockades stop transports and reduce port income; commission relief through Operations.",small);
            GUILayout.Space(12);
            if(s.decision!="")
            {
                GUILayout.Label(c.DecisionTitle,heading);
                GUILayout.Label("Choose before ending this turn. All costs are paid from your treasury.",small);
                var options=c.DecisionOptions;
                for(int i=0;i<options.Length;i++)if(CampaignOrderButton(options[i])){c.DecideRepublic(s.decision,i,out countryMessage);break;}
                GUILayout.Space(12);
            }
            if(CampaignOrderButton("Fund civilian relief / 200 / +6 stability, -10 unrest",s.lastReliefTurn!=c.Save.world.turn))c.FundRepublicRelief(out countryMessage);
            GUILayout.Label("Relief is available once each turn. Mobilization expands the existing manpower pool; new units still require barracks, materials, money and training. Generals command existing regiments; independent detachments march and join a force when they meet.",small);
            GUILayout.Space(16);GUILayout.Label("YOUR OPENING ORDERS",campaignCaption);
            GUILayout.Label((c.Save.openingRecruitment?"[Done]":"[ ]")+" Recruit at a barracks   /   "+(c.Save.openingMarch?"[Done]":"[ ]")+" March a force   /   "+(c.Save.openingConquest?"[Done]":"[ ]")+" Win enemy territory",text);
            GUILayout.Label("Northern front: Lille against the Austrian Netherlands. Rhine front: Strasbourg against the German approaches. Alpine front: Lyon toward Sardinian Turin. From 1793, Spain threatens the Pyrenees. Britain has continental forces in Hanover and can transport armies by sea. Ports, voyages, blockades and sieges are managed through Operations.",small);
            GUILayout.Space(16);GUILayout.Label("A CAMPAIGN THROUGH THE REVOLUTION",campaignCaption);
            GUILayout.Label("1792 / Republic proclaimed · 1793 / wider coalition and Toulon · 1794 / Thermidor · 1795 / Directory · 1796 / Bonaparte's Italian command · 1797 / peace opportunity if Turin and Milan are taken · 1799 / Second Coalition and Brumaire · 1804 / constitutional settlement",text);
            GUILayout.Label("Historical dates open opportunities. Battles, territorial gains and negotiated peace depend on your play. French victories are never awarded by the timeline.",small);
            GUILayout.Space(16);GUILayout.Label("OPEN TERRITORIAL CAMPAIGN",campaignCaption);
            GUILayout.Label("Expand, defend, negotiate and rebuild for as long as you choose. There are no victory quotas, development requirements or turn limit. A political settlement in 1804 changes your government and play continues. Defeat requires every city to be captured AND every army and detachment to be destroyed.",text);
            GUILayout.Label("Scenario prototype: key French frontier ownership is reset for 1792. The shared atlas retains approximate later boundaries elsewhere, including Venice; domestic revolt remains an economic abstraction; naval squadrons use paid strategic contracts.",small);
        }
    }
}
