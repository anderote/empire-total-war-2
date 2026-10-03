using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Opt-in visual review of actual runtime markers: --ui-review /absolute/output/directory.</summary>
    public sealed class BattleUIReview : MonoBehaviour
    {
        string directory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartRequestedReview()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--ui-review");
            if (index < 0 || index + 1 >= args.Length) return;
            var review = new GameObject("Battle UI review").AddComponent<BattleUIReview>();
            DontDestroyOnLoad(review.gameObject);
            review.directory = Path.GetFullPath(args[index + 1]);
        }
        static void VerifyFlagPicking(BattleView view, BattleWorld world)
        {
            const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
            var markers = (BattleTacticalMarkers)typeof(BattleView).GetField("tacticalMarkers", access).GetValue(view);
            var flags = (Rect[])typeof(BattleTacticalMarkers).GetField("flags", access).GetValue(markers);
            var alpha = (float[])typeof(BattleTacticalMarkers).GetField("opacity", access).GetValue(markers);
            int tested = 0;
            foreach (var unit in world.Battalions)
            {
                if (alpha[unit.Id] <= .25f || unit.Routing || unit.AliveCount == 0) continue;
                var projected = view.BattleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(unit.Position, 5));
                if (Mathf.Abs(flags[unit.Id].center.x - projected.x) > .1f ||
                    Mathf.Abs(flags[unit.Id].yMax - (Screen.height - projected.y - 12)) > .1f)
                    throw new Exception("Flag displaced from formation: " + unit.Id);
                var center = flags[unit.Id].center;
                int hit = markers.Pick(new Vector2(center.x, Screen.height - center.y), unit.Side);
                if (hit != unit.Id) throw new Exception("Flag hit-test mismatch: " + unit.Id + " picked " + hit);
                tested++;
            }
            if (tested == 0) throw new Exception("No visible flags to verify.");
            Debug.Log("UI_FLAG_HIT_TEST_PASSED count=" + tested);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(directory);
            yield return new WaitForSecondsRealtime(3);
            var runtime = FindAnyObjectByType<BattleRuntime>(); var view = FindAnyObjectByType<BattleView>();
            if (!runtime || !view) { Debug.LogError("UI review requires a battle scene."); yield break; }
            var armory=FindAnyObjectByType<BattleArmory>();
            if(armory&&armory.IsOpen)typeof(BattleArmory).GetMethod("Close",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(armory,null);
            if (view.IsPaused != null && !view.IsPaused()) view.OnTogglePause?.Invoke();
            view.SelectUnit(0, false);
            var selectedUnit = runtime.World.Battalions[0];
            bool wasRunning = selectedUnit.Running;
            var destination = selectedUnit.Destination;
            view.OrderToggleRun();
            if (selectedUnit.Running == wasRunning || selectedUnit.Routing || selectedUnit.Withdrawing || selectedUnit.Destination != destination)
                throw new Exception("Run toggle changed withdrawal/destination or failed to change gait.");
            view.OrderToggleRun();
            if (selectedUnit.Running != wasRunning) throw new Exception("Run toggle failed to return to previous gait.");
            Debug.Log("UI_RUN_TOGGLE_PASSED");
            view.SetInspectionView(runtime.World.Battalions[0].Position, 85, 55);
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "selected-bases.png"));
            yield return new WaitForSecondsRealtime(1);
            var hud = FindAnyObjectByType<BattleHUD>();
            float uiScale=BattleUILayout.Scale(Screen.width,Screen.height);
            float logicalWidth=Screen.width/uiScale,logicalHeight=Screen.height/uiScale;
            foreach(var logical in new[]{BattleUILayout.Header(logicalWidth),BattleUILayout.MenuButton,BattleUILayout.Roster(logicalWidth,logicalHeight),BattleUILayout.Detail(logicalWidth,logicalHeight)})
                if(!hud.IsPointerOverUI(BattleUILayout.Physical(logical,uiScale).center))throw new Exception("Battle HUD pointer exclusion failed: "+logical);
            if(hud.IsPointerOverUI(BattleUILayout.Physical(BattleUILayout.Map(logicalWidth),uiScale).center))throw new Exception("Removed battlefield map still blocks the field.");
            Debug.Log("UI_HUD_POINTER_EXCLUSION_PASSED");
            var atmosphere=FindAnyObjectByType<BattleAtmosphere>();
            if(!atmosphere||atmosphere.Hour!=7f||atmosphere.Weather!=WeatherKind.Clear||atmosphere.ControlsRect!=Rect.zero)
                throw new Exception("Battle is not fixed clear dawn without atmosphere controls.");
            Rect compact=BattleUILayout.Detail(logicalWidth,logicalHeight);
            if(compact.width>860||compact.height>108)throw new Exception("Selection panel is oversized.");
            if(hud.IsPointerOverUI(new Vector2(20,compact.center.y*uiScale)))throw new Exception("Old full-width selection panel still blocks the field.");
            Debug.Log("UI_COMPACT_PANEL_AND_CLEAR_DAWN_PASSED");
            // Inspect the cursor where a real right-click would move or attack.
            view.SetOverview();
            yield return null;
            Vector2 movePointer=Vector2.zero;
            bool foundMove=false;
            for(int y=120;y<Screen.height-280&&!foundMove;y+=20)
                for(int x=30;x<Screen.width-30;x+=20)
                    if(view.CommandCursorAt(new Vector2(x,y))==BattleView.CommandCursorKind.Move)
                    {movePointer=new Vector2(x,y);foundMove=true;break;}
            if(!foundMove)throw new Exception("No move cursor on open battlefield.");
            view.ReviewCommandPointer=movePointer;
            yield return null;yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,"cursor-move.png"));
            yield return new WaitForSecondsRealtime(.5f);
            var enemy=runtime.World.Battalions[runtime.World.Config.FrenchBattalionCount];
            Vector3 enemyScreen=view.BattleCamera.WorldToScreenPoint(runtime.World.Terrain.SurfacePoint(enemy.Position,2));
            Vector2 attackPointer=new Vector2(enemyScreen.x,enemyScreen.y);
            if(view.CommandCursorAt(attackPointer)!=BattleView.CommandCursorKind.Attack)throw new Exception("Enemy cursor is not attack.");
            view.ReviewCommandPointer=attackPointer;
            yield return null;yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,"cursor-attack.png"));
            yield return new WaitForSecondsRealtime(.5f);
            Vector2 uiPointer=BattleUILayout.Physical(compact,uiScale).center;
            uiPointer.y=Screen.height-uiPointer.y;
            if(view.CommandCursorAt(uiPointer)!=BattleView.CommandCursorKind.Default)throw new Exception("Command cursor persisted over UI.");
            view.ReviewCommandPointer=null;
            view.SetInspectionView(runtime.World.Battalions[0].Position,85,55);
            yield return null;
            Debug.Log("UI_COMMAND_CURSOR_PREVIEW_PASSED");
            const BindingFlags hudAccess=BindingFlags.Instance|BindingFlags.NonPublic;
            var page=typeof(BattleHUD).GetField("rosterPage",hudAccess);
            var menu=typeof(BattleHUD).GetField("menuOpen",hudAccess);
            int cards=0,lastBattery=-1;
            foreach(var regiment in runtime.World.Battalions)if(regiment.Side==ArmySide.French)cards++;
            foreach(var cannon in view.CommandGuns)if(cannon.Side==ArmySide.French&&cannon.UnitId!=lastBattery){cards++;lastBattery=cannon.UnitId;}
            page.SetValue(hud,BattleUILayout.Pages(logicalWidth,cards)-1);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,"hud-last-roster-page.png"));
            yield return new WaitForSecondsRealtime(1);
            page.SetValue(hud,0);menu.SetValue(hud,true);
            yield return new WaitForEndOfFrame();
            if(!hud.IsPointerOverUI(BattleUILayout.Physical(BattleUILayout.Menu(logicalWidth),uiScale).center))throw new Exception("Battle menu pointer exclusion failed.");
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,"hud-battle-menu.png"));
            yield return new WaitForSecondsRealtime(1);
            menu.SetValue(hud,false);

            var unit = runtime.World.Battalions[0];
            view.SetInspectionView(unit.Position + FormationMath.Forward(unit.Facing) * 110, 450, 65);
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "infantry-fire-range.png"));
            yield return new WaitForSecondsRealtime(1);
            view.SetInspectionView(unit.Position + FormationMath.Forward(unit.Facing) * 110, 650, 55);
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            VerifyFlagPicking(view, runtime.World);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "battle-flags.png"));
            yield return new WaitForSecondsRealtime(1);
            var gunSystem = (ArtillerySystem)typeof(BattleView).GetField("commandArtillery", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            if (gunSystem != null && gunSystem.Guns.Length > 0)
            {
                view.SelectGun(0, false);
                var gun = gunSystem.Guns[0];
                view.SetOverview();yield return null;
                if(view.CommandCursorAt(movePointer)!=BattleView.CommandCursorKind.Attack)throw new Exception("Artillery ground cursor should preview bombardment.");
                view.SetSelectedAmmunition(ArtilleryAmmunition.Canister);
                view.SetInspectionView(gun.Position, 650, 75);
                yield return new WaitForSecondsRealtime(1);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "artillery-fire-range.png"));
                yield return new WaitForSecondsRealtime(1);
                view.SetSelectedAmmunition(ArtilleryAmmunition.Roundshot);
            }
            view.SelectUnit(unit.Id, false);
            view.SetOverview();
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "battle-atlas.png"));
            if (view.AtlasBlend < .95f) throw new Exception("Overview did not reach atlas presentation.");
            var atlasMarkers = (BattleTacticalMarkers)typeof(BattleView).GetField("tacticalMarkers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var atlasFlags = (float[])typeof(BattleTacticalMarkers).GetField("opacity", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(atlasMarkers);
            foreach (float opacity in atlasFlags) if (opacity > .05f) throw new Exception("Flag persisted in atlas view.");
            Debug.Log("UI_ANCHORED_FLAGS_AND_ATLAS_REPLACEMENT_PASSED");
            const BindingFlags privateAccess = BindingFlags.Instance | BindingFlags.NonPublic;
            var pickAtlas = typeof(BattleView).GetMethod("PickAtlasUnit", privateAccess);
            var screen = view.BattleCamera.WorldToScreenPoint(runtime.World.Terrain.SurfacePoint(unit.Position, 2));
            int pickedAtlas = (int)pickAtlas.Invoke(view, new object[] { new Vector2(screen.x, screen.y), unit.Side });
            if (pickedAtlas != unit.Id) throw new Exception("Atlas formation pick failed: " + pickedAtlas);
            for (int i = 0; i < 8; i++)
            {
                var ordered = runtime.World.Battalions[i];
                runtime.World.IssueMove(ordered.Id, ordered.Position + new Vector2((i % 3 - 1) * 70, 200), ordered.Facing);
            }
            runtime.World.IssueRangedAttack(runtime.World.Battalions[8].Id, runtime.World.Battalions[runtime.World.Config.FrenchBattalionCount].Id);
            if (gunSystem != null && gunSystem.Guns.Length > 0)
                foreach (var gun in gunSystem.Guns) if (gun.Side == ArmySide.French && gun.UnitId == gunSystem.Guns[0].UnitId)
                    gunSystem.OrderTarget(gun.Id, runtime.World.Config.FrenchBattalionCount + 2);
            view.ReviewAtlasOrders = true;
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "battle-atlas-orders.png"));
            yield return new WaitForSecondsRealtime(1);
            if (view.VisibleAtlasAttackOrders < 1) throw new Exception("Space atlas overlay did not draw attack orders.");
            Debug.Log("UI_ATLAS_ATTACK_ORDERS_PASSED count=" + view.VisibleAtlasAttackOrders);
            view.ReviewAtlasOrders = false;
            view.SetInspectionView(unit.Position, 1000, 60);
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "battle-atlas-transition.png"));
            if (view.AtlasBlend <= 0 || view.AtlasBlend >= 1) throw new Exception("Atlas transition is not intermediate.");
            view.SetInspectionView(unit.Position, 85, 55);
            if (view.AtlasBlend != 0) throw new Exception("Close inspection retained atlas styling.");
            Debug.Log("BATTLE_ATLAS_REVIEW_PASSED");
            Debug.Log("BATTLE_UI_REVIEW_COMPLETE");
            Application.Quit();
        }
    }
}
