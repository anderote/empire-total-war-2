using System;
using System.Collections;
using System.IO;
using EmpireBattle;
using EmpireGame;
using UnityEngine;

namespace Empire.UI
{
    // Built in an isolated development scene. All company and library data is temporary.
    public sealed class RegimentUpgradeReview : MonoBehaviour
    {
        readonly RegimentManagementPanel panel=new RegimentManagementPanel();
        GloryCompany company;GloryRegimentSource source;string output="";bool errors,focusedCapture;
        void Awake()
        {
            var args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"--regiment-upgrade-ui");
            if(flag<0||flag+1>=args.Length){enabled=false;return;}output=args[flag+1];Directory.CreateDirectory(output);
            focusedCapture=Array.IndexOf(args,"--regiment-upgrade-focused-capture")>=0;
            Application.logMessageReceived+=Log;
            company=new GloryCompany();company.Save.coins=12000;company.Save.commanderXP=8000;
            company.Recruit(0);company.Recruit(8);company.Recruit(10);
            var unit=company.Save.regiments[0];unit.name="1st Royal Foot";unit.progression.Award(8000);unit.soldiers=92;unit.progression.Learn(RegimentAbility.SteadyAim);
            company.Equip(unit,"stout-boots-level-3");company.Equip(unit,"extra-cartridges");company.Equip(unit,"winter-greatcoats");unit.loadout.wear=22;
            company.Save.regiments[1].name="2nd Royal Foot";company.Save.regiments[2].name="Horse Guards";company.Save.regiments[3].name="Royal Battery";
            source=new GloryRegimentSource(company,()=>true);panel.Select(unit.id.ToString());
            RegimentLibrary.Shared.Store(RegimentLibrary.Capture(source.Units[0],unit.name,"Development review"),"",out _);
        }
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors=true;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        IEnumerator Shot(string name)
        {yield return new WaitForSecondsRealtime(.35f);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.35f);}
        IEnumerator Start()
        {
            if(focusedCapture)
            {
                Screen.SetResolution(1920,1080,false);yield return new WaitForSecondsRealtime(1.25f);
                panel.ShowTab(0);yield return Shot("01-wide-stats-and-live-model");
                panel.InspectSlot(EquipmentSlot.Mobility);panel.InspectEquipment("stout-boots-level-4");
                yield return Shot("02-wide-purchase-comparison");
                File.WriteAllText(Path.Combine(output,"result.json"),"{\"passed\":"+(!errors?"true":"false")+",\"captureCount\":2,\"width\":1920,\"height\":1080,\"campaignEntered\":false,\"persistentSavesModified\":false}");
                Debug.Log(errors?"REGIMENT_UPGRADE_FOCUSED_CAPTURE_FAILED":"REGIMENT_UPGRADE_FOCUSED_CAPTURE_PASSED");
                Application.Quit(errors?1:0);
                yield break;
            }
            Screen.SetResolution(1440,900,false);yield return new WaitForSecondsRealtime(1);
            panel.ShowTab(0);yield return Shot("01-overview");
            panel.InspectSlot(EquipmentSlot.Mobility);panel.InspectEquipment("stout-boots-level-4");yield return Shot("02-equipment");
            panel.SetEquipmentRecipients(new[]{source.Units[0].Id,source.Units[1].Id});yield return Shot("03-group-order");
            company.Save.coins=0;yield return Shot("04-locked-equipment");company.Save.coins=12000;
            panel.UseCheckedGroup(false);panel.ShowTab(2);yield return Shot("05-stats");panel.ShowTab(3);yield return Shot("06-abilities");panel.ShowTab(4);yield return Shot("07-service");
            Screen.SetResolution(1280,720,false);yield return new WaitForSecondsRealtime(.5f);
            panel.InspectSlot(EquipmentSlot.Ammunition);yield return Shot("08-compact-equipment");panel.ShowTab(2);yield return Shot("09-compact-stats");panel.ShowTab(3);yield return Shot("10-compact-abilities");panel.ShowTab(4);yield return Shot("11-compact-service");
            panel.ShowTab(5);yield return Shot("12-compact-library");
            panel.Select(source.Units[3].Id);panel.ShowTab(2);yield return Shot("13-artillery-stats");
            Screen.SetResolution(1920,1080,false);yield return new WaitForSecondsRealtime(.5f);panel.Select(source.Units[0].Id);panel.InspectSlot(EquipmentSlot.Mobility);yield return Shot("14-wide-equipment");
            File.WriteAllText(Path.Combine(output,"result.json"),"{\"passed\":"+(!errors?"true":"false")+",\"campaignEntered\":false,\"persistentSavesModified\":false}");
            Debug.Log(errors?"REGIMENT_UPGRADE_REVIEW_FAILED":"REGIMENT_UPGRADE_REVIEW_PASSED");Application.Quit(errors?1:0);
        }
        void OnGUI()
        {
            if(source==null)return;
            if(Event.current.isMouse||Event.current.isKey)Event.current.Use();
            Event.current.mousePosition=new Vector2(-1000,-1000);
            using var theme=new EmpireTheme.Scope(true);float scale=CommandUI.Scale(Screen.width,Screen.height);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float w=Screen.width/scale,h=Screen.height/scale;CommandUI.Backdrop(w,h);
            CommandUI.Label(new Rect(24,16,w-48,40),"REGIMENTS & ARMORY",28,false,true);panel.Draw(new Rect(24,68,w-48,h-92),source);GUI.matrix=Matrix4x4.identity;
        }
    }
}
