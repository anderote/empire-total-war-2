using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireBattle;
using Empire.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmpireCampaign.Geography
{
    /// <summary>Infantry campaign encounter in an additive battlefield; returns exact survivors once.</summary>
    public sealed class CampaignBattleBridge : MonoBehaviour
    {
        CountryCampaign campaign;
        CampaignEncounter encounter;
        BattleRuntime runtime;
        Scene origin,battleScene;
        GameObject[] roots;
        bool[] active;
        List<CampaignRegiment> deployment;
        bool playerAttacks,returning,committed;
        Action onCommittedReturn;
        string error="";
        public BattleRuntime Runtime => runtime;
        public bool Returning => returning;
        public static CampaignBattleBridge Launch(CountryCampaign campaign,Action onCommittedReturn=null)
        {
            if(!campaign.CanFightManually())return null;
            var config=CampaignBattleSetup.Create(campaign,campaign.Save.pending,new GeographyAtlas(),out var formations);
            var go=new GameObject("Campaign battle transition");var bridge=go.AddComponent<CampaignBattleBridge>();
            bridge.onCommittedReturn=onCommittedReturn;bridge.campaign=campaign;bridge.encounter=campaign.Save.pending;bridge.origin=SceneManager.GetActiveScene();
            DontDestroyOnLoad(go);
            bridge.roots=bridge.origin.GetRootGameObjects();bridge.active=bridge.roots.Select(r=>r.activeSelf).ToArray();
            foreach(var root in bridge.roots)root.SetActive(false);
            bridge.playerAttacks=bridge.encounter.attacker==campaign.Save.playerFaction;
            bridge.deployment=formations;
            bridge.battleScene=SceneManager.CreateScene("Campaign encounter "+bridge.encounter.id);SceneManager.SetActiveScene(bridge.battleScene);
            bridge.runtime=new GameObject("Campaign battle runtime").AddComponent<BattleRuntime>();bridge.runtime.Config=config;
            bridge.runtime.ReturnToCampaign=()=>bridge.FinishBattle(false,out bridge.error);
            bridge.runtime.ConcedeBattle=()=>bridge.FinishBattle(true,out bridge.error);
            return bridge;
        }
        void OnGUI()
        {
            if(runtime==null||runtime.World==null||returning)return;
            using var theme=new EmpireTheme.Scope(true);

            if(error!="")GUI.Label(new Rect(Screen.width-400,174,375,50),error);
        }
        public bool FinishBattle(bool concede,out string reason)
        {
                reason="Battle is unavailable or still ongoing.";
                if(runtime==null||runtime.World==null||returning)return false;
                var world=runtime.World;
                if(world.DeploymentActive)
                {returning=true;StartCoroutine(Return());reason="Deployment cancelled; encounter remains pending.";return true;}
                bool ended=world.Outcome!=BattleOutcome.Ongoing;
                if(!ended&&!concede)return false;
                bool playerWon=world.Outcome==BattleOutcome.FrenchVictory;
                bool attackerWon=playerAttacks?playerWon:!playerWon;
                if(world.Outcome==BattleOutcome.Draw)attackerWon=false;
                if(!ended)attackerWon=!playerAttacks;
                var result=new Dictionary<string,int>();var xp=new Dictionary<string,int>();var kills=new Dictionary<string,int>();var hits=new Dictionary<string,int>();var shots=new Dictionary<string,int>();
                for(int i=0;i<deployment.Count;i++)
                {
                    int alive=world.Battalions[i].SurvivorCount;
                    if(!ended&&world.Battalions[i].HasEnteredBattle&&world.Battalions[i].Side==ArmySide.French)alive=(int)(alive*.9f);
                    result.Add(deployment[i].id,alive);
                    var unit=world.Battalions[i];
                    kills.Add(deployment[i].id,unit.Kills);
                    hits.Add(deployment[i].id,unit.Hits);
                    shots.Add(deployment[i].id,unit.ShotsFired);
                    xp.Add(deployment[i].id,!unit.HasEnteredBattle?0:UnitProgression.BattleXP(unit.SoldierCount,alive,unit.Kills,unit.Hits,unit.ShotsFired,world.Outcome!=BattleOutcome.Draw&&(unit.Side==ArmySide.French?playerWon:!playerWon)));
                }
                if(!campaign.CommitTacticalBattle(encounter.id,attackerWon,result,out reason,xp,kills,hits,shots,!ended&&concede,world.Outcome==BattleOutcome.Draw,new HashSet<string>(deployment.Where((u,i)=>world.Battalions[i].HasEnteredBattle).Select(u=>u.id))))return false;
                committed=true;runtime.enabled=false;
                returning=true;StartCoroutine(Return());return true;
        }
        IEnumerator Return()
        {
            SceneManager.SetActiveScene(origin);
            for(int i=0;i<roots.Length;i++)if(roots[i])roots[i].SetActive(active[i]);
            yield return SceneManager.UnloadSceneAsync(battleScene);
            if(committed)onCommittedReturn?.Invoke();
            Destroy(gameObject);
        }
    }
}
