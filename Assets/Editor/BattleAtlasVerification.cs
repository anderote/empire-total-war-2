using System;
using System.Reflection;
using EmpireBattle;
using UnityEngine;

public static class BattleAtlasVerification
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Require(bool value,string message)
    {if(!value)throw new Exception("Battle atlas: "+message);}
    static object Invoke(BattleView view,string method,params object[] args)
        => typeof(BattleView).GetMethod(method,Hidden).Invoke(view,args);
    static void Place(BattleWorld world,int id,Vector2 position)
    {
        var unit=world.Battalions[id];Vector2 delta=position-unit.Position;
        unit.Position=unit.Destination=position;
        for(int i=unit.SoldierStart;i<unit.SoldierStart+unit.SoldierCount;i++)world.Soldiers.Positions[i]+=delta;
    }
    public static void VerifyGroundFootprints()
    {
        foreach(bool landscape in new[]{false,true})
        {
            var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=200,
                FrenchArtillery=0,BritishArtillery=0,EnableAI=false,EnableLandscape=landscape,UseDetailedInfantry=false});
            Place(world,0,new Vector2(-900,600));Place(world,1,new Vector2(900,-600));
            var root=new GameObject("Atlas ground footprint verification");
            try
            {
                var view=root.AddComponent<BattleView>();view.Initialize(world);
                var camera=view.BattleCamera;camera.pixelRect=new Rect(0,0,1440,900);camera.aspect=1.6f;
                var unit=world.Battalions[0];unit.Facing=.6f;
                Vector2 half=FormationMath.HalfExtents(unit)+Vector2.one;
                Vector2 right=FormationMath.Right(unit.Facing),forward=FormationMath.Forward(unit.Facing);
                view.SetInspectionView(unit.Position,120,55);view.ToggleAtlasView();
                Require(view.AtlasBlend==1,"M enables the atlas at close range");
                foreach(float distance in new[]{35f,120f,700f})
                foreach(float yaw in new[]{0f,43f,90f})
                foreach(float pitch in new[]{20f,55f,82f})
                {
                    view.SetInspectionView(unit.Position,distance,pitch,yaw);
                    foreach(float side in new[]{-1f,1f})
                    {
                        Vector2 ground=unit.Position+right*(side*half.x*.9f);
                        Vector3 point=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(ground));
                        if(point.z<=camera.nearClipPlane)continue;
                        Require((int)Invoke(view,"PickAtlasUnit",(Vector2)point,ArmySide.French,false)==unit.Id,
                            "Atlas click follows ground footprint at "+distance+" / "+yaw+" / "+pitch+" landscape="+landscape);
                    }
                    if(distance<120)continue;
                    Vector2 outside=unit.Position+forward*(half.y+20);
                    Vector2 empty=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(outside));
                    Require((int)Invoke(view,"PickAtlasUnit",empty,ArmySide.French,false)==-1,
                        "Atlas excludes ground outside the formation at oblique angles");
                    Vector2 edge=unit.Position+right*(half.x*.9f);
                    Vector2 projected=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(edge,.12f));
                    view.SelectUnitsInRect(new Rect(projected.x-2,projected.y-2,4,4));
                    Require(view.SelectedRegimentCount==1,"Atlas drag selects the projected terrain footprint");
                }
                view.SetInspectionView(unit.Position+right*half.x,12,55,43);
                Vector2 visibleEdge=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(unit.Position+right*(half.x-.5f)));
                Require((int)Invoke(view,"PickAtlasUnit",visibleEdge,ArmySide.French,false)==unit.Id,
                    "Visible formation edge remains clickable with its center offscreen");
                Vector2 edgeOnOutline=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(unit.Position+right*(half.x-.5f),.12f));
                Require((bool)Invoke(view,"FormationOverlaps",unit,new Rect(edgeOnOutline.x-2,edgeOnOutline.y-2,4,4)),
                    "Drag selection retains visible ground when the formation crosses the near plane");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        Debug.Log("BATTLE_ATLAS_GROUND_VERIFIED: terrain footprints, rotated close views, empty-ground rejection, drag selection and offscreen centers.");
    }
    public static void Verify()
    {
        VerifyGroundFootprints();
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=2,SoldiersPerBattalion=200,
            FrenchArtillery=1,BritishArtillery=0,EnableAI=false,EnableLandscape=false,UseDetailedInfantry=false});
        Place(world,0,Vector2.zero);Place(world,1,new Vector2(300,0));
        Place(world,2,new Vector2(-300,0));Place(world,3,new Vector2(0,400));
        var root=new GameObject("Battle atlas verification");
        try
        {
            var view=root.AddComponent<BattleView>();view.Initialize(world);
            var camera=view.BattleCamera;camera.pixelRect=new Rect(0,0,1440,900);camera.aspect=1.6f;
            view.SetInspectionView(Vector2.zero,150,55);
            Require(view.AtlasBlend==0,"Close view retains terrain presentation");
            view.SetInspectionView(Vector2.zero,300,55);
            Require(view.AtlasBlend>.05f,"Atlas begins before the previous 350m height threshold");
            view.SetInspectionView(Vector2.zero,650,55);
            Require(view.AtlasBlend==1,"Atlas completes at a lower zoom distance");
            Require(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.x,55))<.01f,"Atlas does not override camera tilt");
            view.SetInspectionView(new Vector2(20,30),1100,35,40);
            Require(view.AtlasBlend==1&&Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.x,35))<.01f&&
                Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y,40))<.01f,"Full atlas accepts tilt, rotation and panning");
            view.SetInspectionView(Vector2.zero,650,55,0);
            Vector2 cursor=new Vector2(830,490);object[] groundArgs={cursor,Vector2.zero};
            Require((bool)Invoke(view,"GroundPoint",groundArgs),"Atlas terrain can be picked");
            var anchor=(Vector2)groundArgs[1];view.ZoomAt(cursor,9);
            for(int i=0;i<180;i++)view.StepCameraZoom(1f/60);
            Vector2 projected=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(anchor));
            Require(Vector2.Distance(projected,cursor)<.5f,"Zoom retains cursor anchor through atlas fade");

            var unit=world.Battalions[0];
            foreach(float distance in new[]{120f,350f,700f})
            foreach(float yaw in new[]{0f,43f})
            {
                view.SetInspectionView(Vector2.zero,distance,55,yaw);
                Vector2 edge=unit.Position+FormationMath.Right(unit.Facing)*FormationMath.HalfExtents(unit).x*.9f;
                Vector2 point=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(edge,.9f));
                var box=new Rect(point.x-2,point.y-2,4,4);
                Vector2 center=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(unit.Position));
                Require(!box.Contains(center),"Edge-only test excludes the regiment center");
                view.SelectUnitsInRect(box);
                Require(view.SelectedRegimentCount==1,"Partial overlap selects exactly one whole regiment at "+distance+" / "+yaw);
                Require((int)Invoke(view,"PickBattalion",point,ArmySide.French,false)==0,"Click on regiment edge selects it");
                unit.Routing=true;view.SelectUnitsInRect(box);
                Require(view.SelectedRegimentCount==0,"Routing regiments are not box selected");unit.Routing=false;
            }
            view.SetInspectionView(Vector2.zero,250,55,0);
            int soldier=unit.SoldierStart;world.Soldiers.Positions[soldier]=new Vector2(65,35);
            Vector2 detached=camera.WorldToScreenPoint(world.Terrain.SurfacePoint(world.Soldiers.Positions[soldier],1));
            view.SelectUnitsInRect(new Rect(detached.x-2,detached.y-2,4,4));
            Require(view.SelectedRegimentCount==1,"Dispersed soldier selects its entire regiment");
            Require((int)Invoke(view,"PickBattalion",detached,ArmySide.French,false)==0,"Dispersed soldier is clickable");
            view.SelectUnit(1,false);view.SelectUnitsInRect(new Rect(detached.x-2,detached.y-2,4,4),true);
            Require(view.SelectedRegimentCount==2,"Additive box selection preserves previous regiments");
            world.Soldiers.Alive[soldier]=false;view.SelectUnitsInRect(new Rect(detached.x-2,detached.y-2,4,4));
            Require(view.SelectedRegimentCount==0,"Dead detached soldiers do not select a regiment");
            view.SetInspectionView(Vector2.zero,7500,55,0);
            Vector3 shotPosition=world.Terrain.SurfacePoint(Vector2.zero,2);
            float width=ProjectileVisibility.Width(camera,shotPosition,2,.26f,1.4f,view.AtlasBlend);
            Vector2 p0=camera.WorldToScreenPoint(shotPosition),p1=camera.WorldToScreenPoint(shotPosition+camera.transform.up*width);
            Require(Vector2.Distance(p0,p1)>1.9f,"Cannonballs retain two-pixel visibility at maximum zoom: pixels="+Vector2.Distance(p0,p1)+" width="+width+" viewport="+camera.pixelRect+" aspect="+camera.aspect+" blend="+view.AtlasBlend);
            var corners=(Vector2[])typeof(BattleView).GetField("selectionCorners",Hidden).GetValue(view);
            typeof(BattleView).GetField("selectionCornerCount",Hidden).SetValue(view,4);
            corners[0]=new Vector2(0,10);corners[1]=new Vector2(10,0);corners[2]=new Vector2(0,-10);corners[3]=new Vector2(-10,0);
            Require(!(bool)Invoke(view,"SelectionOutlineOverlaps",new Rect(8,8,1,1)),"Rotated formations do not select empty bounding-box corners");
            Require((bool)Invoke(view,"SelectionOutlineOverlaps",new Rect(-20,-1,40,2)),"Crossing drag selects even with no formation corner inside");
            view.SelectUnit(-1,false);
            view.ToggleAtlasView();
            Require(view.AtlasBlend==0,"M can turn off the automatic atlas with nothing selected");
            foreach(float cameraDistance in new[]{3f,150f,650f,7500f})
            foreach(float cameraPitch in new[]{8f,55f,82f})
            {
                view.SetInspectionView(new Vector2(20,30),cameraDistance,cameraPitch,43);
                Require(view.AtlasBlend==0,"Manual terrain view survives every camera scale and angle");
                Vector3 position=camera.transform.position;Quaternion rotation=camera.transform.rotation;
                view.ToggleAtlasView();
                Require(view.AtlasBlend==1&&camera.transform.position==position&&camera.transform.rotation==rotation,
                    "White map toggles on without changing the camera");
                view.SetInspectionView(Vector2.zero,cameraDistance<500?7500:3,35,120);
                Require(view.AtlasBlend==1,"White map stays on while zooming, panning and rotating");
                view.ToggleAtlasView();
                Require(view.AtlasBlend==0,"White map toggles off at any scale");
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        UnitShortcutVerification.Verify();
        typeof(InfantryManeuverVerification).GetMethod("VerifyZoomAndDeployment",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        BattleOrdersVerification.Verify();
        Debug.Log("BATTLE_ATLAS_VERIFIED: earlier fade, free camera, cursor anchor, partial/rotated/dispersed regiment selection, routing/dead exclusions, additive selection and maximum-zoom projectile visibility.");
    }
}
