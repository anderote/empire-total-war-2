using Empire.UI;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    // All soldiers are mesh instances. GameObjects are reserved for the camera and terrain.
    public sealed partial class BattleView : MonoBehaviour
    {
        public Action<string,string> OnRosterBattle;
        public Action OnRestart;
        public Action OnConcedeBattle;
        public Action OnTogglePause;
        public Func<bool> IsPaused;
        public Func<float> GetPresentationTime;
        public Action OnCycleSpeed;
        public Action<float> OnSetSpeed;
        public BattleCinematics Cinematics { get; set; }
        public float CameraFocusDepth => battleCamera ? Mathf.Max(.1f, Vector3.Dot(focus-battleCamera.transform.position,battleCamera.transform.forward)) : 100f;
        public Func<float> GetSpeed;
        public bool EnableInput = true;
        public bool HideReviewUI;
        public bool ModernCrowdInstancing=true;
        public int CrowdBatchCapacity=511;
        BattleWorld world;
        Camera battleCamera;
        Mesh soldierMesh, cubeMesh;
        Material french, british, marker, attackMarker;
        readonly Matrix4x4[] instances = new Matrix4x4[1023];
        readonly Plane[] frustum = new Plane[6];
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        bool[] selected;
        Vector3 focus;
        public const float MinimumCameraDistance=3f, MaximumCameraDistance=7500f;
        float distance = 700f, zoomTarget = 700f, yaw, pitch = 55f;
        bool zoomAnchored;
        Vector2 zoomAnchorScreen, zoomAnchorGround;
        BattleHUD hud;
        BattleTacticalMarkers tacticalMarkers;
        CrowdInfantryRenderer crowd;
        CavalryView cavalry;
        public bool UsesDetailedInfantry => crowd!=null&&crowd.Ready;
        public Camera BattleCamera => battleCamera;
        Vector2 leftStart, rightStart;
        Vector2 moveDestination;
        bool selecting, ordering, previewFrontage;
        bool[] previewActive;
        Vector2[] previewDestinations;
        float[] previewFacings;
        int[] previewFiles, previewOrder;
        readonly bool[][] groups = new bool[9][];
        int trackedId = -1;
        float lastRightClick = -10f;
        bool runOrder;
        Vector2 lastRightPosition;
        bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool QueueModifier => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool queueOrder;
        bool Additive => Ctrl || QueueModifier;
        string statusValue = "";
        float statusTime;
        string status { get => statusValue; set { statusValue=value;statusTime=Time.unscaledTime; } }
        GUIStyle titleStyle, textStyle;
        int visibleSoldiers;
        public int VisibleSoldiers => visibleSoldiers;

        public void Initialize(BattleWorld value)
        {
            world = value;
            var atlasWoods = new Vector4[16];
            int woodsCount = Mathf.Min(atlasWoods.Length, world.Terrain.WoodsCenters.Length);
            for (int i = 0; i < woodsCount; i++)
                atlasWoods[i] = new Vector4(world.Terrain.WoodsCenters[i].x, world.Terrain.WoodsCenters[i].y, world.Terrain.WoodsRadii[i], 0);
            Shader.SetGlobalVectorArray("_BattleAtlasWoods", atlasWoods);
            Shader.SetGlobalFloat("_BattleAtlasWoodsCount", woodsCount);
            var atlasFields = new Vector4[16];
            for (int i = 0; i < RuralFieldLayout.Fields.Length; i++)
            {
                var field = RuralFieldLayout.Fields[i];
                atlasFields[i] = new Vector4(field.Center.x, field.Center.y, field.Half.x, field.Half.y);
            }
            Shader.SetGlobalVectorArray("_BattleAtlasFields", atlasFields);
            Shader.SetGlobalFloat("_BattleAtlasFieldCount", world.Config.EnableLandscape ? RuralFieldLayout.Fields.Length : 0);
            Shader.SetGlobalFloat("_BattleAtlasMapScale", world.Config.FieldHalfSize / 1500f);
            selected = new bool[world.Battalions.Length];
            previewActive = new bool[selected.Length];
            previewDestinations = new Vector2[selected.Length];
            previewFacings = new float[selected.Length];
            previewFiles = new int[selected.Length];
            previewOrder = new int[selected.Length];
            for (int i = 0; i < groups.Length; i++) groups[i] = new bool[selected.Length];
            var shader = Resources.Load<Shader>("BattleSoldier");
            if (!shader) shader = Shader.Find("EmpireBattle/InstancedUnlit");
            if (!shader) shader = Shader.Find("Universal Render Pipeline/Unlit");
            french = MakeMaterial(shader, world.Config.PlayerCountryColor);
            british = MakeMaterial(shader, world.Config.EnemyCountryColor);
            french.SetFloat("_AtlasTroop", 1);
            british.SetFloat("_AtlasTroop", 1);
            marker = MakeMaterial(shader, new Color(.35f,.85f,1f));
            attackMarker = MakeMaterial(shader, new Color(1f,.32f,.14f));
            cubeMesh = MakeCube();
            soldierMesh = MakeSoldier();
            var cameraObject = new GameObject("Battle camera");
            owned.Add(cameraObject);
            battleCamera = cameraObject.AddComponent<Camera>();
            battleCamera.tag = "MainCamera";
            battleCamera.nearClipPlane = .12f;
            battleCamera.farClipPlane = Mathf.Max(9000f,world.Config.FieldHalfSize*2.5f);
            battleCamera.fieldOfView = 55;
            battleCamera.clearFlags = CameraClearFlags.SolidColor;
            battleCamera.backgroundColor = new Color(.57f,.68f,.76f);
            cameraObject.AddComponent<AudioListener>();
            SetOverview();
            hud = gameObject.AddComponent<BattleHUD>();
            hud.Initialize(world,this,selected,battleCamera);
            tacticalMarkers=new BattleTacticalMarkers(world,battleCamera,selected,cubeMesh);
            cavalry=new CavalryView(world,battleCamera);
            if(world.Config.UseDetailedInfantry)crowd=new CrowdInfantryRenderer(world,battleCamera,ModernCrowdInstancing,CrowdBatchCapacity);
        }

        public void SetCasualtyView(BattleCasualtyView value){if(cavalry!=null)cavalry.Casualties=value;if(crowd!=null)crowd.Casualties=value;}
        public void SetOverview()
        {
            if(world==null||!battleCamera)return;
            Vector2 minimum=new Vector2(float.MaxValue,float.MaxValue);
            Vector2 maximum=new Vector2(float.MinValue,float.MinValue);
            for(int i=0;i<world.Soldiers.Positions.Length;i++)
            {
                if(!world.Soldiers.Alive[i])continue;
                minimum=Vector2.Min(minimum,world.Soldiers.Positions[i]);
                maximum=Vector2.Max(maximum,world.Soldiers.Positions[i]);
            }
            if(minimum.x==float.MaxValue)return;
            var center=(minimum+maximum)*.5f;
            focus=world.Terrain.SurfacePoint(center);
            zoomAnchored=false;
            yaw=0;pitch=60;
            float radius=(maximum-minimum).magnitude*.5f+40;
            float verticalHalf=battleCamera.fieldOfView*.5f*Mathf.Deg2Rad;
            float horizontalHalf=Mathf.Atan(Mathf.Tan(verticalHalf)*battleCamera.aspect);
            distance=Mathf.Clamp(radius/Mathf.Sin(Mathf.Min(verticalHalf,horizontalHalf))*1.12f,350,MaximumCameraDistance);
            zoomTarget=distance;
            UpdateCamera();
        }

        public void SetInspectionView(Vector2 center,float cameraDistance=120f,float cameraPitch=55f,float? cameraYaw=null)
        {
            if(!battleCamera)return;
            trackedId=-1;
            focus=world.Terrain.SurfacePoint(center);
            distance=Mathf.Clamp(cameraDistance,MinimumCameraDistance,MaximumCameraDistance);
            zoomAnchored=false;
            pitch=Mathf.Clamp(cameraPitch,8,80);
            if(cameraYaw.HasValue)yaw=cameraYaw.Value;
            zoomTarget=distance;
            UpdateCamera();
        }

        Material MakeMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { enableInstancing = true };
            material.SetColor("_BaseColor",color);
            owned.Add(material);
            return material;
        }

        Mesh MakeCube()
        {
            var temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = Instantiate(temporary.GetComponent<MeshFilter>().sharedMesh);
            Destroy(temporary);
            owned.Add(mesh);
            return mesh;
        }

        Mesh MakeSoldier()
        {
            // A single mesh combines torso, head, hat, legs and a musket silhouette.
            var pieces = new CombineInstance[7];
            pieces[0] = Part(new Vector3(0,1.05f,0),new Vector3(.45f,.7f,.28f));
            pieces[1] = Part(new Vector3(0,1.55f,0),new Vector3(.28f,.28f,.26f));
            pieces[2] = Part(new Vector3(0,1.79f,0),new Vector3(.34f,.22f,.3f));
            pieces[3] = Part(new Vector3(-.12f,.38f,0),new Vector3(.17f,.75f,.2f));
            pieces[4] = Part(new Vector3(.12f,.38f,0),new Vector3(.17f,.75f,.2f));
            pieces[5] = Part(new Vector3(.31f,1.04f,.08f),new Vector3(.12f,.55f,.14f));
            pieces[6] = Part(new Vector3(.35f,1.2f,.45f),new Vector3(.07f,.07f,1.4f));
            var mesh = new Mesh { name = "Instanced soldier silhouette" };
            mesh.CombineMeshes(pieces,true,true);
            owned.Add(mesh);
            return mesh;
        }
        CombineInstance Part(Vector3 position,Vector3 scale) => new CombineInstance { mesh=cubeMesh,transform=Matrix4x4.TRS(position,Quaternion.identity,scale) };

        void Update()
        {
            if(world == null) return;
            if(EnableInput)
            {
                HandleCamera();
                if(Input.GetKeyDown(KeyCode.M)&&!Ctrl)ToggleAtlasView();
            }
            Shader.SetGlobalFloat("_BattleAtlasBlend", AtlasBlend);
            tacticalMarkers.AtlasBlend = AtlasBlend;
            tacticalMarkers.UpdateLayout(hud.StatusRect.y-8);
            if(EnableInput){HandleCommands();}
            else {selecting=false;ordering=false;Array.Clear(previewActive,0,previewActive.Length);}
            UpdateBombardSequence();
            UpdateCommandCursor();
            RenderBattle();
        }

        void FocusBattalion(int id)
        {
            if(id<0||id>=world.Battalions.Length)return;
            var b=world.Battalions[id];
            zoomAnchored=false;
            focus=world.Terrain.SurfacePoint(b.Position);
            distance=Mathf.Clamp(b.Files*b.Spacing*2.2f,65,450);
            zoomTarget=distance;
            UpdateCamera();
        }
        int FirstSelected()
        {
            foreach(var b in world.Battalions)if(selected[b.Id]&&b.AliveCount>0)return b.Id;
            return -1;
        }
        void HandleCamera()
        {
            float dt=Time.unscaledDeltaTime;
            float speed=Mathf.Max(2f,distance*.65f)*dt;
            if(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift))speed*=2.5f;
            float h=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0);
            float v=(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow)?1:0);
            if(h!=0||v!=0||Input.GetMouseButton(2)||Input.GetKey(KeyCode.Q)||Input.GetKey(KeyCode.E)||Input.GetKey(KeyCode.V)||Input.GetKey(KeyCode.C))
            {trackedId=-1;zoomAnchored=false;}
            UpdateTrackedUnit();
            focus+=Quaternion.Euler(0,yaw,0)*new Vector3(h,0,v)*speed;
            if(Input.GetMouseButton(2))
            {
                yaw+=Input.GetAxisRaw("Mouse X")*6f;
                pitch=Mathf.Clamp(pitch-Input.GetAxisRaw("Mouse Y")*4f,15,85);
            }
            if(Input.GetKey(KeyCode.Q))yaw-=45f*dt;
            if(Input.GetKey(KeyCode.E))yaw+=45f*dt;
            if(Input.GetKey(KeyCode.X))RequestZoom(Input.mousePosition,Mathf.Exp(dt));
            if(Input.GetKey(KeyCode.Z))RequestZoom(Input.mousePosition,Mathf.Exp(-dt));
            if(Input.GetKey(KeyCode.V))pitch-=30f*dt;
            if(Input.GetKey(KeyCode.C)&&!Ctrl&&FirstSelected()<0)pitch+=30f*dt;
            pitch=Mathf.Clamp(pitch,15,85);
            yaw=Mathf.Repeat(yaw+180f,360f)-180f;
            bool overCards=PointerOverUI(new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y));
            if(!overCards&&Mathf.Abs(Input.mouseScrollDelta.y)>.0001f)
                ZoomAt(Input.mousePosition,Input.mouseScrollDelta.y);
            StepCameraZoom(dt);
            if(Input.GetKeyDown(KeyCode.End)){trackedId=-1;FocusBattalion(FirstSelected());}
            if(Input.GetKeyDown(KeyCode.Delete))TrackSelectedUnit();
            if(Input.GetKeyDown(KeyCode.Home))
            {
                trackedId=-1;
                if(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift))SetOverview();
                else foreach(var b in world.Battalions)if(b.Side==ArmySide.French&&b.AliveCount>0){FocusBattalion(b.Id);break;}
            }
            UpdateCamera();
        }
        public void ZoomAt(Vector2 screen,float wheelSteps)
        {
            float sensitivity=Mathf.Lerp(.035f,.10f,Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(35f,1600f,zoomTarget)));
            RequestZoom(screen,Mathf.Exp(-wheelSteps*sensitivity));
        }
        void RequestZoom(Vector2 screen,float scale)
        {
            if(!battleCamera||float.IsNaN(scale)||scale<0)return;
            float target=Mathf.Clamp(zoomTarget*scale,MinimumCameraDistance,MaximumCameraDistance);
            if(Mathf.Abs(target-zoomTarget)<.0001f)return;
            trackedId=-1;
            zoomAnchored=GroundPoint(screen,out zoomAnchorGround);
            zoomAnchorScreen=screen;
            zoomTarget=target;
        }
        public void StepCameraZoom(float dt)
        {
            if(!battleCamera||dt<=0)return;
            distance=Mathf.Lerp(distance,zoomTarget,1f-Mathf.Exp(-dt*12f));
            UpdateCamera();
            if(zoomAnchored)
            {
                // Correct the terrain anchor throughout the smooth zoom, rather than just
                // at its start. Iteration handles changes in the focus point's elevation.
                for(int iteration=0;iteration<4;iteration++)
                {
                    if(!GroundPoint(zoomAnchorScreen,out Vector2 ground))break;
                    Vector2 correction=zoomAnchorGround-ground;
                    if(correction.sqrMagnitude<.000001f)break;
                    focus.x+=correction.x;focus.z+=correction.y;
                    ClampCameraFocus();UpdateCamera();
                }
                if(Mathf.Abs(distance-zoomTarget)<.001f)zoomAnchored=false;
            }
            ClampCameraFocus();UpdateCamera();
        }
        void ClampCameraFocus()
        {
            focus.x=Mathf.Clamp(focus.x,-world.Config.FieldHalfSize,world.Config.FieldHalfSize);
            focus.z=Mathf.Clamp(focus.z,-world.Config.FieldHalfSize,world.Config.FieldHalfSize);
        }
        void UpdateCamera()
        {
            focus.y=world.Terrain.HeightAt(new Vector2(focus.x,focus.z));
            battleCamera.transform.rotation=Quaternion.Euler(pitch,yaw,0);
            Vector3 position=focus-battleCamera.transform.forward*distance;
            position.y=Mathf.Max(position.y,world.Terrain.HeightAt(new Vector2(position.x,position.z))+.6f);
            battleCamera.transform.position=position;
        }

        bool PointerOverUI(Vector2 guiMouse)
        {
            if(hud!=null&&hud.IsPointerOverUI(guiMouse))return true;
            if(world.Config.RegionalMap||world.Config.RequireDeployment)
                if(new Rect((Screen.width-560)*.5f,124,560,world.DeploymentActive?105:64).Contains(guiMouse))return true;
            if(SelectedGunCount>0&&(gunPanelPointer||GunPanelRect.Contains(guiMouse)))return true;
            var atmosphere=GetComponent<BattleAtmosphere>();
            return atmosphere!=null&&atmosphere.ControlsRect.Contains(guiMouse);
        }

        int PickBattalion(Vector2 mouse,ArmySide side,bool includeRouting=false)
        {
            int atlas=PickAtlasUnit(mouse,side,includeRouting);
            if(atlas>=0)return atlas;
            int flag=tacticalMarkers.Pick(mouse,side,includeRouting);
            if(flag>=0)return flag;
            int formation=PickFormation(mouse,side,includeRouting);
            if(formation>=0)return formation;
            int closest=-1;float best=35;
            foreach(var b in world.Battalions)
            {
                if(b.Side!=side||b.AliveCount==0||(!includeRouting&&b.Routing))continue;
                var p=battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(b.Position,5));
                float d=Vector2.Distance(mouse,p);
                if(p.z>0&&d<best){best=d;closest=b.Id;}
            }
            return closest;
        }
        void SelectBattalion(int id,bool additive)
        {
            if(!additive)ClearSelection();
            if(id>=0)selected[id]=additive?!selected[id]:true;
        }
        void HandleCommands()
        {
            Vector2 mouse=Input.mousePosition;
            Vector2 guiMouse=new Vector2(mouse.x,Screen.height-mouse.y);
            bool overPanel=PointerOverUI(guiMouse);
            bool orienting=Input.GetMouseButton(2);
            if(orienting)
            {
                selecting=false;ordering=false;
                Array.Clear(previewActive,0,previewActive.Length);
            }
            if(Input.GetKeyDown(KeyCode.Escape))
            {
                CancelBombardSequence(true);
                gunBombardOrder=false;
                selecting=false;ordering=false;
                Array.Clear(previewActive,0,previewActive.Length);
            }
            if(Input.GetMouseButtonDown(0)&&!overPanel&&!orienting){leftStart=mouse;selecting=true;}
            if(Input.GetMouseButtonUp(0)&&selecting)
            {
                selecting=false;
                if(Vector2.Distance(leftStart,mouse)>8)
                {
                    var rect=SelectionRect(leftStart,mouse);
                    SelectUnitsInRect(rect,Additive);
                }
                else
                {
                    int gun=PickGun(mouse);
                    if(gun>=0)SelectGun(gun,Additive);
                    else SelectBattalion(PickBattalion(mouse,ArmySide.French),Additive);
                }
            }
            if(Input.GetMouseButtonDown(1)&&!overPanel&&!orienting)
            {
                if (!HasCommandSelection) FireCameraRoundshotAtScreenPoint(mouse);
                else
                {
                int flagTarget=PickBattalion(mouse,ArmySide.British,true);
                bool onGround=GroundPoint(mouse,out moveDestination);
                if(flagTarget>=0)moveDestination=world.Battalions[flagTarget].Position;
                if(flagTarget>=0||onGround)
                {
                    rightStart=mouse;ordering=true;queueOrder=QueueModifier&&!world.DeploymentActive;
                    runOrder=IsDoubleClickRun(Time.unscaledTime-lastRightClick,lastRightPosition,mouse);
                    lastRightClick=Time.unscaledTime;lastRightPosition=mouse;
                }
                }
            }
            if(ordering)
            {
                bool frontage=Vector2.Distance(mouse,rightStart)>10&&GroundPoint(mouse,out _);
                bool validEnd=GroundPoint(mouse,out var end);
                BuildPreview(moveDestination,end,frontage);
                if(Input.GetMouseButtonUp(1))
                {
                    ordering=false;
                    int target=CommandAttackTarget(mouse,frontage);
                    bool bombard=GunBombardModifier&&SelectedGunCount>0;
                    bool bombardLine=bombard&&target<0&&validEnd&&Vector2.Distance(mouse,rightStart)>=BombardDragThresholdPixels;
                    if(target>=0)IssueAttack(target,runOrder);
                    else ApplyPreview(runOrder,frontage);
                    if(bombard)OrderSelectedGunsAlongLine(target,moveDestination,end,bombardLine,queueOrder);
                    else{CancelBombardSequence(true);OrderSelectedGuns(target,frontage?end:moveDestination,false);}
                    Array.Clear(previewActive,0,previewActive.Length);
                }
            }
            if(Input.GetKeyDown(KeyCode.L))ChangeFormation(FormationKind.Line);
            if(Input.GetKeyDown(KeyCode.K))ChangeFormation(FormationKind.Column);
            if(Input.GetKeyDown(KeyCode.J))ChangeFormation(FormationKind.Skirmish);
            if(Input.GetKeyDown(KeyCode.N))ChangeFormation(FormationKind.Square);
            if(Input.GetKeyDown(KeyCode.LeftBracket))OrderAdjustRanks(-1);
            if(Input.GetKeyDown(KeyCode.RightBracket))OrderAdjustRanks(1);
            if(Input.GetKeyDown(KeyCode.F)&&!Ctrl&&FirstSelected()>=0)OrderToggleMelee();
            if(Input.GetKeyDown(KeyCode.G)&&!Ctrl)OrderTogglePursuit();
            if(Input.GetKeyDown(KeyCode.C)&&!Ctrl&&FirstSelected()>=0)OrderChargeTarget();
            if(Input.GetKeyDown(KeyCode.B))ForSelected(b=>world.SetFireMode(b.Id,world.NextFireMode(b)));
            if(Input.GetKeyDown(KeyCode.Backspace)||Input.GetKeyDown(KeyCode.H)&&!Ctrl)OrderHalt();
            if(Input.GetKeyDown(KeyCode.R)&&!Ctrl)OrderToggleRun();
            if(Input.GetKeyDown(KeyCode.T)&&!Ctrl)OrderToggleTrot();
            if(Input.GetKeyDown(KeyCode.P))OnTogglePause?.Invoke();
            if(Ctrl&&Input.GetKeyDown(KeyCode.T))OnCycleSpeed?.Invoke();
            if(Ctrl&&Input.GetKeyDown(KeyCode.M))
            {
                ClearSelection();
                SelectAllGuns();
                foreach(var b in world.Battalions)if(b.Side==ArmySide.French&&b.AliveCount>0&&!b.Routing)selected[b.Id]=true;
            }
            for(int i=0;i<9;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))
            {
                if(Ctrl){Array.Copy(selected,groups[i],selected.Length);SaveGunGroup(i);status=$"Group {i+1} assigned.";}
                else{Array.Copy(groups[i],selected,selected.Length);RecallGunGroup(i);status=$"Group {i+1} selected.";}
            }
        }
        void BuildPreview(Vector2 start,Vector2 end,bool frontage)
        {
            previewFrontage=frontage;
            Array.Clear(previewActive,0,previewActive.Length);
            int count=0,total=0;Vector2 center=Vector2.zero;
            foreach(var b in world.Battalions)if(selected[b.Id]&&b.AliveCount>0&&!b.Routing){count++;total+=b.AliveCount;center+=PreviewOrigin(b);}
            if(count==0)return;
            center/=count;
            var tangent=(end-start).normalized;
            float length=Vector2.Distance(start,end);
            float facing=Mathf.Atan2(-tangent.y,tangent.x);
            int ordered=0;
            foreach(var unit in world.Battalions)
            {
                if(!selected[unit.Id]||unit.AliveCount==0||unit.Routing)continue;
                int insert=ordered;
                // Preserve physical left-to-right order, including reversed drag directions.
                while(frontage&&insert>0&&Vector2.Dot(PreviewOrigin(world.Battalions[previewOrder[insert-1]]),tangent)>Vector2.Dot(PreviewOrigin(unit),tangent))
                {previewOrder[insert]=previewOrder[insert-1];insert--;}
                previewOrder[insert]=unit.Id;ordered++;
            }
            float totalGap=0;
            for(int order=1;order<ordered;order++)
                totalGap+=FrontageGap(world.Battalions[previewOrder[order-1]],world.Battalions[previewOrder[order]]);
            float available=Mathf.Max(count*4.4f,length-totalGap),cursor=0;
            for(int order=0;order<ordered;order++)
            {
                var b=world.Battalions[previewOrder[order]];
                previewActive[b.Id]=true;
                if(frontage)
                {
                    float allotment=available*b.AliveCount/Mathf.Max(1,total);
                    int files=FormationMath.CanResizeFrontage(b)?FormationMath.ClampFrontage(b,Mathf.RoundToInt(allotment/b.Spacing)):b.Files;
                    float width=FormationMath.CanResizeFrontage(b)?files*b.Spacing:FormationMath.HalfExtents(b).x*2;
                    previewFiles[b.Id]=files;
                    previewFacings[b.Id]=facing;
                    previewDestinations[b.Id]=start+tangent*(cursor+width*.5f);
                    cursor+=width;
                    if(order+1<ordered)cursor+=FrontageGap(b,world.Battalions[previewOrder[order+1]]);
                }
                else
                {
                    previewFiles[b.Id]=queueOrder?world.PlannedOrderFiles(b):b.Files;
                    Vector2 travel=start-center;
                    previewFacings[b.Id]=travel.sqrMagnitude>.04f?FormationMath.FacingOf(travel):b.Facing;
                    previewDestinations[b.Id]=start+PreviewOrigin(b)-center;
                }
                previewDestinations[b.Id]=world.ClampDestination(b.Id,previewDestinations[b.Id],previewFiles[b.Id],previewFacings[b.Id],frontage);
            }
        }
        static float FrontageGap(BattalionState first,BattalionState second)
        {
            // Reserve ten files of open space between infantry regiments.
            // Use the wider spacing so mixed formations keep a clear interval.
            return first.Kind==UnitKind.Infantry&&second.Kind==UnitKind.Infantry
                ?10f*Mathf.Max(first.Spacing,second.Spacing):8f;
        }
        public static bool IsDoubleClickRun(float elapsed,Vector2 first,Vector2 second)
            =>elapsed>=0&&elapsed<.3f&&(second-first).sqrMagnitude<625;
        Vector2 PreviewOrigin(BattalionState unit) => queueOrder?world.PlannedOrderPosition(unit):unit.Position;
        void ApplyPreview(bool run,bool frontage)
        {
            foreach(var b in world.Battalions)if(previewActive[b.Id])
            {
                world.OrderMove(b.Id,previewDestinations[b.Id],previewFacings[b.Id],frontage?previewFiles[b.Id]:0,run||b.Running,queueOrder);
            }
            status=queueOrder?"Order queued · Hold Space to preview the full route.":frontage?"Frontage and facing ordered.":run?"Run ordered.":"Move ordered.";
        }
        public void OrderChargeTarget()
        {
            foreach(var u in world.Battalions)if(selected[u.Id]&&!u.Routing&&u.AliveCount>0)
            {
                int target=u.AttackTargetId;
                if(target<0){float best=float.PositiveInfinity;foreach(var v in world.Battalions)if(v.Side!=u.Side&&v.AliveCount>0){float d=(v.Position-u.Position).sqrMagnitude;if(d<best){best=d;target=v.Id;}}}
                if(target>=0)world.OrderAttack(u.Id,target,true,true,QueueModifier);
            }
            status=QueueModifier?"Charge queued · Hold Space to preview the full route.":"Charge ordered.";
        }
        void IssueAttack(int targetId,bool run)
        {
            foreach(var b in world.Battalions)if(selected[b.Id]&&!b.Routing&&b.AliveCount>0)
            {
                world.OrderAttack(b.Id,targetId,UsesMeleeAttack(b,targetId,Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt)),run||b.Running,queueOrder);
            }
            status=queueOrder?"Attack queued · Hold Space to preview the full route.":"Attack ordered.";
        }
        bool GroundPoint(Vector2 screen,out Vector2 point)
        {
            var ray=battleCamera.ScreenPointToRay(screen);
            point=default;
            float begin=0,end=battleCamera.farClipPlane;
            float half=world.Config.FieldHalfSize;
            if(!RayAxis(ray.origin.x,ray.direction.x,half,ref begin,ref end)||
               !RayAxis(ray.origin.z,ray.direction.z,half,ref begin,ref end))return false;
            float previous=begin;
            var initial=ray.GetPoint(begin);
            float previousHeight=initial.y-world.Terrain.HeightAt(new Vector2(initial.x,initial.z));
            if(previousHeight<0)return false;
            float step=Mathf.Max(8,(end-begin)/128f);
            for(float t=begin+step;t<end+step;t+=step)
            {
                float current=Mathf.Min(t,end);
                var p=ray.GetPoint(current);
                float height=p.y-world.Terrain.HeightAt(new Vector2(p.x,p.z));
                if(height<=0)
                {
                    float low=previous,high=current;
                    for(int i=0;i<12;i++)
                    {
                        float middle=(low+high)*.5f;
                        var candidate=ray.GetPoint(middle);
                        if(candidate.y>world.Terrain.HeightAt(new Vector2(candidate.x,candidate.z)))low=middle;
                        else high=middle;
                    }
                    var hit=ray.GetPoint((low+high)*.5f);
                    point=new Vector2(hit.x,hit.z);return true;
                }
                previous=current;
                if(current>=end)break;
            }
            return false;
        }
        static bool RayAxis(float origin,float direction,float half,ref float begin,ref float end)
        {
            if(Mathf.Abs(direction)<.00001f)return Mathf.Abs(origin)<=half;
            float a=(-half-origin)/direction,b=(half-origin)/direction;
            begin=Mathf.Max(begin,Mathf.Min(a,b));end=Mathf.Min(end,Mathf.Max(a,b));
            return end>=begin;
        }
        static Rect SelectionRect(Vector2 a,Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        void ForSelected(Action<BattalionState> action) {foreach(var b in world.Battalions)if(selected[b.Id]&&!b.Routing&&b.AliveCount>0)action(b);}
        void ChangeFormation(FormationKind kind) => ForSelected(b=>world.SetFormation(b.Id,kind));
        void ToggleFire() {ForSelected(b=>{if(b.MeleeMode)world.SetMeleeMode(b.Id,false);else b.FireAtWill=!b.FireAtWill;});ToggleGunFire();}

        void RenderBattle()
        {
            GeometryUtility.CalculateFrustumPlanes(battleCamera,frustum);
            visibleSoldiers=0;
            if(AtlasBlend < .98f && UsesDetailedInfantry){crowd.Render(frustum,GetPresentationTime!=null?GetPresentationTime():world.Time);visibleSoldiers=crowd.VisibleSoldiers;}
            if(AtlasBlend<.98f&&cavalry!=null){cavalry.Render(frustum,GetPresentationTime!=null?GetPresentationTime():world.Time);visibleSoldiers+=cavalry.VisibleRiders;}
            foreach(var b in world.Battalions)
            {
                if(selected[b.Id] && AtlasBlend < .5f)tacticalMarkers.DrawInfantryRange(b);
                if(AtlasBlend>=.98f||UsesDetailedInfantry&&(!selected[b.Id]||AtlasBlend>=.5f))continue;
                var bounds=BattleRenderBounds.Regiment(world,b,world.Time);
                if(!GeometryUtility.TestPlanesAABB(frustum,bounds))continue;
                var rotation=Quaternion.Euler(0,b.Facing*Mathf.Rad2Deg,0);
                Material material=b.Side==ArmySide.French?french:british;
                int batch=0;
                if(AtlasBlend < .98f && !UsesDetailedInfantry)
                for(int i=b.SoldierStart;i<b.SoldierStart+b.SoldierCount;i++)
                {
                    if(!world.Soldiers.Alive[i])continue;
                    Vector2 p=world.Soldiers.Positions[i];
                    float bounce=world.Soldiers.MovementSpeeds[i]>.04f?.035f*Mathf.Sin(world.Soldiers.GaitPhases[i]*Mathf.PI*2):0;
                    rotation=Quaternion.Euler(0,world.Soldiers.Facings[i]*Mathf.Rad2Deg,0);
                    Vector3 display = world.Terrain.SurfacePoint(p,bounce);
                    if (world.Soldiers.IsKnockedDown(i,world.Time))
                    {
                        display = world.SoldierPosition(i,world.Time);
                        float phase=InfantryRecoveryTiming.Phase(world,i,world.Time,out bool rising);
                        int variant=InfantryRecoveryTiming.Variant(i,world.Soldiers.KnockdownImpulses[i],world.Soldiers.KnockdownFacings[i],4);
                        rotation=InfantryRecoveryTiming.Rotation(world,i,world.Time,variant);
                        rotation*=Quaternion.Euler((variant%2==1?85f:-85f)*(rising?1f-phase:phase),0,0);
                    }
                    instances[batch++]=Matrix4x4.TRS(display,rotation,new Vector3(1,RifleFireTiming.IsKneeling(world,b,i,world.Time)?.7f:1,1));
                    visibleSoldiers++;
                    if(batch==1023) {Draw(soldierMesh,instances,batch,material);batch=0;}
                }
                if(batch>0)Draw(soldierMesh,instances,batch,material);
                if(selected[b.Id] && AtlasBlend < .5f)tacticalMarkers.DrawSelection(b);
            }
            bool showOrders=EnableInput&&Input.GetKey(KeyCode.Space);
            if(AtlasBlend < .5f)RenderGunOrders(showOrders);
            RenderBombardQueuePreview();
            if(showOrders && AtlasBlend < .5f)RenderInfantryOrders();
            if(ordering && AtlasBlend < .5f)
            {
                foreach(var b in world.Battalions)if(previewActive[b.Id])RenderSlots(b,previewDestinations[b.Id],previewFacings[b.Id],previewFiles[b.Id],previewFrontage);
            }
            RenderAtlasFormations();
        }
        void RenderSlots(BattalionState b,Vector2 destination,float facing,int files,bool lineFrontage=false)
        {
            int batch=0;
            var rotation=Quaternion.Euler(0,facing*Mathf.Rad2Deg,0);
            files=Mathf.Min(files,b.AliveCount);
            for(int slot=0;slot<b.AliveCount;slot++)
            {
                Vector2 point=destination+FormationMath.SlotOffset(b,slot,files,facing);
                instances[batch++]=Matrix4x4.TRS(world.Terrain.SurfacePoint(point,.12f),rotation,new Vector3(.55f,.12f,.55f));
                if(batch==1023){Draw(cubeMesh,instances,batch,marker);batch=0;}
            }
            if(batch>0)Draw(cubeMesh,instances,batch,marker);
            var forward=FormationMath.Forward(facing);
            float halfDepth=FormationMath.CanResizeFrontage(b)?Mathf.CeilToInt(b.AliveCount/(float)Mathf.Max(1,files))*FormationMath.RankSpacing(b.Spacing)*.5f:FormationMath.HalfExtents(b).y;
            var arrow=destination+forward*(halfDepth+4);
            Graphics.DrawMesh(cubeMesh,Matrix4x4.TRS(world.Terrain.SurfacePoint(arrow,.16f),rotation,new Vector3(.55f,.12f,6)),marker,0,battleCamera);
            var tip=arrow+forward*3;
            var right=FormationMath.Right(facing);
            for(int sign=-1;sign<=1;sign+=2)
            {
                var arm=tip-forward*1.1f+right*(sign*1.1f);
                Graphics.DrawMesh(cubeMesh,Matrix4x4.TRS(world.Terrain.SurfacePoint(arm,.16f),rotation*Quaternion.Euler(0,sign*-45f,0),new Vector3(.5f,.12f,3.1f)),marker,0,battleCamera);
            }
        }
        void Draw(Mesh mesh,Matrix4x4[] matrices,int count,Material material) => Graphics.DrawMeshInstanced(mesh,0,material,matrices,count,null,mesh==soldierMesh?ShadowCastingMode.On:ShadowCastingMode.Off,mesh==soldierMesh,0,battleCamera,LightProbeUsage.Off);

        void OnGUI()
        {
            if(world==null||HideReviewUI||!EnableInput)return;
            using var theme = new EmpireTheme.Scope(true);
            if(titleStyle==null){titleStyle=EmpireTheme.Text(18,EmpireTheme.Ivory,FontStyle.Bold);textStyle=EmpireTheme.Text(14,EmpireTheme.Ivory);textStyle.wordWrap=true;}
            if(!string.IsNullOrEmpty(status)&&Time.unscaledTime-statusTime<2.8f)
                GUI.Label(hud!=null?hud.StatusRect:new Rect(24,Screen.height-220,Screen.width-48,22),status,textStyle);
            DrawAtlas();
            tacticalMarkers.DrawFlags();
            DrawGunControls();
            DrawReviewCommandCursor();
            GUI.color=Color.white;
            if(selecting)
            {
                var r=SelectionRect(leftStart,Input.mousePosition);
                r.y=Screen.height-r.yMax;
                EmpireTheme.Panel(r,new Color(EmpireTheme.Brass.r,EmpireTheme.Brass.g,EmpireTheme.Brass.b,.12f));
            }
            DrawRegimentHoverTip();
        }

        public void SelectUnit(int id,bool additive) => SelectBattalion(id,additive);
        public Action OnExitBattle;
        public void FocusUnit(int id) { trackedId=-1;FocusBattalion(id); }
        public void TrackSelectedUnit()
        {
            int id=FirstSelected();if(id<0)return;
            var unit=world.Battalions[id];trackedId=id;zoomAnchored=false;
            focus=world.Terrain.SurfacePoint(unit.Position);
            distance=zoomTarget=Mathf.Clamp(FormationMath.HalfExtents(unit).magnitude*1.4f,24,90);
            pitch=24;yaw=unit.Facing*Mathf.Rad2Deg;
            status="Following "+unit.Name+" · move the camera to release.";
            UpdateCamera();
        }
        void UpdateTrackedUnit()
        {
            if(trackedId<0)return;
            var unit=world.Battalions[trackedId];
            if(unit.AliveCount<=0){trackedId=-1;return;}
            focus=world.Terrain.SurfacePoint(unit.Position);
        }
        public void OrderAdjustRanks(int change)
        {
            ForSelected(unit=>world.AdjustRanks(unit.Id,change));
            status=change>0?"More ranks ordered.":"Fewer ranks ordered.";
        }
        public bool SelectionCanForm(FormationKind kind)
        {bool any=false;foreach(var unit in world.Battalions)if(selected[unit.Id]){any=true;if(!world.CanForm(unit,kind))return false;}return any;}
        public void OrderFormation(FormationKind kind) => ChangeFormation(kind);
        public void OrderToggleFire() => ToggleFire();
        public void OrderHalt() {CancelBombardSequence(true);gunBombardOrder=false;ForSelected(b=>world.Halt(b.Id));ForSelectedGun(g=>commandArtillery.ClearTarget(g.Id,false));}
        public void OrderToggleRun()
        {
            bool run=false;int count=0;
            ForSelected(b=>{count++;if(!b.Running)run=true;});
            if(count==0)return;
            ForSelected(b=>{b.Running=run;b.Trotting=false;});
            status=run?"Run enabled":"Walk enabled";
        }
        public bool SelectionHasCavalry()
        {foreach(var b in world.Battalions)if(selected[b.Id]&&b.Kind==UnitKind.Cavalry)return true;return false;}
        public void OrderToggleTrot()
        {
            bool trot=false;ForSelected(b=>{if(b.Kind==UnitKind.Cavalry&&!b.Trotting)trot=true;});
            ForSelected(b=>{if(b.Kind==UnitKind.Cavalry){b.Trotting=trot;b.Running=false;}});
            status=trot?"Cavalry trot enabled":"Cavalry walk enabled";
        }
        public bool SelectionCanFixBayonets()
        {foreach(var unit in world.Battalions)if(selected[unit.Id]&&!unit.Routing&&unit.AliveCount>0&&world.CanFixBayonets(unit))return true;return false;}
        public bool SelectedBayonetsFixed()
        {bool any=false;foreach(var unit in world.Battalions)if(selected[unit.Id]&&!unit.Routing&&unit.AliveCount>0&&world.CanFixBayonets(unit)){any=true;if(!unit.BayonetsFixed)return false;}return any;}
        public void OrderToggleBayonets()
        {
            bool fix=!SelectedBayonetsFixed();
            ForSelected(unit=>world.SetBayonets(unit.Id,fix));
            status=fix?"Bayonets fixed: stronger melee and cavalry defence; reloads take 25% longer.":"Bayonets unfixed: normal reload speed; shorter reach and weaker melee.";
        }
        public void OrderWithdraw() => ForSelected(b=>world.Withdraw(b.Id));

        void OnDestroy()
        {
            RestoreCommandCursor();
            Shader.SetGlobalFloat("_BattleAtlasBlend", 0);
            tacticalMarkers?.Dispose();
            crowd?.Dispose();
            cavalry?.Dispose();
            foreach(var item in owned)if(item)Destroy(item);
            owned.Clear();
        }
    }
}
