using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireBattle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmpireCampaign.Geography
{
    // Opt-in native scene/ledger diagnostic. Combat outcomes are deliberately injected;
    // combat and siege rules are covered separately by simulation verification.
    public sealed class CampaignBattleProbe : MonoBehaviour
    {
        static bool started;
        string mode,output;
        int checks;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Bootstrap()
        {
            var args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"--campaign-battle-probe");
            if(started||flag<0)return;started=true;
            var go=new GameObject("Campaign integration diagnostic");DontDestroyOnLoad(go);
            var probe=go.AddComponent<CampaignBattleProbe>();probe.mode=flag+1<args.Length?args[flag+1]:"attack";
            int path=Array.IndexOf(args,"--campaign-probe-output");probe.output=path>=0&&path+1<args.Length?args[path+1]:null;
            probe.StartCoroutine(probe.Run());
        }
        void Check(bool condition,string message)
        {checks++;if(!condition)throw new InvalidOperationException(message);}
        IEnumerator Run()
        {
            // Allow the launch scene to finish its normal initialization first.
            yield return null;yield return null;
            IEnumerator steps=Steps();
            while(true)
            {
                object current;bool next;
                try{next=steps.MoveNext();current=next?steps.Current:null;}
                catch(Exception exception){Report(false,exception.ToString());yield break;}
                if(!next)break;yield return current;
            }
            Report(true,"");
        }
        IEnumerator Steps()
        {
            bool defend=mode.StartsWith("defend",StringComparison.OrdinalIgnoreCase),cancel=mode=="cancel";
            bool republic=Environment.GetCommandLineArgs().Contains("--republic-probe");
            string player=republic?"france":defend?"austria":"france";
            string attacking=republic&&defend?"austria":"france",defending=republic&&defend?"france":"austria";
            var c=new CountryCampaign(RegionCatalog.Load(),EconomyProfiles.Load(),
                JsonUtility.FromJson<TheaterData>(EmpireCampaign.Geography.CampaignResourceData.Load("WorldGeography/world").text),republic?CampaignBalance.Load().Revolutionary():CampaignBalance.Load(),player,1792,republic?CountryCampaign.RepublicScenario:"");
            CampaignRoute frontier=null;string region=null,origin=null;
            var attacker=c.Save.armies.First(a=>a.owner==attacking);
            foreach(var home in c.Owned(attacking))foreach(var target in c.Owned(defending))
            {
                var route=c.Route(attacking,home.regionId,target.regionId);if(route==null)continue;
                string candidateRegion=route.regions.Last(),candidateOrigin=route.regions[route.regions.Count-2];
                // The abstract route graph can connect region capitals whose terrain
                // grid has no passable path. Select a frontier valid for the move
                // command that this probe exercises.
                attacker.regionId=candidateOrigin;attacker.mapPositionKnown=false;
                var capital=c.World.Catalog.regions.First(r=>r.id==candidateRegion).capital;
                if(c.LandPath(attacker,capital)==null)continue;
                if(frontier==null||route.distance<frontier.distance)
                {frontier=route;region=candidateRegion;origin=candidateOrigin;}
            }
            Check(frontier!=null,"Real campaign land frontier exists");
            attacker.regionId=origin;attacker.mapPositionKnown=false;attacker.movement=2000;
            foreach(var army in c.Save.armies.Where(a=>a.owner==defending))army.regionId=c.Owned(defending).First(r=>r.regionId!=region).regionId;
            c.World.State(region).buildings[0]=new RegionBuilding{kind="Fortress",level=2};
            if(defend)typeof(CountryCampaign).GetField("aiActor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(c,attacking);
            Check(c.Move(attacking,attacker.id,region,out var reason),"Legal campaign move creates encounter: "+reason);
            if(defend&&c.Save.pending==null)
            {
                // An AI assault on a fortified region correctly opens a siege first.
                // Convert that siege into its explicit tactical assault before testing
                // the defender's native campaign-to-battle transition.
                var siege=c.Save.sieges.FirstOrDefault(s=>s.regionId==region&&s.defender==defending);
                Check(siege!=null,"AI attack establishes a siege before tactical assault");
                Check(c.AssaultSiege(attacking,siege.id,out reason),"AI siege assault creates tactical encounter: "+reason);
            }
            if(defend)typeof(CountryCampaign).GetField("aiActor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(c,null);
            Check(c.CanFightManually(),"Militia fortified encounter supports tactical battle");
            string id=c.Save.pending.id;var before=JsonUtility.ToJson(c.Save);var originScene=SceneManager.GetActiveScene();
            var originalRoots=originScene.GetRootGameObjects();var originalActive=originalRoots.Select(r=>r.activeSelf).ToArray();
            var originalStrength=c.BattleUnits(c.Save.pending,true).Concat(c.BattleUnits(c.Save.pending,false)).ToDictionary(u=>u.id,u=>u.soldiers);
            var bridge=CampaignBattleBridge.Launch(c);Check(bridge!=null,"Bridge launches");
            float deadline=Time.realtimeSinceStartup+90;
            while(bridge.Runtime.World==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(bridge.Runtime.World!=null,"Battle runtime initialized");var world=bridge.Runtime.World;
            Check(world.DeploymentActive&&world.Time==0,"Actual battle scene starts paused for deployment");
            Check(world.Terrain.Profile.FortLevel==2&&world.Siege!=null,"Campaign fort produces siege terrain");
            Check(world.Terrain.Profile.DefenderSide==(defend?ArmySide.French:ArmySide.British),"Player defender maps to correct tactical side");
            Check(SceneManager.GetActiveScene()!=originScene,"Battle has separate active scene");
            for(int i=0;i<originalRoots.Length;i++)if(originalRoots[i])Check(!originalRoots[i].activeSelf,"Campaign scene roots hidden during battle");
            yield return new WaitForSecondsRealtime(3);
            Check(world.Time==0,"Deployment remains frozen in actual player loop");
            if(cancel)
            {
                Check(bridge.FinishBattle(false,out reason),"Deployment cancellation returns: "+reason);
            }
            else
            {
                Check(world.BeginBattle(out reason),"Begin battle accepts generated deployment: "+reason);
                Check(!bridge.FinishBattle(false,out _),"Ongoing battle cannot return a fabricated result");
                bridge.Runtime.SetPaused(true);
                world.ApplyCasualties(0,7);
                // The probe tests bridge side conversion and exact casualty commit.
                // A defender victory must leave Austrian ownership intact.
                world.Outcome=BattleOutcome.FrenchVictory;
                Check(bridge.FinishBattle(false,out reason),"Finished tactical battle commits: "+reason);
                Check(!bridge.FinishBattle(false,out _),"Returning bridge cannot commit twice");
            }
            deadline=Time.realtimeSinceStartup+30;
            while(bridge&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(!bridge,"Transition object is destroyed after return");
            Check(SceneManager.GetActiveScene()==originScene,"Original campaign scene restored");
            for(int i=0;i<originalRoots.Length;i++)if(originalRoots[i])Check(originalRoots[i].activeSelf==originalActive[i],"Campaign root activity restored");
            if(cancel)Check(JsonUtility.ToJson(c.Save)==before,"Cancellation leaves pending encounter and campaign data unchanged");
            else
            {
                Check(c.Save.pending==null&&c.Save.resolvedEncounters.Count(e=>e==id)==1,"Encounter commits exactly once");
                Check(c.World.State(region).owner==(defend?defending:attacking),"Ownership follows attacker versus defender victory");
                if(!defend)Check(attacker.units[0].soldiers==originalStrength[attacker.units[0].id]-7,"Exact seven tactical casualties; no second autoresolve");
                var ledger=c.Save.activeRegions.ToDictionary(r=>r,r=>0);
                foreach(var army in c.Save.armies)foreach(var regiment in army.units)ledger[regiment.homeRegionId]+=regiment.soldiers;
                foreach(var q in c.Save.recruitment)ledger[q.regionId]+=q.soldiers;
                foreach(var pair in ledger)Check(c.World.State(pair.Key).economy.enlisted==pair.Value,"Enlisted ledger conserved after native transition");
                string json=JsonUtility.ToJson(c.Save,true);
                if(output!=null)File.WriteAllText(output+".save.json",json);
                Check(c.Adopt(JsonUtility.FromJson<CountryCampaignSave>(json),out reason),"Native save restores committed battle: "+reason);
            }
        }
        void Report(bool passed,string error)
        {
            string json=JsonUtility.ToJson(new Result{passed=passed,mode=mode,checks=checks,error=error},true);
            if(output!=null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.WriteAllText(output,json);}
            if(passed)Debug.Log("CAMPAIGN_BATTLE_PROBE_PASSED "+json);else Debug.LogError("CAMPAIGN_BATTLE_PROBE_FAILED "+json);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(passed?0:1);
#else
            Application.Quit(passed?0:1);
#endif
        }
        [Serializable] sealed class Result{public bool passed;public string mode,error;public int checks;}
    }
}
