using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace EmpireBattle
{
    // Opt-in screenshots of actual soldier poses and square geometry, without advancing combat.
    public sealed class FormationVisualReview : MonoBehaviour
    {
        string directory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RequestReview()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--formation-review");
            if(index<0||index+1>=args.Length)return;
            new GameObject("Formation visual review").AddComponent<FormationVisualReview>().directory=Path.GetFullPath(args[index+1]);
        }
        IEnumerator Start()
        {
            Directory.CreateDirectory(directory);yield return new WaitForSecondsRealtime(2);
            var runtime=FindAnyObjectByType<BattleRuntime>();var view=FindAnyObjectByType<BattleView>();
            if(!runtime||!view)throw new Exception("Formation review requires a battle scene");
            if(!runtime.Paused)view.OnTogglePause?.Invoke();
            var world=runtime.World;var unit=world.Battalions[0];
            foreach(var other in world.Battalions){world.Halt(other.Id);other.FireAtWill=false;}
            world.SetFormation(unit.Id,FormationKind.Square);unit.FireAtWill=true;
            for(int i=0;i<800;i++)world.StepMovement(.05f);
            view.SelectUnit(unit.Id,false);view.SetInspectionView(unit.Position,42,70);
            yield return Capture("square-three-ranks.png");
            Vector2 corner=unit.Position+FormationMath.Forward(unit.Facing)*FormationMath.HalfExtents(unit).y*.7f;
            view.SetInspectionView(corner,12,25,180);yield return Capture("square-bayonet-wall.png");
            world.AdjustRanks(unit.Id,3);for(int i=0;i<800;i++)world.StepMovement(.05f);
            view.SetInspectionView(unit.Position,30,75,0);yield return Capture("square-six-ranks.png");
            world.SetFormation(unit.Id,FormationKind.Skirmish);for(int i=0;i<2500;i++)world.StepMovement(.05f);
            view.SetInspectionView(unit.Position,250,65);yield return Capture("skirmish-spread.png");
            view.SetInspectionView(world.Soldiers.Positions[world.SoldierAtSlot(unit,unit.AliveCount/2)],16,30,180);
            yield return Capture("skirmish-kneeling.png");
            Debug.Log("FORMATION_VISUAL_REVIEW_COMPLETE");Application.Quit();
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,name));
            yield return new WaitForSecondsRealtime(.5f);
        }
    }
}
