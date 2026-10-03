using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace EmpireBattle
{
    // Opt-in inspection of the actual command meshes and terrain/formation placement.
    public sealed class RegimentVisualReview:MonoBehaviour
    {
        string directory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Request()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--regiment-review");
            if(index<0||index+1>=args.Length)return;
            var review=new GameObject("Regiment visual review").AddComponent<RegimentVisualReview>();
            review.directory=Path.GetFullPath(args[index+1]);
            DontDestroyOnLoad(review.gameObject);
        }
        IEnumerator Start()
        {
            Directory.CreateDirectory(directory);yield return new WaitForSecondsRealtime(2);
            var runtime=FindAnyObjectByType<BattleRuntime>();var view=FindAnyObjectByType<BattleView>();
            if(!runtime||!view)throw new Exception("Regiment review requires battle runtime");
            if(!runtime.Paused)view.OnTogglePause?.Invoke();
            var world=runtime.World;
            foreach(var unit in world.Battalions){world.Halt(unit.Id);unit.FireAtWill=false;}
            var french=world.Battalions[0];var british=world.Battalions[world.Config.FrenchBattalionCount];
            view.GetPresentationTime=()=>world.Time+.67f;
            foreach(var unit in new[]{french,british}){
                view.SelectUnit(unit.Id,false);int count=CommandPartyLayout.Platoons(unit,world.Config.PlatoonFiles);
                view.SetInspectionView(unit.Position,78,25,unit.Facing*Mathf.Rad2Deg+180);
                yield return Capture(unit.Side+"-line.png");
                foreach(int role in new[]{4,3,0,2}){
                    var offset=CommandPartyLayout.Offset(unit,role,0,count);
                    Vector2 position=unit.Position+FormationMath.Right(unit.Facing)*offset.x+FormationMath.Forward(unit.Facing)*offset.y;
                    if(role==0||role==3)for(int soldier=unit.SoldierStart;soldier<unit.SoldierStart+unit.SoldierCount;soldier++)
                        if(world.Soldiers.Alive[soldier]&&(role==0?world.Soldiers.Captains[soldier]:world.Soldiers.PikeSergeants[soldier])){position=world.Soldiers.Positions[soldier];break;}
                    view.SetInspectionView(position,role==4?9:7,18,unit.Facing*Mathf.Rad2Deg+145);
                    yield return Capture(unit.Side+"-"+(role==4?"colonel":role==3?"sergeant":role==2?"colours":"captain")+".png");
                    if(role==0||role==3){
                        int actor=-1;
                        for(int soldier=unit.SoldierStart;soldier<unit.SoldierStart+unit.SoldierCount;soldier++)
                            if(world.Soldiers.Alive[soldier]&&(role==0?world.Soldiers.Captains[soldier]:world.Soldiers.PikeSergeants[soldier])){actor=soldier;break;}
                        if(actor>=0){
                            world.Soldiers.MeleeActions[actor]=role==0?MeleeAction.SlashRight:MeleeAction.Thrust;
                            world.Soldiers.MeleeStarts[actor]=world.Time;world.Soldiers.MeleeEnds[actor]=world.Time+.95f;
                            view.GetPresentationTime=()=>world.Time+MeleeSystem.ImpactDelay(world.Soldiers.MeleeActions[actor]);
                            yield return Capture(unit.Side+(role==0?"-captain-sabre.png":"-sergeant-thrust.png"));
                            world.Soldiers.MeleeEnds[actor]=world.Time;world.Soldiers.MeleeActions[actor]=MeleeAction.None;
                            if(role==0){
                                world.Soldiers.FireTimes[actor]=world.Time;
                                var rotation=Quaternion.Euler(0,world.Soldiers.Facings[actor]*Mathf.Rad2Deg,0);
                                world.Soldiers.FireOrigins[actor]=world.Soldiers.FireLockOrigins[actor]=world.Terrain.SurfacePoint(world.Soldiers.Positions[actor])+rotation*new Vector3(-.29f,1.3f,.75f);
                                world.Soldiers.FireDirections[actor]=rotation*Vector3.forward;
                                view.GetPresentationTime=()=>world.Time+.02f;
                                yield return Capture(unit.Side+"-captain-revolver.png");
                                world.Soldiers.FireTimes[actor]=-100;
                            }
                            view.GetPresentationTime=()=>world.Time+.67f;
                        }
                    }
                }
            }
            view.SelectUnit(french.Id,false);
            world.SetFormation(french.Id,FormationKind.Square);for(int i=0;i<1000;i++)world.StepMovement(.05f);
            view.SetInspectionView(french.Position,32,65,180);yield return Capture("French-square.png");
            world.SetFormation(french.Id,FormationKind.Column);for(int i=0;i<1000;i++)world.StepMovement(.05f);
            view.SetInspectionView(french.Position,52,40,150);yield return Capture("French-column.png");
            Debug.Log("REGIMENT_VISUAL_REVIEW_COMPLETE");Application.Quit();
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,name));yield return new WaitForSecondsRealtime(.4f);
        }
    }
}
