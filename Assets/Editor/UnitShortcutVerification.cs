using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class UnitShortcutVerification
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool pass,string message){if(!pass)throw new Exception("Unit shortcuts: "+message);}
    public static void Verify()
    {
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=2,SoldiersPerBattalion=64,EnableLandscape=false,EnableAI=false,UseDetailedInfantry=false});
        var root=new GameObject("Unit shortcut verification");
        try{
            var view=root.AddComponent<BattleView>();view.Initialize(world);
            view.SelectUnit(0,false);view.SelectUnit(1,true);
            var first=world.Battalions[0];var second=world.Battalions[1];
            world.IssueMove(0,first.Position+new Vector2(100,30),0);world.IssueMove(1,second.Position+new Vector2(100,30),0);
            view.OrderHalt();Require(!first.Moving&&!second.Moving,"Halt stops every selected formation");
            view.SetInspectionView(first.Position,800,70);view.TrackSelectedUnit();
            int Tracked()=>(int)typeof(BattleView).GetField("trackedId",Hidden).GetValue(view);
            Vector3 Focus()=>(Vector3)typeof(BattleView).GetField("focus",Hidden).GetValue(view);
            Require(Tracked()==0,"Delete selects the first living selected formation");
            float distance=(float)typeof(BattleView).GetField("distance",Hidden).GetValue(view);
            Require(distance>=24&&distance<=90,"Tracking zooms in from the overview");
            first.Position+=new Vector2(32,19);
            typeof(BattleView).GetMethod("UpdateTrackedUnit",Hidden).Invoke(view,null);
            Require(Vector3.Distance(Focus(),world.Terrain.SurfacePoint(first.Position))<.001f,"Camera focus follows movement");
            view.TrackSelectedUnit();Require(Tracked()==0,"Repeated Delete retains tracking");
            view.ZoomAt(new Vector2(400,300),1);Require(Tracked()==-1,"Manual zoom releases tracking");
            view.TrackSelectedUnit();first.AliveCount=0;
            typeof(BattleView).GetMethod("UpdateTrackedUnit",Hidden).Invoke(view,null);Require(Tracked()==-1,"Destroyed units release tracking");
            view.SelectUnit(-1,false);view.TrackSelectedUnit();Require(Tracked()==-1,"Empty selection is harmless");
            Debug.Log("UNIT_SHORTCUTS_VERIFIED: group halt, close tracking, moving focus, repeated Delete, manual release and dead/empty selection.");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
