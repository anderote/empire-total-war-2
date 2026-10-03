using System;
using System.Collections;
using System.IO;
using System.Linq;
using EmpireBattle;
using UnityEngine;

namespace EmpireCampaign.Geography
{
    public sealed class CampaignArmyUIReview : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"--campaign-army-ui-review");
            if(flag<0||flag+1>=args.Length)return;
            var host=new GameObject("Campaign army UI review");DontDestroyOnLoad(host);host.AddComponent<CampaignArmyUIReview>().StartCoroutine(Run(args[flag+1]));
        }
        static IEnumerator Run(string directory)
        {
            yield return new WaitForSecondsRealtime(4);
            var view=UnityEngine.Object.FindAnyObjectByType<EuropeMapView>();
            if(!view){Debug.LogError("Campaign view missing");Application.Quit(1);yield break;}
            yield return view.ReviewCampaignArmyUI(directory);
        }
    }
    public sealed partial class EuropeMapView
    {
        public IEnumerator ReviewCampaignArmyUI(string directory)
        {
            qaRunning=true;Directory.CreateDirectory(directory);
            var steps=CampaignArmyUIReviewSteps(directory);
            while(true)
            {
                bool next;object current;
                try{next=steps.MoveNext();current=next?steps.Current:null;}
                catch(Exception error){File.WriteAllText(Path.Combine(directory,"failed.txt"),error.ToString());Debug.LogException(error);Application.Quit(1);yield break;}
                if(!next)break;yield return current;
            }
            File.WriteAllText(Path.Combine(directory,"checks.txt"),"Passed: additive BP comparison does not consume campaign RNG; army card selection reaches regiment details; recruitment selection preserves CanRecruit gate, recruits exact role and charges treasury; preview does not enqueue; encounter manual/cancel gates; cancel/autoresolve/siege remain functional on disposable in-memory copies; zero-force manual gate. Native captures require visual review.");
            Debug.Log("CAMPAIGN_ARMY_UI_REVIEW_PASSED");Application.Quit();
        }
        IEnumerator CampaignArmyUICapture(string directory,string name)
        {
            yield return new WaitForSecondsRealtime(1.2f);yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));yield return new WaitForSecondsRealtime(.4f);
        }
        static void ArmyUICheck(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
        CountryCampaign ArmyUIClone(CountryCampaign source)
        {
            var copy=new CountryCampaign(catalog,EconomyProfiles.Load(),Atlas.Data,CampaignBalance.Load().Revolutionary(),"france",1800,CountryCampaign.RepublicScenario);
            ArmyUICheck(copy.Adopt(JsonUtility.FromJson<CountryCampaignSave>(JsonUtility.ToJson(source.Save)),out string reason),"QA clone: "+reason);return copy;
        }
        IEnumerator CampaignArmyUIReviewSteps(string directory)
        {
            StartCountry("france");var army=countryCampaign.Save.armies.First(a=>a.owner=="france");SelectCampaignArmy(army);
            yield return CampaignArmyUICapture(directory,"army-tray");
            selectedRegimentId=army.units[0].id;
            yield return CampaignArmyUICapture(directory,"army-card-selection");
            ArmyUICheck(campaignManagement.FocusedId==selectedRegimentId,"Army selection did not reach regiment details");
            selectedRegimentId="";OpenCampaignRecruitment("region-paris");recruitCardRole="Regular";
            int queueBefore=countryCampaign.Save.recruitment.Count;uint rng=countryCampaign.Save.randomState;
            yield return CampaignArmyUICapture(directory,"recruitment-line");
            ArmyUICheck(countryCampaign.Save.recruitment.Count==queueBefore,"Preview enqueued recruitment");
            recruitCardRole="Rifle";yield return CampaignArmyUICapture(directory,"recruitment-selection");
            var state=countryCampaign.World.State("region-paris");bool allowed=countryCampaign.CanRecruit("france",state.regionId,recruitCardRole,"",out _);
            var recruitClone=ArmyUIClone(countryCampaign);int cash=recruitClone.World.FactionState("france").treasury;
            bool recruited=recruitClone.Recruit("france",state.regionId,recruitCardRole,"",out _);
            ArmyUICheck(recruited==allowed,"Recruit gate mismatch");
            if(allowed)ArmyUICheck(recruitClone.Save.recruitment.Last().role==recruitCardRole&&recruitClone.World.FactionState("france").treasury==cash-recruitClone.RecruitCost(recruitCardRole),"Recruit role/cost changed");
            campaignPopup="";CampaignRoute frontier=null;
            foreach(var home in countryCampaign.Owned("france"))foreach(var target in countryCampaign.Owned("austria"))
            {var route=countryCampaign.Route("france",home.regionId,target.regionId);if(route!=null&&(frontier==null||route.distance<frontier.distance))frontier=route;}
            ArmyUICheck(frontier!=null,"Frontier missing");string destination=frontier.regions.Last(),origin=frontier.regions[frontier.regions.Count-2];
            army.regionId=origin;army.movement=2000;
            var defender=countryCampaign.Save.armies.First(a=>a.owner=="austria");defender.regionId=destination;
            foreach(var other in countryCampaign.Save.armies.Where(a=>a.owner=="austria"&&a!=defender))other.regionId=countryCampaign.Owned("austria").First(r=>r.regionId!=destination).regionId;
            ArmyUICheck(countryCampaign.Move("france",army.id,destination,out string why),"Encounter: "+why);
            ArmyUICheck(countryCampaign.Save.pending!=null&&countryCampaign.CanFightManually()&&countryCampaign.CanWithdraw(out _),"Manual/cancel gate missing");
            rng=countryCampaign.Save.randomState;string before=JsonUtility.ToJson(countryCampaign.Save);
            yield return CampaignArmyUICapture(directory,"prebattle-attack");
            ArmyUICheck(countryCampaign.Save.randomState==rng,"BP preview consumed RNG");
            var cancel=ArmyUIClone(countryCampaign);ArmyUICheck(cancel.Withdraw(cancel.Save.pending.id,out _)&&cancel.Save.pending==null,"Cancel attack failed");
            var auto=ArmyUIClone(countryCampaign);ArmyUICheck(auto.Resolve(auto.Save.pending.id,out _)&&auto.Save.pending==null,"Autoresolve failed");
            var siege=ArmyUIClone(countryCampaign);if(siege.CanBesiege(out _))ArmyUICheck(siege.Besiege(siege.Save.pending.id,out _)&&siege.Save.pending==null,"Besiege failed");
            // Tactical entry and deployment cancellation exercise the existing bridge without saving the QA state.
            var bridge=CampaignBattleBridge.Launch(countryCampaign);ArmyUICheck(bridge!=null,"Tactical bridge missing");
            float deadline=Time.realtimeSinceStartup+45;
            while(bridge.Runtime.World==null&&Time.realtimeSinceStartup<deadline)yield return null;
            ArmyUICheck(bridge.Runtime.World!=null&&bridge.Runtime.World.DeploymentActive,"Tactical deployment not initialized");
            ArmyUICheck(bridge.FinishBattle(false,out _),"Deployment cancellation failed");yield return null;
            ArmyUICheck(JsonUtility.ToJson(countryCampaign.Save)==before,"Deployment cancellation changed campaign state");
            var e=countryCampaign.Save.pending;
            countryCampaign.Save.playerFaction=e.defender;
            if(e.garrison.Count==0)e.garrison.Add(new CampaignRegiment {id="qa-local-garrison",homeRegionId=e.regionId,role="Militia",soldiers=250});
            ArmyUICheck(!countryCampaign.CanWithdraw(out _),"Local garrison must disable retreat");
            yield return CampaignArmyUICapture(directory,"prebattle-defend-retreat-gate");
            countryCampaign.Save.playerFaction=e.attacker;e.fieldOnly=true;
            foreach(var a in countryCampaign.EncounterArmies(e,false))a.units.Clear();e.garrison.Clear();
            ArmyUICheck(!countryCampaign.CanFightManually(),"Empty defender must disable manual battle");
            yield return CampaignArmyUICapture(directory,"prebattle-zero-defender");
            countryCampaign.Save.pending=null;qaRunning=false;
        }
    }
}
