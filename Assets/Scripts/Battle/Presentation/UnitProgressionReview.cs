using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace EmpireBattle
{
    // Opt-in standalone smoke test. Uses an in-memory armory and never edits the player's save.
    public sealed class UnitProgressionReview : MonoBehaviour
    {
        [Serializable] sealed class Result {public bool passed;public int itemCount,infantryAmmo,infantryMorale,batteryAmmo,infantryXP,batteryXP;public float batteryReloadScale;}
        BattleRuntime runtime;
        BattleArmorySession session;
        BattleArmory panel;
        string output;
        float started;
        bool infantryShot,batteryShot,selectedBattery,done;
        void Start()
        {
            runtime=GetComponent<BattleRuntime>();session=runtime.ArmorySession;
            panel=FindAnyObjectByType<BattleArmory>();
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--unit-upgrades-output");
            output=index>=0&&index+1<args.Length?args[index+1]:Path.Combine(Application.temporaryCachePath,"UnitUpgradesReview");Directory.CreateDirectory(output);
            if(session==null||!panel){Debug.LogError("UNIT_UPGRADES_PLAYTEST_FAILED missing armory");Application.Quit(1);return;}
            var infantry=session.Entries[0];infantry.progression.Award(850);session.Train(infantry,UnitSkill.Accuracy);session.Train(infantry,UnitSkill.Discipline);
            session.Equip(infantry,"imperial-standard");session.Equip(infantry,"extra-cartridges");
            var battery=session.Entries.First(e=>e.users==EquipmentUsers.Artillery);battery.progression.Award(250);session.Train(battery,UnitSkill.Accuracy);session.Train(battery,UnitSkill.Drill);
            session.Equip(battery,"reserve-caissons");session.Equip(battery,"crew-tools");session.Equip(battery,"imperial-standard");
            panel.SelectEntry(0);started=Time.realtimeSinceStartup;
        }
        void Update()
        {
            if(done||session==null)return;float elapsed=Time.realtimeSinceStartup-started;
            if(!infantryShot&&elapsed>2){infantryShot=true;ScreenCapture.CaptureScreenshot(Path.Combine(output,"regiment-armory.png"));}
            if(!selectedBattery&&elapsed>3.5f){selectedBattery=true;panel.SelectEntry(session.Entries.FindIndex(e=>e.users==EquipmentUsers.Artillery));}
            if(!batteryShot&&elapsed>4.5f){batteryShot=true;ScreenCapture.CaptureScreenshot(Path.Combine(output,"battery-armory.png"));}
            if(elapsed<6.5f)return;done=true;
            var infantry=runtime.World.Battalions[0];var gun=runtime.Artillery.Guns[0];
            bool passed=runtime.Paused&&runtime.World.Time==0&&infantry.Ammunition==45&&infantry.Morale==infantry.Progression.MaxMorale+25&&infantry.Progression.xp==850
                &&gun.Ammunition==375&&gun.Progression.xp==250&&gun.ReloadScale<.9f&&session.Save.Valid
                &&File.Exists(Path.Combine(output,"regiment-armory.png"))&&File.Exists(Path.Combine(output,"battery-armory.png"));
            File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(new Result {passed=passed,itemCount=UnitEquipment.Items.Length,
                infantryAmmo=infantry.Ammunition,infantryMorale=(int)infantry.Morale,batteryAmmo=gun.Ammunition,infantryXP=infantry.Progression.xp,batteryXP=gun.Progression.xp,batteryReloadScale=gun.ReloadScale},true));
            Debug.Log(passed?"UNIT_UPGRADES_PLAYTEST_PASSED":"UNIT_UPGRADES_PLAYTEST_FAILED");Application.Quit(passed?0:1);
        }
    }
}
