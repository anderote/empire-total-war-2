using System;
using System.Collections;
using System.IO;
using System.Linq;
using EmpireBattle;
using EmpireGame;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Empire.UI
{
    // Explicit opt-in review. Armory/company saves are in memory; campaign is never entered.
    public sealed class InterfaceReview : MonoBehaviour
    {
        string directory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Requested()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--ui-workbench-review");
            if(i<0||i+1>=args.Length||FindAnyObjectByType<InterfaceReview>())return;
            var review=new GameObject("Interface visual review").AddComponent<InterfaceReview>();DontDestroyOnLoad(review.gameObject);review.directory=args[i+1];
        }
        IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(.8f);yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));yield return new WaitForSecondsRealtime(.4f);
        }
        IEnumerator Start()
        {
            Directory.CreateDirectory(directory);yield return new WaitForSecondsRealtime(2);
            var menu=FindAnyObjectByType<GameApplication>();if(!menu){Debug.LogError("INTERFACE_REVIEW_FAILED missing menu");Application.Quit(1);yield break;}
            yield return Shot("01-main-menu");
            Screen.SetResolution(1280,720,false);yield return new WaitForSecondsRealtime(1);yield return Shot("01b-compact-main-menu");
            Screen.SetResolution(1440,900,false);yield return new WaitForSecondsRealtime(1);
            menu.ReviewPage("Options");yield return Shot("02-options");menu.ReviewPage("Battles");yield return Shot("03-battles");
            menu.OpenGlory();yield return new WaitForSecondsRealtime(.5f);
            var glory=FindAnyObjectByType<RegimentalGloryMode>();yield return Shot("04-company");
            var savedCompany=new GloryCompany();savedCompany.Save.regiments[0].progression.Award(1850);var savedRecord=new GloryRegimentSource(savedCompany,()=>true).Units[0];
            RegimentLibrary.Shared.Store(RegimentLibrary.Capture(savedRecord,"The Iron First","Regimental Glory / Company"),"",out _);
            glory.ReviewManagementTab(5);yield return Shot("20-glory-library");glory.ReviewPage(1);yield return Shot("05-recruitment");glory.ReviewPage(2);yield return Shot("06-scenarios");
            glory.ReviewStage(7);glory.ReviewPage(3);yield return Shot("16-company-honours");glory.ReviewPage(2);yield return Shot("17-later-scenarios");
            Screen.SetResolution(1280,720,false);yield return new WaitForSecondsRealtime(1);yield return Shot("18-compact-scenarios");
            glory.ReviewPage(3);yield return Shot("19-compact-honours");Screen.SetResolution(1440,900,false);yield return new WaitForSecondsRealtime(1);
            SceneManager.LoadScene("LineBattle");yield return new WaitForSecondsRealtime(3);
            var runtime=FindAnyObjectByType<BattleRuntime>();var armory=FindAnyObjectByType<BattleArmory>();
            if(!runtime||!armory||runtime.ArmorySession==null){Debug.LogError("INTERFACE_REVIEW_FAILED missing battle armory");Application.Quit(1);yield break;}
            var session=runtime.ArmorySession;var entry=session.Entries[0];entry.progression.Award(8000);session.Save.year=1918;
            session.Train(entry,UnitSkill.Health);session.Train(entry,UnitSkill.Ammunition);session.Train(entry,UnitSkill.Accuracy);
            session.Equip(entry,"extra-cartridges");session.Equip(entry,"winter-greatcoats");session.Equip(entry,"magazine-rifles");session.Equip(entry,"grenade-pouches");session.Learn(entry,RegimentAbility.SteadyAim);
            armory.SelectEntry(0);armory.Management.ShowTab(0);yield return Shot("07-soldier-kit");
            armory.Management.InspectSlot(EquipmentSlot.Ammunition);yield return Shot("08-equipment-comparison");
            armory.Management.ShowTab(2);yield return Shot("09-stat-allocation");
            armory.Management.ShowTab(3);yield return Shot("10-abilities");
            armory.Management.ShowTab(4);yield return Shot("11-service");armory.Management.ShowTab(5);yield return Shot("21-battle-library");
            Screen.SetResolution(1280,720,false);yield return new WaitForSecondsRealtime(1);armory.Management.ShowTab(0);yield return Shot("12-compact-kit");
            armory.Management.ShowTab(2);yield return Shot("13-compact-stats");armory.Management.ShowTab(5);yield return Shot("22-compact-library");
            int battery=session.Entries.FindIndex(e=>e.users==EquipmentUsers.Artillery);if(battery>=0){armory.SelectEntry(battery);armory.Management.ShowTab(0);yield return Shot("14-battery-kit");}
            typeof(BattleArmory).GetMethod("Close",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(armory,null);
            runtime.SetPaused(true);FindAnyObjectByType<BattleView>().SelectUnit(0,false);yield return Shot("15-battle-hud");
            bool passed=runtime.World.Battalions[0].Ammunition==53&&entry.progression.UnitLevel>10&&Directory.GetFiles(directory,"*.png").Length>=22;
            File.WriteAllText(Path.Combine(directory,"result.json"),"{\"passed\":"+(passed?"true":"false")+",\"campaignPlaytested\":false,\"persistentSavesModified\":false}");
            Debug.Log(passed?"INTERFACE_REVIEW_PASSED":"INTERFACE_REVIEW_FAILED");Application.Quit(passed?0:1);
        }
    }
}
